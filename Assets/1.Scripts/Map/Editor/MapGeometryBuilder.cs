using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

// ZoneVolume 배치를 기반으로 고정 지형(바닥/외벽/통로)을 빌드한다.
//  - 바닥: 존 영역을 FloorTiles로 타일링 — 타일을 미세 스케일 보정해 영역을 '정확히' 채움
//  - 외벽: 존 둘레를 WallFences로 빈틈없이 두름(세그먼트 길이 보정) — 바닥 끝에 밀착
//  - 통로: MapCorridors(화이트리스트) 기준만 연결. 문 없이 뚫린 통로(바닥 스트립+측벽).
// 프리팹 피벗이 제각각이라 렌더러 바운즈 중심 정렬로 배치한다.
// 결과는 "MapGeometry" 루트 아래 — 빌드 후 씬 저장/프리팹화(Stage1) 대상.
public static class MapGeometryBuilder
{
    private const string RootName = "MapGeometry";

    private struct ZRect { public float minX, maxX, minZ, maxZ, y; }

    [MenuItem("VeyTrace/Map/Build Map Geometry (Floors+Walls+Corridors)")]
    public static void Build()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<MapPrefabCatalogSO>(MapEditorPaths.CatalogPath);
        if (catalog == null || catalog.FloorTiles.Count == 0 || catalog.WallFences.Count == 0)
        {
            Debug.LogWarning("[MapGeometry] 카탈로그의 바닥/벽 풀이 비어있음 — 먼저 'Populate Prefab Catalog' 실행.");
            return;
        }

        var volumes = new List<ZoneVolume>(Object.FindObjectsByType<ZoneVolume>(FindObjectsSortMode.None));
        if (volumes.Count == 0)
        {
            Debug.LogWarning("[MapGeometry] 씬에 ZoneVolume 없음.");
            return;
        }
        volumes.Sort((a, b) => MapCorridors.ZoneId(a).CompareTo(MapCorridors.ZoneId(b)));

        _footprintCache.Clear();

        DestroyExisting(); // Stage1 프리팹 인스턴스 안에 있으면 언팩 후 제거
        Transform root = new GameObject(RootName).transform;
        var stage1 = GameObject.Find("Stage1");
        if (stage1 != null) root.SetParent(stage1.transform, false);

        // 1) 통로 (공유 정의 — 스캐터/오버뷰 UI와 동일 계산)
        var corridors = MapCorridors.FindAll();

        // 화이트리스트인데 지오메트리상 연결 실패한 쌍 경고
        var matched = new HashSet<(int, int)>();
        foreach (var c in corridors)
            matched.Add(Key(MapCorridors.ZoneId(c.A), MapCorridors.ZoneId(c.B)));
        foreach (var pair in MapCorridors.Pairs)
            if (!matched.Contains(Key(pair.a, pair.b)))
                Debug.LogWarning($"[MapGeometry] 통로 쌍 ({pair.a},{pair.b}) — 인접 조건 불충족으로 연결 안 됨 (간격/겹침 확인).");

        int floors = 0, walls = 0;

        // 2) 존별 바닥 + 외벽
        foreach (var vol in volumes)
        {
            int variant = Mathf.Abs(MapCorridors.ZoneId(vol) - 1);
            GameObject floorPrefab = catalog.FloorTiles[variant % catalog.FloorTiles.Count];
            GameObject wallPrefab = catalog.WallFences[variant % catalog.WallFences.Count];

            Transform zoneParent = new GameObject($"Zone_{MapCorridors.ZoneId(vol)}").transform;
            zoneParent.SetParent(root, false);

            ZRect r = RectOf(vol);
            floors += TileFloor(r, floorPrefab, zoneParent);
            walls += BuildZoneWalls(vol, r, wallPrefab, corridors, zoneParent);
        }

