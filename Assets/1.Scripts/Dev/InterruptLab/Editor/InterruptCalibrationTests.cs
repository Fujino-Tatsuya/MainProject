using System;
using System.IO;
using System.Text;
using BeaverLobby.Player.Dash;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 간파 예측 보정 씬(할 일 139) 단계 1 — 표본 CSV 직렬화·세션 기록·일치율 집계의 순수 규칙.
/// 파일 테스트는 임시 폴더에 쓰고 끝나면 지운다.
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

    static InterruptCalibrationSample Sample(InterruptAttemptForecast? owner, InterruptAttemptForecast? server, bool? serverSucceeded,
        DashInterruptPredictReason? reason = null) =>
        new InterruptCalibrationSample
        {
            OwnerForecast = owner,
            OwnerReason = reason,
            ServerForecast = server,
            ServerSucceeded = serverSucceeded,
        };

    static string[] Cells(string row) => row.Split(',');

    // ─── CSV ───────────────────────────────────────────────────────────

    [Test]
    public void 헤더는_사양_열을_모두_담는다()
    {
        string[] header = Cells(InterruptCalibrationCsv.Header);
        Assert.AreEqual(InterruptCalibrationCsv.ColumnCount, header.Length);
        CollectionAssert.AllItemsAreUnique(header);
        Assert.AreEqual("seq", header[0]);
        Assert.AreEqual("user_answer", header[header.Length - 1]);
        foreach (string column in new[]
                 {
                     "real_time", "window_mode", "window_open_len", "window_closed_len", "press_age",
                     "cast_time", "cast_pos_x", "dash_dir_x", "dash_speed", "dash_remaining", "skill_start_time", "prep_delay",
                     "has_target_in_range", "target_window_open", "window_open_elapsed", "window_expected_duration",
                     "resolve_pos_x", "target_distance", "target_angle", "predicted_resolve_time", "predicted_window_remaining",
                     "owner_forecast", "owner_reason", "server_forecast", "server_succeeded",
                     "actual_window_open", "actual_open_elapsed", "actual_remaining",
                 })
            CollectionAssert.Contains(header, column);
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
            var sample = new InterruptCalibrationSample { RealTime = 1.5f, PressAge = 0.125f, UserAnswer = true, WindowMode = CalibrationWindowMode.Periodic };
            string[] header = Cells(InterruptCalibrationCsv.Header);
            string[] cells = Cells(InterruptCalibrationCsv.FormatRow(sample));
            Assert.AreEqual(header.Length, cells.Length);
            Assert.AreEqual("1.5", cells[Array.IndexOf(header, "real_time")]);
            Assert.AreEqual("0.125", cells[Array.IndexOf(header, "press_age")]);
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
    public void 오너_예측_입력과_결과가_칸에_들어간다()
    {
        var input = new DashInterruptPredictInput
        {
            CastTime = 10f,
            CastPosition = new Vector3(1f, 0f, 2f),
            DashDirection = Vector3.forward,
            DashSpeed = 8f,
            DashRemaining = 0.25f,
            SkillStartTime = 10f,
            PrepDelay = 0.5f,
            HasTargetInRange = true,
            TargetWindowOpen = true,
            WindowOpenElapsed = 0.25f,
            WindowExpectedDuration = 0.5f,
        };
        DashInterruptPrediction prediction = DashInterruptPredictor.Predict(input);
        var sample = new InterruptCalibrationSample();
        sample.SetOwnerPrediction(input, prediction);

        string[] header = Cells(InterruptCalibrationCsv.Header);
        string[] cells = Cells(InterruptCalibrationCsv.FormatRow(sample));
        string Cell(string name) => cells[Array.IndexOf(header, name)];

        Assert.AreEqual("10", Cell("cast_time"));
        Assert.AreEqual("2", Cell("cast_pos_z"));
        Assert.AreEqual("8", Cell("dash_speed"));
        Assert.AreEqual("0.5", Cell("prep_delay"));
        Assert.AreEqual("true", Cell("has_target_in_range"));
        Assert.AreEqual("0.5", Cell("window_expected_duration"));
        Assert.AreEqual("4", Cell("resolve_pos_z")); // 2 + 8 × 0.25
        Assert.AreEqual("10.5", Cell("predicted_resolve_time")); // max(10.25, 10 + 0.5)
        Assert.AreEqual("0.25", Cell("predicted_window_remaining"));
        Assert.AreEqual("Weak", Cell("owner_forecast")); // 남은 창 0.25 < 판정까지 0.5
        Assert.AreEqual(nameof(DashInterruptPredictReason.WindowClosesBeforeResolve), Cell("owner_reason"));
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
        recorder.Commit(Sample(InterruptAttemptForecast.Strong, InterruptAttemptForecast.Strong, true), true);

        byte[] bytes = File.ReadAllBytes(recorder.SamplesPath);
        Assert.GreaterOrEqual(bytes.Length, 3);
        Assert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }, new[] { bytes[0], bytes[1], bytes[2] });

        string[] lines = File.ReadAllLines(recorder.SamplesPath, Encoding.UTF8);
        Assert.AreEqual(2, lines.Length);
        Assert.AreEqual(InterruptCalibrationCsv.Header, lines[0]);

        recorder.Commit(Sample(InterruptAttemptForecast.Weak, InterruptAttemptForecast.Weak, false), false);
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
        var skipped = Sample(InterruptAttemptForecast.Strong, InterruptAttemptForecast.Strong, true); // Esc — Commit 안 함
        var answered = Sample(InterruptAttemptForecast.Strong, InterruptAttemptForecast.Strong, true);
        recorder.Commit(answered, true);

        Assert.AreEqual(1, recorder.Count);
        Assert.AreEqual(1, answered.Seq);
        Assert.AreEqual(0, skipped.Seq);
        Assert.IsNull(skipped.UserAnswer);
        Assert.AreEqual(1, recorder.Stats.SampleCount);
    }

    [Test]
    public void summary_파일은_표본_수와_일치율을_쓴다()
    {
        var recorder = new InterruptCalibrationRecorder(tempRoot, new DateTime(2026, 10, 29, 9, 0, 0));
        recorder.Commit(Sample(InterruptAttemptForecast.Weak, InterruptAttemptForecast.Weak, true, DashInterruptPredictReason.WindowClosesBeforeResolve), true);
        string path = recorder.WriteSummary();

        Assert.AreEqual(recorder.SummaryPath, path);
        string text = File.ReadAllText(path, Encoding.UTF8);
        StringAssert.Contains("표본 수: 1", text);
        StringAssert.Contains("오너 예측 ↔ 답: 0/1 (0.0%)", text);
        StringAssert.Contains("서버 결과 ↔ 답: 1/1 (100.0%)", text);
        StringAssert.Contains("오너 약·답 true 1", text);
        StringAssert.Contains("WindowClosesBeforeResolve: 1", text);
    }

    // ─── 일치율 ────────────────────────────────────────────────────────

    [Test]
    public void 대상_없음과_값_없음은_분모에서_뺀다()
    {
        var stats = new InterruptCalibrationStats();
        stats.Add(new InterruptCalibrationSample { OwnerForecast = InterruptAttemptForecast.NoTarget, ServerForecast = InterruptAttemptForecast.Strong, UserAnswer = true });
        stats.Add(new InterruptCalibrationSample { OwnerForecast = InterruptAttemptForecast.Strong, UserAnswer = true }); // 서버 값 없음
        stats.Add(new InterruptCalibrationSample { OwnerForecast = InterruptAttemptForecast.Weak, ServerForecast = InterruptAttemptForecast.NoTarget, ServerSucceeded = false, UserAnswer = false });

        Assert.AreEqual(3, stats.SampleCount);

        Assert.AreEqual(2, stats.OwnerVsAnswer.Total);
        Assert.AreEqual(1, stats.OwnerVsAnswer.Excluded);
        Assert.AreEqual(1f, stats.OwnerVsAnswer.Rate.Value, 1e-6f);

        Assert.AreEqual(1, stats.ServerForecastVsAnswer.Total);
        Assert.AreEqual(2, stats.ServerForecastVsAnswer.Excluded);

        Assert.AreEqual(1, stats.ServerResultVsAnswer.Total);
        Assert.AreEqual(1, stats.ServerResultVsAnswer.Matched);

        Assert.AreEqual(0, stats.OwnerVsServerForecast.Total); // 짝이 다 비었다
        Assert.AreEqual(3, stats.OwnerVsServerForecast.Excluded);
        Assert.IsNull(stats.OwnerVsServerForecast.Rate);
        Assert.AreEqual("0/0 (-)", stats.OwnerVsServerForecast.FormatRate());
    }

    [Test]
    public void 불일치는_유형별로_센다()
    {
        var stats = new InterruptCalibrationStats();
        // 증상: 오너 약인데 답 true (2번), 오너 강인데 답 false (1번), 일치 1번
        stats.Add(new InterruptCalibrationSample { OwnerForecast = InterruptAttemptForecast.Weak, OwnerReason = DashInterruptPredictReason.WindowClosesBeforeResolve, ServerForecast = InterruptAttemptForecast.Strong, ServerSucceeded = true, UserAnswer = true });
        stats.Add(new InterruptCalibrationSample { OwnerForecast = InterruptAttemptForecast.Weak, OwnerReason = DashInterruptPredictReason.WindowClosedNow, ServerForecast = InterruptAttemptForecast.Weak, ServerSucceeded = true, UserAnswer = true });
        stats.Add(new InterruptCalibrationSample { OwnerForecast = InterruptAttemptForecast.Strong, OwnerReason = DashInterruptPredictReason.Success, ServerForecast = InterruptAttemptForecast.Strong, ServerSucceeded = false, UserAnswer = false });
        stats.Add(new InterruptCalibrationSample { OwnerForecast = InterruptAttemptForecast.Strong, OwnerReason = DashInterruptPredictReason.Success, ServerForecast = InterruptAttemptForecast.Strong, ServerSucceeded = true, UserAnswer = true });

        Assert.AreEqual(1, stats.OwnerVsAnswer.Matched);
        Assert.AreEqual(1, stats.OwnerVsAnswer.LeftTrueRightFalse);
        Assert.AreEqual(2, stats.OwnerVsAnswer.LeftFalseRightTrue);
        Assert.AreEqual(0.25f, stats.OwnerVsAnswer.Rate.Value, 1e-6f);

        Assert.AreEqual(1, stats.ServerForecastVsAnswer.LeftTrueRightFalse);
        Assert.AreEqual(1, stats.ServerForecastVsAnswer.LeftFalseRightTrue);

        Assert.AreEqual(4, stats.ServerResultVsAnswer.Matched);

        Assert.AreEqual(1, stats.OwnerVsServerForecast.LeftFalseRightTrue); // 오너 약·서버 강
        Assert.AreEqual(0, stats.OwnerVsServerForecast.LeftTrueRightFalse);

        Assert.AreEqual(1, stats.OwnerMismatchReasons[DashInterruptPredictReason.WindowClosesBeforeResolve]);
        Assert.AreEqual(1, stats.OwnerMismatchReasons[DashInterruptPredictReason.WindowClosedNow]);
        Assert.AreEqual(1, stats.OwnerMismatchReasons[DashInterruptPredictReason.Success]);

        string summary = stats.BuildSummary(null);
        StringAssert.Contains("오너 예측 ↔ 답: 1/4 (25.0%)", summary);
        StringAssert.Contains("오너 강·답 false 1 / 오너 약·답 true 2", summary);
        StringAssert.Contains("오너 ↔ 서버 예측: 오너 강·서버 약 0 / 오너 약·서버 강 1", summary);
    }
}
