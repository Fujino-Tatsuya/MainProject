using UnityEngine;
using System.Collections.Generic;

// 미리 레벨디자인된 존 프리팹의 루트.
// 바닥/벽/테마/노드/몬스터 스폰 위치를 자식으로 포함한 완성 디자인.
// 생성기가 (Size, Difficulty, Role)로 골라 ZoneSlot 앵커에 Instantiate 한다.
public class ZoneLayout : MonoBehaviour
{
    [Header("=== 분류 태그 ===")]
    public ZoneSize Size;
    public ZoneRole Role = ZoneRole.Combat;
    [Tooltip("난이도 밴드 (0 = 기본). 같은 Size 전투 풀 분리에 사용. 역할 존은 무시.")]
    public int Difficulty;

    [Header("=== 테마 (참고/검증용) ===")]
    public string ThemeName;   // 예: Factory / Hospital / Containers

    [Header("=== 몬스터 ===")]
    [Tooltip("이 존에서 스폰할 몬스터 그룹 ID. 실제 스폰 위치는 MonsterSpawnPoints.")]
    public int MonsterGroupID = -1;
    [Tooltip("몬스터 스폰 위치 마커 (자식 transform).")]
    public List<Transform> MonsterSpawnPoints = new List<Transform>();

    [Header("=== 노드 (존 내부 2/3티어) ===")]
    [Tooltip("이 존에 배치된 노드 마커들. MapContentSpawner가 노드별로 스폰/처리. 비면 존 단위 MonsterSpawnPoints로 폴백.")]
    public List<NodeMarker> Nodes = new List<NodeMarker>();

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        foreach (var m in MonsterSpawnPoints)
            if (m != null) Gizmos.DrawWireSphere(m.position, 0.3f);
    }
}
