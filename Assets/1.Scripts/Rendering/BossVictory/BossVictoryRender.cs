using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 보스 B 승리 연출 렌더링(PLAN-boss-victory-cinematic §5.3·C9) — 인물·보스 마스크(+스텐실) → 전체 화면 합성.
///
/// 🔴 렌더러 에셋(PC_Renderer)에 기능을 등록하지 않는다. 연출 동안에만 <see cref="RenderPipelineManager.beginCameraRendering"/> 에서
///    게임 카메라에 패스를 넣는다 → 평소 비용 0, 에셋 무수정.
/// 주입 시점 = 투명 렌더 직전, 안개(BeforeRenderingTransparents) **다음** → 인물 명도에 반투명이 섞이지 않고,
/// 핵심 타격 VFX·타이포(투명 패스)는 합성 **위**에 원색으로 그려진다(레이어: 배경 → 타이포 → 인물·보스 → 핵심 이펙트).
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

    /// <summary>인물 픽셀 스텐실 비트 — 마스크 셰이더가 쓰고 타이포 TMP 재질이 NotEqual 로 읽는다.</summary>
    public const int StencilBit = 128;

    public const string MaskShaderName = "Hidden/Rendering/BossVictoryMask";
    public const string CompositeShaderName = "Hidden/Rendering/BossVictoryComposite";

    static readonly int IdVictoryId = Shader.PropertyToID("_VictoryId");
    static readonly int IdIdTex = Shader.PropertyToID("_VictoryIdTex");
    static readonly int IdWeight = Shader.PropertyToID("_Weight");
    static readonly int IdBgTint = Shader.PropertyToID("_BgTint");
    static readonly int IdGlitch = Shader.PropertyToID("_Glitch");
    static readonly int IdGlitchTime = Shader.PropertyToID("_GlitchTime");
    static readonly int IdGlitchShift = Shader.PropertyToID("_GlitchShift");
    static readonly int IdGlitchStreak = Shader.PropertyToID("_GlitchStreak");
    static readonly int IdGlitchWash = Shader.PropertyToID("_GlitchWash");
    static readonly int IdExposure = Shader.PropertyToID("_Exposure");
    static readonly int IdGamma = Shader.PropertyToID("_Gamma");
    static readonly int IdBgTop = Shader.PropertyToID("_BgTop");
    static readonly int IdBgBottom = Shader.PropertyToID("_BgBottom");
    static readonly int IdKiller = Shader.PropertyToID("_KillerColor");
    static readonly int IdTeammate = Shader.PropertyToID("_TeammateColor");
    static readonly int IdBoss = Shader.PropertyToID("_BossColor");
    static readonly int IdShadow = Shader.PropertyToID("_ShadowColor");

    static Material _maskKiller, _maskTeammate, _maskBoss, _composite;
    static VictoryPass _pass;
    static bool _hooked;

    /// <summary>켜져 있는가(마스크·스텐실이 그려진다).</summary>
    public static bool Active => _hooked;

    /// <summary>
    /// 켠다(멱등). 셰이더가 없으면 false — 그땐 색 전환 없이 카메라·타이포만 돈다(호출측이 진단을 남긴다).
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
        SetGlitch(0f, 0f);

        _pass = new VictoryPass(_maskKiller, _maskTeammate, _maskBoss, _composite)
        {
            // 안개(BeforeRenderingTransparents)와 같은 이벤트에 +1 — 안개 다음에 돈다.
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents + 1,
        };
        _pass.ConfigureInput(ScriptableRenderPassInput.Depth);

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
        _pass = null;
        CoreUtils.Destroy(_maskKiller);
        CoreUtils.Destroy(_maskTeammate);
        CoreUtils.Destroy(_maskBoss);
        CoreUtils.Destroy(_composite);
        _maskKiller = _maskTeammate = _maskBoss = _composite = null;
    }

    /// <summary>색 전환 강도(0 = 원래 색). 색·글리치가 둘 다 0 이면 합성 블릿 자체를 건너뛴다.</summary>
    public static void SetWeight(float weight)
    {
        if (_composite != null) _composite.SetFloat(IdWeight, Mathf.Clamp01(weight));
        if (_pass != null) _pass.Weight = Mathf.Clamp01(weight);
    }

    /// <summary>글리치(지직거림) 세기 0~1. <paramref name="time"/> 은 패턴 시드(초) — 24fps 로 끊어 바뀐다.</summary>
    public static void SetGlitch(float glitch, float time)
    {
        if (_composite != null)
        {
            _composite.SetFloat(IdGlitch, Mathf.Clamp01(glitch));
            _composite.SetFloat(IdGlitchTime, time);
        }
        if (_pass != null) _pass.Glitch = Mathf.Clamp01(glitch);
    }

    public static void ApplyColors(BossVictoryConfig cfg)
    {
        if (_composite == null || cfg == null) return;
        _composite.SetFloat(IdBgTint, Mathf.Clamp01(cfg.backgroundTint));
        _composite.SetFloat(IdGlitchShift, cfg.glitchShift);
        _composite.SetFloat(IdGlitchStreak, cfg.glitchStreak);
        _composite.SetFloat(IdGlitchWash, cfg.glitchWash);
        _composite.SetFloat(IdExposure, Mathf.Max(0.01f, cfg.characterExposure));
        _composite.SetFloat(IdGamma, Mathf.Max(0.01f, cfg.characterGamma));
        // 기획 HEX 는 sRGB 최종색 — 합성은 linear 공간이라 바꿔 넣는다.
        _composite.SetColor(IdBgTop, cfg.backgroundTop.linear);
        _composite.SetColor(IdBgBottom, cfg.backgroundBottom.linear);
        _composite.SetColor(IdKiller, cfg.killerColor.linear);
        _composite.SetColor(IdTeammate, cfg.teammateColor.linear);
        _composite.SetColor(IdBoss, cfg.bossColor.linear);
        _composite.SetColor(IdShadow, cfg.shadowColor.linear);
    }

    /// <summary>렌더러에 비트를 더한다(OR). 돌려받은 목록으로 <see cref="Untag"/> 한다.</summary>
    public static void Tag(GameObject root, uint bit, List<Renderer> tagged)
    {
        if (root == null) return;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;   // 몸만 — 이펙트는 마스크에 넣지 않는다(C9)
            if (r.GetComponentInParent<ExcludeFromSilhouette>(true) != null) continue;                         // 에임 쿼드 등 표시용
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
        if (_pass == null || cam == null || cam.cameraType != CameraType.Game) return;
        UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
        if (data == null || data.renderType != CameraRenderType.Base || data.scriptableRenderer == null) return;
        data.scriptableRenderer.EnqueuePass(_pass);
    }

    // ------------------------------------------------------------------------
    sealed class VictoryPass : ScriptableRenderPass
    {
        static readonly Vector4 FullScreenScaleBias = new Vector4(1f, 1f, 0f, 0f);

        // 원본 셰이더의 패스 태그로 대상 렌더러를 고른다(실루엣과 같은 셋 — Flat Kit 본체는 UniversalForwardOnly).
        static readonly List<ShaderTagId> ShaderTags = new()
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit"),
        };

        readonly Material _maskKiller, _maskTeammate, _maskBoss, _composite;
        public float Weight;
        public float Glitch;

        class MaskData
        {
            public RendererListHandle killer, teammate, boss;
        }

        class CompositeData
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle idTex;
        }

        public VictoryPass(Material killer, Material teammate, Material boss, Material composite)
        {
            _maskKiller = killer;
            _maskTeammate = teammate;
            _maskBoss = boss;
            _composite = composite;
            profilingSampler = new ProfilingSampler("BossVictory");
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

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

            // 1) 마스크 — 깊이 텍스처와 선형 거리 비교(보이는 표면만, 여유 8cm — 하드웨어 LEqual 은 이음새가 점선으로 빠졌다) · 스텐실 128. 타이포가 이 스텐실을 읽으므로 강도 0 이어도 그린다.
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
                builder.SetRenderAttachmentDepth(depth, AccessFlags.ReadWrite);   // 스텐실을 카메라 깊이·스텐실에 남긴다
                builder.SetRenderFunc(static (MaskData d, RasterGraphContext ctx) =>
                {
                    // 보스 → 팀원 → 막타 순 — 겹치면 같은 깊이 판정이라 앞에 있는 쪽만 남는다.
                    ctx.cmd.DrawRendererList(d.boss);
                    ctx.cmd.DrawRendererList(d.teammate);
                    ctx.cmd.DrawRendererList(d.killer);
                });
            }

            if (Weight <= 0.0001f && Glitch <= 0.0001f) return;   // 색·글리치 둘 다 없음 — 합성 블릿 생략

            // 2) 합성 — source → dest, 카메라 컬러를 dest 로 넘긴다(안개와 같은 방식).
            RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            TextureHandle dest = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_BossVictoryColor", false, FilterMode.Bilinear);

            using (var builder = renderGraph.AddRasterRenderPass<CompositeData>("BossVictoryComposite", out CompositeData data))
            {
                data.material = _composite;
                data.source = color;
                data.idTex = idTex;
                builder.UseTexture(color, AccessFlags.Read);
                builder.UseTexture(idTex, AccessFlags.Read);
                builder.SetRenderAttachment(dest, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (CompositeData d, RasterGraphContext ctx) =>
                {
                    d.material.SetTexture(IdIdTex, d.idTex);
                    Blitter.BlitTexture(ctx.cmd, d.source, FullScreenScaleBias, d.material, 0);
                });
            }

            resourceData.cameraColor = dest;
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
}
