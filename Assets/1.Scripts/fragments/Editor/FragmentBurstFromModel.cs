using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Blender 등에서 <b>손으로 만든 파편 모델(FBX)</b>로 버스트 프리팹을 굽는다.
/// 삼각형 기둥(<see cref="MeshFragmentSet"/>)으로는 재질감이 안 나오는 프롭(종이박스 더미 등)용.
///
/// <b>모델 규칙</b>: 파편 하나 = 자식 오브젝트 하나, 원점 = 조각 중심, 자식 배치 = 프롭 원본 위의 제자리.
/// 이름 <c>SM_Frag_&lt;프롭&gt;.fbx</c> → 같은 폴더의 <c>FX_Frag_&lt;프롭&gt;_Burst.prefab</c> 으로 굽는다.
///
/// 같은 경로에 버스트 프리팹이 이미 있으면 그 머티리얼·Rigidbody 값·<see cref="FragmentBurstEffect"/> 튜닝을
/// 이어받고 guid 를 유지한 채 덮어쓴다 — EffectEntry 연결이 그대로 산다.
/// 새로 만들 때는 머티리얼 = 모델 렌더러의 것(대개 임포트 기본 머티리얼이라 굽고 나서 바꿔 끼울 것).
/// </summary>
public static class FragmentBurstFromModel
{
    const string MenuPath = "Assets/Effects/모델 파편으로 버스트 프리팹 굽기";
    const string ModelPrefix = "SM_Frag_";

    [MenuItem(MenuPath, true)]
    static bool Validate()
    {
        foreach (Object o in Selection.objects)
            if (PrefabUtility.GetPrefabAssetType(o) == PrefabAssetType.Model) return true;
        return false;
    }

    [MenuItem(MenuPath)]
    static void BakeSelected()
    {
        foreach (Object o in Selection.objects)
        {
            if (PrefabUtility.GetPrefabAssetType(o) != PrefabAssetType.Model) continue;
            Bake((GameObject)o);
        }
        AssetDatabase.SaveAssets();
    }

    /// <summary>모델 하나로 버스트 프리팹을 굽는다. 반환 = 저장된 프리팹(실패 시 null).</summary>
    public static GameObject Bake(GameObject model)
    {
        string modelPath = AssetDatabase.GetAssetPath(model);
        string baseName = model.name.StartsWith(ModelPrefix) ? model.name.Substring(ModelPrefix.Length) : model.name;
        string dir = System.IO.Path.GetDirectoryName(modelPath).Replace('\\', '/');
        string prefabPath = $"{dir}/FX_Frag_{baseName}_Burst.prefab";

        Transform root = model.transform;
        var pieces = new List<MeshFragmentSetEditor.BurstPiece>();
        Material modelMaterial = null;

        foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || filter.transform == root) continue;

            // 스케일이 끼면 rest pose 만으로는 크기를 못 옮긴다 — 내보내기에서 굽고 올 것.
            if ((filter.transform.lossyScale - Vector3.one).sqrMagnitude > 1e-4f)
                Debug.LogWarning($"[FragmentBurstFromModel] '{filter.name}' 스케일 {filter.transform.lossyScale} — " +
                                 "파편 크기가 원본과 달라진다. 모델 임포트 스케일·내보내기 설정을 확인할 것.", model);

            pieces.Add(new MeshFragmentSetEditor.BurstPiece
            {
                mesh = filter.sharedMesh,
                position = root.InverseTransformPoint(filter.transform.position),
                rotation = Quaternion.Inverse(root.rotation) * filter.transform.rotation,
            });

            var modelRenderer = filter.GetComponent<MeshRenderer>();
            if (modelMaterial == null && modelRenderer != null)
                modelMaterial = modelRenderer.sharedMaterial;
        }

        if (pieces.Count == 0)
        {
            Debug.LogError($"[FragmentBurstFromModel] '{model.name}'에 파편(자식 메시)이 없습니다.", model);
            return null;
        }

        // 기존 버스트에서 머티리얼·물리 값을 이어받는다.
        Material material = modelMaterial;
        float mass = 0.2f, linearDamping = 0.05f, angularDamping = 0.5f;
        var previous = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (previous != null)
        {
            var renderer = previous.GetComponentInChildren<MeshRenderer>(true);
            if (renderer != null && renderer.sharedMaterial != null) material = renderer.sharedMaterial;

            var rb = previous.GetComponentInChildren<Rigidbody>(true);
            if (rb != null) { mass = rb.mass; linearDamping = rb.linearDamping; angularDamping = rb.angularDamping; }
        }

        return MeshFragmentSetEditor.SaveBurstPrefab(prefabPath, $"FX_Frag_{baseName}_Burst", pieces,
            material, mass, linearDamping, angularDamping);
    }
}
