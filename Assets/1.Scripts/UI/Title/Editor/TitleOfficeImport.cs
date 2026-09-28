// ----------------------------------------------------------------------------
//  TitleOfficeImport.cs — 아트 씬(Art/title.unity)의 오피스를 프리팹으로 묶어 1.TitleScene 에 넣는다
//
//  왜 프리팹인가: 씬끼리 오브젝트를 복사하면 아트가 다음에 title.unity 를 고쳐도 타이틀에 안 따라온다.
//  프리팹 한 장으로 묶어 두면 아트 수정은 프리팹에서, 타이틀은 인스턴스 하나만 들고 있으면 된다.
//
//  🔴 아트 씬은 **저장하지 않는다**(계획서 §4-1 "백업 보존"). 루트들을 임시 부모 밑으로 옮겨
//     프리팹을 찍은 뒤, 저장 없이 타이틀 씬을 Single 로 열어 변경을 버린다.
//  🔴 빼는 것: Main Camera(FreeCamera 포함 — 타이틀은 vcam 3대가 몬다) · ProbeVolumePerSceneData
//     (APV 베이크 데이터는 씬 소유라 옮기면 깨진 참조가 된다).
//  멱등: 프리팹은 매번 다시 찍고, 타이틀 씬에 인스턴스가 이미 있으면 새로 만들지 않는다.
// ----------------------------------------------------------------------------
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

static class TitleOfficeImport
{
    const string ArtScenePath = "Assets/0.Scenes/Art/title.unity";
    const string TitleScenePath = "Assets/0.Scenes/MainFlow/1.TitleScene.unity";
    const string PrefabPath = "Assets/2.Prefabs/Title/TitleOffice.prefab";
    const string RootName = "TitleOffice";

    static readonly HashSet<string> Excluded = new HashSet<string> { "Main Camera", "ProbeVolumePerSceneData" };

    [MenuItem("Tools/Title/Authoring/아트 오피스 → 프리팹 → 타이틀 이식")]
    static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[TitleOfficeImport] Play 중에는 안 된다 — 종료 시 씬이 덮어써진다.");
            return;
        }

        // 모달 대화상자는 띄우지 않는다(MCP 로 돌리면 에디터가 멈춘다) — 저장 안 된 씬이 있으면 멈춘다.
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene s = SceneManager.GetSceneAt(i);
            if (s.isDirty)
            {
                Debug.LogError($"[TitleOfficeImport] 저장 안 된 씬이 있다: {s.path} — 저장하거나 되돌린 뒤 다시 실행.");
                return;
            }
        }

        // 1) 아트 씬을 열고 루트를 임시 부모 밑으로 — 월드 좌표 유지(부모가 원점·무회전·스케일 1).
        Scene art = EditorSceneManager.OpenScene(ArtScenePath, OpenSceneMode.Single);
        var root = new GameObject(RootName);
        SceneManager.MoveGameObjectToScene(root, art);

        var moved = new List<string>();
        var skipped = new List<string>();
        foreach (GameObject go in art.GetRootGameObjects())
        {
            if (go == root) continue;
            if (Excluded.Contains(go.name)) { skipped.Add(go.name); continue; }
            go.transform.SetParent(root.transform, worldPositionStays: true);
            moved.Add(go.name);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
        if (!ok || prefab == null)
        {
            Debug.LogError($"[TitleOfficeImport] 프리팹 저장 실패: {PrefabPath}");
            EditorSceneManager.OpenScene(TitleScenePath, OpenSceneMode.Single);
            return;
        }

        // 2) 저장 없이 타이틀 씬으로 — 아트 씬 변경은 여기서 버려진다.
        Scene title = EditorSceneManager.OpenScene(TitleScenePath, OpenSceneMode.Single);

        bool exists = false;
        foreach (GameObject go in title.GetRootGameObjects())
            if (PrefabUtility.GetCorrespondingObjectFromSource(go) == prefab) { exists = true; break; }

        if (!exists)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, title);
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.SetAsFirstSibling();
            EditorSceneManager.MarkSceneDirty(title);
            EditorSceneManager.SaveScene(title);
        }

        Debug.Log($"[TitleOfficeImport] 프리팹 {PrefabPath} — 루트 {moved.Count}개 이동, 제외 [{string.Join(", ", skipped)}]. " +
                  $"타이틀 인스턴스 {(exists ? "이미 있음(유지)" : "새로 배치")}.\n이동: {string.Join(", ", moved)}");
    }

    /// <summary>
    /// 🔴 스카이박스 반사 제거(계획서 §0.4-G). 오피스의 **베이크 반사 프로브**·APV 는 아트 씬 LightingData 에만 구워져 있어
    /// 타이틀에선 프로브가 비고 Unity 기본 스카이박스 환경 반사로 떨어진다 → 타이틀 씬에서 다시 굽는다.
    /// </summary>
    [MenuItem("Tools/Title/Authoring/타이틀 라이팅 베이크")]
    static void BakeTitleLighting()
    {
        if (EditorApplication.isPlaying) { Debug.LogError("[TitleOfficeImport] Play 중에는 굽지 않는다."); return; }

        Scene active = SceneManager.GetActiveScene();
        if (active.path != TitleScenePath)
        {
            Debug.LogError($"[TitleOfficeImport] 1.TitleScene 에서 실행할 것. 현재: {active.path}");
            return;
        }

        Lightmapping.bakeCompleted -= OnBakeCompleted;
        Lightmapping.bakeCompleted += OnBakeCompleted;
        bool started = Lightmapping.BakeAsync();
        Debug.Log($"[TitleOfficeImport] 라이팅 베이크 시작={started}");
    }

    static void OnBakeCompleted()
    {
        Lightmapping.bakeCompleted -= OnBakeCompleted;
        Scene active = SceneManager.GetActiveScene();
        EditorSceneManager.SaveScene(active);
        Debug.Log($"[TitleOfficeImport] 라이팅 베이크 완료 — {active.path} 저장. LightingData={Lightmapping.lightingDataAsset?.name ?? "null"}");
    }
}
