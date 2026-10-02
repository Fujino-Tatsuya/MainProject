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
        visible = false;
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
        visible = false;
        if (panel != null)
            panel.SetActive(false);
    }

    private void Update()
    {
        if (owner == null || source == null || anchor == null)
            return;

        if (!visible)
        {
            if (Time.unscaledTime - hoverStartedAt < showDelay)
                return;

            visible = true;
            if (panel != null)
                panel.SetActive(true);
        }

        RefreshContent(IsShiftHeld());
        Canvas.ForceUpdateCanvases();
        Reposition();
    }

    private void RefreshContent(bool detailed)
    {
        SkillTooltipText tooltip = source.Tooltip;
        if (icon != null)
            icon.sprite = tooltip.Icon;

        if (keyBadge != null)
        {
            bool hasKey = !string.IsNullOrEmpty(displayKey);
            keyBadge.gameObject.SetActive(hasKey);
            keyBadge.text = hasKey ? displayKey : string.Empty;
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
            cooldownText.text = hasCooldown ? $"⏱ {cooldown.Value:0.#}초" : string.Empty;
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
            description.text = ok ? formatted : $"[오류: {error}]";

        if (!ok && !loggedError)
        {
            loggedError = true;
            Debug.LogWarning($"[SkillTooltip] {source.TooltipValueSource?.name}: {error}", source.TooltipValueSource);
        }

        if (shiftHint != null)
        {
            shiftHint.gameObject.SetActive(ok && hasDamage && !detailed);
            shiftHint.text = "자세한 정보를 보려면 [Shift] 키를 누르세요";
        }
    }

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
