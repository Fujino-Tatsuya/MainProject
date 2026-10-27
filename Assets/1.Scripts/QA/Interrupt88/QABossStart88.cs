#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
    /// [서버] 런 전 위치 검사 — 파티 전원(누르는 봇 포함)이 <paramref name="boss"/> 근처(<see cref="QAPartyPlacement88.IsNearTarget"/>)인가.
    /// <paramref name="detail"/> = 플레이어별 <c>clientId:수평거리/높이차(near|far)</c>, 누르는 쪽은 <c>*</c>. 플레이어가 없으면 false.
    /// </summary>
    public static bool IsPartyNear(MonsterBase boss, float radius, long presserClientId, out string detail)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer || boss == null)
        {
            detail = boss == null ? "target=none" : "server=false";
            return false;
        }
        var sb = new System.Text.StringBuilder();
        Vector3 bp = boss.transform.position;
        bool all = true;
        int n = 0;
        foreach (NetworkClient client in nm.ConnectedClientsList)
        {
            if (client.PlayerObject == null)
                continue;
            Vector3 p = client.PlayerObject.transform.position;
            bool near = QAPartyPlacement88.IsNearTarget(p, bp, radius);
            all &= near;
            if (n++ > 0)
                sb.Append(',');
            sb.Append(client.ClientId).Append(presserClientId >= 0 && client.ClientId == (ulong)presserClientId ? "*" : "").Append(':')
              .Append(QAPartyPlacement88.HorizontalDistance(p, bp).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)).Append('/')
              .Append((p.y - bp.y).ToString("F1", System.Globalization.CultureInfo.InvariantCulture))
              .Append(near ? "(near)" : "(far)");
        }
        detail = n == 0 ? "players=none" : "players=" + sb;
        return n > 0 && all;
    }

    /// <summary>
    /// [서버] 런 전 재배치 — 강제 개시 때와 같은 경로(<see cref="MovePartyNear"/>)로 파티를 <paramref name="boss"/> 옆에 다시 놓는다. 옮긴 수.
    /// 중간보스만 구간 격자로 물러날 수 있다(23호 보스방은 링만).
    /// </summary>
    public static int RelocatePartyNear(QAInterruptTargetKind kind, MonsterBase boss, out string method)
    {
        method = "none";
        if (!IsServer || boss == null)
            return 0;
        TutorialDirector d = kind == QAInterruptTargetKind.Midboss ? TutorialDirector.Instance : null;
        return MovePartyNear(d, TutorialArea.Zone4, boss, out method);
    }

    /// <summary>
    /// [서버] 파티를 <paramref name="boss"/> 옆으로 옮긴다. 우선 <see cref="TryFindBossRingPoints"/>(보스와 같은 NavMesh 섬, 벽·소품 겹침 없음),
    /// 못 찾으면 예전 구간 격자(<see cref="TutorialDeathRules.TryFindAreaSpreadPoints"/> — 섬 밖일 수 있다)로 물러난다(<paramref name="d"/> 가 있을 때만). 옮긴 수.
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
            TutorialDeathRules rules = d != null ? d.GetComponent<TutorialDeathRules>() : null;
            if (rules == null || !rules.TryFindAreaSpreadPoints(area, movers.Count, near.x, near.z, Points))
            {
                Debug.LogWarning($"{Tag} {boss.name} 근처 안전 지점 없음 — 파티를 옮기지 않는다(봇이 걸어서 접근).");
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
/// [서버] 할 일 88 23호 런 이상 조건(사용자 결정 ①-a·②-a, 게임 코드 무수정) — <b>23호 런 동안만</b> 켠다. 중간보스엔 쓰지 않는다.
/// <list type="bullet">
/// <item>대상 고정: 콤보 예약 틱(1초)마다 서버 대상이 누르는 클라1 이 아니면 비공개 <c>TwentyThreeBoss.AdoptAggro(Transform, string)</c> 를
///   리플렉션으로 부르고, 콤보 진행 중이면 고정 대상 <c>_comboLock</c> 도 리플렉션으로 누르는 클라1 로 바꾼다(콤보 여부와 무관 — b안,
///   <see cref="QAInterruptLag88Plan.ShouldForceAggro"/>). 결과는 <c>[QA88] event=aggro_force ok=…</c>. 실패해도 런은 계속.</item>
/// <item>넉백 면역: 누르는 클라1 에 QA 출처(<see cref="SourceId"/>) 무기한 <see cref="StatusEffectType.SuperArmor"/> — 넉백 진입점
///   <see cref="Unit.Knockback"/> 이 거부한다. 0.5초마다 빠졌으면 다시 건다(<see cref="QAInterruptLag88Plan.ShouldReapplySuperArmor"/>).
///   🔴 해제: 런 끝·스위트 정리(Close)·종류 바뀜·에디터 감시자 정지 요청(<see cref="ReleaseActive"/>) 모두. 같은 출처만 지우므로 스킬이 건 슈퍼아머는 건드리지 않는다.</item>
/// </list>
/// 간파 영향 확인(2026-10-22): SuperArmor 를 읽는 곳은 넉백 가드·Push/Carry 구속 거부·차단류 CC 적용 거부(<see cref="StatusEffectImmunityPolicy"/>)·
/// 표시(머리 위 상태·상태이상 VFX)뿐 — 간파 판정·간파 슬로모션·<c>[InterruptLag]</c>·간파 시전 조건(<c>PlayerStateController.CanInterrupt</c> = 상태 + BlocksInterrupt)은 읽지 않는다.
/// </summary>
public sealed class QA88TwentyThreeAssist
{
    private const string Tag = "[QA88]";
    /// <summary>QA 슈퍼아머 출처 — 플레이어 NetworkObjectId·스킬 출처와 겹치지 않는 값.</summary>
    public const ulong SourceId = 0x5141_3838_0000_0001UL;

    private static readonly MethodInfo AdoptAggroMethod = typeof(TwentyThreeBoss).GetMethod("AdoptAggro",
        BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(Transform), typeof(string) }, null);
    private static readonly FieldInfo ComboField =
        typeof(TwentyThreeBoss).GetField("_combo", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo ComboLockField =
        typeof(TwentyThreeBoss).GetField("_comboLock", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>지금 켜져 있는 것(감시자가 Play 를 멈추기 전에 해제할 수 있게).</summary>
    public static QA88TwentyThreeAssist Active { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Active = null;

    private readonly TwentyThreeBoss _boss;
    private readonly Player _presser;
    private readonly int _run;
    private bool _on;
    private int _forceOk, _forceFail, _armorApplies;
    private string _lastForceFail = "";

    public int ForceOk => _forceOk;
    public int ForceFail => _forceFail;

    private QA88TwentyThreeAssist(TwentyThreeBoss boss, Player presser, int run)
    {
        _boss = boss;
        _presser = presser;
        _run = run;
    }

    /// <summary>[서버] 켠다. 이전 것이 남아 있으면 먼저 해제한다. 누르는 쪽을 못 찾아도 객체는 돌려준다(진단 표시·경고만).</summary>
    public static QA88TwentyThreeAssist Begin(TwentyThreeBoss boss, ulong presserClientId, bool presserKnown, int run)
    {
        ReleaseActive("replaced");
        Player presser = null;
        NetworkManager nm = NetworkManager.Singleton;
        if (presserKnown && nm != null && nm.IsServer && nm.ConnectedClients.TryGetValue(presserClientId, out NetworkClient client) && client.PlayerObject != null)
            presser = client.PlayerObject.GetComponent<Player>();
        var a = new QA88TwentyThreeAssist(boss, presser, run) { _on = true };
        Active = a;
        Debug.Log($"{Tag} event=assist_begin run={run} presserClientId={(presserKnown ? presserClientId.ToString() : "na")} presser={(presser != null ? "found" : "missing")} " +
                  $"boss={(boss != null ? "found" : "missing")} adoptAggro={(AdoptAggroMethod != null ? "found" : "missing")} comboField={(ComboField != null ? "found" : "missing")} " +
                  $"comboLockField={(ComboLockField != null ? "found" : "missing")}");
        if (presser == null)
            Debug.LogWarning($"{Tag} event=aggro_force ok=false run={run} reason=presser_missing — 대상 고정·넉백 면역 없이 런 계속");
        a.RefreshSuperArmor();
        return a;
    }

    /// <summary>켜져 있는 것을 해제한다(없으면 무시). 에디터 감시자·스위트 정리에서 부른다.</summary>
    public static void ReleaseActive(string reason)
    {
        QA88TwentyThreeAssist a = Active;
        Active = null;
        a?.End(reason);
    }

    /// <summary>[서버] 해제 — QA 출처 슈퍼아머를 지운다. 여러 번 불러도 된다.</summary>
    public void End(string reason)
    {
        if (!_on)
            return;
        _on = false;
        if (Active == this)
            Active = null;
        bool removed = false;
        try
        {
            StatusEffectController status = _presser != null ? _presser.StatusEffects : null;
            removed = status != null && status.Remove(StatusEffectType.SuperArmor, SourceId);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"{Tag} 슈퍼아머 해제 중 오류: {e.Message}");
        }
        Debug.Log($"{Tag} event=assist_end run={_run} reason={QAInterruptLag88Plan.LogToken(reason)} superArmorRemoved={(removed ? "true" : "false")} " +
                  $"aggroForceOk={_forceOk} aggroForceFail={_forceFail} armorApplies={_armorApplies}");
    }

    /// <summary>[서버] 0.5초마다 — QA 슈퍼아머가 빠졌으면 다시 건다.</summary>
    public void RefreshSuperArmor()
    {
        StatusEffectController status = _presser != null ? _presser.StatusEffects : null;
        if (status == null || !QAInterruptLag88Plan.ShouldReapplySuperArmor(_on, status.GetStackCount(StatusEffectType.SuperArmor, SourceId)))
            return;
        status.Apply(StatusEffectType.SuperArmor, 0f, SourceId);
        if (status.GetStackCount(StatusEffectType.SuperArmor, SourceId) > 0)
        {
            _armorApplies++;
            Debug.Log($"{Tag} event=superarmor_apply run={_run} n={_armorApplies} clientId={_presser.OwnerClientId}");
        }
    }

    /// <summary>
    /// [서버] 콤보 예약 직전(1초 틱) — 서버 대상이 누르는 쪽이 아니면 AdoptAggro 로 승계시키고, 콤보 진행 중이면 고정 대상(<c>_comboLock</c>)도
    /// 누르는 쪽으로 바꾼다(b안 — AdoptAggro 는 이미 같은 대상이면 아무것도 안 해 고정 대상이 남는다). 콤보 밖이면 고정 대상은 건드리지 않는다.
    /// </summary>
    public void ForceAggroBeforeReserve()
    {
        if (!_on || _boss == null || _presser == null)
            return;
        if (ComboField == null)
        {
            FailForce("combo_field_missing");
            return;
        }
        if (ComboLockField == null)
        {
            FailForce("comboLock_field_missing");
            return;
        }
        Transform presser = _presser.transform;
        bool comboActive = ComboField.GetValue(_boss) != null;
        var lockBefore = ComboLockField.GetValue(_boss) as Transform;
        bool targetIsPresser = _boss.CurrentTarget == presser;
        if (!QAInterruptLag88Plan.ShouldForceAggro(comboActive, targetIsPresser, lockBefore == presser))
            return;

        bool adopted = false;
        if (!targetIsPresser)
        {
            if (AdoptAggroMethod == null)
            {
                FailForce("method_missing");
                return;
            }
            try
            {
                AdoptAggroMethod.Invoke(_boss, new object[] { presser, "QA88" });
            }
            catch (System.Exception e)
            {
                FailForce("invoke_" + (e.InnerException ?? e).GetType().Name);
                return;
            }
            if (_boss.CurrentTarget != presser)
            {
                FailForce("not_adopted");   // AdoptTarget 이 거부(대상 무효 등)
                return;
            }
            adopted = true;
        }

        // AdoptAggro 가 콤보 중이면 이미 옮겼을 수 있다 — 다시 읽고 남았을 때만 바꾼다.
        bool lockSwapped = false;
        if (QAInterruptLag88Plan.ShouldSwapComboLock(comboActive, ComboLockField.GetValue(_boss) as Transform == presser))
        {
            ComboLockField.SetValue(_boss, presser);
            lockSwapped = true;
        }
        var lockAfter = ComboLockField.GetValue(_boss) as Transform;

        _forceOk++;
        Debug.Log($"{Tag} event=aggro_force ok=true run={_run} n={_forceOk} clientId={_presser.OwnerClientId} presserObjId={_presser.NetworkObjectId} aggroId={_boss.AggroTargetId} " +
                  $"adopted={(adopted ? "true" : "false")} comboActive={(comboActive ? "true" : "false")} " +
                  $"comboLockBefore={ObjId(lockBefore)} comboLockAfter={ObjId(lockAfter)} comboLockSwapped={(lockSwapped ? "true" : "false")}");
    }

    /// <summary>로그용 — 그 Transform 의 NetworkObjectId, null 이면 none.</summary>
    private static string ObjId(Transform t)
    {
        if (t == null)
            return "none";
        NetworkObject no = t.GetComponentInParent<NetworkObject>();
        return no != null && no.IsSpawned ? no.NetworkObjectId.ToString() : "unspawned";
    }

    private void FailForce(string reason)
    {
        _forceFail++;
        // 같은 사유가 1초마다 반복되면 첫 번과 10번째마다만 남긴다.
        if (reason != _lastForceFail || _forceFail % 10 == 0)
            Debug.LogWarning($"{Tag} event=aggro_force ok=false run={_run} reason={reason} fails={_forceFail}");
        _lastForceFail = reason;
    }

    /// <summary>
    /// boss_diag·boss_window 용 — <c>aggroForced=ok/fail presserIsTarget=… comboLockIsPresser=true|false|none|na superArmor=true|false|na</c>.
    /// <c>comboLockIsPresser</c>: none = 고정 대상 없음(콤보 밖·첫 타 전), na = 필드를 못 읽음.
    /// </summary>
    public string Diag()
    {
        string armor = _presser == null || _presser.StatusEffects == null ? "na"
            : _presser.StatusEffects.GetStackCount(StatusEffectType.SuperArmor, SourceId) > 0 ? "true" : "false";
        string comboLock = "na";
        if (ComboLockField != null && _boss != null)
        {
            var t = ComboLockField.GetValue(_boss) as Transform;
            comboLock = t == null ? "none" : _presser != null && t == _presser.transform ? "true" : "false";
        }
        return $"aggroForced={_forceOk}/{_forceFail} presserIsTarget={(_presser != null && _boss != null && _boss.CurrentTarget == _presser.transform ? "true" : "false")} " +
               $"comboLockIsPresser={comboLock} superArmor={armor}";
    }
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
            // 어그로 3종 — 복제값(aggroId, 클라 isTarget 의 원본)·서버 _target(serverTargetId)·23호 이상 조건(presserIsTarget) — 클라 isTarget 과 창 단위로 대조한다.
            Debug.Log($"{Tag} event=boss_window state={(open ? "open" : "closed")} n={_windows} bossState={state} aggroId={_target.AggroTargetId} " +
                      $"serverTargetId={ServerTargetId()} {AssistDiag()} {Players()}");
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
                  $"awakeNormals={AwakeNormals(kind)} {AssistDiag()} {Players()}");
    }

    /// <summary>서버 쪽 현재 대상(<see cref="MonsterBase.CurrentTarget"/>)의 NetworkObjectId. 없으면 0.</summary>
    private string ServerTargetId()
    {
        Transform t = _target != null ? _target.CurrentTarget : null;
        NetworkObject no = t != null ? t.GetComponentInParent<NetworkObject>() : null;
        return no != null && no.IsSpawned ? no.NetworkObjectId.ToString() : "0";
    }

    /// <summary>23호 이상 조건 상태(<see cref="QA88TwentyThreeAssist"/>) — 꺼져 있으면(중간보스·런 밖) off.</summary>
    private static string AssistDiag() =>
        QA88TwentyThreeAssist.Active != null ? QA88TwentyThreeAssist.Active.Diag() : "aggroForced=off superArmor=off";

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
