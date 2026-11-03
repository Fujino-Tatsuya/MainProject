using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using BeaverLobby.Player.Dash;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 할 일 191 5단계 — 간파 QA 기록(D12) 한 줄의 내용(예측·오너 판정·서버 결과·어긋난 이유 조합), 기록기 파일 형식,
/// 서버 시도 슬로우 강·약의 새 경로(<see cref="InterruptManager.TryPredictServerAttempt"/> → <see cref="PlayerInterruptSkillBase.SelectServerAttemptForecast"/>).
/// 파일 테스트는 임시 폴더에 쓰고 끝나면 지운다.
/// </summary>
public sealed class InterruptQaRecordTests
{
    const ulong Actor = 11;
    const ulong Other = 12;
    const ulong Target = 7;
    const uint Window = 3;

    InterruptQaTracker tracker;
    InterruptQaRecord last;
    int completed;
    string tempRoot;

    [SetUp]
    public void SetUp()
    {
        tracker = new InterruptQaTracker { Role = "host", LocalClientId = 0 };
        last = null;
        completed = 0;
        tracker.Completed += r =>
        {
            last = r;
            completed++;
        };
        tempRoot = Path.Combine(Path.GetTempPath(), "InterruptQaRecordTests_" + Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, true);
    }

    static InterruptJudgeResult Result(InterruptJudgeOutcome outcome, InterruptJudgeFailReason reason = InterruptJudgeFailReason.None,
        double judgeTime = 10.4) =>
        new InterruptJudgeResult
        {
            Outcome = outcome,
            FailReason = reason,
            TargetId = outcome == InterruptJudgeOutcome.NoTarget ? 0 : Target,
            WindowId = outcome == InterruptJudgeOutcome.NoTarget ? 0 : Window,
            JudgeTime = judgeTime,
            ResolvePosition = new Vector3(1.5f, 0f, -2f),
        };

    static InterruptJudgeResult Strong => Result(InterruptJudgeOutcome.Success);
    static InterruptJudgeResult Weak(InterruptJudgeFailReason reason = InterruptJudgeFailReason.WindowClosed) => Result(InterruptJudgeOutcome.Failure, reason);
    static InterruptJudgeResult None => Result(InterruptJudgeOutcome.NoTarget, InterruptJudgeFailReason.OutOfRange);

    void Press(in InterruptJudgeResult prediction, ulong actor = Actor, bool viaDash = true, double at = 10.0) =>
        tracker.Predicted(actor, 1, "Gunner", viaDash, at, prediction, at, "12:00:00.000");

    void Approve(params ulong[] actors) =>
        tracker.Approved(new InterruptClaimApproval(Target, Window, actors[0], actors[actors.Length - 1], actors));

    // ─── 한 줄 내용: 예측 × 판정 × 서버 ─────────────────────────────────

    [Test]
    public void 강_예측_판정_성공_첫_승인은_일치이고_최종_성공()
    {
        Press(Strong);
        tracker.Judged(Actor, Strong, 10.4);
        Assert.AreEqual(0, completed, "판정 성공은 서버 결과까지 열려 있다");
        Assert.AreEqual(1, tracker.OpenCount);

        Approve(Actor);
        Assert.AreEqual(1, completed);
        Assert.AreEqual(1, last.Seq);
        Assert.AreEqual(InterruptAttemptForecast.Strong, last.Forecast);
        Assert.AreEqual(InterruptQaJudge.Success, last.Judge);
        Assert.AreEqual(InterruptQaServer.ApprovedFirst, last.Server);
        Assert.AreEqual(1, last.ApprovedCount);
        Assert.IsTrue(last.FinalSuccess);
        Assert.AreEqual(true, last.PredictJudgeMatch);
        Assert.AreEqual(InterruptQaMismatch.None, last.Mismatch);
        Assert.AreEqual(Window, last.WindowId);
        Assert.AreEqual(Target, last.TargetId);
        Assert.AreEqual(0, tracker.OpenCount);
    }

