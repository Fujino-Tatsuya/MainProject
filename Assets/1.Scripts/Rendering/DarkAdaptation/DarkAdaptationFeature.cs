using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// 간파 암전 렌더 기능 — 한 기능에 모드 2개(할 일 87 C3 → 132).
//  · 휘도 암순응(팀원·A2): 휘도 임계보다 어두운 픽셀만 낮추고, 시전자 주변은 원형으로 살린다.
//    후처리 전(HDR)에 돌아 임계를 HDR 휘도로 보고, 남긴 밝은 픽셀에서 Bloom 이 그대로 나온다.
//  · 마스크 암전(오너): 렌더링 레이어 비트 4(InterruptOwnerMaskLayers)를 가진 렌더러만 투명 검정 위에 다시 그려 '남김 레이어'를 만들고,
//    그 덮개 알파(1 − 남김)를 DarkAdaptationState.MaskCoverTarget 에 쓴다. 실제 덮기는 HUD 위 최상위 오버레이 캔버스(InterruptOwnerHudCover)가
//    칸 색·불투명도로 한다 — 오버레이 UI 는 URP 마지막(AfterRendering 뒤)에 그려져 기능 패스로는 HUD 를 덮을 수 없다.
// 강도는 DarkAdaptationState 에서 읽는다 — 모드마다 0 이면 그 패스를 넣지 않는다(평소 비용 0).
public sealed class DarkAdaptationFeature : ScriptableRendererFeature
{
    public const string PassName = "Interrupt DarkAdaptation";
    public const string MaskKeepPassName = "Interrupt OwnerDim Keep";
    public const string MaskCoverPassName = "Interrupt OwnerDim Cover";

    [Tooltip("암순응 전체화면 셰이더(패스 0 휘도 · 패스 1 마스크 덮개). Shader.Find 는 빌드에서 빠지므로 직접 연결한다.")]
    [SerializeField] private Shader _shader;

    private Material _material;
    private DarkAdaptationPass _pass;
    private OwnerMaskPass _maskPass;

    public override void Create()
    {
        CoreUtils.Destroy(_material);
        _material = _shader != null ? CoreUtils.CreateEngineMaterial(_shader) : null;
        _pass = new DarkAdaptationPass
        {
            // Fog(450)·Silhouette/EnemyOutline/MaskBlur(550) 뒤 — 같은 550 끼리는 기능 목록 순서라 목록 맨 끝에 둔다.
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing
        };
        _maskPass?.Dispose();
        _maskPass = new OwnerMaskPass
        {
            // 휘도 패스와 같은 칸 — 넣는 순서대로(휘도 → 마스크) 돈다.
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing
        };

        if (_shader == null)
            Debug.LogWarning("[DarkAdaptation] 셰이더가 연결되지 않아 효과가 실행되지 않는다.", this);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        DarkAdaptationMode mode = DarkAdaptationModes.Select(DarkAdaptationState.Intensity,
            DarkAdaptationState.MaskIntensity, DarkAdaptationState.MaskCoverTarget != null);
        if (mode == DarkAdaptationMode.None || _material == null || _pass == null || _maskPass == null)
            return;

        CameraData cameraData = renderingData.cameraData;
        Camera camera = cameraData.camera;

        // Scene View, 미리보기, 반사, 미니맵 RT와 Overlay 카메라는 제외한다.
        if (cameraData.cameraType != CameraType.Game ||
            cameraData.renderType == CameraRenderType.Overlay ||
            camera == null || camera.targetTexture != null)
            return;

        if ((mode & DarkAdaptationMode.Luminance) != 0)
        {
            _pass.Setup(_material);
            renderer.EnqueuePass(_pass);
        }

        if ((mode & DarkAdaptationMode.Mask) != 0)
        {
            _maskPass.Setup(_material, DarkAdaptationState.MaskCoverTarget);
            renderer.EnqueuePass(_maskPass);
        }
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(_material);
        _material = null;
        _maskPass?.Dispose();
        _maskPass = null;
    }

    // 마스크 암전 — 남김 레이어(비트 4 렌더러를 원래 머티리얼로, 카메라 깊이로 가림 판정) → 덮개 알파를 외부 RT 에 쓴다.
    // 카메라 컬러는 건드리지 않는다(덮기는 오버레이 캔버스). 덮개 RT 는 그래프 밖(UI)이 읽으니 패스 컬링을 끈다.
    private sealed class OwnerMaskPass : ScriptableRenderPass
    {
        private static readonly Vector4 FullScreenScaleBias = new Vector4(1f, 1f, 0f, 0f);

