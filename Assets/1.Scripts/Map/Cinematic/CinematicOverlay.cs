using System;
using System.Collections.Generic;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// 연출 공용 화면 부품(로컬 표시) — 레터박스 · 비네팅 · 스킵 위젯(Space 홀드 → 외곽선 채움) · HUD 끄기 · 연출 카메라.
/// 보스 등장 연출(<see cref="BossIntroPresentation"/>)에서 뽑아 냈다(PLAN-zone4-intro-cinematic — 4구역 소개 연출이 같은 모양·같은 스킵 규칙을 쓴다).
/// 판정·진행은 서버가 한다. 이 클래스는 호출자가 넘긴 값대로 그리기만 한다. 오브젝트는 런타임 생성(씬·프리팹 무수정).
/// </summary>
public sealed class CinematicOverlay
{
    // 스킵 위젯 모양은 타이틀 ESC 건너뛰기(TitleSkipHint)도 같이 쓴다(BossIntroPresentation 의 같은 이름이 여기로 넘긴다).
    public const float SkipPillWidth = 300f;
    public const float SkipPillHeight = 72f;
    public const float SkipKeycapWidth = 104f;
    public const float SkipKeycapHeight = 40f;
    private const int CanvasSortingOrder = 450;
    private const float VolumePriority = 110f;
    private const float HudRescanSeconds = 0.5f;   // 연출 도중 새로 뜬 캔버스도 잡는다

    // 어두운 알약(button_pill_neutral ≈ #1C1B19, 테두리 회색) 위에서 읽히는 색 — 크림 글자 · 금색 키 · 금색 채움 · 완료는 초록.
    public static readonly Color SkipIdleColor = new Color(1f, 1f, 1f, 0.12f);
    private static readonly Color SkipFillColor = new Color(1f, 0.82f, 0.36f, 1f);
    private static readonly Color SkipDoneColor = new Color(0.55f, 0.95f, 0.6f, 1f);
    public static readonly Color SkipTextColor = new Color(0.96f, 0.93f, 0.86f, 1f);
    // 키캡 — keyguide.png 의 Space 키 색(밝은 회색 몸통 · 아랫단 조금 어둡게 · 진한 회색 테두리·글씨).
    private static readonly Color32 KeycapFace = new Color32(222, 222, 222, 255);
    private static readonly Color32 KeycapBottom = new Color32(184, 184, 184, 255);
    private static readonly Color32 KeycapBorder = new Color32(48, 48, 48, 255);
    public static readonly Color KeycapTextColor = new Color32(38, 38, 38, 255);

    private readonly string _name;
    private readonly Sprite _skipButtonSprite;

    private Canvas _canvas;
    private RectTransform _topBar;
    private RectTransform _bottomBar;
    private GameObject _skipRoot;
    private Image _skipFill;
    private TMP_Text _skipLabel;
    private TMP_Text _skipCount;
    private TMP_Text _skipKey;
    private readonly List<TMP_Text> _fontTargets = new List<TMP_Text>();
    private bool _fontApplied;

    private Volume _volume;
    private VolumeProfile _profile;
    private Vignette _vignette;

    private readonly List<Canvas> _hiddenHud = new List<Canvas>();
    private float _hudRescanAt;

    private CinemachineCamera _vcam;
    private bool _cameraActive;

    private float _overlayWeight;
    private readonly BossIntroSkipHold _hold = new BossIntroSkipHold();
    private int _holdSequence = int.MinValue;

    /// <param name="name">런타임 오브젝트 이름 앞머리(하이어라키에서 구분용).</param>
    /// <param name="skipButtonSprite">스킵 알약 배경(button_pill_neutral, 9-슬라이스). null 이면 외곽선만.</param>
    public CinematicOverlay(string name, Sprite skipButtonSprite)
    {
        _name = name;
        _skipButtonSprite = skipButtonSprite;
    }

    /// <summary>연출 전용 요소를 더 얹을 자리(보스 독백 등). 처음 부르면 오버레이를 만든다.</summary>
    public RectTransform Root
    {
        get
        {
            EnsureOverlay();
            return (RectTransform)_canvas.transform;
        }
    }

    public bool IsCameraActive => _cameraActive;
    public CinemachineCamera VirtualCamera => _vcam;

    /// <summary>오버레이 캔버스 자신(HUD 끄기에서 빼야 하는 것).</summary>
    public bool Owns(Canvas canvas) => canvas != null && canvas == _canvas;

