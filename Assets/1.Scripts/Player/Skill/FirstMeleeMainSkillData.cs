using UnityEngine;

/// <summary>
/// Q 진격의 방패 설계값. 수치는 전부 임시값 — 기획 확정 시 에셋에서만 조절한다.
/// 베이스(PlayerSkillData)의 InputType은 Hold, TickInterval은 0보다 커야 한다.
/// </summary>
[CreateAssetMenu(menuName = "Combat/First Melee Main Skill Data")]
public class FirstMeleeMainSkillData : PlayerSkillData
{
    [Header("진격")]
    [SerializeField, Min(0f)] private float advanceSpeed = 6f;
    // 스킬 틱(TickInterval)당 에임 방향으로 조향되는 최대 각도(도). 프레임에서는 시간 비례로 나눠 적용된다.
    [SerializeField, Min(0f)] private float steerAnglePerTick = 5f;

    [Header("견인")]
    [SerializeField, Min(0f)] private float knockbackStrength = 3f;
    [SerializeField, Min(1)] private int maxHitResults = 16;

    public float AdvanceSpeed => advanceSpeed;
    public float SteerAnglePerTick => steerAnglePerTick;
    public float KnockbackStrength => knockbackStrength;
    public int MaxHitResults => maxHitResults;
}
