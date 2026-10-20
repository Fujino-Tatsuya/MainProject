// ----------------------------------------------------------------------------
//  EnemyOutlineFeature.cs — 몬스터 외곽선(할 일 58) 화면 공간 마스크 방식 (URP 17 / RenderGraph)
//
//  구조는 PlayerSilhouetteFeature 를 본떴다. 패스 2개.
//    1) 마스크: EnemyOutlineDrawList 의 렌더러만 전용 컬러+깊이 RT 에 다시 그린다(DrawRenderer).
//               R=커버리지 G=색 인덱스(0 빨강 / 1 나를 노림 회색) B=그 픽셀 맨 앞 대상 표면의 eye 깊이(m) A=커버리지
//    2) 합성:   마스크를 N px 원판으로 넓힌 것 − 원래 마스크 = 바깥 윤곽만 → 카메라 컬러에 알파 블렌딩.
//               윤곽 픽셀의 씬 깊이가 대상보다 앞이면(벽 등) 그 픽셀은 버린다 = 가려진 부분은 안 그림.
//
//  🔴 대상은 레이어도 렌더링 레이어 비트도 아니라 **렌더러 목록**이다. 레이어·원본 머티리얼을 바꾸지 않는다.
//  🔴 여러 렌더러·서브메시가 한 마스크에 합쳐지므로 23호처럼 메시가 여러 개여도 바깥 윤곽 하나가 된다(안쪽 선 없음).
//  🔴 가림은 마스크에서 픽셀을 지우지 않고 **윤곽 픽셀에서** 판정한다 — 마스크에서 지우면 벽이 몸을 자르는
//     경계에 가짜 선이 생긴다(PlayerSilhouetteMask.shader 설계 2판과 같은 이유).
// ----------------------------------------------------------------------------
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public sealed class EnemyOutlineFeature : ScriptableRendererFeature
{
    public static class PassNames
    {
        public const string Mask = "EnemyOutline Mask";
        public const string Composite = "EnemyOutline Composite";
    }

    // 🔴 셰이더를 직렬화 참조로 들고 있어야 한다. Shader.Find 만 쓰면 빌드에서 스트립된다(PlayerSilhouetteFeature 와 같은 이유).
    [Header("셰이더 (반드시 물려 둘 것)")]
    [SerializeField] private Shader _maskShader;
    [SerializeField] private Shader _compositeShader;

    [Header("가림")]
    [Tooltip("윤곽 픽셀의 씬 깊이가 대상 표면보다 이만큼(m) 이상 앞이면 가려진 것으로 보고 안 그린다. " +
             "너무 작으면 발밑 바닥이 윤곽을 잘라 먹고, 너무 크면 얇은 벽 너머로 윤곽이 샌다.")]
    [SerializeField] private float _occludeBias = 0.2f;

    [Header("주입")]
    [Tooltip("기본 BeforeRenderingPostProcessing(550). 같은 이벤트의 PlayerSilhouetteFeature 보다 렌더러 목록에서 " +
             "뒤에 있어 그 뒤에 합성된다.")]
    [SerializeField]
    private RenderPassEvent _injectionPoint = RenderPassEvent.BeforeRenderingPostProcessing;

    /// <summary>
    /// [에디터 캡처 전용] 이 카메라는 targetTexture 가 있어도 외곽선을 건다(Play 없이 전/후 캡처할 때).
    /// 기본 null — 미니맵 같은 RT 카메라에는 걸지 않는다.
    /// </summary>
    public static Camera DevCaptureCamera;

    /// <summary>
    /// 인스펙터 색 → 합성 셰이더에 넘길 값. 합성 셰이더에 Properties 블록이 없어 SetColor 가 감마→선형 변환을
    /// 하지 않으므로, Linear 프로젝트면 여기서 직접 바꿔야 인스펙터 값이 화면 sRGB 값으로 그대로 보인다.
    /// 알파는 그대로. Gamma 프로젝트면 변환 없음.
    /// </summary>
    public static Color ToShaderColor(Color inspectorColor, ColorSpace colorSpace) =>
        colorSpace == ColorSpace.Linear ? inspectorColor.linear : inspectorColor;

    private Material _maskPrimaryMaterial;
    private Material _maskAggroMaterial;
    private Material _compositeMaterial;
    private OutlinePass _pass;

    public override void Create()
    {
        DestroyMaterials();

        if (_maskShader == null) _maskShader = Shader.Find(OutlinePass.MaskShaderName);
        if (_compositeShader == null) _compositeShader = Shader.Find(OutlinePass.CompositeShaderName);

        if (_maskShader != null)
        {
            _maskPrimaryMaterial = CoreUtils.CreateEngineMaterial(_maskShader);
            _maskAggroMaterial = CoreUtils.CreateEngineMaterial(_maskShader);
            _maskPrimaryMaterial.SetFloat(OutlinePass.IdColorIndex, EnemyOutlineDrawList.PrimaryColorIndex);
            _maskAggroMaterial.SetFloat(OutlinePass.IdColorIndex, EnemyOutlineDrawList.AggroColorIndex);
        }

        if (_compositeShader != null)
            _compositeMaterial = CoreUtils.CreateEngineMaterial(_compositeShader);

        _pass = new OutlinePass(_maskPrimaryMaterial, _maskAggroMaterial, _compositeMaterial)
        {
            renderPassEvent = _injectionPoint
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_pass == null || _compositeMaterial == null || _maskPrimaryMaterial == null)
            return;

        // 칠할 대상이 없으면(대부분의 프레임) 마스크 RT 클리어와 합성을 통째로 건너뛴다.
        if (EnemyOutlineDrawList.Count <= 0)
            return;

        Camera camera = renderingData.cameraData.camera;
        UniversalAdditionalCameraData camData = camera.GetUniversalAdditionalCameraData();
        CameraType camType = renderingData.cameraData.cameraType;

        if (camType == CameraType.Preview || camType == CameraType.Reflection)
            return;
        if (camera.targetTexture != null && camera != DevCaptureCamera)
            return;
        if (camData != null && camData.renderType == CameraRenderType.Overlay)
            return;

        ColorSpace colorSpace = QualitySettings.activeColorSpace;
        _pass.Setup(ToShaderColor(EnemyOutlineDrawList.PrimaryColor, colorSpace),
            ToShaderColor(EnemyOutlineDrawList.AggroColor, colorSpace),
            EnemyOutlineDrawList.WidthPixels, _occludeBias);

        // 합성 셰이더가 _CameraDepthTexture 를 읽는다 — 여기서 요청해야 URP 가 만든다.
        _pass.ConfigureInput(ScriptableRenderPassInput.Depth);

        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing) => DestroyMaterials();

    private void DestroyMaterials()
    {
        CoreUtils.Destroy(_maskPrimaryMaterial);
        CoreUtils.Destroy(_maskAggroMaterial);
        CoreUtils.Destroy(_compositeMaterial);
        _maskPrimaryMaterial = null;
        _maskAggroMaterial = null;
        _compositeMaterial = null;
    }

    // ------------------------------------------------------------------------
    private sealed class OutlinePass : ScriptableRenderPass
    {
        public const string MaskShaderName = "Hidden/Rendering/EnemyOutlineMask";
        public const string CompositeShaderName = "Hidden/Rendering/EnemyOutlineComposite";

        public static readonly int IdColorIndex = Shader.PropertyToID("_OutlineColorIndex");
        private static readonly int IdPrimaryColor = Shader.PropertyToID("_OutlinePrimaryColor");
        private static readonly int IdAggroColor = Shader.PropertyToID("_OutlineAggroColor");
        private static readonly int IdOutline = Shader.PropertyToID("_OutlineParams");

        private static readonly Vector4 FullScreenScaleBias = new Vector4(1f, 1f, 0f, 0f);

        private readonly Material _maskPrimary;
        private readonly Material _maskAggro;
        private readonly Material _composite;

        private Color _primaryColor;
        private Color _aggroColor;
        private int _widthPixels;
        private float _occludeBias;

        private class MaskPassData
        {
            // RenderGraph 가 패스 데이터를 풀링한다 — 리스트는 인스턴스당 한 번만 만든다.
            public readonly List<EnemyOutlineDrawList.Item> items = new List<EnemyOutlineDrawList.Item>(64);
            public Material primary;
            public Material aggro;
        }

        private class CompositePassData
        {
            public Material material;
            public TextureHandle mask;
        }

        public OutlinePass(Material maskPrimary, Material maskAggro, Material composite)
        {
            _maskPrimary = maskPrimary;
            _maskAggro = maskAggro;
            _composite = composite;
            profilingSampler = new ProfilingSampler("EnemyOutline");
        }

        public void Setup(Color primary, Color aggro, int widthPixels, float occludeBias)
        {
            _primaryColor = primary;
            _aggroColor = aggro;
            _widthPixels = widthPixels;
            _occludeBias = occludeBias;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_composite == null || _maskPrimary == null || _maskAggro == null)
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            if (resourceData.isActiveTargetBackBuffer)
                return;

            TextureHandle cameraColor = resourceData.activeColorTexture;
            if (!cameraColor.IsValid())
                return;

            // 🔴 마스크는 풀해상도·MSAA 1·Point 다(반해상도면 가는 부위가 사라지고 선 굵기가 해상도에 따라 바뀐다).
            //    B 에 eye 깊이(m)를 담으므로 half float.
            RenderTextureDescriptor maskDesc = cameraData.cameraTargetDescriptor;
            maskDesc.msaaSamples = 1;
            maskDesc.depthBufferBits = 0;
            maskDesc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat;

            RenderTextureDescriptor depthDesc = cameraData.cameraTargetDescriptor;
            depthDesc.msaaSamples = 1;
            depthDesc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.None;
            depthDesc.depthBufferBits = 32;

            // 🔴 매 프레임 0 으로 지운다 — 안 지우면 지난 프레임 대상이 잔상으로 남는다.
            TextureHandle mask = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, maskDesc, "_EnemyOutlineMask", true, FilterMode.Point);
            TextureHandle maskDepth = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, depthDesc, "_EnemyOutlineMaskDepth", true, FilterMode.Point);

            AddMaskPass(renderGraph, mask, maskDepth);
            AddCompositePass(renderGraph, resourceData, mask, cameraColor, maskDesc);
        }

        private void AddMaskPass(RenderGraph renderGraph, TextureHandle mask, TextureHandle maskDepth)
        {
            using var builder =
                renderGraph.AddRasterRenderPass<MaskPassData>(PassNames.Mask, out MaskPassData data);

            data.items.Clear();
            for (int i = 0; i < EnemyOutlineDrawList.Count; i++)
                data.items.Add(EnemyOutlineDrawList.Get(i));
            data.primary = _maskPrimary;
            data.aggro = _maskAggro;

            builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
            builder.SetRenderAttachmentDepth(maskDepth, AccessFlags.Write);

            builder.SetRenderFunc(static (MaskPassData d, RasterGraphContext ctx) =>
            {
                // 모든 대상이 같은 깊이 버퍼를 공유한다 — 겹친 자리는 실제로 앞에 있는 대상의 색·깊이가 남는다.
                for (int i = 0; i < d.items.Count; i++)
                {
                    EnemyOutlineDrawList.Item item = d.items[i];
                    if (item.Renderer == null)
                        continue;

                    Material m = item.ColorIndex == EnemyOutlineDrawList.AggroColorIndex ? d.aggro : d.primary;
                    for (int s = 0; s < item.SubMeshCount; s++)
                        ctx.cmd.DrawRenderer(item.Renderer, m, s, 0);
                }
            });
        }

        private void AddCompositePass(
            RenderGraph renderGraph,
            UniversalResourceData resourceData,
            TextureHandle mask,
            TextureHandle cameraColor,
            RenderTextureDescriptor maskDesc)
        {
            _composite.SetColor(IdPrimaryColor, _primaryColor);
            _composite.SetColor(IdAggroColor, _aggroColor);
            _composite.SetVector(IdOutline, new Vector4(
                1f / Mathf.Max(1, maskDesc.width),
                1f / Mathf.Max(1, maskDesc.height),
                _widthPixels,
                _occludeBias));

            using var builder = renderGraph.AddRasterRenderPass<CompositePassData>(
                PassNames.Composite, out CompositePassData data);

            data.material = _composite;
            data.mask = mask;

            builder.UseTexture(mask, AccessFlags.Read);

            // 합성 셰이더가 _CameraDepthTexture 를 샘플한다 — 읽기 의존성을 선언해 깊이 생산 패스 뒤에 놓는다.
            if (resourceData.cameraDepthTexture.IsValid())
                builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
            builder.UseAllGlobalTextures(true);

            // 🔴 ReadWrite — 기존 화면 위에 선만 얹는다.
            builder.SetRenderAttachment(cameraColor, 0, AccessFlags.ReadWrite);

            builder.SetRenderFunc(static (CompositePassData d, RasterGraphContext ctx) =>
            {
                Blitter.BlitTexture(ctx.cmd, d.mask, FullScreenScaleBias, d.material, 0);
            });
        }
    }
}
