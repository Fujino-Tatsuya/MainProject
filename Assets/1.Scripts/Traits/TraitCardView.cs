using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 특성 선택 창의 카드 1장 — 이름 / 짧은 분류 / 설명 순(명세 §2, 할 일 125 5/5). 클릭하면 특성 ID 해시를 넘긴다.
/// 문구는 Strings 키(<see cref="TraitStringKeys"/>) → 정의 인라인 ko. 설명은 <see cref="TraitTooltip"/> 이 <c>{필드}</c>·<c>{dmg}</c> 를 푼다 —
/// 실패하면 <see cref="UiStrings.SkillTooltipError"/>(개발용 표시) + 경고(스킬 툴팁과 같은 관례).
/// </summary>
public sealed class TraitCardView : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text displayName;
    [Tooltip("분류 줄(이름 아래). 비어 있으면 분류를 그리지 않는다 — 저작 메뉴 '분류·도움말 줄 추가' 가 만든다.")]
    [SerializeField] private TMP_Text category;
    [SerializeField] private TMP_Text description;

    private int traitHash = TraitIdHash.None;
    private Action<int> clicked;

    private void Awake()
    {
        if (button != null)
            button.onClick.AddListener(HandleClick);
    }

    /// <param name="viewer">카드를 보는 로컬 플레이어 — 설명 {dmg} 계산용(없으면 '-').</param>
    public void Bind(TraitDefinition trait, int traitHash, Sprite placeholderIcon, Action<int> onClick, Player viewer)
    {
        this.traitHash = traitHash;
        clicked = onClick;
        if (icon != null)
            icon.sprite = trait != null && trait.Icon != null ? trait.Icon : placeholderIcon;
        if (displayName != null)
            displayName.text = trait != null ? trait.DisplayName : $"특성 {traitHash}";
        if (category != null)
        {
            string line = trait != null ? TraitUiStrings.FormatCategory(trait.Category) : string.Empty;
            category.text = line;
            category.gameObject.SetActive(line.Length > 0);
        }

        if (description != null)
            description.text = DescriptionOf(trait, viewer, this);
    }

    /// <summary>카드·획득 툴팁 공용 설명 문구. 실패하면 오류 표시 + 경고.</summary>
    public static string DescriptionOf(TraitDefinition trait, Player viewer, UnityEngine.Object context)
    {
        if (trait == null)
            return string.Empty;

        if (TraitTooltip.FormatDescription(trait, viewer, false, out string text, out string error))
            return text;

        Debug.LogWarning($"[Trait] '{trait.Id}' 설명 문구 오류 — {error}", context);
        return UiStrings.SkillTooltipError.Format(new System.Collections.Generic.Dictionary<string, string> { ["error"] = error });
    }

    private void HandleClick() => clicked?.Invoke(traitHash);
}
