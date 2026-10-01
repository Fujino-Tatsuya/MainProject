using UnityEngine;

/// <summary>
/// 거너 R 추적 레이저 수치(character_gunner.md §9·§12.6). 베이스의 cooldownTime·targetingMode(SingleTarget)·castRange·
/// targetableLayers·hittableLayers·attackDamageMultiplier(틱 피해 = 최종 공격력 × 이 배율)·animatorStateName 도 쓴다.
/// </summary>
[CreateAssetMenu(fileName = "GunnerTrackingLaserData", menuName = "Player/Gunner/Tracking Laser Data")]
public class GunnerTrackingLaserData : PlayerSkillData
{
    [Tooltip("생성할 추적 레이저(NetworkObject 프리팹).")]
    [SerializeField] private GameObject laserPrefab;

    [Tooltip("시전 모션(초) — 레이저는 확정 즉시 생성되고, 이 시간 뒤 플레이어가 자유로워진다. 0 이면 즉시.")]
    [SerializeField, Min(0f)] private float castDuration = 0.3f;

    [Header("레이저")]
    [SerializeField, Min(0.1f)] private float laserDuration = 6f;
    [SerializeField, Min(0f)] private float moveSpeed = 4f;
    [SerializeField, Min(0.1f)] private float radius = 2f;
    [Tooltip("지속 피해 간격(초).")]
    [SerializeField, Min(0.05f)] private float damageInterval = 0.5f;
    [Tooltip("저장된 과열 단계별 피해 배율 — [기본, 1, 2, 3].")]
    [SerializeField] private float[] stageDamageMultipliers = { 1f, 1.15f, 1.3f, 1.5f };

    [Header("재탐색")]
    [SerializeField, Min(0f)] private float retargetRadius = 8f;
    [SerializeField, Min(0.05f)] private float retargetInterval = 0.5f;

    public GameObject LaserPrefab => laserPrefab;
    public float CastDuration => castDuration;
    public float LaserDuration => laserDuration;
    public float MoveSpeed => moveSpeed;
    public float Radius => radius;
    public float DamageInterval => damageInterval;
    public float RetargetRadius => retargetRadius;
    public float RetargetInterval => retargetInterval;

    public float StageDamageMultiplier(int stage)
    {
        if (stageDamageMultipliers == null || stageDamageMultipliers.Length == 0)
            return 1f;
        return stageDamageMultipliers[Mathf.Clamp(stage, 0, stageDamageMultipliers.Length - 1)];
    }
}
