#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>할 일 88 강제 개시 결과(코루틴 밖으로 넘긴다).</summary>
public sealed class QABossStart88Result
{
    public bool Ok;
    public string Detail = "";
    /// <summary>간파 대상(중간보스 MonsterBase 또는 TwentyThreeBoss). 실패면 null.</summary>
    public MonsterBase Target;
}

/// <summary>
/// 할 일 88 — 중간보스·23호 강제 개시 QA 훅(<b>호스트(서버)에서만</b>). 새 게임 API 없이 기존 디버그 경로만 쓴다:
/// <list type="bullet">
/// <item>중간보스: 구간 스킵 존4(<see cref="TutorialDirector.RequestSkip"/> = Alt+6) → G8 대기(없으면 <see cref="TutorialDebugActions.OpenGatesThrough"/>)
///   → G8 지연 기상(3초)이 지난 뒤 존4 일반몹 휴면 → 중간보스 기상(<c>Tools/Tutorial/Debug/존4 중간보스 기상</c> 과 같은 <see cref="TutorialDirector.WakeZoneServer"/> KeyOnly)
///   → 파티를 중간보스 바로 옆으로(<see cref="TryFindBossRingPoints"/> — 보스와 같은 NavMesh 섬, 다리 개통 전에도 + <see cref="PlayerFallRecovery.RelocateServer"/>).</item>
/// <item>23호: P572·M7 과 같은 G10 개방 → <see cref="BossTeleportManager.ForceStartEncounter"/> → Combat. 간파 창은
///   <see cref="ReserveJumpComboIfIdle"/>(F3 = <c>DevReserveJumpCombo</c>, 3번째 착지 창)로 앞당긴다.</item>
/// </list>
/// 판이 끝나지 않게 <see cref="KeepAliveServer"/> 로 파티·대상 체력을 채운다(<see cref="Unit.SetCurrentHealthOnServer"/> — 통계·피격 연출·사망 신호 없음, P572 와 같은 경로).
/// </summary>
public static class QABossStart88
{
    private const string Tag = "[QA88]";
    private const float ReadyWaitSeconds = 30f;
    private const float SkipWaitSeconds = 60f;
    private const float G8WaitSeconds = 20f;
    private const float CombatWaitSeconds = 150f;
    /// <summary>G8 → 일반몹 지연 기상(<see cref="TutorialZone4Model.DefaultWakeDelaySeconds"/>)이 지날 때까지 + 여유.</summary>
    public const float NormalWakeWaitSeconds = TutorialZone4Model.DefaultWakeDelaySeconds + 0.5f;

    // 파티 지점 검사 — 서 있을 자리에 벽·소품·배리어·몬스터가 겹치면 버린다(스모크 4차 P8 벽 관통 'Wall_00'·'PF_Prop_SM_Prop_Ship…').
    private static readonly int StandBlockMask = LayerMask.GetMask("Default", "Wall", "Env", "Barrier", "Enemy");
    private static readonly int FloorMask = LayerMask.GetMask("Default", "Ground", "Env");
    private const float StandRadius = 0.45f;
    private const float StandHeight = 1.6f;
    private const float FloorMaxOffset = 0.5f;
    private static readonly List<Vector3> Candidates = new List<Vector3>();
    private static readonly List<Vector3> Accepted = new List<Vector3>();

    private static readonly List<MonsterBase> Buffer = new List<MonsterBase>();
    private static readonly List<Vector3> Points = new List<Vector3>();

    private static bool IsServer => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

    private static BossEncounterPhase Phase =>
        BossEncounterDirector.Instance != null ? BossEncounterDirector.Instance.Phase : BossEncounterPhase.Idle;

    /// <summary>로딩 흐름(첫 진입·판 재시작) 진행 중 — 구간 스킵 컨트롤러가 RestartInProgress 판정에 쓰는 것과 같은 원본.</summary>
    private static bool IsLoadingFlowInProgress
    {
        get
        {
            NetworkLoadingFlowController flow = NetworkManager.Singleton != null ? NetworkManager.Singleton.GetComponent<NetworkLoadingFlowController>() : null;
            return flow != null && flow.IsFlowInProgress;
        }
    }

    private static bool CanRequestZone4SkipNow(TutorialDirector d) =>
        CanRequestZone4Skip(d.Step, d.GetComponent<TutorialSkipController>() != null, IsLoadingFlowInProgress);

