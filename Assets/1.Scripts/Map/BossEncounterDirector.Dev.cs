#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// 개발용(F11 — DevBossFinalBlow, 경석 10-27): 등장 연출을 서버에서 바로 스킵한다. DEV_HOTKEYS 없는 릴리스 빌드엔 이 파일이 없다.
// + 보스 처치 연출 반복 디버그 랩(할 일 244 — DebugBossVictory 씬, DebugBossVictoryLab)이 쓰는 진입점.
public sealed partial class BossEncounterDirector
{
    /// <summary>[서버] 보스가 살아 있고 전투 중인가.</summary>
    public bool DevIsBossInCombat =>
        IsServer && IsSpawned && phase.Value == BossEncounterPhase.Combat && _bossUnit != null && _bossUnit.CurrentHealth > 0;

    /// <summary>[서버] 등장 연출 중인가(워크인·강하·착지·지시).</summary>
    public bool DevIsIntroRunning => IsServer && IsSpawned && IsCinematicPhase(phase.Value);

    /// <summary>
    /// [서버] 등장 연출을 전원 동의 스킵과 같은 경로(<see cref="SkipIntroServer"/>)로 넘긴다 — 보스를 착지점에 세우고 카메라 복귀 뒤 전투.
    /// 이미 카메라 복귀 중이면 false(할 일 없음). 연출이 아닌 단계면 false.
    /// </summary>
    public bool DevSkipIntroServer()
    {
        if (!IsServer || !IsSpawned || !_introRunning || !IsCinematicPhase(phase.Value)) return false;
        double elapsed = NetworkManager.ServerTime.Time - phaseStartServerTime.Value;
        if (phase.Value == BossEncounterPhase.Dialogue && elapsed >= cameraReturnAt.Value) return false;
        SkipIntroServer();
        return true;
    }

    // ── 보스 처치 연출 반복 디버그 랩(할 일 244) ──────────────────────────────
    // 실게임 경로(처치 → HandleBossDefeated → TryStartVictoryServer → 사망 표현 → 결과 대기)를 그대로 타고,
    // 랩이 하는 일은 ① 보스를 등장 연출 없이 세우기 ② 표시용 팀원 끼우기 ③ 결과 화면 전환 막기 ④ 원상복구 뿐이다.

    /// <summary>
    /// [모든 피어·랩 전용] 플레이어가 아닌 표시용 팀원 복제본(<c>CharacterSelectPreviewPool</c> 로 만든 몸뿐인 오브젝트).
    /// 처치 연출 표시(대상별 시간 배율·캐릭터 흑백)가 팀원으로 다룬다.
    /// 비어 있으면 실게임과 같다.
    /// </summary>
    public static readonly List<GameObject> DevVictoryExtraTeammates = new List<GameObject>();

    /// <summary>[서버·랩 전용] true 면 결과 화면 전환을 막는다(결과 씬으로 넘어갈 순간에 <see cref="DevVictoryResultHeld"/> 를 한 번 올린다).</summary>
    public static bool DevHoldVictoryResult;

    /// <summary>[서버·랩 전용] 결과 화면으로 넘어갔을 순간(승리 연출 끝 이후) — 랩이 이걸 "한 바퀴 끝"으로 본다.</summary>
    public static event Action DevVictoryResultHeld;

    private bool _devResultHeldRaised;

    /// <summary>[서버] 지금 스폰된 23호(없으면 null).</summary>
    public TwentyThreeBoss DevVictoryBoss => _bossUnit as TwentyThreeBoss;

    /// <summary>[서버] 이번 보스가 승리 연출 대상(B, raidBossPrefab)인가.</summary>
    public bool DevSpawnedVictoryVariant => _spawnedVictoryVariant;

    /// <summary>[서버] 결과 화면 전환을 막고 있는가(한 바퀴 끝).</summary>
    public bool DevVictoryResultReached => _devResultHeldRaised;

