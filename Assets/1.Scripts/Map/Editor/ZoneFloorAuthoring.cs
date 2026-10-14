using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 존 바닥 Ground 콜라이더 저작 도구 (할 일 32 세팅 — Docs/tech/wall-concept.md).
//
// 존 프리팹은 아트 조각의 콜라이더를 전부 뺐다(바닥 포함). 충돌은 방 단위 단순 박스 — 이 메뉴가 존마다 지상층 바닥
// 박스 'Colliders/Floor_00'(ZoneHierarchyAuthoring 표준 자리, Ground 태그·레이어, 비트리거 BoxCollider 하나, 렌더러 없음)을 만든다.
// 다시 돌리면 같은 결과(멱등) — 이미 맞으면 저장하지 않는다. bossroom 은 BossRoomAuthoring 이 관리하므로 제외.
//  · Colliders/Floor_* 가 2개 이상인 존 = 사용자가 수동 분할 → 건드리지 않고 '수동 세팅됨 — 건너뜀'.
//  · 옛 판의 루트 직속 'ZoneFloor' 가 남아 있으면 Colliders/Floor_00 으로 옮기고 이름만 바꾼다(제자리 갱신).
//
//  · 바닥 렌더러 = 루트 아래 경로(자기 포함)에 'floor' 가 들어간 렌더러 중 자기 이름에 'wall' 이 없는 것
//    (바닥 PF 아래 묻힌 벽 조각 제외). 그중 납작한 것(두께 ≤ 1m)만 높이 판정에 쓴다.
//  · floorTop = 납작한 바닥 렌더러 윗면을 5cm 안쪽끼리 묶은 높이 무리 중 복도 높이(루트 y 0 ±0.3m)에 걸친 무리의
//    XZ 면적 최빈 윗면(1cm). 면적 최대 무리가 아니다 — Zone_typeQuest01 은 꺼진 아래층(−2.94)이 더 넓지만 문은 0 에 붙는다.
//    개수가 아니라 면적으로 센다 — 트렌치·몰딩 조각 수십 개가 큰 바닥 판 몇 장을 이기지 않게.
//  · 지상층 = 윗면이 floorTop ±0.3m 인 납작한 바닥 렌더러(몰딩·컨베이어 프레임 같은 얕은 턱 포함). 박스 XZ = 그 합집합 bounds.
//
// 🔴 박스는 언제나 직사각형 1개다. 다음은 멈추지 않고 '수동 확인 필요' 로 보고만 한다:
//    박스 안에 지상층 바닥이 없는 칸(ㄱ자·구멍 — 다른 높이 바닥이 덮고 있으면 그 높이도 적는다) ·
//    높이가 다른 바닥(단상·계단·꺼진 바닥 — floorTop 과 0.3m 넘게 다른 바닥 렌더러, XZ 1m² 이상) ·
//    박스와 겹치는 낙하/구멍 의심 오브젝트(이름에 abyss·droppod·hole·pit·water·platform).
public static class ZoneFloorAuthoring
{
    const string ZonesFolder = "Assets/2.Prefabs/Environment/Layouts/Zones";
    const string BossRoomPath = ZonesFolder + "/bossroom.prefab";
    const string CollidersName = "Colliders";       // ZoneHierarchyAuthoring 표준 그룹
    const string EnvironmentName = "Environment";
    const string FloorPrefix = "Floor_";
    const string FloorName = FloorPrefix + "00";
    const string LegacyFloorName = "ZoneFloor";     // 이전 판이 루트 직속에 만들던 이름
    const string Tag = "[ZoneFloor]";

    const float Thickness = 1f;             // 박스 두께 — 윗면 = floorTop
    const float FlatMaxThickness = 1f;      // 이보다 두꺼운 렌더러는 높이 판정에서 뺀다(계단·블록 단상·크레인)
    const float ClusterGap = 0.05f;         // 윗면 높이 무리 묶음 간격
    const float GroundTolerance = 0.3f;     // floorTop 에서 이 안이면 지상층
    const float MinOtherArea = 1f;          // 높이 다른 바닥 보고 최소 XZ 면적(m²)
    const float Cell = 0.5f;                // 빈 칸 검사 격자
    const float Epsilon = 0.001f;

