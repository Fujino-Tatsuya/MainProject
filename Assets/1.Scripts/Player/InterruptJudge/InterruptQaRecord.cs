using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BeaverLobby.Player.Dash;
using UnityEngine;

/// <summary>오너 판정 단계 결과(할 일 191 D12 QA 기록 칸). 판정 전 끝난 경우(끊김·버림·시전 거부)도 여기 둔다.</summary>
public enum InterruptQaJudge : byte
{
    /// <summary>아직 판정 전.</summary>
    Pending = 0,
    NoTarget = 1,
    Failure = 2,
    Success = 3,
    /// <summary>판정 전 간파 스킬이 정상 완료 아닌 이유로 끝났다(넉백·끌기·잡기 = Cancelled·사망·대시·안전망, D5).</summary>
    Cut = 4,
    /// <summary>판정 시각이 이력(1초) 밖이라 버렸다·다음 시전이 대기 판정을 바꿨다·세션이 끝났다.</summary>
    Dropped = 5,
    /// <summary>서버가 시전 자체를 거부했다(쿨·상태 등 — 판정 없음).</summary>
    CastRejected = 6,
}

/// <summary>서버 단계 결과(할 일 191 D12).</summary>
public enum InterruptQaServer : byte
{
    /// <summary>아직 응답 전.</summary>
    Pending = 0,
    /// <summary>판정 실패·판정 전 종료라 신고하지 않았다.</summary>
    NotClaimed = 1,
    /// <summary>창의 첫 승인(대상 효과를 준 것).</summary>
    ApprovedFirst = 2,
    /// <summary>첫 승인 뒤 동시 인정 시간 안 — 승인(성공 기록만, D8).</summary>
    ApprovedSimultaneous = 3,
    Rejected = 4,
    /// <summary>신고했는데 제한 시간 안에 승인·거부가 안 왔다.</summary>
    NoResponse = 5,
}

/// <summary>
/// 예측 → 판정 → 서버 중 처음 어긋난 단계의 이유(할 일 191 D12). 예측 강 ↔ 최종 성공(판정 성공 ∧ 승인)이 같으면 <see cref="None"/>.
/// CSV 는 이 이름, 화면·요약은 <see cref="InterruptQaRules.Label"/>(한국어).
/// </summary>
public enum InterruptQaMismatch : byte
{
    None = 0,
    /// <summary>강 예측 → 판정 실패: 판정 상자가 대상에 안 닿았다.</summary>
    Range = 1,
    /// <summary>강 예측 → 판정 실패: 대상 정면 허용각 밖.</summary>
    Angle = 2,
    /// <summary>강 예측 → 판정 실패: 판정 시각에 창이 닫혔다(조기 닫힘 포함)·창 없음.</summary>
    WindowClosed = 3,
    /// <summary>강 예측 → 판정 전 넉백·끌기·잡기로 끊김 또는 판정 때 CC 중(D5).</summary>
    CrowdControl = 4,
    /// <summary>판정 성공 → 서버 선착순 패배(D8).</summary>
    FirstCome = 5,
    /// <summary>판정 성공 → 서버 거부(범위·각도·창·그 밖, 이유 칸 참고).</summary>
    ServerRejected = 6,
    /// <summary>약·대상 없음 예측 → 판정 성공: 누른 뒤 대상 위치·정면이 바뀌었다(예측은 보스 이동을 보지 않는다).</summary>
    BossMoved = 7,
    /// <summary>약 예측(창 닫힘·창 없음) → 판정 성공: 누른 뒤 창 시간표가 바뀌었다(새 창·늦은 닫힘).</summary>
    WindowChanged = 8,
    /// <summary>대상 간파 불가 상태(취약·제압)가 예측과 판정 사이에 바뀌었다.</summary>
    TargetState = 9,
    /// <summary>판정 때 시전자가 쓰러졌다·판정 전 사망·대시·안전망으로 끝났다.</summary>
    CasterCut = 10,
    /// <summary>창에 지목자가 따로 있다(레이드 연쇄 간파).</summary>
    NotDesignated = 11,
    /// <summary>판정을 버렸다(이력 밖·교체·세션 끝).</summary>
    Dropped = 12,
    /// <summary>서버가 시전을 거부했다.</summary>
    CastRejected = 13,
    /// <summary>신고 응답 없음.</summary>
    NoResponse = 14,
    Other = 15,
}

