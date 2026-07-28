using BeaverLobby.Player.Dash;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 1회용 저작 도구 — dash-soul 머지 후 Play 검증에서 나온 씬 배선 2건을 처리한다.
// 실행하고 결과 확인되면 이 파일은 삭제한다.
//
// ① MapScene: 우리 WaterRespawnTrigger(AbyssRespawnTrigger) 제거 + 씬 상주 매니저 3종 배치.
//    우리 트리거는 원점의 1×1×1 상자라 실제로 한 번도 발동한 적이 없다. dash-soul의
//    추락 시스템(안전지점 추적·링 탐색·복귀 무적·카메라 연출)으로 일원화한다.
//    dash-soul 시스템들은 씬마다 하나씩 배치해야 하는 매니저를 전제하는데, 그것들이
//    테스트 씬(PlayerDashTest/PlayerBossTest)에만 있어 MapScene에서 조용히 꺼져 있었다:
//      - FallBoundarySettings      없으면 추락 감지 자체를 끔
//      - PlayerDashValidationManager 없으면 대시 요청을 ConfigDisabled로 즉시 반려
//      - Temp_MultiGameRule        없으면 Soul 폴백은 되지만 부활이 불가능
// ② LoadingScene: LoadingCanvas sortingOrder 상향.
//    LoadingCanvas·MapScene Canvas·CombatHUD가 전부 sortingOrder 0 / Screen Space Overlay라
//    additive 로드 구간에서 로딩 화면이 게임 UI 뒤로 밀린다.
public static class MergeFollowUpFixes
{
    const string MapScenePath = "Assets/0.Scenes/MainFlow/4.MapScene.unity";
    const string LoadingScenePath = "Assets/0.Scenes/MainFlow/2.LoadingScene.unity";

    const float FallThresholdY = -30f;
    const int LoadingCanvasSortingOrder = 1000;

