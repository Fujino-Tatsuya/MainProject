#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 119 QA 봇 2차 — 즉사(굴림 없음) 판별 · 벽 끼임 감시 · F10 부활 요청 시각 · 클리어 판정 경로(신호 래치 → 판정 → runs.csv) · 새 CSV 열.
/// </summary>
public sealed class QABoss103Round2Tests
{
    private const int Pal = 0, Asn = 1, Gun = 2;

    private static readonly string[] BossAssets =
    {
        "Assets/2.Prefabs/Monster/Data/No23.asset", "Assets/2.Prefabs/Monster/Data/No23_A.asset", "Assets/2.Prefabs/Monster/Data/No23_B.asset",
        "Assets/2.Prefabs/Monster/Data/No23_Raid.asset", "Assets/2.Prefabs/Monster/Data/No23_Solo.asset",
    };

    private static QABoss103Lethal.Data Defaults(float grace = 1.5f) =>
        QABoss103Lethal.From(12f, 10f, grace, 1, true, 1, 1.2f);

    // ── 1. 즉사 판별 ─────────────────────────────────────────

    [Test]
    public void 북진_압박_1회차_2회차_예고는_즉사()
    {
        QABoss103Lethal.Data d = Defaults();
        ulong safeCut = BossTileGrid.AllMask & ~0b111UL;
        Assert.AreEqual(QABoss103LethalKind.PushNorth, QABoss103Lethal.ClassifyFloor(safeCut, 13.5f, BossTileGrid.AllMask, d), "1회차 = 12 + 상승 1.5, 안전 구역 제외 칸");
        Assert.AreEqual(QABoss103LethalKind.PushNorth, QABoss103Lethal.ClassifyFloor(BossTileGrid.AllMask, 11.5f, BossTileGrid.AllMask, d), "2회차~ = 10 + 1.5, 방 전체");
        Assert.AreEqual(QABoss103LethalKind.PushNorth, QABoss103Lethal.ClassifyFloor(BossTileGrid.AllMask, 11.53f, BossTileGrid.AllMask, d), "float 오차 안");
    }

    [Test]
    public void 폐기_처분_대폭발은_방_전체일_때만_즉사()
    {
        QABoss103Lethal.Data d = Defaults();
        Assert.AreEqual(QABoss103LethalKind.SelfDestruct, QABoss103Lethal.ClassifyFloor(BossTileGrid.AllMask, 1.2f, BossTileGrid.AllMask, d));
        Assert.AreEqual(QABoss103LethalKind.None, QABoss103Lethal.ClassifyFloor(BossTileGrid.AllMask & ~1UL, 1.2f, BossTileGrid.AllMask, d), "일부 칸이면 대폭발이 아니다");
    }

    [Test]
    public void 무적_무시_칸이_0이면_즉사로_보지_않는다_대시로_피할_수_있다()
    {
        QABoss103Lethal.Data off = QABoss103Lethal.From(12f, 10f, 1.5f, 0, true, 0, 1.2f);
        Assert.AreEqual(QABoss103LethalKind.None, QABoss103Lethal.ClassifyFloor(BossTileGrid.AllMask, 11.5f, BossTileGrid.AllMask, off));
        Assert.AreEqual(QABoss103LethalKind.None, QABoss103Lethal.ClassifyFloor(BossTileGrid.AllMask, 1.2f, BossTileGrid.AllMask, off));
        QABoss103Lethal.Data notLethal = QABoss103Lethal.From(12f, 10f, 1.5f, 1, false, 1, 1.2f);
        Assert.AreEqual(QABoss103LethalKind.None, QABoss103Lethal.ClassifyFloor(BossTileGrid.AllMask, 1.2f, BossTileGrid.AllMask, notLethal), "대폭발이 즉사기가 아니면 일반 피해");
        Assert.AreEqual(QABoss103LethalKind.None, QABoss103Lethal.ClassifyFloor(0UL, 11.5f, BossTileGrid.AllMask, Defaults()), "빈 마스크");
    }

