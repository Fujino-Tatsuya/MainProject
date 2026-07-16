using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로컬 플레이어의 HP/실드 바. Unit의 복제 스탯(CurrentHealth/FinalMaxHp/CurrentShield)을 매 프레임 폴링한다.
/// MaxShield는 서버 전용(_health)이라 클라이언트에서 읽을 수 없어, 실드 바는 최대 HP와 같은 스케일로 표시한다.
/// </summary>
public class PlayerHealthHUD : MonoBehaviour
{
    [SerializeField] private Image hpFill;
    [SerializeField] private Image shieldFill;
    [SerializeField] private TMP_Text hpText;

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

        if (shieldFill != null)
            shieldFill.fillAmount = maxHp > 0 ? Mathf.Clamp01((float)shield / maxHp) : 0f;

        if (hpText != null)
        {
            hpText.text = maxHp <= 0
                ? string.Empty
                : shield > 0 ? $"{hp}/{maxHp} (+{shield})" : $"{hp}/{maxHp}";
        }
    }
}
