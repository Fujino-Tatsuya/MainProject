using Unity.AI.Navigation;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;

// 보스룸 경계·기준점 저작 도구.
//
// bossroom.prefab은 아트 모델 인스턴스 64개로만 구성되어 자체 콜라이더가 없다. 바닥·벽 콜라이더는
// FBX 임포터에서 오는데 그것만으로는 방 경계에 틈이 남아, 플레이어가 떨어지면 추락 판정으로
// 스폰 지점까지 튕겨 나간다. 그래서 보이지 않는 경계와 바닥 안전망을 명시적으로 만든다.
//
// 크기는 렌더러 바운즈에서 실측한다 — 아트가 교체돼도 다시 돌리면 맞는다(하드코딩 금지).
//
// 2026-10-13 개정(은희 요청 — 현재 bossroom 구조·값을 그대로 재현):
//  · 경계 안쪽 면 = **보이는 지상층 벽 메시의 안쪽 면**(변마다 1mm 최빈값). 예전처럼 방 전체 렌더러(파이프·체인 포함)
//    바운즈를 쓰지 않는다 — 그 방식은 Z 벽이 0.49m 치우치고 바닥·BossArea 가 벽 밖으로 삐져나왔다.
//  · 사용자가 묶은 그룹(Boss/·TwentyThreeCharger/·Environment/)을 존중한다 — 기존 오브젝트는 **어디에 있든**(깊이 무관)
//    찾아 그 자리에서 갱신하고, 없을 때만 새로 만든다(새로 만들 땐 Boss/ 그룹이 있으면 그 아래). 중복 생성 금지.
//  · 손으로 배치한 PlayerArrivalPoints/자식·BossLandingPoint 는 있으면 건드리지 않는다.
//  · 벽 = Wall 태그+레이어, 바닥 안전망 = Ground 태그+레이어(Docs/tech/wall-concept.md).
//  · 🔴 23호 보스는 GameObject.Find("InvisibleBoundaries") + 자식 이름 Boundary_XMin/XMax/ZMin/ZMax 의 안쪽 면으로
//    아레나를 잡는다(TwentyThreeBoss.ResolveArena) — 이름·구성을 바꾸지 말 것.
public static class BossRoomAuthoring
{
    const string BossRoomPath = "Assets/2.Prefabs/Environment/Layouts/Zones/bossroom.prefab";

    const string FloorColliderName = "BossFloorCollider";
    const string BoundariesName = "InvisibleBoundaries";
    const string ArrivalRootName = "PlayerArrivalPoints";
    const string BossLandingName = "BossLandingPoint";
    const string BossGroupName = "Boss";

    const string WallLayerName = "Wall";
    const string WallTag = "Wall";
    const string GroundLayerName = "Ground";
    const string GroundTag = "Ground";

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
            if (!TryMeasure(root, out _, out float floorTopY))
            {
                Debug.LogError("[BossRoomAuthoring] 렌더러를 찾지 못해 바운즈를 계산할 수 없다.");
                return;
            }

            // 아레나 = 보이는 벽 안쪽 면으로 둘러싼 사각형(방 로컬 XZ). 경계·바닥·BossArea·착지점이 모두 이것을 기준으로 한다.
            if (!TryMeasureWallInnerFaces(root, floorTopY, out Bounds arena))
            {
                Debug.LogError("[BossRoomAuthoring] 벽 메시 안쪽 면을 네 변 모두 찾지 못했다 — 저장하지 않는다.");
                return;
            }

            Debug.Log($"[BossRoomAuthoring] 실측 — 벽 안쪽 면 X {arena.min.x:F3}~{arena.max.x:F3} / " +
                      $"Z {arena.min.z:F3}~{arena.max.z:F3}, 바닥 상단 Y={floorTopY:F2}");

            int wallLayer = LayerMask.NameToLayer(WallLayerName);
            int groundLayer = LayerMask.NameToLayer(GroundLayerName);
            if (wallLayer < 0 || groundLayer < 0)
            {
                Debug.LogError($"[BossRoomAuthoring] '{WallLayerName}'/'{GroundLayerName}' 레이어가 없다.");
                return;
            }

            BuildFloorNet(root, arena, floorTopY, groundLayer);
            BuildBoundaries(root, arena, floorTopY, wallLayer);
            BuildReferencePoints(root, arena, floorTopY);
            BuildBossArea(root, arena, floorTopY);
            BuildNavMeshMargin(root, floorTopY);