    [Test]
    public void 서버_예고_하한_0_3초를_맞춘다()
    {
        QABoss103Lethal.Data d = QABoss103Lethal.From(12f, 10f, 1.5f, 1, true, 1, 0.1f);
        Assert.AreEqual(QABoss103LethalKind.SelfDestruct, QABoss103Lethal.ClassifyFloor(BossTileGrid.AllMask, 0.3f, BossTileGrid.AllMask, d));
    }

    /// <summary>실제 보스 데이터 5종에서 즉사가 아닌 방전(② 실패·집결·측면·연쇄 실패)의 예고가 즉사로 잘못 판별되지 않는다.</summary>
    [Test]
    public void 실제_보스_데이터에서_다른_방전과_예고가_겹치지_않는다()
    {
        int checkedAssets = 0;
        foreach (string path in BossAssets)
        {
            BossDataSO so = AssetDatabase.LoadAssetAtPath<BossDataSO>(path);
            if (so == null)
                continue;
            checkedAssets++;
            QABoss103Lethal.Data d = QABoss103Lethal.From(so.pushNorthFirstChargeTime, so.pushNorthChargeTime, 1.5f, so.pushNorthDischargeIgnoreInvuln,
                                                         so.raidSelfDestructBlastLethal, so.raidSelfDestructBlastIgnoreInvuln, so.raidSelfDestructBlastWarn);
            float[] others = { so.raidDischargeWarnTime, so.relayGatherWarn, so.relaySideWarn, so.chainFailWarn };
            foreach (float w in others)
                Assert.AreEqual(QABoss103LethalKind.None,
                                QABoss103Lethal.ClassifyFloor(BossTileGrid.AllMask, Mathf.Max(0.3f, w), BossTileGrid.AllMask, d), $"{path} 예고 {w}s");
            if (so.pushNorthDischargeIgnoreInvuln != 0)
                Assert.AreEqual(QABoss103LethalKind.PushNorth, QABoss103Lethal.ClassifyFloor(BossTileGrid.AllMask, so.pushNorthChargeTime + 1.5f, BossTileGrid.AllMask, d), path);
        }
        Assert.Greater(checkedAssets, 0, "보스 데이터 에셋을 하나도 못 읽었다");
    }

    [Test]
    public void 토큰()
    {
        Assert.AreEqual("push_north", QABoss103Lethal.Token(QABoss103LethalKind.PushNorth));
        Assert.AreEqual("self_destruct", QABoss103Lethal.Token(QABoss103LethalKind.SelfDestruct));
        Assert.AreEqual("none", QABoss103Lethal.Token(QABoss103LethalKind.None));
    }

    // ── 2. 벽 끼임 ───────────────────────────────────────────

    [Test]
    public void 벽_끼임은_0_25초_이어지면_1회_요청하고_2초_쿨다운()
    {
        var w = new QABoss103WallWatch();
        Assert.IsFalse(w.Tick(10.0, true));
        Assert.IsTrue(w.Busy(10.0), "보는 중이면 기존 끼임 단계를 멈춘다");
        Assert.IsFalse(w.Tick(10.2, true));
        Assert.IsTrue(w.Tick(10.25, true));
        Assert.AreEqual(1, w.Escapes);
        Assert.IsFalse(w.Tick(11.0, true), "쿨다운 안");
        Assert.IsFalse(w.Tick(11.5, false));
        Assert.IsTrue(w.Busy(11.5), "탈출 직후 2초는 기존 단계 멈춤");
        Assert.IsFalse(w.Busy(12.3));
        Assert.IsFalse(w.Tick(12.3, true));
        Assert.IsTrue(w.Tick(12.6, true), "쿨다운 뒤 다시 0.25초");
        Assert.AreEqual(2, w.Escapes);
    }

    [Test]
    public void 벽_스침은_요청하지_않는다()
    {
        var w = new QABoss103WallWatch();
        for (int i = 0; i < 10; i++)
        {
            Assert.IsFalse(w.Tick(10.0 + i * 0.3, true));
            Assert.IsFalse(w.Tick(10.1 + i * 0.3, false), "대시가 벽을 한두 프레임 스침");
        }
        Assert.AreEqual(0, w.Escapes);
    }

