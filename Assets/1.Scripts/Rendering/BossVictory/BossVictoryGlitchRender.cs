using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 보스 처치 연출 — ZZZ 글리치(PLAN-boss-victory-glitch 3단계) 렌더링. 플레이어·보스 마스크 → 반투명 뒤 전체 화면 글리치 합성.
///
/// 🔴 렌더러 에셋(PC_Renderer)에 기능을 등록하지 않는다. 연출 동안에만 <see cref="RenderPipelineManager.beginCameraRendering"/> 에서
///    게임 카메라에 패스 2개를 넣는다 → 평소 비용 0, 에셋 무수정(옛 BossVictoryRender 와 같은 방식).
/// <list type="number">
/// <item>마스크(투명 렌더 직전): 플레이어(흑백 대상)·보스(노이즈 대상) 렌더러를 렌더링 레이어 비트로 골라 ID 텍스처(R 플레이어 · B 보스).</item>
/// <item>합성(투명 렌더 직후, 후처리 전): 흑백·호박색·주사선·물결·계단·늘어남·블록·스미어·슬라이스·RGB 분리·섬광 + 보스 노이즈 UV.</item>
/// </list>
/// 세기는 표시(<see cref="BossVictoryGlitchPresentation"/>)가 설정 SO 타임라인 곡선으로 매 프레임 넣는다. 마스터 세기 0 이면 두 패스 다 건너뛴다.
/// </summary>
public static class BossVictoryGlitchRender
{
    // 렌더링 레이어 비트 — 0 조명 · 1 데칼 · 2·3 실루엣 다음. 옛 승리 연출(Legacy)과 같은 4·6(그쪽은 연결이 끊겨 겹쳐 쓰지 않는다).
    // 추가만 하고 지우지 않는다(대입 금지 — bit 0 을 잃으면 캐릭터가 까매진다).
    public const int PlayerLayerIndex = 4;
    public const int BossLayerIndex = 6;
    public const uint PlayerMask = 1u << PlayerLayerIndex;
    public const uint BossMask = 1u << BossLayerIndex;
    public const uint AllMask = PlayerMask | BossMask;

    public const string MaskShaderName = "Hidden/Rendering/BossVictoryGlitchMask";
    public const string CompositeShaderName = "Hidden/Rendering/BossVictoryGlitch";

    /// <summary>합성 셰이더 칸 — 표시가 매 프레임 넣는 세기(0~1 대부분).</summary>
    public struct Frame
    {
        public float Time;          // 처치 판정 뒤 초(노이즈·흔들림 시간)
        public float Desaturate;    // 화면 흑백
        public float Amber;         // 배경 호박색 틴트(플레이어 제외)
        public float Scanline;      // CRT 주사선
        public float Tear;          // 가로줄 좌우 찢김(유일한 모양 글리치)
        public float Smear;         // 찢긴 줄 밝은 부분 가로 끌림
        public float BossNoise;     // 보스 노이즈 세기
        public Vector2 BossScreenCenter;   // 보스 중심(뷰포트 0~1) — 노이즈 보스 기준 공간
        public float BossScreenSize;       // 보스 화면 높이(뷰포트) — 노이즈 보스 기준 공간 배율
        public Vector2 NoiseOffset;        // 노이즈 UV 오프셋 — 카메라 이동으로만 누적(시간 흐름 없음)
        public Vector3 GroundCircleCenter; // 발밑 회색 원 중심(월드, 246 복원)
        public float GroundCircleRadius;   // 발밑 회색 원 반경(m) — 0 = 끔
        public float Rectify;              // 배경 글자 알파(267 후속)
        public float RectifyFill;          // 안쪽 색 채움 불투명도(267 후속)
        public float RectifyFillElapsed;   // 안쪽 색 클립 시작 뒤 초(원 시간표 기준, 없으면 음수)
    }

