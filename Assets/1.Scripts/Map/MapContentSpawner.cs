using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

// 배치 결과(ZonePlacement)를 실제 인스턴스로 구현한다.
//  - 존 레이아웃 프리팹(바닥/벽/테마/노드 마커) = 비네트워크 시각물 → 서버/클라 각자 로컬 생성
//    (같은 시드라 결과 동일). 규약: 이 프리팹에는 NetworkObject를 두지 않는다.
//  - 몬스터 = 서버만 ZoneLayout의 스폰 마커 위치에 NetworkObject 프리팹을 Spawn() → NGO 복제.
public class MapContentSpawner : MonoBehaviour
{
    public const string RootName = "GeneratedMap";

    private Transform _root;
    private readonly List<NetworkObject> _spawnedNetObjs = new List<NetworkObject>();

    public void SpawnPlacements(MapGenerator gen, List<ZonePlacement> placements)
    {
        ClearGenerated();
        _root = new GameObject(RootName).transform;
#if UNITY_EDITOR
        // 에디터 테스트 생성물(Test Generate 메뉴)은 씬 파일에 저장하지 않는다 —
        // 실수로 씬 저장 시 존 수천 오브젝트가 박제되는 것 방지. 런타임 생성은 어차피 저장 안 됨.
        if (!Application.isPlaying)
            _root.gameObject.hideFlags = HideFlags.DontSaveInEditor;
#endif

        var nm = NetworkManager.Singleton;
        bool isServer = nm != null && nm.IsServer;

        int visuals = 0, monsters = 0, wallCuts = 0;
        if (placements != null)
        {
            foreach (var p in placements)
            {
                if (p.Slot == null || p.LayoutPrefab == null) continue;

                // 존 비주얼 — 양쪽 로컬 생성 (슬롯 앵커 위치/방향 + 출입구 매칭 회전)
                Quaternion rot = p.Slot.transform.rotation * Quaternion.Euler(0f, p.ExtraYawSteps * 90f, 0f);
                GameObject zoneGo = Instantiate(p.LayoutPrefab,
                    p.Slot.transform.position, rot, _root);
                p.Slot.IsFilled = true;
                visuals++;

                // 벽 변으로 붙는 다리 입구 자리의 벽 조각 삭제(통로 뚫기) —
                // 슬롯/프리팹/회전이 결정적이라 서버·클라 동일 결과.
                wallCuts += CutWallsForSlot(zoneGo, p.Slot);

                // 몬스터 — 서버만 스폰 (NetworkObject → NGO 복제, 클라는 수신)
                if (isServer) monsters += SpawnMonstersFor(zoneGo, gen);
            }
        }

        Debug.Log($"[MapContentSpawner] 존 비주얼 {visuals} / 벽 컷 {wallCuts}조각 / 몬스터 {monsters} 스폰 (서버:{isServer}).");
    }

    // 벽 변으로 붙는 다리 입구(ZoneSlot.WallCuts) 자리의 벽/코너/문 조각을 비활성화해 통로를 뚫는다.
    // 프리팹·트랜스폼·순회 순서가 결정적이라 같은 시드면 서버/클라 동일하게 잘린다.
    private static int CutWallsForSlot(GameObject zoneGo, ZoneSlot slot)
    {
        if (slot.WallCuts == null || slot.WallCuts.Count == 0) return 0;

        int n = 0;
        var rends = zoneGo.GetComponentsInChildren<Renderer>();
        foreach (var cut in slot.WallCuts)
        {
            int dir = Mathf.RoundToInt(cut.w);
            bool alongX = dir == 0 || dir == 2; // N/S 변 = 변이 X축으로 진행
            Vector3 half = alongX ? new Vector3(3.5f, 10f, 1.5f) : new Vector3(1.5f, 10f, 3.5f);
            var box = new Bounds(new Vector3(cut.x, 2f, cut.z), half * 2f);

            foreach (var r in rends)
            {
                if (r == null || !r.gameObject.activeSelf) continue;
                if (r.bounds.max.y < 1.2f) continue;      // 바닥/낮은 조각 유지
                if (!IsWallFamily(r.transform)) continue; // 벽/코너/문 계열만
                if (!box.Intersects(r.bounds)) continue;
                r.gameObject.SetActive(false);
                n++;
            }
        }
        return n;
    }