    // ── 스킵(로컬 입력 → 서버 표) ───────────────────────────────────────────

    /// <summary>
    /// Space 홀드를 민다. 홀드를 채운 그 프레임에만 true(표 1회 전송). 회차가 바뀌면 게이지를 비운다.
    /// 게임 메뉴가 열려 있으면 누름으로 치지 않는다.
    /// </summary>
    public bool TickSkip(bool active, int sequence, float holdSeconds)
    {
        if (sequence != _holdSequence)
        {
            _holdSequence = sequence;
            _hold.Reset();
        }
        if (!active) return false;

        GameMenuController menu = GameMenuController.Instance;
        bool menuOpen = menu != null && menu.IsOpen;
        Keyboard keyboard = Keyboard.current;
        // Space = Dash 바인딩이지만 연출 잠금 중엔 대시가 막혀 있고, 잠금이 풀린 뒤에는 새로 눌러야 대시가 나간다(PLAN-boss-encounter-cutscene 정정 9).
        bool pressed = !menuOpen && keyboard != null && keyboard.spaceKey.isPressed;
        return _hold.Tick(pressed, Time.unscaledDeltaTime, holdSeconds);
    }

    // ── 화면 효과·스킵 위젯 ───────────────────────────────────────────────

    /// <summary>
    /// 레터박스·비네팅(<paramref name="cinematic"/> 쪽으로 페이드)과 스킵 위젯을 그린다.
    /// <paramref name="keepCanvas"/> = 호출자가 <see cref="Root"/> 에 얹은 것이 보이는 중(그 동안은 캔버스를 끄지 않는다).
    /// </summary>
    public void Apply(bool cinematic, bool skipVisible, bool keepCanvas, float letterboxHeight, float vignetteIntensity,
                      float vignetteSmoothness, float fadeSeconds, float holdSeconds, int votes, int voters, float deltaTime)
    {
        _overlayWeight = Mathf.MoveTowards(_overlayWeight, cinematic ? 1f : 0f, fadeSeconds > 0f ? deltaTime / fadeSeconds : 1f);

        bool anything = _overlayWeight > 0f || skipVisible || keepCanvas;
        if (!anything)
        {
            if (_canvas != null && _canvas.gameObject.activeSelf) _canvas.gameObject.SetActive(false);
            if (_volume != null) _volume.weight = 0f;
            return;
        }

        EnsureOverlay();
        if (!_canvas.gameObject.activeSelf) _canvas.gameObject.SetActive(true);

        float bar = letterboxHeight * _overlayWeight;
        _topBar.anchorMin = new Vector2(0f, 1f - bar);
        _bottomBar.anchorMax = new Vector2(1f, bar);

        EnsureVolume();
        if (_volume != null)
        {
            _vignette.intensity.value = vignetteIntensity;
            _vignette.smoothness.value = vignetteSmoothness;
            _volume.weight = _overlayWeight;
        }

        if (_skipRoot.activeSelf != skipVisible) _skipRoot.SetActive(skipVisible);
        if (skipVisible)
        {
            _skipFill.fillAmount = _hold.Progress(holdSeconds);
            _skipFill.color = _hold.Voted ? SkipDoneColor : SkipFillColor;
            _skipLabel.text = UiStrings.BossIntroSkip.Text;   // 현재 언어(Loc) — 시트에 행이 아직 없으면 ko 원문
            _skipCount.text = voters > 0 ? $"{votes}/{voters}" : string.Empty;
        }
    }

    /// <summary>글꼴(SeoulAlrim SDF)을 위젯 글자에 한 번 입힌다. null 이면 다음에 다시. <paramref name="extra"/> = 호출자 글자(독백 등).</summary>
    public void ApplyFont(TMP_FontAsset font, params TMP_Text[] extra)
    {
        if (_fontApplied || font == null || _canvas == null) return;
        foreach (TMP_Text t in _fontTargets) if (t != null) t.font = font;
        if (extra != null) foreach (TMP_Text t in extra) if (t != null) t.font = font;
        _fontApplied = true;
    }

    // ── HUD 숨김 ────────────────────────────────────────────────────────────
    // 화면 UI(Screen Space 루트 캔버스)를 Canvas.enabled 로 끈다 — 오브젝트·스크립트는 살아 있어 바인딩·갱신은 그대로 돈다.
    // 월드 공간(머리 위 체력바 등)은 건드리지 않는다. 보스방 도착 암전은 OnGUI(BossTeleportManager)라 캔버스가 아니어서 영향 없다.
    // 늘 남기는 것: 이 오버레이 · ESC 게임 메뉴 · 접속 오버레이 · 로딩 화면. 호출자가 더 남길 것은 keep 으로.

