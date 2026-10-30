using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 특성 선택 창의 카드 1장 — 이름 / 짧은 분류 / 설명 순(명세 §2, 할 일 125 5/5). 클릭하면 특성 ID 해시를 넘긴다.
/// 할 일 165 — C1 아트: 카드판 · 좌상단 특성 아이콘 · 스킬 프레임+마스크 안 스킬 아이콘 · 키캡 1곳(<see cref="TraitKeycapRule"/>).
/// 피드백 1 — 스킬 아이콘은 뷰어의 실제 스킬(<see cref="TraitCardSkillRule"/>, 전투 HUD 와 같은 경로), 못 얻으면 특성 아이콘 → 빈 아이콘.
/// 후속 — LMB(기본 공격) 특성은 기본 공격 아이콘(검)을 보는 사람 캐릭터 색(로스터 themeColor)으로 틴트, 나머지 슬롯은 흰색.
/// 검 아이콘은 투명 배경이라 마스크 안 아이콘 뒤에 검은 배경(<c>skillBackdrop</c>)을 깐다 — 검일 때만 켬.
/// 키 입력이 아닌 특성(<see cref="TraitKeycap.Blank"/>)은 키캡을 숨긴다.
/// 문구는 Strings 키(<see cref="TraitStringKeys"/>) → 정의 인라인 ko. 설명은 <see cref="TraitTooltip"/> 이 <c>{필드}</c>·<c>{dmg}</c> 를 푼다 —
/// 실패하면 <see cref="UiStrings.SkillTooltipError"/>(개발용 표시) + 경고(스킬 툴팁과 같은 관례).
/// </summary>
public sealed class TraitCardView : MonoBehaviour
{
    [SerializeField] private Button button;
    [Tooltip("좌상단 특성 아이콘 자리.")]
    [SerializeField] private Image traitIcon;
    [Tooltip("스킬 프레임+마스크 안 — 특성이 강화하는 스킬 아이콘.")]
    [SerializeField] private Image skillIcon;
    [SerializeField] private TMP_Text displayName;
    [Tooltip("분류 줄(이름 아래). 비어 있으면 분류를 그리지 않는다 — 저작 메뉴 '분류·도움말 줄 추가' 가 만든다.")]
    [SerializeField] private TMP_Text category;
    [SerializeField] private TMP_Text description;

    [Header("C1 아트 (할 일 165)")]
    [Tooltip("키캡/마우스 표시 자리 1곳 — 특성 아이콘 이름으로 스프라이트만 바꾼다(TraitKeycapRule). 키 입력이 아닌 특성은 숨긴다.")]
    [SerializeField] private Image keycap;
    [SerializeField] private Sprite keycapQ;
    [SerializeField] private Sprite keycapE;
    [SerializeField] private Sprite keycapR;
    [SerializeField] private Sprite inputLeftMouse;
    [SerializeField] private Sprite inputRightMouse;

    [Header("기본 공격 슬롯 (할 일 165 후속)")]
    [Tooltip("LMB 특성의 스킬 슬롯 그림(기본 공격 스킬 아이콘이 없어서) — 보는 사람 캐릭터 색으로 틴트한다.")]
    [SerializeField] private Sprite basicAttackIcon;
    [Tooltip("스킬 마스크 안, 스킬 아이콘 뒤 검은 배경(형제 Image) — 검 아이콘일 때만 켠다(TraitCardSkillRule.ShowsBackdrop). 틴트 없음.")]
    [SerializeField] private Image skillBackdrop;
    [Tooltip("보는 사람 캐릭터 색 출처(Entry.themeColor). 못 찾으면 흰색.")]
    [SerializeField] private CharacterRoster characterRoster;

    private int traitHash = TraitIdHash.None;
    private Action<int> clicked;
    private TraitDefinition boundTrait;
    private Player boundViewer;
    private bool shownValue;

    private void Awake()
    {
        if (button != null)
            button.onClick.AddListener(HandleClick);
    }

    // {dmg} — Shift 누르는 동안만 계산 숫자, 떼면 퍼센트(할 일 171). 바뀐 프레임에만 다시 쓴다.
    private void Update()
    {
        if (description == null || boundTrait == null)
            return;

        bool value = TraitTooltip.IsValueHeld();
        if (value != shownValue)
            RefreshDescription(value);
    }

    private void RefreshDescription(bool showValue)
    {
        shownValue = showValue;
        description.text = DescriptionOf(boundTrait, boundViewer, this, showValue);
    }

