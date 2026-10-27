using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 보스 등장 시네마틱의 <b>로컬 표시</b>(PLAN-boss-encounter-cutscene) — 모든 피어에서 <see cref="BossEncounterDirector"/> 복제 상태
/// (단계·단계 시작 ServerTime·회차·스킵 수)만 읽어 그린다. 판정·진행은 서버(디렉터)가 한다.
///
/// · 연출 카메라 = 런타임 vcam 하나(착지점·화면 아래 기준 구도) → <see cref="CameraTargetSwitcher.EnterCinematic"/>.
///   진입은 컷(도착 암전 직후), 복귀는 지시 구간 끝 <see cref="BossEncounterDirector.CameraReturnSeconds"/> 블렌드.
/// · 레터박스(상하 띠)·비네팅(런타임 Volume, CameraFeedback HP 비네팅(우선순위 100) 위) — 같은 가중치로 페이드.
/// · 웰즈 독백(지시 구간) · 스킵 위젯(Space 1초 홀드 → 버튼 외곽선이 채워진다, 채운 표는 유지) — 문구는 TutorialGuideTexts 표.
/// 레터박스·비네팅·스킵 위젯·HUD 끄기·연출 카메라는 공용 <see cref="CinematicOverlay"/>(4구역 소개 연출과 같은 코드 — 10-26 분리, 동작 무변경).
/// 붙는 곳: 디렉터 OnNetworkSpawn 이 런타임에 붙인다(씬·프리팹 무수정).
/// </summary>
[DisallowMultipleComponent]
public sealed class BossIntroPresentation : MonoBehaviour
{
    private const string MonologueId = "BOSS_INTRO_WellsCommand";
    // 스킵 위젯 모양은 타이틀 ESC 건너뛰기(TitleSkipHint)도 같이 쓴다 — internal(경석 10-26). 정본은 CinematicOverlay.
    internal const float SkipPillWidth = CinematicOverlay.SkipPillWidth;
    internal const float SkipPillHeight = CinematicOverlay.SkipPillHeight;
    internal static readonly Color SkipIdleColor = CinematicOverlay.SkipIdleColor;
    internal static readonly Color SkipTextColor = CinematicOverlay.SkipTextColor;
    internal static readonly Color KeycapTextColor = CinematicOverlay.KeycapTextColor;
    internal const float SkipKeycapWidth = CinematicOverlay.SkipKeycapWidth;
    internal const float SkipKeycapHeight = CinematicOverlay.SkipKeycapHeight;
    private const float CameraEnterBlendLate = 0.35f;   // 늦게 붙은 클라 — 암전이 아니므로 짧게 블렌드
    private const float MonologueFadeSeconds = 0.25f;

    private BossEncounterDirector _director;
    private CinematicOverlay _overlay;

    private CanvasGroup _monologueGroup;
    private TMP_Text _monologueText;
    private bool _monologueResolved;   // 문구는 판 하나에 한 번만 찾는다(없으면 경고 1회 — 매 프레임 찍지 않게)
    private float _monologueAlpha;

    private int _impactFxSequence = -1;

    private void Awake()
    {
        _director = GetComponent<BossEncounterDirector>();
    }

    private void OnDestroy()
    {
        _overlay?.Dispose();
    }

    private CinematicOverlay Overlay => _overlay ??= new CinematicOverlay("BossIntro", _director != null ? _director.SkipButtonSprite : null);

