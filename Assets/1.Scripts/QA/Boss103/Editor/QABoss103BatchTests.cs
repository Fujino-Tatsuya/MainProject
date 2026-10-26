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
}
#endif
