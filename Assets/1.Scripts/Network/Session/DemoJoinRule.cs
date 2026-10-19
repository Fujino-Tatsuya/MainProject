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
        bool isPlayerIdConnected,
        bool isMatchActive = false,
        bool isInMatchRoster = false,
        bool isSceneTransitionInProgress = false)
    {
        IsHostSelf = isHostSelf;
        IsDemoFlow = isDemoFlow;
        IsInLobby = isInLobby;
        IsCountdownActive = isCountdownActive;
        ConnectedCount = connectedCount;
        MaxPlayers = maxPlayers;
        IsPlayerIdValid = isPlayerIdValid;
        IsPlayerIdConnected = isPlayerIdConnected;
        IsMatchActive = isMatchActive;
        IsInMatchRoster = isInMatchRoster;
        IsSceneTransitionInProgress = isSceneTransitionInProgress;
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
    /// <summary>판이 진행 중이다(<see cref="MatchRoster.IsActive"/>) — C14.</summary>
    public bool IsMatchActive { get; }
    /// <summary>이 playerId 가 판 명단에 있다(<see cref="MatchRoster.Contains"/>).</summary>
    public bool IsInMatchRoster { get; }
    /// <summary>호스트가 씬을 바꾸는 중이다(로딩 흐름·맵 → 로비 Single 전환). 늦은 씬 동기화를 NGO 씬 이벤트와 겹치지 않게 막는다.</summary>
    public bool IsSceneTransitionInProgress { get; }
}

/// <summary>
/// 부스 데모 접속 승인 — PLAN-48 §4.8. 로비이고, 카운트다운 중이 아니고, 접속자가 3명 미만이면 승인한다.
/// 판 도중에는 판 명단(<see cref="MatchRoster"/>) 안 playerId 만 다시 받는다(C14 재합류, 씬 전환 중이 아닐 때). 명단 밖 새 사람은
/// 판이 끝날 때까지 <see cref="DemoJoinRejectReason.GameInProgress"/>(백필 금지). 명단 안 사람이 씬 전환 중이거나 옛 연결이 아직
/// 남아 있으면 <see cref="DemoJoinRejectReason.RejoinWaiting"/> — 클라 재시도로 해소된다.
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

        var isRosterMember = request.IsMatchActive && request.IsInMatchRoster;
        // 판 명단 안 사람의 중복 = 끊긴 직후 재접속인데 서버가 옛 연결을 아직 못 끊었다(Disconnect 타임아웃) — 재시도로 해소된다(R-c).
        if (request.IsPlayerIdConnected)
            return isRosterMember ? DemoJoinRejectReason.RejoinWaiting : DemoJoinRejectReason.DuplicatePlayerId;
        if (request.IsInLobby && !request.IsCountdownActive)
            return request.ConnectedCount >= request.MaxPlayers ? DemoJoinRejectReason.LobbyFull : DemoJoinRejectReason.None;
        // 판 도중 재합류 — 명단 ≤ 3 이고 같은 playerId 중복은 위에서 막혔으니 인원은 보지 않는다.
        if (isRosterMember)
            return request.IsSceneTransitionInProgress ? DemoJoinRejectReason.RejoinWaiting : DemoJoinRejectReason.None;
        return DemoJoinRejectReason.GameInProgress;
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
