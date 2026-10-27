using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 로비 발사대 둘레 바닥 메시 생성기(할 일 100 C2j). 메뉴 <c>Tools/Lobby/Rebuild Pod Bay Floor</c>.
/// <para>바닥 = 바깥 범위(포드 베이 벽 중심선·인접 홀 타일 경계까지, 로비 루트 로컬 좌표) − 발사대 외곽선.
/// 외곽선 = 발사대의 고정 메시(<see cref="SilhouetteRenderers"/>) 전체 높이를 XZ 로 투영한 실루엣에 <see cref="Margin"/> 여유를 더한 것
/// (아래 가운데 돌출부까지 — 바닥이 그 밑으로 들어가지 않는다, 2026-10-27 floor2). 애니로 움직이는 Pod·Launcher 는 넣지 않는다.
/// 위쪽 난간 플랫폼(<see cref="RailingPlatformCutZ"/> 북쪽 — 난간 띠·가운데 돌출부)은 외곽선에서 잘라 바닥으로 덮는다(floor2 정정).</para>
/// <para>실루엣은 <see cref="Cell"/> 격자로 보수적 래스터화(삼각형이 조금이라도 걸치는 칸 = 막힘)하고, 행마다 좌우 끝 사이를 채운 뒤(가로 오목부 막기),
/// 막힌 칸과의 틈이 여유보다 좁은 칸도 막는다.
/// 바닥은 바깥 범위 경계에서 이어지는 칸만 쓴다(실루엣이 둘러싼 구멍 — 포드 통로 등 — 은 비운다).</para>
/// <para>메시: 윗면 y=0(서브메시 1, 타일 재질), 옆면·아랫면 y=−0.3(서브메시 0, 모서리 재질) — <c>SM_SB_Floor_4x4_T030_v015</c> 와 같은 구성.
/// 윗면 UV 는 그 타일과 같은 매핑(로컬 XZ ÷ −2, 2m 주기)이라 이웃 타일 무늬가 이어진다.
/// 다시 돌리면 같은 메시 에셋을 덮어쓴다(guid 유지).</para>
/// </summary>
public static class LobbyPodBayFloorBuilder
{
    private const string Tag = "[PodBayFloor]";
    public const string MenuPath = "Tools/Lobby/Rebuild Pod Bay Floor";
    public const string LobbyPrefabPath = "Assets/2.Prefabs/Environment/Layouts/Lobby/PF_Lobby_Complete_v016.prefab";
    public const string MeshAssetPath = "Assets/2.Prefabs/Environment/Layouts/Lobby/Generated/Mesh_Lobby_PodBayFloor.asset";
    public const string FloorObjectName = "SB_Floor_PodBay_Generated";
    public const string LaunchStationName = "PF_DropPod_LaunchStation_Combined_v001";
    private const string ReferenceTileMesh = "SM_SB_Floor_4x4_T030_v015";

    /// <summary>외곽선에 넣는 발사대 고정 렌더러(이름). Pod·Launcher_Moving(스킨, 애니)·Hidden_*(꺼짐)은 뺀다.</summary>
    public static readonly string[] SilhouetteRenderers = { "Platform_Fixed", "Tunnel_Fixed" };

    /// <summary>
    /// 외곽선을 자르는 발사대 로컬 z. 이보다 북쪽(위쪽 난간 플랫폼)은 외곽선에서 빼 바닥으로 덮는다.
    /// 루트 로컬 z −6.60 — 본체 윗면 북쪽 끝(−6.67)과 난간 플랫폼 남쪽 벽(−6.58) 사이 바닥 홈(2026-10-27 조사).
    /// </summary>
    public const float RailingPlatformCutZ = 2.22f;

    public const float Margin = 0.02f;
    public const float Cell = 0.01f;
    public const float Thickness = 0.3f;

    /// <summary>
    /// 바깥 범위(로비 루트 로컬 XZ, 직사각형 합집합). 2026-10-26 조사:
    /// 북 z=−4 = 홀 타일(021·028·033) 남쪽 끝 / 동 x=4 = 016_hall 타일 서쪽 끝(z −8~−4), x=4.5 = 벽 023 중심선(z −12~−8) /
    /// 남 z=−12 = 벽 024·025·030 중심선 / 서 x=−8 = 038_administrator 타일 동쪽 끝·벽 036 중심선(z −8~−4), x=−7.5 = 벽 029 중심선(z −12~−8).
    /// </summary>
    public static readonly Rect[] OuterRegion =
    {
        Rect.MinMaxRect(-8f, -8f, 4f, -4f),
        Rect.MinMaxRect(-7.5f, -12f, 4.5f, -8f),
    };

    public sealed class Silhouette
    {
        public readonly List<Vector2[]> Polygons = new List<Vector2[]>();
        public readonly List<string> Renderers = new List<string>();
    }

