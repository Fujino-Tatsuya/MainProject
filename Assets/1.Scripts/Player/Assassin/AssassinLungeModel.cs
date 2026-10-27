using System;

/// <summary>
/// 어쌔신 평타 전진의 순수 규칙 — 일반 E 강타 전진(character_assassin.md §8.2)과 일반 타 매 타 전진(할 일 96).
/// Unity 의존 없이 EditMode 로 검증한다.
/// 전진 거리를 전진 시간 동안 고르게 나눠 물리 틱마다 낸다 — 남은 거리를 넘지 않는다.
/// </summary>
public static class AssassinLungeRules
{
    /// <summary>모드별 전진 거리 — 일반 타 = 매 타 전진, 강타 = 강타 전진, 변신 묶음 = 없음(0).</summary>
    public static float Distance(AssassinBasicAttackMode mode, float normalDistance, float enhancedDistance) => mode switch
    {
        AssassinBasicAttackMode.Normal => Math.Max(0f, normalDistance),
        AssassinBasicAttackMode.Enhanced => Math.Max(0f, enhancedDistance),
        _ => 0f,
    };

    /// <summary>모드별 전진 시간(공격속도 배율 적용 전, 초). 거리와 같은 짝을 고른다.</summary>
    public static float Duration(AssassinBasicAttackMode mode, float normalDuration, float enhancedDuration) => mode switch
    {
        AssassinBasicAttackMode.Normal => Math.Max(0f, normalDuration),
        AssassinBasicAttackMode.Enhanced => Math.Max(0f, enhancedDuration),
        _ => 0f,
    };

    /// <summary>
    /// 이번 물리 틱에 낼 거리. 속도 = 거리 / 시간, 남은 거리에서 멈춘다.
    /// 시간이 0 이하면 남은 거리를 한 틱에 모두 낸다.
    /// </summary>
    public static float StepDistance(float remaining, float distance, float duration, float deltaTime)
    {
        if (remaining <= 0f || distance <= 0f || deltaTime <= 0f)
            return 0f;
        if (duration <= 0f)
            return remaining;

        return Math.Min(distance / duration * deltaTime, remaining);
    }
}
