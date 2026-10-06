using UnityEditor;
using UnityEngine;

/// <summary>
/// 존 게이트 패널(object_panel) 모니터 화면 판 저작 보조(PLAN-title-monitor T2).
/// 화면 면 위치는 런타임에 못 잰다(fbx isReadable 0) → 에디터에서 메시를 평면 묶음으로 나눠 고른 값을 게이트에 쓴다.
/// 패널 프랍 루트가 MeshFilter 를 직접 가지므로 메시 공간 = 패널 로컬 공간이다.
/// (10-06 정리: 1회용 진단 메뉴 1·3·4 — 화면 면 후보 보고·후보 PNG·런타임 재현 PNG — 는 값이 확정돼 삭제. 필요하면 git 이력 `5e3e46fe`.)
/// </summary>
static class ZoneMonitorScreenAuthoring
{
    const string OutlineMatPath = "Assets/3.Materials/Map/MA_ZoneInteractOutline.mat";

    // 확정 후보 — 메시 평면 분석으로 고른 값(패널 로컬). 5번이 Candidates[0] 을 게이트에 쓴다.
    internal static readonly (string name, Vector3 pos, Vector3 euler, Vector2 size)[] Candidates =
    {
        ("A_front_#8", new Vector3(-0.2190f, 1.2047f, 0.2227f), new Vector3(45.6f, 134.1f, 0f), new Vector2(0.681f, 0.675f)),
        ("B_recessed_#9", new Vector3(-0.1640f, 1.1209f, 0.1640f), new Vector3(45.4f, 133.5f, 0f), new Vector2(0.693f, 0.678f)),
    };

    const string ZonePath = "Assets/2.Prefabs/Environment/Layouts/Zones/ZoneL_typeB.prefab";

    /// <summary>
    /// ZoneL_typeB 게이트에 모니터 화면·외곽선 값을 쓴다(후보 A = 앞면 #8).
    /// 🔴 YAML 을 직접 고치면 **프리팹 모드에 열려 있던 옛 내용이 자동 저장으로 덮어쓴다**(10-03 실제로 12줄이 사라졌다).
    ///    열려 있으면 그 스테이지에 쓰고 저장, 아니면 LoadPrefabContents 로 쓴다 — 메모리와 디스크가 항상 같다.
    /// </summary>
    [MenuItem("Tools/Map/Authoring/Zone Monitor Screen/5. ZoneL_typeB 게이트에 값 쓰기")]
    static void WriteGateValues()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/3.Materials/title/MA_TitleMonitorUI.mat");
        var logo = AssetDatabase.LoadAssetAtPath<Texture>("Assets/50.Art/Environment/Textures/Props/Office/monitor_screen.png");
        var outline = AssetDatabase.LoadAssetAtPath<Material>(OutlineMatPath);
        if (mat == null || logo == null || outline == null) { Debug.LogError($"[ZoneMonitorScreen] 참조 누락 mat={mat} logo={logo} outline={outline}"); return; }

        var c = Candidates[0];
        var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
        bool inStage = stage != null && stage.assetPath == ZonePath;
        GameObject root = inStage ? stage.prefabContentsRoot : PrefabUtility.LoadPrefabContents(ZonePath);
        try
        {
            var gate = root.GetComponent<ZoneBridgeGate>();
            var so = new SerializedObject(gate);
            so.FindProperty("monitorScreenEnabled").boolValue = true;
            so.FindProperty("screenLocalPosition").vector3Value = c.pos;
            so.FindProperty("screenLocalEuler").vector3Value = c.euler;
            so.FindProperty("screenSize").vector2Value = c.size * 0.95f;
            so.FindProperty("screenMaterialSource").objectReferenceValue = mat;
            so.FindProperty("screenLogo").objectReferenceValue = logo;
            so.FindProperty("screenOnDuration").floatValue = 0.9f;
            so.FindProperty("highlightOutlineMaterial").objectReferenceValue = outline;
            so.ApplyModifiedPropertiesWithoutUndo();

            if (inStage)
            {
                EditorUtility.SetDirty(gate);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(stage.scene);
                PrefabUtility.SaveAsPrefabAsset(root, ZonePath);   // 스테이지 내용 = 디스크
                Debug.Log("[ZoneMonitorScreen] 프리팹 모드에 열린 ZoneL_typeB 에 쓰고 저장했다.");
            }
            else
            {
                PrefabUtility.SaveAsPrefabAsset(root, ZonePath);
                Debug.Log("[ZoneMonitorScreen] ZoneL_typeB 프리팹 파일에 썼다(프리팹 모드 아님).");
            }
        }
        finally
        {
            if (!inStage) PrefabUtility.UnloadPrefabContents(root);
        }

        // 디스크에서 다시 읽어 확인
        var check = AssetDatabase.LoadAssetAtPath<GameObject>(ZonePath).GetComponent<ZoneBridgeGate>();
        var cs = new SerializedObject(check);
        Debug.Log($"[ZoneMonitorScreen] 확인 — enabled={cs.FindProperty("monitorScreenEnabled").boolValue} " +
                  $"outline={cs.FindProperty("highlightOutlineMaterial").objectReferenceValue} mat={cs.FindProperty("screenMaterialSource").objectReferenceValue}");
    }

    [MenuItem("Tools/Map/Authoring/Zone Monitor Screen/2. 외곽선 머티리얼 만들기")]
    static void CreateOutlineMaterial()
    {
        if (AssetDatabase.LoadAssetAtPath<Material>(OutlineMatPath) != null) { Debug.Log($"[ZoneMonitorScreen] 이미 있다: {OutlineMatPath}"); return; }
        var shader = Shader.Find("Zone/InteractOutline");
        if (shader == null) { Debug.LogError("[ZoneMonitorScreen] 셰이더 Zone/InteractOutline 없음"); return; }
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(OutlineMatPath));
        AssetDatabase.CreateAsset(new Material(shader) { name = "MA_ZoneInteractOutline" }, OutlineMatPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ZoneMonitorScreen] 만들었다: {OutlineMatPath} guid={AssetDatabase.AssetPathToGUID(OutlineMatPath)}");
    }
}
