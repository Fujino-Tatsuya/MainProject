using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 패시브 HUD. 로컬 플레이어의 패시브(IPlayerPassive — 캐릭터 무관)를 바인딩해
/// 쿨다운 진행도(fill)와 Ready 강조를 표시한다. 오너 전용 상태(남은 쿨타임)를 읽으므로
/// 로컬 플레이어에만 유효 — CombatHUD가 Player.LocalPlayer로 바인딩한다.
/// </summary>
public class PassiveHUD : MonoBehaviour
{
    [Tooltip("남은 쿨다운 비율만큼 덮는 Filled 타입 오버레이 (스킬 쿨타임 HUD와 동일).")]
    [SerializeField] private Image cooldownFill;

    [Tooltip("패시브 아이콘. Ready 여부에 따라 색을 바꾼다.")]
    [SerializeField] private Image icon;

    [SerializeField] private SkillSlotHover hover;

    [Tooltip("남은 쿨다운(초) 텍스트. 스킬 쿨타임 HUD와 동일 포맷. 선택.")]
    [SerializeField] private TMP_Text remainingText;

    [Tooltip("Ready일 때 켜질 강조 오브젝트(글로우 등). 선택.")]
    [SerializeField] private GameObject readyHighlight;

    [SerializeField] private Color readyColor = Color.white;
    [SerializeField] private Color cooldownColor = new Color(1f, 1f, 1f, 0.4f);

    private IPlayerPassive passive;
    private ISkillTooltipSource tooltipSource;

    public void Bind(Player player)
    {
        passive = player != null ? player.GetComponent<IPlayerPassive>() : null;
        tooltipSource = passive as ISkillTooltipSource;
        if (tooltipSource == null && player != null)
            tooltipSource = player.GetComponent<GunnerHeat>()?.Data;

        SkillCooldownHUD.SetSlotIcon(icon, tooltipSource);

        hover?.Bind(
            player,
            tooltipSource,
            PlayerSkillSlot.Main,
            acceptsLeftClick: false,
            displayKey: string.Empty,
            cooldownSeconds: passive != null ? passive.CooldownTime : (float?)null);
        Refresh();
    }

    private void Update()
    {
        Refresh();
    }

    private void Refresh()
    {
        float remaining = 0f;
        float total = 0f;
        bool ready = false;

        if (passive != null)
        {
            remaining = passive.RemainingCooldown;
            total = passive.CooldownTime;
            ready = passive.IsReady;
        }

        if (cooldownFill != null)
            cooldownFill.fillAmount = total > 0f ? Mathf.Clamp01(remaining / total) : 0f;

        if (icon != null)
            icon.color = passive == null && tooltipSource != null
                ? readyColor
                : ready ? readyColor : cooldownColor;

        if (remainingText != null)
        {
            // 스킬 쿨타임 HUD와 동일 포맷: 1초 미만은 0.1초 단위, 1초 이상은 올림 정수, 0이면 숨김
            remainingText.text = remaining <= 0f
                ? string.Empty
                : remaining < 1f ? remaining.ToString("F1") : Mathf.CeilToInt(remaining).ToString();
        }

        if (readyHighlight != null && readyHighlight.activeSelf != ready)
            readyHighlight.SetActive(ready);
    }
}