    [Test]
    public void 같은_창_두_번째_승인은_동시_승인()
    {
        Press(Strong, Other);
        Press(Strong, Actor);
        tracker.Judged(Other, Strong, 10.4);
        tracker.Judged(Actor, Strong, 10.4);
        Approve(Other, Actor); // Other 가 첫 승인

        Assert.AreEqual(2, completed);
        Assert.AreEqual(Actor, last.ActorId);
        Assert.AreEqual(InterruptQaServer.ApprovedSimultaneous, last.Server);
        Assert.AreEqual(2, last.ApprovedCount);
        Assert.IsTrue(last.FinalSuccess);
        Assert.AreEqual(InterruptQaMismatch.None, last.Mismatch);
    }

    [Test]
    public void 약_예측_판정_실패는_신고_안_함이고_일치()
    {
        Press(Weak());
        tracker.Judged(Actor, Weak(), 10.4);

        Assert.AreEqual(1, completed);
        Assert.AreEqual(InterruptQaJudge.Failure, last.Judge);
        Assert.AreEqual(InterruptQaServer.NotClaimed, last.Server);
        Assert.IsFalse(last.FinalSuccess);
        Assert.AreEqual(InterruptQaMismatch.None, last.Mismatch);
    }

    [TestCase(InterruptJudgeFailReason.OutOfRange, InterruptQaMismatch.Range)]
    [TestCase(InterruptJudgeFailReason.Angle, InterruptQaMismatch.Angle)]
    [TestCase(InterruptJudgeFailReason.WindowClosed, InterruptQaMismatch.WindowClosed)]
    [TestCase(InterruptJudgeFailReason.NoWindow, InterruptQaMismatch.WindowClosed)]
    [TestCase(InterruptJudgeFailReason.CrowdControlled, InterruptQaMismatch.CrowdControl)]
    [TestCase(InterruptJudgeFailReason.CasterDown, InterruptQaMismatch.CasterCut)]
    [TestCase(InterruptJudgeFailReason.TargetBlocked, InterruptQaMismatch.TargetState)]
    [TestCase(InterruptJudgeFailReason.NotDesignated, InterruptQaMismatch.NotDesignated)]
    public void 강_예측_판정_실패는_판정_이유로_어긋난다(InterruptJudgeFailReason reason, InterruptQaMismatch expected)
    {
        Press(Strong);
        tracker.Judged(Actor, Weak(reason), 10.4);
        Assert.AreEqual(false, last.PredictJudgeMatch);
        Assert.AreEqual(InterruptQaServer.NotClaimed, last.Server);
        Assert.AreEqual(expected, last.Mismatch);
    }

    [Test]
    public void 강_예측_판정_대상_없음은_범위()
    {
        Press(Strong);
        tracker.Judged(Actor, None, 10.4);
        Assert.AreEqual(InterruptQaJudge.NoTarget, last.Judge);
        Assert.AreEqual(InterruptQaMismatch.Range, last.Mismatch);
    }

    [TestCase(InterruptJudgeFailReason.WindowClosed, InterruptQaMismatch.WindowChanged)]
    [TestCase(InterruptJudgeFailReason.NoWindow, InterruptQaMismatch.WindowChanged)]
    [TestCase(InterruptJudgeFailReason.Angle, InterruptQaMismatch.BossMoved)]
    [TestCase(InterruptJudgeFailReason.TargetBlocked, InterruptQaMismatch.TargetState)]
    public void 약_예측_판정_성공은_예측이_막힌_이유가_바뀐_것(InterruptJudgeFailReason predictReason, InterruptQaMismatch expected)
    {
        Press(Weak(predictReason));
        tracker.Judged(Actor, Strong, 10.4);
        Approve(Actor);
        Assert.IsTrue(last.FinalSuccess);
        Assert.AreEqual(expected, last.Mismatch);
    }

    [Test]
    public void 대상_없음_예측_판정_성공은_보스_이동()
    {
        Press(None);
        tracker.Judged(Actor, Strong, 10.4);
        Approve(Actor);
        Assert.AreEqual(InterruptQaMismatch.BossMoved, last.Mismatch);
    }

