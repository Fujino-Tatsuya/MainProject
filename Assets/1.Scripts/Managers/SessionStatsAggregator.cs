using System.Collections.Generic;

/// <summary>
/// 결과 화면 플레이어별 통계의 순수 집계기(MonoBehaviour 아님 — EditMode 테스트 대상).
/// <see cref="SessionStatsTracker"/> 가 서버 이벤트를 받아 그대로 넘기고, 판 종료 시 스냅샷을 뜬다.
///
/// 🔴 필터 규칙은 호출부가 아니라 여기서 고정한다:
///   · 데미지·처치 = 대상이 집계 대상 몬스터일 때만(호출부는 분류 결과만 넘긴다), 공격자 없음 제외, 0 이하 무시.
///   · 처치 = 이번 피해로 체력 0 에 도달한 피해(막타)의 공격자 1회.
///   · 간파 = 공격자 없음 제외.
///   · 회복량 = 대상이 플레이어인 회복만, 회복 주체에게. 주체 없음·0 이하 무시.
///   · 입은 피해 = 대상이 플레이어인 HP 감소만, 그 플레이어에게(공격자 무관). 0 이하 무시.
///   · 사망 = Alive → DeadPresentation 전이만 1회. 같은 상태 통지((s,s) — 스폰 초기 통지)는 행 등록만 한다.
/// </summary>
public sealed class SessionStatsAggregator
{
    public const ulong NoAttacker = CombatStatsEvents.NoAttacker;

    private readonly Dictionary<ulong, PlayerSessionStats> _rows = new Dictionary<ulong, PlayerSessionStats>();
    // 정렬 키 — 기본은 자기 clientId. 재합류로 옮긴 행은 옛 자리(처음 clientId)를 이어 받아 결과 패널 순서가 바뀌지 않는다.
    private readonly Dictionary<ulong, ulong> _orderKeys = new Dictionary<ulong, ulong>();

    public int PlayerCount => _rows.Count;

    public void Reset()
    {
        _rows.Clear();
        _orderKeys.Clear();
    }

    /// <summary>
    /// 판 도중 재합류(할 일 48 C14 R5c) — 같은 사람이 새 clientId 로 돌아왔다. 옛 행을 새 clientId 행으로 옮겨 합친다(같은 사람 1행).
    /// 수치는 더하고(새 행은 재스폰 뒤 값), 캐릭터는 새 행 값이 있으면 그것, 없으면 옛 값. 순서는 옛 자리를 따른다.
    /// 옛 행이 없으면 아무것도 안 한다. 같은 id·집계 대상 아님은 false. 같은 쌍을 두 번 불러도 행이 늘지 않는다.
    /// </summary>
    /// <returns>옛 행을 옮겼는가.</returns>
    public bool Rebind(ulong oldClientId, ulong newClientId)
    {
        if (oldClientId == newClientId || oldClientId == NoAttacker || newClientId == NoAttacker ||
            !_rows.TryGetValue(oldClientId, out PlayerSessionStats old))
            return false;

        PlayerSessionStats merged = old;
        if (_rows.TryGetValue(newClientId, out PlayerSessionStats current))
        {
            merged.Damage = SaturatingAdd(old.Damage, current.Damage);
            merged.Kills = SaturatingAdd(old.Kills, current.Kills);
            merged.Counters = SaturatingAdd(old.Counters, current.Counters);
            merged.Deaths = SaturatingAdd(old.Deaths, current.Deaths);
            merged.Healing = SaturatingAdd(old.Healing, current.Healing);
            merged.DamageTaken = SaturatingAdd(old.DamageTaken, current.DamageTaken);
            if (current.CharacterId >= 0)
                merged.CharacterId = current.CharacterId;
        }

        merged.ClientId = newClientId;
        _rows.Remove(oldClientId);
        _rows[newClientId] = merged;
        _orderKeys[newClientId] = OrderKey(oldClientId);
        _orderKeys.Remove(oldClientId);
        return true;
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

    /// <summary>회복량 — 대상이 플레이어인 회복만 회복 주체에게 더한다(몬스터 자기 회복 제외). 오버힐 제외는 호출 측 실제 회복량이 보장.</summary>
    /// <returns>집계에 반영했는가.</returns>
    public bool RecordHealing(ulong healerClientId, bool targetIsPlayer, int amount)
    {
        if (!targetIsPlayer || healerClientId == NoAttacker || amount <= 0)
            return false;

        RegisterPlayer(healerClientId);
        PlayerSessionStats row = _rows[healerClientId];
        row.Healing = SaturatingAdd(row.Healing, amount);
        _rows[healerClientId] = row;
        return true;
    }

    /// <summary>입은 피해 — 대상이 플레이어일 때 그 플레이어에게 HP 실제 감소량을 더한다(공격자 무관).</summary>
    /// <returns>집계에 반영했는가.</returns>
    public bool RecordDamageTaken(ulong victimClientId, bool targetIsPlayer, int healthLost)
    {
        if (!targetIsPlayer || victimClientId == NoAttacker || healthLost <= 0)
            return false;

        RegisterPlayer(victimClientId);
        PlayerSessionStats row = _rows[victimClientId];
        row.DamageTaken = SaturatingAdd(row.DamageTaken, healthLost);
        _rows[victimClientId] = row;
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

    /// <summary>현재 값을 clientId 오름차순(로비 슬롯 순서, P1 = 호스트)으로 output 에 채운다. 재합류로 옮긴 행은 옛 clientId 자리.</summary>
    public void Snapshot(List<PlayerSessionStats> output)
    {
        output.Clear();
        foreach (PlayerSessionStats row in _rows.Values)
            output.Add(row);

        output.Sort((a, b) =>
        {
            int byOrder = OrderKey(a.ClientId).CompareTo(OrderKey(b.ClientId));
            return byOrder != 0 ? byOrder : a.ClientId.CompareTo(b.ClientId);
        });
    }

    private ulong OrderKey(ulong clientId)
    {
        return _orderKeys.TryGetValue(clientId, out ulong key) ? key : clientId;
    }

    private static int SaturatingAdd(int value, int add)
    {
        long sum = (long)value + add;
        return sum > int.MaxValue ? int.MaxValue : (int)sum;
    }
}
