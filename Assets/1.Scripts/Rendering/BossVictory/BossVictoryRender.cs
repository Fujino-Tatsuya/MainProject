using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 보스 B 승리 연출 렌더링(PLAN-boss-victory-cinematic §5.3·C9) — 인물·보스 마스크 + 반투명 전 색 복사 → 반투명 뒤 전체 화면 합성.
///
/// 🔴 렌더러 에셋(PC_Renderer)에 기능을 등록하지 않는다. 연출 동안에만 <see cref="RenderPipelineManager.beginCameraRendering"/> 에서
///    게임 카메라에 패스 2개를 넣는다 → 평소 비용 0, 에셋 무수정.
/// <list type="number">
/// <item>준비(투명 렌더 직전, 안개 다음): 마스크(ID 텍스처) + 반투명 전 씬색 복사.</item>
/// <item>합성(투명 렌더 직후): 인물·보스(무기 포함) 단색 · <b>반투명이 바꾼 픽셀(이펙트) 단색</b> · 배경 단색 틴트(할 일 200 D1·D2).</item>
/// </list>
/// 색 강도가 0 이면 두 패스 다 건너뛴다(단계 0.72 전엔 비용 0). 두 패스 사이 텍스처는 프레임 데이터(<see cref="VictoryFrameData"/>)로 넘긴다.
/// </summary>
public static class BossVictoryRender
{
    // 렌더링 레이어 비트 — 0 조명 · 1 데칼 · 2·3 실루엣(PlayerSilhouetteLayers) 다음. 추가만 하고 지우지 않는다(대입 금지 — bit 0 을 잃으면 캐릭터가 까매진다).
    public const int KillerLayerIndex = 4;
    public const int TeammateLayerIndex = 5;
    public const int BossLayerIndex = 6;
    public const uint KillerMask = 1u << KillerLayerIndex;
    public const uint TeammateMask = 1u << TeammateLayerIndex;
    public const uint BossMask = 1u << BossLayerIndex;
    public const uint AllMask = KillerMask | TeammateMask | BossMask;

    public const string MaskShaderName = "Hidden/Rendering/BossVictoryMask";
    public const string CompositeShaderName = "Hidden/Rendering/BossVictoryComposite";

    const int CompositePassIndex = 0;
    const int CopyPassIndex = 1;

    static readonly int IdVictoryId = Shader.PropertyToID("_VictoryId");
    static readonly int IdIdTex = Shader.PropertyToID("_VictoryIdTex");
    static readonly int IdPreTex = Shader.PropertyToID("_VictoryPreTex");
    static readonly int IdWeight = Shader.PropertyToID("_Weight");
    static readonly int IdBgTint = Shader.PropertyToID("_BgTint");
    static readonly int IdExposure = Shader.PropertyToID("_Exposure");
    static readonly int IdGamma = Shader.PropertyToID("_Gamma");
    static readonly int IdEffectGain = Shader.PropertyToID("_EffectGain");
    static readonly int IdBgColor = Shader.PropertyToID("_BgColor");
    static readonly int IdKiller = Shader.PropertyToID("_KillerColor");
    static readonly int IdTeammate = Shader.PropertyToID("_TeammateColor");
    static readonly int IdBoss = Shader.PropertyToID("_BossColor");
    static readonly int IdEffect = Shader.PropertyToID("_EffectColor");
    static readonly int IdShadow = Shader.PropertyToID("_ShadowColor");
    static readonly int IdCircle = Shader.PropertyToID("_GroundCircle");          // xyz 중심(월드), w 반경(m)
    static readonly int IdCircleLook = Shader.PropertyToID("_GroundCircleLook");  // x 가장자리 부드러움(m), y 흑백 밝기

    static Material _maskKiller, _maskTeammate, _maskBoss, _composite;
    static PreparePass _prepare;
    static CompositePass _compositePass;
    static float _weight;
    static bool _hooked;

    /// <summary>켜져 있는가.</summary>
    public static bool Active => _hooked;

