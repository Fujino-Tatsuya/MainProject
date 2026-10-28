using BeaverLobby.Player.Dash;
using UnityEngine;

/// <summary>
/// 간파 시도 예측 입력 구성(할 일 132) — 서버 승인 시점과 오너 클라 입력 순간이 같은 규칙으로 <see cref="DashInterruptPredictInput"/> 을 만든다.
/// 순수 함수. 대상·창 값을 모으는 곳만 다르다: 서버 = <see cref="IInterruptSlowMotionTarget.ServerIsInterruptible"/>·서버 창 기록,
/// 오너 = <see cref="IInterruptWindowReplica.ReplicatedWindowOpen"/>·<see cref="InterruptWindowReplicaTracker"/>.
/// </summary>
public static class InterruptAttemptPrediction
{
    /// <summary>
    /// 근접 = 지금 위치·판정 시각 지금 + 준비 시간. 대쉬 간파(<paramref name="viaDash"/>) = 대시 방향·속도·남은 시간을 더해
    /// 예상 대시 끝 위치·판정 시각 max(대시 끝, 평소 Hit). 대상이 없으면 창 칸은 모름(-1), 창이 닫혀 있으면 열린 뒤 경과는 모름.
    /// </summary>
    public static DashInterruptPredictInput BuildInput(float castTime, Vector3 castPosition, float prepDelay,
        bool viaDash, Vector3 dashDirection, float dashSpeed, float dashRemaining,
        bool hasTarget, bool targetWindowOpen, float windowOpenElapsed, float windowExpectedDuration)
    {
        var input = DashInterruptPredictInput.ForMelee(castTime, castPosition, prepDelay);
        if (viaDash)
        {
            input.DashDirection = dashDirection;
            input.DashSpeed = dashSpeed;
            input.DashRemaining = dashRemaining;
        }

        input.HasTargetInRange = hasTarget;
        input.TargetWindowOpen = hasTarget && targetWindowOpen;
        input.WindowOpenElapsed = input.TargetWindowOpen ? windowOpenElapsed : -1f;
        input.WindowExpectedDuration = hasTarget ? windowExpectedDuration : -1f;
        return input;
    }

    /// <summary>판정 앵커를 옮길 양 — 대쉬 간파면 예상 대시 끝 − 지금 위치, 근접은 0.</summary>
    public static Vector3 PredictOffset(bool viaDash, Vector3 dashDirection, float dashSpeed, float dashRemaining) =>
        viaDash ? DashInterruptRules.GetPredictedEndOffset(dashDirection, dashSpeed, dashRemaining) : Vector3.zero;
}
