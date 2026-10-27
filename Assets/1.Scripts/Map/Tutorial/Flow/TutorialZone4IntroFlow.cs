using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 튜토리얼 4구역 진입 전 소개 연출 — <b>서버 진행</b>(PLAN-zone4-intro-cinematic). 표시는 모든 피어의 <see cref="TutorialZone4IntroPresentation"/>.
///
/// 흐름:
///  · 튜토리얼 활성 순간 G7(존4 입구)을 보류한다 — 복도3 특성 선택이 끝나도 바로 열리지 않는다(<see cref="TutorialFlowModel.HoldGate"/>).
///  · 열 조건이 찼고(전원 완료·60초 교착 방지) <b>살아 있는</b> 접속자 전원이 복도3 구간 안이면 연출 시작 —
///    열린 특성 창 자동 획득 · 연출 잠금(입력·무적·낙사, 영구 사망 제외) · 회차 +1 · 복귀 시각 = 샷 합.
///    영혼·영구 사망자는 위치와 무관(구간 추적기가 영혼을 None 으로 지운다 — 기다리면 영영 시작 안 한다). 관전 화면으로 본다.
///  · 스킵 = 접속자 전원(관전자 포함) 동의 → 지금 자리에서 복귀(<see cref="TutorialZone4IntroData.SkipReturnSeconds"/>).
///  · 복귀 블렌드가 끝나는 순간 잠금 해제 → G7 보류 해제(기억한 사유로 열림) → <see cref="TutorialZone4Flow"/> 가 타이머·기상·자막.
///  · QA 구간 스킵(Alt+숫자)이 돌면 연출 없이 보류만 푼다. 연출 중 G7 이 다른 길(디버그 강제)로 열리면 연출을 정리하고 끝낸다.
/// 붙는 곳: 서버에서 디렉터가 튜토리얼을 켠 순간 디렉터 GameObject 에 런타임으로 붙는다(<see cref="TutorialZone4Flow"/> 와 같은 방식 — 씬 무수정).
/// </summary>
[DisallowMultipleComponent]
public sealed class TutorialZone4IntroFlow : MonoBehaviour
{
    private TutorialDirector _director;
    private TutorialZone4IntroData _data;
    private bool _subscribed;
    private bool _held;                         // 이 흐름이 G7 을 보류했다(연출 켜짐)
    private int _sequence;
    private readonly List<ulong> _locked = new List<ulong>();
    private readonly List<ulong> _voters = new List<ulong>();
    private readonly BossIntroSkipVotes _votes = new BossIntroSkipVotes();
    private string _lastWaitLog;

    // ─── 부착(서버) ──────────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void InstallBootstrap()
    {
        TutorialDirector.StateChanged -= EnsureOnServer;
        TutorialDirector.StateChanged += EnsureOnServer;
    }

    private static void EnsureOnServer()
    {
        TutorialDirector d = TutorialDirector.Instance;
        if (d == null || !d.IsSpawned || !d.IsServer || !d.IsTutorialActive) return;
        if (d.GetComponent<TutorialZone4IntroFlow>() != null) return;
        d.gameObject.AddComponent<TutorialZone4IntroFlow>();
    }

    // ─── 생명주기 ────────────────────────────────────────────────────────

    private void OnEnable()
    {
        _director = GetComponent<TutorialDirector>();
        NetworkManager nm = NetworkManager.Singleton;
        if (_director == null || nm == null || !nm.IsServer)
        {
            enabled = false;
            return;
        }

        _data = TutorialZone4IntroData.Load();
        _sequence = _director.Zone4Intro.Sequence;
        if (!_subscribed)
        {
            TutorialDirector.Zone4IntroSkipVoted += HandleSkipVote;
            TutorialDirector.GateOpened += HandleGateOpened;
            _subscribed = true;
        }

        if (_data.IntroEnabled && !_director.IsGateOpen(TutorialGate.G7_Zone4Entry) && _director.Zone4Intro.Phase == TutorialDirector.Zone4IntroPhase.None)
            _held = _director.HoldGateServer(TutorialGate.G7_Zone4Entry);
    }

