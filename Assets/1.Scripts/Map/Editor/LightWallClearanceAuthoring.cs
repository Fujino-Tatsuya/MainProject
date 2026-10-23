using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 존 프리팹의 스포트라이트가 벽에 붙어 있으면 존 안쪽으로 옮긴다 (2026-10-23 사용자 요청).
///
/// 왜: 조명은 바로 아래를 비추는 넓은 스포트라이트라, 벽 가까이 있으면 빛이 벽면에 뭉쳐 하얗게 뜬다.
/// 맵 구조: 존 프리팹엔 벽이 없다. 벽은 Stage1 안의 벽 전용 프리팹(Level_wall_hallway)에 있고,
///          존은 셔플돼 슬롯마다 저작된 (위치, 90° 회전)으로 놓인다(ZoneSlot.Rotations, 씬 오버라이드 포함).
/// 판정: 열린 MapScene 의 ZoneSlot 들을 읽어, 존 프리팹이 들어갈 수 있는 **모든 슬롯 자리**에 놓아 보고
///       Level_wall_hallway 벽 조각(이름에 wall/pillar, floor 제외)까지 수평 거리를 잰다.
///       어느 슬롯에서든 Clearance 미만이면 그 벽 반대쪽(존 중심 쪽)으로 미는 양을 존 로컬 좌표로 모은다.
///       슬롯별 요구를 축마다 가장 큰 값으로 합치고, 최대 MaxShift 까지만 옮긴다. 높이·회전·세기는 그대로.
/// 🔴 4.MapScene 을 열어 둔 상태에서 실행. "보고만" 으로 목록을 확인한 뒤 "적용" 한다.
/// </summary>
static class LightWallClearanceAuthoring
{
    /// <summary>벽 면에서 조명까지 최소 수평 거리(m).</summary>
    const float Clearance = 3f;
    /// <summary>한 조명을 옮기는 최대 거리(m).</summary>
    const float MaxShift = 3f;
    const float MaxWallThickness = 2.5f;
    const string WallRootName = "Level_wall_hallway";
    const string Tag = "[LightWallClearance]";

    [MenuItem("Tools/Map/Authoring/벽에 붙은 조명 안쪽으로 (보고만)")]
    static void Report() => Run(apply: false);

    [MenuItem("Tools/Map/Authoring/벽에 붙은 조명 안쪽으로 (적용)")]
    static void Apply() => Run(apply: true);

    struct Wall { public Bounds Bounds; public string Name; }
    struct Placement { public int SlotId; public Vector3 Pos; public Quaternion Rot; }

