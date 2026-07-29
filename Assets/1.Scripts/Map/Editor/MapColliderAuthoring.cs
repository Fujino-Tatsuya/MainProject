using UnityEditor;
using UnityEngine;

// 일회성 저작 도구 — 맵 프리팹의 바닥/벽/경사로 메시에 MeshCollider 부착 (PLAN 2026-07-21 §1).
//
// 배경: 기존 맵 프리팹(Stage1/WallPrefabs/Zoneprefab)의 바닥·벽은 fbx 모델 인스턴스가 아니라
// "언팩된 사본"(MeshFilter+MeshRenderer만)이라, fbx 임포터 addColliders를 켜도 전파되지 않는다.
// 그래서 프리팹을 직접 순회하며 이름이 floor/wall/hallway 계열인 메시에 MeshCollider를 붙인다.
// (소품/장식은 이름 필터로 제외 — 통행 방해 방지. fbx addColliders는 신규 배치용으로 별도 유지.)
//
// 2026-07-28: slope/stairs 추가. Play 검증에서 경사로를 밟으면 아래로 떨어지는 것이 확인됐다.
// 경사로·계단도 바닥과 같은 "밟고 지나가는" 지오메트리인데 이름 필터에서 빠져 있었다.
// 대상 17개(slope 12 · stairs 5) — ZoneL_typeA/B · ZoneM_typeA/B · Zone_typeQuest01/02.
public static class MapColliderAuthoring
{
    const string TargetFolder = "Assets/2.Prefabs/Map";
    static readonly string[] NameKeywords = { "floor", "wall", "hallway", "slope", "stair" };

