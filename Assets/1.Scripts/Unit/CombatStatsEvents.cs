using System;

/// <summary>
/// 서버 권한 전투 통계 통보 채널(결과 화면 플레이어별 통계 — PLAN-result-stats).
/// <see cref="MonsterDeathEvents"/> 선례처럼 Unit·몬스터 쪽에 통계 의존성을 심지 않으려고 정적 채널로 분리했다.
///
/// 🔴 발행 지점은 판정에 관여하지 않는다 — 이미 확정된 결과를 알리기만 한다.
/// 🔴 <see cref="RaiseServerDamageApplied"/> 는 전투 핫패스(Unit.ApplyHealthDamage)에서 불린다.
///    구독자가 없으면 null 검사 한 번으로 끝나야 한다(할당 금지 — 인자는 전부 값/참조 그대로).
/// 필터(몬스터만·공격자 없음 제외 등)는 발행 쪽이 아니라 구독 쪽(SessionStatsAggregator)이 고정한다.
/// </summary>
public static class CombatStatsEvents
{
    /// <summary>공격자 없음(환경·추락·직접/비율 피해). Unit 의 공격자 귀속 기본값과 같다.</summary>
    public const ulong NoAttacker = ulong.MaxValue;

    /// <summary>
    /// 서버(또는 오프라인)에서 피해가 실제로 적용될 때마다 발생한다.
    /// 인자 = (대상, 공격자 clientId, HP+실드 실제 감소량(막타 초과분 제외), 이번 피해로 체력 0 도달 여부).
    /// 대상 종류는 가리지 않는다(플레이어·더미 포함) — 거르는 건 구독자 몫이다.
    /// </summary>
    public static event Action<Unit, ulong, int, bool> ServerDamageApplied;

    /// <summary>
    /// 서버에서 간파(카운터) 성공이 확정될 때 발생한다. 인자 = (간파당한 몬스터, 공격자 clientId).
    /// 23호 카운터 성공 · 중간보스 3종 간파 창 소비 성공에서만 발행한다(일반몹 그로기 누적은 아니다).
    /// </summary>
    public static event Action<Unit, ulong> ServerCounterSucceeded;

    public static void RaiseServerDamageApplied(Unit target, ulong attackerClientId, int amount, bool lethal)
    {
        ServerDamageApplied?.Invoke(target, attackerClientId, amount, lethal);
    }

    public static void RaiseServerCounterSucceeded(Unit target, ulong attackerClientId)
    {
        ServerCounterSucceeded?.Invoke(target, attackerClientId);
    }
}
