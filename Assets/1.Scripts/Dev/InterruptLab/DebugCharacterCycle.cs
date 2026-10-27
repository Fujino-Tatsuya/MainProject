#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 간파 제작용 캐릭터 순환(DebugInterruptLab 씬에만 둔다, 할 일 115 C6) — F1 로 <see cref="cycleOrder"/> 순서대로 다음 캐릭터.
///
/// 로비 캐릭터 교체(할 일 48 C12, <c>LobbyPlayerSpawner.TrySwapCharacter</c>)와 같은 길이다 — 서버가 지금 PlayerObject 의
/// 위치·회전을 기억하고 디스폰한 뒤 같은 자리에 새 Variant 를 <see cref="PlayerSpawnService.SpawnCharacter"/> 로 세운다.
/// 카메라는 새 오너 플레이어의 <c>Player.OnNetworkSpawn → CameraTargetSwitcher.FocusOwnerPlayer</c> 로 다시 붙는다.
/// <list type="bullet">
/// <item>호스트만 — 서버 API 를 바로 부른다(새 RPC 없음). 클라 창에서 누르면 경고만 남긴다.</item>
/// <item>지금 캐릭터는 PlayerObject 의 프리팹 해시로 로스터에서 찾는다(선택 저장소는 건드리지 않는다).</item>
/// <item>교체하면 HP·쿨·상태가 새로 시작된다(새 오브젝트).</item>
/// </list>
/// 게이트 = <c>UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS</c>(Docs/tech/debug-hotkeys.md).
/// </summary>
[DisallowMultipleComponent]
public sealed class DebugCharacterCycle : MonoBehaviour
{
    private const string Tag = "[DebugInterruptLab]";

    [SerializeField] CharacterRoster characterRoster;
    [Tooltip("F1 순환 순서(로스터 캐릭터 id = 배열 인덱스). 기본 = 팔라딘(0) → 어쌔신(2) → 거너(1).")]
    [SerializeField] int[] cycleOrder = { 0, 2, 1 };
    [Tooltip("연타 방지 간격(초, 실제 시간) — 옛 오브젝트가 파괴되기 전 두 번 교체하지 않게.")]
    [SerializeField, Min(0.1f)] float minInterval = 0.5f;

    double _lastSwapAt = double.NegativeInfinity;

    /// <summary>
    /// 순환의 다음 캐릭터 id. 지금 id 가 순서에 있으면 그 다음(끝이면 처음), 없으면(모름 = -1 포함) 순서의 처음.
    /// 순서가 비면 -1.
    /// </summary>
    public static int NextInCycle(IReadOnlyList<int> order, int currentId)
    {
        if (order == null || order.Count == 0)
            return -1;

        for (int i = 0; i < order.Count; i++)
        {
            if (order[i] == currentId)
                return order[(i + 1) % order.Count];
        }

        return order[0];
    }

    void Update()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard kb = Keyboard.current;
        if (kb == null || !kb.f1Key.wasPressedThisFrame)
            return;

        if (Time.unscaledTimeAsDouble - _lastSwapAt < minInterval)
            return;

        _lastSwapAt = Time.unscaledTimeAsDouble;
        TryCycleLocal();
#endif
    }

    void TryCycleLocal()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
            return;

        if (!networkManager.IsServer)
        {
            Debug.LogWarning($"{Tag} F1 캐릭터 순환은 호스트만 된다(클라 창은 무시).", this);
            return;
        }

        if (characterRoster == null)
        {
            Debug.LogError($"{Tag} CharacterRoster 가 비어 캐릭터를 바꾸지 못했다.", this);
            return;
        }

        ulong clientId = networkManager.LocalClientId;
        if (!networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) ||
            client.PlayerObject == null || !client.PlayerObject.IsSpawned)
        {
            Debug.LogWarning($"{Tag} 바꿀 PlayerObject 가 없다.", this);
            return;
        }

        NetworkObject oldObject = client.PlayerObject;
        int currentId = FindCharacterId(characterRoster, oldObject.PrefabIdHash);
        int nextId = NextInCycle(cycleOrder, currentId);

        // 디스폰 전에 프리팹을 먼저 확인한다 — 실패하면 지금 캐릭터를 그대로 둔다(로비 교체와 같다).
        if (!characterRoster.TryGetAvailableCharacter(nextId, out CharacterRoster.Entry entry) ||
            entry.PlayerPrefab == null || !entry.PlayerPrefab.TryGetComponent<NetworkObject>(out _))
        {
            Debug.LogWarning($"{Tag} 캐릭터 id {nextId} 프리팹을 쓸 수 없다 — 교체 안 함.", this);
            return;
        }

        var pose = new Pose(oldObject.transform.position, oldObject.transform.rotation);
        var scene = oldObject.gameObject.scene;
        string oldName = oldObject.name;
        oldObject.Despawn(true); // NGO 가 client.PlayerObject 를 비운다 — 같은 프레임에 새로 세울 수 있다

        PlayerSpawnResult result = PlayerSpawnService.SpawnCharacter(
            networkManager, clientId, nextId, characterRoster, null, pose, scene);
        if (!result.Succeeded)
        {
            Debug.LogError($"{Tag} 새 캐릭터 스폰 실패({result.Failure}) — Dev Boot 를 다시 시작할 것.", this);
            return;
        }

        Debug.Log($"{Tag} F1 캐릭터 교체 {oldName} → {result.Prefab.name} pos={pose.position}", this);
    }

    /// <summary>프리팹 해시로 로스터의 캐릭터 id 를 찾는다. 없으면 -1.</summary>
    static int FindCharacterId(CharacterRoster roster, uint prefabIdHash)
    {
        IReadOnlyList<CharacterRoster.Entry> entries = roster.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            GameObject prefab = entries[i]?.PlayerPrefab;
            if (prefab != null && prefab.TryGetComponent(out NetworkObject networkObject) &&
                networkObject.PrefabIdHash == prefabIdHash)
            {
                return i;
            }
        }

        return -1;
    }
}
#endif
