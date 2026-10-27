using System.Text;
using BeaverLobby.Player.Dash;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 대쉬 간파 예측 판정 로그(할 일 116 — 로그만, 판정·타이밍·네트워크 불변). 간파 스킬 인스턴스 하나가 하나씩 든다.
/// <list type="bullet">
/// <item>시전(대쉬 간파 성립) 1줄 <c>[DashPredict] event=cast</c> — 서버(호스트 포함)와 원격 오너가 각자 자기 관점으로 남긴다.</item>
/// <item>서버 실제 판정 1줄 <c>event=judge</c> — 예측과의 차이·어긋남 이유. 판정 전에 끝나면 <c>result=aborted</c>.</item>
/// <item>원격 오너 대시 끝 1줄 <c>event=handoff</c>(오너가 본 대시 끝 위치·시각), 판정 전 강제 종료면 <c>event=abort</c>.</item>
/// </list>
/// 계산은 순수 <see cref="DashInterruptPredictor"/>. 꺼져 있으면(<see cref="Enabled"/> 기본 false) 아무것도 하지 않는다 — QA 가 켠다.
/// 🔴 Debug.Log 를 그대로 쓴다(InterruptLag 와 같음). 키 목록 = Docs/tech/dash-interrupt.md §13.
/// 오너 줄과 서버 줄은 새 RPC 없이 <c>clientId</c> + <c>serverT</c>(각 피어의 NetworkManager.ServerTime)로 묶는다.
/// </summary>
public sealed class DashInterruptPredictLog
{
    /// <summary>로그 켜짐 — QA 봇·배치가 켠다. 꺼져 있으면 시전 계산(오버랩 등)도 하지 않는다.</summary>
    public static bool Enabled { get; set; }

    /// <summary>찍은 줄(태그 포함)을 그대로 넘긴다 — QA 가 판 파일로 모은다.</summary>
    public static event System.Action<string> LineEmitted;

