using UnityEngine;
using System.Collections.Generic;

// 존(ZoneLayout) 내부에 배치되는 노드 단위 (2/3티어).
// 디자이너가 위치/티어/콘텐츠타입/몬스터/클리어조건을 지정 → MapContentSpawner가 읽어 스폰·처리.
// (비네트워크 시각/데이터. 몬스터만 서버에서 NetworkObject로 스폰)
public class NodeMarker : MonoBehaviour
{
    [Header("=== 분류 ===")]
    public NodeTier Tier = NodeTier.Tier2_Medium;
    public NodeContentType ContentType = NodeContentType.CombatNode;

    [Header("=== 몬스터 (CombatNode 등) ===")]
    [Tooltip("이 노드에서 스폰할 몬스터 그룹 ID (MapGenConfig.MonsterGroups). -1 = 없음.")]
    public int MonsterGroupID = -1;
    [Tooltip("스폰된 몬스터의 행동.")]
    public MonsterBehavior Behavior = MonsterBehavior.Idle;
    [Tooltip("몬스터 스폰 위치 마커 (자식 transform).")]
    public List<Transform> MonsterSpawnPoints = new List<Transform>();

    [Header("=== 클리어 조건 ===")]
    [Tooltip("이 노드를 '클리어'로 간주하는 조건. None=조건없음 / KillAll=전멸 / KeyUnlock=열쇠 소모.")]
    public ClearCondition Clear = ClearCondition.None;

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = ContentType == NodeContentType.CombatNode ? new Color(1f, 0.3f, 0.3f) : new Color(1f, 0.85f, 0.2f);
        Gizmos.DrawWireCube(transform.position, Vector3.one);
        Gizmos.color = Color.magenta;
        if (MonsterSpawnPoints != null)
            foreach (var m in MonsterSpawnPoints)
                if (m != null) Gizmos.DrawWireSphere(m.position, 0.3f);
    }
}
