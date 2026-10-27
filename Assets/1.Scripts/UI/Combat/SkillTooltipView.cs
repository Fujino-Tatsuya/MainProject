using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>CombatHUD에 하나만 두는 스킬 툴팁. 지연·Shift 갱신·화면 경계 보정을 담당한다.</summary>
public sealed class SkillTooltipView : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField, Min(0f)] private float showDelay = 0.15f;
    [SerializeField] private float anchorGap = 12f;
    [SerializeField] private Vector2 screenPadding = new Vector2(12f, 12f);

    [Header("Header")]
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text displayName;
    [Tooltip("이름 아래 슬롯 줄(예 Q 스킬) — 툴팁 부제, 부제가 없으면(패시브) 키 배지 글자")]
    [SerializeField] private TMP_Text subtitle;
    [Tooltip("우상단 쿨다운 묶음(시계 아이콘 + 초). 쿨다운 없는 스킬은 통째로 숨긴다")]
    [SerializeField] private GameObject cooldownGroup;
    [SerializeField] private TMP_Text cooldownText;

    [Header("Tags")]
    [Tooltip("태그 칩을 놓는 영역(왼쪽 위 기준). 가로 폭을 넘으면 다음 줄로 넘긴다")]
    [SerializeField] private RectTransform tagRow;
    [Tooltip("꺼 둔 칩 원본(Image + 자식 TMP). 필요한 만큼 복제한다")]
    [SerializeField] private RectTransform tagChipTemplate;
    [SerializeField, Min(0f)] private float tagChipPadding = 12f;
    [SerializeField, Min(0f)] private float tagSpacing = 6f;
    [Tooltip("머리 영역(Header) 높이 — 태그가 여러 줄이면 그만큼 늘린다")]
    [SerializeField] private LayoutElement headerLayout;
    [SerializeField, Min(0f)] private float headerMinHeight = 116f;
    [SerializeField, Min(0f)] private float headerBottomPadding = 12f;

    [Header("Body")]
    [SerializeField] private TMP_Text description;
    [SerializeField] private TMP_Text shiftHint;

    private RectTransform rectTransform;
    private Canvas canvas;
    private RectTransform canvasRect;
    private SkillSlotHover owner;
    private ISkillTooltipSource source;
    private Player player;
    private RectTransform anchor;
    private string displayKey;
    private float? cooldown;
    private float hoverStartedAt;
    private bool visible;
    private bool loggedError;
    private bool loggedTagError;
    private string parsedTagIds;
    private readonly List<SkillTooltipTag> tags = new List<SkillTooltipTag>();
    private readonly List<RectTransform> tagChips = new List<RectTransform>();
    private readonly List<float> tagWidths = new List<float>();
    private readonly List<Vector2> tagPositions = new List<Vector2>();

    /// <summary>
    /// [오너 로컬] 툴팁이 지연 뒤 실제로 떴다. 인자 = 그 슬롯의 키 표기(displayKey) —
    /// 스킬 칸은 현재 바인딩 표기(기본 Q·E·RMB·R — 리바인드 따라 바뀜, SkillCooldownHUD), 패시브 칸은 "Passive"(PassiveHUD). 튜토리얼 판정 훅.
    /// </summary>
    public static event System.Action<string> TooltipShown;

    /// <summary>[오너 로컬] 떠 있던 툴팁이 닫혔다(호버 종료·다른 슬롯으로 이동·출처 없음).</summary>
    public static event System.Action TooltipHidden;

    // 도메인 리로드를 끈 Play 진입에서도 이전 세션 구독자가 남지 않게 비운다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticEvents()
    {
        TooltipShown = null;
        TooltipHidden = null;
    }

    // visible 의 유일한 쓰기 창구 — 바뀔 때만 이벤트를 낸다.
    private void SetVisible(bool value)
    {
        if (visible == value)
            return;

        visible = value;
        if (value)
            TooltipShown?.Invoke(displayKey);
        else
            TooltipHidden?.Invoke();
    }

    private void Awake()
    {
        rectTransform = panel != null ? panel.transform as RectTransform : transform as RectTransform;
        canvas = GetComponentInParent<Canvas>();
        canvasRect = canvas != null ? canvas.transform as RectTransform : null;
        if (panel != null)
            panel.SetActive(false);
    }

    public void BeginHover(
        SkillSlotHover hoverOwner,
        ISkillTooltipSource tooltipSource,
        Player tooltipPlayer,
        RectTransform slotAnchor,
        string key,
        float? cooldownSeconds)
    {
        owner = hoverOwner;
        source = tooltipSource;
        player = tooltipPlayer;
        anchor = slotAnchor;
        displayKey = key;
        cooldown = cooldownSeconds;
        hoverStartedAt = Time.unscaledTime;
        SetVisible(false);
        loggedError = false;
        loggedTagError = false;
        if (panel != null)
            panel.SetActive(false);
    }

    public void EndHover(SkillSlotHover hoverOwner)
    {
        if (owner != hoverOwner)
            return;

        owner = null;
        source = null;
        anchor = null;
        SetVisible(false);
        if (panel != null)
            panel.SetActive(false);
    }

    /// <summary>열려 있거나 지연 대기 중인 같은 슬롯의 출처를 교체한다. 표시 지연/가시성은 유지한다.</summary>
    public void RefreshHover(
        SkillSlotHover hoverOwner,
        ISkillTooltipSource tooltipSource,
        Player tooltipPlayer,
        RectTransform slotAnchor,
        string key,
        float? cooldownSeconds)
    {
        if (owner != hoverOwner)
        {
            BeginHover(hoverOwner, tooltipSource, tooltipPlayer, slotAnchor, key, cooldownSeconds);
            return;
        }

        source = tooltipSource;
        player = tooltipPlayer;
        anchor = slotAnchor;
        displayKey = key;
        cooldown = cooldownSeconds;
        loggedError = false;
        loggedTagError = false;

        if (source == null)
        {
            SetVisible(false);
            if (panel != null)
                panel.SetActive(false);
        }
        else if (visible)
            RefreshContent(IsShiftHeld());
    }

    private void Update()
    {
        if (owner == null || source == null || anchor == null)
            return;

        if (!visible)
        {
            if (Time.unscaledTime - hoverStartedAt < showDelay)
                return;

            SetVisible(true);
            if (panel != null)
                panel.SetActive(true);
        }

        RefreshContent(IsShiftHeld());
        Canvas.ForceUpdateCanvases();
        // 패널 세로 = 내용 높이(VerticalLayoutGroup + ContentSizeFitter). 같은 프레임에 크기를 확정해야 화면 경계 보정이 맞는다.
        if (rectTransform != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
        Reposition();
    }

    private void RefreshContent(bool detailed)
    {
        SkillTooltipText tooltip = source.Tooltip;
        if (icon != null)
            icon.sprite = tooltip.Icon != null ? tooltip.Icon : owner.FallbackIcon; // 슬롯과 같은 그림(우클릭은 HUD 공용 아이콘)

        if (displayName != null)
            displayName.text = tooltip.DisplayName;

        if (subtitle != null)
        {
            string slotLine = SlotLineText(tooltip.Subtitle, displayKey);
            subtitle.gameObject.SetActive(slotLine.Length > 0);
            subtitle.text = slotLine;
        }

        bool hasCooldown = cooldown.HasValue && cooldown.Value > 0f;
        if (cooldownGroup != null)
            cooldownGroup.SetActive(hasCooldown);
        else if (cooldownText != null)
            cooldownText.gameObject.SetActive(hasCooldown);
        if (cooldownText != null)
            cooldownText.text = hasCooldown ? CooldownText(cooldown.Value) : string.Empty;

        RefreshTags(tooltip.TagIds);

        SkillTooltipDamage damage = source.GetTooltipDamage(player);
        bool ok = SkillTooltipFormatter.TryFormat(
            tooltip.Description,
            source.TooltipValueSource,
            damage,
            detailed,
            out string formatted,
            out bool hasDamage,
            out string error);

        if (description != null)
            description.text = ok ? formatted : ErrorText(error);

        if (!ok && !loggedError)
        {
            loggedError = true;
            Debug.LogWarning($"[SkillTooltip] {source.TooltipValueSource?.name}: {error}", source.TooltipValueSource);
        }

        if (shiftHint != null)
        {
            // 안내가 필요 없을 때({dmg} 없음·오류·Shift 누른 동안)도 글자만 투명 — 줄 자리는 항상 유지한다.
            shiftHint.gameObject.SetActive(true);
            bool showHint = ok && hasDamage && !detailed;
            shiftHint.canvasRenderer.SetAlpha(showHint ? 1f : 0f); // 프리팹 색(알파 포함)은 건드리지 않는다
            shiftHint.text = UiStrings.SkillTooltipShiftHint.Text;
        }
    }

    /// <summary>키 배지 글자 — 패시브 칸(<see cref="PassiveHUD.DisplayKey"/>)만 번역하고 Q·E·R·RMB 는 그대로. 이벤트 인자 displayKey 는 바꾸지 않는다(튜토리얼 판정).</summary>
    public static string KeyBadgeText(string key) =>
        key == PassiveHUD.DisplayKey ? UiStrings.SkillTooltipPassiveBadge.Text : key;

    /// <summary>
    /// 이름 아래 슬롯 줄 — 툴팁 부제(예 "Q 스킬", Strings 번역)가 있으면 그것, 없으면(패시브) 키 배지 글자(<see cref="KeyBadgeText"/>).
    /// 둘 다 없으면 빈 문자열(줄을 숨긴다).
    /// </summary>
    public static string SlotLineText(string tooltipSubtitle, string key)
    {
        if (!string.IsNullOrWhiteSpace(tooltipSubtitle))
            return tooltipSubtitle;
        return string.IsNullOrEmpty(key) ? string.Empty : KeyBadgeText(key);
    }

    /// <summary>쿨타임 숫자 — <see cref="UiStrings.Seconds"/>(소수 최대 한 자리). 시계 아이콘은 옆 Image(<c>CooldownIcon</c>)다.</summary>
    public static string CooldownText(float seconds) => UiStrings.Seconds(seconds, "0.#");

    /// <summary>
    /// 칩 폭들을 왼쪽 위부터 가로로 놓고 <paramref name="maxWidth"/> 를 넘으면 다음 줄로 넘긴다.
    /// <paramref name="positions"/> = 칩 왼쪽 위(아래쪽이 음수), 폭이 <paramref name="maxWidth"/> 보다 큰 칩은 그 폭으로 줄인다(<paramref name="widths"/> 를 고친다).
    /// 반환 = 전체 높이(칩 없으면 0).
    /// </summary>
    public static float FlowTags(List<float> widths, float maxWidth, float spacing, float rowHeight, List<Vector2> positions)
    {
        positions.Clear();
        if (widths.Count == 0)
            return 0f;

        maxWidth = Mathf.Max(1f, maxWidth);
        float x = 0f;
        float y = 0f;
        for (int i = 0; i < widths.Count; i++)
        {
            float width = Mathf.Min(widths[i], maxWidth);
            widths[i] = width;
            if (x > 0f && x + width > maxWidth)
            {
                x = 0f;
                y -= rowHeight + spacing;
            }

            positions.Add(new Vector2(x, y));
            x += width + spacing;
        }

        return -y + rowHeight;
    }

    private void RefreshTags(string tagIds)
    {
        if (tagRow == null || tagChipTemplate == null)
            return;

        if (!string.Equals(parsedTagIds, tagIds, System.StringComparison.Ordinal))
        {
            parsedTagIds = tagIds;
            // 테이블 Play·빌드는 검증에서 막는다. 인스펙터 값이 틀렸으면 아는 태그만 그리고 경고 한 번.
            if (!SkillTooltipTags.TryParse(tagIds, tags, out string tagError) && !loggedTagError)
            {
                loggedTagError = true;
                Debug.LogWarning($"[SkillTooltip] {source?.TooltipValueSource?.name}: {tagError}", source?.TooltipValueSource);
            }
        }

        while (tagChips.Count < tags.Count)
        {
            RectTransform chip = Instantiate(tagChipTemplate, tagRow);
            chip.name = tagChipTemplate.name + "_" + tagChips.Count;
            tagChips.Add(chip);
        }

        tagWidths.Clear();
        for (int i = 0; i < tagChips.Count; i++)
        {
            bool used = i < tags.Count;
            tagChips[i].gameObject.SetActive(used);
            if (!used)
                continue;

            TMP_Text label = tagChips[i].GetComponentInChildren<TMP_Text>(true);
            string text = SkillTooltipTags.Name(tags[i]).Text;
            float width = tagChipPadding * 2f;
            if (label != null)
            {
                label.text = text;
                width += label.GetPreferredValues(text).x;
            }
            tagWidths.Add(width);
        }

        float rowHeight = tagChipTemplate.rect.height;
        float height = FlowTags(tagWidths, tagRow.rect.width, tagSpacing, rowHeight, tagPositions);
        for (int i = 0; i < tags.Count; i++)
        {
            RectTransform chip = tagChips[i];
            chip.anchorMin = chip.anchorMax = chip.pivot = new Vector2(0f, 1f);
            chip.anchoredPosition = tagPositions[i];
            chip.sizeDelta = new Vector2(tagWidths[i], rowHeight);
        }

        tagRow.gameObject.SetActive(tags.Count > 0);
        if (headerLayout != null)
        {
            float tagBottom = -tagRow.anchoredPosition.y + height + headerBottomPadding;
            float headerHeight = Mathf.Max(headerMinHeight, tags.Count > 0 ? tagBottom : 0f);
            headerLayout.minHeight = headerHeight;
            headerLayout.preferredHeight = headerHeight;
        }
    }

    /// <summary>툴팁 문구 오류 표시({error} = 포매터 오류 — 개발용 문장이라 번역하지 않는다).</summary>
    public static string ErrorText(string error) =>
        UiStrings.SkillTooltipError.Format(new System.Collections.Generic.Dictionary<string, string> { ["error"] = error ?? string.Empty });

    private void Reposition()
    {
        if (rectTransform == null || canvasRect == null || anchor == null)
            return;

        Vector3[] corners = new Vector3[4];
        anchor.GetWorldCorners(corners);
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, (corners[1] + corners[2]) * 0.5f);
        screen.y += anchorGap;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, canvas.worldCamera, out Vector2 local))
            return;

        Rect bounds = canvasRect.rect;
        Rect tip = rectTransform.rect;
        Vector2 pivot = rectTransform.pivot;
        float minX = bounds.xMin + screenPadding.x + tip.width * pivot.x;
        float maxX = bounds.xMax - screenPadding.x - tip.width * (1f - pivot.x);
        float minY = bounds.yMin + screenPadding.y + tip.height * pivot.y;
        float maxY = bounds.yMax - screenPadding.y - tip.height * (1f - pivot.y);
        rectTransform.anchoredPosition = new Vector2(
            Mathf.Clamp(local.x, minX, maxX),
            Mathf.Clamp(local.y, minY, maxY));
    }

    private static bool IsShiftHeld() =>
        Keyboard.current != null &&
        (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
}
