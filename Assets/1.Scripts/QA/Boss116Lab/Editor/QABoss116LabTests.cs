#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

/// <summary>할 일 116-C4 — 대쉬 간파 예측 랩 순수 규칙(<see cref="QABoss116Lab"/>)과 격리(랩 씬·컴포넌트가 밖으로 새지 않음).</summary>
public sealed class QABoss116LabTests
{
    private static readonly string[] Key = { "1", "0", "Normal", "116" };

    private static Dictionary<string, string> Kv(params string[] p)
    {
        var d = new Dictionary<string, string>();
        for (int i = 0; i + 1 < p.Length; i += 2)
            d[p[i]] = p[i + 1];
        return d;
    }

    // ── 판 ───────────────────────────────────────────────────

    [Test]
    public void 판은_시간_상한과_시도_상한_중_먼저()
    {
        Assert.IsNull(QABoss116Lab.EndReason(10, 5, 180, 60));
        Assert.AreEqual("time", QABoss116Lab.EndReason(180, 5, 180, 60));
        Assert.AreEqual("attempts", QABoss116Lab.EndReason(20, 60, 180, 60));
        Assert.AreEqual("attempts", QABoss116Lab.EndReason(200, 60, 180, 60));
        Assert.IsNull(QABoss116Lab.EndReason(1000, 1000, 0, 0));
    }

    [Test]
    public void 설정_파일_왕복()
    {
        var cfg = new QABoss116LabConfig { RunSeconds = 120, MaxAttempts = 25 };
        cfg.Bot.Run = 4;
        cfg.Bot.OutDir = "C:/UnityProject/QA/116lab-x";
        string text = cfg.Format();
        StringAssert.DoesNotContain("zone_s", text, "116-C6 벽 구간 제거 — 구간 교대 줄을 쓰지 않는다");
        Assert.IsTrue(QABoss116LabConfig.TryParse(text + "zone_s=30\n", out QABoss116LabConfig back), "옛 zone_s 줄은 무시");
        Assert.AreEqual(4, back.Bot.Run);
        Assert.AreEqual(QABoss116Lab.DashInterruptPct, back.Bot.DashInterruptPct);
        Assert.AreEqual("C:/UnityProject/QA/116lab-x", back.Bot.OutDir);
        Assert.AreEqual(120, back.RunSeconds, 1e-9);
        Assert.AreEqual(25, back.MaxAttempts);
        Assert.IsFalse(QABoss116LabConfig.TryParse("", out _));
    }

    // ── 상세 원인 ─────────────────────────────────────────────

    [Test]
    public void 대시_게이트는_게임_검사_순서대로_첫_막힘()
    {
        Assert.AreEqual("dash_disabled", QABoss116Lab.DashGate(false, false, false, false, 0, "Idle"));
        Assert.AreEqual("not_input_source", QABoss116Lab.DashGate(true, false, false, false, 0, "Idle"));
        Assert.AreEqual("cannot_move_Hit", QABoss116Lab.DashGate(true, true, false, false, 0, "Hit"));
        Assert.AreEqual("airborne", QABoss116Lab.DashGate(true, true, true, false, 0, "Idle"));
        Assert.AreEqual("no_charge", QABoss116Lab.DashGate(true, true, true, true, 0, "Idle"));
        Assert.AreEqual("state_enter_rejected", QABoss116Lab.DashGate(true, true, true, true, 1, "Idle"));
    }

