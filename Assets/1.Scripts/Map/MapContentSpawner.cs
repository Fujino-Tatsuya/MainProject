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

                // 피벗 보정 — 아트 존 프리팹(2026-07 신규 9종)은 코너 피벗이라 그대로 두면
                // 존이 슬롯 중심에서 반폭만큼 밀린다(벽 컷/다리 정렬 깨짐).
                // 바닥 렌더러 바운즈 중심(XZ)을 슬롯 위치에 맞춘다. 중앙 피벗 프리팹은 delta≈0.
                CenterOnSlot(zoneGo, p.Slot.transform.position);

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

    // 존 비주얼의 둘레(벽 계열) 바운즈 중심(XZ)을 슬롯 위치로 이동 — 코너 피벗 프리팹 보정.
    // 벽이 존 경계를 정의하므로 바닥 타일(자체도 코너 피벗)보다 벽 바운즈가 정확하다.
    // 프리팹·트랜스폼이 결정적이라 같은 시드면 서버/클라 동일 결과. Y는 건드리지 않는다.
    private static void CenterOnSlot(GameObject zoneGo, Vector3 slotPos)
    {
        bool hasWall = false, hasAny = false;
        var wb = new Bounds();
        var ab = new Bounds();
        foreach (var r in zoneGo.GetComponentsInChildren<Renderer>())
        {
            if (r == null) continue;
            if (!hasAny) { ab = r.bounds; hasAny = true; } else ab.Encapsulate(r.bounds);
            if (!IsWallFamily(r.transform)) continue;
            if (!hasWall) { wb = r.bounds; hasWall = true; } else wb.Encapsulate(r.bounds);
        }
        if (!hasAny) return;

        Vector3 delta = slotPos - (hasWall ? wb.center : ab.center);
        delta.y = 0f;
        zoneGo.transform.position += delta;
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
            // 컷 창 = 다리 폭 4m + 여유 0.5m/변 (다리 규격화에 맞춤 — 7m 창은 4m 벽 모듈을 2개까지 잘랐음)
            Vector3 half = alongX ? new Vector3(2.5f, 10f, 1.5f) : new Vector3(1.5f, 10f, 2.5f);
            var box = new Bounds(new Vector3(cut.x, 2f, cut.z), half * 2f);

            int hit = 0;
            foreach (var r in rends)
            {
                if (r == null || !r.gameObject.activeSelf) continue;
                if (r.bounds.max.y < 1.2f) continue;      // 바닥/낮은 조각 유지
                if (!IsWallFamily(r.transform)) continue; // 벽/코너/문 계열만
                if (!box.Intersects(r.bounds)) continue;
                r.gameObject.SetActive(false);
                hit++;
            }
            n += hit;
#if UNITY_EDITOR
            if (hit == 0)
            {
                Renderer near = null; float best = float.MaxValue;
                foreach (var r in rends)
                {
                    if (r == null || !r.gameObject.activeSelf || !IsWallFamily(r.transform)) continue;
                    float d = (r.bounds.ClosestPoint(box.center) - box.center).magnitude;
                    if (d < best) { best = d; near = r; }
                }
                string info = near != null ? $"최근접벽 {near.name} c=({near.bounds.center.x:F1},{near.bounds.center.y:F1},{near.bounds.center.z:F1}) 거리={best:F1}m" : "벽 없음";
                Debug.LogWarning($"[WallCut] {slot.name} 컷({cut.x:F1},{cut.z:F1}) dir={dir}: 0조각 — {info}");
            }
#endif
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
