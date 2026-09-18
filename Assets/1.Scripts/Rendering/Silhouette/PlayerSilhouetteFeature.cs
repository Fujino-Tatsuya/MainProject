// ----------------------------------------------------------------------------
//  PlayerSilhouetteFeature.cs — 벽에 가린 플레이어의 윤곽선 (URP 17 / RenderGraph)
//
//  패스 2개.
//    1) 마스크: 플레이어 모델을 전용 컬러+깊이 RT 에 다시 그린다.
//               R=커버리지 G=가려짐 B=소속(0 내캐릭터 / 1 남) A=커버리지
//    2) 합성:   (커버리지 침식 테두리) × 가려짐 → 카메라 컬러에 알파 블렌딩.
//
//  🔴 대상은 레이어가 아니라 **렌더링 레이어 비트**로 고른다. 실제 플레이어 프리팹(Paladin)의
//     렌더러가 전부 layer 0 이라(루트만 6) 레이어 필터로는 아무것도 안 그려진다.
//     비트를 붙이는 쪽은 PlayerSilhouetteTag / PlayerSilhouetteLayers.
//
//  🔴 색은 머티리얼이 아니라 **합성 패스의 유니폼**이다. 마스크는 소속만 0/1 로 담는다.
//     MaterialPropertyBlock 을 쓰지 않는 이유는 HitFlash 가 SetPropertyBlock(null) 로
//     블록을 통째로 지우기 때문이다 — 피격 한 번에 색이 날아간다.
// ----------------------------------------------------------------------------
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public sealed class PlayerSilhouetteFeature : ScriptableRendererFeature
{
    public static class PassNames
    {
        public const string Mask = "PlayerSilhouette Mask";
        public const string Composite = "PlayerSilhouette Composite";
    }

    // 🔴 셰이더를 직렬화 참조로 들고 있어야 한다. Shader.Find 만 쓰면 어떤 머티리얼·씬도
    //    참조하지 않는 셰이더가 되어 빌드에서 스트립된다(이 저장소에서 미니맵이 이걸로 안 보였다).
    [Header("셰이더 (반드시 물려 둘 것)")]
    [SerializeField] private Shader _maskShader;
    [SerializeField] private Shader _compositeShader;

    [Header("색")]
    [Tooltip("보는 사람 자신의 캐릭터.")]
    [SerializeField] private Color _localColor = new Color(0.25f, 1f, 0.45f, 1f);
    [Tooltip("같이 하는 다른 플레이어.")]
    [SerializeField] private Color _remoteColor = new Color(0.3f, 0.65f, 1f, 1f);

    [Header("선")]
    [Tooltip("선 굵기(픽셀). 1 이 가장 얇다. PixelScanline 이 뒤에서 블록 재샘플을 하므로 " +
             "너무 얇으면 끊겨 보일 수 있다 — Play 로 고를 것.")]
    [Range(1f, 5f)]
    [SerializeField] private float _outlineWidth = 1.5f;

    [Tooltip("자기 자신을 '가려졌다'로 오판하지 않게 하는 깊이 여유(m). " +
             "너무 작으면 캐릭터가 안 가렸는데도 윤곽선이 깜빡인다.")]
    [SerializeField] private float _occludeBias = 0.03f;

    [Header("주입")]
    [Tooltip("기본 BeforeRenderingPostProcessing(550). " +
             "⚠️ AfterRenderingTransparents(500) 도 포스트프로세스 이전이다 — 둘의 차이는 " +
             "블룸 여부가 아니라 다른 550 피처(MaskBlur)와의 순서다.")]
    [SerializeField]
    private RenderPassEvent _injectionPoint = RenderPassEvent.BeforeRenderingPostProcessing;

    /// <summary>
    /// 개발용 on/off. 비용 A/B 측정(<c>RenderCostAB</c>)이 런타임에 끈다.
    /// 🔴 <b>정적</b>인 이유: 렌더러 피처는 씬 오브젝트가 아니라 렌더러 애셋의 서브에셋이라
    /// 씬 쪽에서 참조를 잡기가 번거롭다. 리플렉션으로 파내는 대신 플래그 하나로 끝낸다.
    /// 기본값은 <c>true</c> — 측정 도구가 없으면 항상 켜져 있어야 한다.
    /// </summary>
    public static bool DevEnabled = true;

    private Material _maskLocalMaterial;
    private Material _maskRemoteMaterial;
    private Material _compositeMaterial;
    private SilhouettePass _pass;

    public override void Create()
    {
        // 🔴 재생성 전에 기존 머티리얼을 버린다. Create() 는 인스펙터를 만질 때마다 다시 불린다 —
        //    안 버리면 도메인 리로드까지 머티리얼이 계속 쌓인다(PixelScanlineFeature 가 이걸 한다).
        DestroyMaterials();

        if (_maskShader == null) _maskShader = Shader.Find(SilhouettePass.MaskShaderName);
        if (_compositeShader == null) _compositeShader = Shader.Find(SilhouettePass.CompositeShaderName);

        if (_maskShader != null)
        {
            _maskLocalMaterial = CoreUtils.CreateEngineMaterial(_maskShader);
            _maskRemoteMaterial = CoreUtils.CreateEngineMaterial(_maskShader);
            _maskLocalMaterial.SetFloat(SilhouettePass.IdSilhouetteId, 0f);
            _maskRemoteMaterial.SetFloat(SilhouettePass.IdSilhouetteId, 1f);
        }

        if (_compositeShader != null)
            _compositeMaterial = CoreUtils.CreateEngineMaterial(_compositeShader);

        _pass = new SilhouettePass(_maskLocalMaterial, _maskRemoteMaterial, _compositeMaterial)
        {
            renderPassEvent = _injectionPoint
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!DevEnabled)
            return;

        if (_pass == null || _compositeMaterial == null || _maskLocalMaterial == null)
            return;

        // 프리뷰·리플렉션·미니맵(targetTexture) 카메라에는 걸지 않는다.
        // 오버레이 카메라에 중복으로 걸리는 것도 막는다(PixelScanlineFeature 와 같은 이유).
        UniversalAdditionalCameraData camData = renderingData.cameraData.camera
            .GetUniversalAdditionalCameraData();
        CameraType camType = renderingData.cameraData.cameraType;

        if (camType == CameraType.Preview || camType == CameraType.Reflection)
            return;
        if (renderingData.cameraData.camera.targetTexture != null)
            return;
        if (camData != null && camData.renderType == CameraRenderType.Overlay)
            return;

        _pass.Setup(_localColor, _remoteColor, _outlineWidth, _occludeBias);

        // 🔴 깊이 텍스처는 "있겠거니" 하면 안 된다. 여기서 요청해야 URP 가 만들어 준다.
        //    RP 에셋의 Depth Texture 체크박스와는 별개다(체크가 꺼져 있어도 이 요청이 이긴다).
        _pass.ConfigureInput(ScriptableRenderPassInput.Depth);

        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing) => DestroyMaterials();

    private void DestroyMaterials()
    {
        CoreUtils.Destroy(_maskLocalMaterial);
        CoreUtils.Destroy(_maskRemoteMaterial);
        CoreUtils.Destroy(_compositeMaterial);
        _maskLocalMaterial = null;
        _maskRemoteMaterial = null;
        _compositeMaterial = null;
    }

    // ------------------------------------------------------------------------
    private sealed class SilhouettePass : ScriptableRenderPass
    {
        public const string MaskShaderName = "Hidden/Rendering/PlayerSilhouetteMask";
        public const string CompositeShaderName = "Hidden/Rendering/PlayerSilhouetteComposite";

        public static readonly int IdSilhouetteId = Shader.PropertyToID("_SilhouetteId");
        private static readonly int IdOccludeBias = Shader.PropertyToID("_OccludeBias");
        private static readonly int IdLocalColor = Shader.PropertyToID("_SilhouetteLocalColor");
        private static readonly int IdRemoteColor = Shader.PropertyToID("_SilhouetteRemoteColor");
        private static readonly int IdOutline = Shader.PropertyToID("_SilhouetteOutline");

        private static readonly Vector4 FullScreenScaleBias = new Vector4(1f, 1f, 0f, 0f);

        // overrideMaterial 을 써도 "어떤 렌더러를 그릴지"는 원본 셰이더의 패스 태그로 거른다.
        // 이 셋이 URP 불투명 렌더러가 갖는 태그다 — 빠뜨리면 해당 캐릭터가 통째로 누락된다.
        private static readonly List<ShaderTagId> ShaderTags = new()
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit"),
        };

        private readonly Material _maskLocal;
        private readonly Material _maskRemote;
        private readonly Material _composite;

        private Color _localColor;
        private Color _remoteColor;
        private float _outlineWidth;
        private float _occludeBias;

        private class MaskPassData
        {
            public RendererListHandle localList;
            public RendererListHandle remoteList;
        }

        private class CompositePassData
        {
            public Material material;
            public TextureHandle mask;
        }

        public SilhouettePass(Material maskLocal, Material maskRemote, Material composite)
        {
            _maskLocal = maskLocal;
            _maskRemote = maskRemote;
            _composite = composite;
            profilingSampler = new ProfilingSampler("PlayerSilhouette");
        }

        public void Setup(Color local, Color remote, float outlineWidth, float occludeBias)
        {
            _localColor = local;
            _remoteColor = remote;
            _outlineWidth = outlineWidth;
            _occludeBias = occludeBias;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_composite == null || _maskLocal == null || _maskRemote == null)
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            if (resourceData.isActiveTargetBackBuffer)
                return;

            TextureHandle cameraColor = resourceData.activeColorTexture;
            if (!cameraColor.IsValid())
                return;

            _maskLocal.SetFloat(IdOccludeBias, _occludeBias);
            _maskRemote.SetFloat(IdOccludeBias, _occludeBias);

            // 🔴 마스크는 풀해상도·MSAA 1·Point 다.
            //    반해상도로 내리면 가는 팔다리·무기가 통째로 사라지고 선 굵기도 같이 바뀐다.
            RenderTextureDescriptor maskDesc = cameraData.cameraTargetDescriptor;
            maskDesc.msaaSamples = 1;
            maskDesc.depthBufferBits = 0;
            maskDesc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm;

            RenderTextureDescriptor depthDesc = cameraData.cameraTargetDescriptor;
            depthDesc.msaaSamples = 1;
            depthDesc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.None;
            depthDesc.depthBufferBits = 32;

            // 🔴 매 프레임 0 으로 지운다. 일부 픽셀에만 그리는 마스크라 안 지우면 지난 프레임의
            //    실루엣이 남아 잔상이 생긴다(MaskBlur 의 clear=false 를 그대로 복사하면 안 된다).
            TextureHandle mask = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, maskDesc, "_PlayerSilhouetteMask", true, FilterMode.Point);
            TextureHandle maskDepth = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, depthDesc, "_PlayerSilhouetteMaskDepth", true, FilterMode.Point);

            AddMaskPass(renderGraph, resourceData, renderingData, cameraData, lightData, mask, maskDepth);
            AddCompositePass(renderGraph, mask, cameraColor, maskDesc);
        }

        private void AddMaskPass(
            RenderGraph renderGraph,
            UniversalResourceData resourceData,
            UniversalRenderingData renderingData,
            UniversalCameraData cameraData,
            UniversalLightData lightData,
            TextureHandle mask,
            TextureHandle maskDepth)
        {
            using var builder =
                renderGraph.AddRasterRenderPass<MaskPassData>(PassNames.Mask, out MaskPassData data);

            data.localList = CreateList(renderGraph, renderingData, cameraData, lightData,
                _maskLocal, PlayerSilhouetteLayers.LocalMask);
            data.remoteList = CreateList(renderGraph, renderingData, cameraData, lightData,
                _maskRemote, PlayerSilhouetteLayers.RemoteMask);

            builder.UseRendererList(data.localList);
            builder.UseRendererList(data.remoteList);

            // 마스크 셰이더가 _CameraDepthTexture 를 샘플한다 — 읽기 의존성을 선언해야
            // RenderGraph 가 이 패스를 지우지 않고, 깊이 생산 패스보다 뒤에 놓는다.
            if (resourceData.cameraDepthTexture.IsValid())
                builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
            builder.UseAllGlobalTextures(true);

            builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
            builder.SetRenderAttachmentDepth(maskDepth, AccessFlags.Write);

            builder.SetRenderFunc(static (MaskPassData d, RasterGraphContext ctx) =>
            {
                // 둘은 같은 깊이 버퍼를 공유한다 — 겹쳐도 "나중에 그린 쪽"이 아니라
                // 실제로 앞에 있는 쪽이 이긴다.
                ctx.cmd.DrawRendererList(d.localList);
                ctx.cmd.DrawRendererList(d.remoteList);
            });
        }

        private RendererListHandle CreateList(
            RenderGraph renderGraph,
            UniversalRenderingData renderingData,
            UniversalCameraData cameraData,
            UniversalLightData lightData,
            Material overrideMaterial,
            uint renderingLayerMask)
        {
            DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(
                ShaderTags, renderingData, cameraData, lightData, SortingCriteria.CommonOpaque);

            drawingSettings.overrideMaterial = overrideMaterial;
            drawingSettings.overrideMaterialPassIndex = 0;

            // layerMask 는 전부 통과시키고(-1), 실제 선별은 렌더링 레이어 비트로 한다.
            var filtering = new FilteringSettings(RenderQueueRange.opaque, -1, renderingLayerMask);

            return renderGraph.CreateRendererList(
                new RendererListParams(renderingData.cullResults, drawingSettings, filtering));
        }

        private void AddCompositePass(
            RenderGraph renderGraph,
            TextureHandle mask,
            TextureHandle cameraColor,
            RenderTextureDescriptor maskDesc)
        {
            _composite.SetColor(IdLocalColor, _localColor);
            _composite.SetColor(IdRemoteColor, _remoteColor);
            _composite.SetVector(IdOutline, new Vector4(
                1f / Mathf.Max(1, maskDesc.width),
                1f / Mathf.Max(1, maskDesc.height),
                _outlineWidth,
                0f));

            using var builder = renderGraph.AddRasterRenderPass<CompositePassData>(
                PassNames.Composite, out CompositePassData data);

            data.material = _composite;
            data.mask = mask;

            builder.UseTexture(mask, AccessFlags.Read);

            // 🔴 ReadWrite 다. Write 로만 잡으면 기존 화면을 버리고 시작할 수 있다 —
            //    우리는 그 위에 선만 얹는 것이라 기존 색이 남아 있어야 한다.
            builder.SetRenderAttachment(cameraColor, 0, AccessFlags.ReadWrite);

            builder.SetRenderFunc(static (CompositePassData d, RasterGraphContext ctx) =>
            {
                Blitter.BlitTexture(ctx.cmd, d.mask, FullScreenScaleBias, d.material, 0);
            });
        }
    }
}