/// <summary>
/// 간파 1회 QA 기록 한 줄(할 일 191 D12) — 예측 / 오너 판정 / 서버 결과 / 어긋난 이유 + 시전자·캐릭터·근접/대쉬·창 id·판정 시각.
/// 순수 데이터. <see cref="InterruptQaTracker"/> 가 매니저 이벤트로 채우고, CSV 는 <see cref="InterruptQaCsv"/>. 시각은 서버 시각(초).
/// </summary>
public sealed class InterruptQaRecord
{
    public int Seq;
    /// <summary>기록 피어 벽시계(로컬 HH:mm:ss.fff).</summary>
    public string WallClock;
    /// <summary>기록 피어 역할 — host / client.</summary>
    public string Role;
    public ulong LocalClientId;

    public ulong ActorId;
    public ulong CasterClientId;
    /// <summary>캐릭터 이름(프리팹 이름에서 Player_·(Clone) 을 뗀 것 — Paladin·Gunner·Assassin).</summary>
    public string Character;
    public bool ViaDash;
    /// <summary>누른 시각(서버 시각).</summary>
    public double PressTime;

    public bool HasPrediction;
    public InterruptJudgeResult Prediction;

    public InterruptQaJudge Judge;
    public InterruptJudgeResult JudgeResult;
    /// <summary>판정 전 종료 사유(끊김 = SkillEndReason 이름, 버림 = 이력 밖/교체/세션 끝).</summary>
    public string JudgeDetail;

    public InterruptQaServer Server;
    public InterruptClaimRejectReason RejectReason;
    public InterruptJudgeFailReason ServerJudgeReason;
    /// <summary>승인 배포의 그 창 승인 인원(동시 성공 포함). 승인 아니면 0.</summary>
    public int ApprovedCount;

    /// <summary>서버 시도 슬로우 강·약(같은 피어가 서버일 때만 — 호스트).</summary>
    public InterruptAttemptForecast? ServerAttempt;

    /// <summary>판정 단계 결과 후 서버 응답을 기다리는 제한 시각(서버 시각).</summary>
    public double ServerDeadline;

    public InterruptAttemptForecast? Forecast => HasPrediction ? Prediction.Forecast : (InterruptAttemptForecast?)null;
    public bool PredictedStrong => HasPrediction && Prediction.Forecast == InterruptAttemptForecast.Strong;
    public bool JudgedSuccess => Judge == InterruptQaJudge.Success;
    public bool IsApproved => Server == InterruptQaServer.ApprovedFirst || Server == InterruptQaServer.ApprovedSimultaneous;
    /// <summary>최종 성공 = 판정 성공 ∧ 서버 승인.</summary>
    public bool FinalSuccess => JudgedSuccess && IsApproved;
    /// <summary>예측(강) ↔ 오너 판정(성공)이 같은가. 예측이 없으면 null.</summary>
    public bool? PredictJudgeMatch => HasPrediction ? PredictedStrong == JudgedSuccess : (bool?)null;
    /// <summary>판정 시각(판정 전 종료면 예측 판정 시각).</summary>
    public double JudgeTime => Judge == InterruptQaJudge.Success || Judge == InterruptQaJudge.Failure || Judge == InterruptQaJudge.NoTarget
        ? JudgeResult.JudgeTime
        : HasPrediction ? Prediction.JudgeTime : PressTime;
    /// <summary>판정에 쓴 창(판정 전 종료면 예측의 창).</summary>
    public uint WindowId => JudgeResult.WindowId != 0 ? JudgeResult.WindowId : HasPrediction ? Prediction.WindowId : 0;
    public ulong TargetId => JudgeResult.TargetId != 0 ? JudgeResult.TargetId : HasPrediction ? Prediction.TargetId : 0;

    public InterruptQaMismatch Mismatch => InterruptQaRules.MismatchOf(this);
}

