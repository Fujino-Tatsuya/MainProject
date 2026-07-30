#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// ZoneL_typeB의 다리 개통 장치 저작 도구.
//
// 저작이 필요한 이유: 다리 조각의 "열림 위치"를 코드로 계산하면 메시 길이·피벗 가정이 들어가
// 반드시 어긋난다(v11 슬롯 저작에서 같은 결론이 났다). 그래서 위치는 사람이 맞추고 여기서 저장한다.
//
// 흐름:
//   1) Wire  — 컴포넌트 부착 + 패널 4개·다리 4조각 수집 + 현재 위치를 '닫힘'으로 기록
//   2) (선택) Estimate — 안쪽 조각의 안쪽 끝이 x=0에 닿도록 좌/우 묶음을 각각 슬라이드한 값을 제안
//   3) Record — 프리팹 스테이지에서 손으로 맞춘 현재 위치를 '열림'으로 저장하고 닫힘으로 되돌림
public static class ZoneBridgeGateWiring
{
    const string ZonePath = "Assets/2.Prefabs/Map/Zoneprefab/ZoneL_typeB.prefab";
    const string PanelPrefix = "Env_panel";
    const string BridgePrefix = "Env_bridge";

    [MenuItem("Tools/Map/Authoring/Wire Zone Bridge Gate (ZoneL_typeB)")]
    public static void WireGate()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ZonePath);

        try
        {
            var gate = root.GetComponent<ZoneBridgeGate>();
            bool added = gate == null;
            if (added) gate = root.AddComponent<ZoneBridgeGate>();

            List<Transform> panels = Collect(root, PanelPrefix);
            List<Transform> bridges = Collect(root, BridgePrefix);

            var log = new StringBuilder($"[BridgeGate] {(added ? "컴포넌트 추가" : "기존 컴포넌트 갱신")} — {ZonePath}\n");

            var so = new SerializedObject(gate);
            WriteTransformList(so, "panels", panels);
            log.AppendLine($"  패널 {panels.Count}개: {Names(panels)}");

            // 기존 저작(열림 위치)을 보존하면서 목록을 갱신한다 — Wire를 다시 돌려도 맞춰둔 값이 살아남는다.
            Dictionary<string, (Vector3 open, bool has)> previous = ReadExistingOpen(so);

            SerializedProperty segs = so.FindProperty("segments");
            segs.arraySize = bridges.Count;
            int kept = 0;
            for (int i = 0; i < bridges.Count; i++)
            {
                SerializedProperty e = segs.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("Target").objectReferenceValue = bridges[i];
                e.FindPropertyRelative("ClosedLocalPosition").vector3Value = bridges[i].localPosition;

                bool has = previous.TryGetValue(bridges[i].name, out var prev) && prev.has;
                e.FindPropertyRelative("OpenLocalPosition").vector3Value = has ? prev.open : bridges[i].localPosition;
                e.FindPropertyRelative("HasOpenPosition").boolValue = has;
                if (has) kept++;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            log.AppendLine($"  다리 {bridges.Count}조각: {Names(bridges)}");

            // ⚠️ 콜라이더가 없으면 NavMesh에 안 올라간다. MapNavMeshBaker는
            // useGeometry = PhysicsColliders 이므로 렌더러만 있는 다리는 베이크에서 통째로 빠지고,
            // 개통해도 그 위를 걸을 수 없다(실제로 그렇게 났다). 여기서 보장한다.
            int collidersAdded = EnsureMeshColliders(bridges, log);
            log.AppendLine($"  MeshCollider 신규 부착 {collidersAdded}개");
            log.AppendLine($"  닫힘 위치 = 현재 위치로 기록 / 열림 위치 보존 {kept}건 · 미저작 {bridges.Count - kept}건");

            if (panels.Count == 0)
                Debug.LogError($"[BridgeGate] '{PanelPrefix}*' 오브젝트가 없습니다 — F 대상이 생기지 않습니다.");
            if (bridges.Count == 0)
                Debug.LogError($"[BridgeGate] '{BridgePrefix}*' 오브젝트가 없습니다 — 움직일 다리가 없습니다.");
            if (kept < bridges.Count)
                log.AppendLine("  → 열림 위치 미저작분이 있습니다. ZoneL_typeB를 프리팹 모드로 열어 다리를 " +
                               "연결 위치로 옮기고 'Record Bridge Open Positions'를 실행하세요 " +
                               "(추정값이 필요하면 'Estimate Bridge Open Positions' 먼저).");

            PrefabUtility.SaveAsPrefabAsset(root, ZonePath);
            Debug.Log(log.ToString());
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [MenuItem("Tools/Map/Authoring/Estimate Bridge Open Positions (ZoneL_typeB)")]
    public static void EstimateOpenPositions()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ZonePath);

        try
        {
            var gate = root.GetComponent<ZoneBridgeGate>();
            if (gate == null)
            {
                Debug.LogError("[BridgeGate] 먼저 'Wire Zone Bridge Gate'를 실행하세요.");
                return;
            }

            var so = new SerializedObject(gate);
            SerializedProperty segs = so.FindProperty("segments");

            // 좌/우 묶음을 각각 통째로 슬라이드한다. 안쪽 끝이 x=0에 닿는 양만큼 옮기고, 같은 쪽의
            // 다른 조각도 같은 델타로 움직여 조각 간 상대 배치를 유지한다(신축 다리 가정).
            var byside = new Dictionary<int, List<int>>();
            var innerEdge = new Dictionary<int, float>();

            for (int i = 0; i < segs.arraySize; i++)
            {
                var t = segs.GetArrayElementAtIndex(i).FindPropertyRelative("Target").objectReferenceValue as Transform;
                if (t == null) continue;

                int side = t.localPosition.x >= 0f ? 1 : -1;
                if (!byside.TryGetValue(side, out var list)) byside[side] = list = new List<int>();
                list.Add(i);

                if (!TryLocalBounds(gate.transform, t, out Bounds b)) continue;

                // 중앙(x=0)을 향한 끝. 오른쪽 묶음은 min.x, 왼쪽 묶음은 max.x가 안쪽이다.
                float edge = side > 0 ? b.min.x : b.max.x;
                if (!innerEdge.TryGetValue(side, out float cur) ||
                    (side > 0 ? edge < cur : edge > cur))
                    innerEdge[side] = edge;
            }

            var log = new StringBuilder("[BridgeGate] 열림 위치 추정(안쪽 끝이 x=0에 닿도록 묶음 슬라이드):\n");
            foreach (KeyValuePair<int, List<int>> kv in byside)
            {
                if (!innerEdge.TryGetValue(kv.Key, out float edge))
                {
                    Debug.LogWarning($"[BridgeGate] side {kv.Key}: 렌더러 바운즈를 못 구해 추정을 건너뜁니다.");
                    continue;
                }

                float delta = -edge;   // 안쪽 끝을 0으로
                log.AppendLine($"  side {(kv.Key > 0 ? "+X" : "-X")}: 안쪽 끝 x={edge:F2} → 슬라이드 {delta:+0.00;-0.00}");

                foreach (int i in kv.Value)
                {
                    SerializedProperty e = segs.GetArrayElementAtIndex(i);
                    Vector3 closed = e.FindPropertyRelative("ClosedLocalPosition").vector3Value;
                    Vector3 open = closed + new Vector3(delta, 0f, 0f);
                    e.FindPropertyRelative("OpenLocalPosition").vector3Value = open;
                    e.FindPropertyRelative("HasOpenPosition").boolValue = true;

                    var t = e.FindPropertyRelative("Target").objectReferenceValue as Transform;
                    log.AppendLine($"    {t?.name}: {closed} → {open}");
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, ZonePath);

            log.AppendLine("⚠️ 추정값이다. 프리팹 모드에서 눈으로 확인하고 어긋나면 손으로 맞춘 뒤 " +
                           "'Record Bridge Open Positions'로 덮어써라.");
            Debug.Log(log.ToString());
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [MenuItem("Tools/Map/Authoring/Record Bridge CLOSED Positions (prefab stage)")]
    public static void RecordClosedPositions()
    {
        if (!TryGetStageGate(out ZoneBridgeGate gate, out PrefabStage stage)) return;

        var so = new SerializedObject(gate);
        SerializedProperty segs = so.FindProperty("segments");
        var log = new StringBuilder("[BridgeGate] 현재 위치를 '닫힘(평상시)'으로 저장합니다:\n");
        int n = 0;

        for (int i = 0; i < segs.arraySize; i++)
        {
            SerializedProperty e = segs.GetArrayElementAtIndex(i);
            var t = e.FindPropertyRelative("Target").objectReferenceValue as Transform;
            if (t == null) continue;

            e.FindPropertyRelative("ClosedLocalPosition").vector3Value = t.localPosition;
            log.AppendLine($"  {t.name}: 닫힘 {t.localPosition}");
            n++;
        }

        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(stage.scene);
        log.AppendLine($"완료: {n}조각. 이제 다리를 **연결된 위치**로 옮기고 'Record Bridge OPEN Positions'를 실행하세요.");
        Debug.Log(log.ToString());
    }

    [MenuItem("Tools/Map/Authoring/Record Bridge OPEN Positions (prefab stage)")]
    public static void RecordOpenPositions()
    {
        if (!TryGetStageGate(out ZoneBridgeGate gate, out PrefabStage stage)) return;

        var so = new SerializedObject(gate);
        SerializedProperty segs = so.FindProperty("segments");
        var log = new StringBuilder("[BridgeGate] 현재 위치를 '열림'으로 저장하고 '닫힘'으로 되돌립니다:\n");
        int n = 0;

        for (int i = 0; i < segs.arraySize; i++)
        {
            SerializedProperty e = segs.GetArrayElementAtIndex(i);
            var t = e.FindPropertyRelative("Target").objectReferenceValue as Transform;
            if (t == null) continue;

            Vector3 open = t.localPosition;
            Vector3 closed = e.FindPropertyRelative("ClosedLocalPosition").vector3Value;

            e.FindPropertyRelative("OpenLocalPosition").vector3Value = open;
            e.FindPropertyRelative("HasOpenPosition").boolValue = true;

            // 저장 후 닫힘으로 되돌린다 — 프리팹의 기본 상태는 항상 '끊긴 다리'여야 한다.
            Undo.RecordObject(t, "Record Bridge Open Positions");
            t.localPosition = closed;

            log.AppendLine($"  {t.name}: 열림 {open} / 닫힘 {closed}");
            n++;
        }

        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(stage.scene);

        log.AppendLine($"완료: {n}조각 저장. **프리팹 저장 필요**(Ctrl+S).");
        Debug.Log(log.ToString());
    }

    /// <summary>
    /// 프리팹 모드로 열려 있는 ZoneL_typeB의 게이트를 가져온다.
    /// 프리팹 스테이지에서 작업하는 이유: 저작은 눈으로 위치를 맞추는 일이고, 스테이지 밖에서
    /// <c>LoadPrefabContents</c>로 열면 같은 프리팹을 두 곳에서 편집해 나중 저장이 앞선 것을 덮는다.
    /// </summary>
    static bool TryGetStageGate(out ZoneBridgeGate gate, out PrefabStage stage)
    {
        gate = null;
        stage = PrefabStageUtility.GetCurrentPrefabStage();

        if (stage == null || stage.assetPath != ZonePath)
        {
            Debug.LogError($"[BridgeGate] {ZonePath}를 **프리팹 모드로 열고** 실행하세요 " +
                           "(Project 창에서 프리팹 더블클릭).");
            return false;
        }

        gate = stage.prefabContentsRoot.GetComponent<ZoneBridgeGate>();
        if (gate == null)
        {
            Debug.LogError("[BridgeGate] 프리팹에 ZoneBridgeGate가 없습니다 — 먼저 'Wire Zone Bridge Gate'를 실행하세요.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 다리 조각(과 그 자식)에서 MeshFilter가 있는데 Collider가 없는 곳에 MeshCollider를 붙인다.
    /// 이미 있으면 건드리지 않으므로 재실행 안전하다(손으로 붙인 것도 보존).
    ///
    /// convex는 켜지 않는다 — 다리 데크는 오목한 형상일 수 있고, Rigidbody가 없는 정적 콜라이더라
    /// 비볼록도 그대로 쓸 수 있다. 개통 때 한 번 움직이는 비용은 무시 가능하다.
    /// </summary>
    static int EnsureMeshColliders(List<Transform> bridges, StringBuilder log)
    {
        int added = 0;

        foreach (Transform bridge in bridges)
        {
            foreach (MeshFilter mf in bridge.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                if (mf.GetComponent<Collider>() != null) continue;

                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.convex = false;
                added++;
                log.AppendLine($"    + MeshCollider: {bridge.name}/{mf.name} (mesh {mf.sharedMesh.name})");
            }
        }

        return added;
    }

    static List<Transform> Collect(GameObject root, string prefix)
        => root.GetComponentsInChildren<Transform>(true)
               .Where(t => t != root.transform && t.name.StartsWith(prefix))
               .OrderBy(t => t.name)
               .ToList();

    static string Names(List<Transform> list) => string.Join(", ", list.Select(t => t.name));

    static void WriteTransformList(SerializedObject so, string path, List<Transform> values)
    {
        SerializedProperty p = so.FindProperty(path);
        p.arraySize = values.Count;
        for (int i = 0; i < values.Count; i++)
            p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    static Dictionary<string, (Vector3 open, bool has)> ReadExistingOpen(SerializedObject so)
    {
        var map = new Dictionary<string, (Vector3, bool)>();
        SerializedProperty segs = so.FindProperty("segments");
        for (int i = 0; i < segs.arraySize; i++)
        {
            SerializedProperty e = segs.GetArrayElementAtIndex(i);
            var t = e.FindPropertyRelative("Target").objectReferenceValue as Transform;
            if (t == null) continue;

            map[t.name] = (e.FindPropertyRelative("OpenLocalPosition").vector3Value,
                           e.FindPropertyRelative("HasOpenPosition").boolValue);
        }
        return map;
    }

    /// <summary>존 루트 로컬 좌표계에서의 렌더러 바운즈. 추정 슬라이드 계산용.</summary>
    static bool TryLocalBounds(Transform zoneRoot, Transform target, out Bounds bounds)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        bounds = default;
        if (renderers.Length == 0) return false;

        bool first = true;
        foreach (Renderer r in renderers)
        {
            // renderer.bounds는 월드다 — 존 루트 로컬로 변환해야 x=0(존 중앙) 기준이 성립한다.
            Bounds wb = r.bounds;
            Vector3 c = zoneRoot.InverseTransformPoint(wb.center);
            Vector3 e = zoneRoot.InverseTransformVector(wb.extents);
            var lb = new Bounds(c, new Vector3(Mathf.Abs(e.x) * 2f, Mathf.Abs(e.y) * 2f, Mathf.Abs(e.z) * 2f));

            if (first) { bounds = lb; first = false; }
            else bounds.Encapsulate(lb);
        }
        return true;
    }
}
#endif