    public sealed class FloorGrid
    {
        public Rect Bounds;
        public int Width, Height;
        public bool[] Floor, Blocked;
        public int BlockedCells, HoleCells;
        public bool IsFloor(int x, int z) => x >= 0 && z >= 0 && x < Width && z < Height && Floor[z * Width + x];

        /// <summary>점이 든 칸 번호(범위 밖이면 −1).</summary>
        public int CellAt(Vector2 p)
        {
            int x = Mathf.FloorToInt((p.x - Bounds.xMin) / Cell), z = Mathf.FloorToInt((p.y - Bounds.yMin) / Cell);
            return x >= 0 && z >= 0 && x < Width && z < Height ? z * Width + x : -1;
        }
    }

    [MenuItem(MenuPath)]
    private static void RebuildMenu()
    {
        var report = Rebuild();
        Debug.Log($"{Tag} {report}\n점검: {Verify()}");
    }

    /// <summary>메시를 다시 만들어 에셋에 덮어쓰고, 로비 프리팹에 바닥 오브젝트가 없으면 만든다. 결과 요약을 돌려준다.</summary>
    public static string Rebuild()
    {
        var root = PrefabUtility.LoadPrefabContents(LobbyPrefabPath);
        try
        {
            var sil = CollectSilhouette(root.transform);
            var grid = Rasterize(sil);
            var refTile = FindReferenceTile(root.transform);
            var mesh = BuildMesh(grid);
            mesh = SaveMesh(mesh);

            var floor = root.transform.Find(FloorObjectName);
            bool created = floor == null;
            if (created)
            {
                floor = new GameObject(FloorObjectName).transform;
                floor.SetParent(root.transform, false);
            }
            floor.localPosition = Vector3.zero;
            floor.localRotation = Quaternion.identity;
            floor.localScale = Vector3.one;
            floor.gameObject.layer = refTile.gameObject.layer;
            GameObjectUtility.SetStaticEditorFlags(floor.gameObject, GameObjectUtility.GetStaticEditorFlags(refTile.gameObject));

            // Unity 오브젝트는 ?? 로 가짜 null 을 못 거른다.
            var mf = floor.GetComponent<MeshFilter>();
            if (mf == null) mf = floor.gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = floor.GetComponent<MeshRenderer>();
            if (mr == null) mr = floor.gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterials = refTile.sharedMaterials;
            mr.shadowCastingMode = refTile.shadowCastingMode;
            mr.receiveShadows = refTile.receiveShadows;
            mr.lightProbeUsage = refTile.lightProbeUsage;
            mr.reflectionProbeUsage = refTile.reflectionProbeUsage;
            mr.renderingLayerMask = refTile.renderingLayerMask;
            mr.motionVectorGenerationMode = refTile.motionVectorGenerationMode;

            PrefabUtility.SaveAsPrefabAsset(root, LobbyPrefabPath);
            return $"{(created ? "바닥 오브젝트 생성" : "바닥 오브젝트 갱신")} — 외곽선 렌더러 [{string.Join(", ", sil.Renderers)}], " +
                   $"다각형 {sil.Polygons.Count}, 막힌 칸 {grid.BlockedCells}, 구멍 칸 {grid.HoleCells}, 정점 {mesh.vertexCount}, " +
                   $"윗면 삼각형 {mesh.GetIndexCount(1) / 3}, 옆·아랫면 삼각형 {mesh.GetIndexCount(0) / 3}";
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static MeshRenderer FindReferenceTile(Transform root)
    {
        var tile = root.GetComponentsInChildren<MeshFilter>(true)
            .FirstOrDefault(m => m.sharedMesh != null && m.sharedMesh.name == ReferenceTileMesh && m.gameObject.activeInHierarchy);
        if (tile == null) throw new System.InvalidOperationException($"{Tag} 기준 타일({ReferenceTileMesh})을 못 찾았다.");
        return tile.GetComponent<MeshRenderer>();
    }

    /// <summary>
    /// 발사대 고정 메시 삼각형을 발사대 로컬 z ≤ <see cref="RailingPlatformCutZ"/> 로 자른 뒤 루트 로컬 XZ 로 투영한 다각형(수직 면은 선분) 목록.
    /// </summary>
    public static Silhouette CollectSilhouette(Transform root)
    {
        var station = root.Find(LaunchStationName);
        if (station == null) throw new System.InvalidOperationException($"{Tag} {LaunchStationName} 이 없다.");
        var sil = new Silhouette();
        var stationToRoot = root.worldToLocalMatrix * station.localToWorldMatrix;
        foreach (var name in SilhouetteRenderers)
        {
            var mf = station.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(m => m.name == name);
            if (mf == null || mf.sharedMesh == null) throw new System.InvalidOperationException($"{Tag} 외곽선 렌더러 {name} 이 없다.");
            if (!mf.sharedMesh.isReadable) throw new System.InvalidOperationException($"{Tag} {name} 메시가 Read/Write 꺼짐.");
            sil.Renderers.Add($"{name}({mf.sharedMesh.name})");
            var m = station.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            var verts = mf.sharedMesh.vertices.Select(v => m.MultiplyPoint3x4(v)).ToArray();
            var tris = mf.sharedMesh.triangles;
            for (int i = 0; i < tris.Length; i += 3)
            {
                var clipped = ClipNorthOf(new[] { verts[tris[i]], verts[tris[i + 1]], verts[tris[i + 2]] }, RailingPlatformCutZ);
                if (clipped.Count < 3) continue;
                sil.Polygons.Add(clipped.Select(p => { var q = stationToRoot.MultiplyPoint3x4(p); return new Vector2(q.x, q.z); }).ToArray());
            }
        }
        return sil;
    }

    // 볼록 다각형에서 z > cut 부분을 잘라낸다(Sutherland–Hodgman, 평면 하나).
    private static List<Vector3> ClipNorthOf(Vector3[] poly, float cut)
    {
        var res = new List<Vector3>(poly.Length + 1);
        for (int i = 0; i < poly.Length; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Length];
            bool ain = a.z <= cut, bin = b.z <= cut;
            if (ain) res.Add(a);
            if (ain != bin) res.Add(Vector3.Lerp(a, b, (cut - a.z) / (b.z - a.z)));
        }
        return res;
    }

    /// <summary>난간 플랫폼 경계(<see cref="RailingPlatformCutZ"/>)의 루트 로컬 z.</summary>
    public static float RailingCutRootZ(Transform root)
    {
        var station = root.Find(LaunchStationName);
        if (station == null) throw new System.InvalidOperationException($"{Tag} {LaunchStationName} 이 없다.");
        return root.InverseTransformPoint(station.TransformPoint(new Vector3(0, 0, RailingPlatformCutZ))).z;
    }

    public static Rect OuterBounds()
    {
        var b = OuterRegion[0];
        foreach (var r in OuterRegion) b = Rect.MinMaxRect(Mathf.Min(b.xMin, r.xMin), Mathf.Min(b.yMin, r.yMin), Mathf.Max(b.xMax, r.xMax), Mathf.Max(b.yMax, r.yMax));
        return b;
    }

    /// <summary>바깥 범위 격자에서 바닥 칸을 고른다(실루엣 보수 래스터 + 여유 팽창 + 바깥 경계 연결 칸만).</summary>
    public static FloorGrid Rasterize(Silhouette sil)
    {
        var bounds = OuterBounds();
        var g = new FloorGrid
        {
            Bounds = bounds,
            Width = Mathf.RoundToInt(bounds.width / Cell),
            Height = Mathf.RoundToInt(bounds.height / Cell),
        };
        int w = g.Width, h = g.Height;
        var blocked = new bool[w * h];
        float half = Cell * 0.5f;

        foreach (var poly in sil.Polygons)
        {
            float minX = poly.Min(p => p.x), maxX = poly.Max(p => p.x), minZ = poly.Min(p => p.y), maxZ = poly.Max(p => p.y);
            int x0 = Mathf.Max(0, Mathf.FloorToInt((minX - bounds.xMin) / Cell));
            int x1 = Mathf.Min(w - 1, Mathf.FloorToInt((maxX - bounds.xMin) / Cell));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((minZ - bounds.yMin) / Cell));
            int z1 = Mathf.Min(h - 1, Mathf.FloorToInt((maxZ - bounds.yMin) / Cell));
            if (x0 > x1 || z0 > z1) continue;
            var axes = EdgeAxes(poly);
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                int idx = z * w + x;
                if (blocked[idx]) continue;
                var c = new Vector2(bounds.xMin + (x + 0.5f) * Cell, bounds.yMin + (z + 0.5f) * Cell);
                if (SquareOverlaps(c, half, axes)) blocked[idx] = true;
            }
        }