            PrefabUtility.SaveAsPrefabAsset(root, BossRoomPath);
            Debug.Log("[BossRoomAuthoring] 완료 — 경계·바닥 안전망·기준점 재생성 후 저장.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ── 보스 아레나 기준 오브젝트 ─────────────────────────────────────────

    const string BossAreaName = "BossArea";
    const string BossAreaTag = "BossArea";

    // BossScene 기준값: BoxCollider size (10, 2, 10) / center (0, 1, 0) / isTrigger.
    const float BossAreaHeight = 2f;

    /// <summary>
    /// No.23 BT가 태그로 찾아 쓰는 아레나 기준 오브젝트.
    ///
    /// BossScene에는 tag `BossArea`인 트리거 박스가 아레나 중앙에 있고 BT가 그것을 켜고 끈다.
    /// MapScene에는 그게 없어서(전수 검색 0건) 보스·Wells 쪽 처리가 방 중앙을 기준으로 잡히지 않았다.
    /// 보스룸 프리팹 안에 만들어 두면 방을 어디로 옮기든 항상 중앙을 따라간다.
    ///
    /// ⚠️ KMK 쪽 BossArea에는 <c>TwentyThreeArenaContext</c>(보스 스포너)가 함께 붙어 있다. 여기엔
    /// 붙이지 않는다 — MapScene의 스폰 소유자는 <c>BossEncounterDirector</c> 하나이고, 붙이면 보스가
    /// 두 번 스폰된다.
    /// </summary>
    static void BuildBossArea(GameObject root, Bounds arena, float floorTopY)
    {
        // 있으면 그 자리(부모 유지)에서 갱신, 없으면 Boss/ 그룹(없으면 루트) 아래에 만든다.
        GameObject go = GetOrCreate(root, BossAreaName, out bool created);
        go.tag = BossAreaTag;
        SetRootLocalPosition(root, go.transform, new Vector3(arena.center.x, floorTopY, arena.center.z));

        var box = go.GetComponent<BoxCollider>();
        if (box == null) box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.center = new Vector3(0f, BossAreaHeight * 0.5f, 0f);
        // 아레나(벽 안쪽 면) 전체를 덮는다 — 벽 밖으로 삐져나가지 않게.
        box.size = new Vector3(arena.size.x, BossAreaHeight, arena.size.z);
        if (created) Debug.Log($"[BossRoomAuthoring] {BossAreaName} 새로 만듦 — {go.transform.parent.name} 아래");

        Debug.Log(
            $"[BossRoomAuthoring] {BossAreaName} — tag {BossAreaTag}, 중앙 {go.transform.localPosition}, " +
            $"박스 {box.size} (트리거). ArenaContext는 붙이지 않음(스폰 소유자는 Director).");
    }

    // ── 충전 발판 콜라이더 (2026-08-13, 팀장 Play 관찰) ───────────────────

    // 2026-10-13 bossroom 정리에서 "Env_Floor_bosscharger (1)" → "Env_Floor_bosscharger" 로 이름이 바뀌었다(둘 다 찾는다).
    const string ChargeFloorName = "Env_Floor_bosscharger";
    const string ChargeFloorLegacyName = "Env_Floor_bosscharger (1)";

    /// <summary>
    /// 충전 발판(<c>Env_Floor_bosscharger (1)</c>)에 <b>MeshCollider 를 붙인다.</b>
    ///
    /// 🔴 왜 필요한가 — 팀장 관찰: "저 오브젝트보다 아래에 그려지니까 장판이 제대로 안 보인다.
    ///    바닥을 보고 그 위에 그려져야 한다."
    ///    이 발판은 <b>렌더러만 있고 콜라이더가 없었다</b>(Transform·MeshFilter·MeshRenderer 뿐).
    ///    그래서 <see cref="GroundProbe"/> 의 레이캐스트가 발판을 못 보고 <b>그 아래 바닥(y≈0)</b> 을
    ///    잡는다 → 장판·예고·표식이 발판 y=0.06 보다 낮게 놓여 **발판 메시에 가려진다.**
    ///    "바닥을 보고 그 위에" 를 만족시키려면 그 바닥이 레이캐스트에 보여야 한다.
    ///
    /// 왜 BoxCollider 가 아니라 MeshCollider 인가: 발판은 원판 + 십자 팔 모양이라 박스로 감싸면
    /// <b>발판이 없는 곳까지 높이가 올라가</b> 장판이 허공에 뜬다. 실제 형상을 따라야 한다.
    ///
    /// ⚠️ 레이어는 그대로 둔다(Default) — GroundProbe 가 Default∪Ground 를 항상 포함한다.
    /// ⚠️ NavMesh 는 다시 굽지 않는다. 이 발판은 높이 6cm 라 보행면 판정에 의미가 없고,
    ///    재베이크는 보스룸 전체에 영향을 주므로 별도 판단이 필요하다.
    /// 재실행해도 중복 부착하지 않는다(멱등).
    /// </summary>
    [MenuItem("Tools/Map/Authoring/Setup Boss Charge Floor Collider")]
    public static void SetupBossChargeFloorCollider()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(BossRoomPath);
        try
        {
            Transform floor = FindChild(root.transform, ChargeFloorName) ?? FindChild(root.transform, ChargeFloorLegacyName);
            if (floor == null)
            {
                Debug.LogError($"[BossRoom] '{ChargeFloorName}' 을 못 찾았다 — 이름이 바뀌었는지 확인할 것.");
                return;
            }

            if (floor.GetComponent<Collider>() != null)
            {
                Debug.Log($"[BossRoom] '{floor.name}' 에 이미 콜라이더가 있다 — 변경 없음(멱등).");
                return;
            }

            var mf = floor.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null)
            {
                Debug.LogError($"[BossRoom] '{floor.name}' 에 메시가 없어 MeshCollider 를 붙일 수 없다.");
                return;
            }

            var mc = floor.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.convex = false;   // 정적 바닥이라 볼록 제한이 필요 없다

            PrefabUtility.SaveAsPrefabAsset(root, BossRoomPath, out bool saved);
            Debug.Log(saved
                ? $"[BossRoom] ✅ '{floor.name}' 에 MeshCollider 부착 — 이제 GroundProbe 가 이 발판을 " +
                  $"바닥으로 본다(장판·예고가 위에 그려진다). 발판 y={floor.position.y:F2}"
                : "[BossRoom] 🔴 프리팹 저장 실패");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static Transform FindChild(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform f = FindChild(root.GetChild(i), name);
            if (f != null) return f;
        }
        return null;
    }

