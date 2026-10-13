using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>특성 선택 창의 카드 1장(이름·설명·아이콘). 클릭하면 풀 인덱스를 넘긴다.</summary>
public sealed class TutorialTraitCardView : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text displayName;
    [SerializeField] private TMP_Text description;

    private int traitIndex = -1;
    private Action<int> clicked;

    private void Awake()
    {
        if (button != null)
            button.onClick.AddListener(HandleClick);
    }

    public void Bind(TutorialTraitDefinition trait, int index, Sprite placeholderIcon, Action<int> onClick)
    {
        traitIndex = index;
        clicked = onClick;
        if (icon != null)
            icon.sprite = trait != null && trait.Icon != null ? trait.Icon : placeholderIcon;
        if (displayName != null)
            displayName.text = trait != null ? trait.DisplayName : $"특성 {index}";
        if (description != null)
            description.text = trait != null ? trait.Description : string.Empty;
    }

    private void HandleClick() => clicked?.Invoke(traitIndex);
}
