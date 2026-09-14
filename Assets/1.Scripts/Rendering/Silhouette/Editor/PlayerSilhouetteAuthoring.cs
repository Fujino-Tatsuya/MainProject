using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// 플레이어 실루엣의 배선 지점 세 곳을 한 번에 보장한다(멱등 — 몇 번 돌려도 안전).
//   1) PC_Renderer 에 PlayerSilhouetteFeature 등록 + 셰이더 2개 물리기
//   2) 플레이어 프리팹(Paladin)에 PlayerSilhouetteTag 부착
//   3) 씬의 WallOcclusionDriver 비활성 — 구 투명화와 실루엣이 같이 돌면 둘 다 이상해진다
//
// 구조는 PixelScanlineAuthoring 을 그대로 따랐다(서브에셋 등록 + ValidateRendererFeatures 리플렉션).
public static class PlayerSilhouetteAuthoring
{
    private const string RendererPath = "Assets/99.Settings/PC_Renderer.asset";
    private const string MaskShaderPath =
        "Assets/1.Scripts/Rendering/Silhouette/Shaders/PlayerSilhouetteMask.shader";
    private const string CompositeShaderPath =
        "Assets/1.Scripts/Rendering/Silhouette/Shaders/PlayerSilhouetteComposite.shader";
    private const string PlayerPrefabPath = "Assets/2.Prefabs/Player/Paladin/Paladin.prefab";

    [MenuItem("Tools/Rendering/Look/Wire Player Silhouette")]
    public static void Wire()
    {
        bool featureOk = EnsureRendererFeature();
        bool prefabOk = EnsurePlayerTag();

        Debug.Log(
            $"[PlayerSilhouette] 배선 — 렌더러 피처={(featureOk ? "OK" : "실패")}, " +
            $"플레이어 프리팹={(prefabOk ? "OK" : "실패")}. " +
            "씬의 WallOcclusionDriver 비활성은 'Disable Wall Occlusion (open scene)' 로 따로 한다.");
    }

    private static bool EnsureRendererFeature()
    {
        var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (rendererData == null)
        {
            Debug.LogError($"[PlayerSilhouette] 렌더러 애셋을 찾지 못했다: {RendererPath}");
            return false;
        }

        var maskShader = AssetDatabase.LoadAssetAtPath<Shader>(MaskShaderPath);
        var compositeShader = AssetDatabase.LoadAssetAtPath<Shader>(CompositeShaderPath);
        if (maskShader == null || compositeShader == null)
        {
            Debug.LogError("[PlayerSilhouette] 셰이더를 찾지 못했다. 경로 확인: " +
                           $"{MaskShaderPath} / {CompositeShaderPath}");
            return false;
        }

        PlayerSilhouetteFeature feature = null;
        foreach (ScriptableRendererFeature rendererFeature in rendererData.rendererFeatures)
        {
            if (rendererFeature is PlayerSilhouetteFeature existing)
            {
                feature = existing;
                break;
            }
        }

        if (feature == null)
        {
            feature = ScriptableObject.CreateInstance<PlayerSilhouetteFeature>();
            feature.name = nameof(PlayerSilhouetteFeature);

            // 서브에셋으로 등록해야 Renderer Features 목록이 저장 후에도 유효하다.
            AssetDatabase.AddObjectToAsset(feature, rendererData);
            rendererData.rendererFeatures.Add(feature);
            Debug.Log("[PlayerSilhouette] PlayerSilhouetteFeature 를 PC_Renderer 에 추가했다.");
        }

        var featureSo = new SerializedObject(feature);
        featureSo.FindProperty("_maskShader").objectReferenceValue = maskShader;
        featureSo.FindProperty("_compositeShader").objectReferenceValue = compositeShader;
        featureSo.ApplyModifiedProperties();

        MethodInfo validate = typeof(ScriptableRendererData)
            .GetMethod("ValidateRendererFeatures", BindingFlags.NonPublic | BindingFlags.Instance);
        if (validate != null)
        {
            validate.Invoke(rendererData, null);
        }
        else
        {
            Debug.LogWarning(
                "[PlayerSilhouette] ValidateRendererFeatures 를 찾지 못했다. " +
                "PC_Renderer 를 Inspector 에서 한 번 선택해 피처 맵을 재생성할 것.");
        }

        EditorUtility.SetDirty(feature);
        EditorUtility.SetDirty(rendererData);
        AssetDatabase.SaveAssets();
        return true;
    }

    private static bool EnsurePlayerTag()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[PlayerSilhouette] 플레이어 프리팹을 찾지 못했다: {PlayerPrefabPath}");
            return false;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            if (root.GetComponent<PlayerSilhouetteTag>() == null)
            {
                root.AddComponent<PlayerSilhouetteTag>();
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                Debug.Log($"[PlayerSilhouette] {prefab.name} 에 PlayerSilhouetteTag 를 붙였다.");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        return true;
    }
}
