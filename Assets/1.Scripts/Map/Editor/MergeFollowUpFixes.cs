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