    private static int s_seq;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Enabled = false;
        LineEmitted = null;
        s_seq = 0;
    }

    private bool active;
    private bool serverSide;
    private string castId;
    private ulong clientId;
    private bool isHost;
    private string skillName;
    private DashInterruptOrder order;
    private DashInterruptPredictInput input;
    private DashInterruptPrediction prediction;
    private double castServerT;
    private Unit target;
    private Vector3 targetPosAtCast;
    private float targetYawAtCast;
    private bool hasHandOff;
    private float handOffTime;
    private Vector3 handOffPosition;

    public bool IsActive => active;
    public DashInterruptPrediction Prediction => prediction;

    /// <summary>시전 순간 예측을 계산하고 cast 줄을 남긴다. 이미 진행 중이면 덮어쓴다(앞 시전은 중단으로 남긴다).</summary>
    public void BeginCast(bool server, DashInterruptOrder castOrder, Player owner, string skill,
        in DashInterruptPredictInput castInput, Unit castTarget, string prepSource, string windowSource)
    {
        if (active)
            Abort("recast");

        active = true;
        serverSide = server;
        order = castOrder;
        input = castInput;
        skillName = skill;
        clientId = owner != null ? owner.OwnerClientId : 0UL;
        isHost = clientId == NetworkManager.ServerClientId;
        castId = (server ? "s" : "o") + clientId + "-" + (++s_seq);
        castServerT = ServerNow();
        target = castTarget;
        targetPosAtCast = castTarget != null ? castTarget.transform.position : Vector3.zero;
        targetYawAtCast = castTarget != null ? castTarget.transform.eulerAngles.y : 0f;
        hasHandOff = false;
        prediction = DashInterruptPredictor.Predict(input);

        StringBuilder sb = Begin("cast");
        DashInterruptPredictor.Append(sb, "skill", skillName);
        DashInterruptPredictor.Append(sb, "order", DashInterruptPredictor.Format(order));
        DashInterruptPredictor.Append(sb, "castPos", input.CastPosition);
        DashInterruptPredictor.Append(sb, "dashDir", input.DashDirection);
        DashInterruptPredictor.Append(sb, "dashSpeed", input.DashSpeed, "0.00");
        DashInterruptPredictor.Append(sb, "dashRemainS", input.DashRemaining);
        DashInterruptPredictor.Append(sb, "skillAgeS", input.CastTime - input.SkillStartTime);
        DashInterruptPredictor.Append(sb, "prepS", input.PrepDelay);
        DashInterruptPredictor.Append(sb, "prepSrc", prepSource);
        DashInterruptPredictor.Append(sb, "predEndPos", prediction.PredictedEndPosition);
        DashInterruptPredictor.Append(sb, "predDashEndInS", prediction.PredictedDashEndTime - input.CastTime);
        DashInterruptPredictor.Append(sb, "predResolveInS", prediction.TimeToResolve(input.CastTime));
        DashInterruptPredictor.Append(sb, "target", castTarget != null ? castTarget.name : null);
        DashInterruptPredictor.Append(sb, "targetInRange", input.HasTargetInRange);
        DashInterruptPredictor.Append(sb, "targetOpen", input.TargetWindowOpen);
        DashInterruptPredictor.Append(sb, "winOpenForS", input.WindowOpenElapsed);
        DashInterruptPredictor.Append(sb, "winExpDurS", input.WindowExpectedDuration);
        DashInterruptPredictor.Append(sb, "winRemainS", prediction.WindowRemaining);
        DashInterruptPredictor.Append(sb, "winSrc", windowSource);
        AppendGeometry(sb, prediction.PredictedEndPosition, input.DashDirection, castTarget);
        DashInterruptPredictor.Append(sb, "predict", prediction.PredictsSuccess ? "success" : "fail");
        DashInterruptPredictor.Append(sb, "predReason", DashInterruptPredictor.Format(prediction.Reason));
        Emit(sb);
    }

    /// <summary>대시 끝 → Skill 인수인계. 서버는 기록만, 원격 오너는 handoff 줄을 남기고 끝낸다(오너는 판정하지 않는다).</summary>
    public void NoteHandOff(Vector3 position, float time)
    {
        if (!active || hasHandOff)
            return;

        hasHandOff = true;
        handOffTime = time;
        handOffPosition = position;
        if (serverSide)
            return;

        StringBuilder sb = Begin("handoff");
        DashInterruptPredictor.Append(sb, "skill", skillName);
        DashInterruptPredictor.Append(sb, "order", DashInterruptPredictor.Format(order));
        DashInterruptPredictor.Append(sb, "dashEndInS", time - input.CastTime);
        DashInterruptPredictor.Append(sb, "predDashEndInS", prediction.PredictedDashEndTime - input.CastTime);
        DashInterruptPredictor.Append(sb, "pos", position);
        DashInterruptPredictor.Append(sb, "predEndPos", prediction.PredictedEndPosition);
        DashInterruptPredictor.Append(sb, "posErrM", DashInterruptPredictor.PlanarDistance(prediction.PredictedEndPosition, position));
        DashInterruptPredictor.Append(sb, "shortfallM", DashInterruptPredictor.DashShortfall(
            input.CastPosition, prediction.PredictedEndPosition, position, input.DashDirection));
        Emit(sb);
        active = false;
    }

    /// <summary>[서버] 실제 판정 — judge 줄. <paramref name="actualTarget"/> = 판정 범위 안 간파 대상(없으면 null).</summary>
    public void Resolve(Vector3 resolvePosition, float resolveTime, bool success, Unit actualTarget, bool targetOpenAtResolve)
    {
        if (!active || !serverSide)
        {
            active = false;
            return;
        }

        DashInterruptActualResult result = success
            ? DashInterruptActualResult.Success
            : actualTarget != null ? DashInterruptActualResult.Fail : DashInterruptActualResult.NoTarget;
        WriteJudge(result, resolvePosition, resolveTime, actualTarget, targetOpenAtResolve, null);
    }

    /// <summary>판정 전에 끝났다 — 서버는 judge result=aborted, 원격 오너는 abort 줄.</summary>
    public void Abort(string reason)
    {
        if (!active)
            return;

        if (serverSide)
        {
            WriteJudge(DashInterruptActualResult.Aborted, Vector3.zero, Time.time, null, false, reason);
            return;
        }

        StringBuilder sb = Begin("abort");
        DashInterruptPredictor.Append(sb, "skill", skillName);
        DashInterruptPredictor.Append(sb, "order", DashInterruptPredictor.Format(order));
        DashInterruptPredictor.Append(sb, "reason", reason);
        DashInterruptPredictor.Append(sb, "afterCastS", Time.time - input.CastTime);
        Emit(sb);
        active = false;
    }

    private void WriteJudge(DashInterruptActualResult result, Vector3 resolvePosition, float resolveTime,
        Unit actualTarget, bool targetOpenAtResolve, string abortReason)
    {
        active = false;
        bool aborted = result == DashInterruptActualResult.Aborted;
        Unit moved = target != null ? target : actualTarget;
        float targetMove = 0f, targetYaw = 0f;
        if (target != null)
        {
            targetMove = DashInterruptPredictor.PlanarDistance(targetPosAtCast, target.transform.position);
            targetYaw = Mathf.Abs(Mathf.DeltaAngle(targetYawAtCast, target.transform.eulerAngles.y));
        }

        float shortfall = aborted
            ? 0f
            : DashInterruptPredictor.DashShortfall(input.CastPosition, prediction.PredictedEndPosition, resolvePosition, input.DashDirection);
        var actual = new DashInterruptActualObservation
        {
            Result = result,
            ResolveTime = resolveTime,
            ResolvePosition = resolvePosition,
            TargetWindowOpenAtResolve = targetOpenAtResolve,
            TargetMoveDistance = targetMove,
            TargetYawDelta = targetYaw,
            IsRemoteOwner = !isHost,
        };
        DashInterruptMismatchReason mismatch = DashInterruptPredictor.ClassifyMismatch(prediction, actual, shortfall);
        bool actualSuccess = DashInterruptPredictor.IsActualSuccess(result);

        StringBuilder sb = Begin("judge");
        DashInterruptPredictor.Append(sb, "skill", skillName);
        DashInterruptPredictor.Append(sb, "order", DashInterruptPredictor.Format(order));
        DashInterruptPredictor.Append(sb, "predict", prediction.PredictsSuccess ? "success" : "fail");
        DashInterruptPredictor.Append(sb, "predReason", DashInterruptPredictor.Format(prediction.Reason));
        DashInterruptPredictor.Append(sb, "result", DashInterruptPredictor.Format(result));
        DashInterruptPredictor.Append(sb, "match", prediction.PredictsSuccess == actualSuccess);
        DashInterruptPredictor.Append(sb, "mismatch", DashInterruptPredictor.Format(mismatch));
        if (aborted)
            DashInterruptPredictor.Append(sb, "abortReason", abortReason);
        DashInterruptPredictor.Append(sb, "resolveInS", resolveTime - input.CastTime);
        DashInterruptPredictor.Append(sb, "predResolveInS", prediction.TimeToResolve(input.CastTime));
        DashInterruptPredictor.Append(sb, "timeErrS", resolveTime - prediction.PredictedResolveTime);
        DashInterruptPredictor.Append(sb, "dashEndInS", hasHandOff ? handOffTime - input.CastTime : -1f);
        DashInterruptPredictor.Append(sb, "predDashEndInS", prediction.PredictedDashEndTime - input.CastTime);
        if (!aborted)
        {
            DashInterruptPredictor.Append(sb, "resolvePos", resolvePosition);
            DashInterruptPredictor.Append(sb, "predEndPos", prediction.PredictedEndPosition);
            DashInterruptPredictor.Append(sb, "posErrM", DashInterruptPredictor.PlanarDistance(prediction.PredictedEndPosition, resolvePosition));
            DashInterruptPredictor.Append(sb, "shortfallM", shortfall);
        }
        DashInterruptPredictor.Append(sb, "target", actualTarget != null ? actualTarget.name : moved != null ? moved.name : null);
        DashInterruptPredictor.Append(sb, "targetInRange", actualTarget != null);
        DashInterruptPredictor.Append(sb, "targetOpenAtResolve", targetOpenAtResolve);
        DashInterruptPredictor.Append(sb, "winCloseAfterCastS", WindowCloseAfterCast(moved));
        DashInterruptPredictor.Append(sb, "targetMoveM", targetMove);
        DashInterruptPredictor.Append(sb, "targetYawDeg", targetYaw, "0.0");
        if (!aborted)
            AppendGeometry(sb, resolvePosition, input.DashDirection, moved);
        DashInterruptPredictor.Append(sb, "rttMs", isHost ? 0d : InterruptLagMonitor.RttMs(clientId), "0");
        Emit(sb);
    }

    // 시전 때 열려 있던 창이 시전 뒤 처음 닫힌 시각(시전 기준 초). 서버 기록에만 있다. 모름 = -1.
    private float WindowCloseAfterCast(Unit unit)
    {
        InterruptWindowHistory history = unit != null ? InterruptLagMonitor.HistoryOf(unit.gameObject) : null;
        if (history == null || castServerT < 0d || !history.TryGetFirstCloseAfter(castServerT, out double closeAt))
            return -1f;
        return (float)(closeAt - castServerT);
    }

    private static void AppendGeometry(StringBuilder sb, Vector3 from, Vector3 direction, Unit unit)
    {
        if (unit == null)
        {
            DashInterruptPredictor.Append(sb, "distM", -1f, "0.00");
            DashInterruptPredictor.Append(sb, "angleDeg", -1f, "0");
            return;
        }

        Vector3 to = unit.transform.position;
        DashInterruptPredictor.Append(sb, "distM", DashInterruptPredictor.PlanarDistance(from, to), "0.00");
        DashInterruptPredictor.Append(sb, "angleDeg", DashInterruptPredictor.PlanarAngle(direction, from, to), "0");
    }

    private StringBuilder Begin(string evt)
    {
        var sb = new StringBuilder(512);
        sb.Append(DashInterruptPredictor.Tag).Append(" event=").Append(evt);
        DashInterruptPredictor.Append(sb, "side", serverSide ? "server" : "owner");
        DashInterruptPredictor.Append(sb, "castId", castId);
        DashInterruptPredictor.Append(sb, "clientId", (int)clientId);
        DashInterruptPredictor.Append(sb, "host", isHost);
        DashInterruptPredictor.Append(sb, "serverT", ServerNow());
        DashInterruptPredictor.Append(sb, "castServerT", castServerT);
        return sb;
    }

    private static void Emit(StringBuilder sb)
    {
        string line = sb.ToString();
        Debug.Log(line);
        LineEmitted?.Invoke(line);
    }

    private static double ServerNow()
    {
        NetworkManager nm = NetworkManager.Singleton;
        return nm != null && nm.IsListening ? nm.ServerTime.Time : -1d;
    }

    // ── 창 읽기(서버 = 판정값, 클라 = 복제 표시값) ──

    /// <summary>그 대상의 지금 간파 창. 서버는 판정값, 클라는 복제값(23호·허수아비만 — 그 밖은 false).</summary>
    public static bool IsWindowOpen(Unit unit, IInterruptSlowMotionTarget slowTarget, bool server)
    {
        if (unit == null)
            return false;
        if (server)
            return slowTarget != null && slowTarget.ServerIsInterruptible;
        if (unit is TwentyThreeBoss boss)
            return boss.CounterWindowOpen;
        return unit.TryGetComponent(out TrainingDummyInterrupt dummy) && dummy.IsInterruptible;
    }

    /// <summary>[서버] 창 기록으로 열린 뒤 경과·추정 길이(초). 모르면 -1. 출처 = history / none.</summary>
    public static string EstimateWindow(Unit unit, bool server, bool openNow, out float openElapsed, out float expectedDuration)
    {
        openElapsed = -1f;
        expectedDuration = -1f;
        InterruptWindowHistory history = server && unit != null ? InterruptLagMonitor.HistoryOf(unit.gameObject) : null;
        if (history == null || !InterruptLagMonitor.TryServerNow(out double now))
            return "none";

        if (openNow && history.TryGetOpenSince(now, out double since))
            openElapsed = (float)(now - since);
        if (history.TryGetTypicalOpenDuration(out double typical))
            expectedDuration = (float)typical;
        return openElapsed >= 0f && expectedDuration >= 0f ? "history" : "partial";
    }
}