/// <summary>어긋난 이유 규칙·문구(할 일 191 D12) — 순수 함수.</summary>
public static class InterruptQaRules
{
    /// <summary>
    /// 처음 어긋난 단계의 이유. ① 예측 강 ↔ 판정 성공이 다르면 판정 단계 이유, ② 판정 성공인데 서버가 승인하지 않았으면 서버 단계 이유.
    /// 둘 다 같으면 None — 약 예측 → 판정 실패 → 신고 안 함, 강 예측 → 판정 성공 → 승인 등. 서버 응답 대기 중이면 None.
    /// </summary>
    public static InterruptQaMismatch MismatchOf(InterruptQaRecord r)
    {
        if (r == null || r.Judge == InterruptQaJudge.Pending)
            return InterruptQaMismatch.None;

        bool predictedStrong = r.PredictedStrong;
        if (predictedStrong != r.JudgedSuccess)
            return JudgeStageReason(r);

        if (r.JudgedSuccess && !r.IsApproved)
        {
            switch (r.Server)
            {
                case InterruptQaServer.Rejected:
                    return r.RejectReason == InterruptClaimRejectReason.FirstComeLost ? InterruptQaMismatch.FirstCome : InterruptQaMismatch.ServerRejected;
                case InterruptQaServer.NoResponse:
                    return InterruptQaMismatch.NoResponse;
            }
        }

        return InterruptQaMismatch.None;
    }

    static InterruptQaMismatch JudgeStageReason(InterruptQaRecord r)
    {
        switch (r.Judge)
        {
            case InterruptQaJudge.Cut:
                return r.JudgeDetail == nameof(SkillEndReason.Cancelled) ? InterruptQaMismatch.CrowdControl : InterruptQaMismatch.CasterCut;
            case InterruptQaJudge.Dropped:
                return InterruptQaMismatch.Dropped;
            case InterruptQaJudge.CastRejected:
                return InterruptQaMismatch.CastRejected;
            case InterruptQaJudge.Success:
                // 약·대상 없음 예측 → 판정 성공: 예측이 막힌 이유로 무엇이 바뀌었는지 본다.
                if (!r.HasPrediction || r.Prediction.Outcome == InterruptJudgeOutcome.NoTarget)
                    return InterruptQaMismatch.BossMoved;
                return FromFailReason(r.Prediction.FailReason, true);
            default:
                // 강 예측 → 판정 실패·대상 없음.
                if (r.JudgeResult.Outcome == InterruptJudgeOutcome.NoTarget)
                    return InterruptQaMismatch.Range;
                return FromFailReason(r.JudgeResult.FailReason, false);
        }
    }

    // judgeSucceeded = 예측이 이 이유로 막혔는데 판정은 성공(이유가 사라졌다). 아니면 판정이 이 이유로 실패.
    static InterruptQaMismatch FromFailReason(InterruptJudgeFailReason reason, bool judgeSucceeded)
    {
        switch (reason)
        {
            case InterruptJudgeFailReason.OutOfRange:
                return judgeSucceeded ? InterruptQaMismatch.BossMoved : InterruptQaMismatch.Range;
            case InterruptJudgeFailReason.Angle:
                return judgeSucceeded ? InterruptQaMismatch.BossMoved : InterruptQaMismatch.Angle;
            case InterruptJudgeFailReason.NoWindow:
            case InterruptJudgeFailReason.WindowClosed:
                return judgeSucceeded ? InterruptQaMismatch.WindowChanged : InterruptQaMismatch.WindowClosed;
            case InterruptJudgeFailReason.CrowdControlled:
                return InterruptQaMismatch.CrowdControl;
            case InterruptJudgeFailReason.CasterDown:
                return InterruptQaMismatch.CasterCut;
            case InterruptJudgeFailReason.TargetBlocked:
                return InterruptQaMismatch.TargetState;
            case InterruptJudgeFailReason.NotDesignated:
                return InterruptQaMismatch.NotDesignated;
            default:
                return InterruptQaMismatch.Other;
        }
    }

