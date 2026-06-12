using UnityEngine;
using UnityEditor;
using System.IO;

[InitializeOnLoad]
public static class MapGenPrefabSetup
{
    static MapGenPrefabSetup()
    {
        EditorApplication.delayCall += CreateDummyPrefabs;
    }

    private static void CreateDummyPrefabs()
    {
        string folderPath = "Assets/Resources/MapGen/Prefabs";
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
        {
            AssetDatabase.CreateFolder("Assets", "Resources");
        }
        if (!AssetDatabase.IsValidFolder("Assets/Resources/MapGen"))
        {
            AssetDatabase.CreateFolder("Assets/Resources", "MapGen");
        }
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            AssetDatabase.CreateFolder("Assets/Resources/MapGen", "Prefabs");
        }

        CreatePrefab(folderPath, "Floor_Dummy", PrimitiveType.Plane, new Color(0.2f, 0.2f, 0.2f), new Vector3(2, 1, 2));
        CreatePrefab(folderPath, "Wall_Dummy", PrimitiveType.Cube, new Color(0.4f, 0.2f, 0.1f), new Vector3(1, 3, 1));
        
        CreatePrefab(folderPath, "Tier1_Node_Dummy", PrimitiveType.Cube, Color.red, new Vector3(3, 3, 3));
        
        CreatePrefab(folderPath, "Tier2_Node_Dummy", PrimitiveType.Sphere, Color.blue, new Vector3(2, 2, 2));
        CreatePrefab(folderPath, "Tier2_Obstacle_Dummy", PrimitiveType.Cube, Color.gray, new Vector3(2, 2, 2));
        
        CreatePrefab(folderPath, "Tier3_Node_Dummy", PrimitiveType.Capsule, Color.yellow, new Vector3(1, 1, 1));
        CreatePrefab(folderPath, "Tier3_Obstacle_Dummy", PrimitiveType.Cylinder, new Color(0.8f, 0.8f, 0.8f), new Vector3(1, 1, 1));
        
        AssetDatabase.SaveAssets();
    }

    private static void CreatePrefab(string folderPath, string name, PrimitiveType primitiveType, Color color, Vector3 scale)
    {
        string prefabPath = $"{folderPath}/{name}.prefab";
        if (File.Exists(prefabPath)) return;

        GameObject obj = GameObject.CreatePrimitive(primitiveType);
        obj.name = name;
        obj.transform.localScale = scale;

        // Create Material
        string matPath = $"{folderPath}/{name}_Mat.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (mat.shader == null) mat = new Material(Shader.Find("Standard")); // Fallback
            mat.color = color;
            AssetDatabase.CreateAsset(mat, matPath);
        }
        
        obj.GetComponent<MeshRenderer>().sharedMaterial = mat;

        PrefabUtility.SaveAsPrefabAsset(obj, prefabPath);
        GameObject.DestroyImmediate(obj);
        
        Debug.Log($"[MapGenPrefabSetup] Created Dummy Prefab: {prefabPath}");
    }
}
