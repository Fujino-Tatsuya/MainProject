#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;

/// <summary>할 일 103 C4 순수 집계 — 가짜 이벤트 흐름으로 지표 14개·사망 귀속·패턴 매칭·CSV 직렬화/이스케이프·판 파일·전투 기록 파싱.</summary>
public sealed class QABoss103MetricsTests
{
    private const int Pal = 0, Asn = 1, Gun = 2;

    /// <summary>
    /// 60초 판: 팔라딘 훅 1회 피격(다단 2틱) · 거너 장판 피격 직후 사망 · 어쌔신 변신 10초 · 제압 2회 · 간파 1성공.
    /// 판 파일: 팔라딘(굴림 1·간파 시도 1 + 거부 1·done) · 어쌔신(돌진 회피·done) · 거너 없음.
    /// </summary>
    private static QABoss103RunStats Scenario()
    {
        var s = new QABoss103RunStats();
        s.Begin(100.0);
        s.Lives(3);

        s.AttackStart(110.0, "LeftHook");
        s.PlayerHit(Pal, 110.5, "LeftHook");
        s.PlayerHit(Pal, 110.6, "LeftHook");   // 같은 공격의 다음 틱 — 1건
        s.HealthLost(Pal, 120);

        s.PlayerHit(Gun, 130.0, "floor");
        s.HealthLost(Gun, 500);
        s.Death(Gun, 131.0);
        s.Lives(2);

        for (int i = 0; i < 4; i++)
            s.Use(Pal, QABoss103Action.Basic);
        for (int i = 0; i < 3; i++)
            s.Landed(Pal, QABoss103Action.Basic);
        s.TypedDamage(Pal, QABoss103Action.Basic, 300);
        s.DamageDealt(Pal, 300, false);

        s.Use(Asn, QABoss103Action.R);
        s.Transform(Asn, true, 140.0);
        s.DamageDealt(Asn, 400, false);
        s.Transform(Asn, false, 150.0);
        s.DamageDealt(Asn, 100, false);

        s.Suppressed(true);
        s.DamageDealt(Gun, 200, true);
        s.Suppressed(true);   // 유지 — 다시 세지 않는다
        s.Suppressed(false);
        s.Suppressed(true);

        s.CounterSuccess(Pal);
        s.End(160.0);

        s.AddPart(Pal, new[]
        {
            "threat slot=0 pattern=cone attack=na id=c1 serverT=110.400 roll=take action=none ok=na note=none",
            "counter slot=0 window=1 roll=hit cast=true serverT=120.000",
            "counter slot=0 window=2 roll=miss cast=false serverT=125.000",
            "done slot=0 reason=host_end",
        });
        s.AddPart(Asn, new[]
        {
            "threat slot=1 pattern=dash attack=na id=d1 serverT=120.000 roll=dodge action=dash ok=true note=dir_1/0",
            "done slot=1 reason=destroy",
        });
        s.Finish();
        return s;
    }

    private static QABoss103RunMeta Meta(QABoss103Outcome outcome = QABoss103Outcome.Clear) => new QABoss103RunMeta
    {
        Run = 7, Retry = 0, Tier = QABoss103BotTier.Skilled, Seed = 103, RunSeed = 42, Outcome = outcome, CombatSeconds = 60.0,
        TraitsPaladin = "a;b", DataSource = "table", XlsxSvnRev = "1234", GitCommit = "abc1234", UnityVersion = "6000.3.16f1",
    };

    private static string Col(string[] header, string[] row, string name)
    {
        int i = Array.IndexOf(header, name);
        Assert.GreaterOrEqual(i, 0, $"열 {name} 없음");
        Assert.AreEqual(header.Length, row.Length, "열 수");
        return row[i];
    }

    // ── 지표 14개 ────────────────────────────────────────────

    [Test]
    public void 판_결과_전투시간_전멸_시간초과_목숨_사망()
    {
        QABoss103RunStats s = Scenario();
        string[] run = QABoss103CsvRows.Run(Meta(), s);
        string[] h = QABoss103Metrics.RunsHeader;
        Assert.AreEqual("7", Col(h, run, "run"));
        Assert.AreEqual("Skilled", Col(h, run, "tier"));
        Assert.AreEqual("Clear", Col(h, run, "outcome"));
        Assert.AreEqual("60.0", Col(h, run, "combat_sec"));
        Assert.AreEqual("0", Col(h, run, "wipes"));
        Assert.AreEqual("0", Col(h, run, "timeout"));
        Assert.AreEqual("1", Col(h, run, "party_lives_used"));
        Assert.AreEqual("1", Col(h, run, "deaths_total"));
        Assert.AreEqual("a;b", Col(h, run, "traits_paladin"));
        Assert.AreEqual("table", Col(h, run, "data_source"));

        string[] timeout = QABoss103CsvRows.Run(Meta(QABoss103Outcome.Timeout), s);
        Assert.AreEqual("1", Col(h, timeout, "timeout"));
    }