    [Test]
    public void 시도_결과_사유_7종의_상세_원인()
    {
        Assert.AreEqual("cannot_move_Skill",
            QABoss116Lab.Cause("dash_rejected", Kv("dashOn", "true", "inputSrc", "true", "canMove", "false", "grounded", "true", "charge", "1", "state", "Skill")));
        Assert.AreEqual("no_charge", QABoss116Lab.Cause("dash_rejected", Kv("canMove", "true", "grounded", "true", "charge", "0")));
        Assert.AreEqual("charge_0of2_next_1.25s", QABoss116Lab.Cause("no_charge", Kv("charge", "0", "maxCharge", "2", "nextChargeS", "1.25")));
        Assert.AreEqual("interrupt_cooldown", QABoss116Lab.Cause("rmb_not_started", Kv("intCdS", "3.00")));
        Assert.AreEqual("other_skill_PaladinGuardSkill", QABoss116Lab.Cause("rmb_not_started", Kv("intCdS", "0.00", "activeSkill", "PaladinGuardSkill")));
        Assert.AreEqual("dashing", QABoss116Lab.Cause("rmb_not_started", Kv("intCdS", "0.00", "activeSkill", "none", "endState", "Dash")));
        Assert.AreEqual("awaiting_approval", QABoss116Lab.Cause("rmb_not_started", Kv("intCdS", "0.00", "endState", "Idle", "skillBusy", "true")));
        Assert.AreEqual("not_started_Hit", QABoss116Lab.Cause("rmb_not_started", Kv("intCdS", "0.00", "endState", "Hit", "skillBusy", "false")));
        Assert.AreEqual("delay_beyond_dash", QABoss116Lab.Cause("dash_ended_before_press", Kv("plannedS", "0.300", "dashTotalS", "0.250", "dashAgeS", "0.250")));
        Assert.AreEqual("dash_cut_Hit", QABoss116Lab.Cause("dash_ended_before_press", Kv("plannedS", "0.050", "dashTotalS", "0.250", "dashAgeS", "0.040", "endState", "Hit")));
        Assert.AreEqual("dash_ended_normal", QABoss116Lab.Cause("dash_ended_before_press", Kv("plannedS", "0.050", "dashTotalS", "0.250", "dashAgeS", "0.250")));
        Assert.AreEqual("press_after_window", QABoss116Lab.Cause("skill_after_dash", Kv("dashAgeS", "0.150", "windowS", "0.100")));
        Assert.AreEqual("dash_ended_before_attach", QABoss116Lab.Cause("skill_after_dash", Kv("dashAgeS", "0.050", "windowS", "0.100")));
        Assert.AreEqual("press_at_0.05s_of_0.10s", QABoss116Lab.Cause("attached", Kv("dashAgeS", "0.050", "windowS", "0.100")));
        Assert.AreEqual("bot_idle", QABoss116Lab.Cause("idle_phase_none", Kv()));
    }

    [Test]
    public void 모르는_값_na_는_빈칸으로_읽는다()
    {
        // 판 파일은 빈 값을 na 로 쓴다 — 원인 계산이 na 를 숫자·문자로 오해하지 않는다.
        Assert.AreEqual("not_started_na", QABoss116Lab.Cause("rmb_not_started", Kv("intCdS", "na", "activeSkill", "na", "endState", "na", "state", "na")));
    }

    // ── 대시 거리 ─────────────────────────────────────────────

    [Test]
    public void 대시_의도_거리와_실제_거리()
    {
        QABoss116Lab.DashDistances(20f, 0.25f, 0f, 0f, 5f, 0f, 1f, 0f, out float intent, out float actual, out float shortfall, out bool blocked);
        Assert.AreEqual(5f, intent, 1e-4f);
        Assert.AreEqual(5f, actual, 1e-4f);
        Assert.AreEqual(0f, shortfall, 1e-4f);
        Assert.IsFalse(blocked);

        QABoss116Lab.DashDistances(20f, 0.25f, 0f, 0f, 3f, 2f, 1f, 0f, out _, out actual, out shortfall, out blocked);
        Assert.AreEqual(3f, actual, 1e-4f, "옆으로 미끄러진 거리는 실제 거리에 넣지 않는다(대시 방향 투영)");
        Assert.AreEqual(2f, shortfall, 1e-4f);
        Assert.IsTrue(blocked);

        QABoss116Lab.DashDistances(20f, 0.25f, 0f, 0f, 4.8f, 0f, 1f, 0f, out _, out _, out shortfall, out blocked);
        Assert.AreEqual(0.2f, shortfall, 1e-4f);
        Assert.IsFalse(blocked, "0.3m 미만은 막힘이 아니다");
    }

    // ── 행 묶기 ──────────────────────────────────────────────

