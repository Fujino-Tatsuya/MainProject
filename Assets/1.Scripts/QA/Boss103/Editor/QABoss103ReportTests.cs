#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NUnit.Framework;

/// <summary>
/// 할 일 103 C6 리포트용 가짜 배치(12판 중 11판 진행) — EditMode 검증과 샘플 report.html(에디터에서 <see cref="Input"/> → <see cref="QABoss103Report.Build"/>) 공용.
/// 보통 1·4·7(클리어 600·900s, 전멸) · 숙련 2·5·8 + 11 재실행(전부 클리어 480·540·600·420s) · 초보 3·6·9(전멸·시간초과·클리어 1500s) ·
/// 판 10 = 루프 Error(heartbeat) · 판 11 첫 실행 = Aborted. 굴림 10 · 입력 맞기 보통 3/숙련 1/초보 5 · 실제 피격 4/2/6(굴림 없는 1) · DPS 팔라딘 10·어쌔신 20·거너 30.
/// </summary>
public static class QABoss103ReportFake
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private struct R
    {
        public int Run, Retry;
        public string Outcome;
        public double Sec;
    }

    private static readonly R[] Plan =
    {
        new R { Run = 1, Outcome = "Clear", Sec = 600 }, new R { Run = 2, Outcome = "Clear", Sec = 480 }, new R { Run = 3, Outcome = "Wipe", Sec = 700 },
        new R { Run = 4, Outcome = "Clear", Sec = 900 }, new R { Run = 5, Outcome = "Clear", Sec = 540 }, new R { Run = 6, Outcome = "Timeout", Sec = 1800 },
        new R { Run = 7, Outcome = "Wipe", Sec = 1200 }, new R { Run = 8, Outcome = "Clear", Sec = 600 }, new R { Run = 9, Outcome = "Clear", Sec = 1500 },
        new R { Run = 10, Outcome = "Error", Sec = -1 }, new R { Run = 11, Outcome = "Aborted", Sec = -1 }, new R { Run = 11, Retry = 1, Outcome = "Clear", Sec = 420 },
    };

    public static QABoss103ReportInput Input()
    {
        var runs = new StringBuilder();
        var chars = new StringBuilder();
        var patterns = new StringBuilder();
        var gimmicks = new StringBuilder();
        Head(runs, QABoss103Metrics.RunsHeader);
        Head(chars, QABoss103Metrics.CharsHeader);
        Head(patterns, QABoss103Metrics.PatternsHeader);
        Head(gimmicks, QABoss103Metrics.GimmicksHeader);
        foreach (R r in Plan)
        {
            QABoss103BotTier tier = QABoss103Batch.TierOf(r.Run);
            bool played = r.Sec > 0;
            int take = tier == QABoss103BotTier.Normal ? 3 : tier == QABoss103BotTier.Skilled ? 1 : 5;
            var key = new Dictionary<string, string>
            {
                ["run"] = I(r.Run), ["retry"] = I(r.Retry), ["tier"] = tier.ToString(), ["seed"] = "103",
            };
            var d = new Dictionary<string, string>(key)
            {
                ["run_seed"] = I(QABoss103Batch.RunSeedOf(103, r.Run)),
                ["outcome"] = r.Outcome,
                ["error_reason"] = r.Outcome == "Error" ? "heartbeat" : r.Outcome == "Aborted" ? "집결 판정 안 됨 30초" : "",
                ["combat_sec"] = played ? r.Sec.ToString("F1", Inv) : "",
                ["data_source"] = r.Outcome == "Error" ? "na" : "table",
                ["xlsx_svn_rev"] = "531", ["git_commit"] = "abc1234", ["unity_version"] = "6000.3.16f1",
                ["started_at"] = $"2026-10-26 23:{r.Run:00}:00", ["ended_at"] = $"2026-10-27 0{r.Run % 10}:00:00",
            };
            if (played)
            {
                d["threat_rolls"] = "10";
                d["rolled_take"] = I(take);
                d["actual_hits"] = I(take + 1);
                d["party_lives_used"] = r.Outcome == "Clear" ? "1" : "3";
                d["deaths_total"] = r.Outcome == "Clear" ? "1" : "4";
                d["suppress_count"] = "2";
                d["counter_tries"] = "4";
                d["counter_success"] = tier == QABoss103BotTier.Skilled ? "3" : "1";
                d["traits_paladin"] = "atk_up;hp_up";
                d["traits_assassin"] = "atk_up;speed";
                d["traits_gunner"] = tier == QABoss103BotTier.Beginner ? "crit;hp_up" : "crit";
                d["boss_hp_end_pct"] = r.Outcome == "Clear" ? "0.0" : "35.5";
            }
            Add(runs, QABoss103Metrics.RunsHeader, d);
            if (!played)
                continue;

            int[] dps = { 10, 20, 30 };
            foreach (QABoss103Character ch in new[] { QABoss103Character.Paladin, QABoss103Character.Assassin, QABoss103Character.Gunner })
            {
                int k = (int)ch;
                double dealt = dps[k] * r.Sec;
                var c = new Dictionary<string, string>(key)
                {
                    ["character"] = ch.ToString(),
                    ["damage_taken"] = I(600 * (k + 1)),
                    ["deaths"] = k == 2 ? "1" : "0",
                    ["damage_dealt"] = dealt.ToString("F0", Inv),
                    ["basic_use"] = "100", ["basic_hit"] = "50", ["basic_damage"] = (dealt * 0.6).ToString("F0", Inv),
                    ["q_use"] = "10", ["q_hit"] = "8", ["q_damage"] = (dealt * 0.4).ToString("F0", Inv),
                    ["counter_tries"] = k == 0 ? "4" : "0",
                    ["counter_success"] = k == 0 ? d["counter_success"] : "0",
                    ["dmg_during_suppress"] = "100",
                    ["hits_taken"] = I(k == 0 ? take + 1 : 0),
                    ["hits_unrolled"] = k == 0 ? "1" : "0",
                };
                if (ch == QABoss103Character.Assassin)
                {
                    c["assassin_transform_count"] = "2";
                    c["transform_sec_total"] = (r.Sec / 4).ToString("F1", Inv);
                    c["dmg_normal"] = (dealt / 2).ToString("F0", Inv);
                    c["dmg_transformed"] = (dealt / 2).ToString("F0", Inv);
                }
                if (ch == QABoss103Character.Paladin)
                {
                    c["paladin_guard_success"] = "3";
                    c["paladin_guard_counter"] = "1";
                    c["paladin_guard_ignore"] = "2";
                }
                Add(chars, QABoss103Metrics.CharsHeader, c);
            }
            Add(patterns, QABoss103Metrics.PatternsHeader, new Dictionary<string, string>(key)
                { ["pattern"] = "LeftHook", ["threats"] = "6", ["rolled_take"] = I(take - 1 < 0 ? 0 : take - 1), ["rolled_dodge"] = "4", ["actual_hits"] = I(take), ["deaths"] = "0" });
            Add(patterns, QABoss103Metrics.PatternsHeader, new Dictionary<string, string>(key)
                { ["pattern"] = "floor", ["threats"] = "4", ["rolled_take"] = "1", ["rolled_dodge"] = "3", ["actual_hits"] = "1", ["deaths"] = r.Outcome == "Wipe" ? "1" : "0" });
            Add(gimmicks, QABoss103Metrics.GimmicksHeader, new Dictionary<string, string>(key)
                { ["gimmick"] = "전력 공급", ["result"] = r.Outcome == "Wipe" ? "fail" : "success", ["fight_t"] = "120.0", ["source"] = "event" });
        }
        return new QABoss103ReportInput
        {
            BatchText = "title=x\nstarted_at=2026-10-26 23:00:00\nseed=103\ntotal=12\ndata_source_mode=table\nxlsx_svn_rev=531\ngit_commit=abc1234\ngit_branch=qa/BossBalance103\nunity_version=6000.3.16f1\n",
            RunsCsv = runs.ToString(),
            CharsCsv = chars.ToString(),
            PatternsCsv = patterns.ToString(),
            GimmicksCsv = gimmicks.ToString(),
            Fixes = "- 23:40 <b>예시</b> 수정 메모 & 확인",
            Notes = "2026-10-27 01:00:00 연속 3판 오류 → 60초 쉬고 계속",
            Total = 12,
            LoopState = "running · 판 12",
            GeneratedAt = "2026-10-27 02:00:00",
        };
    }

    private static void Head(StringBuilder sb, string[] header) => sb.Append('﻿').Append(QABoss103Csv.Line(header)).Append("\r\n");

    private static void Add(StringBuilder sb, string[] header, Dictionary<string, string> values) =>
        sb.Append(QABoss103Csv.Line(QABoss103BatchTests.Row(header, values))).Append("\r\n");

    private static string I(int v) => v.ToString(Inv);
}

