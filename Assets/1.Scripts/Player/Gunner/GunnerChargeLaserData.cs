using UnityEngine;

/// <summary>
/// 거너 Q 충전 레이저 수치(character_gunner.md §6·§12.3, D3·D4). 베이스의 cooldownTime·hittableLayers·animatorStateName 도 쓴다.
/// 🔴 베이스의 "Commit Cooldown Manually" 를 켜야 한다 — 쿨타임은 발사 순간부터(§6.1-7).
/// </summary>
[CreateAssetMenu(fileName = "GunnerChargeLaserData", menuName = "Player/Gunner/Charge Laser Data")]
[DataTableSheet("Gunner", Order = 2)]
public class GunnerChargeLaserData : PlayerSkillData
{
    [Header("정신 집중")]
    [Tooltip("최소 → 최대까지 걸리는 집중 시간(초). 최소 충전시간은 없다(D3) — 누르자마자 좌클릭해도 최소치로 나간다.")]
    [SerializeField, Min(0.01f)] private float maxChargeTime = 1.2f;

    [Tooltip("최대 도달 후 자동 발사까지 유지시간(초).")]
    [SerializeField, Min(0f)] private float autoFireHoldTime = 0.8f;

    [Tooltip("집중 진행(0~1) → 보간 계수(0~1). 기본 직선.")]
    [SerializeField] private AnimationCurve chargeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Tooltip("집중 중 이동속도 배율.")]
    [SerializeField, Range(0f, 1f)] private float chargeMoveSpeedMultiplier = 0.5f;

    [Header("사거리·판정")]
    [SerializeField, Min(0.1f)] private float minRange = 6f;
    [SerializeField, Min(0.1f)] private float maxRange = 16f;
    [Tooltip("판정 폭(m).")]
    [SerializeField, Min(0.05f)] private float beamWidth = 1.2f;
    [Tooltip("판정 높이(m).")]
    [SerializeField, Min(0.05f)] private float beamHeight = 2f;
    [SerializeField, DataTableIgnore] private float muzzleHeight = 1.0f;
    [Tooltip("지형 마스크 — 비트리거 콜라이더에 닿으면 그 지점에서 레이저가 끝난다.")]
    [SerializeField] private LayerMask blockingLayers = 2177;
    [Tooltip("아군(보호막 대상) 마스크 — 기본 Player 레이어.")]
    [SerializeField] private LayerMask allyLayers = 64;

    [Header("피해 — 최종 공격력 스냅샷에 곱한다(D3: 집중으로 최소→최대 보간)")]
    [SerializeField, Min(0f)] private float minDamageMultiplier = 0.5f;
    [SerializeField, Min(0f)] private float maxDamageMultiplier = 1.5f;
    [Tooltip("저장된 과열 단계별 피해 배율 — [기본, 1, 2, 3].")]
    [SerializeField] private float[] stageDamageMultipliers = { 1f, 1.15f, 1.3f, 1.5f };

    [Header("보호막 — 집중 영향 없음(D4)")]
    [SerializeField, Min(0)] private int shieldAmount = 30;
    [SerializeField, Min(0f)] private float shieldDuration = 5f;
    [Tooltip("저장된 과열 단계별 보호막 배율 — [기본, 1, 2, 3].")]
    [SerializeField] private float[] stageShieldMultipliers = { 1f, 1.15f, 1.3f, 1.5f };

    [Header("발사 후")]
    [Tooltip("발사 후 후속 동작(초) — 이후 스킬 종료.")]
    [SerializeField, Min(0f)] private float fireRecovery = 0.4f;

    public float MaxChargeTime => maxChargeTime;
    public float AutoFireHoldTime => autoFireHoldTime;
    public float ChargeMoveSpeedMultiplier => chargeMoveSpeedMultiplier;
    public float BeamWidth => beamWidth;
    public float BeamHeight => beamHeight;
    public float MuzzleHeight => muzzleHeight;
    public LayerMask BlockingLayers => blockingLayers;
    public LayerMask AllyLayers => allyLayers;
    public int ShieldAmount => shieldAmount;
    public float ShieldDuration => shieldDuration;
    public float FireRecovery => fireRecovery;

    /// <summary>집중 경과(초) → 보간 계수 0~1.</summary>
    public float ChargeFactor(float elapsed) =>
        Mathf.Clamp01(chargeCurve != null ? chargeCurve.Evaluate(Mathf.Clamp01(elapsed / maxChargeTime)) : elapsed / maxChargeTime);

    public float RangeAt(float factor) => Mathf.Lerp(minRange, maxRange, factor);
    public float DamageMultiplierAt(float factor) => Mathf.Lerp(minDamageMultiplier, maxDamageMultiplier, factor);
    public float StageDamageMultiplier(int stage) => Pick(stageDamageMultipliers, stage);
    public float StageShieldMultiplier(int stage) => Pick(stageShieldMultipliers, stage);

    private static float Pick(float[] table, int stage)
    {
        if (table == null || table.Length == 0)
            return 1f;
        return table[Mathf.Clamp(stage, 0, table.Length - 1)];
    }

    protected override SkillTooltipDamage CalculateTooltipDamage(Player player)
    {
        if (player == null)
            return default;

        float minScale = DamageMultiplierAt(0f) * StageDamageMultiplier(0);
        float maxScale = DamageMultiplierAt(1f) * StageDamageMultiplier(int.MaxValue);
        return SkillTooltipDamage.FromScaledSnapshot(
            player.FinalAttackDamage, AttackDamageMultiplier, FlatDamageBonus, minScale, maxScale);
    }
}
