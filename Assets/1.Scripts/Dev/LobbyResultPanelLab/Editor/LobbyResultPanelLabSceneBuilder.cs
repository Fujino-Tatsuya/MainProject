using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// 할 일 215 로비 결과창 애니 확인 씬 저작 메뉴 — 네트워크 없는 빈 씬에 카메라·조명·배경 도형(천천히 회전, 201 블러 확인용)·
/// EventSystem·<c>LobbyResultPanel.prefab</c> 인스턴스·<see cref="DebugLobbyResultPanelLab"/> 를 놓는다.
/// 다시 실행하면 씬을 지우고 새로 만든다(수동 수정은 사라진다). 빌드 목록에는 넣지 않는다 — 씬을 열고 그냥 ▶.
/// </summary>
public static class LobbyResultPanelLabSceneBuilder
{
    public const string ScenePath = "Assets/0.Scenes/Debug/DebugLobbyResultPanelLab.unity";
    const string Tag = "[LobbyResultPanelLab]";

    [MenuItem("Tools/Debug/215 로비 결과창 테스트 씬 만들기")]
    public static void BuildFromMenu()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("215 로비 결과창 테스트 씬", "Play 모드를 끝낸 뒤 실행하세요.", "확인");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        if (Build())
            EditorUtility.DisplayDialog("215 로비 결과창 테스트 씬",
                $"{ScenePath} 를 만들었습니다.\n씬을 연 채로 ▶(Dev Boot 아님). 키는 화면 왼쪽 위 도움말.", "확인");
    }

    /// <summary>batchmode 진입점(-executeMethod LobbyResultPanelLabSceneBuilder.Build). 저장 확인 창 없이 만든다.</summary>
    public static bool Build()
    {
        GameObject panelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LobbyResultPanelAuthoring.PanelPath);
        if (panelPrefab == null)
        {
            Debug.LogError($"{Tag} 패널 프리팹이 없다: {LobbyResultPanelAuthoring.PanelPath}");
            return false;
        }

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.TryGetComponent(out Camera camera))
            {
                camera.transform.SetPositionAndRotation(new Vector3(0f, 6f, -10f), Quaternion.Euler(25f, 0f, 0f));
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.16f, 0.2f, 0.26f, 1f);
            }
        }

        // 배경 — 패널 뒤로 색 있는 도형이 보이고 움직이게(블러가 있으면 뭉개져 보인다).
        GameObject backdrop = new GameObject("Backdrop");
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.SetParent(backdrop.transform, false);
        floor.transform.localScale = new Vector3(4f, 1f, 4f);

        var spinners = new List<Transform>();
        Color[] colors =
        {
            new Color(0.95f, 0.35f, 0.3f), new Color(0.3f, 0.75f, 0.95f), new Color(0.98f, 0.82f, 0.25f),
            new Color(0.45f, 0.9f, 0.5f), new Color(0.8f, 0.45f, 0.95f), new Color(1f, 1f, 1f)
        };
        PrimitiveType[] shapes =
        {
            PrimitiveType.Cube, PrimitiveType.Cylinder, PrimitiveType.Sphere,
            PrimitiveType.Cube, PrimitiveType.Capsule, PrimitiveType.Cube
        };
        for (int i = 0; i < colors.Length; i++)
        {
            GameObject shape = GameObject.CreatePrimitive(shapes[i]);
            shape.name = $"Shape_{i + 1}";
            shape.transform.SetParent(backdrop.transform, false);
            float x = -7.5f + i * 3f;
            shape.transform.localPosition = new Vector3(x, 1.2f + (i % 2) * 0.8f, 3f + (i % 3) * 2f);
            shape.transform.localScale = Vector3.one * (1.4f + (i % 3) * 0.4f);
            shape.transform.localRotation = Quaternion.Euler(0f, i * 25f, i % 2 == 0 ? 0f : 20f);
            SetColor(shape, colors[i]);
            spinners.Add(shape.transform);
        }

        // 닫기 버튼용 EventSystem(새 Input System).
        var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

        var panelInstance = (GameObject)PrefabUtility.InstantiatePrefab(panelPrefab, scene);
        LobbyResultPanel panel = panelInstance.GetComponent<LobbyResultPanel>();

        var lab = new GameObject("LobbyResultPanelLab");
        DebugLobbyResultPanelLab driver = lab.AddComponent<DebugLobbyResultPanelLab>();
        driver.Setup(panel, spinners.ToArray());

        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"{Tag} {ScenePath} 생성 {(saved ? "성공" : "실패")} — 빌드 목록에는 넣지 않음 (EventSystem={eventSystem.name})");
        return saved;
    }

    // 씬 안 머티리얼(에셋 아님 — 씬 파일에 같이 저장된다). 셰이더 = 기본 프리미티브 머티리얼의 것(URP Lit).
    static void SetColor(GameObject go, Color color)
    {
        var renderer = go.GetComponent<Renderer>();
        if (renderer == null || renderer.sharedMaterial == null)
            return;
        var material = new Material(renderer.sharedMaterial) { name = $"Lab_{go.name}" };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        renderer.sharedMaterial = material;
    }
}
