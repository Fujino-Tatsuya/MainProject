using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// 룩 저작 도구 — 레퍼런스(TeamVault/Art-Planning/레퍼런스/인게임레퍼런스.png)에 맞춘
// 그레이딩·포스트프로세싱 기준값을 한 번에 적용한다.
// 여기 값은 전부 "시작점"이며, 이후 인스펙터에서 스크린샷 비교로 튜닝하는 것을 전제로 한다.
//
// 왜 손으로 안 하고 도구인가:
//   1) 볼륨 오버라이드는 손으로 추가하면 override 체크를 빠뜨리기 쉽다 — 인스펙터에 값은 보이는데
//      실제로는 적용되지 않아서 "고쳤는데 그대로"가 된다. 이 도구는 항상 overrideState를 같이 켠다.
//   2) 되돌렸다가 다시 적용하는 일이 반복되므로 멱등해야 한다(몇 번 실행해도 같은 결과).
public static class RenderingLookAuthoring
{
    private const string RpAssetPath = "Assets/99.Settings/PC_RPAsset.asset";

    private const string MapVolumeProfilePath =
        "Assets/0.Scenes/MainFlow/4.MapScene/Global Volume Profile.asset";

    // PlayerFollowCamera.FollowOffset = (7, 17, -7) → 카메라~플레이어 약 19.7m.
    // ⚠️ DoF 시작 거리는 반드시 이보다 커야 캐릭터가 선명하다.
    //    카메라 오프셋이나 FOV를 바꾸면 아래 DoF 값도 함께 재조정할 것.
    private const float DofStart = 26f;
    private const float DofEnd = 50f;

    [MenuItem("Tools/Rendering/Look/Apply Baseline (HDR Grading + Post)")]
    public static void ApplyBaseline()
    {
        bool gradingOk = ApplyHdrColorGrading();
        bool volumeOk = ApplyMapVolumeBaseline();

        if (gradingOk || volumeOk)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Debug.Log(
            $"[RenderingLook] 기준값 적용 — 그레이딩 모드={(gradingOk ? "OK" : "실패")}, " +
            $"볼륨 프로파일={(volumeOk ? "OK" : "실패")}. " +
            "값은 시작점이므로 스크린샷 비교로 튜닝할 것.");
    }

    // PC_RPAsset 의 컬러 그레이딩을 LDR → HDR 로 올린다.
    // LDR 그레이딩은 톤매핑 이전에 0~1 로 잘라버려서, HDR 을 켜 둔 채로 두면
    // 밝은 부분이 롤오프 없이 그냥 클리핑된다(가로등·물 반사가 흰 덩어리로 뭉친다).
    private static bool ApplyHdrColorGrading()
    {
        var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(RpAssetPath);
        if (rp == null)
        {
            Debug.LogError($"[RenderingLook] RP 애셋을 찾지 못했다: {RpAssetPath}");
            return false;
        }

        // 프로퍼티 setter 가 버전마다 public/internal 이 갈려서 SerializedObject 로 간다.
        var so = new SerializedObject(rp);
        SerializedProperty mode = so.FindProperty("m_ColorGradingMode");
        if (mode == null)
        {
            Debug.LogError("[RenderingLook] m_ColorGradingMode 프로퍼티가 없다 — URP 버전 확인 필요.");
            return false;
        }

        mode.enumValueIndex = (int)ColorGradingMode.HighDynamicRange;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(rp);
        return true;
    }

    private static bool ApplyMapVolumeBaseline()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(MapVolumeProfilePath);
        if (profile == null)
        {
            Debug.LogError($"[RenderingLook] 볼륨 프로파일을 찾지 못했다: {MapVolumeProfilePath}");
            return false;
        }

        // 이전 실행이 남긴 깨진 항목을 먼저 턴다.
        // (서브에셋 등록 없이 Add 하면 저장 시 {fileID: 0} 널로 직렬화된다 — 아래 GetOrAdd 주석 참고)
        int removed = profile.components.RemoveAll(c => c == null);
        if (removed > 0)
            Debug.LogWarning($"[RenderingLook] 볼륨 프로파일에서 깨진 오버라이드 {removed}개를 제거했다.");

        // 톤매핑 — Neutral 을 쓴다. ACES 는 채도를 눌러 어둡게 가져가는데,
        // 레퍼런스는 밝고 채도가 살아 있는 회화풍이라 방향이 반대다.
        var tonemapping = GetOrAdd<Tonemapping>(profile);
        Override(tonemapping.mode, TonemappingMode.Neutral);

