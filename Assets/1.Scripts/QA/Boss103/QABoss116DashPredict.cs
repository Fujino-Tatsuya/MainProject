#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using BeaverLobby.Player.Dash;

/// <summary>할 일 116 — 봇이 고른 대쉬 간파 순서.</summary>
public enum QABoss116Order
{
    None,      // 일반 간파(대쉬 간파 안 씀)
    DashRmb,   // 대시 → 창 안 우클릭
    RmbDash    // 우클릭(일반 간파 시작) → 창 안 대시
}

/// <summary>
/// 할 일 116 — QA 봇 대쉬 간파 굴림(순수). 간파 창 1개당 1회: 쓸지(<see cref="QABoss103Config.DashInterruptPct"/>) → 순서(반반) → 누름 지연.
/// 지연 = 창(<see cref="PlayerDashData"/> dashInterruptWindow) × [<see cref="DelayMin"/>, <see cref="DelayMax"/>] — 창 경계 밖으로 새지 않게 안쪽만.
/// rng 는 사용 여부와 무관하게 항상 3번 굴린다(창마다 굴림 순서가 같다).
/// </summary>
public static class QABoss116DashPlan
{
    public const double DelayMin = 0.1, DelayMax = 0.7;

    public static QABoss116Order Roll(Random rng, int pct, out double delayFraction)
    {
        double use = rng.NextDouble();
        double order = rng.NextDouble();
        double delay = rng.NextDouble();
        delayFraction = DelayMin + (DelayMax - DelayMin) * delay;
        if (pct <= 0 || use * 100.0 >= pct)
            return QABoss116Order.None;
        return order < 0.5 ? QABoss116Order.DashRmb : QABoss116Order.RmbDash;
    }

    public static string Token(QABoss116Order o) =>
        o == QABoss116Order.DashRmb ? "dash_rmb" : o == QABoss116Order.RmbDash ? "rmb_dash" : "none";
}

/// <summary>
/// 할 일 116 — 대쉬 간파 예측 로그(<c>[DashPredict]</c>, <see cref="DashInterruptPredictLog"/>)와 봇 대쉬 간파 시도(<c>dashint</c>)를 판 파일에서 모아
/// <c>dash_predict.csv</c> 행으로 묶는다(순수). 판 파일 줄 = <c>dp event=… key=value …</c>(로그 줄에서 태그만 뺀 것) · <c>dashint slot=… order=… outcome=…</c>.
/// <list type="bullet">
/// <item>행 기준 = 서버 시전(<c>event=cast side=server</c>) 1건. 같은 castId 의 <c>event=judge</c> 로 실제 결과를 채운다.</item>
/// <item>원격 오너 관점(<c>side=owner</c>)은 새 RPC 없이 같은 clientId + |castServerT 차| ≤ <see cref="OwnerMatchSeconds"/> 중 가장 가까운 것.</item>
/// <item>봇 시도는 같은 clientId + |serverT 차| ≤ <see cref="BotMatchSeconds"/>. 서버 시전과 안 묶인 봇 시도도 행으로 남긴다(result = no_cast).</item>
/// </list>
/// </summary>
public static class QABoss116DashPredict
{
    public const string File = "dash_predict.csv";
    public const string PartKind = "dp";
    public const string BotKind = "dashint";
    public const double OwnerMatchSeconds = 0.5;
    public const double BotMatchSeconds = 1.0;
    public const string NoCast = "no_cast";

    public static readonly string[] Header =
    {
        "run", "retry", "tier", "seed",
        "character", "client_id", "host", "order", "bot_outcome", "bot_delay_s",
        "cast_id", "skill", "predict", "pred_reason", "result", "match", "mismatch",
        "pred_resolve_in_s", "resolve_in_s", "time_err_s", "pred_dash_end_in_s", "dash_end_in_s",
        "pos_err_m", "shortfall_m", "dist_m", "angle_deg",
        "target", "target_in_range_cast", "target_open_cast", "win_open_for_s", "win_exp_dur_s", "win_remain_s", "win_src",
        "win_close_after_cast_s", "target_open_at_resolve", "target_move_m", "target_yaw_deg", "prep_s", "prep_src", "rtt_ms",
        "owner_cast_id", "owner_predict", "owner_pred_reason", "owner_match", "owner_dash_end_pos_err_m", "owner_cast_dt_s",
        "server_t",
    };

