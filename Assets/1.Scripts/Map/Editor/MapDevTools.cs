using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

// 개발/디버그용 메뉴: 임시 ZoneVolume 배치 + 에디터 생성 테스트.
public static class MapDevTools
{
    // 씬 바운드에 맞춰 10개 ZoneVolume을 4열 그리드로 임시 배치한다.
    // (위치·크기는 임시 — 인스펙터에서 각 영역 위로 옮기고 조정)
    [MenuItem("VeyTrace/Map/Create Temp Zone Volumes")]
    public static void CreateTempVolumes()
    {
        // 실제 맵 지오메트리가 씬에 (거의) 없으므로, 넓은 고정 그리드로 펼쳐 둔다.
        // 씬 콘텐츠 중심 근처에 두되 크기는 고정값으로 넉넉히 → 스폰포인트 기즈모가 겹치지 않음.
        Bounds sceneBounds = ComputeSceneBounds();
        Vector3 center = sceneBounds.size == Vector3.zero ? Vector3.zero : sceneBounds.center;

        const int cols = 4;
        const int count = 10;
        const float cellW = 50f; // 칸 간격(가로)
        const float cellD = 40f; // 칸 간격(세로)
        int rows = Mathf.CeilToInt(count / (float)cols);

        float originX = center.x - (cols - 1) * cellW * 0.5f;
        float originZ = center.z + (rows - 1) * cellD * 0.5f;

        var existing = GameObject.Find("ZoneVolumes");
        if (existing != null) Object.DestroyImmediate(existing);
        var parent = new GameObject("ZoneVolumes");

        int created = 0;
        for (int i = 1; i <= count; i++)
        {
            var zone = AssetDatabase.LoadAssetAtPath<ZoneDefinitionSO>(MapEditorPaths.ZoneDefPath(i));
            int col = (i - 1) % cols;
            int row = (i - 1) / cols;
            Vector3 pos = new Vector3(originX + col * cellW, center.y, originZ - row * cellD);

            var go = new GameObject($"ZoneVolume_{i}" + (zone != null ? $"_{zone.ZoneName}" : ""));
            go.transform.SetParent(parent.transform, false);
            go.transform.position = pos;

            var vol = go.AddComponent<ZoneVolume>();
            vol.Zone = zone;
            vol.Size = new Vector3(cellW * 0.75f, 1f, cellD * 0.75f); // 칸의 75% → 칸끼리 여백
            bool isA = zone != null && zone.DefaultGrade == ZoneGrade.A_UpToTier1;
            vol.Tier1Count = isA ? 1 : 0;   // 1티어 = 존당 1개
            vol.Tier2Count = 4;
            vol.Tier3Count = 5;
            created++;
        }

        EditorSceneManager.MarkSceneDirty(parent.scene);
        Debug.Log($"[TempVolumes] ZoneVolume {created}개 임시 배치 (그리드 {cols}열, 칸 {cellW}x{cellD}, 중심 {center}). 위치/크기/카운트는 인스펙터에서 조정.");
    }

