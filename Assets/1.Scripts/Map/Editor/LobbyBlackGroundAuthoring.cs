using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 로비 바깥 빈 공간을 가리는 검정 바닥을 만든다 — **로비 자리는 뚫는다** (2026-10-23 사용자 요청).
///
/// 참고: MapScene 의 env_black_tutorial — 튜토리얼 방·통로 모양대로 구멍이 뚫린 큰 판 + Black.mat.
/// 로비 판 높이는 벽 윗면 바로 아래(DefaultPlaneY, 사용자 조정값) — 바깥벽 밑으로 빈 공간이 비치지 않게.
/// 구멍 = 로비 건축물(PF_Lobby_Architecture_v015)을 위에서 내려다본 외곽선. **오차 없이 맞춘다**(사용자 요청: 구멍 크기 = 로비 외곽).
/// 방법: 건축 메시 중 보행면 근처 높이에 걸치는 삼각형(위에서 봐서 넓이가 있는 것 = 바닥·벽 윗면 등)을 모은다 →
///       그 꼭짓점의 X·Z 좌표(mm 단위)를 그대로 격자선으로 쓰는 불균등 격자를 만든다(벽 면이 격자선과 정확히 겹친다) →
///       삼각형이 칸 **안쪽**과 겹치면 로비 칸 → 격자 테두리에서 채워 들어간 칸 = 바깥, 나머지 = 구멍(안쪽 마당 포함).
///       바깥 칸을 사각형으로 합쳐 메시를 만들고, 격자 밖은 사방 Margin 까지 큰 띠 4장. 사각형은 Overlap 만큼 겹친다.
/// 실행 뒤 콜라이더 레이캐스트로 구멍 테두리 전체를 실측해 오차를 콘솔에 남긴다.
/// 메시는 MeshPath 에셋으로 저장(다시 돌리면 같은 에셋을 고쳐 써 guid 유지). 콜라이더 없음·그림자 드리우기 끔.
/// 3.LobbyScene 을 활성 씬으로 두고 실행(Play 정지). 씬 저장은 직접 한다.
/// </summary>
static class LobbyBlackGroundAuthoring
{
    const string LobbyRootName = "PF_Lobby_Complete_v016";
    const string ArchitectureName = "PF_Lobby_Architecture_v015";
    const string ObjectName = "Env_Black_Outside";
    const string MaterialPath = "Assets/3.Materials/~Others/BaseColor/Black.mat";
    const string MeshPath = "Assets/0.Scenes/MainFlow/3.LobbyScene/Env_Black_Outside_Mesh.asset";

    /// <summary>로비 보행면 높이 (스폰 지점 레이캐스트로 확인: y = 0).</summary>
    const float FloorTopY = 0f;
    /// <summary>판 높이 기본값 = 벽 윗면(2.2) 바로 아래. 사용자가 10-23 직접 맞춘 값.
    /// 보행면 아래(−0.5)에 두면 비스듬한 게임 카메라(61°)에서 바깥벽 밑(벽 0.11m 돌출 아래·바닥판 밑 −0.3)으로 빈 공간 띠가 보인다.
    /// 오브젝트가 이미 있으면 그 높이를 그대로 쓴다(사용자 조정 존중).</summary>
    const float DefaultPlaneY = 2.165f;
    /// <summary>외곽선 판정 높이 구간 = 보행면 기준 [BandMin, BandMax] — 판 높이와 무관하게 바닥(−0.3~0)과 벽 윗면(2.2)을 모두 담는다.</summary>
    const float BandMin = -1f;
    const float BandMax = 3f;
    /// <summary>위에서 본 넓이가 이보다 작은 삼각형(세로 벽면 등)은 외곽선 판정에서 뺀다 — mm² 의 2배(외적) 기준.</summary>
    const double MinCross = 2.0;
    /// <summary>칸 안쪽 판정 여유(mm). 격자선에 닿기만 한 삼각형은 그 칸을 차지하지 않는다.</summary>
    const double Eps = 0.25;
    /// <summary>사각형끼리·벽 아래로 이만큼(m) 겹친다 — T자 이음새 1픽셀 틈과 벽 밑 틈을 막는다. 불투명 단색이라 안 보인다.</summary>
    const float Overlap = 0.005f;
    const float GridPad = 3f;
    const float Margin = 150f;
    /// <summary>실측 간격(m)과 탐색 범위(m).</summary>
    const float ProbeStep = 0.5f;
    const float ProbeSearch = 0.5f;
    const string Tag = "[LobbyBlackGround]";