    [Test]
    public void 보스방_경계_밖_판정()
    {
        Assert.IsFalse(QABoss103WallWatch.OutsideArena(0f, 0f, -13f, 13f, -13f, 13f, 0.1f));
        Assert.IsFalse(QABoss103WallWatch.OutsideArena(13.05f, 0f, -13f, 13f, -13f, 13f, 0.1f), "면에 붙은 몸은 봐준다");
        Assert.IsTrue(QABoss103WallWatch.OutsideArena(0f, -13.2f, -13f, 13f, -13f, 13f, 0.1f), "Boundary_ZMin 너머");
        Assert.IsTrue(QABoss103WallWatch.OutsideArena(14f, 0f, -13f, 13f, -13f, 13f, 0.1f));
    }

    [Test]
    public void 끼임_감지기_Reset_은_옆걸음도_멈춘다()
    {
        var s = new QABoss103StuckDetector();
        s.Tick(0.0, 0f, 0f, true);
        Assert.IsTrue(s.Tick(2.1, 0.05f, 0f, true));
        Assert.IsTrue(s.IsSidestepping(2.2));
        s.Reset();
        Assert.IsFalse(s.IsSidestepping(2.2));
        Assert.IsFalse(s.Tick(2.3, 0f, 0f, true), "처음부터 다시 잰다");
    }

    // ── 3. F10 부활 ──────────────────────────────────────────

    [Test]
    public void F10_부활은_Soul_3초_뒤_요청하고_아직_Soul_이면_2초마다()
    {
        var r = new QABoss103ReviveTimer(MapSceneDeathRulesModel.DefaultReviveDelaySeconds);
        Assert.AreEqual(3.0, r.DelaySeconds, "기존 QA 자동 부활과 같은 지연");
        Assert.IsFalse(r.Tick(10.0, true, 3));
        Assert.IsFalse(r.Tick(12.9, true, 3));
        Assert.IsTrue(r.Tick(13.0, true, 3));
        Assert.IsFalse(r.Tick(14.0, true, 2));
        Assert.IsTrue(r.Tick(15.0, true, 2), "RPC 가 처리 안 됐으면 재요청 — 서버가 성공할 때만 목숨이 준다");
        Assert.AreEqual(2, r.Requests);
    }

    [Test]
    public void 공용_목숨_0이면_요청하지_않는다()
    {
        var r = new QABoss103ReviveTimer(3.0);
        Assert.IsFalse(r.Tick(0.0, true, 0));
        Assert.IsFalse(r.Tick(10.0, true, 0));
        Assert.AreEqual(0, r.Requests);
        Assert.IsTrue(r.Tick(10.0, true, -1), "목숨을 모르면 보낸다(게임이 판정)");
    }

    [Test]
    public void Soul_을_벗어나면_지연을_처음부터()
    {
        var r = new QABoss103ReviveTimer(3.0);
        r.Tick(0.0, true, 3);
        Assert.IsFalse(r.Tick(1.0, false, 3));
        Assert.IsFalse(r.Tick(2.0, true, 3));
        Assert.IsFalse(r.Tick(4.9, true, 3));
        Assert.IsTrue(r.Tick(5.0, true, 3));
    }

    [Test]
    public void F10_RPC_는_게임에_그대로_있다()
    {
        MethodInfo m = typeof(PlayerReviveController).GetMethod("RequestDebugReviveRpc", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, Type.EmptyTypes, null);
        Assert.IsNotNull(m, "봇이 리플렉션으로 부르는 F10 경로 — 이름이 바뀌면 봇 부활이 멈춘다");
        Assert.IsNotNull(typeof(PlayerReviveController).GetEvent("ServerReviveCompleted"), "호스트 부활 집계 구독");
    }

