using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 할 일 221 바닥 블렌딩 확인 씬 저작 메뉴 — 빈 씬에 위에서 내려다보는 카메라·기본 조명·<see cref="DebugDecalFloorBlendLab"/> 를 놓고
/// 재질 칸(바닥 3종·플레이어 인디케이터)을 채운다. 인디케이터·예고는 ▶ 때 구동기가 만든다(예고 재질은 런타임 인스턴스라 씬에 저장할 수 없다).
/// 다시 실행하면 씬을 새로 만든다(수동 수정은 사라진다). 🔴 빌드 목록(EditorBuildSettings)에는 넣지 않는다 — 씬을 열고 그냥 ▶.
/// </summary>
public static class DecalFloorBlendLabSceneBuilder
{
    public const string ScenePath = "Assets/0.Scenes/Debug/DebugDecalFloorBlendLab.unity";
    const string Tag = "[DecalFloorBlendLab]";

    const string PatternFloorPath = "Assets/50.Art/Environment/Materials/UsedInMap/Shared/Atlases/Generic_01_A.mat";
    const string SkillRangePath = "Assets/3.Materials/Player/SkillRangeIndicator/SkillRangeIndicator.mat";
    const string TargetCirclePath = "Assets/3.Materials/Player/SkillRangeIndicator/SelectedTargetCircleC.mat";
    const string SkillLinePath = "Assets/3.Materials/Player/SkillLineIndicator/SkillLineRange.mat";
    const string SkillArrowPath = "Assets/3.Materials/Player/SkillLineIndicator/SkillLineArrow.mat";
    const string AimQuadPath = "Assets/3.Materials/Player/AimIndicator/AimIndicator3D.mat";

    [MenuItem("Tools/Debug/221 바닥 블렌딩 확인 씬 만들기")]
    public static void BuildFromMenu()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("221 바닥 블렌딩 확인 씬", "Play 모드를 끝낸 뒤 실행하세요.", "확인");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        if (Build())
            EditorUtility.DisplayDialog("221 바닥 블렌딩 확인 씬",
                $"{ScenePath} 를 만들었습니다.\n씬을 연 채로 ▶. B 를 누르는 동안 블렌딩 끔(전후 비교).\n값: Resources/Rendering/DecalFloorBlendSettings", "확인");
    }

    /// <summary>batchmode 진입점(-executeMethod DecalFloorBlendLabSceneBuilder.Build). 저장 확인 창 없이 만든다.</summary>
    public static bool Build()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.TryGetComponent(out Camera camera))
            {
                // 게임과 비슷한 탑다운 각도로 세 바닥이 다 들어오게
                camera.transform.SetPositionAndRotation(new Vector3(0f, 30f, -24f), Quaternion.Euler(52f, 0f, 0f));
                camera.fieldOfView = 50f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.1f, 0.11f, 0.13f, 1f);
            }
        }

        var labGo = new GameObject("DecalFloorBlendLab");
        var lab = labGo.AddComponent<DebugDecalFloorBlendLab>();
        var so = new SerializedObject(lab);
        Assign(so, "patternFloor", PatternFloorPath);
        Assign(so, "skillRange", SkillRangePath);
        Assign(so, "targetCircle", TargetCirclePath);
        Assign(so, "skillLine", SkillLinePath);
        Assign(so, "skillArrow", SkillArrowPath);
        Assign(so, "aimQuad", AimQuadPath);
        so.ApplyModifiedPropertiesWithoutUndo();

        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ScenePath));
        bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log(saved ? $"{Tag} {ScenePath} 저장" : $"{Tag} 🔴 {ScenePath} 저장 실패");
        return saved;
    }

    static void Assign(SerializedObject so, string field, string path)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) Debug.LogWarning($"{Tag} 재질 없음(칸 비움 — 바닥은 단색으로 대체): {path}");
        so.FindProperty(field).objectReferenceValue = mat;
    }
}
