using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// [서버] 간파 지연 측정 기록기(할 일 74 후속 — 측정만, 보정 없음). 판정 결과를 바꾸지 않는다.
/// <list type="bullet">
/// <item>카운터 창 소유자(<see cref="IInterruptSlowMotionTarget"/> 구현체)가 창 열림/닫힘 때 <see cref="RecordWindow"/> 를 부른다
/// (간파 선착순 장부 <see cref="InterruptWindowClaims"/> 도 이 전이를 받는다 — 할 일 143).</item>
/// <item>간파 판정(<see cref="PlayerInterruptSkillBase"/>)이 <see cref="ReportJudgement"/> 로 1줄 로그 + 집계.</item>
/// <item>판 종료(<see cref="SessionStatsTracker"/> 확정·비활성, 앱 종료) 때 <see cref="FlushSummary"/> 가 요약 줄을 찍는다.</item>
/// </list>
/// 🔴 Debug.Log 를 그대로 쓴다 — Development·DEV_HOTKEYS 릴리스 빌드의 Player.log 에도 남아야 QA 자동화가 파싱한다.
/// 키 목록·보정 판단 절차: Docs/tech/networking.md "Interrupt lag measurement".
/// </summary>
public static class InterruptLagMonitor
{
    public const string ReasonSessionEnd = "session_end";
    public const string ReasonSceneEnd = "scene_end";
    public const string ReasonQuit = "quit";

    private static readonly Dictionary<GameObject, InterruptWindowHistory> Histories =
        new Dictionary<GameObject, InterruptWindowHistory>();
    private static readonly InterruptLagSummary Summary = new InterruptLagSummary();
    private static readonly List<string> Lines = new List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Histories.Clear();
        Summary.Clear();
        Application.quitting -= HandleQuitting;
        Application.quitting += HandleQuitting;
    }

    /// <summary>[서버] 지금 서버 시각. 세션이 없거나 서버가 아니면 false — 오프라인·클라에선 기록하지 않는다.</summary>
    public static bool TryServerNow(out double serverTime)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || !nm.IsServer)
        {
            serverTime = 0d;
            return false;
        }

        serverTime = nm.ServerTime.Time;
        return true;
    }

    /// <summary>
    /// [서버] 카운터 창 전이 기록. 키 = 소유자 GameObject(간파가 판정 콜라이더에서 찾는 Unit 과 같은 GameObject).
    /// 간파 선착순 장부(<see cref="InterruptWindowClaims"/>, 할 일 143)에도 같은 전이를 넘긴다 — 판정에 쓰이는 유일한 몫이다(장부는 서버 판정만 읽는다).
    /// </summary>
    public static void RecordWindow(GameObject owner, bool open)
    {
        if (owner == null)
            return;

        InterruptWindowClaims.Server.RecordWindow(owner.GetInstanceID(), open);
        if (!TryServerNow(out double now))
            return;

        if (!Histories.TryGetValue(owner, out InterruptWindowHistory history))
        {
            history = new InterruptWindowHistory();
            Histories.Add(owner, history);
        }

        history.Record(now, open);
    }

    /// <summary>[서버] 그 소유자의 <paramref name="serverTime"/> 시각 창 상태(기록 기준).</summary>
    public static InterruptWindowState WindowStateAt(GameObject owner, double serverTime) =>
        owner != null && Histories.TryGetValue(owner, out InterruptWindowHistory history)
            ? history.StateAt(serverTime)
            : InterruptWindowState.Unknown;

    /// <summary>
    /// [서버] 간파 시도 예측(할 일 132)용 창 추정 — 지금 열린 뒤 경과·추정 길이(초, 서버 시각 기준). 모르면 -1.
    /// 예측(연출 강·약)에만 쓰고 판정에는 쓰지 않는다.
    /// </summary>
    public static void EstimateWindow(GameObject owner, bool openNow, out float openElapsed, out float expectedDuration)
    {
        openElapsed = -1f;
        expectedDuration = -1f;
        if (owner == null || !Histories.TryGetValue(owner, out InterruptWindowHistory history) || !TryServerNow(out double now))
            return;

        history.Estimate(now, openNow, out openElapsed, out expectedDuration);
    }

    /// <summary>[서버] 보낸 클라의 RTT(ms). 호스트 자신 = 0.</summary>
    public static double RttMs(ulong clientId)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || clientId == NetworkManager.ServerClientId || clientId == nm.LocalClientId)
            return 0d;

        NetworkTransport transport = nm.NetworkConfig != null ? nm.NetworkConfig.NetworkTransport : null;
        return transport != null ? transport.GetCurrentRtt(clientId) : 0d;
    }

    public static void ReportJudgement(in InterruptLagSample sample)
    {
        Summary.Record(sample);
        Debug.Log(sample.ToLogLine());
    }

    /// <summary>
    /// 요약 줄을 찍고 집계·창 기록을 비운다. 판 종료(<see cref="ReasonSessionEnd"/>)는 서버면 0건이어도 찍고,
    /// 그 밖의 사유는 찍지 않은 시도가 남았을 때만 찍는다(판 종료 뒤 씬 정리에서 같은 판이 두 번 찍히지 않게).
    /// </summary>
    public static void FlushSummary(string reason)
    {
        bool force = reason == ReasonSessionEnd && TryServerNow(out _);
        if (Summary.IsEmpty && !force)
        {
            Histories.Clear();
            return;
        }

        Lines.Clear();
        Summary.AppendLogLines(reason, Lines);
        foreach (string line in Lines)
            Debug.Log(line);

        Summary.Clear();
        Histories.Clear();
    }

    private static void HandleQuitting() => FlushSummary(ReasonQuit);
}
