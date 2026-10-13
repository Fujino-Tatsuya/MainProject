using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 존·복도 경계 벽 저작 도구 (할 일 32 세팅 — Docs/tech/wall-concept.md).
//
// 벽 아트의 콜라이더를 지운 뒤로 이동을 막는 것은 InvisibleBoundaries/Boundary_* 박스뿐이다.
// 이 메뉴는 그 박스를 존 프리팹과 복도 바닥 프리팹에 만든다. 다시 돌리면 같은 결과(멱등) — 이미 맞으면 저장하지 않는다.
//
//  · 존 = Zones/*.prefab(bossroom 제외 — BossRoomAuthoring 이 관리) 중 **StageTutorial.prefab 의 ZoneSlot 에 놓이는 존**.
//    바닥 = 주 바닥(PF_Prop_Zone_*_Floor*) 콜라이더 bounds. 주 바닥이 없거나 주 바닥 밖으로 바닥이 이어진 존(L_typeA/B·S 존)은
//    존 안 Ground 레이어 콜라이더 전체 bounds 를 바닥으로 쓴다.
//    문 자리 = StageTutorial 배치(슬롯의 FixedPrefab/Rotations = 위치·YawSteps)에서 같은 StageTutorial 안 복도 바닥
//    (floor_hallway 1 인스턴스)이 존 바닥 변에 닿는 구간(폭 = 복도 바닥 폭). 다른 스테이지는 보지 않는다.
//    존 프리팹 안에는 문 표식·둘레 벽이 없고 문은 배치마다 다르다 — 존 단독으로는 판정할 수 없다.
//    슬롯이 FixedPrefab 이면 그 프리팹만, 아니면 Rotations(Save Placements 저작) 의 프리팹이 그 슬롯에 올 수 있다.
//    FixedPrefab 으로 고정된 프리팹은 셔플 풀에서 빠진다(LayoutPlacer) — 다른 슬롯 배치는 세지 않는다.
//  · 복도 = floor_hallway 1.prefab 자체의 긴 두 변. 튜토리얼·절차 맵 섹션(Level_wall_hallway*)이 이 프리팹을 중첩해 쓰므로
//    한 번 넣으면 모든 복도 인스턴스에 들어간다(섹션·스테이지 프리팹은 바뀌지 않는다).
//
// 건너뛰는 존(로그로 보고): StageTutorial 에 없음(열지 않는다) · 바닥 없음(주 바닥도 Ground 레이어 콜라이더도 없음) ·
// 주 바닥 밖으로 이어진 바닥이 있는데 Ground 없음 · 복도가 닿지 않음 · InvisibleBoundaries 가 회전/스케일됨 ·
// 프리팹 모드에서 저장 안 된 수정이 있음 · 호출자가 넘긴 제외 목록.
public static class BoundaryWallAuthoring
{
    const string ZonesFolder = "Assets/2.Prefabs/Environment/Layouts/Zones";
    const string StagePath = "Assets/2.Prefabs/Environment/Layouts/Stages/StageTutorial.prefab";
    const string BossRoomPath = ZonesFolder + "/bossroom.prefab";
    const string HallwayFloorPath = "Assets/2.Prefabs/Environment/Architecture/Floors/floor_hallway 1.prefab";

    const string BoundariesName = "InvisibleBoundaries";
    const string BoundaryPrefix = "Boundary_";
    const string Tag = "[BoundaryWall]";

    // 두께 0.5m · 높이 8m(보스방 경계 높이 — 대시·넉백으로 넘지 못하게). 벽 안쪽 면 = 바닥 변.
    const float Thickness = 0.5f;
    const float Height = 8f;

    // 복도 끝(문턱)이 존 바닥 변에서 이 거리 안이면 그 변에 닿은 것으로 본다(배치 오차 0.2~0.4m).
    const float DoorTouchTolerance = 0.6f;
    // 주 바닥 밖으로 이만큼 넘게 이어진 바닥이 있으면 주 바닥 bounds 가 존 바닥이 아니다 → 건너뛴다.
    const float ExtraFloorTolerance = 1f;
    // 바닥 판정 — 이름에 floor, 납작하고(두께 ≤ 1m) 윗면이 주 바닥 윗면 범위(최저~최고) ±0.6m 안.
    const float FloorMaxThickness = 1f;
    const float FloorTopTolerance = 0.6f;
    // 이보다 좁은 벽 조각·문 구간은 만들지 않는다.
    const float MinSegment = 0.05f;
    const float Epsilon = 0.001f;

