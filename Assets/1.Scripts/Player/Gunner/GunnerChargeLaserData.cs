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
    [Tooltip("최소 충전 사거리(m) — 집중으로 이 값 → 판정 도형 length(최대 충전 사거리)까지 보간.")]
    [SerializeField, Min(0.1f)] private float minRange = 6f;
    [Tooltip("판정 영역(할 일 153) — 최대 충전 기준. 기준점 = 발사 원점(FireOrigin, 발밑 + 위로 muzzleHeight) + 조준 방향(Point). " +
             "첫 직사각형 length = 최대 충전 사거리, width = 판정 폭(높이는 발사 원점 기준). 충전·지형 차단은 길이 배율, 광폭 레이저는 폭 배율(AttackShapeSet.Scaled).")]
    [SerializeField] private AttackShapeSet hitArea = new AttackShapeSet(AttackShapeOrigin.Point, BeamRect());
    [SerializeField, DataTableIgnore] private float muzzleHeight = 1.0f;
    [Tooltip("지형 마스크 — 비트리거 콜라이더에 닿으면 그 지점에서 레이저가 끝난다. 기본값 = Default·Wall·Env·Barrier(할 일 32).")]
    [SerializeField] private LayerMask blockingLayers = 1050753;
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
    public float MinRange => minRange;
    public AttackShapeSet HitArea => hitArea;
    /// <summary>최대 충전 사거리 = 판정 도형 첫 직사각형 length(직사각형이 아니면 0).</summary>
    public float MaxRange => BeamShape != null ? BeamShape.Length : 0f;
    /// <summary>판정 폭(특성 전) = 판정 도형 첫 직사각형 width — 지형 차단 반경·발사 연출 폭도 이 값(직사각형이 아니면 0).</summary>
    public float BeamWidth => BeamShape != null ? BeamShape.Width : 0f;
    public float MuzzleHeight => muzzleHeight;
    public LayerMask BlockingLayers => blockingLayers;
    public LayerMask AllyLayers => allyLayers;
    public int ShieldAmount => shieldAmount;
    public float ShieldDuration => shieldDuration;
    public float FireRecovery => fireRecovery;

    /// <summary>집중 경과(초) → 보간 계수 0~1.</summary>
    public float ChargeFactor(float elapsed) =>
        Mathf.Clamp01(chargeCurve != null ? chargeCurve.Evaluate(Mathf.Clamp01(elapsed / maxChargeTime)) : elapsed / maxChargeTime);

    /// <summary>충전 사거리 — 최소 충전 사거리 → 최대 충전 사거리(<see cref="MaxRange"/> = 판정 도형 length) 보간.</summary>
    public float RangeAt(float factor) => Mathf.Lerp(minRange, MaxRange, factor);
    public float DamageMultiplierAt(float factor) => Mathf.Lerp(minDamageMultiplier, maxDamageMultiplier, factor);
    public float StageDamageMultiplier(int stage) => Pick(stageDamageMultipliers, stage);
    public float StageShieldMultiplier(int stage) => Pick(stageShieldMultipliers, stage);

    /// <summary>
    /// 원래 발 피해 상태 배율 최소 = 충전 0 · 0단계 · 축열 소모 0 (판정식 GunnerChargeLaserSkill.Fire 와 같은 표, 할 일 127 3/4).
    /// </summary>
    public float MinShotScale(in GunnerHeatTraits traits) => DamageMultiplierAt(0f) * traits.SkillStageScale(0, StageDamageMultiplier(0));

    /// <summary>최대 = 최대 충전 · 도달 가능한 최고 단계(정온 운용 0) · 축열 최대 소모(축열 포격만).</summary>
    public float MaxShotScale(in GunnerHeatTraits traits)
    {
        int top = traits.MaxStage;
        return DamageMultiplierAt(1f) * traits.SkillStageScale(top, StageDamageMultiplier(top)) * traits.MaxHeatShotScale;
    }

    private AttackRectShape BeamShape => hitArea != null && !hitArea.IsEmpty ? hitArea.Shapes[0] as AttackRectShape : null;

    /// <summary>
    /// 발사 판정 영역 — 최대 충전 도형을 실제 길이(충전 사거리 → 지형 차단 뒤)·폭(광폭 레이저 반영)에 맞춘 복사본.
    /// 길이 배율 = length / <see cref="MaxRange"/>, 폭 배율 = width / <see cref="BeamWidth"/>(도형이 여러 개면 모두 같은 배율).
    /// </summary>
    public AttackShapeSet FireArea(float length, float width) =>
        hitArea.Scaled(Ratio(length, MaxRange), Ratio(width, BeamWidth));

    /// <summary>발사 기준 포즈 — 기준점 종류가 Point 면 발사 원점(<paramref name="fireOrigin"/>), CasterAim 이면 그 아래 발밑. 정면 = 조준 방향.</summary>
    public AttackShapePose FirePose(Vector3 fireOrigin, Vector3 direction) =>
        hitArea.ResolvePose(fireOrigin - Vector3.up * muzzleHeight, direction, fireOrigin, direction);

    /// <summary>
    /// 기본 판정 도형(할 일 153) — 예전 발사 상자(SkillHitVolume.Beam: 발사 원점에서 사거리 16 · 폭 1.2 · 높이 2 를 원점 높이 중심)를 옮긴 값.
    /// 뒤 0, 높이 = 발사 원점 기준 ±1.
    /// </summary>
    public static AttackRectShape BeamRect() =>
        new AttackRectShape(16f, 1.2f) { MinHeight = -1f, MaxHeight = 1f };

    private static float Ratio(float value, float full) => full > 0f ? value / full : 0f;

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

        float minScale = MinShotScale(GunnerHeatTraits.From(player.TraitModifiers));
        float maxScale = MaxShotScale(GunnerHeatTraits.From(player.TraitModifiers));
        return SkillTooltipDamage.FromScaledSnapshot(
            player.FinalAttackDamage, AttackDamageMultiplier, FlatDamageBonus, minScale, maxScale, player.DealtDamagePercent);
    }
}
