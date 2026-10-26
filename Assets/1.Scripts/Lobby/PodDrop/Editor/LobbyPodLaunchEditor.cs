using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// 로비 포드 걷기 경로 씬 뷰 편집 도구(할 일 100 C2c, <see cref="LobbyPodLaunch"/>).
/// <list type="bullet">
/// <item>Gizmo(선택 여부와 무관): 걷기 시작 지점 → 공용 경로점(walkPath, 리스트 순서) → 문턱 → 좌석을 잇고 번호·높이를 붙인다. 점마다 바닥까지 수직선.</item>
/// <item>인스펙터: walkPath 를 끌어서 순서 변경, 경로점 추가(선택한 줄 뒤 또는 끝)·삭제·바닥에 붙이기(Undo), 선택한 줄은 씬 뷰에서 이동 핸들.</item>
/// <item>경로 점검: 구간마다 플레이어 캡슐(Player.prefab 의 CapsuleCollider·GameRule 스텝 높이)로 훑어 막히는 구간을 콘솔 경고 + Gizmo 주황.</item>
/// </list>
/// 경로점 이름은 표시용이다 — 동작은 리스트 순서만 본다.
/// </summary>
[CustomEditor(typeof(LobbyPodLaunch))]
public sealed class LobbyPodLaunchEditor : Editor
{
    private const string Tag = "[LobbyPod 경로]";
    private const string PlayerPrefabPath = "Assets/2.Prefabs/Player/Player.prefab";

    // C0 조사값 — 프리팹을 못 읽을 때만 쓴다.
    private const float FallbackRadius = 0.38f;
    private const float FallbackHeight = 1.79f;
    private const float FallbackStepOffset = 0.3f;
    private const float GroundProbeUp = 1.5f;
    private const float GroundProbeDistance = 20f;

    private static readonly Color JoinColor = new Color(0.35f, 0.8f, 1f);      // 하늘 — 사람마다 합류
    private static readonly Color PathColor = new Color(1f, 0.9f, 0.2f);       // 노랑 — 공용 경로(충돌 켬)
    private static readonly Color InteriorColor = new Color(0.8f, 0.6f, 1f);   // 연보라 — 충돌 끔 보간(→ 문턱 → 좌석)
    private static readonly Color BlockedColor = new Color(1f, 0.55f, 0f);     // 주황 — 점검에서 막힌 구간
    private static readonly Color GroundLineColor = new Color(1f, 1f, 1f, 0.45f);

    private struct BlockedSegment
    {
        public Vector3 From;
        public Vector3 To;
        public Vector3 HitPoint;
    }

    private struct PlayerCapsule
    {
        public float Radius;
        public float Height;
        public float StepOffset;
        public int Mask;
        public string Source;
    }

    // 마지막 점검 결과(인스턴스별) — Gizmo 주황 표시용. 에디터 세션 동안만.
    private static readonly Dictionary<int, List<BlockedSegment>> LastBlocked = new Dictionary<int, List<BlockedSegment>>();
    private static readonly Dictionary<int, string> LastSummary = new Dictionary<int, string>();
    private static PlayerCapsule? _capsule;

    private ReorderableList _pathList;

    private LobbyPodLaunch Launch => (LobbyPodLaunch)target;

