using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>획득 특성 다시 보기 줄의 아이콘 1개. 호버하면 이름·설명 툴팁을 요청한다.</summary>
public sealed class TraitIconView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Image icon;

    private TraitDefinition trait;
    private Action<TraitIconView, TraitDefinition> hoverChanged;

    public void Bind(TraitDefinition definition, Sprite placeholderIcon,
        Action<TraitIconView, TraitDefinition> onHoverChanged)
    {
        trait = definition;
        hoverChanged = onHoverChanged;
        if (icon != null)
            icon.sprite = definition != null && definition.Icon != null ? definition.Icon : placeholderIcon;
    }

    // 인자 trait = null 이면 호버 끝.
    public void OnPointerEnter(PointerEventData eventData) => hoverChanged?.Invoke(this, trait);

    public void OnPointerExit(PointerEventData eventData) => hoverChanged?.Invoke(this, null);

    private void OnDisable() => hoverChanged?.Invoke(this, null);
}
