using UnityEngine;

/// <summary>
/// 강조 표시 하나(테두리 펄스 + 위쪽 화살표). <see cref="TutorialGuideUI"/> 가 템플릿을 복제해 대상마다 하나씩 쓰고,
/// 매 프레임 대상의 화면 사각형으로 옮긴다. 레이캐스트는 막지 않는다.
/// </summary>
public sealed class TutorialHighlightMarker : MonoBehaviour
{
    [SerializeField] private RectTransform frame;
    [SerializeField] private RectTransform arrow;
    [SerializeField] private CanvasGroup frameGroup;
    [SerializeField] private float pulseSpeed = 4f;
    [SerializeField] private float pulseScale = 0.08f;
    [SerializeField] private float arrowBob = 10f;
    [SerializeField] private float arrowGap = 12f;

    private RectTransform rectTransform;

    public RectTransform RectTransform => rectTransform != null ? rectTransform : rectTransform = (RectTransform)transform;

    /// <summary>center·size 는 강조 레이어 로컬 좌표(앵커·피벗 중앙 기준).</summary>
    public void Place(Vector2 center, Vector2 size, TutorialHighlightStyle style, float time)
    {
        RectTransform.anchoredPosition = center;
        RectTransform.sizeDelta = size;

        float wave = 0.5f + 0.5f * Mathf.Sin(time * pulseSpeed);
        bool showFrame = style != TutorialHighlightStyle.Arrow;
        bool showArrow = style != TutorialHighlightStyle.Pulse;

        if (frame != null)
        {
            frame.gameObject.SetActive(showFrame);
            frame.localScale = Vector3.one * (1f + pulseScale * wave);
        }

        if (frameGroup != null)
            frameGroup.alpha = Mathf.Lerp(0.45f, 1f, wave);

        if (arrow != null)
        {
            arrow.gameObject.SetActive(showArrow);
            arrow.anchoredPosition = new Vector2(0f, size.y * 0.5f + arrowGap + arrowBob * wave);
        }
    }

    public void SetVisible(bool visible)
    {
        if (gameObject.activeSelf != visible)
            gameObject.SetActive(visible);
    }
}
