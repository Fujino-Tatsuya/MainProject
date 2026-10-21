using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>스폰을 건너뛴 이유. <see cref="None"/> = 스폰됨.</summary>
public enum PlayerSpawnFailure
{
    None,
    /// <summary>ConnectedClients 에 없다(조용히 건너뜀).</summary>
    ClientNotConnected,
    /// <summary>이미 PlayerObject 가 있다 — 기존 오브젝트는 그대로 남는다.</summary>
    AlreadyHasPlayerObject,
    PrefabMissing,
    PrefabMissingNetworkObject,
}

/// <summary>스폰 1건의 결과. 실패면 <see cref="Player"/> 는 null.</summary>
public readonly struct PlayerSpawnResult
{
    public readonly PlayerSpawnFailure Failure;
    public readonly NetworkObject Player;
    public readonly GameObject Prefab;

    public PlayerSpawnResult(PlayerSpawnFailure failure, NetworkObject player, GameObject prefab)
    {
        Failure = failure;
        Player = player;
        Prefab = prefab;
    }

    public bool Succeeded => Failure == PlayerSpawnFailure.None;
}

/// <summary>
/// [서버 전용] 플레이어 1명 스폰 — 프리팹 결정(선택 저장소 → <see cref="CharacterRoster"/> → 기본 프리팹)과
/// Instantiate·씬 이동·<c>SpawnAsPlayerObject</c>. 맵 진입(<see cref="NetworkLoadingFlowController"/>)과 로비가 같이 쓴다(할 일 48 §4.5).
/// 스폰 위치·"한 번만 스폰" 판단은 호출자 몫이다. 서버 여부도 호출자가 확인한다.
/// </summary>
public static class PlayerSpawnService
{
    /// <summary>클라가 로비에서 고른 캐릭터(<see cref="ServerCharacterSelectionStore"/>)의 프리팹. 선택이 없거나 못 쓰면 기본 프리팹.</summary>
    public static GameObject ResolvePlayerPrefab(ulong clientId, CharacterRoster roster, GameObject defaultPrefab)
    {
        return ServerCharacterSelectionStore.TryGet(clientId, out var characterId)
            ? ResolvePlayerPrefab(characterId, roster, defaultPrefab)
            : defaultPrefab;
    }

    /// <summary>캐릭터 id 의 프리팹. 로스터가 없거나, 범위 밖·비활성 칸이거나, 프리팹이 비었으면 기본 프리팹.</summary>
    public static GameObject ResolvePlayerPrefab(int characterId, CharacterRoster roster, GameObject defaultPrefab)
    {
        if (roster != null &&
            roster.TryGetAvailableCharacter(characterId, out var entry) &&
            entry.PlayerPrefab != null)
        {
            return entry.PlayerPrefab;
        }

        return defaultPrefab;
    }

    /// <summary>선택 저장소 기준 프리팹으로 스폰한다. <paramref name="targetScene"/> 이 로드돼 있으면 그 씬으로 옮긴다.</summary>
    public static PlayerSpawnResult SpawnForClient(
        NetworkManager networkManager, ulong clientId, CharacterRoster roster, GameObject defaultPrefab,
        Pose spawnPose, Scene targetScene)
    {
        return Spawn(networkManager, clientId, ResolvePlayerPrefab(clientId, roster, defaultPrefab), spawnPose, targetScene);
    }

    /// <summary>지정한 캐릭터 id 로 스폰한다(선택 저장소를 보지 않는다 — 로비 캐릭터 교체용).</summary>
    public static PlayerSpawnResult SpawnCharacter(
        NetworkManager networkManager, ulong clientId, int characterId, CharacterRoster roster, GameObject defaultPrefab,
        Pose spawnPose, Scene targetScene)
    {
        return Spawn(networkManager, clientId, ResolvePlayerPrefab(characterId, roster, defaultPrefab), spawnPose, targetScene);
    }

    private static PlayerSpawnResult Spawn(
        NetworkManager networkManager, ulong clientId, GameObject prefab, Pose spawnPose, Scene targetScene)
    {
        if (!networkManager.ConnectedClients.TryGetValue(clientId, out var client))
        {
            return new PlayerSpawnResult(PlayerSpawnFailure.ClientNotConnected, null, prefab);
        }

        if (client.PlayerObject != null)
        {
            // 조용히 넘기면 "왜 이 클라만 엉뚱한 곳에 있나"를 추적할 수 없다.
            // 이미 PlayerObject 가 있으면 이 스폰은 건너뛰므로, 그 오브젝트가 어디에 있든 그대로 남는다.
            Edit.LogWarning(
                $"[Loading] clientId={clientId} 는 이미 PlayerObject 를 갖고 있어 스폰을 건너뜁니다. " +
                $"기존 위치={client.PlayerObject.transform.position}, " +
                $"이번에 스폰했을 위치={spawnPose.position}");
            return new PlayerSpawnResult(PlayerSpawnFailure.AlreadyHasPlayerObject, null, prefab);
        }

        if (prefab == null)
        {
            Edit.LogWarning($"[Loading] Player prefab is not assigned. clientId={clientId}");
            return new PlayerSpawnResult(PlayerSpawnFailure.PrefabMissing, null, null);
        }

        if (!prefab.TryGetComponent<NetworkObject>(out _))
        {
            Edit.LogWarning($"[Loading] Player prefab must have a {nameof(NetworkObject)} component. prefab={prefab.name}");
            return new PlayerSpawnResult(PlayerSpawnFailure.PrefabMissingNetworkObject, null, prefab);
        }

        var player = Object.Instantiate(prefab, spawnPose.position, spawnPose.rotation);
        if (targetScene.IsValid() && targetScene.isLoaded)
        {
            SceneManager.MoveGameObjectToScene(player, targetScene);
        }

        var networkObject = player.GetComponent<NetworkObject>();
        networkObject.SpawnAsPlayerObject(clientId, true);
        return new PlayerSpawnResult(PlayerSpawnFailure.None, networkObject, prefab);
    }
}
