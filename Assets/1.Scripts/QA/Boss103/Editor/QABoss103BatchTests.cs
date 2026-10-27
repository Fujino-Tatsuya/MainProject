#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;

/// <summary>할 일 103 C6 연속 루프 순수 규칙 — 남은 판·재실행·단계/시드·오류 판정·상한·쉬는 시간·데이터 출처·CSV 읽기.</summary>
public sealed class QABoss103BatchTests
{
    /// <summary>가짜 runs.csv — (판, 재실행, 결과, 출처, 오류 사유).</summary>
    internal static string RunsCsv(params (int run, int retry, string outcome, string ds, string reason)[] rows)
    {
        var sb = new StringBuilder("\uFEFF").Append(QABoss103Csv.Line(QABoss103Metrics.RunsHeader)).Append("\r\n");
        foreach (var r in rows)
        {
            var d = new Dictionary<string, string>
            {
                ["run"] = r.run.ToString(), ["retry"] = r.retry.ToString(), ["tier"] = QABoss103Batch.TierOf(r.run).ToString(),
                ["seed"] = "103", ["outcome"] = r.outcome, ["data_source"] = r.ds, ["error_reason"] = r.reason,
            };
            sb.Append(QABoss103Csv.Line(Row(QABoss103Metrics.RunsHeader, d))).Append("\r\n");
        }
        return sb.ToString();
    }

    internal static string[] Row(string[] header, Dictionary<string, string> values)
    {
        var row = new string[header.Length];
        for (int i = 0; i < header.Length; i++)
            row[i] = values.TryGetValue(header[i], out string v) ? v : "";
        return row;
    }

    private static List<QABoss103Attempt> Attempts(params (int, int, string, string, string)[] rows) =>
        QABoss103Batch.ReadAttempts(QABoss103CsvTable.Parse(RunsCsv(rows)));

    [Test]
    public void 단계는_판1부터_보통_숙련_초보_번갈아_시드는_판마다_결정적()
    {
        Assert.AreEqual(QABoss103BotTier.Normal, QABoss103Batch.TierOf(1));
        Assert.AreEqual(QABoss103BotTier.Skilled, QABoss103Batch.TierOf(2));
        Assert.AreEqual(QABoss103BotTier.Beginner, QABoss103Batch.TierOf(3));
        Assert.AreEqual(QABoss103BotTier.Normal, QABoss103Batch.TierOf(88));
        Assert.AreEqual(QABoss103BotTier.Beginner, QABoss103Batch.TierOf(90));
        Assert.AreEqual(QABoss103Plan.RunSeed(103, 7), QABoss103Batch.RunSeedOf(103, 7));
        Assert.AreEqual(QABoss103Batch.RunSeedOf(103, 7), QABoss103Batch.RunSeedOf(103, 7));
        Assert.AreNotEqual(QABoss103Batch.RunSeedOf(103, 7), QABoss103Batch.RunSeedOf(103, 8));
    }

    [Test]
    public void 빈_배치는_판1부터_이어하기는_기록없는_판부터()
    {
        Assert.IsTrue(QABoss103Batch.TryNext(new List<QABoss103Attempt>(), 90, out int run, out int retry));
        Assert.AreEqual((1, 0), (run, retry));

        var a = Attempts((1, 0, "Clear", "table", ""), (2, 0, "Wipe", "table", ""), (4, 0, "Timeout", "table", ""));
        Assert.IsTrue(QABoss103Batch.TryNext(a, 90, out run, out retry));
        Assert.AreEqual((3, 0), (run, retry), "중간 빈 판(3)부터");
    }

    [Test]
    public void 오류판은_전부_돈_뒤_끝에서_1회만_재실행()
    {
        var a = Attempts((1, 0, "Clear", "table", ""), (2, 0, "Error", "na", "heartbeat"), (3, 0, "Aborted", "table", "집결 판정 안 됨"));
        Assert.IsTrue(QABoss103Batch.TryNext(a, 3, out int run, out int retry));
        Assert.AreEqual((2, 1), (run, retry));
        CollectionAssert.AreEqual(new[] { 2, 3 }, QABoss103Batch.RetryList(a, 3));

        // 90판이 다 안 끝났으면 재실행보다 첫 실행이 먼저.
        Assert.IsTrue(QABoss103Batch.TryNext(a, 4, out run, out retry));
        Assert.AreEqual((4, 0), (run, retry));

        var b = Attempts((1, 0, "Clear", "table", ""), (2, 0, "Error", "na", "heartbeat"), (3, 0, "Aborted", "table", "x"),
                         (2, 1, "Error", "na", "start_timeout"), (3, 1, "Wipe", "table", ""));
        Assert.IsFalse(QABoss103Batch.TryNext(b, 3, out _, out _), "재실행도 오류면 오류 확정 — 더 돌리지 않는다");
        QABoss103Batch.Summarize(b, 3, out int done, out int retried, out int errors, out int valid);
        Assert.AreEqual(3, done);
        Assert.AreEqual(2, retried);
        Assert.AreEqual(1, errors, "판 2 = 재실행도 오류");
        Assert.AreEqual(2, valid, "판 1 + 판 3 재실행");
        Assert.IsTrue(QABoss103Batch.IsValidFinal(b, 3, 1));
        Assert.IsFalse(QABoss103Batch.IsValidFinal(b, 3, 0), "첫 실행(Aborted)은 통계 제외");
    }

