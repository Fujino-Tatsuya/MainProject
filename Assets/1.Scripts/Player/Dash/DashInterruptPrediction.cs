using UnityEngine;

namespace BeaverLobby.Player.Dash
{
    /// <summary>시전 순간 예측의 결론과 근거. Success 만 '성공 예측'. (할 일 116 → 132 런타임)</summary>
    public enum DashInterruptPredictReason
    {
        Success,                  // 판정 시각에 간파 가능 + 판정 범위 안일 것
        NoTargetInRange,          // 예상 판정 위치·공격 방향에 놓은 판정 앵커에 닿는(정면 허용각 안) 간파 대상이 없다
        WindowClosedNow,          // 대상은 범위 안이지만 지금 간파 창이 닫혀 있다
        WindowClosesBeforeResolve // 창은 열려 있지만 예상 남은 창 시간 < 판정까지 남은 시간
                                  // (또는 열린 뒤 경과가 추정 길이를 넘어 추정을 버렸고 판정까지 남은 시간이 길다 — 할 일 139-2)
    }

    /// <summary>
    /// 간파 시도 예측의 연출 결론(할 일 132 정정1) — 서버 시도 슬로우·오너 로컬 연출이 같은 3상태를 따른다.
    /// 대상 없음 = 슬로우·줌·암전·빛 모임 없음(단계1 이전), 약 = 약 슬로우·약 줌, 강 = 강 슬로우·강 줌 + 오너 암전·빛 모임.
    /// </summary>
    public enum InterruptAttemptForecast : byte
    {
        NoTarget = 0,
        Weak = 1,
        Strong = 2,
    }

    /// <summary>
    /// 시전 순간 입력. 시각은 같은 피어의 Time.time(초). 음수 = 모름. (할 일 116 → 132 런타임)
    /// 근접 간파는 <see cref="ForMelee"/> 로 만든다 — 대시 칸 0, 준비 기준점 = 시전 시각.
    /// </summary>
    public struct DashInterruptPredictInput
    {
        public float CastTime;
        public Vector3 CastPosition;
        public Vector3 DashDirection;     // 평면 정규화
        public float DashSpeed;
        public float DashRemaining;      // 이 피어 대시 남은 시간(근접 = 0)
        public float SkillStartTime;     // 간파 준비 기준점 t0(대시 → 우클릭 = 승인 시각, 근접 = 시전 시각)
        public float PrepDelay;          // 평소 준비 시간(시작 → Hit). 음수 = 모름(→ 0 으로 본다)

        public bool HasTargetInRange;    // 예상 판정 위치에서 판정 오버랩 안에 간파 대상(창 소유자)이 있다
        public bool TargetWindowOpen;    // 그 대상의 지금 간파 창
        public float WindowOpenElapsed;  // 창이 열린 뒤 지난 시간(게임초). 음수 = 모름
        public float WindowExpectedDuration; // 창 길이(게임초) — 로컬로 아는 값 우선, 없으면 과거 관측 중앙값(할 일 139-6). 음수 = 모름, +∞ = 무기한(남은 창 +∞)

        /// <summary>근접 간파 입력 — 대시 없음, 준비 기준점 = 시전 시각. 판정 시각 = 시전 시각 + 준비 시간.</summary>
        public static DashInterruptPredictInput ForMelee(float castTime, Vector3 castPosition, float prepDelay) =>
            new DashInterruptPredictInput
            {
                CastTime = castTime,
                CastPosition = castPosition,
                SkillStartTime = castTime,
                PrepDelay = prepDelay,
                WindowOpenElapsed = -1f,
                WindowExpectedDuration = -1f,
            };
    }

    /// <summary>시전 순간 예측 결과.</summary>
    public struct DashInterruptPrediction
    {
        public Vector3 PredictedEndPosition;
        public float PredictedDashEndTime;
        public float PredictedNormalHitTime;
        public float PredictedResolveTime;
        public float WindowRemaining;    // 음수 = 모름
        public bool PredictsSuccess;
        public DashInterruptPredictReason Reason;

        /// <summary>연출 결론 — 대상 없음 / 약(실패 예측) / 강(성공 예측).</summary>
        public InterruptAttemptForecast Forecast =>
            Reason == DashInterruptPredictReason.NoTargetInRange ? InterruptAttemptForecast.NoTarget
            : PredictsSuccess ? InterruptAttemptForecast.Strong
            : InterruptAttemptForecast.Weak;

        /// <summary>판정까지 남은 시간(예상 판정 시각 − 시전 시각).</summary>
        public float TimeToResolve(float castTime) => PredictedResolveTime - castTime;
    }

    /// <summary>
    /// 간파 시전 순간 성공 예측의 순수 계산 — 근접·대쉬 공용(할 일 116 QA 로그에서 132 런타임으로 이식).
    /// 서버는 이 결과로 시도 슬로우 강·약을 고르고(InterruptProductionRules.SelectAttemptPurpose), 오너 클라도 같은 함수를 쓸 수 있게 상태가 없다.
    /// 예상 판정 시각 = max(예상 대시 끝, t0 + 평소 준비) — <see cref="DashInterruptRules.GetResolveTime"/>. 근접은 대시가 없어 시전 시각 + 준비 시간.
    /// 예상 대시 끝 위치 = 시전 위치 + 대시 방향 × 속도 × 남은 대시 시간 — <see cref="DashInterruptRules.GetPredictedEndOffset"/>(직선, 막힘 모름).
    /// </summary>
    public static class DashInterruptPredictor
    {
        /// <summary>
        /// 창 길이 추정을 버렸을 때(열린 뒤 경과 &gt; 추정 길이 — 지금 열려 있으니 추정이 틀렸다) 판정까지 남은 시간이 이 초 이하면 성공 예측(할 일 139-2).
        /// 139 보정 표본 6개(seq 5·8·15·19·22·26)의 판정까지 남은 시간 0.337~0.347초를 덮는다.
        /// </summary>
        public const float StaleWindowMaxTimeToResolve = 0.4f;