    private void Update()
    {
        if (_director == null || !_director.IsSpawned)
        {
            Apply(false, false, false, Time.unscaledDeltaTime);
            Overlay.ExitCamera(0f);
            Overlay.RestoreHud();
            return;
        }

        bool active = _director.IsIntroCinematicActive;
        BossEncounterPhase phase = _director.Phase;
        NetworkManager nm = NetworkManager.Singleton;
        double now = nm != null ? nm.ServerTime.Time : 0d;
        double elapsed = now - _director.PhaseStartServerTime;

        // 카메라·화면 효과는 지시 구간 끝(복귀 블렌드 시작)까지. 스킵·중단이면 단계가 바뀌는 즉시 걷힌다.
        bool returning = phase == BossEncounterPhase.Dialogue && elapsed >= _director.CameraReturnAt;
        bool wantCinematic = active && !returning;

        if (wantCinematic && !Overlay.IsCameraActive)
            EnterCamera(phase == BossEncounterPhase.Preparing && elapsed < 0.5d ? 0f : CameraEnterBlendLate);
        else if (!wantCinematic && Overlay.IsCameraActive)
            Overlay.ExitCamera(_director.CameraReturnSeconds);
        if (Overlay.IsCameraActive) PlaceCamera();

        // 착지 연출(로컬 파티클) — 회차마다 Impact 에 들어서는 순간 한 번.
        if (active && phase == BossEncounterPhase.Impact && _impactFxSequence != _director.IntroSequence)
        {
            _impactFxSequence = _director.IntroSequence;
            SpawnImpactParticles();
        }

        bool showMonologue = active && phase == BossEncounterPhase.Dialogue && _director.ShowMonologue;
        // HUD — 연출 내내 끄고, 끝나는 순간(전투 시작·스킵·중단) 다시 켠다. 보스 HP바는 그때 1/4 에서 차오른다(BossHealthHUD).
        if (active) Overlay.HideHud();
        else Overlay.RestoreHud();
        // 스킵은 카메라 복귀 전까지만 — 복귀가 시작되면(자연 진행이든 스킵 직후든) 이미 넘어가는 중이라 버튼을 걷는다.
        bool skippable = active && !returning;
        if (Overlay.TickSkip(skippable, _director.IntroSequence, _director.SkipHoldSeconds))
            _director.RequestSkipVote(_director.IntroSequence);
        Apply(wantCinematic, showMonologue, skippable, Time.unscaledDeltaTime);
    }

    // ── 착지 파티클(프로토타입 — 경석 10-23) ─────────────────────────────────
    // 정식 VFX 는 VFX 담당(김민경)이 만든다 — 이건 "어떤 느낌인지" 보려는 코드 파티클이다. 풍압은 넣지 않는다(경석 결정).
    // ① 먼지: 착지점 둘레에서 바닥을 따라 천천히 퍼지며 흩날리는 옅은 먼지(레퍼런스 1 — 낮게 깔린 안개).
    // ② 전기 잔여물: 착지점 주위를 떠다니다 사라지는 작은 푸른 불티(레퍼런스 2). 몸 전기는 23호 RPC(팔다리 펄스 1초).

    private void SpawnImpactParticles()
    {
        Transform landing = _director.LandingPoint;
        if (landing == null) return;
        Vector3 at = landing.position;

        if (_director.IntroDustMaterial != null)
            SpawnDust(at, _director.IntroDustMaterial);
        if (_director.IntroSparkMaterial != null)
            SpawnElectricMotes(at, _director.IntroSparkMaterial, Mathf.Max(0.1f, _director.ImpactElectricSeconds));
    }

    private static ParticleSystem NewSystem(string name, Vector3 position, Material material, int maxParticles)
    {
        var go = new GameObject(name);
        go.transform.position = position;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = maxParticles;
        main.stopAction = ParticleSystemStopAction.Destroy;   // 다 끝나면 오브젝트째 정리

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return ps;
    }

