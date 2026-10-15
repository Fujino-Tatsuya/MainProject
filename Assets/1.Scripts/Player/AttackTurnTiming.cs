using UnityEngine;

/// <summary>
/// 근접 평타 '다 돈 뒤 공격'(할 일 43)의 회전 시간 규칙. 순수 함수 — <see cref="PlayerMovement"/> 가 쓴다.
///
/// · 스냅 각 이하로 꺾으면 즉시 맞추고 지연 0.
/// · 넘으면 각속도(도/초)로 돌고, 그 단의 공격(클립·전진·판정)은 다 돈 뒤에 나간다 — 지연 = 각 / 각속도.
/// · 각속도 0 이하 = 항상 즉시(지연 0).
/// </summary>
public static class AttackTurnTiming
{
    /// <summary><paramref name="angle"/>(도)를 도는 데 걸리는 시간(초). 스냅 각 이하이거나 각속도가 0 이하면 0.</summary>
    public static float Delay(float angle, float snapAngle, float turnSpeed)
    {
        if (turnSpeed <= 0f || !(angle > snapAngle))
            return 0f;

        return Mathf.Min(angle, 180f) / turnSpeed;
    }

    /// <summary>가장 크게 꺾을 때(180°)의 지연 — 오너가 보낸 지연의 상한.</summary>
    public static float MaxDelay(float snapAngle, float turnSpeed) => Delay(180f, snapAngle, turnSpeed);

    /// <summary>
    /// 오너가 시작 요청에 실어 보낸 지연을 [0, <see cref="MaxDelay"/>] 로 자른다(NaN·음수 = 0).
    /// 각을 다시 재지는 않는다 — 서버의 몸 방향은 NetworkTransform 으로 받은 늦은 값이라 오너 값이 더 정확하다.
    /// </summary>
    public static float ClampRequested(float requested, float snapAngle, float turnSpeed)
    {
        if (!(requested > 0f))
            return 0f;

        return Mathf.Min(requested, MaxDelay(snapAngle, turnSpeed));
    }
}