    private static List<string> SampleLines() => new List<string>
    {
        "labwin event=open n=1 t=10.000 attack=Dash bossState=Attack bossM=3.10",
        "labwin event=close n=1 t=11.500 durS=1.500",
        "labwin event=open n=2 t=20.000 attack=Jump bossM=2.00",
        "labwin event=close n=2 t=20.800",
        "labdash n=1 startT=10.400 endT=10.650 startPos=0/0 endPos=2/0 dir=1/0 speed=20.00 durS=0.250 intentM=5.00 actualM=2.00 shortM=3.00 blocked=true " +
        "blockPos=2/0 blockCollider=TwentyThree blockLayer=Enemy blockAtS=0.100 prevState=Skill transfer=true expired=false nextState=Skill " +
        "stopReason=unit_block stopDetail=na caller=OnMotorMovementResolved()_@_PlayerStateController.cs:1832 ticks=5 reqM=1.00 appliedM=0.40 " +
        "frames=3 moveFrames=2 firstMoveFrame=1 firstMoveS=0.033",
        "dashint slot=0 character=Paladin clientId=0 order=dash_rmb outcome=attached delayS=0.050 window=1 serverT=10.450 firstT=10.400 " +
        "dashAgeS=0.050 windowS=0.100 charge=1 maxCharge=2",
        "dp event=cast side=server castId=s0-1 clientId=0 host=true serverT=10.450 castServerT=10.450 predict=success predReason=in_range",
        "dp event=judge side=server castId=s0-1 clientId=0 host=true serverT=10.700 castServerT=10.450 result=fail match=false mismatch=dash_shortened shortfallM=2.90",
        "dashint slot=0 character=Assassin clientId=0 order=rmb_dash outcome=rmb_not_started delayS=0.030 window=2 serverT=20.300 firstT=20.300 intCdS=0.00 endState=Hit",
        "dashint slot=0 character=Paladin clientId=0 order=dash_rmb outcome=no_charge delayS=0 window=3 serverT=40.000 charge=0 maxCharge=2 nextChargeS=0.80",
    };

    [Test]
    public void 시도_행은_창_대시_궤적_서버_판정을_묶는다()
    {
        List<string[]> rows = QABoss116Lab.BuildAttempts(Key, SampleLines());
        Assert.AreEqual(3, rows.Count);
        string[] h = QABoss116Lab.AttemptsHeader;
        string Col(string[] r, string c) => r[System.Array.IndexOf(h, c)];

        string[] a = rows[0];
        Assert.AreEqual("attached", Col(a, "outcome"));
        Assert.AreEqual("1", Col(a, "win_n"));
        Assert.AreEqual("0.400", Col(a, "t_from_open_s"));
        Assert.AreEqual("10.400", Col(a, "dash_start_t"));
        Assert.AreEqual("TwentyThree", Col(a, "dash_block_collider"));
        Assert.AreEqual("unit_block", Col(a, "dash_stop_reason"), "116-C6 — 묶인 대시의 끊긴 이유·전환·프레임 열이 시도 행에 붙는다");
        Assert.AreEqual("true", Col(a, "dash_transfer"));
        Assert.AreEqual("Skill", Col(a, "dash_prev_state"));
        Assert.AreEqual("1", Col(a, "dash_first_move_frame"));
        Assert.AreEqual("0/0", Col(a, "dash_start_pos"));
        Assert.AreEqual("s0-1", Col(a, "cast_id"));
        Assert.AreEqual("fail", Col(a, "result"));
        Assert.AreEqual("dash_shortened", Col(a, "mismatch"));
        CollectionAssert.DoesNotContain(h, "zone", "116-C6 벽 구간 열 제거");
        CollectionAssert.DoesNotContain(h, "wall_m");

        string[] b = rows[1];
        Assert.AreEqual("2", Col(b, "win_n"));
        Assert.AreEqual("not_started_Hit", Col(b, "cause"));
        Assert.AreEqual(QABoss116DashPredict.NoCast, Col(b, "result"));
        Assert.AreEqual("", Col(b, "dash_start_t"), "이미 앞 시도가 묶은 대시는 다시 묶지 않는다");

        string[] c = rows[2];
        Assert.AreEqual("", Col(c, "win_n"), "창 밖(닫힌 뒤 0.6초 넘음) 시도");
        Assert.AreEqual("charge_0of2_next_0.80s", Col(c, "cause"));
    }

