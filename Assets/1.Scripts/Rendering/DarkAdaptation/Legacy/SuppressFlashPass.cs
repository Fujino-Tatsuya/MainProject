using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// [Legacy — 11-04 할 일 211] 제압 간파 섬광 시퀀스 렌더 패스. 원래 DarkAdaptationFeature 안 중첩 클래스였는데 런타임에서 빼며 여기로 옮겼다.
// 어떤 렌더 기능도 이 패스를 넣지 않는다(연결 끊김). 다시 쓰려면 DarkAdaptationFeature.AddRenderPasses 에서 SuppressFlashState.ShouldRender 일 때
// AfterRenderingPostProcessing 으로 EnqueuePass 하면 된다(179 원본 = 브랜치 이전 커밋).
// 제압 간파 섬광(할 일 179) — ① 비트 7 렌더러를 덮어쓰기 재질(패스 1)로 실루엣 마스크 RT 에 흰색 칠(깊이 없음)
// ② 카메라 컬러(후처리 뒤) + 마스크 → 3단 색 합성(패스 0)으로 교체. 마스크는 그래프 전역 텍스처로 넘긴다.
//    A7 대상 내부만(179/192 후속)이면 같은 마스크(간파 대상만 태깅)를 '그릴 범위'로 쓴다 — 패스는 그대로, _SFMask.x 만 다르다.
internal sealed class SuppressFlashPass : ScriptableRenderPass
{
    public const string MaskPassName = "Interrupt SuppressFlash Mask";
    public const string PassName = "Interrupt SuppressFlash";

    private static readonly Vector4 FullScreenScaleBias = new Vector4(1f, 1f, 0f, 0f);
    private static readonly int IdMask = Shader.PropertyToID("_SuppressFlashMask");
    private static readonly int IdCenter = Shader.PropertyToID("_SFCenter");
    private static readonly int IdTone = Shader.PropertyToID("_SFTone");
    private static readonly int IdFx = Shader.PropertyToID("_SFFx");
    private static readonly int IdCross = Shader.PropertyToID("_SFCross");
    private static readonly int IdShape = Shader.PropertyToID("_SFShape");
    private static readonly int IdLook = Shader.PropertyToID("_SFLook");
    private static readonly int IdThresholds = Shader.PropertyToID("_SFThresholds");
    private static readonly int IdMaskMode = Shader.PropertyToID("_SFMask");
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
        profilingSampler = new ProfilingSampler(PassName);
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

        using (var builder = renderGraph.AddRasterRenderPass<MaskPassData>(MaskPassName, out MaskPassData data))
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

        using (var builder = renderGraph.AddRasterRenderPass<CompositePassData>(PassName, out CompositePassData data, profilingSampler))
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
                d.material.SetVector(IdMaskMode, p.Mask);
                d.material.SetColor(IdDark, p.Dark);
                d.material.SetColor(IdMid, p.Mid);
                d.material.SetColor(IdLight, p.Light);
                Blitter.BlitTexture(ctx.cmd, d.source, FullScreenScaleBias, d.material, 0);
            });
        }

        resources.cameraColor = destination;
    }
}
