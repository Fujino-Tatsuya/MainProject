using System.Collections.Generic;
using System.Globalization;

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
    /// <summary>같은 playerId 가 이미 접속 중이다. 끊긴 직후 재접속이면 서버가 이전 연결을 정리한 뒤 들어간다.</summary>
    DuplicatePlayerId,
    /// <summary>ConnectionData 의 playerId 가 비었거나 깨졌다.</summary>
    InvalidPlayerId,
    Unknown,
    /// <summary>판 명단 안 사람이 다시 들어오는 중인데 아직 받을 수 없다 — 씬 전환 중이거나 서버가 옛 연결을 아직 못 끊었다(C14 Q-E).
    /// 재시도하면 들어간다.</summary>
    RejoinWaiting,
}

public static class DemoJoinRejectReasonText
{
    public const int MaxPlayers = 3;

    /// <summary>오버레이(C5)에 띄울 사람이 읽는 문구(<see cref="DemoConnectUiStrings"/> 키). <see cref="DemoJoinRejectReason.None"/> 은 빈 문자열.</summary>
    public static string ToDisplayText(DemoJoinRejectReason reason)
    {
        switch (reason)
        {
            case DemoJoinRejectReason.None: return string.Empty;
            case DemoJoinRejectReason.GameInProgress: return DemoConnectUiStrings.GameInProgress.Text;
            case DemoJoinRejectReason.LobbyFull:
                return DemoConnectUiStrings.LobbyFull.Format(new Dictionary<string, string>
                {
                    ["max"] = MaxPlayers.ToString(CultureInfo.InvariantCulture),
                });
            case DemoJoinRejectReason.HashMismatch: return DemoConnectUiStrings.HashMismatch.Text;
            case DemoJoinRejectReason.Timeout: return DemoConnectUiStrings.Timeout.Text;
            case DemoJoinRejectReason.DuplicatePlayerId: return DemoConnectUiStrings.DuplicatePlayerId.Text;
            case DemoJoinRejectReason.InvalidPlayerId: return DemoConnectUiStrings.InvalidPlayerId.Text;
            case DemoJoinRejectReason.RejoinWaiting: return DemoConnectUiStrings.RejoinWaiting.Text;
            default: return DemoConnectUiStrings.UnknownReason.Text;
        }
    }
}
