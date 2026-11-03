using System.Collections.Generic;
using Unity.Netcode;

/// <summary>
/// 구역 소개 연출(2구역 적 소개 · 4구역 장치·다리 소개) 서버 공용 몫 — 연출 잠금 · 스킵 투표자·표 · "살아 있는 전원이 그 구간 안" 판정
/// (PLAN-main-radio-zone1-4 §4.2 — 4구역 흐름에서 뽑았다). 흐름 컴포넌트가 하나씩 들고 쓴다(서버 전용).
/// 4구역 때와 달라진 것 하나: 이미 다른 계통이 잠근 사람은 잠그지도 풀지도 않는다(<see cref="Lock"/>).
/// </summary>
public sealed class MapSceneAreaIntroServer
{
    private readonly List<ulong> _locked = new List<ulong>();
    private readonly List<ulong> _voters = new List<ulong>();
    private readonly BossIntroSkipVotes _votes = new BossIntroSkipVotes();

    public IReadOnlyList<ulong> Voters => _voters;
    public int LockedCount => _locked.Count;
    public int VoteCount => _votes.Count;

    /// <summary>
    /// 살아 있는 접속자 전원이 <paramref name="area"/> 안. 살아 있는 사람이 0명이면 false(부활을 기다린다 — 전멸은 기존 흐름이 처리).
    /// 영혼·영구 사망자는 위치와 무관(구간 추적기가 영혼을 None 으로 지운다 — 기다리면 영영 시작 안 한다). 관전 화면으로 본다.
    /// </summary>
    public static bool AllAliveIn(MapSceneDirector director, MapSceneArea area, out int inside, out int alive)
    {
        inside = alive = 0;
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || director == null) return false;
        foreach (KeyValuePair<ulong, NetworkClient> pair in nm.ConnectedClients)
        {
            NetworkObject po = pair.Value.PlayerObject;
            if (po == null || !po.IsSpawned) continue;
            PlayerLifeCycleController life = po.GetComponent<PlayerLifeCycleController>();
            if (life != null && life.State != PlayerLifeState.Alive) continue;
            alive++;
            if (director.GetPlayerAreaServer(pair.Key) == area) inside++;
        }
        return alive > 0 && inside == alive;
    }

    // ─── 잠금 ───────────────────────────────────────────────────────────

    /// <summary>연출 잠금(입력·무적·낙사) — 영구 사망 제외 접속자 전원(영혼 포함, 보스 등장 연출과 같은 대상).</summary>
    public void Lock()
    {
        Unlock();
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null) return;
        foreach (KeyValuePair<ulong, NetworkClient> pair in nm.ConnectedClients)
        {
            NetworkObject po = pair.Value.PlayerObject;
            if (po == null || !po.IsSpawned) continue;
            PlayerLifeCycleController life = po.GetComponent<PlayerLifeCycleController>();
            if (life != null && life.State == PlayerLifeState.PermanentDead) continue;
            PlayerEncounterLock encounterLock = po.GetComponent<PlayerEncounterLock>();
            // 이미 다른 계통(보스 연출 등)이 잠근 사람은 장부에 안 넣는다 — 연출이 끝날 때 남의 잠금을 풀지 않게(명세 §7, 로비 LobbyBriefingLockLedger 와 같은 규칙).
            if (encounterLock == null || encounterLock.IsCinematicLocked) continue;
            encounterLock.BeginCinematicServer();
            _locked.Add(pair.Key);
        }
    }

    /// <summary>이 연출이 건 잠금만 푼다(다른 시스템의 잠금은 그대로).</summary>
    public void Unlock()
    {
        NetworkManager nm = NetworkManager.Singleton;
        foreach (ulong id in _locked)
        {
            if (nm == null || !nm.ConnectedClients.TryGetValue(id, out NetworkClient c) || c.PlayerObject == null) continue;
            PlayerEncounterLock encounterLock = c.PlayerObject.GetComponent<PlayerEncounterLock>();
            if (encounterLock != null) encounterLock.EndCinematicServer();
        }
        _locked.Clear();
    }

    // ─── 스킵 투표 ──────────────────────────────────────────────────────

    /// <summary>투표자 = 지금 접속한 전원(관전자 포함 — 경석 10-26).</summary>
    public void RefreshVoters()
    {
        _voters.Clear();
        NetworkManager nm = NetworkManager.Singleton;
        if (nm != null) _voters.AddRange(nm.ConnectedClientsIds);
    }

    public void ClearVotes() => _votes.Clear();

    /// <summary>표를 넣는다(명단을 새로 읽고). 받았으면 true.</summary>
    public bool TryVote(ulong sender)
    {
        RefreshVoters();
        return _votes.TryVote(sender, _voters);
    }

    /// <summary>명단을 새로 읽고 빠진 사람 표를 지운다. 표가 바뀌었으면 true.</summary>
    public bool RefreshAndPrune()
    {
        RefreshVoters();
        return _votes.Prune(_voters);
    }

    public bool IsUnanimous => _votes.IsUnanimous(_voters);
}