    public static string Label(InterruptQaMismatch m) => m switch
    {
        InterruptQaMismatch.None => "일치",
        InterruptQaMismatch.Range => "범위",
        InterruptQaMismatch.Angle => "각도",
        InterruptQaMismatch.WindowClosed => "창 닫힘",
        InterruptQaMismatch.CrowdControl => "CC 끊김",
        InterruptQaMismatch.FirstCome => "선착순",
        InterruptQaMismatch.ServerRejected => "서버 거부",
        InterruptQaMismatch.BossMoved => "보스 이동",
        InterruptQaMismatch.WindowChanged => "창 시간표 변경",
        InterruptQaMismatch.TargetState => "대상 상태 변화",
        InterruptQaMismatch.CasterCut => "시전자 끊김",
        InterruptQaMismatch.NotDesignated => "지목자 아님",
        InterruptQaMismatch.Dropped => "판정 버림",
        InterruptQaMismatch.CastRejected => "시전 거부",
        InterruptQaMismatch.NoResponse => "서버 응답 없음",
        _ => "기타",
    };

    public static string Label(InterruptAttemptForecast? f) => f switch
    {
        InterruptAttemptForecast.Strong => "강",
        InterruptAttemptForecast.Weak => "약",
        InterruptAttemptForecast.NoTarget => "대상 없음",
        _ => "-",
    };

    public static string Label(InterruptQaJudge j) => j switch
    {
        InterruptQaJudge.Success => "성공",
        InterruptQaJudge.Failure => "실패",
        InterruptQaJudge.NoTarget => "대상 없음",
        InterruptQaJudge.Cut => "끊김",
        InterruptQaJudge.Dropped => "버림",
        InterruptQaJudge.CastRejected => "시전 거부",
        _ => "대기",
    };

    public static string Label(InterruptQaServer s) => s switch
    {
        InterruptQaServer.NotClaimed => "신고 안 함",
        InterruptQaServer.ApprovedFirst => "승인",
        InterruptQaServer.ApprovedSimultaneous => "동시 승인",
        InterruptQaServer.Rejected => "거부",
        InterruptQaServer.NoResponse => "응답 없음",
        _ => "대기",
    };

    /// <summary>캐릭터 이름 — 프리팹 인스턴스 이름에서 "(Clone)"·"Player_" 를 뗀다. 비면 "-".</summary>
    public static string CharacterOf(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
            return "-";
        string name = objectName.Replace("(Clone)", string.Empty).Trim();
        if (name.StartsWith("Player_", StringComparison.Ordinal))
            name = name.Substring("Player_".Length);
        return name.Length > 0 ? name : "-";
    }

    /// <summary>한 줄 요약(콘솔·랩 질문 창).</summary>
    public static string Describe(InterruptQaRecord r)
    {
        if (r == null)
            return "-";

        var sb = new StringBuilder();
        sb.Append(r.Character).Append(' ').Append(r.ViaDash ? "대쉬" : "근접")
          .Append(" · 예측 ").Append(Label(r.Forecast));
        if (r.HasPrediction && r.Prediction.FailReason != InterruptJudgeFailReason.None)
            sb.Append('(').Append(r.Prediction.FailReason).Append(')');
        sb.Append(" · 판정 ").Append(Label(r.Judge));
        if (r.JudgeResult.FailReason != InterruptJudgeFailReason.None)
            sb.Append('(').Append(r.JudgeResult.FailReason).Append(')');
        else if (!string.IsNullOrEmpty(r.JudgeDetail))
            sb.Append('(').Append(r.JudgeDetail).Append(')');
        sb.Append(" · 서버 ").Append(Label(r.Server));
        if (r.Server == InterruptQaServer.Rejected)
            sb.Append('(').Append(r.RejectReason).Append(')');
        InterruptQaMismatch m = r.Mismatch;
        sb.Append(" · ").Append(m == InterruptQaMismatch.None ? "일치" : "어긋남: " + Label(m));
        return sb.ToString();
    }
}

