using TMPro;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// 보스 등장 시네마틱의 <b>로컬 표시</b>(PLAN-boss-encounter-cutscene) — 모든 피어에서 <see cref="BossEncounterDirector"/> 복제 상태
/// (단계·단계 시작 ServerTime·회차·스킵 수)만 읽어 그린다. 판정·진행은 서버(디렉터)가 한다.
///
/// · 연출 카메라 = 런타임 <see cref="CinemachineCamera"/> 하나(착지점·화면 아래 기준 구도) → <see cref="CameraTargetSwitcher.EnterCinematic"/>.
///   진입은 컷(도착 암전 직후), 복귀는 지시 구간 끝 <see cref="BossEncounterDirector.CameraReturnSeconds"/> 블렌드.
/// · 레터박스(상하 띠)·비네팅(런타임 Volume, CameraFeedback HP 비네팅(우선순위 100) 위) — 같은 가중치로 페이드.
/// · 웰즈 독백(지시 구간) · 스킵 위젯(Space 1초 홀드 → 버튼 외곽선이 채워진다, 채운 표는 유지) — 문구는 TutorialGuideTexts 표.
/// 붙는 곳: 디렉터 OnNetworkSpawn 이 런타임에 붙인다(씬·프리팹 무수정).
/// </summary>
[DisallowMultipleComponent]
public sealed class BossIntroPresentation : MonoBehaviour
{
    private const string MonologueId = "BOSS_INTRO_WellsCommand";
    // 스킵 위젯 모양은 타이틀 ESC 건너뛰기(TitleSkipHint)도 같이 쓴다 — internal(경석 10-26).
    internal const float SkipPillWidth = 300f;
    internal const float SkipPillHeight = 72f;
    private const int CanvasSortingOrder = 450;
    private const float VolumePriority = 110f;
    private const float CameraEnterBlendLate = 0.35f;   // 늦게 붙은 클라 — 암전이 아니므로 짧게 블렌드
    private const float MonologueFadeSeconds = 0.25f;

    // 어두운 알약(button_pill_neutral ≈ #1C1B19, 테두리 회색) 위에서 읽히는 색 — 크림 글자 · 금색 키 · 금색 채움 · 완료는 초록.
    internal static readonly Color SkipIdleColor = new Color(1f, 1f, 1f, 0.12f);
    private static readonly Color SkipFillColor = new Color(1f, 0.82f, 0.36f, 1f);
    private static readonly Color SkipDoneColor = new Color(0.55f, 0.95f, 0.6f, 1f);
    internal static readonly Color SkipTextColor = new Color(0.96f, 0.93f, 0.86f, 1f);
    // 키캡 — keyguide.png 의 Space 키 색(밝은 회색 몸통 · 아랫단 조금 어둡게 · 진한 회색 테두리·글씨).
    private static readonly Color32 KeycapFace = new Color32(222, 222, 222, 255);
    private static readonly Color32 KeycapBottom = new Color32(184, 184, 184, 255);
    private static readonly Color32 KeycapBorder = new Color32(48, 48, 48, 255);
    internal static readonly Color KeycapTextColor = new Color32(38, 38, 38, 255);
    internal const float SkipKeycapWidth = 104f;
    internal const float SkipKeycapHeight = 40f;

    private BossEncounterDirector _director;

    private CinemachineCamera _vcam;
    private bool _cameraActive;

    private Canvas _canvas;
    private RectTransform _topBar;
    private RectTransform _bottomBar;
    private CanvasGroup _monologueGroup;
    private TMP_Text _monologueText;
    private GameObject _skipRoot;
    private Image _skipFill;
    private TMP_Text _skipLabel;
    private TMP_Text _skipCount;
    private TMP_Text _skipKey;
    private bool _fontApplied;
    private bool _monologueResolved;   // 문구는 판 하나에 한 번만 찾는다(없으면 경고 1회 — 매 프레임 찍지 않게)

    private Volume _volume;
    private VolumeProfile _profile;
    private Vignette _vignette;

