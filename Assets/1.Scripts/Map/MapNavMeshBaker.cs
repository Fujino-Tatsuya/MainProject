using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

// 생성맵 NavMesh 런타임 베이커 (PLAN 2026-07-21 §4).
//
// 존이 런타임에 생성되므로 에디터 사전 베이크가 불가능하다 — MapGenerator.OnGenerated(배치 완료)를
// 구독해 그 시점에 NavMeshSurface를 빌드한다.
//
// 순서 주의: 몬스터 스폰(SpawnPlacements 내부)이 베이크보다 먼저다. 스폰 직후 에이전트는
// isOnNavMesh=false로 남고, 이후 메시가 생겨도 자동 재부착되지 않는다 → 베이크 후 Warp로 재부착.
[RequireComponent(typeof(NavMeshSurface))]
public class MapNavMeshBaker : MonoBehaviour
{
    [SerializeField] private NavMeshSurface surface;

    private void Awake()
    {
        if (surface == null) surface = GetComponent<NavMeshSurface>();

        // 베이크 설정은 코드로 강제 — 씬 세팅 실수 원천 차단.
        //  - PhysicsColliders: 콜라이더 기반 수집. 물 플레인(콜라이더 없음)엔 메시가 안 깔리고,
        //    벽 콜라이더가 통행을 정확히 차단한다. (fbx addColliders + 저작 도구로 콜라이더 확보됨)
        //  - Default 레이어만: 몬스터(Enemy)/플레이어(Player)가 베이크 전에 존재해도 지오메트리에 안 섞임.
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.collectObjects = CollectObjects.All;
        surface.layerMask = LayerMask.GetMask("Default");
    }

    private void OnEnable() { MapGenerator.OnGenerated += HandleGenerated; }
    private void OnDisable() { MapGenerator.OnGenerated -= HandleGenerated; }

    private void HandleGenerated(MapGenerator gen)
    {
        // 순수 클라는 이동 권한이 없어(에이전트 비활성) 베이크 불필요 — 서버/호스트/오프라인만.
        var nm = Unity.Netcode.NetworkManager.Singleton;
        if (nm != null && nm.IsListening && !nm.IsServer) return;

        if (surface == null) return;
        surface.BuildNavMesh();

        ReattachAgents();
        Debug.Log("[MapNavMeshBaker] NavMesh 베이크 완료 + 에이전트 재부착.");
    }

    // 베이크 이전에 스폰된 몬스터 에이전트를 새 메시에 재부착(Warp).
    private static void ReattachAgents()
    {
        foreach (NavMeshAgent agent in Object.FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.None))
        {
            if (agent == null || !agent.enabled || agent.isOnNavMesh) continue;
            if (NavMesh.SamplePosition(agent.transform.position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                agent.Warp(hit.position);
            else
                Debug.LogWarning($"[MapNavMeshBaker] '{agent.name}' 주변 5m에 NavMesh 없음 — 재부착 실패(스폰 마커 위치 확인 필요).", agent);
        }
    }
}
