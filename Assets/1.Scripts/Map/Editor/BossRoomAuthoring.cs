using UnityEditor;
using UnityEngine;

// 보스룸 경계·기준점 저작 도구.
//
// bossroom.prefab은 아트 모델 인스턴스 64개로만 구성되어 자체 콜라이더가 없다. 바닥·벽 콜라이더는
// FBX 임포터에서 오는데 그것만으로는 방 경계에 틈이 남아, 플레이어가 떨어지면 추락 판정으로
// 스폰 지점까지 튕겨 나간다. 그래서 보이지 않는 경계와 바닥 안전망을 명시적으로 만든다.
//
// 크기는 렌더러 바운즈에서 실측한다 — 아트가 교체돼도 다시 돌리면 맞는다(하드코딩 금지).
public static class BossRoomAuthoring
{
    const string BossRoomPath = "Assets/2.Prefabs/Map/Zoneprefab/bossroom.prefab";

    const string FloorColliderName = "BossFloorCollider";
    const string BoundariesName = "InvisibleBoundaries";
    const string ArrivalRootName = "PlayerArrivalPoints";
    const string BossLandingName = "BossLandingPoint";

    const string WallLayerName = "Wall";

    // 경계 벽 두께·높이. 대시(속도 20 · 0.25초)와 넉백으로 넘어가지 못할 높이를 준다.
    const float BoundaryThickness = 1f;
    const float BoundaryHeight = 8f;

    // 바닥 안전망 두께. 상단면을 바닥 표면에 맞추고 아래로 뻗는다.
    const float FloorNetThickness = 1f;

    // 도착 지점 배치 반경. 플레이어 캡슐 지름(0.76m)보다 충분히 크게 벌린다.
    const float ArrivalRingRadius = 2f;

    [MenuItem("Tools/Map/Authoring/Rebuild Boss Room Bounds")]
    public static void RebuildBossRoomBounds()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(BossRoomPath);