    enum Side { XMin, XMax, ZMin, ZMax }

    struct WallSpec
    {
        public string Name;
        public Vector3 Center;   // 프리팹 루트 로컬
        public Vector3 Size;
    }

    [MenuItem("Tools/Map/Authoring/Walls/경계 벽 생성 (존·복도)")]
    public static void Generate() => Generate(null);

    /// <param name="skipZones">이번 실행에서 열지 않을 존 프리팹 이름(사용자가 편집 중인 존 등). 표에 '제외'로 남는다.</param>
    public static void Generate(ICollection<string> skipZones)
    {
        int wallLayer = LayerMask.NameToLayer(GameLayers.Wall);
        int groundLayer = LayerMask.NameToLayer(GameLayers.Ground);
        if (wallLayer < 0 || groundLayer < 0)
        {
            Debug.LogError($"{Tag} '{GameLayers.Wall}'·'{GameLayers.Ground}' 레이어가 없다.");
            return;
        }

        var report = new StringBuilder();
        report.AppendLine($"{Tag} 경계 벽 생성 (StageTutorial 기준) — 두께 {Thickness}m · 높이 {Height}m · 태그/레이어 {GameLayers.Wall}");
        report.AppendLine("| 대상 | 슬롯 | 결과 | 바닥 bounds (루트 로컬) | 문 자리 | 박스 |");

        StageLayout layout = LoadStageLayout(StagePath);
        var unused = new List<string>();

        foreach (string path in AssetDatabase.FindAssets("t:Prefab", new[] { ZonesFolder })
                     .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p))
        {
            if (path == BossRoomPath) continue;
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            var placements = layout.Placements.Where(p => AssetDatabase.GetAssetPath(p.prefab) == path).ToList();
            if (placements.Count == 0) { unused.Add(name); continue; }
            string slots = string.Join(",", placements.Select(p => $"{p.slot}(yaw {p.yaw})"));
            if (skipZones != null && skipZones.Contains(name))
            {
                report.AppendLine($"| {name} | {slots} | 제외(호출자 지정 — 열지 않음) | - | - | - |");
                continue;
            }
            report.AppendLine(ProcessZone(path, slots, placements, layout.Hallways, wallLayer, groundLayer));
        }