/// <summary>
/// 간파 1회 = QA 기록 한 줄 짝짓기(할 일 191 D12) — 순수 C#. <see cref="InterruptQaRecorder"/> 가 매니저 이벤트를 넣는다.
/// <list type="bullet">
/// <item>예측(<see cref="Predicted"/>)이 줄을 연다. 같은 시전자의 판정 대기 줄이 이미 있으면 그 줄은 '버림(교체)'으로 닫는다.</item>
/// <item>판정(<see cref="Judged"/>): 실패·대상 없음 = 신고 안 함으로 닫는다. 성공 = 서버 응답 대기(제한 <see cref="ServerTimeout"/>).</item>
/// <item>승인(<see cref="Approved"/>) — 같은 대상·창의 승인 목록에 그 시전자가 있으면 첫/동시 승인으로 닫는다. 거부(<see cref="Rejected"/>) — 시전자·창이 같으면 닫는다.</item>
/// <item>판정 전 종료(<see cref="Abandoned"/>)는 끊김·버림·시전 거부로 닫는다. 서버 시도 슬로우(<see cref="ServerAttemptPredicted"/>, 호스트)는
///       오너 예측보다 먼저 올 수 있어 시전자별로 잠시 들고 있다가 다음 예측 줄에 붙인다.</item>
/// </list>
/// 닫힌 줄은 <see cref="Completed"/> 로 낸다(seq 는 닫힌 순서).
/// </summary>
public sealed class InterruptQaTracker
{
    public const double DefaultServerTimeout = 2.0;
    /// <summary>판정 대기 줄의 최대 수명(서버 시각) — 매니저 이력보다 길면 버림.</summary>
    public const double PendingJudgeTimeout = 3.0;
    /// <summary>서버 시도 슬로우 예측을 다음 오너 예측 줄에 붙일 수 있는 시간.</summary>
    public const double ServerAttemptPairWindow = 0.5;

    readonly List<InterruptQaRecord> _open = new List<InterruptQaRecord>();
    readonly Dictionary<ulong, (InterruptAttemptForecast Forecast, double Time)> _serverAttempts =
        new Dictionary<ulong, (InterruptAttemptForecast, double)>();
    int _seq;

    public event Action<InterruptQaRecord> Completed;

    /// <summary>판정 성공 뒤 서버 응답을 기다리는 시간(초).</summary>
    public double ServerTimeout { get; set; } = DefaultServerTimeout;

    public string Role { get; set; } = "-";
    public ulong LocalClientId { get; set; }

    /// <summary>닫힌 줄 수 = 마지막 seq.</summary>
    public int CompletedCount => _seq;

    public int OpenCount => _open.Count;

    /// <summary>[오너] 누른 순간 예측 — 줄을 연다.</summary>
    public void Predicted(ulong actorId, ulong casterClientId, string character, bool viaDash, double pressTime,
        in InterruptJudgeResult prediction, double now, string wallClock = null)
    {
        InterruptQaRecord awaiting = FindAwaitingJudge(actorId);
        if (awaiting != null)
            Close(awaiting, InterruptQaJudge.Dropped, "교체");

        var r = new InterruptQaRecord
        {
            WallClock = wallClock ?? string.Empty,
            Role = Role,
            LocalClientId = LocalClientId,
            ActorId = actorId,
            CasterClientId = casterClientId,
            Character = string.IsNullOrEmpty(character) ? "-" : character,
            ViaDash = viaDash,
            PressTime = pressTime,
            HasPrediction = true,
            Prediction = prediction,
        };

        if (_serverAttempts.TryGetValue(actorId, out var attempt))
        {
            _serverAttempts.Remove(actorId);
            if (Math.Abs(now - attempt.Time) <= ServerAttemptPairWindow)
                r.ServerAttempt = attempt.Forecast;
        }

        _open.Add(r);
    }

    /// <summary>[서버] 시도 슬로우 강·약 예측. 같은 시전자의 판정 대기 줄이 있으면 거기, 없으면 다음 예측 줄에 붙인다.</summary>
    public void ServerAttemptPredicted(ulong actorId, InterruptAttemptForecast forecast, double now)
    {
        InterruptQaRecord awaiting = FindAwaitingJudge(actorId);
        if (awaiting != null && !awaiting.ServerAttempt.HasValue)
        {
            awaiting.ServerAttempt = forecast;
            return;
        }

        _serverAttempts[actorId] = (forecast, now);
    }

