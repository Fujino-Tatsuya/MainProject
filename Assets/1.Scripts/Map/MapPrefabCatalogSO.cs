using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "MapPrefabCatalog", menuName = "VeyTrace/Map Prefab Catalog")]
public class MapPrefabCatalogSO : ScriptableObject
{
    // 생성기는 (Tier, Content)에 맞는 풀에서 변형(variant) 인덱스를 시드 기반으로 선택한다.
    // 결과는 GeneratedNodeData.PrefabId 에 인덱스로 기록되고, 서버 스폰 시 동일 카탈로그로 프리팹을 해석한다.
    // 벽·바닥은 Stage1 프리팹에 포함된 고정 지형이라 여기 두지 않는다.

    [Header("=== 1티어 노드(대형) ===")]
    [Tooltip("node_factory / node_hospitalroom / node_operationroom")]
    public List<GameObject> Tier1Nodes = new List<GameObject>();

    [Header("=== 2티어(중형) ===")]
    [Tooltip("노드: Pallet / ConcreteFrame_Pillar / Shipping_Container")]
    public List<GameObject> Tier2Props = new List<GameObject>();
    [Tooltip("장애물: 큐브 프리미티브 (다른 2티어급 크기)")]
    public List<GameObject> Tier2Obstacles = new List<GameObject>();

    [Header("=== 3티어(소형) ===")]
    [Tooltip("장애물: 서클(구) 프리미티브 (다른 3티어급 크기)")]
    public List<GameObject> Tier3Obstacles = new List<GameObject>();
    [Tooltip("회복/순간이동/버프 — 현재 Synty 스택 플레이스홀더, 실제 에셋 나오면 교체")]
    public List<GameObject> Tier3Recovery = new List<GameObject>();
    public List<GameObject> Tier3Teleport = new List<GameObject>();
    public List<GameObject> Tier3Buff = new List<GameObject>();

    [Header("=== 플레이어 스폰 영역 구조물 ===")]
    [Tooltip("node_spownpoint — 스폰으로 뽑힌 영역 중앙에 배치")]
    public GameObject SpawnAreaStructure;

    [Header("=== 인스턴스 스케일 / 회전 보정 (FBX) ===")]
    [Tooltip("1티어 노드(FBX) 스케일 — 맵에 맞추려면 1300")]
    public float Tier1Scale = 1300f;
    [Tooltip("스폰 구조물(FBX) 스케일")]
    public float SpawnStructureScale = 100f;
    [Tooltip("1티어 FBX 축 보정 회전 (X -90)")]
    public Vector3 Tier1Rotation = new Vector3(-90f, 0f, 0f);
    [Tooltip("스폰 구조물 FBX 축 보정 회전")]
    public Vector3 SpawnStructureRotation = new Vector3(-90f, 0f, 0f);
    [Tooltip("3티어(소형) 오브젝트 스케일 배율 — 너무 작아서 2.5배")]
    public float Tier3Scale = 2.5f;

    [Header("=== 역할 영역 마커 (Quad — 이미지 나오면 머티리얼 교체) ===")]
    [Tooltip("보스방으로 뽑힌 존 중앙 바닥 표시")]
    public GameObject BossAreaMarker;
    [Tooltip("스폰으로 뽑힌 존 중앙 바닥 표시")]
    public GameObject SpawnAreaMarker;
    [Tooltip("퀘스트로 뽑힌 존 중앙 바닥 표시")]
    public GameObject QuestAreaMarker;
    [Tooltip("마커 한 변 크기")]
    public float AreaMarkerSize = 10f;

    [Header("=== 고정 지형 (맵 지오메트리 빌드 툴용) ===")]
    [Tooltip("바닥 타일 — 존별 하나씩 순환 사용")]
    public List<GameObject> FloorTiles = new List<GameObject>();
    [Tooltip("외벽 — 존별 한 텍스처로 통일(영역 구분). Concrete_Wall_01/02")]
    public List<GameObject> WallFences = new List<GameObject>();
    [Tooltip("통로 입구용 문 달린 벽 — House_Wall_Door_03")]
    public GameObject WallDoor;

    // (Tier, Content)에 해당하는 프리팹 풀 반환
    public List<GameObject> GetPool(NodeTier tier, NodeContentType content)
    {
        switch (tier)
        {
            case NodeTier.Tier1_Large:
                return Tier1Nodes;
            case NodeTier.Tier2_Medium:
                return content == NodeContentType.Obstacle ? Tier2Obstacles : Tier2Props;
            case NodeTier.Tier3_Small:
                switch (content)
                {
                    case NodeContentType.Recovery: return Tier3Recovery;
                    case NodeContentType.Teleport: return Tier3Teleport;
                    case NodeContentType.Buff:     return Tier3Buff;
                    default:                       return Tier3Obstacles;
                }
        }
        return null;
    }

    // 풀에서 변형 인덱스 선택 (풀이 비어있으면 -1)
    public int PickVariantIndex(System.Random rng, NodeTier tier, NodeContentType content)
    {
        List<GameObject> pool = GetPool(tier, content);
        if (pool == null || pool.Count == 0) return -1;
        return rng.Next(pool.Count);
    }

    // 인덱스로 실제 프리팹 해석 (서버 스폰 단계에서 사용 예정)
    public GameObject GetPrefab(NodeTier tier, NodeContentType content, int variantIndex)
    {
        List<GameObject> pool = GetPool(tier, content);
        if (pool == null || variantIndex < 0 || variantIndex >= pool.Count) return null;
        return pool[variantIndex];
    }
}
