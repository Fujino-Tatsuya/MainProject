using UnityEngine;

/// <summary>
/// 변신 E 원형 5타 설계값(character_assassin.md §9.3). 타당 계수 ×2.8·쿨 12초·조준(고정 거리 1.5m·효과 반경 2m)은 베이스 필드 —
/// 원 중심 거리 = <c>castRange</c>, 원 반경 = <c>aoeRadius</c>. 쿨타임은 확정(= 공격 시작, 서버 승인) 시점이라
/// <c>commitCooldownManually</c> = false — 조준만 하고 취소하면 쿨이 없다.
/// </summary>
[CreateAssetMenu(fileName = "AssassinCircleStrikeSkillData", menuName = "Player/Assassin/Circle Strike Skill Data (변신 E)")]
[DataTableSheet("Assassin", Order = 4)]
public sealed class AssassinCircleStrikeSkillData : PlayerSkillData
{
    [Header("원형 연타")]
    [Tooltip("동일 피해 타격 수. 클립 Hit 이벤트마다 1타, End 가 먼저 오면 남은 타를 마저 친다")]
    [SerializeField, Min(1)] private int hitCount = 5;
    [Tooltip("판정 원기둥 높이(m). 확정 순간 플레이어 발 높이부터 위로")]
    [SerializeField, Min(0.1f), DataTableIgnore] private float areaHeight = 3f;
    [SerializeField, Min(1), DataTableIgnore] private int maxHitResults = 32;

    public int HitCount => hitCount;
    public float AreaRadius => AoeRadius;
    public float CenterDistance => CastRange;
    public float AreaHeight => areaHeight;
    public int MaxHitResults => maxHitResults;
}
