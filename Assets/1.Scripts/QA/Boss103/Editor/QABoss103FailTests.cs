#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;

/// <summary>할 일 119 C3 — 클리어 실패 사유(run_fail.csv)·리포트 실패 표·지난 배치 비교·30판 배치 설정.</summary>
public class QABoss103FailTests
{
    private static QABoss103RunMeta Meta(QABoss103Outcome o, string source = "") => new QABoss103RunMeta
    {
        Run = 3, Retry = 0, Tier = QABoss103BotTier.Beginner, Seed = 103, RunSeed = 309, Outcome = o, CombatSeconds = 700, BossHpEndPct = 42.5, OutcomeSource = source,
    };

    private static QABoss103RunStats Stats()
    {
        var s = new QABoss103RunStats();
        s.Begin(100);
        s.PlayerHit(0, 300, "Dash");
        s.Death(0, 301);
        s.PlayerHit(2, 400, "floor");
        s.Death(2, 401);
        s.PlayerHit(1, 500, "floor");
        s.Death(1, 501);
        s.Gimmicks.Add(new QABoss103GimmickEvent { Gimmick = "북진 2회차", Result = "fail", ServerT = 480 });
        s.End(800);
        return s;
    }

    [Test]
    public void PylonWave_OpensTracksMaxAndCloses()
    {
        var t = new QABoss103PylonWaveTracker();
        t.Tick(10, 0, 0, null);
        t.Damage(50);   // 구간 밖 — 버림
        t.Tick(11, 4, 0, null);
        t.Damage(100);
        t.Tick(12, 4, 2, null);
        t.Tick(13, 2, 1, null);   // 부서진 탑이 내려가며 참여 수가 줄어도 최대 유지
        t.Tick(14, 0, 0, null);
        t.Tick(20, 3, 0, QABoss103FailReason.PylonCharge);
        Assert.AreEqual(2, t.Waves.Count);
        QABoss103PylonWave w = t.Waves[0];
        Assert.AreEqual(4, w.Engaged);
        Assert.AreEqual(2, w.Destroyed);
        Assert.AreEqual(100, w.Damage);
        Assert.IsFalse(w.Open);
        Assert.IsTrue(w.Failed);
        Assert.AreEqual(14, w.EndT, 1e-9);
        Assert.IsTrue(t.Waves[1].Open);
        Assert.AreEqual(QABoss103FailReason.PylonCharge, t.Waves[1].Label);
    }

    [Test]
    public void LabelWaves_UsesNearestPylonGimmick()
    {
        var waves = new List<QABoss103PylonWave>
        {
            new QABoss103PylonWave { StartT = 450, EndT = 478, Engaged = 4, Destroyed = 2, Open = false },
            new QABoss103PylonWave { StartT = 600, EndT = 620, Engaged = 3, Destroyed = 3, Open = false },
            new QABoss103PylonWave { StartT = 700, EndT = 710, Label = "송전기", Engaged = 2, Open = false },
        };
        var g = new List<QABoss103GimmickEvent>
        {
            new QABoss103GimmickEvent { Gimmick = "새 가족", Result = "fail", ServerT = 479 },
            new QABoss103GimmickEvent { Gimmick = "북진 2회차", Result = "fail", ServerT = 480 },
        };
        QABoss103FailReason.LabelWaves(waves, g);
        Assert.AreEqual(QABoss103FailReason.PushNorth, waves[0].Label);
        Assert.AreEqual(QABoss103FailReason.PylonGeneric, waves[1].Label);
        Assert.AreEqual("송전기", waves[2].Label);
        Assert.AreEqual(QABoss103FailReason.PowerSupply, QABoss103FailReason.PylonGimmickOf("전력 공급"));
        Assert.IsNull(QABoss103FailReason.PylonGimmickOf("연쇄 간파"));
    }

