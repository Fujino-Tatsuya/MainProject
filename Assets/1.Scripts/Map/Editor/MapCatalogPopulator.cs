using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

// MapPrefabCatalog.asset 을 알려진 프리팹 경로로 자동 생성·채운다.
// (GUID/fileID 수기 작성 대신 AssetDatabase 경로 로드로 안전하게 연결)
public static class MapCatalogPopulator
{
    private const string CatalogPath = "Assets/Resources/MapGen/MapPrefabCatalog.asset";

    [MenuItem("VeyTrace/Map/Populate Prefab Catalog")]
    public static void Populate()
    {
        MapPrefabCatalogSO catalog = AssetDatabase.LoadAssetAtPath<MapPrefabCatalogSO>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<MapPrefabCatalogSO>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
            Debug.Log("[MapCatalogPopulator] MapPrefabCatalog.asset 생성.");
        }

        // 1티어(대형) — FBX
        catalog.Tier1Nodes = LoadAll(
            "Assets/Resources/MapGen/Prefabs/node_factory.fbx",
            "Assets/Resources/MapGen/Prefabs/node_hospitalroom.fbx",
            "Assets/Resources/MapGen/Prefabs/node_operationroom.fbx");

        // 2티어(중형) — 노드 / 장애물(큐브 프리미티브, 다른 2티어급 크기)
        catalog.Tier2Props = LoadAll(
            "Assets/Synty/PolygonConstruction/Prefabs/Props/SM_Prop_Pallet_03.prefab",
            "Assets/Synty/PolygonConstruction/Prefabs/Buildings/SM_Bld_ConcreteFrame_Pillar_03.prefab",
            "Assets/Synty/PolygonConstruction/Prefabs/Props/SM_Prop_Shipping_Container_01.prefab");
        catalog.Tier2Obstacles = new List<GameObject>
        {
            GetOrCreatePrimitive("Tier2_Obstacle_Cube", PrimitiveType.Cube,
                new Color(0.45f, 0.45f, 0.45f), new Vector3(4f, 3f, 4f))
        };

        // 3티어(소형) — 장애물(서클 프리미티브, 다른 3티어급 크기) /
        // 회복·순간이동·버프 = Synty 스택 플레이스홀더 (실제 에셋 나오면 교체)
        catalog.Tier3Obstacles = new List<GameObject>
        {
            GetOrCreatePrimitive("Tier3_Obstacle_Circle", PrimitiveType.Sphere,
                new Color(0.7f, 0.7f, 0.7f), new Vector3(1.5f, 1.5f, 1.5f))
        };
        catalog.Tier3Recovery = LoadAll("Assets/Synty/PolygonConstruction/Prefabs/Props/SM_Prop_BarrelStack_01.prefab");
        catalog.Tier3Teleport = LoadAll("Assets/Synty/PolygonConstruction/Prefabs/Props/SM_Prop_Brick_Stack_04.prefab");
        catalog.Tier3Buff = LoadAll("Assets/Synty/PolygonConstruction/Prefabs/Props/SM_Prop_ConcreteBag_Stack_03.prefab");

        // 플레이어 스폰 영역 구조물
        catalog.SpawnAreaStructure = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Resources/MapGen/Prefabs/node_spownpoint.fbx");
        if (catalog.SpawnAreaStructure == null) Debug.LogWarning("[MapCatalogPopulator] node_spownpoint.fbx 못 찾음");

