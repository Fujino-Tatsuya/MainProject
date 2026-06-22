using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

// 현재 씬에 MapGenerator를 만들고 Config/Catalog/Zones/보조 컴포넌트를 자동 배선한다.
// (스폰 포인트 배치는 별도 — 레벨 디자인)
public static class MapSceneSetup
{
    [MenuItem("VeyTrace/Map/Setup Scene Generator")]
    public static void SetupSceneGenerator()
    {
        MapGenerator mg = Object.FindFirstObjectByType<MapGenerator>();
        if (mg == null)
        {
            GameObject go = new GameObject("MapGenerator");
            mg = go.AddComponent<MapGenerator>();
            Debug.Log("[MapSceneSetup] MapGenerator 오브젝트 생성.");
        }

        // 보조 컴포넌트
        mg.LayoutPlacer = GetOrAdd<LayoutPlacer>(mg.gameObject);
        mg.Validator = GetOrAdd<MapValidator>(mg.gameObject);
        mg.ContentSpawner = GetOrAdd<MapContentSpawner>(mg.gameObject);
        var overview = GetOrAdd<MapOverviewUI>(mg.gameObject);
        overview.Generator = mg;

        // 에셋 로드 (경로는 MapEditorPaths 단일 출처)
        mg.Config = AssetDatabase.LoadAssetAtPath<MapGenConfigSO>(MapEditorPaths.ConfigPath);
        mg.Catalog = AssetDatabase.LoadAssetAtPath<MapPrefabCatalogSO>(MapEditorPaths.CatalogPath);

        // 존 레이아웃 카탈로그 (프로젝트의 첫 ZoneLayoutCatalogSO 자동 연결)
        var catGuids = AssetDatabase.FindAssets("t:ZoneLayoutCatalogSO");
        if (catGuids.Length > 0)
            mg.ZoneLayoutCatalog = AssetDatabase.LoadAssetAtPath<ZoneLayoutCatalogSO>(
                AssetDatabase.GUIDToAssetPath(catGuids[0]));
        else
            Debug.LogWarning("[MapSceneSetup] ZoneLayoutCatalog 에셋 없음 — 생성 후 인스펙터에서 연결하세요.");

        EditorUtility.SetDirty(mg);
        EditorSceneManager.MarkSceneDirty(mg.gameObject.scene);
        Debug.Log($"[MapSceneSetup] 완료 — Config:{(mg.Config != null)} / Catalog:{(mg.Catalog != null)} / ZoneLayoutCatalog:{(mg.ZoneLayoutCatalog != null)}. (씬 저장 필요)");
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }
}