    [Test]
    public void Row_Wipe_PylonFailNoteAndColumns()
    {
        var waves = new List<QABoss103PylonWave>
        {
            new QABoss103PylonWave { Label = QABoss103FailReason.PushNorth, StartT = 450, EndT = 478, Engaged = 4, Destroyed = 2, Damage = 1234, Open = false },
        };
        var snap = new QABoss103FailSnapshot { BossState = "Attack", BossAttack = "Dash", BossAttackPhase = "Windup", BossPhase = 1, Alive = 0, Lives = 0, ServerT = 520, LastAttack = "Dash" };
        string[] row = QABoss103FailReason.Row(Meta(QABoss103Outcome.Wipe), Stats(), snap, waves);
        Assert.IsNotNull(row);
        Assert.AreEqual(QABoss103FailReason.Header.Length, row.Length);
        string Get(string col) => row[Array.IndexOf(QABoss103FailReason.Header, col)];
        Assert.AreEqual("Wipe", Get("outcome"));
        Assert.AreEqual(QABoss103FailReason.PushNorth, Get("fail_pattern"), "판 끝 120초 안 실패한 송전탑 기믹이 집계 키");
        Assert.AreEqual("Dash", Get("boss_attack"));
        Assert.AreEqual("2", Get("boss_phase"), "페이즈는 1부터");
        Assert.AreEqual("0", Get("alive"));
        Assert.AreEqual("0", Get("lives_left"));
        Assert.AreEqual("42.5", Get("boss_hp_pct"));
        Assert.AreEqual("Assassin", Get("last_death_character"));
        Assert.AreEqual("floor", Get("last_death_pattern"));
        Assert.AreEqual("floor:2;Dash:1", Get("deaths_by_pattern"));
        Assert.AreEqual("4", Get("pylon_total"));
        Assert.AreEqual("2", Get("pylon_destroyed"));
        Assert.AreEqual("1", Get("pylon_waves_failed"));
        Assert.AreEqual("북진 2회차", Get("gimmick_fails"));
        string note = Get("note");
        StringAssert.Contains("북진 압박: 송전탑 4개 중 2개 파괴(탑 피해 1234) — 모든 스킬·공격을 퍼부어도 전부 못 부숨", note);
        StringAssert.Contains("마지막 사망 Assassin ← floor", note);
        StringAssert.Contains("보스 남은 HP 42.5%", note);
        StringAssert.Contains("전멸", note);
    }

    [Test]
    public void Row_FallbacksAndNonFail()
    {
        var snap = new QABoss103FailSnapshot { BossState = "Chase", Alive = 1, Lives = 0, ServerT = 800 };
        // 오래된 실패 구간(판 끝 120초 밖)은 집계 키가 아니다 → 전멸이면 마지막 사망 패턴.
        var old = new List<QABoss103PylonWave> { new QABoss103PylonWave { Label = "송전기", StartT = 100, EndT = 200, Engaged = 2, Destroyed = 1, Open = false } };
        string[] wipe = QABoss103FailReason.Row(Meta(QABoss103Outcome.Wipe), Stats(), snap, old);
        Assert.AreEqual("floor", wipe[Array.IndexOf(QABoss103FailReason.Header, "fail_pattern")]);
        // 조기 시간초과 — 사망 패턴이 아니라 보스 상태.
        string[] early = QABoss103FailReason.Row(Meta(QABoss103Outcome.Timeout, QABoss103EarlyTimeout.Note), Stats(), snap, null);
        Assert.AreEqual("Chase", early[Array.IndexOf(QABoss103FailReason.Header, "fail_pattern")]);
        StringAssert.Contains("조기 시간초과", early[Array.IndexOf(QABoss103FailReason.Header, "note")]);
        // 진행 중 기믹(열린 구간)이 가장 우선.
        var open = new List<QABoss103PylonWave> { new QABoss103PylonWave { Label = QABoss103FailReason.PushNorth, StartT = 700, EndT = 799, Engaged = 4, Destroyed = 1 } };
        string[] timeout = QABoss103FailReason.Row(Meta(QABoss103Outcome.Timeout), Stats(), snap, open);
        Assert.AreEqual(QABoss103FailReason.PushNorth, timeout[Array.IndexOf(QABoss103FailReason.Header, "fail_pattern")]);
        StringAssert.Contains("판 끝까지 진행 중", timeout[Array.IndexOf(QABoss103FailReason.Header, "note")]);

        Assert.IsNull(QABoss103FailReason.Row(Meta(QABoss103Outcome.Clear), Stats(), snap, null));
        Assert.IsNull(QABoss103FailReason.Row(Meta(QABoss103Outcome.Error), Stats(), snap, null));
        Assert.IsNull(QABoss103FailReason.Row(Meta(QABoss103Outcome.Wipe), new QABoss103RunStats(), snap, null), "Combat 전 종료는 행 없음");
    }

    // ── 리포트 ───────────────────────────────────────────────

