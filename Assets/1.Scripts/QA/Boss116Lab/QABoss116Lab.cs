#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// 할 일 116-C4 — 대쉬 간파 예측 랩(호스트 1명 · 전용 디버그 씬 <c>DebugDashPredictLab</c>)의 순수 규칙(EditMode <c>QABoss116LabTests</c>).
/// <list type="bullet">
/// <item>랩 설정 파일(<see cref="QABoss116LabConfig"/>) — 봇 설정(<see cref="QABoss103Config"/>)에 판 길이·시도 상한·구간 교대 간격을 더한다.</item>
/// <item>구간(<see cref="Zone"/>) — 시도 순간 내 몸에서 가장 가까운 Wall/Env 까지 거리로 벽 근처/열린 쪽을 가른다.</item>
/// <item>봇 시도 결과 사유 7종 + 나머지의 상세 원인(<see cref="Cause"/>) — 봇이 판 파일 <c>dashint</c> 줄에 붙인 판단 근거 값만으로 정한다.</item>
/// <item>판 파일 줄(<c>dashint</c>·<c>dp</c>·<c>labwin</c>·<c>labdash</c>) → CSV 3종(<see cref="BuildAttempts"/>·<see cref="BuildWindows"/>·<see cref="BuildDashes"/>)과 요약(<see cref="Summarize"/>).</item>
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
    /// <summary>벽 구간 ↔ 열린 구간 교대 간격(실제 시간 초).</summary>
    public const double ZoneSeconds = 30.0;
    /// <summary>랩은 간파 창마다 대쉬 간파(%).</summary>
    public const int DashInterruptPct = 100;
    /// <summary>이 거리(m) 안에 Wall/Env 표면이 있으면 벽 근처.</summary>
    public const float WallNearMeters = 3.0f;

    public const string ZoneWall = "wall";
    public const string ZoneOpen = "open";

    // 판 파일 줄 종류(랩 디렉터가 parts/run<n>_r<retry>_lab.txt 에 쓴다)
    public const string WindowKind = "labwin";
    public const string DashKind = "labdash";
    public const string ZoneKind = "labzone";
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

    /// <summary>벽까지 거리(m, 모름 = 음수) → 구간.</summary>
    public static string Zone(float wallMeters) => wallMeters >= 0f && wallMeters <= WallNearMeters ? ZoneWall : ZoneOpen;

    /// <summary>이번 구간(판 시작부터 실제 초) — 짝수 칸 = 벽, 홀수 칸 = 열린 쪽(첫 칸 벽).</summary>
    public static string PlannedZone(double elapsed, double zoneSeconds) =>
        zoneSeconds <= 0.0 || double.IsNaN(elapsed) || ((long)Math.Floor(Math.Max(0.0, elapsed) / zoneSeconds)) % 2 == 0 ? ZoneWall : ZoneOpen;

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
                return $"press_at_{F2(Dbl(kv, "dashAgeS"))}s_of_{F2(Dbl(kv, "windowS"))}s";
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
        "n", "character", "order", "outcome", "cause", "zone", "zone_planned", "wall_m", "wall_name", "boss_m", "attack", "boss_state",
        "server_t", "first_t", "delay_s", "window_s", "dash_age_s", "dash_total_s", "wait_s",
        "charge", "max_charge", "next_charge_s", "state", "end_state", "can_move", "can_dash_act", "grounded", "input_src", "dash_on",
        "skill_busy", "active_skill", "int_cd_s", "seen", "dash_log",
        "win_n", "win_open_t", "win_close_t", "win_dur_s", "t_from_open_s", "t_to_close_s",
        "dash_start_t", "dash_intent_m", "dash_actual_m", "dash_short_m", "dash_blocked", "dash_block_pos", "dash_block_collider", "dash_block_layer", "dash_block_at_s",
        "cast_id", "predict", "pred_reason", "result", "match", "mismatch", "shortfall_m", "pos_err_m", "time_err_s",
        "pred_resolve_in_s", "resolve_in_s", "win_close_after_cast_s", "target_open_at_resolve",
    });

    public static readonly string[] WindowsHeader = Concat(QABoss103Metrics.KeyHeader, new[]
    {
        "win_n", "open_t", "close_t", "dur_s", "attack", "boss_state", "zone_planned", "player_wall_m", "boss_m", "attempts", "outcomes",
    });

    public static readonly string[] DashesHeader = Concat(QABoss103Metrics.KeyHeader, new[]
    {
        "dash_n", "start_t", "end_t", "start_pos", "end_pos", "dir", "speed", "intent_m", "actual_m", "short_m", "blocked",
        "block_pos", "block_collider", "block_layer", "block_at_s", "wall_m", "zone", "end_state", "attached", "attempt_n",
    });

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
            float wall = (float)Dbl(b, "wallM");
            Set(r, AttemptsHeader, "zone", Get(b, "wallM").Length > 0 ? Zone(float.IsNaN(wall) ? -1f : wall) : "");
            Copy(r, b, "zone_planned", "zonePlanned");
            Copy(r, b, "wall_m", "wallM");
            Copy(r, b, "wall_name", "wallName");
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
                Copy(r, d.Kv, "dash_intent_m", "intentM");
                Copy(r, d.Kv, "dash_actual_m", "actualM");
                Copy(r, d.Kv, "dash_short_m", "shortM");
                Copy(r, d.Kv, "dash_blocked", "blocked");
                Copy(r, d.Kv, "dash_block_pos", "blockPos");
                Copy(r, d.Kv, "dash_block_collider", "blockCollider");
                Copy(r, d.Kv, "dash_block_layer", "blockLayer");
                Copy(r, d.Kv, "dash_block_at_s", "blockAtS");
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
            Set(r, WindowsHeader, "zone_planned", Get(w.Kv, "zonePlanned"));
            Set(r, WindowsHeader, "player_wall_m", Get(w.Kv, "wallM"));
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
            foreach ((string col, string k) in new[]
                     {
                         ("start_pos", "startPos"), ("end_pos", "endPos"), ("dir", "dir"), ("speed", "speed"), ("intent_m", "intentM"), ("actual_m", "actualM"),
                         ("short_m", "shortM"), ("blocked", "blocked"), ("block_pos", "blockPos"), ("block_collider", "blockCollider"), ("block_layer", "blockLayer"),
                         ("block_at_s", "blockAtS"), ("wall_m", "wallM"), ("end_state", "endState"), ("attached", "attached"),
                     })
                Set(r, DashesHeader, col, Get(d.Kv, k));
            float wall = (float)Dbl(d.Kv, "wallM");
            Set(r, DashesHeader, "zone", Get(d.Kv, "wallM").Length > 0 ? Zone(float.IsNaN(wall) ? -1f : wall) : "");
            Set(r, DashesHeader, "attempt_n", d.Attempt > 0 ? d.Attempt.ToString(CultureInfo.InvariantCulture) : "");
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

    public sealed class Summary
    {
        public readonly Bucket All = new Bucket();
        public readonly SortedDictionary<string, Bucket> ByZone = new SortedDictionary<string, Bucket>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, Bucket> ByOrder = new SortedDictionary<string, Bucket>(StringComparer.Ordinal);
        /// <summary>결과 사유 → 상세 원인 → 수.</summary>
        public readonly SortedDictionary<string, SortedDictionary<string, int>> Causes = new SortedDictionary<string, SortedDictionary<string, int>>(StringComparer.Ordinal);
        /// <summary>막힌 대시의 충돌체 → 수.</summary>
        public readonly SortedDictionary<string, int> Blockers = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public int Windows, WindowsWithAttempt;
        public double WindowMedianS = double.NaN;
    }

    public static Summary Summarize(QABoss103CsvTable attempts, QABoss103CsvTable windows)
    {
        var s = new Summary();
        foreach (string[] r in attempts.Rows)
        {
            s.All.Add(attempts, r);
            Of(s.ByZone, Or(attempts.Get(r, "zone"), "unknown")).Add(attempts, r);
            Of(s.ByOrder, Or(attempts.Get(r, "order"), "unknown")).Add(attempts, r);
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
/// <c>run_s</c>(판 상한 초)·<c>max_attempts</c>(시도 상한)·<c>zone_s</c>(구간 교대 초). 파일이 없으면 랩 씬은 봇 없이(사람이 직접) 돈다.
/// </summary>
public sealed class QABoss116LabConfig
{
    public const string FileName = "qa116lab.txt";

    public QABoss103Config Bot = new QABoss103Config { DashInterruptPct = QABoss116Lab.DashInterruptPct };
    public double RunSeconds = QABoss116Lab.RunSeconds;
    public int MaxAttempts = QABoss116Lab.MaxAttempts;
    public double ZoneSeconds = QABoss116Lab.ZoneSeconds;

    public string Format() =>
        Bot.Format() +
        $"run_s={RunSeconds.ToString("0.###", CultureInfo.InvariantCulture)}\nmax_attempts={MaxAttempts.ToString(CultureInfo.InvariantCulture)}\n" +
        $"zone_s={ZoneSeconds.ToString("0.###", CultureInfo.InvariantCulture)}\n";

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
            else if (k == "zone_s" && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double zs))
                cfg.ZoneSeconds = Math.Max(0.0, zs);
        }
        return true;
    }
}
#endif
