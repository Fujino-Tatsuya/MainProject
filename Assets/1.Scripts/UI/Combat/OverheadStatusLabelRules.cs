/// <summary>머리 위 이름 자리에 대신 띄울 것. <see cref="None"/> = 원래 이름(P1·P2…).</summary>
public enum OverheadStatusLabel : byte
{
    None,
    Grabbed,     // 붙잡힘 — 구속(RestraintMode Carry·Push 둘 다)
    Airborne,
    Stunned,
    Rooted,
    Silenced,
    Debilitated,
    Slowed,
}

/// <summary>
/// 머리 위 이름 라벨을 상태이상 이름으로 바꿀지 판정(할 일 50). MonoBehaviour 밖 순수 계산이라 EditMode 테스트로 고정한다.
/// 대상 = 차단류 CC(<see cref="StatusEffectImmunityPolicy.BlockedCrowdControl"/>) + 붙잡힘. 겹치면 하나만 —
/// 붙잡힘 &gt; 에어본 &gt; 기절(=뇌진탕) &gt; 속박 &gt; 침묵 &gt; 약화 &gt; 둔화. 버프·스탯 배율·슈퍼아머 등은 무시한다.
/// 살아 있지 않으면(사망 연출·Soul·영구 사망) 상태이상이 남아 있어도 이름을 띄운다.
/// </summary>
public static class OverheadStatusLabelRules
{
    /// <summary>CC 우선순위(높은 것 먼저). 집합은 BlockedCrowdControl 과 같아야 한다 — 테스트가 고정한다.</summary>
    public static readonly StatusEffectType[] CrowdControlPriority =
    {
        StatusEffectType.Airborne,
        StatusEffectType.Stunned,
        StatusEffectType.Concussion,   // 뇌진탕(할 일 125) — 표시는 기절과 같다(대기 중에도 Stunned 가 같이 걸려 보통 위에서 먼저 잡힌다)
        StatusEffectType.Rooted,
        StatusEffectType.Silenced,
        StatusEffectType.Debilitated,
        StatusEffectType.Slowed,
    };

    /// <summary>
    /// 표시할 하나를 고른다. 생명주기 컴포넌트가 없으면 살아 있는 것으로 본다(<see cref="OverheadDashIconRules"/> 와 같은 규칙).
    /// </summary>
    public static OverheadStatusLabel Select(
        StatusEffectType activeEffects, bool isRestrained, bool hasLifeCycle, PlayerLifeState lifeState)
    {
        if (hasLifeCycle && lifeState != PlayerLifeState.Alive)
            return OverheadStatusLabel.None;

        if (isRestrained)
            return OverheadStatusLabel.Grabbed;

        for (int i = 0; i < CrowdControlPriority.Length; i++)
        {
            if ((activeEffects & CrowdControlPriority[i]) != 0)
                return FromStatusEffect(CrowdControlPriority[i]);
        }

        return OverheadStatusLabel.None;
    }

    /// <summary>CC 라벨 → 상태이상 타입(문구 키 <c>UI.StatusEffect.&lt;enum&gt;</c> 용). None·Grabbed 는 <see cref="StatusEffectType.None"/>.</summary>
    public static StatusEffectType ToStatusEffect(OverheadStatusLabel label) => label switch
    {
        OverheadStatusLabel.Airborne => StatusEffectType.Airborne,
        OverheadStatusLabel.Stunned => StatusEffectType.Stunned,
        OverheadStatusLabel.Rooted => StatusEffectType.Rooted,
        OverheadStatusLabel.Silenced => StatusEffectType.Silenced,
        OverheadStatusLabel.Debilitated => StatusEffectType.Debilitated,
        OverheadStatusLabel.Slowed => StatusEffectType.Slowed,
        _ => StatusEffectType.None,
    };

    static OverheadStatusLabel FromStatusEffect(StatusEffectType type) => type switch
    {
        StatusEffectType.Airborne => OverheadStatusLabel.Airborne,
        StatusEffectType.Stunned => OverheadStatusLabel.Stunned,
        StatusEffectType.Concussion => OverheadStatusLabel.Stunned,
        StatusEffectType.Rooted => OverheadStatusLabel.Rooted,
        StatusEffectType.Silenced => OverheadStatusLabel.Silenced,
        StatusEffectType.Debilitated => OverheadStatusLabel.Debilitated,
        StatusEffectType.Slowed => OverheadStatusLabel.Slowed,
        _ => OverheadStatusLabel.None,
    };
}
