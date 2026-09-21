using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "MapPrefabCatalog", menuName = "VeyTrace/Map Prefab Catalog")]
public class MapPrefabCatalogSO : ScriptableObject
{
    // 생성기는 (Tier, Content)에 맞는 풀에서 변형(variant) 인덱스를 시드 기반으로 선택한다.
    // 결과는 GeneratedNodeData.PrefabId 에 인덱스로 기록되고, 서버 스폰 시 동일 카탈로그로 프리팹을 해석한다.
    // 벽·바닥은 Stage1 프리팹에 포함된 고정 지형이라 여기 두지 않는다.

    // ══════════════════════════════════════════════════════════════════════════════
    // 🔴 복원용 원본 스냅샷 (2026-09-21, Codex 4차 교차검증 지적 반영)
    //
    //    아래 필드들을 주석 처리했으므로 Unity 가 다음 직렬화에서 에셋의 참조를 **버린다.**
    //    이름·개수만으로는 복원할 수 없어서 `{fileID, guid, type}` 과 **배열 순서**를 그대로 남긴다.
    //
    //    소유 에셋 : Assets/50.Art/MapGen/MapObj/MapZoonSettingObj/MapPrefabCatalog.asset
    //    SVN 리비전: r316  (이 에셋은 git 이 아니라 SVN 관리다)
    //
    //    SpawnAreaStructure: {fileID: 919132149155446097, guid: 5156e85384a1255478aff647be127adf, type: 3}
    //    BossAreaMarker:     {fileID: 6257232294707993470, guid: 53d1d6d4ed204b449ba71955773096c0, type: 3}
    //    SpawnAreaMarker:    {fileID: 4836647589428915071, guid: 03055b4f319e2354695744c8240c8509, type: 3}
    //    QuestAreaMarker:    {fileID: 6894159624124694603, guid: 39f7e1bbdd2fe78408e6c91e7a5338e2, type: 3}
    //    WallDoor:           {fileID: 1366956832668916,   guid: 5a9a03aee9fc2da469081e7616dc19bb, type: 3}
    //
    //    FloorTiles: (순서 유지)
    //      - {fileID: 1762945973701536, guid: 27e2f0253f3de9a4baa6aba994ff3eb7, type: 3}
    //      - {fileID: 1139298442579984, guid: 69bfa6c642d9c9846baa1c4024f377f3, type: 3}
    //      - {fileID: 1507543799964052, guid: 56a7df5d71b7dc84ba419747c924ffc6, type: 3}
    //      - {fileID: 1365389252355666, guid: c586fddde79bf5f4aba2e6a5cdf2338e, type: 3}
    //
    //    WallFences: (순서 유지)
    //      - {fileID: 1396626323766674, guid: f7572585f8e47874fa6cf0f8f60a7adb, type: 3}
    //      - {fileID: 1319772559360156, guid: 70a1ed1496258cf478966b30ae12c1f1, type: 3}
    //
    //    Tier1Nodes: (순서 유지)
    //      - {fileID: 919132149155446097, guid: 9696d4bfd643f45488bac0b4c62108dd, type: 3}
    //      - {fileID: 919132149155446097, guid: c40b4a8908ed189449502ee298518e1e, type: 3}
    //      - {fileID: 919132149155446097, guid: fdb11c4c977e0094a8cfd5fdf3862823, type: 3}
    //    Tier2Props: (순서 유지)
    //      - {fileID: 1045472623685520, guid: 41430ccb06114c04bb00c0175c01c88d, type: 3}
    //      - {fileID: 1957021531820338, guid: 7cca0ece1b5e5374991c1edafd5314b2, type: 3}
    //      - {fileID: 1249707538929122, guid: 5be022cbfecfcf8478a5658a5142f700, type: 3}
    //    Tier2Obstacles: - {fileID: 1402279105088362049, guid: a2828531b1e34f94a8dd016b277d4cfc, type: 3}
    //    Tier3Obstacles: - {fileID: 7446573032957620201, guid: 78aa47cf2a4a3ed439549a76b081bbc0, type: 3}
    //    Tier3Recovery:  - {fileID: 1187625364453610,   guid: c56b7bbe609f8bc4bb153e1e148fabcf, type: 3}
    //    Tier3Teleport:  - {fileID: 1996425905810576,   guid: 0c2869721262ac6429af5bbef6ebeb56, type: 3}
    //    Tier3Buff:      - {fileID: 1690727760587702,   guid: f3401d1420c933746803e19506814fc8, type: 3}
    //
    //    ⚠️ guid 대부분이 **현재 프로젝트에서 해석되지 않는다**(사라진 에셋). 되살리려면
    //       그 프리팹들을 SVN 이력에서 먼저 복구해야 한다.
    // ══════════════════════════════════════════════════════════════════════════════

