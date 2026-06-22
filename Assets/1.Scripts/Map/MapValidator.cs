using UnityEngine;

public class MapValidator : MonoBehaviour
{
    public bool ValidateMapPaths()
    {
        // 보스(NavMesh) 및 플레이어(Rigidbody)가 지나갈 수 있는지 검증.
        // 현재는 NavMesh가 적용되지 않았으므로 단순 true 반환.
        // 추후 A* BFS나 NavMesh 경로 탐색 코드로 교체 예정.
        return true;
    }
}
