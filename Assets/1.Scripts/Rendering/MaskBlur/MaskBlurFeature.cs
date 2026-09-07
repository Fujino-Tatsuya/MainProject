// ----------------------------------------------------------------------------
//  MaskBlurFeature.cs — 화면공간 마스크 블러 렌더러 피처 (URP 17 / RenderGraph)
//
//  FogRendererFeature 와 같은 풀스크린 블릿 패턴이지만 별도 피처로 둔다.
//  포그 패스에 얹으면 FogManager 를 켜야 하고, 그러면 디밍·시야 제한이 함께 돌아온다.
//
//  주입 시점 = BeforeRenderingPostProcessing(550).
//    - 투명 이후이므로 스킬 장판·VFX 가 배경과 함께 흐려진다.
//    - 포스트프로세싱 이전이므로 블룸·톤매핑이 합성 결과 위에 걸린다.
//    - 스크린 오버레이 UI 는 어차피 그 뒤라 영향 없다.
// ----------------------------------------------------------------------------
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public sealed class MaskBlurFeature : ScriptableRendererFeature
{
    // RenderGraph 패스 이름. ProfilerHUD 의 customMarkers 가 이 문자열로 패스를 찾는다.
    //
    // 왜 상수로 묶는가: 마커 이름이 실제 패스 이름과 어긋나면 HUD 는 에러 없이 0.00 ms 를
    // 찍는다. "이 패스는 공짜"로 읽히는 거짓 신호이고, 성능을 판단하는 자리라 특히 위험하다.
    // 패스 이름을 바꿀 일이 생기면 여기 한 곳만 고치면 HUD 배선 도구까지 함께 따라온다.
    public static class PassNames
    {
        public const string DownH = "MaskBlur DownH";
        public const string Vertical = "MaskBlur V";
        public const string Composite = "MaskBlur Composite";
        public const string CopyBack = "MaskBlur CopyBack";
    }

    // 🔴 셰이더를 직렬화 참조로 들고 있어야 한다.
    //    런타임 Shader.Find 만 쓰면 어떤 머티리얼·씬·프리팹도 참조하지 않는 셰이더가 되어
    //    빌드에서 스트립된다("에디터는 되는데 빌드만 안 됨"의 대표 원인. 미니맵이 이걸로 안 보였다).
    //    이 필드가 렌더러 애셋에 직렬화되면서 참조 체인이 생긴다.
    [Tooltip("비워두면 Hidden/Rendering/MaskBlur 를 찾지만, 빌드에서 스트립될 수 있으므로 반드시 물려 둘 것.")]
    [SerializeField] private Shader _shader;

    [SerializeField]
    private RenderPassEvent _injectionPoint = RenderPassEvent.BeforeRenderingPostProcessing;

    private Material _material;
    private MaskBlurPass _pass;

    public override void Create()
    {
        if (_shader == null)
            _shader = Shader.Find(MaskBlurPass.ShaderName);

        if (_shader != null)
            _material = CoreUtils.CreateEngineMaterial(_shader);

        _pass = new MaskBlurPass(_material)
        {
            renderPassEvent = _injectionPoint
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_material == null || _pass == null)
            return;

        CameraType camType = renderingData.cameraData.cameraType;
        if (camType == CameraType.Preview || camType == CameraType.Reflection)
            return;

        MaskBlurSettings settings = MaskBlurController.ActiveSettings;
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

    // ------------------------------------------------------------------------
    private sealed class MaskBlurPass : ScriptableRenderPass
    {
        public const string ShaderName = "Hidden/Rendering/MaskBlur";

        private const int PassDownH = 0;
        private const int PassV = 1;
        private const int PassComposite = 2;

        private static readonly Vector4 FullScreenScaleBias = new Vector4(1f, 1f, 0f, 0f);

        private static readonly int IdStep = Shader.PropertyToID("_MaskBlurStep");
        private static readonly int IdCenter = Shader.PropertyToID("_MaskBlurCenter");
        private static readonly int IdSize = Shader.PropertyToID("_MaskBlurSize");
        private static readonly int IdShape = Shader.PropertyToID("_MaskBlurShape");
        private static readonly int IdFlags = Shader.PropertyToID("_MaskBlurFlags");
        private static readonly int IdMaskTex = Shader.PropertyToID("_MaskBlurMaskTex");
        private static readonly int IdBlurTex = Shader.PropertyToID("_MaskBlurTex");

        private readonly Material _material;
        private MaskBlurSettings _settings;

        private class PassData
        {
            public Material material;
            public TextureHandle source;
            public int shaderPass;
        }

        public MaskBlurPass(Material material)
        {
            _material = material;
            profilingSampler = new ProfilingSampler("MaskBlur");
        }

        public void Setup(MaskBlurSettings settings) => _settings = settings;

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

            int divisor = 1 << Mathf.Clamp(_settings.downsampleShift, 0, 2);
            RenderTextureDescriptor blurDesc = fullDesc;
            blurDesc.width = Mathf.Max(1, fullDesc.width / divisor);
            blurDesc.height = Mathf.Max(1, fullDesc.height / divisor);

            PushMaterialParams(fullDesc, blurDesc);

            TextureHandle blurA = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, blurDesc, "_MaskBlurA", false, FilterMode.Bilinear);
            TextureHandle blurB = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, blurDesc, "_MaskBlurB", false, FilterMode.Bilinear);
            TextureHandle composed = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, fullDesc, "_MaskBlurComposed", false, FilterMode.Bilinear);

            // 1) 다운샘플 + 가로 블러
            AddBlit(renderGraph, PassNames.DownH, source, blurA, PassDownH, null, 0);

            // 2) 세로 블러 → 결과를 전역 _MaskBlurTex 로 노출한다.
            //    RenderGraph 에서는 raster 패스 안에서 TextureHandle 을 머티리얼에 직접 못 꽂으므로,
            //    생산하는 패스가 SetGlobalTextureAfterPass 로 넘기고 소비하는 패스가 전역으로 읽는다.
            AddBlit(renderGraph, PassNames.Vertical, blurA, blurB, PassV, null, IdBlurTex);

            // 3) 원본과 블러를 마스크로 합성
            AddBlit(renderGraph, PassNames.Composite, source, composed, PassComposite, blurB, 0);

            // 4) 카메라 컬러로 복사
            AddCopyBack(renderGraph, composed, source);
        }

        private void PushMaterialParams(
            RenderTextureDescriptor fullDesc,
            RenderTextureDescriptor blurDesc)
        {
            // 블러 스텝은 블러를 도는 해상도의 텍셀 크기 기준이다.
            // 다운샘플 배율을 곱하지 않는다 — 이미 작은 텍스처의 텍셀이라 자동으로 넓게 퍼진다.
            float stepX = _settings.blurStrength / Mathf.Max(1, blurDesc.width);
            float stepY = _settings.blurStrength / Mathf.Max(1, blurDesc.height);
            _material.SetVector(IdStep, new Vector4(stepX, stepY, 0f, 0f));

            _material.SetVector(
                IdCenter,
                new Vector4(_settings.center.x, _settings.center.y, 0f, 0f));

            Vector2 resolved = _settings.ResolveSize(fullDesc.width, fullDesc.height);
            _material.SetVector(IdSize, new Vector4(resolved.x, resolved.y, 0f, 0f));

            _material.SetVector(
                IdShape,
                new Vector4(
                    _settings.roundness,
                    _settings.feather,
                    _settings.desaturate,
                    _settings.darken));

            bool useTexture = _settings.maskTexture != null;
            _material.SetVector(IdFlags, new Vector4(useTexture ? 1f : 0f, 0f, 0f, 0f));
            if (useTexture)
                _material.SetTexture(IdMaskTex, _settings.maskTexture);

        }

        // globalTexture 가 유효하면 그 핸들을 읽기로 잡는다(합성 패스가 전역으로 샘플).
        // exposeAsGlobalId 가 0 이 아니면 이 패스의 결과를 그 이름의 전역으로 노출한다.
        private void AddBlit(
            RenderGraph renderGraph,
            string passName,
            TextureHandle src,
            TextureHandle dst,
            int shaderPass,
            TextureHandle? globalTexture,
            int exposeAsGlobalId)
        {
            using var builder =
                renderGraph.AddRasterRenderPass<PassData>(passName, out PassData data);

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
                Blitter.BlitTexture(
                    ctx.cmd, d.source, FullScreenScaleBias, d.material, d.shaderPass);
            });
        }

        private static void AddCopyBack(
            RenderGraph renderGraph,
            TextureHandle src,
            TextureHandle dst)
        {
            using var builder =
                renderGraph.AddRasterRenderPass<PassData>(PassNames.CopyBack, out PassData data);

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
