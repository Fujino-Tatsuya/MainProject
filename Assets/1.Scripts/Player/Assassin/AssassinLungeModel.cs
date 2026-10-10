using System;

/// <summary>
/// 어쌔신 일반 E 강타 전진의 순수 규칙(character_assassin.md §8.2). Unity 의존 없이 EditMode 로 검증한다.
/// 전진 거리를 전진 시간 동안 고르게 나눠 물리 틱마다 낸다 — 남은 거리를 넘지 않는다.
/// </summary>
public static class AssassinLungeRules
{
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
