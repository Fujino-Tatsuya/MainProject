using UnityEngine;

namespace BeaverLobby.Player.Dash
{
    /// <summary>
    /// 간파 시도 예측의 연출 결론(할 일 132 정정1 → 191) — 서버 시도 슬로우·오너 로컬 연출이 같은 3상태를 따른다.
    /// 대상 없음 = 슬로우·줌·암전·빛 모임 없음(단계1 이전), 약 = 약 슬로우·약 줌, 강 = 강 슬로우·강 줌 + 오너 암전·빛 모임.
    /// 할 일 191 부터 값은 <c>InterruptJudgeResult.Forecast</c>(<c>InterruptManager.Predict</c>)가 낸다 — 옛 예측기(DashInterruptPredictor)는 5단계에서 삭제.
    /// </summary>
    public enum InterruptAttemptForecast : byte
    {
        NoTarget = 0,
        Weak = 1,
        Strong = 2,
    }

    /// <summary>
    /// 간파 대상의 정면 판정(할 일 132 정정1 → 139-3) — 근접·대쉬 공용 순수 계산. 판정 위치 = 근접은 지금 위치, 대쉬는 대시 끝.
    /// 사거리·공격 방향은 여기서 보지 않는다 — 판정 앵커를 공격 방향으로 돌려 판정 위치에 놓은 오버랩(<c>HitboxAnchorShape</c>, 서버 판정과 같은 질의)이 본다.
    /// 여기는 대상 정면 허용각만: 대상의 기존 간파 정면 판정값(23호 counterFrontAngle 등, 정면 판정 없으면 180). 새 수치 칸은 없다.
    /// </summary>
    public static class InterruptTargetGeometry
    {
        /// <summary>정면 판정이 없는 대상의 허용각 — 어느 방향이든 인정.</summary>
        public const float NoFrontRule = 180f;

        /// <summary><paramref name="toOther"/> 가 <paramref name="forward"/> 기준 ±<paramref name="halfAngle"/> 안인가(수평). 겹쳐 방향을 못 구하면 안으로 본다.</summary>
        public static bool IsInArc(Vector3 forward, Vector3 toOther, float halfAngle)
        {
            if (halfAngle >= NoFrontRule)
                return true;

            forward.y = 0f;
            toOther.y = 0f;
            if (forward.sqrMagnitude < 0.0001f || toOther.sqrMagnitude < 0.0001f)
                return true;
            return Vector3.Angle(forward, toOther) <= halfAngle;
        }

        /// <summary>
        /// 판정 위치 <paramref name="resolvePosition"/> 가 대상 정면 허용각 안인가 — 대상 루트 위치·정면으로 잰다(23호 정면 판정과 같은 기준).
        /// 판정 위치가 대상 루트와 겹치면 안으로 본다.
        /// </summary>
        public static bool FacesAttacker(Vector3 resolvePosition, Vector3 targetPosition, Vector3 targetForward, float targetFrontAngle) =>
            IsInArc(targetForward, resolvePosition - targetPosition, targetFrontAngle);
    }
}
