using System;
using System.Collections.Generic;

/// <summary>
/// 서버 전용 clientId ↔ playerId 매핑 — PLAN-48 Q4(재접속 클라는 새 clientId 와 무관하게 playerId 로 이어 간다).
/// 승인 때 <see cref="Register"/>, 퇴장 때 <see cref="RemoveClient"/>(clientId 매핑만 지우고 그 playerId 를 봤다는 기록은 남긴다),
/// 세션 시작·종료 때 <see cref="Clear"/>. 실제 서버 인스턴스는 <see cref="Server"/> 하나이고 NetworkSessionLauncher 가 관리한다.
/// 캐릭터 선택·증강을 playerId 로 옮기는 건 C8 — 지금은 조회 API 까지만.
/// </summary>
public sealed class PlayerIdRegistry
{
    /// <summary>NetworkSessionLauncher 가 채우는 서버 인스턴스. 클라에선 비어 있다.</summary>
    public static PlayerIdRegistry Server { get; } = new PlayerIdRegistry();

    readonly Dictionary<ulong, string> _playerIdByClient = new Dictionary<ulong, string>();
    readonly Dictionary<string, ulong> _clientByPlayerId = new Dictionary<string, ulong>(StringComparer.Ordinal);
    readonly HashSet<string> _knownPlayerIds = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>지금 접속(승인) 중인 인원 — 호스트 포함.</summary>
    public int ConnectedCount => _playerIdByClient.Count;

    /// <summary>이번 세션에 한 번이라도 승인된 playerId. 퇴장해도 남는다.</summary>
    public IReadOnlyCollection<string> KnownPlayerIds => _knownPlayerIds;

    /// <summary>clientId 에 playerId 를 묶는다. 같은 clientId 가 다시 오면 이전 매핑을 바꾼다.</summary>
    public void Register(ulong clientId, string playerId)
    {
        if (string.IsNullOrEmpty(playerId))
            throw new ArgumentException("playerId 가 비었다.", nameof(playerId));

        RemoveClient(clientId);
        _playerIdByClient[clientId] = playerId;
        _clientByPlayerId[playerId] = clientId;
        _knownPlayerIds.Add(playerId);
    }

    /// <summary>퇴장. clientId 매핑만 지운다 — playerId 기준 데이터(<see cref="KnownPlayerIds"/>)는 남긴다.</summary>
    public bool RemoveClient(ulong clientId)
    {
        if (!_playerIdByClient.TryGetValue(clientId, out var playerId))
            return false;

        _playerIdByClient.Remove(clientId);
        if (_clientByPlayerId.TryGetValue(playerId, out var mapped) && mapped == clientId)
            _clientByPlayerId.Remove(playerId);
        return true;
    }

    public bool TryGetPlayerId(ulong clientId, out string playerId)
    {
        return _playerIdByClient.TryGetValue(clientId, out playerId);
    }

    public bool TryGetClientId(string playerId, out ulong clientId)
    {
        clientId = 0;
        return playerId != null && _clientByPlayerId.TryGetValue(playerId, out clientId);
    }

    /// <summary>같은 playerId 가 지금 접속 중인가.</summary>
    public bool IsPlayerConnected(string playerId)
    {
        return playerId != null && _clientByPlayerId.ContainsKey(playerId);
    }

    public bool IsKnownPlayer(string playerId)
    {
        return playerId != null && _knownPlayerIds.Contains(playerId);
    }

    /// <summary>세션 경계 — 매핑과 기록을 모두 지운다.</summary>
    public void Clear()
    {
        _playerIdByClient.Clear();
        _clientByPlayerId.Clear();
        _knownPlayerIds.Clear();
    }
}
