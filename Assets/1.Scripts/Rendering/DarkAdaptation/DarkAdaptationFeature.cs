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
//  · 제압 간파 섬광(모든 피어, 할 일 179): 후처리 뒤 전체 화면 패스 1개(SuppressFlash.shader) — 비트 7 렌더러 실루엣 마스크 → 3단 색 합성.
//    재질·파라미터는 SuppressFlashState(디렉터 칸)에서 온다. 톤매핑 뒤라 3단 색이 그대로 나온다. HUD(오버레이 캔버스)는 그 위.
//  · 적중 섬광(오너, 할 일 132 §15): 방사형 블러 = 카메라 컬러(HDR, 후처리 전)를 적중 지점 중심으로 번지게 한다 — 뒤의 Bloom·색수차(Volume)가 그 위에 걸리고,
//    HUD(오버레이 캔버스)는 블러 밖이다. 플레어 층 = 켜진 LensFlareComponentSRP 를 투명 검정 RT 에 따로 그린다 — 오너 암전 덮개가 카메라 컬러를
//    덮으므로 섬광 오버레이 캔버스가 이 RT 를 덮개 위에 더한다(URP 자체 Data Driven Lens Flare 설정과 무관하게 돈다).
// 강도는 DarkAdaptationState 에서 읽는다 — 모드마다 0 이면 그 패스를 넣지 않는다(평소 비용 0).
public sealed class DarkAdaptationFeature : ScriptableRendererFeature
{
    public const string PassName = "Interrupt DarkAdaptation";
    public const string MaskKeepPassName = "Interrupt OwnerDim Keep";
    public const string MaskCoverPassName = "Interrupt OwnerDim Cover";
    public const string RadialBlurPassName = "Interrupt HitFlash RadialBlur";
    public const string FlareLayerPassName = "Interrupt HitFlash FlareLayer";
    public const string SuppressFlashMaskPassName = "Interrupt SuppressFlash Mask";
    public const string SuppressFlashPassName = "Interrupt SuppressFlash";

    [Tooltip("암순응 전체화면 셰이더(패스 0 휘도 · 패스 1 마스크 덮개 · 패스 2 방사형 블러). Shader.Find 는 빌드에서 빠지므로 직접 연결한다.")]
    [SerializeField] private Shader _shader;
    [Tooltip("적중 섬광 플레어 층 셰이더 = URP 패키지 Hidden/Universal Render Pipeline/LensFlareDataDriven. 비면 플레어 층 없음.")]
    [SerializeField] private Shader _lensFlareShader;

    private Material _material;
    private Material _lensFlareMaterial;
    private DarkAdaptationPass _pass;
    private OwnerMaskPass _maskPass;
    private RadialBlurPass _blurPass;
    private FlareLayerPass _flarePass;
    private SuppressFlashPass _suppressPass;

    public override void Create()
    {
        CoreUtils.Destroy(_material);
        CoreUtils.Destroy(_lensFlareMaterial);
        _material = _shader != null ? CoreUtils.CreateEngineMaterial(_shader) : null;
        _lensFlareMaterial = _lensFlareShader != null ? CoreUtils.CreateEngineMaterial(_lensFlareShader) : null;
        _blurPass = new RadialBlurPass
        {
            // 휘도·마스크 패스 뒤 같은 칸 — 블러한 HDR 위에 후처리(Bloom·색수차)가 걸린다.
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing
        };
        _flarePass?.Dispose();
        _flarePass = new FlareLayerPass
        {
            // 카메라 컬러와 무관(외부 RT) — 오버레이 UI 보다 먼저면 어디든 된다. 후처리 뒤에 둔다.
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing
        };
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
            DarkAdaptationState.MaskIntensity, DarkAdaptationState.MaskCoverTarget != null,
            DarkAdaptationState.HitFlashBlurStrength, DarkAdaptationState.HitFlashFlareTarget != null);
        // 제압 간파 섬광(할 일 179) — 재질은 디렉터 칸(SuppressFlashState)에서 온다. 이 기능의 셰이더 칸과 무관.
        bool suppressFlash = SuppressFlashState.ShouldRender;
        bool darkModes = mode != DarkAdaptationMode.None && _material != null && _pass != null && _maskPass != null;
        if (!darkModes && !suppressFlash)
            return;

        CameraData cameraData = renderingData.cameraData;
        Camera camera = cameraData.camera;

        // Scene View, 미리보기, 반사, 미니맵 RT와 Overlay 카메라는 제외한다.
        if (cameraData.cameraType != CameraType.Game ||
            cameraData.renderType == CameraRenderType.Overlay ||
            camera == null || camera.targetTexture != null)
            return;

