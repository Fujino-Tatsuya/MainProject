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
                if (isServer) monsters += SpawnFromZoneSpawner(zoneGo);

                // BossRoom 역할 존: 진입 트리거(서버 판정) + 범위 표시(전 피어) 부착 — PLAN §6.
                // 존 프리팹은 비네트워크 규약이라 프리팹에 미리 넣지 않고 스폰 시 동적 부착한다.
                if (p.Slot.AssignedRole == ZoneRole.BossRoom)
                    AttachBossEnterZone(zoneGo, isServer);

                // 다리 개통 장치(ZoneL_typeB 등): 존 프리팹이 저작 데이터·로컬 연출을 들고 있고,
                // 상태 복제·판정은 씬 상주 ZoneBridgeGateManager가 (SlotID, 패널 인덱스) 키로 맡는다.
                // 존이 비네트워크라 패널·다리에 NetworkBehaviour를 붙일 수 없기 때문이다.
                RegisterBridgeGate(zoneGo, p.Slot.SlotID);
            }
        }

        Edit.Log($"[MapContentSpawner] 존 비주얼 {visuals} / 몬스터 {monsters} 스폰 (서버:{isServer}).");
    }

    // 존 프리팹이 다리 개통 장치를 들고 있으면 씬 매니저에 등록한다(전 피어 — 링 표시·다리 보간은
    // 양쪽 로컬이고 판정만 서버다). 매니저가 없으면 조용히 꺼지지 않게 경고한다.
    private static void RegisterBridgeGate(GameObject zoneGo, int slotID)
    {
        var gate = zoneGo.GetComponent<ZoneBridgeGate>();
        if (gate == null) return;

        gate.SetSlotID(slotID);

        if (ZoneBridgeGateManager.Instance != null)
        {
            ZoneBridgeGateManager.Instance.RegisterGate(gate);
            return;
        }

        Edit.LogError(
            $"[MapContentSpawner] Slot {slotID}에 다리 개통 장치가 있는데 씬에 ZoneBridgeGateManager가 " +
            "없습니다 — F 상호작용과 다리가 동작하지 않습니다. MapScene에 매니저를 배치하세요.", gate);
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

    /// <summary>
    /// 존 프리팹에 저작된 <see cref="MonsterSpawner"/> 를 서버가 대신 실행한다.
    /// 아트가 존 프리팹에 스포너를 붙이고 자식으로 <see cref="MonsterSpawnPoint"/> 를 놓는 저작 방식.
    ///
    /// 🔴 <b>왜 스포너의 <c>SpawnWave()</c> 를 부르지 않는가</b> (2026-08-18 실측):
    /// <c>NetworkBehaviour.IsServer</c> 는 계산 프로퍼티가 아니라 <b>네트워크 스폰 때 세팅되는
    /// 자동 프로퍼티</b>다. 존은 「비네트워크 규약」(이 파일 헤더)이라 <c>Spawn()</c> 되지 않으므로
    /// 존에 붙은 스포너의 <c>IsServer</c> 는 <b>영원히 false</b> 이고, <c>SpawnWave()</c>·<c>SpawnAt()</c>
    /// 이 첫 줄에서 그대로 return 한다. 존에 <c>NetworkObject</c> 를 붙여도 아무도 스폰해 주지 않아
    /// 결과가 같다. 그래서 마커만 읽어 <b>여기서</b> 스폰한다 — 존 규약을 깨지 않는 쪽이다.
    ///
    /// 스폰 자체는 기존 경로(<see cref="SpawnMonsterInstance"/>)를 그대로 쓴다. 바닥 스냅·NGO 복제·
    /// 정리 추적이 마커 경로와 동일해진다.
    /// </summary>
    private int SpawnFromZoneSpawner(GameObject zoneGo)
    {
        var spawner = zoneGo.GetComponentInChildren<MonsterSpawner>(true);
        if (spawner == null) return 0;

        int n = 0;
        foreach (MonsterSpawnPoint point in spawner.ResolveSpawnPoints())
        {
            if (point == null) continue;

            // 지점별 지정이 우선, 없으면 스포너의 기본 몬스터(스포너 자신의 규약과 동일).
            GameObject prefab = point.MonsterPrefabOverride != null
                ? point.MonsterPrefabOverride
                : spawner.DefaultMonsterPrefab;
            if (prefab == null)
            {
                Edit.LogWarning(
                    $"[MapContentSpawner] {zoneGo.name}/{point.name} 에 스폰할 프리팹이 없다 — " +
                    "MonsterSpawner 의 Default Monster Prefab 또는 지점의 Override 를 채울 것.");
                continue;
            }

            for (int i = 0; i < point.Count; i++)
            {
                if (!TryResolveSpawnPoint(point.GetSpawnPosition(i), zoneGo, out Vector3 spawnPoint))
                    continue;

                SpawnMonsterInstance(prefab, spawnPoint, point.transform.rotation);
                n++;
            }
        }
        return n;
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
            n += SpawnEntriesAt(gen, zoneGo, layout, MonsterBehavior.Idle);
        return n;
    }

    /// <summary>
    /// 마커별 그룹 ID로 스폰한다. 예전 <see cref="SpawnGroupAt"/>는 그룹 하나를 한 번만 해석해
    /// <b>모든 마커에 같은 몬스터</b>를 세웠다 — 마커마다 다른 몬스터를 섞으려면 마커 단위로 해석해야 한다.
    /// </summary>
    private int SpawnEntriesAt(MapGenerator gen, GameObject zoneGo, ZoneLayout layout, MonsterBehavior behavior)
    {
        int n = 0;
        int index = -1;

        foreach (MonsterSpawnEntry entry in layout.ResolveSpawnEntries())
        {
            index++;

            if (entry.Marker == null)
                continue;

            int groupId = entry.MonsterGroupID >= 0 ? entry.MonsterGroupID : layout.MonsterGroupID;

            GameObject monsterPrefab = ResolveMonsterPrefab(gen, groupId);
            if (monsterPrefab == null)
            {
                // 예전에는 여기서 조용히 return 0 이었다 — 마커는 있는데 몹이 없는 상태가 로그 없이 만들어졌다.
                // (MonsterGroupID 0 은 MapGenConfig 에서 MonsterPrefab 이 비어 있고, 기본값은 -1이다.)
                Edit.LogWarning(
                    $"[MapContentSpawner] {zoneGo?.name} 마커 {index}({entry.Marker.name})의 몬스터 그룹 " +
                    $"{groupId} 을 해석하지 못해 스폰하지 않습니다 — MapGenConfig.MonsterGroups 확인 필요.");
                continue;
            }

            if (!TryResolveSpawnPoint(entry.Marker.position, zoneGo, out Vector3 spawnPoint))
                continue;

            SpawnMonsterInstance(monsterPrefab, spawnPoint, entry.Marker.rotation);
            n++;
        }

        return n;
    }

    // 스폰 인스턴스화 + 네트워크 등록/정리 추적. 두 스폰 경로가 공유한다.
    private void SpawnMonsterInstance(GameObject monsterPrefab, Vector3 spawnPoint, Quaternion rotation)
    {
        GameObject go = Instantiate(monsterPrefab, spawnPoint, rotation);

        var netObj = go.GetComponent<NetworkObject>();
        if (netObj != null) { netObj.Spawn(); _spawnedNetObjs.Add(netObj); } // NGO 복제 + despawn 추적
        else go.transform.SetParent(_root, true);  // 비네트워크 몬스터 → 루트 하위로 ClearGenerated가 정리(누수 방지)
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

            // 바닥을 못 찾으면 스폰하지 않는다 — 공중에 스폰된 몹은 중력으로 어비스로 떨어진다.
            if (!TryResolveSpawnPoint(marker.position, zoneGo, out Vector3 spawnPoint))
                continue;

            // TODO: 몬스터 AI 확정 후 behavior 적용 (예: go.GetComponent<MonsterAI>()?.SetBehavior(behavior)).
            SpawnMonsterInstance(monsterPrefab, spawnPoint, marker.rotation);
            n++;
        }
        return n;
    }

    // 스폰 마커를 바닥으로 스냅한다. 자동 저작 마커가 구덩이/허공 위에 찍히거나, 존이 통로와
    // 어긋난 위치에 배치되면(슬롯×프리팹 위치 미저작) 마커 아래에 바닥이 없다.
    //
    // ⚠️ 이전에는 마커 → 존 transform 원점 순으로 찾고, 둘 다 실패하면 **원위치에 그냥 스폰**했다.
    // 그러면 몹이 공중에서 중력으로 어비스로 떨어진다(실제 Play 로그: Zone_typeQuest02 마커 2개).
    // 존 transform 원점은 회전·어긋난 배치에서 바닥 위가 아닐 수 있으므로, 폴백은 **존 렌더러
    // 바운즈 위**에서 쏜다 — 존 자신의 바닥은 언제나 자기 바운즈 안에 있다.
    private static bool TryResolveSpawnPoint(Vector3 markerPosition, GameObject zoneGo, out Vector3 result)
    {
        // ⚠️ Default 단독이면 안 된다. c5826a3 이 보행면 833건을 Default → Ground 로 옮겼기 때문에
        // Default 만 쏘면 두 레이캐스트가 모두 빗나가고, 호출부가 `continue` 하므로 **몬스터가 한 마리도
        // 스폰되지 않는다.** Default ∪ Ground = 이관 이전의 Default 와 같은 집합이다.
        // (본래 규칙은 GroundProbe.TryFindGround 로 통일하는 것 — 여기는 Unit 제외·최빈값 판정이
        //  스폰 스냅에 필요한지 확인이 안 됐으므로 마스크만 맞춰 두고 후속으로 남긴다.)
        int mask = LayerMask.GetMask("Default", "Ground");

        if (Physics.Raycast(markerPosition + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 30f, mask, QueryTriggerInteraction.Ignore))
        {
            result = hit.point + Vector3.up * 0.05f;
            return true;
        }

        if (TryGetZoneBounds(zoneGo, out Bounds zoneBounds))
        {
            Vector3 origin = new Vector3(zoneBounds.center.x, zoneBounds.max.y + 2f, zoneBounds.center.z);
            float distance = zoneBounds.size.y + 12f;
            if (Physics.Raycast(origin, Vector3.down, out hit, distance, mask, QueryTriggerInteraction.Ignore))
            {
                Edit.LogWarning(
                    $"[MapContentSpawner] 스폰 마커가 허공({markerPosition}) — {zoneGo?.name} 바닥 중앙으로 대체. 마커 위치 조정 필요.");
                result = hit.point + Vector3.up * 0.05f;
                return true;
            }
        }

        Edit.LogError(
            $"[MapContentSpawner] {zoneGo?.name}에서 바닥을 찾지 못해 몬스터를 스폰하지 않습니다({markerPosition}) — " +
            "존이 통로와 어긋난 위치에 배치됐을 가능성이 큽니다(Validate Slot Authoring 확인).");
        result = markerPosition;
        return false;
    }

    private static bool TryGetZoneBounds(GameObject zoneGo, out Bounds bounds)
    {
        bounds = default;
        if (zoneGo == null) return false;

        Renderer[] renderers = zoneGo.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return false;

        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return true;
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
