using System;
using System.Collections.Generic;

/// <summary>할 일 103 C3b 기믹 종류 — 로그 <c>kind=</c> 토큰. 우선순위: 실드 → 드론 유도 → 송전탑 → 없음(보스 공격).</summary>
public enum QABoss103GimmickKind
{
    None = 0,
    /// <summary>송전탑(<c>BossChargingPylon</c>) 부수기 — 송전기·레이드 ② 전력 공급·북진 압박.</summary>
    Pylon = 1,
    /// <summary>레이드 ② 드론 유도 — 웰즈 드론 표식이 나를 노리면 Live 탑 옆에 서서 착탄 원에 탑을 넣는다.</summary>
    DroneGuide = 2,
    /// <summary>④ 자폭 실드 — 보스만 치면 깎인다(전원 보스 집중).</summary>
    Shield = 3,
}

/// <summary>송전탑 한 개(이 인스턴스가 본 값). <see cref="Id"/> = NetworkObjectId(피어 공통).</summary>
public struct QABoss103PylonInfo
{
    public ulong Id;
    public float X, Z;
    /// <summary>이번 차징에 올라왔거나 올라오는 중(Live 또는 상승) — 배정 후보.</summary>
    public bool Engaged;
    /// <summary>상승 완료 — 피격 가능.</summary>
    public bool Live;

    public QABoss103PylonInfo(ulong id, float x, float z, bool engaged, bool live)
    {
        Id = id;
        X = x;
        Z = z;
        Engaged = engaged || live;
        Live = live;
    }
}

/// <summary>파티 자리 하나(이 인스턴스가 본 위치). <see cref="Present"/> = 살아 있다(배정을 받는다).</summary>
public struct QABoss103BotSeat
{
    public int Slot;
    public float X, Z;
    public bool Present;

    public QABoss103BotSeat(int slot, float x, float z, bool present)
    {
        Slot = slot;
        X = x;
        Z = z;
        Present = present;
    }
}

/// <summary>
/// 할 일 103 C3b 송전탑 배정(순수, Unity 비의존 — EditMode <c>QABoss103GimmickTests</c>). 봇 3명이 각자 자기 인스턴스에서 같은 규칙으로 파티 전원의 배정표를 굴린다.
/// <list type="bullet">
/// <item>배정이 없는 자리는 자리 순서(0 팔라딘·1 어쌔신·2 거너)대로, 아무도 맡지 않은 후보 탑 중 가장 가까운 탑을 맡는다(같은 거리면 작은 id).
///   3명·4탑이면 각자 가까운 탑 하나씩 — 남는 1개는 먼저 자기 탑을 끝낸(배정이 풀린) 봇이 이어서 맡는다.</item>
/// <item>맡지 않은 탑이 없으면 가장 가까운 후보 탑을 함께 친다(탑 2개·3명이면 셋째가 돕는다).</item>
/// <item>배정은 탑이 후보(Live·상승)인 동안 유지된다 — 거리가 바뀌어도 바꾸지 않는다. 탑이 내려가거나 자리가 빠지면(사망) 풀린다.</item>
/// </list>
/// </summary>
public sealed class QABoss103PylonAssigner
{
    private readonly Dictionary<int, ulong> _targets = new Dictionary<int, ulong>();
    private readonly List<QABoss103BotSeat> _order = new List<QABoss103BotSeat>();
    private readonly HashSet<ulong> _claimed = new HashSet<ulong>();
    private readonly List<int> _drop = new List<int>();

    /// <summary>배정 변경(새 배정) 누적 수.</summary>
    public int Assignments { get; private set; }

    public bool TryGet(int slot, out ulong pylonId) => _targets.TryGetValue(slot, out pylonId);

    public void Clear() => _targets.Clear();

    public void Tick(IReadOnlyList<QABoss103PylonInfo> pylons, IReadOnlyList<QABoss103BotSeat> seats)
    {
        // 1. 풀기 — 탑이 후보가 아니거나 자리가 빠졌다.
        _drop.Clear();
        foreach (KeyValuePair<int, ulong> kv in _targets)
            if (!IsEngaged(pylons, kv.Value) || !IsPresent(seats, kv.Key))
                _drop.Add(kv.Key);
        foreach (int s in _drop)
            _targets.Remove(s);

        // 2. 빈 자리 채우기 — 자리 순서.
        _order.Clear();
        for (int i = 0; i < seats.Count; i++)
            if (seats[i].Present)
                _order.Add(seats[i]);
        _order.Sort((a, b) => a.Slot.CompareTo(b.Slot));

        for (int i = 0; i < _order.Count; i++)
        {
            QABoss103BotSeat seat = _order[i];
            if (_targets.ContainsKey(seat.Slot))
                continue;
            _claimed.Clear();
            foreach (KeyValuePair<int, ulong> kv in _targets)
                _claimed.Add(kv.Value);
            if (TryNearest(pylons, seat.X, seat.Z, _claimed, out ulong id) || TryNearest(pylons, seat.X, seat.Z, null, out id))
            {
                _targets[seat.Slot] = id;
                Assignments++;
            }
        }
    }