    // ── 충전 기둥 (승인 계획 Task 6) ──────────────────────────────────────

    const string ChargePillarName = "Env_Mv_bosscharger_upper";
    const int ExpectedPillarCount = 4;

    // ⚠️ 레이어는 BossScene(EnemyHurtBox=14)과 다르게 Enemy(8)를 쓴다.
    // 현재 플레이어 공격의 targetLayer/hittableLayers는 m_Bits=256(Enemy)뿐이라 14에 두면
    // 기둥을 때릴 수 없다. 실제로 동작하는 ChompBot의 Hurtbox도 Enemy(8)에 있다.
    // 팀이 나중에 플레이어 마스크에 EnemyHurtBox를 추가하면 여기도 함께 옮긴다.
    const string PillarLayerName = "Enemy";

    // 체력·방어는 BossScene 기둥 설정과 동일(체력 5 · 방어 0).
    const int PillarMaxHp = 5;
    const int PillarDefense = 0;

    /// <summary>
    /// 보스룸의 충전 기둥 4개를 서버 권한 피격 대상으로 구성한다.
    ///
    /// 아트 오브젝트에 컴포넌트를 <b>덧붙이는</b> 방식이다 — 부모 프리팹(SVN 아트)은 건드리지 않고
    /// bossroom.prefab에 override로만 남는다. 재실행해도 중복 부착하지 않는다.
    /// </summary>
    [MenuItem("Tools/Map/Authoring/Setup Boss Charge Pillars")]
    public static void SetupBossChargePillars()
    {
        int pillarLayer = LayerMask.NameToLayer(PillarLayerName);
        if (pillarLayer < 0)
        {
            Debug.LogError($"[BossRoomAuthoring] '{PillarLayerName}' 레이어가 없다.");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(BossRoomPath);

        try
        {
            var pillars = new System.Collections.Generic.List<Transform>();
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == ChargePillarName)
                    pillars.Add(child);
            }

            if (pillars.Count != ExpectedPillarCount)
            {
                Debug.LogWarning(
                    $"[BossRoomAuthoring] '{ChargePillarName}' 오브젝트가 {pillars.Count}개다(기대 {ExpectedPillarCount}). " +
                    "아트 구성이 바뀌었는지 확인 필요.");
            }

            if (pillars.Count == 0)
            {
                Debug.LogError($"[BossRoomAuthoring] '{ChargePillarName}'를 찾지 못해 중단한다.");
                return;
            }

            // 이름이 모두 같아 인덱스 순서가 흔들리지 않도록 위치로 정렬한다(결정적 활성 순서).
            pillars.Sort((a, b) =>
            {
                Vector3 pa = a.position;
                Vector3 pb = b.position;
                int compareZ = pa.z.CompareTo(pb.z);
                return compareZ != 0 ? compareZ : pa.x.CompareTo(pb.x);
            });

            for (int i = 0; i < pillars.Count; i++)
                SetupPillar(pillars[i], i, pillarLayer);

            PrefabUtility.SaveAsPrefabAsset(root, BossRoomPath);
            Debug.Log($"[BossRoomAuthoring] 충전 기둥 {pillars.Count}개 구성 완료 후 저장.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void SetupPillar(Transform pillar, int index, int pillarLayer)
    {
        GameObject go = pillar.gameObject;
        go.layer = pillarLayer;

        // Unit(NetworkBehaviour) 요구사항 — NetworkObject가 먼저 있어야 한다.
        if (go.GetComponent<NetworkObject>() == null)
            go.AddComponent<NetworkObject>();

        if (go.GetComponent<NetworkTransform>() == null)
        {
            var networkTransform = go.AddComponent<NetworkTransform>();
            // 부모(보스룸)가 고정이라 로컬 공간 복제로 두면 좌표계 혼선이 없다.
            networkTransform.InLocalSpace = true;
        }

        // 피격 판정용 콜라이더. 이미 있으면 크기를 다시 재지 않는다(튜닝값 보존). 없을 때만 렌더러 실측으로 만든다.
        // 활성 여부는 BossChargingPylon 이 제어한다.
        BoxCollider box = go.GetComponent<BoxCollider>();
        if (box == null)
        {
            box = go.AddComponent<BoxCollider>();
            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                Bounds local = ToRootLocalBounds(pillar, renderer.bounds);
                box.center = local.center;
                box.size = local.size;
            }
            else
            {
                Debug.LogWarning($"[BossRoomAuthoring] {go.name}[{index}]에 Renderer가 없어 콜라이더 크기를 실측하지 못했다.", go);
            }
            box.isTrigger = false;
            box.enabled = false; // 평상시 통행 방해 금지 — 상승 완료 시 BossChargingPylon 이 켠다
        }

        // 🔴 2026-10-13: 레거시 ChargingObject(Unit) 는 더 이상 붙이지 않는다 — 현재 기둥은 BossChargingPylon(Unit)이고,
        //    둘을 한 오브젝트에 붙이면 Unit 이 둘이 된다. BossChargingPylon 이 없으면 수동 부착하도록 알리기만 한다.
        if (go.GetComponent<BossChargingPylon>() == null)
            Debug.LogWarning($"[BossRoomAuthoring] {go.name}[{index}] 에 BossChargingPylon 이 없다 — 인스펙터에서 붙이고 outlineMaterial 을 지정할 것.", go);
        if (go.GetComponent<ChargingObject>() != null)
            Debug.LogWarning($"[BossRoomAuthoring] {go.name}[{index}] 에 레거시 ChargingObject 가 남아 있다 — BossChargingPylon 과 함께 두면 안 된다.", go);

        Debug.Log(
            $"[BossRoomAuthoring] 기둥 {index} 구성 — {go.name} @ local {pillar.localPosition}, " +
            $"레이어 {LayerMask.LayerToName(pillarLayer)}, 콜라이더 {box.size}");
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

        // 바닥 상단 Y는 "가장 높은 바닥"이 아니라 **가장 넓게 깔린 바닥**이다.
        //
        // ⚠️ 예전에는 Mathf.Max를 썼다. 보스룸에는 이름에 "Floor"가 들어가는 솟은 발판
        // (Env_Floor_bosscharger)이 있어서 그 상단(0.611)이 잡혔고, 실제 보행 슬래브
        // (Env_floor_basic_typeA, 0.50)보다 0.11 높은 안전망이 만들어졌다. 그 결과 이 안전망을
        // 바닥으로 잡는 모든 것(폭탄·장판·착지점)이 타일보다 0.11 떠 있었다.
        // 슬래브는 수십 장이고 발판은 몇 개뿐이므로, 높이를 1cm 단위로 묶어 표를 세면 보행면이 이긴다.
        // 🔴 2026-10-14: 개수가 아니라 **XZ 면적**으로 센다 — 바닥을 floor_stone(0.23)으로 바꾼 뒤 솟은 트렌치 덮개(0.51, 작은 판 12장)가
        //    개수로 이겨서 BossFloorCollider 윗면이 0.5 가 됐고 플레이어가 그 턱을 못 올라갔다.
        var floorTopVotes = new System.Collections.Generic.Dictionary<int, float>();

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

            int bucket = Mathf.RoundToInt(bounds.max.y * 100f);   // 1cm 단위
            floorTopVotes.TryGetValue(bucket, out float votes);
            floorTopVotes[bucket] = votes + bounds.size.x * bounds.size.z;   // 면적 가중
        }

        if (!hasAny)
            return false;

        if (!hasFloor)
        {
            floorTopY = roomBounds.min.y;
            return true;
        }

        int bestBucket = 0; float bestVotes = -1f;
        var distribution = new System.Text.StringBuilder();
        foreach (var pair in floorTopVotes)
        {
            distribution.Append($"y{pair.Key / 100f:F2}×{pair.Value:F0}m² ");

            // 표가 같으면 낮은 쪽을 택한다 — 솟은 발판보다 보행면을 기준으로 두는 게 안전하다.
            if (pair.Value > bestVotes || (pair.Value == bestVotes && pair.Key < bestBucket))
            {
                bestVotes = pair.Value;
                bestBucket = pair.Key;
            }
        }

        floorTopY = bestBucket / 100f;
        Debug.Log($"[BossRoomAuthoring] 바닥 상단 Y 후보 분포 — {distribution}→ 채택 {floorTopY:F2} " +
                  $"(면적 최대. 솟은 발판·덮개가 끌어올리는 것을 막는다)");
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

    /// <summary>
    /// 바닥 아트에 틈이 있어도 빠지지 않게 하는 안전망 = 보스방의 실제 보행 콜라이더(바닥 타일은 콜라이더가 빠져 있다).
    /// 상단면을 바닥 표면에 맞추고, 경계 벽 바깥 면까지 덮는다. 있으면 그 자리(부모 유지 — 현재 Boss/ 아래)에서 갱신한다.
    /// </summary>
    static void BuildFloorNet(GameObject root, Bounds arena, float floorTopY, int groundLayer)
    {
        GameObject go = GetOrCreate(root, FloorColliderName, out bool created);
        go.layer = groundLayer;   // Ground — NavMesh 수집·GroundProbe·플레이어 접지 대상
        go.tag = GroundTag;
        SetRootLocalPosition(root, go.transform, Vector3.zero);

        // 같은 이름 BoxCollider 를 여럿 두지 않는다(첫 것만 쓰고 나머지는 지운다).
        BoxCollider[] boxes = go.GetComponents<BoxCollider>();
        BoxCollider box = boxes.Length > 0 ? boxes[0] : go.AddComponent<BoxCollider>();
        for (int i = 1; i < boxes.Length; i++) Object.DestroyImmediate(boxes[i]);

        box.isTrigger = false;
        box.center = new Vector3(arena.center.x, floorTopY - FloorNetThickness * 0.5f, arena.center.z);
        box.size = new Vector3(arena.size.x + BoundaryThickness * 2f, FloorNetThickness, arena.size.z + BoundaryThickness * 2f);

        Debug.Log($"[BossRoomAuthoring] {FloorColliderName}{(created ? " (새로 만듦)" : "")} — size {box.size}, " +
                  $"top Y={floorTopY:F2}, 부모 {go.transform.parent.name}");
    }

    /// <summary>
    /// 네 변을 막는 보이지 않는 경계. 렌더러·트리거 없이 Wall 태그+레이어 BoxCollider만 둔다.
    /// 안쪽 면 = 보이는 벽 안쪽 면(<paramref name="arena"/>). 모서리는 두께만큼 겹친다.
    /// 오브젝트는 지우지 않고 갱신한다(이름 Boundary_XMin/XMax/ZMin/ZMax 고정 — 23호가 이 이름을 읽는다).
    /// </summary>
    static void BuildBoundaries(GameObject root, Bounds arena, float floorTopY, int wallLayer)
    {
        Transform parent = FindDeep(root.transform, BoundariesName);
        if (parent == null)
        {
            parent = new GameObject(BoundariesName).transform;
            parent.SetParent(root.transform, false);
        }
        parent.gameObject.layer = wallLayer;
        parent.gameObject.tag = WallTag;
        parent.localPosition = Vector3.zero;
        parent.localRotation = Quaternion.identity;
        parent.localScale = Vector3.one;

        float centerY = floorTopY + BoundaryHeight * 0.5f;
        float t = BoundaryThickness;
        float half = t * 0.5f;
        float lenX = arena.size.x + t * 2f;   // Z 벽 길이 — 모서리 겹침
        float lenZ = arena.size.z + t * 2f;   // X 벽 길이 — 모서리 겹침

        SetWall(parent, wallLayer, "Boundary_XMin", new Vector3(arena.min.x - half, centerY, arena.center.z), new Vector3(t, BoundaryHeight, lenZ));
        SetWall(parent, wallLayer, "Boundary_XMax", new Vector3(arena.max.x + half, centerY, arena.center.z), new Vector3(t, BoundaryHeight, lenZ));
        SetWall(parent, wallLayer, "Boundary_ZMin", new Vector3(arena.center.x, centerY, arena.min.z - half), new Vector3(lenX, BoundaryHeight, t));
        SetWall(parent, wallLayer, "Boundary_ZMax", new Vector3(arena.center.x, centerY, arena.max.z + half), new Vector3(lenX, BoundaryHeight, t));

        // 규칙 밖 자식(예전 수동 조각 등)은 지운다 — 23호는 정확히 네 이름만 읽는다.
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            string n = parent.GetChild(i).name;
            if (n != "Boundary_XMin" && n != "Boundary_XMax" && n != "Boundary_ZMin" && n != "Boundary_ZMax")
                Object.DestroyImmediate(parent.GetChild(i).gameObject);
        }

        Debug.Log($"[BossRoomAuthoring] {BoundariesName} — 4면, 안쪽 면 X {arena.min.x:F3}~{arena.max.x:F3} / " +
                  $"Z {arena.min.z:F3}~{arena.max.z:F3}, 높이 {BoundaryHeight}, 태그·레이어 {WallTag}/{LayerMask.LayerToName(wallLayer)}");
    }

