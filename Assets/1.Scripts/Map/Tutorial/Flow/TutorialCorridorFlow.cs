using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 튜토리얼 특성 복도 1~4 흐름(S8) — 복도 중간 지점 → 특성 라운드(복도당 1번) → 개인 체크 → 출구 게이트 G2/G4/G7/G10.
/// 원본: Docs/design/tutorial-scenario.md §3.2·§3.4·§3.6·§3.8·§4 · Docs/tech/tutorial-corridors-zone2.md.
/// 규칙은 순수 <see cref="TutorialCorridorRules"/>·<see cref="TutorialCorridorRounds"/>·<see cref="TutorialCorridorProgress"/>(TutorialCorridorModel.cs).
///
/// 붙는 곳: <see cref="TutorialZone1Flow"/> 와 같다 — 디렉터가 튜토리얼을 켠 것이 보이는 순간 디렉터 GameObject 에 런타임 부착(모든 피어).
///
/// 서버:
///  · <see cref="TutorialDirector.CorridorMidReached"/>(개인 첫 도착) → <b>그 사람에게만</b> 제시(S12 판단 1 — §3.2 "그 플레이어가 중간 지점에 왔을 때").
///    복도 첫 도착이면 <see cref="TutorialTraitDirector.StartOfferRoundServer(IEnumerable{ulong}, TutorialTraitRoundDisplay)"/> 로 라운드를 열고,
///    이후 도착자는 <see cref="TutorialTraitDirector.AddParticipantServer"/>(같은 라운드 — 기존 제시·선택 보존). 앞 존에 있는 사람에게는 창을 띄우지 않는다.
///    출구가 이미 열렸거나 타이머 만료 뒤 도착이면 창을 띄우지 않는다.
///  · 제시를 못 받은 사람(0장·디렉터 없음)은 도착 때 바로 체크 → 게이트가 그 사람을 기다리지 않는다.
///  · <see cref="TutorialTraitDirector.ServerTraitChosen"/>(수동·자동) → <see cref="TutorialDirector.ReportCheckServer"/>(TraitCorridorN) → 디렉터 모델이 전원/60초로 출구를 연다.
///  · 출구가 열리면(어떤 이유든) <b>미도착자도 참가자로 넣고</b> 미선택 전원 <see cref="TutorialTraitDirector.AutoChooseAllPendingServer"/> — 창 닫힘·토스트·무적 해제(§3.2 D14 · §3.8).
///  · 보스 타이머 만료(Expired)·보스방 도착 → 진행 중 복도 라운드를 같은 방식으로 즉시 자동 획득 — 강제 이동(3초 경고 뒤)·연출 잠금보다 먼저(tutorial-traits.md §3-5).
///  · G10 → <see cref="TutorialGuideIds.BPadWarn"/> 전원(D17). 단계: 입구 G3/G6/G9 → Corridor2~4, G10 → BossEnter(Corridor1 은 존1 흐름).
///  디렉터 Commit 안(게이트 이벤트)에서 다시 Commit 하지 않게 게이트 후속 처리는 다음 Update 로 미룬다.
/// 모든 피어(로컬 플레이어):
///  · 목표 = 창이 떠 있으면 "특성 고르기", 골랐으면 "동료 대기" + 대기 줄(TUT_WAIT_PARTY). 중간 지점 전에는 앞 존 흐름의 "복도로" 를 건드리지 않는다.
///  · 출구가 열리면 이 흐름이 쓰던 목표를 "다음 구역으로"(복도4 = 엘리베이터 발판으로) 바꾸고 손을 뗀다. 존 첫 진입 안내는 각 존 흐름 몫 —
///    다음 존 단계가 이미 보이면(같은 틱 반영) 그 존 목표를 덮지 않는다(<see cref="TutorialCorridorRules.ShouldShowExitObjective"/>). 단계 Finished 뒤엔 그리지 않는다.
///  · 처음 그릴 때 안내 UI 가 비어 있으면(늦은 클라) <see cref="TutorialGuideUI.RebuildFromState"/>.
///  · 특성 창 교착 방지 게이지 — 복제 대기 상태(<see cref="TutorialDirector.TryGetWait"/>)를 <see cref="TutorialTraitSelectionView.SetDeadline"/> 으로 넣는다
///    (표시 = <see cref="TutorialCorridorRules.ShouldShowTraitDeadline"/>). 특성 모듈은 디렉터를 모른다 — 의존은 이 흐름 → 특성 뷰 한 방향.
/// </summary>
[DisallowMultipleComponent]
public sealed class TutorialCorridorFlow : MonoBehaviour
{
    // 시나리오 §5 에 없는 목표 줄(복도 안내). TutorialGuideIds 는 다른 레인 소유라 여기 둔다.
    public const string PickId = "TUT_C_PICK";
    public const string WaitId = "TUT_C_WAIT";
    public const string NextId = "TUT_C_NEXT";
    public const string GoPadId = "TUT_C4_GO_PAD";

