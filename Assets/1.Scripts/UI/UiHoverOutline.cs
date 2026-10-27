using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 마우스가 올라와 있는 동안 아웃라인 Graphic 을 켠다(할 일 109 — SELECT 셰브론). 버튼과 같은 오브젝트에 붙인다.
/// hover = EventSystem Pointer Enter/Exit — 레이캐스트를 거치므로 클릭 영역 필터(<see cref="UiTriangleRaycastFilter"/>)를 그대로 따른다.
/// 기본은 <see cref="target"/> 이 interactable 일 때만 보인다. <see cref="Blocked"/>(예: 캐릭터 선택 잠금) 동안도 끈다.
/// 꺼지면(패널 닫힘 등) 아웃라인도 끈다.
/// </summary>
[DisallowMultipleComponent]
public sealed class UiHoverOutline : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Tooltip("켜고 끌 아웃라인(기본 꺼 둔다). 예: 자식 Outline_Hover(구운 실루엣 링 Image).")]
    [SerializeField] private Graphic outline;
    [Tooltip("interactable 을 볼 버튼. 비우면 같은 오브젝트의 Selectable.")]
    [SerializeField] private Selectable target;
    [Tooltip("켜면 target 이 interactable 일 때만 아웃라인을 보인다.")]
    [SerializeField] private bool onlyWhenInteractable = true;

    bool _hovered;
    bool _blocked;

    public Graphic Outline => outline;
    public bool IsShown => outline != null && outline.enabled;

    /// <summary>
    /// 켜 두면 hover 중이어도 아웃라인을 끈다(클릭은 그대로 받는다 — interactable 과 다르다).
    /// 바꾸는 즉시 따라간다 — hover 중 막히면 꺼지고, hover 중 풀리면 켜진다.
    /// </summary>
    public bool Blocked
    {
        get => _blocked;
        set
        {
            _blocked = value;
            Apply();
        }
    }

    public void Setup(Graphic outlineGraphic, Selectable targetSelectable)
    {
        outline = outlineGraphic;
        target = targetSelectable;
        Apply();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _hovered = true;
        Apply();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _hovered = false;
        Apply();
    }

    void Update()
    {
        if (_hovered)
            Apply(); // 올려 둔 채 interactable 이 바뀌어도 따라간다
    }

    void OnDisable()
    {
        _hovered = false;
        Apply();
    }

    void Apply()
    {
        if (outline == null)
            return;
        Selectable selectable = target != null ? target : GetComponent<Selectable>();
        bool show = _hovered && !_blocked && (!onlyWhenInteractable || selectable == null || selectable.IsInteractable());
        if (outline.enabled != show)
            outline.enabled = show;
    }
}