    /// <summary>
    /// 켠다(멱등). 셰이더가 없으면 false — 그땐 색 전환 없이 카메라만 돈다(호출측이 진단을 남긴다).
    /// </summary>
    public static bool Enable(BossVictoryConfig cfg)
    {
        if (_hooked) return true;

        Shader mask = cfg != null && cfg.maskShader != null ? cfg.maskShader : Shader.Find(MaskShaderName);
        Shader comp = cfg != null && cfg.compositeShader != null ? cfg.compositeShader : Shader.Find(CompositeShaderName);
        if (mask == null || comp == null) return false;

        _maskKiller = NewMask(mask, new Color(1f, 0f, 0f, 1f));
        _maskTeammate = NewMask(mask, new Color(0f, 1f, 0f, 1f));
        _maskBoss = NewMask(mask, new Color(0f, 0f, 1f, 1f));
        _composite = CoreUtils.CreateEngineMaterial(comp);
        ApplyColors(cfg);
        SetWeight(0f);
        SetGroundCircle(Vector3.zero, 0f, 0f, 1f);

        _prepare = new PreparePass(_maskKiller, _maskTeammate, _maskBoss, _composite)
        {
            // 안개(BeforeRenderingTransparents)와 같은 이벤트에 +1 — 안개 다음, 반투명 전.
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents + 1,
        };
        _prepare.ConfigureInput(ScriptableRenderPassInput.Depth);
        _compositePass = new CompositePass(_composite)
        {
            // 반투명(이펙트·무기 궤적) 뒤 — 이펙트까지 단색으로(할 일 200 D2). 후처리(블룸 등)는 그 뒤.
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents,
        };
        _compositePass.ConfigureInput(ScriptableRenderPassInput.Depth); // 발밑 회색 원(246) — 픽셀 월드 위치

        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        _hooked = true;
        return true;
    }

    /// <summary>끈다(멱등) — 패스·재질을 지운다.</summary>
    public static void Disable()
    {
        if (!_hooked) return;
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        _hooked = false;
        _prepare = null;
        _compositePass = null;
        CoreUtils.Destroy(_maskKiller);
        CoreUtils.Destroy(_maskTeammate);
        CoreUtils.Destroy(_maskBoss);
        CoreUtils.Destroy(_composite);
        _maskKiller = _maskTeammate = _maskBoss = _composite = null;
    }

    /// <summary>색 전환 강도(0 = 원래 색). 0 이면 두 패스 다 건너뛴다.</summary>
    public static void SetWeight(float weight)
    {
        _weight = Mathf.Clamp01(weight);
        if (_composite != null) _composite.SetFloat(IdWeight, _weight);
    }

    /// <summary>
    /// 발밑 회색 원(할 일 246) — 중심에서 <b>수평</b> 거리 <paramref name="radius"/> 안의 배경(바닥·벽·소품 — 높이 한도 없음, 원기둥)을
    /// 주황 틴트 대신 흑백(원래 명도 × <paramref name="brightness"/>)으로. 반경 0 = 끔. 표시가 매 프레임 부른다.
    /// </summary>
    public static void SetGroundCircle(Vector3 center, float radius, float softness, float brightness)
    {
        if (_composite == null) return;
        _composite.SetVector(IdCircle, new Vector4(center.x, center.y, center.z, Mathf.Max(0f, radius)));
        _composite.SetVector(IdCircleLook, new Vector4(Mathf.Max(0f, softness), Mathf.Max(0f, brightness), 0f, 0f));
    }

    public static void ApplyColors(BossVictoryConfig cfg)
    {
        if (_composite == null || cfg == null) return;
        _composite.SetFloat(IdBgTint, Mathf.Clamp01(cfg.backgroundTint));
        _composite.SetFloat(IdExposure, Mathf.Max(0.01f, cfg.characterExposure));
        _composite.SetFloat(IdGamma, Mathf.Max(0.01f, cfg.characterGamma));
        _composite.SetFloat(IdEffectGain, Mathf.Max(0f, cfg.effectDetectGain));
        // 기획 HEX 는 sRGB 최종색 — 합성은 linear 공간이라 바꿔 넣는다.
        _composite.SetColor(IdBgColor, cfg.backgroundColor.linear);
        _composite.SetColor(IdKiller, cfg.killerColor.linear);
        _composite.SetColor(IdTeammate, cfg.teammateColor.linear);
        _composite.SetColor(IdBoss, cfg.bossColor.linear);
        _composite.SetColor(IdEffect, cfg.effectColor.linear);
        _composite.SetColor(IdShadow, cfg.shadowColor.linear);
    }

    /// <summary>렌더러에 비트를 더한다(OR). 돌려받은 목록으로 <see cref="Untag"/> 한다.</summary>
    public static void Tag(GameObject root, uint bit, List<Renderer> tagged)
    {
        if (root == null) return;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            // 몸·무기만 — 이펙트는 합성이 "반투명이 바꾼 픽셀"로 따로 잡는다.
            if (r == null || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (r.GetComponentInParent<ExcludeFromSilhouette>(true) != null) continue;   // 에임 쿼드 등 표시용
            if ((r.renderingLayerMask & bit) != 0) continue;
            r.renderingLayerMask |= bit;
            tagged.Add(r);
        }
    }