    static readonly int IdId = Shader.PropertyToID("_GlitchId");
    static readonly int IdIdTex = Shader.PropertyToID("_GlitchIdTex");
    static readonly int IdA = Shader.PropertyToID("_GlitchA");        // x (미사용), y time, z desat, w amber
    static readonly int IdB = Shader.PropertyToID("_GlitchB");        // x scanline, y tear, z smear, w (미사용)
    static readonly int IdC = Shader.PropertyToID("_GlitchC");        // x bossNoise
    static readonly int IdTear = Shader.PropertyToID("_GlitchTear");  // x 최대 밀림, y 켜지는 줄 비율, z 끌림 길이
    static readonly int IdBossScreen = Shader.PropertyToID("_BossScreen");   // xy 중심, z 화면 높이, w 공간(0 화면 · 1 보스 기준)
    static readonly int IdAmberColor = Shader.PropertyToID("_AmberColor");
    static readonly int IdNoiseTex = Shader.PropertyToID("_NoiseTex");
    static readonly int IdNoiseST = Shader.PropertyToID("_NoiseST");          // xy 타일링, zw UV 오프셋(카메라 이동 누적)
    static readonly int IdNoiseBright = Shader.PropertyToID("_NoiseBright");
    static readonly int IdNoiseDark = Shader.PropertyToID("_NoiseDark");
    static readonly int IdRectifyTex = Shader.PropertyToID("_RectifyTex");
    static readonly int IdRectifyScreen = Shader.PropertyToID("_RectifyScreen");   // 글자마다 xy 최소 · zw 최대(뷰포트)
    static readonly int IdRectifyUV = Shader.PropertyToID("_RectifyUV");           // 글자마다 아틀라스 xy 시작 · zw 크기
    static readonly int IdRectifyParams = Shader.PropertyToID("_RectifyParams");   // x 알파, y 외곽선 반두께(SDF), z 글자 수
    static readonly int IdRectifyColor = Shader.PropertyToID("_RectifyColor");
    static readonly int IdFillRings = Shader.PropertyToID("_RectifyFillRings");       // 링 i — x 반지름(단어 높이 단위), y 알파
    static readonly int IdFillColors = Shader.PropertyToID("_RectifyFillColors");
    static readonly int IdFillRect = Shader.PropertyToID("_RectifyFillRect");         // 단어 사각형(뷰포트 min.xy · max.zw)
    static readonly int IdFillParams = Shader.PropertyToID("_RectifyFillParams");     // x 불투명도, y 경계(단어 높이), z 단어 종횡비, w 주사선 간격(px)
    static readonly int IdFillLook = Shader.PropertyToID("_RectifyFillLook");         // x 주사선 어둡기, y 그라데이션 세기, zw 중심(단어 UV)
    static readonly int IdCircle = Shader.PropertyToID("_GroundCircle");          // xyz 중심(월드), w 반경(m)
    static readonly int IdScanFloor = Shader.PropertyToID("_ScanFloor");          // x 벽 normal.y, y 바닥 normal.y, z 바닥 남김(267 후속)
    static readonly int IdCircleLook = Shader.PropertyToID("_GroundCircleLook");  // x 가장자리 부드러움(m), y 흑백 밝기
    static readonly int IdLook = Shader.PropertyToID("_GlitchLook");          // x 주사선 줄 수, y 찢김 줄 굵기(px), z 굵기 최대 배수, w 찢김 갱신(초당)

    static Material _maskPlayer, _maskBoss, _composite;
    static MaskPass _maskPass;
    static CompositePass _compositePass;
    static bool _anyEffect;   // 이번 프레임 효과가 하나라도 켜졌나 — 다 0 이면 패스를 아예 건너뛴다
    static bool _hooked;

    public static bool Active => _hooked;