    /// <summary>
    /// 존4 구간 스킵을 지금 보내면 받아들여지는가 — 스킵 컨트롤러가 붙었고 <see cref="TutorialSkipPlan.Check"/> 가 Ok.
    /// 로딩 흐름 진행 중이면 컨트롤러가 RestartInProgress 로 거부하고 다시 시도하지 않는다.
    /// </summary>
    public static bool CanRequestZone4Skip(TutorialStep step, bool hasSkipController, bool loadingFlowInProgress) =>
        hasSkipController && TutorialSkipPlan.Check(step, TutorialSkipTarget.Zone4, loadingFlowInProgress) == TutorialSkipVerdict.Ok;

    public static IEnumerator Start(QAInterruptTargetKind kind, QABossStart88Result result) =>
        kind == QAInterruptTargetKind.Midboss ? StartMidboss(result) : StartTwentyThree(result);

    /// <summary>[서버] 존4 중간보스 강제 개시. 맵(튜토리얼 활성)에 파티가 있어야 한다.</summary>
    public static IEnumerator StartMidboss(QABossStart88Result result)
    {
        result.Ok = false;
        result.Target = null;
        TutorialDirector d = TutorialDirector.Instance;
        if (!IsServer || d == null || !d.IsTutorialActive)
        {
            result.Detail = $"서버·튜토리얼 활성 아님(server={IsServer}, director={(d != null)})";
            yield break;
        }

        // 1) 존4 로 구간 스킵(이미 존4 이상이면 보내지 않고 그대로 진행).
        //    맵 첫 진입 로딩 흐름(로딩 씬 언로드·최소 표시 시간)이 끝나기 전 요청은 RestartInProgress 로 한 번 거부되고 끝난다
        //    (스모크 3차 88-20261022_143717) — 스킵 컨트롤러가 받아들일 때까지 기다렸다 한 번만 보낸다.
        if (d.Step < TutorialStep.Zone4Lore)
        {
            float readyUntil = Time.realtimeSinceStartup + ReadyWaitSeconds;
            while (d != null && d.Step < TutorialStep.Zone4Lore && !CanRequestZone4SkipNow(d) && Time.realtimeSinceStartup < readyUntil)
                yield return null;
            if (d == null || (d.Step < TutorialStep.Zone4Lore && !CanRequestZone4SkipNow(d)))
            {
                result.Detail = $"존4 스킵 {ReadyWaitSeconds:0}초 안에 보낼 수 없음(단계={(d != null ? d.Step.ToString() : "없음")}, " +
                                $"스킵 컨트롤러={(d != null && d.GetComponent<TutorialSkipController>() != null)}, 로딩 흐름 진행 중={IsLoadingFlowInProgress})";
                yield break;
            }
        }
        if (d.Step < TutorialStep.Zone4Lore)
        {
            d.RequestSkip(TutorialSkipTarget.Zone4);
            float until = Time.realtimeSinceStartup + SkipWaitSeconds;
            while (d != null && d.Step < TutorialStep.Zone4Lore && Time.realtimeSinceStartup < until)
                yield return null;
            if (d == null || d.Step < TutorialStep.Zone4Lore)
            {
                result.Detail = $"존4 스킵 {SkipWaitSeconds:0}초 안에 안 됨(단계={(d != null ? d.Step.ToString() : "없음")})";
                yield break;
            }
        }

        // 2) G8(설정 자막 끝 = 존4 전투 시작)을 기다렸다가 일반몹을 다시 재운다 — G8 이 일반몹을 깨워 간파 측정에 끼어든다.
        //    🔴 G8 은 일반몹을 바로 깨우지 않고 TutorialZone4Flow 가 **3초 뒤** 깨운다. G8 직후에 재우면 아직 휴면이라 0마리를 재우고
        //    3초 뒤 30마리가 깨어 런 내내 파티를 때렸다(스모크 4차 88-20261022_145020 — slept=0 → 14:50:39 NormalOnly 기상 30마리).
        //    → 지연 기상이 지난 뒤에 재운다.
        float g8Until = Time.realtimeSinceStartup + G8WaitSeconds;
        while (!d.IsGateOpen(TutorialGate.G8_TimerStart) && Time.realtimeSinceStartup < g8Until)
            yield return null;
        if (!d.IsGateOpen(TutorialGate.G8_TimerStart))
            TutorialDebugActions.OpenGatesThrough(d, TutorialGate.G8_TimerStart);
        yield return new WaitForSecondsRealtime(NormalWakeWaitSeconds);
        int slept = d.SetZoneDormantServer(TutorialArea.Zone4, true, TutorialMonsterFilter.NormalOnly);
        Debug.Log($"{Tag} event=normals_slept kind=midboss slept={slept} waitedS={NormalWakeWaitSeconds:0.0}");

        // 3) 중간보스 기상.
        d.WakeZoneServer(TutorialArea.Zone4, TutorialMonsterFilter.KeyOnly);
        d.GetZoneMonstersServer(TutorialArea.Zone4, TutorialMonsterFilter.KeyOnly, Buffer);
        MonsterBase boss = null;
        foreach (MonsterBase m in Buffer)
            if (m != null && m.GetComponent<MonsterCounterWindow>() != null) { boss = m; break; }
        Buffer.Clear();
        if (boss == null)
        {
            result.Detail = "존4 중간보스(MonsterCounterWindow 소유자) 없음 — 이미 처치됐거나 장부 없음";
            yield break;
        }

        // 4) 파티를 중간보스 바로 옆(보스 NavMesh 섬 위)으로.
        int moved = MovePartyNear(d, TutorialArea.Zone4, boss, out string method);
        result.Ok = true;
        result.Target = boss;
        result.Detail = $"중간보스 {boss.name} 기상 · 일반몹 휴면 {slept} · 이동 {moved}명({method})";
        Debug.Log($"{Tag} event=start kind=midboss target={QAInterruptLag88Plan.LogToken(boss.name)} slept={slept} moved={moved} method={method}");
    }

