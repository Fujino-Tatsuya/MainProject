// ----------------------------------------------------------------------------
//  FogRendererFeature.cs - URP RenderGraph 풀스크린 포그 렌더러 피처
//
//  Unity 6 (URP 17) RenderGraph 경로. 불투명 렌더 후 풀스크린 패스로
//  씬색에 포그를 블렌딩한다. PC_Renderer / Mobile_Renderer 양쪽에 추가.
//
//  RenderGraph 배선(ConfigureInput(Color|Depth), AddRasterRenderPass +
//  Blitter.BlitTexture, 중간 텍스처 ping-pong)은 meryuhi/URPFog (MIT)의
//  FullScreenFogRendererFeature 접근을 참조해 재작성. 고지: THIRD_PARTY_NOTICES.md
// ----------------------------------------------------------------------------
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public sealed class FogRendererFeature : ScriptableRendererFeature
{
    [Tooltip("비워두면 Hidden/Fog/FullScreenFog 를 자동으로 찾는다.")]
    [SerializeField] private Shader _shader;

    [Tooltip("포그를 주입할 시점. 보통 불투명 직후(투명/UI 위에 올리지 않음).")]
    [SerializeField] private RenderPassEvent _injectionPoint = RenderPassEvent.BeforeRenderingTransparents;

    private Material _material;
    private FogPass _pass;

    public override void Create()
    {
        if (_shader == null)
            _shader = Shader.Find(FogPass.ShaderName);

        if (_shader != null)
            _material = CoreUtils.CreateEngineMaterial(_shader);

        _pass = new FogPass(_material)
        {
            renderPassEvent = _injectionPoint
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_material == null)
            return;

        CameraType camType = renderingData.cameraData.cameraType;
        if (camType == CameraType.Preview || camType == CameraType.Reflection)
            return;

        if (!FogManager.HasActiveInstance)
            return;

        // 깊이만 필요(씬색은 활성 컬러를 Blitter 가 _BlitTexture 로 바인딩).
        _pass.ConfigureInput(ScriptableRenderPassInput.Depth);
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(_material);
        _material = null;
    }

    // ------------------------------------------------------------------------
    private sealed class FogPass : ScriptableRenderPass
    {
        public const string ShaderName = "Hidden/Fog/FullScreenFog";

        private static readonly Vector4 FullScreenScaleBias = new Vector4(1f, 1f, 0f, 0f);

        private readonly Material _material;

        private class PassData
        {
            public Material material;
            public TextureHandle source;
        }

        public FogPass(Material material)
        {
            _material = material;
            profilingSampler = new ProfilingSampler("FullScreenFog");
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_material == null)
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            if (resourceData.isActiveTargetBackBuffer)
                return;

            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid())
                return;

            RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;

            TextureHandle dest = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, desc, "_FullScreenFogTex", false, FilterMode.Bilinear);

            // 1) source -> dest (포그 머티리얼 적용)
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("FullScreenFog", out PassData data))
            {
                data.material = _material;
                data.source = source;

                builder.UseTexture(source, AccessFlags.Read);

                TextureHandle depth = resourceData.cameraDepthTexture;
                if (depth.IsValid())
                    builder.UseTexture(depth, AccessFlags.Read);

                // _CameraDepthTexture / _FogNoiseTex / _FogMaskTex 등 글로벌 텍스처를
                // raster 패스에서 셰이더가 샘플할 수 있도록 바인딩.
                builder.UseAllGlobalTextures(true);

                builder.SetRenderAttachment(dest, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (PassData d, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(ctx.cmd, d.source, FullScreenScaleBias, d.material, 0);
                });
            }

            // 2) dest -> source (카메라 컬러로 복사)
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("FullScreenFog CopyBack", out PassData data))
            {
                data.source = dest;
                builder.UseTexture(dest, AccessFlags.Read);
                builder.SetRenderAttachment(source, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (PassData d, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(ctx.cmd, d.source, FullScreenScaleBias, 0f, false);
                });
            }
        }
    }
}
