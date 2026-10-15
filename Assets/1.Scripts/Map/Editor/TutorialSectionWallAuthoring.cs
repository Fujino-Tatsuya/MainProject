using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 튜토리얼 복도·존 벽 Box 콜라이더 저작 도구 (할 일 32 세팅 — Docs/tech/wall-concept.md).
//
// Level_wall_hallway_tutorial 은 벽·바닥 메시를 그룹(Hallway_1~4, Sector_1~5)으로 정리해 두었다. 이 메뉴가 그룹마다
// 자식 'Colliders' 아래 지상층 벽 박스 Wall_00, Wall_01 …(비트리거 BoxCollider, Wall 태그·레이어, 렌더러 없음)을 만든다.
// 다시 돌리면 같은 결과(멱등) — 이미 맞으면 저장하지 않는다. wall_group_*·Sections·바닥(floor_*)은 건드리지 않는다.
//
//  · 벽 조각 = 그룹 아래 렌더러 하나하나(코너 COM 은 자식 렌더러 3개로 쪼개 본다). 경로(자기 포함)에 'ground floor' 가
//    들어가면 지상층 벽, 'basement' 가 들어가면 제외, 경로 노드가 'floor' 로 시작하면(floor_hallway 등) 바닥이라 제외.
//    이름으로 못 가르는 조각(Wall_Hbeam 등)은 bounds 로 — 바닥 윗면 +0.5m 아래에서 시작해 +1m 이상 솟으면 벽.
//  · 두께 축 = 렌더러 bounds 의 XZ 중 얇은 축. 두 축 차이가 0.5m 안쪽이면 정사각 조각(코너 기둥).
//  · 같은 축·두께 중심선 ±0.1m 이고 길이 방향 간격 ≤ 0.1m 인 조각을 박스 하나로 합친다(조각 없는 문 자리에서 끊긴다).
//    정사각 조각은 두께 범위가 겹치고 끝이 0.1m 안에 닿는 첫 박스를 길이 방향으로 늘려 붙인다 — 코너 바깥 틈 방지.
//    붙을 박스가 없으면 따로 작은 박스.
//  · 높이 = 바닥 윗면 ~ +8m. 바닥 윗면 = 그룹 안 납작한 바닥 렌더러(floor_*) 윗면 최빈값, 없으면 벽 조각 아랫면 최빈값.
//  · 이름 순서 = 축(X 먼저) → 두께 중심 → 길이 시작. 1cm 반올림.
public static class TutorialSectionWallAuthoring
{
    const string PrefabPath = "Assets/2.Prefabs/Environment/Architecture/Walls/Corridors/Sections/Level_wall_hallway_tutorial.prefab";
    const string CollidersName = "Colliders";
    const string WallPrefix = "Wall_";
    const string Tag = "[TutorialWalls]";

    const float Height = 8f;
    const float LineTolerance = 0.1f;      // 두께 중심선 허용 오차
    const float JoinGap = 0.1f;            // 길이 방향 이 간격 안이면 합친다
    const float SquareDelta = 0.5f;        // XZ 두 축 차이가 이 안이면 정사각 조각
    const float WallMinRise = 1f;          // 이름으로 못 가르는 조각: 바닥 위로 이만큼 솟아야 벽
    const float WallMaxBase = 0.5f;        // 이름으로 못 가르는 조각: 바닥 위 이 높이 아래에서 시작해야 벽
    const float FlatMaxThickness = 1f;
    const float DoorMinGap = 1.5f;
    const float Epsilon = 0.001f;

    static readonly string[] GroupPrefixes = { "Hallway_", "Sector_" };

    enum Axis { X, Z, Square }

    class Piece
    {
        public string Name;
        public Bounds Bounds;   // 그룹 로컬
        public Axis Axis;
    }

    class Segment
    {
        public Axis Axis;           // X = X 방향으로 긴 벽(두께 Z), Z = Z 방향으로 긴 벽(두께 X), Square = 따로 남은 정사각 조각
        public float Min, Max;      // 길이 방향
        public float TMin, TMax;    // 두께 방향
        public List<string> Pieces = new List<string>();
        public float Center => (TMin + TMax) * 0.5f;
    }

    struct WallSpec
    {
        public string Name;
        public Vector3 Center;
        public Vector3 Size;
    }