        if (suppressFlash)
        {
            _suppressPass ??= new SuppressFlashPass { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
            _suppressPass.Setup(SuppressFlashState.Material, SuppressFlashState.Params);
            renderer.EnqueuePass(_suppressPass);
        }

        if (!darkModes)
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

        if ((mode & DarkAdaptationMode.RadialBlur) != 0 && _blurPass != null)
        {
            _blurPass.Setup(_material);
            renderer.EnqueuePass(_blurPass);
        }

        if ((mode & DarkAdaptationMode.FlareLayer) != 0 && _flarePass != null && _lensFlareMaterial != null)
        {
            _flarePass.Setup(_lensFlareMaterial, DarkAdaptationState.HitFlashFlareTarget);
            renderer.EnqueuePass(_flarePass);
        }
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(_material);
        _material = null;
        CoreUtils.Destroy(_lensFlareMaterial);
        _lensFlareMaterial = null;
        _maskPass?.Dispose();
        _maskPass = null;
        _flarePass?.Dispose();
        _flarePass = null;
    }

    // 제압 간파 섬광(할 일 179) — ① 비트 7 렌더러를 덮어쓰기 재질(패스 1)로 실루엣 마스크 RT 에 흰색 칠(깊이 없음)
    // ② 카메라 컬러(후처리 뒤) + 마스크 → 3단 색 합성(패스 0)으로 교체. 마스크는 그래프 전역 텍스처로 넘긴다.
    private sealed class SuppressFlashPass : ScriptableRenderPass
    {
        private static readonly Vector4 FullScreenScaleBias = new Vector4(1f, 1f, 0f, 0f);
        private static readonly int IdMask = Shader.PropertyToID("_SuppressFlashMask");
        private static readonly int IdCenter = Shader.PropertyToID("_SFCenter");
        private static readonly int IdTone = Shader.PropertyToID("_SFTone");
        private static readonly int IdFx = Shader.PropertyToID("_SFFx");
        private static readonly int IdCross = Shader.PropertyToID("_SFCross");
        private static readonly int IdShape = Shader.PropertyToID("_SFShape");
        private static readonly int IdLook = Shader.PropertyToID("_SFLook");
        private static readonly int IdThresholds = Shader.PropertyToID("_SFThresholds");
        private static readonly int IdDark = Shader.PropertyToID("_SFDark");
        private static readonly int IdMid = Shader.PropertyToID("_SFMid");
        private static readonly int IdLight = Shader.PropertyToID("_SFLight");

        private static readonly List<ShaderTagId> ShaderTags = new()
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit"),
        };

        private Material _material;
        private SuppressFlashParams _params;

        private sealed class MaskPassData
        {
            public RendererListHandle list;
        }

        private sealed class CompositePassData
        {
            public Material material;
            public TextureHandle source;
            public SuppressFlashParams parameters;
        }

        public SuppressFlashPass()
        {
            profilingSampler = new ProfilingSampler(SuppressFlashPassName);
            requiresIntermediateTexture = true;
        }

        public void Setup(Material material, SuppressFlashParams parameters)
        {
            _material = material;
            _params = parameters;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_material == null)
                return;

            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            if (resources.isActiveTargetBackBuffer)
                return;

            TextureHandle source = resources.activeColorTexture;
            if (!source.IsValid())
                return;

            TextureDesc maskDesc = renderGraph.GetTextureDesc(source);
            maskDesc.name = "_SuppressFlashMask";
            maskDesc.format = GraphicsFormat.R8_UNorm;
            maskDesc.msaaSamples = MSAASamples.None;
            maskDesc.clearBuffer = true;
            maskDesc.clearColor = Color.clear;
            TextureHandle mask = renderGraph.CreateTexture(maskDesc);

