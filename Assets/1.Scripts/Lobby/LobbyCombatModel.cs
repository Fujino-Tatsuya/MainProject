using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TRAIN_02 일반 전투 장부(할 일 86 T4, 명세 v0.2 §8) — Unity·NGO 비의존(EditMode 테스트 대상). 실행은 <see cref="LobbyTrainingCombat"/>.
/// <list type="bullet">
/// <item>시작 = 스폰한 몬스터 id 목록으로 <see cref="Begin"/>. 목록 밖 사망·중복 사망은 무시.</item>
/// <item>끼임 복구(<see cref="RecordRecovery"/>)는 처치가 아니다 — 남은 수를 줄이지 않는다.</item>
/// <item>전원 처치 = <see cref="TryConsumeAllDead"/> 가 정확히 한 번 true. 빈 목록(스폰 실패)은 완료로 치지 않는다.</item>
/// <item>단계 이탈 = <see cref="Stop"/> — 남은 id 를 돌려줘 호출자가 디스폰한다.</item>
/// </list>
/// </summary>
public sealed class LobbyCombatLedger
{
    readonly List<ulong> _members = new List<ulong>();
    readonly HashSet<ulong> _dead = new HashSet<ulong>();
    readonly Dictionary<ulong, int> _recoveries = new Dictionary<ulong, int>();
    bool _completionReported;

    public bool IsRunning { get; private set; }

    /// <summary>스폰 목록 인원.</summary>
    public int Total => _members.Count;

    public int KilledCount => _dead.Count;

    public int RemainingCount => _members.Count - _dead.Count;

    /// <summary>끼임 복구 누적 횟수(로그용 — 처치와 무관).</summary>
    public int RecoveryCount { get; private set; }

    public IReadOnlyList<ulong> Members => _members;

    /// <summary>새로 시작 — 이전 기록을 지우고 스폰 목록을 고정한다(중복 id 는 한 번만).</summary>
    public void Begin(IEnumerable<ulong> spawned)
    {
        Clear();
        IsRunning = true;
        foreach (var id in spawned)
        {
            if (!_members.Contains(id))
                _members.Add(id);
        }
    }

    public bool Contains(ulong id) => _members.Contains(id);

    public bool IsDead(ulong id) => _dead.Contains(id);

    /// <summary>목록 안 몬스터 사망. 처음일 때만 true.</summary>
    public bool MarkDead(ulong id) => IsRunning && _members.Contains(id) && _dead.Add(id);

    /// <summary>끼임 복구 기록 — 살아 있는 목록 안 몬스터만 true. 남은 수는 그대로.</summary>
    public bool RecordRecovery(ulong id)
    {
        if (!IsRunning || !_members.Contains(id) || _dead.Contains(id))
            return false;

        _recoveries[id] = RecoveriesOf(id) + 1;
        RecoveryCount++;
        return true;
    }

    public int RecoveriesOf(ulong id) => _recoveries.TryGetValue(id, out var n) ? n : 0;

    /// <summary>전원 처치 — 진행 중 + 목록이 있고 + 남은 0 일 때 한 번만 true.</summary>
    public bool TryConsumeAllDead()
    {
        if (!IsRunning || _completionReported || _members.Count == 0 || RemainingCount > 0)
            return false;

        _completionReported = true;
        return true;
    }

    /// <summary>살아 있는 목록 id. <paramref name="result"/> 는 비우고 채운다.</summary>
    public void GetAlive(List<ulong> result)
    {
        result.Clear();
        foreach (var id in _members)
        {
            if (!_dead.Contains(id))
                result.Add(id);
        }
    }

    /// <summary>단계 이탈·판 끝 — 아직 살아 있는 id 를 <paramref name="leftovers"/> 에 담고(디스폰 대상) 기록을 비운다.</summary>
    public void Stop(List<ulong> leftovers)
    {
        if (leftovers != null)
            GetAlive(leftovers);
        Clear();
    }

    void Clear()
    {
        IsRunning = false;
        _members.Clear();
        _dead.Clear();
        _recoveries.Clear();
        RecoveryCount = 0;
        _completionReported = false;
    }
}

/// <summary>끼임 판정 결과.</summary>
public enum LobbyCombatStuckVerdict
{
    None = 0,

    /// <summary>훈련실 경계 밖(넉백으로 밀려남·낙하 등) — 바로 복구.</summary>
    OutOfBounds = 1,

    /// <summary>초기 위치에서 떨어진 채 일정 시간 거의 안 움직였고 가까이 플레이어도 없다 — 도달 불가로 보고 복구.</summary>
    NotMoving = 2,
}

