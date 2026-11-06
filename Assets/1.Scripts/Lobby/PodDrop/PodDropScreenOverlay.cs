using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 포드 강하 연출의 <b>로컬 화면</b>(할 일 100) — 위·아래 레터박스, 전체 검은 화면(페이드), 연출 동안 화면 UI 숨김.
/// 값은 호출자(<see cref="LobbyPodLaunch"/>·<see cref="MapPodArrival"/>)가 매 프레임 시간표에서 넣는다. 런타임에 붙고 캔버스는 이 오브젝트 아래에 만든다 —
/// 로비 씬이 언로드되면 같이 사라지고, 그때 숨겼던 UI 를 되켠다.
/// 캔버스 = <see cref="Prefab"/>(<see cref="PodDropSettings"/> 의 overlayPrefab, 할 일 230)을 복제한다. 비었거나 자식 이름이 안 맞으면 코드로 같은 구성을 만든다.
/// 코드는 띠의 높이 anchor(위 띠 anchorMin.y·아래 띠 anchorMax.y)·검은 화면 알파·루트 정렬 순서만 바꾼다 — 색·스프라이트·자식 장식은 프리팹 값 그대로.
/// 정렬 = 로딩 화면(1000)·ESC 게임 메뉴(900) 아래, HUD(0~200) 위 — 암전 뒤 로딩 화면이 이 검은 화면 위에 뜬다(프리팹 값과 무관하게 500 고정).
/// UI 숨김 규칙은 보스 등장 연출(<see cref="BossIntroPresentation"/>)과 같다(Screen Space 루트 캔버스를 Canvas.enabled 로 끈다).
/// </summary>
[DisallowMultipleComponent]
public sealed class PodDropScreenOverlay : MonoBehaviour
{
    public const int CanvasSortingOrder = 500;
    public const string RootName = "PodDropOverlay";
    public const string TopBarName = "LetterboxTop";
    public const string BottomBarName = "LetterboxBottom";
    public const string FadeName = "Fade";

    private const string Tag = "[PodDropScreenOverlay]";

    private Canvas _canvas;
    private RectTransform _topBar;
    private RectTransform _bottomBar;
    private Image _black;

    private readonly List<Canvas> _hiddenHud = new List<Canvas>();
    private float _hudRescanAt;

    /// <summary>
    /// 새로 뜬 화면 UI 를 다시 찾는 간격(초). 0 = 매 프레임. 호출자가 <see cref="PodDropSettings"/>(로비·맵 단계 hudRescanSeconds) 값을 넣는다.
    /// </summary>
    public float HudRescanInterval { get; set; }

    /// <summary>
    /// 복제할 오버레이 프리팹(<see cref="PodDropSettings.OverlayPrefab"/>). 첫 <see cref="Apply"/> 전에 넣는다 — 그 뒤 바꿔도 이미 만든 캔버스는 그대로.
    /// 루트에 Canvas, 직계 자식 <see cref="TopBarName"/>·<see cref="BottomBarName"/>(RectTransform)·<see cref="FadeName"/>(Image)가 있어야 한다.
    /// </summary>
    public GameObject Prefab { get; set; }

    /// <summary>
    /// 이번 프레임 화면. <paramref name="letterbox"/> = 위·아래 띠 각각의 화면 높이 비율, <paramref name="fade"/> = 검은 화면 알파.
    /// <paramref name="scanHud"/> = 새로 뜬 화면 UI 도 찾아 끈다(false 면 이미 끈 것만 꺼 둔다 — 암전 뒤 로딩 씬 캔버스를 건드리지 않게).
    /// </summary>
    public void Apply(float letterbox, float fade, bool scanHud)
    {
        EnsureCanvas();
        if (!_canvas.gameObject.activeSelf) _canvas.gameObject.SetActive(true);

        SetLetterbox(_topBar, _bottomBar, letterbox);

        Color c = _black.color;
        c.a = Mathf.Clamp01(fade);
        _black.color = c;
        _black.enabled = c.a > 0f;

        if (scanHud) HideHud();
    }

    /// <summary>화면을 걷고 숨긴 UI 를 되켠다. 멱등.</summary>
    public void Clear()
    {
        RestoreHud();
        if (_canvas != null && _canvas.gameObject.activeSelf) _canvas.gameObject.SetActive(false);
    }

