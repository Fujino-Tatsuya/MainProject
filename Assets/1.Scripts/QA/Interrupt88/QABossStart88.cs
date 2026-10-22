#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

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
///   → 존4 일반몹 휴면 → 중간보스 기상(<c>Tools/Tutorial/Debug/존4 중간보스 기상</c> 과 같은 <see cref="TutorialDirector.WakeZoneServer"/> KeyOnly)
///   → 파티를 중간보스 가까운 안전 지점으로(구간 스킵과 같은 <see cref="TutorialDeathRules.TryFindAreaSpreadPoints"/> + <see cref="PlayerFallRecovery.RelocateServer"/>).</item>
/// <item>23호: P572·M7 과 같은 G10 개방 → <see cref="BossTeleportManager.ForceStartEncounter"/> → Combat. 간파 창은
///   <see cref="ReserveJumpComboIfIdle"/>(F3 = <c>DevReserveJumpCombo</c>, 3번째 착지 창)로 앞당긴다.</item>
/// </list>
/// 판이 끝나지 않게 <see cref="KeepAliveServer"/> 로 파티·대상 체력을 채운다(<see cref="Unit.SetCurrentHealthOnServer"/> — 통계·피격 연출·사망 신호 없음, P572 와 같은 경로).
/// </summary>
public static class QABossStart88
{
    private const string Tag = "[QA88]";
    private const float SkipWaitSeconds = 60f;
    private const float G8WaitSeconds = 20f;
    private const float CombatWaitSeconds = 150f;

    private static readonly List<MonsterBase> Buffer = new List<MonsterBase>();
    private static readonly List<Vector3> Points = new List<Vector3>();

    private static bool IsServer => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

    private static BossEncounterPhase Phase =>
        BossEncounterDirector.Instance != null ? BossEncounterDirector.Instance.Phase : BossEncounterPhase.Idle;

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

        // 1) 존4 로 구간 스킵(이미 존4 이상이면 Backward/Finished 로 거부되고 그대로 진행).
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
        float g8Until = Time.realtimeSinceStartup + G8WaitSeconds;
        while (!d.IsGateOpen(TutorialGate.G8_TimerStart) && Time.realtimeSinceStartup < g8Until)
            yield return null;
        if (!d.IsGateOpen(TutorialGate.G8_TimerStart))
            TutorialDebugActions.OpenGatesThrough(d, TutorialGate.G8_TimerStart);
        yield return null;
        int slept = d.SetZoneDormantServer(TutorialArea.Zone4, true, TutorialMonsterFilter.NormalOnly);

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

        // 4) 파티를 중간보스 가까이로.
        int moved = MovePartyNear(d, TutorialArea.Zone4, boss.transform.position);
        result.Ok = true;
        result.Target = boss;
        result.Detail = $"중간보스 {boss.name} 기상 · 일반몹 휴면 {slept} · 이동 {moved}명";
        Debug.Log($"{Tag} event=start kind=midboss target={QAInterruptLag88Plan.LogToken(boss.name)} slept={slept} moved={moved}");
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

    private static int MovePartyNear(TutorialDirector d, TutorialArea area, Vector3 near)
    {
        NetworkManager nm = NetworkManager.Singleton;
        TutorialDeathRules rules = d.GetComponent<TutorialDeathRules>();
        if (nm == null || rules == null)
            return 0;
        var movers = new List<PlayerFallRecovery>();
        foreach (NetworkClient client in nm.ConnectedClientsList)
        {
            PlayerFallRecovery r = client.PlayerObject != null ? client.PlayerObject.GetComponent<PlayerFallRecovery>() : null;
            if (r != null)
                movers.Add(r);
        }
        if (movers.Count == 0 || !rules.TryFindAreaSpreadPoints(area, movers.Count, near.x, near.z, Points))
        {
            Debug.LogWarning($"{Tag} {area} 중간보스 근처 안전 지점 없음 — 파티를 옮기지 않는다(봇이 걸어서 접근).");
            return 0;
        }
        int moved = 0;
        for (int i = 0; i < movers.Count && i < Points.Count; i++)
            if (movers[i].RelocateServer(Points[i]))
                moved++;
        return moved;
    }
}
#endif