    [Test]
    public void 받은_피해_분당_사망()
    {
        List<string[]> rows = QABoss103CsvRows.Chars(Meta(), Scenario());
        string[] h = QABoss103Metrics.CharsHeader;
        Assert.AreEqual(3, rows.Count);
        Assert.AreEqual("Paladin", Col(h, rows[Pal], "character"));
        Assert.AreEqual("120", Col(h, rows[Pal], "damage_taken"));
        Assert.AreEqual("120.0", Col(h, rows[Pal], "damage_taken_per_min"));
        Assert.AreEqual("500.0", Col(h, rows[Gun], "damage_taken_per_min"));
        Assert.AreEqual("1", Col(h, rows[Gun], "deaths"));
        Assert.AreEqual("0", Col(h, rows[Pal], "deaths"));
    }

    [Test]
    public void 총피해_DPS_딜기여율()
    {
        List<string[]> rows = QABoss103CsvRows.Chars(Meta(), Scenario());
        string[] h = QABoss103Metrics.CharsHeader;
        Assert.AreEqual("300", Col(h, rows[Pal], "damage_dealt"));
        Assert.AreEqual("5.0", Col(h, rows[Pal], "dps"));
        Assert.AreEqual("0.3000", Col(h, rows[Pal], "damage_share"));
        Assert.AreEqual("0.5000", Col(h, rows[Asn], "damage_share"));
        Assert.AreEqual("0.2000", Col(h, rows[Gun], "damage_share"));
    }

    [Test]
    public void 행동별_사용_적중_피해()
    {
        List<string[]> rows = QABoss103CsvRows.Chars(Meta(), Scenario());
        string[] h = QABoss103Metrics.CharsHeader;
        Assert.AreEqual("4", Col(h, rows[Pal], "basic_use"));
        Assert.AreEqual("3", Col(h, rows[Pal], "basic_hit"));
        Assert.AreEqual("300", Col(h, rows[Pal], "basic_damage"));
        Assert.AreEqual("1", Col(h, rows[Asn], "r_use"));
        Assert.AreEqual("0", Col(h, rows[Asn], "rclick_use"));
    }

    [Test]
    public void 어쌔신_변신_횟수_지속_점유율_상태별_피해_DPS()
    {
        List<string[]> rows = QABoss103CsvRows.Chars(Meta(), Scenario());
        string[] h = QABoss103Metrics.CharsHeader;
        Assert.AreEqual("1", Col(h, rows[Asn], "assassin_transform_count"));
        Assert.AreEqual("10.0", Col(h, rows[Asn], "transform_sec_total"));
        Assert.AreEqual("0.1667", Col(h, rows[Asn], "transform_uptime"));
        Assert.AreEqual("100", Col(h, rows[Asn], "dmg_normal"));
        Assert.AreEqual("400", Col(h, rows[Asn], "dmg_transformed"));
        Assert.AreEqual("2.0", Col(h, rows[Asn], "dps_normal"));       // 100 / (60 − 10)
        Assert.AreEqual("40.0", Col(h, rows[Asn], "dps_transformed")); // 400 / 10
        Assert.AreEqual("", Col(h, rows[Pal], "assassin_transform_count"), "어쌔신만");
    }

    [Test]
    public void 변신이_판_끝까지_켜져_있으면_끝에서_닫는다()
    {
        var s = new QABoss103RunStats();
        s.Begin(0.0);
        s.Transform(Asn, true, 50.0);
        s.End(60.0);
        Assert.AreEqual(1, s.Chars[Asn].TransformCount);
        Assert.AreEqual(10.0, s.Chars[Asn].TransformSeconds, 1e-9);
    }

    [Test]
    public void 팔라딘_방어_성공은_알림_구독_전엔_빈칸()
    {
        QABoss103RunStats s = Scenario();
        string[] h = QABoss103Metrics.CharsHeader;
        Assert.AreEqual("", Col(h, QABoss103CsvRows.Chars(Meta(), s)[Pal], "paladin_guard_success"));
        Assert.AreEqual("", Col(h, QABoss103CsvRows.Chars(Meta(), s)[Pal], "paladin_guard_counter"));
        s.Chars[Pal].GuardSuccess = 3;
        Assert.AreEqual("3", Col(h, QABoss103CsvRows.Chars(Meta(), s)[Pal], "paladin_guard_success"));
    }

    // ── C5 게임 코드 알림 ────────────────────────────────────