    /// <summary>후보 탑 중 (x,z) 에서 가장 가까운 것(<paramref name="exclude"/> 제외). 같은 거리면 작은 id.</summary>
    public static bool TryNearest(IReadOnlyList<QABoss103PylonInfo> pylons, float x, float z, HashSet<ulong> exclude, out ulong id)
    {
        id = 0;
        bool found = false;
        float best = float.MaxValue;
        for (int i = 0; i < pylons.Count; i++)
        {
            QABoss103PylonInfo p = pylons[i];
            if (!p.Engaged || (exclude != null && exclude.Contains(p.Id)))
                continue;
            float dx = p.X - x, dz = p.Z - z;
            float sq = dx * dx + dz * dz;
            if (!found || sq < best - 1e-6f || (Math.Abs(sq - best) <= 1e-6f && p.Id < id))
            {
                found = true;
                best = sq;
                id = p.Id;
            }
        }
        return found;
    }

    private static bool IsEngaged(IReadOnlyList<QABoss103PylonInfo> pylons, ulong id)
    {
        for (int i = 0; i < pylons.Count; i++)
            if (pylons[i].Id == id)
                return pylons[i].Engaged;
        return false;
    }

    private static bool IsPresent(IReadOnlyList<QABoss103BotSeat> seats, int slot)
    {
        for (int i = 0; i < seats.Count; i++)
            if (seats[i].Slot == slot)
                return seats[i].Present;
        return false;
    }
}

/// <summary>할 일 103 C3b 기믹 순수 규칙 — 기믹 선택·드론 유도 자리·착탄 원 판정·로그 토큰. Unity 의존 없음(EditMode <c>QABoss103GimmickTests</c>).</summary>
public static class QABoss103Gimmick
{
    /// <summary>드론 유도 자리 = 탑 몸 표면에서 이만큼 바깥(m) — 몸통 콜라이더에 끼지 않고 착탄 원(반경 ≥ 2m)에 탑이 들어간다.</summary>
    public const float GuideStandOff = 0.5f;

    /// <summary>유도 자리에 도착했다고 보는 수평 거리(m).</summary>
    public const float GuideTolerance = 0.35f;

    /// <summary>
    /// 착탄 원에 걸친 탑으로 보는 여유(m) — <c>WellsDroneAttack.PylonHitSlack</c>(0.75)와 같은 값. 게임 값이 바뀌면 여기도 맞춘다(판정 로그용, 행동엔 안 쓴다).
    /// </summary>
    public const float PylonHitSlack = 0.75f;

    /// <summary>
    /// 이번 프레임 기믹. <paramref name="shield"/> = 자폭 실드 남음, <paramref name="markedMe"/> = 드론 표식(추적) 대상이 나,
    /// <paramref name="anyLivePylon"/> = Live 탑 있음, <paramref name="myPylon"/> = 나에게 배정된 탑 있음.
    /// </summary>
    public static QABoss103GimmickKind Decide(bool shield, bool markedMe, bool anyLivePylon, bool myPylon)
    {
        if (shield)
            return QABoss103GimmickKind.Shield;
        if (markedMe && anyLivePylon)
            return QABoss103GimmickKind.DroneGuide;
        if (myPylon)
            return QABoss103GimmickKind.Pylon;
        return QABoss103GimmickKind.None;
    }

    /// <summary>
    /// 드론 유도 자리 — 탑 중심(px,pz)에서 봇 쪽으로 (몸 반경 + <see cref="GuideStandOff"/>) 떨어진 점. 봇이 탑 중심에 있으면 +z 쪽.
    /// </summary>
    public static void GuideSpot(float px, float pz, float extent, float bx, float bz, out float x, out float z)
    {
        float dx = bx - px, dz = bz - pz;
        float len = (float)Math.Sqrt(dx * dx + dz * dz);
        if (len < 1e-4f)
        {
            dx = 0f;
            dz = 1f;
            len = 1f;
        }
        float d = Math.Max(0f, extent) + GuideStandOff;
        x = px + dx / len * d;
        z = pz + dz / len * d;
    }

    /// <summary>고정 원(중심 lx,lz·반경)에 탑 중심(px,pz)이 걸리는가 — 게임 판정과 같은 식(중심 거리 ≤ 반경 + <see cref="PylonHitSlack"/>).</summary>
    public static bool InDroneReach(float lx, float lz, float radius, float px, float pz)
    {
        float dx = px - lx, dz = pz - lz;
        return Math.Sqrt(dx * dx + dz * dz) <= radius + PylonHitSlack;
    }

    /// <summary>
    /// 지금 대상(탑)에 R 을 써도 되는가 — 보스 대상 지정 R(팔라딘·거너)은 보스 전용, 자기 강화 R(어쌔신 변신)은 탑에도 쓴다. 보스 대상이면 항상 true.
    /// </summary>
    public static bool AllowR(bool targetIsBoss, bool rIsSingleTarget) => targetIsBoss || !rIsSingleTarget;

    public static string Token(QABoss103GimmickKind kind)
    {
        switch (kind)
        {
            case QABoss103GimmickKind.Pylon: return "pylon";
            case QABoss103GimmickKind.DroneGuide: return "drone_guide";
            case QABoss103GimmickKind.Shield: return "shield";
            default: return "none";
        }
    }

    /// <summary>로그 대상 토큰 — 탑 = <c>pylon_&lt;NetworkObjectId&gt;</c>.</summary>
    public static string PylonToken(ulong id) => "pylon_" + id;
}
