using System.Globalization;
using System.Text;
using UnityEngine;

namespace BeaverLobby.Player.Dash
{
    /// <summary>대쉬 간파가 어느 순서로 성립했는가. (할 일 116)</summary>
    public enum DashInterruptOrder
    {
        DashThenInterrupt, // 대시 → 창 안 우클릭(§1)
        InterruptThenDash  // 우클릭(일반 간파 시작) → 창 안 대시 전환(§2.1)
    }

    /// <summary>시전 순간 예측의 결론과 근거. Success 만 '성공 예측'. (할 일 116)</summary>
    public enum DashInterruptPredictReason
    {
        Success,                  // 판정 시각에 간파 가능 + 판정 범위 안일 것
        NoTargetInRange,          // 예상 대시 끝에서 판정 범위 안에 간파 대상이 없다
        WindowClosedNow,          // 대상은 범위 안이지만 지금 간파 창이 닫혀 있다
        WindowClosesBeforeResolve // 창은 열려 있지만 예상 남은 창 시간 < 판정까지 남은 시간
    }

    /// <summary>서버 실제 판정 결과. (할 일 116)</summary>
    public enum DashInterruptActualResult
    {
        Success,   // 카운터 창 소비 성공
        Fail,      // 판정 범위 안에 간파 대상이 있었지만 성공 못 함(창 닫힘 등)
        NoTarget,  // 판정 범위 안에 간파 대상이 없었다
        Aborted    // 판정 전에 끝났다(대시 강제 종료·피격 등)
    }

    /// <summary>예측과 실제가 어긋난 이유(코드에서 판별 가능한 것만). 맞았으면 None. (할 일 116)</summary>
    public enum DashInterruptMismatchReason
    {
        None,
        DashShortened,             // 대시가 벽·유닛에 막혀 예상보다 덜 갔다
        WindowClosedBeforeResolve, // 판정 전에 보스 간파 창이 닫혔다
        ResolveDelayed,            // 판정이 예상 시각보다 늦어 창이 닫혔다
        WindowOpenedAfterCast,     // 시전 때 닫혀 있던 창이 판정 전에 열렸다
        WindowLongerThanEstimated, // 창이 예상(추정 길이)보다 오래 열려 있었다
        TargetMoved,               // 대상이 시전 뒤 움직였다
        TargetRotated,             // 대상이 시전 뒤 돌았다
        OutOfRange,                // 위 원인 없이 범위·각도 판단이 실제와 달랐다
        NetworkLag,                // 원격 오너 — 지연 말고 다른 원인을 못 찾았다
        Aborted,                   // 판정 전에 간파가 끝났다
        Unknown                    // 대상이 범위 안·창 열림인데도 성공하지 못했다 등
    }

    /// <summary>시전 순간 입력. 시각은 같은 피어의 Time.time(초). 음수 = 모름. (할 일 116)</summary>
    public struct DashInterruptPredictInput
    {
        public float CastTime;
        public Vector3 CastPosition;
        public Vector3 DashDirection;     // 평면 정규화
        public float DashSpeed;
        public float DashRemaining;      // 이 피어 대시 남은 시간
        public float SkillStartTime;     // 간파 준비 기준점 t0(대시 → 우클릭 = 승인 시각, 우클릭 → 대시 = 처음 승인 시각)
        public float PrepDelay;          // 평소 준비 시간(시작 → Hit). 음수 = 모름(→ 0 으로 본다)

        public bool HasTargetInRange;    // 예상 대시 끝에서 판정 오버랩 안에 간파 대상(창 소유자)이 있다
        public bool TargetWindowOpen;    // 그 대상의 지금 간파 창
        public float WindowOpenElapsed;  // 창이 열린 뒤 지난 시간. 음수 = 모름
        public float WindowExpectedDuration; // 창 추정 길이(과거 관측). 음수 = 모름
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

    /// <summary>실제 판정 순간 관측값(서버). 시각은 Time.time. (할 일 116)</summary>
    public struct DashInterruptActualObservation
    {
        public DashInterruptActualResult Result;
        public float ResolveTime;
        public Vector3 ResolvePosition;
        public bool TargetWindowOpenAtResolve; // 범위 안 대상의 판정 순간 창(대상 없으면 false)
        public float TargetMoveDistance;       // 시전 → 판정 동안 대상 평면 이동 거리(대상 모름 = 0)
        public float TargetYawDelta;           // 시전 → 판정 동안 대상 회전(도, 절댓값)
        public bool IsRemoteOwner;
    }