/// <summary>
/// TRAIN_02 끼임 복구 규칙(명세 v0.2 §8 "도달 불가능한 위치에 갇히면 초기 위치로 복구, 처치 아님") — Unity 수학 타입만 쓰는 순수 규칙.
/// 단순 규칙 두 개만 둔다:
/// <list type="number">
/// <item>훈련실 경계(XZ 사각형 + 여유, 높이 범위) 밖 → 즉시.</item>
/// <item><see cref="StillSeconds"/> 동안 수평 <see cref="MoveEpsilon"/> 이상 안 움직였고, 초기 위치에서 <see cref="HomeRadius"/> 넘게 떨어져 있고,
/// 가장 가까운 플레이어가 <see cref="PlayerReachRadius"/> 밖 → 복구. 플레이어 옆에서 서서 싸우는 몹·제자리 대기 몹은 건드리지 않는다.</item>
/// </list>
/// 복구 = 초기 위치 Warp(호출자) + <see cref="OnRecovered"/> 로 정지 시계 재시작.
/// </summary>
public sealed class LobbyCombatStuckTracker
{
    public const float StillSeconds = 8f;
    public const float MoveEpsilon = 0.5f;
    public const float HomeRadius = 1f;
    public const float PlayerReachRadius = 3f;

    /// <summary>경계 수평 여유(벽 두께·넉백 걸침).</summary>
    public const float BoundsMargin = 0.5f;

    /// <summary>경계 높이 여유(아래 = 낙하 판정, 위 = 점프·띄움).</summary>
    public const float BoundsBelow = 1f;
    public const float BoundsAbove = 3f;

    struct Entry
    {
        public Vector3 Home;
        public Vector3 WindowStart;
        public double WindowStartTime;
    }

    readonly Dictionary<ulong, Entry> _entries = new Dictionary<ulong, Entry>();

    public int Count => _entries.Count;

    public bool IsTracked(ulong id) => _entries.ContainsKey(id);

    /// <summary>추적 시작 — 초기 위치(복구 목적지) 고정.</summary>
    public void Track(ulong id, Vector3 home, double now)
    {
        _entries[id] = new Entry { Home = home, WindowStart = home, WindowStartTime = now };
    }

    public bool TryGetHome(ulong id, out Vector3 home)
    {
        home = default;
        if (!_entries.TryGetValue(id, out var e))
            return false;

        home = e.Home;
        return true;
    }

    public void Forget(ulong id) => _entries.Remove(id);

    public void Clear() => _entries.Clear();

    /// <summary>
    /// 한 번 평가. <paramref name="nearestPlayerDistance"/> = 살아 있는 가장 가까운 플레이어까지 수평 거리(없으면 <see cref="float.PositiveInfinity"/>).
    /// 추적 밖 id 는 None.
    /// </summary>
    public LobbyCombatStuckVerdict Evaluate(ulong id, double now, Vector3 position, Bounds room, float nearestPlayerDistance)
    {
        if (!_entries.TryGetValue(id, out var e))
            return LobbyCombatStuckVerdict.None;

        if (!IsInside(room, position))
            return LobbyCombatStuckVerdict.OutOfBounds;

        if (HorizontalDistance(position, e.WindowStart) >= MoveEpsilon)
        {
            e.WindowStart = position;
            e.WindowStartTime = now;
            _entries[id] = e;
            return LobbyCombatStuckVerdict.None;
        }

        if (now - e.WindowStartTime < StillSeconds)
            return LobbyCombatStuckVerdict.None;

        if (HorizontalDistance(position, e.Home) <= HomeRadius || nearestPlayerDistance <= PlayerReachRadius)
            return LobbyCombatStuckVerdict.None;

        return LobbyCombatStuckVerdict.NotMoving;
    }

    /// <summary>복구(Warp) 뒤 — 정지 시계를 초기 위치에서 다시 잰다.</summary>
    public void OnRecovered(ulong id, double now)
    {
        if (!_entries.TryGetValue(id, out var e))
            return;

        e.WindowStart = e.Home;
        e.WindowStartTime = now;
        _entries[id] = e;
    }

    /// <summary>훈련실 경계 안인가 — XZ 는 <see cref="BoundsMargin"/> 만큼 넓게, 높이는 아래 <see cref="BoundsBelow"/>·위 <see cref="BoundsAbove"/>.</summary>
    public static bool IsInside(Bounds room, Vector3 position)
    {
        var min = room.min;
        var max = room.max;
        return position.x >= min.x - BoundsMargin && position.x <= max.x + BoundsMargin
            && position.z >= min.z - BoundsMargin && position.z <= max.z + BoundsMargin
            && position.y >= min.y - BoundsBelow && position.y <= max.y + BoundsAbove;
    }

    public static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        var dx = a.x - b.x;
        var dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
}

/// <summary>TRAIN_02 단계 범위 — 몬스터가 있어야 하는가(T22 전투 훈련, Activity Combat 동안만).</summary>
public static class LobbyCombatRules
{
    public static bool MonstersActive(LobbyTutorialActivity activity) => activity == LobbyTutorialActivity.Combat;

    /// <summary>
    /// 구성 지점 오프셋 → 로비 후보 위치. 회전·스케일 없이 기준점 + (지점 위치 − 구성 원점) 그대로 — 존2 배치 모양을 바꾸지 않는다.
    /// </summary>
    public static Vector3 CandidatePosition(Vector3 origin, Vector3 sourceRoot, Vector3 sourcePoint) =>
        origin + (sourcePoint - sourceRoot);
}