    [TestCase(InterruptClaimRejectReason.FirstComeLost, InterruptQaMismatch.FirstCome)]
    [TestCase(InterruptClaimRejectReason.OutOfRange, InterruptQaMismatch.ServerRejected)]
    [TestCase(InterruptClaimRejectReason.WindowClosed, InterruptQaMismatch.ServerRejected)]
    public void 판정_성공_뒤_거부는_서버_이유로_어긋난다(InterruptClaimRejectReason reason, InterruptQaMismatch expected)
    {
        Press(Strong);
        tracker.Judged(Actor, Strong, 10.4);
        tracker.Rejected(new InterruptClaimRejection(Actor, Target, Window, reason, InterruptJudgeFailReason.OutOfRange));

        Assert.AreEqual(InterruptQaServer.Rejected, last.Server);
        Assert.AreEqual(reason, last.RejectReason);
        Assert.AreEqual(InterruptJudgeFailReason.OutOfRange, last.ServerJudgeReason);
        Assert.IsFalse(last.FinalSuccess);
        Assert.AreEqual(true, last.PredictJudgeMatch, "예측·판정은 일치 — 어긋난 단계는 서버");
        Assert.AreEqual(expected, last.Mismatch);
    }

    [Test]
    public void 다른_창_거부나_남의_승인은_줄을_닫지_않는다()
    {
        Press(Strong);
        tracker.Judged(Actor, Strong, 10.4);
        tracker.Rejected(new InterruptClaimRejection(Actor, Target, Window + 1, InterruptClaimRejectReason.WindowClosed, InterruptJudgeFailReason.None));
        tracker.Approved(new InterruptClaimApproval(Target, Window, Other, Other, new[] { Other }));
        Assert.AreEqual(0, completed);
        Assert.AreEqual(1, tracker.OpenCount);
    }

    [Test]
    public void 서버_응답이_없으면_제한_시간_뒤_응답_없음()
    {
        Press(Strong);
        tracker.Judged(Actor, Strong, 10.4);
        tracker.Tick(10.4 + InterruptQaTracker.DefaultServerTimeout - 0.01);
        Assert.AreEqual(0, completed);

        tracker.Tick(10.4 + InterruptQaTracker.DefaultServerTimeout);
        Assert.AreEqual(InterruptQaServer.NoResponse, last.Server);
        Assert.AreEqual(InterruptQaMismatch.NoResponse, last.Mismatch);
    }

    // ─── 판정 전 종료 ──────────────────────────────────────────────────

    [Test]
    public void 넉백_끌기_잡기로_끊기면_CC_끊김_그밖은_시전자_끊김()
    {
        Press(Strong);
        tracker.Abandoned(Actor, InterruptQaJudge.Cut, nameof(SkillEndReason.Cancelled));
        Assert.AreEqual(InterruptQaJudge.Cut, last.Judge);
        Assert.AreEqual(InterruptQaServer.NotClaimed, last.Server);
        Assert.AreEqual(InterruptQaMismatch.CrowdControl, last.Mismatch);

        Press(Strong, at: 11.0);
        tracker.Abandoned(Actor, InterruptQaJudge.Cut, nameof(SkillEndReason.CasterDied));
        Assert.AreEqual(InterruptQaMismatch.CasterCut, last.Mismatch);
    }

    [Test]
    public void 약_예측이_판정_전에_끊기면_어긋남_아님()
    {
        Press(Weak());
        tracker.Abandoned(Actor, InterruptQaJudge.Cut, nameof(SkillEndReason.Cancelled));
        Assert.AreEqual(InterruptQaMismatch.None, last.Mismatch, "약 예측 ↔ 최종 실패는 같다");
    }

    [Test]
    public void 시전_거부_교체_시간_초과_세션_끝은_각각_닫힌다()
    {
        Press(Strong);
        tracker.Abandoned(Actor, InterruptQaJudge.CastRejected, "서버 시전 거부");
        Assert.AreEqual(InterruptQaMismatch.CastRejected, last.Mismatch);

        Press(Strong, at: 11.0);
        Press(Weak(), at: 11.5); // 판정 없이 다음 시전
        Assert.AreEqual(InterruptQaJudge.Dropped, last.Judge);
        Assert.AreEqual("교체", last.JudgeDetail);
        Assert.AreEqual(InterruptQaMismatch.Dropped, last.Mismatch);

        tracker.Tick(11.5 + InterruptQaTracker.PendingJudgeTimeout + 0.01);
        Assert.AreEqual("시간 초과", last.JudgeDetail);

        Press(Strong, at: 20.0);
        Press(Strong, Other, at: 20.0);
        tracker.Judged(Other, Strong, 20.4);
        tracker.Flush();
        Assert.AreEqual(0, tracker.OpenCount);
        Assert.AreEqual(5, completed);
        Assert.AreEqual(5, tracker.CompletedCount);
    }