    [Test]
    public void 부활_집계_qa_revives_와_목숨_불일치_note()
    {
        var s = new QABoss103RunStats();
        s.Begin(0.0);
        s.QaRevive(Pal, 1);
        s.QaRevive(Pal, 1);
        s.QaRevive(Gun, -1);
        s.QaRevive(9, 1);   // 자리 밖 무시
        s.End(60.0);
        var meta = new QABoss103RunMeta { Outcome = QABoss103Outcome.Wipe, CombatSeconds = 60.0 };
        List<string[]> rows = QABoss103CsvRows.Chars(meta, s);
        int col = Array.IndexOf(QABoss103Metrics.CharsHeader, "qa_revives");
        Assert.AreEqual("2", rows[Pal][col]);
        Assert.AreEqual("0", rows[Asn][col]);
        Assert.AreEqual("1", rows[Gun][col]);
        Assert.AreEqual(3, s.QaRevivesTotal());

        int note = Array.IndexOf(QABoss103Metrics.RunsHeader, "note");
        StringAssert.DoesNotContain(QABoss103RunStats.ReviveLifeMismatchNote, QABoss103CsvRows.Run(meta, s)[note]);
        s.QaRevive(Asn, 0);   // 목숨을 안 깎은 부활
        StringAssert.Contains(QABoss103RunStats.ReviveLifeMismatchNote, QABoss103CsvRows.Run(meta, s)[note]);
    }

    // ── 4. 클리어 판정 경로 ──────────────────────────────────

    [Test]
    public void 보스_Died_신호면_클리어_출처_boss_died()
    {
        var sig = new QABoss103EndSignals();
        sig.Poll(false, false);
        Assert.AreEqual(QABoss103Outcome.None, sig.Judge(100.0));
        sig.OnBossDied();
        Assert.AreEqual(QABoss103Outcome.Clear, sig.Judge(100.0));
        Assert.AreEqual("boss_died", sig.Source(QABoss103Outcome.Clear));
        sig.Poll(true, false);
        Assert.AreEqual("boss_died+director_defeated", sig.Source(QABoss103Outcome.Clear));
    }

    [Test]
    public void 보스_사망_뒤_결과_화면으로_맵을_떠나도_클리어_유지()
    {
        var sig = new QABoss103EndSignals();
        sig.OnBossDied();
        sig.Poll(false, true);   // 결과 화면 전환 — 디렉터 사라짐(=null)·씬 바뀜
        Assert.AreEqual(QABoss103Outcome.Clear, sig.Judge(300.0), "예전 폴링은 디렉터가 없어지면 '맵 이탈 = 전멸'로 셀 수 있었다");
    }

    [Test]
    public void 디렉터_클리어_확정만_있어도_클리어이고_꺼져도_유지()
    {
        var sig = new QABoss103EndSignals();
        sig.Poll(true, false);
        sig.Poll(false, true);
        Assert.AreEqual(QABoss103Outcome.Clear, sig.Judge(10.0));
        Assert.AreEqual("director_defeated", sig.Source(QABoss103Outcome.Clear));
    }

    [Test]
    public void 전멸과_시간초과_출처()
    {
        var wipe = new QABoss103EndSignals();
        wipe.OnWipe();
        Assert.AreEqual(QABoss103Outcome.Wipe, wipe.Judge(10.0));
        Assert.AreEqual("party_wipe_respawned", wipe.Source(QABoss103Outcome.Wipe));

        var left = new QABoss103EndSignals();
        left.Poll(false, true);
        Assert.AreEqual(QABoss103Outcome.Wipe, left.Judge(10.0));
        Assert.AreEqual("left_map", left.Source(QABoss103Outcome.Wipe));

        var both = new QABoss103EndSignals();
        both.OnWipe();
        both.OnBossDied();
        Assert.AreEqual(QABoss103Outcome.Clear, both.Judge(10.0), "같은 틱이면 클리어 우선");

        var slow = new QABoss103EndSignals();
        Assert.AreEqual(QABoss103Outcome.Timeout, slow.Judge(QABoss103Plan.CombatLimitSeconds));
        Assert.AreEqual("combat_limit", slow.Source(QABoss103Outcome.Timeout));
    }