        // 행마다 발사대 좌우 끝 사이를 모두 막는다 — 아래 가운데 홈(터널 남쪽)처럼 가로로 오목한 곳에 바닥이 들어가지 않게(floor2 요청).
        // 아래로 벌어진 모서리 모따기는 그 행의 끝 바깥이라 그대로 바닥이 된다.
        for (int z = 0; z < h; z++)
        {
            int lo = -1, hi = -1;
            for (int x = 0; x < w; x++)
                if (blocked[z * w + x]) { if (lo < 0) lo = x; hi = x; }
            for (int x = lo + 1; lo >= 0 && x < hi; x++) blocked[z * w + x] = true;
        }

        // 막힌 칸과의 틈(칸 경계 사이 거리)이 여유보다 좁은 칸도 막는다.
        int r = Mathf.CeilToInt(Margin / Cell) + 1;
        var kernel = new List<Vector2Int>();
        for (int dz = -r; dz <= r; dz++)
        for (int dx = -r; dx <= r; dx++)
        {
            float gx = Mathf.Max(0, Mathf.Abs(dx) - 1) * Cell, gz = Mathf.Max(0, Mathf.Abs(dz) - 1) * Cell;
            if (gx * gx + gz * gz < Margin * Margin - 1e-9f) kernel.Add(new Vector2Int(dx, dz));
        }
        var dilated = (bool[])blocked.Clone();
        for (int z = 0; z < h; z++)
        for (int x = 0; x < w; x++)
        {
            if (!blocked[z * w + x]) continue;
            foreach (var k in kernel)
            {
                int nx = x + k.x, nz = z + k.y;
                if (nx >= 0 && nz >= 0 && nx < w && nz < h) dilated[nz * w + nx] = true;
            }
        }

