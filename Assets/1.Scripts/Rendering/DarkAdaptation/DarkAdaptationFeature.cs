using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// 간파 성공 암순응 — 휘도 임계보다 어두운 픽셀만 낮추고, 시전자 주변은 원형으로 살린다(할 일 87 C3).
// 후처리 전(HDR)에 돌아 임계를 HDR 휘도로 보고, 남긴 밝은 픽셀에서 Bloom 이 그대로 나온다.
// 강도는 DarkAdaptationState 에서 읽는다 — 0 이면 패스를 넣지 않는다(평소 비용 0).
public sealed class DarkAdaptationFeature : ScriptableRendererFeature
{
    public const string PassName = "Interrupt DarkAdaptation";

    [Tooltip("암순응 전체화면 셰이더. Shader.Find 는 빌드에서 빠지므로 직접 연결한다.")]
    [SerializeField] private Shader _shader;

    private Material _material;
    private DarkAdaptationPass _pass;

    public override void Create()
    {
        CoreUtils.Destroy(_material);
        _material = _shader != null ? CoreUtils.CreateEngineMaterial(_shader) : null;
        _pass = new DarkAdaptationPass
        {
            // Fog(450)·Silhouette/EnemyOutline/MaskBlur(550) 뒤 — 같은 550 끼리는 기능 목록 순서라 목록 맨 끝에 둔다.
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing
        };

        if (_shader == null)
            Debug.LogWarning("[DarkAdaptation] 셰이더가 연결되지 않아 효과가 실행되지 않는다.", this);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (DarkAdaptationState.Intensity <= 0f || _material == null || _pass == null)
            return;

        CameraData cameraData = renderingData.cameraData;
        Camera camera = cameraData.camera;

        // Scene View, 미리보기, 반사, 미니맵 RT와 Overlay 카메라는 제외한다.
        if (cameraData.cameraType != CameraType.Game ||
            cameraData.renderType == CameraRenderType.Overlay ||
            camera == null || camera.targetTexture != null)
            return;

        _pass.Setup(_material);
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(_material);
        _material = null;
    }

    private sealed class DarkAdaptationPass : ScriptableRenderPass
    {
        private static readonly Vector4 FullScreenScaleBias = new Vector4(1f, 1f, 0f, 0f);
        private static readonly int IdParams = Shader.PropertyToID("_DarkAdaptParams");
        private static readonly int IdReveal = Shader.PropertyToID("_DarkAdaptReveal");
        private static readonly int IdAspect = Shader.PropertyToID("_DarkAdaptAspect");

        private Material _material;

        private sealed class PassData
        {
            public Material material;
            public TextureHandle source;
            public Vector4 parameters;
            public Vector4 reveal;
            public float aspect;
        }

        public DarkAdaptationPass()
        {
            profilingSampler = new ProfilingSampler(PassName);
            requiresIntermediateTexture = true;
        }

        public void Setup(Material material)
        {
            _material = material;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_material == null)
                return;

            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer)
            {
                Debug.LogError("[DarkAdaptation] 카메라 컬러가 BackBuffer여서 암순응을 적용할 수 없다.");
                return;
            }

            TextureHandle source = resources.activeColorTexture;
            if (!source.IsValid())
                return;

            RenderTextureDescriptor cameraDescriptor = frameData.Get<UniversalCameraData>().cameraTargetDescriptor;
            float aspect = Mathf.Max(1, cameraDescriptor.width) / (float)Mathf.Max(1, cameraDescriptor.height);

            TextureDesc descriptor = renderGraph.GetTextureDesc(source);
            descriptor.name = "_CameraColorDarkAdaptation";
            descriptor.clearBuffer = false;
            TextureHandle destination = renderGraph.CreateTexture(descriptor);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(PassName, out PassData data))
            {
                data.material = _material;
                data.source = source;
                // x=강도, y=휘도 임계, z=임계 부드러움, w=최대 어둡힘
                data.parameters = new Vector4(
                    Mathf.Clamp01(DarkAdaptationState.Intensity),
                    Mathf.Max(0f, DarkAdaptationState.LuminanceThreshold),
                    Mathf.Max(0f, DarkAdaptationState.ThresholdSoftness),
                    Mathf.Clamp01(DarkAdaptationState.MaxDarken));
                // xy=원형 중심(뷰포트), z=반경(화면 높이 비율, 0=끔), w=가장자리 부드러움 — 화면비는 셰이더가 _DarkAdaptAspect 로 보정
                data.reveal = new Vector4(
                    DarkAdaptationState.RevealCenter.x,
                    DarkAdaptationState.RevealCenter.y,
                    Mathf.Max(0f, DarkAdaptationState.RevealRadius),
                    Mathf.Max(0f, DarkAdaptationState.RevealSoftness));
                data.aspect = aspect;

                builder.UseTexture(source, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (PassData d, RasterGraphContext context) =>
                {
                    // 다른 카메라의 설정과 섞이지 않도록 기록해 둔 값을 실행 직전에 전달한다.
                    d.material.SetVector(IdParams, d.parameters);
                    d.material.SetVector(IdReveal, d.reveal);
                    d.material.SetFloat(IdAspect, d.aspect);
                    Blitter.BlitTexture(context.cmd, d.source, FullScreenScaleBias, d.material, 0);
                });
            }

            resources.cameraColor = destination;
        }
    }
}