    public static void Untag(List<Renderer> tagged)
    {
        for (int i = 0; i < tagged.Count; i++)
            if (tagged[i] != null) tagged[i].renderingLayerMask &= ~AllMask;
        tagged.Clear();
    }

    static Material NewMask(Shader shader, Color id)
    {
        Material m = CoreUtils.CreateEngineMaterial(shader);
        m.SetColor(IdVictoryId, id);
        return m;
    }

    static void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
    {
        if (_prepare == null || _weight <= 0.0001f || cam == null || cam.cameraType != CameraType.Game) return;
        UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
        if (data == null || data.renderType != CameraRenderType.Base || data.scriptableRenderer == null) return;
        data.scriptableRenderer.EnqueuePass(_prepare);
        data.scriptableRenderer.EnqueuePass(_compositePass);
    }

    static RenderTextureDescriptor ColorDesc(UniversalCameraData cameraData)
    {
        RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
        desc.depthBufferBits = 0;
        desc.msaaSamples = 1;
        return desc;
    }

    static readonly Vector4 FullScreenScaleBias = new Vector4(1f, 1f, 0f, 0f);

    // ------------------------------------------------------------------------
    /// <summary>준비 패스 → 합성 패스로 넘기는 이번 프레임 텍스처. 프레임 번호로 다른 프레임 값을 거른다.</summary>
    sealed class VictoryFrameData : ContextItem
    {
        public TextureHandle idTex;
        public TextureHandle preTex;
        public int frame = -1;

        public override void Reset()
        {
            idTex = TextureHandle.nullHandle;
            preTex = TextureHandle.nullHandle;
            frame = -1;
        }
    }

    class BlitData
    {
        public Material material;
        public TextureHandle source;
        public TextureHandle idTex;
        public TextureHandle preTex;
    }

    // 1) 마스크 + 반투명 전 색 복사.
    sealed class PreparePass : ScriptableRenderPass
    {
        // 원본 셰이더의 패스 태그로 대상 렌더러를 고른다(실루엣과 같은 셋 — Flat Kit 본체는 UniversalForwardOnly).
        static readonly List<ShaderTagId> ShaderTags = new()
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit"),
        };

        readonly Material _maskKiller, _maskTeammate, _maskBoss, _composite;

        class MaskData
        {
            public RendererListHandle killer, teammate, boss;
        }

