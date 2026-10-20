using System;
using System.Collections.Generic;

/// <summary>
/// 캐릭터 선택 저장소의 순수 본체 — clientId 별 현재 선택 + playerId 별 마지막 선택(PLAN-48 Q4 재접속 유지).
/// 재접속하면 clientId 가 바뀌므로 <see cref="BindPlayer"/> 로 새 clientId 에 playerId 를 묶을 때 마지막 선택을 되살린다.
/// <see cref="RemoveClient"/> 는 clientId 쪽만 지우고 playerId 기억은 남긴다. 세션 경계는 <see cref="Clear"/>.
/// 서버 인스턴스는 <see cref="ServerCharacterSelectionStore"/> 가 감싼다. EditMode 테스트 대상.
/// </summary>
public sealed class CharacterSelectionMemory
{
    readonly Dictionary<ulong, int> _byClient = new Dictionary<ulong, int>();
    readonly Dictionary<ulong, string> _playerByClient = new Dictionary<ulong, string>();
    readonly Dictionary<string, int> _byPlayer = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>clientId 에 playerId 를 묶는다. 그 playerId 의 마지막 선택이 있으면 이 clientId 의 선택으로 되살린다.</summary>
    public void BindPlayer(ulong clientId, string playerId)
    {
        if (string.IsNullOrEmpty(playerId))
            return;

        _playerByClient[clientId] = playerId;
        if (_byPlayer.TryGetValue(playerId, out var characterId))
            _byClient[clientId] = characterId;
    }

    /// <summary>선택 기록 — 묶인 playerId 가 있으면 그쪽 기억도 갱신한다.</summary>
    public void Set(ulong clientId, int characterId)
    {
        _byClient[clientId] = characterId;
        if (_playerByClient.TryGetValue(clientId, out var playerId))
            _byPlayer[playerId] = characterId;
    }

    public bool TryGet(ulong clientId, out int characterId)
    {
        return _byClient.TryGetValue(clientId, out characterId);
    }

    /// <summary>퇴장 — clientId 쪽만 지운다. playerId 의 마지막 선택은 남아 재접속 때 <see cref="BindPlayer"/> 가 되살린다.</summary>
    public void RemoveClient(ulong clientId)
    {
        _byClient.Remove(clientId);
        _playerByClient.Remove(clientId);
    }

    /// <summary>세션 경계 — 전부 지운다.</summary>
    public void Clear()
    {
        _byClient.Clear();
        _playerByClient.Clear();
        _byPlayer.Clear();
    }
}