    private static bool IsWallFamily(Transform t)
    {
        for (var c = t; c != null; c = c.parent)
        {
            string nm = c.name.ToLowerInvariant();
            if (nm.Contains("wall") || nm.Contains("corner") || nm.Contains("door")) return true;
        }
        return false;
    }

    private int SpawnMonstersFor(GameObject zoneGo, MapGenerator gen)
    {
        var layout = zoneGo.GetComponent<ZoneLayout>();
        if (layout == null) return 0;

        int n = 0;
        int combatNodes = 0;
        // 노드 단위 스폰 — 전투 노드(CombatNode)만 몬스터 스폰.
        if (layout.Nodes != null && layout.Nodes.Count > 0)
        {
            foreach (var node in layout.Nodes)
            {
                if (node == null || node.ContentType != NodeContentType.CombatNode) continue;
                combatNodes++;
                n += SpawnGroupAt(gen, node.MonsterGroupID, node.MonsterSpawnPoints, node.Behavior);
            }
        }
        // 전투 노드가 하나도 없으면 존 단위 마커로 폴백 (역할/단순 존 호환)
        if (combatNodes == 0)
            n += SpawnGroupAt(gen, layout.MonsterGroupID, layout.MonsterSpawnPoints, MonsterBehavior.Idle);
        return n;
    }

    // 그룹ID의 몬스터를 주어진 마커들에 스폰 (서버 전용 호출). 몬스터 에셋 확정 전이면 0.
    private int SpawnGroupAt(MapGenerator gen, int monsterGroupID, System.Collections.Generic.List<Transform> points, MonsterBehavior behavior)
    {
        if (points == null || points.Count == 0) return 0;
        GameObject monsterPrefab = ResolveMonsterPrefab(gen, monsterGroupID);
        if (monsterPrefab == null) return 0; // 몬스터 에셋 확정 전엔 마커만 두고 스킵

        int n = 0;
        foreach (var marker in points)
        {
            if (marker == null) continue;
            GameObject go = Instantiate(monsterPrefab, marker.position, marker.rotation);
            // TODO: 몬스터 AI 확정 후 behavior 적용 (예: go.GetComponent<MonsterAI>()?.SetBehavior(behavior)).
            var netObj = go.GetComponent<NetworkObject>();
            if (netObj != null) { netObj.Spawn(); _spawnedNetObjs.Add(netObj); } // NGO 복제 + despawn 추적
            else go.transform.SetParent(_root, true);  // 비네트워크 몬스터 → 루트 하위로 ClearGenerated가 정리(누수 방지)
            n++;
        }
        return n;
    }

    // MonsterGroupID → 프리팹 (Config.MonsterGroups). 실제 몬스터 에셋은 추후.
    private static GameObject ResolveMonsterPrefab(MapGenerator gen, int groupId)
    {
        var cfg = gen != null ? gen.Config : null;
        if (cfg == null || cfg.MonsterGroups == null) return null;
        foreach (var g in cfg.MonsterGroups)
            if (g.GroupID == groupId) return g.MonsterPrefab;
        return null;
    }

    // 이전 생성물 제거 (재생성/디버그). 서버라면 네트워크 오브젝트도 despawn.
    public void ClearGenerated()
    {
        foreach (var netObj in _spawnedNetObjs)
        {
            if (netObj != null && netObj.IsSpawned) netObj.Despawn();
            else if (netObj != null) Destroy(netObj.gameObject);
        }
        _spawnedNetObjs.Clear();

        var existing = GameObject.Find(RootName);
        if (existing != null)
        {
            if (Application.isPlaying) Destroy(existing);
            else DestroyImmediate(existing);
        }
    }
}