    [Test]
    public void 창_행은_든_시도_수와_결과를_센다()
    {
        List<string[]> rows = QABoss116Lab.BuildWindows(Key, SampleLines());
        string[] h = QABoss116Lab.WindowsHeader;
        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual("1.500", rows[0][System.Array.IndexOf(h, "dur_s")]);
        Assert.AreEqual("Dash", rows[0][System.Array.IndexOf(h, "attack")]);
        Assert.AreEqual("1", rows[0][System.Array.IndexOf(h, "attempts")]);
        Assert.AreEqual("rmb_not_started", rows[1][System.Array.IndexOf(h, "outcomes")]);
    }

    [Test]
    public void 대시_행은_묶인_시도_번호를_적는다()
    {
        List<string[]> rows = QABoss116Lab.BuildDashes(Key, SampleLines());
        string[] h = QABoss116Lab.DashesHeader;
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual("1", rows[0][System.Array.IndexOf(h, "attempt_n")]);
        Assert.AreEqual("dash_rmb", rows[0][System.Array.IndexOf(h, "order")]);
        Assert.AreEqual("Paladin", rows[0][System.Array.IndexOf(h, "character")]);
        Assert.AreEqual("unit_block", rows[0][System.Array.IndexOf(h, "stop_reason")]);
        Assert.AreEqual("0.250", rows[0][System.Array.IndexOf(h, "dur_s")]);
        Assert.AreEqual("true", rows[0][System.Array.IndexOf(h, "blocked")]);
        CollectionAssert.DoesNotContain(h, "zone");
    }

    [Test]
    public void 요약은_순서별_적중률과_원인과_순서x캐릭터_거리를_센다()
    {
        string csv = QABoss103Csv.Line(QABoss116Lab.AttemptsHeader) + "\n" +
                     string.Join("\n", QABoss116Lab.BuildAttempts(Key, SampleLines()).Select(QABoss103Csv.Line));
        string win = QABoss103Csv.Line(QABoss116Lab.WindowsHeader) + "\n" +
                     string.Join("\n", QABoss116Lab.BuildWindows(Key, SampleLines()).Select(QABoss103Csv.Line));
        QABoss116Lab.Summary s = QABoss116Lab.Summarize(QABoss103CsvTable.Parse(csv), QABoss103CsvTable.Parse(win));
        Assert.AreEqual(3, s.All.Attempts);
        Assert.AreEqual(1, s.All.Casts);
        Assert.AreEqual(1, s.All.Judged);
        Assert.AreEqual(0, s.All.Match);
        Assert.AreEqual(1, s.All.Shortened);
        Assert.AreEqual(2, s.ByOrder["dash_rmb"].Attempts);
        Assert.AreEqual(1, s.ByOrder["rmb_dash"].Attempts);
        Assert.AreEqual(1, s.Blockers["TwentyThree"]);

        QABoss116Lab.DistBucket pal = s.ByOrderCharacter["dash_rmb × Paladin"];
        Assert.AreEqual(2, pal.Attempts);
        Assert.AreEqual(1, pal.Dashes, "대시가 묶인 시도만 거리를 센다");
        Assert.AreEqual(5.0, pal.MeanIntent, 1e-9);
        Assert.AreEqual(2.0, pal.MeanActual, 1e-9);
        Assert.AreEqual(3.0, pal.MeanShort, 1e-9);
        Assert.AreEqual(1, pal.Shortened);
        Assert.AreEqual(1, pal.Transfers);
        Assert.AreEqual(1, pal.Reasons["unit_block"]);
        Assert.AreEqual(1, s.ByOrderCharacter["rmb_dash × Assassin"].Attempts);
        Assert.AreEqual(0, s.ByOrderCharacter["rmb_dash × Assassin"].Dashes);
        Assert.AreEqual(1, s.StopReasons["unit_block"]["dash_rmb"]);
        Assert.AreEqual(1, s.StopReasons.Count);
        Assert.AreEqual(1, s.Causes["no_charge"]["charge_0of2_next_0.80s"]);
        Assert.AreEqual(2, s.Windows);
        Assert.AreEqual(2, s.WindowsWithAttempt);
        Assert.AreEqual(1.15, s.WindowMedianS, 1e-9);
    }

