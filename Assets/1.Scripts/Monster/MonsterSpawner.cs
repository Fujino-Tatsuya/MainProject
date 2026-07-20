using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// 몬스터 서버 스폰 관리자. 서버에서만 NetworkObject를 Instantiate + Spawn() → NGO 복제.
//
// 맵 생성 연동을 위해 공개 스폰 API를 노출한다:
//   - SpawnAt(point): 특정 스폰 지점 1건 스폰
//   - SpawnWave():    등록된 모든 지점 스폰
// (MapContentSpawner 등에서 서버 컨텍스트로 호출.)
[DisallowMultipleComponent]
public class MonsterSpawner : NetworkBehaviour
{
    [Header("기본 몬스터 프리팹 (NetworkObject 필수)")]
    [SerializeField] private GameObject defaultMonsterPrefab;

    [Header("스폰 지점")]
    [Tooltip("비우면 자식 계층에서 MonsterSpawnPoint 자동 수집")]
    [SerializeField] private List<MonsterSpawnPoint> spawnPoints = new List<MonsterSpawnPoint>();

    [Header("옵션")]
    [SerializeField] private bool autoSpawnOnStart = true;
    [Tooltip("동시 생존 상한(0 이하 = 무제한)")]
    [SerializeField] private int maxAlive = 0;

    private readonly List<NetworkObject> _alive = new List<NetworkObject>();

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (!IsServer) return;

        if (spawnPoints == null || spawnPoints.Count == 0)
            spawnPoints = new List<MonsterSpawnPoint>(GetComponentsInChildren<MonsterSpawnPoint>(true));

        if (autoSpawnOnStart)
            SpawnWave();
    }

    // 등록된 모든 스폰 지점에서 몬스터 스폰(서버 전용). 스폰된 마리 수 반환.
    public int SpawnWave()
    {
        if (!IsServer) return 0;

        int total = 0;
        foreach (MonsterSpawnPoint point in spawnPoints)
        {
            if (point == null) continue;
            total += SpawnAt(point);
        }
        return total;
    }

    // 특정 스폰 지점에서 몬스터 스폰(서버 전용). 스폰된 마리 수 반환.
    public int SpawnAt(MonsterSpawnPoint point)
    {
        if (!IsServer || point == null) return 0;

        GameObject prefab = point.MonsterPrefabOverride != null ? point.MonsterPrefabOverride : defaultMonsterPrefab;
        if (prefab == null)
        {
            Debug.LogError($"[MonsterSpawner] 스폰할 프리팹이 없습니다. (point={point.name})", this);
            return 0;
        }

        int spawned = 0;
        for (int i = 0; i < point.Count; i++)
        {
            if (maxAlive > 0 && CountAlive() >= maxAlive)
                break;

            NetworkObject netObj = SpawnOne(prefab, point.GetSpawnPosition(i), point.transform.rotation);
            if (netObj != null) spawned++;
        }
        return spawned;
    }

    // 프리팹 1건을 지정 위치/회전으로 스폰(서버 전용).
    public NetworkObject SpawnOne(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (!IsServer || prefab == null) return null;

        GameObject go = Instantiate(prefab, position, rotation);
        NetworkObject netObj = go.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Debug.LogError($"[MonsterSpawner] 프리팹 '{prefab.name}'에 NetworkObject가 없습니다.", this);
            Destroy(go);
            return null;
        }

        netObj.Spawn();
        _alive.Add(netObj);
        return netObj;
    }

    // 생존 몬스터 수(디스폰된 항목 정리 포함).
    public int CountAlive()
    {
        _alive.RemoveAll(no => no == null || !no.IsSpawned);
        return _alive.Count;
    }

    public override void OnNetworkDespawn()
    {
        // 스포너가 내려갈 때 추적 목록만 정리(개별 몬스터 수명은 각자 관리).
        if (IsServer)
            _alive.Clear();
        base.OnNetworkDespawn();
    }
}