    [MenuItem("Tools/Map/Authoring/Walls/튜토리얼 섹션 벽 콜라이더 (멱등)")]
    public static void Generate()
    {
        int wallLayer = LayerMask.NameToLayer(GameLayers.Wall);
        if (wallLayer < 0)
        {
            Debug.LogError($"{Tag} '{GameLayers.Wall}' 레이어가 없다.");
            return;
        }

        var report = new StringBuilder();
        report.AppendLine($"{Tag} 튜토리얼 섹션 벽 콜라이더 — 높이 {Height}m · 태그/레이어 {GameLayers.Wall} · {PrefabPath}");
        var notes = new List<string>();

        EditPrefab(PrefabPath, root =>
        {
            bool changed = false;
            foreach (Transform group in root.transform.Cast<Transform>().Where(IsGroup).OrderBy(g => g.name))
            {
                if (Quaternion.Angle(group.localRotation, Quaternion.identity) > 0.01f || (group.localScale - Vector3.one).sqrMagnitude > 1e-6f)
                {
                    report.AppendLine($"== {group.name}: 건너뜀 — 그룹이 회전/스케일돼 있다(rot {group.localEulerAngles}, scale {group.localScale})");
                    continue;
                }

                List<Piece> pieces = CollectPieces(group, out float floorTop, out string floorSource, notes);
                List<Segment> segments = Merge(pieces, notes, group.name);
                List<WallSpec> specs = BuildSpecs(segments, floorTop);
                bool groupChanged = Apply(group, specs, wallLayer);
                changed |= groupChanged;

                report.AppendLine($"== {group.name}: {(groupChanged ? "갱신" : "변경 없음")} · 박스 {specs.Count}개 · 조각 {pieces.Count}개 · 바닥 윗면 {floorTop:0.##}({floorSource})");
                report.AppendLine("| 이름 | 축 | 중심 (그룹 로컬) | 크기 | 조각 수 |");
                for (int i = 0; i < specs.Count; i++)
                {
                    WallSpec s = specs[i];
                    report.AppendLine($"| {s.Name} | {segments[i].Axis} | ({s.Center.x:0.##}, {s.Center.y:0.##}, {s.Center.z:0.##}) | {s.Size.x:0.##} × {s.Size.y:0.##} × {s.Size.z:0.##} | {segments[i].Pieces.Count} |");
                }
                foreach (string door in FindDoors(segments))
                    report.AppendLine($"  문 자리: {door}");
            }
            return changed;
        }, out string editError);

        if (editError != null) report.AppendLine("건너뜀: " + editError);
        if (notes.Count > 0)
        {
            report.AppendLine("확인 목록:");
            foreach (string n in notes) report.AppendLine("  · " + n);
        }
        Debug.Log(report.ToString());
    }

    static bool IsGroup(Transform t) => GroupPrefixes.Any(p => t.name.StartsWith(p));

    // ─── 실측 ──────────────────────────────────────────────────────────────

    static List<Piece> CollectPieces(Transform group, out float floorTop, out string floorSource, List<string> notes)
    {
        var walls = new List<(Renderer r, Bounds b)>();
        var unnamed = new List<(Renderer r, Bounds b)>();
        var floors = new List<Bounds>();
        foreach (Renderer r in group.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (IsUnder(r.transform, group, CollidersName)) continue;
            string path = PathLower(r.transform, group);
            Bounds b = ToLocal(group, r.bounds);
            if (path.Contains("basement")) continue;
            if (path.Contains("ground floor")) walls.Add((r, b));
            else if (StartsWithFloor(r.transform, group)) { if (b.size.y <= FlatMaxThickness) floors.Add(b); }
            else unnamed.Add((r, b));
        }

        if (floors.Count > 0)
        {
            floorTop = floors.GroupBy(b => Snap(b.max.y)).OrderByDescending(g => g.Sum(b => b.size.x * b.size.z)).ThenBy(g => g.Key).First().Key;
            floorSource = "바닥 렌더러 윗면";
        }
        else if (walls.Count > 0)
        {
            floorTop = walls.GroupBy(w => Snap(w.b.min.y)).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
            floorSource = "바닥 렌더러 없음 — 지상층 벽 아랫면";
        }
        else
        {
            floorTop = 0f;
            floorSource = "바닥·벽 없음 — 0";
        }

        var pieces = walls.Select(w => ToPiece(w.r.name, w.b)).ToList();
        foreach (var u in unnamed)
        {
            bool wallLike = u.b.min.y <= floorTop + WallMaxBase && u.b.max.y >= floorTop + WallMinRise;
            notes.Add($"{group.name}: 이름으로 못 가른 '{u.r.name}' y {u.b.min.y:0.##}~{u.b.max.y:0.##} → {(wallLike ? "벽으로 포함" : "제외(바닥에서 솟은 벽형 아님)")}");
            if (wallLike) pieces.Add(ToPiece(u.r.name, u.b));
        }
        return pieces;
    }

