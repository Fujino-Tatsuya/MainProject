using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// URP 17 RenderGraph용 전체 화면 픽셀레이트·스캔라인 피처.
// AfterRenderingPostProcessing에서 월드 화면만 처리하고, Screen Space Overlay UI는 이후에
// URP가 그리므로 효과에서 제외된다.
public sealed class PixelScanlineFeature : ScriptableRendererFeature
{
    public const string PassName = "PixelScanline FullScreen";

    // 런타임 Shader.Find만 쓰면 빌드에서 스트립될 수 있으므로 PC Renderer에 직렬화한다.
    [Tooltip("빌드 스트립 방지를 위해 PixelScanline 셰이더를 반드시 연결할 것.")]
    [SerializeField] private Shader _shader;

    [SerializeField]
    private RenderPassEvent _injectionPoint = RenderPassEvent.AfterRenderingPostProcessing;

    private Material _material;
    private PixelScanlinePass _pass;

    public override void Create()
    {
        CoreUtils.Destroy(_material);

        if (_shader == null)
            _shader = Shader.Find(PixelScanlinePass.ShaderName);

        _material = _shader != null ? CoreUtils.CreateEngineMaterial(_shader) : null;
        _pass = new PixelScanlinePass(_material)
        {
            renderPassEvent = _injectionPoint
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_material == null || _pass == null)
            return;

        CameraData cameraData = renderingData.cameraData;
        Camera camera = cameraData.camera;
        CameraType cameraType = cameraData.cameraType;

        if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection)
            return;

        // 카메라 스택의 Overlay 카메라에서 두 번 적용하지 않는다.
        if (cameraData.renderType == CameraRenderType.Overlay)
            return;

        // 미니맵 베이크 등 RenderTexture 카메라는 화면 연출 대상이 아니다.
        if (camera == null || camera.targetTexture != null)
            return;

        PixelScanlineSettings settings = PixelScanlineController.ActiveSettings;
        if (settings == null)
            return;

        _pass.Setup(settings);
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(_material);
        _material = null;
    }

    private sealed class PixelScanlinePass : ScriptableRenderPass
    {
        public const string ShaderName = "Hidden/Rendering/PixelScanline";

        private static readonly Vector4 FullScreenScaleBias = new Vector4(1f, 1f, 0f, 0f);
        private static readonly int IdResolution = Shader.PropertyToID("_PixelScanlineResolution");
        private static readonly int IdPixel = Shader.PropertyToID("_PixelScanlinePixel");
        private static readonly int IdScanline = Shader.PropertyToID("_PixelScanlinePattern");
        private static readonly int IdColor = Shader.PropertyToID("_PixelScanlineColor");

        private readonly Material _material;
        private PixelScanlineSettings _settings;

        private class PassData
        {
            public Material material;
            public TextureHandle source;
        }

        public PixelScanlinePass(Material material)
        {
            _material = material;
            profilingSampler = new ProfilingSampler(PassName);
        }

        public void Setup(PixelScanlineSettings settings)
        {
            _settings = settings;

            // BackBuffer는 입력 텍스처로 읽을 수 없다. URP가 이 패스 전에 중간 컬러를
            // 확보하게 해 결과를 한 번의 효과 블릿으로 교체할 수 있게 한다.
            requiresIntermediateTexture = true;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_material == null || _settings == null)
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            if (resourceData.isActiveTargetBackBuffer)
            {
                Debug.LogError(
                    "[PixelScanline] BackBuffer는 입력으로 읽을 수 없어 패스를 건너뛴다. " +
                    "중간 컬러 텍스처 설정을 확인할 것.");
                return;
            }

            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid())
                return;

            RenderTextureDescriptor descriptor = cameraData.cameraTargetDescriptor;
            int width = Mathf.Max(1, descriptor.width);
            int height = Mathf.Max(1, descriptor.height);
            PushMaterialParams(width, height);

            TextureDesc destinationDesc = renderGraph.GetTextureDesc(source);
            destinationDesc.name = "_CameraColorPixelScanline";
            destinationDesc.clearBuffer = false;
            TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

            using (var builder =
                   renderGraph.AddRasterRenderPass<PassData>(PassName, out PassData data))
            {
                data.material = _material;
                data.source = source;

                builder.UseTexture(source, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (PassData d, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(
                        ctx.cmd,
                        d.source,
                        FullScreenScaleBias,
                        d.material,
                        0);
                });
            }

            // 다음 URP 패스가 새 결과를 카메라 컬러로 읽게 한다. 원본으로 다시 복사하는
            // 두 번째 풀스크린 블릿이 필요 없어 효과 자체는 1패스다.
            resourceData.cameraColor = destination;
        }

        private void PushMaterialParams(int width, int height)
        {
            _material.SetVector(
                IdResolution,
                new Vector4(width, height, 1f / width, 1f / height));

            _material.SetVector(
                IdPixel,
                new Vector4(
                    Mathf.Max(1, _settings.pixelBlockSize),
                    _settings.pixelateEnabled ? 1f : 0f,
                    0f,
                    0f));

            _material.SetVector(
                IdScanline,
                new Vector4(
                    Mathf.Max(1, _settings.scanlineThicknessPx),
                    Mathf.Max(0, _settings.scanlineSpacingPx),
                    _settings.scanlineEnabled ? 1f : 0f,
                    Mathf.Clamp01(_settings.scanlineOpacity)));

            Color color = _settings.scanlineColor;
            _material.SetColor(IdColor, new Color(color.r, color.g, color.b, 1f));
        }
    }
}
