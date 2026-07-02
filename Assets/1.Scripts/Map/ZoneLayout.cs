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

    [Header("=== 출입구 (임포터 자동 감지 — 오탐 시 수동 보정) ===")]
    [Tooltip("로컬 기준 각 변의 출입구(벽 트임) 여부. N=+Z, E=+X, S=-Z, W=-X. 배치 시 회전 매칭에 사용.")]
    public bool OpenN;
    public bool OpenE;
    public bool OpenS;
    public bool OpenW;

    // dir: 0=N(+Z) 1=E(+X) 2=S(-Z) 3=W(-X)
    public bool HasOpening(int dir) => dir switch { 0 => OpenN, 1 => OpenE, 2 => OpenS, _ => OpenW };
    public int OpeningCount => (OpenN ? 1 : 0) + (OpenE ? 1 : 0) + (OpenS ? 1 : 0) + (OpenW ? 1 : 0);

    [Header("=== 몬스터 ===")]
    [Tooltip("이 존에서 스폰할 몬스터 그룹 ID. 실제 스폰 위치는 MonsterSpawnPoints.")]
    public int MonsterGroupID = -1;
    [Tooltip("몬스터 스폰 위치 마커 (자식 transform).")]
    public List<Transform> MonsterSpawnPoints = new List<Transform>();

    [Header("=== 연결 소켓 (출입구) ===")]
    [Tooltip("같은 크기 존끼리 통일된 로컬 좌표를 따라야 함. Stage1 다리와 정렬 검증/디버그용.")]
    public List<Transform> Sockets = new List<Transform>();

    [Header("=== 노드 (존 내부 2/3티어) ===")]
    [Tooltip("이 존에 배치된 노드 마커들. MapContentSpawner가 노드별로 스폰/처리. 비면 존 단위 MonsterSpawnPoints로 폴백.")]
    public List<NodeMarker> Nodes = new List<NodeMarker>();

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        foreach (var s in Sockets)
            if (s != null) Gizmos.DrawWireSphere(s.position, 0.4f);
        Gizmos.color = Color.red;
        foreach (var m in MonsterSpawnPoints)
            if (m != null) Gizmos.DrawWireSphere(m.position, 0.3f);
    }
}