    /// <summary>
    /// 대쉬 간파 시전 순간 예측과 실제 판정 비교의 순수 계산. (할 일 116 — 로그 전용, 117 성공 예측 연출에서 재사용)
    /// 예상 판정 시각 = max(예상 대시 끝, t0 + 평소 준비) — <see cref="DashInterruptRules.GetResolveTime"/>.
    /// 예상 대시 끝 위치 = 시전 위치 + 대시 방향 × 속도 × 남은 대시 시간 — <see cref="DashInterruptRules.GetPredictedEndOffset"/>(직선, 막힘 모름).
    /// </summary>
    public static class DashInterruptPredictor
    {
        /// <summary>대시가 이만큼(m) 이상 덜 가면 '막혀 짧아짐'.</summary>
        public const float DashShortfallThreshold = 0.3f;
        /// <summary>판정이 예상보다 이만큼(초) 이상 늦으면 '판정 지연'.</summary>
        public const float ResolveDelayThreshold = 0.05f;
        /// <summary>대상이 이만큼(m) 이상 움직이면 '대상 이동'.</summary>
        public const float TargetMoveThreshold = 0.5f;
        /// <summary>대상이 이만큼(도) 이상 돌면 '대상 회전'.</summary>
        public const float TargetYawThreshold = 20f;

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

        /// <summary>남은 창 시간 = 추정 길이 − 열린 뒤 경과(음수면 0). 둘 중 하나라도 모르면 -1.</summary>
        public static float EstimateWindowRemaining(float openElapsed, float expectedDuration)
        {
            if (openElapsed < 0f || expectedDuration < 0f)
                return -1f;
            return Mathf.Max(0f, expectedDuration - openElapsed);
        }

        /// <summary>대시 방향으로 실제 이동한 거리가 예상보다 모자란 양(m). 양수 = 덜 감.</summary>
        public static float DashShortfall(Vector3 castPosition, Vector3 predictedEnd, Vector3 actualPosition, Vector3 direction)
        {
            Vector3 dir = direction;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f)
                return 0f;
            dir.Normalize();
            Vector3 predictedTravel = predictedEnd - castPosition;
            Vector3 actualTravel = actualPosition - castPosition;
            return Vector3.Dot(predictedTravel, dir) - Vector3.Dot(actualTravel, dir);
        }

        /// <summary>평면 거리(m).</summary>
        public static float PlanarDistance(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            d.y = 0f;
            return d.magnitude;
        }

        /// <summary>대시 방향과 (대상 − 위치) 사이 평면 각도(도). 같은 위치면 0.</summary>
        public static float PlanarAngle(Vector3 direction, Vector3 from, Vector3 to)
        {
            Vector3 dir = direction;
            dir.y = 0f;
            Vector3 toTarget = to - from;
            toTarget.y = 0f;
            if (dir.sqrMagnitude < 0.0001f || toTarget.sqrMagnitude < 0.0001f)
                return 0f;
            return Vector3.Angle(dir, toTarget);
        }

        /// <summary>실제 결과가 '성공'과 같은가 — 예측 적중 판단 기준.</summary>
        public static bool IsActualSuccess(DashInterruptActualResult result) => result == DashInterruptActualResult.Success;

        /// <summary>
        /// 어긋남 이유 분류. 예측 = 실제면 None. 판별 순서는 코드에서 확실히 보이는 원인부터
        /// (중단 → 대시 짧아짐 → 창 닫힘/지연 → 대상 이동·회전 → 원격 지연 → 범위 판단).
        /// </summary>
        public static DashInterruptMismatchReason ClassifyMismatch(
            in DashInterruptPrediction prediction, in DashInterruptActualObservation actual, float shortfall)
        {
            bool actualSuccess = IsActualSuccess(actual.Result);
            if (prediction.PredictsSuccess == actualSuccess)
                return DashInterruptMismatchReason.None;

            if (actual.Result == DashInterruptActualResult.Aborted)
                return DashInterruptMismatchReason.Aborted;

            float timeError = actual.ResolveTime - prediction.PredictedResolveTime;

            if (prediction.PredictsSuccess)
            {
                // 성공 예측 → 실제 실패
                if (actual.Result == DashInterruptActualResult.NoTarget)
                    return ClassifyGeometry(actual, shortfall);

                if (!actual.TargetWindowOpenAtResolve)
                    return timeError > ResolveDelayThreshold
                        ? DashInterruptMismatchReason.ResolveDelayed
                        : DashInterruptMismatchReason.WindowClosedBeforeResolve;

                return DashInterruptMismatchReason.Unknown;
            }

            // 실패 예측 → 실제 성공
            switch (prediction.Reason)
            {
                case DashInterruptPredictReason.WindowClosedNow:
                    return DashInterruptMismatchReason.WindowOpenedAfterCast;
                case DashInterruptPredictReason.WindowClosesBeforeResolve:
                    return DashInterruptMismatchReason.WindowLongerThanEstimated;
                default:
                    return ClassifyGeometry(actual, shortfall);
            }
        }