            using (var builder = renderGraph.AddRasterRenderPass<MaskPassData>(SuppressFlashMaskPassName, out MaskPassData data))
            {
                DrawingSettings drawing = RenderingUtils.CreateDrawingSettings(
                    ShaderTags, renderingData, cameraData, lightData, SortingCriteria.None);
                drawing.overrideMaterial = _material;
                drawing.overrideMaterialPassIndex = 1;
                var filtering = new FilteringSettings(RenderQueueRange.all, -1, SuppressFlashMaskLayers.Mask);
                data.list = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, drawing, filtering));

                builder.UseRendererList(data.list);
                builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                builder.SetGlobalTextureAfterPass(mask, IdMask);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (MaskPassData d, RasterGraphContext ctx) => ctx.cmd.DrawRendererList(d.list));
            }

            TextureDesc descriptor = renderGraph.GetTextureDesc(source);
            descriptor.name = "_CameraColorSuppressFlash";
            descriptor.clearBuffer = false;
            TextureHandle destination = renderGraph.CreateTexture(descriptor);

            using (var builder = renderGraph.AddRasterRenderPass<CompositePassData>(SuppressFlashPassName, out CompositePassData data, profilingSampler))
            {
                data.material = _material;
                data.source = source;
                data.parameters = _params;

                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(mask, AccessFlags.Read);
                builder.UseGlobalTexture(IdMask);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (CompositePassData d, RasterGraphContext ctx) =>
                {
                    SuppressFlashParams p = d.parameters;
                    d.material.SetVector(IdCenter, p.Center);
                    d.material.SetVector(IdTone, p.Tone);
                    d.material.SetVector(IdFx, p.Fx);
                    d.material.SetVector(IdCross, p.Cross);
                    d.material.SetVector(IdShape, p.Shape);
                    d.material.SetVector(IdLook, p.Look);
                    d.material.SetVector(IdThresholds, p.Thresholds);
                    d.material.SetColor(IdDark, p.Dark);
                    d.material.SetColor(IdMid, p.Mid);
                    d.material.SetColor(IdLight, p.Light);
                    Blitter.BlitTexture(ctx.cmd, d.source, FullScreenScaleBias, d.material, 0);
                });
            }

            resources.cameraColor = destination;
        }
    }

    // 적중 섬광 방사형 블러 — 카메라 컬러를 적중 지점 쪽으로 여러 번 겹쳐 읽는다(패스 2). 휘도 패스와 같은 교체 방식.
    private sealed class RadialBlurPass : ScriptableRenderPass
    {
        private static readonly Vector4 FullScreenScaleBias = new Vector4(1f, 1f, 0f, 0f);
        private static readonly int IdBlur = Shader.PropertyToID("_HitFlashBlur");

        private Material _material;

        private sealed class PassData
        {
            public Material material;
            public TextureHandle source;
            public Vector4 blur;
        }

        public RadialBlurPass()
        {
            profilingSampler = new ProfilingSampler(RadialBlurPassName);
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
                return;

            TextureHandle source = resources.activeColorTexture;
            if (!source.IsValid())
                return;

            TextureDesc descriptor = renderGraph.GetTextureDesc(source);
            descriptor.name = "_CameraColorHitFlashBlur";
            descriptor.clearBuffer = false;
            TextureHandle destination = renderGraph.CreateTexture(descriptor);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(RadialBlurPassName, out PassData data, profilingSampler))
            {
                data.material = _material;
                data.source = source;
                // xy=중심(뷰포트), z=세기(중심까지 거리 중 샘플이 덮는 비율)
                data.blur = new Vector4(DarkAdaptationState.HitFlashBlurCenter.x, DarkAdaptationState.HitFlashBlurCenter.y,
                    Mathf.Clamp01(DarkAdaptationState.HitFlashBlurStrength), 0f);

                builder.UseTexture(source, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (PassData d, RasterGraphContext context) =>
                {
                    d.material.SetVector(IdBlur, d.blur);
                    Blitter.BlitTexture(context.cmd, d.source, FullScreenScaleBias, d.material, 2);
                });
            }

            resources.cameraColor = destination;
        }
    }

    // 적중 섬광 플레어 층 — 켜진 LensFlareComponentSRP 들을 외부 RT(투명 검정)에 그린다. 그래프 밖(UI)이 읽으니 패스 컬링을 끈다.
    // 그리기는 URP 후처리와 같은 SRP core 함수(LensFlareCommonSRP.DoLensFlareDataDrivenCommon) — 플레어 모양·색은 LensFlareDataSRP 에셋 그대로.
    private sealed class FlareLayerPass : ScriptableRenderPass
    {
        private Material _material;
        private RenderTexture _target;
        private RenderTexture _wrappedTarget;
        private RTHandle _handle;

        private sealed class PassData
        {
            public Material material;
            public UniversalCameraData cameraData;
            public TextureHandle target;
            public float width;
            public float height;
        }

        public FlareLayerPass()
        {
            profilingSampler = new ProfilingSampler(FlareLayerPassName);
        }

        public void Setup(Material material, RenderTexture target)
        {
            _material = material;
            _target = target;
        }

        public void Dispose()
        {
            _handle?.Release();
            _handle = null;
            _wrappedTarget = null;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_material == null || _target == null)
                return;

            if (_wrappedTarget != _target || _handle == null)
            {
                _handle?.Release();
                _handle = RTHandles.Alloc(_target);
                _wrappedTarget = _target;
            }

            TextureHandle target = renderGraph.ImportTexture(_handle);
            using (var builder = renderGraph.AddUnsafePass<PassData>(FlareLayerPassName, out PassData data, profilingSampler))
            {
                data.material = _material;
                data.cameraData = frameData.Get<UniversalCameraData>();
                data.target = target;
                data.width = _target.width;
                data.height = _target.height;
                // DoLensFlareDataDrivenCommon 이 안에서 SetRenderTarget 을 부른다(URP 와 같은 Write 선언).
                builder.UseTexture(target, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData d, UnsafeGraphContext ctx) =>
                {
                    ctx.cmd.SetRenderTarget(d.target);
                    ctx.cmd.ClearRenderTarget(false, true, Color.clear);

                    Camera camera = d.cameraData.camera;
                    Matrix4x4 viewProjection = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix;
                    LensFlareCommonSRP.DoLensFlareDataDrivenCommon(
                        d.material, camera, new Rect(0f, 0f, d.width, d.height), d.cameraData.xr, d.cameraData.xr.multipassId,
                        d.width, d.height,
                        false, 1f, 1f,
                        true,
                        camera.transform.position,
                        viewProjection,
                        ctx.cmd,
                        false, false, null, null,
                        d.target,
                        // 섬광 플레어는 Light 없이 컴포넌트 세기만 쓴다.
                        static (Light light, Camera cam, Vector3 wo) => 1f,
                        false);
                });
            }
        }
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
