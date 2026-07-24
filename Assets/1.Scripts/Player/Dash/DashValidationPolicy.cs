using System;

namespace BeaverLobby.Player.Dash
{
    /// <summary>
    /// 서버 권한 대시 요청 검증(순수 계산). NetworkClock/MonoBehaviour를 참조하지 않는다. (PLAN §9)
    ///
    /// RTT 보정: NGO Client LocalTime은 서버보다 편도지연만큼 앞서 있으므로 RTT/2를 뺀다.
    ///   oneWayDelay        = serverRtt / 2
    ///   estimatedServerStart = clientLocalTime - oneWayDelay
    ///   estimatedServerEnd   = estimatedServerStart + dashDuration
    ///   remainingServerState = max(0, estimatedServerEnd - serverNow)
    ///
    /// 검증 순서(요약): Config → Payload/방향 → RTT 유효 → Snapshot(at-or-before) →
    /// 당시 Dead/Soul → 당시 CC → 당시 LandingProtection → 당시 Grounded → 당시 충전 →
    /// 과거 승인 → 현재 CC/사망 재검사(ApprovedButInterrupted).
    ///
    /// 멱등/RequestId 중복 처리는 상위(캐시/컨트롤러) 책임이며 이 정책은 다루지 않는다.
    /// </summary>
    public static class DashValidationPolicy
    {
        /// <summary>Owner가 보낸 최소 요청 파라미터(평면 정규화 전 방향 포함).</summary>
        public readonly struct Request
        {
            public readonly double ClientNetworkLocalTime;
            public readonly double DirectionX;
            public readonly double DirectionZ;

            public Request(double clientNetworkLocalTime, double directionX, double directionZ)
            {
                ClientNetworkLocalTime = clientNetworkLocalTime;
                DirectionX = directionX;
                DirectionZ = directionZ;
            }
        }

        /// <summary>요청 도착 시점(현재)의 서버 권한 상태. 과거 승인 후 재검사에 쓰인다.</summary>
        public readonly struct CurrentState
        {
            public readonly bool Dead;
            public readonly bool Soul;
            public readonly bool CrowdControlled;

            public CurrentState(bool dead, bool soul, bool crowdControlled)
            {
                Dead = dead;
                Soul = soul;
                CrowdControlled = crowdControlled;
            }
        }

        public static DashValidationResult Validate(
            bool dashEnabled,
            double dashDuration,
            double snapshotFreshnessTolerance,
            double serverNow,
            double serverRtt,
            bool rttAvailable,
            in Request request,
            DashSnapshotHistory history,
            in CurrentState current)
        {
            if (!dashEnabled)
            {
                return DashValidationResult.Reject(DashRejectReason.ConfigDisabled);
            }

            if (!IsFinite(request.ClientNetworkLocalTime) ||
                !IsFinite(request.DirectionX) ||
                !IsFinite(request.DirectionZ))
            {
                return DashValidationResult.Reject(DashRejectReason.InvalidPayload);
            }

            double directionMagnitudeSq = request.DirectionX * request.DirectionX +
                                          request.DirectionZ * request.DirectionZ;
            if (directionMagnitudeSq <= 0.0)
            {
                return DashValidationResult.Reject(DashRejectReason.InvalidPayload);
            }

            if (!rttAvailable || serverRtt < 0.0 || !IsFinite(serverRtt))
            {
                return DashValidationResult.Reject(DashRejectReason.RttUnavailable);
            }

            double oneWayDelay = serverRtt * 0.5;
            double estimatedServerStart = request.ClientNetworkLocalTime - oneWayDelay;

            if (history == null ||
                !history.TrySelectAtOrBefore(estimatedServerStart, snapshotFreshnessTolerance, out DashStateSnapshot snapshot))
            {
                return DashValidationResult.Reject(DashRejectReason.NoFreshSnapshot);
            }

            // 검증 순서: 당시 상태 → 당시 충전 (PLAN §9)
            if (snapshot.Dead || snapshot.Soul)
            {
                return DashValidationResult.Reject(DashRejectReason.DeadOrSoul);
            }

            if (snapshot.CrowdControlled)
            {
                return DashValidationResult.Reject(DashRejectReason.CrowdControlled);
            }

            if (snapshot.LandingProtected)
            {
                return DashValidationResult.Reject(DashRejectReason.LandingProtected);
            }

            if (!snapshot.Grounded)
            {
                return DashValidationResult.Reject(DashRejectReason.NotGrounded);
            }

            if (snapshot.ChargeCount <= 0)
            {
                return DashValidationResult.Reject(DashRejectReason.NoCharge);
            }

            // 과거 기준 승인. 남은 서버 대시 시간 산출.
            double estimatedServerEnd = estimatedServerStart + dashDuration;
            double remainingServerDuration = Math.Max(0.0, estimatedServerEnd - serverNow);

            // 현재 상태 재검사: 과거엔 유효했으나 요청 도착 전 CC/사망/Soul이면 충전은 인정하되 대시는 중단.
            bool interrupted = current.Dead || current.Soul || current.CrowdControlled;

            return DashValidationResult.Approve(remainingServerDuration, interrupted);
        }

        private static bool IsFinite(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