    private TutorialDirector _director;
    private TutorialTraitDirector _traits;
    private NetworkManager _nm;
    private bool _isServer;

    // 서버
    private readonly TutorialCorridorRounds _rounds = new TutorialCorridorRounds();
    private readonly List<TutorialGate> _openedGateQueue = new List<TutorialGate>();
    private readonly List<ulong> _clientScratch = new List<ulong>();
    private BossTimerManager.TimerState _lastTimerState = BossTimerManager.TimerState.Idle;

    // 로컬
    private TutorialGuideUI _ui;
    private int _ownedCorridor;
    private TutorialTraitSelectionView _traitView;

    // ─── 부착(모든 피어) ───────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void InstallBootstrap()
    {
        TutorialDirector.StateChanged -= Ensure;
        TutorialDirector.StateChanged += Ensure;
    }

    private static void Ensure()
    {
        TutorialDirector d = TutorialDirector.Instance;
        if (d == null || !d.IsSpawned || !d.IsTutorialActive) return;
        if (d.GetComponent<TutorialCorridorFlow>() != null) return;
        d.gameObject.AddComponent<TutorialCorridorFlow>();
    }

    // ─── 생명주기 ────────────────────────────────────────────────────────

    private void OnEnable()
    {
        _director = GetComponent<TutorialDirector>();
        _nm = NetworkManager.Singleton;
        if (_director == null || _nm == null || !_nm.IsListening)
        {
            enabled = false;
            return;
        }

        _isServer = _nm.IsServer;
        if (_isServer)
        {
            TutorialDirector.CorridorMidReached += HandleCorridorMidReachedServer;
            TutorialDirector.BossRoomArrived += HandleBossRoomArrivedServer;
        }

        TutorialDirector.StateChanged += Render;
        TutorialDirector.GateOpened += HandleGateOpened;
        BindTraits(TutorialTraitDirector.Instance);
        _ui = TutorialGuideUI.Instance;

        if (_traits == null)
            Edit.LogWarning("[Tutorial] 복도 흐름: 씬에 TutorialTraitDirector 가 없다 — 복도 특성은 건너뛰고 중간 지점 도착만으로 체크한다.", this);
        Edit.Log($"[Tutorial] 복도 흐름 켜짐 (서버:{_isServer}) — 특성 디렉터 {(_traits != null ? "있음" : "없음")}.", this);
        Render();
    }

    private void OnDisable()
    {
        TutorialDirector.CorridorMidReached -= HandleCorridorMidReachedServer;
        TutorialDirector.BossRoomArrived -= HandleBossRoomArrivedServer;
        TutorialDirector.StateChanged -= Render;
        TutorialDirector.GateOpened -= HandleGateOpened;
        BindTraits(null);
        if (_traitView != null) _traitView.SetDeadline(false, 0f, 0f, 0, 0);
        _traitView = null;
    }

    private void BindTraits(TutorialTraitDirector traits)
    {
        if (_traits != null)
        {
            _traits.ServerTraitChosen -= HandleTraitChosenServer;
            _traits.ClientStateChanged -= Render;
        }

        _traits = traits;
        if (_traits == null || !isActiveAndEnabled) return;
        if (_isServer) _traits.ServerTraitChosen += HandleTraitChosenServer;
        _traits.ClientStateChanged += Render;
    }

    private void Update()
    {
        if (_director == null) return;

        if (TutorialTraitDirector.Instance != _traits)
        {
            BindTraits(TutorialTraitDirector.Instance);
            Render();
        }

        if (_isServer)
        {
            FlushOpenedGatesServer();
            WatchTimerExpiryServer();
        }

        // 안내 UI 가 늦게 깨어나거나 바뀌면 다시 그린다.
        if (TutorialGuideUI.Instance != _ui)
        {
            _ui = TutorialGuideUI.Instance;
            Render();
        }

        // 특성 창이 늦게 깨어나면 게이지 값을 다시 넣는다.
        if (TutorialTraitSelectionView.Instance != _traitView) PushTraitDeadline();
    }