        // 3) 통로 바닥 + 측벽 (문 없음 — 뚫린 길)
        Transform corrParent = new GameObject("Corridors").transform;
        corrParent.SetParent(root, false);
        foreach (var c in corridors)
        {
            if (c.Length < 0.5f) continue;

            int variant = Mathf.Abs(MapCorridors.ZoneId(c.A) - 1);
            GameObject floorPrefab = catalog.FloorTiles[variant % catalog.FloorTiles.Count];
            GameObject wallPrefab = catalog.WallFences[variant % catalog.WallFences.Count];

            ZRect cr;
            cr.y = c.Y;
            float half = MapCorridors.Width * 0.5f;
            if (c.AlongX) { cr.minX = c.Start; cr.maxX = c.End; cr.minZ = c.Center - half; cr.maxZ = c.Center + half; }
            else          { cr.minZ = c.Start; cr.maxZ = c.End; cr.minX = c.Center - half; cr.maxX = c.Center + half; }

            floors += TileFloor(cr, floorPrefab, corrParent);
            if (c.Length > 2f) walls += BuildCorridorSideWalls(c, wallPrefab, corrParent);
        }

        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        Debug.Log($"[MapGeometry] 완료 — 통로 {corridors.Count}/{MapCorridors.Pairs.Length} / 바닥 {floors} / 벽 {walls}. (씬 저장 필요)");
    }

    [MenuItem("VeyTrace/Map/Clear Map Geometry")]
    public static void Clear()
    {
        DestroyExisting();
        Debug.Log("[MapGeometry] 제거 완료.");
    }

    // 기존 MapGeometry 제거 — 프리팹 인스턴스(Stage1) 내부면 언팩 후 삭제
    private static void DestroyExisting()
    {
        var old = GameObject.Find(RootName);
        if (old == null) return;

        if (PrefabUtility.IsPartOfPrefabInstance(old))
        {
            var outermost = PrefabUtility.GetOutermostPrefabInstanceRoot(old);
            if (outermost != null)
                PrefabUtility.UnpackPrefabInstance(outermost, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
        }
        Object.DestroyImmediate(old);
    }

    private static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);

    // ---------- 바닥 ----------

    // 타일을 미세 스케일 보정해 영역을 정확히 채운다 (넘침/빈틈 없음 → 벽이 바닥 끝에 밀착)
    private static int TileFloor(ZRect r, GameObject prefab, Transform parent)
    {
        Vector3 fp = FootprintOf(prefab);
        float tx = Mathf.Max(0.5f, fp.x), tz = Mathf.Max(0.5f, fp.z);
        float w = r.maxX - r.minX, d = r.maxZ - r.minZ;
        if (w <= 0.01f || d <= 0.01f) return 0;

        int cols = Mathf.Max(1, Mathf.RoundToInt(w / tx));
        int rows = Mathf.Max(1, Mathf.RoundToInt(d / tz));
        float stepX = w / cols, stepZ = d / rows;
        var scaleMul = new Vector3(stepX / tx, 1f, stepZ / tz);

        int count = 0;
        for (int cx = 0; cx < cols; cx++)
            for (int cz = 0; cz < rows; cz++)
            {
                PlaceAligned(prefab,
                    new Vector2(r.minX + stepX * (cx + 0.5f), r.minZ + stepZ * (cz + 0.5f)),
                    r.y, Quaternion.identity, parent, scaleMul);
                count++;
            }
        return count;
    }

    // ---------- 벽 ----------

    private static int BuildZoneWalls(ZoneVolume vol, ZRect r, GameObject wall,
        List<MapCorridors.Corridor> corridors, Transform parent)
    {
        // 변마다 통로 입구 중심 수집
        var north = new List<float>(); var south = new List<float>();
        var east = new List<float>(); var west = new List<float>();
        foreach (var c in corridors)
        {
            if (c.AlongX)
            {
                if (c.A == vol) east.Add(c.Center);
                else if (c.B == vol) west.Add(c.Center);
            }
            else
            {
                if (c.A == vol) north.Add(c.Center);
                else if (c.B == vol) south.Add(c.Center);
            }
        }

        int count = 0;
        count += WallLine(wall, r.maxZ, true, r.minX, r.maxX, north, r.y, parent);  // 북
        count += WallLine(wall, r.minZ, true, r.minX, r.maxX, south, r.y, parent);  // 남
        count += WallLine(wall, r.maxX, false, r.minZ, r.maxZ, east, r.y, parent);  // 동
        count += WallLine(wall, r.minX, false, r.minZ, r.maxZ, west, r.y, parent);  // 서
        return count;
    }

    private static int BuildCorridorSideWalls(MapCorridors.Corridor c, GameObject wall, Transform parent)
    {
        float y = c.Y;
        float half = MapCorridors.Width * 0.5f;
        var noMouths = new List<float>();
        int count = 0;
        if (c.AlongX)
        {
            count += WallLine(wall, c.Center + half, true, c.Start, c.End, noMouths, y, parent);
            count += WallLine(wall, c.Center - half, true, c.Start, c.End, noMouths, y, parent);
        }
        else
        {
            count += WallLine(wall, c.Center + half, false, c.Start, c.End, noMouths, y, parent);
            count += WallLine(wall, c.Center - half, false, c.Start, c.End, noMouths, y, parent);
        }
        return count;
    }

    // 한 직선 위에 벽을 빈틈없이 깐다. 입구(mouth) 구간만 비운다(문 없음 — 뚫린 통로).
    // 각 구간을 세그먼트 길이 보정으로 정확히 채워 끝/모서리에 밀착시킨다.
    private static int WallLine(GameObject wall, float lineCoord, bool alongX,
        float from, float to, List<float> mouthCenters, float y, Transform parent)
    {
        // 입구를 빼고 남는 벽 구간들 계산
        var intervals = new List<Vector2>();
        mouthCenters.Sort();
        float cursor = from;
        float half = MapCorridors.Width * 0.5f;
        foreach (float m in mouthCenters)
        {
            if (m - half > cursor) intervals.Add(new Vector2(cursor, m - half));
            cursor = Mathf.Max(cursor, m + half);
        }
        if (to > cursor) intervals.Add(new Vector2(cursor, to));

        Vector3 fp = FootprintOf(wall);
        bool axisX = fp.x >= fp.z;                       // 프리팹의 긴 축
        float unit = Mathf.Max(0.5f, Mathf.Max(fp.x, fp.z));
        float yaw = alongX ? (axisX ? 0f : 90f) : (axisX ? 90f : 0f);
        Quaternion rot = Quaternion.Euler(0f, yaw, 0f);

        int count = 0;
        foreach (var iv in intervals)
        {
            float len = iv.y - iv.x;
            if (len < 0.4f) continue;

            int segs = Mathf.Max(1, Mathf.RoundToInt(len / unit));
            float step = len / segs;
            float lengthScale = step / unit; // 세그먼트 길이 보정 → 구간 정확히 채움
            Vector3 scaleMul = axisX ? new Vector3(lengthScale, 1f, 1f) : new Vector3(1f, 1f, lengthScale);

            for (int i = 0; i < segs; i++)
            {
                float center = iv.x + step * (i + 0.5f);
                PlaceAligned(wall,
                    alongX ? new Vector2(center, lineCoord) : new Vector2(lineCoord, center),
                    y, rot, parent, scaleMul);
                count++;
            }
        }
        return count;
    }

    // ---------- 유틸 ----------

    private static ZRect RectOf(ZoneVolume v)
    {
        Bounds b = v.GetBounds();
        return new ZRect { minX = b.min.x, maxX = b.max.x, minZ = b.min.z, maxZ = b.max.z, y = v.transform.position.y };
    }

    // 스케일 보정 후, 렌더러 바운즈 중심(XZ)이 목표점에 오도록 배치 (피벗 무관)
    private static GameObject PlaceAligned(GameObject prefab, Vector2 targetXZ, float y,
        Quaternion rot, Transform parent, Vector3? scaleMul = null)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        if (scaleMul.HasValue)
            go.transform.localScale = Vector3.Scale(go.transform.localScale, scaleMul.Value);
        go.transform.SetPositionAndRotation(new Vector3(targetXZ.x, y, targetXZ.y), rot);

        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length > 0)
        {
            Bounds b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            go.transform.position += new Vector3(targetXZ.x - b.center.x, 0f, targetXZ.y - b.center.z);
        }
        return go;
    }

    private static readonly Dictionary<GameObject, Vector3> _footprintCache = new Dictionary<GameObject, Vector3>();

    private static Vector3 FootprintOf(GameObject prefab)
    {
        if (_footprintCache.TryGetValue(prefab, out var cached)) return cached;

        var temp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        var rends = temp.GetComponentsInChildren<Renderer>();
        Bounds b = rends.Length > 0 ? rends[0].bounds : new Bounds(Vector3.zero, Vector3.one);
        foreach (var rend in rends) b.Encapsulate(rend.bounds);
        Object.DestroyImmediate(temp);

        _footprintCache[prefab] = b.size;
        return b.size;
    }
}