    [MenuItem("Tools/Map/Authoring/Add Floor+Wall MeshColliders")]
    public static void AddFloorWallColliders()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { TargetFolder });
        int prefabsChanged = 0, collidersAdded = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            int added = 0;

            try
            {
                foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    // 중첩 프리팹 인스턴스 내부는 건너뜀 — 원본 프리팹이 이 스캔에 포함되므로
                    // 원본에서 1회만 부착(인스턴스 오버라이드 오염 방지).
                    if (PrefabUtility.IsPartOfPrefabInstance(mf.gameObject))
                        continue;

                    if (!IsFloorOrWall(mf))
                        continue;

                    // 계단은 이 패스에서 제외한다 — 별도 메뉴(Rebuild Stair Ramp Colliders)가
                    // 경사면 BoxCollider로 대체한다. 여기서 MeshCollider를 붙이면 램프와 겹쳐
                    // 턱이 되살아난다.
                    if (IsStair(mf))
                        continue;

                    // 이미 어떤 콜라이더든 있으면 유지(수동 저작 존중).
                    if (mf.GetComponent<Collider>() != null)
                        continue;

                    mf.gameObject.AddComponent<MeshCollider>(); // sharedMesh는 MeshFilter에서 자동 참조
                    added++;
                }

                if (added > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    prefabsChanged++;
                    collidersAdded += added;
                    Debug.Log($"[MapColliderAuthoring] {path} — MeshCollider {added}개 부착.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        Debug.Log($"[MapColliderAuthoring] 완료 — 프리팹 {prefabsChanged}개 수정, 콜라이더 총 {collidersAdded}개 부착.");
    }

    // 오브젝트명 · 메시명 · 머티리얼명 중 하나라도 키워드를 포함하면 대상.
    static bool IsFloorOrWall(MeshFilter mf)
    {
        if (MatchesKeyword(mf.gameObject.name))
            return true;
        if (mf.sharedMesh != null && MatchesKeyword(mf.sharedMesh.name))
            return true;

        // 아트가 바닥 메시를 Cube.209 처럼 무의미한 이름으로 내보내는 경우가 있어
        // 이름만으로는 바닥과 소품을 가르지 못한다. 머티리얼로 한 번 더 판정한다.
        // (MA_floor_urethane → 바닥으로 잡히고, MA_prop01 환풍구류는 그대로 제외된다.)
        MeshRenderer renderer = mf.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            foreach (Material mat in renderer.sharedMaterials)
                if (mat != null && MatchesKeyword(mat.name))
                    return true;
        }

        return false;
    }

    const string RampChildName = "__StairRampCollider";

    // 계단은 콜라이더를 계단 형상에 맞추지 않고 경사면(램프) 하나로 대체한다.
    // 보이는 건 계단 그대로지만 물리적으로는 slope와 동일해져서, stepOffset 보정이 없는
    // Rigidbody 이동으로도 걸어 올라갈 수 있다. 볼록 승격만으로는 부족했다(계단 밑단
    // 수직면이 남아 캡슐이 걸린다).
    [MenuItem("Tools/Map/Authoring/Rebuild Stair Ramp Colliders")]
    public static void RebuildStairRamps()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { TargetFolder });
        int prefabsChanged = 0, rampsBuilt = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            int built = 0;

            try
            {
                foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (PrefabUtility.IsPartOfPrefabInstance(mf.gameObject))
                        continue;
                    if (!IsStair(mf) || mf.sharedMesh == null)
                        continue;
                    if (BuildRamp(mf))
                        built++;
                }

                if (built > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    prefabsChanged++;
                    rampsBuilt += built;
                    Debug.Log($"[MapColliderAuthoring] {path} — 계단 램프 {built}개 생성.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        Debug.Log($"[MapColliderAuthoring] 계단 램프 완료 — 프리팹 {prefabsChanged}개, 램프 {rampsBuilt}개.");
    }

    static bool BuildRamp(MeshFilter mf)
    {
        Bounds bounds = mf.sharedMesh.bounds;

        // 오르는 방향 찾기 — 각 수평축으로 반씩 나눠 위쪽 절반의 최고 높이를 비교한다.
        MeasureAscent(mf.sharedMesh, bounds, out bool alongX, out float sign);

        float run = alongX ? bounds.size.x : bounds.size.z;
        float width = alongX ? bounds.size.z : bounds.size.x;
        float rise = bounds.size.y;
        if (run <= Mathf.Epsilon || rise <= Mathf.Epsilon)
            return false;

        float angle = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
        const float thickness = 0.5f;
        float length = Mathf.Sqrt(run * run + rise * rise);

        // 계단 원본 콜라이더는 제거 — 램프와 동시에 존재하면 턱이 그대로 남는다.
        foreach (Collider stale in mf.GetComponents<Collider>())
            Object.DestroyImmediate(stale);

        Transform existing = mf.transform.Find(RampChildName);
        if (existing != null)
            Object.DestroyImmediate(existing.gameObject);

        var rampGo = new GameObject(RampChildName);
        rampGo.transform.SetParent(mf.transform, false);

        // 램프 표면이 저점 바닥에서 고점 상단까지 이어지도록 중앙에 놓고 기울인다.
        Vector3 center = bounds.center;
        center.y = bounds.min.y + rise * 0.5f;
        rampGo.transform.localPosition = center;
        rampGo.transform.localRotation = alongX
            ? Quaternion.Euler(0f, 0f, angle * sign)
            : Quaternion.Euler(-angle * sign, 0f, 0f);

        var box = rampGo.AddComponent<BoxCollider>();
        box.size = alongX
            ? new Vector3(length, thickness, width)
            : new Vector3(width, thickness, length);
        box.center = new Vector3(0f, -thickness * 0.5f, 0f); // 상단면이 램프면

        Debug.Log($"[MapColliderAuthoring] 램프 {mf.gameObject.name} — " +
                  $"{(alongX ? "X" : "Z")}축 {(sign > 0 ? "+" : "-")}, {angle:F1}도, 길이 {length:F2}");
        return true;
    }

    // 위쪽 절반과 아래쪽 절반의 최고 정점 높이를 비교해 오르는 축·방향을 판정한다.
    static void MeasureAscent(Mesh mesh, Bounds bounds, out bool alongX, out float sign)
    {
        Vector3[] vertices = mesh.vertices;
        float xLow = float.NegativeInfinity, xHigh = float.NegativeInfinity;
        float zLow = float.NegativeInfinity, zHigh = float.NegativeInfinity;

        foreach (Vector3 v in vertices)
        {
            if (v.x < bounds.center.x) xLow = Mathf.Max(xLow, v.y);
            else xHigh = Mathf.Max(xHigh, v.y);

            if (v.z < bounds.center.z) zLow = Mathf.Max(zLow, v.y);
            else zHigh = Mathf.Max(zHigh, v.y);
        }

        float xDelta = xHigh - xLow;
        float zDelta = zHigh - zLow;

        alongX = Mathf.Abs(xDelta) > Mathf.Abs(zDelta);
        sign = Mathf.Sign(alongX ? xDelta : zDelta);
        if (sign == 0f)
            sign = 1f;
    }

    // 계단 판정 — 램프 대체 대상. 경사로(slope)는 이미 램프라 원본 메시 그대로 둔다.
    static bool IsStair(MeshFilter mf)
    {
        if (mf.gameObject.name.ToLowerInvariant().Contains("stair"))
            return true;
        return mf.sharedMesh != null && mf.sharedMesh.name.ToLowerInvariant().Contains("stair");
    }

    static bool MatchesKeyword(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        string lower = name.ToLowerInvariant();
        foreach (string k in NameKeywords)
            if (lower.Contains(k))
                return true;
        return false;
    }

    // 2026-07-29 추가: 현재 열려있는 씬의 Level_wall_hallway 자식들에게 MeshCollider 부착
    [MenuItem("Tools/Map/Authoring/Add MeshColliders to Active Scene (Level_wall_hallway)")]
    public static void AddMeshCollidersToActiveSceneHallway()
    {
        var root = GameObject.Find("Level_wall_hallway");
        if (root == null)
        {
            Debug.LogError("[MapColliderAuthoring] 현재 씬에서 'Level_wall_hallway' 오브젝트를 찾을 수 없습니다.");
            return;
        }

        int addedCount = 0;
        var meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
        
        Undo.SetCurrentGroupName("Add Mesh Colliders (Scene)");
        int group = Undo.GetCurrentGroup();

        foreach (var mf in meshFilters)
        {
            var go = mf.gameObject;
            if (go.GetComponent<Collider>() == null)
            {
                var meshCollider = Undo.AddComponent<MeshCollider>(go);
                meshCollider.sharedMesh = mf.sharedMesh;
                addedCount++;
            }
        }

        Undo.CollapseUndoOperations(group);
        Debug.Log($"[MapColliderAuthoring] 씬 내 'Level_wall_hallway' 하위 오브젝트에 {addedCount}개의 MeshCollider를 부착했습니다.");
    }
}
