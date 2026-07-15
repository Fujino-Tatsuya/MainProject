using UnityEngine;
using System.Collections.Generic;

public class SpawnPoint : MonoBehaviour
{
    [Header("=== 스폰 포인트 정보 ===")]
    public int PointID;

    [Tooltip("이 포인트에서 생성 가능한 노드 티어 (1티어 전용, 2티어 전용 등)")]
    public NodeTier AllowedTier;

    [Header("=== 몬스터 스폰 위치 (인스펙터 인증) ===")]
    [Tooltip("이 노드 주변에서 몬스터가 스폰될 위치들. 실제 스폰은 추후 서버에서 처리.")]
    public List<Transform> MonsterSpawnPoints = new List<Transform>();

    [Header("=== 런타임 데이터 (제너레이터가 채움) ===")]
    [Tooltip("이번 생성에서 이미 노드/장애물이 할당되었는지")]
    public bool IsAssigned;
    public GeneratedNodeData NodeData;

    // 재생성 시 런타임 상태 초기화
    public void ResetRuntime()
    {
        IsAssigned = false;
        NodeData = default;
    }

/*    private void OnDrawGizmos()
    {
        // 에디터에서 식별하기 쉽도록 티어별로 색상을 다르게 그려줍니다.
        switch (AllowedTier)
        {
            case NodeTier.Tier1_Large: Gizmos.color = Color.red; break;
            case NodeTier.Tier2_Medium: Gizmos.color = Color.yellow; break;
            case NodeTier.Tier3_Small: Gizmos.color = Color.green; break;
        }

        Gizmos.DrawWireSphere(transform.position, 0.5f);
    }*/
}

