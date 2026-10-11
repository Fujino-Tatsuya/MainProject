using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// [서버] 튜토리얼 존 장부 — 맵 생성 때 스폰된 몬스터를 존1~4 로 묶고, 휴면/기상을 존 단위로 적용하고, 사망으로 존 클리어를 판정한다.
/// 규칙은 순수 <see cref="TutorialMonsterLedger"/>, 이 클래스는 MonsterBase 참조·사망 구독만 한다. 소유자는 <see cref="TutorialDirector"/>.
/// 원본: Docs/design/tutorial-scenario.md §2.2·§4 · Docs/tech/tutorial-director.md §8.
///
/// 스폰 순서: <see cref="MapGenerator.OnGenerated"/> 는 <c>MapContentSpawner.SpawnPlacements</c>(몬스터 <c>Spawn()</c> 동기 완료) 뒤에 온다.
/// 디렉터는 그 이벤트(또는 그 뒤의 자기 스폰)에서 활성화하므로 이 시점엔 몬스터가 모두 스폰·초기화돼 있다.
/// 존 배정은 그 순간의 위치(= 스폰 위치 — 아직 플레이어가 존1 에 있어 아무도 움직이지 않았다)로 한다.
/// </summary>
public sealed class TutorialMonsterZones
{
    private readonly TutorialMonsterLedger _ledger = new TutorialMonsterLedger();
    private readonly Dictionary<ulong, MonsterBase> _monsters = new Dictionary<ulong, MonsterBase>();
    private readonly List<ulong> _buffer = new List<ulong>();
    private int _unassigned;

    public TutorialMonsterLedger Ledger => _ledger;

    /// <summary>존 클리어(존마다 1번).</summary>
    public event Action<TutorialArea> ZoneCleared
    {
        add => _ledger.ZoneCleared += value;
        remove => _ledger.ZoneCleared -= value;
    }

    /// <summary>장부 몬스터 사망(몬스터 id, 존, 핵심 여부).</summary>
    public event Action<ulong, TutorialArea, bool> MemberDied
    {
        add => _ledger.MemberDied += value;
        remove => _ledger.MemberDied -= value;
    }

    /// <summary>존 사각형(<paramref name="areas"/>)으로 지금 살아 있는 스폰된 몬스터를 묶는다. 핵심 = 랭크가 Normal 이 아닌 것(중간보스급).</summary>
    public void Build(TutorialAreaTracker areas)
    {
        _ledger.SetClearRule(TutorialArea.Zone2, TutorialZoneClearRule.AllDead);
        _ledger.SetClearRule(TutorialArea.Zone3, TutorialZoneClearRule.KeyDead);
        _ledger.SetClearRule(TutorialArea.Zone4, TutorialZoneClearRule.KeyDead);

        MonsterBase[] found = UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.InstanceID);
        foreach (MonsterBase m in found)
        {
            if (m == null || !m.IsSpawned || m.CurrentHealth <= 0) continue;
            Vector3 p = m.transform.position;
            TutorialArea zone = TutorialMonsterLedger.ClassifyZone(areas, p.x, p.z);
            if (zone == TutorialArea.None)
            {
                _unassigned++;
                Edit.LogWarning($"[Tutorial] 존 장부: {m.name} @ ({p.x:0.#}, {p.z:0.#}) — 어느 존에도 없다 → 장부 밖(휴면·판정 대상 아님).", m);
                continue;
            }
            if (_ledger.Add(m.NetworkObjectId, zone, m.Rank != MonsterRank.Normal))
                _monsters[m.NetworkObjectId] = m;
        }

        MonsterDeathEvents.ServerMonsterDied += HandleMonsterDied;
    }

    public void Dispose()
    {
        MonsterDeathEvents.ServerMonsterDied -= HandleMonsterDied;
    }

    private void HandleMonsterDied(Unit unit)
    {
        if (unit is MonsterBase m && m.IsSpawned) _ledger.MarkDead(m.NetworkObjectId);
    }

    /// <summary>존 몬스터(살아 있는 것) 휴면/기상. 바뀐 수.</summary>
    public int SetZoneDormant(TutorialArea zone, bool dormant, TutorialMonsterFilter filter)
    {
        _ledger.GetAlive(zone, filter, _buffer);
        int n = 0;
        foreach (ulong id in _buffer)
        {
            if (!_monsters.TryGetValue(id, out MonsterBase m) || m == null || m.IsDormant == dormant) continue;
            m.SetDormantServer(dormant);
            n++;
        }
        return n;
    }

    /// <summary>존의 살아 있는 몬스터 참조(등록 순서). <paramref name="result"/> 는 비우고 채운다.</summary>
    public void GetAliveMonsters(TutorialArea zone, TutorialMonsterFilter filter, List<MonsterBase> result)
    {
        result.Clear();
        _ledger.GetAlive(zone, filter, _buffer);
        foreach (ulong id in _buffer)
            if (_monsters.TryGetValue(id, out MonsterBase m) && m != null) result.Add(m);
    }

    public bool TryGetZone(MonsterBase m, out TutorialArea zone, out bool key) =>
        _ledger.TryGet(m.NetworkObjectId, out zone, out key, out _);

    /// <summary>장부 요약(로그용). 존마다 전체/생존/핵심, 장부 밖 수.</summary>
    public string Describe()
    {
        var sb = new StringBuilder();
        foreach (TutorialArea zone in TutorialMonsterLedger.Zones)
        {
            int total = _ledger.Count(zone);
            int alive = _ledger.Count(zone, TutorialMonsterFilter.All, true);
            int key = _ledger.Count(zone, TutorialMonsterFilter.KeyOnly);
            int dormant = 0;
            _ledger.GetAlive(zone, TutorialMonsterFilter.All, _buffer);
            foreach (ulong id in _buffer)
                if (_monsters.TryGetValue(id, out MonsterBase m) && m != null && m.IsDormant) dormant++;

            sb.Append($"{zone} {alive}/{total}");
            if (key > 0)
            {
                sb.Append(" (핵심 ");
                bool first = true;
                foreach (KeyValuePair<ulong, MonsterBase> kv in _monsters)
                {
                    if (!_ledger.TryGet(kv.Key, out TutorialArea z, out bool k, out _) || z != zone || !k) continue;
                    sb.Append(first ? "" : ", ").Append(kv.Value != null ? kv.Value.name : $"#{kv.Key}");
                    first = false;
                }
                sb.Append(')');
            }
            sb.Append($" 휴면 {dormant}");
            if (_ledger.IsZoneCleared(zone)) sb.Append(" 클리어");
            sb.Append(" · ");
        }
        sb.Append($"장부 밖 {_unassigned}");
        return sb.ToString();
    }
}
