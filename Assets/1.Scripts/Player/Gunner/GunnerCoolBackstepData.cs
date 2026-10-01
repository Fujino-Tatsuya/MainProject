using UnityEngine;

/// <summary>거너 E 냉각 백스텝 수치(character_gunner.md §7·§12.4). 베이스의 cooldownTime·animatorStateName 도 쓴다.</summary>
[CreateAssetMenu(fileName = "GunnerCoolBackstepData", menuName = "Player/Gunner/Cool Backstep Data")]
public class GunnerCoolBackstepData : PlayerSkillData
{
    [Tooltip("이동 거리(m). 막히면 그 전에서 멈춘다.")]
    [SerializeField, Min(0f)] private float distance = 3f;

    [Tooltip("이동 시간(초) — 이 시간이 지나면 E 동작이 끝난다.")]
    [SerializeField, Min(0.01f)] private float moveDuration = 0.25f;

    public float Distance => distance;
    public float MoveDuration => moveDuration;
}
