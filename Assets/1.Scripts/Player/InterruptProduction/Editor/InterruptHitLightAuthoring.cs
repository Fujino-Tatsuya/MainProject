using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 적중 라이트 연출(할 일 194) 에셋 저작 메뉴 — 수치 SO·월드 이펙트 프리팹을 만들고 NetworkManager.prefab 디렉터 칸에 잇는다.
/// 다시 실행하면 프리팹을 같은 경로에 다시 짓는다(guid 유지). SO 는 있으면 값을 그대로 둔다.
/// 런타임 생성 없음 — 메시·재질·프리팹은 전부 여기서 에셋으로 저장한다.
/// </summary>
internal static class InterruptHitLightAuthoring
{
    public const string Folder = "Assets/5.VFX/Interrupt/HitLight";
    public const string LookPath = Folder + "/InterruptHitLightLook.asset";
    public const string PrefabPath = Folder + "/FX_InterruptHitLight.prefab";
    const string BloomProfilePath = "Assets/5.VFX/Interrupt/HitFlash/VP_InterruptHitFlash_Bloom.asset";
    const string NetworkManagerPrefabPath = "Assets/2.Prefabs/Network/NetworkManager.prefab";

    [MenuItem("Tools/Interrupt/적중 라이트 연출 에셋 만들기 (194)")]
    public static void BuildAll()
    {
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();

        InterruptHitLightLook look = EnsureLook();
        GameObject prefab = BuildPrefab();
        WireDirector(look, prefab != null ? prefab.GetComponent<InterruptHitLightFx>() : null);
        AssetDatabase.SaveAssets();
        Debug.Log($"[InterruptHitLight] 에셋 저작 완료 — {LookPath}, {PrefabPath}");
    }

    static InterruptHitLightLook EnsureLook()
    {
        var look = AssetDatabase.LoadAssetAtPath<InterruptHitLightLook>(LookPath);
        if (look == null)
        {
            look = ScriptableObject.CreateInstance<InterruptHitLightLook>();
            AssetDatabase.CreateAsset(look, LookPath);
        }

        // 블룸 프로필이 비어 있을 때만 간파 전용 블룸을 잇는다(사람이 바꾼 값은 그대로).
        var so = new SerializedObject(look);
        SerializedProperty bloom = so.FindProperty("bloomProfile");
        if (bloom.objectReferenceValue == null)
        {
            bloom.objectReferenceValue = AssetDatabase.LoadAssetAtPath<VolumeProfile>(BloomProfilePath);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        return look;
    }

    static GameObject BuildPrefab()
    {
        var root = new GameObject("FX_InterruptHitLight");
        try
        {
            root.AddComponent<InterruptHitLightFx>();
            return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    static void WireDirector(InterruptHitLightLook look, InterruptHitLightFx fx)
    {
        using (var scope = new PrefabUtility.EditPrefabContentsScope(NetworkManagerPrefabPath))
        {
            var director = scope.prefabContentsRoot.GetComponent<InterruptProductionDirector>();
            if (director == null)
            {
                Debug.LogError("[InterruptHitLight] NetworkManager.prefab 에 InterruptProductionDirector 가 없다");
                return;
            }

            var so = new SerializedObject(director);
            so.FindProperty("hitLightLook").objectReferenceValue = look;
            so.FindProperty("hitLightVfx").objectReferenceValue = fx;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
