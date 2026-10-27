using System;
using System.Collections.Generic;

// 튜토리얼 존 장부의 순수 규칙(Unity·NGO 비의존 — EditMode 테스트 대상).
// 원본: Docs/design/tutorial-scenario.md §2.2·§4 · PLAN-tutorial.md S4 · Docs/tech/tutorial-director.md §8.
// 배선(몬스터 찾기·휴면 적용·사망 구독)은 MapSceneMonsterZones(서버)가 한다.

/// <summary>존 클리어 규칙.</summary>
public enum MapSceneZoneClearRule : byte
{
    /// <summary>클리어 판정 안 함(존1 등).</summary>
    None = 0,
    /// <summary>존 몬스터 전멸(존2, D13).</summary>
    AllDead = 1,
    /// <summary>
    /// 핵심 몬스터(중간보스급 — 존3 스피너봇·존4 GauntletBot) 전원 처치(존3·존4, D4).
    /// 그 존에 핵심 몬스터가 하나도 없으면 <see cref="AllDead"/> 로 본다(장부가 비지 않는 한 판정이 멈추지 않게).
    /// </summary>
    KeyDead = 2,
}

/// <summary>존 안에서 고를 몬스터 — 휴면·기상 대상 선택.</summary>
public enum MapSceneMonsterFilter : byte
{
    All = 0,
    /// <summary>핵심 몬스터 제외(존4 기상 — 중간보스는 다리 개통 때 따로).</summary>
    NormalOnly = 1,
    /// <summary>핵심 몬스터만(존4 중간보스 기상).</summary>
    KeyOnly = 2,
}

/// <summary>
/// 몬스터 → 존(<see cref="MapSceneArea"/> Zone1~4) 장부 + 존 클리어 판정.
///
/// 규칙:
///  · 존 배정 = 등록 시점 위치(XZ)가 들어 있는 존 사각형(<see cref="ClassifyZone"/>). 어느 존에도 없으면 배정하지 않는다(장부 밖 — 판정·휴면 대상 아님).
///  · 사망은 한 번만 센다. 모르는 id·이미 죽은 id 는 무시.
///  · 클리어는 존마다 한 번만 낸다(<see cref="ZoneCleared"/>). 판정은 사망 때만 한다 — 몬스터가 0마리인 존은 클리어되지 않는다.
/// </summary>
public sealed class MapSceneMonsterLedger
{
    private struct Entry
    {
        public MapSceneArea Zone;
        public bool Key;
        public bool Dead;
    }

    private const int AreaSlots = 16;

    private readonly Dictionary<ulong, Entry> _entries = new Dictionary<ulong, Entry>();
    private readonly List<ulong> _order = new List<ulong>();   // 등록 순서(결정적 순회)
    private readonly MapSceneZoneClearRule[] _rules = new MapSceneZoneClearRule[AreaSlots];
    private int _clearedMask;

    /// <summary>존 클리어(존마다 1번).</summary>
    public event Action<MapSceneArea> ZoneCleared;

    /// <summary>장부 몬스터 사망(id, 존, 핵심 여부) — 클리어 판정보다 먼저 낸다.</summary>
    public event Action<ulong, MapSceneArea, bool> MemberDied;

    public IReadOnlyList<ulong> Ids => _order;

    /// <summary>존 사각형으로 존을 고른다(Zone1~4 만, 경계 포함·여유 없음). 없으면 None.</summary>
    public static MapSceneArea ClassifyZone(MapSceneAreaTracker areas, float x, float z)
    {
        if (areas == null) return MapSceneArea.None;
        foreach (MapSceneArea zone in Zones)
            if (areas.TryGetArea(zone, out MapSceneRect rect) && rect.Contains(x, z))
                return zone;
        return MapSceneArea.None;
    }

    public static readonly MapSceneArea[] Zones =
        { MapSceneArea.Zone1, MapSceneArea.Zone2, MapSceneArea.Zone3, MapSceneArea.Zone4 };

    public void SetClearRule(MapSceneArea zone, MapSceneZoneClearRule rule) => _rules[(int)zone] = rule;
    public MapSceneZoneClearRule GetClearRule(MapSceneArea zone) => _rules[(int)zone];

    /// <summary>등록. 존이 None 이거나 이미 있는 id 면 false.</summary>
    public bool Add(ulong id, MapSceneArea zone, bool key)
    {
        if (zone == MapSceneArea.None || _entries.ContainsKey(id)) return false;
        _entries[id] = new Entry { Zone = zone, Key = key };
        _order.Add(id);
        return true;
    }

    public bool TryGet(ulong id, out MapSceneArea zone, out bool key, out bool dead)
    {
        zone = MapSceneArea.None;
        key = dead = false;
        if (!_entries.TryGetValue(id, out Entry e)) return false;
        zone = e.Zone;
        key = e.Key;
        dead = e.Dead;
        return true;
    }

    /// <summary>사망 반영. 새로 반영되면 true(<see cref="MemberDied"/> → 필요하면 <see cref="ZoneCleared"/>).</summary>
    public bool MarkDead(ulong id)
    {
        if (!_entries.TryGetValue(id, out Entry e) || e.Dead) return false;
        e.Dead = true;
        _entries[id] = e;

        MemberDied?.Invoke(id, e.Zone, e.Key);

        if (!IsZoneCleared(e.Zone) && MeetsClearRule(e.Zone))
        {
            _clearedMask |= 1 << (int)e.Zone;
            ZoneCleared?.Invoke(e.Zone);
        }
        return true;
    }

    public bool IsZoneCleared(MapSceneArea zone) => (_clearedMask & (1 << (int)zone)) != 0;

    /// <summary>지금 클리어 조건을 만족하는가(이미 냈는지와 무관).</summary>
    public bool MeetsClearRule(MapSceneArea zone)
    {
        switch (_rules[(int)zone])
        {
            case MapSceneZoneClearRule.AllDead:
                return Count(zone, MapSceneMonsterFilter.All, false) > 0 && Count(zone, MapSceneMonsterFilter.All, true) == 0;
            case MapSceneZoneClearRule.KeyDead:
                if (Count(zone, MapSceneMonsterFilter.KeyOnly, false) == 0) goto case MapSceneZoneClearRule.AllDead;
                return Count(zone, MapSceneMonsterFilter.KeyOnly, true) == 0;
            default:
                return false;
        }
    }

    /// <summary>존 몬스터 수. <paramref name="aliveOnly"/> 면 살아 있는 것만.</summary>
    public int Count(MapSceneArea zone, MapSceneMonsterFilter filter = MapSceneMonsterFilter.All, bool aliveOnly = false)
    {
        int n = 0;
        foreach (Entry e in _entries.Values)
            if (e.Zone == zone && Matches(e.Key, filter) && (!aliveOnly || !e.Dead)) n++;
        return n;
    }

    /// <summary>존의 살아 있는 몬스터 id(등록 순서). <paramref name="result"/> 는 비우고 채운다.</summary>
    public void GetAlive(MapSceneArea zone, MapSceneMonsterFilter filter, List<ulong> result)
    {
        result.Clear();
        foreach (ulong id in _order)
        {
            Entry e = _entries[id];
            if (e.Zone == zone && !e.Dead && Matches(e.Key, filter)) result.Add(id);
        }
    }

    public static bool Matches(bool key, MapSceneMonsterFilter filter) => filter switch
    {
        MapSceneMonsterFilter.NormalOnly => !key,
        MapSceneMonsterFilter.KeyOnly => key,
        _ => true,
    };
}
