using System;

/// <summary>
/// 서버 권한 몬스터 사망 통보 채널.
/// MonsterBase의 사망 단일 지점에서 발행하고, 세션 통계(처치 수)가 구독한다.
/// (보스도 MonsterBase 파생이라 같은 지점을 쓴다 — 폐기된 BossBase는 더 이상 관여하지 않는다.)
/// 몬스터 쪽에 통계 의존성을 심지 않기 위해 정적 채널로 분리했다.
/// </summary>
public static class MonsterDeathEvents
{
    /// <summary>인자는 죽은 유닛. 서버(또는 오프라인)에서만 발행된다.</summary>
    public static event Action<Unit> ServerMonsterDied;

    public static void RaiseServerMonsterDied(Unit unit)
    {
        ServerMonsterDied?.Invoke(unit);
    }
}
