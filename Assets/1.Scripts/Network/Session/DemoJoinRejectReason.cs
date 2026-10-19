/// <summary>
/// 부스 데모 접속이 안 된(또는 끊긴) 마지막 사유 — PLAN-48 §4.8.
/// <see cref="ReconnectLoop"/> 가 오버레이용으로 보존하고, DemoJoinRule(C6)이 승인 거절 Reason 과 이 코드를 서로 바꾼다.
/// </summary>
public enum DemoJoinRejectReason
{
    None,
    /// <summary>호스트가 로비가 아니거나 카운트다운 중. 호스트가 로비로 오면 자동으로 입장한다.</summary>
    GameInProgress,
    /// <summary>접속자 3명이 다 찼다.</summary>
    LobbyFull,
    /// <summary>NetworkConfig 해시가 다르다(빌드·설정이 다른 exe).</summary>
    HashMismatch,
    /// <summary>제한 시간 안에 접속되지 않았다(호스트가 꺼져 있거나 주소가 틀림).</summary>
    Timeout,
    Unknown,
}

public static class DemoJoinRejectReasonText
{
    public const int MaxPlayers = 3;

    /// <summary>오버레이(C5)에 띄울 사람이 읽는 문구. <see cref="DemoJoinRejectReason.None"/> 은 빈 문자열.</summary>
    public static string ToDisplayText(DemoJoinRejectReason reason)
    {
        switch (reason)
        {
            case DemoJoinRejectReason.None: return string.Empty;
            case DemoJoinRejectReason.GameInProgress: return "게임 진행 중 — 판이 끝나면 자동 입장";
            case DemoJoinRejectReason.LobbyFull: return $"인원 가득 ({MaxPlayers}/{MaxPlayers})";
            case DemoJoinRejectReason.HashMismatch: return "버전 불일치 — 호스트와 같은 빌드인지 확인";
            case DemoJoinRejectReason.Timeout: return "응답 없음 — 호스트를 기다리는 중";
            default: return "알 수 없는 이유로 연결되지 않음";
        }
    }
}
