using System;
using System.Collections.Generic;

/// <summary>로비 자리 하나 — <see cref="LobbyReadyRules"/> 의 순수 상태(NGO 복제는 LobbySession 의 LobbyPlayerEntry).</summary>
public readonly struct LobbySeat
{
    public LobbySeat(ulong clientId, int characterId, bool ready)
    {
        ClientId = clientId;
        CharacterId = characterId;
        Ready = ready;
    }

    public ulong ClientId { get; }
    public int CharacterId { get; }
    public bool Ready { get; }

    public LobbySeat WithReady(bool ready) => new LobbySeat(ClientId, CharacterId, ready);
    public LobbySeat WithCharacter(int characterId) => new LobbySeat(ClientId, characterId, Ready);
}

/// <summary>캐릭터 변경 거부 사유 — <see cref="LobbyReadyRules.TryChangeCharacter"/>.</summary>
public enum LobbyCharacterChangeReject
{
    None = 0,
    /// <summary>로비 자리에 없는 clientId.</summary>
    UnknownPlayer,
    /// <summary>준비 상태 — 준비를 풀어야 바꿀 수 있다.</summary>
    Ready,
    /// <summary>카운트다운 중.</summary>
    CountdownActive,
    /// <summary>카운트다운이 끝나 게임 시작 래치가 걸렸다.</summary>
    GameStarting,
    /// <summary>직전 교체 뒤 쿨다운이 안 지났다.</summary>
    Cooldown,
    /// <summary>로스터에 없거나 쓸 수 없는 캐릭터.</summary>
    UnavailableCharacter,
}

/// <summary>
/// [서버] 로비 준비·카운트다운 규칙 — PLAN-48 §4.6. MonoBehaviour·NGO 의존 없는 순수 상태(EditMode 테스트 대상).
/// <list type="bullet">
/// <item>접속자 1~<see cref="MaxPlayers"/>명이 전원 Ready 가 되는 순간 카운트다운 시작. 0명이면 시작하지 않는다.</item>
/// <item>누가 Unready 하면 카운트다운 취소. 퇴장·새 접속이면 카운트다운 취소 + 전원 Ready 초기화(인원이 바뀌면 다시 확인받는다).</item>
/// <item>Ready 상태·카운트다운 중·시작 래치 뒤에는 캐릭터 변경 거부.</item>
/// <item>호스트 예외 없음 — 호스트도 똑같이 준비한다.</item>
/// <item>서버 시각 ≥ 종료 시각이면 <see cref="TryConsumeStart"/> 가 한 번만 true(시작 래치). 래치 뒤에는 준비·인원 변경이 카운트다운을 건드리지 않는다.</item>
/// </list>
/// 시각은 호출자가 넣는다(LobbySession = NetworkManager.ServerTime.Time). 종료 시각 0 이하 = 카운트다운 없음.
/// </summary>
public sealed class LobbyReadyRules
{
    /// <summary>카운트다운 없음을 뜻하는 종료 시각.</summary>
    public const double NoCountdown = 0d;

    // 시작 시각 0 + 카운트다운 0초여도 "진행 중"(> 0)으로 보이게 하는 하한.
    const double MinActiveEndTime = 1e-6;

    readonly List<LobbySeat> _seats = new List<LobbySeat>();
    readonly Dictionary<ulong, double> _lastCharacterChangeTime = new Dictionary<ulong, double>();

    public LobbyReadyRules(int maxPlayers)
    {
        if (maxPlayers < 1)
            throw new ArgumentOutOfRangeException(nameof(maxPlayers), "1 이상이어야 한다.");
        MaxPlayers = maxPlayers;
    }

    public int MaxPlayers { get; }

    /// <summary>접속 순서대로의 자리.</summary>
    public IReadOnlyList<LobbySeat> Seats => _seats;

    /// <summary>카운트다운 종료 서버 시각. <see cref="NoCountdown"/>(0) 이하 = 없음.</summary>
    public double CountdownEndTime { get; private set; } = NoCountdown;

    /// <summary>카운트다운 중이거나 시작 래치가 걸렸다(접속 승인은 이걸 "진행 중"으로 본다).</summary>
    public bool IsCountdownActive => CountdownEndTime > NoCountdown;

    /// <summary>카운트다운이 끝나 게임 시작을 한 번 내보냈다.</summary>
    public bool HasStarted { get; private set; }

    public int ReadyCount
    {
        get
        {
            var count = 0;
            foreach (var seat in _seats)
            {
                if (seat.Ready)
                    count++;
            }

            return count;
        }
    }

    /// <summary>1~MaxPlayers 명이고 전원 Ready.</summary>
    public bool AreAllReady => _seats.Count >= 1 && _seats.Count <= MaxPlayers && ReadyCount == _seats.Count;

    public bool TryGetSeat(ulong clientId, out LobbySeat seat)
    {
        var index = IndexOf(clientId);
        seat = index >= 0 ? _seats[index] : default;
        return index >= 0;
    }

