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
        mg.NodePlacer = GetOrAdd<NodePlacer>(mg.gameObject);
        mg.ObstaclePlacer = GetOrAdd<ObstaclePlacer>(mg.gameObject);
        mg.Validator = GetOrAdd<MapValidator>(mg.gameObject);
        mg.ContentSpawner = GetOrAdd<MapContentSpawner>(mg.gameObject);
        var overview = GetOrAdd<MapOverviewUI>(mg.gameObject);
        overview.Generator = mg;

        // 에셋 로드
        mg.Config = AssetDatabase.LoadAssetAtPath<MapGenConfigSO>("Assets/Resources/MapGen/MapGenConfig.asset");
        mg.Catalog = AssetDatabase.LoadAssetAtPath<MapPrefabCatalogSO>("Assets/Resources/MapGen/MapPrefabCatalog.asset");

        // ZoneDef_1~10 순서대로
        var zones = new List<ZoneDefinitionSO>();
        for (int i = 1; i <= 10; i++)
        {
            var z = AssetDatabase.LoadAssetAtPath<ZoneDefinitionSO>($"Assets/Resources/MapGen/ZoneDef_{i}.asset");
            if (z != null) zones.Add(z);
            else Debug.LogWarning($"[MapSceneSetup] ZoneDef_{i} 못 찾음");
        }
        mg.Zones = zones;

        EditorUtility.SetDirty(mg);
        EditorSceneManager.MarkSceneDirty(mg.gameObject.scene);
        Debug.Log($"[MapSceneSetup] 완료 — Config:{(mg.Config != null)} / Catalog:{(mg.Catalog != null)} / Zones:{zones.Count}. (씬 저장 필요)");
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }
}