    struct Tri { public int Ax, Az, Bx, Bz, Cx, Cz; }

    [MenuItem("Tools/Map/Authoring/로비 바깥 검정 바닥 만들기")]
    static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError($"{Tag} Play 모드를 먼저 정지하고 실행.");
            return;
        }
        var scene = EditorSceneManager.GetActiveScene();
        Transform lobby = null;
        foreach (GameObject go in scene.GetRootGameObjects())
        {
            lobby = FindDeep(go.transform, LobbyRootName);
            if (lobby != null) break;
        }
        if (lobby == null) { Debug.LogError($"{Tag} 활성 씬 '{scene.name}' 에 {LobbyRootName} 이 없다."); return; }
        Transform arch = FindDeep(lobby, ArchitectureName);
        if (arch == null) { Debug.LogError($"{Tag} {LobbyRootName} 아래에 {ArchitectureName} 이 없다."); return; }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null) { Debug.LogError($"{Tag} 머티리얼 없음: {MaterialPath}"); return; }

        Transform parent = lobby.parent; // Environment
        Transform existing = parent != null ? parent.Find(ObjectName) : null;
        float planeY = existing != null ? existing.position.y : DefaultPlaneY;
        float bandMin = FloorTopY + BandMin, bandMax = FloorTopY + BandMax;

        // ── 1. 건축 삼각형 모으기 (mm 정수 좌표) ──
        var tris = new List<Tri>();
        int usedParts = 0, boxFallback = 0;
        foreach (MeshFilter mf in arch.GetComponentsInChildren<MeshFilter>(false))
        {
            var mr = mf.GetComponent<MeshRenderer>();
            if (mr == null || !mr.enabled || mf.sharedMesh == null) continue;
            Bounds rb = mr.bounds;
            if (rb.max.y < bandMin || rb.min.y > bandMax) continue;
            usedParts++;
            if (!CollectTris(mf, bandMin, bandMax, tris))
            {
                boxFallback++;
                int x0 = Mm(rb.min.x), x1 = Mm(rb.max.x), z0 = Mm(rb.min.z), z1 = Mm(rb.max.z);
                tris.Add(new Tri { Ax = x0, Az = z0, Bx = x0, Bz = z1, Cx = x1, Cz = z1 });
                tris.Add(new Tri { Ax = x0, Az = z0, Bx = x1, Bz = z1, Cx = x1, Cz = z0 });
            }
        }
        if (tris.Count == 0) { Debug.LogError($"{Tag} 외곽선을 만들 건축 삼각형이 없다."); return; }

        // ── 2. 꼭짓점 좌표 = 격자선 ──
        var xsSet = new HashSet<int>();
        var zsSet = new HashSet<int>();
        int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
        foreach (Tri t in tris)
        {
            xsSet.Add(t.Ax); xsSet.Add(t.Bx); xsSet.Add(t.Cx);
            zsSet.Add(t.Az); zsSet.Add(t.Bz); zsSet.Add(t.Cz);
            minX = Math.Min(minX, Math.Min(t.Ax, Math.Min(t.Bx, t.Cx))); maxX = Math.Max(maxX, Math.Max(t.Ax, Math.Max(t.Bx, t.Cx)));
            minZ = Math.Min(minZ, Math.Min(t.Az, Math.Min(t.Bz, t.Cz))); maxZ = Math.Max(maxZ, Math.Max(t.Az, Math.Max(t.Bz, t.Cz)));
        }
        int pad = Mm(GridPad);
        xsSet.Add(minX - pad); xsSet.Add(maxX + pad);
        zsSet.Add(minZ - pad); zsSet.Add(maxZ + pad);
        int[] X = Sorted(xsSet), Z = Sorted(zsSet);
        int nx = X.Length - 1, nz = Z.Length - 1;

        // ── 3. 로비 칸 찍기 ──
        var solid = new bool[nx, nz];
        foreach (Tri t in tris) Raster(t, X, Z, solid);

        // ── 4. 바깥 = 테두리에서 채워 들어간 칸, 나머지 = 구멍 ──
        bool[,] outside = FloodOutside(solid);
        var hole = new bool[nx, nz];
        double holeArea = 0;
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
                if (!outside[i, j]) { hole[i, j] = true; holeArea += (X[i + 1] - X[i]) * 1e-3 * (Z[j + 1] - Z[j]) * 1e-3; }

        // ── 5. 메시 ──
        var rects = new List<Rect>();
        Greedy(X, Z, hole, rects);
        float gx0 = X[0] * 1e-3f, gx1 = X[nx] * 1e-3f, gz0 = Z[0] * 1e-3f, gz1 = Z[nz] * 1e-3f;
        float cx = (minX + maxX) * 0.5e-3f, cz = (minZ + maxZ) * 0.5e-3f;
        float hx = (maxX - minX) * 0.5e-3f + Margin, hz = (maxZ - minZ) * 0.5e-3f + Margin;
        float rx0 = cx - hx, rx1 = cx + hx, rz0 = cz - hz, rz1 = cz + hz;
        rects.Add(Rect.MinMaxRect(rx0, rz0, rx1, gz0)); // 아래
        rects.Add(Rect.MinMaxRect(rx0, gz1, rx1, rz1)); // 위
        rects.Add(Rect.MinMaxRect(rx0, gz0, gx0, gz1)); // 왼쪽
        rects.Add(Rect.MinMaxRect(gx1, gz0, rx1, gz1)); // 오른쪽

        // ── 6. 오브젝트 ──
        GameObject obj;
        if (existing != null) obj = existing.gameObject;
        else
        {
            obj = new GameObject(ObjectName);
            Undo.RegisterCreatedObjectUndo(obj, "Lobby Black Ground");
            if (parent != null) obj.transform.SetParent(parent, false);
            else if (obj.scene != scene) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(obj, scene);
        }
        foreach (Collider c in obj.GetComponents<Collider>()) Undo.DestroyObjectImmediate(c);
        // Unity 오브젝트는 '가짜 null' 이 있어 ?? 대신 == null 로 검사한다.
        var filter = obj.GetComponent<MeshFilter>();
        if (filter == null) filter = Undo.AddComponent<MeshFilter>(obj);
        var renderer = obj.GetComponent<MeshRenderer>();
        if (renderer == null) renderer = Undo.AddComponent<MeshRenderer>(obj);

        Undo.RecordObject(obj.transform, "Lobby Black Ground");
        obj.transform.SetPositionAndRotation(new Vector3(cx, planeY, cz), Quaternion.identity);
        obj.transform.localScale = Vector3.one;

        Mesh mesh = WriteMesh(rects, planeY, obj.transform);

        Undo.RecordObject(filter, "Lobby Black Ground");
        filter.sharedMesh = mesh;
        Undo.RecordObject(renderer, "Lobby Black Ground");
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;

        EditorSceneManager.MarkSceneDirty(scene);

        // ── 7. 보고 ──
        var sb = new StringBuilder();
        sb.AppendLine($"{Tag} 판 y={planeY:0.00}, 건축 외곽 X {minX * 1e-3f:0.000}~{maxX * 1e-3f:0.000} / Z {minZ * 1e-3f:0.000}~{maxZ * 1e-3f:0.000}, " +
                      $"구멍 {holeArea:0.0}㎡, 사각형 {rects.Count}개, 정점 {mesh.vertexCount}개");
        sb.AppendLine($"  건축 조각 {usedParts}개 (메시 못 읽어 AABB 대체 {boxFallback}개), 삼각형 {tris.Count}개, 격자 {nx}×{nz} (꼭짓점 좌표 격자)");
        Probe(sb, lobby, X, Z, hole, planeY);
        AppendOverhang(sb, lobby, arch, X, Z, hole, planeY);
        sb.AppendLine("  미리보기(1m 한 칸, 위=+Z, '.'=구멍, '#'=검정):");
        AppendPreview(sb, X, Z, hole);
        Debug.Log(sb.ToString());
    }

    // ───────── 삼각형 ─────────

    static int Mm(float m) => Mathf.RoundToInt(m * 1000f);

    static int[] Sorted(HashSet<int> set)
    {
        var a = new int[set.Count];
        set.CopyTo(a);
        Array.Sort(a);
        return a;
    }

    /// <summary>높이 구간에 걸치고 위에서 봐서 넓이가 있는 삼각형을 mm 정수 XZ 로 모은다. 메시를 못 읽으면 false.</summary>
    static bool CollectTris(MeshFilter mf, float bandMin, float bandMax, List<Tri> output)
    {
        Mesh m = mf.sharedMesh;
        Vector3[] local;
        try { local = m.vertices; }
        catch { return false; }
        if (local == null || local.Length == 0) return false;

        Matrix4x4 l2w = mf.transform.localToWorldMatrix;
        var w = new Vector3[local.Length];
        for (int i = 0; i < local.Length; i++) w[i] = l2w.MultiplyPoint3x4(local[i]);

        for (int s = 0; s < m.subMeshCount; s++)
        {
            if (m.GetTopology(s) != MeshTopology.Triangles) continue;
            int[] idx = m.GetTriangles(s);
            for (int k = 0; k + 2 < idx.Length; k += 3)
            {
                Vector3 a = w[idx[k]], b = w[idx[k + 1]], c = w[idx[k + 2]];
                float ymin = Mathf.Min(a.y, Mathf.Min(b.y, c.y)), ymax = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
                if (ymax < bandMin || ymin > bandMax) continue;
                var t = new Tri { Ax = Mm(a.x), Az = Mm(a.z), Bx = Mm(b.x), Bz = Mm(b.z), Cx = Mm(c.x), Cz = Mm(c.z) };
                long cross = (long)(t.Bx - t.Ax) * (t.Cz - t.Az) - (long)(t.Bz - t.Az) * (t.Cx - t.Ax);
                if (Math.Abs(cross) < MinCross) continue; // 세로 면(위에서 보면 선)
                output.Add(t);
            }
        }
        return true;
    }

    /// <summary>a[i] ≤ v 인 마지막 i (없으면 -1).</summary>
    static int LastLE(int[] a, int v)
    {
        int lo = 0, hi = a.Length - 1, r = -1;
        while (lo <= hi) { int mid = (lo + hi) >> 1; if (a[mid] <= v) { r = mid; lo = mid + 1; } else hi = mid - 1; }
        return r;
    }

    /// <summary>a[i] &lt; v 인 마지막 i (없으면 -1).</summary>
    static int LastLT(int[] a, int v)
    {
        int lo = 0, hi = a.Length - 1, r = -1;
        while (lo <= hi) { int mid = (lo + hi) >> 1; if (a[mid] < v) { r = mid; lo = mid + 1; } else hi = mid - 1; }
        return r;
    }

    /// <summary>삼각형이 칸 안쪽(경계에서 Eps 만큼 들어간 영역)과 겹치는 칸을 로비로 찍는다.</summary>
    static void Raster(Tri t, int[] X, int[] Z, bool[,] solid)
    {
        int minX = Math.Min(t.Ax, Math.Min(t.Bx, t.Cx)), maxX = Math.Max(t.Ax, Math.Max(t.Bx, t.Cx));
        int minZ = Math.Min(t.Az, Math.Min(t.Bz, t.Cz)), maxZ = Math.Max(t.Az, Math.Max(t.Bz, t.Cz));
        int nx = X.Length - 1, nz = Z.Length - 1;
        int i0 = Math.Max(0, LastLE(X, minX)), i1 = Math.Min(nx - 1, LastLT(X, maxX));
        int j0 = Math.Max(0, LastLE(Z, minZ)), j1 = Math.Min(nz - 1, LastLT(Z, maxZ));
        for (int i = i0; i <= i1; i++)
        {
            double x0 = X[i] + Eps, x1 = X[i + 1] - Eps;
            if (x1 <= x0 || maxX <= x0 || minX >= x1) continue;
            for (int j = j0; j <= j1; j++)
            {
                if (solid[i, j]) continue;
                double z0 = Z[j] + Eps, z1 = Z[j + 1] - Eps;
                if (z1 <= z0 || maxZ <= z0 || minZ >= z1) continue;
                if (Separated(t.Ax, t.Az, t.Bx, t.Bz, t, x0, z0, x1, z1) ||
                    Separated(t.Bx, t.Bz, t.Cx, t.Cz, t, x0, z0, x1, z1) ||
                    Separated(t.Cx, t.Cz, t.Ax, t.Az, t, x0, z0, x1, z1))
                    continue;
                solid[i, j] = true;
            }
        }
    }

    /// <summary>변 (px,pz)→(qx,qz) 의 법선 축에서 삼각형과 칸이 떨어져 있으면 true (닿기만 해도 떨어진 것으로 본다).</summary>
    static bool Separated(int px, int pz, int qx, int qz, Tri t, double x0, double z0, double x1, double z1)
    {
        double ex = qx - px, ez = qz - pz;
        if (ex == 0 && ez == 0) return false;
        double nx = -ez, nz = ex;
        double ta = nx * t.Ax + nz * t.Az, tb = nx * t.Bx + nz * t.Bz, tc = nx * t.Cx + nz * t.Cz;
        double tMin = Math.Min(ta, Math.Min(tb, tc)), tMax = Math.Max(ta, Math.Max(tb, tc));
        double p0 = nx * x0 + nz * z0, p1 = nx * x1 + nz * z0, p2 = nx * x0 + nz * z1, p3 = nx * x1 + nz * z1;
        double bMin = Math.Min(Math.Min(p0, p1), Math.Min(p2, p3)), bMax = Math.Max(Math.Max(p0, p1), Math.Max(p2, p3));
        return tMax <= bMin || bMax <= tMin;
    }

    /// <summary>격자 테두리에서 시작해 막히지 않은 칸을 4방향으로 채운다 = 바깥.</summary>
    static bool[,] FloodOutside(bool[,] blocked)
    {
        int nx = blocked.GetLength(0), nz = blocked.GetLength(1);
        var outside = new bool[nx, nz];
        var q = new Queue<Vector2Int>();
        void Push(int i, int j)
        {
            if (i < 0 || j < 0 || i >= nx || j >= nz || outside[i, j] || blocked[i, j]) return;
            outside[i, j] = true;
            q.Enqueue(new Vector2Int(i, j));
        }
        for (int i = 0; i < nx; i++) { Push(i, 0); Push(i, nz - 1); }
        for (int j = 0; j < nz; j++) { Push(0, j); Push(nx - 1, j); }
        while (q.Count > 0)
        {
            Vector2Int p = q.Dequeue();
            Push(p.x + 1, p.y); Push(p.x - 1, p.y); Push(p.x, p.y + 1); Push(p.x, p.y - 1);
        }
        return outside;
    }

    /// <summary>구멍이 아닌 칸을 가로로 늘인 뒤 같은 폭으로 세로로 늘여 사각형(m)으로 합친다.</summary>
    static void Greedy(int[] X, int[] Z, bool[,] hole, List<Rect> rects)
    {
        int nx = X.Length - 1, nz = Z.Length - 1;
        var used = new bool[nx, nz];
        for (int j = 0; j < nz; j++)
        {
            for (int i = 0; i < nx; i++)
            {
                if (hole[i, j] || used[i, j]) continue;
                int w = 1;
                while (i + w < nx && !hole[i + w, j] && !used[i + w, j]) w++;
                int h = 1;
                while (j + h < nz)
                {
                    bool ok = true;
                    for (int k = 0; k < w && ok; k++) ok = !hole[i + k, j + h] && !used[i + k, j + h];
                    if (!ok) break;
                    h++;
                }
                for (int a = 0; a < w; a++)
                    for (int b = 0; b < h; b++)
                        used[i + a, j + b] = true;
                rects.Add(Rect.MinMaxRect(X[i] * 1e-3f, Z[j] * 1e-3f, X[i + w] * 1e-3f, Z[j + h] * 1e-3f));
            }
        }
    }

    /// <summary>사각형들(XZ, 월드)을 위를 향한 판 메시로 만들어 MeshPath 에셋에 쓴다.</summary>
    static Mesh WriteMesh(List<Rect> rects, float y, Transform obj)
    {
        var verts = new List<Vector3>(rects.Count * 4);
        var uvs = new List<Vector2>(rects.Count * 4);
        var tris = new List<int>(rects.Count * 6);
        foreach (Rect r in rects)
        {
            if (r.width <= 0f || r.height <= 0f) continue;
            int v = verts.Count;
            float x0 = r.xMin - Overlap, x1 = r.xMax + Overlap, z0 = r.yMin - Overlap, z1 = r.yMax + Overlap;
            // 위에서 볼 때 시계 방향 (0,1,2)(0,2,3) → 법선 +Y
            AddVert(verts, uvs, obj, x0, y, z0);
            AddVert(verts, uvs, obj, x0, y, z1);
            AddVert(verts, uvs, obj, x1, y, z1);
            AddVert(verts, uvs, obj, x1, y, z0);
            tris.Add(v); tris.Add(v + 1); tris.Add(v + 2);
            tris.Add(v); tris.Add(v + 2); tris.Add(v + 3);
        }
        Vector3 up = obj.InverseTransformDirection(Vector3.up).normalized;
        var normals = new List<Vector3>(verts.Count);
        for (int i = 0; i < verts.Count; i++) normals.Add(up);

        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        bool isNew = mesh == null;
        if (isNew) mesh = new Mesh();
        else { Undo.RecordObject(mesh, "Lobby Black Ground"); mesh.Clear(); } // Ctrl+Z 로 Transform 과 함께 되돌린다
        mesh.name = "Env_Black_Outside_Mesh";
        mesh.indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        if (isNew) AssetDatabase.CreateAsset(mesh, MeshPath);
        else EditorUtility.SetDirty(mesh);
        AssetDatabase.SaveAssetIfDirty(mesh); // 다른 더티 에셋은 건드리지 않는다
        return mesh;
    }

    static void AddVert(List<Vector3> verts, List<Vector2> uvs, Transform obj, float x, float y, float z)
    {
        verts.Add(obj.InverseTransformPoint(new Vector3(x, y, z)));
        uvs.Add(new Vector2(x / 10f, z / 10f));
    }

    // ───────── 실측 ─────────

    /// <summary>
    /// 구멍 테두리를 따라 ProbeStep 간격으로, 테두리 1cm 안쪽(구멍)과 1cm 바깥(검정)에서 위→아래 레이캐스트.
    /// 안쪽에 로비 콜라이더가 없으면 = 빈 공간이 보이는 곳, 바깥에 로비가 있으면 = 구멍이 로비보다 작은 곳.
    /// 실패한 곳은 테두리에 수직으로 1cm 씩 더듬어 실제 로비 끝까지 거리(오차)를 잰다. 콜라이더 기준이라 렌더 메시와 다를 수 있다.
    /// </summary>
    static void Probe(StringBuilder sb, Transform lobby, int[] X, int[] Z, bool[,] hole, float planeY)
    {
        Physics.SyncTransforms();
        int nx = X.Length - 1, nz = Z.Length - 1;
        int samples = 0, voidCount = 0, smallCount = 0;
        float voidMax = 0f, smallMax = 0f;
        Vector3 voidAt = default, smallAt = default;

        void Check(float x, float z, float dx, float dz) // (dx,dz) = 구멍 → 검정 방향 단위벡터
        {
            samples++;
            if (!HitsLobby(lobby, x - dx * 0.01f, z - dz * 0.01f, planeY))
            {
                float e = Search(lobby, x, z, -dx, -dz, planeY, want: true);
                voidCount++;
                if (e > voidMax) { voidMax = e; voidAt = new Vector3(x, 0, z); }
            }
            if (HitsLobby(lobby, x + dx * 0.01f, z + dz * 0.01f, planeY))
            {
                float e = Search(lobby, x, z, dx, dz, planeY, want: false);
                smallCount++;
                if (e > smallMax) { smallMax = e; smallAt = new Vector3(x, 0, z); }
            }
        }

        // 세로 테두리 (X 격자선)
        for (int i = 0; i + 1 < nx; i++)
            for (int j = 0; j < nz; j++)
                if (hole[i, j] != hole[i + 1, j])
                {
                    float x = X[i + 1] * 1e-3f, za = Z[j] * 1e-3f, zb = Z[j + 1] * 1e-3f;
                    float dx = hole[i, j] ? 1f : -1f;
                    int n = Mathf.Max(1, Mathf.CeilToInt((zb - za) / ProbeStep));
                    for (int k = 0; k < n; k++) Check(x, za + (k + 0.5f) * (zb - za) / n, dx, 0f);
                }
        // 가로 테두리 (Z 격자선)
        for (int j = 0; j + 1 < nz; j++)
            for (int i = 0; i < nx; i++)
                if (hole[i, j] != hole[i, j + 1])
                {
                    float z = Z[j + 1] * 1e-3f, xa = X[i] * 1e-3f, xb = X[i + 1] * 1e-3f;
                    float dz = hole[i, j] ? 1f : -1f;
                    int n = Mathf.Max(1, Mathf.CeilToInt((xb - xa) / ProbeStep));
                    for (int k = 0; k < n; k++) Check(xa + (k + 0.5f) * (xb - xa) / n, z, 0f, dz);
                }

        sb.AppendLine($"  실측(콜라이더 레이캐스트, 테두리 {samples}곳): " +
                      $"빈 공간 보임 {voidCount}곳(최대 {Fmt(voidMax)}{(voidCount > 0 ? $" @({voidAt.x:0.00},{voidAt.z:0.00})" : "")}), " +
                      $"구멍이 로비보다 작음 {smallCount}곳(최대 {Fmt(smallMax)}{(smallCount > 0 ? $" @({smallAt.x:0.00},{smallAt.z:0.00})" : "")})");
    }

    static string Fmt(float e) => e >= ProbeSearch ? $"{ProbeSearch * 100:0}cm 이상" : $"{e * 100:0}cm";

    /// <summary>테두리에서 (dx,dz) 방향으로 1cm 씩 가며 로비 유무가 want 가 되는 첫 거리.</summary>
    static float Search(Transform lobby, float x, float z, float dx, float dz, float planeY, bool want)
    {
        for (float d = 0.01f; d <= ProbeSearch; d += 0.01f)
            if (HitsLobby(lobby, x + dx * d, z + dz * d, planeY) == want) return d;
        return ProbeSearch;
    }

    static readonly RaycastHit[] Hits = new RaycastHit[16];

    static bool HitsLobby(Transform lobby, float x, float z, float planeY)
    {
        // 판 높이와 무관하게 바닥 밑(판정 구간 아래끝)까지 — 문턱처럼 판보다 낮은 로비도 잡는다.
        float top = FloorTopY + 10f, bottom = FloorTopY + BandMin;
        int n = Physics.RaycastNonAlloc(new Vector3(x, top, z), Vector3.down, Hits, top - bottom, ~0, QueryTriggerInteraction.Ignore);
        for (int k = 0; k < n; k++)
            if (Hits[k].collider.transform.IsChildOf(lobby)) return true;
        return false;
    }

    /// <summary>건축 밖 로비 조각 중 검정 판 위로 삐져나오는 것(판보다 위면 판 위에 그대로 보인다 — 참고용).</summary>
    static void AppendOverhang(StringBuilder sb, Transform lobby, Transform arch, int[] X, int[] Z, bool[,] hole, float planeY)
    {
        var below = new List<string>();
        int over = 0;
        foreach (MeshRenderer mr in lobby.GetComponentsInChildren<MeshRenderer>(false))
        {
            if (mr.transform.IsChildOf(arch) || !mr.enabled) continue;
            Bounds b = mr.bounds;
            if (!OverlapsBlack(X, Z, hole, b)) continue;
            if (b.min.y < planeY - 0.01f) { if (below.Count < 10) below.Add(mr.name); }
            else over++;
        }
        sb.AppendLine($"  건축 밖 소품이 검정 판 위로 걸침(AABB 대략): {over}개" +
                      (below.Count > 0 ? $", ⚠ 판 아래로 내려가 가려질 수 있는 것: {string.Join(", ", below)}" : ", 판 아래로 내려가는 것 없음"));
    }

    static bool OverlapsBlack(int[] X, int[] Z, bool[,] hole, Bounds b)
    {
        int nx = X.Length - 1, nz = Z.Length - 1;
        int bx0 = Mm(b.min.x), bx1 = Mm(b.max.x), bz0 = Mm(b.min.z), bz1 = Mm(b.max.z);
        if (bx0 < X[0] || bz0 < Z[0] || bx1 > X[nx] || bz1 > Z[nz]) return true;
        int i0 = Math.Max(0, LastLE(X, bx0)), i1 = Math.Min(nx - 1, LastLT(X, bx1));
        int j0 = Math.Max(0, LastLE(Z, bz0)), j1 = Math.Min(nz - 1, LastLT(Z, bz1));
        for (int i = i0; i <= i1; i++)
            for (int j = j0; j <= j1; j++)
                if (!hole[i, j]) return true;
        return false;
    }

    static void AppendPreview(StringBuilder sb, int[] X, int[] Z, bool[,] hole)
    {
        int nx = X.Length - 1, nz = Z.Length - 1;
        int x0 = (int)Math.Floor(X[0] / 1000.0), x1 = (int)Math.Ceiling(X[nx] / 1000.0);
        int z0 = (int)Math.Floor(Z[0] / 1000.0), z1 = (int)Math.Ceiling(Z[nz] / 1000.0);
        for (int zm = z1 - 1; zm >= z0; zm--)
        {
            var line = new StringBuilder("  ");
            for (int xm = x0; xm < x1; xm++)
            {
                int i = LastLE(X, xm * 1000 + 500), j = LastLE(Z, zm * 1000 + 500);
                bool h = i >= 0 && i < nx && j >= 0 && j < nz && hole[i, j];
                line.Append(h ? '.' : '#');
            }
            sb.AppendLine(line.ToString());
        }
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t)
        {
            Transform r = FindDeep(c, name);
            if (r != null) return r;
        }
        return null;
    }
}