        // URP 불투명·투명 렌더러가 갖는 패스 태그(잔상 = UniversalForward). 실루엣 기능과 같다.
        private static readonly List<ShaderTagId> ShaderTags = new()
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit"),
        };

        private Material _material;
        private RenderTexture _coverTarget;
        private RenderTexture _wrappedTarget;
        private RTHandle _coverHandle;

        private sealed class KeepPassData
        {
            public RendererListHandle opaque;
            public RendererListHandle transparent;
        }

        private sealed class CoverPassData
        {
            public Material material;
            public TextureHandle keep;
        }

        public OwnerMaskPass()
        {
            profilingSampler = new ProfilingSampler(MaskKeepPassName);
        }

        public void Setup(Material material, RenderTexture coverTarget)
        {
            _material = material;
            _coverTarget = coverTarget;
        }

        public void Dispose()
        {
            _coverHandle?.Release();
            _coverHandle = null;
            _wrappedTarget = null;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_material == null || _coverTarget == null)
                return;

            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            if (resources.isActiveTargetBackBuffer)
                return;

            TextureHandle color = resources.activeColorTexture;
            TextureHandle depth = resources.activeDepthTexture;
            if (!color.IsValid() || !depth.IsValid())
                return;

            // 외부 RT 감싸기 — 대상이 바뀔 때만 다시 만든다(화면 크기 변경 등).
            if (_wrappedTarget != _coverTarget || _coverHandle == null)
            {
                _coverHandle?.Release();
                _coverHandle = RTHandles.Alloc(_coverTarget);
                _wrappedTarget = _coverTarget;
            }

            // 남김 레이어 — 카메라 컬러와 크기·MSAA 는 같게(깊이 첨부와 맞춘다), 형식은 알파가 있는 HDR 로(기본 HDR 형식은 알파가 없다).
            TextureDesc keepDesc = renderGraph.GetTextureDesc(color);
            keepDesc.name = "_InterruptOwnerDimKeep";
            keepDesc.format = GraphicsFormat.R16G16B16A16_SFloat;
            keepDesc.clearBuffer = true;
            keepDesc.clearColor = Color.clear;
            TextureHandle keep = renderGraph.CreateTexture(keepDesc);

            using (var builder = renderGraph.AddRasterRenderPass<KeepPassData>(MaskKeepPassName, out KeepPassData data, profilingSampler))
            {
                data.opaque = CreateList(renderGraph, renderingData, cameraData, lightData,
                    RenderQueueRange.opaque, SortingCriteria.CommonOpaque);
                data.transparent = CreateList(renderGraph, renderingData, cameraData, lightData,
                    RenderQueueRange.transparent, SortingCriteria.CommonTransparent);
                builder.UseRendererList(data.opaque);
                builder.UseRendererList(data.transparent);

                // 원래 머티리얼로 그린다 — 그림자·SSAO 등 전역 텍스처를 그대로 쓴다(RenderObjects 와 같다).
                builder.UseAllGlobalTextures(true);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderAttachment(keep, 0, AccessFlags.Write);
                // 카메라 깊이로 가림 판정 — 벽 뒤 오너는 남기지 않는다. 불투명 재그리기는 같은 깊이를 다시 쓴다.
                builder.SetRenderAttachmentDepth(depth, AccessFlags.ReadWrite);

                builder.SetRenderFunc(static (KeepPassData d, RasterGraphContext ctx) =>
                {
                    ctx.cmd.DrawRendererList(d.opaque);
                    ctx.cmd.DrawRendererList(d.transparent);
                });
            }

            TextureHandle cover = renderGraph.ImportTexture(_coverHandle);
            using (var builder = renderGraph.AddRasterRenderPass<CoverPassData>(MaskCoverPassName, out CoverPassData data))
            {
                data.material = _material;
                data.keep = keep;
                builder.UseTexture(keep, AccessFlags.Read);
                builder.SetRenderAttachment(cover, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (CoverPassData d, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(ctx.cmd, d.keep, FullScreenScaleBias, d.material, 1);
                });
            }
        }

        private static RendererListHandle CreateList(RenderGraph renderGraph, UniversalRenderingData renderingData,
            UniversalCameraData cameraData, UniversalLightData lightData, RenderQueueRange queue, SortingCriteria sorting)
        {
            DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(
                ShaderTags, renderingData, cameraData, lightData, sorting);
            // layerMask 는 전부 통과(-1), 선별은 렌더링 레이어 비트로.
            var filtering = new FilteringSettings(queue, -1, InterruptOwnerMaskLayers.Mask);
            return renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, drawingSettings, filtering));
        }
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
