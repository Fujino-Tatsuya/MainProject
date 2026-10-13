using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 튜토리얼 존1 흐름(S7) — 스폰 환영 · 이동 5m · 대시 · 허수아비 적중 5회 · 패시브 툴팁 1초 → 개인 체크 → G1.
/// 원본: Docs/design/tutorial-scenario.md §3.0·§3.1·§5 · PLAN-tutorial.md S7 · Docs/tech/tutorial-zone1.md.
/// 규칙은 순수 <see cref="TutorialZone1Rules"/>·<see cref="TutorialZone1Progress"/> 등(TutorialZone1Model.cs).
///
/// 붙는 곳: <see cref="TutorialDirector"/> 가 튜토리얼을 켠 것이 그 피어에서 보이는 순간(<see cref="TutorialDirector.StateChanged"/>)
/// 디렉터 GameObject 에 런타임으로 붙는다(<b>모든 피어</b> — 씬·프리팹 저장 없음). 절차 맵은 디렉터가 안 켜져 안 붙는다.
///
/// 서버:
///  · 허수아비 스폰 — StageTutorial 의 <see cref="TutorialZone1Dummies"/> 자리에 간파 사이클 없는 변형을 스폰
///  · 대시 = <see cref="PlayerDashController.ServerDashApproved"/> → <see cref="TutorialDirector.ReportCheckServer"/>(Dash)
///  · 적중 = <see cref="Player.ServerAttackLanded"/>(평타 <see cref="AttackType.Default"/>, 대상이 그 허수아비) → AttackHit(보고 1번 = 적중 1회)
///  · G1 열림 → 단계 Corridor1(<see cref="TutorialDirector.SetStepServer"/>, 다음 프레임 — 디렉터 Commit 안에서 다시 Commit 하지 않게)
///  · G1 뒤 파티가 존1 을 떠나면 허수아비 디스폰(S12 — <see cref="TutorialZone1Rules.ShouldDespawnDummies"/>)
/// 오너(로컬 플레이어):
///  · 이동 = XZ 누적(순간이동 제외) 5m → <see cref="TutorialDirector.ReportLocalMoveDistance"/>(서버 상한 검증)
///  · 패시브 = <see cref="SkillTooltipView.TooltipShown"/>("Passive") 가 1초 유지 → <see cref="TutorialDirector.ReportLocalCheck"/>(PassiveTooltip)
///    이동·대시·평타를 마친 뒤에만 센다(§3.1 순서) · 호버가 끝나면 캐릭터별 패시브 자막
///  · 자막: 착지 1초 뒤 환영(S0) · 대시 차례 보조 · 거너 과열 1단계 · R 조준 시작(존1 1회)
///  · 목표 패널 = 디렉터 복제 상태(개인 진행·대기)로 매번 다시 계산. 처음 그릴 때는 <see cref="TutorialGuideUI.RebuildFromState"/>(늦은 클라 — 자막 생략)
///  · G1 이 열리면 "복도로" 한 번 남기고 손을 뗀다(복도1·존2 안내는 S8).
/// </summary>
[DisallowMultipleComponent]
public sealed class TutorialZone1Flow : MonoBehaviour
{
    // 시나리오 ID(TutorialGuideIds) 외에 이 흐름이 쓰는 문구 행.
    // 🔴 TUT_Z1_ATTACK_GUNNER · TUT_Z1_PASSIVE_{캐릭터} 는 문구 변형 규칙(id + _캐릭터)과 겹쳐 목표 줄(TUT_Z1_ATTACK·TUT_Z1_PASSIVE)을
    //    덮어 버린다 → 자막은 아래 별도 id 로 쓰고, 그 두 행은 비워 둔다(Docs/tech/tutorial-zone1.md).
    public const string AttackHeatId = "TUT_Z1_ATTACK_HEAT";
    public const string PassiveInfoId = "TUT_Z1_PASSIVE_INFO";
    public const string CheckMoveId = "TUT_Z1_CHECK_MOVE";
    public const string CheckDashId = "TUT_Z1_CHECK_DASH";
    public const string CheckAttackId = "TUT_Z1_CHECK_ATTACK";
    public const string CheckPassiveId = "TUT_Z1_CHECK_PASSIVE";

    private const float PlayerScanInterval = 0.5f;
    private const float MoveReportRetrySeconds = 1f;

