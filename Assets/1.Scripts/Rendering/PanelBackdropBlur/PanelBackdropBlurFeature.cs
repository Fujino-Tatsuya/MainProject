// ----------------------------------------------------------------------------
//  PanelBackdropBlurFeature.cs — 패널 배경 블러 렌더러 피처 (할 일 201, URP 17 / RenderGraph)
//
//  패널이 열려 있는 동안(PanelBackdropBlur.Amount > 0) 카메라 컬러 전체를 흐린다.
//  주입 시점 = BeforeRenderingPostProcessing — 투명 이후라 월드·VFX 와 함께, 블러 동안 Screen Space Camera 로
//  잠깐 바뀐 HUD 캔버스(PanelBackdropBlur 가 바꾼다)까지 흐려진다. 패널(Overlay)은 그 뒤에 그려져 선명하다.
//  블러가 0 이면 패스를 넣지 않는다(비용 0). Game 카메라만(씬 뷰·프리뷰 제외).
//  MaskBlurFeature(203)와 피처·셰이더·설정이 따로다. PC_Renderer 에만 넣는다.
// ----------------------------------------------------------------------------
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public sealed class PanelBackdropBlurFeature : ScriptableRendererFeature
{
    public static class PassNames
    {
        public const string Horizontal = "PanelBlur H";
        public const string Vertical = "PanelBlur V";
        public const string Composite = "PanelBlur Composite";
        public const string CopyBack = "PanelBlur CopyBack";
    }

    // 🔴 셰이더를 직렬화 참조로 들고 있어야 빌드에서 스트립되지 않는다(MaskBlurFeature 와 같은 이유).
    [Tooltip("Hidden/Rendering/PanelBackdropBlur. 반드시 물려 둘 것(비면 Shader.Find — 빌드에서 스트립될 수 있다).")]
    [SerializeField] private Shader _shader;

    [Tooltip("세기·축소·반복·채도·어둡게·보간 시간. 비면 패널 블러를 쓰지 않는다(패널은 블러 없이 열린다).")]
    [SerializeField] private PanelBackdropBlurSettings _settings;

    [SerializeField]
    private RenderPassEvent _injectionPoint = RenderPassEvent.BeforeRenderingPostProcessing;

    private Material _material;
    private PanelBlurPass _pass;

    public override void Create()
    {
        if (_shader == null)
            _shader = Shader.Find(PanelBlurPass.ShaderName);

        if (_shader != null)
            _material = CoreUtils.CreateEngineMaterial(_shader);

        _pass = new PanelBlurPass(_material) { renderPassEvent = _injectionPoint };

        PanelBackdropBlur.Settings = _material != null && isActive ? _settings : null;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_material == null || _pass == null || _settings == null)
            return;

        if (renderingData.cameraData.cameraType != CameraType.Game)
            return;

        float amount = PanelBackdropBlur.Amount;
        if (amount <= 0f)
            return;

        // 직접 구동(218 타이틀) 중이면 SO 대신 구동 값. 반경 비례면 블러 세기에 Amount 를 곱한다.
        PanelBackdropBlurLook look = PanelBackdropBlur.TryGetDrivenLook(out PanelBackdropBlurLook driven)
            ? driven
            : new PanelBackdropBlurLook(_settings.blurStrength, _settings.saturation, _settings.darken, false);
        if (look.scaleRadiusWithAmount)
            look.blurStrength *= amount;

        _pass.Setup(_settings, amount, look);
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        if (PanelBackdropBlur.Settings == _settings)
            PanelBackdropBlur.Settings = null;
        CoreUtils.Destroy(_material);
        _material = null;
    }

    // ------------------------------------------------------------------------
    private sealed class PanelBlurPass : ScriptableRenderPass
    {
        public const string ShaderName = "Hidden/Rendering/PanelBackdropBlur";

        private const int PassH = 0;
        private const int PassV = 1;
        private const int PassComposite = 2;

        private static readonly Vector4 FullScreenScaleBias = new Vector4(1f, 1f, 0f, 0f);
        private static readonly int IdStep = Shader.PropertyToID("_PanelBlurStep");
        private static readonly int IdParams = Shader.PropertyToID("_PanelBlurParams");
        private static readonly int IdBlurTex = Shader.PropertyToID("_PanelBlurTex");

        private readonly Material _material;
        private PanelBackdropBlurSettings _settings;
        private float _amount;
        private PanelBackdropBlurLook _look;

        private class PassData
        {
            public Material material;
            public TextureHandle source;
            public int shaderPass;
        }

        public PanelBlurPass(Material material)
        {
            _material = material;
            profilingSampler = new ProfilingSampler("PanelBackdropBlur");
        }

        public void Setup(PanelBackdropBlurSettings settings, float amount, PanelBackdropBlurLook look)
        {
            _settings = settings;
            _amount = amount;
            _look = look;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_material == null || _settings == null)
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            if (resourceData.isActiveTargetBackBuffer)
                return;

            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid())
                return;

            RenderTextureDescriptor fullDesc = cameraData.cameraTargetDescriptor;
            fullDesc.depthBufferBits = 0;
            fullDesc.msaaSamples = 1;

            int divisor = 1 << Mathf.Clamp(_settings.downsampleShift, 0, 3);
            RenderTextureDescriptor blurDesc = fullDesc;
            blurDesc.width = Mathf.Max(1, fullDesc.width / divisor);
            blurDesc.height = Mathf.Max(1, fullDesc.height / divisor);

            _material.SetVector(IdStep, new Vector4(
                _look.blurStrength / Mathf.Max(1, blurDesc.width),
                _look.blurStrength / Mathf.Max(1, blurDesc.height), 0f, 0f));
            _material.SetVector(IdParams, new Vector4(_amount, _look.saturation, _look.darken, 0f));

            TextureHandle blurA = UniversalRenderer.CreateRenderGraphTexture(renderGraph, blurDesc, "_PanelBlurA", false, FilterMode.Bilinear);
            TextureHandle blurB = UniversalRenderer.CreateRenderGraphTexture(renderGraph, blurDesc, "_PanelBlurB", false, FilterMode.Bilinear);
            TextureHandle composed = UniversalRenderer.CreateRenderGraphTexture(renderGraph, fullDesc, "_PanelBlurComposed", false, FilterMode.Bilinear);

            // 1) 다운샘플 + 가로 → 세로, 반복. 마지막 세로 결과를 전역 _PanelBlurTex 로 노출한다.
            int iterations = Mathf.Clamp(_settings.iterations, 1, 4);
            for (int i = 0; i < iterations; i++)
            {
                AddBlit(renderGraph, PassNames.Horizontal, i == 0 ? source : blurB, blurA, PassH, null, 0);
                AddBlit(renderGraph, PassNames.Vertical, blurA, blurB, PassV, null, i == iterations - 1 ? IdBlurTex : 0);
            }

            // 2) 원본과 블러를 세기로 합성(채도·어둡게 포함) → 3) 카메라 컬러로 복사
            AddBlit(renderGraph, PassNames.Composite, source, composed, PassComposite, blurB, 0);
            AddCopyBack(renderGraph, composed, source);
        }

        private void AddBlit(RenderGraph renderGraph, string passName, TextureHandle src, TextureHandle dst,
            int shaderPass, TextureHandle? globalTexture, int exposeAsGlobalId)
        {
            using var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out PassData data);

            data.material = _material;
            data.source = src;
            data.shaderPass = shaderPass;

            builder.UseTexture(src, AccessFlags.Read);
            if (globalTexture.HasValue)
            {
                builder.UseTexture(globalTexture.Value, AccessFlags.Read);
                builder.UseAllGlobalTextures(true);
            }

            builder.SetRenderAttachment(dst, 0, AccessFlags.Write);
            if (exposeAsGlobalId != 0)
                builder.SetGlobalTextureAfterPass(dst, exposeAsGlobalId);

            builder.SetRenderFunc(static (PassData d, RasterGraphContext ctx) =>
            {
                Blitter.BlitTexture(ctx.cmd, d.source, FullScreenScaleBias, d.material, d.shaderPass);
            });
        }

        private static void AddCopyBack(RenderGraph renderGraph, TextureHandle src, TextureHandle dst)
        {
            using var builder = renderGraph.AddRasterRenderPass<PassData>(PassNames.CopyBack, out PassData data);

            data.source = src;
            builder.UseTexture(src, AccessFlags.Read);
            builder.SetRenderAttachment(dst, 0, AccessFlags.Write);
            builder.SetRenderFunc(static (PassData d, RasterGraphContext ctx) =>
            {
                Blitter.BlitTexture(ctx.cmd, d.source, FullScreenScaleBias, 0f, false);
            });
        }
    }
}