    /// <summary>[오너] Hit 타이밍 판정.</summary>
    public void Judged(ulong actorId, in InterruptJudgeResult result, double now)
    {
        InterruptQaRecord r = FindAwaitingJudge(actorId);
        if (r == null)
            return;

        r.JudgeResult = result;
        if (result.Outcome == InterruptJudgeOutcome.Success)
        {
            r.Judge = InterruptQaJudge.Success;
            r.ServerDeadline = now + Math.Max(0.0, ServerTimeout);
            return;
        }

        r.Judge = result.Outcome == InterruptJudgeOutcome.NoTarget ? InterruptQaJudge.NoTarget : InterruptQaJudge.Failure;
        r.Server = InterruptQaServer.NotClaimed;
        Complete(r);
    }

    /// <summary>[오너] 판정 전 종료 — <paramref name="kind"/> = 끊김·버림·시전 거부, <paramref name="detail"/> = 사유 문구.</summary>
    public void Abandoned(ulong actorId, InterruptQaJudge kind, string detail)
    {
        InterruptQaRecord r = FindAwaitingJudge(actorId);
        if (r != null)
            Close(r, kind, detail);
    }

    /// <summary>[모든 피어] 승인 배포.</summary>
    public void Approved(in InterruptClaimApproval approval)
    {
        for (int i = 0; i < _open.Count; i++)
        {
            InterruptQaRecord r = _open[i];
            if (r.Judge != InterruptQaJudge.Success || r.JudgeResult.TargetId != approval.TargetId ||
                r.JudgeResult.WindowId != approval.WindowId || !Contains(approval.ApprovedActorIds, r.ActorId))
                continue;

            r.Server = approval.FirstActorId == r.ActorId ? InterruptQaServer.ApprovedFirst : InterruptQaServer.ApprovedSimultaneous;
            r.ApprovedCount = approval.ApprovedActorIds != null ? approval.ApprovedActorIds.Count : 0;
            Complete(r);
            i--;
        }
    }

    /// <summary>[오너] 거부 통지.</summary>
    public void Rejected(in InterruptClaimRejection rejection)
    {
        for (int i = 0; i < _open.Count; i++)
        {
            InterruptQaRecord r = _open[i];
            if (r.Judge != InterruptQaJudge.Success || r.ActorId != rejection.ActorId || r.JudgeResult.WindowId != rejection.WindowId)
                continue;

            r.Server = InterruptQaServer.Rejected;
            r.RejectReason = rejection.Reason;
            r.ServerJudgeReason = rejection.JudgeFailReason;
            Complete(r);
            return;
        }
    }

    /// <summary>제한 시간이 지난 줄을 닫는다(매 프레임 불러도 된다). 시각 = 서버 시각.</summary>
    public void Tick(double now)
    {
        for (int i = _open.Count - 1; i >= 0; i--)
        {
            InterruptQaRecord r = _open[i];
            if (r.Judge == InterruptQaJudge.Success && now >= r.ServerDeadline)
            {
                r.Server = InterruptQaServer.NoResponse;
                Complete(r);
            }
            else if (r.Judge == InterruptQaJudge.Pending && now - r.PressTime > PendingJudgeTimeout)
            {
                Close(r, InterruptQaJudge.Dropped, "시간 초과");
            }
        }
    }

    /// <summary>열린 줄을 모두 닫는다(세션 끝) — 판정 대기 = 버림, 서버 대기 = 응답 없음.</summary>
    public void Flush()
    {
        while (_open.Count > 0)
        {
            InterruptQaRecord r = _open[0];
            if (r.Judge == InterruptQaJudge.Success)
            {
                r.Server = InterruptQaServer.NoResponse;
                Complete(r);
            }
            else
            {
                Close(r, InterruptQaJudge.Dropped, "세션 끝");
            }
        }

        _serverAttempts.Clear();
    }

    InterruptQaRecord FindAwaitingJudge(ulong actorId)
    {
        for (int i = _open.Count - 1; i >= 0; i--)
            if (_open[i].ActorId == actorId && _open[i].Judge == InterruptQaJudge.Pending)
                return _open[i];
        return null;
    }

    void Close(InterruptQaRecord r, InterruptQaJudge kind, string detail)
    {
        r.Judge = kind;
        r.JudgeDetail = detail;
        r.Server = InterruptQaServer.NotClaimed;
        Complete(r);
    }

    void Complete(InterruptQaRecord r)
    {
        _open.Remove(r);
        r.Seq = ++_seq;
        Completed?.Invoke(r);
    }