    /// <summary>가짜 배치(<see cref="QABoss103ReportFake"/> — 전멸 3·7, 시간초과 6)에 실패 사유 행 + 통계 밖 판 10(오류) 행.</summary>
    private static QABoss103ReportInput WithFails()
    {
        QABoss103ReportInput input = QABoss103ReportFake.Input();
        var sb = new StringBuilder();
        sb.Append(QABoss103Csv.Line(QABoss103FailReason.Header)).Append("\r\n");
        void Add(int run, string tier, string outcome, string pattern, string note)
        {
            var cells = new string[QABoss103FailReason.Header.Length];
            for (int i = 0; i < cells.Length; i++)
                cells[i] = "";
            void Set(string col, string v) => cells[Array.IndexOf(QABoss103FailReason.Header, col)] = v;
            Set("run", run.ToString()); Set("retry", "0"); Set("tier", tier); Set("seed", "103");
            Set("outcome", outcome); Set("fail_pattern", pattern); Set("alive", "0"); Set("lives_left", "0"); Set("boss_hp_pct", "40.0"); Set("note", note);
            sb.Append(QABoss103Csv.Line(cells)).Append("\r\n");
        }
        Add(3, "Beginner", "Wipe", QABoss103FailReason.PushNorth, "북진 압박: 송전탑 4개 중 2개 파괴");
        Add(6, "Beginner", "Timeout", "Chase", "시간초과");
        Add(7, "Normal", "Wipe", QABoss103FailReason.PushNorth, "북진 압박: 송전탑 4개 중 3개 파괴");
        Add(10, "Normal", "Wipe", "Dash", "통계 밖");
        input.FailCsv = sb.ToString();
        return input;
    }

    [Test]
    public void Report_FailPatternCounts_ValidRunsOnlyByTier()
    {
        SortedDictionary<string, int[]> map = QABoss103Report.FailPatternCounts(WithFails());
        Assert.IsFalse(map.ContainsKey("Dash"), "오류 판(10)은 집계 제외");
        // 열 = 보통·숙련·초보·전체
        CollectionAssert.AreEqual(new[] { 1, 0, 1, 2 }, map[QABoss103FailReason.PushNorth]);
        CollectionAssert.AreEqual(new[] { 0, 0, 1, 1 }, map["Chase"]);
    }

    [Test]
    public void Report_HasFailTableRulesAndPreviousComparison()
    {
        QABoss103ReportInput input = WithFails();
        input.CompareDir = "C:/UnityProject/QA/103-20261027_0057";
        input.Compare = QABoss103ReportFake.Input();
        string html = QABoss103Report.Build(input);
        StringAssert.Contains("클리어 실패 사유", html);
        StringAssert.Contains("패턴별 실패 집계", html);
        StringAssert.Contains("북진 압박: 송전탑 4개 중 2개 파괴", html);
        StringAssert.Contains("119 규칙 변경점", html);
        StringAssert.Contains("즉사 대시 안 함", html);
        StringAssert.Contains("벽 끼임 텔레포트", html);
        StringAssert.Contains("F10 부활 = 공용 목숨 1", html);
        StringAssert.Contains("툴팁 규칙", html);
        StringAssert.Contains("지난 배치 비교 — 103-20261027_0057", html);
        StringAssert.Contains("+0.0%p", html, "같은 입력 비교 → 차이 0");
        StringAssert.Contains("xlsx SVN 리비전", html, "데이터 출처·리비전 머리 유지");

        input.Compare = null;
        StringAssert.Contains("비교 기준 폴더를 읽지 못했다", QABoss103Report.Build(input));
        input.CompareDir = "";
        StringAssert.DoesNotContain("지난 배치 비교", QABoss103Report.Build(input));
    }

    // ── 30판 배치 ────────────────────────────────────────────

    [Test]
    public void Batch30_TenRunsPerTierAndFolderNames()
    {
        var count = new Dictionary<QABoss103BotTier, int>();
        for (int run = 1; run <= QABoss103Batch.TotalRuns119; run++)
        {
            QABoss103BotTier t = QABoss103Batch.TierOf(run);
            count[t] = (count.TryGetValue(t, out int n) ? n : 0) + 1;
        }
        Assert.AreEqual(10, count[QABoss103BotTier.Normal]);
        Assert.AreEqual(10, count[QABoss103BotTier.Skilled]);
        Assert.AreEqual(10, count[QABoss103BotTier.Beginner]);
        Assert.AreEqual(QABoss103BotTier.Normal, QABoss103Batch.TierOf(1), "103 과 같은 순서 — 보통 → 숙련 → 초보");

        var at = new DateTime(2026, 10, 27, 9, 5, 0);
        Assert.AreEqual("119-20261027_0905", QABoss103Batch.BatchFolderName(at, QABoss103Batch.Prefix119));
        Assert.AreEqual("103-20261027_0905", QABoss103Batch.BatchFolderName(at));
        Assert.AreEqual("20261027_0905", QABoss103Batch.BatchStamp("119-20261027_0905"));
        Assert.Greater(string.CompareOrdinal(QABoss103Batch.BatchStamp("103-20261027_1000"), QABoss103Batch.BatchStamp("119-20261027_0905")), 0,
            "최신 폴더는 접두어가 아니라 시각으로 고른다");
        Assert.IsNull(QABoss103Batch.BatchStamp("103-single-20261027_090500"));
        Assert.IsNull(QABoss103Batch.BatchStamp("88-20261027_0905"));
    }
}
#endif