    [Test]
    public void 오류_판정_결과와_데이터_출처()
    {
        var a = Attempts((1, 0, "Clear", "table", ""), (2, 0, "Clear", "inspector", ""), (3, 0, "Wipe", "host=table;client-mppm1=inspector", ""),
                         (4, 0, "Timeout", "table", ""), (5, 0, "Aborted", "table", "맵 진입 실패"), (6, 0, "Error", "table", "data_source host=table_unapplied"),
                         (7, 0, "None", "table", ""));
        bool[] want = { false, true, true, false, true, true, true };
        for (int i = 0; i < want.Length; i++)
            Assert.AreEqual(want[i], a[i].IsError, $"판 {a[i].Run}");
        Assert.AreEqual("data_source", QABoss103Batch.ReasonOf(a[1]));
        Assert.AreEqual("aborted", QABoss103Batch.ReasonOf(a[4]));
        Assert.AreEqual("data_source", QABoss103Batch.ReasonOf(a[5]));
        Assert.AreEqual("", QABoss103Batch.ReasonOf(a[0]));
    }

    [Test]
    public void 같은_칸에_루프_Error_뒤_호스트_정상_행이_오면_정상_행이_대표()
    {
        var a = Attempts((1, 0, "Error", "na", "heartbeat"), (1, 0, "Clear", "table", ""));
        Assert.AreEqual("Clear", QABoss103Batch.Effective(a, 1, 0).OutcomeText);
        Assert.IsFalse(QABoss103Batch.TryNext(a, 1, out _, out _));
    }

    [Test]
    public void 진행_감시_상한과_심장박동()
    {
        double nan = double.NaN;
        Assert.IsNull(QABoss103Batch.Judge(new QABoss103Watch { SincePlay = 30, SinceHostBeat = 3, SinceOldestCloneBeat = 4, SinceCombat = nan }));
        Assert.AreEqual("start_timeout", QABoss103Batch.Judge(new QABoss103Watch { SincePlay = 481, SinceHostBeat = 2, SinceOldestCloneBeat = nan, SinceCombat = nan }));
        Assert.IsNull(QABoss103Batch.Judge(new QABoss103Watch { SincePlay = 2000, SinceHostBeat = 2, SinceOldestCloneBeat = nan, SinceCombat = 1500 }), "Combat 25분 — 판정 대기");
        Assert.IsNull(QABoss103Batch.Judge(new QABoss103Watch { SincePlay = 2400, SinceHostBeat = 2, SinceOldestCloneBeat = nan, SinceCombat = 1800 + 179 }), "30분 + 3분 안");
        Assert.AreEqual("end_timeout", QABoss103Batch.Judge(new QABoss103Watch { SincePlay = 2400, SinceHostBeat = 2, SinceOldestCloneBeat = nan, SinceCombat = 1800 + 181 }));
        Assert.AreEqual("heartbeat", QABoss103Batch.Judge(new QABoss103Watch { SincePlay = 200, SinceHostBeat = 121, SinceOldestCloneBeat = nan, SinceCombat = 50 }));
        Assert.AreEqual("heartbeat", QABoss103Batch.Judge(new QABoss103Watch { SincePlay = 130, SinceHostBeat = nan, SinceOldestCloneBeat = nan, SinceCombat = nan }), "박동이 한 번도 없으면 Play 진입부터");
        Assert.IsNull(QABoss103Batch.Judge(new QABoss103Watch { SincePlay = 100, SinceHostBeat = nan, SinceOldestCloneBeat = nan, SinceCombat = nan }));
        Assert.AreEqual("heartbeat_client", QABoss103Batch.Judge(new QABoss103Watch { SincePlay = 300, SinceHostBeat = 1, SinceOldestCloneBeat = 125, SinceCombat = 10 }));
    }

