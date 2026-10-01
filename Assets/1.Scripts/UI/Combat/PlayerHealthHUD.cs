using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로컬 플레이어의 HP 바 + 실드 바(별도 바). Unit의 복제 스탯을 매 프레임 폴링한다.
/// 실드 바 비율 = 남은 실드 합 / 지금 걸린 실드들이 부여한 양의 합(Unit 복제 인스턴스 목록 기준), 수치는 절대량을 표시한다.
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

    /// <summary>
    /// OnDisable에서 끊은 구독을 되살린다. 이 HUD의 GameObject만 껐다 켜는 경우
    /// Bind가 다시 불리지 않아 잔상이 조용히 죽기 때문에 필요하다.
    /// (CombatHUD 루트 토글은 CombatHUD.OnEnable → Bind로 이미 복구된다.)
    /// </summary>
    private void OnEnable()
    {
        if (player == null)
            return;

        // 이미 구독된 상태에서 재진입해도 중복되지 않게 한 번 떼고 붙인다.
        player.ClientHpChanged -= delayed.OnHpChanged;
        player.ClientHpChanged += delayed.OnHpChanged;
        delayed.Bind(player.CurrentHealth);
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

        // 비율 = 남은 보호막 합 / 지금 걸린 보호막들이 부여한 양의 합. 최대 HP 를 기준으로 두면 보호막이 HP 보다 클 때
        // 바가 꽉 찬 채 멈춰 있다가 HP 아래로 내려와서야 줄기 시작한다. 새 보호막이 붙으면 분모도 함께 커진다.
        if (shieldFill != null)
        {
            int remaining = 0;
            int granted = 0;
            if (player != null && !displayOverrideZero)
                player.GetReplicatedShieldTotals(out remaining, out granted);
            shieldFill.fillAmount = granted > 0 ? Mathf.Clamp01((float)remaining / granted) : 0f;
        }

        if (shieldText != null)
            shieldText.text = shield.ToString();
    }
}