    static Piece ToPiece(string name, Bounds b)
    {
        Axis axis = Mathf.Abs(b.size.x - b.size.z) <= SquareDelta ? Axis.Square : b.size.x > b.size.z ? Axis.X : Axis.Z;
        return new Piece { Name = name, Bounds = b, Axis = axis };
    }

    static bool IsUnder(Transform t, Transform group, string name)
    {
        for (; t != null && t != group; t = t.parent)
            if (t.name == name && t.parent == group) return true;
        return false;
    }

    static string PathLower(Transform t, Transform group)
    {
        var sb = new StringBuilder();
        for (; t != null && t != group; t = t.parent) sb.Append(t.name.ToLowerInvariant()).Append('/');
        return sb.ToString();
    }

    static bool StartsWithFloor(Transform t, Transform group)
    {
        for (; t != null && t != group; t = t.parent)
            if (t.name.ToLowerInvariant().StartsWith("floor")) return true;
        return false;
    }

    // ─── 합치기 ────────────────────────────────────────────────────────────

    static List<Segment> Merge(List<Piece> pieces, List<string> notes, string groupName)
    {
        var segments = new List<Segment>();
        foreach (Axis axis in new[] { Axis.X, Axis.Z })
        {
            List<Segment> lines = pieces.Where(p => p.Axis == axis).Select(p => ToSegment(p, axis))
                .OrderBy(s => s.Center).ThenBy(s => s.Min).ToList();

            // 두께 중심선 무리(첫 조각 기준 ±0.1m) → 그 안에서 길이 방향으로 이어 붙인다.
            var buckets = new List<List<Segment>>();
            foreach (Segment s in lines)
            {
                if (buckets.Count > 0 && Mathf.Abs(s.Center - buckets[buckets.Count - 1][0].Center) <= LineTolerance + Epsilon)
                    buckets[buckets.Count - 1].Add(s);
                else
                    buckets.Add(new List<Segment> { s });
            }
            foreach (List<Segment> bucket in buckets)
            {
                Segment cur = null;
                foreach (Segment s in bucket.OrderBy(s => s.Min))
                {
                    if (cur != null && s.Min <= cur.Max + JoinGap + Epsilon)
                    {
                        cur.Max = Mathf.Max(cur.Max, s.Max);
                        cur.TMin = Mathf.Min(cur.TMin, s.TMin);
                        cur.TMax = Mathf.Max(cur.TMax, s.TMax);
                        cur.Pieces.AddRange(s.Pieces);
                        continue;
                    }
                    cur = s;
                    segments.Add(cur);
                }
            }
        }

        foreach (Piece sq in pieces.Where(p => p.Axis == Axis.Square).OrderBy(p => p.Bounds.min.x).ThenBy(p => p.Bounds.min.z))
        {
            Segment host = segments.Where(s => s.Axis != Axis.Square).FirstOrDefault(s => Touches(s, sq.Bounds));
            if (host != null)
            {
                Range(sq.Bounds, host.Axis, out float min, out float max, out _, out _);
                host.Min = Mathf.Min(host.Min, min);
                host.Max = Mathf.Max(host.Max, max);
                host.Pieces.Add(sq.Name);
                continue;
            }
            Segment own = ToSegment(sq, Axis.X);
            own.Axis = Axis.Square;
            segments.Add(own);
            notes.Add($"{groupName}: 정사각 조각 '{sq.Name}' 이 붙을 직선이 없어 따로 박스");
        }

        foreach (Segment s in segments.Where(s => s.Axis != Axis.Square && s.Pieces.Count == 1))
            notes.Add($"{groupName}: 합칠 이웃이 없는 고립 조각 '{s.Pieces[0]}' ({s.Axis} 축, 길이 {s.Max - s.Min:0.##}m)");

        // 이름 순서 — 축(X → Z → Square) → 두께 중심 → 길이 시작.
        return segments.OrderBy(s => s.Axis).ThenBy(s => Snap(s.Center)).ThenBy(s => Snap(s.Min)).ToList();
    }

