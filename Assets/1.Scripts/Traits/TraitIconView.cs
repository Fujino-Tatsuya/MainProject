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

    /// <summary>아이콘 그림 한 변(가운데 정렬 정사각형). 칸(호버 판정)은 루트 RectTransform — 그림은 투명 여백만큼 더 크다(할 일 253).</summary>
    public void SetIconSide(float side)
    {
        if (icon != null)
            icon.rectTransform.sizeDelta = new Vector2(side, side);
    }

    // 인자 trait = null 이면 호버 끝.
    public void OnPointerEnter(PointerEventData eventData) => hoverChanged?.Invoke(this, trait);

    public void OnPointerExit(PointerEventData eventData) => hoverChanged?.Invoke(this, null);

    private void OnDisable() => hoverChanged?.Invoke(this, null);
}
