using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 포드 강하 연출의 <b>로컬 화면</b>(할 일 100) — 위·아래 레터박스, 전체 검은 화면(페이드), 연출 동안 화면 UI 숨김.
/// 값은 호출자(<see cref="LobbyPodLaunch"/>)가 매 프레임 시간표에서 넣는다. 런타임에 붙고(씬·프리팹 무수정) 캔버스는 이 오브젝트 아래에 만든다 —
/// 로비 씬이 언로드되면 같이 사라지고, 그때 숨겼던 UI 를 되켠다.
/// 정렬 = 로딩 화면(1000)·ESC 게임 메뉴(900) 아래, HUD(0~200) 위 — 암전 뒤 로딩 화면이 이 검은 화면 위에 뜬다.
/// UI 숨김 규칙은 보스 등장 연출(<see cref="BossIntroPresentation"/>)과 같다(Screen Space 루트 캔버스를 Canvas.enabled 로 끈다).
/// </summary>
[DisallowMultipleComponent]
public sealed class PodDropScreenOverlay : MonoBehaviour
{
    private const int CanvasSortingOrder = 500;
    private const float HudRescanSeconds = 0.5f;   // 연출 도중 새로 뜬 캔버스도 잡는다

    private Canvas _canvas;
    private RectTransform _topBar;
    private RectTransform _bottomBar;
    private Image _black;

    private readonly List<Canvas> _hiddenHud = new List<Canvas>();
    private float _hudRescanAt;

    /// <summary>
    /// 이번 프레임 화면. <paramref name="letterbox"/> = 위·아래 띠 각각의 화면 높이 비율, <paramref name="fade"/> = 검은 화면 알파.
    /// <paramref name="scanHud"/> = 새로 뜬 화면 UI 도 찾아 끈다(false 면 이미 끈 것만 꺼 둔다 — 암전 뒤 로딩 씬 캔버스를 건드리지 않게).
    /// </summary>
    public void Apply(float letterbox, float fade, bool scanHud)
    {
        EnsureCanvas();
        if (!_canvas.gameObject.activeSelf) _canvas.gameObject.SetActive(true);

        letterbox = Mathf.Clamp(letterbox, 0f, 0.5f);
        _topBar.anchorMin = new Vector2(0f, 1f - letterbox);
        _bottomBar.anchorMax = new Vector2(1f, letterbox);

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

    private void OnDestroy()
    {
        RestoreHud();
    }

    private void HideHud()
    {
        if (Time.unscaledTime < _hudRescanAt) return;
        _hudRescanAt = Time.unscaledTime + HudRescanSeconds;

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

        var root = new GameObject("PodDropOverlay", typeof(RectTransform));
        root.transform.SetParent(transform, false);
        _canvas = root.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = CanvasSortingOrder;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _topBar = Bar(root.transform, "LetterboxTop", new Vector2(0f, 1f), new Vector2(1f, 1f));
        _bottomBar = Bar(root.transform, "LetterboxBottom", new Vector2(0f, 0f), new Vector2(1f, 0f));
        // 검은 화면은 띠 위에 그린다(나중 형제가 위).
        _black = Bar(root.transform, "Fade", Vector2.zero, Vector2.one).GetComponent<Image>();
        _black.color = new Color(0f, 0f, 0f, 0f);
        _black.enabled = false;
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