        public static DashInterruptPrediction Predict(in DashInterruptPredictInput input)
        {
            var p = new DashInterruptPrediction();
            float dashRemaining = Mathf.Max(0f, input.DashRemaining);
            p.PredictedEndPosition = input.CastPosition +
                                     DashInterruptRules.GetPredictedEndOffset(input.DashDirection, input.DashSpeed, dashRemaining);
            p.PredictedDashEndTime = input.CastTime + dashRemaining;
            p.PredictedNormalHitTime = input.SkillStartTime + Mathf.Max(0f, input.PrepDelay);
            p.PredictedResolveTime = DashInterruptRules.GetResolveTime(p.PredictedDashEndTime, p.PredictedNormalHitTime);
            bool staleEstimate = input.TargetWindowOpen && IsWindowEstimateStale(input.WindowOpenElapsed, input.WindowExpectedDuration);
            p.WindowRemaining = staleEstimate ? -1f : EstimateWindowRemaining(input.WindowOpenElapsed, input.WindowExpectedDuration);
            float timeToResolve = p.TimeToResolve(input.CastTime);

            if (!input.HasTargetInRange)
                p.Reason = DashInterruptPredictReason.NoTargetInRange;
            else if (!input.TargetWindowOpen)
                p.Reason = DashInterruptPredictReason.WindowClosedNow;
            else if (staleEstimate)
                p.Reason = timeToResolve <= StaleWindowMaxTimeToResolve
                    ? DashInterruptPredictReason.Success
                    : DashInterruptPredictReason.WindowClosesBeforeResolve;
            else if (p.WindowRemaining >= 0f && p.WindowRemaining < timeToResolve)
                p.Reason = DashInterruptPredictReason.WindowClosesBeforeResolve;
            else
                p.Reason = DashInterruptPredictReason.Success; // 남은 창을 모르면 '열려 있음'만으로 성공 예측

            p.PredictsSuccess = p.Reason == DashInterruptPredictReason.Success;
            return p;
        }

        /// <summary>
        /// 열려 있는 창의 길이 추정이 틀렸나 — 열린 뒤 경과가 추정 길이(과거 관측 중앙값)를 이미 넘었다(할 일 139-2).
        /// 무기한 창(디버그 허수아비)·관측이 적은 창에서 나온다. 둘 중 하나라도 모르면 false.
        /// </summary>
        public static bool IsWindowEstimateStale(float openElapsed, float expectedDuration) =>
            openElapsed >= 0f && expectedDuration >= 0f && openElapsed > expectedDuration;

        /// <summary>
        /// 대쉬 간파 재예측 대상인가(할 일 139-2) — 누른 순간 대상은 범위 안인데 창이 닫혀 있어 약이었다.
        /// 대시 동안 창이 열리면 그 순간 다시 예측해 강으로만 올린다(강 → 약 내리기 없음). 근접 간파는 재예측하지 않는다.
        /// </summary>
        public static bool ShouldRepredictDuringDash(in DashInterruptPrediction prediction) =>
            prediction.Reason == DashInterruptPredictReason.WindowClosedNow;

        /// <summary>근접 간파 예상 판정 시각 = 시전 시각 + 준비 시간(모름·음수 = 0).</summary>
        public static float GetMeleeResolveTime(float castTime, float prepDelay) => castTime + Mathf.Max(0f, prepDelay);

        /// <summary>남은 창 시간 = 추정 길이 − 열린 뒤 경과(음수면 0). 둘 중 하나라도 모르면 -1. 무기한(+∞) 길이면 +∞ — 창 닫힘으로 약이 나오지 않는다.</summary>
        public static float EstimateWindowRemaining(float openElapsed, float expectedDuration)
        {
            if (openElapsed < 0f || expectedDuration < 0f)
                return -1f;
            return Mathf.Max(0f, expectedDuration - openElapsed);
        }

        /// <summary>
        /// 평소 준비 시간(시작 → Hit) 추정 — 이 피어에서 관측한 값 &gt; 데이터 HitDelay &gt; 모름(-1).
        /// 이벤트 방식 간파는 첫 Hit 관측 전까지 데이터 HitDelay 를 근사로 쓴다(116 QA 와 같은 순서).
        /// </summary>
        public static float EstimatePrepDelay(float observedPrepDelay, bool hasData, float dataHitDelay)
        {
            if (observedPrepDelay > 0f)
                return observedPrepDelay;
            return hasData ? dataHitDelay : -1f;
        }
    }

    /// <summary>
    /// 간파 예측 대상의 정면 판정(할 일 132 정정1 → 139-3) — 근접·대쉬 공용 순수 계산. 판정 위치 = 근접은 지금 위치, 대쉬는 예상 대시 끝.
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