    /// <summary>
    /// Match 와 같은 구독(<c>bossUnit.Died += signals.OnBossDied</c>) — 게임 <see cref="Unit.Died"/> 이벤트 형식에 래치 처리기를 그대로 붙여 부르면 클리어.
    /// (NetworkBehaviour 를 EditMode 에서 만들지 않는다 — 이벤트 형식·처리기 바인딩만 실제 타입으로 확인.) 23호가 Unit 을 상속하는지도 본다.
    /// </summary>
    [Test]
    public void 게임_Unit_Died_이벤트_형식으로_클리어()
    {
        EventInfo died = typeof(Unit).GetEvent("Died");
        Assert.IsNotNull(died, "Unit.Died");
        Assert.AreEqual(typeof(Action), died.EventHandlerType);
        Assert.IsTrue(typeof(Unit).IsAssignableFrom(typeof(TwentyThreeBoss)), "Match 가 23호를 Unit 으로 구독한다");
        var sig = new QABoss103EndSignals();
        Delegate handler = Delegate.CreateDelegate(died.EventHandlerType, sig, nameof(QABoss103EndSignals.OnBossDied));
        handler.DynamicInvoke();
        Assert.IsTrue(sig.BossDied);
        Assert.AreEqual(QABoss103Outcome.Clear, sig.Judge(42.0));
        Assert.AreEqual("boss_died", sig.Source(QABoss103Outcome.Clear));
    }

    [Test]
    public void 클리어_판_runs_csv_행_outcome_source_와_보스_HP_0()
    {
        var sig = new QABoss103EndSignals();
        sig.OnBossDied();
        sig.Poll(true, false);
        QABoss103Outcome outcome = sig.Judge(321.0);
        var rec = new QABoss103RunRecord { Run = 4, BaseSeed = 103, RunSeed = 9, Outcome = outcome, CombatSeconds = 321.0, OutcomeSource = sig.Source(outcome), Detail = "보스 처치" };
        StringAssert.Contains("outcome=Clear", rec.ToLogLine());
        StringAssert.Contains("source=boss_died+director_defeated", rec.ToLogLine());

        var s = new QABoss103RunStats();
        s.Begin(0.0);
        s.End(321.0);
        var meta = new QABoss103RunMeta { Run = rec.Run, Outcome = rec.Outcome, CombatSeconds = rec.CombatSeconds, OutcomeSource = rec.OutcomeSource, BossHpEndPct = 0.0, Note = rec.Detail };
        string[] row = QABoss103CsvRows.Run(meta, s);
        string[] h = QABoss103Metrics.RunsHeader;
        Assert.AreEqual(h.Length, row.Length);
        Assert.AreEqual("Clear", row[Array.IndexOf(h, "outcome")]);
        Assert.AreEqual("boss_died+director_defeated", row[Array.IndexOf(h, "outcome_source")]);
        Assert.AreEqual("0.0", row[Array.IndexOf(h, "boss_hp_end_pct")]);
        Assert.AreEqual("", row[Array.IndexOf(h, "error_reason")], "최종 결과는 오류 사유 칸을 비운다");
        Assert.AreEqual("outcome_source", h[h.Length - 1], "끝에 붙인 열(배치 중간 머리 확장)");
    }

    // ── 5. 즉사·벽 집계 ──────────────────────────────────────

