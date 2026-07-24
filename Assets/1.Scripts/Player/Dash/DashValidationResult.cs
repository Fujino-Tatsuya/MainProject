namespace BeaverLobby.Player.Dash
{
    /// <summary>
    /// 대시 요청 검증 결과(불변). (PLAN §9)
    ///
    /// 승인(<see cref="IsApproved"/>)은 "충전 소비를 인정한다"는 의미다.
    /// 실제 새 대시 상태/무적을 시작할지는 <see cref="RemainingServerDuration"/> &gt; 0 이고
    /// <see cref="WasInterruptedByServerState"/> 가 false일 때만이다.
    /// - 요청 도착 시 이미 대시가 끝났으면 remaining = 0 (충전·회복 장부만 반영).
    /// - 과거엔 유효했으나 현재 CC/사망이면 interrupted = true (현재 상태 우선, Owner 대시 중단).
    /// </summary>
    internal readonly struct DashValidationResult
    {
        public readonly bool IsApproved;
        public readonly DashRejectReason Reason;
        public readonly double RemainingServerDuration;
        public readonly bool WasInterruptedByServerState;

        private DashValidationResult(bool approved, DashRejectReason reason, double remaining, bool interrupted)
        {
            IsApproved = approved;
            Reason = reason;
            RemainingServerDuration = remaining;
            WasInterruptedByServerState = interrupted;
        }

        public static DashValidationResult Reject(DashRejectReason reason)
            => new DashValidationResult(false, reason, 0.0, false);

        public static DashValidationResult Approve(double remainingServerDuration, bool interrupted)
            => new DashValidationResult(true, DashRejectReason.None, remainingServerDuration, interrupted);
    }
}