    /// <summary>띠 높이만 바꾼다 — 위 띠 anchorMin.y, 아래 띠 anchorMax.y. 나머지 anchor·offset 은 그대로(저작 메뉴도 이걸로 기본 높이를 저장).</summary>
    public static void SetLetterbox(RectTransform topBar, RectTransform bottomBar, float letterbox)
    {
        letterbox = Mathf.Clamp(letterbox, 0f, 0.5f);
        Vector2 min = topBar.anchorMin;
        min.y = 1f - letterbox;
        topBar.anchorMin = min;
        Vector2 max = bottomBar.anchorMax;
        max.y = letterbox;
        bottomBar.anchorMax = max;
    }

    /// <summary>
    /// 기본 구성(프리팹이 없을 때·저작 메뉴의 처음 내용) — Overlay 캔버스(정렬 500, 1920×1080 기준 스케일러) + 검은 띠 2장 + 검은 화면(알파 0·꺼짐).
    /// 검은 화면은 띠 위에 그린다(나중 형제가 위).
    /// </summary>
    public static GameObject CreateDefault(Transform parent, float letterbox)
    {
        var root = new GameObject(RootName, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = CanvasSortingOrder;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform top = Bar(root.transform, TopBarName, new Vector2(0f, 1f), new Vector2(1f, 1f));
        RectTransform bottom = Bar(root.transform, BottomBarName, new Vector2(0f, 0f), new Vector2(1f, 0f));
        SetLetterbox(top, bottom, letterbox);
        Image black = Bar(root.transform, FadeName, Vector2.zero, Vector2.one).GetComponent<Image>();
        black.color = new Color(0f, 0f, 0f, 0f);
        black.enabled = false;
        return root;
    }

    private void OnDestroy()
    {
        RestoreHud();
    }

    private void HideHud()
    {
        if (HudRescanInterval > 0f && Time.unscaledTime < _hudRescanAt) return;
        _hudRescanAt = Time.unscaledTime + HudRescanInterval;

        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas c = canvases[i];
            if (c == null || !c.enabled || !c.isRootCanvas || c.renderMode == RenderMode.WorldSpace) continue;
            if (c == _canvas || Keeps(c)) continue;
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

    // 남기는 것: ESC 게임 메뉴 · 접속 오버레이 · 로딩 화면.
    private static bool Keeps(Canvas canvas)
    {
        GameObject go = canvas.gameObject;
        return Has<GameMenuController>(go) || Has<DemoConnectionOverlay>(go) || Has<NetworkLoadingScreenView>(go);
    }

    // 컨트롤러가 캔버스 위(부모)에 붙은 구성도 있다 — 양쪽 다 본다.
    private static bool Has<T>(GameObject go) where T : Component =>
        go.GetComponentInChildren<T>(true) != null || go.GetComponentInParent<T>(true) != null;

    private void EnsureCanvas()
    {
        if (_canvas != null) return;

        if (Prefab != null)
        {
            GameObject instance = Instantiate(Prefab, transform, false);
            instance.name = Prefab.name;   // "(Clone)" 없이 — Play 중 Hierarchy 에서 찾기 쉽게
            if (Bind(instance)) return;
            Debug.LogWarning($"{Tag} 프리팹 '{Prefab.name}' 구성이 안 맞다 — 루트 Canvas, 직계 자식 '{TopBarName}'·'{BottomBarName}'(RectTransform)·'{FadeName}'(Image) 필요. 코드 기본 구성으로 대체.", Prefab);
            DestroyImmediate(instance);   // 같은 프레임에 기본 구성과 겹쳐 보이지 않게
        }

        Bind(CreateDefault(transform, 0f));
    }

    private bool Bind(GameObject root)
    {
        Canvas canvas = root.GetComponent<Canvas>();
        RectTransform top = root.transform.Find(TopBarName) as RectTransform;
        RectTransform bottom = root.transform.Find(BottomBarName) as RectTransform;
        Transform fade = root.transform.Find(FadeName);
        Image black = fade != null ? fade.GetComponent<Image>() : null;
        if (canvas == null || top == null || bottom == null || black == null) return false;

        canvas.sortingOrder = CanvasSortingOrder;
        _canvas = canvas;
        _topBar = top;
        _bottomBar = bottom;
        _black = black;
        return true;
    }

    private static RectTransform Bar(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = Color.black;
        image.raycastTarget = false;
        RectTransform rect = image.rectTransform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return rect;
    }
}
