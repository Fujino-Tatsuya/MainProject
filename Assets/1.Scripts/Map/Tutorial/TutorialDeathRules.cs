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
///  · 사망(Alive → DeadPresentation) → 3초 뒤 자동 부활 + 2초 무적. 차감 여부 = 사망 시점 G11(보스방 도착) 열림.
///    G11 전 사망(존1~복도4·패드 대기·타이머 만료 강제 이동 중 — G8 타이머 시작 뒤 포함)은 목숨 수와 무관하게 Soul 로 가게
///    <see cref="PlayerLifeCycleController.ServerDeathKeepsSoul"/> 를 건다.
///    G11 뒤는 기존 규칙 — 파티 공용 목숨(할 일 30, MultiGameRule) 0 이면 PermanentDead(관전), 전멸이면 PartyWipeWatcher.
///    차감 부활 시점에 공용 목숨이 이미 0 이면(다른 사람 부활로 비었다) 부활이 거부되고 Soul 로 남는다 — 전멸 판정은 그 Soul 을 탈락으로 센다.
///  · 부활 지점 = 지금 단계 구간의 안전한 지점(<see cref="TutorialRevivePointRules"/>) — 격자 후보를 바닥·점유 검사로 거른 뒤 고른다.
///    그 플레이어의 뒤 배리어 너머(지나온 최전방 구간보다 뒤 · 지난 배리어 앞면에서 여유 미만)는 빼고, 단계 구간이 그보다 뒤면 올린다(C2).
///  · 추락 복구의 스폰 폴백(존1)이 그 플레이어의 뒤 배리어 너머면 같은 규칙의 안전 지점을 대신 준다(<see cref="PlayerSafePointTracker.ReturnPointFallback"/>).
///  · 보스 진입 패드 범위 + 1m 안은 부활·추락 복구 지점에서 뺀다(C3 — <see cref="ServerPadAreas"/>). G10 을 지난 사람의 보스 진입 존 후보가
///    전부 빠지면 G10 배리어 바로 앞(보스 진입 존 쪽, 패드 밖)으로. 추락 복구 거름(<see cref="TutorialBarriers.IsReturnPointAllowed"/>)도 같은 범위를 뺀다.
///  · 보스방 이동 카운트다운(<see cref="BossTeleportManager.IsCountdownActive"/> — 패드·타이머 만료 강제 이동 공통)이 보이면
///    대기 부활을 즉시로 바꾼다. 카운트다운 중 부활 자리 = 보스 진입 고정 지점(<see cref="TutorialSafePoints.TryResolveFixedPoint"/>, 할 일 80) —
///    지점이 없거나 카운트다운이 끝난 뒤(엘리베이터·도착 뒤 — 연출 잠금 보류 후)면 그 자리.
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
    private List<TutorialBarrierSpec> _barrierSpecs;
    private BossTeleportManager _teleport;
    private NetworkManager _nm;
    private System.Func<ulong, bool> _keepsSoul;
    private System.Func<ulong, Vector3, Vector3?> _fallFallback;
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
        _obstacleMask = _groundMask | LayerMask.GetMask("Wall", "Barrier");   // 할 일 32 — 배리어는 Barrier 레이어

        PlayerLifeCycleController.ServerLifeStateChanged += HandleServerLifeStateChanged;
        _nm.OnClientDisconnectCallback += HandleClientDisconnected;

        if (PlayerLifeCycleController.ServerDeathKeepsSoul != null)
            Edit.LogWarning("[Tutorial] 사망 목적지 훅이 이미 걸려 있다 — 튜토리얼 규칙으로 덮는다.", this);
        _keepsSoul = KeepsSoul;
        PlayerLifeCycleController.ServerDeathKeepsSoul = _keepsSoul;
        _fallFallback = FallReturnFallback;
        PlayerSafePointTracker.ReturnPointFallback = _fallFallback;

        BuildAreas();
        Edit.Log($"[Tutorial] 사망 규칙 켜짐 — {reviveDelaySeconds:0.#}초 뒤 자동 부활 · 무적 {reviveInvulnerabilitySeconds:0.#}초 · " +
                 "G11(보스방 도착) 전 차감 없음 / 뒤 1개 차감.", this);
    }

    private void OnDisable()
    {
        PlayerLifeCycleController.ServerLifeStateChanged -= HandleServerLifeStateChanged;
        if (_nm != null) _nm.OnClientDisconnectCallback -= HandleClientDisconnected;
        if (_keepsSoul != null && PlayerLifeCycleController.ServerDeathKeepsSoul == _keepsSoul)
            PlayerLifeCycleController.ServerDeathKeepsSoul = null;
        _keepsSoul = null;
        if (_fallFallback != null && PlayerSafePointTracker.ReturnPointFallback == _fallFallback)
            PlayerSafePointTracker.ReturnPointFallback = null;
        _fallFallback = null;
        BindTeleport(null);
    }

    private void BuildAreas()
    {
        TutorialStageMarker stage = TutorialStageMarker.Active;
        _areas = new TutorialAreaTracker();
        if (!TutorialAreaLayout.TryBuild(stage != null ? stage.transform : null, _areas, out string error))
            Edit.LogWarning($"[Tutorial] 사망 규칙: 구간 계산 실패 — {error}. 안전 지점을 못 찾으면 그 자리에서 부활한다.", this);
        // 개인 뒤 배리어 앞면 여유 판정용 — 디렉터·TutorialBarriers 와 같은 구간·같은 규칙이라 같은 모양.
        _barrierSpecs = TutorialBarrierRules.BuildSpecs(
            _areas, TutorialBarrierRules.DefaultThickness, TutorialBarrierRules.DefaultSideOverlap, out _);
    }

    /// <summary>
    /// [서버, 추락 복구 스폰 폴백 대체] 안전지점 큐가 모두 실패했고 스폰(존1)이 그 플레이어의 뒤 배리어 너머일 때 —
    /// 부활 지점과 같은 규칙으로 그 플레이어 기준 지금 단계 구간의 안전 지점을 준다. 못 찾으면 null(스폰 위치).
    /// </summary>
    private Vector3? FallReturnFallback(ulong clientId, Vector3 fallPoint)
    {
        if (_finished || _director == null || !_director.IsTutorialActive) return null;
        if (!TryFindRevivePoint(clientId, fallPoint, out Vector3 point, out _, out TutorialArea area)) return null;
        Edit.Log($"[Tutorial] client {clientId} 추락 복구 — 스폰이 뒤 배리어 너머라 {area} 안전 지점 {point} 으로.", this);
        return point;
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
        // Alive → Soul 은 판 도중 재합류 생명 복원(할 일 48 C14 R5c — PlayerLifeCycleController.RestoreForRejoinServer)만 낸다.
        // 퇴장 때 대기 부활이 취소됐으므로(HandleClientDisconnected) 새 사망처럼 다시 걸어 Soul 에 갇히지 않게 한다.
        bool died = to == PlayerLifeState.DeadPresentation || to == PlayerLifeState.Soul;
        if (from != PlayerLifeState.Alive || !died || !RulesActive) return;

        bool bossRoomReached = TutorialDeathRulesModel.IsLifeChargedAt(OpenedGateMask());
        _model.RecordDeath(clientId, _nm.ServerTime.Time, bossRoomReached);
        Edit.Log($"[Tutorial] client {clientId} 사망 — " +
                 (_model.IsMoveInProgress ? "보스방 이동 중이라 Soul 이 되는 즉시 그 자리에서" : $"{reviveDelaySeconds:0.#}초 뒤") +
                 $" 자동 부활(목숨 차감 {(bossRoomReached ? "1개 — G11 뒤" : "없음 — G11 전")}).", this);
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

    // ─── 전멸 리스폰(할 일 30 (2)) ────────────────────────────────────────

    /// <summary>
    /// [서버] 보스 재도전 되감기(<see cref="TutorialDirector.RewindForBossRetryServer"/>) 뒤 사망 규칙을 다시 켠다 — 보스방에 다시 도착(G11)하기 전 사망은
    /// 차감 없이 복도4 에서 자동 부활, 도착 뒤는 다시 일반 규칙. 남은 대기 기록은 버린다(리스폰이 전원을 살렸다).
    /// </summary>
    public void ResumeForBossRetry()
    {
        if (_model == null) return;
        foreach (ulong id in _model.PendingIds) _model.Forget(id);
        _model.EndMove();
        _finished = false;
        Edit.Log("[Tutorial] 사망 규칙 다시 켜짐(보스 재도전) — G11 전 사망은 차감 없음.", this);
    }

    /// <summary>
    /// [서버] 전멸 리스폰 지점 <paramref name="count"/> 개 — 복도4 격자(<see cref="TutorialRevivePointRules.SampleGrid"/>)를
    /// 최전방 = 복도4 마스크(<see cref="TutorialBackBarrierRules.MaskWithFrontmost"/>)의 G9 앞면 여유·바닥·점유로 거른 뒤
    /// 패드 범위 + 1m 밖에서 서로 떨어지게 고른다(<see cref="PartyWipeRespawnRules.TrySelectSpread"/>). 복도4 가 비면 false.
    /// </summary>
    public bool TryFindWipeRespawnPoints(int count, List<Vector3> into)
    {
        into.Clear();
        if (_areas == null || !_areas.TryGetArea(TutorialArea.Corridor4, out TutorialRect rect)) return false;
        return TryFindAreaSpreadPoints(TutorialArea.Corridor4, count, rect.CenterX, rect.CenterZ, into);
    }

    /// <summary>
    /// [서버] 구간 <paramref name="area"/> 안 지점 <paramref name="count"/> 개 — 전멸 리스폰과 같은 규칙: 격자(<see cref="TutorialRevivePointRules.SampleGrid"/>)를
    /// 최전방 = <paramref name="area"/> 마스크(<see cref="TutorialBackBarrierRules.MaskWithFrontmost"/>)의 지난 배리어 앞면 여유·바닥·점유로 거른 뒤
    /// 패드 범위 + 1m 밖에서 기준점(<paramref name="refX"/>, <paramref name="refZ"/>)에 가까운 순으로 서로 떨어지게 고른다.
    /// 구간 스킵(<see cref="TutorialSkipController"/> — 기준점 = 입구 배리어)도 쓴다. 후보가 없으면 false.
    /// </summary>
    public bool TryFindAreaSpreadPoints(TutorialArea area, int count, float refX, float refZ, List<Vector3> into)
    {
        into.Clear();
        if (_areas == null || !_areas.TryGetArea(area, out TutorialRect rect)) return false;

        TutorialStageMarker stage = TutorialStageMarker.Active;
        float floorY = stage != null ? stage.transform.position.y : 0f;
        int passed = TutorialBackBarrierRules.MaskWithFrontmost(area);
        float clearance = _director != null ? _director.BackBarrierClearance : TutorialBackBarrierRules.DefaultClearance;

        _grid.Clear();
        TutorialRevivePointRules.SampleGrid(rect, candidateSpacing, candidateInset, _grid);
        var points = new List<(float x, float z)>();
        var ys = new List<float>();
        foreach ((float x, float z) in _grid)
        {
            if (TutorialBackBarrierRules.IsNearPassedFace(_barrierSpecs, area, x, z, passed, clearance)) continue;
            if (!TryResolveStandPoint(x, z, floorY, out float y)) continue;
            points.Add((x, z));
            ys.Add(y);
        }

        var chosen = new List<int>();
        if (!PartyWipeRespawnRules.TrySelectSpread(points, count, refX, refZ, PartyWipeRespawnRules.DefaultSpacing,
                                                    ServerPadAreas(), TutorialRevivePointRules.DefaultPadMargin, chosen))
            return false;

        foreach (int i in chosen) into.Add(new Vector3(points[i].x, ys[i], points[i].z));
        return true;
    }

    /// <summary>[서버] 구간 사각형(구간 계산 실패면 false) — 구간 스킵의 기준점 계산용.</summary>
    public bool TryGetAreaRect(TutorialArea area, out TutorialRect rect)
    {
        rect = default;
        return _areas != null && _areas.TryGetArea(area, out rect);
    }

    /// <summary>[서버] 구간 <paramref name="area"/> 로 들어가는 배리어(너머 구간 = area)의 모양. 존1 등 없으면 false.</summary>
    public bool TryGetEntryBarrier(TutorialArea area, out TutorialBarrierSpec spec)
    {
        spec = default;
        if (_barrierSpecs == null) return false;
        foreach (TutorialBarrierSpec s in _barrierSpecs)
        {
            if (TutorialBackBarrierRules.AreaBeyond(s) != area) continue;
            spec = s;
            return true;
        }
        return false;
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
        // 보스방 이동 카운트다운 중 부활(즉시) = 보스 진입 고정 지점(할 일 80). 지점이 없으면 기존대로 그 자리.
        if (order.InPlace && _model.IsMoveInProgress && TutorialSafePoints.Active != null
            && TutorialSafePoints.Active.TryResolveFixedPoint(TutorialArea.BossEnter, clientId, "카운트다운 중 부활", out target))
        {
            inPlace = false;
            where = $"{TutorialArea.BossEnter} 안전 지점(카운트다운 중) {target}";
        }
        else if (!inPlace)
        {
            if (TryFindRevivePoint(clientId, here, out target, out bool safe, out TutorialArea area))
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
            if (order.ConsumeLife)
            {
                Edit.LogWarning($"[Tutorial] client {clientId} 자동 부활 거부 — 파티 공용 목숨이 없다(또는 상태 {po.GetComponent<PlayerLifeCycleController>()?.State}). Soul 로 남는다.", this);
                return;
            }
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

    private bool TryFindRevivePoint(ulong clientId, Vector3 deathPos, out Vector3 point, out bool safe, out TutorialArea area)
    {
        point = deathPos;
        safe = false;
        area = TutorialArea.None;
        if (_areas == null) return false;

        TutorialStageMarker stage = TutorialStageMarker.Active;
        float floorY = stage != null ? stage.transform.position.y : deathPos.y;
        int mask = OpenedGateMask();
        TutorialStep step = _director.Step;   // 지금 단계 구간 = 단계 기준(배리어 기준이 상한) — S12
        // 개인 뒤 배리어(C2) — 그 플레이어가 지나온 최전방 구간 이상만, 지난 배리어 앞면에서 여유 이상만(로컬 콜라이더 끼임 방지).
        int passed = _director.GetBackBarrierMaskServer(clientId);
        float clearance = _director.BackBarrierClearance;

        _candidates.Clear();
        _candidateY.Clear();
        foreach (TutorialArea a in TutorialRevivePointRules.AreaOrder(step, mask, passed))
        {
            if (!_areas.TryGetArea(a, out TutorialRect rect)) continue;
            _grid.Clear();
            TutorialRevivePointRules.SampleGrid(rect, candidateSpacing, candidateInset, _grid);
            foreach ((float x, float z) in _grid)
            {
                if (TutorialBackBarrierRules.IsNearPassedFace(_barrierSpecs, a, x, z, passed, clearance)) continue;
                if (!TryResolveStandPoint(x, z, floorY, out float y)) continue;
                _candidates.Add(new TutorialRevivePoint(x, z, a));
                _candidateY.Add(y);
            }
        }

        CollectThreats();
        IReadOnlyList<TutorialPadArea> pads = ServerPadAreas();
        if (TutorialRevivePointRules.TrySelect(_candidates, _threats, step, mask, passed, deathPos.x, deathPos.z,
                                               pads, TutorialRevivePointRules.DefaultPadMargin, out int index, out safe))
        {
            TutorialRevivePoint chosen = _candidates[index];
            point = new Vector3(chosen.X, _candidateY[index], chosen.Z);
            area = chosen.Area;
            return true;
        }

        // 패드 제외(C3)로 남는 후보가 없다 — G10 을 지난 사람(보스 진입 존 차례)만 G10 배리어 바로 앞으로.
        if (pads.Count == 0 || TutorialBackBarrierRules.FrontmostPassedArea(passed) != TutorialArea.BossEnter) return false;
        return TryPadFallback(pads, floorY, clearance, out point, out safe, out area);
    }

    private bool TryPadFallback(IReadOnlyList<TutorialPadArea> pads, float floorY, float clearance,
                                out Vector3 point, out bool safe, out TutorialArea area)
    {
        point = default;
        safe = false;
        area = TutorialArea.None;
        if (_barrierSpecs == null) return false;
        foreach (TutorialBarrierSpec s in _barrierSpecs)
        {
            if (s.Gate != TutorialGate.G10_BossEnterAndPad) continue;
            _grid.Clear();
            TutorialRevivePointRules.PadFallbackCandidates(s, pads, TutorialRevivePointRules.DefaultPadMargin,
                                                           TutorialRevivePointRules.DefaultPadFallbackDistance, clearance, _grid);
            foreach ((float x, float z) in _grid)
            {
                if (!TryResolveStandPoint(x, z, floorY, out float y)) continue;
                point = new Vector3(x, y, z);
                safe = TutorialRevivePointRules.Clearance(x, z, _threats) >= 0f;
                area = TutorialArea.BossEnter;
                Edit.Log($"[Tutorial] 부활 지점 — 보스 진입 존 후보가 모두 패드 범위라 G10 배리어 앞 {point} 으로.", this);
                return true;
            }
        }
        return false;
    }

    // ─── 보스 진입 패드 범위(C3) ─────────────────────────────────────────

    private static readonly List<TutorialPadArea> PadAreas = new List<TutorialPadArea>();
    private static readonly List<Collider> PadColliders = new List<Collider>();
    private static int _padAreasFrame = -1;

    /// <summary>
    /// [서버] 씬의 보스 진입 패드(<see cref="BossEnterTrigger"/> — 서버에만 붙는다) 트리거 콜라이더 범위를 XZ 로 투영한 목록(여러 패드면 전부).
    /// 박스·메시 = bounds 사각형, 구·캡슐 = bounds 중심 원. 패드가 없으면(클라·비튜토리얼·아직 스폰 전) 빈 목록. 프레임당 한 번 다시 읽는다.
    /// </summary>
    public static IReadOnlyList<TutorialPadArea> ServerPadAreas()
    {
        if (_padAreasFrame == Time.frameCount) return PadAreas;
        _padAreasFrame = Time.frameCount;
        PadAreas.Clear();
        foreach (BossEnterTrigger t in FindObjectsByType<BossEnterTrigger>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            t.GetComponents(PadColliders);
            foreach (Collider c in PadColliders)
            {
                if (c == null || !c.enabled || !c.isTrigger) continue;
                Bounds b = c.bounds;
                PadAreas.Add(c is SphereCollider || c is CapsuleCollider
                    ? TutorialPadArea.Circle(b.center.x, b.center.z, Mathf.Max(b.extents.x, b.extents.z))
                    : TutorialPadArea.Box(b.min.x, b.min.z, b.max.x, b.max.z));
            }
        }
        return PadAreas;
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