    [Test]
    public void 즉사_판_파일은_굴림이_아니라_lethal_no_roll_로_세고_굴림_없는_피격에서_뺀다()
    {
        var s = new QABoss103RunStats();
        s.Begin(100.0);
        s.PlayerHit(Pal, 112.0, "floor");   // 북진 방전에 맞음(굴림 없음)
        s.PlayerHit(Asn, 150.0, "floor");   // 굴림도 즉사도 없는 장판 피격
        s.QaWallEscape(Gun);
        s.QaWallEscape(Gun);
        s.End(200.0);
        s.AddPart(Pal, new[]
        {
            "lethal slot=0 pattern=floor attack=na id=floor#3 serverT=100.500 lethal=push_north hitMs=11500",
            "done slot=0 reason=host_end",
        });
        s.AddPart(Asn, new[] { "done slot=1 reason=destroy" });
        s.Finish();

        Assert.AreEqual(0, s.Rolls.Count, "즉사는 굴림이 아니다");
        var meta = new QABoss103RunMeta { Outcome = QABoss103Outcome.Wipe, CombatSeconds = 100.0 };
        string[] h = QABoss103Metrics.CharsHeader;
        List<string[]> rows = QABoss103CsvRows.Chars(meta, s);
        Assert.AreEqual(h.Length, rows[Pal].Length);
        Assert.AreEqual("1", rows[Pal][Array.IndexOf(h, "lethal_no_roll")]);
        Assert.AreEqual("0", rows[Asn][Array.IndexOf(h, "lethal_no_roll")]);
        Assert.AreEqual("", rows[Gun][Array.IndexOf(h, "lethal_no_roll")], "판 파일 없음");
        Assert.AreEqual("0", rows[Pal][Array.IndexOf(h, "hits_unrolled")], "즉사 위험에 맞은 것은 감지 못한 위험이 아니다");
        Assert.AreEqual("1", rows[Asn][Array.IndexOf(h, "hits_unrolled")]);
        Assert.AreEqual("2", rows[Gun][Array.IndexOf(h, "qa_wall_escapes")]);
        Assert.AreEqual("0", rows[Pal][Array.IndexOf(h, "qa_wall_escapes")]);

        string[] run = QABoss103CsvRows.Run(meta, s);
        Assert.AreEqual("0", run[Array.IndexOf(QABoss103Metrics.RunsHeader, "threat_rolls")]);

        List<string[]> pats = QABoss103CsvRows.Patterns(meta, s);
        string[] ph = QABoss103Metrics.PatternsHeader;
        string[] floor = pats.Find(r => r[Array.IndexOf(ph, "pattern")] == "floor");
        Assert.IsNotNull(floor);
        Assert.AreEqual(ph.Length, floor.Length);
        Assert.AreEqual("1", floor[Array.IndexOf(ph, "lethal_no_roll")]);
        Assert.AreEqual("0", floor[Array.IndexOf(ph, "threats")]);
        Assert.AreEqual("2", floor[Array.IndexOf(ph, "actual_hits")]);
    }

    [Test]
    public void 새_열은_모두_끝에_붙는다()
    {
        string[] c = QABoss103Metrics.CharsHeader;
        int rules = QABoss103CharRules.Count;
        Assert.AreEqual("qa_wall_escapes", c[c.Length - rules - 2]);
        Assert.AreEqual("lethal_no_roll", c[c.Length - rules - 1]);
        Assert.AreEqual("counter_roll_success", c[c.Length - rules - 3], "C7e 끝 열 뒤");
        for (int i = 0; i < rules; i++)
            Assert.AreEqual(QABoss103CharRules.Tokens[i], c[c.Length - rules + i], "C2 규칙 열이 맨 끝");
        string[] p = QABoss103Metrics.PatternsHeader;
        Assert.AreEqual("lethal_no_roll", p[p.Length - 1]);
        Assert.AreEqual("deaths", p[p.Length - 2]);
        string[] r = QABoss103Metrics.RunsHeader;
        Assert.AreEqual("note", r[r.Length - 2]);
    }

    // ── C2 툴팁 규칙 ─────────────────────────────────────────

