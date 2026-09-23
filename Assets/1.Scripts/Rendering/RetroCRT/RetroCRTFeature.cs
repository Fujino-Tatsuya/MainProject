using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

// Cyanilux Fullscreen Shader Graph를 맵의 게임 화면에만 적용한다.
public sealed class RetroCRTFeature : ScriptableRendererFeature
{
    public const string PassName = "Cyanilux CRT";

    [Tooltip("사용할 셰이더 변형이 빌드에 포함되도록 머티리얼을 직접 연결한다.")]
    [SerializeField] private Material _material;

    [Tooltip("기존 픽셀레이트·스캔라인용 셰이더. 빌드에 포함되도록 직접 연결한다.")]
    [SerializeField] private Shader _pixelScanlineShader;

    private RetroCRTPass _pass;
    private Material _pixelScanlineMaterial;
    private PixelScanlinePass _pixelScanlinePass;

    public override void Create()
    {
        CoreUtils.Destroy(_pixelScanlineMaterial);
        _pixelScanlineMaterial = _pixelScanlineShader != null
            ? CoreUtils.CreateEngineMaterial(_pixelScanlineShader)
            : null;
        _pixelScanlinePass = new PixelScanlinePass
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing
        };
        _pass = new RetroCRTPass
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing
        };

        if (_material == null)
            Debug.LogWarning("[RetroCRT] CRT 머티리얼이 연결되지 않아 효과가 실행되지 않는다.", this);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        RetroCRTController controller = RetroCRTController.ActiveController;
        if (controller == null)
            return;

        CameraData cameraData = renderingData.cameraData;
        Camera camera = cameraData.camera;

        // Scene View, 미리보기, 반사, 미니맵 RT와 Overlay 카메라는 제외한다.
        if (cameraData.cameraType != CameraType.Game ||
            cameraData.renderType == CameraRenderType.Overlay ||
            camera == null || camera.targetTexture != null)
            return;

        // 원본 월드를 픽셀화/스캔라인 처리한 뒤 CRT의 곡률과 RGB 마스크를 적용한다.
        // CRT가 꺼져도 두 효과를 각각 사용할 수 있다.
        if (_pixelScanlinePass != null && _pixelScanlineMaterial != null && controller.HasPixelScanlineEffect)
        {
            _pixelScanlinePass.Setup(_pixelScanlineMaterial, controller);
            renderer.EnqueuePass(_pixelScanlinePass);
        }

        // 컨트롤러가 자기 머티리얼을 들고 있으면 그것을 쓴다(키워드를 따로 켜야 하는 씬용).
        // 비어 있으면 Renderer Feature 의 공유 머티리얼 — 이쪽 저작값은 절대 건드리지 않는다.
        Material material = controller.MaterialOverride != null ? controller.MaterialOverride : _material;

        if (_pass != null && material != null && controller.EffectEnabled)
        {
            _pass.Setup(material, controller);
            renderer.EnqueuePass(_pass);
        }
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(_pixelScanlineMaterial);
        _pixelScanlineMaterial = null;
    }

    private sealed class RetroCRTPass : ScriptableRenderPass
    {
        private Material _material;
        private RetroCRTController _controller;
        private Material _validatedMaterial;

        public RetroCRTPass()
        {
            profilingSampler = new ProfilingSampler(PassName);
            // BackBuffer는 입력으로 읽을 수 없으므로 URP의 중간 카메라 컬러를 요청한다.
            requiresIntermediateTexture = true;
        }

        public void Setup(Material material, RetroCRTController controller)
        {
            _material = material;
            _controller = controller;

            // 참조 이름이 바뀌면 MPB 는 예외 없이 조용히 무시한다. 머티리얼이 바뀔 때 한 번만 검사한다.
            if (_validatedMaterial != material)
            {
                CrtParamInfo.WarnMissingProperties(material, material);
                _validatedMaterial = material;
            }
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer)
            {
                Debug.LogError("[RetroCRT] 카메라 컬러가 BackBuffer여서 CRT를 적용할 수 없다.");
                return;
            }

            TextureHandle source = resources.activeColorTexture;
            if (!source.IsValid() || _material == null || _controller == null)
                return;

            TextureDesc descriptor = renderGraph.GetTextureDesc(source);
            descriptor.name = "_CameraColorRetroCRT";
            descriptor.clearBuffer = false;
            TextureHandle destination = renderGraph.CreateTexture(descriptor);

            // URP 17.3 FullScreenPassRendererFeature와 같은 Shader Graph 실행 규약:
            // ProceduralTriangle + _BlitTexture. 기존 HLSL용 Blitter 호출로 대체하지 않는다.
            var parameters = new RenderGraphUtils.BlitMaterialParameters(
                source, destination, _material, 0);
            // 패스별 값만 전달한다. 공유 머티리얼 에셋과 다른 카메라의 예약된 값은 변경하지 않는다.
            var properties = new MaterialPropertyBlock();

            // 베젤은 기존대로 항상 컨트롤러 값을 따른다.
            properties.SetFloat(CrtParamInfo.IdOf(CrtParam.BezelSize), _controller.BezelSize);

            // 나머지는 오버라이드가 걸린 것만 덮는다.
            // 안 걸리면 아무것도 담지 않으므로 머티리얼 저작값이 그대로 산다 — 맵 씬 룩이 안 흔들린다.
            if (_controller.HasOverrides)
            {
                for (var i = 0; i < CrtParamInfo.Count; i++)
                {
                    var param = (CrtParam)i;
                    if (!_controller.TryGetOverride(param, out float value))
                        continue;

                    if (CrtParamInfo.IsVector(param))
                        properties.SetVector(CrtParamInfo.IdOf(param), new Vector4(value, value, 0f, 0f));
                    else
                        properties.SetFloat(CrtParamInfo.IdOf(param), value);
                }
            }

            parameters.propertyBlock = properties;
            renderGraph.AddBlitPass(parameters, passName: PassName);

            // 새 결과를 다음 패스에 넘긴다. 원본으로 복사하는 추가 패스는 필요 없다.
            // Screen Space Overlay HUD는 이 효과 이후에 그려진다.
            resources.cameraColor = destination;
        }
    }
}
