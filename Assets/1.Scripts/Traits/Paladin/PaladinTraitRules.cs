/// <summary>팔라딘 특성의 스킬 쪽 판단(할 일 126) — 스킬 코드가 부르고 EditMode 테스트가 고정한다.</summary>
public static class PaladinTraitRules
{
    /// <summary>
    /// 수호의 잔광(PAL_3_E_SHIELD) — E 가 끝나는 순간(= 방어 성공 뒤 피해 무시 단계가 끝나는 순간) 보호막 사건을 낼까.
    /// 이번 시전에 방어에 성공했고(실패면 예약 없음) 시전자가 살아 있을 때만(사망 = 예약 취소).
    /// 같은 시전의 중복은 효과 거름(oncePerCast, 방어 성공 시전 ID)이 막는다.
    /// </summary>
    public static bool GrantsGuardAfterglow(bool blockedThisCast, SkillEndReason reason, int casterHealth) =>
        blockedThisCast && reason != SkillEndReason.CasterDied && casterHealth > 0;
}
