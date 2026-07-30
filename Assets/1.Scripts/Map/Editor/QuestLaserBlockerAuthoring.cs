using UnityEditor;
using UnityEngine;

// 레이저 배리어(layprefab)에 물리 차단벽을 붙이는 저작 도구.
//
// Quest 존이 미구현이라 통로를 물리적으로 막아야 한다. 레이저는 지금 시각 오브젝트뿐이어서
// 그냥 통과된다 — 보이는 것과 판정이 어긋나는 상태다. 그래서 레이저 배열 실측 바운즈에 맞춘
// 보이지 않는 BoxCollider(Wall 레이어)를 프리팹에 넣어 모든 인스턴스가 함께 막히게 한다.
//
// ⚠️ NavMesh는 Default 레이어 콜라이더만 수집한다(MapNavMeshBaker). 즉 이 벽은 플레이어·몬스터의
// 물리 이동은 막지만 NavMesh 경로 자체를 깎지는 않는다 — 몬스터가 경로를 그 너머로 잡을 수 있다.
// 보스룸 투명 경계와 같은 성질이며, Quest 존이 구현되면 이 도구로 만든 벽을 제거한다.
public static class QuestLaserBlockerAuthoring
{
    const string LaserPrefabPath = "Assets/2.Prefabs/Map/Props/layprefab.prefab";
    const string BlockerName = "LaserBlockWall";
    const string WallLayerName = "Wall";

    // 통로 높이. 대시·넉백으로 넘지 못할 높이를 준다(보스룸 경계와 동일 기준).
    const float WallHeight = 8f;

    // 얇은 축 최소 두께. 얇으면 고속 이동이 관통한다.
    const float MinThickness = 0.6f;

    // 레이저가 바닥에서 떠 있어도 발밑까지 막도록 아래로 더 뻗는 양.
    const float DownExtension = 2f;

    /// <summary>
    /// 레이저 프리팹에 심은 차단벽을 제거한다.
    ///
    /// ⚠️ 이 방식은 폐기됐다: layprefab은 Stage1 통로 26곳에 들어 있어 <b>보스 진입로까지 봉쇄</b>했고,
    /// 어느 슬롯이 Quest가 되는지는 시드마다 달라 정적 배치로 맞출 수 없다. 현재 Quest 차단은
    /// MapContentSpawner가 역할 확정 시점에 해당 존만 감싸는 방식(AttachQuestBlockade)으로 처리한다.
    /// 특정 통로를 영구히 막아야 할 때만 아래 Setup을 쓰고, 그 경우도 대상 인스턴스를 좁혀야 한다.
    /// </summary>
    [MenuItem("Tools/Map/Authoring/Remove Quest Laser Blockers")]
    public static void RemoveLaserBlockers()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(LaserPrefabPath);

        try
        {
            Transform existing = root.transform.Find(BlockerName);
            if (existing == null)
            {
                Debug.Log($"[QuestLaserBlocker] {BlockerName}이 없다 — 제거할 것 없음.");
                return;
            }

            Object.DestroyImmediate(existing.gameObject);
            PrefabUtility.SaveAsPrefabAsset(root, LaserPrefabPath);
            Debug.Log($"[QuestLaserBlocker] {BlockerName} 제거 후 저장 — 레이저 통로 통행이 복구된다.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [MenuItem("Tools/Map/Authoring/Setup Quest Laser Blockers")]
    public static void SetupLaserBlockers()
    {
        int wallLayer = LayerMask.NameToLayer(WallLayerName);
        if (wallLayer < 0)
        {
            Debug.LogError($"[QuestLaserBlocker] '{WallLayerName}' 레이어가 없다.");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(LaserPrefabPath);

        try
        {
            if (!TryMeasureLocalBounds(root, out Bounds local))
            {
                Debug.LogError("[QuestLaserBlocker] 렌더러를 찾지 못해 차단벽 크기를 계산할 수 없다.");
                return;
            }

            Transform existing = root.transform.Find(BlockerName);
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            var go = new GameObject(BlockerName);
            go.transform.SetParent(root.transform, false);
            go.layer = wallLayer;

            var box = go.AddComponent<BoxCollider>();

            // 통로를 가로지르는 넓은 축은 그대로 쓰고, 얇은 축만 최소 두께를 보장한다.
            float sizeX = local.size.x;
            float sizeZ = local.size.z;
            bool thinAlongX = sizeX <= sizeZ;

            box.size = new Vector3(
                thinAlongX ? Mathf.Max(sizeX, MinThickness) : sizeX,
                WallHeight,
                thinAlongX ? sizeZ : Mathf.Max(sizeZ, MinThickness));

            // 아래로 DownExtension만큼 더 뻗도록 중심을 내린다.
            float bottom = local.min.y - DownExtension;
            box.center = new Vector3(local.center.x, bottom + WallHeight * 0.5f, local.center.z);
            box.isTrigger = false;

            PrefabUtility.SaveAsPrefabAsset(root, LaserPrefabPath);

            Debug.Log(
                $"[QuestLaserBlocker] {BlockerName} 생성 — size {box.size}, center {box.center}, " +
                $"레이어 {WallLayerName}. 레이저 실측 바운즈 {local.size}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// 레이저 렌더러 전체를 감싸는 루트 로컬 바운즈.
    /// renderer.bounds는 월드 좌표이고 LoadPrefabContents는 프리팹을 원점이 아닌 프리뷰 씬에 올린다 —
    /// 그대로 BoxCollider.center(로컬)에 넣으면 벽이 엉뚱한 곳에 생긴다.
    /// </summary>
    static bool TryMeasureLocalBounds(GameObject root, out Bounds local)
    {
        local = default;
        bool hasAny = false;

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            Bounds converted = ToRootLocalBounds(root.transform, renderer.bounds);
            if (!hasAny)
            {
                local = converted;
                hasAny = true;
                continue;
            }

            local.Encapsulate(converted);
        }

        return hasAny;
    }

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
}
