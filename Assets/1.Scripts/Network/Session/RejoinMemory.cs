using System;
using System.Collections.Generic;

/// <summary>
/// 서버 전용 재합류 보관소 — 할 일 48 C14. 판 도중 나간 사람의 상태를 playerId 키로 들고 있다가 같은 사람이
/// 다시 스폰되면(<see cref="MatchRoster.PlayerRejoined"/>) 각 시스템이 꺼내 간다. 꺼내면 지운다(같은 기록을 두 번 되돌리지 않는다).
/// <list type="bullet">
/// <item>특성(R4c): <see cref="TraitDirector"/> 가 퇴장 때 <see cref="SaveTraits"/>, 재합류 때 <see cref="TryTakeTraits"/>.
/// 특성 디렉터가 새로 스폰되면(맵 다시 로드) 이전 판 특성 기록은 의미가 없어 <see cref="ClearTraits"/>.</item>
/// <item>생명·체력(R5c): NGO 는 퇴장 콜백(OnClientDisconnectCallback)보다 <b>먼저</b> PlayerObject 를 디스폰한다(NetworkConnectionManager.OnClientDisconnectFromServer).
/// 그래서 <see cref="PlayerLifeCycleController"/> 가 디스폰 때 clientId 키로 <see cref="NoteDespawnedLife"/> 하고, 퇴장 콜백이
/// <see cref="CommitLifeOnDisconnect"/> 로 playerId 키 기록으로 옮긴다(퇴장 콜백 순간 PlayerObject 유무와 무관). 디스폰 메모는 퇴장이 아닌
/// 디스폰(판 재시작·전환)에도 남지만 퇴장으로 확정되기 전에는 쓰이지 않는다. 재합류 때 <see cref="TryTakeLife"/>.
/// 전멸 리스폰 때 <see cref="OverwriteAbsentLivesAfterWipe"/>(부재자 = 보관 중인 기록 전부 → Alive·만피).</item>
/// </list>
/// 비우기: 판 명단과 같은 경계(<c>NetworkLoadingFlowController.BeginMatchRoster/EndMatchRoster</c> — 판 시작·맵 → 로비 전환·세션 시작·종료).
/// static 보관소라 비우기 누락이 곧 누적이다(R1) — 비울 때 크기를 로그로 남긴다.
/// 실제 서버 인스턴스는 <see cref="Server"/> 하나다. 순수 C#(EditMode 테스트 대상).
/// </summary>
public sealed class RejoinMemory
{
    /// <summary>서버 인스턴스.</summary>
    public static RejoinMemory Server { get; } = new RejoinMemory();

    readonly Dictionary<string, TraitRejoinSnapshot> _traits =
        new Dictionary<string, TraitRejoinSnapshot>(StringComparer.Ordinal);
    readonly Dictionary<string, PlayerRejoinLifeRecord> _lives =
        new Dictionary<string, PlayerRejoinLifeRecord>(StringComparer.Ordinal);
    // 디스폰 순간 생명 기록(clientId 키) — 퇴장 콜백에서 playerId 키로 옮기거나 버린다.
    readonly Dictionary<ulong, PlayerRejoinLifeRecord> _despawnedLives = new Dictionary<ulong, PlayerRejoinLifeRecord>();
    readonly List<string> _keyBuffer = new List<string>();

    /// <summary>보관 중인 특성 기록 수.</summary>
    public int TraitCount => _traits.Count;

    /// <summary>보관 중인 생명 기록 수(퇴장 확정된 것만).</summary>
    public int LifeCount => _lives.Count;

    /// <summary>퇴장 확정 전 디스폰 메모 수.</summary>
    public int DespawnedLifeCount => _despawnedLives.Count;

    /// <summary>보관 중인 전체 기록 수(누적 감시 로그용) — 특성 + 생명 + 디스폰 메모.</summary>
    public int Count => _traits.Count + _lives.Count + _despawnedLives.Count;

    /// <summary>퇴장 — 그 playerId 의 특성 상태를 보관한다(이전 기록은 덮는다). playerId·기록이 비면 false.</summary>
    public bool SaveTraits(string playerId, TraitRejoinSnapshot snapshot)
    {
        if (string.IsNullOrEmpty(playerId) || snapshot == null)
            return false;

        _traits[playerId] = snapshot;
        return true;
    }