    // 레벨디자인 스크린샷 기준 영역 1~10 배치 (픽셀 측정 → 0.1 스케일, 맵 전체 ≈ 167×140).
    // 기존 ZoneVolume은 위치/크기만 갱신(티어 카운트 등 인스펙터 수정값 보존), 없으면 생성.
    [MenuItem("VeyTrace/Map/Apply Zone Layout (LevelDesign)")]
    public static void ApplyZoneLayout()
    {
        // (존번호, 중심, 크기) — 스크린샷 비율. 실제 Stage1 맵 스케일이 다르면 전체 이동/스케일만 조정.
        // 크기는 측정값 -3 (존 사이 최소 3유닛 간격 → 벽 겹침 방지, 통로 빌더가 사이를 연결)
        var layout = new (int zone, Vector3 pos, Vector3 size)[]
        {
            (1,  new Vector3(-30.5f, 0f,  13.7f), new Vector3(53f, 1f, 51f)), // 좌측 대형(분수)
            (2,  new Vector3( 24.0f, 0f,  42.7f), new Vector3(50f, 1f, 51f)), // 상단 중앙 대형(병원)
            (3,  new Vector3( 56.0f, 0f, -38.3f), new Vector3(52f, 1f, 43f)), // 우하단 대형
            (4,  new Vector3( 11.0f, 0f,  -9.8f), new Vector3(22f, 1f, 47f)), // 중앙 세로 긴 영역
            (5,  new Vector3(-17.5f, 0f, -37.8f), new Vector3(21f, 1f, 42f)), // 중앙좌측 세로 긴 영역
            (6,  new Vector3(-18.5f, 0f,  57.2f), new Vector3(25f, 1f, 22f)), // 좌최상단 소형(보스후보)
            (7,  new Vector3( 67.5f, 0f,  29.2f), new Vector3(23f, 1f, 26f)), // 우최상단 소형(보스후보)
            (8,  new Vector3( 13.0f, 0f, -58.3f), new Vector3(26f, 1f, 21f)), // 중앙최하단 소형(보스후보)
            (9,  new Vector3(-58.5f, 0f, -32.3f), new Vector3(47f, 1f, 27f)), // 좌하단 가로 긴 영역
            (10, new Vector3( 57.5f, 0f,  -0.8f), new Vector3(49f, 1f, 26f)), // 우중앙 가로 긴 영역
        };

        var volumes = Object.FindObjectsByType<ZoneVolume>(FindObjectsSortMode.None);
        Transform parent = GameObject.Find("ZoneVolumes")?.transform;
        if (parent == null) parent = new GameObject("ZoneVolumes").transform;

        int moved = 0, created = 0;
        foreach (var entry in layout)
        {
            var zoneAsset = AssetDatabase.LoadAssetAtPath<ZoneDefinitionSO>(MapEditorPaths.ZoneDefPath(entry.zone));
            if (zoneAsset == null) { Debug.LogWarning($"[ZoneLayout] ZoneDef_{entry.zone} 못 찾음"); continue; }

            ZoneVolume vol = null;
            foreach (var v in volumes)
                if (v.Zone == zoneAsset) { vol = v; break; }

            if (vol == null)
            {
                var go = new GameObject($"ZoneVolume_{entry.zone}_{zoneAsset.ZoneName}");
                go.transform.SetParent(parent, false);
                vol = go.AddComponent<ZoneVolume>();
                vol.Zone = zoneAsset;
                // 신규 생성 시 기본 카운트 (기존 볼륨은 인스펙터 값 보존)
                vol.Tier1Count = zoneAsset.DefaultGrade == ZoneGrade.A_UpToTier1 ? 1 : 0; // 1티어 = 존당 1개
                vol.Tier2Count = 4;
                vol.Tier3Count = 5;
                created++;
            }
            else moved++;

            vol.transform.position = entry.pos;
            vol.Size = entry.size;
            EditorUtility.SetDirty(vol);
        }

        EditorSceneManager.MarkSceneDirty(parent.gameObject.scene);
        Debug.Log($"[ZoneLayout] 적용 완료 — 갱신 {moved} / 생성 {created}. 이후 'Scatter Spawn Points' 재실행 필요. (씬 저장 필요)");
    }

    // MapGenerator를 찾아 랜덤 시드로 Generate 실행 + 결과 요약 로그.
    [MenuItem("VeyTrace/Map/Test Generate")]
    public static void TestGenerate()
    {
        var mg = Object.FindFirstObjectByType<MapGenerator>();
        if (mg == null)
        {
            Debug.LogWarning("[TestGenerate] 씬에 MapGenerator 없음 — 먼저 'Setup Scene Generator' 실행.");
            return;
        }

        var results = mg.Generate(System.Environment.TickCount, 0); // 난이도 Lv0 (baseline)

        var byKey = new Dictionary<string, int>();
        foreach (var r in results)
        {
            string key = $"{(r.Slot != null ? r.Slot.Size.ToString() : "?")}/{r.Role}";
            byKey.TryGetValue(key, out int c);
            byKey[key] = c + 1;
        }

        var sb = new System.Text.StringBuilder();
        sb.Append($"[TestGenerate] 배치 {results.Count}개 — ");
        foreach (var kv in byKey) sb.Append($"{kv.Key}:{kv.Value}  ");
        Debug.Log(sb.ToString());
    }

    // 오버뷰 UI 요소 덤프 (디버그 — 비정상 크기 요소 추적용)
    [MenuItem("VeyTrace/Map/Dump Overview Children")]
    public static void DumpOverviewChildren()
    {
        var ui = Object.FindFirstObjectByType<MapOverviewUI>();
        var area = ui != null ? ui.transform.Find("MapOverviewCanvas/MapArea") : null;
        if (area == null) { Debug.LogWarning("[Dump] MapArea 없음 — UI를 먼저 열 것."); return; }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[Dump] MapArea 자식 {area.childCount}개 (이름 | size | pos):");
        foreach (Transform child in area)
        {
            var rt = child as RectTransform;
            if (rt == null) continue;
            sb.AppendLine($"  {child.name} | {rt.sizeDelta.x:F0}x{rt.sizeDelta.y:F0} | ({rt.anchoredPosition.x:F0},{rt.anchoredPosition.y:F0})");
        }
        Debug.Log(sb.ToString());
    }