    // ─── 서버: 라운드 ──────────────────────────────────────────────────────

    private bool IsTimerExpired => BossTimerManager.Instance != null && BossTimerManager.Instance.State == BossTimerManager.TimerState.Expired;

    private void HandleCorridorMidReachedServer(ulong clientId, int corridor)
    {
        if (!_director.IsTutorialActive || _director.IsFinished) return;

        TutorialCorridorRoundDecision decision = _rounds.Decide(corridor,
            _director.IsGateOpen(TutorialCorridorRules.ExitGate(corridor)), IsTimerExpired);
        switch (decision)
        {
            case TutorialCorridorRoundDecision.Start:
                StartRoundServer(corridor, clientId);
                break;
            case TutorialCorridorRoundDecision.Join:
                JoinRoundServer(corridor, clientId);
                break;
            case TutorialCorridorRoundDecision.GateAlreadyOpen:
            case TutorialCorridorRoundDecision.TimerExpired:
                _rounds.MarkHandled(corridor, 0);
                Edit.Log($"[Tutorial] 복도{corridor} 특성 라운드 건너뜀 — {decision}.", this);
                break;
        }
    }

    // 첫 도착자에게만 제시하며 라운드를 연다(§3.2 판단 1) — 아직 앞 존에 있는 사람에게는 창을 띄우지 않는다.
    private void StartRoundServer(int corridor, ulong firstClientId)
    {
        TutorialCheck check = TutorialCorridorRules.CheckOf(corridor);
        if (_traits == null || !_traits.IsSpawned)
        {
            _rounds.MarkStarted(corridor, 0);
            _director.ReportCheckServer(firstClientId, check);
            Edit.LogWarning($"[Tutorial] 복도{corridor}: 특성 디렉터가 없다 — 선택 없이 도착자마다 체크(게이트 {TutorialCorridorRules.ExitGate(corridor)}).", this);
            return;
        }

        int before = _traits.Round;
        _clientScratch.Clear();
        _clientScratch.Add(firstClientId);
        bool offered = _traits.StartOfferRoundServer(_clientScratch, TutorialCorridorRules.DisplayOf(corridor));
        int round = _traits.Round != before ? _traits.Round : 0;   // 서버 준비 전이면 라운드가 안 바뀐다
        _rounds.MarkStarted(corridor, round);
        // 0장(풀 소진)·제시 실패 = 고를 게 없다 → 바로 체크.
        if (!offered) _director.ReportCheckServer(firstClientId, check);
        Edit.Log($"[Tutorial] 복도{corridor} 특성 라운드 {(round > 0 ? $"{round} 시작" : "없음")} — 첫 도착 client {firstClientId} " +
                 $"{(offered ? "제시" : "제시 없음(바로 체크)")}.", this);
    }

    // 같은 라운드에 늦게 도착한 사람 — 그 사람에게만 제시한다. 이미 고른 사람·기존 제시는 그대로.
    private void JoinRoundServer(int corridor, ulong clientId)
    {
        TutorialCheck check = TutorialCorridorRules.CheckOf(corridor);
        int round = _rounds.RoundOf(corridor);
        if (_traits == null || !_traits.IsSpawned || round == 0 || _traits.Round != round)
        {
            // 고를 창이 없는 복도(특성 디렉터 없음·라운드 못 엶) — 도착만으로 체크.
            _director.ReportCheckServer(clientId, check);
            return;
        }

        TutorialTraitJoinResult result = _traits.AddParticipantServer(clientId);
        if (result == TutorialTraitJoinResult.Empty || result == TutorialTraitJoinResult.NoRound)
            _director.ReportCheckServer(clientId, check);
        Edit.Log($"[Tutorial] 복도{corridor} 늦은 도착 client {clientId} — 라운드 {round} 참가 {result}.", this);
    }