    // ── 대시 끊긴 이유(116-C6) — 게임 [Dash] 로그 문구 그대로 ────────

    private const string EnterLine =
        "[Dash] 상태 진입 — 이전상태=Skill 방향=(0.71, -0.71) 속도=12.0 지속=0.300s 종료예정=12.345 접지=True 이동결과권한=True 시작위치=(1.00, 0.00, 2.00) | 충돌해결=PlayerMotor";

    private const string EarlyExitLine =
        "[Dash] 종료: 조기 중단 — 남은시간 0.250s / 진행률 17% 중단 원인=Skill 전이 요청지점=BeginSkillFromDash() @ PlayerStateController.cs:452 " +
        "| 다음상태=Skill 경과=0.050s/0.300s tick=3회 요청이동=0.60m 적용이동=0.10m 실제이동=0.10m 막힌tick=0 접지상실=False";

    private const string FullExitLine =
        "[Dash] 종료: 정상 종료(지속시간 만료) | 다음상태=Idle 경과=0.300s/0.300s tick=15회 요청이동=3.60m 적용이동=3.60m 실제이동=3.58m 막힌tick=0 접지상실=False";

    [Test]
    public void 대시_진입_줄을_읽는다()
    {
        Assert.IsTrue(QABoss116DashStop.TryParseEnter(EnterLine, out string prev, out float speed, out float dur, out float dx, out float dz));
        Assert.AreEqual("Skill", prev);
        Assert.AreEqual(12f, speed, 1e-4f);
        Assert.AreEqual(0.3f, dur, 1e-4f);
        Assert.AreEqual(0.71f, dx, 1e-4f);
        Assert.AreEqual(-0.71f, dz, 1e-4f);
        Assert.IsFalse(QABoss116DashStop.TryParseEnter("[Dash] 상태 진입: Skill 상태를 덮어쓰고 대시로 전이합니다.", out _, out _, out _, out _, out _),
            "BeginDash 의 덮어쓰기 경고는 진입 줄이 아니다");
    }

    [Test]
    public void 대시_종료_줄을_읽는다()
    {
        var r = new QABoss116DashRecord();
        Assert.IsTrue(QABoss116DashStop.TryParseExit(EarlyExitLine, r));
        Assert.IsTrue(r.HasExit);
        Assert.IsFalse(r.Expired);
        Assert.AreEqual("Skill", r.NextState);
        Assert.AreEqual("Skill 전이", r.InterruptCause);
        Assert.AreEqual("BeginSkillFromDash() @ PlayerStateController.cs:452", r.Caller);
        Assert.AreEqual(3, r.Ticks, "막힌tick= 이 아니라 tick= 을 읽는다");
        Assert.AreEqual(0.6f, r.ReqM, 1e-4f);
        Assert.AreEqual(0.1f, r.AppliedM, 1e-4f);
        Assert.AreEqual(0.1f, r.LogActualM, 1e-4f);
        Assert.AreEqual(0.05f, r.ExitElapsedS, 1e-4f);
        Assert.AreEqual(0.3f, r.ExitDurationS, 1e-4f);
        Assert.AreEqual(0, r.BlockedTicks);
        Assert.IsFalse(r.LostGround);

        var full = new QABoss116DashRecord();
        Assert.IsTrue(QABoss116DashStop.TryParseExit(FullExitLine, full));
        Assert.IsTrue(full.Expired);
        Assert.AreEqual("Idle", full.NextState);
        Assert.AreEqual("", full.Caller);
        Assert.AreEqual(15, full.Ticks);
        Assert.AreEqual(QABoss116DashStop.Full, QABoss116DashStop.Classify(full, 0.05));
        Assert.AreEqual(QABoss116DashStop.ShortUnexplained, QABoss116DashStop.Classify(full, 1.0));
    }