    private void OnDisable()
    {
        if (_subscribed)
        {
            TutorialDirector.Zone4IntroSkipVoted -= HandleSkipVote;
            TutorialDirector.GateOpened -= HandleGateOpened;
            _subscribed = false;
        }
        Unlock();
    }

    private void Update()
    {
        if (_director == null || !_director.IsSpawned || !_director.IsTutorialActive) return;

        TutorialDirector.Zone4IntroState state = _director.Zone4Intro;
        switch (state.Phase)
        {
            case TutorialDirector.Zone4IntroPhase.None:
                TickWaiting();
                break;
            case TutorialDirector.Zone4IntroPhase.Running:
                TickRunning(state);
                break;
        }
    }

    // ─── 대기 → 시작 ─────────────────────────────────────────────────────

    private void TickWaiting()
    {
        if (!_held || !_director.IsGateHeldServer(TutorialGate.G7_Zone4Entry)) return;

        if (IsQaSkipRunning())
        {
            _held = false;
            bool opened = _director.ReleaseHeldGateServer(TutorialGate.G7_Zone4Entry);
            Edit.Log($"[Zone4Intro] 구간 스킵이 G7 을 지난다(Alt+6 이상) — 연출 없이 G7 보류 해제({(opened ? "열림" : "조건 대기")}).", this);
            return;
        }

        if (!_director.IsGateReadyHeldServer(TutorialGate.G7_Zone4Entry)) return;

        if (!AllAliveInCorridor3(out int inside, out int alive))
        {
            string wait = $"{inside}/{alive}";
            if (wait != _lastWaitLog)
            {
                _lastWaitLog = wait;
                Edit.Log($"[Zone4Intro] 특성 선택 끝 — 살아 있는 전원이 복도3 에 모이길 기다린다({wait}).", this);
            }
            return;
        }

        StartIntro();
    }

