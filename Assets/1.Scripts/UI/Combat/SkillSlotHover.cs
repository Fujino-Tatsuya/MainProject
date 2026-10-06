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
    private PlayerSkillController skillController;
    private SkillLineIndicator lineIndicator;
    private ISkillPreviewSource previewSource;
    private PlayerSkillTargeting targeting;
    private PlayerSkillData rangePreviewData;
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
        EndSkillPreview();
        player = owner;
        input = owner != null ? owner.GetComponent<PlayerInputReader>() : null;
        skillController = owner != null ? owner.GetComponent<PlayerSkillController>() : null;
        lineIndicator = owner != null ? owner.GetComponentInChildren<SkillLineIndicator>(true) : null;
        source = tooltipSource;
        slot = inputSlot;
        targeting = owner != null ? owner.GetComponent<PlayerSkillTargeting>() : null;
        PlayerSkillBase skill = skillController != null ? skillController.GetSkill(slot) : null;
        previewSource = skill as ISkillPreviewSource;
        // 대상 지정 스킬(R 등)은 조준 때와 같은 사거리 원을 호버로 미리 보여준다(지점 지정이면 지점 원도).
        rangePreviewData = skill != null && skill.Data != null &&
                           skill.Data.TargetingMode != SkillTargetingMode.None && skill.Data.CastRange > 0f
            ? skill.Data
            : null;
        clickable = acceptsLeftClick;
        keyLabel = displayKey;
        cooldown = cooldownSeconds;

        if (tooltipView == null)
        {
            CombatHUD hud = GetComponentInParent<CombatHUD>();
            tooltipView = hud != null ? hud.GetComponentInChildren<SkillTooltipView>(true) : null;
        }

        if (pointerInside)
        {
            tooltipView?.BeginHover(this, source, player, (RectTransform)transform, keyLabel, cooldown);
            BeginSkillPreview();
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        pointerInside = true;
        Hovering.Add(GetInstanceID());
        tooltipView?.BeginHover(this, source, player, transform as RectTransform, keyLabel, cooldown);
        BeginSkillPreview();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        pointerInside = false;
        Hovering.Remove(GetInstanceID());
        tooltipView?.EndHover(this);
        EndSkillPreview();
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
        EndSkillPreview();
        ReleaseVirtualInput();
    }

    private void BeginSkillPreview()
    {
        if (previewSource != null)
            lineIndicator?.BeginPreview(previewSource);

        if (rangePreviewData != null)
            targeting?.BeginRangePreview(rangePreviewData);
    }

    private void EndSkillPreview()
    {
        if (previewSource != null)
            lineIndicator?.EndPreview(previewSource);

        if (rangePreviewData != null)
            targeting?.EndRangePreview();
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
