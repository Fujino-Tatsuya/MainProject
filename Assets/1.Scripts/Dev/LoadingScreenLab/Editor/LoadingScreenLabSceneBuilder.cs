using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 할 일 290 로딩 화면 확인 씬 저작 메뉴 — 빈 씬에 <see cref="DebugLoadingScreenLab"/> 하나만 놓는다(카메라·EventSystem 은
/// ▶ 때 덧붙여 여는 2.LoadingScene 것을 쓴다). 다시 실행하면 씬을 새로 만든다. 빌드 목록에는 넣지 않는다 — 씬을 열고 그냥 ▶.
/// </summary>
public static class LoadingScreenLabSceneBuilder
{
    public const string ScenePath = "Assets/0.Scenes/Debug/DebugLoadingScreenLab.unity";

    [MenuItem("Tools/Debug/290 로딩 화면 테스트 씬 만들기")]
    public static void BuildFromMenu()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("290 로딩 화면 테스트 씬", "Play 모드를 끝낸 뒤 실행하세요.", "확인");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        if (Build())
            EditorUtility.DisplayDialog("290 로딩 화면 테스트 씬",
                $"{ScenePath} 를 만들었습니다.\n씬을 연 채로 ▶. 키는 화면 오른쪽 위 도움말.", "확인");
    }

    /// <summary>batchmode 진입점(-executeMethod LoadingScreenLabSceneBuilder.Build).</summary>
    public static bool Build()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("LoadingScreenLab").AddComponent<DebugLoadingScreenLab>();
        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"[LoadingScreenLab] {ScenePath} 생성 {(saved ? "성공" : "실패")} — 빌드 목록에는 넣지 않음");
        return saved;
    }
}