    /// <summary>로그 줄 → 판 파일 줄(<c>dp …</c>). 태그가 없으면 null.</summary>
    public static string ToPartLine(string logLine)
    {
        if (string.IsNullOrEmpty(logLine) || !logLine.StartsWith(DashInterruptPredictor.Tag, StringComparison.Ordinal))
            return null;
        return PartKind + logLine.Substring(DashInterruptPredictor.Tag.Length);
    }

    private sealed class Entry
    {
        public Dictionary<string, string> Cast, Judge, HandOff, Abort;
        public int ClientId;
        public double CastServerT;
        public bool Used;
    }

    private sealed class Bot
    {
        public Dictionary<string, string> Kv;
        public int ClientId;
        public double ServerT;
        public bool Used;
    }

    /// <summary>
    /// 판 파일 줄들(모든 자리) → CSV 행. <paramref name="key"/> = 판 열(run·retry·tier·seed).
    /// <paramref name="characterOfClient"/> 는 봇 줄의 clientId → 캐릭터로 채운다(없으면 skill 이름).
    /// </summary>
    public static List<string[]> BuildRows(string[] key, IEnumerable<string> lines)
    {
        var server = new Dictionary<string, Entry>(StringComparer.Ordinal);
        var owner = new Dictionary<string, Entry>(StringComparer.Ordinal);
        var serverOrder = new List<Entry>();
        var bots = new List<Bot>();
        var character = new Dictionary<int, string>();

        foreach (string line in lines)
        {
            if (!QABoss103PartFile.TryParse(line, out string kind, out Dictionary<string, string> kv))
                continue;
            if (kind == BotKind)
            {
                var b = new Bot { Kv = kv, ClientId = Int(kv, "clientId"), ServerT = Dbl(kv, "serverT") };
                bots.Add(b);
                string ch = Get(kv, "character");
                if (ch.Length > 0)
                    character[b.ClientId] = ch;
                continue;
            }
            if (kind != PartKind)
                continue;

            string id = Get(kv, "castId");
            if (id.Length == 0)
                continue;
            bool isServer = Get(kv, "side") == "server";
            Dictionary<string, Entry> map = isServer ? server : owner;
            if (!map.TryGetValue(id, out Entry e))
            {
                e = new Entry { ClientId = Int(kv, "clientId"), CastServerT = Dbl(kv, "castServerT") };
                map[id] = e;
                if (isServer)
                    serverOrder.Add(e);
            }
            switch (Get(kv, "event"))
            {
                case "cast": e.Cast = kv; break;
                case "judge": e.Judge = kv; break;
                case "handoff": e.HandOff = kv; break;
                case "abort": e.Abort = kv; break;
            }
        }

        var rows = new List<string[]>();
        foreach (Entry s in serverOrder)
        {
            if (s.Cast == null)
                continue;
            Entry o = Closest(owner.Values, s.ClientId, s.CastServerT, OwnerMatchSeconds, e => e.Cast != null);
            if (o != null)
                o.Used = true;
            Bot b = ClosestBot(bots, s.ClientId, s.CastServerT);
            if (b != null)
                b.Used = true;
            rows.Add(Row(key, s, o, b, character));
        }

        foreach (Bot b in bots)
        {
            if (b.Used)
                continue;
            rows.Add(Row(key, null, null, b, character));
        }
        return rows;
    }

    private static Entry Closest(IEnumerable<Entry> pool, int clientId, double t, double limit, Func<Entry, bool> ok)
    {
        Entry best = null;
        double bestDt = double.MaxValue;
        foreach (Entry e in pool)
        {
            if (e.Used || e.ClientId != clientId || !ok(e) || double.IsNaN(e.CastServerT) || double.IsNaN(t))
                continue;
            double dt = Math.Abs(e.CastServerT - t);
            if (dt <= limit && dt < bestDt)
            {
                best = e;
                bestDt = dt;
            }
        }
        return best;
    }

    private static Bot ClosestBot(List<Bot> bots, int clientId, double t)
    {
        Bot best = null;
        double bestDt = double.MaxValue;
        foreach (Bot b in bots)
        {
            if (b.Used || b.ClientId != clientId || double.IsNaN(b.ServerT) || double.IsNaN(t))
                continue;
            double dt = Math.Abs(b.ServerT - t);
            if (dt <= BotMatchSeconds && dt < bestDt)
            {
                best = b;
                bestDt = dt;
            }
        }
        return best;
    }

