using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로컬 플레이어의 HP 바 + 실드 바(별도 바). Unit의 복제 스탯을 매 프레임 폴링한다.
/// 실드는 상한(MaxShield) 개념이 없으므로 바 비율은 최대 HP 대비로 그리고, 수치는 절대량을 표시한다.
/// 실드가 0이면 실드 바 전체를 숨긴다.
/// </summary>
public class PlayerHealthHUD : MonoBehaviour
{
    [SerializeField] private Image hpFill;
    [SerializeField] private TMP_Text hpText;
    [SerializeField] private GameObject shieldBar;
    [SerializeField] private Image shieldFill;
    [SerializeField] private TMP_Text shieldText;

    private Player player;

    public void Bind(Player boundPlayer)
    {
        player = boundPlayer;
        Refresh();
    }

    private void Update()
    {
        Refresh();
    }

    private void Refresh()
    {
        int hp = 0;
        int maxHp = 0;
        int shield = 0;

        if (player != null)
        {
            hp = player.CurrentHealth;
            maxHp = player.FinalMaxHp;
            shield = player.CurrentShield;
        }

        if (hpFill != null)
            hpFill.fillAmount = maxHp > 0 ? Mathf.Clamp01((float)hp / maxHp) : 0f;

        if (hpText != null)
            hpText.text = maxHp > 0 ? $"{hp}/{maxHp}" : string.Empty;

        bool hasShield = shield > 0;
        if (shieldBar != null && shieldBar.activeSelf != hasShield)
            shieldBar.SetActive(hasShield);

        if (!hasShield)
            return;

        if (shieldFill != null)
            shieldFill.fillAmount = maxHp > 0 ? Mathf.Clamp01((float)shield / maxHp) : 0f;

        if (shieldText != null)
            shieldText.text = shield.ToString();
    }
}
