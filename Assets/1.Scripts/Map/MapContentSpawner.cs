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
        // NGO 로딩 플로우 중엔 활성 씬이 아직 소스(로비)/로딩 씬 — new GameObject는 활성 씬에 생기므로
        // 그대로 두면 소스 씬 언로드와 함께 생성물 전체가 파괴된다. 스포너가 있는 씬(MapScene)으로 이동.
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_root.gameObject, gameObject.scene);
#if UNITY_EDITOR
        // 에디터 테스트 생성물(Test Generate 메뉴)은 씬 파일에 저장하지 않는다 —
        // 실수로 씬 저장 시 존 수천 오브젝트가 박제되는 것 방지. 런타임 생성은 어차피 저장 안 됨.
        if (!Application.isPlaying)
            _root.gameObject.hideFlags = HideFlags.DontSaveInEditor;
#endif

        var nm = NetworkManager.Singleton;
        bool isServer = nm != null && nm.IsServer;

        int visuals = 0, monsters = 0;
        if (placements != null)
        {
            foreach (var p in placements)
            {
                if (p.Slot == null || p.LayoutPrefab == null) continue;

                // v11: 회전 = 슬롯에 저작된 프리팹별 YawSteps(90° 4택). 위치 = 조합별 저장 위치(있으면), 없으면 슬롯 baseline.
                // 미저작이면 임시 0°+baseline으로 배치하되 경고 — 조용한 실패 없음(저작 창으로 채워야 통로에 붙음).
                if (!p.Slot.TryGetYaw(p.LayoutPrefab, out int yawSteps))
                    Debug.LogWarning($"[MapContentSpawner] 회전 미저작 (Slot {p.Slot.SlotID} × {p.LayoutPrefab.name}) — 임시 0°. Zone Rotation Authoring 필요.");
                Vector3 pos = p.Slot.TryGetPosition(p.LayoutPrefab, out var savedPos) ? savedPos : p.Slot.transform.position;
                Quaternion rot = Quaternion.Euler(0f, yawSteps * 90f, 0f);
                GameObject zoneGo = Instantiate(p.LayoutPrefab, pos, rot, _root);

                // 저작 툴(Save Placements)이 되받을 수 있게 (SlotID, 원본 프리팹) 식별자 부착.
                var idc = zoneGo.AddComponent<GeneratedZoneIdentity>();
                idc.SlotID = p.Slot.SlotID;
                idc.SourcePrefab = p.LayoutPrefab;

                p.Slot.IsFilled = true;
                visuals++;

                // 몬스터 — 서버만 스폰 (NetworkObject → NGO 복제, 클라는 수신)
                if (isServer) monsters += SpawnMonstersFor(zoneGo, gen);
            }
        }

        Debug.Log($"[MapContentSpawner] 존 비주얼 {visuals} / 몬스터 {monsters} 스폰 (서버:{isServer}).");
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
