#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// 할 일 116-C4 — 대쉬 간파 예측 랩(호스트 1명 · 전용 디버그 씬 <c>DebugDashPredictLab</c>)의 순수 규칙(EditMode <c>QABoss116LabTests</c>).
/// <list type="bullet">
/// <item>랩 설정 파일(<see cref="QABoss116LabConfig"/>) — 봇 설정(<see cref="QABoss103Config"/>)에 판 길이·시도 상한을 더한다.</item>
/// <item>봇 시도 결과 사유 7종 + 나머지의 상세 원인(<see cref="Cause"/>) — 봇이 판 파일 <c>dashint</c> 줄에 붙인 판단 근거 값만으로 정한다.</item>
/// <item>판 파일 줄(<c>dashint</c>·<c>dp</c>·<c>labwin</c>·<c>labdash</c>) → CSV 3종(<see cref="BuildAttempts"/>·<see cref="BuildWindows"/>·<see cref="BuildDashes"/>)과 요약(<see cref="Summarize"/>).</item>
/// <item>116-C6: 벽 구간 제거. 대시 1회의 끊긴 이유(<see cref="QABoss116DashStop"/>)·프레임 이동 요약을 시도 행에 붙이고, 순서×캐릭터별 의도 vs 실제 거리를 센다.</item>
/// </list>
/// 시각은 모두 호스트 <c>NetworkManager.ServerTime</c>(초) — 봇 시도·예측 로그·카운터 창·대시 궤적이 같은 시계다.
/// </summary>
public static class QABoss116Lab
{
    public const string Tag = "[QA116Lab]";
    public const string Prefix = "116lab";
    public const string ScenePath = "Assets/0.Scenes/Debug/DebugDashPredictLab.unity";

    public const int TotalRuns = 10;
    /// <summary>1판 상한(실제 시간 초) — 이것과 <see cref="MaxAttempts"/> 중 먼저.</summary>
    public const double RunSeconds = 180.0;
    /// <summary>1판 대쉬 간파 시도 상한.</summary>
    public const int MaxAttempts = 60;
    /// <summary>랩은 간파 창마다 대쉬 간파(%).</summary>
    public const int DashInterruptPct = 100;

    // 판 파일 줄 종류(랩 디렉터가 parts/run<n>_r<retry>_lab.txt 에 쓴다)
    public const string WindowKind = "labwin";
    public const string DashKind = "labdash";
    public const string EndKind = "labend";

    public const string AttemptsFile = "lab_attempts.csv";
    public const string WindowsFile = "lab_windows.csv";
    public const string DashesFile = "lab_dashes.csv";

    /// <summary>봇 시도 ↔ 서버 시전 묶기 상한(초) — <see cref="QABoss116DashPredict.BotMatchSeconds"/> 와 같다.</summary>
    public const double CastMatchSeconds = 1.0;
    /// <summary>봇 시도 ↔ 대시 궤적 묶기 — 첫 입력 0.2초 전 ~ 마지막 입력 1초 뒤에 시작한 대시.</summary>
    public const double DashLeadSeconds = 0.2, DashLagSeconds = 1.0;
    /// <summary>창이 닫힌 뒤 이만큼(초)까지 누른 시도는 그 창의 시도로 본다(늦은 누름 표본).</summary>
    public const double WindowLateSeconds = 0.6;
    /// <summary>대시가 이만큼(m) 덜 갔으면 막힘.</summary>
    public const float ShortfallMeters = 0.3f;

    public static string LabPartPath(string outDir, int run, int retry) =>
        System.IO.Path.Combine(outDir, QABoss103PartFile.Folder, $"run{run}_r{retry}_lab.txt");

    public static string DonePath(string outDir, int run, int retry) =>
        System.IO.Path.Combine(outDir, QABoss103PartFile.Folder, $"run{run}_r{retry}_labdone.txt");

