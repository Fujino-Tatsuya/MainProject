using UnityEngine;

/// <summary>
/// 대시 이동의 충돌 검사 한 틱 — 실제 대시 이동(<see cref="PlayerMotor"/> 시뮬레이션)과 대시 경로 예측(<see cref="PlayerDashPath"/>)이 같은 구현을 부른다(할 일 191).
/// 런타임 구현 = <see cref="PlayerMotor"/>(같은 캡슐·장애물 레이어·스킨·반복 횟수). EditMode 는 가짜 벽으로 바꿔 끼운다.
/// </summary>
public interface IPlayerDashSweep
{
    /// <summary><paramref name="position"/> 에서 <paramref name="desiredDelta"/> 만큼 움직일 때 실제 적용 이동량(벽·급경사·유닛 스윕).</summary>
    Vector3 Resolve(Vector3 position, Vector3 desiredDelta);

    /// <summary><paramref name="position"/> 에서 <paramref name="direction"/> 으로 <paramref name="distance"/> 갈 때 먼저 막는 것이 유닛(몬스터·다른 플레이어 몸)인가.</summary>
    bool IsUnitBlockingAhead(Vector3 position, Vector3 direction, float distance);
}

/// <summary>대시 경로 예측 결과(할 일 191).</summary>
public struct PlayerDashPathResult
{
    /// <summary>대시가 끝나는 위치.</summary>
    public Vector3 End;
    /// <summary>시작부터 대시가 끝날 때까지(초). 유닛에 막혀 조기 종료하면 그 틱까지, 아니면 남은 대시 시간 그대로.</summary>
    public float Duration;
    /// <summary>유닛에 막혀 그 자리에서 대시가 끝났다(기획 10-11 — 벽은 대시 상태를 끝내지 않는다).</summary>
    public bool EndedByUnit;
    /// <summary>벽·지형에 막혀 요청보다 덜 갔다.</summary>
    public bool BlockedByWall;
}

/// <summary>
/// 진행 중 대시의 끝 위치·시각 예측(할 일 191 §3.1 ② 대쉬) — 순수 계산. 충돌은 <see cref="IPlayerDashSweep"/>(= 실제 대시 이동과 같은 스윕)에 묻는다.
/// 고정 틱마다 방향 × 속도 × dt 를 요청하고(<see cref="PlayerMovementSimulation"/> 의 대시 성분과 같다), 막혀 깎이면 남은 경로를 먼저 막는 것이 유닛인지 보고
/// 유닛이면 그 틱에서 끝낸다(<see cref="PlayerUnitCollisionRules.ShouldEndDashOnUnitBlock"/> — 대시 상태가 쓰는 같은 규칙). 벽은 남은 시간까지 상태를 유지한다.
/// 평지 가정 — 경사 투영·접지 스냅·낙하는 넣지 않는다(수평 위치만 본다).
/// </summary>
public static class PlayerDashPath
{
    public static PlayerDashPathResult Simulate(IPlayerDashSweep sweep, Vector3 start, Vector3 direction, float speed,
        float remaining, float fixedDeltaTime)
    {
        var result = new PlayerDashPathResult { End = start, Duration = Mathf.Max(0f, remaining) };
        direction.y = 0f;
        if (sweep == null || remaining <= 0f || speed <= 0f || fixedDeltaTime <= 0f || direction.sqrMagnitude <= 0.0001f)
            return result;

        direction.Normalize();
        // 대시 상태는 fixedTime < 종료 시각인 틱마다 한 틱 전체(dt)를 요청한다 — 남은 시간을 틱 수로 올림.
        int ticks = Mathf.CeilToInt(remaining / fixedDeltaTime - 0.0001f);
        Vector3 position = start;
        Vector3 step = direction * (speed * fixedDeltaTime);
        float requested = step.magnitude;

        for (int i = 0; i < ticks; i++)
        {
            Vector3 applied = sweep.Resolve(position, step);
            applied.y = 0f;
            position += applied;
            float appliedPlanar = applied.magnitude;

            if (appliedPlanar < requested - PlayerUnitCollisionRules.DashBlockedTolerance)
            {
                if (PlayerUnitCollisionRules.ShouldEndDashOnUnitBlock(requested, appliedPlanar,
                        sweep.IsUnitBlockingAhead(position, direction, requested - appliedPlanar)))
                {
                    result.EndedByUnit = true;
                    result.Duration = Mathf.Min(result.Duration, (i + 1) * fixedDeltaTime);
                    break;
                }

                result.BlockedByWall = true;
            }
        }

        result.End = position;
        return result;
    }
}