/// <summary>할 일 103 C6 HTML 리포트 — 가짜 CSV 로 표·수치, 빈 데이터·오류만 있는 데이터.</summary>
public sealed class QABoss103ReportTests
{
    [Test]
    public void 단계별_요약은_오류판을_빼고_재실행_결과를_쓴다()
    {
        List<QABoss103Report.Summary> s = QABoss103Report.Summaries(QABoss103ReportFake.Input());
        Assert.AreEqual(4, s.Count, "보통·숙련·초보·전체");
        QABoss103Report.Summary normal = s[0], skilled = s[1], beginner = s[2], all = s[3];

        Assert.AreEqual(3, normal.Runs, "판 10(Error) 제외");
        Assert.AreEqual(2, normal.Clears);
        Assert.AreEqual(750.0, normal.ClearMean, 1e-6);
        Assert.AreEqual(750.0, normal.ClearMedian, 1e-6);
        Assert.AreEqual(1, normal.Wipes);

        Assert.AreEqual(4, skilled.Runs, "판 11 재실행 포함, 첫 실행(Aborted) 제외");
        Assert.AreEqual(1.0, skilled.ClearRate, 1e-9);
        Assert.AreEqual(510.0, skilled.ClearMean, 1e-6);
        Assert.AreEqual(510.0, skilled.ClearMedian, 1e-6);

        Assert.AreEqual(3, beginner.Runs);
        Assert.AreEqual(1, beginner.Timeouts);
        Assert.AreEqual(1.0 / 3.0, beginner.ClearRate, 1e-9);
        Assert.AreEqual(10, all.Runs);

        Assert.AreEqual(0.3, normal.InputHitRate, 1e-9);
        Assert.AreEqual(0.3, normal.ActualHitRate, 1e-9, "(4 − 굴림 없는 1) / 10");
        Assert.AreEqual(0.1, skilled.InputHitRate, 1e-9);
        Assert.AreEqual(0.1, skilled.ActualHitRate, 1e-9, "(실제 2 − 굴림 없는 1) / 10");
        Assert.AreEqual(1.0, normal.UnrolledPerRun, 1e-9);

        Assert.AreEqual(10.0, all.Dps[QABoss103Character.Paladin], 1e-6);
        Assert.AreEqual(20.0, all.Dps[QABoss103Character.Assassin], 1e-6);
        Assert.AreEqual(30.0, skilled.Dps[QABoss103Character.Gunner], 1e-6);
        Assert.AreEqual(0.75, skilled.CounterRate, 1e-9, "숙련 성공 3 / 시도 4");
        Assert.AreEqual(2.0, all.SuppressAvg, 1e-9);
        Assert.AreEqual(300.0, all.SuppressDamageAvg, 1e-9, "캐릭터 3명 × 100");
    }

