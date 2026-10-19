using System;

/// <summary>승인 콜백이 모은 서버 상태 — <see cref="DemoJoinRule.Evaluate"/> 의 입력.</summary>
public readonly struct DemoJoinRequest
{
    public DemoJoinRequest(
        bool isHostSelf,
        bool isDemoFlow,
        bool isInLobby,
        bool isCountdownActive,
        int connectedCount,
        int maxPlayers,
        bool isPlayerIdValid,
        bool isPlayerIdConnected)
    {
        IsHostSelf = isHostSelf;
        IsDemoFlow = isDemoFlow;
        IsInLobby = isInLobby;
        IsCountdownActive = isCountdownActive;
        ConnectedCount = connectedCount;
        MaxPlayers = maxPlayers;
        IsPlayerIdValid = isPlayerIdValid;
        IsPlayerIdConnected = isPlayerIdConnected;
    }

    /// <summary>StartHost 때 호스트 자신의 승인 요청.</summary>
    public bool IsHostSelf { get; }
    /// <summary>부스 데모 흐름(<see cref="DemoSessionDirector.IsDemoFlowActive"/>)인가. 아니면 종전처럼 항상 승인한다.</summary>
    public bool IsDemoFlow { get; }
    /// <summary>호스트의 활성 씬이 로비이고 로딩 흐름이 돌고 있지 않다.</summary>
    public bool IsInLobby { get; }
    public bool IsCountdownActive { get; }
    /// <summary>호스트를 포함해 이미 승인된 인원(승인됐지만 아직 접속 처리 전인 클라 포함).</summary>
    public int ConnectedCount { get; }
    public int MaxPlayers { get; }
    public bool IsPlayerIdValid { get; }
    /// <summary>같은 playerId 가 다른 clientId 로 이미 접속 중이다.</summary>
    public bool IsPlayerIdConnected { get; }
}

/// <summary>
/// 부스 데모 접속 승인 — PLAN-48 §4.8. 로비이고, 카운트다운 중이 아니고, 접속자가 3명 미만이면 승인한다.
/// 거절 사유는 <see cref="ToApprovalReason"/> 로 ConnectionApprovalResponse.Reason 에 넣고,
/// 클라는 NetworkManager.DisconnectReason 을 <see cref="TryParseApprovalReason"/> 로 되돌린다. 순수 함수다(EditMode 테스트 대상).
/// </summary>
public static class DemoJoinRule
{
    public const int MaxPlayers = DemoJoinRejectReasonText.MaxPlayers;
    /// <summary>거절 Reason 문자열 앞머리. 뒤에 enum 이름이 붙는다 — "demo-join:GameInProgress".</summary>
    public const string ReasonPrefix = "demo-join:";

    /// <summary>승인이면 <see cref="DemoJoinRejectReason.None"/>, 거절이면 그 사유.</summary>
    public static DemoJoinRejectReason Evaluate(in DemoJoinRequest request)
    {
        // 호스트 자신은 NGO 가 거절을 받지 않는다. 데모 흐름이 아니면(Dev 부팅·태그 없는 MPPM·옛 수동 접속) 종전처럼 다 받는다.
        if (request.IsHostSelf || !request.IsDemoFlow)
            return DemoJoinRejectReason.None;
        if (!request.IsPlayerIdValid)
            return DemoJoinRejectReason.InvalidPlayerId;
        if (request.IsPlayerIdConnected)
            return DemoJoinRejectReason.DuplicatePlayerId;
        if (!request.IsInLobby || request.IsCountdownActive)
            return DemoJoinRejectReason.GameInProgress;
        if (request.ConnectedCount >= request.MaxPlayers)
            return DemoJoinRejectReason.LobbyFull;
        return DemoJoinRejectReason.None;
    }

    /// <summary>서버가 response.Reason 에 넣을 문자열. None 은 빈 문자열(NGO 는 빈 Reason 이면 사유 메시지를 보내지 않는다).</summary>
    public static string ToApprovalReason(DemoJoinRejectReason reason)
    {
        return reason == DemoJoinRejectReason.None ? string.Empty : ReasonPrefix + reason;
    }

    /// <summary>
    /// 클라가 받은 DisconnectReason 이 우리 거절 코드면 그 사유를 돌려준다. 앞머리가 없거나 모르는 이름이면 false
    /// (구 빌드 호스트·NGO 자체 문구 등).
    /// </summary>
    public static bool TryParseApprovalReason(string disconnectReason, out DemoJoinRejectReason reason)
    {
        reason = DemoJoinRejectReason.None;
        if (string.IsNullOrEmpty(disconnectReason))
            return false;

        var text = disconnectReason.Trim();
        if (!text.StartsWith(ReasonPrefix, StringComparison.Ordinal))
            return false;

        var name = text.Substring(ReasonPrefix.Length);
        // Enum.TryParse 는 "3" 같은 숫자도 받는다 — 이름만 받는다.
        if (name.Length == 0 || !char.IsLetter(name[0]))
            return false;
        if (!Enum.TryParse(name, ignoreCase: false, out DemoJoinRejectReason parsed)
            || parsed == DemoJoinRejectReason.None
            || !Enum.IsDefined(typeof(DemoJoinRejectReason), parsed))
            return false;

        reason = parsed;
        return true;
    }
}
