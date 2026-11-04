using System;
using System.Globalization;
using BeaverLobby.Player.Dash;

/// <summary>할 일 193 — 창 시간표 관측 한 틱의 변화.</summary>
public enum QABoss103WindowEdge
{
    None = 0,
    /// <summary>새 창이 열렸다(창 id 가 바뀌었고 지금 열려 있다).</summary>
    Opened = 1,
    /// <summary>추적하던 창이 닫혔다(조기 닫힘·예정 닫힘·다음 창으로 교체).</summary>
    Closed = 2,
}

/// <summary>
/// 할 일 193 — 봇의 '누를 타이밍' 원본 = <see cref="InterruptManager"/> 창 시간표 복제(<see cref="InterruptWindowRecord"/>).
/// 대상의 가장 최근 창(<see cref="InterruptManager.TryGetLatestWindow"/>)을 매 틱 넣으면 열림·닫힘 엣지를 낸다. 순수 C# — EditMode 고정.
/// <list type="bullet">
/// <item>열림 = 지금(서버 시각) <see cref="InterruptWindowRecord.IsOpenAt"/>. 예정 닫힘이 지났는데 닫힘 배포가 없으면 닫힌 것으로 본다(판정과 같은 규칙).</item>
/// <item>같은 틱에 앞 창 닫힘 + 새 창 열림이면 이번 틱 = 닫힘, 다음 틱 = 열림.</item>
/// <item>한 번 연 창 id 는 다시 열지 않는다(늦은 닫힘 갱신이 와도).</item>
/// </list>
/// </summary>
public sealed class QABoss103WindowWatch
{
    private uint _lastOpenedId;

    /// <summary>지금 추적 중인 열린 창(없으면 <see cref="InterruptWindowRecord.WindowId"/> = 0).</summary>
    public InterruptWindowRecord Current { get; private set; }

    public bool IsOpen => Current.WindowId != 0;

    /// <summary>처음부터 다시 — 지금 열려 있는 창도 다음 틱에 새로 연다(봇이 쉬었다 돌아온 경우).</summary>
    public void Reset()
    {
        _lastOpenedId = 0;
        Current = default;
    }

    /// <summary>
    /// 한 틱. <paramref name="hasRecord"/>·<paramref name="latest"/> = 대상의 가장 최근 창, <paramref name="serverNow"/> = 지금 서버 시각.
    /// 닫힘 엣지면 <paramref name="closed"/> = 닫힌 창(마지막으로 본 값 — 같은 id 면 최신 기록).
    /// </summary>
    public QABoss103WindowEdge Step(bool hasRecord, in InterruptWindowRecord latest, double serverNow, out InterruptWindowRecord closed)
    {
        closed = default;
        bool latestOpen = hasRecord && latest.WindowId != 0 && latest.IsOpenAt(serverNow);

        if (IsOpen)
        {
            bool same = hasRecord && latest.WindowId == Current.WindowId;
            if (same && latestOpen)
            {
                Current = latest;   // 늦은 닫힘·닫힘 예정 갱신을 따라간다
                return QABoss103WindowEdge.None;
            }
            closed = same ? latest : Current;
            Current = default;
            return QABoss103WindowEdge.Closed;
        }

        if (latestOpen && latest.WindowId != _lastOpenedId)
        {
            _lastOpenedId = latest.WindowId;
            Current = latest;
            return QABoss103WindowEdge.Opened;
        }
        return QABoss103WindowEdge.None;
    }
}

/// <summary>할 일 193 — 봇이 이번 틱 간파를 어떻게 할까(<see cref="QABoss103InterruptPress.Decide"/>).</summary>
public enum QABoss103PressCall
{
    /// <summary>예측 강 + 시전 가능 — 누른다.</summary>
    Press = 0,
    /// <summary>판정 상자는 닿지만 지금은 못 누르거나(시전 중·CC) 창 기록을 아직 못 받았다 — 기다린다.</summary>
    Wait = 1,
    /// <summary>판정 상자가 안 닿는다(범위·정면) — 다가간다.</summary>
    Approach = 2,
    /// <summary>판정 시각에 창이 닫혀 있다·지목자 아님·대상 간파 불가·시전자 쓰러짐 — 이 창은 포기.</summary>
    GiveUp = 3,
}