    static void Run(bool apply)
    {
        ZoneSlot[] slots = Object.FindObjectsByType<ZoneSlot>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .OrderBy(s => s.SlotID).ToArray();
        if (slots.Length == 0)
        {
            Debug.LogError($"{Tag} 열린 씬에 ZoneSlot 이 없다 — 4.MapScene 을 열고 다시 실행.");
            return;
        }

        List<Wall> walls = CollectWalls();
        if (walls.Count == 0)
        {
            Debug.LogError($"{Tag} 열린 씬에서 '{WallRootName}' 벽 조각을 못 찾았다.");
            return;
        }

        // 존 프리팹 → 들어갈 수 있는 슬롯 자리들
        var byPrefab = new Dictionary<GameObject, List<Placement>>();
        foreach (ZoneSlot s in slots)
        {
            if (s.Rotations == null) continue;
            foreach (ZoneSlot.RotationEntry e in s.Rotations)
            {
                if (e.Prefab == null) continue;
                if (!byPrefab.TryGetValue(e.Prefab, out var list)) byPrefab[e.Prefab] = list = new List<Placement>();
                list.Add(new Placement
                {
                    SlotId = s.SlotID,
                    Pos = e.HasPosition ? e.Position : s.transform.position,
                    Rot = Quaternion.Euler(0f, e.YawSteps * 90f, 0f),
                });
            }
        }

        var sb = new StringBuilder();
        int totalMoved = 0, totalWarn = 0;

        foreach (var kv in byPrefab.OrderBy(k => k.Key.name))
        {
            string path = AssetDatabase.GetAssetPath(kv.Key);
            if (string.IsNullOrEmpty(path)) continue;
            List<Placement> places = kv.Value;

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Light[] lights = root.GetComponentsInChildren<Light>(true)
                    .Where(l => l.type == LightType.Spot).ToArray();
                Quaternion rootInv = Quaternion.Inverse(root.transform.rotation);

                int moved = 0;
                var lines = new StringBuilder();
                foreach (Light light in lights)
                {
                    // Instantiate(prefab, pos, rot) 기준 로컬 오프셋 (루트 스케일 포함)
                    Vector3 offset = rootInv * (light.transform.position - root.transform.position);

                    float posX = 0f, negX = 0f, posZ = 0f, negZ = 0f;
                    var why = new List<string>();
                    foreach (Placement pl in places)
                    {
                        Vector3 world = pl.Pos + pl.Rot * offset;
                        Vector2 xz = XZ(world);
                        Wall hit = default; float d = float.MaxValue;
                        foreach (Wall w in walls)
                        {
                            if (w.Bounds.max.y < world.y - 8f || w.Bounds.min.y > world.y + 1f) continue;
                            float dd = Dist(xz, w.Bounds);
                            if (dd < d) { d = dd; hit = w; }
                        }
                        if (d >= Clearance - 0.01f) continue;

                        Vector2 toCenter = XZ(pl.Pos) - xz;
                        Vector2 dir = d > 0.001f ? (xz - Nearest(xz, hit.Bounds)) / d
                                     : (toCenter.sqrMagnitude > 0.0001f ? toCenter.normalized : Vector2.zero);
                        if (Vector2.Dot(dir, toCenter) < 0f && toCenter.magnitude > 1f) dir = -dir;

                        Vector2 pushW = dir * (Clearance - d);
                        Vector3 pushL = Quaternion.Inverse(pl.Rot) * new Vector3(pushW.x, 0f, pushW.y);
                        posX = Mathf.Max(posX, pushL.x); negX = Mathf.Min(negX, pushL.x);
                        posZ = Mathf.Max(posZ, pushL.z); negZ = Mathf.Min(negZ, pushL.z);
                        why.Add($"슬롯{pl.SlotId} '{hit.Name}' {d:0.0}m");
                    }
                    if (why.Count == 0) continue;

                    // 반대 방향 요구가 함께 있으면(양쪽 벽) 서로 상쇄 — 경고
                    bool conflict = (posX > 0.05f && negX < -0.05f) || (posZ > 0.05f && negZ < -0.05f);
                    Vector3 push = new Vector3(posX + negX, 0f, posZ + negZ);
                    if (push.magnitude > MaxShift) push = push.normalized * MaxShift;
                    if (push.magnitude < 0.01f && !conflict) continue;

                    moved++;
                    if (conflict) totalWarn++;
                    Vector3 to = offset + push;
                    lines.AppendLine($"  {light.name}: ({offset.x:0.00}, {offset.z:0.00}) → ({to.x:0.00}, {to.z:0.00}) [{push.magnitude:0.00}m] " +
                                     string.Join(", ", why) + (conflict ? " ⚠ 슬롯마다 반대쪽 벽 — 상쇄됨" : ""));
                    if (apply)
                        light.transform.position += root.transform.rotation * push;
                }

                sb.AppendLine($"{root.name}: 슬롯 {places.Count}곳, 스포트 {lights.Length}개 중 {moved}개 이동");
                sb.Append(lines);
                totalMoved += moved;
                if (apply && moved > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        string head = $"{Tag} {(apply ? "적용" : "보고만")} — 이동 {totalMoved}개, 경고 {totalWarn}개 " +
                      $"(벽 조각 {walls.Count}개, 벽에서 {Clearance}m, 최대 {MaxShift}m)";
        Debug.Log(head + "\n" + sb);
        if (apply) AssetDatabase.SaveAssets();
    }

    /// <summary>열린 씬의 Level_wall_hallway 아래 벽 조각 렌더러 AABB.</summary>
    static List<Wall> CollectWalls()
    {
        var result = new List<Wall>();
        // 씬에서 Stage1 은 꺼져 있다(런타임에 켬) — 비활성 포함으로 찾고, 꺼진 렌더러는 bounds 가 0 이라 메시로 계산.
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t.name != WallRootName) continue; // _tutorial 변형(튜토리얼 스테이지)은 제외
            foreach (MeshFilter mf in t.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || !IsWallPiece(mf.transform, t)) continue;
                Bounds b = ToWorld(mf.sharedMesh.bounds, mf.transform.localToWorldMatrix);
                if (Mathf.Min(b.size.x, b.size.z) > MaxWallThickness) continue; // 합친 큰 메시 제외
                result.Add(new Wall { Bounds = b, Name = PieceName(mf.transform, t) });
            }
        }
        return result;
    }

    static bool IsWallPiece(Transform t, Transform stop)
    {
        bool wall = false;
        for (; t != null && t != stop; t = t.parent)
        {
            string n = t.name.ToLowerInvariant();
            if (n.Contains("floor")) return false;
            if (n.Contains("wall") || n.Contains("pillar")) wall = true;
        }
        return wall;
    }

    /// <summary>벽 루트 바로 아래 섹션 이름 + 조각 이름 (로그용).</summary>
    static string PieceName(Transform t, Transform stop)
    {
        string piece = t.name;
        Transform section = t;
        while (section.parent != null && section.parent != stop) section = section.parent;
        return section == t ? piece : $"{section.name}/{piece}";
    }

    /// <summary>로컬 AABB 의 8 꼭짓점을 월드로 옮겨 다시 감싼다.</summary>
    static Bounds ToWorld(Bounds local, Matrix4x4 m)
    {
        Vector3 c = local.center, e = local.extents;
        var b = new Bounds(m.MultiplyPoint3x4(c), Vector3.zero);
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
            b.Encapsulate(m.MultiplyPoint3x4(c + corner));
        }
        return b;
    }

    static Vector2 Nearest(Vector2 p, Bounds b) =>
        new Vector2(Mathf.Clamp(p.x, b.min.x, b.max.x), Mathf.Clamp(p.y, b.min.z, b.max.z));

    static float Dist(Vector2 p, Bounds b) => Vector2.Distance(p, Nearest(p, b));

    static Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);
}
