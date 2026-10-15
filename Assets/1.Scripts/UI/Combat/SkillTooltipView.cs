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
    [SerializeField] private TMP_Text keyBadge;
    [SerializeField] private TMP_Text displayName;
    [SerializeField] private TMP_Text subtitle;
    [SerializeField] private TMP_Text cooldownText;

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

    /// <summary>
    /// [오너 로컬] 툴팁이 지연 뒤 실제로 떴다. 인자 = 그 슬롯의 키 표기(displayKey) —
    /// Q·E·R·RMB(SkillCooldownHUD), 패시브 칸은 "Passive"(PassiveHUD). 튜토리얼 판정 훅.
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

        if (keyBadge != null)
        {
            bool hasKey = !string.IsNullOrEmpty(displayKey);
            keyBadge.gameObject.SetActive(hasKey);
            keyBadge.text = hasKey ? KeyBadgeText(displayKey) : string.Empty;
        }

        if (displayName != null)
            displayName.text = tooltip.DisplayName;

        if (subtitle != null)
        {
            bool hasSubtitle = !string.IsNullOrWhiteSpace(tooltip.Subtitle);
            subtitle.gameObject.SetActive(hasSubtitle);
            subtitle.text = hasSubtitle ? tooltip.Subtitle : string.Empty;
        }

        if (cooldownText != null)
        {
            bool hasCooldown = cooldown.HasValue;
            cooldownText.gameObject.SetActive(hasCooldown);
            cooldownText.text = hasCooldown ? CooldownText(cooldown.Value) : string.Empty;
        }

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

    /// <summary>쿨타임 줄 — 아이콘 + <see cref="UiStrings.Seconds"/>(소수 최대 한 자리).</summary>
    public static string CooldownText(float seconds) =>
        $"<sprite name=\"cooldown\" color=#B8C4D6> {UiStrings.Seconds(seconds, "0.#")}";

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