    [Test]
    public void 팔라딘_방어_알림_구독하면_0부터_종류별로_센다()
    {
        QABoss103RunStats s = Scenario();
        s.GuardNotifyOn();
        string[] h = QABoss103Metrics.CharsHeader;
        List<string[]> rows = QABoss103CsvRows.Chars(Meta(), s);
        Assert.AreEqual("0", Col(h, rows[Pal], "paladin_guard_success"), "구독했는데 0회 = 0(빈칸 아님)");
        Assert.AreEqual("0", Col(h, rows[Pal], "paladin_guard_counter"));
        Assert.AreEqual("0", Col(h, rows[Pal], "paladin_guard_ignore"));

        s.GuardAbsorbed(Pal, counter: true);
        s.GuardAbsorbed(Pal, counter: false);
        s.GuardAbsorbed(Pal, counter: false);
        s.GuardAbsorbed(-1, counter: true);   // 모르는 자리 — 무시
        rows = QABoss103CsvRows.Chars(Meta(), s);
        Assert.AreEqual("3", Col(h, rows[Pal], "paladin_guard_success"), "흡수 전부(방어 성공 + 반격 중 무시)");
        Assert.AreEqual("1", Col(h, rows[Pal], "paladin_guard_counter"));
        Assert.AreEqual("2", Col(h, rows[Pal], "paladin_guard_ignore"));
        Assert.AreEqual("", Col(h, rows[Gun], "paladin_guard_success"), "팔라딘 열은 팔라딘만");
    }

    [Test]
    public void 기믹_알림_행_결과_정규화와_note()
    {
        QABoss103GimmickEvent e = QABoss103GimmickNotify.FromEvent("연쇄 간파", "Partial", 3, 230.0, 100.0);
        Assert.AreEqual("연쇄 간파", e.Gimmick);
        Assert.AreEqual("partial", e.Result);
        Assert.AreEqual("event", e.Source);
        Assert.AreEqual(130.0, e.FightT, 1e-9);
        Assert.AreEqual(230.0, e.ServerT, 1e-9);
        Assert.AreEqual("successes=3", e.Note);

        Assert.AreEqual("success", QABoss103GimmickNotify.NormalizeResult("Full"));
        Assert.AreEqual("fail", QABoss103GimmickNotify.NormalizeResult("None"));
        Assert.AreEqual("timeout", QABoss103GimmickNotify.NormalizeResult("timeout"));
        Assert.AreEqual("pushes=4", QABoss103GimmickNotify.NoteOf("간파 릴레이", 4));
        Assert.AreEqual("destroyed_tiles=2", QABoss103GimmickNotify.NoteOf("전력 공급", 2));
        Assert.AreEqual("", QABoss103GimmickNotify.NoteOf("전력 공급", 0));
        Assert.AreEqual("counter", QABoss103GimmickNotify.NoteOf("새 가족", 1));
        Assert.AreEqual("", QABoss103GimmickNotify.NoteOf("북진 2회차", 2), "회차는 이름에 있다");
        Assert.IsTrue(double.IsNaN(QABoss103GimmickNotify.FromEvent("송전기", "success", 0, 5.0, double.NaN).FightT));
    }

    [Test]
    public void 기믹_알림이_우선이고_같은_로그_행은_지운다()
    {
        var logs = new List<QABoss103GimmickEvent>
        {
            new QABoss103GimmickEvent { Gimmick = "송전기", Result = "success", FightT = 30.0 },
            new QABoss103GimmickEvent { Gimmick = "송전기", Result = "success", FightT = 90.0 },
            new QABoss103GimmickEvent { Gimmick = "간파 릴레이", Result = "ended", FightT = 150.0, Note = "보스 사망" },
            new QABoss103GimmickEvent { Gimmick = "연쇄 간파", Result = "partial", FightT = 130.0, Note = "3/4" },
            new QABoss103GimmickEvent { Gimmick = "북진 1회차", Result = "fail", FightT = 100.0 },
        };
        var events = new List<QABoss103GimmickEvent>
        {
            QABoss103GimmickNotify.FromEvent("송전기", "success", 0, 130.4, 100.0),
            QABoss103GimmickNotify.FromEvent("연쇄 간파", "Partial", 3, 230.1, 100.0),
            QABoss103GimmickNotify.FromEvent("북진 1회차", "success", 1, 199.0, 100.0),   // 로그는 fail — 결과가 다르면 둘 다 남긴다
        };
        List<QABoss103GimmickEvent> m = QABoss103GimmickNotify.Merge(events, logs);
        var got = new List<string>();
        foreach (QABoss103GimmickEvent g in m)
            got.Add($"{g.Gimmick}|{g.Result}|{g.Source}");
        CollectionAssert.AreEqual(new[]
        {
            "송전기|success|event",     // 30.4
            "송전기|success|log",       // 90 — 알림 1건은 로그 1건만 지운다
            "북진 1회차|success|event", // 99
            "북진 1회차|fail|log",      // 100
            "연쇄 간파|partial|event",  // 130.1
            "간파 릴레이|ended|log",    // 150 — 결과 없는 끝은 로그만 안다
        }, got);
    }

