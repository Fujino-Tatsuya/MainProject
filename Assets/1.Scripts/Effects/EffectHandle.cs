using System;

/// <summary>
/// 루프 이펙트 하나를 가리키는 핸들. <see cref="EffectManager.PlayLooping"/>이 발급하고
/// <see cref="EffectManager.Release"/>가 소비한다.
///
/// <b>세대 카운터가 있는 이유</b>: 슬롯은 재사용된다. class + null 체크로는 stale 핸들을 막지 못한다 —
/// 핸들 객체는 살아 있고 안의 인스턴스만 다른 이펙트로 재배정되기 때문에, 늦게 도착한 Release()가
/// <b>엉뚱한 이펙트를 끈다</b>. 세대가 다르면 조용한 no-op이 된다.
///
/// 원샷(<see cref="EffectManager.Play"/>)은 핸들을 발급하지 않는다. 실패 모드가 대칭이 아니기 때문 —
/// 원샷 핸들을 버리면 무해하지만 루프 핸들을 버리면 풀이 고갈된다.
/// </summary>
public readonly struct EffectHandle : IEquatable<EffectHandle>
{
    internal readonly int slot;
    internal readonly int generation;   // 0 = 발급되지 않은 핸들. 매니저의 세대는 1부터 시작한다.

    internal EffectHandle(int slot, int generation)
    {
        this.slot = slot;
        this.generation = generation;
    }

    /// <summary>발급되지 않은 핸들. Release()에 넘겨도 안전한 no-op.</summary>
    public static EffectHandle None => default;

    /// <summary>매니저가 발급한 핸들인가. (지금도 살아 있는지는 매니저만 안다)</summary>
    public bool IsSet => generation != 0;

    public bool Equals(EffectHandle other) => slot == other.slot && generation == other.generation;
    public override bool Equals(object obj) => obj is EffectHandle other && Equals(other);
    public override int GetHashCode() => (slot * 397) ^ generation;
    public override string ToString() => IsSet ? $"EffectHandle({slot}#{generation})" : "EffectHandle(None)";
}
