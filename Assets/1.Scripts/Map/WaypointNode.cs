using UnityEngine;

/// <summary>
/// 이동 플랫폼 경로 노드의 속성. 각 웨이포인트(WP) 오브젝트에 부착한다.
/// - 목적지(Destination): 도착 시 지정 시간만큼 정지.
/// - 경유지(Waypoint): 정지 없이 통과(경로를 구부리는 지점).
/// 컴포넌트가 없는 WP는 목적지 + 플랫폼 기본 정지시간으로 취급(하위 호환).
/// </summary>
public class WaypointNode : MonoBehaviour
{
    public enum NodeType
    {
        Destination, // 목적지 — 정지
        Waypoint,    // 경유지 — 통과
    }

    [Tooltip("목적지=도착 시 정지 / 경유지=정지 없이 통과")]
    public NodeType type = NodeType.Destination;

    [Tooltip("목적지일 때 정지 시간(초). 음수면 플랫폼의 기본 정지시간 사용.")]
    public float pauseSeconds = -1f;

    public bool IsWaypoint => type == NodeType.Waypoint;

    /// <summary>이 노드 도착 시 정지 시간. 경유지=0, 목적지=pauseSeconds(음수면 fallback).</summary>
    public float ResolvePause(float fallback)
    {
        if (IsWaypoint)
        {
            return 0f;
        }
        return pauseSeconds >= 0f ? pauseSeconds : Mathf.Max(0f, fallback);
    }
}