    static bool Contains(IReadOnlyList<ulong> list, ulong id)
    {
        if (list == null)
            return false;
        for (int i = 0; i < list.Count; i++)
            if (list[i] == id)
                return true;
        return false;
    }
}

/// <summary>
/// QA 기록 CSV 한 줄(할 일 191 D12). 헤더와 행은 같은 <see cref="Columns"/> 표에서 나온다(보정 랩 139 CSV 와 같은 규칙 —
/// InvariantCulture, 값 없음 = 빈 칸, 쉼표·따옴표·줄바꿈 칸은 따옴표로 감싼다, 줄바꿈 CRLF).
/// </summary>
public static class InterruptQaCsv
{
    public const string NewLine = "\r\n";

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static readonly (string Name, Func<InterruptQaRecord, string> Value)[] Columns =
    {
        ("seq", r => r.Seq.ToString(Inv)),
        ("wall_clock", r => r.WallClock ?? ""),
        ("role", r => r.Role ?? ""),
        ("local_client", r => r.LocalClientId.ToString(Inv)),
        ("caster_client", r => r.CasterClientId.ToString(Inv)),
        ("caster_id", r => r.ActorId.ToString(Inv)),
        ("character", r => r.Character ?? ""),
        ("mode", r => r.ViaDash ? "dash" : "melee"),
        ("press_time", r => D(r.PressTime)),
        ("judge_time", r => D(r.JudgeTime)),
        ("judge_delay", r => D(r.JudgeTime - r.PressTime)),
        ("target_id", r => Id(r.TargetId)),
        ("window_id", r => r.WindowId != 0 ? r.WindowId.ToString(Inv) : ""),
        ("prediction", r => r.HasPrediction ? r.Prediction.Forecast.ToString() : ""),
        ("prediction_reason", r => r.HasPrediction ? Reason(r.Prediction.FailReason) : ""),
        ("prediction_window", r => r.HasPrediction && r.Prediction.WindowId != 0 ? r.Prediction.WindowId.ToString(Inv) : ""),
        ("dash_blocked", r => r.HasPrediction && r.ViaDash ? (r.Prediction.DashBlockedByWall ? "wall" : r.Prediction.DashEndedByUnit ? "unit" : "none") : ""),
        ("judge", r => r.Judge.ToString()),
        ("judge_reason", r => Reason(r.JudgeResult.FailReason)),
        ("judge_detail", r => r.JudgeDetail ?? ""),
        ("server", r => r.Server.ToString()),
        ("server_reason", r => r.Server == InterruptQaServer.Rejected ? r.RejectReason.ToString() : ""),
        ("server_judge_reason", r => r.Server == InterruptQaServer.Rejected ? Reason(r.ServerJudgeReason) : ""),
        ("approved_count", r => r.ApprovedCount > 0 ? r.ApprovedCount.ToString(Inv) : ""),
        ("server_attempt", r => r.ServerAttempt?.ToString() ?? ""),
        ("predict_judge_match", r => B(r.PredictJudgeMatch)),
        ("final_success", r => r.Judge == InterruptQaJudge.Pending ? "" : B(r.FinalSuccess)),
        ("mismatch_reason", r => r.Mismatch.ToString()),
        ("resolve_x", r => r.HasPrediction ? F(r.Prediction.ResolvePosition.x) : ""),
        ("resolve_z", r => r.HasPrediction ? F(r.Prediction.ResolvePosition.z) : ""),
    };

    public static readonly string Header = string.Join(",", Columns.Select(c => c.Name));

    public static int ColumnCount => Columns.Length;

    public static IEnumerable<string> ColumnNames => Columns.Select(c => c.Name);

    public static string FormatRow(InterruptQaRecord record) =>
        string.Join(",", Columns.Select(c => Escape(c.Value(record))));

    /// <summary>쉼표·따옴표·줄바꿈이 있으면 따옴표로 감싸고 안의 따옴표는 두 번 쓴다.</summary>
    public static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    static string Reason(InterruptJudgeFailReason reason) => reason == InterruptJudgeFailReason.None ? "" : reason.ToString();
    static string Id(ulong id) => id != 0 ? id.ToString(Inv) : "";
    static string D(double v) => double.IsNaN(v) || double.IsInfinity(v) ? "" : v.ToString("0.####", Inv);
    static string F(float v) => v.ToString("0.###", Inv);
    static string B(bool? v) => v.HasValue ? (v.Value ? "true" : "false") : "";
}