    /// <summary>새 접속 — 자리를 만들고 전원 Ready 초기화 + 카운트다운 취소. 이미 있는 clientId 면 아무것도 안 하고 false.</summary>
    public bool AddPlayer(ulong clientId, int characterId)
    {
        if (IndexOf(clientId) >= 0)
            return false;

        _seats.Add(new LobbySeat(clientId, characterId, false));
        ResetAfterRosterChange();
        return true;
    }

    /// <summary>퇴장 — 자리를 지우고 전원 Ready 초기화 + 카운트다운 취소. 없는 clientId 면 false.</summary>
    public bool RemovePlayer(ulong clientId)
    {
        var index = IndexOf(clientId);
        if (index < 0)
            return false;

        _seats.RemoveAt(index);
        _lastCharacterChangeTime.Remove(clientId);
        ResetAfterRosterChange();
        return true;
    }

    /// <summary>
    /// Ready 지정. 바뀌었으면 true. Unready = 카운트다운 취소, 이번 Ready 로 전원 Ready 가 되면 now + countdownSeconds 에 끝나는 카운트다운 시작.
    /// 모르는 clientId·시작 래치 뒤에는 false.
    /// </summary>
    public bool SetReady(ulong clientId, bool ready, double now, double countdownSeconds)
    {
        var index = IndexOf(clientId);
        if (index < 0 || HasStarted || _seats[index].Ready == ready)
            return false;

        _seats[index] = _seats[index].WithReady(ready);
        if (!ready)
        {
            CountdownEndTime = NoCountdown;
        }
        else if (AreAllReady && !IsCountdownActive)
        {
            CountdownEndTime = Math.Max(now + Math.Max(0d, countdownSeconds), MinActiveEndTime);
        }

        return true;
    }

    public bool ToggleReady(ulong clientId, double now, double countdownSeconds)
    {
        return TryGetSeat(clientId, out var seat) && SetReady(clientId, !seat.Ready, now, countdownSeconds);
    }

    /// <summary>지금 캐릭터를 바꿀 수 있는가(캐릭터 유효성은 <see cref="TryChangeCharacter"/> 가 따로 본다).</summary>
    public LobbyCharacterChangeReject CanChangeCharacter(ulong clientId, double now, double cooldownSeconds)
    {
        var index = IndexOf(clientId);
        if (index < 0)
            return LobbyCharacterChangeReject.UnknownPlayer;
        if (HasStarted)
            return LobbyCharacterChangeReject.GameStarting;
        if (IsCountdownActive)
            return LobbyCharacterChangeReject.CountdownActive;
        if (_seats[index].Ready)
            return LobbyCharacterChangeReject.Ready;
        if (_lastCharacterChangeTime.TryGetValue(clientId, out var last) && now - last < cooldownSeconds)
            return LobbyCharacterChangeReject.Cooldown;
        return LobbyCharacterChangeReject.None;
    }

    /// <summary>
    /// 캐릭터 변경. 허용이면 None 을 돌려주고 자리를 바꾼다(같은 캐릭터면 바꾸지 않고 쿨다운도 걸지 않는다).
    /// isCharacterAvailable = 로스터 검증 결과(호출자 몫).
    /// </summary>
    public LobbyCharacterChangeReject TryChangeCharacter(
        ulong clientId, int characterId, bool isCharacterAvailable, double now, double cooldownSeconds)
    {
        var reject = CanChangeCharacter(clientId, now, cooldownSeconds);
        if (reject != LobbyCharacterChangeReject.None)
            return reject;
        if (!isCharacterAvailable)
            return LobbyCharacterChangeReject.UnavailableCharacter;

        var index = IndexOf(clientId);
        if (_seats[index].CharacterId == characterId)
            return LobbyCharacterChangeReject.None;

        _seats[index] = _seats[index].WithCharacter(characterId);
        _lastCharacterChangeTime[clientId] = now;
        return LobbyCharacterChangeReject.None;
    }

    /// <summary>카운트다운이 끝났으면(now ≥ 종료 시각) 시작 래치를 걸고 처음 한 번만 true.</summary>
    public bool TryConsumeStart(double now)
    {
        if (HasStarted || !IsCountdownActive || now < CountdownEndTime)
            return false;

        HasStarted = true;
        return true;
    }

    /// <summary>남은 초(0 이상). 종료 시각이 0 이하면 0 — 클라 표시용.</summary>
    public static double RemainingSeconds(double countdownEndTime, double now)
    {
        return countdownEndTime > NoCountdown ? Math.Max(0d, countdownEndTime - now) : 0d;
    }

    void ResetAfterRosterChange()
    {
        // 시작 래치 뒤에는 로비가 곧 내려간다 — 인원 변화가 시작을 되돌리지 않는다.
        if (HasStarted)
            return;

        for (var i = 0; i < _seats.Count; i++)
            _seats[i] = _seats[i].WithReady(false);
        CountdownEndTime = NoCountdown;
    }

    int IndexOf(ulong clientId)
    {
        for (var i = 0; i < _seats.Count; i++)
        {
            if (_seats[i].ClientId == clientId)
                return i;
        }

        return -1;
    }
}
