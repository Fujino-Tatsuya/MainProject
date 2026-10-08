using UnityEngine;

/// <summary>
/// R 변신(Parry_R) 설계값. 쿨타임(8초)은 실제 변신 종료 시점부터라 <c>commitCooldownManually</c> = true(§10.2).
/// 스택·지속 표는 <see cref="AssassinStateData"/>.
/// </summary>
[CreateAssetMenu(fileName = "AssassinTransformSkillData", menuName = "Player/Assassin/Transform Skill Data (R)")]
[DataTableSheet("Assassin", Order = 5)]
public sealed class AssassinTransformSkillData : PlayerSkillData
{
}