    public static string BatchFolderName(DateTime at) => Prefix + "-" + at.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture);

    /// <summary>판이 끝났나 — 시간 상한 또는 시도 상한(먼저 닿는 것). 끝이면 사유.</summary>
    public static string EndReason(double elapsed, int attempts, double runSeconds, int maxAttempts)
    {
        if (maxAttempts > 0 && attempts >= maxAttempts)
            return "attempts";
        if (runSeconds > 0.0 && elapsed >= runSeconds)
            return "time";
        return null;
    }

    // ── 상세 원인 ─────────────────────────────────────────────

    /// <summary>
    /// 대시 시작 게이트(<c>PlayerDashController.TryBeginPredictedDash</c> 의 검사 순서 그대로) — 막힌 첫 게이트.
    /// 다 통과했는데 거부면 상태 진입 거부(<c>state_enter_rejected</c>).
    /// </summary>
    public static string DashGate(bool dashOn, bool inputSource, bool canMove, bool grounded, int charge, string state)
    {
        if (!dashOn)
            return "dash_disabled";
        if (!inputSource)
            return "not_input_source";
        if (!canMove)
            return "cannot_move_" + Or(state, "na");
        if (!grounded)
            return "airborne";
        if (charge <= 0)
            return "no_charge";
        return "state_enter_rejected";
    }

    /// <summary>
    /// 봇 시도 결과(<paramref name="outcome"/>) + 판단 근거 값(<paramref name="kv"/> — 봇 <c>dashint</c> 줄) → 상세 원인 토큰.
    /// <list type="bullet">
    /// <item><c>dash_rejected</c>·<c>no_dash</c> — 시작 게이트(<see cref="DashGate"/>). 게임 로그가 잡혔으면 그 문구가 우선이다(<c>dash_log</c> 열).</item>
    /// <item><c>no_charge</c> — 충전/최대·다음 충전까지.</item>
    /// <item><c>rmb_not_started</c> — 우클릭 뒤 상한 안에 간파 Skill 이 안 됐다: 쿨·다른 스킬·대시 중·승인 대기·상태.</item>
    /// <item><c>dash_ended_before_press</c> — 계획 지연이 대시 길이 이상이면 지연 초과, 대시가 일찍 끊겼으면 끊긴 상태.</item>
    /// <item><c>skill_after_dash</c> — 누른 순간 대시 경과가 창 밖이면 창 밖 누름, 안이면 부착 전 대시 끝.</item>
    /// <item><c>attached</c> — 누른 순간 대시 경과/창.</item>
    /// </list>
    /// </summary>
    public static string Cause(string outcome, IReadOnlyDictionary<string, string> kv)
    {
        string state = Get(kv, "state");
        switch (outcome ?? "")
        {
            case "dash_rejected":
            case "no_dash":
                return DashGate(Bool(kv, "dashOn", true), Bool(kv, "inputSrc", true), Bool(kv, "canMove", true), Bool(kv, "grounded", true),
                    Int(kv, "charge", 1), state);
            case "no_charge":
                return $"charge_{Int(kv, "charge", 0)}of{Int(kv, "maxCharge", 0)}_next_{F2(Dbl(kv, "nextChargeS"))}s";
            case "rmb_not_started":
            {
                double cd = Dbl(kv, "intCdS");
                string active = Get(kv, "activeSkill");
                string endState = Or(Get(kv, "endState"), state);
                if (!double.IsNaN(cd) && cd > 0.0)
                    return "interrupt_cooldown";
                if (active.Length > 0 && active != "none" && active.IndexOf("Interrupt", StringComparison.Ordinal) < 0)
                    return "other_skill_" + active;
                if (endState == "Dash")
                    return "dashing";
                if (Bool(kv, "skillBusy", false))
                    return "awaiting_approval";
                return "not_started_" + Or(endState, "na");
            }
            case "dash_ended_before_press":
            {
                double planned = Dbl(kv, "plannedS"), total = Dbl(kv, "dashTotalS"), age = Dbl(kv, "dashAgeS");
                if (!double.IsNaN(planned) && !double.IsNaN(total) && planned >= total - 1e-3)
                    return "delay_beyond_dash";
                if (!double.IsNaN(age) && !double.IsNaN(total) && age < total - 0.03)
                    return "dash_cut_" + Or(Get(kv, "endState"), "na");
                return "dash_ended_normal";
            }
            case "skill_after_dash":
            {
                double age = Dbl(kv, "dashAgeS"), window = Dbl(kv, "windowS");
                if (!double.IsNaN(age) && !double.IsNaN(window) && age > window)
                    return "press_after_window";
                return "dash_ended_before_attach";
            }
            case "attached":
            {
                // 사람 우클릭 → 대시(C5)는 대시 경과가 없다 — 두 입력 간격(gapS)으로 대신한다.
                double age = Dbl(kv, "dashAgeS");
                if (double.IsNaN(age))
                    age = Dbl(kv, "gapS");
                return $"press_at_{F2(age)}s_of_{F2(Dbl(kv, "windowS"))}s";
            }
            case "interrupt_ended_before_dash":
                return "interrupt_ended_" + Or(Get(kv, "endState"), "na");
            case "dash_without_transfer":
                return "dash_started_not_attached";
            case "no_attach":
                return "rmb_pressed_not_attached_" + Or(Get(kv, "endState"), "na");
            case "unavailable":
                return Dbl(kv, "windowS") > 0 ? "latch_missing" : "window_disabled";
            default:
                return outcome != null && outcome.StartsWith("idle_", StringComparison.Ordinal) ? "bot_idle" : "";
        }
    }

    // ── CSV ──────────────────────────────────────────────────

    public static readonly string[] AttemptsHeader = Concat(QABoss103Metrics.KeyHeader, new[]
    {
        "n", "character", "order", "outcome", "cause", "boss_m", "attack", "boss_state",
        "server_t", "first_t", "delay_s", "window_s", "dash_age_s", "dash_total_s", "wait_s",
        "charge", "max_charge", "next_charge_s", "state", "end_state", "can_move", "can_dash_act", "grounded", "input_src", "dash_on",
        "skill_busy", "active_skill", "int_cd_s", "seen", "dash_log",
        "win_n", "win_open_t", "win_close_t", "win_dur_s", "t_from_open_s", "t_to_close_s",
        "dash_start_t", "dash_intent_m", "dash_actual_m", "dash_short_m", "dash_blocked", "dash_block_pos", "dash_block_collider", "dash_block_layer", "dash_block_at_s",
        // 할 일 116-C6 — 묶인 대시의 전환·끊긴 이유·프레임 이동(labdash 줄 그대로)
        "dash_start_pos", "dash_end_pos", "dash_prev_state", "dash_prev_frame", "dash_transfer", "dash_expired", "dash_next_state",
        "dash_stop_reason", "dash_stop_detail", "dash_caller", "dash_ticks", "dash_req_m", "dash_applied_m", "dash_blocked_ticks",
        "dash_frames", "dash_move_frames", "dash_first_move_frame", "dash_first_move_s", "dash_min_ts", "dash_upper_w",
        "dash_can_move_prev", "dash_root_motion", "dash_boss_along_m", "dash_boss_lateral_m", "dash_interrupt_aborted",
        "cast_id", "predict", "pred_reason", "result", "match", "mismatch", "shortfall_m", "pos_err_m", "time_err_s",
        "pred_resolve_in_s", "resolve_in_s", "win_close_after_cast_s", "target_open_at_resolve",
        // 할 일 116-C5 — 시도 출처(bot·human)·두 입력 간격·입력 순간 카운터 창 열림(사람 시도만 값이 있다)
        "source", "gap_s", "counter_open_first", "counter_open_second", "same_frame",
    });

    public static readonly string[] WindowsHeader = Concat(QABoss103Metrics.KeyHeader, new[]
    {
        "win_n", "open_t", "close_t", "dur_s", "attack", "boss_state", "boss_m", "attempts", "outcomes",
    });

    /// <summary>labdash 줄 키 → 대시 CSV 열(116-C6 열 포함). 시도 CSV 는 같은 키를 <c>dash_</c> 를 붙여 쓴다(<see cref="DashAttemptCols"/>).</summary>
    private static readonly (string Col, string Key)[] DashCols =
    {
        ("start_pos", "startPos"), ("end_pos", "endPos"), ("dir", "dir"), ("speed", "speed"), ("dur_s", "durS"), ("intent_m", "intentM"), ("actual_m", "actualM"),
        ("short_m", "shortM"), ("blocked", "blocked"), ("block_pos", "blockPos"), ("block_collider", "blockCollider"), ("block_layer", "blockLayer"),
        ("block_at_s", "blockAtS"), ("end_state", "endState"), ("attached", "attached"),
        ("prev_state", "prevState"), ("prev_frame", "prevFrame"), ("transfer", "transfer"), ("expired", "expired"), ("next_state", "nextState"),
        ("stop_reason", "stopReason"), ("stop_detail", "stopDetail"), ("caller", "caller"), ("ticks", "ticks"), ("req_m", "reqM"), ("applied_m", "appliedM"),
        ("log_actual_m", "logActualM"), ("blocked_ticks", "blockedTicks"), ("lost_ground", "lostGround"), ("exit_elapsed_s", "exitElapsedS"),
        ("frames", "frames"), ("move_frames", "moveFrames"), ("first_move_frame", "firstMoveFrame"), ("first_move_s", "firstMoveS"),
        ("min_ts", "minTs"), ("upper_w", "upperW"), ("can_move_prev", "canMovePrev"), ("root_motion", "rootMotion"),
        ("boss_m", "bossM"), ("boss_along_m", "bossAlongM"), ("boss_lateral_m", "bossLateralM"), ("interrupt_aborted", "interruptAborted"),
    };

    private static readonly (string Col, string Key)[] DashAttemptCols =
    {
        ("dash_intent_m", "intentM"), ("dash_actual_m", "actualM"), ("dash_short_m", "shortM"), ("dash_blocked", "blocked"), ("dash_block_pos", "blockPos"),
        ("dash_block_collider", "blockCollider"), ("dash_block_layer", "blockLayer"), ("dash_block_at_s", "blockAtS"),
        ("dash_start_pos", "startPos"), ("dash_end_pos", "endPos"), ("dash_prev_state", "prevState"), ("dash_prev_frame", "prevFrame"),
        ("dash_transfer", "transfer"), ("dash_expired", "expired"), ("dash_next_state", "nextState"), ("dash_stop_reason", "stopReason"),
        ("dash_stop_detail", "stopDetail"), ("dash_caller", "caller"), ("dash_ticks", "ticks"), ("dash_req_m", "reqM"), ("dash_applied_m", "appliedM"),
        ("dash_blocked_ticks", "blockedTicks"), ("dash_frames", "frames"), ("dash_move_frames", "moveFrames"), ("dash_first_move_frame", "firstMoveFrame"),
        ("dash_first_move_s", "firstMoveS"), ("dash_min_ts", "minTs"), ("dash_upper_w", "upperW"), ("dash_can_move_prev", "canMovePrev"),
        ("dash_root_motion", "rootMotion"), ("dash_boss_along_m", "bossAlongM"), ("dash_boss_lateral_m", "bossLateralM"),
        ("dash_interrupt_aborted", "interruptAborted"),
    };

    public static readonly string[] DashesHeader = Concat(QABoss103Metrics.KeyHeader, Concat(new[] { "dash_n", "start_t", "end_t" },
        Concat(Cols(DashCols), new[] { "attempt_n", "order", "character" })));

    private static string[] Cols((string Col, string Key)[] map)
    {
        var r = new string[map.Length];
        for (int i = 0; i < map.Length; i++)
            r[i] = map[i].Col;
        return r;
    }

    private sealed class Win
    {
        public int N;
        public double Open, Close = double.NaN;
        public Dictionary<string, string> Kv;
        public readonly List<string> Outcomes = new List<string>();
    }

    private sealed class Dash
    {
        public int N;
        public double Start, End;
        public Dictionary<string, string> Kv;
        public int Attempt = -1;
    }

    private sealed class Cast
    {
        public Dictionary<string, string> Cast_, Judge;
        public int ClientId;
        public double T;
        public bool Used;
    }

    /// <summary>판 파일 줄들 → 창(열림·닫힘 짝) 목록. 닫힘이 없는 창은 NaN.</summary>
    private static List<Win> Windows(IEnumerable<string> lines)
    {
        var list = new List<Win>();
        foreach (string line in lines)
        {
            if (!QABoss103PartFile.TryParse(line, out string kind, out Dictionary<string, string> kv) || kind != WindowKind)
                continue;
            int n = Int(kv, "n", -1);
            string evt = Get(kv, "event");
            if (evt == "open")
                list.Add(new Win { N = n, Open = Dbl(kv, "t"), Kv = kv });
            else if (evt == "close")
            {
                Win w = list.Find(x => x.N == n);
                if (w != null)
                {
                    w.Close = Dbl(kv, "t");
                    foreach (KeyValuePair<string, string> p in kv)
                        if (!w.Kv.ContainsKey(p.Key))
                            w.Kv[p.Key] = p.Value;
                }
            }
        }
        return list;
    }

    private static List<Dash> Dashes(IEnumerable<string> lines)
    {
        var list = new List<Dash>();
        foreach (string line in lines)
            if (QABoss103PartFile.TryParse(line, out string kind, out Dictionary<string, string> kv) && kind == DashKind)
                list.Add(new Dash { N = Int(kv, "n", list.Count + 1), Start = Dbl(kv, "startT"), End = Dbl(kv, "endT"), Kv = kv });
        return list;
    }

    /// <summary>시각 <paramref name="t"/> 에 열려 있던(닫힌 뒤 <see cref="WindowLateSeconds"/> 까지 포함) 가장 늦게 열린 창. 없으면 null.</summary>
    private static Win WindowAt(List<Win> wins, double t)
    {
        if (double.IsNaN(t))
            return null;
        Win best = null;
        foreach (Win w in wins)
        {
            if (double.IsNaN(w.Open) || w.Open > t)
                continue;
            double until = double.IsNaN(w.Close) ? double.PositiveInfinity : w.Close + WindowLateSeconds;
            if (t <= until && (best == null || w.Open > best.Open))
                best = w;
        }
        return best;
    }

    /// <summary>
    /// 시도 행(봇 <c>dashint</c> 1줄 = 1행). 서버 시전(<c>dp event=cast side=server</c>)은 같은 clientId + |serverT 차| ≤ <see cref="CastMatchSeconds"/> 가장 가까운 것,
    /// 대시 궤적은 [첫 입력 − <see cref="DashLeadSeconds"/>, 마지막 입력 + <see cref="DashLagSeconds"/>] 에 시작한 첫 대시, 창은 첫 입력 시각에 열려 있던 창.
    /// </summary>
    public static List<string[]> BuildAttempts(string[] key, IEnumerable<string> lines)
    {
        var all = new List<string>(lines);
        List<Win> wins = Windows(all);
        List<Dash> dashes = Dashes(all);
        var casts = new List<Cast>();
        var castById = new Dictionary<string, Cast>(StringComparer.Ordinal);
        var bots = new List<Dictionary<string, string>>();
        foreach (string line in all)
        {
            if (!QABoss103PartFile.TryParse(line, out string kind, out Dictionary<string, string> kv))
                continue;
            if (kind == QABoss116DashPredict.BotKind)
            {
                bots.Add(kv);
                continue;
            }
            if (kind != QABoss116DashPredict.PartKind || Get(kv, "side") != "server")
                continue;
            string id = Get(kv, "castId");
            if (id.Length == 0)
                continue;
            if (!castById.TryGetValue(id, out Cast c))
            {
                c = new Cast { ClientId = Int(kv, "clientId", -1), T = Dbl(kv, "castServerT") };
                castById[id] = c;
                casts.Add(c);
            }
            if (Get(kv, "event") == "cast")
                c.Cast_ = kv;
            else if (Get(kv, "event") == "judge")
                c.Judge = kv;
        }

        var rows = new List<string[]>();
        int n = 0;
        foreach (Dictionary<string, string> b in bots)
        {
            n++;
            var r = NewRow(AttemptsHeader, key);
            string outcome = Get(b, "outcome");
            double serverT = Dbl(b, "serverT"), firstT = Dbl(b, "firstT");
            if (double.IsNaN(firstT))
                firstT = serverT;
            Set(r, AttemptsHeader, "n", n.ToString(CultureInfo.InvariantCulture));
            Set(r, AttemptsHeader, "character", Get(b, "character"));
            Set(r, AttemptsHeader, "order", Get(b, "order"));
            Set(r, AttemptsHeader, "outcome", outcome);
            Set(r, AttemptsHeader, "cause", Cause(outcome, b));
            Copy(r, b, "boss_m", "bossM");
            Copy(r, b, "attack", "attack");
            Copy(r, b, "boss_state", "bossState");
            Copy(r, b, "server_t", "serverT");
            Set(r, AttemptsHeader, "first_t", F3(firstT));
            Copy(r, b, "delay_s", "delayS");
            Copy(r, b, "window_s", "windowS");
            Copy(r, b, "dash_age_s", "dashAgeS");
            Copy(r, b, "dash_total_s", "dashTotalS");
            Copy(r, b, "wait_s", "waitS");
            Copy(r, b, "charge", "charge");
            Copy(r, b, "max_charge", "maxCharge");
            Copy(r, b, "next_charge_s", "nextChargeS");
            Copy(r, b, "state", "state");
            Copy(r, b, "end_state", "endState");
            Copy(r, b, "can_move", "canMove");
            Copy(r, b, "can_dash_act", "canDashAct");
            Copy(r, b, "grounded", "grounded");
            Copy(r, b, "input_src", "inputSrc");
            Copy(r, b, "dash_on", "dashOn");
            Copy(r, b, "skill_busy", "skillBusy");
            Copy(r, b, "active_skill", "activeSkill");
            Copy(r, b, "int_cd_s", "intCdS");
            Copy(r, b, "seen", "seen");
            Copy(r, b, "dash_log", "dashLog");
            Set(r, AttemptsHeader, "source", Or(Get(b, "source"), "bot"));
            Copy(r, b, "gap_s", "gapS");
            Copy(r, b, "counter_open_first", "counterOpenFirst");
            Copy(r, b, "counter_open_second", "counterOpenSecond");
            Copy(r, b, "same_frame", "sameFrame");

            Win w = WindowAt(wins, firstT);
            if (w != null)
            {
                w.Outcomes.Add(outcome);
                Set(r, AttemptsHeader, "win_n", w.N.ToString(CultureInfo.InvariantCulture));
                Set(r, AttemptsHeader, "win_open_t", F3(w.Open));
                Set(r, AttemptsHeader, "win_close_t", F3(w.Close));
                Set(r, AttemptsHeader, "win_dur_s", F3(w.Close - w.Open));
                Set(r, AttemptsHeader, "t_from_open_s", F3(firstT - w.Open));
                Set(r, AttemptsHeader, "t_to_close_s", F3(w.Close - firstT));
            }

            double lastT = double.IsNaN(serverT) ? firstT : Math.Max(firstT, serverT);
            Dash d = null;
            foreach (Dash x in dashes)
                if (x.Attempt < 0 && !double.IsNaN(x.Start) && x.Start >= firstT - DashLeadSeconds && x.Start <= lastT + DashLagSeconds)
                {
                    d = x;
                    break;
                }
            if (d != null)
            {
                d.Attempt = n;
                Set(r, AttemptsHeader, "dash_start_t", F3(d.Start));
                foreach ((string col, string k) in DashAttemptCols)
                    Copy(r, d.Kv, col, k);
            }

            Cast best = null;
            double bestDt = double.MaxValue;
            int clientId = Int(b, "clientId", -1);
            foreach (Cast c in casts)
            {
                if (c.Used || c.Cast_ == null || c.ClientId != clientId || double.IsNaN(c.T) || double.IsNaN(serverT))
                    continue;
                double dt = Math.Abs(c.T - serverT);
                if (dt <= CastMatchSeconds && dt < bestDt)
                {
                    best = c;
                    bestDt = dt;
                }
            }
            if (best != null)
            {
                best.Used = true;
                Copy(r, best.Cast_, "cast_id", "castId");
                Copy(r, best.Cast_, "predict", "predict");
                Copy(r, best.Cast_, "pred_reason", "predReason");
                Set(r, AttemptsHeader, "result", best.Judge != null ? Get(best.Judge, "result") : "missing");
                Copy(r, best.Judge, "match", "match");
                Copy(r, best.Judge, "mismatch", "mismatch");
                Copy(r, best.Judge, "shortfall_m", "shortfallM");
                Copy(r, best.Judge, "pos_err_m", "posErrM");
                Copy(r, best.Judge, "time_err_s", "timeErrS");
                Copy(r, best.Cast_, "pred_resolve_in_s", "predResolveInS");
                Copy(r, best.Judge, "resolve_in_s", "resolveInS");
                Copy(r, best.Judge, "win_close_after_cast_s", "winCloseAfterCastS");
                Copy(r, best.Judge, "target_open_at_resolve", "targetOpenAtResolve");
            }
            else
                Set(r, AttemptsHeader, "result", QABoss116DashPredict.NoCast);
            rows.Add(r);

            void Copy(string[] row, Dictionary<string, string> src, string col, string k) => Set(row, AttemptsHeader, col, Get(src, k));
        }
        return rows;
    }

    /// <summary>카운터 창 행(열림 1회 = 1행) — 그 창에 든 봇 시도 수·결과. <see cref="BuildAttempts"/> 와 같은 창 규칙.</summary>
    public static List<string[]> BuildWindows(string[] key, IEnumerable<string> lines)
    {
        var all = new List<string>(lines);
        List<Win> wins = Windows(all);
        foreach (string line in all)
        {
            if (!QABoss103PartFile.TryParse(line, out string kind, out Dictionary<string, string> kv) || kind != QABoss116DashPredict.BotKind)
                continue;
            double t = Dbl(kv, "firstT");
            if (double.IsNaN(t))
                t = Dbl(kv, "serverT");
            WindowAt(wins, t)?.Outcomes.Add(Get(kv, "outcome"));
        }
        var rows = new List<string[]>();
        foreach (Win w in wins)
        {
            var r = NewRow(WindowsHeader, key);
            Set(r, WindowsHeader, "win_n", w.N.ToString(CultureInfo.InvariantCulture));
            Set(r, WindowsHeader, "open_t", F3(w.Open));
            Set(r, WindowsHeader, "close_t", F3(w.Close));
            Set(r, WindowsHeader, "dur_s", F3(w.Close - w.Open));
            Set(r, WindowsHeader, "attack", Get(w.Kv, "attack"));
            Set(r, WindowsHeader, "boss_state", Get(w.Kv, "bossState"));
            Set(r, WindowsHeader, "boss_m", Get(w.Kv, "bossM"));
            Set(r, WindowsHeader, "attempts", w.Outcomes.Count.ToString(CultureInfo.InvariantCulture));
            Set(r, WindowsHeader, "outcomes", string.Join(";", w.Outcomes));
            rows.Add(r);
        }
        return rows;
    }

    /// <summary>대시 궤적 행(호스트 플레이어 대시 1회 = 1행). attempt_n = <see cref="BuildAttempts"/> 가 묶은 시도 번호.</summary>
    public static List<string[]> BuildDashes(string[] key, IEnumerable<string> lines)
    {
        var all = new List<string>(lines);
        List<Dash> dashes = Dashes(all);
        // 시도 번호 묶기 — BuildAttempts 와 같은 규칙.
        int n = 0;
        var bound = new Dictionary<Dash, Dictionary<string, string>>();
        foreach (string line in all)
        {
            if (!QABoss103PartFile.TryParse(line, out string kind, out Dictionary<string, string> kv) || kind != QABoss116DashPredict.BotKind)
                continue;
            n++;
            double serverT = Dbl(kv, "serverT"), firstT = Dbl(kv, "firstT");
            if (double.IsNaN(firstT))
                firstT = serverT;
            double lastT = double.IsNaN(serverT) ? firstT : Math.Max(firstT, serverT);
            foreach (Dash x in dashes)
                if (x.Attempt < 0 && !double.IsNaN(x.Start) && x.Start >= firstT - DashLeadSeconds && x.Start <= lastT + DashLagSeconds)
                {
                    x.Attempt = n;
                    bound[x] = kv;
                    break;
                }
        }
        var rows = new List<string[]>();
        foreach (Dash d in dashes)
        {
            var r = NewRow(DashesHeader, key);
            Set(r, DashesHeader, "dash_n", d.N.ToString(CultureInfo.InvariantCulture));
            Set(r, DashesHeader, "start_t", F3(d.Start));
            Set(r, DashesHeader, "end_t", F3(d.End));
            foreach ((string col, string k) in DashCols)
                Set(r, DashesHeader, col, Get(d.Kv, k));
            Set(r, DashesHeader, "attempt_n", d.Attempt > 0 ? d.Attempt.ToString(CultureInfo.InvariantCulture) : "");
            if (bound.TryGetValue(d, out Dictionary<string, string> attempt))
            {
                Set(r, DashesHeader, "order", Get(attempt, "order"));
                Set(r, DashesHeader, "character", Get(attempt, "character"));
            }
            rows.Add(r);
        }
        return rows;
    }

    /// <summary>
    /// 대시 궤적 요약(순수) — 의도 거리(속도 × 길이)·실제 거리(시작 → 끝을 대시 방향에 투영)·덜 간 거리. 덜 간 거리 ≥ <see cref="ShortfallMeters"/> 면 막힘.
    /// </summary>
    public static void DashDistances(float speed, float totalSeconds, float startX, float startZ, float endX, float endZ, float dirX, float dirZ,
        out float intent, out float actual, out float shortfall, out bool blocked)
    {
        intent = Math.Max(0f, speed) * Math.Max(0f, totalSeconds);
        float len = (float)Math.Sqrt(dirX * dirX + dirZ * dirZ);
        float dx = endX - startX, dz = endZ - startZ;
        actual = len > 1e-4f ? (dx * dirX + dz * dirZ) / len : (float)Math.Sqrt(dx * dx + dz * dz);
        shortfall = Math.Max(0f, intent - actual);
        blocked = shortfall >= ShortfallMeters;
    }

    // ── 요약(리포트) ─────────────────────────────────────────

    public sealed class Bucket
    {
        public int Attempts, Casts, Judged, Match, Shortened, Blocked;
        public readonly SortedDictionary<string, int> Outcomes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> Mismatch = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public double Accuracy => Judged > 0 ? (double)Match / Judged : double.NaN;

        public void Add(QABoss103CsvTable t, string[] r)
        {
            Attempts++;
            Inc(Outcomes, Or(t.Get(r, "outcome"), "none"));
            if (t.Get(r, "dash_blocked") == "true")
                Blocked++;
            string result = t.Get(r, "result");
            if (result.Length == 0 || result == QABoss116DashPredict.NoCast)
                return;
            Casts++;
            string mm = t.Get(r, "mismatch");
            if (mm.Length > 0 && mm != "none")
                Inc(Mismatch, mm);
            if (mm == "dash_shortened")
                Shortened++;
            if (result == "aborted" || result == "missing")
                return;
            Judged++;
            if (t.Get(r, "match") == "true")
                Match++;
        }
    }

    /// <summary>순서×캐릭터 한 칸 — 묶인 대시의 의도·실제·덜 간 거리 평균과 끊긴 이유(116-C6).</summary>
    public sealed class DistBucket
    {
        public int Attempts, Dashes, Shortened, Transfers;
        public double Intent, Actual, Short;
        public readonly SortedDictionary<string, int> Reasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public double MeanIntent => Dashes > 0 ? Intent / Dashes : double.NaN;
        public double MeanActual => Dashes > 0 ? Actual / Dashes : double.NaN;
        public double MeanShort => Dashes > 0 ? Short / Dashes : double.NaN;
    }

    public sealed class Summary
    {
        public readonly Bucket All = new Bucket();
        public readonly SortedDictionary<string, Bucket> ByOrder = new SortedDictionary<string, Bucket>(StringComparer.Ordinal);
        /// <summary>첫 입력 순간 카운터 창 — in·out·unknown(봇 시도는 값이 없어 unknown). 할 일 116-C5.</summary>
        public readonly SortedDictionary<string, Bucket> ByCounter = new SortedDictionary<string, Bucket>(StringComparer.Ordinal);
        /// <summary>결과 사유 → 상세 원인 → 수.</summary>
        public readonly SortedDictionary<string, SortedDictionary<string, int>> Causes = new SortedDictionary<string, SortedDictionary<string, int>>(StringComparer.Ordinal);
        /// <summary>막힌 대시의 충돌체 → 수.</summary>
        public readonly SortedDictionary<string, int> Blockers = new SortedDictionary<string, int>(StringComparer.Ordinal);
        /// <summary>"순서 × 캐릭터" → 거리 칸(116-C6).</summary>
        public readonly SortedDictionary<string, DistBucket> ByOrderCharacter = new SortedDictionary<string, DistBucket>(StringComparer.Ordinal);
        /// <summary>대시 끊긴 이유 → 순서 → 수(묶인 대시만, 116-C6).</summary>
        public readonly SortedDictionary<string, SortedDictionary<string, int>> StopReasons = new SortedDictionary<string, SortedDictionary<string, int>>(StringComparer.Ordinal);
        public int Windows, WindowsWithAttempt;
        public double WindowMedianS = double.NaN;
    }

    public static Summary Summarize(QABoss103CsvTable attempts, QABoss103CsvTable windows)
    {
        var s = new Summary();
        foreach (string[] r in attempts.Rows)
        {
            s.All.Add(attempts, r);
            string order = Or(attempts.Get(r, "order"), "unknown");
            Of(s.ByOrder, order).Add(attempts, r);
            AddDistance(s, attempts, r, order);
            string open = attempts.Get(r, "counter_open_first");
            Of(s.ByCounter, open == "true" ? "in" : open == "false" ? "out" : "unknown").Add(attempts, r);
            string outcome = Or(attempts.Get(r, "outcome"), "none");
            if (!s.Causes.TryGetValue(outcome, out SortedDictionary<string, int> causes))
                s.Causes[outcome] = causes = new SortedDictionary<string, int>(StringComparer.Ordinal);
            Inc(causes, Or(attempts.Get(r, "cause"), "-"));
            if (attempts.Get(r, "dash_blocked") == "true")
                Inc(s.Blockers, Or(attempts.Get(r, "dash_block_collider"), "unknown"));
        }
        if (windows != null)
        {
            var durs = new List<double>();
            foreach (string[] r in windows.Rows)
            {
                s.Windows++;
                if (windows.Num(r, "attempts") > 0)
                    s.WindowsWithAttempt++;
                double d = windows.Num(r, "dur_s");
                if (!double.IsNaN(d))
                    durs.Add(d);
            }
            if (durs.Count > 0)
            {
                durs.Sort();
                s.WindowMedianS = durs.Count % 2 == 1 ? durs[durs.Count / 2] : (durs[durs.Count / 2 - 1] + durs[durs.Count / 2]) / 2.0;
            }
        }
        return s;
    }

    /// <summary>순서×캐릭터 거리 칸과 끊긴 이유 — 대시가 묶인 시도만 거리·이유를 센다(시도 수는 전부).</summary>
    private static void AddDistance(Summary s, QABoss103CsvTable t, string[] r, string order)
    {
        string k = order + " × " + Or(t.Get(r, "character"), "unknown");
        if (!s.ByOrderCharacter.TryGetValue(k, out DistBucket b))
            s.ByOrderCharacter[k] = b = new DistBucket();
        b.Attempts++;
        double intent = t.Num(r, "dash_intent_m"), actual = t.Num(r, "dash_actual_m"), shortM = t.Num(r, "dash_short_m");
        if (double.IsNaN(intent) || double.IsNaN(actual))
            return;
        b.Dashes++;
        b.Intent += intent;
        b.Actual += actual;
        b.Short += double.IsNaN(shortM) ? Math.Max(0.0, intent - actual) : shortM;
        if (!double.IsNaN(shortM) && shortM >= ShortfallMeters)
            b.Shortened++;
        if (t.Get(r, "dash_transfer") == "true")
            b.Transfers++;
        string reason = Or(t.Get(r, "dash_stop_reason"), "unknown");
        Inc(b.Reasons, reason);
        if (!s.StopReasons.TryGetValue(reason, out SortedDictionary<string, int> byOrder))
            s.StopReasons[reason] = byOrder = new SortedDictionary<string, int>(StringComparer.Ordinal);
        Inc(byOrder, order);
    }

    private static Bucket Of(SortedDictionary<string, Bucket> d, string k)
    {
        if (!d.TryGetValue(k, out Bucket b))
            d[k] = b = new Bucket();
        return b;
    }

    private static void Inc(SortedDictionary<string, int> d, string k) => d[k] = (d.TryGetValue(k, out int n) ? n : 0) + 1;

    // ── 도구 ─────────────────────────────────────────────────

    private static string[] NewRow(string[] header, string[] key)
    {
        var r = new string[header.Length];
        for (int i = 0; i < r.Length; i++)
            r[i] = "";
        for (int i = 0; i < key.Length && i < QABoss103Metrics.KeyHeader.Length; i++)
            r[i] = key[i];
        return r;
    }

    private static void Set(string[] r, string[] header, string col, string v)
    {
        int i = Array.IndexOf(header, col);
        if (i >= 0)
            r[i] = v ?? "";
    }

    private static string[] Concat(string[] a, string[] b)
    {
        var r = new string[a.Length + b.Length];
        a.CopyTo(r, 0);
        b.CopyTo(r, a.Length);
        return r;
    }

    public static string Get(IReadOnlyDictionary<string, string> kv, string k) =>
        kv != null && kv.TryGetValue(k, out string v) && v != "na" ? v ?? "" : "";

    private static int Int(IReadOnlyDictionary<string, string> kv, string k, int fallback) =>
        int.TryParse(Get(kv, k), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

    private static double Dbl(IReadOnlyDictionary<string, string> kv, string k) =>
        double.TryParse(Get(kv, k), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;

    private static bool Bool(IReadOnlyDictionary<string, string> kv, string k, bool fallback)
    {
        string v = Get(kv, k);
        return v == "true" ? true : v == "false" ? false : fallback;
    }

    private static string Or(string v, string fallback) => string.IsNullOrEmpty(v) ? fallback : v;

    public static string F2(double v) => double.IsNaN(v) ? "na" : v.ToString("0.00", CultureInfo.InvariantCulture);
    public static string F3(double v) => double.IsNaN(v) || double.IsInfinity(v) ? "" : v.ToString("0.000", CultureInfo.InvariantCulture);
}

/// <summary>
/// 할 일 116-C4 — 랩 판 설정(<c>&lt;프로젝트 루트&gt;/qa116lab.txt</c>, 배치 메뉴가 판마다 쓴다). 봇 설정 줄(<see cref="QABoss103Config.Format"/>) +
/// <c>run_s</c>(판 상한 초)·<c>max_attempts</c>(시도 상한). 파일이 없으면 랩 씬은 봇 없이(사람이 직접) 돈다. 옛 <c>zone_s</c> 줄은 무시한다(116-C6 벽 구간 제거).
/// </summary>
public sealed class QABoss116LabConfig
{
    public const string FileName = "qa116lab.txt";

    public QABoss103Config Bot = new QABoss103Config { DashInterruptPct = QABoss116Lab.DashInterruptPct };
    public double RunSeconds = QABoss116Lab.RunSeconds;
    public int MaxAttempts = QABoss116Lab.MaxAttempts;

    public string Format() =>
        Bot.Format() +
        $"run_s={RunSeconds.ToString("0.###", CultureInfo.InvariantCulture)}\nmax_attempts={MaxAttempts.ToString(CultureInfo.InvariantCulture)}\n";

    public static bool TryParse(string text, out QABoss116LabConfig cfg)
    {
        cfg = new QABoss116LabConfig();
        if (!QABoss103Config.TryParse(text, out QABoss103Config bot))
            return false;
        cfg.Bot = bot;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            int eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
            if (k == "run_s" && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double rs))
                cfg.RunSeconds = Math.Max(10.0, rs);
            else if (k == "max_attempts" && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ma))
                cfg.MaxAttempts = Math.Max(0, ma);
        }
        return true;
    }
}
#endif