        var inside = new bool[w * h];
        for (int z = 0; z < h; z++)
        for (int x = 0; x < w; x++)
        {
            var c = new Vector2(bounds.xMin + (x + 0.5f) * Cell, bounds.yMin + (z + 0.5f) * Cell);
            inside[z * w + x] = OuterRegion.Any(rc => rc.Contains(c));
        }

        // 바깥 범위 경계 칸에서 이어지는 칸만 바닥.
        g.Floor = new bool[w * h];
        g.Blocked = dilated;
        var queue = new Queue<int>();
        for (int z = 0; z < h; z++)
        for (int x = 0; x < w; x++)
        {
            int idx = z * w + x;
            if (!inside[idx] || dilated[idx]) continue;
            bool edge = x == 0 || z == 0 || x == w - 1 || z == h - 1 || !inside[idx - 1] || !inside[idx + 1] || !inside[idx - w] || !inside[idx + w];
            if (edge) { g.Floor[idx] = true; queue.Enqueue(idx); }
        }
        while (queue.Count > 0)
        {
            int idx = queue.Dequeue();
            int x = idx % w, z = idx / w;
            if (x > 0) Visit(idx - 1);
            if (x < w - 1) Visit(idx + 1);
            if (z > 0) Visit(idx - w);
            if (z < h - 1) Visit(idx + w);
        }
        void Visit(int n)
        {
            if (g.Floor[n] || !inside[n] || dilated[n]) return;
            g.Floor[n] = true;
            queue.Enqueue(n);
        }