    /// <summary>켠다(멱등). 셰이더가 없으면 false(호출측이 진단을 남긴다).</summary>
    public static bool Enable(BossVictoryGlitchSettings settings)
    {
        if (_hooked) return true;
        Shader mask = settings != null && settings.maskShader != null ? settings.maskShader : Shader.Find(MaskShaderName);
        Shader comp = settings != null && settings.compositeShader != null ? settings.compositeShader : Shader.Find(CompositeShaderName);
        if (mask == null || comp == null) return false;

        _maskPlayer = NewMask(mask, new Color(1f, 0f, 0f, 1f));
        _maskBoss = NewMask(mask, new Color(0f, 0f, 1f, 1f));
        _composite = CoreUtils.CreateEngineMaterial(comp);
        ApplyLook(settings);
        _anyEffect = false;

        _maskPass = new MaskPass(_maskPlayer, _maskBoss)
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents + 1,   // 안개 다음, 반투명 전
        };
        _maskPass.ConfigureInput(ScriptableRenderPassInput.Depth);
        _compositePass = new CompositePass(_composite)
        {
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents,       // 이펙트까지 그린 뒤, 후처리 전
        };
        _compositePass.ConfigureInput(ScriptableRenderPassInput.Depth);   // 발밑 회색 원 — 픽셀 월드 위치

        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        _hooked = true;
        return true;
    }

    /// <summary>끈다(멱등).</summary>
    public static void Disable()
    {
        if (!_hooked) return;
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        _hooked = false;
        _maskPass = null;
        _compositePass = null;
        CoreUtils.Destroy(_maskPlayer);
        CoreUtils.Destroy(_maskBoss);
        CoreUtils.Destroy(_composite);
        _maskPlayer = _maskBoss = _composite = null;
        BossVictoryRectifyText.Destroy(_rectifyAsset);
        _rectifyAsset = null;
        _rectifyFont = null;
        _rectifyText = null;
        _rectifyGlyphs.Clear();
        _rectifyCount = 0;
        _anyEffect = false;
    }

    /// <summary>색·노이즈·모양 칸(설정 SO) — 켤 때 + 랩에서 값을 바꿨을 때.</summary>
    public static void ApplyLook(BossVictoryGlitchSettings s)
    {
        if (_composite == null || s == null) return;
        _composite.SetColor(IdAmberColor, s.amberColor.linear);
        _composite.SetTexture(IdNoiseTex, s.bossNoiseTexture != null ? s.bossNoiseTexture : Texture2D.grayTexture);
        _noiseTiling = s.bossNoiseTiling;
        _composite.SetColor(IdNoiseBright, s.bossNoiseBrightColor.linear * Mathf.Max(0f, s.bossNoiseBrightIntensity));
        _composite.SetColor(IdNoiseDark, s.bossNoiseDarkColor.linear);
        _composite.SetVector(IdLook, new Vector4(Mathf.Max(1f, s.scanlineCount), Mathf.Max(1f, s.tearLinePixels),
            Mathf.Max(1f, s.tearLineMaxMultiplier), Mathf.Max(1f, s.tearRefreshRate)));
        _composite.SetVector(IdTear, new Vector4(Mathf.Max(0f, s.tearMaxShift), Mathf.Clamp01(s.tearDensity), Mathf.Max(0f, s.tearStreakLength), 0f));
        _noiseSpace = s.bossNoiseSpace == BossVictoryGlitchSettings.NoiseSpace.Boss ? 1f : 0f;
        float floorMin = Mathf.Clamp01(s.scanlineFloorNormalMin);
        _composite.SetVector(IdScanFloor, new Vector4(floorMin, Mathf.Max(floorMin + 0.001f, Mathf.Clamp01(s.scanlineFloorNormalMax)), Mathf.Clamp01(s.scanlineFloorKeep), 0f));
        _composite.SetVector(IdCircleLook, new Vector4(Mathf.Max(0f, s.groundCircleSoftness), Mathf.Max(0f, s.groundCircleBrightness), 0f, 0f));
        ApplyRectify(s);
        ApplyFillLook(s);
    }

    // 배경 글자(267 후속) — 켤 때 아틀라스 1회, 배치는 화면 비율이 바뀔 때만.
    static TMPro.TMP_FontAsset _rectifyAsset;
    static readonly List<BossVictoryRectifyText.Glyph> _rectifyGlyphs = new List<BossVictoryRectifyText.Glyph>();
    static readonly Vector4[] _rectifyScreen = new Vector4[BossVictoryRectifyText.MaxGlyphs];
    static readonly Vector4[] _rectifyUV = new Vector4[BossVictoryRectifyText.MaxGlyphs];
    static float _rectifyCap, _rectifyAspect = -1f, _rectifyWidth, _rectifyCenterY, _rectifyHalf;
    static int _rectifyCount;
    static Font _rectifyFont;
    static string _rectifyText;

    static void ApplyRectify(BossVictoryGlitchSettings s)
    {
        if (_rectifyFont != s.rectifyFont || _rectifyText != s.rectifyText)
        {
            BossVictoryRectifyText.Destroy(_rectifyAsset);
            _rectifyAsset = BossVictoryRectifyText.CreateAtlas(s.rectifyFont, s.rectifyText, _rectifyGlyphs, out _rectifyCap);
            _rectifyFont = s.rectifyFont;
            _rectifyText = s.rectifyText;
            if (s.rectifyFont != null && _rectifyAsset == null) Debug.LogWarning($"[BossVictory] 배경 글자 아틀라스를 못 만들었다 — 폰트 {s.rectifyFont.name} · '{s.rectifyText}'");
        }
        _rectifyWidth = s.rectifyWidth;
        _rectifyCenterY = s.rectifyCenterY;
        _rectifyHalf = BossVictoryRectifyText.OutlineHalfWidthSdf(_rectifyCap, s.rectifyOutline, BossVictoryRectifyText.AtlasPadding);
        _rectifyAspect = -1f;   // 다음 프레임에 다시 배치
        _composite.SetColor(IdRectifyColor, s.rectifyColor.linear);
        _composite.SetTexture(IdRectifyTex, _rectifyAsset != null ? _rectifyAsset.atlasTexture : Texture2D.blackTexture);
    }

    static void LayoutRectify()
    {
        float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
        if (Mathf.Approximately(aspect, _rectifyAspect)) return;
        _rectifyAspect = aspect;
        _rectifyCount = _rectifyAsset == null ? 0 : BossVictoryRectifyText.Layout(_rectifyGlyphs, _rectifyCap, BossVictoryRectifyText.AtlasPadding,
            new Vector2(_rectifyAsset.atlasWidth, _rectifyAsset.atlasHeight), _rectifyWidth, _rectifyCenterY, aspect, _rectifyScreen, _rectifyUV);
        _composite.SetVectorArray(IdRectifyScreen, _rectifyScreen);
        _composite.SetVectorArray(IdRectifyUV, _rectifyUV);
    }

    // 안쪽 색 Ring Wipe Fill(267 후속) — 설정 값 사본(ApplyLook)·링 3개 배열.
    static Color[] _fillColors = new Color[0];
    static Vector2 _fillCenter = new Vector2(0.46f, 0.5f);
    static float _fillDelay, _fillFade, _fillExpand = 0.6f, _fillSoft, _fillGradient, _scanSpacing = 0.003f, _scanDark;
    static readonly Vector4[] _fillRings = new Vector4[BossVictoryRectifyFill.RingCount];
    static readonly Vector4[] _fillRingColors = new Vector4[BossVictoryRectifyFill.RingCount];

    static void ApplyFillLook(BossVictoryGlitchSettings s)
    {
        _fillColors = s.rectifyFillColors ?? new Color[0];
        _fillCenter = s.rectifyFillCenter;
        _fillDelay = s.rectifyFillDelay;
        _fillFade = s.rectifyFillFade;
        _fillExpand = s.rectifyFillExpand;
        _fillSoft = s.rectifyFillSoftness;
        _fillGradient = Mathf.Clamp01(s.rectifyFillGradient);
        _scanSpacing = s.rectifyScanlineSpacing;
        _scanDark = Mathf.Clamp01(s.rectifyScanlineDark);
    }

    // 링 3개 — 단어 사각형 UV(종횡비 보정, 단위 = 단어 높이). Rmax = 중심에서 네 모서리까지.
    static void SetFillFrame(in Frame f, float aspect)
    {
        float opacity = _rectifyCount > 0 && _fillColors.Length > 0 && f.RectifyFillElapsed >= 0f ? Mathf.Clamp01(f.RectifyFill) : 0f;
        Vector4 word = BossVictoryRectifyFill.WordRect(_rectifyScreen, _rectifyCount);
        float wordAspect = BossVictoryRectifyFill.WordAspect(word, aspect);
        float rMax = BossVictoryRectifyFill.MaxRadius(_fillCenter, wordAspect);
        for (int i = 0; i < BossVictoryRectifyFill.RingCount; i++)
        {
            BossVictoryRectifyFill.Ring r = opacity > 0f && i < _fillColors.Length
                ? BossVictoryRectifyFill.Evaluate(i, f.RectifyFillElapsed, _fillDelay, _fillFade, _fillExpand)
                : default;
            _fillRings[i] = r.exists ? new Vector4(BossVictoryRectifyFill.Radius(r.grow, rMax, _fillSoft), r.alpha, 0f, 0f) : Vector4.zero;
            _fillRingColors[i] = i < _fillColors.Length ? (Vector4)_fillColors[i].linear : Vector4.zero;
        }
        _composite.SetVectorArray(IdFillRings, _fillRings);
        _composite.SetVectorArray(IdFillColors, _fillRingColors);
        _composite.SetVector(IdFillRect, word);
        _composite.SetVector(IdFillParams, new Vector4(opacity, Mathf.Max(0f, _fillSoft), wordAspect, Mathf.Max(1f, _scanSpacing * Screen.height)));
        _composite.SetVector(IdFillLook, new Vector4(_scanDark, _fillGradient, _fillCenter.x, _fillCenter.y));
    }

    static float _noiseSpace;
    static Vector2 _noiseTiling = Vector2.one;

    /// <summary>이번 프레임 세기(표시가 매 프레임).</summary>
    public static void SetFrame(in Frame f)
    {
        _anyEffect = Mathf.Max(Mathf.Max(f.Desaturate, f.Amber), Mathf.Max(Mathf.Max(f.Scanline, f.Tear), Mathf.Max(f.Smear, f.BossNoise))) > 0.0001f;
        if (_composite == null) return;
        _composite.SetVector(IdA, new Vector4(0f, f.Time, f.Desaturate, f.Amber));
        _composite.SetVector(IdB, new Vector4(f.Scanline, f.Tear, f.Smear, 0f));
        _composite.SetVector(IdC, new Vector4(f.BossNoise, 0f, 0f, 0f));
        _composite.SetVector(IdBossScreen, new Vector4(f.BossScreenCenter.x, f.BossScreenCenter.y, Mathf.Max(0.01f, f.BossScreenSize), _noiseSpace));
        _composite.SetVector(IdNoiseST, new Vector4(_noiseTiling.x, _noiseTiling.y, f.NoiseOffset.x, f.NoiseOffset.y));
        LayoutRectify();
        SetFillFrame(f, _rectifyAspect > 0f ? _rectifyAspect : 16f / 9f);
        _composite.SetVector(IdRectifyParams, new Vector4(_rectifyCount > 0 ? Mathf.Clamp01(f.Rectify) : 0f, _rectifyHalf, _rectifyCount,
            _rectifyAsset != null ? _rectifyAsset.atlasHeight / (BossVictoryRectifyText.AtlasPadding + 1f) : 1f));
        _composite.SetVector(IdCircle, new Vector4(f.GroundCircleCenter.x, f.GroundCircleCenter.y, f.GroundCircleCenter.z, Mathf.Max(0f, f.GroundCircleRadius)));
    }

    /// <summary>렌더러에 비트를 더한다(OR). 돌려받은 목록으로 <see cref="Untag"/>.</summary>
    public static void Tag(GameObject root, uint bit, List<Renderer> tagged)
    {
        if (root == null) return;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            // 몸·무기만 — 이펙트는 마스크에 넣지 않는다.
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
        m.SetColor(IdId, id);
        return m;
    }

    static void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
    {
        if (_maskPass == null || !_anyEffect || cam == null || cam.cameraType != CameraType.Game) return;
        UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
        if (data == null || data.renderType != CameraRenderType.Base || data.scriptableRenderer == null) return;
        data.scriptableRenderer.EnqueuePass(_maskPass);
        data.scriptableRenderer.EnqueuePass(_compositePass);
    }

    static readonly Vector4 FullScreenScaleBias = new Vector4(1f, 1f, 0f, 0f);

    // ------------------------------------------------------------------------
    sealed class GlitchFrameData : ContextItem
    {
        public TextureHandle idTex;
        public int frame = -1;

        public override void Reset()
        {
            idTex = TextureHandle.nullHandle;
            frame = -1;
        }
    }

    sealed class MaskPass : ScriptableRenderPass
    {
        static readonly List<ShaderTagId> ShaderTags = new()
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit"),
        };

        readonly Material _player, _boss;

        class MaskData
        {
            public RendererListHandle player, boss;
        }

        public MaskPass(Material player, Material boss)
        {
            _player = player;
            _boss = boss;
            profilingSampler = new ProfilingSampler("BossVictoryGlitchMask");
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            GlitchFrameData shared = frameData.GetOrCreate<GlitchFrameData>();
            shared.frame = -1;

            if (resourceData.isActiveTargetBackBuffer) return;
            TextureHandle color = resourceData.activeColorTexture;
            if (!color.IsValid()) return;

            RenderTextureDescriptor idDesc = cameraData.cameraTargetDescriptor;
            idDesc.msaaSamples = 1;
            idDesc.depthBufferBits = 0;
            idDesc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm;
            TextureHandle idTex = UniversalRenderer.CreateRenderGraphTexture(renderGraph, idDesc, "_BossVictoryGlitchId", true, FilterMode.Point);

            using (var builder = renderGraph.AddRasterRenderPass<MaskData>("BossVictoryGlitchMask", out MaskData data))
            {
                data.player = List(renderGraph, renderingData, cameraData, lightData, _player, PlayerMask);
                data.boss = List(renderGraph, renderingData, cameraData, lightData, _boss, BossMask);
                builder.UseRendererList(data.player);
                builder.UseRendererList(data.boss);
                // 마스크 셰이더가 _CameraDepthTexture 로 "보이는 표면"만 고른다 — 읽기 선언이 있어야 깊이 복사 뒤에 놓인다.
                if (resourceData.cameraDepthTexture.IsValid())
                    builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                builder.UseAllGlobalTextures(true);
                builder.SetRenderAttachment(idTex, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (MaskData d, RasterGraphContext ctx) =>
                {
                    ctx.cmd.DrawRendererList(d.boss);
                    ctx.cmd.DrawRendererList(d.player);
                });
            }

            shared.idTex = idTex;
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

    sealed class CompositePass : ScriptableRenderPass
    {
        readonly Material _composite;

        class BlitData
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle idTex;
        }

        public CompositePass(Material composite)
        {
            _composite = composite;
            profilingSampler = new ProfilingSampler("BossVictoryGlitch");
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (!frameData.Contains<GlitchFrameData>()) return;
            GlitchFrameData shared = frameData.Get<GlitchFrameData>();
            if (shared.frame != Time.frameCount || !shared.idTex.IsValid()) return;
            shared.frame = -1;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            if (resourceData.isActiveTargetBackBuffer) return;
            TextureHandle color = resourceData.activeColorTexture;
            if (!color.IsValid()) return;

            RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            TextureHandle dest = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_BossVictoryGlitchColor", false, FilterMode.Bilinear);
            using (var builder = renderGraph.AddRasterRenderPass<BlitData>("BossVictoryGlitch", out BlitData data))
            {
                data.material = _composite;
                data.source = color;
                data.idTex = shared.idTex;
                builder.UseTexture(color, AccessFlags.Read);
                builder.UseTexture(shared.idTex, AccessFlags.Read);
                // 발밑 회색 원(246 복원) — 셰이더가 _CameraDepthTexture 로 픽셀 월드 위치를 되살린다(읽기 선언 = 깊이 복사 뒤에 놓임).
                if (resourceData.cameraDepthTexture.IsValid())
                    builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                builder.SetRenderAttachment(dest, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (BlitData d, RasterGraphContext ctx) =>
                {
                    d.material.SetTexture(IdIdTex, d.idTex);
                    Blitter.BlitTexture(ctx.cmd, d.source, FullScreenScaleBias, d.material, 0);
                });
            }
            resourceData.cameraColor = dest;
        }
    }
}