    [Test]
    public void 기믹_알림이_없으면_로그_그대로()
    {
        List<QABoss103GimmickEvent> logs = QABoss103FightLog.Parse("  [  30.5s] 송전기 성공\n  [  40.0s] ② 전력 공급 실패 → 방전\n", out _);
        List<QABoss103GimmickEvent> m = QABoss103GimmickNotify.Merge(new List<QABoss103GimmickEvent>(), logs);
        Assert.AreEqual(2, m.Count);
        Assert.AreEqual("log", m[0].Source);
        Assert.AreEqual(0, QABoss103GimmickNotify.Merge(null, null).Count);
    }

    [Test]
    public void 기믹_알림_행_CSV_source_event()
    {
        QABoss103RunStats s = Scenario();
        s.Gimmicks.AddRange(QABoss103GimmickNotify.Merge(
            new List<QABoss103GimmickEvent> { QABoss103GimmickNotify.FromEvent("간파 릴레이", "success", 4, 130.5, 100.0) }, null));
        List<string[]> rows = QABoss103CsvRows.Gimmicks(Meta(), s);
        string[] h = QABoss103Metrics.GimmicksHeader;
        Assert.AreEqual("event", Col(h, rows[0], "source"));
        Assert.AreEqual("30.5", Col(h, rows[0], "fight_t"));
        Assert.AreEqual("130.500", Col(h, rows[0], "serverT"));
        Assert.AreEqual("pushes=4", Col(h, rows[0], "note"));
    }

    [Test]
    public void 간파_시도는_판파일_성공은_서버_거부된_누름은_시도_아님()
    {
        QABoss103RunStats s = Scenario();
        string[] h = QABoss103Metrics.CharsHeader;
        List<string[]> rows = QABoss103CsvRows.Chars(Meta(), s);
        Assert.AreEqual("1", Col(h, rows[Pal], "counter_tries"));
        Assert.AreEqual("1", Col(h, rows[Pal], "counter_success"));
        Assert.AreEqual("", Col(h, rows[Gun], "counter_tries"), "판 파일 없음 = 빈칸");
        string[] run = QABoss103CsvRows.Run(Meta(), s);
        Assert.AreEqual("1", Col(QABoss103Metrics.RunsHeader, run, "counter_tries"));
        Assert.AreEqual("1", Col(QABoss103Metrics.RunsHeader, run, "counter_success"));
    }

    [Test]
    public void 제압_횟수와_제압_중_피해()
    {
        QABoss103RunStats s = Scenario();
        Assert.AreEqual(2, s.SuppressCount);
        string[] h = QABoss103Metrics.CharsHeader;
        Assert.AreEqual("200", Col(h, QABoss103CsvRows.Chars(Meta(), s)[Gun], "dmg_during_suppress"));
        Assert.AreEqual("2", Col(QABoss103Metrics.RunsHeader, QABoss103CsvRows.Run(Meta(), s), "suppress_count"));
    }

    [Test]
    public void 패턴별_굴림_피격_사망과_실제_피격률_입력()
    {
        QABoss103RunStats s = Scenario();
        SortedDictionary<string, int[]> t = s.PatternTable();
        CollectionAssert.AreEqual(new[] { 1, 1, 0, 1, 0 }, t["LeftHook"], "부채꼴 굴림 → 시간표의 LeftHook");
        CollectionAssert.AreEqual(new[] { 0, 0, 0, 1, 1 }, t["floor"]);
        CollectionAssert.AreEqual(new[] { 1, 0, 1, 0, 0 }, t["Dash"], "돌진 모양은 시간표보다 모양");
        string[] run = QABoss103CsvRows.Run(Meta(), s);
        string[] h = QABoss103Metrics.RunsHeader;
        Assert.AreEqual("2", Col(h, run, "threat_rolls"));
        Assert.AreEqual("1", Col(h, run, "rolled_take"));
        Assert.AreEqual("2", Col(h, run, "actual_hits"));

        List<string[]> rows = QABoss103CsvRows.Patterns(Meta(), s);
        Assert.AreEqual(3, rows.Count);
        Assert.AreEqual("Dash", Col(QABoss103Metrics.PatternsHeader, rows[0], "pattern"), "사전순");
    }

