#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using System;
using Unity.Netcode;
using UnityEngine;

// 로비 예외 상황 단축키 Alt+숫자(할 일 112, QA·부스 운영용) — 입력·토스트는 각 피어의 LobbyRecoveryHotkeys, 실행은 서버.
// 🔴 에디터·Development Build·DEV_HOTKEYS 빌드에만 있다(심볼을 지운 릴리스 빌드엔 RPC 도 없다 — 호스트·클라는 같은 심볼 빌드여야 한다).
//    복제 목록(클리어 플래그)은 이 파일이 아니라 본체에 있다 — NetworkVariable 구성은 빌드마다 같아야 한다.
// 원본: Docs/tech/debug-hotkeys.md §3 · Docs/tech/lobby-training.md "튜토리얼 클리어 플래그".
public sealed partial class LobbyTrainingDirector
{
    private const string RecoveryTag = "[LobbyRecovery]";

    // 스폰 지점 높이 보정과 같은 값 — 바닥에 정확히 놓으면 콜라이더가 파고든 채 시작한다.
    private const float RecoveryGroundClearance = 0.5f;

    /// <summary>[요청한 피어] 서버가 돌려준 결과 — 토스트용.</summary>
    public static event Action<LobbyRecoveryAction, LobbyRecoveryResult> LocalRecoveryResult;