    // ── 벽 안쪽 면 실측 ──────────────────────────────────────────────────

    /// <summary>
    /// 보이는 지상층 벽 메시(이름에 wall — WallTransparency 제외)의 안쪽 면을 변마다 1mm 최빈값으로 잡는다.
    /// 얇은 축이 X 인 벽 = X 변(XMin/XMax), 얇은 축이 Z 인 벽 = Z 변. 모서리처럼 정사각에 가까운 조각은 투표에서 뺀다.
    /// 지하층 벽(바닥 아래)은 보행 경계가 아니라서 뺀다. 방 중심은 바닥 렌더러 바운즈 중심(없으면 벽 바운즈 중심).
    /// </summary>
    static bool TryMeasureWallInnerFaces(GameObject root, float floorTopY, out Bounds arena)
    {
        arena = default;
        var walls = new System.Collections.Generic.List<Bounds>();
        Bounds floorUnion = default, wallUnion = default;
        bool hasFloor = false, hasWall = false;

        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            Bounds b = ToRootLocalBounds(root.transform, r.bounds);
            if (LooksLikeWall(r.transform, root.transform))
            {
                if (b.max.y < floorTopY + 1f || b.min.y < floorTopY - 0.5f) continue;   // 지상층만
                walls.Add(b);
                if (!hasWall) { wallUnion = b; hasWall = true; } else wallUnion.Encapsulate(b);
            }
            else if (LooksLikeFloor(r))
            {
                if (!hasFloor) { floorUnion = b; hasFloor = true; } else floorUnion.Encapsulate(b);
            }
        }
        if (!hasWall) return false;
        Vector3 c = hasFloor ? floorUnion.center : wallUnion.center;