        // 화이트 밸런스 — 레퍼런스의 따뜻한 톤. 현재 씬은 기본 스카이박스 앰비언트 때문에 파란 회색이다.
        var whiteBalance = GetOrAdd<WhiteBalance>(profile);
        Override(whiteBalance.temperature, 10f);
        Override(whiteBalance.tint, 0f);

        // 컬러 조정 — 채도와 대비를 올려 "회색 떡칠"에서 빼낸다.
        // ⚠️ 배경 탈채도(PLAN-vision §3)와 반대 방향이다. 새 레퍼런스는 배경 채도가 살아 있고
        //    캐릭터 분리는 디포커스·접지그림자가 담당한다 — 팀장·아트 확인 항목.
        var colorAdjustments = GetOrAdd<ColorAdjustments>(profile);
        Override(colorAdjustments.postExposure, 0.2f);
        Override(colorAdjustments.contrast, 12f);
        Override(colorAdjustments.saturation, 18f);
        Override(colorAdjustments.colorFilter, new Color(1f, 0.98f, 0.94f, 1f));

        // 배경 디포커스 — PLAN-vision §4 단계 3. 탑다운이라 카메라 거리가 고정이라 튜닝이 쉽다.
        // Gaussian 을 쓰는 이유: Bokeh 보다 싸고, 거리 구간을 직접 지정할 수 있어
        // "플레이어는 선명 / 먼 배경만 흐림"을 정확히 자를 수 있다.
        var depthOfField = GetOrAdd<DepthOfField>(profile);
        Override(depthOfField.mode, DepthOfFieldMode.Gaussian);
        Override(depthOfField.gaussianStart, DofStart);
        Override(depthOfField.gaussianEnd, DofEnd);
        Override(depthOfField.gaussianMaxRadius, 1f);
        Override(depthOfField.highQualitySampling, true);

        // 비네트 — 화면 가장자리를 눌러 중앙(캐릭터)으로 시선을 모은다. 약하게.
        var vignette = GetOrAdd<Vignette>(profile);
        Override(vignette.intensity, 0.25f);
        Override(vignette.smoothness, 0.4f);

        EditorUtility.SetDirty(profile);
        return true;
    }

    // 볼륨 오버라이드를 켜면서 값을 넣는다.
    // overrideState 를 안 켜면 인스펙터에 값이 보여도 실제로는 적용되지 않는다.
    private static void Override<T>(VolumeParameter<T> parameter, T value)
    {
        parameter.overrideState = true;
        parameter.value = value;
    }

    // 🔴 VolumeProfile.Add<T>() 는 컴포넌트를 메모리에만 만들고 프로파일 애셋의 서브에셋으로
    //    등록하지 않는다. 그대로 저장하면 components 목록이 {fileID: 0} 널로 직렬화되고,
    //    인스펙터에는 아무것도 안 뜬다. 도구는 "성공"이라고 로그를 남기므로 조용한 실패가 된다.
    //    반드시 AddObjectToAsset 으로 같이 묶어야 한다.
    private static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (!profile.TryGet(out T component))
            component = profile.Add<T>();

        // ⚠️ 목록에 이미 있어도 안심하면 안 된다. 등록을 빠뜨린 채 Add 된 컴포넌트는
        //    메모리에는 멀쩡히 살아 있어서 TryGet 이 찾아내지만, 애셋에는 없으므로
        //    저장하면 다시 널이 된다. 존재 여부가 아니라 "애셋에 속해 있는가"로 판단할 것.
        if (!AssetDatabase.Contains(component))
        {
            component.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(component, profile);
        }

        return component;
    }

    // 벽 투명화(디밍 차폐) 껐다 켜기.
    // 끄면 머티리얼 교체 자체가 일어나지 않아 벽이 원본 아트 머티리얼로 렌더된다
    // (셰이더 전역 _WallOccRange.w 도 0 이라 디더 경로를 통째로 건너뛴다).
    [MenuItem("Tools/Rendering/Look/Toggle Wall Occlusion (open scene)")]
    public static void ToggleWallOcclusion()
    {
        var driver = Object.FindFirstObjectByType<WallOcclusionDriver>(FindObjectsInactive.Include);
        if (driver == null)
        {
            Debug.LogWarning(
                "[RenderingLook] 열린 씬에서 WallOcclusionDriver 를 찾지 못했다. " +
                "4.MapScene 을 연 뒤 다시 실행할 것.");
            return;
        }

        driver.enabled = !driver.enabled;
        EditorUtility.SetDirty(driver);
        EditorSceneManager.MarkSceneDirty(driver.gameObject.scene);

        Debug.Log(
            $"[RenderingLook] 벽 투명화 = {(driver.enabled ? "ON" : "OFF")} " +
            $"(씬 '{driver.gameObject.scene.name}'). 씬 저장 필요.");
    }
}