    private static void SpawnDust(Vector3 at, Material material)
    {
        ParticleSystem ps = NewSystem("BossIntroDust", at + Vector3.up * 0.3f, material, 60);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 3f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.4f, 3.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 3.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(2.2f, 4.2f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(0.62f, 0.6f, 0.58f, 0.45f);
        main.gravityModifier = 0f;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 36), new ParticleSystem.Burst(0.15f, 14) });

        // 바닥 평면 고리에서 바깥으로(원형 셰이프는 XY 평면이라 눕힌다).
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 2.2f;
        shape.radiusThickness = 0.4f;
        shape.rotation = new Vector3(90f, 0f, 0f);

        ParticleSystem.LimitVelocityOverLifetimeModule limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.limit = 0.6f;
        limit.dampen = 0.08f;   // 처음 퍼지고 곧 느려져 떠다닌다

        ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        // 세 축이 같은 곡선 모드여야 한다(하나만 TwoConstants 면 Unity 가 거부한다).
        vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.y = new ParticleSystem.MinMaxCurve(0.05f, 0.35f);
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        ParticleSystem.NoiseModule noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.35f;
        noise.frequency = 0.4f;

        ParticleSystem.RotationOverLifetimeModule rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);

        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.6f));

        ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
        color.enabled = true;
        color.color = FadeGradient(Color.white, 0.12f, 0.75f);

        ps.Play();
    }

    // emitSeconds = 몸 전기와 같은 시간(디렉터 impactElectricSeconds, 경석 10-23: 1.5초) 동안 피어나고, 남은 불티는 수명대로 사라진다.
    private static void SpawnElectricMotes(Vector3 at, Material material, float emitSeconds)
    {
        // 양·범위 = 처음 프로토타입의 2배(경석 10-23 Play: 반경 3.2 → 6.4m, 초당 22 → 44 · 첫 분출 18 → 36).
        ParticleSystem ps = NewSystem("BossIntroElectricMotes", at + Vector3.up * 1.2f, material, 260);

        ParticleSystem.MainModule main = ps.main;
        main.duration = emitSeconds;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.45f, 0.75f, 1f, 1f), new Color(0.75f, 0.95f, 1f, 1f));
        main.gravityModifier = -0.03f;   // 아주 천천히 떠오른다

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 44f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 36) });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 6.4f;
        shape.radiusThickness = 0.8f;

        ParticleSystem.NoiseModule noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.7f;
        noise.frequency = 1.1f;
        noise.scrollSpeed = 0.6f;

        // 깜빡임 — 크기가 빠르게 들쑥날쑥(전기 불티 느낌).
        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        var flicker = new AnimationCurve(
            new Keyframe(0f, 0.3f), new Keyframe(0.12f, 1f), new Keyframe(0.25f, 0.55f), new Keyframe(0.4f, 1f),
            new Keyframe(0.6f, 0.6f), new Keyframe(0.8f, 0.9f), new Keyframe(1f, 0f));
        size.size = new ParticleSystem.MinMaxCurve(1f, flicker);

        ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
        color.enabled = true;
        color.color = FadeGradient(Color.white, 0.08f, 0.7f);

        ps.Play();
    }

    // 알파만 0 → 1 → 0(나타나는 구간·사라지기 시작하는 지점).
    private static Gradient FadeGradient(Color tint, float fadeIn, float fadeOutStart)
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(tint, 0f), new GradientColorKey(tint, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeIn), new GradientAlphaKey(1f, fadeOutStart), new GradientAlphaKey(0f, 1f) });
        return g;
    }

    // ── 카메라 ─────────────────────────────────────────────────────────────

    private void EnterCamera(float blendSeconds)
    {
        if (!TryCameraPose(out Vector3 position, out Quaternion rotation)) return;
        Overlay.EnterCamera(position, rotation, _director.CameraFov, blendSeconds);
    }

    // 구도 — 대열 뒤(화면 아래)에서 착지점 쪽(위)을 낮은 각도로 본다. 파티가 카메라에서 멀어지며 보스에게 걸어간다.
    private void PlaceCamera()
    {
        if (TryCameraPose(out Vector3 position, out Quaternion rotation))
            Overlay.PlaceCamera(position, rotation, _director.CameraFov);
    }

    private bool TryCameraPose(out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;
        Transform landing = _director.LandingPoint;
        if (landing == null) return false;

        Vector3 south = GameplayScreenSouth();
        Vector3 focus = landing.position + south * _director.CameraFocusSouth + Vector3.up * _director.CameraFocusHeight;
        CinematicFraming.Pose(focus, south, _director.CameraPitch, _director.CameraDistance, out position, out rotation);
        return true;
    }

    /// <summary>
    /// [모든 피어] 게임플레이 화면의 아래 방향(수평) — 고정 게임 카메라 회전(<see cref="CameraTargetSwitcher.TryGetGameplayViewRotation"/>) 기준.
    /// 🔴 렌더 카메라 transform 을 읽으면 안 된다 — 연출 중에는 이 연출 카메라가 그 transform 을 움직여 자기 자신을 기준으로 삼는다
    ///    (경석 10-23 Play: 요가 게임 카메라 −45° 가 아니라 0° 쪽으로 잡혀 방 벽이 화면과 나란히 보였다). 디렉터 대열·보스 방향도 이것을 쓴다.
    /// </summary>
    public static Vector3 GameplayScreenSouth()
    {
        CameraTargetSwitcher switcher = CameraTargetSwitcher.Active;
        if (switcher != null && switcher.TryGetGameplayViewRotation(out Quaternion view))
            return BossIntroFormation.Flat(-(view * Vector3.forward));

        Camera cam = switcher != null && switcher.GameplayCamera != null ? switcher.GameplayCamera : Camera.main;
        Vector3 dir = cam != null
            ? BossChargeFacingPolicy.ScreenSouth(cam.transform.forward, cam.transform.up, BossChargeFacingPolicy.DefaultFallback)
            : BossChargeFacingPolicy.DefaultFallback;
        return BossIntroFormation.Flat(dir);
    }

    // ── 화면 효과·UI ────────────────────────────────────────────────────────

    private void Apply(bool cinematic, bool monologue, bool skipVisible, float deltaTime)
    {
        _monologueAlpha = Mathf.MoveTowards(_monologueAlpha, monologue ? 1f : 0f, deltaTime / MonologueFadeSeconds);

        bool hasDirector = _director != null;
        Overlay.Apply(cinematic, skipVisible, _monologueAlpha > 0f,
            hasDirector ? _director.LetterboxHeight : 0.04f,
            hasDirector ? _director.VignetteIntensity : 0.35f,
            hasDirector ? _director.VignetteSmoothness : 0.42f,
            hasDirector ? _director.OverlayFadeSeconds : 0.4f,
            hasDirector ? _director.SkipHoldSeconds : 1f,
            hasDirector ? _director.SkipVoteCount : 0,
            hasDirector ? _director.SkipVoterCount : 0,
            deltaTime);
        ApplyFont();

        if (_monologueAlpha <= 0f && _monologueGroup == null) return;   // 독백을 한 번도 안 띄웠다 — 만들 필요 없다
        EnsureMonologue();
        _monologueGroup.alpha = _monologueAlpha;
        if (monologue && !_monologueResolved)
        {
            string line = ResolveText(MonologueId);
            _monologueResolved = line != null;
            _monologueText.text = line ?? string.Empty;
        }
    }

    // 독백 — 오버레이 위, 아래 띠 위 가운데.
    private void EnsureMonologue()
    {
        if (_monologueGroup != null) return;
        var mono = new GameObject("Monologue", typeof(RectTransform), typeof(CanvasGroup));
        mono.transform.SetParent(Overlay.Root, false);
        var monoRect = (RectTransform)mono.transform;
        monoRect.anchorMin = new Vector2(0.5f, 0f);
        monoRect.anchorMax = new Vector2(0.5f, 0f);
        monoRect.pivot = new Vector2(0.5f, 0f);
        monoRect.anchoredPosition = new Vector2(0f, 120f);
        monoRect.sizeDelta = new Vector2(1400f, 80f);
        _monologueGroup = mono.GetComponent<CanvasGroup>();
        _monologueGroup.alpha = 0f;
        _monologueText = CinematicOverlay.Label(mono.transform, "Text", 44f, TextAlignmentOptions.Center);
        CinematicOverlay.Stretch((RectTransform)_monologueText.transform);
    }

    // null = 안내 UI 가 아직 없다(다음 프레임에 다시). 빈 문자열 = 표에 없다(경고 1회 후 빈 칸).
    private string ResolveText(string id)
    {
        TutorialGuideUI ui = TutorialGuideUI.Instance;
        if (ui == null) return null;
        if (ui.TryResolveText(id, out string text)) return text;
        Edit.LogWarning($"[BossIntro] 문구 '{id}' 를 TutorialGuideTexts 에서 못 찾았다 — 빈 칸으로 둔다.", this);
        return string.Empty;
    }

    private void ApplyFont()
    {
        TMP_FontAsset font = _director != null ? _director.IntroFont : null;   // SeoulAlrim SDF(디렉터 프리팹)
        if (font == null)
        {
            TutorialGuideUI ui = TutorialGuideUI.Instance;
            font = ui != null ? ui.SubtitleFont : null;
        }
        if (font == null) return;   // 다음 프레임에 다시(안내 UI 가 늦게 뜰 수 있다)
        Overlay.ApplyFont(font);    // 스킵 위젯 글자 — 한 번만
        if (_monologueText != null && _monologueText.font != font) _monologueText.font = font;
    }

    // 타이틀 ESC 건너뛰기(TitleSkipHint)가 쓰는 이름 — 정본은 CinematicOverlay.
    internal static Sprite KeycapSprite(int width, int height) => CinematicOverlay.KeycapSprite(width, height);

    internal static Sprite RoundedOutlineSprite(int width, int height, float radius, float thickness) =>
        CinematicOverlay.RoundedOutlineSprite(width, height, radius, thickness);
}
