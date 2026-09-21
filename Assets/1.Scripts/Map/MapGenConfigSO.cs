using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "MapGenConfig", menuName = "VeyTrace/Map Generator Config")]
public class MapGenConfigSO : ScriptableObject
{
    // ⚠️ 2026-09-21 SO 전수조사에서 **유령 필드**로 확인돼 주석 처리했다.
    //    (선언 파일 밖 참조 0 · 프로퍼티 경유도 없음. 헤더가 말하는 "구 워크플로"가 실제로 사라진 것.)
    //    되살리려면 소비처(MapSpawnPointScatter 계열)를 먼저 만들 것.
    //    마지막 저작값 — Tier1 30 / Tier2 10 / Tier3 5
    //
    // [Header("=== 스폰포인트 분산 배제 반경 (구 워크플로 — MapSpawnPointScatter용) ===")]
    // public float Tier1ExclusionRadius = 15f;
    // public float Tier2ExclusionRadius = 10f;
    // public float Tier3ExclusionRadius = 5f;

    [Header("=== 몬스터 그룹 풀 (MonsterGroupID → 프리팹) ===")]
    public List<MonsterGroupData> MonsterGroups;
}
