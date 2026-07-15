using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 스킬 슬롯 4개(Q/E/우클릭/R)의 쿨타임 표시.
/// PlayerSkillController의 쿨타임 장부를 매 프레임 폴링한다.
/// 오너 클라이언트 장부는 PlaySkillClientRpc 수신 시점에 미러링된다(표시용, 서버 검증과 무관).
/// </summary>
public class SkillCooldownHUD : MonoBehaviour
{
    [System.Serializable]
    private class SlotWidget
    {
        public PlayerSkillSlot slot;
        public Image cooldownFill;   // Filled 타입 오버레이 — 남은 쿨타임 비율만큼 덮는다
        public TMP_Text remainingText;
    }

    [SerializeField] private SlotWidget[] slots;

    private PlayerSkillController skillController;

    public void Bind(Player player)
    {
        skillController = player != null ? player.GetComponent<PlayerSkillController>() : null;
        Refresh();
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
                widget.remainingText.text = remaining <= 0f
                    ? string.Empty
                    : remaining < 10f ? remaining.ToString("F1") : Mathf.CeilToInt(remaining).ToString();
            }
        }
    }
}