    // 네트워크 통합 배선: MapScene을 빌드 세팅에 추가 + MapNetworkSync(NetworkObject) 배치
    // (로딩 플로우의 targetSceneName=MapScene 과 한 세트 — NGO가 MapScene 로드 시 자동 생성 시작)
    [MenuItem("VeyTrace/Map/Wire Network Integration")]
    public static void WireNetworkIntegration()
    {
        // 1) MapScene 빌드 세팅 등록 (NetworkSceneManager는 빌드 목록의 씬만 로드 가능)
        const string mapScenePath = "Assets/0.Scenes/MapScene.unity";
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!scenes.Exists(s => s.path == mapScenePath))
        {
            scenes.Add(new EditorBuildSettingsScene(mapScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[WireNet] MapScene을 빌드 세팅에 추가.");
        }

        // 2) MapScene에 MapNetworkSync + NetworkObject 배선
        var mg = Object.FindFirstObjectByType<MapGenerator>();
        if (mg == null)
        {
            Debug.LogWarning("[WireNet] MapGenerator 없음 — MapScene을 열고 실행할 것.");
            return;
        }

        var syncGo = GameObject.Find("MapNetworkSync");
        if (syncGo == null) syncGo = new GameObject("MapNetworkSync");

        if (syncGo.GetComponent<Unity.Netcode.NetworkObject>() == null)
            syncGo.AddComponent<Unity.Netcode.NetworkObject>();

        var sync = syncGo.GetComponent<MapNetworkSync>();
        if (sync == null) sync = syncGo.AddComponent<MapNetworkSync>();

        // private [SerializeField] mapGenerator 연결
        var so = new SerializedObject(sync);
        so.FindProperty("mapGenerator").objectReferenceValue = mg;
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(syncGo.scene);
        Debug.Log("[WireNet] MapNetworkSync 배선 완료 (NetworkObject + mapGenerator 참조). 씬 저장 필요.");
    }

    // 오버뷰 맵 UI 토글 (플레이 모드 테스트용 — 게임에선 M 키)
    [MenuItem("VeyTrace/Map/Toggle Overview UI")]
    public static void ToggleOverviewUI()
    {
        var ui = Object.FindFirstObjectByType<MapOverviewUI>();
        if (ui == null) { Debug.LogWarning("[MapDevTools] MapOverviewUI 없음 — 'Setup Scene Generator' 먼저."); return; }
        ui.Toggle();
    }

    // 고정 지형(MapGeometry) + 존/스폰포인트(ZoneVolumes)를 Stage1 프리팹으로 저장
    [MenuItem("VeyTrace/Map/Make Stage1 Prefab")]
    public static void MakeStage1Prefab()
    {
        var geo = GameObject.Find("MapGeometry");
        var zones = GameObject.Find("ZoneVolumes");
        if (geo == null || zones == null)
        {
            Debug.LogWarning("[Stage1] MapGeometry 또는 ZoneVolumes가 씬에 없음 — 먼저 배치/빌드 실행.");
            return;
        }

        GameObject root = GameObject.Find("Stage1");
        if (root == null) root = new GameObject("Stage1");
        geo.transform.SetParent(root.transform, true);
        zones.transform.SetParent(root.transform, true);

        if (!AssetDatabase.IsValidFolder("Assets/2.Prefabs/Map"))
            AssetDatabase.CreateFolder("Assets/2.Prefabs", "Map");

        var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(
            root, "Assets/2.Prefabs/Map/Stage1.prefab", InteractionMode.AutomatedAction);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log(prefab != null
            ? "[Stage1] 프리팹 저장 완료: Assets/2.Prefabs/Map/Stage1.prefab (씬 인스턴스 연결됨)"
            : "[Stage1] 프리팹 저장 실패");
    }

    // 생성된 맵 콘텐츠 제거 (에디터 정리용)
    [MenuItem("VeyTrace/Map/Clear Generated")]
    public static void ClearGenerated()
    {
        var spawner = Object.FindFirstObjectByType<MapContentSpawner>();
        if (spawner != null) spawner.ClearGenerated();
        else
        {
            var root = GameObject.Find(MapContentSpawner.RootName);
            if (root != null) Object.DestroyImmediate(root);
        }
        Debug.Log("[MapDevTools] 생성물 제거 완료.");
    }

    private static Bounds ComputeSceneBounds()
    {
        var renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
        if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.zero);

        Bounds b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        return b;
    }
}