    [Test]
    public void 회피_결과와_굴림_없는_피격()
    {
        QABoss103RunStats s = Scenario();
        string[] h = QABoss103Metrics.CharsHeader;
        List<string[]> rows = QABoss103CsvRows.Chars(Meta(), s);
        Assert.AreEqual("1", Col(h, rows[Pal], "evade_take"));
        Assert.AreEqual("1", Col(h, rows[Asn], "evade_dodge"));
        Assert.AreEqual("1", Col(h, rows[Pal], "hits_taken"));
        Assert.AreEqual("0", Col(h, rows[Pal], "hits_unrolled"), "훅 피격 0.1초 전 굴림");
        Assert.AreEqual("", Col(h, rows[Gun], "hits_unrolled"), "판 파일 없음");
        Assert.AreEqual("missing_parts=Gunner", s.PartsNote());
        StringAssert.Contains("missing_parts=Gunner", Col(QABoss103Metrics.RunsHeader, QABoss103CsvRows.Run(Meta(), s), "note"));
    }

    [Test]
    public void 판파일_끝표시가_없으면_partial()
    {
        var s = new QABoss103RunStats();
        s.Begin(0);
        s.AddPart(Pal, new[] { "done slot=0" });
        s.AddPart(Asn, new[] { "threat slot=1 pattern=jump serverT=1.0 roll=take" });
        s.AddPart(Gun, new[] { "done slot=2" });
        Assert.AreEqual("partial_parts=Assassin", s.PartsNote());
    }

    [Test]
    public void 굴림_없는_피격은_다른_패턴_굴림을_인정하지_않는다()
    {
        var s = new QABoss103RunStats();
        s.Begin(0);
        s.AttackStart(10.0, "Upper");
        s.PlayerHit(Pal, 11.0, "Upper");
        s.PlayerHit(Pal, 20.0, "Jump");
        s.PlayerHit(Pal, 40.0, QABoss103Metrics.Unknown);
        s.AddPart(Pal, new[]
        {
            "threat slot=0 pattern=cone serverT=10.2 roll=take",
            "threat slot=0 pattern=floor serverT=19.0 roll=take",
            "threat slot=0 pattern=generic serverT=38.5 roll=take",
        });
        s.Finish();
        Assert.AreEqual(1, s.Chars[Pal].HitsUnrolled, "점프 피격 = 굴림 없음, 모름 피격 = 3초 안 아무 굴림");
    }

    [Test]
    public void 봇_충실도_피함_굴림은_그_위험_피격이_없어야_성공()
    {
        var s = new QABoss103RunStats();
        s.Begin(0);
        s.PlayerHit(Pal, 11.3, "Jump");     // jump#1(10.0, 예상 1.2초) 창 안 → 회피 실패
        s.PlayerHit(Pal, 25.0, "floor");    // floor#2(20.0, 예상 1.2초) 창 2.2초 밖 → 그 위험 아님
        s.PlayerHit(Pal, 31.4, "floor");    // floor#3 은 맞기 굴림 — 회피 지표와 무관
        s.CounterSuccess(Pal, 40.4);
        s.CounterSuccess(Pal, 60.0);        // 누름 50.0 뒤 10초 — 다른 누름의 결과 아님
        s.AddPart(Pal, new[]
        {
            "threat slot=0 pattern=jump attack=Jump serverT=10.0 roll=dodge action=dash ok=true hitMs=1200",
            "threat slot=0 pattern=floor serverT=20.0 roll=dodge action=dash ok=true hitMs=1200",
            "threat slot=0 pattern=floor serverT=30.0 roll=take hitMs=1200",
            "threat slot=0 pattern=drone serverT=33.0 roll=dodge action=none note=idle_life_Dead",
            "counter slot=0 window=1 roll=hit cast=true serverT=40.2",
            "counter slot=0 window=2 roll=hit cast=false serverT=50.0",
            "counter slot=0 window=3 roll=miss cast=true serverT=59.9",
            "done slot=0",
        });
        s.AddPart(Asn, new[]
        {
            "counter slot=1 window=1 roll=hit cast=true serverT=40.3",   // 같은 창을 팔라딘이 40.4 에 가져갔다 — 뺀다
            "counter slot=1 window=4 roll=hit cast=true serverT=70.0",   // 아무도 성공 못 함 — 실패로 센다
            "done slot=1",
        });
        s.Finish();
        Assert.AreEqual(1, s.Chars[Asn].CounterHitRolls, "다른 봇이 먼저 가져간 창의 누름은 셈에서 뺀다");
        Assert.AreEqual(0, s.Chars[Asn].CounterHitSuccess);
        QABoss103CharStats c = s.Chars[Pal];
        Assert.AreEqual(2, c.DodgeRolls, "idle_ 굴림은 행동 못 함 — 뺀다");
        Assert.AreEqual(1, c.DodgeSuccess);
        Assert.AreEqual(2, c.CounterHitRolls, "성공 굴림만(시전 거부 포함)");
        Assert.AreEqual(1, c.CounterHitSuccess);
        string[] h = QABoss103Metrics.CharsHeader;
        List<string[]> rows = QABoss103CsvRows.Chars(Meta(), s);
        Assert.AreEqual("1", Col(h, rows[Pal], "dodge_success"));
        Assert.AreEqual("1", Col(h, rows[Pal], "counter_roll_success"));
        Assert.AreEqual("", Col(h, rows[Gun], "dodge_success"), "판 파일 없음");
        Assert.AreEqual(2.5, QABoss103Metrics.FidelityWindow(double.NaN), 1e-9, "예상 피격 모름 = 1.5 + 1");
    }

