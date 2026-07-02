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

    [Header("=== 다리 연결 방향 (ZoneWiring이 연결 그래프에서 채움, 월드 기준) ===")]
    [Tooltip("이 슬롯에 붙는 다리 개수(월드 방향별). 회전 매칭이 개방변으로 최대한 많은 다리를 커버하도록 사용.")]
    public int ConnN;
    public int ConnE;
    public int ConnS;
    public int ConnW;

    // dir: 0=N(+Z) 1=E(+X) 2=S(-Z) 3=W(-X)
    public int ConnCount(int dir) => dir switch { 0 => ConnN, 1 => ConnE, 2 => ConnS, _ => ConnW };
    public bool HasConn(int dir) => ConnCount(dir) > 0;

    [Tooltip("벽 변으로 붙는 다리 입구(월드 xz, w=변 방향 0~3). 존 스폰 시 이 지점과 겹치는 벽 조각을 삭제해 통로를 뚫는다. ZoneWiring 다리 빌더가 채움.")]
    public System.Collections.Generic.List<Vector4> WallCuts = new System.Collections.Generic.List<Vector4>();

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