    private TutorialDirector _director;
    private NetworkManager _nm;
    private bool _isServer;

    // 서버
    private readonly HashSet<Unit> _dummies = new HashSet<Unit>();
    private readonly Dictionary<Player, Action<PlayerAttackLanded>> _attackSubs = new Dictionary<Player, Action<PlayerAttackLanded>>();
    private readonly List<Transform> _spots = new List<Transform>();
    private float _scanTimer;
    private bool _pendingCorridorStep;

    // 오너
    private readonly TutorialMoveAccumulator _move = new TutorialMoveAccumulator();
    private readonly TutorialTooltipHold _passiveHold = new TutorialTooltipHold();
    private readonly TutorialLandingTimer _landing = new TutorialLandingTimer();
    private float _moveReportedAt = float.NegativeInfinity;
    private bool _passiveInfoPending;
    private bool _dashSubShown, _heatTipShown, _rTipShown;
    private bool _handedOff;

    private Player _local;
    private GunnerHeat _heat;
    private PlayerSkillTargeting _targeting;
    private PlayerGroundingSensor _grounding;
    private PlayerLifeCycleController _life;

    private TutorialGuideUI _ui;
    private bool _rebuilt;
    private bool _passiveHighlighted;

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
        if (d.GetComponent<TutorialZone1Flow>() != null) return;
        d.gameObject.AddComponent<TutorialZone1Flow>();
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
            PlayerDashController.ServerDashApproved += HandleDashApprovedServer;
            SpawnDummiesServer();
            _scanTimer = 0f;
        }

        SkillTooltipView.TooltipShown += HandleTooltipShown;
        SkillTooltipView.TooltipHidden += HandleTooltipHidden;
        Player.LocalPlayerChanged += BindLocal;
        TutorialDirector.StateChanged += Render;
        TutorialDirector.GateOpened += HandleGateOpened;
        BindLocal(Player.LocalPlayer);
        _ui = TutorialGuideUI.Instance;

        Edit.Log($"[Tutorial] 존1 흐름 켜짐 (서버:{_isServer}) — 허수아비 {_dummies.Count}개.", this);
        Render();
    }

    private void OnDisable()
    {
        PlayerDashController.ServerDashApproved -= HandleDashApprovedServer;
        SkillTooltipView.TooltipShown -= HandleTooltipShown;
        SkillTooltipView.TooltipHidden -= HandleTooltipHidden;
        Player.LocalPlayerChanged -= BindLocal;
        TutorialDirector.StateChanged -= Render;
        TutorialDirector.GateOpened -= HandleGateOpened;
        BindLocal(null);

        foreach (KeyValuePair<Player, Action<PlayerAttackLanded>> pair in _attackSubs)
            if (pair.Key != null) pair.Key.ServerAttackLanded -= pair.Value;
        _attackSubs.Clear();
    }

    /// <summary>존1 단계가 진행 중인가 — G1 이 아직 닫혀 있다.</summary>
    private bool Zone1Running =>
        _director != null && _director.IsTutorialActive && !_director.IsGateOpen(TutorialGate.G1_Corridor1Entry);

    private void Update()
    {
        if (_director == null) return;

        if (_isServer)
        {
            if (_pendingCorridorStep)
            {
                _pendingCorridorStep = false;
                _director.SetStepServer(TutorialStep.Corridor1);
            }

            _scanTimer -= Time.unscaledDeltaTime;
            if (_scanTimer <= 0f)
            {
                _scanTimer = PlayerScanInterval;
                if (Zone1Running) ScanPlayersServer();
                else if (_dummies.Count > 0) TryDespawnDummiesServer();
            }
        }

        // 안내 UI 가 늦게 깨어나거나 바뀌면 처음부터 다시 그린다.
        if (TutorialGuideUI.Instance != _ui)
        {
            _ui = TutorialGuideUI.Instance;
            _rebuilt = false;
            Render();
        }

        TickLocal();
    }

    // ─── 서버: 허수아비 · 대시 · 적중 ─────────────────────────────────────────

    private void SpawnDummiesServer()
    {
        TutorialStageMarker stage = TutorialStageMarker.Active;
        TutorialZone1Dummies spots = stage != null ? stage.GetComponentInChildren<TutorialZone1Dummies>(true) : null;
        if (spots == null || spots.DummyPrefab == null)
        {
            Edit.LogWarning("[Tutorial] 존1 허수아비 자리(TutorialZone1Dummies)나 프리팹이 없다 — 평타 체크(Z1-3)는 교착 방지(60초)로만 넘어간다. " +
                            "저작 메뉴 Tools/Map/Authoring/Tutorial/8. Place Zone1 Dummies 확인.", this);
            return;
        }

        spots.GetSpots(_spots);
        foreach (Transform spot in _spots)
        {
            GameObject go = Instantiate(spots.DummyPrefab, spot.position, spot.rotation);
            // OnEnable 시점은 맵 씬이 Additive 로 막 올라온 로딩 중이라 활성 씬 = LoadingScene 이다.
            // 그대로 두면 로딩 씬 언로드 때 Spawn(true) 허수아비가 같이 파괴된다 → 자리(StageTutorial)의 씬으로 옮긴다.
            SceneManager.MoveGameObjectToScene(go, spots.gameObject.scene);
            go.name = $"{spots.DummyPrefab.name}_{_dummies.Count}";
            var no = go.GetComponent<NetworkObject>();
            var unit = go.GetComponent<Unit>();
            if (no == null || unit == null)
            {
                Edit.LogError($"[Tutorial] 존1 허수아비 프리팹 '{spots.DummyPrefab.name}' 에 NetworkObject/Unit 이 없다.", this);
                Destroy(go);
                return;
            }
            no.Spawn(true);
            _dummies.Add(unit);
        }
    }

    // 플레이어 스폰 시점이 디렉터 활성과 엇갈려도 놓치지 않게 주기적으로 훑는다(존1 동안만).
    private void ScanPlayersServer()
    {
        foreach (NetworkClient client in _nm.ConnectedClientsList)
        {
            Player player = client.PlayerObject != null ? client.PlayerObject.GetComponent<Player>() : null;
            if (player == null || _attackSubs.ContainsKey(player)) continue;

            Action<PlayerAttackLanded> handler = e => HandleAttackLandedServer(player, e);
            player.ServerAttackLanded += handler;
            _attackSubs.Add(player, handler);
        }
    }

    // G1 이 열리고 파티가 존1 을 떠나면(살아 있는 사람이 존1 에 없음 · 또는 존2 단계) 허수아비를 거둔다(S12 정리) — 보는 앞에서 사라지지 않게.
    private void TryDespawnDummiesServer()
    {
        bool anyInZone1 = false;
        foreach (ulong id in _nm.ConnectedClientsIds)
        {
            if (_director.GetPlayerAreaServer(id) != TutorialArea.Zone1) continue;
            anyInZone1 = true;
            break;
        }
        if (!TutorialZone1Rules.ShouldDespawnDummies(_director.IsGateOpen(TutorialGate.G1_Corridor1Entry), anyInZone1, _director.Step))
            return;

        int n = 0;
        foreach (Unit unit in _dummies)
        {
            if (unit == null) continue;
            NetworkObject no = unit.NetworkObject;
            if (no == null || !no.IsSpawned) continue;
            no.Despawn(true);
            n++;
        }
        _dummies.Clear();
        Edit.Log($"[Tutorial] 존1 허수아비 {n}개 디스폰 — G1 열림 · 파티가 존1 을 떠남.", this);
    }

    private void HandleDashApprovedServer(Player player)
    {
        if (player == null || !Zone1Running) return;
        _director.ReportCheckServer(player.OwnerClientId, TutorialCheck.Dash);
    }

    private void HandleAttackLandedServer(Player player, PlayerAttackLanded landed)
    {
        // 기본 공격만(어쌔신 강타도 Default 로 온다 — 평타라 센다). 판정 1번 = 적중 1회(관통으로 여러 개를 맞혀도 1).
        if (player == null || landed.AttackType != AttackType.Default || !Zone1Running || landed.Targets == null) return;
        for (int i = 0; i < landed.Targets.Count; i++)
        {
            if (!_dummies.Contains(landed.Targets[i])) continue;
            _director.ReportCheckServer(player.OwnerClientId, TutorialCheck.AttackHit);
            return;
        }
    }

    private void HandleGateOpened(TutorialGate gate, TutorialGateOpenReason reason)
    {
        if (gate != TutorialGate.G1_Corridor1Entry) return;
        if (_isServer) _pendingCorridorStep = true;
        Render();
    }

    // ─── 오너: 로컬 플레이어 ────────────────────────────────────────────────

    private void BindLocal(Player player)
    {
        if (_heat != null) _heat.StageChanged -= HandleHeatStageChanged;
        if (_targeting != null) _targeting.TargetingStarted -= HandleTargetingStarted;

        _local = player;
        _heat = player != null ? player.GetComponentInChildren<GunnerHeat>(true) : null;
        _targeting = player != null ? player.GetComponent<PlayerSkillTargeting>() : null;
        _grounding = player != null ? player.GetComponent<PlayerGroundingSensor>() : null;
        _life = player != null ? player.GetComponent<PlayerLifeCycleController>() : null;
        _move.Break();

        if (!isActiveAndEnabled) return;
        if (_heat != null) _heat.StageChanged += HandleHeatStageChanged;
        if (_targeting != null) _targeting.TargetingStarted += HandleTargetingStarted;
        Render();   // 캐릭터별 문구
    }

    private bool LocalAlive => _local != null && (_life == null || _life.State == PlayerLifeState.Alive);

    private bool TryGetLocalProgress(out TutorialPlayerProgress progress)
    {
        progress = default;
        return _director != null && _director.TryGetPlayerProgress(_nm.LocalClientId, out progress);
    }

    private void TickLocal()
    {
        if (!Zone1Running || _local == null) return;

        float dt = Time.unscaledDeltaTime;
        bool alive = LocalAlive;

        // S0 — 착지 후 1초(낙하·사망 중에는 세지 않는다).
        if (!_landing.Fired && _landing.Tick(alive && _grounding != null && _grounding.IsGrounded, dt))
            TutorialGuideUI.Instance?.ShowSubtitle(TutorialGuideIds.S0Welcome);

        TryGetLocalProgress(out TutorialPlayerProgress p);

        // Z1-1 이동 — XZ 누적, 순간이동·사망 구간은 끊는다.
        if (!p.Has(TutorialCheck.Move))
        {
            if (!alive) _move.Break();
            else
            {
                Vector3 pos = _local.transform.position;
                _move.Sample(pos.x, pos.z, dt);
                if (_move.TotalMeters >= TutorialZone1Rules.RequiredMoveMeters
                    && Time.unscaledTime - _moveReportedAt >= MoveReportRetrySeconds)
                {
                    _moveReportedAt = Time.unscaledTime;
                    _director.ReportLocalMoveDistance(_move.TotalMeters);
                }
            }
        }

        // Z1-4 패시브 툴팁 1초 유지.
        if (_passiveHold.Tick(Time.unscaledTimeAsDouble))
        {
            _passiveInfoPending = true;
            _director.ReportLocalCheck(TutorialCheck.PassiveTooltip);
            Render();
        }
    }

    private void HandleTooltipShown(string key)
    {
        if (!string.Equals(key, TutorialZone1Rules.PassiveTooltipKey, StringComparison.Ordinal))
        {
            _passiveHold.Hide();
            return;
        }
        if (!Zone1Running || !TryGetLocalProgress(out TutorialPlayerProgress p)) return;
        if (p.Has(TutorialCheck.PassiveTooltip) || _passiveHold.IsComplete || !TutorialZone1Progress.IsPassiveTurn(p)) return;
        _passiveHold.Show(Time.unscaledTimeAsDouble);
    }

    private void HandleTooltipHidden()
    {
        _passiveHold.Hide();
        if (!_passiveInfoPending) return;

        // Z1-5 — 호버가 끝난 뒤 캐릭터별 패시브 한 줄 → 목표 "복도로".
        _passiveInfoPending = false;
        if (Zone1Running) TutorialGuideUI.Instance?.ShowSubtitle(PassiveInfoId);
        Render();
    }

    private void HandleHeatStageChanged(int stage)
    {
        if (stage < 1 || _heatTipShown || !Zone1Running) return;
        if (TryGetLocalProgress(out TutorialPlayerProgress p) && p.Has(TutorialCheck.AttackHit)) return;
        _heatTipShown = true;
        TutorialGuideUI.Instance?.ShowSubtitle(AttackHeatId);
    }

    private void HandleTargetingStarted(PlayerSkillSlot slot)
    {
        if (slot != PlayerSkillSlot.Ultimate || _rTipShown || !Zone1Running) return;
        _rTipShown = true;
        TutorialGuideUI.Instance?.ShowSubtitle(TutorialGuideIds.TipRCancel);
    }

    // ─── 목표 패널(모든 피어 — 로컬 플레이어 기준) ──────────────────────────────

    private void Render()
    {
        if (_director == null || _nm == null || !_director.IsTutorialActive || _handedOff) return;
        TutorialGuideUI ui = TutorialGuideUI.Instance;
        if (ui == null) return;

        if (_director.IsGateOpen(TutorialGate.G1_Corridor1Entry))
        {
            _handedOff = true;
            _passiveHold.Hide();
            _passiveInfoPending = false;
            // 존1 을 지켜본 클라만 "복도로" 를 남긴다 — G1 뒤에 처음 그리는(늦은) 클라는 다음 단계(S8) 몫.
            if (_rebuilt && _director.Step <= TutorialStep.Corridor1)
            {
                ui.SetObjective(TutorialGuideIds.Z1Go);
                ui.ClearWait();
            }
            ui.ClearHighlight(TutorialHudTarget.SkillSlotP);
            return;
        }

        if (!TryGetLocalProgress(out TutorialPlayerProgress p)) return;

        TutorialZone1View view = TutorialZone1Progress.Resolve(p, _passiveInfoPending);
        string objectiveId = ObjectiveIdOf(view.Task);
        var args = new Dictionary<string, string> { { "n", view.AttackHits.ToString() } };
        TutorialChecklistItem[] checklist =
        {
            new TutorialChecklistItem(CheckMoveId, view.MoveDone),
            new TutorialChecklistItem(CheckDashId, view.DashDone),
            new TutorialChecklistItem(CheckAttackId, view.AttackDone),
            new TutorialChecklistItem(CheckPassiveId, view.PassiveDone),
        };

        bool hasWait = _director.TryGetWait(out TutorialGate waitGate, out int done, out int total, out float remaining);
        bool showWait = TutorialZone1Progress.ShowsWait(hasWait, waitGate, p);
        float seconds = Mathf.Max(0f, remaining);

        if (!_rebuilt)
        {
            // 처음 그릴 때(늦은 클라 포함) — 지나간 자막은 생략하고 지금 상태만.
            _rebuilt = true;
            ui.RebuildFromState(new TutorialGuideState
            {
                ObjectiveId = objectiveId,
                ObjectiveArgs = args,
                Checklist = checklist,
                Wait = showWait ? new TutorialWaitState(done, total, seconds) : default,
                Highlights = view.HighlightPassiveSlot
                    ? new[] { new TutorialHighlightRequest(TutorialHudTarget.SkillSlotP, TutorialHighlightStyle.Pulse) }
                    : Array.Empty<TutorialHighlightRequest>(),
            });
        }
        else
        {
            ui.SetObjective(objectiveId, args, checklist);
            if (showWait) ui.SetWaitParty(done, total, seconds);
            else ui.ClearWait();
            // 같은 강조를 매번 다시 걸면 UI 가 HUD 칸 찾기 재시도·경고를 초기화한다 — 바뀔 때만.
            if (view.HighlightPassiveSlot != _passiveHighlighted)
            {
                if (view.HighlightPassiveSlot) ui.Highlight(TutorialHudTarget.SkillSlotP, TutorialHighlightStyle.Pulse);
                else ui.ClearHighlight(TutorialHudTarget.SkillSlotP);
            }
        }
        _passiveHighlighted = view.HighlightPassiveSlot;

        // Z1-2 보조 자막 — 대시 차례가 처음 왔을 때 1번.
        if (view.Task == TutorialZone1Task.Dash && !_dashSubShown)
        {
            _dashSubShown = true;
            ui.ShowSubtitle(TutorialGuideIds.Z1DashSub);
        }
    }

    private static string ObjectiveIdOf(TutorialZone1Task task) => task switch
    {
        TutorialZone1Task.Move => TutorialGuideIds.Z1Move,
        TutorialZone1Task.Dash => TutorialGuideIds.Z1Dash,
        TutorialZone1Task.Attack => TutorialGuideIds.Z1Attack,
        TutorialZone1Task.Passive => TutorialGuideIds.Z1Passive,
        _ => TutorialGuideIds.Z1Go,
    };
}