    // 살아 있는 접속자 전원이 복도3 안. 살아 있는 사람이 0명이면 false(부활을 기다린다 — 전멸은 기존 흐름이 처리).
    private bool AllAliveInCorridor3(out int inside, out int alive)
    {
        inside = alive = 0;
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null) return false;
        foreach (KeyValuePair<ulong, NetworkClient> pair in nm.ConnectedClients)
        {
            NetworkObject po = pair.Value.PlayerObject;
            if (po == null || !po.IsSpawned) continue;
            PlayerLifeCycleController life = po.GetComponent<PlayerLifeCycleController>();
            if (life != null && life.State != PlayerLifeState.Alive) continue;
            alive++;
            if (_director.GetPlayerAreaServer(pair.Key) == TutorialArea.Corridor3) inside++;
        }
        return alive > 0 && inside == alive;
    }

    private void StartIntro()
    {
        // 60초 교착 방지로 준비된 경우 아직 특성 창이 열린 사람이 있다 — 연출 위에 창이 남지 않게 먼저 닫는다(자동 획득).
        TutorialTraitDirector traits = TutorialTraitDirector.Instance;
        int auto = traits != null ? traits.AutoChooseAllPendingServer() : 0;

        Lock();
        _votes.Clear();
        RefreshVoters();
        _sequence++;
        double now = NetworkManager.Singleton.ServerTime.Time;
        _director.SetZone4IntroServer(new TutorialDirector.Zone4IntroState
        {
            Phase = TutorialDirector.Zone4IntroPhase.Running,
            Sequence = _sequence,
            StartServerTime = now,
            ReturnAt = _data.IntroSeconds,
            ReturnSeconds = _data.ReturnBlendSeconds,
            SkipAt = -1f,
            Votes = 0,
            Voters = (byte)Mathf.Min(255, _voters.Count),
        });
        _lastWaitLog = null;
        Edit.Log($"[Zone4Intro] 소개 연출 시작(회차 {_sequence}) — 샷 {_data.IntroSeconds:0.##}초 + 복귀 {_data.ReturnBlendSeconds:0.##}초 · " +
                 $"잠금 {_locked.Count}명 · 투표자 {_voters.Count}명{(auto > 0 ? $" · 특성 자동 획득 {auto}명" : "")}.", this);
    }

    // ─── 진행 ───────────────────────────────────────────────────────────

    private void TickRunning(TutorialDirector.Zone4IntroState state)
    {
        double now = NetworkManager.Singleton.ServerTime.Time;
        float elapsed = state.Elapsed(now);

        if (!state.IsSkipClosed(now))
        {
            RefreshVoters();
            bool pruned = _votes.Prune(_voters);
            if (_votes.IsUnanimous(_voters))
            {
                Skip(state, elapsed);
                return;
            }
            if (pruned || state.Voters != _voters.Count)
            {
                state.Votes = (byte)_votes.Count;
                state.Voters = (byte)Mathf.Min(255, _voters.Count);
                _director.SetZone4IntroServer(state);
            }
        }

        if (state.ReturnAt >= 0f && elapsed >= state.ReturnAt + state.ReturnSeconds)
            Finish(state, "복귀 완료");
    }

    private void HandleSkipVote(ulong sender, int sequence)
    {
        if (_director == null) return;
        TutorialDirector.Zone4IntroState state = _director.Zone4Intro;
        if (state.Phase != TutorialDirector.Zone4IntroPhase.Running || sequence != state.Sequence) return;
        double now = NetworkManager.Singleton.ServerTime.Time;
        if (state.IsSkipClosed(now)) return;   // 이미 스킵했거나 내려가는 중 — 더 받을 표가 없다

        RefreshVoters();
        if (!_votes.TryVote(sender, _voters)) return;
        state.Votes = (byte)_votes.Count;
        state.Voters = (byte)Mathf.Min(255, _voters.Count);
        _director.SetZone4IntroServer(state);
        Edit.Log($"[Zone4Intro] 스킵 동의 — client {sender} ({_votes.Count}/{_voters.Count}).", this);
    }

    // 전원 동의 — 남은 샷·대사는 끊는다. 다리·중앙 섬 샷(중간보스) 전이면 그 샷으로 옮겨 가 잠깐 머문 뒤 복귀하고(경석 10-27 —
    // 바로 플레이어로 가지 말고 중간보스를 한 번 보여 준다), 이미 그 샷이면 지금 자리에서 바로 복귀한다.
    private void Skip(TutorialDirector.Zone4IntroState state, float elapsed)
    {
        _votes.Clear();
        elapsed = Mathf.Max(0f, elapsed);
        bool showBoss = elapsed < _data.BossShotArrival;
        state.SkipAt = showBoss ? elapsed : -1f;
        state.ReturnAt = showBoss ? elapsed + _data.SkipToBossSeconds + _data.SkipBossHoldSeconds : elapsed;
        state.ReturnSeconds = _data.SkipReturnSeconds;
        state.Votes = 0;
        _director.SetZone4IntroServer(state);
        Edit.Log(showBoss
            ? $"[Zone4Intro] 소개 연출 스킵 — 전원 동의, {elapsed:0.##}초 지점 → 중간보스 샷 {_data.SkipToBossSeconds:0.##}+{_data.SkipBossHoldSeconds:0.##}초 → 복귀 {_data.SkipReturnSeconds:0.##}초."
            : $"[Zone4Intro] 소개 연출 스킵 — 전원 동의, 이미 중간보스 샷({elapsed:0.##}초) → 바로 복귀 {_data.SkipReturnSeconds:0.##}초.", this);
    }

    private void Finish(TutorialDirector.Zone4IntroState state, string why)
    {
        Unlock();
        state.Phase = TutorialDirector.Zone4IntroPhase.Done;
        state.Votes = 0;
        _director.SetZone4IntroServer(state);
        _held = false;

        bool opened = _director.IsGateOpen(TutorialGate.G7_Zone4Entry) || _director.ReleaseHeldGateServer(TutorialGate.G7_Zone4Entry);
        if (opened) Edit.Log($"[Zone4Intro] 소개 연출 끝({why}) — G7 열림.", this);
        else Edit.LogWarning($"[Zone4Intro] 소개 연출 끝({why})인데 G7 이 안 열렸다 — 게이트 순서·보류 상태 확인.", this);
    }

    // 연출 중 G7 이 다른 길(디버그 강제 개방 등)로 열렸다 — 연출을 정리한다(잠금 해제·화면 복귀). 정상 종료 때는 이미 Done 이다.
    private void HandleGateOpened(TutorialGate gate, TutorialGateOpenReason reason)
    {
        if (gate != TutorialGate.G7_Zone4Entry || _director == null) return;
        _held = false;
        TutorialDirector.Zone4IntroState state = _director.Zone4Intro;
        if (state.Phase != TutorialDirector.Zone4IntroPhase.Running) return;

        float elapsed = state.Elapsed(NetworkManager.Singleton.ServerTime.Time);
        state.ReturnAt = state.ReturnAt >= 0f ? Mathf.Min(state.ReturnAt, elapsed) : elapsed;
        Edit.LogWarning($"[Zone4Intro] 연출 중 G7 이 다른 길로 열렸다({reason}) — 연출을 정리한다.", this);
        Finish(state, "G7 외부 개방");
    }

    // ─── 잠금·투표자 ─────────────────────────────────────────────────────

    // 연출 잠금(입력·무적·낙사) — 영구 사망 제외 접속자 전원(영혼 포함, 보스 등장 연출과 같은 대상).
    private void Lock()
    {
        Unlock();
        NetworkManager nm = NetworkManager.Singleton;
        foreach (KeyValuePair<ulong, NetworkClient> pair in nm.ConnectedClients)
        {
            NetworkObject po = pair.Value.PlayerObject;
            if (po == null || !po.IsSpawned) continue;
            PlayerLifeCycleController life = po.GetComponent<PlayerLifeCycleController>();
            if (life != null && life.State == PlayerLifeState.PermanentDead) continue;
            PlayerEncounterLock encounterLock = po.GetComponent<PlayerEncounterLock>();
            if (encounterLock == null) continue;
            encounterLock.BeginCinematicServer();
            _locked.Add(pair.Key);
        }
    }

    private void Unlock()
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

    // 투표자 = 지금 접속한 전원(관전자 포함 — 경석 10-26).
    private void RefreshVoters()
    {
        _voters.Clear();
        NetworkManager nm = NetworkManager.Singleton;
        if (nm != null) _voters.AddRange(nm.ConnectedClientsIds);
    }

#if UNITY_EDITOR
    /// <summary>[에디터 디버그] 접속자 전원 스킵 표 — Space 홀드 대신 서버 표 경로를 그대로 탄다(MCP 로는 Game 뷰 포커스가 없어 홀드가 풀린다).</summary>
    public void DebugVoteAllServer()
    {
        if (_director == null) return;
        int sequence = _director.Zone4Intro.Sequence;
        RefreshVoters();
        foreach (ulong id in _voters.ToArray()) HandleSkipVote(id, sequence);
    }
#endif

    private bool IsQaSkipRunning()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
        TutorialSkipController skip = GetComponent<TutorialSkipController>();
        return skip != null && skip.IsSkippingThrough(TutorialGate.G7_Zone4Entry);   // Alt+6 이상만 — Alt+5(복도3)는 G7 을 안 지난다
#else
        return false;
#endif
    }
}
