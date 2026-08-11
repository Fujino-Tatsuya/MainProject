using UnityEngine;

/// <summary>
/// 프리팹의 ParticleSystem 설정에서 <b>마지막 입자가 죽는 시각</b>을 계산한다.
/// <see cref="EffectEntry"/>가 저작 시점에 이걸로 수명을 자동으로 채운다 — 사람이 숫자를 적지 않게.
///
/// ⚠️ <c>main.duration</c>은 <b>방출 시간이지 수명이 아니다.</b> 1초 동안 뿜고 입자가 1.4초 사는
/// 시스템은 2.4초까지 살아 있다. 그래서 <c>duration + startDelay + startLifetime</c>을 더한다.
///
/// <b>이 계산이 못 잡는 것 (하한값이라는 뜻)</b>
/// - <b>서브 이미터</b>: 부모가 죽은 <i>뒤에</i> 태어나는 입자는 부모의 수명 밖이다.
/// - 커브 모드의 중간 구간: 키프레임 사이를 샘플링으로 훑지만 완전하지는 않다.
/// 그래서 <see cref="EffectDurationProbe"/>의 실측이 여전히 안전망으로 남는다.
/// </summary>
public static class EffectLifetime
{
    /// <summary>계산 불가(루프 시스템이 섞여 있음).</summary>
    public const float Unknown = -1f;

    private const int CurveSamples = 33;

    /// <summary>
    /// 파트 배열 전체가 끝나는 시각. 각 파트의 <c>delay + 프리팹 수명</c> 중 최대값이다.
    /// 루프 시스템이 하나라도 있으면 <see cref="Unknown"/>.
    /// </summary>
    public static float Estimate(EffectPart[] parts)
    {
        if (parts == null || parts.Length == 0) return 0f;

        float longest = 0f;

        for (int i = 0; i < parts.Length; i++)
        {
            EffectPart part = parts[i];
            if (part == null || part.prefab == null) continue;

            float prefabLifetime = PrefabLifetime(part.prefab);
            if (prefabLifetime == Unknown) return Unknown;

            float end = part.delay + prefabLifetime;
            if (end > longest) longest = end;
        }

        return longest;
    }

    /// <summary>프리팹 안의 모든 ParticleSystem 중 가장 늦게 끝나는 시각.</summary>
    /// <remarks>
    /// 루트만 보지 않는 이유: "가장 오래 사는 걸 루트에 둔다"는 규칙은 아무도 검증해주지 않아
    /// 어기면 조용히 잘린다. 에디트 타임에는 전부 훑는 게 공짜라 규칙 자체가 필요 없어진다.
    /// </remarks>
    public static float PrefabLifetime(GameObject prefab)
    {
        if (prefab == null) return 0f;

        var systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
        if (systems.Length == 0) return 0f;   // Shuriken이 아닌 파트 — 여기서는 잴 수 없다

        float longest = 0f;

        for (int i = 0; i < systems.Length; i++)
        {
            float lifetime = SystemLifetime(systems[i]);
            if (lifetime == Unknown) return Unknown;
            if (lifetime > longest) longest = lifetime;
        }

        return longest;
    }

    private static float SystemLifetime(ParticleSystem system)
    {
        ParticleSystem.MainModule main = system.main;
        if (main.loop) return Unknown;   // 루프는 끝나지 않는다 — outroParts 쪽에서 잰다

        float particleLifetime = Max(main.startLifetime);
        float total = main.duration + Max(main.startDelay) + particleLifetime;

        // 트레일을 입자와 함께 지우지 않으면 입자가 죽은 뒤에도 트레일이 남는다.
        // trails.lifetime은 입자 수명에 대한 비율이다.
        ParticleSystem.TrailModule trails = system.trails;
        if (trails.enabled && !trails.dieWithParticles)
        {
            total += Max(trails.lifetime) * particleLifetime;
        }

        return total;
    }

    /// <summary>MinMaxCurve가 가질 수 있는 최대값. 4가지 모드를 전부 처리한다.</summary>
    public static float Max(ParticleSystem.MinMaxCurve curve)
    {
        switch (curve.mode)
        {
            case ParticleSystemCurveMode.Constant:
                return curve.constant;

            case ParticleSystemCurveMode.TwoConstants:
                return Mathf.Max(curve.constantMin, curve.constantMax);

            case ParticleSystemCurveMode.Curve:
            case ParticleSystemCurveMode.TwoCurves:
                return CurveMax(curve.curveMax) * curve.curveMultiplier;

            default:
                return 0f;
        }
    }

    private static float CurveMax(AnimationCurve curve)
    {
        if (curve == null || curve.length == 0) return 0f;

        float max = float.NegativeInfinity;

        // 키프레임 값만 보면 탄젠트가 만든 오버슛을 놓친다. 균일 샘플링으로 함께 훑는다.
        for (int i = 0; i < curve.length; i++)
        {
            if (curve[i].value > max) max = curve[i].value;
        }

        for (int i = 0; i < CurveSamples; i++)
        {
            float value = curve.Evaluate(i / (float)(CurveSamples - 1));
            if (value > max) max = value;
        }

        return Mathf.Max(0f, max);
    }
}