        for (int i = 0; i < w * h; i++)
        {
            if (!inside[i]) continue;
            if (dilated[i]) g.BlockedCells++;
            else if (!g.Floor[i]) g.HoleCells++;
        }
        return g;
    }

    // 다각형 변의 법선 축과 그 축 위 다각형 투영 구간. 선분(수직 면 투영)도 처리한다.
    private static List<(Vector2 n, float min, float max)> EdgeAxes(Vector2[] poly)
    {
        var axes = new List<(Vector2, float, float)>();
        for (int i = 0; i < poly.Length; i++)
        {
            var e = poly[(i + 1) % poly.Length] - poly[i];
            if (e.sqrMagnitude < 1e-12f) continue;
            var n = new Vector2(-e.y, e.x).normalized;
            float mn = float.MaxValue, mx = float.MinValue;
            foreach (var p in poly) { float d = Vector2.Dot(n, p); mn = Mathf.Min(mn, d); mx = Mathf.Max(mx, d); }
            axes.Add((n, mn, mx));
        }
        return axes;
    }

    private static bool SquareOverlaps(Vector2 c, float half, List<(Vector2 n, float min, float max)> axes)
    {
        foreach (var (n, mn, mx) in axes)
        {
            float cd = Vector2.Dot(n, c), ext = half * (Mathf.Abs(n.x) + Mathf.Abs(n.y));
            if (cd + ext < mn || cd - ext > mx) return false;
        }
        return true;
    }

    /// <summary>바닥 칸을 직사각형으로 합친다(행 구간 → 같은 구간끼리 세로 병합). 루트 로컬 XZ.</summary>
    public static List<Rect> MergeRects(FloorGrid g)
    {
        var rects = new List<Rect>();
        var open = new Dictionary<(int, int), int>(); // (x0,x1) → 시작 z
        for (int z = 0; z <= g.Height; z++)
        {
            var runs = new HashSet<(int, int)>();
            if (z < g.Height)
            {
                for (int x = 0; x < g.Width;)
                {
                    if (!g.IsFloor(x, z)) { x++; continue; }
                    int s = x;
                    while (x < g.Width && g.IsFloor(x, z)) x++;
                    runs.Add((s, x));
                }
            }
            foreach (var key in open.Keys.ToList())
            {
                if (runs.Contains(key)) continue;
                rects.Add(CellRect(g, key.Item1, open[key], key.Item2, z));
                open.Remove(key);
            }
            foreach (var run in runs)
                if (!open.ContainsKey(run)) open[run] = z;
        }
        return rects;
    }

    private static Rect CellRect(FloorGrid g, int x0, int z0, int x1, int z1) =>
        Rect.MinMaxRect(g.Bounds.xMin + x0 * Cell, g.Bounds.yMin + z0 * Cell, g.Bounds.xMin + x1 * Cell, g.Bounds.yMin + z1 * Cell);

    public static Mesh BuildMesh(FloorGrid g)
    {
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var top = new List<int>();
        var sides = new List<int>();
        float bottom = -Thickness;

        // 타일(SM_SB_Floor_4x4_T030_v015)과 같은 매핑: uv = −xz / 2.
        void Quad(List<int> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, System.Func<Vector3, Vector2> uv)
        {
            int i = verts.Count;
            foreach (var p in new[] { a, b, c, d }) { verts.Add(p); normals.Add(n); uvs.Add(uv(p)); }
            tris.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
        }
        Vector2 TopUv(Vector3 p) => new Vector2(-p.x * 0.5f, -p.z * 0.5f);

        foreach (var r in MergeRects(g))
        {
            Quad(top, new Vector3(r.xMin, 0, r.yMin), new Vector3(r.xMin, 0, r.yMax), new Vector3(r.xMax, 0, r.yMax), new Vector3(r.xMax, 0, r.yMin), Vector3.up, TopUv);
            Quad(sides, new Vector3(r.xMin, bottom, r.yMin), new Vector3(r.xMax, bottom, r.yMin), new Vector3(r.xMax, bottom, r.yMax), new Vector3(r.xMin, bottom, r.yMax), Vector3.down, TopUv);
        }

        // 옆면: 바닥 칸과 바닥 아닌 칸 사이 경계를 같은 줄끼리 이어 붙인다.
        Vector2 SideUv(Vector3 p, Vector3 n) => new Vector2((Mathf.Abs(n.x) > 0.5f ? p.z : p.x) * 0.5f, p.y * 0.5f);
        // 세로 경계(x 방향 법선)
        for (int x = 0; x <= g.Width; x++)
        {
            for (int dir = -1; dir <= 1; dir += 2)
            {
                int z = 0;
                while (z < g.Height)
                {
                    bool Edge(int zz) => dir < 0 ? (g.IsFloor(x, zz) && !g.IsFloor(x - 1, zz)) : (g.IsFloor(x - 1, zz) && !g.IsFloor(x, zz));
                    if (!Edge(z)) { z++; continue; }
                    int s = z;
                    while (z < g.Height && Edge(z)) z++;
                    float px = g.Bounds.xMin + x * Cell, z0 = g.Bounds.yMin + s * Cell, z1 = g.Bounds.yMin + z * Cell;
                    var n = new Vector3(dir, 0, 0);
                    if (dir < 0)
                        Quad(sides, new Vector3(px, bottom, z0), new Vector3(px, bottom, z1), new Vector3(px, 0, z1), new Vector3(px, 0, z0), n, p => SideUv(p, n));
                    else
                        Quad(sides, new Vector3(px, bottom, z1), new Vector3(px, bottom, z0), new Vector3(px, 0, z0), new Vector3(px, 0, z1), n, p => SideUv(p, n));
                }
            }
        }
        // 가로 경계(z 방향 법선)
        for (int z = 0; z <= g.Height; z++)
        {
            for (int dir = -1; dir <= 1; dir += 2)
            {
                int x = 0;
                while (x < g.Width)
                {
                    bool Edge(int xx) => dir < 0 ? (g.IsFloor(xx, z) && !g.IsFloor(xx, z - 1)) : (g.IsFloor(xx, z - 1) && !g.IsFloor(xx, z));
                    if (!Edge(x)) { x++; continue; }
                    int s = x;
                    while (x < g.Width && Edge(x)) x++;
                    float pz = g.Bounds.yMin + z * Cell, x0 = g.Bounds.xMin + s * Cell, x1 = g.Bounds.xMin + x * Cell;
                    var n = new Vector3(0, 0, dir);
                    if (dir < 0)
                        Quad(sides, new Vector3(x1, bottom, pz), new Vector3(x0, bottom, pz), new Vector3(x0, 0, pz), new Vector3(x1, 0, pz), n, p => SideUv(p, n));
                    else
                        Quad(sides, new Vector3(x0, bottom, pz), new Vector3(x1, bottom, pz), new Vector3(x1, 0, pz), new Vector3(x0, 0, pz), n, p => SideUv(p, n));
                }
            }
        }

        var mesh = new Mesh { name = "Mesh_Lobby_PodBayFloor", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(sides, 0);
        mesh.SetTriangles(top, 1);
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh SaveMesh(Mesh mesh)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshAssetPath);
        if (existing == null)
        {
            var dir = System.IO.Path.GetDirectoryName(MeshAssetPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(dir).Replace('\\', '/'), System.IO.Path.GetFileName(dir));
            AssetDatabase.CreateAsset(mesh, MeshAssetPath);
            return mesh;
        }
        // 같은 에셋에 덮어써 guid·참조를 유지한다.
        existing.Clear();
        EditorUtility.CopySerialized(mesh, existing);
        existing.name = System.IO.Path.GetFileNameWithoutExtension(MeshAssetPath);
        Object.DestroyImmediate(mesh);
        EditorUtility.SetDirty(existing);
        AssetDatabase.SaveAssetIfDirty(existing);
        return existing;
    }

    /// <summary>
    /// 생성된 메시 윗면 삼각형과 외곽선 다각형 사이 검사. 겹침 수와 최소 틈(m)을 돌려준다.
    /// 두 볼록 다각형이 안 겹치면 최소 거리는 한쪽 꼭짓점과 다른 쪽 변 사이에서 나온다.
    /// </summary>
    public static (int overlaps, float minGap, int checkedPairs) CheckIntrusion(Mesh mesh, Matrix4x4 meshToRoot, Silhouette sil)
    {
        var v = mesh.vertices;
        var t = mesh.GetTriangles(1);
        var tris = new List<Vector2[]>();
        for (int i = 0; i < t.Length; i += 3)
            tris.Add(new[] { t[i], t[i + 1], t[i + 2] }.Select(k => { var p = meshToRoot.MultiplyPoint3x4(v[k]); return new Vector2(p.x, p.z); }).ToArray());

        const float bucket = 0.25f, reach = 0.1f;
        var grid = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < tris.Count; i++)
        {
            var (mn, mx) = Box(tris[i]);
            for (int bz = Mathf.FloorToInt(mn.y / bucket); bz <= Mathf.FloorToInt(mx.y / bucket); bz++)
            for (int bx = Mathf.FloorToInt(mn.x / bucket); bx <= Mathf.FloorToInt(mx.x / bucket); bx++)
            {
                if (!grid.TryGetValue((bx, bz), out var l)) grid[(bx, bz)] = l = new List<int>();
                l.Add(i);
            }
        }

        int overlaps = 0, pairs = 0;
        float minGap = float.MaxValue;
        var seen = new HashSet<int>();
        foreach (var poly in sil.Polygons)
        {
            var (mn, mx) = Box(poly);
            seen.Clear();
            for (int bz = Mathf.FloorToInt((mn.y - reach) / bucket); bz <= Mathf.FloorToInt((mx.y + reach) / bucket); bz++)
            for (int bx = Mathf.FloorToInt((mn.x - reach) / bucket); bx <= Mathf.FloorToInt((mx.x + reach) / bucket); bx++)
            {
                if (!grid.TryGetValue((bx, bz), out var l)) continue;
                foreach (var i in l)
                {
                    if (!seen.Add(i)) continue;
                    pairs++;
                    if (ConvexOverlap(poly, tris[i])) { overlaps++; minGap = 0; continue; }
                    minGap = Mathf.Min(minGap, ConvexDistance(poly, tris[i]));
                }
            }
        }
        return (overlaps, minGap, pairs);
    }

    private static (Vector2, Vector2) Box(Vector2[] p) =>
        (new Vector2(p.Min(q => q.x), p.Min(q => q.y)), new Vector2(p.Max(q => q.x), p.Max(q => q.y)));

    // 분리축 검사(접하기만 하면 겹침 아님).
    private static bool ConvexOverlap(Vector2[] a, Vector2[] b)
    {
        foreach (var poly in new[] { a, b })
        {
            for (int i = 0; i < poly.Length; i++)
            {
                var e = poly[(i + 1) % poly.Length] - poly[i];
                if (e.sqrMagnitude < 1e-12f) continue;
                var n = new Vector2(-e.y, e.x).normalized;
                float amin = a.Min(p => Vector2.Dot(n, p)), amax = a.Max(p => Vector2.Dot(n, p));
                float bmin = b.Min(p => Vector2.Dot(n, p)), bmax = b.Max(p => Vector2.Dot(n, p));
                if (amax <= bmin + 1e-6f || bmax <= amin + 1e-6f) return false;
            }
        }
        return true;
    }

    private static float ConvexDistance(Vector2[] a, Vector2[] b)
    {
        float d = float.MaxValue;
        foreach (var p in a) d = Mathf.Min(d, PointToPoly(p, b));
        foreach (var p in b) d = Mathf.Min(d, PointToPoly(p, a));
        return d;
    }

    private static float PointToPoly(Vector2 p, Vector2[] poly)
    {
        float d = float.MaxValue;
        for (int i = 0; i < poly.Length; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Length];
            var ab = b - a;
            float tt = ab.sqrMagnitude < 1e-12f ? 0 : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            d = Mathf.Min(d, Vector2.Distance(p, a + ab * tt));
        }
        return d;
    }

    /// <summary>윗면 서브메시를 사각형으로 되돌린다(<see cref="BuildMesh"/> 가 사각형마다 정점 4개·인덱스 6개를 쓴다). 루트 로컬 XZ.</summary>
    public static List<Rect> TopRects(Mesh mesh, Matrix4x4 meshToRoot)
    {
        var v = mesh.vertices;
        var t = mesh.GetTriangles(1);
        var rects = new List<Rect>();
        for (int i = 0; i < t.Length; i += 6)
        {
            var pts = new[] { t[i], t[i + 1], t[i + 2], t[i + 5] }.Select(k => meshToRoot.MultiplyPoint3x4(v[k])).ToArray();
            rects.Add(Rect.MinMaxRect(pts.Min(p => p.x), pts.Min(p => p.z), pts.Max(p => p.x), pts.Max(p => p.z)));
        }
        return rects;
    }

    /// <summary>켜진 기존 바닥 타일(<c>SM_SB_Floor*</c> 메시)의 루트 로컬 XZ 범위.</summary>
    public static List<Rect> ExistingTiles(Transform root)
    {
        var toRoot = root.worldToLocalMatrix;
        return root.GetComponentsInChildren<MeshFilter>(true)
            .Where(m => m.gameObject.activeInHierarchy && m.name != FloorObjectName && m.sharedMesh != null && m.sharedMesh.name.StartsWith("SM_SB_Floor"))
            .Select(m =>
            {
                var b = m.sharedMesh.bounds;
                var mat = toRoot * m.transform.localToWorldMatrix;
                var c = new[] { b.min, b.max, new Vector3(b.min.x, 0, b.max.z), new Vector3(b.max.x, 0, b.min.z) }.Select(p => mat.MultiplyPoint3x4(p)).ToArray();
                return Rect.MinMaxRect(c.Min(p => p.x), c.Min(p => p.z), c.Max(p => p.x), c.Max(p => p.z));
            }).ToList();
    }

    /// <summary>생성 바닥 윗면과 기존 타일이 겹치는 사각형 쌍 수와 겹친 넓이(m²).</summary>
    public static (int pairs, float area) CheckTileOverlap(List<Rect> floor, List<Rect> tiles)
    {
        int n = 0;
        float area = 0;
        foreach (var f in floor)
        foreach (var t in tiles)
        {
            float w = Mathf.Min(f.xMax, t.xMax) - Mathf.Max(f.xMin, t.xMin), h = Mathf.Min(f.yMax, t.yMax) - Mathf.Max(f.yMin, t.yMin);
            if (w > 1e-4f && h > 1e-4f) { n++; area += w * h; }
        }
        return (n, area);
    }

    /// <summary>
    /// 이음매 검사. 바깥 범위 둘레를 1cm 간격으로 훑어 안쪽 점이 바닥도 외곽선(여유 포함)도 아니면 틈으로 센다(그중 바깥이 기존 타일인 곳 = 타일 이음매 틈).
    /// 바깥 범위 안 칸 중 바닥도 외곽선도 아닌 칸은 <see cref="FloorGrid.HoleCells"/>(외곽선이 둘러싼 발사대 밑)로 따로 센다.
    /// </summary>
    public static (int samples, int gaps, int tileSeamGaps) CheckSeams(FloorGrid g, List<Rect> floor, List<Rect> tiles)
    {
        const float step = 0.01f, off = 0.005f;
        int samples = 0, gaps = 0, tileGaps = 0;
        bool InOuter(Vector2 p) => OuterRegion.Any(r => r.Contains(p));
        foreach (var r in OuterRegion)
        {
            var edges = new[]
            {
                (a: new Vector2(r.xMin, r.yMin), b: new Vector2(r.xMax, r.yMin), n: Vector2.down),
                (a: new Vector2(r.xMin, r.yMax), b: new Vector2(r.xMax, r.yMax), n: Vector2.up),
                (a: new Vector2(r.xMin, r.yMin), b: new Vector2(r.xMin, r.yMax), n: Vector2.left),
                (a: new Vector2(r.xMax, r.yMin), b: new Vector2(r.xMax, r.yMax), n: Vector2.right),
            };
            foreach (var (a, b, n) in edges)
            {
                int count = Mathf.RoundToInt(Vector2.Distance(a, b) / step);
                for (int i = 0; i < count; i++)
                {
                    var p = Vector2.Lerp(a, b, (i + 0.5f) / count);
                    var pin = p - n * off;
                    var pout = p + n * off;
                    if (InOuter(pout) || !InOuter(pin)) continue; // 두 사각형이 맞닿은 안쪽 변
                    samples++;
                    int c = g.CellAt(pin);
                    bool covered = floor.Any(f => f.Contains(pin)) || (c >= 0 && g.Blocked[c]);
                    if (covered) continue;
                    gaps++;
                    if (tiles.Any(t => t.Contains(pout))) tileGaps++;
                }
            }
        }
        return (samples, gaps, tileGaps);
    }

    public struct CheckResult
    {
        public int Overlaps, Pairs, BlockedFloorCells, Tiles, TilePairs, SeamSamples, SeamGaps, TileSeamGaps, HoleCells, RailingUncoveredCells;
        public float MinGap, TileArea, RailingFloorArea;

        public override string ToString() =>
            $"외곽선 겹침 {Overlaps}, 최소 틈 {MinGap:F4}m (검사 쌍 {Pairs}), 막힌 칸(오목부 포함) 위 바닥 칸 {BlockedFloorCells} / " +
            $"기존 타일 {Tiles}개와 겹침 {TilePairs}쌍 {TileArea:F4}m² / " +
            $"둘레 이음매 틈 {SeamGaps} (그중 타일 경계 {TileSeamGaps}, 표본 {SeamSamples}) / 발사대 밑 빈 칸 {HoleCells} / " +
            $"난간 플랫폼 경계 북쪽 바닥 {RailingFloorArea:F3}m², 못 덮은 칸 {RailingUncoveredCells}";
    }

    /// <summary>현재 로비 프리팹의 생성 바닥을 침범·타일 겹침·이음매로 검사한다. 바닥 오브젝트가 없으면 null.</summary>
    public static CheckResult? Check()
    {
        var root = PrefabUtility.LoadPrefabContents(LobbyPrefabPath);
        try
        {
            var floor = root.transform.Find(FloorObjectName);
            if (floor == null) return null;
            var sil = CollectSilhouette(root.transform);
            var mesh = floor.GetComponent<MeshFilter>().sharedMesh;
            var meshToRoot = root.transform.worldToLocalMatrix * floor.localToWorldMatrix;
            var res = new CheckResult();
            (res.Overlaps, res.MinGap, res.Pairs) = CheckIntrusion(mesh, meshToRoot, sil);
            var grid = Rasterize(sil);
            var rects = TopRects(mesh, meshToRoot);
            var tiles = ExistingTiles(root.transform);
            res.Tiles = tiles.Count;
            (res.TilePairs, res.TileArea) = CheckTileOverlap(rects, tiles);
            (res.SeamSamples, res.SeamGaps, res.TileSeamGaps) = CheckSeams(grid, rects, tiles);
            res.HoleCells = grid.HoleCells;
            // 행 채움으로 막은 오목부(삼각형 밖)까지 포함한 막힌 칸 위 바닥 칸 수.
            foreach (var r in rects)
            for (float z = r.yMin + Cell * 0.5f; z < r.yMax; z += Cell)
            for (float x = r.xMin + Cell * 0.5f; x < r.xMax; x += Cell)
            {
                int c = grid.CellAt(new Vector2(x, z));
                if (c < 0 || grid.Blocked[c]) res.BlockedFloorCells++;
            }
            // 난간 플랫폼 경계(+여유) 북쪽, 바깥 범위 안은 전부 바닥이어야 한다.
            float cutRoot = RailingCutRootZ(root.transform);
            for (int z = 0; z < grid.Height; z++)
            for (int x = 0; x < grid.Width; x++)
            {
                var p = new Vector2(grid.Bounds.xMin + (x + 0.5f) * Cell, grid.Bounds.yMin + (z + 0.5f) * Cell);
                if (p.y > cutRoot + Margin + Cell && OuterRegion.Any(o => o.Contains(p)) && !grid.Floor[z * grid.Width + x]) res.RailingUncoveredCells++;
            }
            foreach (var r in rects)
                res.RailingFloorArea += r.width * Mathf.Max(0, r.yMax - Mathf.Max(r.yMin, cutRoot));
            return res;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>현재 로비 프리팹으로 침범·타일 겹침·이음매 검사 요약 문자열(메뉴 실행 뒤 확인용).</summary>
    public static string Verify() => Check()?.ToString() ?? "바닥 오브젝트 없음";
}
