using UnityEngine;

// Stage1 스켈레톤에 배치되는 존 슬롯 앵커.
// 생성기가 역할을 배정하고, 이 transform(position/rotation)에 선택된 ZoneLayout 프리팹을 Instantiate 한다.
// (기존 ZoneVolume/ZoneDefinitionSO + SpawnPoint 절차배치 모델을 대체)
public class ZoneSlot : MonoBehaviour
{
    [Header("=== 슬롯 정의 ===")]
    public int SlotID;
    public ZoneSize Size;
    [Tooltip("오버뷰 UI/검증용 풋프린트(가로 x, 세로 z). 실제 비주얼은 배치되는 프리팹이 가짐.")]
    public Vector2 Footprint = new Vector2(20f, 20f);

    [Header("=== 역할 후보 플래그 (배정 가능 역할) ===")]
    public bool IsQuestCandidate;
    public bool IsBossCandidate;
    public bool IsSpawnCandidate;

    [Header("=== 런타임 (생성기가 채움) ===")]
    public ZoneRole AssignedRole = ZoneRole.Combat;
    public bool IsFilled;

    public void ResetRuntime()
    {
        AssignedRole = ZoneRole.Combat;
        IsFilled = false;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Size switch
        {
            ZoneSize.Large  => new Color(1f, 0.3f, 0.3f),
            ZoneSize.Medium => new Color(1f, 0.9f, 0.3f),
            ZoneSize.Small  => new Color(0.3f, 1f, 0.4f),
            _ => Color.white
        };
        Gizmos.DrawWireCube(transform.position, Vector3.one * 2f);
        // 방향 표시 (앵커 forward = 프리팹 배치 방향)
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 2f);
    }
}
