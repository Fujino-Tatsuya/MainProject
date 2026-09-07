using System.Linq;
using Unity.AI.Navigation;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// 저작 도구 — MonsterScene 을 "보스 전투 한 사이클" 테스트 씬으로 구성한다 (PLAN P7, 2026-08-10).
//
// 멱등이다. 여러 번 실행해도 같은 결과가 나오며, 이미 있는 오브젝트는 다시 만들지 않고 값만 다시 맞춘다.
//
// ── 왜 스크립트인가 ─────────────────────────────────────────────────────────
// 활성 토글·NavMesh 재베이크·private 직렬화 필드 배선은 MCP 로 못 한다. 그리고 이 씬 구성은
// 앞으로도 몇 번 더 다시 맞출 일이 생긴다(보스 프리팹 교체·아레나 위치 조정).
//
// ── 실측으로 확정한 것 (문서 인용이 아니라 파일에서 뽑았다) ──────────────────
// 1. bossroom 의 보행면은 **로컬 y = 0.50** 이다. `BossFloorCollider` 의 BoxCollider 가
//    size.y = 1 / center.y = 0 이라 박스가 -0.5..+0.5 를 덮고 **윗면이 0.50** 이다.
//    `BossLandingPoint`·`PlayerArrivalPoints`·`BossArea` 가 전부 y = 0.50 인 것과 일치한다.
//    → 그래서 bossroom 루트를 **y = -0.50** 에 놓아 보행면을 **월드 y = 0** 으로 만든다.
//      NGO 는 PlayerPrefab 을 프리팹 좌표(원점)에 스폰하므로, 이렇게 해야 호스트든 MPPM
//      클라이언트든 **바닥 위에** 뜬다. 방을 원점에 두면 플레이어가 바닥 0.5m 안에 박힌 채
//      시작하고, 그 뒤는 디페네트레이션 운에 맡기게 된다.
//
// 2. 🔴 **NavMeshSurface 는 `Env` 에 붙어 있다**(베이크 산출물 이름이 `NavMesh-Env` 인 이유).
//    그래서 `Env` 를 통째로 끄면 NavMesh 자체가 사라진다. 여기서는 **Env 루트를 살려 두고
//    자식(Ground·Wall1~4)만 끈다.**
//    그리고 그 서피스는 `CollectObjects = Children` + `UseGeometry = RenderMeshes` 였다 —
//    bossroom 을 루트에 두면 수집 대상이 아니어서 **빈 NavMesh** 가 구워진다.
//    → `MapNavMeshBaker` 가 이미 검증한 설정(PhysicsColliders / All / Default∪Ground)으로 바꾼다.
//      Ground 를 빼면 보행면이 하나도 안 들어간다(그 주석의 경고와 같은 함정).
//
// 3. 🔴 `NetworkManager.PlayerPrefab` 과 `MonsterTestBootstrap.playerPrefab` 이 **둘 다 구
//    `Player.prefab`** 을 가리키고 있었다(guid 55ee4e06…). Paladin(af4a760f…)은 이 씬에 한 번도
//    나오지 않는다. "이미 Paladin 으로 설정됨"이라는 인수인계 기록은 사실이 아니다 — 둘 다 바꾼다.
//
// 4. `MonsterTestBootstrap` 은 Play 시 **자동 StartHost + 호스트 플레이어 스폰**을 한다.
//    PLAN 대로 이걸 비활성 보존하므로, 호스트 시작 경로는 `ForProfile` 의 "Start Host" 버튼이 된다.
public static class MonsterSceneBossSetup
{
    const string SceneName = "MonsterScene";
    const string BossRoomPrefab = "Assets/2.Prefabs/Map/Zoneprefab/bossroom.prefab";
    const string BossPrefab = "Assets/2.Prefabs/Monster/Boss/TwentyThree.prefab";
    const string PaladinPrefab = "Assets/2.Prefabs/Player/Paladin/Paladin.prefab";
    const string NavMeshAssetPath = "Assets/0.Scenes/MonsterScene/NavMesh-Env.asset";

    const string RoomName = "bossroom";
    const string RigName = "BossTestRig";
    const string ArenaName = "TwentyThreeArenaContext";

