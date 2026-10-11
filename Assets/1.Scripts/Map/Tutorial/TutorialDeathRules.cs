using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 튜토리얼 사망 규칙(S11, §2.5 D5) — <b>서버 전용</b>. 규칙은 순수 <see cref="TutorialDeathRulesModel"/>·<see cref="TutorialRevivePointRules"/>,
/// 이 컴포넌트는 서버 신호를 모델에 넣고 결과(부활·이동·무적)를 실행한다.
/// 원본: Docs/design/tutorial-scenario.md §2.5·§3.7·§3.9·§4 · PLAN-tutorial.md S11 · Docs/tech/tutorial-director.md §8.
///
/// 붙는 곳: 서버에서 <see cref="TutorialDirector"/> 가 튜토리얼을 켠 순간(<see cref="TutorialDirector.StateChanged"/> 에서
/// <see cref="TutorialDirector.IsTutorialActive"/>) 디렉터 GameObject 에 런타임으로 붙는다 — 씬·프리팹 저장 없음.
/// 절차 맵·비튜토리얼은 디렉터가 켜지지 않아 붙지 않는다(기존 규칙 그대로).
///
/// 흐름:
///  · 사망(Alive → DeadPresentation) → 3초 뒤 자동 부활 + 2초 무적. 차감 여부 = 사망 시점 G8(타이머 시작) 열림.
///    G8 전 사망은 목숨 수와 무관하게 Soul 로 가게 <see cref="PlayerLifeCycleController.ServerDeathKeepsSoul"/> 를 건다.
///    G8 뒤는 기존 규칙 — 목숨 0 이면 PermanentDead(관전), 전원이면 PartyWipeWatcher 결과 화면.
///  · 부활 지점 = 지금 단계 구간의 안전한 지점(<see cref="TutorialRevivePointRules"/>) — 격자 후보를 바닥·점유 검사로 거른 뒤 고른다.
///  · 보스방 이동 카운트다운(<see cref="BossTeleportManager.IsCountdownActive"/> — 패드·타이머 만료 강제 이동 공통)이 보이면
///    대기 부활을 즉시·그 자리로 바꾼다. 튜토리얼 구간 밖(보스방)에 있는 사람도 그 자리에서 되살린다(옛 구간으로 끌고 가지 않음).
///  · 보스방 도착(<see cref="BossTeleportManager.AlivePlayersArrived"/>/<see cref="BossTeleportManager.ArrivalAborted"/>) 또는
///    단계 <see cref="TutorialStep.Finished"/> 이후 사망은 일반 규칙(자동 부활 없음). 남은 대기는 그 자리에서 끝낸다.
///  · 연출 잠금(<see cref="PlayerEncounterLock.IsCinematicLocked"/>) 중이면 보류하고 경고 1회 — 풀리면 다음 프레임에 부활.
///  · 추락 생존 복구(존2 물 구멍)는 건드리지 않는다. 추락 사망도 사망이라 같은 규칙으로 부활한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class TutorialDeathRules : MonoBehaviour
{
    [Header("자동 부활 (§2.5)")]
    [SerializeField, Min(0f)] private float reviveDelaySeconds = (float)TutorialDeathRulesModel.DefaultReviveDelaySeconds;
    [SerializeField, Min(0f)] private float reviveInvulnerabilitySeconds = 2f;

    [Header("부활 지점")]
    [Tooltip("몬스터 인지 범위로 보는 반경(m). MonsterDataSO.detectionRadius 기본 8m 보다 넉넉히(몬스터 데이터는 공개 API 가 없어 고정값).")]
    [SerializeField, Min(0f)] private float monsterAwarenessRadius = 10f;
    [SerializeField, Min(0.5f)] private float candidateSpacing = TutorialRevivePointRules.DefaultSpacing;
    [Tooltip("구간 가장자리에서 이만큼 안쪽에만 후보를 깐다(m) — 벽·배리어(복도 끝 안쪽 0.4m)에서 떨어지게.")]
    [SerializeField, Min(0f)] private float candidateInset = TutorialRevivePointRules.DefaultInset;
    [Tooltip("1층 바닥만 쓴다 — 스테이지 높이 ± 이 값(m) 안의 바닥만 후보(존4 2층·벽 윗면 제외).")]
    [SerializeField, Min(0.1f)] private float floorHeightTolerance = 1f;
    [SerializeField, Min(0.05f)] private float occupancyRadius = 0.4f;
    [SerializeField, Range(0f, 30f)] private float maxFlatAngle = 10f;

    private const float ProbeHeight = 3f;

    private TutorialDirector _director;
    private TutorialDeathRulesModel _model;
    private TutorialAreaTracker _areas;
    private BossTeleportManager _teleport;
    private NetworkManager _nm;
    private System.Func<ulong, bool> _keepsSoul;
    private bool _finished;
    private int _groundMask;
    private int _obstacleMask;

    private readonly List<(float x, float z)> _grid = new List<(float x, float z)>();
    private readonly List<TutorialRevivePoint> _candidates = new List<TutorialRevivePoint>();
    private readonly List<float> _candidateY = new List<float>();
    private readonly List<TutorialThreat> _threats = new List<TutorialThreat>();

    // ─── 부착(서버) ──────────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void InstallBootstrap()
    {
        // 도메인 리로드를 꺼도 두 번 걸리지 않게 먼저 뗀다.
        TutorialDirector.StateChanged -= EnsureOnServer;
        TutorialDirector.StateChanged += EnsureOnServer;
    }

    private static void EnsureOnServer()
    {
        TutorialDirector d = TutorialDirector.Instance;
        if (d == null || !d.IsSpawned || !d.IsServer || !d.IsTutorialActive) return;
        if (d.GetComponent<TutorialDeathRules>() != null) return;
        d.gameObject.AddComponent<TutorialDeathRules>();
    }

    // ─── 생명주기 ────────────────────────────────────────────────────────

    private void OnEnable()
    {
        _director = GetComponent<TutorialDirector>();
        _nm = NetworkManager.Singleton;
        if (_director == null || _nm == null || !_nm.IsServer)
        {
            enabled = false;
            return;
        }

        _model = new TutorialDeathRulesModel(reviveDelaySeconds);
        _finished = false;
        _groundMask = LayerMask.GetMask("Default", "Ground", "Env");
        _obstacleMask = _groundMask | LayerMask.GetMask("Wall");

        PlayerLifeCycleController.ServerLifeStateChanged += HandleServerLifeStateChanged;
        _nm.OnClientDisconnectCallback += HandleClientDisconnected;

        if (PlayerLifeCycleController.ServerDeathKeepsSoul != null)
            Edit.LogWarning("[Tutorial] 사망 목적지 훅이 이미 걸려 있다 — 튜토리얼 규칙으로 덮는다.", this);
        _keepsSoul = KeepsSoul;
        PlayerLifeCycleController.ServerDeathKeepsSoul = _keepsSoul;

        BuildAreas();
        Edit.Log($"[Tutorial] 사망 규칙 켜짐 — {reviveDelaySeconds:0.#}초 뒤 자동 부활 · 무적 {reviveInvulnerabilitySeconds:0.#}초 · " +
                 "G8 전 차감 없음 / 뒤 1개 차감.", this);
    }

    private void OnDisable()
    {
        PlayerLifeCycleController.ServerLifeStateChanged -= HandleServerLifeStateChanged;
        if (_nm != null) _nm.OnClientDisconnectCallback -= HandleClientDisconnected;
        if (_keepsSoul != null && PlayerLifeCycleController.ServerDeathKeepsSoul == _keepsSoul)
            PlayerLifeCycleController.ServerDeathKeepsSoul = null;
        _keepsSoul = null;
        BindTeleport(null);
    }

    private void BuildAreas()
    {
        TutorialStageMarker stage = TutorialStageMarker.Active;
        _areas = new TutorialAreaTracker();
        if (!TutorialAreaLayout.TryBuild(stage != null ? stage.transform : null, _areas, out string error))
            Edit.LogWarning($"[Tutorial] 사망 규칙: 구간 계산 실패 — {error}. 안전 지점을 못 찾으면 그 자리에서 부활한다.", this);
    }

    /// <summary>튜토리얼 규칙으로 새 사망을 받는가.</summary>
    private bool RulesActive =>
        !_finished && _director != null && _director.IsTutorialActive && _director.Step != TutorialStep.Finished;

    private bool KeepsSoul(ulong clientId) => _model != null && _model.IsPenaltyFree(clientId);

    private void Update()
    {
        if (_model == null || _nm == null || !_nm.IsServer) return;

        if (!_finished && _director != null && _director.Step == TutorialStep.Finished)
            Finish("단계 Finished");

        TrackBossMove();

        if (_model.PendingCount == 0) return;

        double now = _nm.ServerTime.Time;
        foreach (ulong clientId in _model.PendingIds)
            Tick(clientId, now);
    }

    // ─── 신호 → 모델 ─────────────────────────────────────────────────────

    private void HandleServerLifeStateChanged(ulong clientId, PlayerLifeState from, PlayerLifeState to)
    {
        if (from != PlayerLifeState.Alive || to != PlayerLifeState.DeadPresentation || !RulesActive) return;

        bool timerStarted = _director.IsGateOpen(TutorialGate.G8_TimerStart);
        _model.RecordDeath(clientId, _nm.ServerTime.Time, timerStarted);
        Edit.Log($"[Tutorial] client {clientId} 사망 — " +
                 (_model.IsMoveInProgress ? "보스방 이동 중이라 Soul 이 되는 즉시 그 자리에서" : $"{reviveDelaySeconds:0.#}초 뒤") +
                 $" 자동 부활(목숨 차감 {(timerStarted ? "1개 — G8 뒤" : "없음 — G8 전")}).", this);
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (_model != null && _model.Forget(clientId))
            Edit.Log($"[Tutorial] client {clientId} 이탈 — 대기 중인 자동 부활 취소.", this);
    }

    /// <summary>패드·타이머 만료 강제 이동 공통 카운트다운을 본다(서버 복제값).</summary>
    private void TrackBossMove()
    {
        if (_teleport != BossTeleportManager.Instance) BindTeleport(BossTeleportManager.Instance);

        bool countdown = _teleport != null && _teleport.IsSpawned && _teleport.IsCountdownActive;
        if (countdown && !_model.IsMoveInProgress)
        {
            int n = _model.BeginMove();
            Edit.Log($"[Tutorial] 보스방 이동 카운트다운 시작 — 대기 중인 자동 부활 {n}건을 취소하고 그 자리에서 즉시 부활시킨다.", this);
        }
        else if (!countdown && _model.IsMoveInProgress)
        {
            _model.EndMove();
        }
    }

    private void BindTeleport(BossTeleportManager next)
    {
        if (_teleport != null)
        {
            _teleport.AlivePlayersArrived -= HandleArrived;
            _teleport.ArrivalAborted -= HandleArrivalAborted;
        }
        _teleport = next;
        if (_teleport != null)
        {
            _teleport.AlivePlayersArrived += HandleArrived;
            _teleport.ArrivalAborted += HandleArrivalAborted;
        }
    }

    private void HandleArrived(IReadOnlyList<ulong> _) => Finish("보스방 도착");
    private void HandleArrivalAborted() => Finish("보스방 도착 무효(ACK 실패) — 이미 이동함");

    private void Finish(string why)
    {
        if (_finished) return;
        _finished = true;
        int n = _model.ConvertAllToInPlace();
        Edit.Log($"[Tutorial] 사망 규칙 종료({why}) — 이후 사망은 일반 규칙. 남은 자동 부활 {_model.PendingCount}건은 그 자리에서 마무리" +
                 (n > 0 ? $"(안전 지점 → 그 자리 {n}건)." : "."), this);
    }

    // ─── 판정 → 실행 ─────────────────────────────────────────────────────

    private void Tick(ulong clientId, double now)
    {
        if (!_nm.ConnectedClients.TryGetValue(clientId, out NetworkClient client) || client.PlayerObject == null)
        {
            _model.Forget(clientId);
            return;
        }

        NetworkObject po = client.PlayerObject;
        var life = po.GetComponent<PlayerLifeCycleController>();
        if (life == null)
        {
            _model.Forget(clientId);
            return;
        }

        var encounterLock = po.GetComponent<PlayerEncounterLock>();
        bool locked = encounterLock != null && encounterLock.IsCinematicLocked;

        switch (_model.Evaluate(clientId, now, life.State, locked, out TutorialReviveOrder order, out bool firstBlock))
        {
            case TutorialReviveVerdict.BlockedByCinematic:
                if (firstBlock)
                    Edit.LogWarning($"[Tutorial] client {clientId} 자동 부활 보류 — 보스 등장 연출 잠금 중이라 생명주기 전이가 막혀 있다(상태 {life.State}). " +
                                    "잠금이 풀리면 그 자리에서 부활한다.", this);
                break;

            case TutorialReviveVerdict.Revive:
                Revive(clientId, po, order);
                break;
        }
    }

    private void Revive(ulong clientId, NetworkObject po, TutorialReviveOrder order)
    {
        var revive = po.GetComponent<PlayerReviveController>();
        if (revive == null)
        {
            Debug.LogError($"[Tutorial] client {clientId} 자동 부활 불가 — PlayerReviveController 가 없다(Soul 로 남는다).", this);
            return;
        }

        Vector3 here = po.transform.position;
        bool inPlace = order.InPlace || !IsInsideTutorial(here);
        Vector3 target = here;
        string where = "그 자리";
        if (!inPlace)
        {
            if (TryFindRevivePoint(here, out target, out bool safe, out TutorialArea area))
                where = $"{area} {(safe ? "안전 지점" : "안전 지점 없음 → 몬스터에서 가장 먼 지점")} {target}";
            else
            {
                target = here;
                inPlace = true;
                where = "그 자리(후보 없음)";
            }
        }

        if (!revive.TryCompleteReviveOnServer(order.ConsumeLife))
        {
            Debug.LogError($"[Tutorial] client {clientId} 자동 부활 실패 — 상태 {po.GetComponent<PlayerLifeCycleController>()?.State}, " +
                           $"목숨 차감 {order.ConsumeLife}. Soul 로 남는다.", this);
            return;
        }

        po.GetComponent<PlayerLandingProtection>()?.BeginProtection(
            InvulnerabilityCause.Revive, reviveInvulnerabilitySeconds, applyStun: false);

        if (!inPlace)
        {
            var recovery = po.GetComponent<PlayerFallRecovery>();
            if (recovery == null || !recovery.RelocateServer(target))
                where = "그 자리(순간이동 불가)";
        }

        Edit.Log($"[Tutorial] client {clientId} 자동 부활 — {where}, 목숨 차감 {(order.ConsumeLife ? "1개" : "없음")}" +
                 (order.WasBlocked ? ", 연출 잠금 해제 뒤" : "") + ".", this);
    }

    // ─── 부활 지점 ───────────────────────────────────────────────────────

    private bool IsInsideTutorial(Vector3 p)
    {
        if (_areas == null) return false;
        for (TutorialArea a = TutorialArea.Zone1; a <= TutorialArea.BossEnter; a++)
            if (_areas.TryGetArea(a, out TutorialRect r) && r.Contains(p.x, p.z, 1f)) return true;
        return false;
    }

    private int OpenedGateMask()
    {
        int mask = 0;
        for (int i = 0; i < TutorialFlowModel.GateCount; i++)
            if (_director.IsGateOpen((TutorialGate)i)) mask |= 1 << i;
        return mask;
    }

    private bool TryFindRevivePoint(Vector3 deathPos, out Vector3 point, out bool safe, out TutorialArea area)
    {
        point = deathPos;
        safe = false;
        area = TutorialArea.None;
        if (_areas == null) return false;

        TutorialStageMarker stage = TutorialStageMarker.Active;
        float floorY = stage != null ? stage.transform.position.y : deathPos.y;
        int mask = OpenedGateMask();
        TutorialStep step = _director.Step;   // 지금 단계 구간 = 단계 기준(배리어 기준이 상한) — S12

        _candidates.Clear();
        _candidateY.Clear();
        foreach (TutorialArea a in TutorialRevivePointRules.AreaOrder(step, mask))
        {
            if (!_areas.TryGetArea(a, out TutorialRect rect)) continue;
            _grid.Clear();
            TutorialRevivePointRules.SampleGrid(rect, candidateSpacing, candidateInset, _grid);
            foreach ((float x, float z) in _grid)
            {
                if (!TryResolveStandPoint(x, z, floorY, out float y)) continue;
                _candidates.Add(new TutorialRevivePoint(x, z, a));
                _candidateY.Add(y);
            }
        }

        CollectThreats();
        if (!TutorialRevivePointRules.TrySelect(_candidates, _threats, step, mask, deathPos.x, deathPos.z, out int index, out safe))
            return false;

        TutorialRevivePoint chosen = _candidates[index];
        point = new Vector3(chosen.X, _candidateY[index], chosen.Z);
        area = chosen.Area;
        return true;
    }

    // 1층 평평한 바닥이 있고 서 있을 자리가 비었는가(벽·배리어(Wall)·소품 점유 제외).
    private bool TryResolveStandPoint(float x, float z, float floorY, out float y)
    {
        y = floorY;
        var origin = new Vector3(x, floorY + ProbeHeight, z);
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, ProbeHeight * 2f, _groundMask, QueryTriggerInteraction.Ignore))
            return false;
        if (Mathf.Abs(hit.point.y - floorY) > floorHeightTolerance) return false;
        if (Vector3.Angle(hit.normal, Vector3.up) > maxFlatAngle) return false;

        Vector3 center = hit.point + Vector3.up * (occupancyRadius + 0.05f);
        if (Physics.CheckSphere(center, occupancyRadius, _obstacleMask, QueryTriggerInteraction.Ignore)) return false;

        y = hit.point.y;
        return true;
    }

    private void CollectThreats()
    {
        _threats.Clear();
        MonsterBase[] monsters = FindObjectsByType<MonsterBase>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (MonsterBase m in monsters)
        {
            if (m == null || !m.isActiveAndEnabled || m.CurrentHealth <= 0 || m.State == MonsterState.Dead) continue;
            Vector3 p = m.transform.position;
            _threats.Add(new TutorialThreat(p.x, p.z, monsterAwarenessRadius));
        }
    }
}