    /// <param name="viewer">카드를 보는 로컬 플레이어 — 설명 {dmg} 계산용(없으면 '-').</param>
    public void Bind(TraitDefinition trait, int traitHash, Sprite placeholderIcon, Action<int> onClick, Player viewer)
    {
        this.traitHash = traitHash;
        clicked = onClick;
        Sprite traitSprite = trait != null && trait.Icon != null ? trait.Icon : placeholderIcon;
        TraitKeycap key = TraitKeycapRule.FromIconName(trait != null && trait.Icon != null ? trait.Icon.name : null);
        if (traitIcon != null)
            traitIcon.sprite = traitSprite;
        bool backdrop = false;
        if (skillIcon != null)
        {
            // 아이콘 없는 특성(옛 임시 특성)은 키를 모른다 — 패시브로 오인하지 않게 스킬 조회를 건너뛴다.
            bool known = trait != null && trait.Icon != null;
            TraitCardSkillSource source = known ? TraitCardSkillRule.SourceOf(key, out _) : TraitCardSkillSource.Skill;
            Sprite skillSprite = !known ? null
                : source == TraitCardSkillSource.BasicAttack ? basicAttackIcon
                : SkillIconOf(viewer, key);
            skillIcon.sprite = skillSprite != null ? skillSprite : traitSprite;
            // 색은 Bind 마다 덮는다(카드 재사용 시 이전 색 초기화). 검 그림이 실제로 들어갔을 때만 틴트.
            if (skillSprite == null)
                source = TraitCardSkillSource.Skill; // 특성 아이콘으로 떨어졌으면 틴트 없음
            Color viewerColor = Color.white;
            bool hasColor = source == TraitCardSkillSource.BasicAttack && TryViewerColor(viewer, out viewerColor);
            skillIcon.color = TraitCardSkillRule.SlotTint(source, hasColor, viewerColor);
            backdrop = TraitCardSkillRule.ShowsBackdrop(source);
        }
        // 켜고 끄기도 Bind 마다 덮는다(카드 재사용 시 이전 상태 초기화).
        if (skillBackdrop != null)
            skillBackdrop.gameObject.SetActive(backdrop);

        if (displayName != null)
            displayName.text = trait != null ? trait.DisplayName : $"특성 {traitHash}";
        if (category != null)
        {
            string line = trait != null ? TraitUiStrings.FormatCategory(trait.Category) : string.Empty;
            category.text = line;
            category.gameObject.SetActive(line.Length > 0);
        }

        boundTrait = trait;
        boundViewer = viewer;
        if (description != null)
            RefreshDescription(TraitTooltip.IsValueHeld());

        if (keycap != null)
        {
            Sprite keySprite = KeycapSpriteOf(key);
            keycap.sprite = keySprite;
            keycap.gameObject.SetActive(keySprite != null);
        }
    }

    /// <summary>
    /// 특성이 강화하는 뷰어 스킬의 아이콘(<see cref="TraitCardSkillRule"/>). 경로는 SkillCooldownHUD(슬롯 스킬 Data)·
    /// PassiveHUD(IPlayerPassive → IPassiveTooltipProvider)와 같다. 뷰어가 없거나 아이콘이 비면 null.
    /// </summary>
    private static Sprite SkillIconOf(Player viewer, TraitKeycap key)
    {
        if (viewer == null)
            return null;

        ISkillTooltipSource source = null;
        switch (TraitCardSkillRule.SourceOf(key, out PlayerSkillSlot slot))
        {
            case TraitCardSkillSource.Skill:
                PlayerSkillController skills = viewer.GetComponent<PlayerSkillController>();
                source = skills != null ? skills.GetSkill(slot)?.Data : null;
                break;
            case TraitCardSkillSource.Passive:
                source = viewer.GetComponent<IPlayerPassive>() as ISkillTooltipSource;
                if (source == null)
                    source = viewer.GetComponentInChildren<IPassiveTooltipProvider>(true)?.PassiveTooltip;
                break;
        }

        return source != null ? source.Tooltip.Icon : null;
    }

    // 보는 사람 캐릭터 색 — NetworkObject.PrefabIdHash → 로스터 칸(SpawnedCharacterRoster/CharacterRosterLookup 경로) → Entry.ThemeColor.
    private bool TryViewerColor(Player viewer, out Color color)
    {
        color = Color.white;
        if (!SpawnedCharacterRoster.TryGetEntry(viewer, characterRoster, out CharacterRoster.Entry entry))
            return false;
        color = entry.ThemeColor;
        return true;
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
            default: return null;
        }
    }

    /// <summary>카드·획득 툴팁 공용 설명 문구 — 본문 굵게(<see cref="TraitTooltip.Bold"/>). 실패하면 오류 표시 + 경고.</summary>
    /// <param name="showValue">{dmg} 계산 숫자(Shift 누르는 동안) / false = 퍼센트 글자.</param>
    public static string DescriptionOf(TraitDefinition trait, Player viewer, UnityEngine.Object context, bool showValue)
    {
        if (trait == null)
            return string.Empty;

        if (TraitTooltip.FormatDescription(trait, viewer, showValue, out string text, out string error))
            return TraitTooltip.Bold(text);

        Debug.LogWarning($"[Trait] '{trait.Id}' 설명 문구 오류 — {error}", context);
        return UiStrings.SkillTooltipError.Format(new System.Collections.Generic.Dictionary<string, string> { ["error"] = error });
    }

    private void HandleClick() => clicked?.Invoke(traitHash);
}
