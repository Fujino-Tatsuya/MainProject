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
///  · <see cref="TutorialDirector.CorridorMidReached"/>(개인 첫 도착) → 그 복도 라운드가 아직이면 <see cref="TutorialTraitDirector.StartOfferRoundServer(TutorialTraitRoundDisplay)"/>
///    — <b>접속 중인 전원에게 한 번에</b> 제시(특성 디렉터 라운드 규칙: 새 라운드는 이전 제시를 지운다). 출구가 이미 열렸거나 타이머 만료면 열지 않는다.
///  · 제시를 못 받은 사람(0장·디렉터 없음)은 바로 체크 → 게이트가 그 사람을 기다리지 않는다.
///  · <see cref="TutorialTraitDirector.ServerTraitChosen"/>(수동·자동) → <see cref="TutorialDirector.ReportCheckServer"/>(TraitCorridorN) → 디렉터 모델이 전원/60초로 출구를 연다.
///  · 출구가 열리면(어떤 이유든) 미선택자 <see cref="TutorialTraitDirector.AutoChooseAllPendingServer"/> — 창 닫힘·토스트·무적 해제(§3.2 D14).
///  · 보스 타이머 만료(Expired) → 즉시 자동 획득 — 강제 이동(3초 경고 뒤)·연출 잠금보다 먼저(tutorial-traits.md §3-5).
///  · G10 → <see cref="TutorialGuideIds.BPadWarn"/> 전원(D17). 단계: 입구 G3/G6/G9 → Corridor2~4, G10 → BossEnter(Corridor1 은 존1 흐름).
///  디렉터 Commit 안(게이트 이벤트)에서 다시 Commit 하지 않게 게이트 후속 처리는 다음 Update 로 미룬다.
/// 모든 피어(로컬 플레이어):
///  · 목표 = 창이 떠 있으면 "특성 고르기", 골랐으면 "동료 대기" + 대기 줄(TUT_WAIT_PARTY). 중간 지점 전에는 앞 존 흐름의 "복도로" 를 건드리지 않는다.
///  · 출구가 열리면 이 흐름이 쓰던 목표를 "다음 구역으로"(복도4 = 엘리베이터 발판으로) 바꾸고 손을 뗀다. 존 첫 진입 안내는 각 존 흐름 몫.
///  · 처음 그릴 때 안내 UI 가 비어 있으면(늦은 클라) <see cref="TutorialGuideUI.RebuildFromState"/>.
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
            TutorialDirector.CorridorMidReached += HandleCorridorMidReachedServer;

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
        TutorialDirector.StateChanged -= Render;
        TutorialDirector.GateOpened -= HandleGateOpened;
        BindTraits(null);
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
    }

    // ─── 서버: 라운드 ──────────────────────────────────────────────────────

    private bool IsTimerExpired => BossTimerManager.Instance != null && BossTimerManager.Instance.State == BossTimerManager.TimerState.Expired;

    private void HandleCorridorMidReachedServer(ulong clientId, int corridor)
    {
        if (!_director.IsTutorialActive) return;

        TutorialCorridorRoundDecision decision = _rounds.Decide(corridor,
            _director.IsGateOpen(TutorialCorridorRules.ExitGate(corridor)), IsTimerExpired);
        switch (decision)
        {
            case TutorialCorridorRoundDecision.Start:
                StartRoundServer(corridor, clientId);
                break;
            case TutorialCorridorRoundDecision.GateAlreadyOpen:
            case TutorialCorridorRoundDecision.TimerExpired:
                _rounds.MarkHandled(corridor, 0);
                Edit.Log($"[Tutorial] 복도{corridor} 특성 라운드 건너뜀 — {decision}.", this);
                break;
        }
    }

    private void StartRoundServer(int corridor, ulong firstClientId)
    {
        TutorialCheck check = TutorialCorridorRules.CheckOf(corridor);
        if (_traits == null || !_traits.IsSpawned)
        {
            _rounds.MarkHandled(corridor, 0);
            ReportConnectedServer(check, onlyWithoutPending: false);
            Edit.LogWarning($"[Tutorial] 복도{corridor}: 특성 디렉터가 없다 — 선택 없이 전원 체크(게이트 {TutorialCorridorRules.ExitGate(corridor)}).", this);
            return;
        }

        bool offered = _traits.StartOfferRoundServer(TutorialCorridorRules.DisplayOf(corridor));
        _rounds.MarkHandled(corridor, offered ? _traits.Round : 0);
        // 0장 받은 사람(풀 소진)·제시 실패 = 고를 게 없다 → 바로 체크.
        ReportConnectedServer(check, onlyWithoutPending: offered);
        Edit.Log($"[Tutorial] 복도{corridor} 특성 라운드 {(offered ? $"{_traits.Round} 시작" : "제시 없음(건너뜀)")} — 첫 도착 client {firstClientId}.", this);
    }

    private void ReportConnectedServer(TutorialCheck check, bool onlyWithoutPending)
    {
        _clientScratch.Clear();
        foreach (ulong id in _nm.ConnectedClientsIds) _clientScratch.Add(id);
        foreach (ulong id in _clientScratch)
        {
            if (onlyWithoutPending && _traits != null && _traits.HasPendingChoiceServer(id)) continue;
            _director.ReportCheckServer(id, check);
        }
    }

    private void HandleTraitChosenServer(TutorialTraitChosen chosen)
    {
        int corridor = _rounds.CorridorOfRound(chosen.Round);
        if (corridor == 0 || !_director.IsTutorialActive) return;
        _director.ReportCheckServer(chosen.ClientId, TutorialCorridorRules.CheckOf(corridor));
    }

    // 출구가 열렸는데 아직 안 고른 사람 → 자동 획득(창 닫힘). 그 복도 라운드가 지금 라운드일 때만.
    private void AutoChoosePendingServer(int corridor, string reason)
    {
        int round = _rounds.RoundOf(corridor);
        if (_traits == null || round == 0 || _traits.Round != round) return;
        int n = _traits.AutoChooseAllPendingServer();
        if (n > 0) Edit.Log($"[Tutorial] 복도{corridor} 미선택 {n}명 자동 획득 — {reason}.", this);
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
                AutoChoosePendingServer(exit, $"{gate} 열림");
                if (exit == 4)
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

        int n = _traits.AutoChooseAllPendingServer();
        Edit.Log($"[Tutorial] 보스 타이머 만료 — 열린 특성 창 {n}명 자동 획득(강제 이동 전).", this);
    }

    // ─── 게이트(모든 피어) ─────────────────────────────────────────────────

    private void HandleGateOpened(TutorialGate gate, TutorialGateOpenReason reason)
    {
        if (_isServer) _openedGateQueue.Add(gate);

        int exit = TutorialCorridorRules.CorridorOfExitGate(gate);
        if (exit > 0 && exit == _ownedCorridor)
        {
            // 이 흐름이 목표를 쓰던 복도만 — 다음 존 흐름이 첫 진입 때 덮어쓴다.
            _ownedCorridor = 0;
            TutorialGuideUI ui = TutorialGuideUI.Instance;
            if (ui != null)
            {
                ui.ClearWait();
                ui.SetObjective(exit == 4 ? GoPadId : NextId);
            }
        }
        Render();
    }

    // ─── 목표 패널(모든 피어 — 로컬 플레이어 기준) ──────────────────────────────

    private void Render()
    {
        if (_director == null || _nm == null || !_director.IsTutorialActive) return;
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
