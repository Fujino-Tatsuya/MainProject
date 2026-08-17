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

    // 🔴 아트 V3 납품이 존을 통째로 교체하면서 이름이 바뀌었다(소실 커밋 1bffe5a "LevelPackage.Ver10 Import").
    // 구 이름은 Env_panel / Env_bridge01 이었고 원본 프리팹째로 사라졌다 — git·디스크 어디에도 없다.
    // 이 도구는 이름으로 수집하므로 아트가 또 이름을 바꾸면 여기만 고치면 된다.
    const string PanelPrefix = "PF_Prop_object_panel";

    // ⚠️ "floor_" 로 뭉뚱그리면 floor_stone·floor_staire·floor_stone_trench 까지 전부 걸린다.
    // 존 하나에 floor_stone 이 100개 넘게 있으므로 반드시 정확히 적는다.
    static readonly string[] BridgePrefixes = { "floor_bridge", "floor_MV_bridge" };

    // 실제로 움직이는 조각. MV = MoVing — Z로 4m 긴 데크판이다.
    // floor_bridge 는 Z 0.85m·높이 1.88m 짜리 짧고 두꺼운 교대(橋台)라 제자리에 남는다.
    // 구 저작도 외곽 2조각만 움직였고 내측 2조각은 Open == Closed 였다.
    const string MovingPrefix = "floor_MV_bridge";

    // 팀장 지정 개통 위치(2026-08-17). **이동량이 아니라 도착 Z 절대좌표**다.
    // 데크 길이 4m · 콜라이더 중심 오프셋 −0.408 이라 열리면 각각
    //   001: z 13.278 → 10.6   → 점유 [8.19, 12.19]
    //   002: z −12.871 → −10.14 → 점유 [−12.55, −8.55]
    // 를 덮어 중앙 플랫폼(≈ ±8)과 교대 floor_bridge(z = 12.19 / −11.79)를 잇는다.
    // 🔴 좌우가 비대칭인 것은 사람이 눈으로 맞춘 값이라 그렇다 — 같게 만들지 말 것.
    static readonly (string Name, float OpenZ)[] AuthoredOpenZ =
    {
        ("floor_MV_bridge_001", 10.6f),
        ("floor_MV_bridge_002", -10.14f),
    };

    // 구 저작의 연출값(같은 커밋). AddComponent 기본값(1.2 / 0.12)과 다르므로 새로 붙일 때 복원한다.
    const float LegacyRingRadius = 1.55f;
    const float LegacyRingWidth = 0.2f;

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
            List<Transform> bridges = Collect(root, BridgePrefixes);

            // 새로 붙인 것이면 구 저작의 연출값을 복원한다. 컴포넌트 기본값과 다르므로
            // 이걸 빼면 링이 눈에 띄게 작고 얇아진다(반지름 1.55→1.2 · 굵기 0.2→0.12).
            if (added)
            {
                var soLook = new SerializedObject(gate);
                soLook.FindProperty("ringRadius").floatValue = LegacyRingRadius;
                soLook.FindProperty("ringWidth").floatValue = LegacyRingWidth;
                soLook.ApplyModifiedPropertiesWithoutUndo();
            }

            var log = new StringBuilder($"[BridgeGate] {(added ? "컴포넌트 추가" : "기존 컴포넌트 갱신")} — {ZonePath}\n");

            var so = new SerializedObject(gate);
            WriteTransformList(so, "panels", panels);
            log.AppendLine($"  패널 {panels.Count}개: {Names(panels)}");

            // 기존 저작(열림 위치)을 보존하면서 목록을 갱신한다 — Wire를 다시 돌려도 맞춰둔 값이 살아남는다.
            Dictionary<string, (Vector3 open, bool has)> previous = ReadExistingOpen(so);

            SerializedProperty segs = so.FindProperty("segments");
            segs.arraySize = bridges.Count;
            int kept = 0, authored = 0, heldStatic = 0;
            for (int i = 0; i < bridges.Count; i++)
            {
                string name = bridges[i].name;
                Vector3 closed = bridges[i].localPosition;

                SerializedProperty e = segs.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("Target").objectReferenceValue = bridges[i];
                e.FindPropertyRelative("ClosedLocalPosition").vector3Value = closed;

                bool has = previous.TryGetValue(name, out var prev) && prev.has;
                Vector3 open = has ? prev.open : closed;

                // 🔴 저작표에 있는 조각은 여기서 바로 채운다. 예전엔 Wire 만 돌리고 끝나서
                // "F는 먹는데 다리가 안 움직인다"로 헤맸다(2026-08-17) — 한 번에 끝나게 한다.
                if (!has)
                {
                    int row = System.Array.FindIndex(AuthoredOpenZ, a => a.Name == name);
                    if (row >= 0)
                    {
                        open = closed;
                        open.z = AuthoredOpenZ[row].OpenZ;
                        has = true;
                        authored++;
                        log.AppendLine($"    ↔ 저작표 적용: {name} z {closed.z:F3} → {open.z:F3}");
                    }
                    else if (!name.StartsWith(MovingPrefix))
                    {
                        // 표에 없고 이름도 '움직이는 조각'이 아니면 고정 교대다. Open == Closed 로
                        // **명시 저작**한다 — 미저작으로 남기면 런타임이 "어디로 갈지 모른다"며 매 스폰마다
                        // 에러를 뱉는데, 이건 정상 상황이라 거짓 경보가 된다. 구 저작도 이렇게 했다
                        // (2026-07-29: 내측 2조각이 HasOpenPosition=1 · Open==Closed).
                        open = closed;
                        has = true;
                        heldStatic++;
                        log.AppendLine($"    · 고정으로 저작: {name} (Open = Closed)");
                    }
                }

                e.FindPropertyRelative("OpenLocalPosition").vector3Value = open;
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
            log.AppendLine($"  닫힘 위치 = 현재 위치로 기록 / 열림 위치: 저작표 적용 {authored}건 · " +
                           $"고정 저작 {heldStatic}건 · 기존 보존 {kept - authored - heldStatic}건 · " +
                           $"미저작 {bridges.Count - kept}건");

            if (panels.Count == 0)
                Debug.LogError($"[BridgeGate] '{PanelPrefix}*' 오브젝트가 없습니다 — F 대상이 생기지 않습니다. " +
                               "아트가 이름을 또 바꿨는지 확인하고 PanelPrefix 를 고치세요.");
            if (bridges.Count == 0)
                Debug.LogError($"[BridgeGate] '{string.Join("* / ", BridgePrefixes)}*' 오브젝트가 없습니다 — 움직일 다리가 없습니다. " +
                               "아트가 이름을 또 바꿨는지 확인하고 BridgePrefixes 를 고치세요.");
            // 미저작이 남았다는 것은 "개통해도 그 조각은 안 움직인다"는 뜻이다 — 로그 한 줄로 묻히면
            // 원인을 못 찾는다(2026-08-17). 움직여야 할 이름인데 표에 없으면 에러로 올린다.
            if (kept < bridges.Count)
            {
                var stuck = bridges.Where(b => b.name.StartsWith(MovingPrefix))
                                   .Select(b => b.name)
                                   .Where(n => System.Array.FindIndex(AuthoredOpenZ, a => a.Name == n) < 0)
                                   .ToList();

                if (stuck.Count > 0)
                    Debug.LogError($"[BridgeGate] '{MovingPrefix}*' 인데 AuthoredOpenZ 표에 없어 " +
                                   $"개통해도 안 움직입니다: {string.Join(", ", stuck)} — 표를 갱신하세요 " +
                                   $"({nameof(ZoneBridgeGateWiring)}.{nameof(AuthoredOpenZ)}).");

                log.AppendLine($"  → 미저작 {bridges.Count - kept}조각은 개통해도 제자리에 머뭅니다. " +
                               "움직일 조각이면 AuthoredOpenZ 표에 넣거나, 프리팹 모드에서 손으로 맞춘 뒤 " +
                               "'Record Bridge OPEN Positions'로 저장하세요. " +
                               "(고정 교대는 미저작이 정상입니다.)");
            }

            PrefabUtility.SaveAsPrefabAsset(root, ZonePath);
            Debug.Log(log.ToString());
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// 팀장이 지정한 개통 위치(<see cref="AuthoredOpenZ"/>)를 써 넣는다.
    ///
    /// 왜 추정(<see cref="EstimateOpenPositions"/>)이 아닌가: 추정은 "안쪽 끝을 존 중앙(0)에 붙인다"는
    /// 규칙인데 이 존은 <b>중앙에 플랫폼이 있다</b> — 중앙까지 밀면 다리가 플랫폼을 파고든다.
    /// 실제로 필요한 것은 플랫폼 가장자리(≈ ±8)까지이고, 그 값은 사람이 보고 정한다.
    ///
    /// 움직이는 것은 표에 적힌 조각뿐이고 나머지는 <c>Open == Closed</c> 로 둔다.
    /// </summary>
    [MenuItem("Tools/Map/Authoring/Apply Authored Bridge Open Positions (ZoneL_typeB)")]
    public static void ApplyAuthoredOpenPositions()
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
            if (segs.arraySize == 0)
            {
                Debug.LogError("[BridgeGate] segments 가 비어 있습니다 — Wire 가 다리를 못 찾은 것입니다.");
                return;
            }

            // 저작값이 Z 절대좌표이므로 배치가 Z축이 아니면 그대로 쓰면 안 된다.
            int axis = DetectBridgeAxis(segs);
            if (axis != 2)
            {
                Debug.LogError($"[BridgeGate] 다리 축이 {AxisName(axis)} 로 감지됐습니다 — 저작표는 Z 절대좌표 기준입니다. " +
                               "아트가 존을 회전시켰다면 AuthoredOpenZ 를 다시 정해야 합니다. 중단합니다.");
                return;
            }

            var log = new StringBuilder("[BridgeGate] 팀장 지정 개통 위치 적용 (Z 절대좌표)\n");
            int moved = 0, held = 0;
            var unmatched = new List<string>();

            for (int i = 0; i < segs.arraySize; i++)
            {
                SerializedProperty e = segs.GetArrayElementAtIndex(i);
                var t = e.FindPropertyRelative("Target").objectReferenceValue as Transform;
                if (t == null) continue;

                Vector3 closed = e.FindPropertyRelative("ClosedLocalPosition").vector3Value;
                Vector3 open = closed;

                int row = System.Array.FindIndex(AuthoredOpenZ, a => a.Name == t.name);
                if (row >= 0)
                {
                    open.z = AuthoredOpenZ[row].OpenZ;
                    moved++;
                    log.AppendLine($"  ↔ {t.name}: z {closed.z:F3} → {open.z:F3} (이동 {open.z - closed.z:+0.000;-0.000})");
                }
                else
                {
                    held++;
                    log.AppendLine($"  · {t.name}: 고정 (Open = Closed)");
                    if (t.name.StartsWith(MovingPrefix)) unmatched.Add(t.name);
                }

                e.FindPropertyRelative("OpenLocalPosition").vector3Value = open;
                e.FindPropertyRelative("HasOpenPosition").boolValue = true;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, ZonePath);

            log.AppendLine($"완료: 이동 {moved}조각 · 고정 {held}조각.");

            // 움직여야 할 이름인데 표에 없으면 조용히 고정돼 버린다 — 그건 반드시 알려야 한다.
            if (unmatched.Count > 0)
                Debug.LogError($"[BridgeGate] '{MovingPrefix}*' 인데 AuthoredOpenZ 표에 없어 고정으로 처리된 조각: " +
                               $"{string.Join(", ", unmatched)} — 표를 갱신하세요.");
            if (moved != AuthoredOpenZ.Length)
                Debug.LogWarning($"[BridgeGate] 표에는 {AuthoredOpenZ.Length}개가 있는데 {moved}개만 적용됐습니다 — " +
                                 "이름이 바뀌었는지 확인하세요.");

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

            // 🔴 축을 하드코딩하지 않는다. 이 도구는 원래 X축 다리를 전제로 썼는데 V3 아트 납품이
            // 존을 Z축 배치로 바꿔 그대로 돌리면 조용히 엉뚱한 값을 쓴다. 저작 데이터에서 알아낸다.
            int axis = DetectBridgeAxis(segs);

            // 양쪽 묶음을 각각 통째로 슬라이드한다. 안쪽 끝이 0에 닿는 양만큼 옮기고, 같은 쪽의
            // 다른 조각도 같은 델타로 움직여 조각 간 상대 배치를 유지한다(신축 다리 가정).
            var byside = new Dictionary<int, List<int>>();
            var innerEdge = new Dictionary<int, float>();

            for (int i = 0; i < segs.arraySize; i++)
            {
                var t = segs.GetArrayElementAtIndex(i).FindPropertyRelative("Target").objectReferenceValue as Transform;
                if (t == null) continue;

                int side = t.localPosition[axis] >= 0f ? 1 : -1;
                if (!byside.TryGetValue(side, out var list)) byside[side] = list = new List<int>();
                list.Add(i);

                if (!TryLocalBounds(gate.transform, t, out Bounds b)) continue;

                // 중앙(0)을 향한 끝. + 묶음은 min, − 묶음은 max 가 안쪽이다.
                float edge = side > 0 ? b.min[axis] : b.max[axis];
                if (!innerEdge.TryGetValue(side, out float cur) ||
                    (side > 0 ? edge < cur : edge > cur))
                    innerEdge[side] = edge;
            }

            var log = new StringBuilder($"[BridgeGate] 열림 위치 추정(안쪽 끝이 {AxisName(axis)}=0에 닿도록 묶음 슬라이드):\n");
            foreach (KeyValuePair<int, List<int>> kv in byside)
            {
                if (!innerEdge.TryGetValue(kv.Key, out float edge))
                {
                    Debug.LogWarning($"[BridgeGate] side {kv.Key}: 렌더러 바운즈를 못 구해 추정을 건너뜁니다.");
                    continue;
                }

                float delta = -edge;   // 안쪽 끝을 0으로
                log.AppendLine($"  side {(kv.Key > 0 ? "+" : "-")}{AxisName(axis)}: " +
                               $"안쪽 끝 {AxisName(axis)}={edge:F2} → 슬라이드 {delta:+0.00;-0.00}");

                Vector3 offset = Vector3.zero;
                offset[axis] = delta;

                foreach (int i in kv.Value)
                {
                    SerializedProperty e = segs.GetArrayElementAtIndex(i);
                    Vector3 closed = e.FindPropertyRelative("ClosedLocalPosition").vector3Value;
                    Vector3 open = closed + offset;
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
            // 🔴 V3 아트는 콜라이더를 조각 루트에 두고 메시는 중첩 프리팹 안에 둔다.
            // 그래서 자식 MeshFilter 만 보면 "콜라이더 없음"으로 읽혀 중복 MeshCollider 를 얹고,
            // 게다가 중첩 프리팹에 added-component 오버라이드까지 남긴다.
            // 조각 어딘가에 이미 콜라이더가 있으면 그 조각은 통째로 건너뛴다.
            Collider existing = bridge.GetComponentInChildren<Collider>(true);
            if (existing != null)
            {
                log.AppendLine($"    = 콜라이더 있음, 건너뜀: {bridge.name} ({existing.GetType().Name} on {existing.name})");
                continue;
            }

            foreach (MeshFilter mf in bridge.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                if (mf.GetComponentInParent<Collider>() != null) continue;

                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.convex = false;
                added++;
                log.AppendLine($"    + MeshCollider: {bridge.name}/{mf.name} (mesh {mf.sharedMesh.name})");
            }
        }

        return added;
    }

    /// <summary>
    /// 다리가 뻗는 축을 저작 데이터에서 알아낸다(0 = X, 2 = Z). Y는 후보가 아니다 — 수평 다리다.
    ///
    /// 🔴 하드코딩하면 아트가 존 배치를 돌릴 때 <b>조용히</b> 틀린다. 실제로 그렇게 됐다 —
    /// 구 저작은 <c>x = ±14</c> 였고 V3 납품 후에는 <c>x = 0.037</c> 고정 · <c>z = ±13</c> 이다.
    /// 조각들의 닫힘 위치가 존 중앙에서 더 크게 벌어진 축을 고른다.
    /// </summary>
    static int DetectBridgeAxis(SerializedProperty segs)
    {
        float spreadX = 0f, spreadZ = 0f;

        for (int i = 0; i < segs.arraySize; i++)
        {
            Vector3 closed = segs.GetArrayElementAtIndex(i)
                                 .FindPropertyRelative("ClosedLocalPosition").vector3Value;
            spreadX = Mathf.Max(spreadX, Mathf.Abs(closed.x));
            spreadZ = Mathf.Max(spreadZ, Mathf.Abs(closed.z));
        }

        return spreadZ > spreadX ? 2 : 0;
    }

    static string AxisName(int axis) => axis == 0 ? "x" : axis == 1 ? "y" : "z";

    static List<Transform> Collect(GameObject root, string prefix)
        => Collect(root, new[] { prefix });

    /// <summary>
    /// 이름 접두사로 수집한다. 접두사가 여러 개인 이유는 아트가 다리를 두 종류
    /// (<c>floor_bridge</c> 교대 + <c>floor_MV_bridge</c> 데크)로 나눠 납품했기 때문이다.
    /// 정렬은 이름순 — <b>이 순서가 곧 패널 인덱스이자 복제 키</b>라 안정적이어야 한다.
    /// </summary>
    static List<Transform> Collect(GameObject root, string[] prefixes)
        => root.GetComponentsInChildren<Transform>(true)
               .Where(t => t != root.transform && prefixes.Any(p => t.name.StartsWith(p)))
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