    [Test]
    public void 도중_신호를_모은다()
    {
        var r = new QABoss116DashRecord();
        r.Feed("[Dash] tick #2: 스윕이 이동을 차단 — 요청 0.200m → 적용 0.000m (PlayerMotor 벽/급경사 판정). 대시 상태와 무적은 종료시각까지 유지됩니다.");
        r.Feed("[Dash] 취소 원인 분류: 서버 거부(NotGrounded) (requestId=3)");
        r.Feed("[DashInterrupt] 대시 중 강제 종료 → 간파 종료 (Interrupted, 다음 상태 Idle)");
        r.Feed("[Dash] 이동 없음: Tick이 한 번도 돌지 않았습니다");
        Assert.AreEqual(1, r.SweepLines);
        Assert.IsTrue(r.ServerCancel);
        Assert.AreEqual("서버 거부(NotGrounded)", r.CancelCause);
        Assert.IsTrue(r.InterruptAborted);
        Assert.IsTrue(r.NoTickLine);
        Assert.IsFalse(r.UnitBlock);
        r.Feed("[Dash] tick #4: 유닛에 막혀 대시 종료 — 요청 0.200m → 적용 0.050m");
        Assert.IsTrue(r.UnitBlock);
        StringAssert.StartsWith("서버 거부(NotGrounded)", r.Detail());
    }

    private static QABoss116DashRecord Exited(bool expired, string next, int ticks = 10, float req = 3f, float applied = 3f, float actual = 3f,
        int blockedTicks = 0, bool lostGround = false) =>
        new QABoss116DashRecord
        {
            HasExit = true, Expired = expired, NextState = next, Ticks = ticks, ReqM = req, AppliedM = applied, LogActualM = actual,
            BlockedTicks = blockedTicks, LostGround = lostGround,
        };

    [Test]
    public void 끊긴_이유는_우선순위대로()
    {
        QABoss116DashRecord cancel = Exited(false, "Idle", ticks: 0);
        cancel.ServerCancel = true;
        cancel.UnitBlock = true;
        Assert.AreEqual(QABoss116DashStop.ServerCancel, QABoss116DashStop.Classify(cancel, 3.0), "서버 취소가 가장 먼저");

        QABoss116DashRecord unit = Exited(false, "Skill", ticks: 2);
        unit.UnitBlock = true;
        Assert.AreEqual(QABoss116DashStop.UnitBlock, QABoss116DashStop.Classify(unit, 3.0));

        Assert.AreEqual(QABoss116DashStop.Knockback, QABoss116DashStop.Classify(Exited(false, "Knockback"), 3.0));
        Assert.AreEqual(QABoss116DashStop.Restrained, QABoss116DashStop.Classify(Exited(false, "Restrained"), 3.0));
        Assert.AreEqual(QABoss116DashStop.Cinematic, QABoss116DashStop.Classify(Exited(false, "Cinematic"), 3.0));
        Assert.AreEqual(QABoss116DashStop.Dead, QABoss116DashStop.Classify(Exited(false, "Dead"), 3.0));
        Assert.AreEqual(QABoss116DashStop.Redash, QABoss116DashStop.Classify(Exited(false, "Dash"), 3.0));
        Assert.AreEqual("early_Skill", QABoss116DashStop.Classify(Exited(false, "Skill"), 3.0));
        Assert.AreEqual(QABoss116DashStop.NoTick, QABoss116DashStop.Classify(Exited(true, "Idle", ticks: 0), 3.0));
        Assert.AreEqual(QABoss116DashStop.MotorIdle, QABoss116DashStop.Classify(Exited(true, "Idle", req: 0f, applied: 0f, actual: 0f), 3.0));
        Assert.AreEqual(QABoss116DashStop.WallSweep, QABoss116DashStop.Classify(Exited(true, "Idle", applied: 1f, actual: 1f, blockedTicks: 4), 2.0));
        Assert.AreEqual(QABoss116DashStop.WallSweep, QABoss116DashStop.Classify(Exited(true, "Idle", applied: 2f, actual: 2f), 1.0), "막힌tick 없이 적용 < 요청 90%");
        Assert.AreEqual(QABoss116DashStop.Airborne, QABoss116DashStop.Classify(Exited(true, "Idle", lostGround: true), 0.5));
        Assert.AreEqual(QABoss116DashStop.PostApplyLoss, QABoss116DashStop.Classify(Exited(true, "Idle", actual: 1f), 2.0));
        Assert.AreEqual(QABoss116DashStop.NoExitLog, QABoss116DashStop.Classify(new QABoss116DashRecord(), 3.0));
        StringAssert.Contains("PlayerUnitCollisionRules", QABoss116DashStop.Describe(QABoss116DashStop.UnitBlock));
        StringAssert.Contains("ChangeState", QABoss116DashStop.Describe("early_Skill"));
    }

