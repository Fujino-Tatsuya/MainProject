namespace BeaverLobby.Player.Dash
{
    /// <summary>서버가 대시 요청을 거부한 사유. (PLAN §9 검증 순서)</summary>
    internal enum DashRejectReason
    {
        None = 0,

        /// <summary>대시 설정 비활성(Config 오류 또는 미수신).</summary>
        ConfigDisabled,

        /// <summary>NaN/Infinity 또는 0 방향 등 비정상 Payload.</summary>
        InvalidPayload,

        /// <summary>원격 Client RTT가 아직 0이거나 사용 불가.</summary>
        RttUnavailable,

        /// <summary>요청시각 이전의 신선한 Snapshot이 없음(History 범위 밖/freshness 초과).</summary>
        NoFreshSnapshot,

        /// <summary>당시 Dead 또는 Soul 상태.</summary>
        DeadOrSoul,

        /// <summary>당시 CC(군중 제어) 상태.</summary>
        CrowdControlled,

        /// <summary>당시 착지 보호 상태.</summary>
        LandingProtected,

        /// <summary>당시 Grounded 아님.</summary>
        NotGrounded,

        /// <summary>당시 사용 가능한 충전 없음.</summary>
        NoCharge,
    }
}
