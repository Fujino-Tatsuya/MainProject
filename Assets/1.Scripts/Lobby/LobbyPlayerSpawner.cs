using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// [서버 전용] 걷는 로비(3.LobbyScene)에 플레이어를 세운다 — PLAN-48 §4.3·§4.4, C9.
/// <list type="bullet">
/// <item>시점: 이 씬의 NGO <c>LoadEventCompleted</c> 의 ClientsThatCompleted(부팅·판 끝 복귀), 늦게 접속한 클라는 <c>SynchronizeComplete</c> 뒤.</item>
/// <item>프리팹: <see cref="PlayerSpawnService"/>(선택 저장소 → <see cref="CharacterRoster"/> → Variant) — 맵 진입과 같은 결정.</item>
/// <item>위치: 스폰 지점 3개 중 비어 있는 가장 앞 칸. 이미 PlayerObject 가 있는 클라는 건너뛴다(중복 스폰 없음).</item>
/// <item>퇴장: NGO 가 PlayerObject 를 치운다(DontDestroyWithOwner=0). 한 프레임 뒤에도 남아 있으면 여기서 디스폰한다.</item>
/// <item>캐릭터 교체(C12): <see cref="TrySwapCharacter"/> — 디스폰 → 같은 자리에 새 Variant. 자리(스폰 지점)는 유지된다.</item>
/// <item>추락: 플레이어가 <see cref="fallRespawnY"/> 아래로 내려가면 피해 없이 자기 스폰 지점으로 되돌린다(<see cref="PlayerFallRecovery.RelocateServer"/>).</item>
/// <item>로비를 떠날 때(카운트다운 → Loading): <c>NetworkLoadingFlowController.UnloadNetworkSourceScene</c> 가 동적 객체(로비 플레이어 포함)를 먼저 디스폰한다.
/// 시작 래치가 걸린 뒤에는 새로 세우지 않는다.</item>
/// </list>
/// 일반 MonoBehaviour 다 — 씬 이벤트는 서버의 NetworkManager.SceneManager 에서 받는다.
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyPlayerSpawner : MonoBehaviour
{
    private const string Tag = "[LobbyPlayerSpawner]";

    [SerializeField] private Transform[] spawnPoints = new Transform[3];
    [SerializeField] private CharacterRoster characterRoster;
    [Tooltip("로스터·선택이 모두 비었을 때 쓰는 프리팹(NetworkLoadingFlowController 의 기본 프리팹과 같게).")]
    [SerializeField] private GameObject defaultPlayerPrefab;
    [Tooltip("스폰 지점 위로 띄우는 높이(m) — 바닥 콜라이더에 겹쳐 나지 않게.")]
    [SerializeField, Min(0f)] private float spawnHeightOffset = 0.5f;
    [Tooltip("이 월드 Y 아래로 떨어지면 자기 스폰 지점으로 되돌린다(피해 없음).")]
    [SerializeField] private float fallRespawnY = -10f;
    [Tooltip("같은 플레이어를 다시 되돌리기까지 최소 간격(초) — 오너에게 순간이동이 닿기 전 중복 요청 방지.")]
    [SerializeField, Min(0.1f)] private float fallRespawnCooldownSeconds = 1f;

    private readonly Dictionary<ulong, int> _slotByClient = new Dictionary<ulong, int>();
    private readonly Dictionary<ulong, float> _lastRelocateAt = new Dictionary<ulong, float>();
    private readonly List<ulong> _slotClientBuffer = new List<ulong>();
    private NetworkManager _networkManager;
    private NetworkSceneManager _sceneManager;

    private void OnEnable()
    {
        TryRegister();
    }

    private void Start()
    {
        TryRegister();
    }

    private void OnDisable()
    {
        Unregister();
    }

    private void TryRegister()
    {
        if (_sceneManager != null)
            return;

        var networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening || networkManager.SceneManager == null)
            return;

        _networkManager = networkManager;
        _sceneManager = networkManager.SceneManager;
        _sceneManager.OnSceneEvent += HandleSceneEvent;
        _networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
    }

    private void Unregister()
    {
        if (_sceneManager != null)
            _sceneManager.OnSceneEvent -= HandleSceneEvent;
        if (_networkManager != null)
            _networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;

        _sceneManager = null;
        _networkManager = null;
    }

    private bool IsServer => _networkManager != null && _networkManager.IsListening && _networkManager.IsServer;

    // 카운트다운이 끝나 시작 래치가 걸렸으면 로비는 내려가는 중이다 — 새로 세우지 않는다.
    private static bool IsLobbyClosing => LobbySession.Active != null && LobbySession.Active.CanStartGame;

    private void HandleSceneEvent(SceneEvent sceneEvent)
    {
        if (!IsServer)
            return;

        switch (sceneEvent.SceneEventType)
        {
            case SceneEventType.LoadEventCompleted:
                if (sceneEvent.SceneName != gameObject.scene.name || sceneEvent.ClientsThatCompleted == null)
                    return;
                foreach (var clientId in sceneEvent.ClientsThatCompleted)
                    SpawnFor(clientId, "LoadEventCompleted");
                break;

            case SceneEventType.SynchronizeComplete:
                // 서버에서는 동기화를 마친 클라 id 가 온다. 로비가 활성 씬일 때 늦게 붙은 클라다.
                if (sceneEvent.ClientId == _networkManager.LocalClientId)
                    return;
                SpawnFor(sceneEvent.ClientId, "SynchronizeComplete");
                break;
        }
    }

    private void SpawnFor(ulong clientId, string context)
    {
        if (IsLobbyClosing)
        {
            Debug.Log($"[SceneFlow] {Tag} client {clientId} 스폰 건너뜀 — 게임 시작 래치 뒤 ({context})");
            return;
        }

        if (!_networkManager.ConnectedClients.TryGetValue(clientId, out var client))
            return;

        // 이미 PlayerObject 가 있으면 조용히 건너뛴다(PlayerSpawnService 는 같은 경우 경고를 남긴다).
        if (client.PlayerObject != null && client.PlayerObject.IsSpawned)
            return;

        var slot = AcquireSlot(clientId);
        var result = PlayerSpawnService.SpawnForClient(
            _networkManager, clientId, characterRoster, defaultPlayerPrefab, ResolveSpawnPose(slot), gameObject.scene);

        if (!result.Succeeded)
        {
            Debug.LogWarning($"{Tag} client {clientId} 스폰 실패: {result.Failure} ({context})", this);
            ReleaseSlot(clientId);
            return;
        }

        Debug.Log($"[SceneFlow] {Tag} client {clientId} 스폰 prefab={result.Prefab.name} slot={slot} ({context})");
    }

    /// <summary>
    /// [서버] 로비 캐릭터 교체(C12, PLAN-48 §4.5 A) — 지금 PlayerObject 의 위치·회전을 기억하고 디스폰한 뒤
    /// 같은 자리에 <paramref name="characterId"/> 의 Variant 를 <see cref="PlayerSpawnService.SpawnCharacter"/> 로 세운다.
    /// 디스폰 전에 프리팹을 먼저 확인해, 실패하면 기존 오브젝트를 그대로 둔다. 추락 복귀 스폰 지점(자리)은 clientId 기준이라 그대로 이어진다.
    /// 규칙 검증(준비·카운트다운·쿨다운·거리)은 <see cref="LobbySession"/> 몫이다.
    /// </summary>
    public bool TrySwapCharacter(ulong clientId, int characterId, out string failure)
    {
        failure = null;
        if (!IsServer)
        {
            failure = "서버 아님";
            return false;
        }

        if (!_networkManager.ConnectedClients.TryGetValue(clientId, out var client) ||
            client.PlayerObject == null || !client.PlayerObject.IsSpawned)
        {
            failure = "PlayerObject 없음";
            return false;
        }

        var prefab = PlayerSpawnService.ResolvePlayerPrefab(characterId, characterRoster, defaultPlayerPrefab);
        if (prefab == null || !prefab.TryGetComponent<NetworkObject>(out _))
        {
            failure = $"프리팹 없음(characterId={characterId})";
            return false;
        }

        var oldObject = client.PlayerObject;
        var pose = new Pose(oldObject.transform.position, oldObject.transform.rotation);
        var oldName = oldObject.name;
        oldObject.Despawn(true);   // NGO 가 client.PlayerObject 를 비운다 — 같은 프레임에 새로 세울 수 있다

        var result = PlayerSpawnService.SpawnCharacter(
            _networkManager, clientId, characterId, characterRoster, defaultPlayerPrefab, pose, gameObject.scene);
        if (!result.Succeeded)
        {
            // 디스폰은 이미 끝났다 — 빈손으로 두지 않게 자기 스폰 지점에 저장소 기준으로 다시 세운다.
            failure = $"새 Variant 스폰 실패: {result.Failure}";
            Debug.LogError($"{Tag} client {clientId} 교체 스폰 실패({result.Failure}) — 스폰 지점에 다시 세운다.", this);
            SpawnFor(clientId, "SwapFallback");
            return false;
        }

        Debug.Log($"[SceneFlow] {Tag} client {clientId} 캐릭터 교체 {oldName} → {result.Prefab.name} pos={pose.position}");
        return true;
    }

    private int AcquireSlot(ulong clientId)
    {
        if (_slotByClient.TryGetValue(clientId, out var existing))
            return existing;

        var count = Mathf.Max(1, spawnPoints != null ? spawnPoints.Length : 0);
        for (var slot = 0; slot < count; slot++)
        {
            if (!_slotByClient.ContainsValue(slot))
            {
                _slotByClient[clientId] = slot;
                return slot;
            }
        }

        // 자리보다 사람이 많으면(승인 규칙상 없어야 한다) 순환해서 겹쳐 세운다.
        var overflow = _slotByClient.Count % count;
        _slotByClient[clientId] = overflow;
        return overflow;
    }

    private void ReleaseSlot(ulong clientId)
    {
        _slotByClient.Remove(clientId);
        _lastRelocateAt.Remove(clientId);
    }

    private Pose ResolveSpawnPose(int slot)
    {
        var point = spawnPoints != null && slot >= 0 && slot < spawnPoints.Length ? spawnPoints[slot] : null;
        if (point == null)
        {
            Debug.LogWarning($"{Tag} 스폰 지점 {slot} 이 비었다 — 이 오브젝트 위치로 대신한다.", this);
            point = transform;
        }

        return new Pose(point.position + Vector3.up * spawnHeightOffset, point.rotation);
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (!IsServer)
            return;

        ReleaseSlot(clientId);
        if (isActiveAndEnabled)
            StartCoroutine(DespawnLeftoverPlayerObject(clientId));
    }

    // NGO 가 퇴장한 클라의 PlayerObject 를 치운다. 그 정리가 끝난 다음 프레임에도 남아 있을 때만 직접 디스폰한다(이중 디스폰 방지).
    private IEnumerator DespawnLeftoverPlayerObject(ulong clientId)
    {
        yield return null;
        if (!IsServer)
            yield break;

        foreach (var networkObject in _networkManager.SpawnManager.SpawnedObjectsList)
        {
            if (networkObject != null && networkObject.IsSpawned && networkObject.IsPlayerObject && networkObject.OwnerClientId == clientId)
            {
                Debug.LogWarning($"{Tag} 퇴장한 client {clientId} 의 PlayerObject 가 남아 디스폰한다.", this);
                networkObject.Despawn(true);
                yield break;
            }
        }
    }

    private void FixedUpdate()
    {
        if (!IsServer || _slotByClient.Count == 0)
            return;

        _slotClientBuffer.Clear();
        _slotClientBuffer.AddRange(_slotByClient.Keys);
        foreach (var clientId in _slotClientBuffer)
            ReturnIfFallen(clientId);
    }

    private void ReturnIfFallen(ulong clientId)
    {
        if (!_networkManager.ConnectedClients.TryGetValue(clientId, out var client))
            return;

        var playerObject = client.PlayerObject;
        if (playerObject == null || !playerObject.IsSpawned || playerObject.transform.position.y >= fallRespawnY)
            return;

        if (_lastRelocateAt.TryGetValue(clientId, out var last) && Time.time - last < fallRespawnCooldownSeconds)
            return;

        _lastRelocateAt[clientId] = Time.time;
        var pose = ResolveSpawnPose(_slotByClient[clientId]);
        var recovery = playerObject.GetComponent<PlayerFallRecovery>();
        var relocated = recovery != null && recovery.RelocateServer(pose.position);
        Debug.Log($"[SceneFlow] {Tag} client {clientId} 추락 → 스폰 지점 복귀 relocated={relocated} y={playerObject.transform.position.y:F1}");
    }
}