    public void HideHud(Predicate<Canvas> keep = null)
    {
        if (Time.unscaledTime < _hudRescanAt) return;
        _hudRescanAt = Time.unscaledTime + HudRescanSeconds;

        Canvas[] canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas c = canvases[i];
            if (c == null || !c.enabled || !c.isRootCanvas || c.renderMode == RenderMode.WorldSpace) continue;
            if (c == _canvas || AlwaysKept(c) || (keep != null && keep(c))) continue;
            c.enabled = false;
            _hiddenHud.Add(c);
        }
    }

    public void RestoreHud()
    {
        if (_hiddenHud.Count == 0) return;
        for (int i = 0; i < _hiddenHud.Count; i++)
            if (_hiddenHud[i] != null) _hiddenHud[i].enabled = true;   // 파괴된 캔버스는 건너뛴다(?. 는 파괴된 Unity 객체를 못 거른다)
        _hiddenHud.Clear();
        _hudRescanAt = 0f;
    }

    private static bool AlwaysKept(Canvas canvas)
    {
        GameObject go = canvas.gameObject;
        return Has<GameMenuController>(go) || Has<DemoConnectionOverlay>(go) || Has<NetworkLoadingScreenView>(go);
    }

    // 컨트롤러가 캔버스 위(부모)에 붙은 구성도 있다 — 양쪽 다 본다.
    public static bool Has<T>(GameObject go) where T : Component =>
        go.GetComponentInChildren<T>(true) != null || go.GetComponentInParent<T>(true) != null;

    // ── 카메라 ─────────────────────────────────────────────────────────────

    /// <summary>연출 vcam 을 만들고(처음 한 번) 그 자리에 둔 채 게임 카메라에서 블렌드해 들어간다. 스위처가 없으면 false.</summary>
    public bool EnterCamera(Vector3 position, Quaternion rotation, float fov, float blendSeconds)
    {
        CameraTargetSwitcher switcher = CameraTargetSwitcher.Active;
        if (switcher == null) return false;

        if (_vcam == null)
        {
            var go = new GameObject(_name + "Camera");
            _vcam = go.AddComponent<CinemachineCamera>();
            _vcam.Priority = 0;
        }

        PlaceCamera(position, rotation, fov);
        switcher.EnterCinematic(_vcam, blendSeconds);
        _cameraActive = true;
        return true;
    }

    public void PlaceCamera(Vector3 position, Quaternion rotation, float fov)
    {
        if (_vcam == null) return;
        LensSettings lens = _vcam.Lens;
        lens.FieldOfView = fov;
        _vcam.Lens = lens;
        _vcam.transform.SetPositionAndRotation(position, rotation);
    }

    public void ExitCamera(float blendSeconds)
    {
        if (!_cameraActive) return;
        _cameraActive = false;
        CameraTargetSwitcher switcher = CameraTargetSwitcher.Active;
        if (switcher != null && _vcam != null) switcher.ExitCinematic(_vcam, blendSeconds);
    }

    // ── 디밍 기준점 ────────────────────────────────────────────────────────
    // 층 디밍(FogManager — 플레이어 높이·시야 밖을 어둡게)의 기준점은 평소 카메라 추적 대상(플레이어)이다. 연출 카메라가 플레이어에게서
    // 먼 곳을 비추면 화면이 통째로 어둡게 깔린다(경석 10-27 4구역 소개). 연출 동안만 기준점을 카메라가 바라보는 점으로 옮겨
    // 비추는 곳이 평소 플레이어 주변처럼 보이게 한다. 프로필(에셋) 값은 건드리지 않는다 — Play 중 에셋을 바꾸면 남는다.

    private Transform _dimFocus;
    private FogManager _dimFog;
    private Transform _dimPrevOverride;
    private bool _dimOverriding;

    /// <summary>디밍 기준점을 <paramref name="focus"/> 로 옮긴다. null 이면 원래(연출 전 override, 보통 비어 있음 = 플레이어)로 되돌린다.</summary>
    public void SetDimFocus(Vector3? focus)
    {
        if (!focus.HasValue)
        {
            if (!_dimOverriding) return;
            if (_dimFog != null && _dimFog.dimPlayerOverride == _dimFocus) _dimFog.dimPlayerOverride = _dimPrevOverride;
            _dimOverriding = false;
            return;
        }

        FogManager fog = FogManager.Active;
        if (fog == null) return;
        if (_dimFocus == null) _dimFocus = new GameObject(_name + "DimFocus").transform;
        _dimFocus.position = focus.Value;
        if (_dimOverriding && _dimFog == fog) return;
        _dimFog = fog;
        _dimPrevOverride = fog.dimPlayerOverride;
        fog.dimPlayerOverride = _dimFocus;
        _dimOverriding = true;
    }

    /// <summary>디밍 세기를 줄인다(0 = 평소, 1 = 안 누름). 연출 동안 올렸다가 복귀하며 0 으로 — 0 이 되면 손을 뗀다.</summary>
    public void SetDimSuppress(float amount)
    {
        amount = Mathf.Clamp01(amount);
        FogManager fog = amount > 0f ? FogManager.Active : null;
        if (_dimSuppressFog != null && _dimSuppressFog != fog) _dimSuppressFog.dimSuppress = 0f;   // 끝났거나 매니저가 바뀌었다 — 내가 올린 것만 원래대로
        if (fog != null) fog.dimSuppress = amount;
        _dimSuppressFog = fog;
    }

    private FogManager _dimSuppressFog;

    // ── 정리 ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        SetDimSuppress(0f);
        SetDimFocus(null);
        if (_dimFocus != null) UnityEngine.Object.Destroy(_dimFocus.gameObject);
        RestoreHud();
        ExitCamera(0f);
        if (_vcam != null) UnityEngine.Object.Destroy(_vcam.gameObject);
        if (_canvas != null) UnityEngine.Object.Destroy(_canvas.gameObject);
        if (_volume != null) UnityEngine.Object.Destroy(_volume.gameObject);
        if (_profile != null) UnityEngine.Object.Destroy(_profile);
    }

    // ── 만들기 ─────────────────────────────────────────────────────────────

    private void EnsureVolume()
    {
        if (_volume != null) return;
        CameraTargetSwitcher switcher = CameraTargetSwitcher.Active;
        UnityEngine.Camera cam = switcher != null && switcher.GameplayCamera != null ? switcher.GameplayCamera : UnityEngine.Camera.main;

        var go = new GameObject(_name + "Vignette");
        if (cam != null) go.layer = cam.gameObject.layer;   // 카메라 볼륨 레이어 마스크 안에 두려고(CameraFeedback 볼륨과 같은 레이어)
        _volume = go.AddComponent<Volume>();
        _volume.isGlobal = true;
        _volume.priority = VolumePriority;
        _volume.weight = 0f;
        _profile = ScriptableObject.CreateInstance<VolumeProfile>();
        _profile.name = _name + " Runtime Volume";
        _profile.hideFlags = HideFlags.DontSave;
        _volume.profile = _profile;
        _vignette = _profile.Add<Vignette>();
        _vignette.intensity.overrideState = true;
        _vignette.smoothness.overrideState = true;
    }

    private void EnsureOverlay()
    {
        if (_canvas != null) return;

        var root = new GameObject(_name + "Overlay", typeof(RectTransform));
        _canvas = root.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = CanvasSortingOrder;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _topBar = Bar(root.transform, "LetterboxTop", new Vector2(0f, 1f), new Vector2(1f, 1f));
        _bottomBar = Bar(root.transform, "LetterboxBottom", new Vector2(0f, 0f), new Vector2(1f, 0f));

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

        if (_skipButtonSprite != null)
        {
            Image bg = NewImage(pill.transform, "Background", _skipButtonSprite, Color.white);
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

        _fontTargets.Add(_skipLabel);
        _fontTargets.Add(_skipCount);
        _fontTargets.Add(_skipKey);
        _skipRoot.SetActive(false);
    }

    // ── UI 헬퍼(보스 독백·무전 캡션도 쓴다) ─────────────────────────────────

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

    public static Image NewImage(Transform parent, string name, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    public static TMP_Text Label(Transform parent, string name, float size, TextAlignmentOptions alignment)
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

    public static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    // 키캡 — 둥근 사각형 몸통(아랫단 25% 어둡게 = 입체감) + 진한 테두리 3px, 안티에일리어싱.
    public static Sprite KeycapSprite(int width, int height)
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
    public static Sprite RoundedOutlineSprite(int width, int height, float radius, float thickness)
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
