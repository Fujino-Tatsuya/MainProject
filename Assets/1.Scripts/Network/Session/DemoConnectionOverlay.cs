using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 부스 데모 연결 대기 화면 — PLAN-48 §4.2. <see cref="DemoSessionDirector"/> 가 DDOL 로 만들고 매 프레임 문구를 넘긴다.
/// 씬 UI 가 아니라 DDOL 인 이유: "설정 파일 없음 — 호스트 모드" 3초 안내가 로비로 넘어가도 끊기지 않아야 하고,
/// 대기 씬(6.DemoConnectScene)·부트스트랩 씬 어디서든 같은 화면이 떠야 한다.
/// 코드로 조립하므로 프리팹이 없다. 글꼴은 TMP 기본 + NotoSansKR 폴백(TMP Settings)이 한글을 그린다.
/// </summary>
public sealed class DemoConnectionOverlay : MonoBehaviour
{
    private const int SortingOrder = 30000;

    private GameObject _statusPanel;
    private TMP_Text _statusText;
    private GameObject _noticePanel;
    private TMP_Text _noticeText;

    public static DemoConnectionOverlay Create()
    {
        var root = new GameObject("DemoConnectionOverlay");
        DontDestroyOnLoad(root);
        var overlay = root.AddComponent<DemoConnectionOverlay>();
        overlay.Build();
        return overlay;
    }

    public void SetStatus(bool visible, string text)
    {
        SetPanel(_statusPanel, _statusText, visible, text);
    }

    public void SetNotice(bool visible, string text)
    {
        SetPanel(_noticePanel, _noticeText, visible, text);
    }

    private static void SetPanel(GameObject panel, TMP_Text label, bool visible, string text)
    {
        if (panel.activeSelf != visible)
            panel.SetActive(visible);
        if (visible && label.text != text)
            label.text = text;
    }

    private void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        // 입력은 받지 않는다 — 로비 버튼을 가리지 않게 GraphicRaycaster 를 두지 않는다.

        _statusPanel = CreatePanel("Status", new Vector2(0.5f, 0.5f), new Vector2(900f, 360f), new Color(0f, 0f, 0f, 0.75f));
        _statusText = CreateLabel(_statusPanel.transform, 40f);

        _noticePanel = CreatePanel("Notice", new Vector2(0.5f, 0.9f), new Vector2(900f, 90f), new Color(0.35f, 0.2f, 0f, 0.85f));
        _noticeText = CreateLabel(_noticePanel.transform, 36f);

        _statusPanel.SetActive(false);
        _noticePanel.SetActive(false);
    }

    private GameObject CreatePanel(string panelName, Vector2 anchor, Vector2 size, Color color)
    {
        var panel = new GameObject(panelName, typeof(RectTransform), typeof(Image));
        var rect = (RectTransform)panel.transform;
        rect.SetParent(transform, false);
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        var image = panel.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return panel;
    }

    private static TMP_Text CreateLabel(Transform parent, float fontSize)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(24f, 16f);
        rect.offsetMax = new Vector2(-24f, -16f);
        var label = go.AddComponent<TextMeshProUGUI>();
        label.fontSize = fontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.Normal;
        return label;
    }
}
