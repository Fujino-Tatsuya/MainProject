using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>간파 성공·벽 충돌 한 번의 게이지 결과(<see cref="TrainingSparringRules"/>).</summary>
public readonly struct TrainingSparringGaugeOutcome
{
    public TrainingSparringGaugeOutcome(float nextGauge, bool reduced, bool isSuppress, float duration)
    {
        NextGauge = nextGauge;
        Reduced = reduced;
        IsSuppress = isSuppress;
        Duration = duration;
    }

    /// <summary>다음 게이지(감소가 없으면 그대로).</summary>
    public float NextGauge { get; }
    /// <summary>게이지가 실제로 줄었다(칸 감소 이벤트·모델 OnWallImpact(reduced)).</summary>
    public bool Reduced { get; }
    /// <summary>게이지 0 → 제압.</summary>
    public bool IsSuppress { get; }
    /// <summary>행동 불능 시간(간파 성공 = 그로기 또는 제압, 벽 = 0).</summary>
    public float Duration { get; }
}

/// <summary>
/// [서버] TRAIN_03 훈련 상대(<see cref="TrainingSparringBot"/>) 순수 판정 — EditMode 테스트 대상. 23호 규칙(<see cref="BossCounterProgress"/>)을 그대로 쓰고,
/// 훈련 보정(A/B 칸 전이 중지·처치 방지·지정 벽)만 여기서 더한다. 값은 전부 호출측이 23호 데이터에서 넘긴다.
/// </summary>
public static class TrainingSparringRules
{
    /// <summary>처치 방지 중 체력 하한(Unit.HealthFloor). 방지가 아니면 0 = 하한 없음.</summary>
    public static int HealthFloor(bool preventsDeath) => preventsDeath ? 1 : 0;

    /// <summary>
    /// D 마무리 체력. 지금 체력이 마무리 체력보다 낮으면 그대로(회복하지 않는다), 1~최대 체력으로 자른다.
    /// </summary>
    public static int FinishHealth(int currentHealth, int maxHealth, int finishHealth)
    {
        int max = Math.Max(1, maxHealth);
        int target = Math.Min(Math.Max(1, finishHealth), Math.Max(1, currentHealth));
        return Math.Min(target, max);
    }

    /// <summary>
    /// 간파 성공. 칸 전이 허용(C·D)이면 23호와 같다 — 게이지 감소, 0 이면 제압(breakDuration), 남으면 그로기(groggyDuration).
    /// 허용 안 함(A/B)이면 게이지·제압 없이 그로기만(취약은 호출측이 그로기와 같이 건다).
    /// </summary>
    public static TrainingSparringGaugeOutcome ResolveCounter(float gauge, float step, float groggyDuration, float breakDuration,
                                                             bool allowsGaugeTransition)
    {
        if (!allowsGaugeTransition)
            return new TrainingSparringGaugeOutcome(gauge, false, false, Mathf.Max(0.05f, groggyDuration));

        BossCounterOutcome o = BossCounterProgress.Resolve(gauge, step, groggyDuration, breakDuration);
        return new TrainingSparringGaugeOutcome(o.NextGauge, o.NextGauge < Clamp(gauge), o.IsSuppress, o.Duration);
    }

    /// <summary>
    /// 지정 벽 유효 충돌(취약·이번 취약 첫 환경일 때만 호출측이 부른다). 허용(C·D)이면 environmentGaugeStep 감소, 0 이면 제압.
    /// 이미 0 이라 줄 게 없으면 Reduced = false — 모델이 벽 목표로 인정하지 않는다(명세 §9.5).
    /// </summary>
    public static TrainingSparringGaugeOutcome ResolveWall(float gauge, float step, bool allowsGaugeTransition)
    {
        if (!allowsGaugeTransition || Clamp(gauge) <= 0.001f)
            return new TrainingSparringGaugeOutcome(gauge, false, false, 0f);

        BossCounterOutcome o = BossCounterProgress.Resolve(gauge, step, 0f, 0f);
        return new TrainingSparringGaugeOutcome(o.NextGauge, o.NextGauge < Clamp(gauge), o.IsSuppress, 0f);
    }

    /// <summary>칸 UI 의 남은 칸 — 게이지를 감소량으로 나눠 올림(부동소수 오차 여유는 총 칸 판정과 같다). 감소량 0 이하면 0.</summary>
    public static int SegmentsRemaining(float gauge, float step)
    {
        float g = Clamp(gauge);
        if (step <= 0f || g <= 0.001f) return 0;
        return (int)Math.Ceiling(g / step - 0.0001f);
    }

    /// <summary>벽 충돌 결과 콜라이더가 이 상대의 지정 벽 목록에 있는가. 목록이 비면 어떤 벽도 유효하지 않다(장식 벽 불인정).</summary>
    public static bool IsDesignatedWall<T>(T wall, IReadOnlyList<T> designated) where T : class
    {
        if (wall == null || designated == null) return false;
        for (int i = 0; i < designated.Count; i++)
            if (designated[i] != null && ReferenceEquals(designated[i], wall))
                return true;
        return false;
    }

    /// <summary>
    /// 23호 간파 정면 판정(<c>counterRequiresFront</c>·<c>counterFrontAngle</c>). 정면 판정을 끄면 항상 true, 공격자가 겹쳐 방향을 못 구하면 정면으로 본다.
    /// </summary>
    public static bool IsFromFront(Vector3 forward, Vector3 toAttacker, float frontAngle, bool requiresFront)
    {
        if (!requiresFront) return true;
        toAttacker.y = 0f;
        forward.y = 0f;
        if (toAttacker.sqrMagnitude < 0.0001f || forward.sqrMagnitude < 0.0001f) return true;
        return Vector3.Angle(forward, toAttacker) <= frontAngle;
    }

    /// <summary>
    /// 취약 중 팔라딘 Q 경직 길이 — 23호는 남은 취약 시간까지만. B 학습용 취약 유지면 남은 시간이 의미 없어 Q 경직 그대로.
    /// 0.05 이하면 경직 없음(0).
    /// </summary>
    public static float PushStaggerSeconds(float staggerDuration, float vulnerableRemaining, bool holdsVulnerable)
    {
        float stun = holdsVulnerable ? staggerDuration : Mathf.Min(staggerDuration, vulnerableRemaining);
        return stun > 0.05f ? stun : 0f;
    }

    static float Clamp(float gauge) => Mathf.Clamp(gauge, 0f, BossCounterProgress.GaugeMax);
}
