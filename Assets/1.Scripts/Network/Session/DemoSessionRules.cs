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

/// <summary>
/// DemoSessionDirector 의 순수 판단 — 켜질지(게이트), 끊긴 사유, 전송 타임아웃. EditMode 테스트 대상.
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
    /// NGO 가 멈춘 사유. 거절 Reason 문자열 → 코드 변환은 C6(DemoJoinRule)에서 붙인다 — 지금은
    /// 접속 중에 사유 없이 멈췄으면 응답 없음(Timeout), 그 밖은 Unknown.
    /// </summary>
    public static DemoJoinRejectReason ResolveStopReason(ReconnectState state, string disconnectReason)
    {
        if (state == ReconnectState.Connecting && string.IsNullOrWhiteSpace(disconnectReason))
            return DemoJoinRejectReason.Timeout;
        return DemoJoinRejectReason.Unknown;
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
