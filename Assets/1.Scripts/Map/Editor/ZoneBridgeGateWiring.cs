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

    [MenuItem("Tools/Map/Authoring/Record Bridge Open Positions (prefab stage)")]
    public static void RecordOpenPositions()
    {
        PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage == null || stage.assetPath != ZonePath)
        {
            Debug.LogError($"[BridgeGate] {ZonePath}를 **프리팹 모드로 열고** 다리를 연결 위치로 옮긴 뒤 실행하세요. " +
                           "(Project 창에서 프리팹 더블클릭)");
            return;
        }

        var gate = stage.prefabContentsRoot.GetComponent<ZoneBridgeGate>();
        if (gate == null)
        {
            Debug.LogError("[BridgeGate] 먼저 'Wire Zone Bridge Gate'를 실행하세요.");
            return;
        }

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
