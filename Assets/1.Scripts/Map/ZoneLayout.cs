using UnityEngine;
using System.Collections.Generic;

// 마커 하나에 어떤 몬스터를 세울지 개별 지정한다.
//
// 왜 필요한가 — 이전에는 존 단위 MonsterGroupID 하나로 ResolveMonsterPrefab 을 한 번만 돌려
// **모든 마커에 같은 몬스터**를 세웠다(예: ZoneL_typeA = 마커 5개 전부 ChompBot).
// 마커별로 다른 몬스터를 섞으려면 지정이 마커 단위여야 한다.
[System.Serializable]
public struct MonsterSpawnEntry
{
    [Tooltip("스폰 위치 마커 (존 프리팹의 자식 transform).")]
    public Transform Marker;

    [Tooltip("이 마커에서 스폰할 몬스터 그룹 ID(MapGenConfig.MonsterGroups). " +
             "-1이면 존의 MonsterGroupID 를 그대로 쓴다.")]
    public int MonsterGroupID;
}

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
    [Tooltip("존 단위 기본 몬스터 그룹 ID. MonsterSpawnEntries 의 개별 ID가 -1일 때 이 값이 쓰인다.")]
    public int MonsterGroupID = -1;
    [Tooltip("몬스터 스폰 위치 마커 (자식 transform). ⚠️ 구버전 경로 — MonsterSpawnEntries 가 비었을 때만 쓰인다.")]
    public List<Transform> MonsterSpawnPoints = new List<Transform>();

    [Tooltip("마커별 몬스터 지정. 여기에 항목이 하나라도 있으면 이 목록이 권위이고 " +
             "MonsterSpawnPoints 는 무시된다. 비어 있으면 기존 동작(모든 마커에 MonsterGroupID 하나)을 유지한다. " +
             "손으로 채운다 — 구 마커에서 옮겨주던 마이그레이션 메뉴는 소진돼 2026-08-18 에 삭제했다.")]
    public List<MonsterSpawnEntry> MonsterSpawnEntries = new List<MonsterSpawnEntry>();

    /// <summary>
    /// 실제 스폰에 쓸 마커 목록을 하나로 정규화한다. Entries 가 비면 구버전 경로로 폴백한다.
    /// 저작 데이터를 잃지 않기 위해 두 경로를 모두 살려 둔다(마커 27개 유실 재발 방지).
    /// </summary>
    public IEnumerable<MonsterSpawnEntry> ResolveSpawnEntries()
    {
        if (MonsterSpawnEntries != null && MonsterSpawnEntries.Count > 0)
        {
            foreach (MonsterSpawnEntry entry in MonsterSpawnEntries)
                yield return entry;
            yield break;
        }

        if (MonsterSpawnPoints == null)
            yield break;

        foreach (Transform marker in MonsterSpawnPoints)
            yield return new MonsterSpawnEntry { Marker = marker, MonsterGroupID = -1 };
    }

    [Header("=== 노드 (존 내부 2/3티어) ===")]
    [Tooltip("이 존에 배치된 노드 마커들. MapContentSpawner가 노드별로 스폰/처리. 비면 존 단위 MonsterSpawnPoints로 폴백.")]
    public List<NodeMarker> Nodes = new List<NodeMarker>();

    private void OnDrawGizmosSelected()
    {
        // 정규화 경로를 그린다 — Entries 로 전환한 뒤에도 기즈모가 비지 않게.
        foreach (MonsterSpawnEntry entry in ResolveSpawnEntries())
        {
            if (entry.Marker == null) continue;

            // 개별 지정(≥0)은 초록, 존 기본값(-1) 상속은 빨강 — 저작 누락이 눈에 보이게.
            Gizmos.color = entry.MonsterGroupID >= 0 ? Color.green : Color.red;
            Gizmos.DrawWireSphere(entry.Marker.position, 0.3f);
        }
    }
}