    // 🔴 2026-09-21 전수조사 보고 — **아래 풀 4종과 맨 아래 접근자 3개(GetPool / PickVariantIndex /
    //    GetPrefab)도 프로젝트 전체에서 참조 0 이다.** 즉 이 클래스에서 실제로 살아 있는 것은
    //    **오버뷰/미니맵 아이콘 Texture2D 3개뿐**이고, 맵 생성은 ZoneLayout 경로로 옮겨간 것으로 보인다.
    //    ⚠️ 필드가 아니라 **공개 메서드까지 들어내는 것**이라 이번 정리에서는 건드리지 않았다.
    //    맵 생성 재개 계획이 없다면 이 블록 전체가 삭제 후보다(팀장 판단).

    // ⚠️ 2026-09-21 정리 — 맵 생성이 ZoneLayout 경로로 옮겨가며 **구버전으로 남은 카탈로그**다.
    //    풀 7종과 접근자 3개(GetPool/PickVariantIndex/GetPrefab) 전부 참조 0 (Codex 4차 확인).
    //    원본 참조는 위 스냅샷 블록에 보존돼 있다.
    // [Header("=== 1티어 노드(대형) ===")]
    // [Tooltip("node_factory / node_hospitalroom / node_operationroom")]
    // public List<GameObject> Tier1Nodes = new List<GameObject>();

    // [Header("=== 2티어(중형) ===")]
    // [Tooltip("노드: Pallet / ConcreteFrame_Pillar / Shipping_Container")]
    // public List<GameObject> Tier2Props = new List<GameObject>();
    // [Tooltip("장애물: 큐브 프리미티브 (다른 2티어급 크기)")]
    // public List<GameObject> Tier2Obstacles = new List<GameObject>();

    // [Header("=== 3티어(소형) ===")]
    // [Tooltip("장애물: 서클(구) 프리미티브 (다른 3티어급 크기)")]
    // public List<GameObject> Tier3Obstacles = new List<GameObject>();
    // [Tooltip("회복/순간이동/버프 — 현재 Synty 스택 플레이스홀더, 실제 에셋 나오면 교체")]
    // public List<GameObject> Tier3Recovery = new List<GameObject>();
    // public List<GameObject> Tier3Teleport = new List<GameObject>();
    // public List<GameObject> Tier3Buff = new List<GameObject>();

    // ⚠️ 2026-09-21 SO 전수조사 — **유령 필드**(참조 0)라 주석 처리했다.
    //    주석 처리하면 Unity 가 에셋의 값·배선도 버리므로 마지막 저작값을 남긴다.
    //    배선돼 있던 프리팹 1개는 **guid 가 이미 해석 불가**(프로젝트에서 사라진 에셋)였다.
    // [Header("=== 플레이어 스폰 영역 구조물 ===")]
    // [Tooltip("node_spownpoint — 스폰으로 뽑힌 영역 중앙에 배치")]
    // public GameObject SpawnAreaStructure;

    // ⚠️ 2026-09-21 SO 전수조사 — **유령 필드**(참조 0)라 주석 처리했다.
    //    주석 처리하면 Unity 가 에셋의 값·배선도 버리므로 마지막 저작값을 남긴다.
    //    마지막 저작값 — Tier1Scale 1300 / SpawnStructureScale 100 / Tier3Scale 2.5 /
    //    Tier1Rotation (-90,0,0) / SpawnStructureRotation (-90,0,0)
    // [Header("=== 인스턴스 스케일 / 회전 보정 (FBX) ===")]
    // [Tooltip("1티어 노드(FBX) 스케일 — 맵에 맞추려면 1300")]
    // public float Tier1Scale = 1300f;
    // [Tooltip("스폰 구조물(FBX) 스케일")]
    // public float SpawnStructureScale = 100f;
    // [Tooltip("1티어 FBX 축 보정 회전 (X -90)")]
    // public Vector3 Tier1Rotation = new Vector3(-90f, 0f, 0f);
    // [Tooltip("스폰 구조물 FBX 축 보정 회전")]
    // public Vector3 SpawnStructureRotation = new Vector3(-90f, 0f, 0f);
    // [Tooltip("3티어(소형) 오브젝트 스케일 배율 — 너무 작아서 2.5배")]
    // public float Tier3Scale = 2.5f;