    // bossroom 보행면(로컬 0.50)을 월드 y=0 으로 내린다. 근거는 위 주석 1번.
    static readonly Vector3 RoomPosition = new Vector3(0f, -0.5f, 0f);

    // 아레나 중심이 (0.49, ?, 0.49) 이고 방 반경이 약 10.5m 다. 플레이어는 원점에서 시작하므로
    // 보스를 +Z 로 5.5m 떼어 놓는다 — 시작 즉시 붙지 않고, 추격·거리 기반 공격 선택이 관찰된다.
    static readonly Vector3 BossPosition = new Vector3(0.49f, 0f, 5.49f);

    // 끌 기존 몹 세팅. 삭제하지 않는다(PLAN: 비활성 보존).
    static readonly string[] DeactivateRoots = { "MonsterSpawner", "TestBootStrap" };

    // Env 루트는 NavMeshSurface 를 들고 있으므로 살리고, 지오메트리 자식만 끈다.
    static readonly string[] DeactivateEnvChildren = { "Ground", "Wall1", "Wall2", "Wall3", "Wall4" };

    [MenuItem("Tools/Boss/MonsterScene — 보스 전투 씬 구성 (P7)")]
    public static void Setup()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != SceneName)
        {
            Debug.LogError($"[P7] 활성 씬이 '{scene.name}' 이다. {SceneName} 을 열고 실행할 것. " +
                           "(여기서 씬을 열면 미저장 변경 대화상자가 떠서 에디터가 멈출 수 있다.)");
            return;
        }

        GameObject room = AuthorBossRoom(scene);
        if (room == null) return;

        DeactivateLegacySetup(scene);
        GameObject rig = EnsureRoot(scene, RigName);
        EnsureComponent<ForProfile>(rig);
        AuthorArena(scene);
        AuthorPlayerPrefab(scene);
        int tris = BakeNavMesh(scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        Debug.Log($"[P7] MonsterScene 구성 완료. NavMesh 삼각형 {tris}개. " +
                  "Play 하면 화면 왼쪽 위 \"Start Host\" 를 눌러야 시작된다(TestBootStrap 을 껐으므로).");
        Verify();
    }

    // ── bossroom ────────────────────────────────────────────────────────────
    static GameObject AuthorBossRoom(Scene scene)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(BossRoomPrefab);
        if (source == null)
        {
            Debug.LogError($"[P7] {BossRoomPrefab} 를 못 찾았다.");
            return null;
        }

        GameObject room = FindRoot(scene, RoomName);
        if (room == null)
        {
            room = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
            room.name = RoomName;
            Debug.Log("[P7] bossroom 인스턴스를 새로 놓았다.");
        }

        room.transform.SetPositionAndRotation(RoomPosition, Quaternion.identity);
        room.transform.localScale = Vector3.one;
        return room;
    }

    // ── 기존 몹 세팅 비활성 보존 ─────────────────────────────────────────────
    static void DeactivateLegacySetup(Scene scene)
    {
        foreach (string name in DeactivateRoots)
        {
            GameObject go = FindRoot(scene, name);
            if (go == null) { Debug.LogWarning($"[P7] 루트 '{name}' 이 없다 — 건너뛴다."); continue; }
            if (go.activeSelf) { go.SetActive(false); Debug.Log($"[P7] '{name}' 비활성(보존)."); }
        }

        GameObject env = FindRoot(scene, "Env");
        if (env == null)
        {
            Debug.LogError("[P7] 'Env' 루트가 없다 — NavMeshSurface 가 여기 붙어 있어야 한다.");
            return;
        }

        // Env 자체는 절대 끄지 않는다(NavMeshSurface 가 함께 죽는다).
        if (!env.activeSelf) env.SetActive(true);

        foreach (string childName in DeactivateEnvChildren)
        {
            Transform child = env.transform.Find(childName);
            if (child == null) { Debug.LogWarning($"[P7] Env/{childName} 이 없다 — 건너뛴다."); continue; }
            if (child.gameObject.activeSelf)
            {
                child.gameObject.SetActive(false);
                Debug.Log($"[P7] 'Env/{childName}' 비활성(보존) — bossroom 이 자기 바닥·벽을 갖고 있다.");
            }
        }
    }

    // ── 보스 스폰 주체 ──────────────────────────────────────────────────────
    //
    // BossEncounterDirector 는 쓸 수 없다. 자기 주석대로 MapScene 상주이고, 스폰이
    // BossTeleportManager 의 도착 신호(AlivePlayersArrived) 뒤에 있어서 이 씬에서는
    // "BossTeleportManager 를 찾지 못했습니다" LogError 만 내고 아무것도 스폰하지 않는다.
    // → 입장 연출은 이 씬에서 검증할 수 없다(MapScene 몫). 여기서는 전투 사이클만 본다.
    static void AuthorArena(Scene scene)
    {
        var boss = AssetDatabase.LoadAssetAtPath<GameObject>(BossPrefab);
        if (boss == null) { Debug.LogError($"[P7] {BossPrefab} 를 못 찾았다."); return; }

        GameObject arena = EnsureRoot(scene, ArenaName);
        EnsureComponent<NetworkObject>(arena);   // NetworkBehaviour 선행 요구
        var context = EnsureComponent<TwentyThreeArenaContext>(arena);
        arena.transform.position = Vector3.zero;

        var so = new SerializedObject(context);
        so.FindProperty("bossPrefab").objectReferenceValue = boss;
        so.FindProperty("bossPos").vector3Value = BossPosition;
        so.ApplyModifiedPropertiesWithoutUndo();
        Debug.Log($"[P7] {ArenaName}: bossPrefab={boss.name}, bossPos={BossPosition}.");
    }

    // ── 플레이어 프리팹 ────────────────────────────────────────────────────
    static void AuthorPlayerPrefab(Scene scene)
    {
        var paladin = AssetDatabase.LoadAssetAtPath<GameObject>(PaladinPrefab);
        if (paladin == null) { Debug.LogError($"[P7] {PaladinPrefab} 를 못 찾았다."); return; }

        NetworkManager nm = scene.GetRootGameObjects()
            .Select(r => r.GetComponentInChildren<NetworkManager>(true))
            .FirstOrDefault(c => c != null);
        if (nm == null) { Debug.LogError("[P7] 씬에 NetworkManager 가 없다."); return; }

        if (nm.NetworkConfig.PlayerPrefab != paladin)
        {
            string was = nm.NetworkConfig.PlayerPrefab != null ? nm.NetworkConfig.PlayerPrefab.name : "(없음)";
            nm.NetworkConfig.PlayerPrefab = paladin;
            EditorUtility.SetDirty(nm);
            Debug.Log($"[P7] NetworkManager.PlayerPrefab: {was} → Paladin. " +
                      "MPPM 클라이언트도 이 경로로 스폰되므로 여기를 바꿔야 2인 검증이 된다.");
        }

        // 비활성이라 지금 돌지 않지만, 나중에 켜는 사람이 구 Player 를 스폰하지 않도록 함께 맞춘다.
        var bootstrap = scene.GetRootGameObjects()
            .Select(r => r.GetComponentInChildren<MonsterTestBootstrap>(true))
            .FirstOrDefault(c => c != null);
        if (bootstrap != null)
        {
            var bso = new SerializedObject(bootstrap);
            bso.FindProperty("playerPrefab").objectReferenceValue = paladin;
            bso.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    // ── NavMesh ────────────────────────────────────────────────────────────
    static int BakeNavMesh(Scene scene)
    {
        GameObject env = FindRoot(scene, "Env");
        if (env == null) return 0;

        var surface = env.GetComponent<NavMeshSurface>();
        if (surface == null)
        {
            Debug.LogError("[P7] Env 에 NavMeshSurface 가 없다 — 베이크 대상을 못 찾았다.");
            return 0;
        }

        // MapNavMeshBaker 와 같은 설정. 근거는 파일 상단 주석 2번.
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.collectObjects = CollectObjects.All;
        surface.layerMask = LayerMask.GetMask("Default", "Ground");
        EditorUtility.SetDirty(surface);

        surface.BuildNavMesh();

        // 에디터 베이크 결과를 애셋으로 고정한다. 안 하면 씬을 다시 열 때 사라진다.
        if (surface.navMeshData != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(surface.navMeshData)))
        {
            string dir = System.IO.Path.GetDirectoryName(NavMeshAssetPath);
            if (!AssetDatabase.IsValidFolder(dir))
                AssetDatabase.CreateFolder("Assets/0.Scenes", "MonsterScene");

            AssetDatabase.DeleteAsset(NavMeshAssetPath);
            AssetDatabase.CreateAsset(surface.navMeshData, NavMeshAssetPath);
            EditorUtility.SetDirty(surface);
        }

        NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
        int tris = tri.indices.Length / 3;
        if (tris == 0)
            Debug.LogError("[P7] 🔴 NavMesh 가 빈 채로 구워졌다 — 보스는 제자리에 선다. " +
                           "수집 마스크(Default∪Ground)와 bossroom 의 BossFloorCollider 활성 여부를 볼 것.");
        return tris;
    }

    // Play 중에 호스트를 시작한다. `ForProfile` 의 "Start Host" 는 **OnGUI(IMGUI)** 라
    // Input System 으로 합성한 클릭이 닿지 않는다 — 자동화·원격 검증에는 이 경로를 쓴다.
    // 사람이 직접 할 때는 그냥 화면의 버튼을 누르면 된다.
    [MenuItem("Tools/Boss/MonsterScene — [Play 중] StartHost")]
    public static void StartHostInPlay()
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("[P7] Play 중에만 쓸 수 있다.");
            return;
        }

        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null) { Debug.LogError("[P7] NetworkManager.Singleton 이 없다."); return; }
        if (nm.IsListening) { Debug.Log("[P7] 이미 호스트가 켜져 있다 — 건너뛴다."); return; }

        bool ok = nm.StartHost();
        Debug.Log($"[P7] StartHost() = {ok}. PlayerPrefab = " +
                  $"{(nm.NetworkConfig.PlayerPrefab != null ? nm.NetworkConfig.PlayerPrefab.name : "(없음)")}");
    }

    // ── 검증 (읽기 전용) ────────────────────────────────────────────────────
    [MenuItem("Tools/Boss/MonsterScene — 구성 검증 (읽기 전용)")]
    public static void Verify()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != SceneName) { Debug.LogError($"[P7] 활성 씬이 '{scene.name}' 이다."); return; }

        var sb = new System.Text.StringBuilder("[P7] 구성 검증\n");

        GameObject room = FindRoot(scene, RoomName);
        sb.AppendLine(room == null
            ? "  ✗ bossroom 없음"
            : $"  {(room.transform.position == RoomPosition ? "✓" : "✗")} bossroom @ {room.transform.position} " +
              $"(보행면 = y {room.transform.position.y + 0.5f:0.##})");

        int pylons = Object.FindObjectsByType<BossChargingPylon>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        sb.AppendLine($"  {(pylons == 4 ? "✓" : "✗")} BossChargingPylon {pylons}개 (기대 4)");

        GameObject arena = FindRoot(scene, ArenaName);
        if (arena == null) sb.AppendLine("  ✗ TwentyThreeArenaContext 없음");
        else
        {
            var ctx = arena.GetComponent<TwentyThreeArenaContext>();
            var so = new SerializedObject(ctx);
            var prefab = so.FindProperty("bossPrefab").objectReferenceValue;
            sb.AppendLine($"  {(prefab != null ? "✓" : "✗")} ArenaContext bossPrefab={(prefab != null ? prefab.name : "비어 있음")} " +
                          $"bossPos={so.FindProperty("bossPos").vector3Value}");
            sb.AppendLine($"  {(arena.GetComponent<NetworkObject>() != null ? "✓" : "✗")} ArenaContext 에 NetworkObject");
        }

        bool forProfile = Object.FindObjectsByType<ForProfile>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 0;
        sb.AppendLine($"  {(forProfile ? "✓" : "✗")} ForProfile (Start Host 버튼)");

        NetworkManager nm = scene.GetRootGameObjects()
            .Select(r => r.GetComponentInChildren<NetworkManager>(true)).FirstOrDefault(c => c != null);
        string playerPrefab = nm != null && nm.NetworkConfig.PlayerPrefab != null ? nm.NetworkConfig.PlayerPrefab.name : "(없음)";
        sb.AppendLine($"  {(playerPrefab == "Paladin" ? "✓" : "✗")} NetworkManager.PlayerPrefab = {playerPrefab}");

        foreach (string name in DeactivateRoots)
        {
            GameObject go = FindRoot(scene, name);
            sb.AppendLine($"  {(go == null || !go.activeSelf ? "✓" : "✗")} {name} 비활성");
        }

        GameObject env = FindRoot(scene, "Env");
        sb.AppendLine($"  {(env != null && env.activeSelf ? "✓" : "✗")} Env 활성 유지(NavMeshSurface 보유)");
        if (env != null)
            foreach (string childName in DeactivateEnvChildren)
            {
                Transform c = env.transform.Find(childName);
                sb.AppendLine($"  {(c == null || !c.gameObject.activeSelf ? "✓" : "✗")} Env/{childName} 비활성");
            }

        NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
        int tris = tri.indices.Length / 3;
        sb.AppendLine($"  {(tris > 0 ? "✓" : "✗")} NavMesh 삼각형 {tris}개");
        if (tris > 0)
        {
            float minY = tri.vertices.Min(v => v.y), maxY = tri.vertices.Max(v => v.y);
            sb.AppendLine($"      높이 범위 y {minY:0.##}~{maxY:0.##} (플레이어 스폰면 0 과 맞아야 한다)");

            // 🔴 삼각형 수만 보고 "구워졌다"고 판정하면 안 된다 — 작은 섬 하나로도 0 이 아니다.
            // 방 바닥(BossFloorCollider)은 21×21m 다. 평면 범위가 그에 못 미치면 보스가 갇힌다.
            float minX = tri.vertices.Min(v => v.x), maxX = tri.vertices.Max(v => v.x);
            float minZ = tri.vertices.Min(v => v.z), maxZ = tri.vertices.Max(v => v.z);
            sb.AppendLine($"      평면 범위 x {minX:0.#}~{maxX:0.#} ({maxX - minX:0.#}m) · " +
                          $"z {minZ:0.#}~{maxZ:0.#} ({maxZ - minZ:0.#}m) — 기대 약 21m×21m");

            // 실제로 걸어다닐 지점들이 메시 위에 있는지 표본으로 확인한다.
            CheckSample(sb, "플레이어 스폰(원점)", Vector3.zero);
            CheckSample(sb, "보스 스폰", BossPosition);
            CheckSample(sb, "방 구석(+X+Z)", new Vector3(8f, 0f, 8f));
            CheckSample(sb, "방 구석(-X-Z)", new Vector3(-8f, 0f, -8f));
        }

        Debug.Log(sb.ToString());
    }

    // 그 지점 1m 안에 NavMesh 가 있는지. 스폰 지점이 메시 밖이면 에이전트가 isOnNavMesh=false 로
    // 남아 제자리에 굳는다("not close enough to the NavMesh").
    static void CheckSample(System.Text.StringBuilder sb, string label, Vector3 point)
    {
        bool ok = NavMesh.SamplePosition(point, out NavMeshHit hit, 1f, NavMesh.AllAreas);
        sb.AppendLine(ok
            ? $"      ✓ {label} {point} → 메시 {Vector3.Distance(point, hit.position):0.##}m 이내"
            : $"      ✗ {label} {point} → 1m 안에 NavMesh 없음");
    }

    // ── 유틸 ───────────────────────────────────────────────────────────────
    static GameObject FindRoot(Scene scene, string name) =>
        scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);

    static GameObject EnsureRoot(Scene scene, string name)
    {
        GameObject go = FindRoot(scene, name);
        if (go != null) return go;

        go = new GameObject(name);
        SceneManager.MoveGameObjectToScene(go, scene);
        Debug.Log($"[P7] '{name}' 을 새로 만들었다.");
        return go;
    }

    static T EnsureComponent<T>(GameObject go) where T : Component
    {
        T c = go.GetComponent<T>();
        if (c == null)
        {
            c = go.AddComponent<T>();
            Debug.Log($"[P7] '{go.name}' 에 {typeof(T).Name} 추가.");
        }
        return c;
    }
}
