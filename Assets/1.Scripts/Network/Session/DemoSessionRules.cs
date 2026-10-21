using System;

/// <summary>UnityTransport 에 덮어쓸 연결·끊김 타임아웃 — PLAN-48 §4.1 끝. NetworkConfig 해시와 무관하다.</summary>
public readonly struct DemoTransportTimeouts
{
    public DemoTransportTimeouts(int connectTimeoutMS, int maxConnectAttempts, int disconnectTimeoutMS)
    {
        ConnectTimeoutMS = connectTimeoutMS;
        MaxConnectAttempts = maxConnectAttempts;
        DisconnectTimeoutMS = disconnectTimeoutMS;
    }

    /// <summary>시도 한 번의 대기 시간.</summary>
    public int ConnectTimeoutMS { get; }
    public int MaxConnectAttempts { get; }
    public int DisconnectTimeoutMS { get; }
}

/// <summary>타이틀 START 를 누른 뒤 갈 곳 — 할 일 70(부팅하면 호스트·클라 모두 타이틀을 거친다).</summary>
public enum DemoTitleStartRoute
{
    /// <summary>데모 흐름이 꺼져 있다 — 종전처럼 로비를 로컬로 로드한다(수동 접속 패널).</summary>
    LoadLobbyLocally,
    /// <summary>부팅 뒤 첫 START — 데모 흐름(대기 씬 → 호스팅/자동 접속)을 시작한다.</summary>
    BeginDemoFlow,
    /// <summary>데모 흐름이 이미 돌고 있다 — 아무것도 안 한다(살아 있는 세션 위에 로비를 로컬 로드하면 안 된다).</summary>
    Ignore,
}

/// <summary>
/// DemoSessionDirector 의 순수 판단 — 켜질지(게이트), 타이틀 START 뒤 갈 곳, 끊긴 사유, 전송 타임아웃. EditMode 테스트 대상.
/// </summary>
public static class DemoSessionRules
{
    public const string BootstrapSceneName = "0.BootStrapScene";

    /// <summary>
    /// 부스 데모 흐름을 켤지. 0.BootStrapScene 에서 시작한 메인 흐름일 때만 켠다.
    /// <list type="bullet">
    /// <item>툴바 Dev 부팅은 Dev_Boot 씬, 디버그 씬 Play 는 그 씬이 활성 씬이라 꺼진다(같은 NetworkManager.prefab 이 들어 있어도).</item>
    /// <item>MPPM 가상 플레이어는 <c>Client</c> 태그가 있을 때만 켠다. 태그 없이 켜면 가상 플레이어마다 같은 PC 의 7777 에서
    /// 호스트를 시도해 무한 재시도에 빠진다 — 태그가 없으면 기존 타이틀 → 로비 수동 접속 흐름을 그대로 쓴다.</item>
    /// </list>
    /// </summary>
    public static bool ShouldRunDemoFlow(bool enabledToggle, string activeSceneName, bool isVirtualPlayer, bool hasClientTag)
    {
        if (!enabledToggle)
            return false;
        if (!string.Equals(activeSceneName, BootstrapSceneName, StringComparison.Ordinal))
            return false;
        return !isVirtualPlayer || hasClientTag;
    }

    /// <summary>
    /// 타이틀 START(시작 연출 끝) 뒤 갈 곳. 데모 흐름도 부팅 직후엔 타이틀을 거치고, START 를 눌러야 대기 씬 →
    /// 호스팅/자동 접속을 시작한다(할 일 70). 판 끝 로비 복귀·재접속 대기는 타이틀을 다시 거치지 않으므로
    /// 시작은 실행당 한 번뿐이다.
    /// </summary>
    public static DemoTitleStartRoute ResolveTitleStart(bool isDemoFlowActive, bool hasBegun)
    {
        if (!isDemoFlowActive)
            return DemoTitleStartRoute.LoadLobbyLocally;
        return hasBegun ? DemoTitleStartRoute.Ignore : DemoTitleStartRoute.BeginDemoFlow;
    }

    /// <summary>
    /// NGO 가 멈춘 사유(<paramref name="disconnectReason"/> = NetworkManager.DisconnectReason).
    /// <list type="number">
    /// <item>서버 승인 거절 코드(<see cref="DemoJoinRule.ToApprovalReason"/>)면 그 사유. NGO 는 이 문자열을 DisconnectReasonMessage 로 보내고
    /// 클라의 DisconnectReason 이 그대로 돌려준다.</item>
    /// <item>접속 중에 사유가 없거나 전송 계층이 연결을 못 맺었으면(<c>[MaxConnectionAttempts]</c>·<c>[ProtocolTimeout]</c>) 응답 없음.</item>
    /// <item>접속 중에 서버가 사유 없이 끊었으면(<c>[ClosedByRemote]</c>) 버전 불일치로 본다 — NGO 는 NetworkConfig 해시가 다르면
    /// 승인 콜백 전에 사유 없이 끊는다(ConnectionRequestMessage.Deserialize). 우리 승인 콜백은 거절할 때 늘 사유를 넣으므로
    /// 남는 경우는 해시 불일치, 그리고 드문 핸드셰이크 중 호스트 종료·연결 요청 버퍼 타임아웃뿐이다.</item>
    /// <item>그 밖(접속 후 끊김 등)은 Unknown.</item>
    /// </list>
    /// NGO 의 DisconnectReason 은 <c>"[Disconnect Event][Client-N][TransportClientId-M][이벤트] 문구"</c> 형식이다.
    /// </summary>
    public static DemoJoinRejectReason ResolveStopReason(ReconnectState state, string disconnectReason)
    {
        if (DemoJoinRule.TryParseApprovalReason(disconnectReason, out var rejected))
            return rejected;
        if (state != ReconnectState.Connecting)
            return DemoJoinRejectReason.Unknown;
        if (string.IsNullOrWhiteSpace(disconnectReason)
            || ContainsTransportEvent(disconnectReason, "MaxConnectionAttempts")
            || ContainsTransportEvent(disconnectReason, "ProtocolTimeout"))
            return DemoJoinRejectReason.Timeout;
        if (ContainsTransportEvent(disconnectReason, "ClosedByRemote"))
            return DemoJoinRejectReason.HashMismatch;
        return DemoJoinRejectReason.Unknown;
    }

    static bool ContainsTransportEvent(string disconnectReason, string eventName)
    {
        return disconnectReason.IndexOf($"[{eventName}]", StringComparison.Ordinal) >= 0;
    }

    /// <summary>
    /// 연결 제한 시간을 1초 남짓한 시도 여러 번으로 나눈다(전송 계층은 시도 간격 × 횟수가 지나야 실패를 낸다).
    /// 5초 → 1000ms × 5회, 2.5초 → 833ms × 3회.
    /// </summary>
    public static DemoTransportTimeouts ComputeTransportTimeouts(float connectTimeoutSeconds, float disconnectTimeoutSeconds)
    {
        var connectMs = Math.Max(1, (int)Math.Round(connectTimeoutSeconds * 1000d));
        var attempts = Math.Max(1, (int)Math.Ceiling(connectMs / 1000d));
        var perAttemptMs = Math.Max(1, (int)Math.Round((double)connectMs / attempts));
        var disconnectMs = Math.Max(1, (int)Math.Round(disconnectTimeoutSeconds * 1000d));
        return new DemoTransportTimeouts(perAttemptMs, attempts, disconnectMs);
    }
}