        report.AppendLine(ProcessHallwayFloor(HallwayFloorPath, wallLayer));
        if (unused.Count > 0)
            report.AppendLine($"StageTutorial 에 없는 존(열지 않음 — 이전에 넣은 박스는 그대로): {string.Join(", ", unused)}");
        Debug.Log(report.ToString());
    }

    // ─── 존 ────────────────────────────────────────────────────────────────

    static string ProcessZone(string path, string slots, List<Placement> placements, List<HallwayRect> hallways,
                              int wallLayer, int groundLayer)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(path);
        string row = null;
        EditPrefab(path, root =>
        {
            string source = "주 바닥";
            string fallbackReason = null;
            if (!TryGetMainFloor(root, out Bounds floor, out float floorBottom, out float floorTop, out List<Transform> floorRoots))
                fallbackReason = "주 바닥(PF_Prop_Zone_*_Floor*) 콜라이더 없음";
            else
            {
                string extra = FindExtraFloor(root, floor, floorBottom, floorTop, floorRoots);
                if (extra != null) fallbackReason = $"주 바닥 밖으로 이어진 바닥 {extra}";
            }
            if (fallbackReason != null)
            {
                if (!TryGetGroundFloor(root, groundLayer, out floor, out floorBottom, out floorTop))
                {
                    row = $"| {name} | {slots} | 건너뜀: {fallbackReason} · Ground 레이어 콜라이더 없음 | - | - | - |";
                    return false;
                }
                source = "Ground";
            }
            string top = Mathf.Approximately(floorBottom, floorTop) ? $"{floorTop:0.##}" : $"{floorBottom:0.##}~{floorTop:0.##}";
            string fb = $"{source}: x {floor.min.x:0.##}~{floor.max.x:0.##} z {floor.min.z:0.##}~{floor.max.z:0.##} 윗면 {top}";

            var doors = new Dictionary<Side, List<Vector2>>();
            foreach (Side s in System.Enum.GetValues(typeof(Side))) doors[s] = new List<Vector2>();
            var misaligned = new List<string>();
            CollectDoors(floor, placements, hallways, doors, misaligned);
            string warn = misaligned.Count > 0 ? " ⚠ 어긋난 배치(저작 확인 필요): " + string.Join(", ", misaligned) : "";
            if (doors.Values.All(d => d.Count == 0))
            {
                row = $"| {name} | {slots} | 건너뜀: 복도가 바닥 변에 닿지 않음{warn} | {fb} | - | - |";
                return false;
            }

            List<WallSpec> specs = BuildZoneSpecs(floor, floorBottom, floorTop, doors);
            string doorText = string.Join(" / ", doors.Where(d => d.Value.Count > 0)
                .Select(d => $"{d.Key} " + string.Join(",", Merge(d.Value).Select(v => $"{v.x:0.##}~{v.y:0.##}"))));

            bool changed = Apply(root, specs, wallLayer, out string err);
            if (err != null)
            {
                row = $"| {name} | {slots} | 건너뜀: {err} | {fb} | {doorText} | - |";
                return false;
            }
            row = $"| {name} | {slots} | {(changed ? "갱신" : "변경 없음")}{warn} | {fb} | {doorText} | {specs.Count} |";
            return changed;
        }, out string editError);
        return editError != null ? $"| {name} | {slots} | 건너뜀: {editError} | - | - | - |" : row;
    }

    /// <summary>
    /// 주 바닥 = 이름이 PF_Prop_Zone_*_Floor* 인 오브젝트들 아래 콜라이더의 합친 bounds(루트 로컬, XZ 둘레에 쓴다).
    /// 높이 = 그 아래 납작한 '바닥' 조각(이름에 floor, 두께 ≤ 1m) 윗면의 최저~최고. 2단 바닥(Zone_typeQuest01 = 0 과 −2.94)도
    /// 두 높이를 다 막도록 벽은 최저 윗면부터 최고 윗면 + 높이까지 세운다. 바닥 PF 아래 묻힌 벽 조각(지하 −8.8 등)은 이름으로 거른다.
    /// </summary>
    static bool TryGetMainFloor(GameObject root, out Bounds floor, out float floorBottom, out float floorTop, out List<Transform> floorRoots)
    {
        floor = default;
        floorBottom = floorTop = 0f;
        floorRoots = root.GetComponentsInChildren<Transform>(true)
            .Where(t => t.name.StartsWith("PF_Prop_Zone_") && t.name.Contains("_Floor")).ToList();
        var cols = floorRoots.SelectMany(f => f.GetComponentsInChildren<Collider>(true)).Where(c => !c.isTrigger).Distinct().ToList();
        if (cols.Count == 0) return false;

        floorBottom = float.MaxValue;
        floorTop = float.MinValue;
        bool any = false;
        foreach (Collider c in cols)
        {
            Bounds b = ToLocal(root.transform, c.bounds);
            if (!any) { floor = b; any = true; } else floor.Encapsulate(b);
            if (b.size.y > FloorMaxThickness || !c.name.ToLowerInvariant().Contains("floor")) continue;
            floorBottom = Mathf.Min(floorBottom, b.max.y);
            floorTop = Mathf.Max(floorTop, b.max.y);
        }
        if (floorTop < floorBottom) floorBottom = floorTop = floor.max.y;   // 납작한 바닥 조각이 없으면 bounds 윗면
        floor.SetMinMax(Snap(floor.min), Snap(floor.max));
        floorBottom = Snap(floorBottom);
        floorTop = Snap(floorTop);
        return true;
    }

    /// <summary>주 바닥 밖으로 1m 넘게 이어진 바닥 콜라이더가 있으면 그 이름. 경계를 주 바닥에 두면 그 바닥을 자르게 된다.</summary>
    static string FindExtraFloor(GameObject root, Bounds floor, float floorBottom, float floorTop, List<Transform> floorRoots)
    {
        foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
        {
            if (c.isTrigger || !c.name.ToLowerInvariant().Contains("floor")
                || floorRoots.Any(f => c.transform.IsChildOf(f)) || IsUnderBoundaries(c.transform, root.transform)) continue;
            Bounds b = ToLocal(root.transform, c.bounds);
            if (b.size.y > FloorMaxThickness || b.max.y < floorBottom - FloorTopTolerance || b.max.y > floorTop + FloorTopTolerance) continue;
            float beyond = Mathf.Max(floor.min.x - b.min.x, b.max.x - floor.max.x, floor.min.z - b.min.z, b.max.z - floor.max.z);
            if (beyond > ExtraFloorTolerance)
                return $"'{c.name}'(x {b.min.x:0.#}~{b.max.x:0.#} z {b.min.z:0.#}~{b.max.z:0.#}, {beyond:0.#}m)";
        }
        return null;
    }

    /// <summary>
    /// 주 바닥을 못 쓰는 존의 바닥 = 존 안 Ground 레이어 콜라이더(경계 박스 제외) 전체의 합친 bounds(루트 로컬).
    /// 높이 = 그 콜라이더 윗면의 최저~최고(주 바닥과 같이 두 높이를 다 막는다).
    /// </summary>
    static bool TryGetGroundFloor(GameObject root, int groundLayer, out Bounds floor, out float floorBottom, out float floorTop)
    {
        floor = default;
        floorBottom = float.MaxValue;
        floorTop = float.MinValue;
        var cols = root.GetComponentsInChildren<Collider>(true)
            .Where(c => !c.isTrigger && c.gameObject.layer == groundLayer && !IsUnderBoundaries(c.transform, root.transform)).ToList();
        if (cols.Count == 0) return false;

        for (int i = 0; i < cols.Count; i++)
        {
            Bounds b = ToLocal(root.transform, cols[i].bounds);
            if (i == 0) floor = b; else floor.Encapsulate(b);
            floorBottom = Mathf.Min(floorBottom, b.max.y);
            floorTop = Mathf.Max(floorTop, b.max.y);
        }
        floor.SetMinMax(Snap(floor.min), Snap(floor.max));
        floorBottom = Snap(floorBottom);
        floorTop = Snap(floorTop);
        return true;
    }

    static bool IsUnderBoundaries(Transform t, Transform root)
    {
        for (; t != null && t != root; t = t.parent)
            if (t.name == BoundariesName) return true;
        return false;
    }

    // ─── 스테이지 배치 → 문 자리 ───────────────────────────────────────────

    /// <summary>슬롯 하나에 존이 놓이는 배치(스테이지 루트 로컬). 스테이지를 내린 뒤에도 쓰므로 슬롯은 이름만.</summary>
    struct Placement
    {
        public string slot;
        public GameObject prefab;
        public Vector3 pos;
        public int yaw;
    }

    /// <summary>복도 바닥(floor_hallway 1 인스턴스) — 전체 bounds 와 돌 바닥(문턱 제외) bounds 의 스테이지 공간 XZ 꼭짓점 4개.</summary>
    struct HallwayRect
    {
        public Vector3[] AllCorners;
        public Vector3[] StoneCorners;
    }

    class StageLayout
    {
        public readonly List<Placement> Placements = new List<Placement>();
        public readonly List<HallwayRect> Hallways = new List<HallwayRect>();
    }

    static StageLayout LoadStageLayout(string stagePath)
    {
        var layout = new StageLayout();
        GameObject stage = PrefabUtility.LoadPrefabContents(stagePath);
        try
        {
            Transform sr = stage.transform;
            ZoneSlot[] slots = stage.GetComponentsInChildren<ZoneSlot>(true);
            var pinned = new HashSet<GameObject>(slots.Where(s => s.FixedPrefab != null).Select(s => s.FixedPrefab));
            foreach (ZoneSlot slot in slots)
            {
                foreach (ZoneSlot.RotationEntry r in slot.Rotations)
                {
                    if (r.Prefab == null) continue;
                    bool allowed = slot.FixedPrefab != null ? r.Prefab == slot.FixedPrefab : !pinned.Contains(r.Prefab);
                    if (!allowed) continue;
                    // 저장 위치(Save Placements)는 스테이지 루트가 원점일 때의 월드 값 = 루트 로컬. 슬롯 baseline 은 루트 로컬로 옮긴다.
                    Vector3 pos = r.HasPosition ? r.Position : sr.InverseTransformPoint(slot.transform.position);
                    layout.Placements.Add(new Placement { slot = slot.name, prefab = r.Prefab, pos = pos, yaw = r.YawSteps });
                }
            }

            foreach (Transform t in stage.GetComponentsInChildren<Transform>(true))
            {
                if (!PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)) continue;
                GameObject src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(t.gameObject);
                if (src == null || AssetDatabase.GetAssetPath(src) != HallwayFloorPath) continue;
                Collider[] cols = t.GetComponentsInChildren<Collider>(true)
                    .Where(c => !c.isTrigger && !IsUnderBoundaries(c.transform, t)).ToArray();
                Collider[] stones = cols.Where(c => !c.name.ToLowerInvariant().Contains("threshold")).ToArray();
                if (cols.Length == 0 || stones.Length == 0) continue;
                layout.Hallways.Add(new HallwayRect { AllCorners = Corners(sr, cols), StoneCorners = Corners(sr, stones) });
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(stage);
        }
        return layout;
    }

    /// <summary>콜라이더들의 합친 bounds 를 스테이지 루트 로컬 XZ 사각형 꼭짓점으로.</summary>
    static Vector3[] Corners(Transform stageRoot, Collider[] cols)
    {
        Bounds b = ToLocal(stageRoot, cols[0].bounds);
        foreach (Collider c in cols) b.Encapsulate(ToLocal(stageRoot, c.bounds));
        return new[]
        {
            new Vector3(b.min.x, 0f, b.min.z), new Vector3(b.max.x, 0f, b.min.z),
            new Vector3(b.min.x, 0f, b.max.z), new Vector3(b.max.x, 0f, b.max.z),
        };
    }

    /// <summary>
    /// 이 존의 배치마다 복도가 닿는 변·구간(존 루트 로컬)을 모은다.
    /// 복도가 하나도 닿지 않거나 복도가 바닥 안쪽으로 파고드는 배치는 <paramref name="misaligned"/> 에 적는다(Save Placements 저작 어긋남 의심).
    /// </summary>
    static void CollectDoors(Bounds floor, List<Placement> placements, List<HallwayRect> hallways, Dictionary<Side, List<Vector2>> doors,
                             List<string> misaligned)
    {
        foreach (Placement p in placements)
        {
            int touches = 0;
            bool intrudes = false;
            Matrix4x4 toZone = Matrix4x4.TRS(p.pos, Quaternion.Euler(0f, 90f * p.yaw, 0f), Vector3.one).inverse;
            foreach (HallwayRect h in hallways)
            {
                Rect all = ToRect(toZone, h.AllCorners), stone = ToRect(toZone, h.StoneCorners);
                bool overlapZ = all.yMax > floor.min.z + MinSegment && all.yMin < floor.max.z - MinSegment;
                bool overlapX = all.xMax > floor.min.x + MinSegment && all.xMin < floor.max.x - MinSegment;
                int before = doors.Values.Sum(d => d.Count);
                if (overlapZ && Mathf.Abs(all.xMax - floor.min.x) <= DoorTouchTolerance) doors[Side.XMin].Add(new Vector2(stone.yMin, stone.yMax));
                else if (overlapZ && Mathf.Abs(all.xMin - floor.max.x) <= DoorTouchTolerance) doors[Side.XMax].Add(new Vector2(stone.yMin, stone.yMax));
                else if (overlapX && Mathf.Abs(all.yMax - floor.min.z) <= DoorTouchTolerance) doors[Side.ZMin].Add(new Vector2(stone.xMin, stone.xMax));
                else if (overlapX && Mathf.Abs(all.yMin - floor.max.z) <= DoorTouchTolerance) doors[Side.ZMax].Add(new Vector2(stone.xMin, stone.xMax));
                touches += doors.Values.Sum(d => d.Count) - before;

                float inX = Mathf.Min(all.xMax, floor.max.x) - Mathf.Max(all.xMin, floor.min.x);
                float inZ = Mathf.Min(all.yMax, floor.max.z) - Mathf.Max(all.yMin, floor.min.z);
                if (inX > DoorTouchTolerance && inZ > DoorTouchTolerance) intrudes = true;
            }
            if (touches == 0 || intrudes)
                misaligned.Add($"{p.slot}(yaw {p.yaw}{(intrudes ? ", 복도가 바닥 안으로 파고듦" : "")}{(touches == 0 ? ", 닿는 복도 없음" : "")})");
        }
    }

    /// <summary>스테이지 공간 꼭짓점 4개를 존 로컬 XZ 사각형으로(x = x, y = z).</summary>
    static Rect ToRect(Matrix4x4 toZone, Vector3[] corners)
    {
        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
        foreach (Vector3 c in corners)
        {
            Vector3 q = toZone.MultiplyPoint3x4(c);
            minX = Mathf.Min(minX, q.x); maxX = Mathf.Max(maxX, q.x);
            minZ = Mathf.Min(minZ, q.z); maxZ = Mathf.Max(maxZ, q.z);
        }
        return Rect.MinMaxRect(Snap(minX), Snap(minZ), Snap(maxX), Snap(maxZ));
    }

    // ─── 벽 사양 ───────────────────────────────────────────────────────────

    static List<WallSpec> BuildZoneSpecs(Bounds floor, float floorBottom, float floorTop, Dictionary<Side, List<Vector2>> doors)
    {
        var specs = new List<WallSpec>();
        float h = floorTop + Height - floorBottom, cy = floorBottom + h * 0.5f, t = Thickness;
        // 모서리는 X·Z 벽을 두께만큼 늘려 겹친다(틈 없음).
        AddSide(specs, Side.XMin, floor.min.z - t, floor.max.z + t, doors[Side.XMin],
            (a, b) => (new Vector3(floor.min.x - t * 0.5f, cy, (a + b) * 0.5f), new Vector3(t, h, b - a)));
        AddSide(specs, Side.XMax, floor.min.z - t, floor.max.z + t, doors[Side.XMax],
            (a, b) => (new Vector3(floor.max.x + t * 0.5f, cy, (a + b) * 0.5f), new Vector3(t, h, b - a)));
        AddSide(specs, Side.ZMin, floor.min.x - t, floor.max.x + t, doors[Side.ZMin],
            (a, b) => (new Vector3((a + b) * 0.5f, cy, floor.min.z - t * 0.5f), new Vector3(b - a, h, t)));
        AddSide(specs, Side.ZMax, floor.min.x - t, floor.max.x + t, doors[Side.ZMax],
            (a, b) => (new Vector3((a + b) * 0.5f, cy, floor.max.z + t * 0.5f), new Vector3(b - a, h, t)));
        return specs;
    }

    /// <summary>변 [from, to] 에서 문 구간을 뺀 조각마다 벽 하나. 조각이 하나면 Boundary_{변}, 여럿이면 _A, _B …(축 오름차순).</summary>
    static void AddSide(List<WallSpec> specs, Side side, float from, float to, List<Vector2> doors,
                        System.Func<float, float, (Vector3 center, Vector3 size)> make)
    {
        var pieces = new List<Vector2>();
        float cur = from;
        foreach (Vector2 d in Merge(doors))
        {
            if (d.x - cur > MinSegment) pieces.Add(new Vector2(cur, d.x));
            cur = Mathf.Max(cur, d.y);
        }
        if (to - cur > MinSegment) pieces.Add(new Vector2(cur, to));

        for (int i = 0; i < pieces.Count; i++)
        {
            (Vector3 c, Vector3 s) = make(pieces[i].x, pieces[i].y);
            string suffix = pieces.Count == 1 ? "" : "_" + (char)('A' + i);
            specs.Add(new WallSpec { Name = BoundaryPrefix + side + suffix, Center = c, Size = s });
        }
    }

    static List<Vector2> Merge(List<Vector2> intervals)
    {
        var sorted = intervals.Where(v => v.y - v.x > MinSegment).OrderBy(v => v.x).ToList();
        var merged = new List<Vector2>();
        foreach (Vector2 v in sorted)
        {
            if (merged.Count > 0 && v.x <= merged[merged.Count - 1].y + MinSegment)
                merged[merged.Count - 1] = new Vector2(merged[merged.Count - 1].x, Mathf.Max(merged[merged.Count - 1].y, v.y));
            else
                merged.Add(v);
        }
        return merged;
    }

    // ─── 복도 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 복도 바닥 프리팹의 긴 두 변에 벽. 너비 = 돌 바닥(문턱 제외) 콜라이더 폭, 길이 = 문턱까지 포함한 전체 바닥 길이 —
    /// 양 끝은 존 경계 박스(문 자리 양옆)와 겹친다.
    /// </summary>
    static string ProcessHallwayFloor(string path, int wallLayer)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(path);
        string row = null;
        EditPrefab(path, root =>
        {
            Collider[] cols = root.GetComponentsInChildren<Collider>(true)
                .Where(c => !c.isTrigger && !IsUnderBoundaries(c.transform, root.transform)).ToArray();
            Collider[] stones = cols.Where(c => !c.name.ToLowerInvariant().Contains("threshold")).ToArray();
            if (stones.Length == 0)
            {
                row = $"| {name} (복도) | 건너뜀: 바닥 콜라이더 없음 | - | - | - |";
                return false;
            }
            Bounds all = ToLocal(root.transform, cols[0].bounds);
            foreach (Collider c in cols) all.Encapsulate(ToLocal(root.transform, c.bounds));
            Bounds stone = ToLocal(root.transform, stones[0].bounds);
            foreach (Collider c in stones) stone.Encapsulate(ToLocal(root.transform, c.bounds));
            all.SetMinMax(Snap(all.min), Snap(all.max));
            stone.SetMinMax(Snap(stone.min), Snap(stone.max));

            float top = stone.max.y, cy = top + Height * 0.5f, t = Thickness;
            bool alongX = stone.size.x >= stone.size.z;   // 긴 축 = 복도 길이
            var specs = new List<WallSpec>();
            if (alongX)
            {
                specs.Add(new WallSpec { Name = BoundaryPrefix + Side.ZMin, Center = new Vector3(all.center.x, cy, stone.min.z - t * 0.5f), Size = new Vector3(all.size.x, Height, t) });
                specs.Add(new WallSpec { Name = BoundaryPrefix + Side.ZMax, Center = new Vector3(all.center.x, cy, stone.max.z + t * 0.5f), Size = new Vector3(all.size.x, Height, t) });
            }
            else
            {
                specs.Add(new WallSpec { Name = BoundaryPrefix + Side.XMin, Center = new Vector3(stone.min.x - t * 0.5f, cy, all.center.z), Size = new Vector3(t, Height, all.size.z) });
                specs.Add(new WallSpec { Name = BoundaryPrefix + Side.XMax, Center = new Vector3(stone.max.x + t * 0.5f, cy, all.center.z), Size = new Vector3(t, Height, all.size.z) });
            }
            string fb = $"폭({(alongX ? "z" : "x")}) {(alongX ? stone.min.z : stone.min.x):0.##}~{(alongX ? stone.max.z : stone.max.x):0.##} · " +
                        $"길이({(alongX ? "x" : "z")}) {(alongX ? all.min.x : all.min.z):0.##}~{(alongX ? all.max.x : all.max.z):0.##} top {top:0.##}";

            bool changed = Apply(root, specs, wallLayer, out string err);
            row = err != null
                ? $"| {name} (복도) | 건너뜀: {err} | {fb} | 양 끝 | - |"
                : $"| {name} (복도) | {(changed ? "갱신" : "변경 없음")} | {fb} | 양 끝(길이 방향) | {specs.Count} |";
            return changed && err == null;
        }, out string editError);
        return editError != null ? $"| {name} (복도) | 건너뜀: {editError} | - | - | - |" : row;
    }

    // ─── 적용(멱등) ────────────────────────────────────────────────────────

    /// <summary>
    /// InvisibleBoundaries 아래 Boundary_* 를 사양과 같게 맞춘다. 이미 같으면 false(저장 안 함).
    /// 사양에 없는 Boundary_* 는 지우고, 남는 오브젝트는 Transform + BoxCollider 만 둔다(렌더러·필터 제거).
    /// InvisibleBoundaries 의 기존 위치 오프셋은 보존하고 center 로 보정한다.
    /// </summary>
    static bool Apply(GameObject root, List<WallSpec> specs, int wallLayer, out string error)
    {
        error = null;
        Transform parent = root.transform.Find(BoundariesName);
        if (parent != null && (Quaternion.Angle(parent.localRotation, Quaternion.identity) > 0.01f || (parent.localScale - Vector3.one).sqrMagnitude > 1e-6f))
        {
            error = $"{BoundariesName} 이 회전/스케일돼 있다(rot {parent.localEulerAngles}, scale {parent.localScale})";
            return false;
        }

        if (parent != null && Matches(parent, specs, wallLayer)) return false;

        if (parent == null)
        {
            parent = new GameObject(BoundariesName).transform;
            parent.SetParent(root.transform, false);
            parent.gameObject.layer = wallLayer;
        }

        var keep = new HashSet<string>(specs.Select(s => s.Name));
        foreach (Transform child in parent.Cast<Transform>().ToList())
            if (child.name.StartsWith(BoundaryPrefix) && !keep.Contains(child.name))
                Object.DestroyImmediate(child.gameObject);

        foreach (WallSpec s in specs)
        {
            Transform t = parent.Find(s.Name);
            if (t == null)
            {
                t = new GameObject(s.Name).transform;
                t.SetParent(parent, false);
            }
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            GameObject go = t.gameObject;
            go.layer = wallLayer;
            go.tag = GameLayers.Wall;
            foreach (Component c in go.GetComponents<Component>())
                if (!(c is Transform) && !(c is BoxCollider)) Object.DestroyImmediate(c);
            BoxCollider[] boxes = go.GetComponents<BoxCollider>();
            for (int i = 1; i < boxes.Length; i++) Object.DestroyImmediate(boxes[i]);
            BoxCollider box = boxes.Length > 0 ? boxes[0] : go.AddComponent<BoxCollider>();
            box.isTrigger = false;
            box.enabled = true;
            box.center = s.Center - parent.localPosition;
            box.size = s.Size;
        }
        return true;
    }

    static bool Matches(Transform parent, List<WallSpec> specs, int wallLayer)
    {
        var existing = parent.Cast<Transform>().Where(c => c.name.StartsWith(BoundaryPrefix)).ToList();
        if (existing.Count != specs.Count) return false;
        foreach (WallSpec s in specs)
        {
            List<Transform> same = existing.Where(c => c.name == s.Name).ToList();
            if (same.Count != 1) return false;
            Transform t = same[0];
            GameObject go = t.gameObject;
            if (go.layer != wallLayer || !go.CompareTag(GameLayers.Wall)) return false;
            if (t.localPosition != Vector3.zero || t.localRotation != Quaternion.identity || t.localScale != Vector3.one) return false;
            Component[] comps = go.GetComponents<Component>();
            if (comps.Length != 2 || !(comps[1] is BoxCollider box)) return false;
            if (box.isTrigger || !box.enabled) return false;
            if ((box.center - (s.Center - parent.localPosition)).sqrMagnitude > Epsilon * Epsilon) return false;
            if ((box.size - s.Size).sqrMagnitude > Epsilon * Epsilon) return false;
        }
        return true;
    }

    // ─── 공용 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 프리팹을 열어 <paramref name="edit"/> 하고, true 면 저장한다. 그 프리팹이 프리팹 모드로 열려 있으면 그 스테이지에서 편집·저장한다
    /// (저장 안 된 수정이 있으면 건드리지 않고 <paramref name="error"/>).
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

    // 실측값을 1cm 로 반올림 — 콜라이더 bounds 의 부동소수 잡음(수 mm)이 다시 돌릴 때마다 diff 를 만들지 않게.
    static float Snap(float v) => Mathf.Round(v * 100f) / 100f;
    static Vector3 Snap(Vector3 v) => new Vector3(Snap(v.x), Snap(v.y), Snap(v.z));

    /// <summary>
    /// 월드 bounds → 루트 로컬 bounds(8 꼭짓점 변환 후 재포장). LoadPrefabContents 는 원점이 아닌 프리뷰 씬에 올릴 수 있다
    /// (BossRoomAuthoring.ToRootLocalBounds 와 같은 이유).
    /// </summary>
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
