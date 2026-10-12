using System.Collections.Generic;

/// <summary>
/// 결과 화면 플레이어별 통계의 순수 집계기(MonoBehaviour 아님 — EditMode 테스트 대상).
/// <see cref="SessionStatsTracker"/> 가 서버 이벤트를 받아 그대로 넘기고, 판 종료 시 스냅샷을 뜬다.
///
/// 🔴 필터 규칙은 호출부가 아니라 여기서 고정한다:
///   · 데미지·처치 = 대상이 집계 대상 몬스터일 때만(호출부는 분류 결과만 넘긴다), 공격자 없음 제외, 0 이하 무시.
///   · 처치 = 이번 피해로 체력 0 에 도달한 피해(막타)의 공격자 1회.
///   · 간파 = 공격자 없음 제외.
///   · 사망 = Alive → DeadPresentation 전이만 1회. 같은 상태 통지((s,s) — 스폰 초기 통지)는 행 등록만 한다.
/// </summary>
public sealed class SessionStatsAggregator
{
    public const ulong NoAttacker = CombatStatsEvents.NoAttacker;

    private readonly Dictionary<ulong, PlayerSessionStats> _rows = new Dictionary<ulong, PlayerSessionStats>();

    public int PlayerCount => _rows.Count;

    public void Reset()
    {
        _rows.Clear();
    }

    /// <summary>행을 보장한다(값 0). 판 중 끊긴 플레이어도 한 번 등록되면 끝까지 남는다.</summary>
    public void RegisterPlayer(ulong clientId)
    {
        if (clientId == NoAttacker || _rows.ContainsKey(clientId))
            return;

        _rows.Add(clientId, new PlayerSessionStats
        {
            ClientId = clientId,
            CharacterId = PlayerSessionStats.UnknownCharacterId
        });
    }

    /// <summary>캐릭터 id 를 기록한다. 음수(모름)는 기존 값을 덮지 않는다.</summary>
    public void SetCharacterId(ulong clientId, int characterId)
    {
        if (characterId < 0 || clientId == NoAttacker)
            return;

        RegisterPlayer(clientId);
        PlayerSessionStats row = _rows[clientId];
        row.CharacterId = characterId;
        _rows[clientId] = row;
    }

    /// <returns>집계에 반영했는가.</returns>
    public bool RecordDamage(ulong attackerClientId, bool targetIsCountedMonster, int amount, bool lethal)
    {
        if (!targetIsCountedMonster || attackerClientId == NoAttacker || amount <= 0)
            return false;

        RegisterPlayer(attackerClientId);
        PlayerSessionStats row = _rows[attackerClientId];
        row.Damage = SaturatingAdd(row.Damage, amount);
        if (lethal)
            row.Kills = SaturatingAdd(row.Kills, 1);
        _rows[attackerClientId] = row;
        return true;
    }

    /// <returns>집계에 반영했는가.</returns>
    public bool RecordCounterSuccess(ulong attackerClientId)
    {
        if (attackerClientId == NoAttacker)
            return false;

        RegisterPlayer(attackerClientId);
        PlayerSessionStats row = _rows[attackerClientId];
        row.Counters = SaturatingAdd(row.Counters, 1);
        _rows[attackerClientId] = row;
        return true;
    }

    /// <returns>사망으로 셌는가. 어떤 전이든 행은 등록한다.</returns>
    public bool RecordLifeStateChange(ulong clientId, PlayerLifeState previous, PlayerLifeState current)
    {
        if (clientId == NoAttacker)
            return false;

        RegisterPlayer(clientId);
        if (previous != PlayerLifeState.Alive || current != PlayerLifeState.DeadPresentation)
            return false;

        PlayerSessionStats row = _rows[clientId];
        row.Deaths = SaturatingAdd(row.Deaths, 1);
        _rows[clientId] = row;
        return true;
    }

    public bool TryGet(ulong clientId, out PlayerSessionStats stats)
    {
        return _rows.TryGetValue(clientId, out stats);
    }

    /// <summary>현재 값을 clientId 오름차순(로비 슬롯 순서, P1 = 호스트)으로 output 에 채운다.</summary>
    public void Snapshot(List<PlayerSessionStats> output)
    {
        output.Clear();
        foreach (PlayerSessionStats row in _rows.Values)
            output.Add(row);

        output.Sort((a, b) => a.ClientId.CompareTo(b.ClientId));
    }

    private static int SaturatingAdd(int value, int add)
    {
        long sum = (long)value + add;
        return sum > int.MaxValue ? int.MaxValue : (int)sum;
    }
}
