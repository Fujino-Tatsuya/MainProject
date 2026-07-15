using UnityEngine;

/// <summary>
/// E 수호자의 의지 설계값. 보호막 수치/지속시간은 기획서 TBD — 에셋에서 조정한다.
/// </summary>
[CreateAssetMenu(menuName = "Combat/Skills/Guardian Will Skill Data (수호자의 의지)")]
public class GuardianWillSkillData : PlayerSkillData
{
    [Header("수호자의 의지")]
    [SerializeField, Min(0)] private int shieldAmount = 10;
    // 0 이하면 시간 만료 없음 (사망/소진으로만 소멸)
    [SerializeField, Min(0f)] private float shieldDuration = 5f;

    public int ShieldAmount => shieldAmount;
    public float ShieldDuration => shieldDuration;
}
