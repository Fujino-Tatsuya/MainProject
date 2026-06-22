using UnityEngine;

[CreateAssetMenu(fileName = "ZoneDefinition", menuName = "VeyTrace/Zone Definition")]
public class ZoneDefinitionSO : ScriptableObject
{
    [Header("=== 구역 기본 정보 ===")]
    public int ZoneID;
    public string ZoneName;

    [Header("=== 구역 등급 및 역할 ===")]
    [Tooltip("A등급(1티어 포함) / B등급(2티어 이하). 퀘스트·보스·스폰 역할은 런타임에 후보 중 랜덤 배정.")]
    public ZoneGrade DefaultGrade = ZoneGrade.A_UpToTier1;

    [Tooltip("퀘스트 구역 후보 (맵 당 후보 2곳)")]
    public bool IsQuestZoneCandidate = false;

    [Tooltip("플레이어 스폰 위치 후보 (맵 당 후보 2곳)")]
    public bool IsPlayerSpawnCandidate = false;

    [Tooltip("보스 방 입구 후보 (맵 당 후보 3곳)")]
    public bool IsBossGateCandidate = false;

    // 스폰 포인트는 씬의 SpawnPoint.ParentZone 이 이 SO를 가리켜 연결한다.
    // (SO 에셋은 씬 오브젝트 참조를 직렬화할 수 없으므로 여기서 리스트로 들지 않는다.
    //  생성기가 런타임에 씬을 스캔해 ParentZone 별로 그룹핑한다 — MapGenerator.GatherSpawnPoints)
}