    [Test]
    public void 쉬는_시간_오류10초_연속3판마다60초_정상3초()
    {
        Assert.AreEqual(3, QABoss103Batch.CooldownSeconds(false, 0));
        Assert.AreEqual(10, QABoss103Batch.CooldownSeconds(true, 1));
        Assert.AreEqual(10, QABoss103Batch.CooldownSeconds(true, 2));
        Assert.AreEqual(60, QABoss103Batch.CooldownSeconds(true, 3));
        Assert.AreEqual(10, QABoss103Batch.CooldownSeconds(true, 4));
        Assert.AreEqual(60, QABoss103Batch.CooldownSeconds(true, 6));
        Assert.IsFalse(QABoss103Batch.IsStreakPause(0));
    }

    [Test]
    public void 런타임_데이터_출처는_적용_스냅샷으로_본다()
    {
        const string snap = "{\"items\":[{\"instanceId\":123,\"globalObjectId\":\"g\",\"json\":\"{}\"}]}";
        Assert.AreEqual("table", QABoss103Batch.SourceFromRuntime(false, snap));
        Assert.AreEqual("table_unapplied", QABoss103Batch.SourceFromRuntime(false, ""), "설정만 테이블 — 실제 적용 없음");
        Assert.AreEqual("table_unapplied", QABoss103Batch.SourceFromRuntime(false, "{\"items\":[]}"));
        Assert.AreEqual("inspector", QABoss103Batch.SourceFromRuntime(true, ""));

        Assert.AreEqual("table", QABoss103Batch.CombineSources("table", new[] { KV("client-mppm1", "table"), KV("client-mppm2", "na") }));
        Assert.AreEqual("host=table;client-mppm1=inspector", QABoss103Batch.CombineSources("table", new[] { KV("client-mppm1", "inspector") }));
        Assert.AreEqual("host=inspector", QABoss103Batch.CombineSources("inspector", null));
        Assert.IsFalse(QABoss103Batch.IsTable("host=inspector"));
    }

    private static KeyValuePair<string, string> KV(string k, string v) => new KeyValuePair<string, string>(k, v);

    [Test]
    public void CSV_읽기는_BOM_따옴표_칸안_쉼표_줄바꿈을_처리()
    {
        string text = "\uFEFFa,b,c\r\n1,\"x, y\",\"줄\n바꿈 \"\"인용\"\"\"\r\n\r\n2,,\r\n";
        QABoss103CsvTable t = QABoss103CsvTable.Parse(text);
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, t.Header);
        Assert.AreEqual(2, t.Rows.Count);
        Assert.AreEqual("x, y", t.Get(t.Rows[0], "b"));
        Assert.AreEqual("줄\n바꿈 \"인용\"", t.Get(t.Rows[0], "c"));
        Assert.AreEqual("", t.Get(t.Rows[1], "b"));
        Assert.IsTrue(double.IsNaN(t.Num(t.Rows[1], "c")));
        Assert.AreEqual("", t.Get(t.Rows[1], "없는열"));
        Assert.AreEqual(0, QABoss103CsvTable.Parse("").Rows.Count);