    [Test]
    public void 기믹_전투기록_파싱()
    {
        const string log =
            "[23호/전투기록] 기존 · 인원 3 · 처치\n" +
            "  총 3.0분(180s) · 플레이어 피해 합 1000\n" +
            "  [   0.0s] 개전 — 기존 · 인원 3 · 최대 HP 1000\n" +
            "  [  30.5s] 송전기 성공\n" +
            "  [  60.0s] 제압 — 간파\n" +
            "  [  90.0s] 북진 압박 시작\n" +
            "  [  95.0s] 북진 1회차 — 간파 → 1칸\n" +
            "  [ 100.0s] 북진 1회차 실패 — 즉사 방전\n" +
            "  [ 120.0s] 연쇄 간파 시작 — 3명 · 4회\n" +
            "  [ 125.0s] 연쇄 간파 1회 성공\n" +
            "  [ 130.0s] 연쇄 간파 결과 3/4 → Partial\n" +
            "  [ 140.0s] 간파 릴레이 시작 — 6시 집결\n" +
            "  [ 150.0s] 간파 릴레이 종료 — 보스 사망\n" +
            "  [ 160.0s] ④ 폐기 처분 저지(남은 2.0s)\n" +
            "  [ 165.0s] ② 전력 공급 실패 → 방전\n" +
            "  [ 170.0s] 간파 릴레이 시작 — 6시 집결\n";
        List<QABoss103GimmickEvent> g = QABoss103FightLog.Parse(log, out int sup);
        Assert.AreEqual(1, sup);
        var got = new List<string>();
        foreach (QABoss103GimmickEvent e in g)
            got.Add($"{e.Gimmick}|{e.Result}|{e.FightT:0.0}");
        CollectionAssert.AreEqual(new[]
        {
            "송전기|success|30.5",
            "북진 1회차 간파|success|95.0",
            "북진 1회차|fail|100.0",
            "연쇄 간파 1회|success|125.0",
            "연쇄 간파|partial|130.0",
            "간파 릴레이|ended|150.0",
            "자폭 저지|success|160.0",
            "전력 공급|fail|165.0",
            "간파 릴레이|timeout|170.0",
        }, got);
        Assert.AreEqual("3/4", g[4].Note);
        Assert.AreEqual("보스 사망", g[5].Note);
        Assert.AreEqual("log", g[0].Source);
    }

    [Test]
    public void 기믹_행_직렬화()
    {
        QABoss103RunStats s = Scenario();
        s.Gimmicks.Add(new QABoss103GimmickEvent { Gimmick = "송전기", Result = "success", FightT = 30.5, ServerT = 130.5 });
        List<string[]> rows = QABoss103CsvRows.Gimmicks(Meta(), s);
        string[] h = QABoss103Metrics.GimmicksHeader;
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual("송전기", Col(h, rows[0], "gimmick"));
        Assert.AreEqual("30.5", Col(h, rows[0], "fight_t"));
        Assert.AreEqual("130.500", Col(h, rows[0], "serverT"));
        Assert.AreEqual("log", Col(h, rows[0], "source"));
    }

    // ── 사망 귀속·피격 묶음·목숨 ──────────────────────────────

    [Test]
    public void 사망은_3초_안_마지막_피격_패턴_아니면_unknown()
    {
        var s = new QABoss103RunStats();
        s.Begin(0);
        s.PlayerHit(Pal, 10.0, "Jump");
        s.PlayerHit(Pal, 11.0, "drone");
        s.Death(Pal, 13.5);
        s.PlayerHit(Gun, 10.0, "Upper");
        s.Death(Gun, 13.5);
        Assert.AreEqual("drone", s.Deaths[0].Pattern);
        Assert.AreEqual(QABoss103Metrics.Unknown, s.Deaths[1].Pattern);
    }

    [Test]
    public void 피격은_같은_패턴_0_3초_안이면_1건()
    {
        var s = new QABoss103RunStats();
        s.Begin(0);
        s.PlayerHit(Pal, 1.0, "floor");
        s.PlayerHit(Pal, 1.2, "floor");
        s.PlayerHit(Pal, 1.4, "floor");   // 이어지는 틱 — 마지막 시각 기준
        s.PlayerHit(Pal, 1.45, "Upper");
        s.PlayerHit(Pal, 2.0, "floor");
        s.PlayerHit(Asn, 1.0, "floor");
        Assert.AreEqual(3, s.Chars[Pal].HitsTaken);
        Assert.AreEqual(1, s.Chars[Asn].HitsTaken);
    }

