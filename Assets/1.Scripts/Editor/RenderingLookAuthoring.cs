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

    // ⚠️ 이 메뉴는 손으로 튜닝한 값을 덮어쓴다(비네트 강도·채도·대비 등).
    //    스크린샷 비교로 값을 잡아 둔 뒤에는 다시 실행하지 말 것.
    //    DoF 만 다시 잡고 싶으면 아래 Apply DoF 메뉴를 쓴다.
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

    // 벽 투명화(플레이어를 가리는 벽을 비운다) 껐다 켜기.
    // ⚠️ 이 기능은 켜 두는 것이 기본이다 — 끄면 벽 뒤 플레이어가 그냥 안 보인다.
    //    화면에 보이는 디더(스크린도어) 무늬는 품질 문제가 아니라 의도된 비용 절충이다:
    //    반투명 블렌딩 대신 clip() 으로 처리해 불투명 큐 한 패스로 끝낸다.
    //    A/B 비교할 때만 잠깐 끄고, 끝나면 반드시 되돌릴 것.
    [MenuItem("Tools/Rendering/Look/Toggle Wall Occlusion (open scene)")]
    public static void ToggleWallOcclusion()
    {
        Toggle<WallOcclusionDriver>("벽 투명화");
    }

    // 포그 매니저 껐다 켜기.
    // ⚠️ 이름은 "포그"지만 실제로 화면을 어둡게 만드는 건 디밍(dimEnabled)과 시야 제한(losEnabled)이다.
    //    프로파일의 fogEnabled 는 이미 0 이라 포그 자체는 안 그려진다.
    //    FogRendererFeature 는 FogManager.HasActiveInstance 로 조기 반환하므로
    //    컴포넌트만 끄면 풀스크린 패스가 통째로 빠지고, OnDisable 이 셰이더 전역도 0 으로 되돌린다.
    [MenuItem("Tools/Rendering/Look/Toggle Fog Manager (open scene)")]
    public static void ToggleFogManager()
    {
        Toggle<FogManager>("포그 매니저(디밍·시야)");
    }

    // ---- DoF: 플레이어 초점 (사진식 얕은 심도) ----
    //
    // Gaussian → Bokeh 로 바꾸는 이유:
    // Gaussian 은 gaussianStart 바깥만 흐리게 하는 "원거리 전용"이다. 카메라보다 가까운 쪽,
    // 즉 화면 아래쪽 바닥은 절대 흐려지지 않는다. 목표한 레퍼런스(f/16 사진)는 초점면 앞뒤가
    // 모두 흐려지는 그림이므로 물리 기반 Bokeh 가 맞다.
    //
    // 🔴 DoF 로 "화면 외곽"을 흐리게 할 수는 없다. DoF 는 화면 위치가 아니라 깊이로 판단한다.
    //    탑다운에서 좌우 외곽은 중앙과 깊이가 거의 같아 흐려지지 않고, 흐려지는 곳은
    //    가까운 아래쪽과 먼 위쪽뿐이다. 진짜 방사형 외곽 블러가 필요하면 풀스크린 패스를
    //    따로 만들어야 한다(FogRendererFeature 와 같은 계통).
    //
    // 값 근거 — FollowOffset (7,17,-7) → 카메라~플레이어 약 19.7m.
    // focalLength 80mm / aperture f4 에서 과초점거리 ≈ 53m 이므로 선명 구간은 약 14~31m 다.
    // 화면 대부분은 선명하고 가장 가까운 아래쪽과 먼 위쪽만 살짝 흐려진다("살짝"에 해당).
    // 더 강하게 하려면 aperture 를 낮추거나(f2.8) focalLength 를 올린다(100~150mm).
    private const float PlayerFocusDistance = 19.7f;

    [MenuItem("Tools/Rendering/Look/Apply DoF — Bokeh (player focus)")]
    public static void ApplyPlayerFocusDof()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(MapVolumeProfilePath);
        if (profile == null)
        {
            Debug.LogError($"[RenderingLook] 볼륨 프로파일을 찾지 못했다: {MapVolumeProfilePath}");
            return;
        }

        var dof = GetOrAdd<DepthOfField>(profile);
        Override(dof.mode, DepthOfFieldMode.Bokeh);
        Override(dof.focusDistance, PlayerFocusDistance);
        Override(dof.focalLength, 80f);
        Override(dof.aperture, 4f);
        Override(dof.bladeCount, 5);
        Override(dof.bladeCurvature, 1f);

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();

        Debug.Log(
            $"[RenderingLook] DoF = Bokeh, 초점 {PlayerFocusDistance}m " +
            "(80mm f4 → 선명 구간 약 14~31m). " +
            "⚠️ Bokeh 는 Gaussian 보다 비싸다 — AA 성능 비교 시 이 비용을 함께 볼 것.");
    }

    // ---- 안티에일리어싱 A/B ----
    //
    // 게임플레이 카메라는 씬에 없다 — CameraTargetSwitcher 가 MainCamera.prefab 을 런타임에
    // Instantiate 한다. 그래서 인스펙터로 찾아 들어가려면 프리팹을 직접 열어야 하는데,
    // 그 사실 자체가 안 알려져 있어서 "카메라 설정을 바꿨는데 안 먹는다"가 반복된다.
    // 여기서 프리팹을 직접 고쳐 그 함정을 우회한다.
    //
    // 무엇을 비교하는가:
    //   None — 기준선. 계단현상 그대로, 디더는 날것.
    //   SMAA — 계단현상만 잡는다. 시간축 누적이 없어 고스팅이 없고 프레임 비용도 예측 가능하다.
    //          ⚠️ 이걸 쓸 때는 WallOcclusionSettings.animateDither 를 반드시 끈다.
    //   TAA  — 계단현상 + 디더를 함께 녹일 수 있지만, 분산 클램프가 디더를 기각하면
    //          뿌연 얼룩이 되고 빠른 이동에서 고스팅이 생긴다.
    private const string MainCameraPrefabPath = "Assets/2.Prefabs/Camera/MainCamera.prefab";

    [MenuItem("Tools/Rendering/Look/AA — None")]
    public static void SetAaNone() => SetAntialiasing(AntialiasingMode.None);

    [MenuItem("Tools/Rendering/Look/AA — SMAA")]
    public static void SetAaSmaa() =>
        SetAntialiasing(AntialiasingMode.SubpixelMorphologicalAntiAliasing);

    [MenuItem("Tools/Rendering/Look/AA — TAA")]
    public static void SetAaTaa() =>
        SetAntialiasing(AntialiasingMode.TemporalAntiAliasing);

    private static void SetAntialiasing(AntialiasingMode mode)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MainCameraPrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[RenderingLook] 카메라 프리팹을 찾지 못했다: {MainCameraPrefabPath}");
            return;
        }

        var data = prefab.GetComponentInChildren<UniversalAdditionalCameraData>(true);
        if (data == null)
        {
            Debug.LogError(
                "[RenderingLook] UniversalAdditionalCameraData 가 없다 — " +
                $"{MainCameraPrefabPath} 구조를 확인할 것.");
            return;
        }

        data.antialiasing = mode;
        EditorUtility.SetDirty(data);
        PrefabUtility.SavePrefabAsset(prefab);

        string note = mode == AntialiasingMode.TemporalAntiAliasing
            ? "animateDither 를 켜도 되는 유일한 모드다."
            : "WallOcclusionSettings.animateDither 는 꺼 둘 것(지글거림).";

        Debug.Log($"[RenderingLook] 안티에일리어싱 = {mode}. {note}");
    }

    private static void Toggle<T>(string label) where T : MonoBehaviour
    {
        var target = Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
        if (target == null)
        {
            Debug.LogWarning(
                $"[RenderingLook] 열린 씬에서 {typeof(T).Name} 을(를) 찾지 못했다. " +
                "4.MapScene 을 연 뒤 다시 실행할 것.");
            return;
        }

        target.enabled = !target.enabled;
        EditorUtility.SetDirty(target);
        EditorSceneManager.MarkSceneDirty(target.gameObject.scene);

        Debug.Log(
            $"[RenderingLook] {label} = {(target.enabled ? "ON" : "OFF")} " +
            $"(씬 '{target.gameObject.scene.name}'). 씬 저장 필요.");
    }
}