        var votes = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<int, int>>
        {
            ["XMin"] = new(), ["XMax"] = new(), ["ZMin"] = new(), ["ZMax"] = new(),
        };
        foreach (Bounds b in walls)
        {
            if (Mathf.Abs(b.size.x - b.size.z) < 0.5f) continue;   // 모서리·기둥
            if (b.size.x < b.size.z)
            {
                if (b.center.x < c.x) Vote(votes["XMin"], b.max.x); else Vote(votes["XMax"], b.min.x);
            }
            else
            {
                if (b.center.z < c.z) Vote(votes["ZMin"], b.max.z); else Vote(votes["ZMax"], b.min.z);
            }
        }

        if (!TryMode(votes["XMin"], out float minX) || !TryMode(votes["XMax"], out float maxX) ||
            !TryMode(votes["ZMin"], out float minZ) || !TryMode(votes["ZMax"], out float maxZ))
            return false;

        arena = new Bounds();
        arena.SetMinMax(new Vector3(minX, floorTopY, minZ), new Vector3(maxX, floorTopY, maxZ));
        return true;
    }

    static void Vote(System.Collections.Generic.Dictionary<int, int> table, float value)
    {
        int bucket = Mathf.RoundToInt(value * 1000f);   // 1mm 단위(현재 값 ±13.366 을 그대로 재현)
        table.TryGetValue(bucket, out int n);
        table[bucket] = n + 1;
    }

    static bool TryMode(System.Collections.Generic.Dictionary<int, int> table, out float value)
    {
        value = 0f;
        int best = int.MinValue, bestVotes = -1;
        foreach (var pair in table)
            if (pair.Value > bestVotes || (pair.Value == bestVotes && Mathf.Abs(pair.Key) < Mathf.Abs(best)))
            {
                bestVotes = pair.Value;
                best = pair.Key;
            }
        if (bestVotes < 0) return false;
        value = best / 1000f;
        return true;
    }

    static bool LooksLikeWall(Transform t, Transform root)
    {
        for (; t != null && t != root; t = t.parent)
        {
            string n = t.name.ToLowerInvariant();
            if (n.Contains("walltransparency")) return false;
            if (n.StartsWith("wall")) return true;   // wall_brick_* · walll_brick_corner* · Environment/Wall
        }
        return false;
    }

    // ── 찾기·만들기 (사용자 그룹 존중 — 깊이 무관, 중복 생성 금지) ─────────────────

    static Transform FindDeep(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t != root && t.name == name) return t;
        return null;
    }

    /// <summary>이름으로 깊이 무관하게 찾고, 없으면 Boss/ 그룹(없으면 루트) 아래에 만든다.</summary>
    static GameObject GetOrCreate(GameObject root, string name, out bool created)
    {
        Transform found = FindDeep(root.transform, name);
        created = found == null;
        if (found != null) return found.gameObject;

        Transform group = FindDeep(root.transform, BossGroupName);
        var go = new GameObject(name);
        go.transform.SetParent(group != null ? group : root.transform, false);
        return go;
    }

    /// <summary>부모가 무엇이든 루트 로컬 좌표로 위치를 맞춘다(그룹이 원점이 아니어도 결과가 같게).</summary>
    static void SetRootLocalPosition(GameObject root, Transform t, Vector3 rootLocal)
    {
        t.position = root.transform.TransformPoint(rootLocal);
        t.localRotation = Quaternion.identity;
    }

    static void SetWall(Transform parent, int layer, string name, Vector3 rootLocalCenter, Vector3 size)
    {
        Transform child = parent.Find(name);
        GameObject go = child != null ? child.gameObject : new GameObject(name);
        if (child == null) go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        go.layer = layer;
        go.tag = WallTag;

        foreach (Component comp in go.GetComponents<Component>())
            if (comp is Renderer || comp is MeshFilter) Object.DestroyImmediate(comp);

        BoxCollider[] boxes = go.GetComponents<BoxCollider>();
        BoxCollider box = boxes.Length > 0 ? boxes[0] : go.AddComponent<BoxCollider>();
        for (int i = 1; i < boxes.Length; i++) Object.DestroyImmediate(boxes[i]);
        box.isTrigger = false;
        // InvisibleBoundaries 가 루트 원점·항등이므로 center = 루트 로컬 좌표.
        box.center = parent.InverseTransformPoint(parent.root.TransformPoint(rootLocalCenter));
        box.size = size;
    }

    // ─── NavMesh 여유 띠 (2026-09-29 경석 · 팀장 승인) ─────────────────────────────
    // 맵 NavMesh 는 한 장(Humanoid 반경 0.5)이라 보스방 벽에서 0.5m 까지 보스 중심이 간다 → 23호 몸 캡슐(1.53)이
    // 투명벽 밖으로 ~1m 돌출 → 벽-보스 사이 플레이어 끼임 · 손 소켓이 경계 밖으로 나가 잡기 해제가 벽 안.
    // 보스방에는 보스 한 마리뿐이므로 **이 방만** 투명벽 안쪽에 Not Walkable 띠를 둘러 NavMesh 가장자리를 안쪽으로 민다.
    // 🔴 띠 폭 상한: 취약 넉백 벽 판정 = "보스 중심 + 몸 반경(1.53) ≥ 벽 안쪽 면"(TwentyThreeBoss.HitsArenaWall).
    //    가장자리가 벽에서 1.53 보다 멀어지면 벽 충돌이 **영영 성립하지 않는다.** 목표 가장자리 ≈ 1.5m.
    // ⚠️ 베이크 침식(반경 0.5)이 띠 경계에도 추가로 적용되는지는 미확인 — 적용되면 가장자리 = 폭 + 0.5, 아니면 = 폭.
    //    Play 시 `[23호] NavMesh 여유` 로그(보스 → 네 벽 방향 가장자리 거리)로 확인하고 폭을 맞춘다.
    const string NavMarginName = "NavMeshMargin";
    const float NavMarginWidth = 1.0f;
    const float NavMarginHeight = 4f;
    static readonly string[] NavMarginNames = { "Margin_XMin", "Margin_XMax", "Margin_ZMin", "Margin_ZMax" };

    [MenuItem("Tools/Map/Authoring/Build Boss Room NavMesh Margin")]
    public static void BuildBossRoomNavMeshMargin()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(BossRoomPath);
        try
        {
            if (!TryMeasure(root, out _, out float floorTopY))
            {
                Debug.LogError("[BossRoomAuthoring] 렌더러를 찾지 못해 바닥 높이를 계산할 수 없다.");
                return;
            }
            if (!BuildNavMeshMargin(root, floorTopY)) return;
            PrefabUtility.SaveAsPrefabAsset(root, BossRoomPath);
            Debug.Log("[BossRoomAuthoring] NavMesh 여유 띠 저장 완료.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // 경계는 **저작된 InvisibleBoundaries 안쪽 면**에서 읽는다(재실측하지 않는다 — 벽 판정과 같은 원본).
    static bool BuildNavMeshMargin(GameObject root, float floorTopY)
    {
        Transform boundaries = FindDeep(root.transform, BoundariesName);
        if (boundaries == null)
        {
            Debug.LogError($"[BossRoomAuthoring] {BoundariesName} 가 없다 — Rebuild Boss Room Bounds 를 먼저 돌릴 것.");
            return false;
        }

        float minX = 0f, maxX = 0f, minZ = 0f, maxZ = 0f;
        int found = 0;
        foreach (BoxCollider b in boundaries.GetComponentsInChildren<BoxCollider>(true))
        {
            Vector3 c = root.transform.InverseTransformPoint(b.transform.TransformPoint(b.center));
            Vector3 h = root.transform.InverseTransformVector(b.transform.TransformVector(b.size * 0.5f));
            switch (b.name)
            {
                case "Boundary_XMin": minX = c.x + Mathf.Abs(h.x); found |= 1; break;
                case "Boundary_XMax": maxX = c.x - Mathf.Abs(h.x); found |= 2; break;
                case "Boundary_ZMin": minZ = c.z + Mathf.Abs(h.z); found |= 4; break;
                case "Boundary_ZMax": maxZ = c.z - Mathf.Abs(h.z); found |= 8; break;
            }
        }
        if (found != 15)
        {
            Debug.LogError("[BossRoomAuthoring] Boundary_XMin/XMax/ZMin/ZMax 가 다 있지 않다 — 여유 띠를 만들지 않는다.");
            return false;
        }

        // 2026-10-13: 지우고 다시 만들면 값이 같아도 fileID 가 매번 바뀌어 prefab diff 가 생긴다 → 있으면 제자리 갱신.
        Transform existing = FindDeep(root.transform, NavMarginName);
        GameObject parent = existing != null ? existing.gameObject : new GameObject(NavMarginName);
        if (existing == null) parent.transform.SetParent(root.transform, false);
        // 띠 center 는 루트 로컬 값 → 부모를 루트와 같은 좌표계로 되돌린다(손으로 옮긴 위치·회전·스케일 무시).
        SetRootLocalPosition(root, parent.transform, Vector3.zero);
        parent.transform.localScale = Vector3.one;
        for (int i = parent.transform.childCount - 1; i >= 0; i--)
        {
            Transform c = parent.transform.GetChild(i);
            if (System.Array.IndexOf(NavMarginNames, c.name) < 0) Object.DestroyImmediate(c.gameObject);
        }

        int notWalkable = UnityEngine.AI.NavMesh.GetAreaFromName("Not Walkable");
        float w = NavMarginWidth, y = floorTopY, lenX = maxX - minX, lenZ = maxZ - minZ;
        float cx = (minX + maxX) * 0.5f, cz = (minZ + maxZ) * 0.5f;

        AddNavMargin(parent, "Margin_XMin", new Vector3(minX + w * 0.5f, y, cz), new Vector3(w, NavMarginHeight, lenZ), notWalkable);
        AddNavMargin(parent, "Margin_XMax", new Vector3(maxX - w * 0.5f, y, cz), new Vector3(w, NavMarginHeight, lenZ), notWalkable);
        AddNavMargin(parent, "Margin_ZMin", new Vector3(cx, y, minZ + w * 0.5f), new Vector3(lenX, NavMarginHeight, w), notWalkable);
        AddNavMargin(parent, "Margin_ZMax", new Vector3(cx, y, maxZ - w * 0.5f), new Vector3(lenX, NavMarginHeight, w), notWalkable);

        Debug.Log($"[BossRoomAuthoring] {NavMarginName} — 폭 {w}m · 안쪽 경계 X {minX:F2}~{maxX:F2} / Z {minZ:F2}~{maxZ:F2} · area {notWalkable}");
        return true;
    }

    static void AddNavMargin(GameObject parent, string name, Vector3 center, Vector3 size, int area)
    {
        Transform child = parent.transform.Find(name);
        GameObject go = child != null ? child.gameObject : new GameObject(name);
        if (child == null) go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        var vol = go.GetComponent<NavMeshModifierVolume>();
        if (vol == null) vol = go.AddComponent<NavMeshModifierVolume>();
        vol.center = center;
        vol.size = size;
        vol.area = area;
    }

    /// <summary>
    /// 텔레포트 도착 지점 3개와 보스 착지 지점. (승인 계획 Task 1)
    /// 🔴 2026-10-13: **이미 있으면 건드리지 않는다**(손으로 배치한 PlayerArrivePos1~3·Boss/BossLandingPoint 보존 —
    ///    예전엔 지우고 Player1~3 부채꼴로 다시 만들어 수동 배치가 사라졌고, BossLandingPoint 가 Boss/ 아래로 옮겨진 뒤엔
    ///    직속 Find 로 못 찾아 착지점이 둘이 됐다). 없을 때만 방 중심 기준으로 만든다.
    /// </summary>
    static void BuildReferencePoints(GameObject root, Bounds arena, float floorTopY)
    {
        Vector3 floorCenter = new Vector3(arena.center.x, floorTopY, arena.center.z);

        if (FindDeep(root.transform, ArrivalRootName) == null)
        {
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
            Debug.Log($"[BossRoomAuthoring] {ArrivalRootName} 새로 만듦 — 중심 {floorCenter}");
        }

        if (FindDeep(root.transform, BossLandingName) == null)
        {
            GameObject landing = GetOrCreate(root, BossLandingName, out _);
            SetRootLocalPosition(root, landing.transform, floorCenter);
            Debug.Log($"[BossRoomAuthoring] {BossLandingName} 새로 만듦 — 중심 {floorCenter}, 부모 {landing.transform.parent.name}");
        }
    }
}