        // C4 가 쓰는 형식 그대로 왕복.
        string line = QABoss103Csv.Line(new[] { "1", "a,b", " 공백", "\"q\"" });
        QABoss103CsvTable back = QABoss103CsvTable.Parse("h1,h2,h3,h4\r\n" + line + "\r\n");
        CollectionAssert.AreEqual(new[] { "1", "a,b", " 공백", "\"q\"" }, back.Rows[0]);
    }

    [Test]
    public void 상태_파일과_심장박동_경로_설정_batch_키()
    {
        string text = QABoss103Batch.FormatKeyValues(new[] { KV("state", "running"), KV("note", "줄\n바꿈") });
        Dictionary<string, string> d = QABoss103Batch.ParseKeyValues(text);
        Assert.AreEqual("running", d["state"]);
        Assert.AreEqual("줄 바꿈", d["note"]);

        DateTime now = new DateTime(2026, 10, 26, 23, 0, 0, DateTimeKind.Utc);
        Assert.AreEqual(now, QABoss103Batch.FromTicks(QABoss103Batch.Ticks(now)));
        Assert.IsNull(QABoss103Batch.FromTicks("0"));
        Assert.AreEqual(30.0, QABoss103Batch.Seconds(now, now.AddSeconds(30)), 1e-6);
        Assert.IsTrue(double.IsNaN(QABoss103Batch.Seconds(null, now)));

        StringAssert.EndsWith("run12_r1_client-mppm3f.txt", QABoss103Batch.BeatPath("C:/QA/103-x", 12, 1, "client-mppm3f").Replace('\\', '/'));
        StringAssert.StartsWith(QABoss103Batch.BeatPrefix(12, 1), "run12_r1_client-mppm3f.txt");
        Assert.AreEqual("103-20261026_2310", QABoss103Batch.BatchFolderName(new DateTime(2026, 10, 26, 23, 10, 59)));

        var cfg = new QABoss103Config { Seed = 103, Run = 5, OutDir = "C:/UnityProject/QA/103-20261026_2310", Retry = 1, Batch = 1 };
        Assert.IsTrue(QABoss103Config.TryParse(cfg.Format(), out QABoss103Config bk));
        Assert.AreEqual(1, bk.Batch);
        Assert.AreEqual(1, bk.Retry);
        Assert.IsNull(bk.Tier, "연속 루프 = 단계 자동");
        Assert.IsTrue(QABoss103Config.TryParse(new QABoss103Config().Format(), out QABoss103Config plain));
        Assert.AreEqual(0, plain.Batch, "1판 메뉴는 batch 아님");
    }

    [Test]
    public void 끝나지_않은_배치_상태만_멈춤_대상()
    {
        foreach (string s in new[] { "prepare", "activating", "starting", "running", "ending", "stopping", "finishing" })
            Assert.IsTrue(QABoss103Batch.IsActiveState(s), s);
        foreach (string s in new[] { null, "", " ", "idle", "stopped", "done" })
            Assert.IsFalse(QABoss103Batch.IsActiveState(s), s ?? "null");
    }

    // ── C7d ──────────────────────────────────────────────────

    [Test]
    public void 접속_거절_로그_판별()
    {
        Assert.IsTrue(QABoss103Batch.IsNetConfigMismatchLog("[Netcode] NetworkConfig mismatch. The configuration between the server and client does not match"));
        Assert.IsTrue(QABoss103Batch.IsNetConfigMismatchLog("[Netcode] Incomplete connection request message given config - possible NetworkConfig mismatch."));
        Assert.IsFalse(QABoss103Batch.IsNetConfigMismatchLog("[Netcode] Client connected"));
        Assert.IsFalse(QABoss103Batch.IsNetConfigMismatchLog(null));
    }

    [Test]
    public void 정체_90초_동안_활동_0_이면_정체()
    {
        var w = new QABoss103StallWatch();
        Assert.IsFalse(w.Tick(0.0, 0));
        Assert.IsFalse(w.Tick(89.9, 0));
        Assert.IsTrue(w.Tick(90.0, 0));
    }

    [Test]
    public void 정체_활동이_늘면_다시_센다()
    {
        var w = new QABoss103StallWatch();
        w.Tick(0.0, 0);
        Assert.IsFalse(w.Tick(80.0, 1));
        Assert.IsFalse(w.Tick(169.9, 1));
        Assert.IsFalse(w.Tick(150.0, 2));
        Assert.AreEqual(0.0, w.QuietSeconds(150.0));
        Assert.IsFalse(w.Tick(239.9, 2));
        Assert.IsTrue(w.Tick(240.0, 2));
    }

    [Test]
    public void 정체_북진_압박처럼_보스_피해_없이_탑_피해_사망_부활만_있어도_정체_아님()
    {
        // C7f 오탐 — 보스 무적 중 탑 공격 · 즉사 방전 · QA 부활 · 목숨 차감 · 기믹 알림만 오간다.
        var a = new QABoss103Activity();
        var w = new QABoss103StallWatch();
        Assert.AreEqual(QABoss103Activity.None, a.LastKind);
        w.Tick(0.0, a.Count);
        string[] kinds = { "other_damage", "gimmick", "life_DeadPresentation", "lives", "life_Alive", "boss_log", "fight_log", "boss_hp" };
        double t = 0.0;
        foreach (string k in kinds)
        {
            t += 80.0;
            a.Note(k);
            Assert.IsFalse(w.Tick(t, a.Count), k);
            Assert.AreEqual(k, a.LastKind);
        }
        Assert.IsFalse(w.Tick(t + 89.9, a.Count));
        Assert.IsTrue(w.Tick(t + 90.0, a.Count));
        Assert.AreEqual("boss_hp", a.LastKind);
    }

    [Test]
    public void 조기_시간초과_목숨_0_보스_HP_5분_불변이면_true()
    {
        var e = new QABoss103EarlyTimeout();
        Assert.IsFalse(e.Tick(0.0, 0, 5000));
        Assert.IsFalse(e.Tick(299.9, 0, 5000));
        Assert.IsTrue(e.Tick(300.0, 0, 5000));
        Assert.AreEqual("early_no_progress", QABoss103EarlyTimeout.Note);
    }

    [Test]
    public void 조기_시간초과_목숨이_남거나_HP가_바뀌거나_모르면_다시_센다()
    {
        var e = new QABoss103EarlyTimeout();
        Assert.IsFalse(e.Tick(0.0, 2, 5000));
        Assert.IsFalse(e.Tick(400.0, 1, 5000));   // 목숨 남음 — 안 센다
        Assert.IsFalse(e.Tick(400.0, 0, 5000));   // 목숨 0 — 여기부터
        Assert.IsFalse(e.Tick(650.0, 0, 4990));   // 보스 HP 변화 — 다시
        Assert.AreEqual(0.0, e.FlatSeconds(650.0));
        Assert.IsFalse(e.Tick(949.9, 0, 4990));
        Assert.IsFalse(e.Tick(950.0, 0, int.MinValue));   // HP 모름 — 끊김
        Assert.IsFalse(e.Tick(951.0, -1, 4990));          // 목숨 모름 — 끊김
        Assert.IsFalse(e.Tick(952.0, 0, 4990));
        Assert.IsTrue(e.Tick(1252.0, 0, 4990));
    }

    [Test]
    public void 정체_오류_사유는_stall_재실행_대상()
    {
        string detail = $"{QABoss103Batch.ReasonStall} — Combat 중 90초 동안 보스에 준 피해·받은 피해 0";
        List<QABoss103Attempt> a = QABoss103Batch.ReadAttempts(QABoss103CsvTable.Parse(RunsCsv((1, 0, "Error", "table", detail))));
        Assert.IsTrue(a[0].IsError);
        Assert.AreEqual("stall", QABoss103Batch.ReasonOf(a[0]));
        Assert.AreEqual(new List<int> { 1 }, QABoss103Batch.RetryList(a, 1));
    }

    [Test]
    public void 재배치_qa_relocates_열()
    {
        var s = new QABoss103RunStats();
        s.Begin(0.0);
        s.QaRelocate(0);
        s.QaRelocate(0);
        s.QaRelocate(9);   // 자리 밖은 무시
        s.End(60.0);
        var meta = new QABoss103RunMeta { Outcome = QABoss103Outcome.Wipe, CombatSeconds = 60.0 };
        int col = Array.IndexOf(QABoss103Metrics.CharsHeader, "qa_relocates");
        Assert.AreEqual(Array.IndexOf(QABoss103Metrics.CharsHeader, "qa_revives") + 1, col, "qa_revives 다음 — 뒤에는 C7e 충실도 열만 붙는다(배치 중간 머리 확장)");
        Assert.AreEqual("lethal_no_roll", QABoss103Metrics.CharsHeader[QABoss103Metrics.CharsHeader.Length - 1 - QABoss103CharRules.Count], "할 일 119 끝 열(뒤에 C2 규칙 열)");
        List<string[]> rows = QABoss103CsvRows.Chars(meta, s);
        Assert.AreEqual("2", rows[0][col]);
        Assert.AreEqual("0", rows[1][col]);
    }

    [Test]
    public void 배치_중간에_붙은_열은_머리만_바꾸고_옛_행은_그대로()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"qa103_hdr_{Guid.NewGuid():N}.csv");
        try
        {
            string[] oldHeader = { "run", "a" };
            string[] newHeader = { "run", "a", "b" };
            QABoss103Csv.Append(path, oldHeader, new List<string[]> { new[] { "1", "x" } });
            QABoss103Csv.Append(path, newHeader, new List<string[]> { new[] { "2", "y", "z" } });
            QABoss103CsvTable t = QABoss103CsvTable.Parse(System.IO.File.ReadAllText(path));
            CollectionAssert.AreEqual(newHeader, t.Header);
            Assert.AreEqual(2, t.Rows.Count);
            Assert.AreEqual("", t.Get(t.Rows[0], "b"));
            Assert.AreEqual("z", t.Get(t.Rows[1], "b"));
            Assert.IsFalse(QABoss103Csv.ExtendHeader(path, new[] { "a", "run", "b" }), "순서가 다르면 건드리지 않는다");
        }
        finally
        {
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }
    }
}
#endif
