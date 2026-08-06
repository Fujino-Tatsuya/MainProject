using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// 마스크 블러 배선 도구. 세 곳을 이어야 기능이 살아난다:
//   1) MaskBlurSettings 애셋
//   2) PC_Renderer 에 MaskBlurFeature (+ 셰이더 참조)
//   3) 씬에 MaskBlurController (+ settings)
// 하나라도 빠지면 조용히 아무 일도 안 일어나므로 한 번에 처리하고 결과를 로그로 남긴다.
public static class MaskBlurAuthoring
{
    private const string SettingsPath = "Assets/99.Settings/MaskBlurSettings.asset";
    private const string RendererPath = "Assets/99.Settings/PC_Renderer.asset";
    private const string ShaderPath =
        "Assets/1.Scripts/Rendering/MaskBlur/Shaders/MaskBlur.shader";
    private const string ControllerObjectName = "MaskBlurController";

    [MenuItem("Tools/Rendering/Look/Wire Mask Blur (open scene)")]
    public static void Wire()
    {
        MaskBlurSettings settings = EnsureSettings();
        bool featureOk = EnsureRendererFeature();
        bool sceneOk = EnsureSceneController(settings);

        Debug.Log(
            $"[MaskBlur] 배선 — settings={(settings != null ? "OK" : "실패")}, " +
            $"렌더러 피처={(featureOk ? "OK" : "실패")}, " +
            $"씬 컨트롤러={(sceneOk ? "OK" : "실패")}. " +
            "셋 다 OK 여야 동작한다.");
    }

    // ProfilerHUD 의 customMarkers 를 마스크 블러 패스로 교체한다.
    //
    // 마커 이름은 MaskBlurFeature.PassNames 에서 가져온다 — 손으로 적으면 오타가 나도
    // HUD 가 에러 없이 0.00 ms 를 찍어 "이 패스는 공짜"라는 거짓 신호가 된다.
    [MenuItem("Tools/Rendering/Look/Wire ProfilerHUD Markers (MaskBlur)")]
    public static void WireProfilerMarkers()
    {
        var hud = Object.FindFirstObjectByType<ProfilerHUD>(FindObjectsInactive.Include);
        if (hud == null)
        {
            Debug.LogWarning(
                "[MaskBlur] 열린 씬에서 ProfilerHUD 를 찾지 못했다. " +
                "4.MapScene 을 연 뒤 다시 실행할 것.");
            return;
        }

        hud.customMarkers.Clear();
        AddMarker(hud, "Blur DownH", MaskBlurFeature.PassNames.DownH);
        AddMarker(hud, "Blur V", MaskBlurFeature.PassNames.Vertical);
        AddMarker(hud, "Blur Comp", MaskBlurFeature.PassNames.Composite);
        AddMarker(hud, "Blur Copy", MaskBlurFeature.PassNames.CopyBack);

        EditorUtility.SetDirty(hud);
        var scene = hud.gameObject.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log(
            $"[MaskBlur] ProfilerHUD 마커를 마스크 블러 4패스로 교체했다(씬 '{scene.name}'). " +
            "F8 로 확인할 것 — 0.00 ms 로만 찍히면 패스 이름이 어긋난 것이다.");
    }

    private static void AddMarker(ProfilerHUD hud, string label, string markerName)
    {
        hud.customMarkers.Add(new ProfilerHUD.MarkerSpec
        {
            label = label,
            markerName = markerName,
            budgetMs = 0f
        });
    }

    private static MaskBlurSettings EnsureSettings()
    {
        var existing = AssetDatabase.LoadAssetAtPath<MaskBlurSettings>(SettingsPath);
        if (existing != null)
            return existing;

        var created = ScriptableObject.CreateInstance<MaskBlurSettings>();
        AssetDatabase.CreateAsset(created, SettingsPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[MaskBlur] 설정 애셋 생성: {SettingsPath}");
        return created;
    }

    private static bool EnsureRendererFeature()
    {
        var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (rendererData == null)
        {
            Debug.LogError($"[MaskBlur] 렌더러 애셋을 찾지 못했다: {RendererPath}");
            return false;
        }

        var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null)
        {
            Debug.LogError($"[MaskBlur] 셰이더를 찾지 못했다: {ShaderPath}");
            return false;
        }

        MaskBlurFeature feature = null;
        foreach (var f in rendererData.rendererFeatures)
        {
            if (f is MaskBlurFeature existing)
            {
                feature = existing;
                break;
            }
        }

        if (feature == null)
        {
            feature = ScriptableObject.CreateInstance<MaskBlurFeature>();
            feature.name = nameof(MaskBlurFeature);

            // 🔴 서브에셋으로 등록하지 않으면 저장 시 목록이 null 로 직렬화된다.
            //    (VolumeProfile.Add 와 같은 함정 — 도구는 "성공"을 찍고 화면은 그대로다.)
            AssetDatabase.AddObjectToAsset(feature, rendererData);
            rendererData.rendererFeatures.Add(feature);
            Debug.Log("[MaskBlur] MaskBlurFeature 를 PC_Renderer 에 추가했다.");
        }

        // 셰이더 참조를 직렬화해 둔다 — 이게 없으면 빌드에서 스트립된다.
        var featureSo = new SerializedObject(feature);
        featureSo.FindProperty("_shader").objectReferenceValue = shader;
        featureSo.ApplyModifiedProperties();

        // m_RendererFeatureMap 은 features 목록과 개수가 맞아야 한다. URP 가 OnValidate 에서
        // 재계산하지만 그 메서드가 internal 이라 직접 부를 수 없어 리플렉션으로 깨운다.
        // 실패해도 치명적이지 않다 — 인스펙터로 PC_Renderer 를 한 번 선택하면 URP 가 스스로 고친다.
        MethodInfo validate = typeof(UnityEngine.Rendering.Universal.ScriptableRendererData)
            .GetMethod("ValidateRendererFeatures", BindingFlags.NonPublic | BindingFlags.Instance);
        if (validate != null)
            validate.Invoke(rendererData, null);
        else
            Debug.LogWarning(
                "[MaskBlur] ValidateRendererFeatures 를 찾지 못했다(URP 버전 차이). " +
                "PC_Renderer 를 인스펙터에서 한 번 선택해 맵을 재생성할 것.");

        EditorUtility.SetDirty(rendererData);
        AssetDatabase.SaveAssets();
        return true;
    }

    private static bool EnsureSceneController(MaskBlurSettings settings)
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogWarning("[MaskBlur] 활성 씬이 없다. 4.MapScene 을 연 뒤 다시 실행할 것.");
            return false;
        }

        var controller = Object.FindFirstObjectByType<MaskBlurController>(FindObjectsInactive.Include);
        if (controller == null)
        {
            var host = new GameObject(ControllerObjectName);
            controller = host.AddComponent<MaskBlurController>();
            Undo.RegisterCreatedObjectUndo(host, "Create MaskBlurController");
            Debug.Log($"[MaskBlur] '{ControllerObjectName}' 를 씬 '{scene.name}' 에 만들었다.");
        }

        controller.SetSettings(settings);
        EditorUtility.SetDirty(controller);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return true;
    }
}