    /// <summary>[서버] 23호 보스전 강제 개시 → Combat.</summary>
    public static IEnumerator StartTwentyThree(QABossStart88Result result)
    {
        result.Ok = false;
        result.Target = null;
        if (!IsServer)
        {
            result.Detail = "서버 아님";
            yield break;
        }

        // 23호 경로(게이트 강제 개방·ForceStartEncounter)엔 로딩 흐름 거부가 없지만, 첫 런이면 맵 진입 로딩 흐름이 아직 끝나지 않았을 수 있다 —
        // 로딩 씬 언로드 도중 패드 이동이 겹치지 않게 같은 조건으로 기다린다.
        float readyUntil = Time.realtimeSinceStartup + ReadyWaitSeconds;
        while (IsLoadingFlowInProgress && Time.realtimeSinceStartup < readyUntil)
            yield return null;
        if (IsLoadingFlowInProgress)
        {
            result.Detail = $"맵 진입 로딩 흐름이 {ReadyWaitSeconds:0}초 안에 안 끝남";
            yield break;
        }

        TutorialDirector tutorial = TutorialDirector.Instance;
        if (tutorial != null && tutorial.IsTutorialActive && !tutorial.IsGateOpen(TutorialGate.G10_BossEnterAndPad))
            TutorialDebugActions.OpenGatesThrough(tutorial, TutorialGate.G10_BossEnterAndPad);

        BossTeleportManager tp = BossTeleportManager.Instance;
        float deadline = Time.realtimeSinceStartup + 30f;
        while (tp != null && Phase != BossEncounterPhase.Combat && !tp.ForceStartEncounter(0f) && Time.realtimeSinceStartup < deadline)
            yield return new WaitForSecondsRealtime(1f);
        float combatUntil = Time.realtimeSinceStartup + CombatWaitSeconds;
        while (Phase != BossEncounterPhase.Combat && Time.realtimeSinceStartup < combatUntil)
            yield return null;
        if (Phase != BossEncounterPhase.Combat)
        {
            result.Detail = $"보스전 Combat 안 됨(단계={Phase}, BossTeleportManager={(tp != null)})";
            yield break;
        }

        TwentyThreeBoss boss = Object.FindAnyObjectByType<TwentyThreeBoss>();
        if (boss == null)
        {
            result.Detail = "Combat 인데 TwentyThreeBoss 없음";
            yield break;
        }
        result.Ok = true;
        result.Target = boss;
        result.Detail = $"23호 Combat · 점프 콤보 예약={ReserveJumpComboIfIdle(boss)}";
        Debug.Log($"{Tag} event=start kind=23 target={QAInterruptLag88Plan.LogToken(boss.name)}");
    }