    [MenuItem("Tools/Map/Authoring/[1회용] 낙하 경계 + 로딩 캔버스 정리")]
    public static void Run()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.Log("[MergeFollowUp] 사용자가 취소했다. 아무것도 바꾸지 않았다.");
            return;
        }

        FixMapScene();
        FixLoadingScene();

        Debug.Log("[MergeFollowUp] 완료. 결과를 확인하고 이 스크립트를 삭제할 것.");
    }

    // Soul/Corpse가 지형에만 부딪히고 전투 대상·서로와는 통과하도록 잡는 목록.
    // 여기 없는 레이어와는 전부 충돌을 끈다(이름 없는 레이어 포함).
    static readonly string[] SolidLayers = { "Default", "Ground", "Wall", "Env" };

    [MenuItem("Tools/Map/Authoring/[1회용] Soul·Corpse 레이어 + 충돌 매트릭스")]
    public static void SetupDeathLayers()
    {
        int soul = EnsureLayer("Soul");
        int corpse = LayerMask.NameToLayer("Corpse");
        if (corpse < 0)
            corpse = EnsureLayer("Corpse");

        if (soul < 0 || corpse < 0)
        {
            Debug.LogError("[MergeFollowUp] 빈 레이어 슬롯이 없어 중단한다.");
            return;
        }

        foreach (int layer in new[] { soul, corpse })
        {
            for (int other = 0; other < 32; other++)
            {
                bool solid = System.Array.IndexOf(SolidLayers, LayerMask.LayerToName(other)) >= 0;
                Physics.IgnoreLayerCollision(layer, other, !solid);
            }
        }

        AssetDatabase.SaveAssets();

        Debug.Log($"[MergeFollowUp] Soul={soul}, Corpse={corpse}. " +
                  $"충돌 유지 레이어: {string.Join(", ", SolidLayers)} / 나머지 전부 무시.");
    }

    const string GameManagerPrefabPath = "Assets/2.Prefabs/Managers/GameManager.prefab";

    // 씬 재편(MainFlow) 이후 GameManager가 들고 있던 씬 이름이 낡아 LoadScene이 실패한다.
    // GoToResult / GoToLobby / 타이틀 복귀가 전부 이 값으로 SceneManager.LoadScene을 호출한다.
    static readonly (string field, string value)[] SceneNameFixes =
    {
        ("titleSceneName", "1.TitleScene"),
        ("lobbySceneName", "3.BeaverLobby"),
        ("resultSceneName", "5.ResultScene"),
    };

    [MenuItem("Tools/Map/Authoring/[1회용] 사망→Result→로비 사이클 배선")]
    public static void WireResultCycle()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        FixGameManagerSceneNames();
        AddWipeWatcherToMapScene();
    }

    static void FixGameManagerSceneNames()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(GameManagerPrefabPath);
        try
        {
            var manager = root.GetComponentInChildren<GameManager>(true);
            if (manager == null)
            {
                Debug.LogError("[MergeFollowUp] GameManager 컴포넌트를 찾지 못했다.");
                return;
            }

            var so = new SerializedObject(manager);
            int changed = 0;
            foreach ((string field, string value) in SceneNameFixes)
            {
                SerializedProperty prop = so.FindProperty(field);
                if (prop == null)
                {
                    Debug.LogWarning($"[MergeFollowUp] 필드 '{field}'를 찾지 못했다.");
                    continue;
                }

                if (prop.stringValue == value)
                    continue;

                Debug.Log($"[MergeFollowUp] {field}: '{prop.stringValue}' -> '{value}'");
                prop.stringValue = value;
                changed++;
            }

            if (changed > 0)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, GameManagerPrefabPath);
                Debug.Log($"[MergeFollowUp] GameManager 씬 이름 {changed}건 수정.");
            }
            else
            {
                Debug.Log("[MergeFollowUp] GameManager 씬 이름은 이미 최신.");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void AddWipeWatcherToMapScene()
    {
        Scene scene = EditorSceneManager.OpenScene(MapScenePath, OpenSceneMode.Single);
        int added = 0;

        if (Object.FindFirstObjectByType<PartyWipeWatcher>(FindObjectsInactive.Include) == null)
        {
            new GameObject(nameof(PartyWipeWatcher)).AddComponent<PartyWipeWatcher>();
            added++;
            Debug.Log("[MergeFollowUp] PartyWipeWatcher 배치 — 전원 PermanentDead면 Result로 전환.");
        }

        if (Object.FindFirstObjectByType<SessionStatsTracker>(FindObjectsInactive.Include) == null)
        {
            new GameObject(nameof(SessionStatsTracker)).AddComponent<SessionStatsTracker>();
            added++;
            Debug.Log("[MergeFollowUp] SessionStatsTracker 배치 — 생존 시간·처치 수 집계.");
        }

        if (added == 0)
        {
            Debug.Log("[MergeFollowUp] MapScene 통계·전멸 감시자는 이미 배치됨.");
            return;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    const string ResultScenePath = "Assets/0.Scenes/MainFlow/5.ResultScene.unity";

    // ResultScene에는 GotoLobby 버튼만 있고 결과 표시가 없었다.
    // 클리어 여부 / 생존 시간 / 처치 수 세 줄을 만들고 ResultStatsView를 붙인다.
    [MenuItem("Tools/Map/Authoring/[1회용] Result 결과 표시 UI 생성")]
    public static void BuildResultStatsUI()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        Scene scene = EditorSceneManager.OpenScene(ResultScenePath, OpenSceneMode.Single);

        if (Object.FindFirstObjectByType<ResultStatsView>(FindObjectsInactive.Include) != null)
        {
            Debug.Log("[MergeFollowUp] ResultStatsView 이미 존재.");
            return;
        }

        Canvas canvas = null;
        foreach (Canvas candidate in Object.FindObjectsByType<Canvas>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (candidate.transform.parent == null)
            {
                canvas = candidate;
                break;
            }
        }

        if (canvas == null)
        {
            Debug.LogError("[MergeFollowUp] ResultScene에서 루트 Canvas를 찾지 못했다.");
            return;
        }

        var container = new GameObject("ResultStats", typeof(RectTransform));
        container.transform.SetParent(canvas.transform, false);
        var containerRect = (RectTransform)container.transform;
        containerRect.anchorMin = new Vector2(0.5f, 0.5f);
        containerRect.anchorMax = new Vector2(0.5f, 0.5f);
        containerRect.anchoredPosition = new Vector2(0f, 120f);
        containerRect.sizeDelta = new Vector2(600f, 260f);

        CreateStatText(containerRect, "Text_Outcome", 64f, 80f);
        CreateStatText(containerRect, "Text_Survival", 34f, 0f);
        CreateStatText(containerRect, "Text_Kills", 34f, -50f);

        container.AddComponent<ResultStatsView>();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[MergeFollowUp] Result 결과 표시 UI 생성 — 위치·서체는 인스펙터에서 조정할 것.");
    }

    static void CreateStatText(RectTransform parent, string name, float fontSize, float y)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, y);
        rect.sizeDelta = new Vector2(600f, fontSize * 1.6f);

        var text = go.AddComponent<TMPro.TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.alignment = TMPro.TextAlignmentOptions.Center;
        text.text = name;
    }

    const string PlayerPrefabPath = "Assets/2.Prefabs/Player/Player.prefab";
    const string CorpseMaterialPath = "Assets/3.Materials/Player/MA_CorpsePlaceholder.mat";

    // 시체 플레이스홀더가 자홍색으로 보이던 문제.
    // 빌트인 Default-Material(guid 0000..f000..)을 쓰고 있었는데, 그 셰이더는 URP에서
    // 지원되지 않아 자홍색 폴백으로 렌더된다. URP Lit 머티리얼로 교체한다.
    [MenuItem("Tools/Map/Authoring/[1회용] 시체 플레이스홀더 머티리얼 (URP)")]
    public static void FixCorpsePlaceholderMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(CorpseMaterialPath);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("[MergeFollowUp] URP Lit 셰이더를 찾을 수 없어 중단한다.");
                return;
            }

            string folder = System.IO.Path.GetDirectoryName(CorpseMaterialPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets/3.Materials", "Player");

            material = new Material(shader);
            material.color = new Color(0.35f, 0.32f, 0.30f); // 임시 시체 색
            AssetDatabase.CreateAsset(material, CorpseMaterialPath);
            Debug.Log($"[MergeFollowUp] 머티리얼 생성: {CorpseMaterialPath}");
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            int changed = 0;
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.gameObject.name != "CorpseVisualPlaceholder")
                    continue;

                renderer.sharedMaterial = material;
                changed++;
            }

            if (changed > 0)
            {
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                Debug.Log($"[MergeFollowUp] CorpseVisualPlaceholder 머티리얼 교체 {changed}건.");
            }
            else
            {
                Debug.LogWarning("[MergeFollowUp] CorpseVisualPlaceholder를 찾지 못했다.");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static int EnsureLayer(string layerName)
    {
        int existing = LayerMask.NameToLayer(layerName);
        if (existing >= 0)
        {
            Debug.Log($"[MergeFollowUp] 레이어 '{layerName}' 이미 존재(index {existing}).");
            return existing;
        }

        var tagManager = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");

        // 0~7은 Unity 내장 예약 슬롯이라 8번부터 찾는다.
        for (int i = 8; i < layers.arraySize; i++)
        {
            SerializedProperty slot = layers.GetArrayElementAtIndex(i);
            if (!string.IsNullOrEmpty(slot.stringValue))
                continue;

            slot.stringValue = layerName;
            tagManager.ApplyModifiedProperties();
            Debug.Log($"[MergeFollowUp] 레이어 '{layerName}' 추가(index {i}).");
            return i;
        }

        return -1;
    }

    static void FixMapScene()
    {
        Scene scene = EditorSceneManager.OpenScene(MapScenePath, OpenSceneMode.Single);

        // WaterRespawnTrigger 제거는 완료됐고 스크립트도 삭제됐다(dec1eb3).
        FallBoundarySettings boundary = Object.FindFirstObjectByType<FallBoundarySettings>(
            FindObjectsInactive.Include);
        if (boundary == null)
        {
            var go = new GameObject("FallBoundarySettings");
            boundary = go.AddComponent<FallBoundarySettings>();
            Debug.Log("[MergeFollowUp] FallBoundarySettings 신규 배치.", go);
        }

        // 필드가 private [SerializeField]라 SerializedObject로 명시 설정한다.
        var so = new SerializedObject(boundary);
        so.FindProperty("fallThresholdY").floatValue = FallThresholdY;
        so.ApplyModifiedPropertiesWithoutUndo();

        // 대시 서버 검증 매니저도 씬에 하나 필요하다. 없으면 PlayerDashController가
        // SubmitDashRequestServerRpc에서 ConfigDisabled로 즉시 반려해 대시가 통째로 죽는다.
        // (PlayerDashTest 씬에만 배치돼 있어 MapScene에서 대시가 동작하지 않았다.)
        if (Object.FindFirstObjectByType<PlayerDashValidationManager>(FindObjectsInactive.Include) == null)
        {
            var dashGo = new GameObject(nameof(PlayerDashValidationManager));
            dashGo.AddComponent<PlayerDashValidationManager>();
            Debug.Log("[MergeFollowUp] PlayerDashValidationManager 신규 배치.", dashGo);
        }

        // 부활 규칙 보유자. 없으면 사망 시 Soul 폴백까지는 가지만
        // PlayerReviveController가 gameRule null에서 즉시 반환해 부활이 불가능하다.
        // NetworkBehaviour이므로 NetworkObject를 함께 붙인 씬 상주 오브젝트여야 한다.
        // defaultLifeCount는 코드 기본값(3)을 쓴다 — 밸런스 값은 담당자가 조정할 것.
        if (Object.FindFirstObjectByType<Temp_MultiGameRule>(FindObjectsInactive.Include) == null)
        {
            var ruleGo = new GameObject(nameof(Temp_MultiGameRule));
            ruleGo.AddComponent<NetworkObject>();
            ruleGo.AddComponent<Temp_MultiGameRule>();
            Debug.Log("[MergeFollowUp] Temp_MultiGameRule 신규 배치(NetworkObject 포함).", ruleGo);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[MergeFollowUp] MapScene 저장 — FallBoundarySettings threshold={FallThresholdY}, " +
                  $"대시 검증 매니저·부활 규칙 확인.");
    }

    static void FixLoadingScene()
    {
        Scene scene = EditorSceneManager.OpenScene(LoadingScenePath, OpenSceneMode.Single);

        int changed = 0;
        foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (canvas.transform.parent != null)
                continue; // 중첩 Canvas는 루트 정렬값을 따라가므로 건드리지 않는다

            canvas.overrideSorting = false;
            canvas.sortingOrder = LoadingCanvasSortingOrder;
            EditorUtility.SetDirty(canvas);
            changed++;
            Debug.Log($"[MergeFollowUp] {canvas.gameObject.name} sortingOrder={LoadingCanvasSortingOrder}", canvas);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[MergeFollowUp] LoadingScene 저장 — 루트 Canvas {changed}개 수정.");
    }
}
