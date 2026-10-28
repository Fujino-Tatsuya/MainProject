using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 돌파의 방패(PAL_2_DASH, 할 일 126 4/4) 수치 한 벌 — 정의 에셋의 스킬 수치 기여(⑩ 공용 <see cref="TraitSkillValueEffect"/> 칸 — 할 일 133 에서 옛 PaladinShieldDashEffect 를 합쳤다)에서 읽는다.
/// 스킬은 <see cref="PlayerTraitModifiers.ResolveSkillValue"/>(이름, 0)으로, 카드·테스트는 정의에서 직접(<see cref="FromTrait"/>) 같은 이름으로 읽는다.
/// 값이 하나라도 없거나 0 이하면 <see cref="IsValid"/> = 거짓 — 스킬은 원래 Q 로 돌고 오류를 남긴다(콘텐츠 검증이 먼저 잡는다).
/// </summary>
public readonly struct PaladinShieldDashSettings
{
    public readonly float Distance;
    public readonly float Speed;
    public readonly float PrepareTime;
    public readonly float RecoveryTime;
    public readonly float DamageRatio;
    public readonly float PushDistance;
    public readonly float PushDuration;

    public PaladinShieldDashSettings(float distance, float speed, float prepareTime, float recoveryTime,
        float damageRatio, float pushDistance, float pushDuration)
    {
        Distance = distance;
        Speed = speed;
        PrepareTime = prepareTime;
        RecoveryTime = recoveryTime;
        DamageRatio = damageRatio;
        PushDistance = pushDistance;
        PushDuration = pushDuration;
    }

    /// <summary>돌진 이동 시간(초) = 거리 ÷ 속도(3m ÷ 18m/s ≈ 0.167).</summary>
    public float TravelTime => Speed > 0f ? Distance / Speed : 0f;

    /// <summary>준비 + 이동 + 회수 = 시전 전체 길이(초).</summary>
    public float TotalTime => PrepareTime + TravelTime + RecoveryTime;

    /// <summary>단발 넉백 속도(m/s) = 밀치기 거리 ÷ 시간.</summary>
    public float PushSpeed => PushDuration > 0f ? PushDistance / PushDuration : 0f;

    public bool IsValid => Positive(Distance) && Positive(Speed) && NonNegative(PrepareTime) && NonNegative(RecoveryTime) &&
                           Positive(DamageRatio) && Positive(PushDistance) && Positive(PushDuration);

    /// <summary>[서버·클라] 고른 특성 수치에서 — 특성이 없으면 전부 0(무효).</summary>
    public static PaladinShieldDashSettings From(PlayerTraitModifiers modifiers)
    {
        if (modifiers == null)
            return default;

        return new PaladinShieldDashSettings(
            modifiers.ResolveSkillValue(PaladinTraitValues.DashDistanceKey, 0f),
            modifiers.ResolveSkillValue(PaladinTraitValues.DashSpeedKey, 0f),
            modifiers.ResolveSkillValue(PaladinTraitValues.DashPrepareKey, 0f),
            modifiers.ResolveSkillValue(PaladinTraitValues.DashRecoveryKey, 0f),
            modifiers.ResolveSkillValue(PaladinTraitValues.DashDamageRatioKey, 0f),
            modifiers.ResolveSkillValue(PaladinTraitValues.DashPushDistanceKey, 0f),
            modifiers.ResolveSkillValue(PaladinTraitValues.DashPushDurationKey, 0f));
    }

    /// <summary>정의 에셋 하나에서 — 카드 별칭·콘텐츠 검증용. 없는 이름은 0.</summary>
    public static PaladinShieldDashSettings FromTrait(TraitDefinition trait)
    {
        var modifiers = new List<TraitModifier>();
        trait?.GetModifiers(modifiers);
        return new PaladinShieldDashSettings(
            Find(modifiers, PaladinTraitValues.DashDistanceKey),
            Find(modifiers, PaladinTraitValues.DashSpeedKey),
            Find(modifiers, PaladinTraitValues.DashPrepareKey),
            Find(modifiers, PaladinTraitValues.DashRecoveryKey),
            Find(modifiers, PaladinTraitValues.DashDamageRatioKey),
            Find(modifiers, PaladinTraitValues.DashPushDistanceKey),
            Find(modifiers, PaladinTraitValues.DashPushDurationKey));
    }

    private static float Find(List<TraitModifier> modifiers, string key)
    {
        foreach (TraitModifier modifier in modifiers)
        {
            if (modifier.Kind == TraitModifierKind.SkillValue && modifier.Key == key)
                return modifier.Value;
        }

        return 0f;
    }

    private static bool Positive(float value) => value > 0f && !float.IsInfinity(value);
    private static bool NonNegative(float value) => value >= 0f && !float.IsInfinity(value);
}

