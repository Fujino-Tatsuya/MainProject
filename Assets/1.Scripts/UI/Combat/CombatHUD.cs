using UnityEngine;

/// <summary>
/// 전투 HUD 루트. 로컬 플레이어 스폰/교체(Player.LocalPlayerChanged)를 구독해
/// 하위 위젯에 바인딩한다. 씬 상주 Screen Space 캔버스 프리팹에 부착.
/// </summary>
public class CombatHUD : MonoBehaviour
{
    [SerializeField] private PlayerHealthHUD playerHealthHUD;
    [SerializeField] private StatusEffectHUD statusEffectHUD;
    [SerializeField] private PassiveHUD passiveHUD;
    [SerializeField] private SkillCooldownHUD skillCooldownHUD;
    [SerializeField] private DashCooldownHUD dashCooldownHUD;


    private void OnEnable()
    {
        Player.LocalPlayerChanged += Bind;
        Bind(Player.LocalPlayer);
    }

    private void OnDisable()
    {
        Player.LocalPlayerChanged -= Bind;
    }

    private void Bind(Player player)
    {
        if (skillCooldownHUD != null)
            skillCooldownHUD.Bind(player);

        if (dashCooldownHUD != null)
            dashCooldownHUD.Bind(player);

        if (playerHealthHUD != null)
            playerHealthHUD.Bind(player);

        if (statusEffectHUD != null)
            statusEffectHUD.Bind(player);

        if (passiveHUD != null)
            passiveHUD.Bind(player);
    }
}
