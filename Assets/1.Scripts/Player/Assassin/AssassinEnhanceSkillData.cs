using UnityEngine;

/// <summary>
/// 일반 E 단검 강화(Buff) 설계값. 쿨타임(6초)은 준비가 아니라 강타 시작·강화 제거 때 시작하므로
/// <c>commitCooldownManually</c> = true 로 둔다(character_assassin.md §8).
/// </summary>
[CreateAssetMenu(fileName = "AssassinEnhanceSkillData", menuName = "Player/Assassin/Enhance Skill Data (일반 E)")]
[DataTableSheet("Assassin", Order = 3)]
public sealed class AssassinEnhanceSkillData : PlayerSkillData
{
}