    /// <summary>재합류 — 그 playerId 의 특성 기록을 꺼내고 지운다. 없으면 false.</summary>
    public bool TryTakeTraits(string playerId, out TraitRejoinSnapshot snapshot)
    {
        snapshot = null;
        if (string.IsNullOrEmpty(playerId) || !_traits.TryGetValue(playerId, out snapshot))
            return false;

        _traits.Remove(playerId);
        return true;
    }

    /// <summary>특성 기록만 비운다. 비우기 전 수를 돌려준다.</summary>
    public int ClearTraits()
    {
        int count = _traits.Count;
        _traits.Clear();
        return count;
    }

    /// <summary>디스폰 — 그 clientId 의 생명 기록을 메모한다(같은 clientId 는 덮는다).</summary>
    public void NoteDespawnedLife(ulong clientId, PlayerRejoinLifeRecord record)
    {
        _despawnedLives[clientId] = record;
    }

    /// <summary>
    /// 퇴장 확정 — 그 clientId 의 디스폰 메모를 playerId 키 기록으로 옮긴다(이전 기록은 덮는다).
    /// 메모가 없으면(스폰 보류 중 다시 나감 — PlayerObject 가 없었다) 이전 기록을 그대로 두고 false. 메모는 어느 쪽이든 지운다.
    /// </summary>
    public bool CommitLifeOnDisconnect(ulong clientId, string playerId, out PlayerRejoinLifeRecord record)
    {
        if (!_despawnedLives.TryGetValue(clientId, out record))
            return false;

        _despawnedLives.Remove(clientId);
        return SaveLife(playerId, record);
    }

    /// <summary>그 playerId 의 생명 기록을 보관한다(이전 기록은 덮는다). playerId 가 비면 false.</summary>
    public bool SaveLife(string playerId, PlayerRejoinLifeRecord record)
    {
        if (string.IsNullOrEmpty(playerId))
            return false;

        _lives[playerId] = record;
        return true;
    }

    /// <summary>재합류 — 그 playerId 의 생명 기록을 꺼내고 지운다. 없으면 false(새 PlayerObject 의 Alive·만피 그대로).</summary>
    public bool TryTakeLife(string playerId, out PlayerRejoinLifeRecord record)
    {
        record = default;
        if (string.IsNullOrEmpty(playerId) || !_lives.TryGetValue(playerId, out record))
            return false;

        _lives.Remove(playerId);
        return true;
    }

    /// <summary>
    /// 전멸 리스폰 — 보관 중인 생명 기록(= 지금 자리에 없는 사람)을 전부 Alive·만피로 덮는다. 그 자리에 있었으면 살아났을 것.
    /// 바꾼 수를 돌려준다(이미 Alive·만피였던 기록은 세지 않는다). 디스폰 메모는 손대지 않는다(퇴장 확정 전 — 같은 프레임 안에 확정된다).
    /// </summary>
    public int OverwriteAbsentLivesAfterWipe()
    {
        var full = PlayerRejoinLifeRecord.AliveFull;
        _keyBuffer.Clear();
        foreach (var pair in _lives)
        {
            if (!pair.Value.Equals(full))
                _keyBuffer.Add(pair.Key);
        }

        foreach (var key in _keyBuffer)
            _lives[key] = full;

        int changed = _keyBuffer.Count;
        _keyBuffer.Clear();
        return changed;
    }

    /// <summary>생명 기록·디스폰 메모만 비운다(판 재시작 — 처음 로드한 것처럼). 비우기 전 수를 돌려준다.</summary>
    public int ClearLives()
    {
        int count = _lives.Count + _despawnedLives.Count;
        _lives.Clear();
        _despawnedLives.Clear();
        return count;
    }

    /// <summary>전부 비운다. 비우기 전 전체 수를 돌려준다.</summary>
    public int Clear()
    {
        int count = Count;
        _traits.Clear();
        _lives.Clear();
        _despawnedLives.Clear();
        return count;
    }
}