/// <summary>
/// 할 일 193 — 봇의 '누를지 말지' = <see cref="InterruptManager.Predict"/> 결과(사람이 누를 때 오너 시도 cue 와 같은 계산). 봇 전용 형상 계산은 없다.
/// 순수 함수 — EditMode 고정.
/// </summary>
public static class QABoss103InterruptPress
{
    /// <summary>
    /// 예측에서 판정 상자가 대상에 닿았나 — 성공, 또는 범위·정면을 지나 그 뒤 단계(생존·CC·간파 불가·창·지목자)에서 막힌 실패.
    /// 계산기는 범위·정면에서 막힌 대상을 '대상 없음'으로 낸다(<c>InterruptJudgeCalculator.Evaluate</c>).
    /// </summary>
    public static bool InReach(in InterruptJudgeResult prediction) =>
        prediction.Outcome == InterruptJudgeOutcome.Success ||
        prediction.Outcome == InterruptJudgeOutcome.Failure && prediction.FailReason > InterruptJudgeFailReason.Angle;

    /// <summary>예측 + 시전 가능(Idle/Move·다른 스킬 없음) → 이번 틱 할 일.</summary>
    public static QABoss103PressCall Decide(in InterruptJudgeResult prediction, bool castable)
    {
        switch (prediction.Outcome)
        {
            case InterruptJudgeOutcome.Success:
                return castable ? QABoss103PressCall.Press : QABoss103PressCall.Wait;
            case InterruptJudgeOutcome.NoTarget:
                return QABoss103PressCall.Approach;
        }
        switch (prediction.FailReason)
        {
            case InterruptJudgeFailReason.OutOfRange:
            case InterruptJudgeFailReason.Angle:
                return QABoss103PressCall.Approach;
            case InterruptJudgeFailReason.CrowdControlled:
            case InterruptJudgeFailReason.NoWindow:
                return QABoss103PressCall.Wait;
            default:
                return QABoss103PressCall.GiveUp;
        }
    }

    /// <summary>
    /// 창 닫힘 예정(서버 시각) → 봇 실시간. 남은 서버 초를 그대로 더한다(슬로우가 끼면 실제 닫힘은 이보다 늦다 — 보수 쪽).
    /// 길이 모름(+∞)·NaN 이면 +∞.
    /// </summary>
    public static double CloseAtReal(double nowReal, double serverNow, double plannedCloseServer)
    {
        if (double.IsNaN(plannedCloseServer) || double.IsPositiveInfinity(plannedCloseServer) || double.IsNaN(serverNow))
            return double.PositiveInfinity;
        return nowReal + Math.Max(0.0, plannedCloseServer - serverNow);
    }

    /// <summary>
    /// 지금 눌렀다고 보고 예측할 누른 시각(서버) = 직전 LateUpdate 샘플 시각 — 매니저는 LateUpdate 에서 시계를 맞춘 뒤 그 시각으로 샘플한다.
    /// <paramref name="localNow"/>·<paramref name="deltaTime"/> = 이번 Update 의 <c>Time.timeAsDouble</c>·<c>Time.deltaTime</c>.
    /// 이력 끝을 넘지 않게 <see cref="SampleEpsilon"/> 만큼 당긴다.
    /// </summary>
    public static double ProbeLocalTime(double localNow, float deltaTime) => localNow - Math.Max(0f, deltaTime) - SampleEpsilon;

    public const double SampleEpsilon = 1e-4;

    /// <summary>로그 토큰 — <c>Strong</c>·<c>Weak_WindowClosed</c>·<c>NoTarget</c>.</summary>
    public static string Token(in InterruptJudgeResult prediction)
    {
        InterruptAttemptForecast f = prediction.Forecast;
        return f == InterruptAttemptForecast.Weak ? $"{f}_{prediction.FailReason}" : f.ToString();
    }
}