    static readonly string[] HazardKeys = { "abyss", "droppod", "hole", "pit", "water", "platform" };

    struct FloorPiece
    {
        public Renderer Renderer;
        public Bounds Bounds;   // 루트 로컬
        public float Top;       // 1cm 반올림
        public float Area;      // XZ 면적
        public bool Flat;
    }

    [MenuItem("Tools/Map/Authoring/Floors/존 바닥 생성 (멱등)")]
    public static void Generate()
    {
        int groundLayer = LayerMask.NameToLayer(GameLayers.Ground);
        if (groundLayer < 0)
        {
            Debug.LogError($"{Tag} '{GameLayers.Ground}' 레이어가 없다.");
            return;
        }

        var report = new StringBuilder();
        report.AppendLine($"{Tag} 존 바닥 생성 — 두께 {Thickness}m · 태그/레이어 {GameLayers.Ground} · 이름 {CollidersName}/{FloorName}");
        report.AppendLine("| 존 | 결과 | floorTop | 박스 XZ (루트 로컬) | 박스 크기 | 수동 확인 |");
        var manual = new List<string>();

        foreach (string path in AssetDatabase.FindAssets("t:Prefab", new[] { ZonesFolder })
                     .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p))
        {
            if (path == BossRoomPath) continue;
            report.AppendLine(ProcessZone(path, groundLayer, manual));
        }