    partial void DevBlockResultTransition(ref bool block)
    {
        if (!DevHoldVictoryResult) return;
        block = true;
        if (_devResultHeldRaised) return;
        _devResultHeldRaised = true;
        Edit.Log("[BossVictoryLab] 결과 화면 전환 차단 — 한 바퀴 끝", this);
        DevVictoryResultHeld?.Invoke();
    }

    /// <summary>
    /// [서버] 등장 연출 없이 보스를 <paramref name="position"/>·<paramref name="rotation"/> 에 세운다 — 실게임 스폰(<see cref="SpawnBossServer"/>)을 그대로 쓰고
    /// 착지 포즈만 푼다. 서버 FSM 은 스폰 때처럼 멈춘 채로 둔다(공격하지 않고 서 있기만 — 처치·사망 경로는 살아 있다). 이미 있으면 디스폰 후 새로.
    /// </summary>
    public bool DevSpawnVictoryBossServer(Vector3 position, Quaternion rotation, out string error)
    {
        error = null;
        if (!IsServer || !IsSpawned) { error = "서버가 아니거나 디렉터가 스폰 전"; return false; }

        if (_bossNetworkObject != null) DespawnBoss();
        _bossMonster = null;
        _bossEntranceAnim = null;
        _introRunning = false;
        _combatStarted = false;

        // 인원 HP 보정·잠금 대상 = 지금 접속한 전원(랩은 솔로 호스트).
        _eligibleClientIds.Clear();
        foreach (KeyValuePair<ulong, NetworkClient> pair in NetworkManager.ConnectedClients)
            if (pair.Value?.PlayerObject != null) _eligibleClientIds.Add(pair.Key);
        eligibleCount.Value = _eligibleClientIds.Count;

        if (bossLandingPoint == null) bossLandingPoint = FindLandingPointByName(this);
        if (!SpawnBossServer()) { error = "보스 스폰 실패(프리팹·착지점 확인)"; return false; }

        _descendTo = position;
        MoveBoss(position);
        if (_bossNetworkObject != null) _bossNetworkObject.transform.rotation = rotation;
        _bossEntranceAnim?.EndEntranceAnimationServer();
        SetPhase(BossEncounterPhase.Combat);

        if (!_spawnedVictoryVariant)
            error = "B(레이드) 보스가 아니라 승리 연출이 안 뜬다 — 툴바 보스 A/B 를 B 로";
        return true;
    }

    /// <summary>
    /// [서버] 승리 연출 원상복구 — 시체 보스 디스폰 · 복제 상태 비활성(회차 번호는 유지 → 다음 시작은 새 회차) · 처치/결과 예약 지움 ·
    /// 연출 잠금(입력·무적) 해제 · 페이즈 Idle. 표시는 비활성을 보고 스스로 정리한다.
    /// </summary>
    public void DevResetVictoryServer()
    {
        if (!IsServer || !IsSpawned) return;

        DespawnBoss();
        _bossMonster = null;
        _bossEntranceAnim = null;
        _combatStarted = false;

        VictoryState v = _victory.Value;
        if (v.Active) _victory.Value = new VictoryState { Sequence = v.Sequence };

        _defeatHandled = false;
        _resultTransitionAt = 0d;
        _victoryMinResultAt = 0d;
        _devResultHeldRaised = false;

        foreach (KeyValuePair<ulong, NetworkClient> pair in NetworkManager.ConnectedClients)
        {
            NetworkObject po = pair.Value != null ? pair.Value.PlayerObject : null;
            if (po == null) continue;
            PlayerEncounterLock encounterLock = po.GetComponent<PlayerEncounterLock>();
            if (encounterLock != null) encounterLock.SetCinematicInputLockServer(false);
            PlayerInvulnerability invulnerability = po.GetComponent<PlayerInvulnerability>();
            if (invulnerability != null) invulnerability.RemoveServerToken(InvulnerabilityCause.BossVictory);
        }

        SetPhase(BossEncounterPhase.Idle);
    }
}
#endif
