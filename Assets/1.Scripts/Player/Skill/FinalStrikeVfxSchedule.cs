/// <summary>
/// 최후의 심판(R) 마무리 낙뢰(칼이 떨어지는 FX_Lightening_Strike)를 <b>한 번만 쏘고, 언제 걷는지</b>의 순수 규칙.
///
/// 언제 쏘는지는 클립 이벤트가 정한다 — Garen_Ultimate_Skill 의 Custom0(칼이 피해 시각에 닿도록 Hit 보다 앞에 둔다).
/// 앞서 쏜 만큼 "칼은 떨어지기 시작했는데 채널이 끊기는" 구간이 생긴다 → 완주(Completed)가 아니면 걷는다.
///
/// 서버가 시전마다 <see cref="Reset"/> 하고 Custom0 마다 <see cref="TryFire"/> 를 묻는다. 한 시전에 한 번만 true 다.
/// </summary>
public sealed class FinalStrikeVfxSchedule
{
    private bool fired;

    public bool Fired => fired;

    /// <summary>
    /// 칼이 이미 떨어지는 중에 채널이 끝났다 — 걷어야 하나. 완주만 끝까지 재생하고,
    /// 대상 소실·사망·취소·안전망 만료 등 나머지는 피해가 없으니 전부 걷는다.
    /// (종료 사유는 전 피어에 실려 오지만 '피해가 났는지'는 서버만 안다 — 그래서 사유로 판단한다.)
    /// </summary>
    public static bool ShouldRecall(SkillEndReason reason) => reason != SkillEndReason.Completed;

    /// <summary>시전 시작. 지난 시전의 발사 기록을 지운다.</summary>
    public void Reset() => fired = false;

    /// <summary>시전 뒤 첫 호출만 true. 이후로는 같은 시전 동안 계속 false 다.</summary>
    public bool TryFire()
    {
        if (fired) return false;

        fired = true;
        return true;
    }
}