        // 고정 지형 — 바닥 4종 / 외벽 2종 (존별 텍스처 통일) / 통로 문 벽
        catalog.FloorTiles = LoadAll(
            "Assets/Synty/PolygonConstruction/Prefabs/Buildings/SM_Bld_Concrete_Floor_01.prefab",
            "Assets/Synty/PolygonConstruction/Prefabs/Buildings/SM_Bld_Concrete_Floor_02.prefab",
            "Assets/Synty/PolygonConstruction/Prefabs/Buildings/SM_Bld_Concrete_Floor_03.prefab",
            "Assets/Synty/PolygonConstruction/Prefabs/Buildings/SM_Bld_Concrete_Floor_04.prefab");
        catalog.WallFences = LoadAll(
            "Assets/Synty/PolygonConstruction/Prefabs/Buildings/SM_Bld_Concrete_Wall_01.prefab",
            "Assets/Synty/PolygonConstruction/Prefabs/Buildings/SM_Bld_Concrete_Wall_02.prefab");
        catalog.WallDoor = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Synty/PolygonConstruction/Prefabs/Buildings/SM_Bld_House_Wall_Door_03.prefab");
        if (catalog.WallDoor == null) Debug.LogWarning("[MapCatalogPopulator] SM_Bld_House_Wall_Door_03 못 찾음");

        // 역할 영역 마커 (Quad + 아이콘 텍스처)
        catalog.BossAreaMarker = GetOrCreateMarkerQuad("Marker_BossArea", "Assets/Resources/MapGen/Boss.png", new Color(0.85f, 0.2f, 0.2f));
        catalog.SpawnAreaMarker = GetOrCreateMarkerQuad("Marker_SpawnArea", "Assets/Resources/MapGen/Spawn.png", new Color(0.2f, 0.85f, 0.35f));
        catalog.QuestAreaMarker = GetOrCreateMarkerQuad("Marker_QuestArea", "Assets/Resources/MapGen/Quest.png", new Color(0.95f, 0.8f, 0.2f));

        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[MapCatalogPopulator] 완료 — T1:{catalog.Tier1Nodes.Count} / T2:{catalog.Tier2Props.Count} / T3장애물:{catalog.Tier3Obstacles.Count}");
    }

    // 역할 마커 Quad 프리팹 + 아이콘 텍스처 머티리얼 (텍스처는 재실행 시에도 항상 갱신)
    private static GameObject GetOrCreateMarkerQuad(string name, string texturePath, Color fallback)
    {
        var prefab = GetOrCreatePrimitive(name, PrimitiveType.Quad, fallback, Vector3.one);

        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        var mat = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Resources/MapGen/Prefabs/{name}_Mat.mat");
        if (mat == null) return prefab;

        // 아이콘이 라이팅 영향 없이 또렷하게 보이도록 Unlit + 알파 투명
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader != null) mat.shader = shader;
        if (tex != null)
        {
            mat.SetTexture("_BaseMap", tex);
            mat.SetTexture("_MainTex", tex); // 폴백 셰이더 호환
            mat.color = Color.white;
        }
        else
        {
            Debug.LogWarning($"[MapCatalogPopulator] 마커 텍스처 못 찾음: {texturePath}");
            mat.color = fallback;
        }
        // URP 투명 설정
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);

        return prefab;
    }

    // 프리미티브 더미 프리팹 생성 (있으면 재사용) — 장애물 표시용
    private static GameObject GetOrCreatePrimitive(string name, PrimitiveType type, Color color, Vector3 scale)
    {
        string prefabPath = $"Assets/Resources/MapGen/Prefabs/{name}.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (existing != null) return existing;

        GameObject obj = GameObject.CreatePrimitive(type);
        obj.name = name;
        obj.transform.localScale = scale;

        string matPath = $"Assets/Resources/MapGen/Prefabs/{name}_Mat.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (mat.shader == null) mat = new Material(Shader.Find("Standard"));
            mat.color = color;
            AssetDatabase.CreateAsset(mat, matPath);
        }
        obj.GetComponent<MeshRenderer>().sharedMaterial = mat;

        var prefab = PrefabUtility.SaveAsPrefabAsset(obj, prefabPath);
        Object.DestroyImmediate(obj);
        Debug.Log($"[MapCatalogPopulator] 프리미티브 생성: {prefabPath}");
        return prefab;
    }

    private static List<GameObject> LoadAll(params string[] paths)
    {
        var list = new List<GameObject>();
        foreach (var path in paths)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) Debug.LogWarning($"[MapCatalogPopulator] 프리팹 못 찾음: {path}");
            else list.Add(go);
        }
        return list;
    }
}
