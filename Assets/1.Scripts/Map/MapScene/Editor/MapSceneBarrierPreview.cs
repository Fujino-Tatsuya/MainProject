using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 튜토리얼 배리어(S3) 배치를 Play 없이 확인한다.
///  · 미리보기 = 열린 씬의 <see cref="MapSceneStageMarker"/>(4.MapScene Stage1) 기준으로 런타임과 같은 배리어를 씬 루트에 만든다.
///    <c>HideFlags.DontSave</c> — 씬에 저장되지 않는다(씬을 저장하지 말 것). 삭제 메뉴로 지운다.
///  · 점검 = 각 배리어 양 끝이 벽 콜라이더에 닿는지(옆 틈 없음) 로그. 마커가 열린 씬에 없으면 StageTutorial 프리팹으로 점검만.
/// </summary>
internal static class MapSceneBarrierPreview
{
    const string StagePath = "Assets/2.Prefabs/Environment/Layouts/Stages/StageTutorial.prefab";
    const string PreviewName = "TutorialBarriers_Preview (저장 안 됨)";
    const string Tag = "[TutorialBarrier]";

    [MenuItem("Tools/Map/Authoring/MapScene/5. Preview Barriers (씬 저장 안 함)")]
    static void Preview()
    {
        ClearPreview();
        MapSceneStageMarker marker = Object.FindAnyObjectByType<MapSceneStageMarker>(FindObjectsInactive.Include);
        if (marker == null)
        {
            Debug.LogWarning($"{Tag} 열린 씬에 MapSceneStageMarker 가 없다 — 4.MapScene 을 열고 다시. 프리팹으로 점검만 한다.");
            CheckPrefab();
            return;
        }

        List<MapSceneBarrierSpec> specs = BuildSpecs(marker.transform);
        var root = new GameObject(PreviewName) { hideFlags = HideFlags.DontSave };
        foreach (MapSceneBarrierSpec s in specs)
        {
            GameObject go = MapSceneBarriers.CreateBarrierObject(s, root.transform, marker.transform.position.y,
                MapSceneBarriers.DefaultHeight, marker.BarrierMaterial, MapSceneBarriers.DefaultColor);
            go.hideFlags = HideFlags.DontSave;
        }
        Report(specs, marker.transform);
        Selection.activeGameObject = root;
    }

    [MenuItem("Tools/Map/Authoring/MapScene/6. Clear Barrier Preview")]
    static void ClearPreview()
    {
        int n = 0;
        foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (go == null || go.name != PreviewName || EditorUtility.IsPersistent(go) || go.transform.parent != null) continue;
            Object.DestroyImmediate(go);
            n++;
        }
        if (n > 0) Debug.Log($"{Tag} 미리보기 {n}개 삭제.");
    }

    [MenuItem("Tools/Map/Authoring/MapScene/7. Check Barriers (StageTutorial Prefab)")]
    static void CheckPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(StagePath);
        try { Report(BuildSpecs(root.transform), root.transform); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static List<MapSceneBarrierSpec> BuildSpecs(Transform stage)
    {
        var areas = new MapSceneAreaTracker();
        if (!MapSceneAreaLayout.TryBuild(stage, areas, out string e1)) Debug.LogWarning($"{Tag} 구간 계산 실패 — {e1}");
        else if (e1 != null) Debug.LogWarning($"{Tag} 구간 경고 — {e1}");
        List<MapSceneBarrierSpec> specs = MapSceneBarrierRules.BuildSpecs(areas,
            MapSceneBarrierRules.DefaultThickness, MapSceneBarrierRules.DefaultSideOverlap, out string e2);
        if (e2 != null) Debug.LogWarning($"{Tag} {e2}");
        return specs;
    }

    /// <summary>
    /// 배리어마다 위치·크기와 양 끝(가로 방향) 벽 접촉 여부. 끝에서 안쪽 겹침 구간에 벽 콜라이더(비트리거) bounds 가 있으면 '벽'.
    /// 벽이 없으면 배리어 옆으로 빠질 틈이 있을 수 있다(⚠).
    /// </summary>
    static void Report(List<MapSceneBarrierSpec> specs, Transform stage)
    {
        var walls = new List<Bounds>();
        foreach (Collider c in stage.GetComponentsInChildren<Collider>(true))
            // 배리어 자신은 뺀다 — 할 일 32 부터 배리어 = Barrier 레이어(벽은 Wall 레이어로 옮겨질 수 있어 Wall 로 거르면 안 된다).
            if (!c.isTrigger && c.gameObject.layer != LayerMask.NameToLayer(GameLayers.Barrier) && c.bounds.size.y > 1f) walls.Add(c.bounds);

        float y = stage.position.y + 1f;
        int warn = 0;
        foreach (MapSceneBarrierSpec s in specs)
        {
            s.Rect.GetWorldBounds(out float minX, out float minZ, out float maxX, out float maxZ);
            bool alongX = (maxX - minX) < (maxZ - minZ);   // 얇은 쪽 = 복도 길이 방향
            float o = MapSceneBarrierRules.DefaultSideOverlap + 0.1f;
            Bounds endA = alongX
                ? new Bounds(new Vector3((minX + maxX) * 0.5f, y, minZ + o * 0.5f), new Vector3(maxX - minX, 0.2f, o))
                : new Bounds(new Vector3(minX + o * 0.5f, y, (minZ + maxZ) * 0.5f), new Vector3(o, 0.2f, maxZ - minZ));
            Bounds endB = alongX
                ? new Bounds(new Vector3((minX + maxX) * 0.5f, y, maxZ - o * 0.5f), new Vector3(maxX - minX, 0.2f, o))
                : new Bounds(new Vector3(maxX - o * 0.5f, y, (minZ + maxZ) * 0.5f), new Vector3(o, 0.2f, maxZ - minZ));
            bool a = Touches(walls, endA), b = Touches(walls, endB);
            if (!a || !b) warn++;
            Debug.Log($"{Tag} {s} — x {minX:0.##}~{maxX:0.##} z {minZ:0.##}~{maxZ:0.##} · 양 끝 벽 {(a ? "O" : "⚠ 없음")}/{(b ? "O" : "⚠ 없음")}");
        }
        string msg = $"{Tag} 배리어 {specs.Count}개 (기대 {MapSceneBarrierRules.BarrierCount}) · 끝 벽 경고 {warn}";
        if (specs.Count != MapSceneBarrierRules.BarrierCount || warn > 0) Debug.LogWarning(msg);
        else Debug.Log(msg);
    }

    static bool Touches(List<Bounds> walls, Bounds probe)
    {
        foreach (Bounds w in walls)
            if (w.Intersects(probe)) return true;
        return false;
    }
}