/// <summary>
/// 돌파의 방패 서버 경로 장부(순수 계산 — EditMode 테스트가 고정한다). 어쌔신 Q 경로 판정과 같은 정책:
/// 입력 순간 방향·시작 위치를 고정하고, 경과 시간으로 경로를 재구성해 새로 나아간 구간만 훑는다(끝점만 보지 않는다).
/// 오너가 벽·낭떠러지 전에서 멈춘 실제 거리를 보고하면 경로 상한만 줄인다(늘리지 않음). 대상은 시전당 1회(피해 1회 + 넉백 1회).
/// </summary>
public sealed class PaladinShieldDashPath
{
    /// <summary>경로 표본 간격(m) — 앵커 판정 폭보다 충분히 촘촘하게.</summary>
    public const float SampleSpacing = 0.25f;

    private readonly HashSet<int> hitTargets = new HashSet<int>();
    private PaladinShieldDashSettings settings;
    private bool sampledStart;

    public Vector3 Origin { get; private set; }
    public Vector3 Heading { get; private set; } = Vector3.forward;
    public float Cap { get; private set; }
    public float Traveled { get; private set; }
    public bool IsActive { get; private set; }
    public PaladinShieldDashSettings Settings => settings;

    /// <summary>시전 시작 — 방향은 여기서 고정한다(이후 조향 없음).</summary>
    public void Begin(Vector3 origin, Vector3 heading, PaladinShieldDashSettings dashSettings)
    {
        settings = dashSettings;
        Origin = origin;
        heading.y = 0f;
        Heading = heading.sqrMagnitude > 1e-6f ? heading.normalized : Vector3.forward;
        Cap = Mathf.Max(0f, dashSettings.Distance);
        Traveled = 0f;
        sampledStart = false;
        hitTargets.Clear();
        IsActive = true;
    }

    public void End()
    {
        IsActive = false;
        hitTargets.Clear();
    }

    /// <summary>시전 시작 뒤 <paramref name="elapsed"/> 초에 경로가 나아간 거리 — 준비 동안 0, 이동 동안 속도 × 시간, 상한에서 멈춘다.</summary>
    public float TraveledAt(float elapsed) =>
        Mathf.Clamp((elapsed - settings.PrepareTime) * settings.Speed, 0f, Cap);

    /// <summary>준비 + 이동 + 회수가 끝났나.</summary>
    public bool IsFinished(float elapsed) => elapsed >= settings.TotalTime;

    /// <summary>
    /// 오너가 보고한 실제 이동 거리(벽·낭떠러지 안전 정지). 0~최대 거리만 받는다. 이미 훑은 거리 아래로는 줄이지 않는다.
    /// </summary>
    public bool ReportStop(float reported)
    {
        if (!IsActive || float.IsNaN(reported) || float.IsInfinity(reported) || reported < 0f || reported > settings.Distance + 0.05f)
            return false;

        Cap = Mathf.Min(Cap, Mathf.Max(reported, Traveled));
        return true;
    }

    /// <summary>
    /// 경로를 <paramref name="target"/> m 까지 늘리고 이번에 새로 지난 표본 거리들을 <paramref name="samples"/> 에 담는다(시작점 포함 1회, 끝점 포함).
    /// </summary>
    public void AdvanceTo(float target, List<float> samples)
    {
        samples.Clear();
        if (!IsActive)
            return;

        target = Mathf.Clamp(target, 0f, Cap);
        if (!sampledStart)
        {
            sampledStart = true;
            samples.Add(0f);
        }

        if (target <= Traveled)
            return;

        float from = Traveled;
        for (float d = from + SampleSpacing; d < target; d += SampleSpacing)
            samples.Add(d);
        samples.Add(target);
        Traveled = target;
    }

    /// <summary>경로 위 거리 → 월드 위치(바닥 평면, 시작 높이).</summary>
    public Vector3 PointAt(float distance) => Origin + Heading * distance;

    /// <summary>대상 1명당 1회(피해 1회 + 넉백 1회). 처음이면 참.</summary>
    public bool TryRegisterTarget(int targetId) => IsActive && hitTargets.Add(targetId);

    /// <summary>
    /// 단발 타격 공격 정보 — 피해 = 교체 Q 1회, 넉백 = 이 모드 전용 거리·시간(진행 방향, 끌고 가지 않는다). 경직은 원래 Q 데이터 값.
    /// 특성 밀치기·뇌진탕은 스킬이 이 뒤에 원래 Q 와 같은 한 곳(<c>ApplyTraitCrowdControlServer</c>)으로 싣는다.
    /// </summary>
    public AttackInfo CreateAttack(int damage, AttackType type, float staggerDuration, AttackHitPattern pattern) =>
        new AttackInfo(damage, type,
            knockbackStrength: settings.PushSpeed,
            knockbackDuration: settings.PushDuration,
            staggerDuration: staggerDuration,
            knockbackDirection: Heading,
            hitPattern: pattern);
}
