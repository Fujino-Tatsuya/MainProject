using System;
using System.Globalization;

/// <summary>
/// 할 일 140-2 — 3페이즈 북진 무력화 간파 창(마지막 송전탑 파괴 → <c>pushNorthStunTime</c> 초, 기본 3) 전용 순수 규칙. 보스 쪽은 읽기만 한다.
/// <list type="bullet">
/// <item>판별: 봇이 본 활성(Live) 탑이 모두 사라진 뒤 <see cref="LinkSeconds"/> 안에 열린 창(<see cref="IsPushNorthWindow"/>). 호스트는 보스 기믹 값으로 한 번 더 거른다.</item>
/// <item>닫힘 = 탑이 사라진 시각(없으면 창 열림) + 무력화 시간 — 일반 창의 관측 최솟값(기본 0.8초) 대신 실제 창 길이(<see cref="CloseAt"/>).</item>
/// <item>포기 거리 = 마감까지 걸어서·대시로 갈 수 있는 거리(<see cref="ApproachSlack"/>). 일반 창은 기존 2.5m 그대로.</item>
/// <item>실패 굴림 누름 = 창 안(<see cref="MissPressAt"/>) — 일반 창의 '닫힘 + 150ms' 는 3초 창 밖이다. 누를 때 보스 반대쪽을 겨눠 일부러 빗나간다.</item>
/// <item>대쉬 간파: 마감까지 걸어서 못 닿고 대시 한 번 거리 안이면(<see cref="UseDashInterrupt"/>).</item>
/// </list>
/// 결과 줄 <c>[QA103] event=pn_counter</c>(<see cref="FormatLine"/>) — 야간 QA events.tsv 기록 전용 줄.
/// </summary>
public static class QABoss103PushNorthCounter
{
    /// <summary>활성 탑이 모두 사라진 뒤 이 안(초)에 열린 창 = 북진 무력화 창. 서버는 같은 틱에 연다 — 복제 지연 여유.</summary>
    public const double LinkSeconds = 1.0;
    /// <summary>무력화 시간을 못 읽을 때(초) — <c>BossDataSO.pushNorthStunTime</c> 기본값.</summary>
    public const double DefaultStunSeconds = 3.0;
    /// <summary>이동 속도를 못 읽을 때(m/s) — 보수값.</summary>
    public const double FallbackMoveSpeed = 5.0;
    /// <summary>대시 한 번 거리를 못 읽을 때(m) — 기본 대시 20m/s × 0.25초.</summary>
    public const double FallbackDashReach = 5.0;
    /// <summary>대쉬 간파는 대시 거리보다 이만큼(m) 덜 남았을 때만 — 대시 끝이 사거리 안에 들게.</summary>
    public const double DashReachMargin = 0.3;
    /// <summary>실패 굴림 누름이 창 닫힘보다 최소 이만큼(초) 앞.</summary>
    public const double MinInsideSeconds = 0.05;
    /// <summary>판정(누름 + 선딜) 시각보다 이만큼(초) 이상 일찍 닫혔으면 '판정 전에 닫힘'(다른 사람 간파 등).</summary>
    public const double JudgeSlackSeconds = 0.05;
    /// <summary>예상 닫힘보다 이만큼(초) 이상 일찍 닫혔으면 적중으로 본다(간파 성공 = 무력화가 바로 풀린다).</summary>
    public const double EarlyCloseSeconds = 0.25;

    public const string Event = "pn_counter";

    /// <summary>창 열림이 활성 탑 소멸(<paramref name="clearedAt"/>, 음수 = 못 봄) 뒤 <see cref="LinkSeconds"/> 안인가.</summary>
    public static bool IsPushNorthWindow(double openAt, double clearedAt, double link = LinkSeconds) =>
        clearedAt >= 0.0 && openAt >= clearedAt && openAt - clearedAt <= link;

    /// <summary>예상 닫힘 = (탑 소멸 시각, 창 열림보다 늦으면 열림) + 무력화 시간. 무력화 시간이 0 이하·NaN 이면 <see cref="DefaultStunSeconds"/>.</summary>
    public static double CloseAt(double openAt, double clearedAt, double stunSeconds)
    {
        double start = clearedAt >= 0.0 && clearedAt <= openAt ? clearedAt : openAt;
        double stun = stunSeconds > 0.0 ? stunSeconds : DefaultStunSeconds;
        return start + stun;
    }