        public PreparePass(Material killer, Material teammate, Material boss, Material composite)
        {
            _maskKiller = killer;
            _maskTeammate = teammate;
            _maskBoss = boss;
            _composite = composite;
            profilingSampler = new ProfilingSampler("BossVictoryPrepare");
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            VictoryFrameData shared = frameData.GetOrCreate<VictoryFrameData>();
            shared.frame = -1;

            if (resourceData.isActiveTargetBackBuffer) return;
            TextureHandle color = resourceData.activeColorTexture;
            TextureHandle depth = resourceData.activeDepthTexture;
            if (!color.IsValid() || !depth.IsValid()) return;

            RenderTextureDescriptor idDesc = cameraData.cameraTargetDescriptor;
            idDesc.msaaSamples = 1;
            idDesc.depthBufferBits = 0;
            idDesc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm;
            // 매 프레임 지운다(일부 픽셀에만 그리는 마스크 — 안 지우면 잔상).
            TextureHandle idTex = UniversalRenderer.CreateRenderGraphTexture(renderGraph, idDesc, "_BossVictoryId", true, FilterMode.Point);

            // 마스크 — 깊이 텍스처와 선형 거리 비교(보이는 표면만, 여유 8cm — 하드웨어 LEqual 은 이음새가 점선으로 빠졌다).
            using (var builder = renderGraph.AddRasterRenderPass<MaskData>("BossVictoryMask", out MaskData data))
            {
                data.killer = List(renderGraph, renderingData, cameraData, lightData, _maskKiller, KillerMask);
                data.teammate = List(renderGraph, renderingData, cameraData, lightData, _maskTeammate, TeammateMask);
                data.boss = List(renderGraph, renderingData, cameraData, lightData, _maskBoss, BossMask);
                builder.UseRendererList(data.killer);
                builder.UseRendererList(data.teammate);
                builder.UseRendererList(data.boss);
                // 마스크 셰이더가 _CameraDepthTexture 로 "보이는 표면"을 고른다 — 읽기 선언이 있어야 깊이 복사 뒤에 놓인다.
                if (resourceData.cameraDepthTexture.IsValid())
                    builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                builder.UseAllGlobalTextures(true);
                builder.SetRenderAttachment(idTex, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(depth, AccessFlags.ReadWrite);   // 마스크 셰이더의 스텐실 기록(옛 타이포용 — 지금 읽는 곳 없음)
                builder.SetRenderFunc(static (MaskData d, RasterGraphContext ctx) =>
                {
                    // 보스 → 팀원 → 막타 순 — 겹치면 같은 깊이 판정이라 앞에 있는 쪽만 남는다.
                    ctx.cmd.DrawRendererList(d.boss);
                    ctx.cmd.DrawRendererList(d.teammate);
                    ctx.cmd.DrawRendererList(d.killer);
                });
            }

            // 반투명 전 씬색 복사 — 합성이 "반투명(이펙트)이 바꾼 픽셀"을 이것과의 차이로 찾는다.
            TextureHandle preTex = UniversalRenderer.CreateRenderGraphTexture(renderGraph, ColorDesc(cameraData), "_BossVictoryPre", false, FilterMode.Point);
            using (var builder = renderGraph.AddRasterRenderPass<BlitData>("BossVictoryCopyPre", out BlitData data))
            {
                data.material = _composite;
                data.source = color;
                builder.UseTexture(color, AccessFlags.Read);
                builder.SetRenderAttachment(preTex, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (BlitData d, RasterGraphContext ctx) =>
                    Blitter.BlitTexture(ctx.cmd, d.source, FullScreenScaleBias, d.material, CopyPassIndex));
            }

            shared.idTex = idTex;
            shared.preTex = preTex;
            shared.frame = Time.frameCount;
        }

        static RendererListHandle List(RenderGraph rg, UniversalRenderingData renderingData, UniversalCameraData cameraData,
            UniversalLightData lightData, Material overrideMaterial, uint layerMask)
        {
            DrawingSettings drawing = RenderingUtils.CreateDrawingSettings(ShaderTags, renderingData, cameraData, lightData, SortingCriteria.CommonOpaque);
            drawing.overrideMaterial = overrideMaterial;
            drawing.overrideMaterialPassIndex = 0;
            var filtering = new FilteringSettings(RenderQueueRange.opaque, -1, layerMask);
            return rg.CreateRendererList(new RendererListParams(renderingData.cullResults, drawing, filtering));
        }
    }

    // 2) 합성 — source → dest, 카메라 컬러를 dest 로 넘긴다(안개와 같은 방식).
    sealed class CompositePass : ScriptableRenderPass
    {
        readonly Material _composite;

        public CompositePass(Material composite)
        {
            _composite = composite;
            profilingSampler = new ProfilingSampler("BossVictoryComposite");
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (!frameData.Contains<VictoryFrameData>()) return;
            VictoryFrameData shared = frameData.Get<VictoryFrameData>();
            if (shared.frame != Time.frameCount || !shared.idTex.IsValid() || !shared.preTex.IsValid()) return;
            shared.frame = -1;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            if (resourceData.isActiveTargetBackBuffer) return;
            TextureHandle color = resourceData.activeColorTexture;
            if (!color.IsValid()) return;

            TextureHandle dest = UniversalRenderer.CreateRenderGraphTexture(renderGraph, ColorDesc(cameraData), "_BossVictoryColor", false, FilterMode.Bilinear);
            using (var builder = renderGraph.AddRasterRenderPass<BlitData>("BossVictoryComposite", out BlitData data))
            {
                data.material = _composite;
                data.source = color;
                data.idTex = shared.idTex;
                data.preTex = shared.preTex;
                builder.UseTexture(color, AccessFlags.Read);
                builder.UseTexture(shared.idTex, AccessFlags.Read);
                builder.UseTexture(shared.preTex, AccessFlags.Read);
                // 발밑 회색 원(246) — 셰이더가 _CameraDepthTexture 로 픽셀 월드 위치를 되살린다.
                if (resourceData.cameraDepthTexture.IsValid())
                    builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                builder.UseAllGlobalTextures(true);
                builder.SetRenderAttachment(dest, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (BlitData d, RasterGraphContext ctx) =>
                {
                    d.material.SetTexture(IdIdTex, d.idTex);
                    d.material.SetTexture(IdPreTex, d.preTex);
                    Blitter.BlitTexture(ctx.cmd, d.source, FullScreenScaleBias, d.material, CompositePassIndex);
                });
            }

            resourceData.cameraColor = dest;
        }
    }
}