/// <summary>
/// 할 일 193 — 봇 한 명의 간파 신고 결과 집계(시도·오너 판정 성공·서버 승인 첫/동시·거부 사유별·응답 없음).
/// 출처 = 191 <see cref="InterruptQaRecorder.RecordCompleted"/> 한 줄(봇이 자기 오너 기록만 판 파일 <c>claim</c> 줄로 옮긴다) — 별도 기록기 없음.
/// </summary>
public sealed class QABoss103ClaimTally
{
    /// <summary>거부 사유 열 순서 — <see cref="InterruptClaimRejectReason"/> 1~5.</summary>
    public static readonly InterruptClaimRejectReason[] RejectReasons =
    {
        InterruptClaimRejectReason.OutOfRange,
        InterruptClaimRejectReason.Angle,
        InterruptClaimRejectReason.WindowClosed,
        InterruptClaimRejectReason.FirstComeLost,
        InterruptClaimRejectReason.ServerRejected,
    };

    /// <summary>CSV 열 꼬리(<c>claim_reject_</c> 뒤).</summary>
    public static readonly string[] RejectTokens = { "out_of_range", "angle", "window_closed", "first_come", "server" };

    /// <summary>간파 1회 = QA 기록 1줄(누름마다, 판정 전 끊김·시전 거부 포함).</summary>
    public int Attempts;
    /// <summary>오너 판정 성공(= 신고 1회).</summary>
    public int JudgeSuccess;
    public int ApprovedFirst, ApprovedSimultaneous, Rejected;
    /// <summary>신고했는데 제한 시간 안 응답 없음.</summary>
    public int NoResponse;
    /// <summary>거부 사유별 — 순서 <see cref="RejectReasons"/>.</summary>
    public readonly int[] RejectByReason = new int[5];

    public int Approved => ApprovedFirst + ApprovedSimultaneous;

    public void Add(InterruptQaJudge judge, InterruptQaServer server, InterruptClaimRejectReason reject)
    {
        Attempts++;
        if (judge == InterruptQaJudge.Success)
            JudgeSuccess++;
        switch (server)
        {
            case InterruptQaServer.ApprovedFirst:
                ApprovedFirst++;
                break;
            case InterruptQaServer.ApprovedSimultaneous:
                ApprovedSimultaneous++;
                break;
            case InterruptQaServer.Rejected:
                Rejected++;
                int i = Array.IndexOf(RejectReasons, reject);
                RejectByReason[i >= 0 ? i : RejectByReason.Length - 1]++;   // 모르는 사유 = 그 밖의 서버 거부
                break;
            case InterruptQaServer.NoResponse:
                NoResponse++;
                break;
        }
    }

    public void Add(InterruptQaRecord record)
    {
        if (record != null)
            Add(record.Judge, record.Server, record.RejectReason);
    }

    /// <summary>판 파일 <c>claim</c> 줄 값(enum 이름)으로 더한다. 못 읽는 값은 기본값(대기·없음).</summary>
    public void Add(string judge, string server, string reject) =>
        Add(Parse(judge, InterruptQaJudge.Pending), Parse(server, InterruptQaServer.Pending), Parse(reject, InterruptClaimRejectReason.None));

    /// <summary>거부 사유별을 <c>out_of_range=1 angle=0 …</c> 한 토큰열로(로그용).</summary>
    public string RejectSummary()
    {
        var parts = new string[RejectTokens.Length];
        for (int i = 0; i < RejectTokens.Length; i++)
            parts[i] = RejectTokens[i] + "=" + RejectByReason[i].ToString(CultureInfo.InvariantCulture);
        return string.Join(" ", parts);
    }

    private static T Parse<T>(string s, T fallback) where T : struct =>
        !string.IsNullOrEmpty(s) && Enum.TryParse(s, out T v) ? v : fallback;
}
