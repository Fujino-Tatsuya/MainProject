using System;
using System.IO;
using System.Text;
using BeaverLobby.Player.Dash;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 간파 보정 랩(할 일 139 → 191 5단계 '예측 vs 판정 vs 서버') — 표본 CSV 직렬화·세션 기록·일치율 집계의 순수 규칙.
/// 표본 = QA 기록 한 줄(<see cref="InterruptQaRecord"/>) + 허수아비 창·대상 거리·사람 답. 파일 테스트는 임시 폴더에 쓰고 끝나면 지운다.
/// </summary>
public sealed class InterruptCalibrationTests
{
    string tempRoot;

    [SetUp]
    public void SetUp()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "InterruptCalibrationTests_" + Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, true);
    }

    static InterruptJudgeResult Result(InterruptJudgeOutcome outcome, InterruptJudgeFailReason reason = InterruptJudgeFailReason.None) =>
        new InterruptJudgeResult { Outcome = outcome, FailReason = reason, TargetId = 7, WindowId = 3, JudgeTime = 10.5, ResolvePosition = new Vector3(1f, 0f, 4f) };

    static InterruptJudgeResult ForForecast(InterruptAttemptForecast forecast, InterruptJudgeFailReason reason = InterruptJudgeFailReason.None) =>
        forecast == InterruptAttemptForecast.Strong ? Result(InterruptJudgeOutcome.Success)
        : forecast == InterruptAttemptForecast.Weak ? Result(InterruptJudgeOutcome.Failure, reason == InterruptJudgeFailReason.None ? InterruptJudgeFailReason.WindowClosed : reason)
        : Result(InterruptJudgeOutcome.NoTarget, InterruptJudgeFailReason.OutOfRange);

    // 예측 → 판정 → 서버까지 닫힌 QA 기록 한 줄.
    static InterruptQaRecord Record(InterruptAttemptForecast forecast, InterruptQaJudge judge, InterruptQaServer server,
        InterruptJudgeFailReason predictReason = InterruptJudgeFailReason.None, InterruptJudgeFailReason judgeReason = InterruptJudgeFailReason.None)
    {
        InterruptJudgeOutcome outcome = judge == InterruptQaJudge.Success ? InterruptJudgeOutcome.Success
            : judge == InterruptQaJudge.Failure ? InterruptJudgeOutcome.Failure : InterruptJudgeOutcome.NoTarget;
        return new InterruptQaRecord
        {
            ActorId = 11,
            Character = "Paladin",
            ViaDash = true,
            PressTime = 10.0,
            HasPrediction = true,
            Prediction = ForForecast(forecast, predictReason),
            Judge = judge,
            JudgeResult = Result(outcome, judgeReason),
            Server = server,
        };
    }

    static InterruptCalibrationSample Sample(InterruptQaRecord record) => new InterruptCalibrationSample { Record = record };

    static string[] Cells(string row) => row.Split(',');

    // ─── CSV ───────────────────────────────────────────────────────────

    [Test]
    public void 헤더는_랩_칸과_QA_기록_열을_모두_담는다()
    {
        string[] header = Cells(InterruptCalibrationCsv.Header);
        Assert.AreEqual(InterruptCalibrationCsv.ColumnCount, header.Length);
        CollectionAssert.AllItemsAreUnique(header);
        Assert.AreEqual("seq", header[0]);
        Assert.AreEqual("user_answer", header[header.Length - 1]);
        foreach (string column in new[]
                 {
                     "real_time", "window_mode", "window_open_len", "window_closed_len",
                     "qa_seq", "caster_id", "character", "mode", "press_time", "judge_time", "window_id",
                     "prediction", "prediction_reason", "judge", "judge_reason", "server", "server_reason", "server_attempt",
                     "predict_judge_match", "final_success", "mismatch_reason",
                     "target_distance", "target_angle", "actual_window_open", "actual_open_elapsed", "actual_remaining",
                 })
            CollectionAssert.Contains(header, column);

        // 옛 예측식 칸(139 — 대시 속도·준비 지연·예측 창 남음·오너/서버 재예측)은 없다.
        foreach (string removed in new[] { "press_age", "dash_speed", "prep_delay", "predicted_window_remaining", "owner_forecast", "server_forecast", "owner_repredicted" })
            CollectionAssert.DoesNotContain(header, removed);
    }

    [Test]
    public void 값_없는_칸은_빈_칸이고_열_수는_헤더와_같다()
    {
        string row = InterruptCalibrationCsv.FormatRow(new InterruptCalibrationSample { Seq = 3 });
        string[] cells = Cells(row);
        Assert.AreEqual(InterruptCalibrationCsv.ColumnCount, cells.Length);
        Assert.AreEqual("3", cells[0]);
        for (int i = 1; i < cells.Length; i++)
            Assert.AreEqual("", cells[i], Cells(InterruptCalibrationCsv.Header)[i]);
    }

    [Test]
    public void 숫자는_InvariantCulture_로_쓴다()
    {
        var previous = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE"); // 소수점 = 쉼표
            var sample = new InterruptCalibrationSample { RealTime = 1.5f, TargetDistance = 0.125f, UserAnswer = true, WindowMode = CalibrationWindowMode.Periodic };
            string[] header = Cells(InterruptCalibrationCsv.Header);
            string[] cells = Cells(InterruptCalibrationCsv.FormatRow(sample));
            Assert.AreEqual(header.Length, cells.Length);
            Assert.AreEqual("1.5", cells[Array.IndexOf(header, "real_time")]);
            Assert.AreEqual("0.125", cells[Array.IndexOf(header, "target_distance")]);
            Assert.AreEqual("true", cells[Array.IndexOf(header, "user_answer")]);
            Assert.AreEqual("Periodic", cells[Array.IndexOf(header, "window_mode")]);
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Test]
    public void 쉼표_따옴표_줄바꿈은_따옴표로_감싼다()
    {
        Assert.AreEqual("abc", InterruptCalibrationCsv.Escape("abc"));
        Assert.AreEqual("", InterruptCalibrationCsv.Escape(null));
        Assert.AreEqual("\"a,b\"", InterruptCalibrationCsv.Escape("a,b"));
        Assert.AreEqual("\"say \"\"hi\"\"\"", InterruptCalibrationCsv.Escape("say \"hi\""));
        Assert.AreEqual("\"a\nb\"", InterruptCalibrationCsv.Escape("a\nb"));
    }

    [Test]
    public void QA_기록_예측_판정_서버가_칸에_들어간다()
    {
        InterruptQaRecord record = Record(InterruptAttemptForecast.Weak, InterruptQaJudge.Success, InterruptQaServer.ApprovedFirst,
            predictReason: InterruptJudgeFailReason.WindowClosed);
        record.Seq = 5;
        record.ServerAttempt = InterruptAttemptForecast.Strong;
        var sample = Sample(record);
        sample.ActualWindowOpen = true;
        sample.ActualRemaining = float.PositiveInfinity;

        string[] header = Cells(InterruptCalibrationCsv.Header);
        string[] cells = Cells(InterruptCalibrationCsv.FormatRow(sample));
        string Cell(string name) => cells[Array.IndexOf(header, name)];

        Assert.AreEqual(header.Length, cells.Length);
        Assert.AreEqual("5", Cell("qa_seq"));
        Assert.AreEqual("dash", Cell("mode"));
        Assert.AreEqual("Paladin", Cell("character"));
        Assert.AreEqual("Weak", Cell("prediction"));
        Assert.AreEqual("WindowClosed", Cell("prediction_reason"));
        Assert.AreEqual("Success", Cell("judge"));
        Assert.AreEqual("ApprovedFirst", Cell("server"));
        Assert.AreEqual("Strong", Cell("server_attempt"));
        Assert.AreEqual("false", Cell("predict_judge_match"));
        Assert.AreEqual("true", Cell("final_success"));
        Assert.AreEqual(nameof(InterruptQaMismatch.WindowChanged), Cell("mismatch_reason"));
        Assert.AreEqual("true", Cell("actual_window_open"));
        Assert.AreEqual("inf", Cell("actual_remaining"));
    }

    // ─── 세션 기록 ─────────────────────────────────────────────────────

    [Test]
    public void 세션_폴더는_루트_아래_139calib_시각()
    {
        var recorder = new InterruptCalibrationRecorder(tempRoot, new DateTime(2026, 10, 29, 9, 5, 7));
        Assert.AreEqual(Path.Combine(tempRoot, "139calib-20261029_090507"), recorder.SessionFolder);
        Assert.IsFalse(Directory.Exists(recorder.SessionFolder)); // 첫 표본 전에는 만들지 않는다
        Assert.IsNull(recorder.WriteSummary());
        Assert.IsFalse(Directory.Exists(recorder.SessionFolder));
    }

    [Test]
    public void 표본마다_즉시_한_줄_덧붙이고_파일은_UTF8_BOM_헤더_1줄()
    {
        var recorder = new InterruptCalibrationRecorder(tempRoot, new DateTime(2026, 10, 29, 9, 0, 0));
        recorder.Commit(Sample(Record(InterruptAttemptForecast.Strong, InterruptQaJudge.Success, InterruptQaServer.ApprovedFirst)), true);

        byte[] bytes = File.ReadAllBytes(recorder.SamplesPath);
        Assert.GreaterOrEqual(bytes.Length, 3);
        Assert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }, new[] { bytes[0], bytes[1], bytes[2] });

        string[] lines = File.ReadAllLines(recorder.SamplesPath, Encoding.UTF8);
        Assert.AreEqual(2, lines.Length);
        Assert.AreEqual(InterruptCalibrationCsv.Header, lines[0]);

        recorder.Commit(Sample(Record(InterruptAttemptForecast.Weak, InterruptQaJudge.Failure, InterruptQaServer.NotClaimed)), false);
        lines = File.ReadAllLines(recorder.SamplesPath, Encoding.UTF8);
        Assert.AreEqual(3, lines.Length);
        Assert.AreEqual("1", Cells(lines[1])[0]);
        Assert.AreEqual("2", Cells(lines[2])[0]);
        Assert.AreEqual("false", Cells(lines[2])[InterruptCalibrationCsv.ColumnCount - 1]);

        // BOM 은 맨 앞 한 번만
        byte[] all = File.ReadAllBytes(recorder.SamplesPath);
        int boms = 0;
        for (int i = 0; i + 2 < all.Length; i++)
            if (all[i] == 0xEF && all[i + 1] == 0xBB && all[i + 2] == 0xBF)
                boms++;
        Assert.AreEqual(1, boms);
    }

    [Test]
    public void 건너뛴_표본은_seq_와_일치율에_들어가지_않는다()
    {
        var recorder = new InterruptCalibrationRecorder(tempRoot, new DateTime(2026, 10, 29, 9, 0, 0));
        var skipped = Sample(Record(InterruptAttemptForecast.Strong, InterruptQaJudge.Success, InterruptQaServer.ApprovedFirst)); // Esc — Commit 안 함
        var answered = Sample(Record(InterruptAttemptForecast.Strong, InterruptQaJudge.Success, InterruptQaServer.ApprovedFirst));
        recorder.Commit(answered, true);

        Assert.AreEqual(1, recorder.Count);
        Assert.AreEqual(1, answered.Seq);
        Assert.AreEqual(0, skipped.Seq);
        Assert.IsNull(skipped.UserAnswer);
        Assert.AreEqual(1, recorder.Stats.SampleCount);
    }

    [Test]
    public void summary_파일은_표본_수와_일치율과_어긋난_이유를_쓴다()
    {
        var recorder = new InterruptCalibrationRecorder(tempRoot, new DateTime(2026, 10, 29, 9, 0, 0));
        recorder.Commit(Sample(Record(InterruptAttemptForecast.Weak, InterruptQaJudge.Success, InterruptQaServer.ApprovedFirst,
            predictReason: InterruptJudgeFailReason.WindowClosed)), true);
        string path = recorder.WriteSummary();

        Assert.AreEqual(recorder.SummaryPath, path);
        string text = File.ReadAllText(path, Encoding.UTF8);
        StringAssert.Contains("표본 수: 1", text);
        StringAssert.Contains("예측 ↔ 답: 0/1 (0.0%)", text);
        StringAssert.Contains("오너 판정 ↔ 답: 1/1 (100.0%)", text);
        StringAssert.Contains("서버 결과 ↔ 답: 1/1 (100.0%)", text);
        StringAssert.Contains("예측 약·답 true 1", text);
        StringAssert.Contains("창 시간표 변경(WindowChanged): 1", text);
    }

    // ─── 일치율 ────────────────────────────────────────────────────────

    [Test]
    public void 대상_없음과_값_없음은_분모에서_뺀다()
    {
        var stats = new InterruptCalibrationStats();
        // 예측 대상 없음 · 판정 대상 없음 · 신고 안 함
        stats.Add(new InterruptCalibrationSample { Record = Record(InterruptAttemptForecast.NoTarget, InterruptQaJudge.NoTarget, InterruptQaServer.NotClaimed), UserAnswer = false });
        // 강 · 성공 · 서버 응답 없음(값 없음)
        stats.Add(new InterruptCalibrationSample { Record = Record(InterruptAttemptForecast.Strong, InterruptQaJudge.Success, InterruptQaServer.NoResponse), UserAnswer = true });
        // 기록 없음
        stats.Add(new InterruptCalibrationSample { UserAnswer = true });

        Assert.AreEqual(3, stats.SampleCount);

        Assert.AreEqual(1, stats.ForecastVsAnswer.Total);
        Assert.AreEqual(2, stats.ForecastVsAnswer.Excluded);
        Assert.AreEqual(1f, stats.ForecastVsAnswer.Rate.Value, 1e-6f);

        Assert.AreEqual(1, stats.JudgeVsAnswer.Total);
        Assert.AreEqual(2, stats.JudgeVsAnswer.Excluded);

        Assert.AreEqual(1, stats.ServerVsAnswer.Total); // 신고 안 함 = false, 응답 없음 = 제외
        Assert.AreEqual(1, stats.ServerVsAnswer.Matched);

        var empty = new InterruptCalibrationStats();
        Assert.IsNull(empty.ForecastVsJudge.Rate);
        Assert.AreEqual("0/0 (-)", empty.ForecastVsJudge.FormatRate());
    }

    [Test]
    public void 불일치는_유형별로_세고_어긋난_이유를_모은다()
    {
        var stats = new InterruptCalibrationStats();
        // 약(창 닫힘) → 판정 성공 → 승인 · 답 true : 예측만 틀림(창 시간표 변경)
        stats.Add(new InterruptCalibrationSample { Record = Record(InterruptAttemptForecast.Weak, InterruptQaJudge.Success, InterruptQaServer.ApprovedFirst, InterruptJudgeFailReason.WindowClosed), UserAnswer = true });
        // 강 → 판정 실패(범위) · 답 false : 예측만 틀림(범위)
        stats.Add(new InterruptCalibrationSample { Record = Record(InterruptAttemptForecast.Strong, InterruptQaJudge.Failure, InterruptQaServer.NotClaimed, judgeReason: InterruptJudgeFailReason.OutOfRange), UserAnswer = false });
        // 강 → 판정 성공 → 선착순 거부 · 답 true
        InterruptQaRecord firstCome = Record(InterruptAttemptForecast.Strong, InterruptQaJudge.Success, InterruptQaServer.Rejected);
        firstCome.RejectReason = InterruptClaimRejectReason.FirstComeLost;
        stats.Add(new InterruptCalibrationSample { Record = firstCome, UserAnswer = true });
        // 강 → 성공 → 승인 · 답 true : 전부 일치
        stats.Add(new InterruptCalibrationSample { Record = Record(InterruptAttemptForecast.Strong, InterruptQaJudge.Success, InterruptQaServer.ApprovedFirst), UserAnswer = true });

        Assert.AreEqual(2, stats.ForecastVsAnswer.Matched);
        Assert.AreEqual(1, stats.ForecastVsAnswer.LeftTrueRightFalse);
        Assert.AreEqual(1, stats.ForecastVsAnswer.LeftFalseRightTrue);

        Assert.AreEqual(4, stats.JudgeVsAnswer.Matched);

        Assert.AreEqual(3, stats.ServerVsAnswer.Matched);
        Assert.AreEqual(1, stats.ServerVsAnswer.LeftFalseRightTrue); // 서버 아님·답 true(선착순)

        Assert.AreEqual(1, stats.ForecastVsJudge.LeftFalseRightTrue); // 약·성공
        Assert.AreEqual(1, stats.ForecastVsJudge.LeftTrueRightFalse); // 강·실패

        Assert.AreEqual(1, stats.MismatchReasons[InterruptQaMismatch.WindowChanged]);
        Assert.AreEqual(1, stats.MismatchReasons[InterruptQaMismatch.Range]);
        Assert.AreEqual(1, stats.MismatchReasons[InterruptQaMismatch.FirstCome]);
        Assert.IsFalse(stats.MismatchReasons.ContainsKey(InterruptQaMismatch.None));

        string summary = stats.BuildSummary(null);
        StringAssert.Contains("예측 ↔ 답: 2/4 (50.0%)", summary);
        StringAssert.Contains("예측 ↔ 오너 판정: 예측 강·판정 실패 1 / 예측 약·판정 성공 1", summary);
        StringAssert.Contains("선착순(FirstCome): 1", summary);
    }
}