    private static string[] Row(string[] key, Entry s, Entry o, Bot b, Dictionary<int, string> character)
    {
        var r = new string[Header.Length];
        for (int i = 0; i < r.Length; i++)
            r[i] = "";
        for (int i = 0; i < key.Length && i < 4; i++)
            r[i] = key[i];

        Dictionary<string, string> cast = s?.Cast, judge = s?.Judge;
        int clientId = s != null ? s.ClientId : b.ClientId;
        Set(r, "client_id", clientId.ToString(CultureInfo.InvariantCulture));
        Set(r, "character", character.TryGetValue(clientId, out string ch) ? ch : CharacterFromSkill(Get(cast, "skill")));
        Set(r, "host", cast != null ? Get(cast, "host") : (clientId == 0 ? "true" : "false"));
        Set(r, "order", cast != null ? Get(cast, "order") : Get(b?.Kv, "order"));
        Set(r, "bot_outcome", b != null ? Get(b.Kv, "outcome") : "none");
        Set(r, "bot_delay_s", Get(b?.Kv, "delayS"));
        Set(r, "server_t", s != null ? Get(cast, "castServerT") : Get(b?.Kv, "serverT"));
        if (s == null)
        {
            Set(r, "result", NoCast);
            return r;
        }

        Set(r, "cast_id", Get(cast, "castId"));
        Set(r, "skill", Get(cast, "skill"));
        Set(r, "predict", Get(cast, "predict"));
        Set(r, "pred_reason", Get(cast, "predReason"));
        Set(r, "result", judge != null ? Get(judge, "result") : "missing");
        Set(r, "match", Get(judge, "match"));
        Set(r, "mismatch", Get(judge, "mismatch"));
        Set(r, "pred_resolve_in_s", Get(cast, "predResolveInS"));
        Set(r, "resolve_in_s", Get(judge, "resolveInS"));
        Set(r, "time_err_s", Get(judge, "timeErrS"));
        Set(r, "pred_dash_end_in_s", Get(cast, "predDashEndInS"));
        Set(r, "dash_end_in_s", Get(judge, "dashEndInS"));
        Set(r, "pos_err_m", Get(judge, "posErrM"));
        Set(r, "shortfall_m", Get(judge, "shortfallM"));
        Set(r, "dist_m", Get(cast, "distM"));
        Set(r, "angle_deg", Get(cast, "angleDeg"));
        Set(r, "target", Get(cast, "target"));
        Set(r, "target_in_range_cast", Get(cast, "targetInRange"));
        Set(r, "target_open_cast", Get(cast, "targetOpen"));
        Set(r, "win_open_for_s", Get(cast, "winOpenForS"));
        Set(r, "win_exp_dur_s", Get(cast, "winExpDurS"));
        Set(r, "win_remain_s", Get(cast, "winRemainS"));
        Set(r, "win_src", Get(cast, "winSrc"));
        Set(r, "win_close_after_cast_s", Get(judge, "winCloseAfterCastS"));
        Set(r, "target_open_at_resolve", Get(judge, "targetOpenAtResolve"));
        Set(r, "target_move_m", Get(judge, "targetMoveM"));
        Set(r, "target_yaw_deg", Get(judge, "targetYawDeg"));
        Set(r, "prep_s", Get(cast, "prepS"));
        Set(r, "prep_src", Get(cast, "prepSrc"));
        Set(r, "rtt_ms", Get(judge, "rttMs"));
        if (o != null)
        {
            Set(r, "owner_cast_id", Get(o.Cast, "castId"));
            Set(r, "owner_predict", Get(o.Cast, "predict"));
            Set(r, "owner_pred_reason", Get(o.Cast, "predReason"));
            string result = Get(judge, "result");
            if (result.Length > 0 && result != "aborted")
                Set(r, "owner_match", (Get(o.Cast, "predict") == "success") == (result == "success") ? "true" : "false");
            Set(r, "owner_dash_end_pos_err_m", Get(o.HandOff, "posErrM"));
            Set(r, "owner_cast_dt_s", (o.CastServerT - s.CastServerT).ToString("0.000", CultureInfo.InvariantCulture));
        }
        return r;
    }