/// <summary>
/// QA 기록 집계(할 일 191 D12) — 줄 수·예측↔판정 일치·어긋난 이유별·서버 결과별 개수. summary.txt 본문.
/// PLAN §6 완료 조건(예측·판정 어긋남은 보스 이동·CC 끊김만)과 §5 리스크(거부 이유 비율 → 허용 오차 조정)를 이 비율로 본다.
/// </summary>
public sealed class InterruptQaSummary
{
    readonly Dictionary<InterruptQaMismatch, int> _mismatch = new Dictionary<InterruptQaMismatch, int>();
    readonly Dictionary<InterruptQaServer, int> _server = new Dictionary<InterruptQaServer, int>();
    readonly Dictionary<InterruptClaimRejectReason, int> _reject = new Dictionary<InterruptClaimRejectReason, int>();

    public int Count { get; private set; }
    public int PredictJudgeTotal { get; private set; }
    public int PredictJudgeMatched { get; private set; }

    public int MismatchCount(InterruptQaMismatch m) => _mismatch.TryGetValue(m, out int n) ? n : 0;
    public int ServerCount(InterruptQaServer s) => _server.TryGetValue(s, out int n) ? n : 0;

    public void Add(InterruptQaRecord r)
    {
        if (r == null)
            return;

        Count++;
        if (r.PredictJudgeMatch.HasValue && (r.JudgedSuccess || r.Judge == InterruptQaJudge.Failure || r.Judge == InterruptQaJudge.NoTarget))
        {
            PredictJudgeTotal++;
            if (r.PredictJudgeMatch.Value)
                PredictJudgeMatched++;
        }

        Increment(_mismatch, r.Mismatch);
        Increment(_server, r.Server);
        if (r.Server == InterruptQaServer.Rejected)
            Increment(_reject, r.RejectReason);
    }

    public string Build(string sessionLabel)
    {
        var sb = new StringBuilder();
        sb.Append("간파 QA 기록(191) 요약").Append(InterruptQaCsv.NewLine);
        if (!string.IsNullOrEmpty(sessionLabel))
            sb.Append("세션: ").Append(sessionLabel).Append(InterruptQaCsv.NewLine);
        sb.Append("간파 수: ").Append(Count).Append(InterruptQaCsv.NewLine);
        sb.Append("예측(강) ↔ 오너 판정(성공) 일치: ").Append(PredictJudgeMatched).Append('/').Append(PredictJudgeTotal)
          .Append(" (판정까지 간 것만)").Append(InterruptQaCsv.NewLine);
        sb.Append(InterruptQaCsv.NewLine);

        sb.Append("[어긋난 이유]").Append(InterruptQaCsv.NewLine);
        foreach (InterruptQaMismatch m in (InterruptQaMismatch[])Enum.GetValues(typeof(InterruptQaMismatch)))
            if (_mismatch.TryGetValue(m, out int n))
                sb.Append(InterruptQaRules.Label(m)).Append('(').Append(m).Append("): ").Append(n).Append(InterruptQaCsv.NewLine);
        sb.Append(InterruptQaCsv.NewLine);

        sb.Append("[서버 결과]").Append(InterruptQaCsv.NewLine);
        foreach (InterruptQaServer s in (InterruptQaServer[])Enum.GetValues(typeof(InterruptQaServer)))
            if (_server.TryGetValue(s, out int n))
                sb.Append(InterruptQaRules.Label(s)).Append('(').Append(s).Append("): ").Append(n).Append(InterruptQaCsv.NewLine);
        if (_reject.Count > 0)
        {
            sb.Append("거부 이유: ");
            sb.Append(string.Join(" · ", _reject.Select(p => $"{p.Key} {p.Value}")));
            sb.Append(InterruptQaCsv.NewLine);
        }
        return sb.ToString();
    }

    static void Increment<T>(Dictionary<T, int> map, T key)
    {
        map.TryGetValue(key, out int n);
        map[key] = n + 1;
    }
}
