using UnityEngine;

namespace BeaverLobby.Player.Dash
{
    /// <summary>시전 순간 예측의 결론과 근거. Success 만 '성공 예측'. (할 일 116 → 132 런타임)</summary>
    public enum DashInterruptPredictReason
    {
        Success,                  // 판정 시각에 간파 가능 + 판정 범위 안일 것
        NoTargetInRange,          // 예상 판정 위치에서 판정 범위 안에 간파 대상이 없다
        WindowClosedNow,          // 대상은 범위 안이지만 지금 간파 창이 닫혀 있다
        WindowClosesBeforeResolve // 창은 열려 있지만 예상 남은 창 시간 < 판정까지 남은 시간
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
        public float WindowOpenElapsed;  // 창이 열린 뒤 지난 시간. 음수 = 모름
        public float WindowExpectedDuration; // 창 추정 길이(과거 관측). 음수 = 모름

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
        public static DashInterruptPrediction Predict(in DashInterruptPredictInput input)
        {
            var p = new DashInterruptPrediction();
            float dashRemaining = Mathf.Max(0f, input.DashRemaining);
            p.PredictedEndPosition = input.CastPosition +
                                     DashInterruptRules.GetPredictedEndOffset(input.DashDirection, input.DashSpeed, dashRemaining);
            p.PredictedDashEndTime = input.CastTime + dashRemaining;
            p.PredictedNormalHitTime = input.SkillStartTime + Mathf.Max(0f, input.PrepDelay);
            p.PredictedResolveTime = DashInterruptRules.GetResolveTime(p.PredictedDashEndTime, p.PredictedNormalHitTime);
            p.WindowRemaining = EstimateWindowRemaining(input.WindowOpenElapsed, input.WindowExpectedDuration);

            if (!input.HasTargetInRange)
                p.Reason = DashInterruptPredictReason.NoTargetInRange;
            else if (!input.TargetWindowOpen)
                p.Reason = DashInterruptPredictReason.WindowClosedNow;
            else if (p.WindowRemaining >= 0f && p.WindowRemaining < p.TimeToResolve(input.CastTime))
                p.Reason = DashInterruptPredictReason.WindowClosesBeforeResolve;
            else
                p.Reason = DashInterruptPredictReason.Success; // 남은 창을 모르면 '열려 있음'만으로 성공 예측

            p.PredictsSuccess = p.Reason == DashInterruptPredictReason.Success;
            return p;
        }

        /// <summary>근접 간파 예상 판정 시각 = 시전 시각 + 준비 시간(모름·음수 = 0).</summary>
        public static float GetMeleeResolveTime(float castTime, float prepDelay) => castTime + Mathf.Max(0f, prepDelay);

        /// <summary>남은 창 시간 = 추정 길이 − 열린 뒤 경과(음수면 0). 둘 중 하나라도 모르면 -1.</summary>
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
}