    /// <summary>
    /// 포기 거리 여유(m, 사거리 밖으로 이만큼까지 굴린다) = 이동 속도 × (마감까지 − 반응 지연) + 대시 거리, 단 일반 창 여유(<paramref name="baseSlack"/>)보다 작지 않다.
    /// </summary>
    public static double ApproachSlack(double secondsToDeadline, double reactSeconds, double moveSpeed, double dashReach, double baseSlack)
    {
        double speed = moveSpeed > 0.0 ? moveSpeed : FallbackMoveSpeed;
        double walk = Math.Max(0.0, secondsToDeadline - Math.Max(0.0, reactSeconds)) * speed;
        return Math.Max(baseSlack, walk + Math.Max(0.0, dashReach));
    }

    /// <summary>
    /// 실패 굴림 누름 시각 — 일반 규칙(닫힘 + <paramref name="lateSeconds"/>)을 마감(<see cref="QABoss103HitTiming.CounterPressDeadline"/>) 안으로 당기고,
    /// 반응 지연보다 이르지 않게, 그래도 닫힘 − <see cref="MinInsideSeconds"/> 를 넘지 않는다.
    /// </summary>
    public static double MissPressAt(double openAt, double closeAt, double reactSeconds, double lateSeconds)
    {
        double t = Math.Min(closeAt + lateSeconds, QABoss103HitTiming.CounterPressDeadline(closeAt));
        t = Math.Max(t, openAt + Math.Max(0.0, reactSeconds));
        return Math.Min(t, closeAt - MinInsideSeconds);
    }

    /// <summary>
    /// 대쉬 간파를 쓸까 — 남은 틈(<paramref name="gap"/> = 표면 거리 − 사거리)이 있고 대시 한 번 거리(− 여유) 안이며,
    /// 걸어서는 마감까지 못 닿을 때.
    /// </summary>
    public static bool UseDashInterrupt(double gap, double secondsToDeadline, double moveSpeed, double dashReach)
    {
        if (!(gap > 0.0) || gap > dashReach - DashReachMargin)
            return false;
        double speed = moveSpeed > 0.0 ? moveSpeed : FallbackMoveSpeed;
        return gap / speed >= secondsToDeadline;
    }

    /// <summary>
    /// 결과 토큰. 시도 없음 = <c>none</c>(이유는 따로), 시전 거부 = <c>rejected</c>, 실패 굴림(일부러 빗나감) = <c>miss</c>,
    /// 판정 전에 닫힘 = <c>closed_before_judge</c>, 예상보다 일찍 닫힘 = <c>hit</c>(추정), 그 밖 = <c>miss</c>.
    /// </summary>
    public static string Outcome(bool tried, bool cast, bool missRoll, double judgeAt, double closedAt, double expectedCloseAt)
    {
        if (!tried)
            return "none";
        if (!cast)
            return "rejected";
        if (missRoll)
            return "miss";
        if (!double.IsNaN(closedAt) && closedAt < judgeAt - JudgeSlackSeconds)
            return "closed_before_judge";
        if (!double.IsNaN(closedAt) && closedAt < expectedCloseAt - EarlyCloseSeconds)
            return "hit";
        return "miss";
    }

    /// <summary>창 1개 결과 줄(<c>[QA103] event=pn_counter …</c>) — 값 토큰에 공백이 없어야 한다.</summary>
    public static string FormatLine(string tag, string bot, int slot, string tier, double openGameT, double openServerT, double windowS,
                                    double distM, double surfM, double reachM, double slackM, string roll, bool tried, bool dash,
                                    string result, string reason, double pressMs, double closeMs)
    {
        return $"{tag} event={Event} bot={bot} slot={slot.ToString(CultureInfo.InvariantCulture)} tier={tier} openT={F(openGameT, "F2")} openServerT={F(openServerT, "F3")} " +
               $"windowMs={F(windowS * 1000.0, "F0")} distM={F(distM, "F2")} surfM={F(surfM, "F2")} reachM={F(reachM, "F2")} slackM={F(slackM, "F2")} " +
               $"roll={Tok(roll)} tried={(tried ? "true" : "false")} dash={(dash ? "true" : "false")} result={Tok(result)} reason={Tok(reason)} " +
               $"pressMs={F(pressMs, "F0")} closeMs={F(closeMs, "F0")}";
    }

    private static string F(double v, string fmt) => double.IsNaN(v) || double.IsInfinity(v) ? "na" : v.ToString(fmt, CultureInfo.InvariantCulture);

    private static string Tok(string s) => string.IsNullOrEmpty(s) ? "na" : s.Replace(' ', '_').Replace('\t', '_');
}
