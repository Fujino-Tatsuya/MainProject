using System.Collections.Generic;

/// <summary>제압 가속 버프를 받는 쪽 — 서버 어댑터(<see cref="SuppressHaste"/>)와 EditMode 가짜가 구현한다.</summary>
public interface ISuppressHasteTarget
{
    PlayerLifeState LifeState { get; }

    /// <summary><see cref="StatusEffectController.Apply(StatusEffectType, float, float, ulong, int)"/> 와 같은 뜻.</summary>
    void Apply(StatusEffectType type, float magnitude, float duration, ulong sourceId, int maxStacks);

    /// <summary><see cref="StatusEffectController.Remove"/> 와 같은 뜻.</summary>
    bool Remove(StatusEffectType type, ulong sourceId);
}

/// <summary>배율 3개 + 지속 규칙(xlsx <c>SuppressHaste</c> 시트 → <see cref="SuppressHasteData"/>).</summary>
public readonly struct SuppressHasteValues
{
    public readonly float MoveSpeedMultiplier;
    public readonly float AttackSpeedMultiplier;
    public readonly float CooldownRateMultiplier;
    public readonly float DurationOverride;

    public SuppressHasteValues(float moveSpeedMultiplier, float attackSpeedMultiplier, float cooldownRateMultiplier, float durationOverride)
    {
        MoveSpeedMultiplier = moveSpeedMultiplier;
        AttackSpeedMultiplier = attackSpeedMultiplier;
        CooldownRateMultiplier = cooldownRateMultiplier;
        DurationOverride = durationOverride;
    }

    /// <summary>SO 가 없을 때 — 시트 기본값과 같다(×2 · ×2 · ×2 · 제압 길이를 따름).</summary>
    public static SuppressHasteValues Default => new SuppressHasteValues(2f, 2f, 2f, 0f);
}

/// <summary>
/// 제압 가속 버프 규칙(할 일 91 T6, PLAN-91 §3.4) — 대상 선정·지속·부여·쓰러짐 제거를 한 곳에 둔다.
///   · 대상 = 그 순간 <see cref="PlayerLifeState.Alive"/> 인 플레이어만(쓰러짐 연출·Soul·사망 제외).
///   · 지속 = durationOverride &gt; 0 이면 그 값, 0 이면 이번 제압 길이. 0 이하가 나오면 걸지 않는다(상태이상 지속 0 = 수동 해제형이라 영구가 된다).
///   · 상태이상 3개, 출처 = 보스, 스택 1 — 같은 출처 재진입은 시간·수치 갱신(<see cref="StatusEffectController.Apply(StatusEffectType, float, float, ulong, int)"/>).
///   · Alive 를 벗어나면 그 출처 3개 제거. 부활해도 다시 걸지 않는다. 만료는 상태이상 스윕.
/// MonoBehaviour 아님 — EditMode 테스트 대상.
/// </summary>
public static class SuppressHasteRules
{
    public const int MaxStacks = 1;

    /// <summary>상태이상 HUD 에 아이콘을 보이는 대표 타입. 나머지 둘은 <see cref="IsHudHidden"/>.</summary>
    public const StatusEffectType HudRepresentative = StatusEffectType.CooldownRateModifier;

    public static bool IsEligible(PlayerLifeState state) => state == PlayerLifeState.Alive;

    /// <summary>생명주기 전이 뒤 버프를 빼야 하는지 — Alive 가 아니게 되면 뺀다.</summary>
    public static bool ShouldRevoke(PlayerLifeState next) => next != PlayerLifeState.Alive;

    /// <summary>이번 버프 지속(초). false = 걸지 않는다(결과가 0 이하·NaN).</summary>
    public static bool TryResolveDuration(float durationOverride, float suppressDuration, out float duration)
    {
        duration = durationOverride > 0f ? durationOverride : suppressDuration;
        return duration > 0f;
    }

    /// <summary>버프가 거는 (타입, 배율) 3개. 순서 = 대표(쿨) 먼저.</summary>
    public static IEnumerable<(StatusEffectType type, float magnitude)> Effects(SuppressHasteValues values)
    {
        yield return (StatusEffectType.CooldownRateModifier, values.CooldownRateMultiplier);
        yield return (StatusEffectType.MoveSpeedFinalMultiplier, values.MoveSpeedMultiplier);
        yield return (StatusEffectType.AttackSpeedFinalMultiplier, values.AttackSpeedMultiplier);
    }

    /// <summary>대표 아닌 버프 타입 — HUD 에서 숨긴다(아이콘 1개로 보이게, PLAN-91 R5).</summary>
    public static bool IsHudHidden(StatusEffectType type) =>
        type == StatusEffectType.MoveSpeedFinalMultiplier || type == StatusEffectType.AttackSpeedFinalMultiplier;

    /// <summary>살아있는 대상마다 3개를 건다. 건 대상 수를 돌려준다(지속이 0 이하면 0).</summary>
    public static int Grant(IEnumerable<ISuppressHasteTarget> targets, SuppressHasteValues values, ulong sourceId, float suppressDuration)
    {
        if (targets == null || !TryResolveDuration(values.DurationOverride, suppressDuration, out float duration))
            return 0;

        int granted = 0;
        foreach (ISuppressHasteTarget target in targets)
        {
            if (target == null || !IsEligible(target.LifeState))
                continue;

            foreach ((StatusEffectType type, float magnitude) in Effects(values))
                target.Apply(type, magnitude, duration, sourceId, MaxStacks);
            granted++;
        }

        return granted;
    }

    /// <summary>그 출처가 건 3개를 뺀다. 하나라도 뺐으면 true.</summary>
    public static bool Revoke(ISuppressHasteTarget target, ulong sourceId)
    {
        if (target == null)
            return false;

        bool removed = false;
        foreach (var effect in Effects(SuppressHasteValues.Default))
            removed |= target.Remove(effect.type, sourceId);
        return removed;
    }
}
