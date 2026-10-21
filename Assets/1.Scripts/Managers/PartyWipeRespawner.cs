using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 전멸 → 리스폰(할 일 30 (2)) — <b>서버 전용</b>. <see cref="PartyWipeWatcher"/> 가 전멸을 확정하면 결과 화면 대신 여기를 먼저 부른다.
/// 규칙은 순수 <see cref="PartyWipeRespawnRules"/>, 이 클래스는 기존 서버 경로를 정해진 순서로 부른다.
/// 원본: Docs/tech/party-lives-respawn.md §3 · 보스 훅 Docs/tech/handoff-boss-encounter-reset.md.
///
/// 순서(§3.1):
///  0) 리스폰 가능 판정(보스 미격파 + 튜토리얼 G10 열림 / 절차 맵 보스 진입 패드 있음)과 지점 탐색 — 실패하면 아무것도 안 바꾸고 false(결과 화면).
///  1) 보스 리셋 — <see cref="BossEncounterDirector.ResetForRetryServer"/>(디스폰·Idle) · <see cref="BossTeleportManager.ResetForRetryServer"/> ·
///     <see cref="BossTimerManager.StopForRetryServer"/>(재가동 안 함).
///  2) 튜토리얼 — 개인 뒤 배리어 마스크(최전방 = 복도4)를 <b>먼저</b> 쓰고 되감기(Finished → BossEnter, G11 닫기) · 사망 규칙 재개.
///  3) 공용 목숨 재충전 → 4) 전원 체력 가득·Alive(<see cref="PlayerLifeCycleController.TryRespawnAfterWipeOnServer"/>) + 부활 무적 2초 + 지점 이동.
///  5) <see cref="PartyWipeRespawned"/> — 3단계(특성 재선택 팝업)가 붙는다.
/// </summary>
public static class PartyWipeRespawner
{
    /// <summary>리스폰 직후 무적(초) — 튜토리얼 자동 부활과 같은 값.</summary>
    public const float ReviveInvulnerabilitySeconds = 2f;

    private const float ProbeHeight = 3f;
    private const float OccupancyRadius = 0.4f;
    private const float FloorTolerance = 1f;

    /// <summary>
    /// [서버] 전멸 리스폰이 끝났다(리스폰한 clientId 목록, 오름차순). 특성 초기화 팝업(<see cref="TutorialTraitDirector"/>, 3단계)이 구독한다.
    /// 같은 판에서 여러 번 날 수 있다(전멸할 때마다).
    /// </summary>
    public static event Action<IReadOnlyList<ulong>> PartyWipeRespawned;

