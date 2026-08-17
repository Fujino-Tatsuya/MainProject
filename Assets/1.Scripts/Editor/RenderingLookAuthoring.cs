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
    private const string MapVolumeProfilePath =
        "Assets/0.Scenes/MainFlow/4.MapScene/Global Volume Profile.asset";

    private static void Override<T>(VolumeParameter<T> parameter, T value)
    {
        parameter.overrideState = true;
        parameter.value = value;
    }

    // 🔴 VolumeProfile.Add<T>() 는 컴포넌트를 메모리에만 만들고 프로파일 애셋의 서브에셋으로
    //    등록하지 않는다. 그대로 저장하면 components 목록이 {fileID: 0} 널로 직렬화되고,
    //    인스펙터에는 아무것도 안 뜬다. 도구는 "성공"이라고 로그를 남기므로 조용한 실패가 된다.
    //    반드시 AddObjectToAsset 으로 같이 묶어야 한다.
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
    // 마스크 블러와 DoF 는 같은 일(배경 디포커스)을 서로 다른 기준으로 하므로 겹친다.
    // 둘 다 켜면 이중 블러가 되고 Bokeh 는 비싸다 — 하나만 쓰는 게 맞다.
    [MenuItem("Tools/Rendering/Look/DoF — Off")]
    public static void DisableDof()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(MapVolumeProfilePath);
        if (profile == null || !profile.TryGet(out DepthOfField dof))
        {
            Debug.LogWarning("[RenderingLook] 볼륨 프로파일에 DepthOfField 가 없다.");
            return;
        }

        Override(dof.mode, DepthOfFieldMode.Off);
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        // 되살리는 메뉴는 없다 — 배경 디포커스는 e07d2ac 에서 Mask Blur 로 교체됐고
        // DoF 적용 메뉴(Baseline·Bokeh)는 2026-08-18 에 삭제했다. 필요하면 볼륨 프로파일에서 직접 켠다.
        Debug.Log("[RenderingLook] DoF = Off. 배경 디포커스는 Mask Blur 가 담당한다 " +
                  "(Tools/Rendering/Look/Wire Mask Blur).");
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
