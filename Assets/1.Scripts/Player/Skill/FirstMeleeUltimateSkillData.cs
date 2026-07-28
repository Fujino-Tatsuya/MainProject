using UnityEngine;

/// <summary>
/// R 최후의 심판 설계값. 채널 시간은 base의 MaxActiveDuration, 사거리는 base의 CastRange,
/// 피해는 base의 AttackDamageMultiplier/FlatDamageBonus를 사용한다(스냅샷 = 최종 공격력 × 계수).
/// TargetingMode는 에셋에서 SingleTarget으로, TargetableLayers는 Enemy로 설정한다.
/// 궁극 고유 추가 튜닝이 생기면 여기 필드를 추가한다.
/// </summary>
[CreateAssetMenu(menuName = "Combat/Skills/FirstMelee Ultimate Skill Data (최후의 심판)")]
public class FirstMeleeUltimateSkillData : PlayerSkillData
{
}