    /// <summary>스킬 클래스 이름 → 캐릭터(봇 줄이 없을 때 대체).</summary>
    public static string CharacterFromSkill(string skill)
    {
        if (string.IsNullOrEmpty(skill))
            return "";
        if (skill.IndexOf("Assassin", StringComparison.OrdinalIgnoreCase) >= 0)
            return QABoss103Character.Assassin.ToString();
        if (skill.IndexOf("Gunner", StringComparison.OrdinalIgnoreCase) >= 0)
            return QABoss103Character.Gunner.ToString();
        return QABoss103Character.Paladin.ToString();
    }

    private static void Set(string[] r, string col, string v)
    {
        int i = Array.IndexOf(Header, col);
        if (i >= 0)
            r[i] = v ?? "";
    }

    private static string Get(Dictionary<string, string> kv, string k) =>
        kv != null && kv.TryGetValue(k, out string v) ? v : "";

    private static int Int(Dictionary<string, string> kv, string k) =>
        int.TryParse(Get(kv, k), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : -1;

    private static double Dbl(Dictionary<string, string> kv, string k) =>
        double.TryParse(Get(kv, k), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v >= 0 ? v : double.NaN;

    // ── 요약(리포트) ─────────────────────────────────────────

    /// <summary>적중률 묶음 하나. 판정 = result 가 success·fail·no_target 인 행(중단·시전 없음 제외).</summary>
    public sealed class Tally
    {
        public int Rows, Judged, Match, PredSuccessActualFail, PredFailActualSuccess, Aborted, NoCast, Missing;
        public int OwnerJudged, OwnerMatch;
        public double Accuracy => Judged > 0 ? (double)Match / Judged : double.NaN;
        public double OwnerAccuracy => OwnerJudged > 0 ? (double)OwnerMatch / OwnerJudged : double.NaN;

        public void Add(QABoss103CsvTable t, string[] r)
        {
            Rows++;
            string result = t.Get(r, "result");
            if (result == QABoss116DashPredict.NoCast) { NoCast++; return; }
            if (result == "aborted") { Aborted++; return; }
            if (result == "missing" || result.Length == 0) { Missing++; return; }
            Judged++;
            bool predSuccess = t.Get(r, "predict") == "success";
            bool actualSuccess = result == "success";
            if (predSuccess == actualSuccess)
                Match++;
            else if (predSuccess)
                PredSuccessActualFail++;
            else
                PredFailActualSuccess++;
            string om = t.Get(r, "owner_match");
            if (om.Length > 0)
            {
                OwnerJudged++;
                if (om == "true")
                    OwnerMatch++;
            }
        }
    }

    public sealed class Summary
    {
        public readonly Tally All = new Tally();
        public readonly SortedDictionary<string, Tally> ByOrder = new SortedDictionary<string, Tally>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, Tally> ByCharacter = new SortedDictionary<string, Tally>(StringComparer.Ordinal);
        public readonly Dictionary<string, int> Mismatch = new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly Dictionary<string, int> BotOutcome = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>어긋남 이유 많은 순(같으면 이름순).</summary>
        public List<KeyValuePair<string, int>> TopMismatch()
        {
            var list = new List<KeyValuePair<string, int>>(Mismatch);
            list.Sort((a, b) => a.Value != b.Value ? b.Value.CompareTo(a.Value) : string.CompareOrdinal(a.Key, b.Key));
            return list;
        }
    }

    public static Summary Summarize(QABoss103CsvTable t)
    {
        var s = new Summary();
        foreach (string[] r in t.Rows)
        {
            s.All.Add(t, r);
            Bucket(s.ByOrder, Or(t.Get(r, "order"), "unknown")).Add(t, r);
            Bucket(s.ByCharacter, Or(t.Get(r, "character"), "unknown")).Add(t, r);
            string mm = t.Get(r, "mismatch");
            if (mm.Length > 0 && mm != "none")
                s.Mismatch[mm] = (s.Mismatch.TryGetValue(mm, out int n) ? n : 0) + 1;
            string bo = Or(t.Get(r, "bot_outcome"), "none");
            s.BotOutcome[bo] = (s.BotOutcome.TryGetValue(bo, out int m) ? m : 0) + 1;
        }
        return s;
    }

    private static Tally Bucket(SortedDictionary<string, Tally> d, string k)
    {
        if (!d.TryGetValue(k, out Tally t))
            d[k] = t = new Tally();
        return t;
    }

    private static string Or(string v, string fallback) => string.IsNullOrEmpty(v) ? fallback : v;
}
#endif