    // 아직 라운드에 없는(중간 지점 미도착) 접속자를 참가자로 넣는다 — 바로 뒤의 자동 획득 대상이 되게(§3.8 "존4 에 남아 있던 플레이어").
    // 0장이면 고를 게 없으니 바로 체크한다. 넣은 인원 수.
    private int AddAbsentParticipantsServer(int corridor)
    {
        _clientScratch.Clear();
        foreach (ulong id in _nm.ConnectedClientsIds) _clientScratch.Add(id);

        int added = 0;
        foreach (ulong id in _clientScratch)
        {
            if (_traits.IsInRoundServer(id)) continue;
            TutorialTraitJoinResult result = _traits.AddParticipantServer(id);
            if (result == TutorialTraitJoinResult.Empty)
                _director.ReportCheckServer(id, TutorialCorridorRules.CheckOf(corridor));
            if (result == TutorialTraitJoinResult.Offered || result == TutorialTraitJoinResult.Empty)
                added++;
        }
        return added;
    }

    private void HandleTraitChosenServer(TutorialTraitChosen chosen)
    {
        int corridor = _rounds.CorridorOfRound(chosen.Round);
        if (corridor == 0 || !_director.IsTutorialActive) return;
        _director.ReportCheckServer(chosen.ClientId, TutorialCorridorRules.CheckOf(corridor));
    }

    /// <summary>그 복도 라운드가 특성 디렉터의 지금 라운드인가.</summary>
    private bool IsCurrentRound(int corridor)
    {
        int round = _rounds.RoundOf(corridor);
        return _traits != null && _traits.IsSpawned && round > 0 && _traits.Round == round;
    }

