using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>스킬 칸의 호버·클릭을 툴팁과 PlayerInputReader 가상 입력으로 연결한다.</summary>
public sealed class SkillSlotHover : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    private static readonly HashSet<int> Hovering = new HashSet<int>();
    private static readonly HashSet<int> Pressing = new HashSet<int>();
    private static readonly HashSet<SkillSlotHover> ActiveSlots = new HashSet<SkillSlotHover>();

    [SerializeField] private SkillTooltipView tooltipView;

    private Player player;
    private PlayerInputReader input;
    private ISkillTooltipSource source;
    private PlayerSkillSlot slot;
    private bool clickable;
    private bool pointerInside;
    private bool leftPressActive;
    private float? cooldown;
    private string keyLabel;

    /// <summary>스킬 칸 위 좌클릭이 기본 공격·조준 확정으로 새는 것을 막는 전역 게이트.</summary>
    public static bool BlocksPrimaryInput => Hovering.Count > 0 || Pressing.Count > 0 || IsPointerInsideActiveSlot();

    private void OnEnable() => ActiveSlots.Add(this);

    public void Bind(
        Player owner,
        ISkillTooltipSource tooltipSource,
        PlayerSkillSlot inputSlot,
        bool acceptsLeftClick,
        string displayKey,
        float? cooldownSeconds)
    {
        player = owner;
        input = owner != null ? owner.GetComponent<PlayerInputReader>() : null;
        source = tooltipSource;
        slot = inputSlot;
        clickable = acceptsLeftClick;
        keyLabel = displayKey;
        cooldown = cooldownSeconds;

        if (tooltipView == null)
        {
            CombatHUD hud = GetComponentInParent<CombatHUD>();
            tooltipView = hud != null ? hud.GetComponentInChildren<SkillTooltipView>(true) : null;
        }

        if (pointerInside)
            tooltipView?.RefreshHover(this, source, player, (RectTransform)transform, keyLabel, cooldown);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        pointerInside = true;
        Hovering.Add(GetInstanceID());
        tooltipView?.BeginHover(this, source, player, transform as RectTransform, keyLabel, cooldown);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        pointerInside = false;
        Hovering.Remove(GetInstanceID());
        tooltipView?.EndHover(this);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || !clickable || source == null)
            return;

        leftPressActive = true;
        Pressing.Add(GetInstanceID());
        input?.SetVirtualSkillInput(slot, true);
        eventData.Use();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || !leftPressActive)
            return;

        ReleaseVirtualInput();
        eventData.Use();
    }

    private void OnDisable()
    {
        ActiveSlots.Remove(this);
        pointerInside = false;
        Hovering.Remove(GetInstanceID());
        tooltipView?.EndHover(this);
        ReleaseVirtualInput();
    }

    private void ReleaseVirtualInput()
    {
        if (!leftPressActive)
            return;

        leftPressActive = false;
        Pressing.Remove(GetInstanceID());
        input?.SetVirtualSkillInput(slot, false);
    }

    private static bool IsPointerInsideActiveSlot()
    {
        if (Mouse.current == null)
            return false;

        Vector2 position = Mouse.current.position.ReadValue();
        foreach (SkillSlotHover candidate in ActiveSlots)
        {
            if (candidate == null || !candidate.isActiveAndEnabled || !(candidate.transform is RectTransform rect))
                continue;

            Canvas canvas = candidate.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            if (RectTransformUtility.RectangleContainsScreenPoint(rect, position, camera))
                return true;
        }

        return false;
    }
}
