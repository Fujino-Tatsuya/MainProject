using EuniTween;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 마우스를 올리거나(키보드·패드로) 선택하면 버튼 둘레에 흰 테두리가 짧게 켜진다(할 일 224 — 로비 결과창 '닫기' 버튼).
/// 버튼 Image 는 건드리지 않는다 — 기존 Button Transition(ColorTint)이 그대로 동작한다.
/// 테두리 = 버튼 바로 뒤 형제 Image 하나(처음 켜질 때 만들어 재사용, 런타임 텍스처·메시 생성 없음). 버튼 자리·크기를 매 프레임 따라가
/// 버튼보다 <c>outset</c> 만큼 크게 그려 버튼 둘레만 보인다. 흰 스프라이트(기본 Unity 내장 UISprite)에 색을 곱하므로 진짜 흰색이 나온다.
/// 시간 = unscaled, 이징 = EuniTween. 꺼지거나(패널 닫힘) 비활성 버튼이면 즉시 숨긴다.
/// </summary>
[DisallowMultipleComponent]
public sealed class UiHoverBorder : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    [Tooltip("테두리 스프라이트(흰색 9-slice 여야 색이 그대로 나온다). 기본 = Unity 내장 UI/Skin/UISprite.psd. 비우면 테두리 없음.")]
    [SerializeField] private Sprite outlineSprite;
    [Tooltip("테두리 색(켜졌을 때). 알파 = 최대 세기.")]
    [SerializeField] private Color outlineColor = new Color(1f, 1f, 1f, 0.95f);
    [Tooltip("버튼 밖으로 나오는 테두리 두께(UI px, 사방).")]
    [SerializeField] private float outset = 5f;
    [Tooltip("스프라이트 pixelsPerUnitMultiplier — 작을수록 모서리가 둥글다(알약 모양 버튼 = 작게).")]
    [SerializeField] private float cornerScale = 0.12f;
    [Tooltip("켜지고 꺼지는 시간(초, unscaled). 0 이하면 즉시.")]
    [SerializeField] private float fadeTime = 0.1f;
    [Tooltip("켜짐 이징(EuniTween).")]
    [SerializeField] private Ease ease = Ease.OutQuad;
    [Tooltip("키보드·패드로 선택됐을 때도 표시.")]
    [SerializeField] private bool showOnSelect = true;

    private Selectable _selectable;
    private Image _outline;
    private bool _hover;
    private bool _selected;
    private float _progress; // 0 = 꺼짐, 1 = 켜짐

    private void Awake() => _selectable = GetComponent<Selectable>();

    public void OnPointerEnter(PointerEventData eventData) => _hover = true;
    public void OnPointerExit(PointerEventData eventData) => _hover = false;
    public void OnSelect(BaseEventData eventData) => _selected = true;
    public void OnDeselect(BaseEventData eventData) => _selected = false;

    private void OnDisable()
    {
        _hover = false;
        _selected = false;
        _progress = 0f;
        if (_outline != null)
            _outline.enabled = false;
    }

    private void Update()
    {
        bool interactable = _selectable == null || _selectable.IsInteractable();
        bool on = interactable && (_hover || (showOnSelect && _selected));
        _progress = Step(_progress, on ? 1f : 0f, Time.unscaledDeltaTime, fadeTime);
        Apply();
    }

    /// <summary>켜짐 진행도 한 프레임 — 목표 쪽으로 <paramref name="dt"/>/<paramref name="fadeTime"/> 만큼. 시간 0 이하 = 바로 목표.</summary>
    public static float Step(float current, float target, float dt, float fadeTime)
    {
        if (fadeTime <= 0f)
            return target;
        return Mathf.MoveTowards(current, target, Mathf.Max(0f, dt) / fadeTime);
    }

    private void Apply()
    {
        if (_progress <= 0f)
        {
            if (_outline != null)
                _outline.enabled = false;
            return;
        }
        if (_outline == null && !CreateOutline())
            return;

        var rect = (RectTransform)transform;
        RectTransform outline = _outline.rectTransform;
        outline.anchorMin = rect.anchorMin;
        outline.anchorMax = rect.anchorMax;
        outline.pivot = rect.pivot;
        outline.anchoredPosition = rect.anchoredPosition;
        outline.sizeDelta = rect.sizeDelta + new Vector2(outset, outset) * 2f;
        outline.localRotation = rect.localRotation;
        outline.localScale = rect.localScale;
        int buttonIndex = rect.GetSiblingIndex();
        int outlineIndex = outline.GetSiblingIndex();
        if (outlineIndex != buttonIndex - 1) // 버튼 바로 뒤 — 버튼이 가운데를 덮어 둘레만 보인다
            outline.SetSiblingIndex(outlineIndex < buttonIndex ? buttonIndex - 1 : buttonIndex);

        Color color = outlineColor;
        color.a *= Easing.Evaluate(ease, _progress);
        _outline.color = color;
        _outline.pixelsPerUnitMultiplier = cornerScale;
        _outline.enabled = true;
    }

    private bool CreateOutline()
    {
        if (outlineSprite == null || transform.parent == null)
            return false;
        var go = new GameObject(name + "_HoverBorder", typeof(RectTransform), typeof(Image));
        go.layer = gameObject.layer;
        go.transform.SetParent(transform.parent, false);
        _outline = go.GetComponent<Image>();
        _outline.sprite = outlineSprite;
        _outline.type = Image.Type.Sliced;
        _outline.raycastTarget = false;
        _outline.enabled = false;
        return true;
    }
}