    // 연출 중 꺼 둔 HUD 캔버스(경석 10-23 — 스킵 위젯만 남긴다). 연출이 끝나면 이것만 다시 켠다.
    private readonly System.Collections.Generic.List<Canvas> _hiddenHud = new System.Collections.Generic.List<Canvas>();
    private float _hudRescanAt;
    private const float HudRescanSeconds = 0.5f;   // 연출 도중 새로 뜬 캔버스도 잡는다

    private int _impactFxSequence = -1;

    private float _overlayWeight;
    private float _monologueAlpha;
    private readonly BossIntroSkipHold _hold = new BossIntroSkipHold();
    private int _holdSequence = -1;

    private void Awake()
    {
        _director = GetComponent<BossEncounterDirector>();
    }

    private void OnDestroy()
    {
        RestoreHud();
        ExitCamera(0f);
        if (_vcam != null) Destroy(_vcam.gameObject);
        if (_canvas != null) Destroy(_canvas.gameObject);
        if (_volume != null) Destroy(_volume.gameObject);
        if (_profile != null) Destroy(_profile);
    }

    private void Update()
    {
        if (_director == null || !_director.IsSpawned)
        {
            Apply(false, false, false, Time.unscaledDeltaTime);
            ExitCamera(0f);
            RestoreHud();
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

        if (wantCinematic && !_cameraActive)
            EnterCamera(phase == BossEncounterPhase.Preparing && elapsed < 0.5d ? 0f : CameraEnterBlendLate);
        else if (!wantCinematic && _cameraActive)
            ExitCamera(_director.CameraReturnSeconds);
        if (_cameraActive) PlaceCamera();

        // 착지 연출(로컬 파티클) — 회차마다 Impact 에 들어서는 순간 한 번.
        if (active && phase == BossEncounterPhase.Impact && _impactFxSequence != _director.IntroSequence)
        {
            _impactFxSequence = _director.IntroSequence;
            SpawnImpactParticles();
        }

        bool showMonologue = active && phase == BossEncounterPhase.Dialogue && _director.ShowMonologue;
        // HUD — 연출 내내 끄고, 끝나는 순간(전투 시작·스킵·중단) 다시 켠다. 보스 HP바는 그때 1/4 에서 차오른다(BossHealthHUD).
        if (active) HideHud();
        else RestoreHud();
        // 스킵은 카메라 복귀 전까지만 — 복귀가 시작되면(자연 진행이든 스킵 직후든) 이미 넘어가는 중이라 버튼을 걷는다.
        bool skippable = active && !returning;
        TickSkip(skippable);
        Apply(wantCinematic, showMonologue, skippable, Time.unscaledDeltaTime);
    }

    // ── 스킵(로컬 입력 → 서버 표) ───────────────────────────────────────────

    private void TickSkip(bool active)
    {
        int sequence = _director.IntroSequence;
        if (sequence != _holdSequence)
        {
            _holdSequence = sequence;
            _hold.Reset();
        }
        if (!active) return;

        GameMenuController menu = GameMenuController.Instance;
        bool menuOpen = menu != null && menu.IsOpen;
        Keyboard keyboard = Keyboard.current;
        // Space = Dash 바인딩이지만 연출 잠금 중엔 대시가 막혀 있고, 잠금이 풀린 뒤에는 새로 눌러야 대시가 나간다(PLAN 정정 9 — Play 회귀 확인 항목).
        bool pressed = !menuOpen && keyboard != null && keyboard.spaceKey.isPressed;

        if (_hold.Tick(pressed, Time.unscaledDeltaTime, _director.SkipHoldSeconds))
            _director.RequestSkipVote(sequence);
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

    // ── HUD 숨김 ────────────────────────────────────────────────────────────
    // 화면 UI(Screen Space 루트 캔버스)를 Canvas.enabled 로 끈다 — 오브젝트·스크립트는 살아 있어 바인딩·갱신은 그대로 돈다.
    // 월드 공간(머리 위 체력바 등)은 건드리지 않는다. 보스방 도착 암전은 OnGUI(BossTeleportManager)라 캔버스가 아니어서 영향 없다.
    // 남기는 것: 이 연출 오버레이(스킵 위젯) · ESC 게임 메뉴 · 접속 오버레이 · 로딩 화면.

    private void HideHud()
    {
        if (Time.unscaledTime < _hudRescanAt) return;
        _hudRescanAt = Time.unscaledTime + HudRescanSeconds;

        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas c = canvases[i];
            if (c == null || !c.enabled || !c.isRootCanvas || c.renderMode == RenderMode.WorldSpace) continue;
            if (c == _canvas || KeepsDuringIntro(c)) continue;
            c.enabled = false;
            _hiddenHud.Add(c);
        }
    }

    private void RestoreHud()
    {
        if (_hiddenHud.Count == 0) return;
        for (int i = 0; i < _hiddenHud.Count; i++)
            if (_hiddenHud[i] != null) _hiddenHud[i].enabled = true;   // 파괴된 캔버스는 건너뛴다(?. 는 파괴된 Unity 객체를 못 거른다)
        _hiddenHud.Clear();
        _hudRescanAt = 0f;
    }

    private static bool KeepsDuringIntro(Canvas canvas)
    {
        GameObject go = canvas.gameObject;
        return Has<GameMenuController>(go) || Has<DemoConnectionOverlay>(go) || Has<NetworkLoadingScreenView>(go);
    }

    // 컨트롤러가 캔버스 위(부모)에 붙은 구성도 있다 — 양쪽 다 본다.
    private static bool Has<T>(GameObject go) where T : Component =>
        go.GetComponentInChildren<T>(true) != null || go.GetComponentInParent<T>(true) != null;

    // ── 카메라 ─────────────────────────────────────────────────────────────

    private void EnterCamera(float blendSeconds)
    {
        CameraTargetSwitcher switcher = CameraTargetSwitcher.Active;
        if (switcher == null || _director.LandingPoint == null) return;

        if (_vcam == null)
        {
            var go = new GameObject("BossIntroCamera");
            _vcam = go.AddComponent<CinemachineCamera>();
            _vcam.Priority = 0;
        }

        LensSettings lens = _vcam.Lens;
        lens.FieldOfView = _director.CameraFov;
        _vcam.Lens = lens;
        PlaceCamera();
        switcher.EnterCinematic(_vcam, blendSeconds);
        _cameraActive = true;
    }

    private void ExitCamera(float blendSeconds)
    {
        if (!_cameraActive) return;
        _cameraActive = false;
        CameraTargetSwitcher switcher = CameraTargetSwitcher.Active;
        if (switcher != null && _vcam != null) switcher.ExitCinematic(_vcam, blendSeconds);
    }

    // 구도 — 대열 뒤(화면 아래)에서 착지점 쪽(위)을 낮은 각도로 본다. 파티가 카메라에서 멀어지며 보스에게 걸어간다.
    private void PlaceCamera()
    {
        Transform landing = _director.LandingPoint;
        if (_vcam == null || landing == null) return;

        Vector3 south = ScreenSouth();
        Vector3 north = -south;
        Vector3 focus = landing.position + south * _director.CameraFocusSouth + Vector3.up * _director.CameraFocusHeight;
        Quaternion rotation = Quaternion.LookRotation(north, Vector3.up) * Quaternion.Euler(_director.CameraPitch, 0f, 0f);
        _vcam.transform.SetPositionAndRotation(focus - rotation * Vector3.forward * _director.CameraDistance, rotation);
    }

    private static Vector3 ScreenSouth() => GameplayScreenSouth();

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
        float fade = _director != null ? _director.OverlayFadeSeconds : 0.4f;
        _overlayWeight = Mathf.MoveTowards(_overlayWeight, cinematic ? 1f : 0f, fade > 0f ? deltaTime / fade : 1f);
        _monologueAlpha = Mathf.MoveTowards(_monologueAlpha, monologue ? 1f : 0f, deltaTime / MonologueFadeSeconds);

        bool anything = _overlayWeight > 0f || _monologueAlpha > 0f || skipVisible;
        if (!anything)
        {
            if (_canvas != null && _canvas.gameObject.activeSelf) _canvas.gameObject.SetActive(false);
            if (_volume != null) _volume.weight = 0f;
            return;
        }

        EnsureOverlay();
        if (!_canvas.gameObject.activeSelf) _canvas.gameObject.SetActive(true);
        ApplyFont();

        float bar = (_director != null ? _director.LetterboxHeight : 0.04f) * _overlayWeight;
        _topBar.anchorMin = new Vector2(0f, 1f - bar);
        _bottomBar.anchorMax = new Vector2(1f, bar);

        EnsureVolume();
        if (_volume != null)
        {
            _vignette.intensity.value = _director != null ? _director.VignetteIntensity : 0.35f;
            _vignette.smoothness.value = _director != null ? _director.VignetteSmoothness : 0.42f;
            _volume.weight = _overlayWeight;
        }

        _monologueGroup.alpha = _monologueAlpha;
        if (monologue && !_monologueResolved)
        {
            string line = ResolveText(MonologueId);
            _monologueResolved = line != null;
            _monologueText.text = line ?? string.Empty;
        }

        if (_skipRoot.activeSelf != skipVisible) _skipRoot.SetActive(skipVisible);
        if (skipVisible)
        {
            float holdSeconds = _director != null ? _director.SkipHoldSeconds : 1f;
            _skipFill.fillAmount = _hold.Progress(holdSeconds);
            _skipFill.color = _hold.Voted ? SkipDoneColor : SkipFillColor;
            _skipLabel.text = UiStrings.BossIntroSkip.Text;   // 현재 언어(Loc) — 시트에 행이 아직 없으면 ko 원문
            int total = _director != null ? _director.EligibleCount : 0;
            _skipCount.text = total > 0 ? $"{(_director != null ? _director.SkipVoteCount : 0)}/{total}" : string.Empty;
        }
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
        if (_fontApplied) return;
        TMP_FontAsset font = _director != null ? _director.IntroFont : null;   // SeoulAlrim SDF(디렉터 프리팹)
        if (font == null)
        {
            TutorialGuideUI ui = TutorialGuideUI.Instance;
            font = ui != null ? ui.SubtitleFont : null;
        }
        if (font == null) return;   // 다음 프레임에 다시(안내 UI 가 늦게 뜰 수 있다)
        _monologueText.font = font;
        _skipLabel.font = font;
        _skipCount.font = font;
        _skipKey.font = font;
        _fontApplied = true;
    }

