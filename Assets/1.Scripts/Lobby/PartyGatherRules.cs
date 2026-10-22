using System;
using System.Collections.Generic;

/// <summary>참여자 한 명의 이번 판정 입력 — <see cref="PartyGatherRules.SetMemberState"/>.</summary>
public readonly struct PartyGatherMemberState
{
    public PartyGatherMemberState(bool inside, bool incapacitated, bool busy)
    {
        Inside = inside;
        Incapacitated = incapacitated;
        Busy = busy;
    }

    /// <summary>구역 사각형 안.</summary>
    public bool Inside { get; }

    /// <summary>쓰러짐·사망 — 안에 있어도 집결로 인정하지 않는다(자동 부활 없음).</summary>
    public bool Incapacitated { get; }

    /// <summary>진행 중 행동(공격·스킬·대시·넉백 등 Idle/Move 가 아닌 상태) — 카운트다운을 시작하지 않는다.</summary>
    public bool Busy { get; }

    /// <summary>집결 인정 = 안 + 쓰러짐·사망 아님.</summary>
    public bool Gathered => Inside && !Incapacitated;
}

/// <summary>
/// [서버] 파티 집결 규칙 — 할 일 86 PLAN §3.1·§7(유지 시간 = 전부 5초, 데이터 값). MonoBehaviour·NGO 의존 없는 순수 상태(EditMode 테스트 대상).
/// <list type="bullet">
/// <item>참여자 전원이 집결 인정(안 + 쓰러짐·사망 아님)이고 진행 중 행동이 없으면 카운트다운 시작. 참여자 0명이면 시작하지 않는다.</item>
/// <item>카운트다운 중 한 명 이탈·쓰러짐 → 즉시 0 초기화. 참여자 추가·제거(인원 변화) → 즉시 0 초기화(남은 전원이 안이면 다음 판정에서 처음부터 다시).</item>
/// <item>유지 시간을 채우면 확정 1회(래치) — <see cref="Tick"/> 이 한 번만 true. 래치 뒤엔 판정하지 않는다(<see cref="ResetConfirmation"/> 로만 푼다).</item>
/// <item>비활성이면 판정·카운트 없음(끄는 순간 카운트다운도 0).</item>
/// <item>카운트다운 중 = 신규 공격·스킬·대시·상호작용 시작 제한 신호(<see cref="IsRestrictingActions"/>, 걷기 허용). 행동이 이미 진행 중이면 시작만 미룬다 — 시작 뒤의 행동은 카운트다운을 끊지 않는다.</item>
/// </list>
/// 시각은 호출자가 넣는다(PartyGatherZone = NetworkManager.ServerTime.Time). 종료 시각 0 이하 = 카운트다운 없음.
/// </summary>
public sealed class PartyGatherRules
{
    /// <summary>카운트다운 없음을 뜻하는 종료 시각.</summary>
    public const double NoCountdown = 0d;

    /// <summary>기본 집결 유지 시간(초) — PLAN-86 §7.</summary>
    public const double DefaultHoldSeconds = 5d;

    // 시작 시각 0 + 유지 0초여도 "진행 중"(> 0)으로 보이게 하는 하한.
    const double MinActiveEndTime = 1e-6;

    readonly List<ulong> _participants = new List<ulong>();
    readonly Dictionary<ulong, PartyGatherMemberState> _states = new Dictionary<ulong, PartyGatherMemberState>();
    double _holdSeconds;

    public PartyGatherRules(double holdSeconds = DefaultHoldSeconds)
    {
        HoldSeconds = holdSeconds;
    }

    /// <summary>유지 시간(초, 0 이상). 바꿔도 이미 시작한 카운트다운의 종료 시각은 그대로다.</summary>
    public double HoldSeconds
    {
        get => _holdSeconds;
        set => _holdSeconds = Math.Max(0d, value);
    }

    public bool IsActive { get; private set; }

    /// <summary>확정을 한 번 내보냈다(래치).</summary>
    public bool IsConfirmed { get; private set; }

    /// <summary>카운트다운 종료 서버 시각. <see cref="NoCountdown"/>(0) 이하 = 없음.</summary>
    public double CountdownEndTime { get; private set; } = NoCountdown;

    public bool IsCountdownActive => CountdownEndTime > NoCountdown;

    /// <summary>신규 공격·스킬·대시·상호작용 시작을 막아야 하는가(= 카운트다운 중). 걷기는 막지 않는다.</summary>
    public bool IsRestrictingActions => IsActive && !IsConfirmed && IsCountdownActive;

    public IReadOnlyList<ulong> Participants => _participants;

