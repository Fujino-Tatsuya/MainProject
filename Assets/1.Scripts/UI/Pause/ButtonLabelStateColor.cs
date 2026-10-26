using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 누를 수 있는 UI 의 반응 규칙(경석 10-26 — ESC 메뉴·설정창 공통, PLAN-title-start-settings-tabs 6차).
/// 누를 수 있음 = 순백 글자 · 원래 그림 / 고름(마우스 올림·키보드/패드 선택) = 회색 글자 · 그림 ×0.55 / 누름 = ×0.45 / 비활성 최우선.
/// 본 그림(Selectable.targetGraphic)은 Button 의 ColorTint 가 맡고, 이 컴포넌트는 uGUI 가 안 바꾸는 <b>글자</b>와
/// <b>자식 아이콘</b>(✓/✗·X·드롭다운 화살표)을 같은 상태로 맞춘다.
/// ESC 메뉴 버튼은 프리팹에 붙어 있고, 설정창은 <see cref="UiFeedbackStyle"/> 가 런타임에 붙인다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ButtonLabelStateColor : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
{
    /// <summary>고름 배율 — 설정 탭 selectedTabTint 와 같다.</summary>
    public static readonly Color ChosenTint = new Color(0.55f, 0.55f, 0.55f, 1f);
    /// <summary>누름 배율.</summary>
    public static readonly Color PressedTint = new Color(0.45f, 0.45f, 0.45f, 1f);
    /// <summary>비활성 배율(그림) — 회색 hover 와 헷갈리지 않게 반투명.</summary>
    public static readonly Color DisabledTint = new Color(0.6f, 0.6f, 0.6f, 0.5f);
    /// <summary>회색 글자 — 설정 탭 selectedTabLabelColor 와 같다.</summary>
    public static readonly Color ChosenLabel = new Color(0.51f, 0.5f, 0.49f, 1f);
    private static readonly Color DisabledLabel = new Color(1f, 1f, 1f, 0.4f);

    [SerializeField] private TMP_Text label;
    [Tooltip("누를 수 있는 상태 — 설정 탭 tabLabelColor 와 같다.")]
    [SerializeField] private Color normalColor = Color.white;
    [Tooltip("고른 상태 — 설정 탭 selectedTabLabelColor 와 같다.")]
    [SerializeField] private Color chosenColor = new Color(0.51f, 0.5f, 0.49f, 1f);
    [Tooltip("본 그림과 같이 어두워질 자식 아이콘.")]
    [SerializeField] private Graphic[] decor = System.Array.Empty<Graphic>();

    private Selectable _selectable;
    private Color[] _decorBase;
    private bool _hovered;
    private bool _selected;
    private bool _pressed;
    private bool _pinned;
    private bool _wasInteractable = true;

    // 그림까지 고정할 때(드롭다운 열림·키 입력 대기) — 본 그림 색과 ColorTint 를 잠시 바꿨다가 되돌린다.
    private bool _graphicPinned;
    private Color _targetBase;
    private ColorBlock _colorsBase;

    /// <summary>
    /// 마우스로 누르고 떼면 EventSystem 선택을 푼다 — 계속 다시 누르는 컨트롤(토글)이 클릭 뒤 다른 곳을 누를 때까지
    /// 어둡게 남지 않게(경석 10-26 7차). 키보드·패드로 고른 경우는 그대로 남는다.
    /// </summary>
    public bool ReleaseSelectionOnMouseUp { get; set; }

    /// <summary>글자·아이콘을 고른 상태로 고정 — 설정창에서 지금 열린 탭(본 그림은 <see cref="SettingsBodyView"/> 가 맡는다).</summary>
    public bool Pinned
    {
        get => _pinned;
        set { _pinned = value; Apply(); }
    }

    /// <summary>런타임에 붙일 때 글자·색·아이콘을 넣는다.</summary>
    public void Configure(TMP_Text target, Color normal, Color chosen, Graphic[] icons = null)
    {
        label = target;
        normalColor = normal;
        chosenColor = chosen;
        if (icons != null) decor = icons;
        _decorBase = null;
        Apply();
    }

    /// <summary>
    /// 본 그림까지 고른 상태(×0.55)로 고정 — 드롭다운 목록이 열린 동안·키 변경 입력 대기 동안.
    /// 선택이 목록 항목으로 옮겨가도 머리가 일반 상태로 돌아오지 않게 한다(Codex 회의 10-26).
    /// </summary>
    public void PinWithGraphic(bool pin)
    {
        CacheSelectable();
        if (_selectable != null && _selectable.targetGraphic != null && pin != _graphicPinned)
        {
            Graphic g = _selectable.targetGraphic;
            if (pin)
            {
                _targetBase = g.color;
                _colorsBase = _selectable.colors;
                g.color = _targetBase * ChosenTint;
                ColorBlock c = _colorsBase;
                c.normalColor = Color.white;
                c.highlightedColor = Color.white;   // 이미 ×0.55 — 두 번 곱하지 않는다
                c.selectedColor = Color.white;
                c.pressedColor = Color.white;
                _selectable.colors = c;
            }
            else
            {
                g.color = _targetBase;
                _selectable.colors = _colorsBase;
            }
            _graphicPinned = pin;
        }
        _pinned = pin;
        Apply();
    }

    private void Awake() => CacheSelectable();

    private void CacheSelectable()
    {
        if (_selectable == null) TryGetComponent(out _selectable);
    }

    private void OnEnable()
    {
        // 메뉴를 다시 열 때 EventSystem 이 이 버튼을 아직 고르고 있을 수 있다.
        _hovered = false;
        _pressed = false;
        _selected = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;
        Apply();
    }

    private void OnDisable()
    {
        if (_graphicPinned) PinWithGraphic(false);   // 패널이 닫힐 때 고정을 남기지 않는다
    }

    // 비활성 전환(적용 버튼·프레임 제한 등)은 포인터 이벤트 없이 바뀐다 — 바뀐 프레임에만 다시 칠한다.
    private void LateUpdate()
    {
        bool interactable = _selectable == null || _selectable.IsInteractable();
        if (interactable != _wasInteractable) Apply();
    }

    public void OnPointerEnter(PointerEventData eventData) { _hovered = true; Apply(); }
    public void OnPointerExit(PointerEventData eventData) { _hovered = false; Apply(); }
    public void OnPointerDown(PointerEventData eventData) { if (eventData.button == PointerEventData.InputButton.Left) { _pressed = true; Apply(); } }
    public void OnPointerUp(PointerEventData eventData)
    {
        _pressed = false;
        EventSystem es = EventSystem.current;
        if (ReleaseSelectionOnMouseUp && eventData.button == PointerEventData.InputButton.Left &&
            es != null && es.currentSelectedGameObject == gameObject)
            es.SetSelectedGameObject(null);   // → OnDeselect · Selectable 은 포인터가 안에 있으면 Highlighted 로
        Apply();
    }
    public void OnSelect(BaseEventData eventData) { _selected = true; Apply(); }
    public void OnDeselect(BaseEventData eventData) { _selected = false; Apply(); }

    private void Apply()
    {
        CacheSelectable();
        bool interactable = _selectable == null || _selectable.IsInteractable();
        _wasInteractable = interactable;
        bool chosen = _hovered || _selected || _pinned || _pressed;

        if (label != null)
            label.color = !interactable ? DisabledLabel : chosen ? chosenColor : normalColor;

        if (decor == null || decor.Length == 0) return;
        if (_decorBase == null || _decorBase.Length != decor.Length)
        {
            _decorBase = new Color[decor.Length];
            for (int i = 0; i < decor.Length; i++) _decorBase[i] = decor[i] != null ? decor[i].color : Color.white;
        }

        Color tint = !interactable ? DisabledTint : _pressed ? PressedTint : chosen ? ChosenTint : Color.white;
        for (int i = 0; i < decor.Length; i++)
            if (decor[i] != null) decor[i].color = _decorBase[i] * tint;
    }
}
