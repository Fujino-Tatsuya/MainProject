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
                if (p.Slot == null) continue;

                // 조용한 구멍 금지 — 여기서 건너뛰면 그 슬롯 자리는 바닥이 없어 플레이어가 떨어진다.
                if (p.LayoutPrefab == null)
                {
                    Edit.LogError(
                        $"[MapContentSpawner] Slot {p.Slot.SlotID}({p.Role}/{p.Slot.Size})에 배치할 디자인이 " +
                        "없습니다 — 그 자리는 바닥 없는 구멍으로 남습니다. 카탈로그 풀 저작을 확인하세요.");
                    continue;
                }

                // v11: 회전 = 슬롯에 저작된 프리팹별 YawSteps(90° 4택). 위치 = 조합별 저장 위치(있으면), 없으면 슬롯 baseline.
                // 미저작이면 임시 0°+baseline으로 배치하되 경고 — 조용한 실패 없음(저작 창으로 채워야 통로에 붙음).
                if (!p.Slot.TryGetYaw(p.LayoutPrefab, out int yawSteps))
                    Debug.LogWarning($"[MapContentSpawner] 회전 미저작 (Slot {p.Slot.SlotID} × {p.LayoutPrefab.name}) — 임시 0°. Zone Rotation Authoring 필요.");

                // 위치 미저작도 조용히 넘기지 않는다 — baseline으로 떨어지면 문이 통로와 어긋나
                // 바닥이 벌어진 것처럼 보이고 그 틈으로 떨어진다(시드에 따라만 나타난다).
                bool hasSavedPos = p.Slot.TryGetPosition(p.LayoutPrefab, out var savedPos);
                if (!hasSavedPos)
                    Debug.LogWarning($"[MapContentSpawner] 위치 미저작 (Slot {p.Slot.SlotID} × {p.LayoutPrefab.name}) — 슬롯 baseline 사용. Save Placements 필요.");

                Vector3 pos = hasSavedPos ? savedPos : p.Slot.transform.position;
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

                // BossRoom 역할 존: 진입 트리거(서버 판정) + 범위 표시(전 피어) 부착 — PLAN §6.
                // 존 프리팹은 비네트워크 규약이라 프리팹에 미리 넣지 않고 스폰 시 동적 부착한다.
                if (p.Slot.AssignedRole == ZoneRole.BossRoom)
                    AttachBossEnterZone(zoneGo, isServer);
            }
        }

        Edit.Log($"[MapContentSpawner] 존 비주얼 {visuals} / 몬스터 {monsters} 스폰 (서버:{isServer}).");
    }

    // BossRoom 존에 진입 판정(서버)과 범위 표시(전 피어)를 부착.
    private static void AttachBossEnterZone(GameObject zoneGo, bool isServer)
    {
        Bounds bounds = new Bounds(zoneGo.transform.position, Vector3.one * 4f);
        Renderer[] renderers = zoneGo.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length > 0)
        {
            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        }

        // 패드 크기 = BossTeleportManager 인스펙터 값(존 전체가 아니라 존 중앙의 작은 진입 패드 — 팀장 확정).
        Vector2 pad = BossTeleportManager.Instance != null
            ? BossTeleportManager.Instance.EnterPadSize
            : new Vector2(6f, 6f);

        // y는 바닥(로컬 0) 기준 고정 — 렌더 바운즈 중심을 쓰면 램프/기둥 등 높은 구조물이
        // 중심을 끌어올려 박스 바닥이 플레이어 키 위로 떠 진입 판정이 조용히 빠질 수 있다.
        Vector3 centerLocal = zoneGo.transform.InverseTransformPoint(bounds.center);
        centerLocal.y = 2f;
        Vector3 size = new Vector3(
            Mathf.Max(1f, pad.x),
            4f, // 바닥 0~4m 커버
            Mathf.Max(1f, pad.y));

        // 범위 표시(모든 피어 로컬 연출) — 대기 시안/진입 초록.
        var ringGo = new GameObject("BossEnterRing");
        ringGo.transform.SetParent(zoneGo.transform, false);
        ringGo.AddComponent<BossEnterZoneVisual>().Setup(centerLocal, new Vector2(size.x, size.z));

        if (!isServer) return;

        // 진입 판정(서버 권한).
        BoxCollider box = zoneGo.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.center = centerLocal;
        box.size = size;
        zoneGo.AddComponent<BossEnterTrigger>();
        Edit.Log($"[MapContentSpawner] BossEnter 트리거 부착 — {zoneGo.name} @ {zoneGo.transform.position} (박스 {box.size})", zoneGo);
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
                n += SpawnGroupAt(gen, zoneGo, node.MonsterGroupID, node.MonsterSpawnPoints, node.Behavior);
            }
        }
        // 전투 노드가 하나도 없으면 존 단위 마커로 폴백 (역할/단순 존 호환)
        if (combatNodes == 0)
            n += SpawnGroupAt(gen, zoneGo, layout.MonsterGroupID, layout.MonsterSpawnPoints, MonsterBehavior.Idle);
        return n;
    }

    // 그룹ID의 몬스터를 주어진 마커들에 스폰 (서버 전용 호출). 몬스터 에셋 확정 전이면 0.
    private int SpawnGroupAt(MapGenerator gen, GameObject zoneGo, int monsterGroupID, System.Collections.Generic.List<Transform> points, MonsterBehavior behavior)
    {
        if (points == null || points.Count == 0) return 0;
        GameObject monsterPrefab = ResolveMonsterPrefab(gen, monsterGroupID);
        if (monsterPrefab == null) return 0; // 몬스터 에셋 확정 전엔 마커만 두고 스킵

        int n = 0;
        foreach (var marker in points)
        {
            if (marker == null) continue;
            GameObject go = Instantiate(monsterPrefab, SnapToFloor(marker.position, zoneGo), marker.rotation);
            // TODO: 몬스터 AI 확정 후 behavior 적용 (예: go.GetComponent<MonsterAI>()?.SetBehavior(behavior)).
            var netObj = go.GetComponent<NetworkObject>();
            if (netObj != null) { netObj.Spawn(); _spawnedNetObjs.Add(netObj); } // NGO 복제 + despawn 추적
            else go.transform.SetParent(_root, true);  // 비네트워크 몬스터 → 루트 하위로 ClearGenerated가 정리(누수 방지)
            n++;
        }
        return n;
    }

    // 스폰 마커를 바닥으로 스냅. 자동 저작 마커가 구덩이/허공 위에 찍히면 몹이 공중에 떠서
    // 방치된다(터렛은 이동이 없어 영구 부유) — 실패 시 존 중심 바닥으로 폴백하고 경고를 남긴다.
    private static Vector3 SnapToFloor(Vector3 position, GameObject zoneGo)
    {
        int mask = LayerMask.GetMask("Default");
        if (Physics.Raycast(position + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 30f, mask, QueryTriggerInteraction.Ignore))
            return hit.point + Vector3.up * 0.05f;

        Vector3 center = zoneGo != null ? zoneGo.transform.position : position;
        if (Physics.Raycast(center + Vector3.up * 5f, Vector3.down, out hit, 30f, mask, QueryTriggerInteraction.Ignore))
        {
            Edit.LogWarning($"[MapContentSpawner] 스폰 마커가 허공({position}) — {zoneGo?.name} 중심 바닥으로 대체. 마커 위치 조정 필요.");
            return hit.point + Vector3.up * 0.05f;
        }

        Edit.LogWarning($"[MapContentSpawner] 스폰 마커·존 중심 모두 바닥 없음({position}) — 원위치 스폰. {zoneGo?.name} 확인 필요.");
        return position;
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