    [Test]
    public void 판정_없는_시전자의_판정은_무시한다()
    {
        tracker.Judged(Actor, Strong, 10.4);
        tracker.Abandoned(Actor, InterruptQaJudge.Cut, "x");
        Assert.AreEqual(0, completed);
        Assert.AreEqual(0, tracker.OpenCount);
    }

    // ─── 서버 시도 슬로우 칸 ───────────────────────────────────────────

    [Test]
    public void 서버_시도_슬로우는_오너_예측_전후_어느_쪽이든_같은_줄에_붙는다()
    {
        // 호스트 오너: 서버 요청 처리(승인)가 오너 예측보다 먼저
        tracker.ServerAttemptPredicted(Actor, InterruptAttemptForecast.Weak, 10.0);
        Press(Strong, at: 10.0);
        tracker.Judged(Actor, Weak(), 10.4);
        Assert.AreEqual(InterruptAttemptForecast.Weak, last.ServerAttempt);

        // 오너 예측이 먼저
        Press(Strong, at: 11.0);
        tracker.ServerAttemptPredicted(Actor, InterruptAttemptForecast.Strong, 11.0);
        tracker.Judged(Actor, Weak(), 11.4);
        Assert.AreEqual(InterruptAttemptForecast.Strong, last.ServerAttempt);
    }

    [Test]
    public void 오래된_서버_시도_슬로우는_붙지_않는다()
    {
        tracker.ServerAttemptPredicted(Actor, InterruptAttemptForecast.Strong, 10.0);
        Press(Strong, at: 10.0 + InterruptQaTracker.ServerAttemptPairWindow + 0.1);
        tracker.Judged(Actor, Weak(), 11.0);
        Assert.IsNull(last.ServerAttempt);
    }

    [Test]
    public void 서버_시도_슬로우_강약은_매니저_예측_3상태이고_예측_못_하면_슬로우_없음()
    {
        Assert.AreEqual(InterruptAttemptForecast.Strong, PlayerInterruptSkillBase.SelectServerAttemptForecast(true, Strong));
        Assert.AreEqual(InterruptAttemptForecast.Weak, PlayerInterruptSkillBase.SelectServerAttemptForecast(true, Weak()));
        Assert.AreEqual(InterruptAttemptForecast.NoTarget, PlayerInterruptSkillBase.SelectServerAttemptForecast(true, None));
        Assert.AreEqual(InterruptAttemptForecast.NoTarget, PlayerInterruptSkillBase.SelectServerAttemptForecast(false, Strong),
            "매니저·이력이 없어 예측 못 함 = 대상 없음(슬로우 없음)");
    }

    [Test]
    public void 서버_시도_슬로우는_매니저_Predict_한_경로만_쓴다()
    {
        string skill = File.ReadAllText(Path.Combine(Application.dataPath, "1.Scripts/Player/Skill/PlayerInterruptSkillBase.cs"));
        string manager = File.ReadAllText(Path.Combine(Application.dataPath, "1.Scripts/Player/InterruptJudge/InterruptManager.cs"));

        Match slow = Regex.Match(skill, @"void TryStartSlowMotion\(\)\s*\{(?<body>.*?)\n    \}", RegexOptions.Singleline);
        Assert.IsTrue(slow.Success, "TryStartSlowMotion 을 찾지 못했다");
        StringAssert.Contains("TryPredictServerAttempt", slow.Groups["body"].Value);
        StringAssert.Contains("SelectServerAttemptForecast", slow.Groups["body"].Value);

        Match predict = Regex.Match(manager, @"public bool TryPredictServerAttempt\((?<body>.*?)\n    \}", RegexOptions.Singleline);
        Assert.IsTrue(predict.Success, "TryPredictServerAttempt 를 찾지 못했다");
        StringAssert.Contains("Predict(cast)", predict.Groups["body"].Value);
        StringAssert.Contains("ServerAttemptPredicted?.Invoke", predict.Groups["body"].Value);

        // 옛 예측기는 어디에도 없다
        foreach (string removed in new[] { "DashInterruptPredictor", "TryPredictOwnerAttempt", "PredictOwnerInterrupt", "TickServerDashReprediction" })
        {
            StringAssert.DoesNotContain(removed + "(", skill);
            StringAssert.DoesNotContain(removed + ".", skill);
        }
    }

