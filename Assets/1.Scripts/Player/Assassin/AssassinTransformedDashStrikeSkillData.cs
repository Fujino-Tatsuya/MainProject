using UnityEngine;

/// <summary>
/// 변신 Q 관통 돌진 설계값(character_assassin.md §7.2·§7.3). 이동·폭·계수는 일반 Q 와 같고 쿨 3초,
/// 사용당 최초 유효 적중(보스·몹·송전기)에서 기본 쿨의 50% = 1.5초를 1회 차감한다.
/// </summary>
[CreateAssetMenu(fileName = "AssassinTransformedDashStrikeSkillData", menuName = "Player/Assassin/Dash Strike Skill Data (변신 Q)")]
[DataTableSheet("Assassin", Order = 6)]
public sealed class AssassinTransformedDashStrikeSkillData : AssassinDashStrikeSkillData
{
    [Header("적중 보상")]
    [SerializeField, Min(0f)] private float hitCooldownReduction = 1.5f;

    public override float HitCooldownReduction => hitCooldownReduction;
}