    public int ParticipantCount => _participants.Count;

    /// <summary>집결 인정 인원(안 + 쓰러짐·사망 아님) — "집결 n/m" 의 n.</summary>
    public int GatheredCount
    {
        get
        {
            var count = 0;
            foreach (var clientId in _participants)
            {
                if (_states.TryGetValue(clientId, out var state) && state.Gathered)
                    count++;
            }

            return count;
        }
    }

    /// <summary>참여자 전원 집결 인정(1명 이상).</summary>
    public bool AreAllGathered => _participants.Count > 0 && GatheredCount == _participants.Count;

    public bool IsParticipant(ulong clientId) => _participants.Contains(clientId);

    /// <summary>켜고 끈다. 바뀌면 카운트다운 0. 확정 래치는 건드리지 않는다.</summary>
    public void SetActive(bool active)
    {
        if (IsActive == active)
            return;

        IsActive = active;
        CancelCountdown();
    }

    /// <summary>참여자 추가 — 인원 변화라 카운트다운 0. 이미 있으면 false.</summary>
    public bool AddParticipant(ulong clientId)
    {
        if (_participants.Contains(clientId))
            return false;

        _participants.Add(clientId);
        CancelCountdown();
        return true;
    }

    /// <summary>참여자 제거(연결 종료 확정 등) — 인원 변화라 카운트다운 0. 없으면 false.</summary>
    public bool RemoveParticipant(ulong clientId)
    {
        if (!_participants.Remove(clientId))
            return false;

        _states.Remove(clientId);
        CancelCountdown();
        return true;
    }

    /// <summary>명단 통째 교체. 구성이 바뀌었으면 카운트다운 0 + true(순서만 다르면 바뀐 것으로 보지 않는다).</summary>
    public bool SetParticipants(IEnumerable<ulong> clientIds)
    {
        var next = new List<ulong>();
        foreach (var clientId in clientIds)
        {
            if (!next.Contains(clientId))
                next.Add(clientId);
        }

        var same = next.Count == _participants.Count;
        if (same)
        {
            foreach (var clientId in next)
            {
                if (!_participants.Contains(clientId))
                {
                    same = false;
                    break;
                }
            }
        }

        if (same)
            return false;

        _participants.Clear();
        _participants.AddRange(next);
        var stale = new List<ulong>();
        foreach (var clientId in _states.Keys)
        {
            if (!_participants.Contains(clientId))
                stale.Add(clientId);
        }

        foreach (var clientId in stale)
            _states.Remove(clientId);
        CancelCountdown();
        return true;
    }

    /// <summary>참여자의 이번 상태. 참여자가 아니면 무시하고 false. 상태가 없는 참여자는 밖으로 본다.</summary>
    public bool SetMemberState(ulong clientId, PartyGatherMemberState state)
    {
        if (!_participants.Contains(clientId))
            return false;

        _states[clientId] = state;
        return true;
    }

    /// <summary>
    /// 판정 한 번. 확정 순간에만 true(1회). 비활성·확정 뒤에는 아무것도 하지 않는다.
    /// 전원 집결이 깨지면 카운트다운 0, 전원 집결 + 진행 중 행동 없음이면 now 부터 카운트다운, 종료 시각에 닿으면 확정.
    /// </summary>
    public bool Tick(double now)
    {
        if (!IsActive || IsConfirmed)
            return false;

        if (!AreAllGathered)
        {
            CancelCountdown();
            return false;
        }

        if (!IsCountdownActive)
        {
            if (AnyBusy())
                return false;

            CountdownEndTime = Math.Max(now + _holdSeconds, MinActiveEndTime);
        }

        if (now < CountdownEndTime)
            return false;

        IsConfirmed = true;
        CountdownEndTime = NoCountdown;
        return true;
    }

    /// <summary>확정 래치를 풀고 카운트다운 0 — 같은 구역을 다시 쓸 때(판 끝 로비 복귀 등).</summary>
    public void ResetConfirmation()
    {
        IsConfirmed = false;
        CancelCountdown();
    }

    /// <summary>남은 초(0 이상). 종료 시각이 0 이하면 0 — 클라 표시용.</summary>
    public static double RemainingSeconds(double countdownEndTime, double now)
    {
        return countdownEndTime > NoCountdown ? Math.Max(0d, countdownEndTime - now) : 0d;
    }

    bool AnyBusy()
    {
        foreach (var clientId in _participants)
        {
            if (_states.TryGetValue(clientId, out var state) && state.Busy)
                return true;
        }

        return false;
    }

    void CancelCountdown()
    {
        CountdownEndTime = NoCountdown;
    }
}