    // ─── CSV·요약·기록기 ───────────────────────────────────────────────

    [Test]
    public void CSV_헤더와_행은_열_수가_같고_예측_판정_서버_이유가_칸에_들어간다()
    {
        Press(Weak(InterruptJudgeFailReason.WindowClosed));
        tracker.Judged(Actor, Strong, 10.4);
        tracker.Rejected(new InterruptClaimRejection(Actor, Target, Window, InterruptClaimRejectReason.FirstComeLost, InterruptJudgeFailReason.None));

        string[] header = InterruptQaCsv.Header.Split(',');
        string[] cells = InterruptQaCsv.FormatRow(last).Split(',');
        string Cell(string name) => cells[Array.IndexOf(header, name)];

        Assert.AreEqual(InterruptQaCsv.ColumnCount, header.Length);
        Assert.AreEqual(header.Length, cells.Length);
        CollectionAssert.AllItemsAreUnique(header);
        Assert.AreEqual("1", Cell("seq"));
        Assert.AreEqual("host", Cell("role"));
        Assert.AreEqual("Gunner", Cell("character"));
        Assert.AreEqual("dash", Cell("mode"));
        Assert.AreEqual("11", Cell("caster_id"));
        Assert.AreEqual("10", Cell("press_time"));
        Assert.AreEqual("10.4", Cell("judge_time"));
        Assert.AreEqual("0.4", Cell("judge_delay"));
        Assert.AreEqual("3", Cell("window_id"));
        Assert.AreEqual("Weak", Cell("prediction"));
        Assert.AreEqual("WindowClosed", Cell("prediction_reason"));
        Assert.AreEqual("none", Cell("dash_blocked"));
        Assert.AreEqual("Success", Cell("judge"));
        Assert.AreEqual("", Cell("judge_reason"));
        Assert.AreEqual("Rejected", Cell("server"));
        Assert.AreEqual("FirstComeLost", Cell("server_reason"));
        Assert.AreEqual("false", Cell("predict_judge_match"));
        Assert.AreEqual("false", Cell("final_success"));
        Assert.AreEqual("WindowChanged", Cell("mismatch_reason"), "처음 어긋난 단계(예측 ↔ 판정)의 이유");
        Assert.AreEqual("1.5", Cell("resolve_x"));
    }

    [Test]
    public void 캐릭터_이름은_프리팹_이름에서_뗀다()
    {
        Assert.AreEqual("Paladin", InterruptQaRules.CharacterOf("Player_Paladin(Clone)"));
        Assert.AreEqual("Gunner", InterruptQaRules.CharacterOf("Gunner"));
        Assert.AreEqual("-", InterruptQaRules.CharacterOf(null));
    }

    [Test]
    public void 요약은_줄_수와_예측_판정_일치와_이유별_개수()
    {
        var summary = new InterruptQaSummary();
        tracker.Completed += summary.Add;

        Press(Strong);
        tracker.Judged(Actor, Strong, 10.4);
        Approve(Actor);
        Press(Strong, at: 11.0);
        tracker.Judged(Actor, Weak(InterruptJudgeFailReason.OutOfRange), 11.4);
        Press(Strong, at: 12.0);
        tracker.Abandoned(Actor, InterruptQaJudge.Cut, nameof(SkillEndReason.Cancelled));

        Assert.AreEqual(3, summary.Count);
        Assert.AreEqual(2, summary.PredictJudgeTotal, "판정 전 끊김은 일치율 분모 밖");
        Assert.AreEqual(1, summary.PredictJudgeMatched);
        Assert.AreEqual(1, summary.MismatchCount(InterruptQaMismatch.Range));
        Assert.AreEqual(1, summary.MismatchCount(InterruptQaMismatch.CrowdControl));
        Assert.AreEqual(1, summary.ServerCount(InterruptQaServer.ApprovedFirst));

        string text = summary.Build("s");
        StringAssert.Contains("간파 수: 3", text);
        StringAssert.Contains("예측(강) ↔ 오너 판정(성공) 일치: 1/2", text);
        StringAssert.Contains("범위(Range): 1", text);
        StringAssert.Contains("CC 끊김(CrowdControl): 1", text);
    }

