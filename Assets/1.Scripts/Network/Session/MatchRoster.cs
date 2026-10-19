using System;
using System.Collections.Generic;

/// <summary>
/// 서버 전용 판 명단 — 할 일 48 C14(판 도중 재합류). 판이 시작될 때 접속해 있던 playerId 를 고정하고,
/// 판 도중에는 이 명단 안 사람만 다시 받는다(백필 금지 — 판 인원은 로비 상한 3명을 넘지 않는다).
/// <see cref="PlayerIdRegistry.KnownPlayerIds"/> 는 세션 단위(로비에서만 있다 나간 사람 포함)라 명단으로 못 쓴다.
/// <list type="bullet">
/// <item><see cref="Begin"/>: 로비 → 맵 로딩 시작(NetworkLoadingFlowController.StartGameLoading 래치 통과 직후).</item>
/// <item><see cref="End"/>: 맵 → 로비 Single 전환 진입, 세션 시작·종료.</item>
/// </list>
/// 데모 흐름 밖(Dev 부팅·태그 없는 MPPM)에선 Begin 을 부르지 않는다 — 명단 기능 전체가 꺼진다.
/// 실제 서버 인스턴스는 <see cref="Server"/> 하나다. 순수 C#(EditMode 테스트 대상).
/// </summary>
public sealed class MatchRoster
{
    /// <summary>서버 인스턴스. 클라에선 늘 비활성이다.</summary>
    public static MatchRoster Server { get; } = new MatchRoster();

    /// <summary>
    /// [서버] 판 도중 재합류 — 같은 playerId 가 새 clientId 로 다시 스폰된 직후(인자: playerId, oldClientId, newClientId).
    /// 새 PlayerObject 가 이미 있다. clientId 로 키를 잡은 시스템(특성·튜토리얼·생명·통계)이 여기서 옛 키를 새 키로 옮긴다(R4c·R5c).
    /// 발행은 <see cref="RaisePlayerRejoined"/> 만 한다.
    /// </summary>
    public static event Action<string, ulong, ulong> PlayerRejoined;

    /// <summary>[서버] <see cref="PlayerRejoined"/> 발행. 구독자 하나가 던져도 나머지는 계속 받는다. 받은 구독자 수를 돌려준다.</summary>
    public static int RaisePlayerRejoined(string playerId, ulong oldClientId, ulong newClientId)
    {
        var handlers = PlayerRejoined;
        if (handlers == null)
            return 0;

        var delegates = handlers.GetInvocationList();
        foreach (var handler in delegates)
        {
            try
            {
                ((Action<string, ulong, ulong>)handler)(playerId, oldClientId, newClientId);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
            }
        }

        return delegates.Length;
    }

    readonly HashSet<string> _playerIds = new HashSet<string>(StringComparer.Ordinal);
    // 그 판에서 playerId 가 마지막으로 쓴 clientId — 퇴장해도 남는다(재합류 때 옛 clientId 로 키를 옮기는 데 쓴다).
    readonly Dictionary<string, ulong> _lastClientByPlayerId = new Dictionary<string, ulong>(StringComparer.Ordinal);
    // 지금 명단에 묶여 접속 중인 clientId.
    readonly Dictionary<ulong, string> _playerIdByBoundClient = new Dictionary<ulong, string>();

    /// <summary>판 진행 중(명단 고정됨).</summary>
    public bool IsActive { get; private set; }

    /// <summary>명단 인원.</summary>
    public int Count => _playerIds.Count;

    /// <summary>지금 명단에 묶여 접속 중인 인원.</summary>
    public int BoundCount => _playerIdByBoundClient.Count;

    /// <summary>
    /// 판 시작 — 명단을 새로 고정한다. 빈 값·중복은 건너뛴다. 이미 활성이면 이전 명단을 버리고 다시 잡는다.
    /// clientId 묶기는 호출자가 <see cref="BindClient"/> 로 한다.
    /// </summary>
    public void Begin(IEnumerable<string> playerIds)
    {
        Reset();
        IsActive = true;
        if (playerIds == null)
            return;

        foreach (var playerId in playerIds)
        {
            if (!string.IsNullOrEmpty(playerId))
                _playerIds.Add(playerId);
        }
    }

    /// <summary>판 끝 — 명단을 비우고 비활성으로 돌린다. 비우기 전 명단 인원(누적 감시 로그용)을 돌려준다.</summary>
    public int End()
    {
        var count = _playerIds.Count;
        Reset();
        return count;
    }

    public bool Contains(string playerId)
    {
        return IsActive && playerId != null && _playerIds.Contains(playerId);
    }

    /// <summary>
    /// 명단 안 playerId 를 지금 clientId 에 묶는다. 명단 밖이거나 비활성이면 false.
    /// <paramref name="rejoined"/> = 이 판에서 다른 clientId 로 묶인 적이 있다(재합류), <paramref name="oldClientId"/> = 그 clientId.
    /// 같은 clientId 를 다시 묶으면 재합류가 아니다.
    /// </summary>
    public bool BindClient(string playerId, ulong clientId, out bool rejoined, out ulong oldClientId)
    {
        rejoined = false;
        oldClientId = 0;
        if (!Contains(playerId))
            return false;

        if (_lastClientByPlayerId.TryGetValue(playerId, out var last) && last != clientId)
        {
            rejoined = true;
            oldClientId = last;
            _playerIdByBoundClient.Remove(last);
        }

        UnbindClient(clientId);
        _lastClientByPlayerId[playerId] = clientId;
        _playerIdByBoundClient[clientId] = playerId;
        return true;
    }

    /// <summary>퇴장 — clientId 묶임만 푼다. 그 playerId 의 마지막 clientId 기록과 명단 자리는 남는다.</summary>
    public bool UnbindClient(ulong clientId)
    {
        return _playerIdByBoundClient.Remove(clientId);
    }

    /// <summary>이 판에서 그 playerId 가 마지막으로 쓴 clientId(지금 접속 여부와 무관).</summary>
    public bool TryGetLastClientId(string playerId, out ulong clientId)
    {
        clientId = 0;
        return playerId != null && _lastClientByPlayerId.TryGetValue(playerId, out clientId);
    }

    void Reset()
    {
        IsActive = false;
        _playerIds.Clear();
        _lastClientByPlayerId.Clear();
        _playerIdByBoundClient.Clear();
    }
}
