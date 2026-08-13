using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// 설정 애셋, PC Renderer 피처, 열린 씬 컨트롤러의 세 배선 지점을 한 번에 보장한다.
public static class PixelScanlineAuthoring
{
    private const string SettingsPath = "Assets/99.Settings/PixelScanlineSettings.asset";
    private const string RendererPath = "Assets/99.Settings/PC_Renderer.asset";
    private const string ShaderPath =
        "Assets/1.Scripts/Rendering/PixelScanline/Shaders/PixelScanline.shader";
    private const string ControllerObjectName = "PixelScanlineController";

    [MenuItem("Tools/Rendering/Look/Wire Pixel Scanline (open scene)")]
    public static void Wire()
    {
        PixelScanlineSettings settings = EnsureSettings();
        bool featureOk = EnsureRendererFeature();
        bool sceneOk = EnsureSceneController(settings);

        Debug.Log(
            $"[PixelScanline] 배선 — settings={(settings != null ? "OK" : "실패")}, " +
            $"렌더러 피처={(featureOk ? "OK" : "실패")}, " +
            $"씬 컨트롤러={(sceneOk ? "OK" : "실패")}. " +
            "셋 다 OK여야 동작한다.");
    }

    private static PixelScanlineSettings EnsureSettings()
    {
        var existing = AssetDatabase.LoadAssetAtPath<PixelScanlineSettings>(SettingsPath);
        if (existing != null)
            return existing;

        var created = ScriptableObject.CreateInstance<PixelScanlineSettings>();
        AssetDatabase.CreateAsset(created, SettingsPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[PixelScanline] 설정 애셋 생성: {SettingsPath}");
        return created;
    }

    private static bool EnsureRendererFeature()
    {
        var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (rendererData == null)
        {
            Debug.LogError($"[PixelScanline] 렌더러 애셋을 찾지 못했다: {RendererPath}");
            return false;
        }

        var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null)
        {
            Debug.LogError($"[PixelScanline] 셰이더를 찾지 못했다: {ShaderPath}");
            return false;
        }

        PixelScanlineFeature feature = null;
        foreach (ScriptableRendererFeature rendererFeature in rendererData.rendererFeatures)
        {
            if (rendererFeature is PixelScanlineFeature existing)
            {
                feature = existing;
                break;
            }
        }

        if (feature == null)
        {
            feature = ScriptableObject.CreateInstance<PixelScanlineFeature>();
            feature.name = nameof(PixelScanlineFeature);

            // 서브에셋으로 등록해야 Renderer Features 목록이 저장 후에도 유효하다.
            AssetDatabase.AddObjectToAsset(feature, rendererData);
            rendererData.rendererFeatures.Add(feature);
            Debug.Log("[PixelScanline] PixelScanlineFeature를 PC_Renderer에 추가했다.");
        }

        var featureSo = new SerializedObject(feature);
        featureSo.FindProperty("_shader").objectReferenceValue = shader;
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
                "[PixelScanline] ValidateRendererFeatures를 찾지 못했다. " +
                "PC_Renderer를 Inspector에서 한 번 선택해 피처 맵을 재생성할 것.");
        }

        EditorUtility.SetDirty(feature);
        EditorUtility.SetDirty(rendererData);
        AssetDatabase.SaveAssets();
        return true;
    }

    private static bool EnsureSceneController(PixelScanlineSettings settings)
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogWarning("[PixelScanline] 활성 씬이 없다. 전투 씬을 연 뒤 다시 실행할 것.");
            return false;
        }

        var controller =
            Object.FindFirstObjectByType<PixelScanlineController>(FindObjectsInactive.Include);
        if (controller == null)
        {
            var host = new GameObject(ControllerObjectName);
            controller = host.AddComponent<PixelScanlineController>();
            Undo.RegisterCreatedObjectUndo(host, "Create PixelScanlineController");
            Debug.Log($"[PixelScanline] '{ControllerObjectName}'를 씬 '{scene.name}'에 만들었다.");
        }

        controller.SetSettings(settings);
        EditorUtility.SetDirty(controller);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return true;
    }
}
