using UnityEngine;

// 한 영역의 경계(bounds)와 티어별 스폰 포인트 개수를 정의한다.
// 스폰 포인트 자동 분산 배치 툴(MapSpawnPointScatter)이 이 정보를 읽어 SpawnPoint를 생성한다.
// 씬에 영역마다 1개씩 배치하고 Zone(해당 ZoneDefinition)을 지정한다.
public class ZoneVolume : MonoBehaviour
{
    public ZoneDefinitionSO Zone;

    [Tooltip("영역 경계 크기 (transform 위치 중심)")]
    public Vector3 Size = new Vector3(20f, 1f, 20f);

    [Header("=== 티어별 생성할 스폰 포인트 수 ===")]
    [Tooltip("1티어 = 존당 1개 (A등급만). B등급은 0.")]
    public int Tier1Count = 0;
    public int Tier2Count = 3;
    public int Tier3Count = 4;

    public Bounds GetBounds()
    {
        return new Bounds(transform.position, Size);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.15f);
        Gizmos.DrawCube(transform.position, Size);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(transform.position, Size);
    }
}
