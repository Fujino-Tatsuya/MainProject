using System;
using System.Collections.Generic;

/// <summary>
/// 서버 전용 재합류 보관소 — 할 일 48 C14. 판 도중 나간 사람의 상태를 playerId 키로 들고 있다가 같은 사람이
/// 다시 스폰되면(<see cref="MatchRoster.PlayerRejoined"/>) 각 시스템이 꺼내 간다. 꺼내면 지운다(같은 기록을 두 번 되돌리지 않는다).
/// <list type="bullet">
/// <item>특성(R4c): <see cref="TutorialTraitDirector"/> 가 퇴장 때 <see cref="SaveTraits"/>, 재합류 때 <see cref="TryTakeTraits"/>.
/// 특성 디렉터가 새로 스폰되면(맵 다시 로드) 이전 판 특성 기록은 의미가 없어 <see cref="ClearTraits"/>.</item>
/// <item>생명·체력(R5c): 이 클래스에 붙인다.</item>
/// </list>
/// 비우기: 판 명단과 같은 경계(<c>NetworkLoadingFlowController.BeginMatchRoster/EndMatchRoster</c> — 판 시작·맵 → 로비 전환·세션 시작·종료).
/// static 보관소라 비우기 누락이 곧 누적이다(R1) — 비울 때 크기를 로그로 남긴다.
/// 실제 서버 인스턴스는 <see cref="Server"/> 하나다. 순수 C#(EditMode 테스트 대상).
/// </summary>
public sealed class RejoinMemory
{
    /// <summary>서버 인스턴스.</summary>
    public static RejoinMemory Server { get; } = new RejoinMemory();

    readonly Dictionary<string, TutorialTraitRejoinSnapshot> _traits =
        new Dictionary<string, TutorialTraitRejoinSnapshot>(StringComparer.Ordinal);

    /// <summary>보관 중인 특성 기록 수.</summary>
    public int TraitCount => _traits.Count;

    /// <summary>보관 중인 전체 기록 수(누적 감시 로그용).</summary>
    public int Count => _traits.Count;

    /// <summary>퇴장 — 그 playerId 의 특성 상태를 보관한다(이전 기록은 덮는다). playerId·기록이 비면 false.</summary>
    public bool SaveTraits(string playerId, TutorialTraitRejoinSnapshot snapshot)
    {
        if (string.IsNullOrEmpty(playerId) || snapshot == null)
            return false;

        _traits[playerId] = snapshot;
        return true;
    }

    /// <summary>재합류 — 그 playerId 의 특성 기록을 꺼내고 지운다. 없으면 false.</summary>
    public bool TryTakeTraits(string playerId, out TutorialTraitRejoinSnapshot snapshot)
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

    /// <summary>전부 비운다. 비우기 전 전체 수를 돌려준다.</summary>
    public int Clear()
    {
        int count = Count;
        _traits.Clear();
        return count;
    }
}