    private void EnsureVolume()
    {
        if (_volume != null) return;
        CameraTargetSwitcher switcher = CameraTargetSwitcher.Active;
        Camera cam = switcher != null && switcher.GameplayCamera != null ? switcher.GameplayCamera : Camera.main;

        var go = new GameObject("BossIntroVignette");
        if (cam != null) go.layer = cam.gameObject.layer;   // 카메라 볼륨 레이어 마스크 안에 두려고(CameraFeedback 볼륨과 같은 레이어)
        _volume = go.AddComponent<Volume>();
        _volume.isGlobal = true;
        _volume.priority = VolumePriority;
        _volume.weight = 0f;
        _profile = ScriptableObject.CreateInstance<VolumeProfile>();
        _profile.name = "BossIntro Runtime Volume";
        _profile.hideFlags = HideFlags.DontSave;
        _volume.profile = _profile;
        _vignette = _profile.Add<Vignette>();
        _vignette.intensity.overrideState = true;
        _vignette.smoothness.overrideState = true;
    }

    private void EnsureOverlay()
    {
        if (_canvas != null) return;

        var root = new GameObject("BossIntroOverlay", typeof(RectTransform));
        _canvas = root.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = CanvasSortingOrder;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _topBar = Bar(root.transform, "LetterboxTop", new Vector2(0f, 1f), new Vector2(1f, 1f));
        _bottomBar = Bar(root.transform, "LetterboxBottom", new Vector2(0f, 0f), new Vector2(1f, 0f));

        // 독백 — 아래 띠 위, 가운데.
        var mono = new GameObject("Monologue", typeof(RectTransform), typeof(CanvasGroup));
        mono.transform.SetParent(root.transform, false);
        var monoRect = (RectTransform)mono.transform;
        monoRect.anchorMin = new Vector2(0.5f, 0f);
        monoRect.anchorMax = new Vector2(0.5f, 0f);
        monoRect.pivot = new Vector2(0.5f, 0f);
        monoRect.anchoredPosition = new Vector2(0f, 120f);
        monoRect.sizeDelta = new Vector2(1400f, 80f);
        _monologueGroup = mono.GetComponent<CanvasGroup>();
        _monologueGroup.alpha = 0f;
        _monologueText = Label(mono.transform, "Text", 44f, TextAlignmentOptions.Center);
        Stretch((RectTransform)_monologueText.transform);

        // 스킵 버튼 — 오른쪽 아래. [n/m] ( SPACE  건너뛰기 ) — 알약 배경(button_pill_neutral) 둘레를 홀드 진행만큼 금색 외곽선이 채운다.
        // HUD 는 연출 중 꺼져 있어 겹치지 않는다(HideHud). 문구 = Strings 시트 UI.BossIntro.Skip(ko 건너뛰기 / en Skip).
        _skipRoot = new GameObject("SkipButton", typeof(RectTransform));
        _skipRoot.transform.SetParent(root.transform, false);
        var skipRect = (RectTransform)_skipRoot.transform;
        skipRect.anchorMin = new Vector2(1f, 0f);
        skipRect.anchorMax = new Vector2(1f, 0f);
        skipRect.pivot = new Vector2(1f, 0f);
        skipRect.anchoredPosition = new Vector2(-56f, 72f);
        skipRect.sizeDelta = new Vector2(SkipPillWidth + 96f, SkipPillHeight);

        var pill = new GameObject("Pill", typeof(RectTransform));
        pill.transform.SetParent(_skipRoot.transform, false);
        var pillRect = (RectTransform)pill.transform;
        pillRect.anchorMin = new Vector2(1f, 0.5f);
        pillRect.anchorMax = new Vector2(1f, 0.5f);
        pillRect.pivot = new Vector2(1f, 0.5f);
        pillRect.sizeDelta = new Vector2(SkipPillWidth, SkipPillHeight);

        Sprite background = _director != null ? _director.SkipButtonSprite : null;
        if (background != null)
        {
            Image bg = NewImage(pill.transform, "Background", background, Color.white);
            Stretch(bg.rectTransform);
            bg.type = Image.Type.Sliced;
            // 원본 276px 높이·테두리 130px(200 PPU) — 72 높이에 양 끝 반원이 맞게 테두리를 절반으로.
            bg.pixelsPerUnitMultiplier = 2f;
        }

        Sprite outline = RoundedOutlineSprite((int)SkipPillWidth, (int)SkipPillHeight, SkipPillHeight * 0.5f - 1f, 4f);
        Image idle = NewImage(pill.transform, "Outline", outline, SkipIdleColor);
        Stretch(idle.rectTransform);
        _skipFill = NewImage(pill.transform, "OutlineFill", outline, SkipFillColor);
        Stretch(_skipFill.rectTransform);
        _skipFill.type = Image.Type.Filled;
        _skipFill.fillMethod = Image.FillMethod.Radial360;
        _skipFill.fillOrigin = (int)Image.Origin360.Top;
        _skipFill.fillClockwise = true;
        _skipFill.fillAmount = 0f;

        // 키캡(왼쪽) — HUD 키 안내(Assets/50.Art/UI/HUD/keyguide.png)의 "Space" 와 같은 모양: 밝은 회색 몸통 · 진한 테두리 · 진한 글씨.
        // 키 안내 그림은 한 장으로 구워져 있어 잘라 쓸 수 없어 같은 색·비율로 그린다(경석 10-23 — 글자만으로는 누를 키로 안 읽힌다).
        Image keycap = NewImage(pill.transform, "Keycap", KeycapSprite((int)SkipKeycapWidth, (int)SkipKeycapHeight), Color.white);
        RectTransform keycapRect = keycap.rectTransform;
        keycapRect.anchorMin = new Vector2(0f, 0.5f);
        keycapRect.anchorMax = new Vector2(0f, 0.5f);
        keycapRect.pivot = new Vector2(0f, 0.5f);
        keycapRect.sizeDelta = new Vector2(SkipKeycapWidth, SkipKeycapHeight);
        keycapRect.anchoredPosition = new Vector2(22f, 1f);
        _skipKey = Label(keycap.transform, "Key", 21f, TextAlignmentOptions.Center);
        Stretch((RectTransform)_skipKey.transform);
        ((RectTransform)_skipKey.transform).offsetMin = new Vector2(0f, 3f);   // 키캡 아랫단(그림자) 위 가운데
        _skipKey.text = "Space";
        _skipKey.color = KeycapTextColor;
        _skipKey.fontStyle = FontStyles.Bold;

        _skipLabel = Label(pill.transform, "Label", 28f, TextAlignmentOptions.Right);
        var labelRect = (RectTransform)_skipLabel.transform;
        Stretch(labelRect);
        labelRect.offsetMax = new Vector2(-30f, 0f);
        _skipLabel.color = SkipTextColor;

        // 동의 수 — 알약 밖(장면 위)이라 검은 외곽선으로 대비.
        _skipCount = Label(_skipRoot.transform, "Count", 26f, TextAlignmentOptions.Right);
        var countRect = (RectTransform)_skipCount.transform;
        countRect.anchorMin = new Vector2(0f, 0f);
        countRect.anchorMax = new Vector2(1f, 1f);
        countRect.offsetMin = Vector2.zero;
        countRect.offsetMax = new Vector2(-SkipPillWidth - 14f, 0f);
        _skipCount.color = SkipTextColor;
        _skipCount.outlineWidth = 0.25f;
        _skipCount.outlineColor = new Color32(0, 0, 0, 220);

        _skipRoot.SetActive(false);
    }