    [Test]
    public void 목숨은_줄어든_양만_소모()
    {
        var s = new QABoss103RunStats();
        s.Lives(3);
        s.Lives(1);
        s.Lives(3);   // 전멸 리스폰 리필
        s.Lives(2);
        Assert.AreEqual(3, s.LivesUsed);
    }

    [Test]
    public void Combat_전에_끝난_판은_runs_한_줄만()
    {
        var meta = Meta(QABoss103Outcome.Aborted);
        meta.Detail = "맵 진입 실패";
        meta.CombatSeconds = -1;
        string[] run = QABoss103CsvRows.Run(meta, null);
        string[] h = QABoss103Metrics.RunsHeader;
        Assert.AreEqual("Aborted", Col(h, run, "outcome"));
        Assert.AreEqual("맵 진입 실패", Col(h, run, "error_reason"));
        Assert.AreEqual("", Col(h, run, "combat_sec"));
        Assert.AreEqual("", Col(h, run, "threat_rolls"));
        Assert.AreEqual(0, QABoss103CsvRows.Chars(meta, null).Count);
        Assert.AreEqual(0, QABoss103CsvRows.Patterns(meta, new QABoss103RunStats()).Count);
    }

    // ── 출처 분류·굴림 패턴 ───────────────────────────────────

    [Test]
    public void 피격_출처_분류는_안쪽_프레임부터()
    {
        Assert.AreEqual("floor", QABoss103Metrics.ClassifySource(new[] { "Unit", "Player", "BossPatternTargets", "BossElectricFloor", "TwentyThreeBoss" }, "Jump"));
        Assert.AreEqual("Upper", QABoss103Metrics.ClassifySource(new[] { "Unit", "Hurtbox", "TwentyThreeBoss+<HitRoutine>d__12" }, "Upper"));
        Assert.AreEqual("Grab", QABoss103Metrics.ClassifySource(new[] { "Unit", "GrabController", "TwentyThreeBoss" }, "Dash"));
        Assert.AreEqual("drone", QABoss103Metrics.ClassifySource(new[] { "WellsDroneAttack+<>c" }, null));
        Assert.AreEqual("bomb", QABoss103Metrics.ClassifySource(new[] { "BossBomb" }, null));
        Assert.AreEqual("charge_ball", QABoss103Metrics.ClassifySource(new[] { "AreaZone" }, null));
        Assert.AreEqual(QABoss103Metrics.Unknown, QABoss103Metrics.ClassifySource(new[] { "TwentyThreeBoss" }, null));
        Assert.AreEqual(QABoss103Metrics.Unknown, QABoss103Metrics.ClassifySource(new[] { "Unit", "Player" }, "Upper"));
        Assert.AreEqual(QABoss103Metrics.Unknown, QABoss103Metrics.ClassifySource(null, "Upper"));
    }

    [Test]
    public void 굴림_패턴_장판_드론_공격id_모양_시간표()
    {
        var starts = new List<KeyValuePair<double, string>>
        {
            new KeyValuePair<double, string>(10.0, "LeftHook"),
            new KeyValuePair<double, string>(20.0, "Upper"),
        };
        Assert.AreEqual("floor", QABoss103Metrics.PatternOfRoll("floor", "Upper", 20.0, starts));
        Assert.AreEqual("RightHook", QABoss103Metrics.PatternOfRoll("cone", "RightHook", 20.0, starts), "호스트 봇은 공격 id 를 안다");
        Assert.AreEqual("Upper", QABoss103Metrics.PatternOfRoll("cone", "na", 20.1, starts));
        Assert.AreEqual("Upper", QABoss103Metrics.PatternOfRoll("cone", "na", 19.9, starts), "시작보다 조금 앞선 감지(시계 오차)");
        Assert.AreEqual("LeftHook", QABoss103Metrics.PatternOfRoll("band", "na", 15.0, starts));
        Assert.AreEqual(QABoss103Metrics.Unknown, QABoss103Metrics.PatternOfRoll("cone", "na", 30.0, starts), "6초 넘게 지난 시작");
        Assert.AreEqual("Jump", QABoss103Metrics.PatternOfRoll("jump", "na", 20.1, starts));
        Assert.AreEqual(QABoss103Metrics.Unknown, QABoss103Metrics.PatternOfRoll("generic", "na", 5.0, starts));
    }

    // ── CSV ──────────────────────────────────────────────────

