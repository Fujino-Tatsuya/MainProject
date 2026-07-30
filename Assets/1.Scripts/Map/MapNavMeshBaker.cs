using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using VeyTrace.RuntimeSafety;

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
        //  - Default + Ground 만: 몬스터(Enemy)/플레이어(Player)가 베이크 전에 존재해도 지오메트리에 안 섞임.
        //    ⚠️ Ground 를 빼면 안 된다. c5826a3 이 보행면 833건을 Default → Ground 로 옮겼고,
        //    맵의 벽은 Wall(7)이 아니라 Default(0)에 있다. Default 만 수집하면 남는 건 벽과 소품뿐이라
        //    바닥이 하나도 안 들어가고 NavMesh 가 빈 채로 구워진다(몬스터·보스가 전혀 못 움직인다).
        //    Default ∪ Ground = 이관 이전의 Default 와 같은 집합이다.
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.collectObjects = CollectObjects.All;
        surface.layerMask = LayerMask.GetMask("Default", "Ground");
    }

    private void OnEnable() { MapGenerator.OnGenerated += HandleGenerated; }
    private void OnDisable() { MapGenerator.OnGenerated -= HandleGenerated; }

    private void HandleGenerated(MapGenerator gen) => Bake("맵 생성");

    /// <summary>
    /// 런타임에 지형이 바뀐 뒤 다시 굽는다(다리 개통 등). 서버/오프라인만.
    ///
    /// 전체 서피스를 다시 굽기 때문에 한순간 히칭이 있다. 개통은 실행당 1회성 이벤트라
    /// 지금은 수용하지만, 런타임 변형이 늘어나면 부분 갱신(NavMeshObstacle carve 등)으로 옮겨야 한다.
    /// </summary>
    public void RebakeNow(string reason) => Bake(reason);

    private void Bake(string reason)
    {
        // 순수 클라는 이동 권한이 없어(에이전트 비활성) 베이크 불필요 — 서버/호스트/오프라인만.
        var nm = Unity.Netcode.NetworkManager.Singleton;
        if (nm != null && nm.IsListening && !nm.IsServer) return;

        if (surface == null) return;

        // 다리처럼 "나중에 연결되는 지오메트리"는 **베이크 시점에 연결돼 있어야** NavMesh가 이어진다.
        // 런타임 재베이크는 이 서피스가 맵 전체(원점~x≈500)를 덮어 수백 ms 멈추므로 답이 아니다 —
        // 열린 상태로 한 번 굽고, 평상시에는 NavMeshObstacle 카브로 막는다(ZoneBridgeGate).
        // 스코프가 이 함수 안에서 왕복을 끝내므로 순서가 보장된다.
        using (ZoneBridgeGate.BakeOpenScope bridges = ZoneBridgeGate.BakeOpenScope.Begin())
        using (UnreadableMeshColliderBakeScope fallback =
               UnreadableMeshColliderBakeScope.BeginLoadedScenes())
        {
            if (fallback.ProxyCount > 0)
            {
                Debug.Log(
                    $"[MapNavMeshBaker] Replaced {fallback.ProxyCount} unreadable " +
                    "MeshCollider(s) with temporary BoxCollider bake proxies.",
                    this);
            }

            if (bridges.GateCount > 0)
                Debug.Log($"[MapNavMeshBaker] 다리 게이트 {bridges.GateCount}개를 연결 상태로 두고 굽습니다.", this);

            surface.BuildNavMesh();
        }

        ReattachAgents();
        Debug.Log($"[MapNavMeshBaker] NavMesh 베이크 완료 + 에이전트 재부착 (사유: {reason}).");
    }

    // 재부착 허용 거리. 이보다 멀리 옮기지 않는다 — 넓게 잡으면 틈을 건너뛴다.
    private const float ReattachRadius = 5f;
    private const float ReattachMaxDrift = 1.5f;

    // 베이크 이전에 스폰된 몬스터 에이전트를 새 메시에 재부착(Warp).
    //
    // ⚠️ 예전엔 5m 안에서 찾은 아무 지점으로나 Warp했다. 이 맵은 플랫폼 사이가 어두운 틈으로
    // 갈라져 있어서, 자기 발밑에 메시가 없는 몹이 **틈 건너 옆 플랫폼으로 순간이동**했다.
    // 그러면 "갈 수 없는 곳에 몬스터가 있다"가 되고, 거기서부터는 정상 경로가 없다.
    // → 부착은 제자리 보정(1.5m) 수준만 허용하고, 그보다 멀면 옮기지 않고 에러로 알린다.
    //   제자리에 굳은 몹은 눈에 보이고 고칠 수 있다. 엉뚱한 섬으로 간 몹은 원인 추적이 어렵다.
    private static void ReattachAgents()
    {
        foreach (NavMeshAgent agent in Object.FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.None))
        {
            if (agent == null || !agent.enabled || agent.isOnNavMesh) continue;

            if (!NavMesh.SamplePosition(agent.transform.position, out NavMeshHit hit, ReattachRadius, NavMesh.AllAreas))
            {
                Debug.LogWarning($"[MapNavMeshBaker] '{agent.name}' 주변 {ReattachRadius}m에 NavMesh 없음 — " +
                                 "재부착 실패(스폰 마커 위치 확인 필요).", agent);
                continue;
            }

            float drift = Vector3.Distance(agent.transform.position, hit.position);
            if (drift > ReattachMaxDrift)
            {
                Debug.LogError(
                    $"[MapNavMeshBaker] '{agent.name}' 재부착을 건너뜁니다 — 가장 가까운 NavMesh가 {drift:F2}m " +
                    $"떨어져 있어(허용 {ReattachMaxDrift}m) 옮기면 틈을 건너 다른 플랫폼으로 순간이동합니다. " +
                    "이 몹의 스폰 마커가 NavMesh 밖입니다(존 저작 확인).", agent);
                continue;
            }

            agent.Warp(hit.position);
        }
    }
}
