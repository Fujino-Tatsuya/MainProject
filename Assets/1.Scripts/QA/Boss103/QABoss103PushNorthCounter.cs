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
/// <item>할 일 140-4 성공 굴림 누름: 실제 표면 거리(AABB 아님) ≤ 사거리 − 여유, 정면 한도 안쪽에 멈춘 뒤(<see cref="ShouldPress"/>). 보스가 대각선(화면 남쪽)을 보면
///       AABB 거리가 실제보다 최대 0.7m 짧아 사거리 끝(AABB 1.73m / 사거리 1.75m)에서 누른 간파가 서버 판정에 안 닿았다.</item>
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
    /// <summary>할 일 140-4 — 호스트 계측기가 북진 중 서버 간파 판정마다 남기는 줄(<see cref="FormatServerCastLine"/>).</summary>
    public const string ServerCastEvent = "pn_server_cast";

    // ── 할 일 140-4 — 성공 굴림 누름 자리(사거리 끝 2cm 에서 눌러 서버 판정이 안 닿았다) ──

    /// <summary>누름 자리 여유(m) — 실제 표면 거리 ≤ 사거리 − 이 값. 사거리가 작으면 <see cref="PressMarginRatio"/> 비율로 줄인다.</summary>
    public const double PressMarginM = 0.5;
    /// <summary>사거리가 작을 때 여유 = 사거리 × 이 비율(<see cref="PressMarginM"/> 보다 작으면 이쪽).</summary>
    public const double PressMarginRatio = 0.3;
    /// <summary>정면 여유(도) — 서버 <c>counterFrontAngle</c> 보다 이만큼 안쪽에서 누른다(한도의 절반보다 좁히지는 않는다).</summary>
    public const double FrontMarginDeg = 15.0;
    /// <summary>멈춘 뒤 누르기까지 — 이동 입력을 끊고 최소 이 틱 수 + <see cref="SettleSeconds"/> 초(서버에 보이는 위치가 따라오게).</summary>
    public const int SettleTicks = 2;
    public const double SettleSeconds = 0.1;
    /// <summary>마감 이만큼(초) 앞부터는 여유 자리에 못 갔어도 사거리 안이면 누른다(마감 순간엔 조건 없이 1회 — 기존 규칙).</summary>
    public const double FallbackLeadSeconds = 0.4;
    /// <summary>누른 뒤 판정 + 이만큼(초)까지 멈춰 있는다 — 선딜 동안 다른 행동(원거리 거리 벌리기)이 사거리 밖으로 끌고 가지 않게.</summary>
    public const double PostPressHoldSeconds = 0.1;

    /// <summary>누름 자리 여유(m) = min(<see cref="PressMarginM"/>, 사거리 × <see cref="PressMarginRatio"/>). 사거리 0 이하·NaN = 0.</summary>
    public static double PressMargin(double reach) =>
        reach > 0.0 ? Math.Min(PressMarginM, reach * PressMarginRatio) : 0.0;

    /// <summary>누름 자리 = 실제 표면 거리가 이 값 이하(사거리 − <see cref="PressMargin"/>).</summary>
    public static double PressDistance(double reach) => reach > 0.0 ? reach - PressMargin(reach) : 0.0;

    /// <summary>봇이 쓰는 정면 한도(도) — 서버 한도에서 <see cref="FrontMarginDeg"/> 안쪽, 한도의 절반보다 좁히지 않는다. 정면 판정이 꺼져 있으면 180.</summary>
    public static double FrontLimit(double serverLimitDeg, bool requiresFront)
    {
        if (!requiresFront)
            return 180.0;
        double limit = Math.Max(0.0, Math.Min(180.0, serverLimitDeg));
        return Math.Max(limit * 0.5, limit - FrontMarginDeg);
    }

    /// <summary>누름 자리 서기 = 실제 표면 거리 ≤ <see cref="PressDistance"/> + 정면각 ≤ <see cref="FrontLimit"/>.</summary>
    public static bool InPressSpot(double trueSurfM, double reach, double frontDeg, double serverLimitDeg, bool requiresFront) =>
        trueSurfM <= PressDistance(reach) && frontDeg <= FrontLimit(serverLimitDeg, requiresFront);

    /// <summary>멈춤이 충분한가 — 이동 입력 0 인 틱 수와 시간이 둘 다 채워졌다.</summary>
    public static bool IsSettled(int stillTicks, double stillSeconds) =>
        stillTicks >= SettleTicks && stillSeconds >= SettleSeconds;

    /// <summary>
    /// 성공 굴림 누름 판단. 마감(<paramref name="deadline"/>) = 조건 없이 1회, 마감 − <see cref="FallbackLeadSeconds"/> 부터 = 사거리 안(<paramref name="inReach"/>)이면,
    /// 그 전 = 누름 자리 + 멈춤 충분.
    /// </summary>
    public static bool ShouldPress(double now, double deadline, bool inPressSpot, bool settled, bool inReach)
    {
        if (now >= deadline)
            return true;
        if (now >= deadline - FallbackLeadSeconds && inReach)
            return true;
        return inPressSpot && settled;
    }

    /// <summary>수직 캡슐(원기둥) 표면까지 수평 거리 = 중심축 거리 − 반경(안이면 0). AABB 근사가 대각선에서 최대 반경 × (√2 − 1) 짧게 본다.</summary>
    public static double CylinderSurfaceDistance(double px, double pz, double cx, double cz, double radius)
    {
        double dx = px - cx, dz = pz - cz;
        return Math.Max(0.0, Math.Sqrt(dx * dx + dz * dz) - Math.Max(0.0, radius));
    }

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

    /// <summary>
    /// 서버 판정 1회 줄(<c>[QA103] event=pn_server_cast …</c>) — 서버가 본 시전자 위치·보스까지 실제 표면 거리(<c>trueSurfM</c>)·AABB 거리(<c>aabbSurfM</c>, 봇·오너 예측이 쓰는 값)·
    /// 전방위 사거리·정면각·판정 앵커가 지금 보스 HurtBox 에 겹치나(<c>overlap</c>)·수신 수·성공. 값 토큰에 공백이 없어야 한다.
    /// </summary>
    public static string FormatServerCastLine(string tag, string bot, int slot, string step, bool windowOpen, int resolved, bool success,
                                              double x, double z, double bossX, double bossZ, double trueSurfM, double aabbSurfM, double reachM,
                                              double frontDeg, double frontLimitDeg, string overlap, double serverT)
    {
        return $"{tag} event={ServerCastEvent} bot={Tok(bot)} slot={slot.ToString(CultureInfo.InvariantCulture)} step={Tok(step)} windowOpen={(windowOpen ? "true" : "false")} " +
               $"resolved={resolved.ToString(CultureInfo.InvariantCulture)} success={(success ? "true" : "false")} pos={F(x, "F2")}/{F(z, "F2")} boss={F(bossX, "F2")}/{F(bossZ, "F2")} " +
               $"trueSurfM={F(trueSurfM, "F2")} aabbSurfM={F(aabbSurfM, "F2")} reachM={F(reachM, "F2")} frontDeg={F(frontDeg, "F0")} frontLimit={F(frontLimitDeg, "F0")} " +
               $"overlap={Tok(overlap)} serverT={F(serverT, "F3")}";
    }

    private static string F(double v, string fmt) => double.IsNaN(v) || double.IsInfinity(v) ? "na" : v.ToString(fmt, CultureInfo.InvariantCulture);

    private static string Tok(string s) => string.IsNullOrEmpty(s) ? "na" : s.Replace(' ', '_').Replace('\t', '_');
}
