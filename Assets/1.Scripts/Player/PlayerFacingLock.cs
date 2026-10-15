using System.Collections.Generic;

/// <summary>
/// 플레이어 회전 잠금 — 원인(토큰)별로 따로 잡고 따로 푼다(할 일 27).
/// 원인이 하나라도 남아 있으면 잠겨 있다. 다른 원인의 해제가 내 잠금을 풀지 않는다.
///
/// 막는 것은 <b>자동 회전</b>(이동 입력 회전·<see cref="PlayerMovement.RotateToward"/>)뿐이다.
/// <see cref="PlayerMovement.RotateImmediately"/> 는 행동이 직접 내리는 회전(대시 진입·낙하 복귀 등)이라 막지 않는다 —
/// 잠금을 건 행동은 그 행동이 끊길 때 함께 풀린다(상태 Exit 가 Enter 보다 먼저 돈다).
/// </summary>
public sealed class PlayerFacingLock
{
    private readonly HashSet<object> causes = new HashSet<object>();

    public bool IsLocked => causes.Count > 0;

    /// <returns>새로 잡았으면 true. 같은 원인이 이미 잡고 있으면 false(중복 획득은 한 번으로 친다).</returns>
    public bool Acquire(object cause) => cause != null && causes.Add(cause);

    /// <returns>이 원인이 잡고 있었으면 true. 안 잡은 원인의 해제는 아무것도 안 한다.</returns>
    public bool Release(object cause) => cause != null && causes.Remove(cause);

    public bool IsHeldBy(object cause) => cause != null && causes.Contains(cause);
}