    [Test]
    public void 특성_표는_등장판_클리어율_그_특성_캐릭터_DPS()
    {
        SortedDictionary<string, QABoss103Report.TraitCell[]> t = QABoss103Report.Traits(QABoss103ReportFake.Input());
        Assert.AreEqual(10, t["crit"][3].Runs, "통계 판 전부");
        Assert.AreEqual(30.0, t["crit"][3].Dps, 1e-6, "거너만 가짐");
        Assert.AreEqual(20.0, t["speed"][3].Dps, 1e-6);
        Assert.AreEqual(15.0, t["atk_up"][3].Dps, 1e-6, "팔라딘 10 + 어쌔신 20 — 같은 시간 가중");
        Assert.AreEqual(3, t["hp_up"][2].Runs, "초보");
        Assert.AreEqual(1.0 / 3.0, t["hp_up"][2].ClearRate, 1e-9);
        Assert.AreEqual(4, t["crit"][1].Runs, "숙련 4판");
    }

    [Test]
    public void HTML_은_외부_리소스_없는_단일_파일에_머리_표_차트_판목록()
    {
        string html = QABoss103Report.Build(QABoss103ReportFake.Input());
        StringAssert.Contains("할 일 103 3인 보스전(웰즈&amp;23호 B) 밸런싱 QA", html);
        StringAssert.Contains("11/12", html, "진행 n/total");
        StringAssert.Contains("진행(+재실행 1)", html);
        StringAssert.Contains("오류 판 1개(통계 제외) — heartbeat 1", html);
        StringAssert.Contains("배치 시작 r531", html);
        StringAssert.Contains("abc1234", html);
        StringAssert.Contains("&lt;b&gt;예시&lt;/b&gt; 수정 메모 &amp; 확인", html, "fixes.md 는 그대로(이스케이프)");
        StringAssert.Contains("연속 3판 오류", html);
        StringAssert.Contains("<svg", html);
        StringAssert.Contains("단계별 클리어율", html);
        StringAssert.Contains("클리어 시간 분포", html);
        StringAssert.Contains("캐릭터별 DPS", html);
        StringAssert.Contains("입력 vs 실제 피격률", html);
        StringAssert.Contains("특성별 등장·클리어율·DPS", html);
        StringAssert.Contains("전력 공급", html);
        StringAssert.Contains("LeftHook", html);
        StringAssert.Contains("450ms", html, "봇 프로필 표");
        for (int i = 1; i <= 14; i++)
            StringAssert.Contains($"<td colspan=\"5\">{i}. ", html, $"지표 {i}");
        Assert.IsFalse(html.Contains("http://") || html.Contains("https://") || html.Contains("<script") || html.Contains("<link"), "외부 리소스·스크립트 없음");
        StringAssert.Contains("판 목록", html);
        StringAssert.Contains("aborted — 집결 판정 안 됨 30초", html);
    }