    static Segment ToSegment(Piece p, Axis axis)
    {
        Range(p.Bounds, axis, out float min, out float max, out float tmin, out float tmax);
        var s = new Segment { Axis = axis, Min = min, Max = max, TMin = tmin, TMax = tmax };
        s.Pieces.Add(p.Name);
        return s;
    }

    static void Range(Bounds b, Axis axis, out float min, out float max, out float tmin, out float tmax)
    {
        bool x = axis != Axis.Z;
        min = x ? b.min.x : b.min.z;
        max = x ? b.max.x : b.max.z;
        tmin = x ? b.min.z : b.min.x;
        tmax = x ? b.max.z : b.max.x;
    }

    /// <summary>정사각 조각이 이 박스의 두께 범위와 겹치고, 길이 방향으로 0.1m 안에 닿는가.</summary>
    static bool Touches(Segment s, Bounds b)
    {
        Range(b, s.Axis, out float min, out float max, out float tmin, out float tmax);
        bool thickOverlap = tmin < s.TMax - Epsilon && tmax > s.TMin + Epsilon;
        bool alongTouch = min <= s.Max + JoinGap + Epsilon && max >= s.Min - JoinGap - Epsilon;
        return thickOverlap && alongTouch;
    }

    static List<WallSpec> BuildSpecs(List<Segment> segments, float floorTop)
    {
        var specs = new List<WallSpec>();
        for (int i = 0; i < segments.Count; i++)
        {
            Segment s = segments[i];
            float min = Snap(s.Min), max = Snap(s.Max), tmin = Snap(s.TMin), tmax = Snap(s.TMax);
            float along = (min + max) * 0.5f, thick = (tmin + tmax) * 0.5f;
            bool x = s.Axis != Axis.Z;
            specs.Add(new WallSpec
            {
                Name = WallPrefix + i.ToString("00"),
                Center = Snap(new Vector3(x ? along : thick, floorTop + Height * 0.5f, x ? thick : along)),
                Size = Snap(new Vector3(x ? max - min : tmax - tmin, Height, x ? tmax - tmin : max - min)),
            });
        }
        return specs;
    }

    /// <summary>같은 두께 중심선(±0.1m) 위 박스 사이 간격 ≥ 1.5m — 문 자리 후보.</summary>
    static List<string> FindDoors(List<Segment> segments)
    {
        var doors = new List<string>();
        foreach (var byAxis in segments.Where(s => s.Axis != Axis.Square).GroupBy(s => s.Axis))
        {
            var sorted = byAxis.OrderBy(s => s.Center).ThenBy(s => s.Min).ToList();
            for (int i = 0; i + 1 < sorted.Count; i++)
            {
                Segment a = sorted[i], b = sorted[i + 1];
                if (Mathf.Abs(a.Center - b.Center) > LineTolerance + Epsilon) continue;
                float gap = b.Min - a.Max;
                if (gap < DoorMinGap) continue;
                string line = byAxis.Key == Axis.X ? $"z {a.Center:0.##}" : $"x {a.Center:0.##}";
                string along = byAxis.Key == Axis.X ? "x" : "z";
                doors.Add($"{line} 선 {along} {a.Max:0.##}~{b.Min:0.##} (폭 {gap:0.##}m)");
            }
        }
        return doors;
    }

    // ─── 적용(멱등) ────────────────────────────────────────────────────────