        if (manual.Count > 0)
        {
            report.AppendLine("수동 확인 필요:");
            foreach (string m in manual) report.AppendLine("  · " + m);
        }
        Debug.Log(report.ToString());
    }

    static string ProcessZone(string path, int groundLayer, List<string> manual)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(path);
        string row = null;
        EditPrefab(path, root =>
        {
            List<Transform> floors = FloorColliders(root.transform);
            if (floors.Count > 1)
            {
                row = $"| {name} | 수동 세팅됨 — 건너뜀({CollidersName}/{FloorPrefix}* {floors.Count}개) | - | - | - | - |";
                return false;
            }

            List<FloorPiece> pieces = CollectFloorPieces(root);
            List<FloorPiece> flat = pieces.Where(p => p.Flat).ToList();
            if (flat.Count == 0)
            {
                row = $"| {name} | 건너뜀: 납작한 바닥 렌더러 없음 | - | - | - | - |";
                return false;
            }

            float floorTop = FindFloorTop(flat, out string fallback);
            List<FloorPiece> ground = flat.Where(p => Mathf.Abs(p.Top - floorTop) <= GroundTolerance).ToList();
            Bounds xz = ground[0].Bounds;
            foreach (FloorPiece p in ground) xz.Encapsulate(p.Bounds);
            xz.SetMinMax(Snap(xz.min), Snap(xz.max));

            Vector3 center = Snap(new Vector3(xz.center.x, floorTop - Thickness * 0.5f, xz.center.z));
            Vector3 size = Snap(new Vector3(xz.size.x, Thickness, xz.size.z));

            List<string> reasons = Inspect(root, pieces, ground, xz, floorTop);
            if (fallback != null) reasons.Insert(0, fallback);
            bool changed = Apply(root, center, size, groundLayer, out string note);

            string box = $"x {xz.min.x:0.##}~{xz.max.x:0.##} z {xz.min.z:0.##}~{xz.max.z:0.##}";
            string dim = $"{size.x:0.##} × {size.y:0.##} × {size.z:0.##}";
            string flag = reasons.Count > 0 ? "⚠ " + reasons.Count + "건" : "-";
            row = $"| {name} | {(changed ? "갱신" : "변경 없음")}{note} | {floorTop:0.##} | {box} | {dim} | {flag} |";
            foreach (string r in reasons) manual.Add($"{name}: {r}");
            return changed;
        }, out string editError);
        return editError != null ? $"| {name} | 건너뜀: {editError} | - | - | - | - |" : row;
    }

    // ─── 실측 ──────────────────────────────────────────────────────────────

    static List<FloorPiece> CollectFloorPieces(GameObject root)
    {
        var list = new List<FloorPiece>();
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (r.name.ToLowerInvariant().Contains("wall") || !PathHasFloor(r.transform, root.transform)) continue;
            Bounds b = ToLocal(root.transform, r.bounds);
            list.Add(new FloorPiece
            {
                Renderer = r,
                Bounds = b,
                Top = Snap(b.max.y),
                Area = b.size.x * b.size.z,
                Flat = b.size.y <= FlatMaxThickness,
            });
        }
        return list;
    }

    static bool PathHasFloor(Transform t, Transform root)
    {
        for (; t != null && t != root; t = t.parent)
            if (t.name.ToLowerInvariant().Contains("floor")) return true;
        return false;
    }

    /// <summary>
    /// 지상층 = 복도가 붙는 높이. StageTutorial 실측: 존은 y 0 에 놓이고 복도 바닥 윗면도 0 이다 → 루트 y 0 ±0.3m 에 걸친 무리.
    /// Zone_typeQuest01 처럼 꺼진 아래층(−2.94) 면적이 더 넓어도 문 높이를 고른다. 그런 무리가 없을 때만 면적 최대 무리.
    /// 무리 안에서는 면적이 가장 큰 1cm 높이(동점이면 낮은 쪽).
    /// </summary>
    static float FindFloorTop(List<FloorPiece> flat, out string fallback)
    {
        List<List<FloorPiece>> clusters = Cluster(flat);
        List<FloorPiece> best = clusters.Where(c => c.Any(p => Mathf.Abs(p.Top) <= GroundTolerance))
            .OrderByDescending(c => c.Sum(p => p.Area)).FirstOrDefault();
        fallback = null;
        IEnumerable<FloorPiece> candidates;
        if (best != null)
            candidates = best.Where(p => Mathf.Abs(p.Top) <= GroundTolerance);
        else
        {
            candidates = clusters.OrderByDescending(c => c.Sum(p => p.Area)).First();
            fallback = "루트 y 0(복도 높이) 근처 바닥 없음 — 면적 최대 높이를 지상층으로 씀";
        }
        return candidates.GroupBy(p => p.Top).OrderByDescending(g => g.Sum(p => p.Area)).ThenBy(g => g.Key).First().Key;
    }

    /// <summary>윗면을 <see cref="ClusterGap"/> 안쪽끼리 묶은 높이 무리(낮은 순).</summary>
    static List<List<FloorPiece>> Cluster(IEnumerable<FloorPiece> pieces)
    {
        var clusters = new List<List<FloorPiece>>();
        foreach (FloorPiece p in pieces.OrderBy(p => p.Top))
        {
            if (clusters.Count > 0 && p.Top - clusters[clusters.Count - 1].Last().Top <= ClusterGap + Epsilon)
                clusters[clusters.Count - 1].Add(p);
            else
                clusters.Add(new List<FloorPiece> { p });
        }
        return clusters;
    }

    /// <summary>박스 하나로 덮지 못하는 곳 — 수동 확인 사유 목록.</summary>
    static List<string> Inspect(GameObject root, List<FloorPiece> pieces, List<FloorPiece> ground, Bounds xz, float floorTop)
    {
        var reasons = new List<string>();

        // 1) 박스 안 지상층 바닥이 없는 칸(격자 중심 검사). 다른 높이 바닥이 덮으면 그 높이로, 아무것도 없으면 빈 곳으로.
        List<FloorPiece> others = pieces.Where(p => Mathf.Abs(p.Top - floorTop) > GroundTolerance).ToList();
        var holes = new Dictionary<string, (Bounds b, int n)>();
        for (float x = xz.min.x + Cell * 0.5f; x < xz.max.x; x += Cell)
        for (float z = xz.min.z + Cell * 0.5f; z < xz.max.z; z += Cell)
        {
            if (ground.Any(p => Inside(p.Bounds, x, z))) continue;
            List<float> tops = others.Where(p => Inside(p.Bounds, x, z)).Select(p => p.Top).Distinct().OrderBy(t => t).ToList();
            string key = tops.Count == 0 ? "지상층 바닥 없음(빈 곳 — ㄱ자/구멍)" : "지상층 바닥 없음, 다른 높이 바닥 " + string.Join("/", tops.Select(t => t.ToString("0.##")));
            var cellBounds = new Bounds(new Vector3(x, floorTop, z), new Vector3(Cell, 0f, Cell));
            holes[key] = holes.TryGetValue(key, out var h) ? (Grow(h.b, cellBounds), h.n + 1) : (cellBounds, 1);
        }
        foreach (var h in holes)
            reasons.Add($"{h.Key} {h.Value.n * Cell * Cell:0.#}m² — 범위 {Range(h.Value.b)}");

        // 2) 높이가 다른 바닥(단상·계단·꺼진 바닥) — 윗면 높이 무리별.
        foreach (List<FloorPiece> g in Cluster(others.Where(p => p.Area >= MinOtherArea)))
        {
            Bounds b = g[0].Bounds;
            foreach (FloorPiece p in g) b.Encapsulate(p.Bounds);
            float lo = g.First().Top, hi = g.Last().Top;
            string top = Mathf.Approximately(lo, hi) ? $"{lo:0.##}" : $"{lo:0.##}~{hi:0.##}";
            string names = string.Join(",", g.Select(p => p.Renderer.name).Distinct().Take(3));
            reasons.Add($"높이가 다른 바닥 윗면 {top}({(lo > floorTop ? "지상층 위" : "지상층 아래")}) {g.Count}개 — 범위 {Range(b)} [{names}]");
        }

        // 3) 박스가 덮는 낙하/구멍 의심 오브젝트.
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            string lower = t.name.ToLowerInvariant();
            if (t == root.transform || !HazardKeys.Any(k => lower.Contains(k))) continue;
            if (t.parent != null && HazardKeys.Any(k => t.parent.name.ToLowerInvariant().Contains(k))) continue;   // 무리 꼭대기만
            Renderer[] rs = t.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) continue;
            Bounds b = ToLocal(root.transform, rs[0].bounds);
            foreach (Renderer r in rs) b.Encapsulate(ToLocal(root.transform, r.bounds));
            bool overlaps = b.max.x > xz.min.x && b.min.x < xz.max.x && b.max.z > xz.min.z && b.min.z < xz.max.z;
            if (overlaps)
                reasons.Add($"박스가 덮는 낙하/구멍 의심 '{t.name}' y {b.min.y:0.##}~{b.max.y:0.##} — 범위 {Range(b)}");
        }
        return reasons;
    }

    static bool Inside(Bounds b, float x, float z) =>
        x > b.min.x + Epsilon && x < b.max.x - Epsilon && z > b.min.z + Epsilon && z < b.max.z - Epsilon;

    static Bounds Grow(Bounds a, Bounds b) { a.Encapsulate(b); return a; }

    static string Range(Bounds b) => $"x {b.min.x:0.#}~{b.max.x:0.#} z {b.min.z:0.#}~{b.max.z:0.#}";

    // ─── 적용(멱등) ────────────────────────────────────────────────────────

    /// <summary>
    /// 존 루트 직속 Colliders 아래 Floor_* 목록. 2개 이상이면 사용자가 수동 분할한 존 — 건드리지 않는다.
    /// </summary>
    static List<Transform> FloorColliders(Transform root)
    {
        Transform colliders = root.Find(CollidersName);
        return colliders == null ? new List<Transform>()
            : colliders.Cast<Transform>().Where(c => c.name.StartsWith(FloorPrefix, System.StringComparison.Ordinal)).ToList();
    }

    /// <summary>
    /// Colliders/Floor_00 을 사양과 같게 맞춘다. 이미 같으면 false(저장 안 함).
    /// Floor_* 가 하나면 그것을 제자리 갱신, 없으면 옛 이름 ZoneFloor(어디 있든)를 Colliders/Floor_00 으로 옮기고, 그것도 없으면 만든다.
    /// 남는 컴포넌트는 Transform + BoxCollider 하나(렌더러·필터 등 제거). 호출 전에 Floor_* 가 2개 이상인 존은 걸러낸다.
    /// </summary>
    static bool Apply(GameObject root, Vector3 center, Vector3 size, int groundLayer, out string note)
    {
        note = "";
        List<Transform> floors = FloorColliders(root.transform);
        Transform t = floors.Count == 1 ? floors[0] : null;
        Transform legacy = FindDeep(root.transform, LegacyFloorName);
        if (t != null && legacy != null)
            note = $"(옛 {LegacyFloorName} 남음 — {legacy.parent.name} 아래, 수동 확인)";
        if (t != null && Matches(t, center, size, groundLayer)) return false;

        Transform colliders = root.transform.Find(CollidersName);
        if (colliders == null)
        {
            colliders = new GameObject(CollidersName).transform;
            colliders.SetParent(root.transform, false);
            Transform env = root.transform.Find(EnvironmentName);
            if (env != null) colliders.SetSiblingIndex(env.GetSiblingIndex() + 1);
            note += $"({CollidersName} 그룹 생성)";
        }

        if (t == null && legacy != null)
        {
            note += $"({LegacyFloorName} → {CollidersName}/{FloorName} — 이전 부모 {legacy.parent.name})";
            t = legacy;
            t.SetParent(colliders, false);
            t.name = FloorName;
        }
        else if (t == null)
        {
            t = new GameObject(FloorName).transform;
            t.SetParent(colliders, false);
            note += "(생성)";
        }
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;
        GameObject go = t.gameObject;
        go.SetActive(true);
        go.layer = groundLayer;
        go.tag = GameLayers.Ground;
        foreach (Component c in go.GetComponents<Component>())
            if (!(c is Transform) && !(c is BoxCollider)) Object.DestroyImmediate(c);
        BoxCollider[] boxes = go.GetComponents<BoxCollider>();
        for (int i = 1; i < boxes.Length; i++) Object.DestroyImmediate(boxes[i]);
        BoxCollider box = boxes.Length > 0 ? boxes[0] : go.AddComponent<BoxCollider>();
        box.isTrigger = false;
        box.enabled = true;
        box.center = center;
        box.size = size;
        return true;
    }

    static bool Matches(Transform t, Vector3 center, Vector3 size, int groundLayer)
    {
        GameObject go = t.gameObject;
        if (!go.activeSelf || go.layer != groundLayer || !go.CompareTag(GameLayers.Ground)) return false;
        if (t.localPosition != Vector3.zero || t.localRotation != Quaternion.identity || t.localScale != Vector3.one) return false;
        Component[] comps = go.GetComponents<Component>();
        if (comps.Length != 2 || !(comps[1] is BoxCollider box)) return false;
        if (box.isTrigger || !box.enabled) return false;
        return (box.center - center).sqrMagnitude <= Epsilon * Epsilon && (box.size - size).sqrMagnitude <= Epsilon * Epsilon;
    }

    // ─── 공용 ──────────────────────────────────────────────────────────────

    static Transform FindDeep(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t != root && t.name == name) return t;
        return null;
    }

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

    /// <summary>월드 bounds → 루트 로컬 bounds(8 꼭짓점 변환 후 재포장). LoadPrefabContents 는 원점이 아닌 프리뷰 씬에 올릴 수 있다.</summary>
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
