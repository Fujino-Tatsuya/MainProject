using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>특성 선택 창의 카드 1장(이름·설명·아이콘). 클릭하면 특성 ID 해시를 넘긴다.</summary>
public sealed class TraitCardView : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text displayName;
    [SerializeField] private TMP_Text description;

    private int traitHash = TraitIdHash.None;
    private Action<int> clicked;

    private void Awake()
    {
        if (button != null)
            button.onClick.AddListener(HandleClick);
    }

    public void Bind(TraitDefinition trait, int traitHash, Sprite placeholderIcon, Action<int> onClick)
    {
        this.traitHash = traitHash;
        clicked = onClick;
        if (icon != null)
            icon.sprite = trait != null && trait.Icon != null ? trait.Icon : placeholderIcon;
        if (displayName != null)
            displayName.text = trait != null ? trait.DisplayName : $"특성 {traitHash}";
        if (description != null)
            description.text = trait != null ? trait.Description : string.Empty;
    }

    private void HandleClick() => clicked?.Invoke(traitHash);
}
