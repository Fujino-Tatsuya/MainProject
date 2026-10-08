using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 스킬 슬롯 4개(Q/E/우클릭/R)의 쿨타임 표시.
/// PlayerSkillController의 쿨타임 장부를 매 프레임 폴링한다.
/// 오너 클라이언트 장부는 PlaySkillClientRpc 수신 시점에 미러링된다(표시용, 서버 검증과 무관).
/// </summary>
public class SkillCooldownHUD : MonoBehaviour, ICombatUiBlockedStateView
{
    [System.Serializable]
    private class SlotWidget
    {
        public PlayerSkillSlot slot;
        public Image icon = null;
        public Image cooldownFill;   // Filled 타입 오버레이 — 남은 쿨타임 비율만큼 덮는다
        public TMP_Text remainingText;
        public SkillSlotHover hover = null;

        [System.NonSerialized] public Graphic[] graphics;
        [System.NonSerialized] public Color[] normalColors;
        [System.NonSerialized] public Sprite defaultIcon;
        [System.NonSerialized] public bool defaultIconCached;
    }

    [SerializeField] private SlotWidget[] slots;
    [SerializeField] private Color blockedColor = new Color(0.35f, 0.35f, 0.35f, 0.75f);

    private PlayerSkillController skillController;
    private Player boundPlayer;
    private bool isBlocked;

    private void Awake()
    {
        CacheSlotColors();
        CacheDefaultIcons();
    }

    public void Bind(Player player)
    {
        if (skillController != null)
            skillController.SlotBindingChanged -= HandleSlotBindingChanged;

        boundPlayer = player;
        skillController = player != null ? player.GetComponent<PlayerSkillController>() : null;
        if (skillController != null)
            skillController.SlotBindingChanged += HandleSlotBindingChanged;

        BindTooltipSlots(player);
        Refresh();
    }

    private void OnDestroy()
    {
        if (skillController != null)
            skillController.SlotBindingChanged -= HandleSlotBindingChanged;
    }

    private void BindTooltipSlots(Player player)
    {
        if (slots == null)
            return;

        foreach (SlotWidget widget in slots)
        {
            if (widget == null)
                continue;

            BindTooltipSlot(widget, player);
        }
    }

    private void BindTooltipSlot(SlotWidget widget, Player player)
    {
        PlayerSkillData data = skillController != null ? skillController.GetSkill(widget.slot)?.Data : null;
        ISkillTooltipSource source = data;
        CacheDefaultIcon(widget);

        if (widget.icon != null)
        {
            Sprite skillIcon = source != null ? source.Tooltip.Icon : null;
            widget.icon.sprite = skillIcon != null ? skillIcon : widget.defaultIcon;
        }

        widget.hover?.Bind(
            player,
            source,
            widget.slot,
            acceptsLeftClick: true,
            displayKey: KeyLabel(widget.slot),
            cooldownSeconds: data != null ? data.CooldownTime : (float?)null,
            fallbackIcon: widget.icon != null ? widget.icon.sprite : null);
    }

    private void HandleSlotBindingChanged(PlayerSkillSlot slot)
    {
        if (slots == null)
            return;

        foreach (SlotWidget widget in slots)
        {
            if (widget != null && widget.slot == slot)
                BindTooltipSlot(widget, boundPlayer);
        }
    }

    /// <summary>
    /// 슬롯 프레임 안 마스크 밑 아이콘(<c>Slot_Q/Icon_mask/Icon_Q</c> 등)만 바꾼다 — 프레임 Image 는 건드리지 않는다.
    /// 출처에 아이콘이 없으면 CombatHUD 에 설정된 값을 그대로 둔다(예: 우클릭 — 캐릭터 공용 아이콘).
    /// </summary>
    public static void SetSlotIcon(Image icon, ISkillTooltipSource source)
    {
        Sprite sprite = source != null ? source.Tooltip.Icon : null;
        if (icon != null && sprite != null)
            icon.sprite = sprite;
    }

    private static string KeyLabel(PlayerSkillSlot slot) => slot switch
    {
        PlayerSkillSlot.Main => "Q",
        PlayerSkillSlot.Sub => "E",
        PlayerSkillSlot.Interrupt => "RMB",
        PlayerSkillSlot.Ultimate => "R",
        _ => string.Empty,
    };

    /// <summary>
    /// 사용 불가 색상만 전환한다. Refresh는 계속 실행되므로 Cooldown Fill은 멈추지 않는다.
    /// </summary>
    public void SetBlocked(bool blocked)
    {
        if (isBlocked == blocked)
            return;

        CacheSlotColors();
        isBlocked = blocked;

        if (slots == null)
            return;

        foreach (SlotWidget widget in slots)
        {
            if (widget == null || widget.graphics == null)
                continue;

            for (int i = 0; i < widget.graphics.Length; i++)
            {
                if (widget.graphics[i] != null)
                    widget.graphics[i].color = blocked
                        ? blockedColor
                        : widget.normalColors[i];
            }
        }
    }

    private void Update()
    {
        Refresh();
    }

    private void Refresh()
    {
        if (slots == null)
            return;

        foreach (SlotWidget widget in slots)
        {
            if (widget == null)
                continue;

            float remaining = 0f;
            float total = 0f;

            if (skillController != null)
            {
                PlayerSkillBase skill = skillController.GetSkill(widget.slot);
                if (skill != null && skill.Data != null)
                {
                    remaining = skillController.GetCooldownRemaining(widget.slot);
                    total = skill.Data.CooldownTime;
                }
            }

            if (widget.cooldownFill != null)
                widget.cooldownFill.fillAmount = total > 0f ? Mathf.Clamp01(remaining / total) : 0f;

            if (widget.remainingText != null)
            {
                // 1초 미만은 소수점(0.1초 단위), 1초 이상은 정수(올림), 0이면 숨김
                widget.remainingText.text = remaining <= 0f
                    ? string.Empty
                    : remaining < 1f ? remaining.ToString("F1") : Mathf.CeilToInt(remaining).ToString();
            }
        }
    }

    private void CacheSlotColors()
    {
        if (slots == null)
            return;

        foreach (SlotWidget widget in slots)
        {
            if (widget == null ||
                widget.graphics != null ||
                widget.cooldownFill == null)
            {
                continue;
            }

            // 기존 프리팹에서 CooldownFill의 부모가 슬롯 루트다.
            Transform slotRoot = widget.cooldownFill.transform.parent;
            if (slotRoot == null)
                continue;

            widget.graphics = slotRoot.GetComponentsInChildren<Graphic>(true);
            widget.normalColors = new Color[widget.graphics.Length];

            for (int i = 0; i < widget.graphics.Length; i++)
                widget.normalColors[i] = widget.graphics[i].color;
        }
    }

    private void CacheDefaultIcons()
    {
        if (slots == null)
            return;

        foreach (SlotWidget widget in slots)
            CacheDefaultIcon(widget);
    }

    private static void CacheDefaultIcon(SlotWidget widget)
    {
        if (widget == null || widget.defaultIconCached)
            return;

        widget.defaultIcon = widget.icon != null ? widget.icon.sprite : null;
        widget.defaultIconCached = true;
    }
}