    private void OnEnable()
    {
        SerializedProperty path = serializedObject.FindProperty("walkPath");
        _pathList = new ReorderableList(serializedObject, path, true, true, true, true)
        {
            drawHeaderCallback = rect => EditorGUI.LabelField(rect, "공용 경로점 (walkPath) — 위에서부터 걷는 순서, 끌어서 바꾼다"),
            drawElementCallback = (rect, index, active, focused) =>
            {
                rect.height = EditorGUIUtility.singleLineHeight;
                rect.y += 1f;
                EditorGUI.PropertyField(rect, path.GetArrayElementAtIndex(index), new GUIContent($"#{index + 1}"));
            },
            onAddCallback = _ => AddPoint(),
            onRemoveCallback = _ => DeleteSelectedPoint(),
            onSelectCallback = _ => SceneView.RepaintAll(),
        };
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // 필드 순서 그대로 그리되 walkPath 만 끌어서 순서 바꾸는 리스트로.
        SerializedProperty it = serializedObject.GetIterator();
        bool enterChildren = true;
        while (it.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (it.propertyPath == "m_Script")
            {
                using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(it);
                continue;
            }

            if (it.propertyPath == "walkPath")
            {
                _pathList.DoLayoutList();
                continue;
            }

            EditorGUILayout.PropertyField(it, true);
        }

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("걷기 경로 편집", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "리스트에서 줄을 고르면 씬 뷰에 이동 핸들이 뜬다. '경로점 추가'는 고른 줄 뒤(없으면 끝)에 넣는다.\n" +
            "선: 하늘 = 각자 걷기 시작 지점 → 첫 경로점, 노랑 = 공용 경로(충돌 켬), 연보라 = 충돌 끔 보간(→ 문턱 → 좌석), 주황 = 점검에서 막힘.",
            MessageType.None);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("경로점 추가")) AddPoint();
            using (new EditorGUI.DisabledScope(SelectedIndex() < 0))
            {
                if (GUILayout.Button("선택한 경로점 삭제")) DeleteSelectedPoint();
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("경로점 바닥에 붙이기")) SnapToGround();
            if (GUILayout.Button("경로 점검")) CheckPath(Launch);
        }

        if (LastSummary.TryGetValue(Launch.GetInstanceID(), out string summary))
            EditorGUILayout.HelpBox(summary, summary.StartsWith("막힘 없음") ? MessageType.Info : MessageType.Warning);
    }

    // 리스트에서 고른 경로점 — 씬 뷰에서 바로 옮긴다(인스펙터를 떠나지 않게).
    private void OnSceneGUI()
    {
        int index = SelectedIndex();
        if (index < 0 || index >= Launch.EditorWalkPath.Count)
            return;

        Transform point = Launch.EditorWalkPath[index];
        if (point == null)
            return;

        EditorGUI.BeginChangeCheck();
        Vector3 moved = Handles.PositionHandle(point.position, Quaternion.identity);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(point, "경로점 이동");
            point.position = moved;
        }
    }

    private int SelectedIndex()
    {
        if (_pathList == null) return -1;
        int index = _pathList.index;
        return index >= 0 && index < _pathList.count ? index : -1;
    }

    // ---------------- 편집 ----------------

    private void AddPoint()
    {
        _pathList.index = AddPoint(Launch, SelectedIndex());
        SceneView.RepaintAll();
    }

    private void DeleteSelectedPoint()
    {
        int index = SelectedIndex();
        if (index < 0)
            return;

        int count = DeletePoint(Launch, index);
        _pathList.index = Mathf.Min(index, count - 1);
        SceneView.RepaintAll();
    }

    /// <summary>
    /// <paramref name="after"/> 번째 경로점 뒤(음수면 끝)에 새 경로점을 만들어 넣는다 — 앞뒤 점의 가운데, 기존 경로점과 같은 부모 아래. Undo 한 번에 되돌린다.
    /// 넣은 칸 번호를 돌려준다.
    /// </summary>
    public static int AddPoint(LobbyPodLaunch launch, int after)
    {
        var serializedObject = new SerializedObject(launch);
        SerializedProperty path = serializedObject.FindProperty("walkPath");
        int group = Undo.GetCurrentGroup();
        int insertAt = after >= 0 && after < path.arraySize ? after + 1 : path.arraySize;

        Transform previous = PreviousPoint(launch, insertAt);
        Transform next = NextPoint(launch, insertAt);
        Vector3 position = previous != null && next != null
            ? Vector3.Lerp(previous.position, next.position, 0.5f)
            : previous != null ? previous.position + Vector3.forward
            : next != null ? next.position - Vector3.forward
            : launch.transform.position;

        Transform parent = PointParent(launch);
        var go = new GameObject(GameObjectUtility.GetUniqueNameForSibling(parent, "W"));
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        Transform anchor = insertAt > 0 && insertAt - 1 < launch.EditorWalkPath.Count ? launch.EditorWalkPath[insertAt - 1] : null;
        if (anchor != null && anchor.parent == parent)
            go.transform.SetSiblingIndex(anchor.GetSiblingIndex() + 1);
        Undo.RegisterCreatedObjectUndo(go, "경로점 추가");

        path.InsertArrayElementAtIndex(insertAt);
        path.GetArrayElementAtIndex(insertAt).objectReferenceValue = go.transform;
        serializedObject.ApplyModifiedProperties();

        Undo.SetCurrentGroupName("경로점 추가");
        Undo.CollapseUndoOperations(group);
        Debug.Log($"{Tag} 경로점 #{insertAt + 1} 추가 — '{go.name}' {position:F2}", go);
        return insertAt;
    }

    /// <summary>
    /// <paramref name="index"/> 번째 경로점을 리스트에서 빼고 오브젝트를 지운다(걷기 시작·문턱·좌석이 같이 쓰면 리스트에서만). Undo 한 번에 되돌린다.
    /// 남은 경로점 수를 돌려준다.
    /// </summary>
    public static int DeletePoint(LobbyPodLaunch launch, int index)
    {
        var serializedObject = new SerializedObject(launch);
        SerializedProperty path = serializedObject.FindProperty("walkPath");
        if (index < 0 || index >= path.arraySize)
            return path.arraySize;

        Transform point = path.GetArrayElementAtIndex(index).objectReferenceValue as Transform;

        int group = Undo.GetCurrentGroup();
        path.GetArrayElementAtIndex(index).objectReferenceValue = null;
        path.DeleteArrayElementAtIndex(index);
        serializedObject.ApplyModifiedProperties();

        // 다른 칸(걷기 시작·문턱·좌석·같은 경로점 중복)이 쓰는 오브젝트면 리스트에서만 뺀다.
        if (point != null && !IsReferencedElsewhere(launch, point))
            Undo.DestroyObjectImmediate(point.gameObject);

        Undo.SetCurrentGroupName("선택한 경로점 삭제");
        Undo.CollapseUndoOperations(group);
        Debug.Log($"{Tag} 경로점 #{index + 1} 삭제{(point != null ? $" — '{point.name}'" : " (빈 칸)")}", launch);
        return path.arraySize;
    }

    private void SnapToGround() => SnapToGround(Launch);

    /// <summary>걷기 시작 지점·공용 경로점을 지면 높이로(문턱·좌석은 포드 안 높이라 건드리지 않는다). Undo 한 번에 되돌린다.</summary>
    public static void SnapToGround(LobbyPodLaunch launch)
    {
        PlayerCapsule capsule = GetCapsule();
        var points = new List<Transform>();
        foreach (Transform t in launch.EditorWalkStarts) if (t != null && !points.Contains(t)) points.Add(t);
        foreach (Transform t in launch.EditorWalkPath) if (t != null && !points.Contains(t)) points.Add(t);

        int group = Undo.GetCurrentGroup();
        var log = new StringBuilder($"{Tag} 바닥에 붙이기");
        foreach (Transform t in points)
        {
            if (!TryGround(launch, t.position, capsule.Mask, out RaycastHit hit))
            {
                log.Append($"\n  {t.name}: 아래에 지면 없음 — 그대로");
                continue;
            }

            float dy = hit.point.y - t.position.y;
            if (Mathf.Abs(dy) < 1e-4f) continue;
            Undo.RecordObject(t, "경로점 바닥에 붙이기");
            t.position = hit.point;
            log.Append($"\n  {t.name}: {dy:+0.00;-0.00}m ({hit.collider.name})");
        }

        Undo.SetCurrentGroupName("경로점 바닥에 붙이기");
        Undo.CollapseUndoOperations(group);
        Debug.Log(log.ToString(), launch);
        SceneView.RepaintAll();
    }

    private static Transform PreviousPoint(LobbyPodLaunch launch, int insertAt)
    {
        for (int i = insertAt - 1; i >= 0; i--)
            if (i < launch.EditorWalkPath.Count && launch.EditorWalkPath[i] != null) return launch.EditorWalkPath[i];
        foreach (Transform t in launch.EditorWalkStarts) if (t != null) return t;
        return null;
    }

    private static Transform NextPoint(LobbyPodLaunch launch, int insertAt)
    {
        for (int i = insertAt; i < launch.EditorWalkPath.Count; i++)
            if (launch.EditorWalkPath[i] != null) return launch.EditorWalkPath[i];
        return launch.EditorThreshold;
    }

    // 새 경로점은 기존 경로점(없으면 문턱·걷기 시작 지점)과 같은 부모(PodLaunch) 아래에 만든다.
    private static Transform PointParent(LobbyPodLaunch launch)
    {
        foreach (Transform t in launch.EditorWalkPath) if (t != null) return t.parent;
        if (launch.EditorThreshold != null) return launch.EditorThreshold.parent;
        foreach (Transform t in launch.EditorWalkStarts) if (t != null) return t.parent;
        return launch.transform;
    }

    private static bool IsReferencedElsewhere(LobbyPodLaunch launch, Transform point)
    {
        if (launch.EditorThreshold == point) return true;
        foreach (Transform t in launch.EditorWalkStarts) if (t == point) return true;
        foreach (Transform t in launch.EditorSeats) if (t == point) return true;
        foreach (Transform t in launch.EditorWalkPath) if (t == point) return true;
        return false;
    }

    // ---------------- 경로 점검 ----------------

    /// <summary>구간마다 플레이어 캡슐로 훑어 막히는 곳을 찾는다. 결과는 콘솔 + Gizmo 주황. 막힌 구간 수를 돌려준다.</summary>
    public static int CheckPath(LobbyPodLaunch launch)
    {
        _capsule = null; // 프리팹·GameRule 을 다시 읽는다
        PlayerCapsule capsule = GetCapsule();
        var blocked = new List<BlockedSegment>();
        var log = new StringBuilder();
        int segments = 0;

        foreach (Segment segment in Segments(launch))
        {
            segments++;
            Vector3 from = Grounded(launch, segment.From, capsule.Mask);
            Vector3 to = Grounded(launch, segment.To, capsule.Mask);
            List<RaycastHit> hits = SweepCapsule(launch, capsule, from, to);
            if (hits.Count == 0)
                continue;

            var names = new List<string>();
            foreach (RaycastHit hit in hits)
            {
                string name = $"{HierarchyPath(hit.collider.transform)} (레이어 {LayerMask.LayerToName(hit.collider.gameObject.layer)}" +
                              (hit.distance <= 0f ? ", 시작점에서 겹침" : $", {hit.distance:0.00}m 지점") + ")";
                if (!names.Contains(name)) names.Add(name);
            }

            Vector3 hitPoint = hits[0].distance > 0f ? hits[0].point : from;
            blocked.Add(new BlockedSegment { From = segment.From, To = segment.To, HitPoint = hitPoint });
            log.Append($"\n  막힘: {segment.Label}{(segment.CollisionOff ? " [충돌 끔 보간 — 막히진 않지만 벽을 뚫고 지나가 보인다]" : "")}");
            foreach (string name in names) log.Append($"\n    - {name}");
        }

        int id = launch.GetInstanceID();
        LastBlocked[id] = blocked;
        string head = blocked.Count == 0
            ? $"막힘 없음 — 구간 {segments}개"
            : $"막힌 구간 {blocked.Count}/{segments}개 (Gizmo 주황)";
        LastSummary[id] = $"{head}\n캡슐 반지름 {capsule.Radius:0.00}·높이 {capsule.Height:0.00}·스텝 {capsule.StepOffset:0.00} ({capsule.Source})";

        string message = $"{Tag} 경로 점검 — {head}. 캡슐 반지름 {capsule.Radius:0.00}m·높이 {capsule.Height:0.00}m·스텝 {capsule.StepOffset:0.00}m ({capsule.Source}){log}";
        if (blocked.Count == 0) Debug.Log(message, launch);
        else Debug.LogWarning(message, launch);

        SceneView.RepaintAll();
        return blocked.Count;
    }

    private struct Segment
    {
        public Vector3 From;
        public Vector3 To;
        public string Label;
        public bool CollisionOff;
    }

    // 실제 보행과 같은 경로 — 슬롯마다 걷기 시작 → 첫 점(합류), 공용 경로, → 문턱(충돌 끔), 문턱 → 좌석은 포드 안이라 점검하지 않는다.
    private static IEnumerable<Segment> Segments(LobbyPodLaunch launch)
    {
        PodLaunchRules.WalkRoute route = SharedRoute(launch);
        List<Vector3> starts = Compact(launch.EditorWalkStarts);
        Vector3? threshold = launch.EditorThreshold != null ? launch.EditorThreshold.position : (Vector3?)null;
        Vector3? first = route.Ground.Length > 0 ? route.Ground[0] : threshold;

        if (first.HasValue)
        {
            for (int slot = 0; slot < starts.Count; slot++)
                yield return new Segment
                {
                    From = starts[slot], To = first.Value,
                    Label = $"걷기 시작 {slot} → {(route.Ground.Length > 0 ? "경로점 #1" : "문턱")}",
                    CollisionOff = route.Ground.Length == 0,
                };
        }

        for (int i = 1; i < route.Ground.Length; i++)
            yield return new Segment { From = route.Ground[i - 1], To = route.Ground[i], Label = $"경로점 #{i} → #{i + 1}" };

        if (route.Ground.Length > 0 && threshold.HasValue)
            yield return new Segment
            {
                From = route.Ground[route.Ground.Length - 1], To = threshold.Value,
                Label = $"경로점 #{route.Ground.Length} → 문턱", CollisionOff = true,
            };
    }

    // 실제 보행 경로의 공용 부분(좌석만 사람마다 다르다 — 여기선 문턱 위치를 자리 표시로 쓴다).
    private static PodLaunchRules.WalkRoute SharedRoute(LobbyPodLaunch launch)
    {
        Transform seat = launch.EditorThreshold != null ? launch.EditorThreshold : launch.transform;
        return launch.EditorBuildRoute(seat);
    }

    // 발 위치에서 스텝 높이만큼 띄운 캡슐(계단 턱은 넘는다 — 평소 걷기와 같다)로 from → to 를 훑는다. 포드 콜라이더(연출 중 꺼진다)·트리거는 뺀다.
    private static List<RaycastHit> SweepCapsule(LobbyPodLaunch launch, PlayerCapsule capsule, Vector3 from, Vector3 to)
    {
        var result = new List<RaycastHit>();
        Vector3 delta = to - from;
        float distance = delta.magnitude;
        if (distance < 1e-3f)
            return result;

        float bottom = capsule.StepOffset + 0.02f + capsule.Radius;
        float top = Mathf.Max(bottom, capsule.Height - capsule.Radius);
        RaycastHit[] hits = Physics.CapsuleCastAll(from + Vector3.up * bottom, from + Vector3.up * top, capsule.Radius,
            delta / distance, distance, capsule.Mask, QueryTriggerInteraction.Ignore);

        foreach (RaycastHit hit in hits)
        {
            if (IsPodCollider(launch, hit.collider)) continue;
            result.Add(hit);
        }

        result.Sort((a, b) => a.distance.CompareTo(b.distance));
        return result;
    }

    private static PlayerCapsule GetCapsule()
    {
        if (_capsule.HasValue)
            return _capsule.Value;

        var capsule = new PlayerCapsule
        {
            Radius = FallbackRadius,
            Height = FallbackHeight,
            StepOffset = FallbackStepOffset,
            Mask = LayerMask.GetMask("Default", "Ground", "Wall", "Env", "Barrier"),
            Source = "기본값(C0 조사값)",
        };

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        PlayerMotor motor = prefab != null ? prefab.GetComponentInChildren<PlayerMotor>(true) : null;
        CapsuleCollider collider = motor != null ? motor.GetComponent<CapsuleCollider>() : null;
        if (collider != null)
        {
            Vector3 scale = collider.transform.lossyScale;
            capsule.Radius = collider.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            capsule.Height = Mathf.Max(collider.height * Mathf.Abs(scale.y), capsule.Radius * 2f);
            capsule.Source = "Player.prefab";

            var gameRule = new SerializedObject(motor).FindProperty("gameRule")?.objectReferenceValue as PlayerGameRuleData;
            if (gameRule != null)
            {
                capsule.StepOffset = gameRule.StepOffset;
                capsule.Mask = gameRule.ObstacleMask.value;
                capsule.Source += $" + {gameRule.name}";
            }
        }

        // 연출 중엔 유닛 통과(PassThroughUnitsOverride) — 플레이어·영혼·적 몸은 막지 않는다.
        capsule.Mask &= ~LayerMask.GetMask("Player", "Soul", "Enemy");
        _capsule = capsule;
        return capsule;
    }

    private static bool TryGround(LobbyPodLaunch launch, Vector3 point, int mask, out RaycastHit ground)
    {
        RaycastHit[] hits = Physics.RaycastAll(point + Vector3.up * GroundProbeUp, Vector3.down, GroundProbeDistance, mask, QueryTriggerInteraction.Ignore);
        ground = default;
        float best = float.MaxValue;
        foreach (RaycastHit hit in hits)
        {
            if (IsPodCollider(launch, hit.collider) || hit.distance >= best) continue;
            best = hit.distance;
            ground = hit;
        }

        return best < float.MaxValue;
    }

    private static Vector3 Grounded(LobbyPodLaunch launch, Vector3 point, int mask) =>
        TryGround(launch, point, mask, out RaycastHit hit) ? hit.point : point;

    private static bool IsPodCollider(LobbyPodLaunch launch, Collider collider) =>
        launch.EditorPod != null && collider.transform.IsChildOf(launch.EditorPod);

    private static List<Vector3> Compact(IReadOnlyList<Transform> points)
    {
        var list = new List<Vector3>();
        foreach (Transform t in points) if (t != null) list.Add(t.position);
        return list;
    }

    private static string HierarchyPath(Transform t)
    {
        string path = t.name;
        for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
        return path;
    }

    // ---------------- Gizmo ----------------

    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
    private static void DrawPathGizmo(LobbyPodLaunch launch, GizmoType gizmoType)
    {
        if (Application.isPlaying)
            return; // 런타임엔 포드가 움직인다 — 편집용 표시만

        PlayerCapsule capsule = GetCapsule();
        PodLaunchRules.WalkRoute route = SharedRoute(launch);
        Transform threshold = launch.EditorThreshold;
        Vector3? first = route.Ground.Length > 0 ? route.Ground[0] : threshold != null ? threshold.position : (Vector3?)null;
        GUIStyle style = LabelStyle();

        // 걷기 시작 → 첫 점(사람마다 합류)
        List<Vector3> starts = Compact(launch.EditorWalkStarts);
        for (int slot = 0; slot < starts.Count; slot++)
        {
            if (first.HasValue) DrawLine(starts[slot], first.Value, route.Ground.Length > 0 ? JoinColor : InteriorColor, 3f);
            DrawPoint(launch, starts[slot], JoinColor, $"시작 {slot}", capsule.Mask, style);
        }

        // 공용 경로(충돌 켬)
        for (int i = 0; i < route.Ground.Length; i++)
        {
            if (i > 0) DrawLine(route.Ground[i - 1], route.Ground[i], PathColor, 4f);
            DrawPoint(launch, route.Ground[i], PathColor, $"#{i + 1}", capsule.Mask, style);
        }

        // → 문턱 → 좌석(충돌 끔 보간)
        if (threshold != null)
        {
            if (route.Ground.Length > 0) DrawLine(route.Ground[route.Ground.Length - 1], threshold.position, InteriorColor, 3f);
            DrawPoint(launch, threshold.position, InteriorColor, "문턱", capsule.Mask, style);
        }

        IReadOnlyList<Transform> seats = launch.EditorSeats;
        for (int i = 0; i < seats.Count; i++)
        {
            if (seats[i] == null) continue;
            Vector3 from = threshold != null ? threshold.position : route.Ground.Length > 0 ? route.Ground[route.Ground.Length - 1] : seats[i].position;
            DrawLine(from, seats[i].position, InteriorColor, 2f);
            DrawPoint(launch, seats[i].position, InteriorColor, $"좌석 {i}", capsule.Mask, style);
        }

        // 마지막 점검에서 막힌 구간
        if (LastBlocked.TryGetValue(launch.GetInstanceID(), out List<BlockedSegment> blocked))
        {
            foreach (BlockedSegment segment in blocked)
            {
                DrawLine(segment.From + Vector3.up * 0.05f, segment.To + Vector3.up * 0.05f, BlockedColor, 8f);
                Gizmos.color = BlockedColor;
                Gizmos.DrawWireSphere(segment.HitPoint, 0.3f);
            }
        }
    }

    private static void DrawLine(Vector3 from, Vector3 to, Color color, float width)
    {
        Handles.color = color;
        Handles.DrawAAPolyLine(width, from, to);
    }

    // 점 + 번호·높이 라벨 + 바닥까지 수직선(높이 변화가 보이게).
    private static void DrawPoint(LobbyPodLaunch launch, Vector3 position, Color color, string label, int mask, GUIStyle style)
    {
        Gizmos.color = color;
        Gizmos.DrawSphere(position, 0.12f);

        string height = $"y {position.y:0.00}";
        if (TryGround(launch, position, mask, out RaycastHit ground))
        {
            float above = position.y - ground.point.y;
            if (Mathf.Abs(above) > 0.02f)
            {
                Handles.color = GroundLineColor;
                Handles.DrawDottedLine(position, ground.point, 3f);
                Handles.DrawWireDisc(ground.point, Vector3.up, 0.15f);
                height += $" (바닥{above:+0.00;-0.00})";
            }
        }

        style.normal.textColor = color;
        Handles.Label(position + Vector3.up * 0.35f, $"{label}\n{height}", style);
    }

    private static GUIStyle _style;

    private static GUIStyle LabelStyle()
    {
        if (_style == null)
            _style = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.LowerCenter, fontSize = 11 };
        return _style;
    }
}