    [Test]
    public void 프레임_이동_요약()
    {
        var r = new QABoss116DashRecord();
        r.Frame(0f, 0.016f, 1f, 0f);       // 첫 프레임 제자리
        r.Frame(0.005f, 0.033f, 0.3f, 0.5f); // 1cm 미만은 이동 아님, 슬로우
        r.Frame(0.4f, 0.050f, 0.3f, 1f);
        r.Frame(0.4f, 0.066f, 1f, 1f);
        Assert.AreEqual(4, r.Frames);
        Assert.AreEqual(2, r.MoveFrames);
        Assert.AreEqual(2, r.FirstMoveFrame);
        Assert.AreEqual(0.05f, r.FirstMoveS, 1e-5f);
        Assert.AreEqual(0.3f, r.MinTimeScale, 1e-5f);
        Assert.AreEqual(1f, r.MaxUpperWeight, 1e-5f);
        string[] kv = r.ToKv("full");
        Assert.AreEqual("2", kv[System.Array.IndexOf(kv, "firstMoveFrame") + 1]);
        Assert.AreEqual("full", kv[System.Array.IndexOf(kv, "stopReason") + 1]);
        Assert.AreEqual("na", kv[System.Array.IndexOf(kv, "expired") + 1], "종료 줄을 못 봤으면 na");
    }

    // ── 격리 ─────────────────────────────────────────────────

    // QABoss116LabManualInput(C5)은 씬에 놓지 않고 디렉터가 수동 모드에서만 붙인다 — 어느 씬·프리팹에도 없어야 한다.
    private static readonly System.Type[] LabComponents = { typeof(QABoss116LabDirector), typeof(QABoss116LabCamera), typeof(QABoss116LabManualInput) };

    private static string GuidOf(System.Type t) =>
        AssetDatabase.AssetPathToGUID(AssetDatabase.FindAssets($"t:MonoScript {t.Name}").Select(AssetDatabase.GUIDToAssetPath)
            .Single(p => Path.GetFileNameWithoutExtension(p) == t.Name));

    [Test]
    public void 랩_씬은_빌드_목록에_없다()
    {
        Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(QABoss116Lab.ScenePath));
        CollectionAssert.DoesNotContain(EditorBuildSettings.scenes.Select(s => s.path.Replace('\\', '/')).ToArray(), QABoss116Lab.ScenePath);
    }

    [Test]
    public void 랩_컴포넌트는_랩_씬에만_있다()
    {
        string[] guids = LabComponents.Select(GuidOf).ToArray();
        Assert.IsTrue(guids.All(g => !string.IsNullOrEmpty(g)));
        bool Uses(string path) { string text = File.ReadAllText(path); return guids.Any(g => text.Contains("guid: " + g)); }
        Assert.IsTrue(Uses(QABoss116Lab.ScenePath), "검색이 실제로 잡는지 — 랩 씬에는 있어야 한다");
        string[] others = Directory.GetFiles("Assets", "*.unity", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles("Assets", "*.prefab", SearchOption.AllDirectories))
            .Select(p => p.Replace('\\', '/'))
            .Where(p => p != QABoss116Lab.ScenePath)
            .Where(Uses)
            .ToArray();
        CollectionAssert.IsEmpty(others);
    }

    [Test]
    public void 랩_배치는_창_최소화를_부르지_않는다()
    {
        string path = AssetDatabase.FindAssets("t:MonoScript QABoss116LabBatchRunner").Select(AssetDatabase.GUIDToAssetPath)
            .Single(p => Path.GetFileNameWithoutExtension(p) == "QABoss116LabBatchRunner");
        StringAssert.DoesNotContain("QAWindowMinimizer.", File.ReadAllText(path));
    }
}
#endif