    [Test]
    public void 기록기는_세션_폴더에_BOM_헤더_한_줄과_줄마다_덧붙이고_끝에_요약을_쓴다()
    {
        var recorder = new InterruptQaRecorder(_ => (1UL, "Gunner"), () => 10.0);
        var startedAt = new DateTime(2026, 11, 2, 9, 5, 7);
        int raised = 0;
        Action<InterruptQaRecord> count = _ => raised++;
        InterruptQaRecorder.RecordCompleted += count;
        try
        {
            recorder.Begin(true, 0, true, tempRoot, startedAt);
            Assert.AreEqual(Path.Combine(tempRoot, "191interrupt-20261102_090507-host0"), recorder.SessionFolder);
            Assert.IsFalse(Directory.Exists(recorder.SessionFolder), "첫 줄 전에는 폴더를 만들지 않는다");

            recorder.Tracker.Predicted(Actor, 1, "Gunner", true, 10.0, Weak(), 10.0);
            recorder.Tracker.Judged(Actor, Weak(), 10.4);
            recorder.Tracker.Predicted(Actor, 1, "Gunner", true, 11.0, Strong, 11.0);
            recorder.End(); // 판정 대기 줄 = 세션 끝 버림

            Assert.AreEqual(2, raised);
            Assert.IsFalse(recorder.IsActive);

            byte[] bytes = File.ReadAllBytes(recorder.RecordsPath);
            Assert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }, new[] { bytes[0], bytes[1], bytes[2] });
            int boms = 0;
            for (int i = 0; i + 2 < bytes.Length; i++)
                if (bytes[i] == 0xEF && bytes[i + 1] == 0xBB && bytes[i + 2] == 0xBF)
                    boms++;
            Assert.AreEqual(1, boms);

            string[] lines = File.ReadAllLines(recorder.RecordsPath, Encoding.UTF8);
            Assert.AreEqual(3, lines.Length);
            Assert.AreEqual(InterruptQaCsv.Header, lines[0]);
            StringAssert.Contains("세션 끝", lines[2]);

            string summary = File.ReadAllText(recorder.SummaryPath, Encoding.UTF8);
            StringAssert.Contains("간파 수: 2", summary);
            StringAssert.Contains("191interrupt-20261102_090507-host0", summary);
        }
        finally
        {
            InterruptQaRecorder.RecordCompleted -= count;
            recorder.End();
        }
    }

    [Test]
    public void 파일_기록을_끄면_이벤트만_내고_폴더를_만들지_않는다()
    {
        var recorder = new InterruptQaRecorder(_ => (2UL, "Paladin"), () => 10.0);
        InterruptQaRecord seen = null;
        Action<InterruptQaRecord> capture = r => seen = r;
        InterruptQaRecorder.RecordCompleted += capture;
        try
        {
            recorder.Begin(false, 2, false, tempRoot, new DateTime(2026, 11, 2, 9, 0, 0));
            StringAssert.EndsWith("-client2", recorder.SessionFolder);
            recorder.Tracker.Predicted(Actor, 2, "Paladin", false, 10.0, Weak(), 10.0);
            recorder.Tracker.Judged(Actor, Weak(), 10.4);
            recorder.End();

            Assert.IsNotNull(seen);
            Assert.AreEqual("client", seen.Role);
            Assert.AreEqual(2UL, seen.LocalClientId);
            Assert.IsFalse(Directory.Exists(tempRoot));
            Assert.IsNull(recorder.WriteSummary());
        }
        finally
        {
            InterruptQaRecorder.RecordCompleted -= capture;
            recorder.End();
        }
    }
}
