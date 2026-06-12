using UnityEngine;

public class ObstaclePlacer : MonoBehaviour
{
    public void PlaceAdditionalObstacles()
    {
        // 1티어, 2티어, 3티어 노드의 배제 영역을 피해서
        // 추가적인 시각적/물리적 장애물을 배치하는 로직
        // 현재는 노드 자체를 장애물(NodeContentType.Obstacle)로 생성하고 있으므로,
        // 필요 시 맵 전체의 여백을 꾸미는 역할로 사용합니다.
    }
}