    [Test]
    public void CSV_이스케이프()
    {
        Assert.AreEqual("", QABoss103Csv.Escape(null));
        Assert.AreEqual("한글", QABoss103Csv.Escape("한글"));
        Assert.AreEqual("\"a,b\"", QABoss103Csv.Escape("a,b"));
        Assert.AreEqual("\"say \"\"hi\"\"\"", QABoss103Csv.Escape("say \"hi\""));
        Assert.AreEqual("\"a\nb\"", QABoss103Csv.Escape("a\nb"));
        Assert.AreEqual("\" lead\"", QABoss103Csv.Escape(" lead"));
        Assert.AreEqual("a,\"b,c\",", QABoss103Csv.Line(new[] { "a", "b,c", "" }));
    }

    [Test]
    public void CSV_append_는_BOM_과_헤더를_한_번만()
    {
        string dir = Path.Combine(Path.GetTempPath(), "qa103-csv-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "runs.csv");
        try
        {
            QABoss103Csv.Append(path, new[] { "run", "이름" }, new[] { new[] { "1", "가,나" } });
            QABoss103Csv.Append(path, new[] { "run", "이름" }, new[] { new[] { "2", "다" } });
            byte[] bytes = File.ReadAllBytes(path);
            Assert.AreEqual(0xEF, bytes[0]);
            Assert.AreEqual(0xBB, bytes[1]);
            Assert.AreEqual(0xBF, bytes[2]);
            string text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            Assert.AreEqual("run,이름\r\n1,\"가,나\"\r\n2,다\r\n", text);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Test]
    public void 헤더_첫_열은_run_retry_tier_seed()
    {
        foreach (string[] h in new[] { QABoss103Metrics.RunsHeader, QABoss103Metrics.CharsHeader, QABoss103Metrics.PatternsHeader, QABoss103Metrics.GimmicksHeader })
            CollectionAssert.AreEqual(new[] { "run", "retry", "tier", "seed" }, new[] { h[0], h[1], h[2], h[3] });
        Assert.AreEqual(QABoss103Metrics.CharsHeader.Length, new HashSet<string>(QABoss103Metrics.CharsHeader).Count, "열 이름 중복 없음");
    }

    // ── 판 파일·설정 ─────────────────────────────────────────

    [Test]
    public void 판파일_줄_형식_왕복()
    {
        string line = QABoss103PartFile.Format("threat", "slot", "1", "note", "dir=0.5/1 state=Idle");
        Assert.IsTrue(QABoss103PartFile.TryParse(line, out string kind, out Dictionary<string, string> kv));
        Assert.AreEqual("threat", kind);
        Assert.AreEqual("1", kv["slot"]);
        Assert.AreEqual("dir_0.5/1_state_Idle", kv["note"], "값의 공백·'=' 은 토큰화");
        Assert.IsFalse(QABoss103PartFile.TryParse("   ", out _, out _));
        StringAssert.EndsWith("run3_r1_s2.txt", QABoss103PartFile.PartPath("C:/QA/103-x", 3, 1, 2));
        StringAssert.EndsWith("run3_r0_end.txt", QABoss103PartFile.EndPath("C:/QA/103-x", 3, 0));
    }

    [Test]
    public void 판파일_쓰기_읽기_done()
    {
        string dir = Path.Combine(Path.GetTempPath(), "qa103-part-" + Guid.NewGuid().ToString("N"));
        string path = QABoss103PartFile.PartPath(dir, 1, 0, 1);
        try
        {
            var w = new QABoss103PartWriter(path);
            w.Write("counter slot=1 cast=true");
            List<string> mid = QABoss103PartFile.ReadShared(path);
            Assert.AreEqual(1, mid.Count, "쓰는 중에도 읽힌다(줄마다 flush)");
            w.Close(1, "destroy");
            w.Close(1, "again");
            List<string> lines = QABoss103PartFile.ReadShared(path);
            Assert.AreEqual(2, lines.Count);
            StringAssert.StartsWith("done slot=1", lines[1]);
            Assert.IsNull(QABoss103PartFile.ReadShared(Path.Combine(dir, "none.txt")));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Test]
    public void 설정_출력폴더_재실행_왕복()
    {
        var cfg = new QABoss103Config { Seed = 5, Run = 4, OutDir = "C:/UnityProject/QA/103-배치 1", Retry = 1 };
        Assert.IsTrue(QABoss103Config.TryParse(cfg.Format(), out QABoss103Config back));
        Assert.AreEqual("C:/UnityProject/QA/103-배치 1", back.OutDir);
        Assert.AreEqual(1, back.Retry);
        Assert.IsTrue(back.HasOut);
        Assert.IsTrue(QABoss103Config.TryParse("seed=1\n", out QABoss103Config none));
        Assert.IsFalse(none.HasOut);
        Assert.AreEqual(0, none.Retry);
    }
}
#endif