    [Test]
    public void 빈_데이터도_리포트가_나온다()
    {
        string html = QABoss103Report.Build(new QABoss103ReportInput());
        StringAssert.Contains("0/90", html);
        StringAssert.Contains("아직 끝난 판이 없다", html);
        StringAssert.Contains("특성 기록 없음", html);
        StringAssert.Contains("클리어 없음", html);
        List<QABoss103Report.Summary> s = QABoss103Report.Summaries(new QABoss103ReportInput());
        Assert.AreEqual(0, s[3].Runs);
        Assert.IsTrue(double.IsNaN(s[3].ClearRate));
    }

    [Test]
    public void 오류만_있는_데이터는_통계_0판_오류만_보인다()
    {
        var input = new QABoss103ReportInput
        {
            RunsCsv = QABoss103BatchTests.RunsCsv((1, 0, "Error", "na", "start_timeout 단계 lobby"), (2, 0, "Clear", "inspector", ""), (1, 1, "Error", "na", "heartbeat")),
            Total = 2,
        };
        string html = QABoss103Report.Build(input);
        StringAssert.Contains("오류 판 2개(통계 제외)", html);
        StringAssert.Contains("data_source 1", html);
        StringAssert.Contains("heartbeat 1", html);
        StringAssert.Contains("<b class=\"err\">2</b>", html);
        Assert.AreEqual(0, QABoss103Report.Summaries(input)[3].Runs);
        StringAssert.Contains("테이블이 아닌 행은 오류 판", html);
    }

    [Test]
    public void 형식_도구()
    {
        Assert.AreEqual("10:05", QABoss103Report.Time(605.4));
        Assert.AreEqual("–", QABoss103Report.Time(-1));
        Assert.AreEqual("33.3%", QABoss103Report.Pct(1.0 / 3.0, 1));
        Assert.AreEqual("–", QABoss103Report.Pct(double.NaN, 1));
        Assert.AreEqual(2.5, QABoss103Report.Median(new List<double> { 4, 1, 3, 2 }), 1e-9);
        Assert.AreEqual("&lt;a href=&quot;x&quot;&gt;", QABoss103Report.H("<a href=\"x\">"));
    }
}
#endif