        try
        {
            if (!TryMeasure(root, out Bounds roomBounds, out float floorTopY))
            {
                Debug.LogError("[BossRoomAuthoring] 렌더러를 찾지 못해 바운즈를 계산할 수 없다.");
                return;
            }

            Debug.Log($"[BossRoomAuthoring] 실측 — X {roomBounds.min.x:F1}~{roomBounds.max.x:F1} / " +
                      $"Z {roomBounds.min.z:F1}~{roomBounds.max.z:F1} / " +
                      $"Y {roomBounds.min.y:F1}~{roomBounds.max.y:F1}, 바닥 상단 Y={floorTopY:F2}");

            int wallLayer = LayerMask.NameToLayer(WallLayerName);
            if (wallLayer < 0)
            {
                Debug.LogError($"[BossRoomAuthoring] '{WallLayerName}' 레이어가 없다.");
                return;
            }

            BuildFloorNet(root, roomBounds, floorTopY);
            BuildBoundaries(root, roomBounds, floorTopY, wallLayer);
            BuildReferencePoints(root, roomBounds, floorTopY);

            PrefabUtility.SaveAsPrefabAsset(root, BossRoomPath);
            Debug.Log("[BossRoomAuthoring] 완료 — 경계·바닥 안전망·기준점 재생성 후 저장.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// 방 전체 바운즈와 바닥 상단 Y를 렌더러에서 실측한다.
    /// 바닥 상단은 이름·메시·머티리얼에 floor가 들어간 렌더러들의 최고점으로 잡는다.
    /// 못 찾으면 방 전체의 최저점으로 폴백한다.
    /// </summary>
    static bool TryMeasure(GameObject root, out Bounds roomBounds, out float floorTopY)
    {
        roomBounds = default;
        floorTopY = 0f;

        bool hasAny = false;
        bool hasFloor = false;
        float floorMax = float.NegativeInfinity;

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            // 우리가 만든 경계 오브젝트는 렌더러가 없으므로 여기 걸리지 않는다.
            // renderer.bounds는 월드 좌표다. LoadPrefabContents는 프리팹을 원점이 아닌 임시
            // 프리뷰 씬에 올리므로, 그대로 쓰면 BoxCollider.center(로컬)에 넣을 때 전체가 밀린다.
            Bounds bounds = ToRootLocalBounds(root.transform, renderer.bounds);

            if (!hasAny)
            {
                roomBounds = bounds;
                hasAny = true;
            }
            else
            {
                roomBounds.Encapsulate(bounds);
            }

            if (!LooksLikeFloor(renderer))
                continue;

            hasFloor = true;
            floorMax = Mathf.Max(floorMax, bounds.max.y);
        }

        if (!hasAny)
            return false;

        floorTopY = hasFloor ? floorMax : roomBounds.min.y;
        return true;
    }

    /// <summary>월드 바운즈를 프리팹 루트 로컬 바운즈로 변환한다(8개 꼭짓점 변환 후 재포장).</summary>
    static Bounds ToRootLocalBounds(Transform root, Bounds world)
    {
        Vector3 center = world.center;
        Vector3 extents = world.extents;

        var result = new Bounds(root.InverseTransformPoint(center), Vector3.zero);
        for (int corner = 0; corner < 8; corner++)
        {
            var offset = new Vector3(
                (corner & 1) == 0 ? -extents.x : extents.x,
                (corner & 2) == 0 ? -extents.y : extents.y,
                (corner & 4) == 0 ? -extents.z : extents.z);

            result.Encapsulate(root.InverseTransformPoint(center + offset));
        }

        return result;
    }

    static bool LooksLikeFloor(Renderer renderer)
    {
        if (renderer.gameObject.name.ToLowerInvariant().Contains("floor"))
            return true;

        foreach (Material material in renderer.sharedMaterials)
        {
            if (material != null && material.name.ToLowerInvariant().Contains("floor"))
                return true;
        }

        return false;
    }

    /// <summary>바닥 아트에 틈이 있어도 빠지지 않게 하는 안전망. 상단면을 바닥 표면에 맞춘다.</summary>
    static void BuildFloorNet(GameObject root, Bounds roomBounds, float floorTopY)
    {
        Transform existing = root.transform.Find(FloorColliderName);
        if (existing != null)
            Object.DestroyImmediate(existing.gameObject);

        var go = new GameObject(FloorColliderName);
        go.transform.SetParent(root.transform, false);
        go.layer = root.layer; // 바닥은 Default(=walkable) 유지 — NavMesh 수집 대상이 된다

        var box = go.AddComponent<BoxCollider>();
        box.center = new Vector3(
            roomBounds.center.x,
            floorTopY - FloorNetThickness * 0.5f,
            roomBounds.center.z);
        box.size = new Vector3(roomBounds.size.x, FloorNetThickness, roomBounds.size.z);

        Debug.Log($"[BossRoomAuthoring] {FloorColliderName} — size {box.size}, top Y={floorTopY:F2}");
    }

    /// <summary>네 변을 막는 보이지 않는 경계. 렌더러·트리거 없이 Wall 레이어 BoxCollider만 둔다.</summary>
    static void BuildBoundaries(GameObject root, Bounds roomBounds, float floorTopY, int wallLayer)
    {
        Transform existing = root.transform.Find(BoundariesName);
        if (existing != null)
            Object.DestroyImmediate(existing.gameObject);

        var parent = new GameObject(BoundariesName);
        parent.transform.SetParent(root.transform, false);
        parent.layer = wallLayer;

        float centerY = floorTopY + BoundaryHeight * 0.5f;
        float halfX = roomBounds.size.x * 0.5f;
        float halfZ = roomBounds.size.z * 0.5f;
        float half = BoundaryThickness * 0.5f;

        // 두께의 절반만큼 바깥으로 밀어 벽 안쪽 면이 방 경계와 일치하게 한다.
        // 모서리는 X 벽을 Z 두께만큼 늘려 겹치게 만들어 틈을 없앤다.
        AddWall(parent, wallLayer, "Boundary_XMin",
            new Vector3(roomBounds.center.x - halfX - half, centerY, roomBounds.center.z),
            new Vector3(BoundaryThickness, BoundaryHeight, roomBounds.size.z + BoundaryThickness * 2f));

        AddWall(parent, wallLayer, "Boundary_XMax",
            new Vector3(roomBounds.center.x + halfX + half, centerY, roomBounds.center.z),
            new Vector3(BoundaryThickness, BoundaryHeight, roomBounds.size.z + BoundaryThickness * 2f));

        AddWall(parent, wallLayer, "Boundary_ZMin",
            new Vector3(roomBounds.center.x, centerY, roomBounds.center.z - halfZ - half),
            new Vector3(roomBounds.size.x, BoundaryHeight, BoundaryThickness));

        AddWall(parent, wallLayer, "Boundary_ZMax",
            new Vector3(roomBounds.center.x, centerY, roomBounds.center.z + halfZ + half),
            new Vector3(roomBounds.size.x, BoundaryHeight, BoundaryThickness));

        Debug.Log($"[BossRoomAuthoring] {BoundariesName} — 4면, 높이 {BoundaryHeight}, " +
                  $"레이어 {LayerMask.LayerToName(wallLayer)}");
    }

    static void AddWall(GameObject parent, int layer, string name, Vector3 center, Vector3 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.layer = layer;

        var box = go.AddComponent<BoxCollider>();
        box.center = center;
        box.size = size;
    }

    /// <summary>
    /// 텔레포트 도착 지점 3개와 보스 착지 지점. (승인 계획 Task 1)
    /// 방 중심 기준 삼각 배치로 서로 캡슐 지름 이상 벌리고, 경계에서도 떨어뜨린다.
    /// 실제 위치는 플레이 테스트 후 프리팹에서 수동 조정할 수 있다.
    /// </summary>
    static void BuildReferencePoints(GameObject root, Bounds roomBounds, float floorTopY)
    {
        Transform staleArrival = root.transform.Find(ArrivalRootName);
        if (staleArrival != null)
            Object.DestroyImmediate(staleArrival.gameObject);

        Transform staleLanding = root.transform.Find(BossLandingName);
        if (staleLanding != null)
            Object.DestroyImmediate(staleLanding.gameObject);

        Vector3 floorCenter = new Vector3(roomBounds.center.x, floorTopY, roomBounds.center.z);

        var arrivalRoot = new GameObject(ArrivalRootName);
        arrivalRoot.transform.SetParent(root.transform, false);
        arrivalRoot.transform.localPosition = floorCenter;

        // 보스 착지점을 중심에 두고, 플레이어는 보스 쪽을 바라보도록 반대편에 배치한다.
        for (int i = 0; i < 3; i++)
        {
            float angle = 210f + i * 60f; // 중심 아래쪽(-Z)에 부채꼴로 벌린다
            float rad = angle * Mathf.Deg2Rad;
            var point = new GameObject($"Player{i + 1}");
            point.transform.SetParent(arrivalRoot.transform, false);
            point.transform.localPosition = new Vector3(
                Mathf.Cos(rad) * ArrivalRingRadius,
                0f,
                Mathf.Sin(rad) * ArrivalRingRadius);
            point.transform.localRotation = Quaternion.LookRotation(
                -point.transform.localPosition.normalized, Vector3.up);
        }

        var landing = new GameObject(BossLandingName);
        landing.transform.SetParent(root.transform, false);
        landing.transform.localPosition = floorCenter;

        Debug.Log($"[BossRoomAuthoring] {ArrivalRootName} 3개 + {BossLandingName} — 중심 {floorCenter}");
    }
}