    /// <summary>[서버] 결과 화면 대신 리스폰했으면 true. false 면 아무것도 바꾸지 않았다(호출자가 결과 화면으로 간다).</summary>
    public static bool TryRespawnServer()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || !nm.IsServer)
            return false;

        TutorialDirector tutorial = TutorialDirector.Instance;
        bool tutorialActive = tutorial != null && tutorial.IsTutorialActive;
        BossEncounterDirector encounter = BossEncounterDirector.Instance;
        IReadOnlyList<TutorialPadArea> pads = TutorialDeathRules.ServerPadAreas();

        bool available = PartyWipeRespawnRules.CanRespawn(
            tutorialActive,
            tutorialActive && tutorial.IsGateOpen(TutorialGate.G10_BossEnterAndPad),
            pads.Count > 0,
            encounter != null && encounter.IsBossDefeatedServer);
        if (!available)
        {
            Debug.Log($"[PartyWipe] 리스폰 대상 아님(튜토리얼 {tutorialActive}, 패드 {pads.Count}개, 보스 격파 {encounter != null && encounter.IsBossDefeatedServer}) — 결과 화면.");
            return false;
        }

        var players = new List<NetworkObject>();
        foreach (NetworkClient client in nm.ConnectedClientsList)
        {
            if (client.PlayerObject != null && client.PlayerObject.GetComponent<PlayerLifeCycleController>() != null)
                players.Add(client.PlayerObject);
        }
        players.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));
        if (players.Count == 0)
            return false;

        var points = new List<Vector3>();
        TutorialDeathRules deathRules = tutorialActive ? tutorial.GetComponent<TutorialDeathRules>() : null;
        string where;
        if (deathRules != null && deathRules.TryFindWipeRespawnPoints(players.Count, points))
            where = "복도4";
        else if (TryFindPadRingPoints(players.Count, points))
            where = "보스 진입 패드 밖";
        else
        {
            Debug.LogError("[PartyWipe] 리스폰 지점을 찾지 못했다(복도4·패드 둘레 모두 바닥 없음) — 결과 화면으로 간다.");
            return false;
        }

        // 1) 보스 — 격파 직후 경합이면 여기서 거부된다(아무것도 안 바꾼 상태).
        if (encounter != null && !encounter.ResetForRetryServer())
            return false;
        if (BossTeleportManager.Instance != null) BossTeleportManager.Instance.ResetForRetryServer();
        if (BossTimerManager.Instance != null) BossTimerManager.Instance.StopForRetryServer();

        // 2) 튜토리얼 — 마스크 먼저(되감기로 '진행 중'이 되는 순간 G10 뒤 배리어가 앞을 막지 않게).
        if (tutorialActive)
        {
            foreach (NetworkObject po in players)
                tutorial.ResetBackBarrierMaskServer(po.OwnerClientId, TutorialArea.Corridor4);
            tutorial.RewindForBossRetryServer();
            deathRules?.ResumeForBossRetry();
        }

        // 3) 공용 목숨
        MultiGameRule rule = UnityEngine.Object.FindFirstObjectByType<MultiGameRule>();
        int lives = -1;
        if (rule == null || !rule.TryRefillPartyLivesOnServer(out lives))
            Debug.LogWarning("[PartyWipe] 공용 목숨을 다시 채우지 못했다(MultiGameRule 없음·스폰 전).");

        // 4) 플레이어
        var respawned = new List<ulong>();
        for (int i = 0; i < players.Count; i++)
        {
            NetworkObject po = players[i];
            var life = po.GetComponent<PlayerLifeCycleController>();
            if (!life.TryRespawnAfterWipeOnServer())
            {
                Debug.LogError($"[PartyWipe] client {po.OwnerClientId} 리스폰 실패(상태 {life.State}) — 그대로 둔다.", life);
                continue;
            }

            po.GetComponent<PlayerLandingProtection>()?.BeginProtection(
                InvulnerabilityCause.Revive, ReviveInvulnerabilitySeconds, applyStun: false);

            var recovery = po.GetComponent<PlayerFallRecovery>();
            if (recovery == null || !recovery.RelocateServer(points[i]))
                Debug.LogError($"[PartyWipe] client {po.OwnerClientId} 리스폰 지점으로 옮기지 못했다 — 제자리.", po);

            respawned.Add(po.OwnerClientId);
        }

        // 판 도중 재합류(할 일 48 C14 R5c) — 지금 자리에 없는 사람의 보관 생명 기록도 Alive·만피로(그 자리에 있었으면 살아났을 것).
        int absentLives = RejoinMemory.Server.OverwriteAbsentLivesAfterWipe();

        Debug.Log($"[PartyWipe] 전멸 리스폰 — {where} {respawned.Count}명, 공용 목숨 {lives}, 보스 처음부터" +
                  (absentLives > 0 ? $", 부재자 생명 기록 {absentLives}건 Alive·만피로." : "."));
        PartyWipeRespawned?.Invoke(respawned);
        return true;
    }

    // 절차 맵(또는 복도4 를 못 찾은 튜토리얼) — 보스 진입 패드 둘레(범위 + 1m + 여유) 바닥에서 고른다.
    private static bool TryFindPadRingPoints(int count, List<Vector3> into)
    {
        into.Clear();
        int ground = LayerMask.GetMask("Default", "Ground", "Env");
        int obstacle = ground | LayerMask.GetMask("Wall", "Barrier");   // 할 일 32 — 배리어는 Barrier 레이어
        IReadOnlyList<TutorialPadArea> pads = TutorialDeathRules.ServerPadAreas();

        foreach (BossEnterTrigger trigger in UnityEngine.Object.FindObjectsByType<BossEnterTrigger>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!trigger.TryGetComponent(out BoxCollider box) || !box.enabled) continue;
            Bounds b = box.bounds;
            TutorialPadArea pad = TutorialPadArea.Box(b.min.x, b.min.z, b.max.x, b.max.z);
            float floorY = b.min.y;

            var ring = new List<(float x, float z)>();
            PartyWipeRespawnRules.RingCandidates(pad, TutorialRevivePointRules.DefaultPadMargin,
                                                 PartyWipeRespawnRules.DefaultRingOffset, PartyWipeRespawnRules.DefaultRingStep, ring);
            var points = new List<(float x, float z)>();
            var ys = new List<float>();
            foreach ((float x, float z) in ring)
            {
                if (!TryResolveStandPoint(x, z, floorY, ground, obstacle, out float y)) continue;
                points.Add((x, z));
                ys.Add(y);
            }

            var chosen = new List<int>();
            if (!PartyWipeRespawnRules.TrySelectSpread(points, count, pad.CenterX, pad.CenterZ, PartyWipeRespawnRules.DefaultSpacing,
                                                        pads, TutorialRevivePointRules.DefaultPadMargin, chosen))
                continue;

            foreach (int i in chosen) into.Add(new Vector3(points[i].x, ys[i], points[i].z));
            return true;
        }
        return false;
    }

    // TutorialDeathRules.TryResolveStandPoint 와 같은 규칙 — 평평한 바닥이 있고 서 있을 자리가 비었는가.
    private static bool TryResolveStandPoint(float x, float z, float floorY, int ground, int obstacle, out float y)
    {
        y = floorY;
        var origin = new Vector3(x, floorY + ProbeHeight, z);
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, ProbeHeight * 2f, ground, QueryTriggerInteraction.Ignore))
            return false;
        if (Mathf.Abs(hit.point.y - floorY) > FloorTolerance) return false;

        Vector3 center = hit.point + Vector3.up * (OccupancyRadius + 0.05f);
        if (Physics.CheckSphere(center, OccupancyRadius, obstacle, QueryTriggerInteraction.Ignore)) return false;

        y = hit.point.y;
        return true;
    }
}