    /// <summary>
    /// 그룹 아래 Colliders/Wall_NN 을 사양과 같게 맞춘다. 이미 같으면 false.
    /// Colliders 아래 사양에 없는 자식은 지우고, 남는 오브젝트는 Transform + BoxCollider 만 둔다.
    /// </summary>
    static bool Apply(Transform group, List<WallSpec> specs, int wallLayer)
    {
        Transform parent = group.Find(CollidersName);
        if (parent != null && Matches(parent, specs, wallLayer)) return false;

        if (parent == null)
        {
            parent = new GameObject(CollidersName).transform;
            parent.SetParent(group, false);
        }
        parent.localPosition = Vector3.zero;
        parent.localRotation = Quaternion.identity;
        parent.localScale = Vector3.one;
        parent.gameObject.layer = wallLayer;
        parent.gameObject.SetActive(true);

        var keep = new HashSet<string>(specs.Select(s => s.Name));
        foreach (Transform child in parent.Cast<Transform>().ToList())
            if (!keep.Contains(child.name)) Object.DestroyImmediate(child.gameObject);

        foreach (WallSpec s in specs)
        {
            Transform t = parent.Find(s.Name);
            if (t == null)
            {
                t = new GameObject(s.Name).transform;
                t.SetParent(parent, false);
            }
            t.SetSiblingIndex(specs.IndexOf(s));
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            GameObject go = t.gameObject;
            go.SetActive(true);
            go.layer = wallLayer;
            go.tag = GameLayers.Wall;
            foreach (Component c in go.GetComponents<Component>())
                if (!(c is Transform) && !(c is BoxCollider)) Object.DestroyImmediate(c);
            BoxCollider[] boxes = go.GetComponents<BoxCollider>();
            for (int i = 1; i < boxes.Length; i++) Object.DestroyImmediate(boxes[i]);
            BoxCollider box = boxes.Length > 0 ? boxes[0] : go.AddComponent<BoxCollider>();
            box.isTrigger = false;
            box.enabled = true;
            box.center = s.Center;
            box.size = s.Size;
        }
        return true;
    }

    static bool Matches(Transform parent, List<WallSpec> specs, int wallLayer)
    {
        if (parent.localPosition != Vector3.zero || parent.localRotation != Quaternion.identity || parent.localScale != Vector3.one) return false;
        if (parent.gameObject.layer != wallLayer || !parent.gameObject.activeSelf) return false;
        if (parent.childCount != specs.Count) return false;
        for (int i = 0; i < specs.Count; i++)
        {
            WallSpec s = specs[i];
            Transform t = parent.GetChild(i);
            GameObject go = t.gameObject;
            if (t.name != s.Name || !go.activeSelf || go.layer != wallLayer || !go.CompareTag(GameLayers.Wall)) return false;
            if (t.localPosition != Vector3.zero || t.localRotation != Quaternion.identity || t.localScale != Vector3.one) return false;
            Component[] comps = go.GetComponents<Component>();
            if (comps.Length != 2 || !(comps[1] is BoxCollider box)) return false;
            if (box.isTrigger || !box.enabled) return false;
            if ((box.center - s.Center).sqrMagnitude > Epsilon * Epsilon) return false;
            if ((box.size - s.Size).sqrMagnitude > Epsilon * Epsilon) return false;
        }
        return true;
    }

    // ─── 공용 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 프리팹을 열어 <paramref name="edit"/> 하고, true 면 저장한다. 그 프리팹이 프리팹 모드로 열려 있으면 그 스테이지에서 편집·저장한다
    /// (저장 안 된 수정이 있으면 건드리지 않고 <paramref name="error"/>). BoundaryWallAuthoring.EditPrefab 과 같다.
    /// </summary>
    static void EditPrefab(string path, System.Func<GameObject, bool> edit, out string error)
    {
        error = null;
        PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null && stage.assetPath == path)
        {
            if (stage.scene.isDirty)
            {
                error = "프리팹 모드에 저장 안 된 수정이 있다 — 저장하거나 닫고 다시 돌릴 것";
                return;
            }
            if (edit(stage.prefabContentsRoot))
            {
                PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, path);
                stage.ClearDirtiness();
            }
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            if (edit(root)) PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // 실측값을 1cm 로 반올림 — 렌더러 bounds 의 부동소수 잡음이 다시 돌릴 때마다 diff 를 만들지 않게.
    static float Snap(float v) => Mathf.Round(v * 100f) / 100f;
    static Vector3 Snap(Vector3 v) => new Vector3(Snap(v.x), Snap(v.y), Snap(v.z));

    /// <summary>월드 bounds → 그룹 로컬 bounds(8 꼭짓점 변환 후 재포장). LoadPrefabContents 는 원점이 아닌 프리뷰 씬에 올릴 수 있다.</summary>
    static Bounds ToLocal(Transform root, Bounds world)
    {
        Vector3 c = world.center, e = world.extents;
        var result = new Bounds(root.InverseTransformPoint(c), Vector3.zero);
        for (int i = 0; i < 8; i++)
            result.Encapsulate(root.InverseTransformPoint(c + new Vector3(
                (i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z)));
        return result;
    }
}
