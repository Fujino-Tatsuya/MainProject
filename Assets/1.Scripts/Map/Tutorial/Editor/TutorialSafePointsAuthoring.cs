using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 튜토리얼 스텝별 고정 안전 지점(할 일 80) 저작 — 열린 씬(4.MapScene)의 <see cref="TutorialStageMarker"/> 기준으로
/// <see cref="TutorialSafePoints"/> 와 구간별 <c>SafePoint_{구간}</c> Transform 을 만든다.
/// 🔴 멱등 — 이미 지점이 있는 구간은 건드리지 않는다(손으로 옮긴 위치 보존). 빠진 구간만 기본 위치
/// (<see cref="TutorialSafePointRules.TryDefaultAnchor"/> — 존1 가운데, 그 밖 = 들어오는 배리어 안쪽)에 만든다. 저장은 사람이(또는 호출자가).
/// </summary>
internal static class TutorialSafePointsAuthoring
{
    const string Tag = "[TutorialSafePoints]";

    [MenuItem("Tools/Map/Authoring/Tutorial/8. Place Safe Points (열린 씬)")]
    static void Place()
    {
        TutorialStageMarker marker = Object.FindAnyObjectByType<TutorialStageMarker>(FindObjectsInactive.Include);
        if (marker == null)
        {
            Debug.LogError($"{Tag} 열린 씬에 TutorialStageMarker 가 없다 — 4.MapScene 을 열고 다시.");
            return;
        }

        var areas = new TutorialAreaTracker();
        if (!TutorialAreaLayout.TryBuild(marker.transform, areas, out string error))
        {
            Debug.LogError($"{Tag} 구간 계산 실패 — {error}");
            return;
        }
        var specs = TutorialBarrierRules.BuildSpecs(areas, TutorialBarrierRules.DefaultThickness, TutorialBarrierRules.DefaultSideOverlap, out _);

        Scene scene = marker.gameObject.scene;
        TutorialSafePoints sp = Object.FindAnyObjectByType<TutorialSafePoints>(FindObjectsInactive.Include);
        if (sp == null)
        {
            var go = new GameObject("TutorialSafePoints");
            SceneManager.MoveGameObjectToScene(go, scene);
            Undo.RegisterCreatedObjectUndo(go, "Create TutorialSafePoints");
            sp = go.AddComponent<TutorialSafePoints>();
        }

        var so = new SerializedObject(sp);
        SerializedProperty list = so.FindProperty("points");
        float floorY = marker.transform.position.y;
        int made = 0, kept = 0;
        for (var area = TutorialArea.Zone1; area <= TutorialArea.BossEnter; area++)
        {
            if (sp.TryGetPoint(area, out _))
            {
                kept++;
                continue;
            }

            float inset = TutorialAreaTracker.IsZone(area) || area == TutorialArea.BossEnter
                ? TutorialSafePointRules.DefaultZoneEntryInset : TutorialSafePointRules.DefaultCorridorEntryInset;
            if (!TutorialSafePointRules.TryDefaultAnchor(area, areas, specs, inset, out float x, out float z))
            {
                Debug.LogWarning($"{Tag} {area} — 기본 위치를 못 구했다(구간·배리어 없음). 건너뛴다.");
                continue;
            }

            var child = new GameObject($"SafePoint_{area}");
            Undo.RegisterCreatedObjectUndo(child, "Create SafePoint");
            child.transform.SetParent(sp.transform, false);
            child.transform.position = new Vector3(x, floorY, z);

            int i = list.arraySize;
            list.InsertArrayElementAtIndex(i);
            SerializedProperty e = list.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("area").intValue = (int)area;
            e.FindPropertyRelative("point").objectReferenceValue = child.transform;
            made++;
            Debug.Log($"{Tag} {area} 지점 생성 — ({x:0.##}, {floorY:0.##}, {z:0.##})");
        }

        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"{Tag} 끝 — 새로 {made}개 · 그대로 {kept}개. 씬을 저장할 것.");
    }
}