    /// <summary>[아무 피어] 예외 상황 동작 요청 — 서버가 보낸 사람 기준으로 판정·실행하고 결과를 그 사람에게만 돌려준다.</summary>
    public void RequestRecovery(LobbyRecoveryAction action)
    {
        if (!IsSpawned)
            return;

        RequestRecoveryServerRpc(action);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestRecoveryServerRpc(LobbyRecoveryAction action, RpcParams p = default)
    {
        var sender = p.Receive.SenderClientId;
        var result = RunRecoveryServer(sender, action);
        var message = $"{RecoveryTag} client {sender} {action} → {result} ({Describe()})";
        if (result == LobbyRecoveryResult.Failed)
            Debug.LogWarning(message, this);
        else
            Debug.Log($"[SceneFlow] {message}");

        RecoveryResultClientRpc(action, result, RpcTarget.Single(sender, RpcTargetUse.Temp));
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void RecoveryResultClientRpc(LobbyRecoveryAction action, LobbyRecoveryResult result, RpcParams p = default)
    {
        LocalRecoveryResult?.Invoke(action, result);
    }

    private LobbyRecoveryResult RunRecoveryServer(ulong sender, LobbyRecoveryAction action)
    {
        if (_flow == null)
            return LobbyRecoveryResult.Failed;

        switch (action)
        {
            case LobbyRecoveryAction.ToggleTrainingClear:
                var cleared = !_flow.IsTrainingCleared(sender);
                if (!ServerSetTrainingCleared(sender, cleared, out var skipped))
                    return LobbyRecoveryResult.Failed;
                return !cleared ? LobbyRecoveryResult.TrainingClearOff
                    : skipped ? LobbyRecoveryResult.TrainingClearSkippedToSortie
                    : LobbyRecoveryResult.TrainingClearOn;

            case LobbyRecoveryAction.Unstuck:
                var hasPlayer = TryGetPlayer(sender, out var player);
                var briefing = GetComponent<LobbyBriefing>();
                var verdict = LobbyRecoveryHotkeyRules.UnstuckVerdict(
                    hasPlayer, briefing != null && briefing.ServerIsRunning, IsLeavingLobby());
                if (verdict != LobbyRecoveryResult.Unstuck)
                    return verdict;
                return ServerUnstuck(sender, player) ? LobbyRecoveryResult.Unstuck : LobbyRecoveryResult.Failed;

            case LobbyRecoveryAction.ReloadLobby:
                // 호스트만 — 리슨 서버의 호스트 = 서버 자신의 clientId.
                var reload = LobbyRecoveryHotkeyRules.ReloadVerdict(sender == NetworkManager.LocalClientId, IsLeavingLobby());
                if (reload != LobbyRecoveryResult.Reloading)
                    return reload;
                return ServerReloadLobby() ? LobbyRecoveryResult.Reloading : LobbyRecoveryResult.Failed;

            default:
                return LobbyRecoveryResult.Failed;
        }
    }

    private NetworkLoadingFlowController LoadingFlow =>
        NetworkManager != null ? NetworkManager.GetComponent<NetworkLoadingFlowController>() : null;

    // 출격 래치(메인 진입)를 걸었거나 씬 전환 중 — 로비가 내려가는 중이라 옮기거나 다시 불러오지 않는다.
    private bool IsLeavingLobby()
    {
        var flow = LoadingFlow;
        return ServerHasLaunched || (flow != null && flow.IsSceneTransitionInProgress);
    }

    // [서버] 로비 다시 불러오기 — 판 끝 로비 복귀와 같은 경로(동적 객체 디스폰 → NGO Single 로드 → LobbyPlayerSpawner 가 전원 다시 세움).
    // 🔴 TryRestartTargetScene(로딩 씬 경유)은 맵 씬 전용이다 — 맵 첫 스폰(SpawnAllPlayers)·MainGameReady 까지 돌아 로비에 쓸 수 없다.
    private bool ServerReloadLobby()
    {
        var flow = LoadingFlow;
        if (flow == null)
        {
            Debug.LogError($"{RecoveryTag} 로비 리로드 실패 — NetworkLoadingFlowController 가 없다(NetworkManager.prefab 인스턴스인지 확인).", this);
            return false;
        }

        var sceneName = gameObject.scene.name;
        if (!flow.TryLoadSessionSceneSingle(sceneName, out var reason))
        {
            Debug.LogError($"{RecoveryTag} 로비 리로드 실패 — {reason}.", this);
            return false;
        }

        Debug.Log($"[SceneFlow] {RecoveryTag} 로비 리로드 시작 scene={sceneName} ({Describe()}) — 클리어 플래그 [{string.Join(",", _flow.TrainingCleared)}] 는 다음 진행기로 잇는다.");
        return true;
    }

    // [서버] 멈춤 탈출 — 이동을 막는 기존 상태를 각 소유 API 로 풀고(새 차단 계통 없음) 안전 지점으로 옮긴다.
    // 브리핑 잠금은 호출 전에 걸렀다 — 여기 오는 연출 잠금은 남은 찌꺼기다.
    private bool ServerUnstuck(ulong clientId, Player player)
    {
        var fall = player.GetComponent<PlayerFallRecovery>();
        if (fall == null)
        {
            Debug.LogWarning($"{RecoveryTag} client {clientId} 멈춤 탈출 불가 — PlayerFallRecovery 없음.", this);
            return false;
        }

        if (!TryGetRecoveryPosition(clientId, out var destination, out var where))
        {
            Debug.LogWarning($"{RecoveryTag} client {clientId} 멈춤 탈출 불가 — 안전 지점(구역·스폰 지점)을 못 찾았다.", this);
            return false;
        }

        fall.CancelRecoveryServer();   // 낙하 복구 연출·입력 잠금(오너)
        var encounterLock = player.GetComponent<PlayerEncounterLock>();
        if (encounterLock != null)
        {
            encounterLock.EndCinematicServer();
            encounterLock.SetCinematicInputLockServer(false);
            encounterLock.SetCinematicHiddenServer(false);
        }

        player.GetComponent<PlayerSkillController>()?.EndActiveSkillServer(SkillEndReason.Cancelled);
        player.GetComponent<IPlayerBasicAttack>()?.CancelCurrentAttack();
        var debuffs = player.GetComponent<StatusEffectController>()?.ClearDebuffsServer() ?? 0;

        if (!fall.RelocateServer(destination))
            return false;

        Debug.Log($"[SceneFlow] {RecoveryTag} client {clientId} 멈춤 탈출 → {where} {destination} (디버프 {debuffs}개 해제)");
        return true;
    }

    // 고정 명단 + 훈련·브리핑 단계면 그 단계 구역 가운데 바닥(LobbyTutorialRules.RecoveryPointFor), 아니면(또는 바닥을 못 찾으면) 자기 스폰 지점.
    private bool TryGetRecoveryPosition(ulong clientId, out Vector3 position, out string where)
    {
        var table = Table;
        var point = table != null
            ? LobbyTutorialRules.RecoveryPointFor(table.Steps, _flow.StepIndex, _flow.IsRosterLocked && ContainsRoster(clientId))
            : LobbyGatherPoint.None;
        var zone = ZoneOf(point);
        if (zone != null && GroundProbe.TryFindGround(zone.WorldCenter, 0, out var ground, out _))
        {
            position = ground.point + Vector3.up * RecoveryGroundClearance;
            where = $"{point} 구역";
            return true;
        }

        var spawner = FindAnyObjectByType<LobbyPlayerSpawner>();
        if (spawner != null && spawner.TryGetSpawnPosition(clientId, out position))
        {
            where = "스폰 지점";
            return true;
        }

        position = default;
        where = null;
        return false;
    }
}
#endif