    // 미도착자까지 참가자로 넣고 미선택 전원 자동 획득(창 닫힘·토스트·무적 해제). 그 복도 라운드가 지금 라운드일 때만.
    private void AutoChooseRoundServer(int corridor, string reason)
    {
        if (!IsCurrentRound(corridor)) return;
        int absent = AddAbsentParticipantsServer(corridor);
        int n = _traits.AutoChooseAllPendingServer();
        if (n > 0 || absent > 0) Edit.Log($"[Tutorial] 복도{corridor} 자동 획득 {n}명(미도착 {absent}명 포함) — {reason}.", this);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>
    /// [서버, 디버그 구간 스킵 — <see cref="TutorialSkipController"/>] 건너뛰는 복도의 특성 라운드를 <b>창 없이</b> 끝낸다:
    /// 라운드를 아직 안 열었으면 제시 없이 열고(<see cref="TutorialTraitDirector.StartRoundWithoutOfferServer"/>), 아직 참가하지 않은 접속자는
    /// 제시 없이 바로 무작위 1장 획득(<see cref="TutorialTraitDirector.GrantParticipantServer"/>) — 제시 번호가 안 올라 클라 창이 열렸다 닫히며 깜빡이지 않는다.
    /// 이미 중간 지점에 도착해 창을 보고 있던 사람만 정식 자동 획득(<see cref="TutorialTraitDirector.AutoChooseAllPendingServer"/>)으로 창이 닫힌다.
    /// 어느 쪽이든 <see cref="TutorialTraitDirector.ServerTraitChosen"/> → <see cref="HandleTraitChosenServer"/> 가 특성 체크를 보고한다.
    /// 고를 게 없던 사람(0장·특성 디렉터 없음)은 정식 흐름처럼 바로 체크한다.
    /// 출구 게이트는 디렉터 모델이 전원 완료로 연다. 이 복도 체크가 켜진 인원 수가 아니라 획득 수를 돌려준다.
    /// </summary>
    public int SkipRoundServer(int corridor)
    {
        if (!_isServer || _director == null || !TutorialCorridorRules.IsValid(corridor)) return 0;
        TutorialCheck check = TutorialCorridorRules.CheckOf(corridor);

        int auto = 0;
        if (_traits != null && _traits.IsSpawned)
        {
            if (!_rounds.IsHandled(corridor))
            {
                // 라운드 번호를 복도에 먼저 묶는다 — 바로 뒤 획득 통지(HandleTraitChosenServer)가 이 복도로 체크를 보고하게.
                int before = _traits.Round;
                _traits.StartRoundWithoutOfferServer(TutorialCorridorRules.DisplayOf(corridor));
                _rounds.MarkStarted(corridor, _traits.Round != before ? _traits.Round : 0);
            }
            if (IsCurrentRound(corridor))
            {
                _clientScratch.Clear();
                foreach (ulong id in _nm.ConnectedClientsIds) _clientScratch.Add(id);
                foreach (ulong id in _clientScratch)
                    if (!_traits.IsInRoundServer(id) && _traits.GrantParticipantServer(id) == TutorialTraitJoinResult.Granted)
                        auto++;
                auto += _traits.AutoChooseAllPendingServer();
            }
        }
        else
        {
            _rounds.MarkHandled(corridor, 0);
        }

        // 제시를 못 받은 사람(0장·라운드 없음)은 정식 흐름에서도 도착만으로 체크된다 — 이미 켜진 체크는 디렉터가 무시한다.
        foreach (ulong id in _nm.ConnectedClientsIds)
            if (_traits == null || !_traits.HasPendingChoiceServer(id)) _director.ReportCheckServer(id, check);

        Edit.Log($"[Tutorial] 복도{corridor} 특성 라운드 스킵 — 자동 획득 {auto}명.", this);
        return auto;
    }
#endif

    // 지금 진행 중(출구 닫힘)인 복도 라운드 — 없으면 0.
    private int OpenRoundCorridor()
    {
        if (_traits == null) return 0;
        int corridor = _rounds.CorridorOfRound(_traits.Round);
        return corridor > 0 && !_director.IsGateOpen(TutorialCorridorRules.ExitGate(corridor)) ? corridor : 0;
    }

    private void FlushOpenedGatesServer()
    {
        if (_openedGateQueue.Count == 0) return;
        TutorialGate[] gates = _openedGateQueue.ToArray();
        _openedGateQueue.Clear();

        foreach (TutorialGate gate in gates)
        {
            int exit = TutorialCorridorRules.CorridorOfExitGate(gate);
            if (exit > 0)
            {
                AutoChooseRoundServer(exit, $"{gate} 열림");
                if (exit == 4 && !_director.IsFinished)
                {
                    _director.ShowSubtitleServer(TutorialGuideIds.BPadWarn);
                    _director.SetStepServer(TutorialStep.BossEnter);
                }
            }

            int entry = TutorialCorridorRules.CorridorOfEntryGate(gate);
            if (entry >= 2) _director.SetStepServer(TutorialCorridorRules.StepOf(entry));   // Corridor1 은 존1 흐름이 한다
        }
    }

    // 만료 경고(3초) 뒤 강제 이동·연출 잠금이 온다 — 잠금 중엔 배율 특성이 거부되므로 만료를 본 즉시 자동 획득.
    private void WatchTimerExpiryServer()
    {
        BossTimerManager timer = BossTimerManager.Instance;
        BossTimerManager.TimerState state = timer != null ? timer.State : BossTimerManager.TimerState.Idle;
        if (state == _lastTimerState) return;
        _lastTimerState = state;
        if (state != BossTimerManager.TimerState.Expired || _traits == null) return;

        // 진행 중인 복도 라운드면 미도착자까지 넣어 전원 자동 획득. 아니면 남은 미선택자만.
        int corridor = OpenRoundCorridor();
        if (corridor > 0) AutoChooseRoundServer(corridor, "보스 타이머 만료(강제 이동 전)");
        int n = _traits.AutoChooseAllPendingServer();
        Edit.Log($"[Tutorial] 보스 타이머 만료 — 열린 특성 창 정리(복도{corridor} · 추가 자동 획득 {n}명).", this);
    }

    // 보스방 도착(패드·만료 강제 이동) = 튜토리얼 안내 끝(§3.9) — 열린 특성 창을 남기지 않는다(자동 획득 → 창 닫힘·무적 해제).
    private void HandleBossRoomArrivedServer(IReadOnlyList<ulong> arrived)
    {
        if (_traits == null) return;
        int corridor = OpenRoundCorridor();
        if (corridor > 0) AutoChooseRoundServer(corridor, "보스방 도착");
        int n = _traits.AutoChooseAllPendingServer();
        if (corridor > 0 || n > 0) Edit.Log($"[Tutorial] 보스방 도착 — 특성 창 정리(복도{corridor} · 추가 자동 획득 {n}명).", this);
    }

    // ─── 게이트(모든 피어) ─────────────────────────────────────────────────

    private void HandleGateOpened(TutorialGate gate, TutorialGateOpenReason reason)
    {
        if (_isServer) _openedGateQueue.Add(gate);

        int exit = TutorialCorridorRules.CorridorOfExitGate(gate);
        if (exit > 0 && exit == _ownedCorridor)
        {
            // 이 흐름이 목표를 쓰던 복도만 — 다음 존 흐름이 첫 진입 때 덮어쓴다.
            // 같은 틱에 다음 존 단계가 먼저 반영됐으면(존 흐름이 이미 첫 진입 목표를 썼으면) 덮지 않는다.
            _ownedCorridor = 0;
            TutorialGuideUI ui = TutorialGuideUI.Instance;
            if (ui != null && !_director.IsFinished)
            {
                ui.ClearWait();
                if (TutorialCorridorRules.ShouldShowExitObjective(exit, _director.Step))
                    ui.SetObjective(exit == 4 ? GoPadId : NextId);
            }
        }
        Render();
    }

    // ─── 목표 패널(모든 피어 — 로컬 플레이어 기준) ──────────────────────────────

    private void Render()
    {
        PushTraitDeadline();

        // 보스방 도착(만료 강제 이동이면 복도4 출구가 닫힌 채일 수 있다) 뒤에는 안내를 다시 그리지 않는다 — 정리는 존4 안내 몫.
        if (_director == null || _nm == null || !_director.IsTutorialActive || _director.IsFinished) return;
        TutorialGuideUI ui = TutorialGuideUI.Instance;
        if (ui == null) return;

        int corridor = TutorialCorridorRules.ActiveCorridor(_director.IsGateOpen);
        if (corridor == 0) return;

        ulong local = _nm.LocalClientId;
        _director.TryGetPlayerProgress(local, out TutorialPlayerProgress p);
        bool pending = _traits != null && _traits.HasPendingChoice(local);
        bool hasWait = _director.TryGetWait(out TutorialGate waitGate, out int done, out int total, out float remaining);

        TutorialCorridorView view = TutorialCorridorProgress.Resolve(corridor, pending, p, hasWait, waitGate);
        if (!view.OwnsObjective) return;

        string objectiveId = view.Task == TutorialCorridorTask.Pick ? PickId : WaitId;
        float seconds = Mathf.Max(0f, remaining);

        if (_ownedCorridor != corridor && TutorialGuideFlowUtil.IsGuideEmpty(ui))
        {
            // 늦은 클라 — 지나간 자막 없이 지금 상태만.
            ui.RebuildFromState(new TutorialGuideState
            {
                ObjectiveId = objectiveId,
                Wait = view.ShowWait ? new TutorialWaitState(done, total, seconds) : default,
            });
        }
        else
        {
            ui.SetObjective(objectiveId);
            if (view.ShowWait) ui.SetWaitParty(done, total, seconds);
            else ui.ClearWait();
        }
        _ownedCorridor = corridor;
    }

    // ─── 특성 창 교착 방지 게이지(모든 피어) ────────────────────────────────

    // 복제 대기 상태가 바뀔 때마다(StateChanged·특성 복제 변경 → Render) 서버 시각 기준 남은 초로 다시 맞춘다. 사이 감소는 창이 한다.
    private void PushTraitDeadline()
    {
        _traitView = TutorialTraitSelectionView.Instance;
        if (_traitView == null || _director == null) return;

        bool show = false;
        int done = 0, total = 0;
        float remaining = -1f;
        if (_director.IsTutorialActive && !_director.IsFinished)
        {
            bool hasWait = _director.TryGetWait(out TutorialGate waitGate, out done, out total, out remaining);
            int corridor = TutorialCorridorRules.ActiveCorridor(_director.IsGateOpen);
            show = TutorialCorridorRules.ShouldShowTraitDeadline(corridor, hasWait, waitGate, remaining);
        }
        _traitView.SetDeadline(show, remaining, _director.DeadlockSeconds, done, total);
    }
}

/// <summary>존·복도 흐름 공용 — 안내 UI 상태 조회.</summary>
public static class TutorialGuideFlowUtil
{
    /// <summary>
    /// 안내 UI 가 아직 아무 목표도 안 그렸다 = 이 클라는 앞 단계를 못 봤다(늦은 클라) → 첫 그림은 RebuildFromState.
    /// 앞 단계를 본 클라에서 Rebuild 를 부르면 대기 중인 자막이 지워지므로 그때는 SetObjective 를 쓴다.
    /// </summary>
    public static bool IsGuideEmpty(TutorialGuideUI ui) => ui != null && string.IsNullOrEmpty(ui.CaptureState().ObjectiveId);
}
