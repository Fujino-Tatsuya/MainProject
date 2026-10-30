using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 특성 선택 창의 카드 1장 — 이름 / 짧은 분류 / 설명 순(명세 §2, 할 일 125 5/5). 클릭하면 특성 ID 해시를 넘긴다.
/// 할 일 165 — C1 아트: 카드판 · 캐릭터 심볼(<see cref="TraitDefinition.Character"/>) · 스킬 프레임+마스크 안 아이콘 · 키캡 1곳(<see cref="TraitKeycapRule"/>).
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

    [Header("C1 아트 (할 일 165)")]
    [Tooltip("캐릭터 심볼 자리(좌상단). 비면 그리지 않는다.")]
    [SerializeField] private Image characterSymbol;
    [Tooltip("키캡/마우스 표시 자리 1곳 — 특성 아이콘 이름으로 스프라이트만 바꾼다(TraitKeycapRule).")]
    [SerializeField] private Image keycap;
    [SerializeField] private Sprite paladinSymbol;
    [SerializeField] private Sprite gunnerSymbol;
    [SerializeField] private Sprite assassinSymbol;
    [SerializeField] private Sprite keycapBlank;
    [SerializeField] private Sprite keycapQ;
    [SerializeField] private Sprite keycapE;
    [SerializeField] private Sprite keycapR;
    [SerializeField] private Sprite inputLeftMouse;
    [SerializeField] private Sprite inputRightMouse;

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

        if (characterSymbol != null)
            SetSprite(characterSymbol, trait != null ? SymbolOf(trait.Character) : null);
        if (keycap != null)
        {
            string iconName = trait != null && trait.Icon != null ? trait.Icon.name : null;
            SetSprite(keycap, KeycapSpriteOf(TraitKeycapRule.FromIconName(iconName)));
        }
    }

    private static void SetSprite(Image image, Sprite sprite)
    {
        image.sprite = sprite;
        image.gameObject.SetActive(sprite != null);
    }

    private Sprite SymbolOf(TutorialCharacter character)
    {
        switch (character)
        {
            case TutorialCharacter.Paladin: return paladinSymbol;
            case TutorialCharacter.Gunner: return gunnerSymbol;
            case TutorialCharacter.Assassin: return assassinSymbol;
            default: return null;
        }
    }

    private Sprite KeycapSpriteOf(TraitKeycap key)
    {
        switch (key)
        {
            case TraitKeycap.Q: return keycapQ;
            case TraitKeycap.E: return keycapE;
            case TraitKeycap.R: return keycapR;
            case TraitKeycap.LeftMouse: return inputLeftMouse;
            case TraitKeycap.RightMouse: return inputRightMouse;
            default: return keycapBlank;
        }
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