    /// <summary>[서버] 23호 점프×3 콤보(3번째 착지 간파 창)가 예약돼 있지 않으면 예약한다(F3 과 같은 Dev API). 예약했거나 이미 예약이면 true.</summary>
    public static bool ReserveJumpComboIfIdle(TwentyThreeBoss boss)
    {
        if (boss == null || !IsServer)
            return false;
        return boss.DevJumpComboReserved || boss.DevReserveJumpCombo();
    }

    /// <summary>
    /// [서버] 파티 전원과 <paramref name="target"/> 이 <paramref name="threshold"/>(최대 체력 비율) 아래면 최대 체력으로 채운다.
    /// 살아 있는 대상만 — 이미 죽은(체력 0) 유닛은 되살리지 않는다. 채운 수.
    /// </summary>
    public static int KeepAliveServer(Unit target, float threshold = 0.7f)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer)
            return 0;
        int healed = 0;
        foreach (NetworkClient client in nm.ConnectedClientsList)
        {
            Player p = client.PlayerObject != null ? client.PlayerObject.GetComponent<Player>() : null;
            if (p == null)
                continue;
            PlayerLifeCycleController life = p.GetComponent<PlayerLifeCycleController>();
            if (life != null && life.State != PlayerLifeState.Alive)
                continue;
            if (Fill(p, threshold))
                healed++;
        }
        if (target != null && Fill(target, threshold))
            healed++;
        return healed;
    }

    private static bool Fill(Unit u, float threshold)
    {
        if (u.CurrentHealth <= 0 || u.CurrentHealth >= u.MaxHp * threshold)
            return false;
        u.SetCurrentHealthOnServer(u.MaxHp);
        return true;
    }

    /// <summary>
    /// [서버] 파티를 <paramref name="boss"/> 옆으로 옮긴다. 우선 <see cref="TryFindBossRingPoints"/>(보스와 같은 NavMesh 섬, 벽·소품 겹침 없음),
    /// 못 찾으면 예전 구간 격자(<see cref="TutorialDeathRules.TryFindAreaSpreadPoints"/> — 섬 밖일 수 있다)로 물러난다. 옮긴 수.
    /// </summary>
    private static int MovePartyNear(TutorialDirector d, TutorialArea area, MonsterBase boss, out string method)
    {
        method = "none";
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null)
            return 0;
        var movers = new List<PlayerFallRecovery>();
        foreach (NetworkClient client in nm.ConnectedClientsList)
        {
            PlayerFallRecovery r = client.PlayerObject != null ? client.PlayerObject.GetComponent<PlayerFallRecovery>() : null;
            if (r != null)
                movers.Add(r);
        }
        if (movers.Count == 0)
            return 0;

        Vector3 near = boss.transform.position;
        bool found = TryFindBossRingPoints(boss, movers.Count, Points, out string ringDetail);
        Debug.Log($"{Tag} event=party_points method=navring ok={(found ? "true" : "false")} need={movers.Count} detail={QAInterruptLag88Plan.LogToken(ringDetail)}");
        if (found)
        {
            method = "navring";
        }
        else
        {
            TutorialDeathRules rules = d.GetComponent<TutorialDeathRules>();
            if (rules == null || !rules.TryFindAreaSpreadPoints(area, movers.Count, near.x, near.z, Points))
            {
                Debug.LogWarning($"{Tag} {area} 중간보스 근처 안전 지점 없음 — 파티를 옮기지 않는다(봇이 걸어서 접근).");
                return 0;
            }
            method = "area";
            Debug.LogWarning($"{Tag} 보스 NavMesh 링 지점 부족({ringDetail}) — 구간 격자로 물러남(보스 섬 밖일 수 있음).");
        }

        int moved = 0;
        for (int i = 0; i < movers.Count && i < Points.Count; i++)
        {
            bool ok = movers[i].RelocateServer(Points[i]);
            if (ok)
                moved++;
            Debug.Log($"{Tag} event=party_point client={movers[i].OwnerClientId} method={method} ok={(ok ? "true" : "false")} " +
                      $"pos={Fmt(Points[i])} bossPos={Fmt(near)} dist={QAPartyPlacement88.HorizontalDistance(Points[i], near):F1}");
        }
        return moved;
    }

    /// <summary>
    /// [서버] <paramref name="boss"/> 의 NavMesh 위치 둘레 링(<see cref="QAPartyPlacement88.RingCandidates"/>)에서
    /// NavMesh 위 · 보스→지점 경로 완결(같은 섬 — 다리 개통 전 중앙 섬 밖은 끊긴다)·우회 아님 · 바닥 있음 · 서 있을 캡슐에 벽/소품/배리어/몬스터 겹침 없음
    /// 을 통과한 지점에서 서로 떨어진 <paramref name="count"/> 개를 고른다. 다 못 고르면 false. <paramref name="detail"/> = 거른 사유별 수.
    /// </summary>
    public static bool TryFindBossRingPoints(MonsterBase boss, int count, List<Vector3> into, out string detail)
    {
        into.Clear();
        if (boss == null || !NavMesh.SamplePosition(boss.transform.position, out NavMeshHit center, 2f, NavMesh.AllAreas))
        {
            detail = "보스 발밑 2m 안에 NavMesh 없음";
            return false;
        }

        QAPartyPlacement88.RingCandidates(center.position, QAPartyPlacement88.DefaultRadii, QAPartyPlacement88.DefaultSteps, Candidates);
        Accepted.Clear();
        int noMesh = 0, noPath = 0, detour = 0, noFloor = 0, blocked = 0;
        var path = new NavMeshPath();
        foreach (Vector3 cand in Candidates)
        {
            if (!NavMesh.SamplePosition(cand, out NavMeshHit hit, 0.75f, NavMesh.AllAreas))
            {
                noMesh++;
                continue;
            }
            if (!NavMesh.CalculatePath(center.position, hit.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
            {
                noPath++;
                continue;
            }
            if (!QAPartyPlacement88.IsDirectPath(PathLength(path), QAPartyPlacement88.HorizontalDistance(center.position, hit.position)))
            {
                detour++;
                continue;
            }
            if (!Physics.Raycast(hit.position + Vector3.up * 1.5f, Vector3.down, out RaycastHit floor, 3f, FloorMask, QueryTriggerInteraction.Ignore)
                || Mathf.Abs(floor.point.y - hit.position.y) > FloorMaxOffset)
            {
                noFloor++;
                continue;
            }
            Vector3 p = floor.point;
            if (Physics.CheckCapsule(p + Vector3.up * (StandRadius + 0.15f), p + Vector3.up * StandHeight, StandRadius, StandBlockMask, QueryTriggerInteraction.Ignore))
            {
                blocked++;
                continue;
            }
            Accepted.Add(p);
        }
        int n = QAPartyPlacement88.SelectSpread(Accepted, count, QAPartyPlacement88.DefaultSpacing, into);
        detail = $"후보 {Candidates.Count} · 통과 {Accepted.Count} · 선택 {n}/{count} " +
                 $"(NavMesh 없음 {noMesh} · 경로 끊김 {noPath} · 우회 {detour} · 바닥 없음 {noFloor} · 겹침 {blocked})";
        return n >= count;
    }

    private static float PathLength(NavMeshPath path)
    {
        Vector3[] corners = path.corners;
        float len = 0f;
        for (int i = 1; i < corners.Length; i++)
            len += Vector3.Distance(corners[i - 1], corners[i]);
        return len;
    }

    internal static string Fmt(Vector3 v) => $"{v.x:F2}/{v.y:F2}/{v.z:F2}";
}

/// <summary>
/// [서버] 할 일 88 런 진단 — 대상 보스의 공격 진입·간파 창 열림/닫힘 엣지(매 호출)와 <see cref="SummarySeconds"/> 마다 요약을 <c>[QA88]</c> 줄로 남긴다.
/// 스모크 4차는 "창 0" 만 남아 보스가 안 때린 건지 클라가 못 본 건지 가를 수 없었다 — 호스트 <c>boss_window</c> 와 클론 <c>bot_diag</c> 를 나란히 보면 갈린다.
/// </summary>
public sealed class QABossWatch88
{
    private const string Tag = "[QA88]";
    public const float SummarySeconds = 5f;

    private static readonly List<MonsterBase> Normals = new List<MonsterBase>();
    private readonly System.Text.StringBuilder _sb = new System.Text.StringBuilder();

    private MonsterBase _target;
    private MonsterCounterWindow _window;
    private NavMeshAgent _agent;
    private MonsterState _lastState;
    private bool _wasOpen;
    private int _attacks, _windows;
    private double _lastWindowAt = -1.0;
    private float _nextSummary;

    public void Bind(MonsterBase target)
    {
        _target = target;
        _window = target != null ? target.GetComponent<MonsterCounterWindow>() : null;
        _agent = target != null ? target.GetComponent<NavMeshAgent>() : null;
        _lastState = target != null ? target.State : default;
        _wasOpen = false;
        _attacks = _windows = 0;
        _lastWindowAt = -1.0;
        _nextSummary = 0f;
    }

    public void Tick(QAInterruptTargetKind kind)
    {
        if (_target == null)
            return;
        MonsterState state = _target.State;
        if (state != _lastState)
        {
            if (state == MonsterState.Attack)
            {
                _attacks++;
                Debug.Log($"{Tag} event=boss_attack n={_attacks} aggroId={_target.AggroTargetId} {Players()}");
            }
            _lastState = state;
        }

        bool open = _target is TwentyThreeBoss boss ? boss.CounterWindowOpen : _window != null && _window.IsOpen;
        if (open != _wasOpen)
        {
            if (open)
            {
                _windows++;
                _lastWindowAt = Time.realtimeSinceStartupAsDouble;
            }
            Debug.Log($"{Tag} event=boss_window state={(open ? "open" : "closed")} n={_windows} {Players()}");
            _wasOpen = open;
        }

        if (Time.realtimeSinceStartup < _nextSummary)
            return;
        _nextSummary = Time.realtimeSinceStartup + SummarySeconds;
        string lastWindow = _lastWindowAt < 0.0 ? "na" : (Time.realtimeSinceStartupAsDouble - _lastWindowAt).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        Debug.Log($"{Tag} event=boss_diag kind={QAInterruptLag88Plan.KindToken(kind)} target={QAInterruptLag88Plan.LogToken(_target.name)} state={state} " +
                  $"dormant={(_target.IsDormant ? "true" : "false")} hp={_target.CurrentHealth}/{_target.MaxHp} " +
                  $"onNavMesh={(_agent != null && _agent.isOnNavMesh ? "true" : "false")} pos={QABossStart88.Fmt(_target.transform.position)} " +
                  $"aggroId={_target.AggroTargetId} attacks={_attacks} windows={_windows} lastWindowAgoS={lastWindow} " +
                  $"awakeNormals={AwakeNormals(kind)} {Players()}");
    }

    /// <summary>존4 일반몹 중 깨어 있는 수(중간보스 런만, 아니면 na) — 휴면이 풀려 측정에 끼어드는지 본다.</summary>
    private static string AwakeNormals(QAInterruptTargetKind kind)
    {
        TutorialDirector d = TutorialDirector.Instance;
        if (kind != QAInterruptTargetKind.Midboss || d == null || !d.IsTutorialActive)
            return "na";
        d.GetZoneMonstersServer(TutorialArea.Zone4, TutorialMonsterFilter.NormalOnly, Normals);
        int awake = 0;
        foreach (MonsterBase m in Normals)
            if (m != null && m.CurrentHealth > 0 && !m.IsDormant)
                awake++;
        Normals.Clear();
        return awake.ToString();
    }

    /// <summary>플레이어별 보스까지 수평 거리 <c>players=0:3.1,1:4.2</c>.</summary>
    private string Players()
    {
        _sb.Clear();
        _sb.Append("players=");
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || _target == null)
            return _sb.Append("none").ToString();
        bool first = true;
        foreach (NetworkClient client in nm.ConnectedClientsList)
        {
            if (client.PlayerObject == null)
                continue;
            if (!first)
                _sb.Append(',');
            first = false;
            _sb.Append(client.ClientId).Append(':')
               .Append(QAPartyPlacement88.HorizontalDistance(client.PlayerObject.transform.position, _target.transform.position)
                   .ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
        }
        if (first)
            _sb.Append("none");
        return _sb.ToString();
    }
}
#endif