    // ⚠️ 2026-09-21 SO 전수조사 — **유령 필드**(참조 0)라 주석 처리했다.
    //    주석 처리하면 Unity 가 에셋의 값·배선도 버리므로 마지막 저작값을 남긴다.
    //    배선돼 있던 프리팹 — Marker_BossArea / Marker_SpawnArea / Marker_QuestArea, 크기 20.
    //    ⚠️ 미니맵/오버뷰가 쓰는 아이콘은 아래 Texture2D 3종이다. 이 마커(바닥 Quad)와 다른 것이다.
    // [Header("=== 역할 영역 마커 (Quad — 이미지 나오면 머티리얼 교체) ===")]
    // [Tooltip("보스방으로 뽑힌 존 중앙 바닥 표시")]
    // public GameObject BossAreaMarker;
    // [Tooltip("스폰으로 뽑힌 존 중앙 바닥 표시")]
    // public GameObject SpawnAreaMarker;
    // [Tooltip("퀘스트로 뽑힌 존 중앙 바닥 표시")]
    // public GameObject QuestAreaMarker;
    // [Tooltip("마커 한 변 크기")]
    // public float AreaMarkerSize = 20f;

    [Header("=== 오버뷰 UI 아이콘 (Resources 밖으로 이동 → 직렬화 참조, 빌드 안전) ===")]
    public Texture2D BossIcon;
    public Texture2D SpawnIcon;
    public Texture2D QuestIcon;

    // ⚠️ 2026-09-21 SO 전수조사 — **유령 필드**(참조 0)라 주석 처리했다.
    //    주석 처리하면 Unity 가 에셋의 값·배선도 버리므로 마지막 저작값을 남긴다.
    //    FloorTiles 4개 · WallFences 2개 · WallDoor 1개가 배선돼 있었으나 **guid 가 전부 해석 불가**
    //    (프로젝트에 없는 에셋). 빌드 툴 자체가 사라진 흔적이다.
    // [Header("=== 고정 지형 (맵 지오메트리 빌드 툴용) ===")]
    // [Tooltip("바닥 타일 — 존별 하나씩 순환 사용")]
    // public List<GameObject> FloorTiles = new List<GameObject>();
    // [Tooltip("외벽 — 존별 한 텍스처로 통일(영역 구분). Concrete_Wall_01/02")]
    // public List<GameObject> WallFences = new List<GameObject>();
    // [Tooltip("통로 입구용 문 달린 벽 — House_Wall_Door_03")]
    // public GameObject WallDoor;

    // ⚠️ 2026-09-21 정리 — 위 풀들과 함께 죽은 접근자 3개. 외부 호출·문자열 바인딩 0 (Codex 4차 확인).
    //    맵 생성을 이 카탈로그로 되살릴 때 풀 선언과 같이 복구할 것.
    // // (Tier, Content)에 해당하는 프리팹 풀 반환
    // public List<GameObject> GetPool(NodeTier tier, NodeContentType content)
    // {
    // switch (tier)
    // {
    // case NodeTier.Tier1_Large:
    // return Tier1Nodes;
    // case NodeTier.Tier2_Medium:
    // return content == NodeContentType.Obstacle ? Tier2Obstacles : Tier2Props;
    // case NodeTier.Tier3_Small:
    // switch (content)
    // {
    // case NodeContentType.Recovery: return Tier3Recovery;
    // case NodeContentType.Teleport: return Tier3Teleport;
    // case NodeContentType.Buff:     return Tier3Buff;
    // default:                       return Tier3Obstacles;
    // }
    // }
    // return null;
    // }
    //
    // // 풀에서 변형 인덱스 선택 (풀이 비어있으면 -1)
    // public int PickVariantIndex(System.Random rng, NodeTier tier, NodeContentType content)
    // {
    // List<GameObject> pool = GetPool(tier, content);
    // if (pool == null || pool.Count == 0) return -1;
    // return rng.Next(pool.Count);
    // }
    //
    // // 인덱스로 실제 프리팹 해석 (서버 스폰 단계에서 사용 예정)
    // public GameObject GetPrefab(NodeTier tier, NodeContentType content, int variantIndex)
    // {
    // List<GameObject> pool = GetPool(tier, content);
    // if (pool == null || variantIndex < 0 || variantIndex >= pool.Count) return null;
    // return pool[variantIndex];
    // }
}
