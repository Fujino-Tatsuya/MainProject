using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 로비 튜토리얼 월드 마커 프리팹 저작(할 일 158 C6) — 메뉴 <c>Tools/Lobby/로비 튜토리얼 마커 구성</c>.
/// <c>Assets/2.Prefabs/Lobby/Tutorial/LobbyTutorialMarkers.prefab</c> = 단계별 바닥 경로 묶음(<see cref="LobbyTutorialPathSet"/>, 경로 = 111 화살표 프리팹 중첩)
/// + 집결 민트 테두리 풀(<see cref="LobbyGatherZoneBorders"/>). 멱등 — 이름으로 찾아 있으면 그대로 두고 연결·값만 맞춘다.
/// 경로 점 좌표는 사람이 씬에서 배치한다(111 과 같은 방식). 런타임 생성 없음.
/// </summary>
internal static class LobbyTutorialMarkersAuthoring
{
    private const string Dir = "Assets/2.Prefabs/Lobby/Tutorial";
    private const string PrefabPath = Dir + "/LobbyTutorialMarkers.prefab";
    private const string BorderMaterialPath = Dir + "/M_LobbyGatherBorder.mat";
    private const string ArrowPathPrefab = "Assets/2.Prefabs/Lobby/FloorArrow/LobbyFloorArrowPath.prefab";
    private const string BorderShader = "Lobby/GatherZoneBorder";
    private const int BorderPoolSize = 8;

    [MenuItem("Tools/Lobby/로비 튜토리얼 마커 구성")]
    private static void Build()
    {
        var arrow = AssetDatabase.LoadAssetAtPath<GameObject>(ArrowPathPrefab);
        var shader = Shader.Find(BorderShader);
        if (arrow == null || shader == null)
        {
            Debug.LogError($"[LobbyTutorialMarkersAuthoring] {ArrowPathPrefab} 또는 셰이더 {BorderShader} 를 못 찾았다.");
            return;
        }

        Directory.CreateDirectory(Dir);
        var material = EnsureBorderMaterial(shader);

        bool exists = File.Exists(PrefabPath);
        GameObject root = exists ? PrefabUtility.LoadPrefabContents(PrefabPath) : new GameObject("LobbyTutorialMarkers");
        try
        {
            root.name = "LobbyTutorialMarkers";
            BuildPaths(root, arrow);
            BuildBorders(root, material);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[LobbyTutorialMarkersAuthoring] {(exists ? "갱신" : "생성")}: {PrefabPath}");
        }
        finally
        {
            if (exists)
                PrefabUtility.UnloadPrefabContents(root);
            else
                Object.DestroyImmediate(root);
        }
    }

    private static Material EnsureBorderMaterial(Shader shader)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(BorderMaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "M_LobbyGatherBorder" };
            AssetDatabase.CreateAsset(material, BorderMaterialPath);
        }
        else if (material.shader != shader)
        {
            material.shader = shader;
            EditorUtility.SetDirty(material);
        }

        return material;
    }

    private static void BuildPaths(GameObject root, GameObject arrow)
    {
        var set = Ensure<LobbyTutorialPathSet>(root);
        var pathsRoot = Child(root.transform, "Paths");
        var entries = new List<(string id, LobbyFloorArrowPath path)>();
        IReadOnlyList<string> ids = LobbyTutorialPathIds.All;
        for (int i = 0; i < ids.Count; i++)
        {
            string name = ids[i]; // 오브젝트 이름 = 경로 ID(PATH_*)
            Transform found = pathsRoot.Find(name);
            if (found == null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(arrow, pathsRoot);
                instance.name = name;
                // 배치 전 구분용으로 2m 씩 벌려 둔다 — 실제 좌표는 씬에서 사람이 옮긴다.
                instance.transform.localPosition = new Vector3(i * 2f, 0f, 0f);
                found = instance.transform;
            }

            entries.Add((ids[i], found.GetComponent<LobbyFloorArrowPath>()));
        }

        var so = new SerializedObject(set);
        var list = so.FindProperty("entries");
        list.arraySize = entries.Count;
        for (int i = 0; i < entries.Count; i++)
        {
            var element = list.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("id").stringValue = entries[i].id;
            var paths = element.FindPropertyRelative("paths");
            paths.arraySize = 1;
            paths.GetArrayElementAtIndex(0).objectReferenceValue = entries[i].path;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void BuildBorders(GameObject root, Material material)
    {
        var bordersRoot = Child(root.transform, "GatherBorders");
        var borders = Ensure<LobbyGatherZoneBorders>(bordersRoot.gameObject);
        var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        var pool = new List<MeshRenderer>();
        for (int i = 0; i < BorderPoolSize; i++)
        {
            var t = Child(bordersRoot, $"Border_{i}");
            var filter = Ensure<MeshFilter>(t.gameObject);
            filter.sharedMesh = quad;
            var renderer = Ensure<MeshRenderer>(t.gameObject);
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            t.gameObject.SetActive(false);
            pool.Add(renderer);
        }

        var so = new SerializedObject(borders);
        var list = so.FindProperty("pool");
        list.arraySize = pool.Count;
        for (int i = 0; i < pool.Count; i++)
            list.GetArrayElementAtIndex(i).objectReferenceValue = pool[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Transform Child(Transform parent, string name)
    {
        Transform found = parent.Find(name);
        if (found != null)
            return found;

        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private static T Ensure<T>(GameObject go) where T : Component =>
        go.TryGetComponent(out T existing) ? existing : go.AddComponent<T>();
}