    private static RectTransform Bar(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
    {
        Image image = NewImage(parent, name, null, Color.black);
        RectTransform rect = image.rectTransform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return rect;
    }

    private static Image NewImage(Transform parent, string name, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static TMP_Text Label(Transform parent, string name, float size, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.text = string.Empty;
        return text;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    // 키캡 — 둥근 사각형 몸통(아랫단 25% 어둡게 = 입체감) + 진한 테두리 3px, 안티에일리어싱.
    internal static Sprite KeycapSprite(int width, int height)
    {
        const float radius = 9f;
        const float border = 3f;
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
        var pixels = new Color32[width * height];
        float hw = width * 0.5f, hh = height * 0.5f;
        float bottomBand = height * 0.25f;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float px = Mathf.Abs(x + 0.5f - hw) - (hw - radius - 0.5f);
            float py = Mathf.Abs(y + 0.5f - hh) - (hh - radius - 0.5f);
            float d = new Vector2(Mathf.Max(px, 0f), Mathf.Max(py, 0f)).magnitude + Mathf.Min(Mathf.Max(px, py), 0f) - radius;   // 안 = 음수
            float coverage = Mathf.Clamp01(0.5f - d);
            if (coverage <= 0f) { pixels[y * width + x] = new Color32(0, 0, 0, 0); continue; }

            Color32 face = y < bottomBand ? KeycapBottom : KeycapFace;   // 텍스처 y 0 = 아래
            float inner = Mathf.Clamp01(-d - border + 0.5f);               // 테두리 안쪽 비율
            Color32 c = Color32.Lerp(KeycapBorder, face, inner);
            c.a = (byte)(coverage * 255f);
            pixels[y * width + x] = c;
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
    }

    // 키 아이콘 외곽선 — 둥근 사각형 테두리(안티에일리어싱). 런타임 생성이라 아트 에셋이 필요 없다.
    internal static Sprite RoundedOutlineSprite(int width, int height, float radius, float thickness)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
        var pixels = new Color32[width * height];
        float hw = width * 0.5f, hh = height * 0.5f;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            // 둥근 사각형 부호 거리(안 = 음수).
            float px = Mathf.Abs(x + 0.5f - hw) - (hw - radius - 1f);
            float py = Mathf.Abs(y + 0.5f - hh) - (hh - radius - 1f);
            float outside = new Vector2(Mathf.Max(px, 0f), Mathf.Max(py, 0f)).magnitude + Mathf.Min(Mathf.Max(px, py), 0f) - radius;
            float edge = Mathf.Abs(outside + thickness * 0.5f) - thickness * 0.5f;
            byte a = (byte)(Mathf.Clamp01(0.5f - edge) * 255f);
            pixels[y * width + x] = new Color32(255, 255, 255, a);
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
    }
}