        private static DashInterruptMismatchReason ClassifyGeometry(in DashInterruptActualObservation actual, float shortfall)
        {
            if (shortfall > DashShortfallThreshold)
                return DashInterruptMismatchReason.DashShortened;
            if (actual.TargetMoveDistance > TargetMoveThreshold)
                return DashInterruptMismatchReason.TargetMoved;
            if (actual.TargetYawDelta > TargetYawThreshold)
                return DashInterruptMismatchReason.TargetRotated;
            if (actual.IsRemoteOwner)
                return DashInterruptMismatchReason.NetworkLag;
            return DashInterruptMismatchReason.OutOfRange;
        }

        // ── 로그 문자열(key=value 한 줄) — QA 파서가 읽는다. 키 목록 = Docs/tech/dash-interrupt.md §13 ──

        public const string Tag = "[DashPredict]";

        public static string Format(DashInterruptOrder order) =>
            order == DashInterruptOrder.DashThenInterrupt ? "dash_rmb" : "rmb_dash";

        public static string Format(DashInterruptPredictReason reason)
        {
            switch (reason)
            {
                case DashInterruptPredictReason.Success: return "success";
                case DashInterruptPredictReason.NoTargetInRange: return "no_target_in_range";
                case DashInterruptPredictReason.WindowClosedNow: return "window_closed_now";
                default: return "window_closes_before_resolve";
            }
        }

        public static string Format(DashInterruptActualResult result)
        {
            switch (result)
            {
                case DashInterruptActualResult.Success: return "success";
                case DashInterruptActualResult.Fail: return "fail";
                case DashInterruptActualResult.NoTarget: return "no_target";
                default: return "aborted";
            }
        }

        public static string Format(DashInterruptMismatchReason reason)
        {
            switch (reason)
            {
                case DashInterruptMismatchReason.None: return "none";
                case DashInterruptMismatchReason.DashShortened: return "dash_shortened";
                case DashInterruptMismatchReason.WindowClosedBeforeResolve: return "window_closed_before_resolve";
                case DashInterruptMismatchReason.ResolveDelayed: return "resolve_delayed";
                case DashInterruptMismatchReason.WindowOpenedAfterCast: return "window_opened_after_cast";
                case DashInterruptMismatchReason.WindowLongerThanEstimated: return "window_longer_than_estimated";
                case DashInterruptMismatchReason.TargetMoved: return "target_moved";
                case DashInterruptMismatchReason.TargetRotated: return "target_rotated";
                case DashInterruptMismatchReason.OutOfRange: return "out_of_range";
                case DashInterruptMismatchReason.NetworkLag: return "network_lag";
                case DashInterruptMismatchReason.Aborted: return "aborted";
                default: return "unknown";
            }
        }

        public static void Append(StringBuilder sb, string key, string value) =>
            sb.Append(' ').Append(key).Append('=').Append(Token(value));

        public static void Append(StringBuilder sb, string key, bool value) =>
            sb.Append(' ').Append(key).Append('=').Append(value ? "true" : "false");

        public static void Append(StringBuilder sb, string key, float value, string format = "0.000") =>
            sb.Append(' ').Append(key).Append('=').Append(value.ToString(format, CultureInfo.InvariantCulture));

        public static void Append(StringBuilder sb, string key, double value, string format = "0.000") =>
            sb.Append(' ').Append(key).Append('=').Append(value.ToString(format, CultureInfo.InvariantCulture));

        public static void Append(StringBuilder sb, string key, int value) =>
            sb.Append(' ').Append(key).Append('=').Append(value.ToString(CultureInfo.InvariantCulture));

        public static void Append(StringBuilder sb, string key, Vector3 value) =>
            sb.Append(' ').Append(key).Append('=')
                .Append(value.x.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                .Append(value.z.ToString("0.00", CultureInfo.InvariantCulture));

        /// <summary>값에 공백·'=' 이 있으면 key=value 파싱이 깨진다 — '_' 로 바꾼다. 비면 none.</summary>
        public static string Token(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "none";

            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
                sb.Append(char.IsWhiteSpace(c) || c == '=' ? '_' : c);
            return sb.ToString();
        }

        /// <summary>key=value 한 줄에서 키 값을 꺼낸다(QA 파서·테스트 공용). 없으면 null.</summary>
        public static string ReadField(string line, string key)
        {
            if (string.IsNullOrEmpty(line) || string.IsNullOrEmpty(key))
                return null;

            string needle = " " + key + "=";
            int at = line.IndexOf(needle, System.StringComparison.Ordinal);
            if (at < 0)
                return null;
            int start = at + needle.Length;
            int end = line.IndexOf(' ', start);
            return end < 0 ? line.Substring(start) : line.Substring(start, end - start);
        }
    }
}
