using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>base Player.prefab에 공용 직선 인디케이터를 멱등 저작한다.</summary>
public static class SkillLineIndicatorAuthoring
{
    private const string PlayerPrefabPath = "Assets/2.Prefabs/Player/Player.prefab";
    private const string RangeMaterialPath =
        "Assets/3.Materials/Player/SkillLineIndicator/SkillLineRange.mat";
    private const string ArrowMaterialPath =
        "Assets/3.Materials/Player/SkillLineIndicator/SkillLineArrow.mat";

    [MenuItem("Tools/Player/Authoring/Wire Skill Line Indicator")]
    public static void Wire()
    {
        Material rangeMaterial = AssetDatabase.LoadAssetAtPath<Material>(RangeMaterialPath);
        Material arrowMaterial = AssetDatabase.LoadAssetAtPath<Material>(ArrowMaterialPath);
        if (rangeMaterial == null || arrowMaterial == null)
        {
            Debug.LogError(
                $"[SkillLineIndicatorAuthoring] 머티리얼이 없습니다: " +
                $"range={rangeMaterial != null}, arrow={arrowMaterial != null}");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        if (root == null)
        {
            Debug.LogError($"[SkillLineIndicatorAuthoring] 프리팹을 열 수 없습니다: {PlayerPrefabPath}");
            return;
        }

        try
        {
            Transform indicatorRoot = root.transform.Find("SkillLineIndicator");
            if (indicatorRoot == null)
            {
                var go = new GameObject("SkillLineIndicator");
                indicatorRoot = go.transform;
                indicatorRoot.SetParent(root.transform, false);
            }

            SkillLineIndicator indicator = indicatorRoot.GetComponent<SkillLineIndicator>();
            if (indicator == null)
                indicator = indicatorRoot.gameObject.AddComponent<SkillLineIndicator>();

            Transform fill = EnsureQuad(indicatorRoot, "Fill", rangeMaterial);
            Transform ghost = EnsureQuad(indicatorRoot, "Ghost", rangeMaterial);
            Transform arrow = EnsureQuad(indicatorRoot, "Arrow", arrowMaterial);

            var serialized = new SerializedObject(indicator);
            serialized.FindProperty("fillQuad").objectReferenceValue = fill;
            serialized.FindProperty("ghostQuad").objectReferenceValue = ghost;
            serialized.FindProperty("arrowQuad").objectReferenceValue = arrow;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "[SkillLineIndicatorAuthoring] 완료 — Player.prefab/SkillLineIndicator " +
                "(Fill, Ghost, Arrow) 배선.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Transform EnsureQuad(Transform parent, string name, Material material)
    {
        Transform child = parent.Find(name);
        if (child == null)
        {
            GameObject created = GameObject.CreatePrimitive(PrimitiveType.Quad);
            created.name = name;
            child = created.transform;
            child.SetParent(parent, false);
        }

        child.localPosition = Vector3.zero;
        child.localRotation = Quaternion.identity;
        child.localScale = Vector3.one;

        Collider collider = child.GetComponent<Collider>();
        if (collider != null)
            Object.DestroyImmediate(collider);

        MeshFilter filter = child.GetComponent<MeshFilter>();
        if (filter == null)
            filter = child.gameObject.AddComponent<MeshFilter>();
        if (filter.sharedMesh == null)
            filter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");

        MeshRenderer renderer = child.GetComponent<MeshRenderer>();
        if (renderer == null)
            renderer = child.gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        renderer.allowOcclusionWhenDynamic = false;
        renderer.enabled = false;
        return child;
    }
}