    [Test]
    public void 규칙_발동_줄은_그_캐릭터_열에만_센다()
    {
        var s = new QABoss103RunStats();
        s.Begin(0.0);
        s.End(60.0);
        s.AddPart(Gun, new[]
        {
            "rule slot=2 rule=gunner_overheat",
            "rule slot=2 rule=gunner_overheat_e",
            "rule slot=2 rule=gunner_overheat",
            "rule slot=2 rule=unknown_rule",
            "done slot=2",
        });
        s.AddPart(Asn, new[] { "rule slot=1 rule=assassin_enhance_e", "rule slot=1 rule=assassin_rage_wait", "done slot=1" });
        var meta = new QABoss103RunMeta { Outcome = QABoss103Outcome.Wipe, CombatSeconds = 60.0 };
        string[] h = QABoss103Metrics.CharsHeader;
        List<string[]> rows = QABoss103CsvRows.Chars(meta, s);
        Assert.AreEqual(h.Length, rows[Gun].Length);
        Assert.AreEqual("2", rows[Gun][Array.IndexOf(h, "gunner_overheat")]);
        Assert.AreEqual("1", rows[Gun][Array.IndexOf(h, "gunner_overheat_e")]);
        Assert.AreEqual("", rows[Gun][Array.IndexOf(h, "assassin_enhance_e")], "다른 캐릭터 규칙 열은 빈칸");
        Assert.AreEqual("1", rows[Asn][Array.IndexOf(h, "assassin_enhance_e")]);
        Assert.AreEqual("1", rows[Asn][Array.IndexOf(h, "assassin_rage_wait")]);
        Assert.AreEqual("0", rows[Asn][Array.IndexOf(h, "assassin_blade_storm_e")]);
        Assert.AreEqual("", rows[Asn][Array.IndexOf(h, "gunner_overheat_e")]);
        Assert.AreEqual("", rows[Pal][Array.IndexOf(h, "assassin_enhance_e")], "판 파일 없음");
    }

    [Test]
    public void 규칙_표는_열마다_한_줄이고_주인이_맞다()
    {
        Assert.AreEqual(Enum.GetValues(typeof(QABoss103CharRule)).Length, QABoss103CharRules.Count);
        Assert.AreEqual("gunner_overheat_e", QABoss103CharRules.Token(QABoss103CharRule.GunnerOverheatE));
        foreach (QABoss103CharRule rule in Enum.GetValues(typeof(QABoss103CharRule)))
        {
            string token = QABoss103CharRules.Token(rule);
            int n = 0;
            foreach (QABoss103CharRuleRow row in QABoss103CharRules.Table)
                if (row.Column == token)
                {
                    n++;
                    Assert.AreEqual(QABoss103CharRules.Owner(rule), row.Character, token);
                    Assert.AreEqual("신규", row.Status, token);
                }
            Assert.AreEqual(1, n, token + " 표 줄");
        }
        foreach (QABoss103CharRuleRow row in QABoss103CharRules.Table)
            StringAssert.EndsWith("/tooltip.description", row.TooltipKey, "근거 = 툴팁 설명 키");
    }

    [Test]
    public void 규칙_근거_툴팁_키는_StringTable_에_있다()
    {
        string text = System.IO.File.ReadAllText("Assets/9.ScriptableObject/Localization/StringTable.asset");
        foreach (QABoss103CharRuleRow row in QABoss103CharRules.Table)
            StringAssert.Contains("key: " + row.TooltipKey, text, row.Skill);
    }

    [Test]
    public void 구간은_켜짐마다_한_번_세고_반응_지연_뒤에_반응한다()
    {
        var e = new QABoss103Episode();
        Assert.IsTrue(e.Tick(true, 10.0));
        Assert.IsFalse(e.Tick(true, 10.1), "켜진 채면 다시 세지 않는다");
        Assert.IsFalse(e.Reacted(10.1, 300.0));
        Assert.IsTrue(e.Reacted(10.3, 300.0));
        Assert.IsFalse(e.Tick(false, 11.0));
        Assert.IsFalse(e.Reacted(11.0, 0.0), "꺼지면 반응 없음");
        Assert.IsTrue(e.Tick(true, 12.0));
        Assert.AreEqual(2, e.Count);
        Assert.AreEqual(12.0, e.Since);
    }

    [Test]
    public void 리포트에_규칙_표와_발동_횟수가_나온다()
    {
        string html = QABoss103Report.Build(QABoss103ReportFake.Input());
        StringAssert.Contains("캐릭터 스킬 툴팁 규칙(할 일 119)", html);
        StringAssert.Contains("규칙 발동 횟수(판당 평균 · 합계)", html);
        foreach (string token in QABoss103CharRules.Tokens)
            StringAssert.Contains(token, html);
        StringAssert.Contains("과열을 해제한다", html);
    }
}
#endif
