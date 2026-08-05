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
    [SerializeField] private DelayedHealthBar delayed = new DelayedHealthBar();

    private Player player;
    private bool displayOverrideZero;

    public void Bind(Player boundPlayer)
    {
        if (player != null)
            player.ClientHpChanged -= delayed.OnHpChanged;

        player = boundPlayer;

        if (player != null)
            player.ClientHpChanged += delayed.OnHpChanged;

        delayed.Bind(player != null ? player.CurrentHealth : 0);
        Refresh();
    }

    /// <summary>
    /// HP/Shield의 실제 복제값을 변경하지 않고 HUD 표시만 0으로 덮는다.
    /// Soul 표현 정책에서 사용한다.
    /// </summary>
    public void SetDisplayOverrideZero(bool shouldOverride)
    {
        displayOverrideZero = shouldOverride;
        if (shouldOverride)
            delayed.Bind(0);
        Refresh();
    }

    private void Update()
    {
        Refresh();

        int hp = player != null && !displayOverrideZero ? player.CurrentHealth : 0;
        int maxHp = player != null ? player.FinalMaxHp : 0;
        delayed.Tick(Time.deltaTime, hp, maxHp);
    }

    private void OnDisable()
    {
        if (player != null)
            player.ClientHpChanged -= delayed.OnHpChanged;
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

        if (displayOverrideZero)
        {
            hp = 0;
            shield = 0;
        }

        if (hpFill != null)
            hpFill.fillAmount = maxHp > 0 ? Mathf.Clamp01((float)hp / maxHp) : 0f;

        if (hpText != null)
            hpText.text = maxHp > 0 ? $"{hp}/{maxHp}" : string.Empty;

        // Soul에서는 실제 Shield가 없어도 0 표기를 유지한다.
        bool hasShield = displayOverrideZero || shield > 0;
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
