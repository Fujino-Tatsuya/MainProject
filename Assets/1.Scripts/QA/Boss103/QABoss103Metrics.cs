using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>할 일 103 C4 — 플레이어 행동 종류(평타·Q·E·R·우클릭). CSV 열 접두어 = <see cref="QABoss103Metrics.ActionPrefix"/>.</summary>
public enum QABoss103Action
{
    Basic = 0,
    Q = 1,
    E = 2,
    R = 3,
    RClick = 4,
}

/// <summary>서버 피격 1건(같은 캐릭터·같은 패턴이 <see cref="QABoss103Metrics.HitMergeSeconds"/> 안에 겹치면 1건).</summary>
public sealed class QABoss103Hit
{
    public int Slot;
    public double T;
    public string Pattern;
}

/// <summary>봇이 굴린 회피 1건(봇 판 파일 <c>threat</c> 줄).</summary>
public sealed class QABoss103Roll
{
    public int Slot;
    public string Kind;
    public string Attack = "na";
    public double ServerT;
    public bool Dodge;
    public string Action = "none";
    public string Ok = "na";
    public string Note = "none";
    /// <summary>판 끝에 정해지는 패턴 키(<see cref="QABoss103Metrics.PatternOfRoll"/>).</summary>
    public string Pattern;
}

/// <summary>보스 기믹 발생 1건(run_gimmicks.csv 한 줄).</summary>
public sealed class QABoss103GimmickEvent
{
    public string Gimmick;
    /// <summary>success·fail·timeout·partial·ended.</summary>
    public string Result;
    /// <summary>보스 전투 기록 기준 초(개전부터). 모르면 NaN.</summary>
    public double FightT = double.NaN;
    /// <summary>서버 시각(초). 로그 출처는 Combat 진입 서버 시각 + <see cref="FightT"/> 근사.</summary>
    public double ServerT = double.NaN;
    /// <summary>log(전투 기록 파싱) · event(C5 게임 코드 알림).</summary>
    public string Source = "log";
    public string Note = "";
}

/// <summary>판 × 캐릭터 집계(서버 + 봇 판 파일).</summary>
public sealed class QABoss103CharStats
{
    public readonly QABoss103Character Character;
    public long DamageTaken;
    public int Deaths;
    public long DamageDealt;
    public readonly int[] Uses = new int[5];
    public readonly int[] Hits = new int[5];
    public readonly long[] TypedDamage = new long[5];
    public int TransformCount;
    public double TransformSeconds;
    public long DamageNormal, DamageTransformed;
    public int CounterSuccess;
    public long DamageDuringSuppress;
    /// <summary>C5 — 팔라딘 방어 성공(게임 코드 알림). null = 아직 출처 없음(빈칸).</summary>
    public int? GuardSuccess;

    // 봇 판 파일(없으면 PartPresent=false → 열 비움)
    public bool PartPresent;
    public bool PartDone;
    public int CounterTries;
    public int EvadeDodge, EvadeTake, EvadeNoCharge, EvadeBlocked, EvadeExpired;

    public int HitsTaken;
    /// <summary>굴림 없는 피격(감지 못한 위험). 판 파일이 없으면 -1.</summary>
    public int HitsUnrolled = -1;

    internal bool Transformed;
    internal double TransformSince;

    public QABoss103CharStats(QABoss103Character character) => Character = character;
}

/// <summary>
/// 할 일 103 C4 판 단위 순수 집계(EditMode <c>QABoss103MetricsTests</c>) — Unity·네트워크 의존 없음.
/// 입력은 서버 계측기(<c>QABoss103Recorder</c>)의 호출과 봇 판 파일 줄, 출력은 CSV 4종의 행.
/// 시간은 모두 같은 시계(호스트 <c>NetworkManager.ServerTime</c>, 초)다. 전투 시간 = <see cref="Begin"/>(Combat 진입) ~ <see cref="End"/>.
/// DPS·분당 값은 그 시간 기준(사망·영혼 시간 포함 단순 시간).
/// </summary>
public sealed class QABoss103RunStats
{
    public readonly QABoss103CharStats[] Chars =
    {
        new QABoss103CharStats(QABoss103Character.Paladin),
        new QABoss103CharStats(QABoss103Character.Assassin),
        new QABoss103CharStats(QABoss103Character.Gunner),
    };

    public double CombatStart = double.NaN;
    public double CombatEnd = double.NaN;
    public int Wipes;
    public int LivesUsed;
    public int SuppressCount;
    public readonly List<QABoss103Hit> Hits = new List<QABoss103Hit>();
    /// <summary>사망(캐릭터, 시각, 귀속 패턴).</summary>
    public readonly List<QABoss103Hit> Deaths = new List<QABoss103Hit>();
    public readonly List<KeyValuePair<double, string>> AttackStarts = new List<KeyValuePair<double, string>>();
    public readonly List<QABoss103Roll> Rolls = new List<QABoss103Roll>();
    public readonly List<QABoss103GimmickEvent> Gimmicks = new List<QABoss103GimmickEvent>();

    private int _lives = -1;
    private bool _suppressed;

    public double Duration => double.IsNaN(CombatStart) || double.IsNaN(CombatEnd) ? double.NaN : Math.Max(0.0, CombatEnd - CombatStart);
    public bool Started => !double.IsNaN(CombatStart);

    private QABoss103CharStats C(int slot) => slot >= 0 && slot < Chars.Length ? Chars[slot] : null;

    public void Begin(double t) => CombatStart = t;

    /// <summary>판 끝 — 열린 변신 구간을 닫는다.</summary>
    public void End(double t)
    {
        CombatEnd = t;
        foreach (QABoss103CharStats c in Chars)
            if (c.Transformed)
            {
                c.TransformSeconds += Math.Max(0.0, t - c.TransformSince);
                c.Transformed = false;
            }
    }

    // ── 서버 입력 ─────────────────────────────────────────────

    /// <summary>보스에게 실제 들어간 피해(HP+실드, <c>ServerDamageApplied</c>). 제압 중·어쌔신 변신 중이면 따로 더한다.</summary>
    public void DamageDealt(int slot, int amount, bool bossSuppressed)
    {
        QABoss103CharStats c = C(slot);
        if (c == null || amount <= 0)
            return;
        c.DamageDealt += amount;
        if (bossSuppressed)
            c.DamageDuringSuppress += amount;
        if (c.Transformed)
            c.DamageTransformed += amount;
        else
            c.DamageNormal += amount;
    }

    /// <summary>행동 종류별 피해(<c>ClientDamagedAttributed</c> — 방어 뒤·체력 클램프 전).</summary>
    public void TypedDamage(int slot, QABoss103Action action, int amount)
    {
        QABoss103CharStats c = C(slot);
        if (c != null && amount > 0)
            c.TypedDamage[(int)action] += amount;
    }

    public void Use(int slot, QABoss103Action action)
    {
        QABoss103CharStats c = C(slot);
        if (c != null)
            c.Uses[(int)action]++;
    }

    /// <summary>공격 판정 1회가 적을 맞혔다(<c>ServerAttackLanded</c>).</summary>
    public void Landed(int slot, QABoss103Action action)
    {
        QABoss103CharStats c = C(slot);
        if (c != null)
            c.Hits[(int)action]++;
    }

    public void HealthLost(int slot, int amount)
    {
        QABoss103CharStats c = C(slot);
        if (c != null && amount > 0)
            c.DamageTaken += amount;
    }

    /// <summary>플레이어 피격(서버). 같은 캐릭터·같은 패턴이 <see cref="QABoss103Metrics.HitMergeSeconds"/> 안이면 같은 1건(다단·틱).</summary>
    public void PlayerHit(int slot, double t, string pattern)
    {
        QABoss103CharStats c = C(slot);
        if (c == null)
            return;
        pattern = string.IsNullOrEmpty(pattern) ? QABoss103Metrics.Unknown : pattern;
        for (int i = Hits.Count - 1; i >= 0; i--)
        {
            QABoss103Hit h = Hits[i];
            if (t - h.T > QABoss103Metrics.HitMergeSeconds)
                break;
            if (h.Slot == slot && h.Pattern == pattern)
            {
                h.T = t;   // 이어지는 틱은 마지막 시각으로 — 사망 귀속이 최근 피격을 본다
                return;
            }
        }
        Hits.Add(new QABoss103Hit { Slot = slot, T = t, Pattern = pattern });
        c.HitsTaken++;
    }

    /// <summary>사망(Alive → 그 밖). 직전 <see cref="QABoss103Metrics.DeathWindowSeconds"/> 안 마지막 피격 패턴에 귀속, 없으면 unknown.</summary>
    public void Death(int slot, double t)
    {
        QABoss103CharStats c = C(slot);
        if (c == null)
            return;
        c.Deaths++;
        string pattern = QABoss103Metrics.Unknown;
        for (int i = Hits.Count - 1; i >= 0; i--)
        {
            QABoss103Hit h = Hits[i];
            if (h.Slot != slot || h.T > t + 1e-6)
                continue;
            if (t - h.T <= QABoss103Metrics.DeathWindowSeconds)
                pattern = h.Pattern;
            break;
        }
        Deaths.Add(new QABoss103Hit { Slot = slot, T = t, Pattern = pattern });
    }

    public void CounterSuccess(int slot)
    {
        QABoss103CharStats c = C(slot);
        if (c != null)
            c.CounterSuccess++;
    }

    /// <summary>보스 제압 상태 폴링 — 상승 엣지마다 1회.</summary>
    public void Suppressed(bool now)
    {
        if (now && !_suppressed)
            SuppressCount++;
        _suppressed = now;
    }

    /// <summary>어쌔신 변신 상태 폴링/이벤트 — 켜짐 엣지 = 1회, 켜진 시간 누적.</summary>
    public void Transform(int slot, bool on, double t)
    {
        QABoss103CharStats c = C(slot);
        if (c == null || on == c.Transformed)
            return;
        if (on)
        {
            c.TransformCount++;
            c.TransformSince = t;
        }
        else
        {
            c.TransformSeconds += Math.Max(0.0, t - c.TransformSince);
        }
        c.Transformed = on;
    }

    /// <summary>공용 목숨 값(<c>PartyLivesChanged</c>). 줄어든 양만 소모로 센다(전멸 리스폰 리필은 무시).</summary>
    public void Lives(int lives)
    {
        if (_lives >= 0 && lives < _lives)
            LivesUsed += _lives - lives;
        _lives = lives;
    }

    public void Wipe() => Wipes++;

    /// <summary>[서버] 보스 공격 시작(<c>_currentEntry.attackId</c>) — 클라 봇 굴림을 공격 id 로 잇는 시간표.</summary>
    public void AttackStart(double t, string attackId)
    {
        if (!string.IsNullOrEmpty(attackId))
            AttackStarts.Add(new KeyValuePair<double, string>(t, attackId));
    }

    // ── 봇 판 파일 ────────────────────────────────────────────

    /// <summary>봇 판 파일 줄을 넣는다(<see cref="QABoss103PartFile"/> 형식). 파일이 있으면 그 캐릭터 열을 채운다.</summary>
    public void AddPart(int slot, IEnumerable<string> lines)
    {
        QABoss103CharStats c = C(slot);
        if (c == null)
            return;
        c.PartPresent = true;
        foreach (string line in lines)
        {
            if (!QABoss103PartFile.TryParse(line, out string kind, out Dictionary<string, string> kv))
                continue;
            switch (kind)
            {
                case "threat":
                    var r = new QABoss103Roll
                    {
                        Slot = slot,
                        Kind = Get(kv, "pattern", "generic"),
                        Attack = Get(kv, "attack", "na"),
                        ServerT = ParseD(Get(kv, "serverT", "")),
                        Dodge = Get(kv, "roll", "take") == "dodge",
                        Action = Get(kv, "action", "none"),
                        Ok = Get(kv, "ok", "na"),
                        Note = Get(kv, "note", "none"),
                    };
                    Rolls.Add(r);
                    if (!r.Dodge)
                        c.EvadeTake++;
                    else if (r.Ok == "true")
                        c.EvadeDodge++;
                    if (r.Action == "no_charge")
                        c.EvadeNoCharge++;
                    else if (r.Action == "blocked")
                        c.EvadeBlocked++;
                    if (r.Note == "expired")
                        c.EvadeExpired++;
                    break;
                case "counter":
                    // 게임이 시전을 거부한 누름(쿨·상태)은 시도가 아니다.
                    if (Get(kv, "cast", "true") != "false")
                        c.CounterTries++;
                    break;
                case "done":
                    c.PartDone = true;
                    break;
            }
        }
    }

    private static string Get(Dictionary<string, string> kv, string key, string fallback) =>
        kv.TryGetValue(key, out string v) && v.Length > 0 ? v : fallback;

    private static double ParseD(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;

    // ── 판 끝 계산 ────────────────────────────────────────────

    /// <summary>굴림 → 패턴 키, 캐릭터별 굴림 없는 피격. CSV 쓰기 전에 1회.</summary>
    public void Finish()
    {
        foreach (QABoss103Roll r in Rolls)
            r.Pattern = QABoss103Metrics.PatternOfRoll(r.Kind, r.Attack, r.ServerT, AttackStarts);
        for (int s = 0; s < Chars.Length; s++)
        {
            QABoss103CharStats c = Chars[s];
            if (!c.PartPresent)
            {
                c.HitsUnrolled = -1;
                continue;
            }
            int n = 0;
            foreach (QABoss103Hit h in Hits)
                if (h.Slot == s && !QABoss103Metrics.HasRollFor(h, Rolls))
                    n++;
            c.HitsUnrolled = n;
        }
    }

    public int CounterTriesTotal(out bool complete)
    {
        complete = true;
        int n = 0;
        foreach (QABoss103CharStats c in Chars)
        {
            if (!c.PartPresent)
                complete = false;
            n += c.CounterTries;
        }
        return n;
    }

    public int CounterSuccessTotal()
    {
        int n = 0;
        foreach (QABoss103CharStats c in Chars)
            n += c.CounterSuccess;
        return n;
    }

    public int DeathsTotal()
    {
        int n = 0;
        foreach (QABoss103CharStats c in Chars)
            n += c.Deaths;
        return n;
    }

    public long DamageDealtTotal()
    {
        long n = 0;
        foreach (QABoss103CharStats c in Chars)
            n += c.DamageDealt;
        return n;
    }

    public int RolledTake()
    {
        int n = 0;
        foreach (QABoss103Roll r in Rolls)
            if (!r.Dodge)
                n++;
        return n;
    }

    /// <summary>판 파일이 없거나(missing) 끝 표시가 없는(partial) 캐릭터 — runs.csv note.</summary>
    public string PartsNote()
    {
        var missing = new List<string>();
        var partial = new List<string>();
        foreach (QABoss103CharStats c in Chars)
        {
            if (!c.PartPresent)
                missing.Add(c.Character.ToString());
            else if (!c.PartDone)
                partial.Add(c.Character.ToString());
        }
        var sb = new StringBuilder();
        if (missing.Count > 0)
            sb.Append("missing_parts=").Append(string.Join("+", missing));
        if (partial.Count > 0)
            sb.Append(sb.Length > 0 ? " " : "").Append("partial_parts=").Append(string.Join("+", partial));
        return sb.ToString();
    }

    /// <summary>패턴별 [굴림, 굴림 맞기, 굴림 피하기, 실제 피격, 사망] — 패턴 키 사전순.</summary>
    public SortedDictionary<string, int[]> PatternTable()
    {
        var t = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
        int[] Row(string p)
        {
            if (!t.TryGetValue(p, out int[] row))
                t[p] = row = new int[5];
            return row;
        }
        foreach (QABoss103Roll r in Rolls)
        {
            int[] row = Row(r.Pattern ?? QABoss103Metrics.Unknown);
            row[0]++;
            row[r.Dodge ? 2 : 1]++;
        }
        foreach (QABoss103Hit h in Hits)
            Row(h.Pattern)[3]++;
        foreach (QABoss103Hit d in Deaths)
            Row(d.Pattern)[4]++;
        return t;
    }
}

/// <summary>할 일 103 C4 규칙·CSV 열(순수).</summary>
public static class QABoss103Metrics
{
    public const string Unknown = "unknown";
    /// <summary>같은 캐릭터·같은 패턴 피해가 이 안에 이어지면 피격 1건(다단 히트·장판 틱).</summary>
    public const double HitMergeSeconds = 0.3;
    /// <summary>사망 귀속 — 사망 직전 이 안의 마지막 피격 패턴.</summary>
    public const double DeathWindowSeconds = 3.0;
    /// <summary>굴림 ↔ 피격 매칭 — 피격 전 이 안에 같은 패턴 굴림이 있으면 '굴린 위험'.</summary>
    public const double RollLookbackSeconds = 6.0;
    /// <summary>굴림 ↔ 피격 매칭 — 피격 뒤 여유(클라 감지가 서버 판정보다 늦게 찍힐 수 있다).</summary>
    public const double RollLookaheadSeconds = 0.3;
    /// <summary>패턴 모름 피격이면 이 안의 아무 굴림이나 '굴린 위험'으로 본다.</summary>
    public const double UnknownLookbackSeconds = 3.0;
    /// <summary>굴림 시각 → 공격 시간표: 공격 시작이 굴림보다 이만큼 늦어도 같은 공격으로 본다(시계 오차).</summary>
    public const double AttackLeadSlack = 0.25;
    /// <summary>굴림 시각 → 공격 시간표: 공격 시작부터 이보다 지난 굴림은 그 공격이 아니다.</summary>
    public const double AttackMaxAge = 6.0;

    public static readonly string[] ActionPrefix = { "basic", "q", "e", "r", "rclick" };

    /// <summary>스택 프레임 타입 → 피격 출처 패턴(안쪽 프레임부터 처음 맞는 것). 보스 본체는 그 순간 공격 id.</summary>
    private static readonly Dictionary<string, string> SourcePatterns = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        { "BossElectricFloor", "floor" },
        { "WellsDroneAttack", "drone" },
        { "BossBomb", "bomb" },
        { "AreaZone", "charge_ball" },
        { "GrabController", "Grab" },
    };

    private const string BossTypeName = "TwentyThreeBoss";

    /// <summary>
    /// 서버 피격의 패턴 키. <paramref name="typeNamesInnermostFirst"/> = 피해 적용 호출 스택의 선언 타입 이름(안쪽부터, 중첩 타입은 <c>A+B</c>).
    /// 장판·드론·폭탄·차징 장판·잡기 컨트롤러가 먼저, 23호 본체면 <paramref name="currentAttackId"/>, 아무것도 없으면 unknown.
    /// </summary>
    public static string ClassifySource(IEnumerable<string> typeNamesInnermostFirst, string currentAttackId)
    {
        if (typeNamesInnermostFirst == null)
            return Unknown;
        foreach (string raw in typeNamesInnermostFirst)
        {
            if (string.IsNullOrEmpty(raw))
                continue;
            int plus = raw.IndexOf('+');
            string name = plus >= 0 ? raw.Substring(0, plus) : raw;
            int dot = name.LastIndexOf('.');
            if (dot >= 0)
                name = name.Substring(dot + 1);
            if (SourcePatterns.TryGetValue(name, out string p))
                return p;
            if (name == BossTypeName)
                return string.IsNullOrEmpty(currentAttackId) || currentAttackId == "na" ? Unknown : currentAttackId;
        }
        return Unknown;
    }

    /// <summary>
    /// 봇 굴림 → 패턴 키. 장판·드론은 그대로, 굴림에 공격 id 가 있으면(호스트 봇) 그것, 모양으로 확정되는 것(돌진·레이지·잡기·점프)은 그 id,
    /// 나머지(부채꼴·전진·띠·예고 없음)는 서버 공격 시간표에서 굴림 시각(+<see cref="AttackLeadSlack"/>) 이전 마지막 시작(<see cref="AttackMaxAge"/> 안). 없으면 unknown.
    /// </summary>
    public static string PatternOfRoll(string kind, string attack, double serverT, IReadOnlyList<KeyValuePair<double, string>> attackStarts)
    {
        if (kind == "floor" || kind == "drone")
            return kind;
        if (!string.IsNullOrEmpty(attack) && attack != "na")
            return attack;
        // 모양만으로 공격이 하나로 정해지는 위험은 시간표보다 모양을 믿는다(시간표의 직전 공격이 남아 있을 수 있다).
        switch (kind)
        {
            case "dash": return "Dash";
            case "rage_dash": return "RageDash";
            case "grab": return "Grab";
            case "jump": return "Jump";
        }
        if (!double.IsNaN(serverT) && attackStarts != null)
        {
            for (int i = attackStarts.Count - 1; i >= 0; i--)
            {
                double start = attackStarts[i].Key;
                if (start > serverT + AttackLeadSlack)
                    continue;
                if (serverT - start <= AttackMaxAge)
                    return attackStarts[i].Value;
                break;
            }
        }
        return Unknown;
    }

    /// <summary>그 피격 전에 같은 캐릭터가 같은 패턴 위험을 굴렸나(패턴 모름이면 아무 굴림).</summary>
    public static bool HasRollFor(QABoss103Hit hit, IReadOnlyList<QABoss103Roll> rolls)
    {
        bool unknown = hit.Pattern == Unknown;
        double back = unknown ? UnknownLookbackSeconds : RollLookbackSeconds;
        foreach (QABoss103Roll r in rolls)
        {
            if (r.Slot != hit.Slot || double.IsNaN(r.ServerT))
                continue;
            if (r.ServerT < hit.T - back || r.ServerT > hit.T + RollLookaheadSeconds)
                continue;
            if (unknown || r.Pattern == hit.Pattern)
                return true;
        }
        return false;
    }

    // ── CSV 열 ───────────────────────────────────────────────

    public static readonly string[] KeyHeader = { "run", "retry", "tier", "seed" };

    public static readonly string[] RunsHeader = Concat(KeyHeader, new[]
    {
        "run_seed", "outcome", "error_reason", "combat_sec", "wipes", "timeout", "party_lives_used", "deaths_total",
        "traits_paladin", "traits_assassin", "traits_gunner", "boss_hp_end_pct", "suppress_count",
        "counter_tries", "counter_success", "threat_rolls", "rolled_take", "actual_hits",
        "data_source", "xlsx_svn_rev", "git_commit", "started_at", "ended_at", "unity_version", "note",
    });

    public static readonly string[] CharsHeader = BuildCharsHeader();

    public static readonly string[] PatternsHeader = Concat(KeyHeader, new[]
        { "pattern", "threats", "rolled_take", "rolled_dodge", "actual_hits", "deaths" });

    public static readonly string[] GimmicksHeader = Concat(KeyHeader, new[]
        { "gimmick", "result", "fight_t", "serverT", "source", "note" });

    private static string[] BuildCharsHeader()
    {
        var h = new List<string>(KeyHeader)
        {
            "character", "damage_taken", "damage_taken_per_min", "deaths", "damage_dealt", "dps", "damage_share",
        };
        foreach (string p in ActionPrefix)
        {
            h.Add(p + "_use");
            h.Add(p + "_hit");
            h.Add(p + "_damage");
        }
        h.AddRange(new[]
        {
            "assassin_transform_count", "transform_sec_total", "transform_uptime", "dmg_normal", "dmg_transformed", "dps_normal", "dps_transformed",
            "paladin_guard_success", "counter_tries", "counter_success", "dmg_during_suppress",
            "evade_dodge", "evade_take", "evade_no_charge", "evade_blocked", "evade_expired", "hits_taken", "hits_unrolled",
        });
        return h.ToArray();
    }

    private static string[] Concat(string[] a, string[] b)
    {
        var r = new string[a.Length + b.Length];
        a.CopyTo(r, 0);
        b.CopyTo(r, a.Length);
        return r;
    }
}

/// <summary>판 머리(모든 CSV 첫 열 + runs.csv 메타). 호스트가 채운다.</summary>
public sealed class QABoss103RunMeta
{
    public int Run;
    public int Retry;
    public QABoss103BotTier Tier;
    public int Seed;
    public int RunSeed;
    public QABoss103Outcome Outcome;
    public string Detail = "";
    /// <summary>Combat 진입부터 판 종료까지(초, 실시간). 음수 = Combat 전에 끝남.</summary>
    public double CombatSeconds = -1.0;
    public string TraitsPaladin = "", TraitsAssassin = "", TraitsGunner = "";
    /// <summary>판 끝 보스 체력 %. NaN = 모름.</summary>
    public double BossHpEndPct = double.NaN;
    public string DataSource = "", XlsxSvnRev = "", GitCommit = "", StartedAt = "", EndedAt = "", UnityVersion = "";
    public string Note = "";

    public string[] Key() => new[] { Inv(Run), Inv(Retry), Tier.ToString(), Inv(Seed) };

    private static string Inv(int v) => v.ToString(CultureInfo.InvariantCulture);
}

/// <summary>CSV 행 직렬화(순수). 숫자는 InvariantCulture, 모름은 빈칸.</summary>
public static class QABoss103CsvRows
{
    public static string[] Run(QABoss103RunMeta m, QABoss103RunStats s)
    {
        bool started = s != null && s.Started;
        bool rolls = started && s.Chars[0].PartPresent | s.Chars[1].PartPresent | s.Chars[2].PartPresent;
        int tries = started ? s.CounterTriesTotal(out bool complete) : 0;
        bool finalOutcome = m.Outcome == QABoss103Outcome.Clear || m.Outcome == QABoss103Outcome.Wipe || m.Outcome == QABoss103Outcome.Timeout;
        string note = m.Note ?? "";
        string parts = started ? s.PartsNote() : "";
        if (parts.Length > 0)
            note = note.Length > 0 ? note + " " + parts : parts;
        var r = new List<string>(m.Key())
        {
            I(m.RunSeed),
            m.Outcome.ToString(),
            finalOutcome ? "" : m.Detail,
            m.CombatSeconds < 0.0 ? "" : D(m.CombatSeconds, 1),
            started ? I(s.Wipes) : "",
            m.Outcome == QABoss103Outcome.Timeout ? "1" : "0",
            started ? I(s.LivesUsed) : "",
            started ? I(s.DeathsTotal()) : "",
            m.TraitsPaladin, m.TraitsAssassin, m.TraitsGunner,
            D(m.BossHpEndPct, 1),
            started ? I(s.SuppressCount) : "",
            rolls ? I(tries) : "",
            started ? I(s.CounterSuccessTotal()) : "",
            rolls ? I(s.Rolls.Count) : "",
            rolls ? I(s.RolledTake()) : "",
            started ? I(s.Hits.Count) : "",
            m.DataSource, m.XlsxSvnRev, m.GitCommit, m.StartedAt, m.EndedAt, m.UnityVersion,
            note,
        };
        return r.ToArray();
    }

    public static List<string[]> Chars(QABoss103RunMeta m, QABoss103RunStats s)
    {
        var rows = new List<string[]>();
        if (s == null || !s.Started)
            return rows;
        double dur = s.Duration;
        long total = s.DamageDealtTotal();
        foreach (QABoss103CharStats c in s.Chars)
        {
            var r = new List<string>(m.Key())
            {
                c.Character.ToString(),
                L(c.DamageTaken),
                D(PerMinute(c.DamageTaken, dur), 1),
                I(c.Deaths),
                L(c.DamageDealt),
                D(PerSecond(c.DamageDealt, dur), 1),
                D(total > 0 ? (double)c.DamageDealt / total : double.NaN, 4),
            };
            for (int a = 0; a < QABoss103Metrics.ActionPrefix.Length; a++)
            {
                r.Add(I(c.Uses[a]));
                r.Add(I(c.Hits[a]));
                r.Add(L(c.TypedDamage[a]));
            }
            bool assassin = c.Character == QABoss103Character.Assassin;
            double normalSec = double.IsNaN(dur) ? double.NaN : Math.Max(0.0, dur - c.TransformSeconds);
            r.Add(assassin ? I(c.TransformCount) : "");
            r.Add(assassin ? D(c.TransformSeconds, 1) : "");
            r.Add(assassin ? D(dur > 0 ? c.TransformSeconds / dur : double.NaN, 4) : "");
            r.Add(assassin ? L(c.DamageNormal) : "");
            r.Add(assassin ? L(c.DamageTransformed) : "");
            r.Add(assassin ? D(PerSecond(c.DamageNormal, normalSec), 1) : "");
            r.Add(assassin ? D(PerSecond(c.DamageTransformed, c.TransformSeconds), 1) : "");
            r.Add(c.Character == QABoss103Character.Paladin && c.GuardSuccess.HasValue ? I(c.GuardSuccess.Value) : "");
            r.Add(c.PartPresent ? I(c.CounterTries) : "");
            r.Add(I(c.CounterSuccess));
            r.Add(L(c.DamageDuringSuppress));
            r.Add(c.PartPresent ? I(c.EvadeDodge) : "");
            r.Add(c.PartPresent ? I(c.EvadeTake) : "");
            r.Add(c.PartPresent ? I(c.EvadeNoCharge) : "");
            r.Add(c.PartPresent ? I(c.EvadeBlocked) : "");
            r.Add(c.PartPresent ? I(c.EvadeExpired) : "");
            r.Add(I(c.HitsTaken));
            r.Add(c.HitsUnrolled >= 0 ? I(c.HitsUnrolled) : "");
            rows.Add(r.ToArray());
        }
        return rows;
    }

    public static List<string[]> Patterns(QABoss103RunMeta m, QABoss103RunStats s)
    {
        var rows = new List<string[]>();
        if (s == null || !s.Started)
            return rows;
        foreach (KeyValuePair<string, int[]> kv in s.PatternTable())
        {
            var r = new List<string>(m.Key()) { kv.Key };
            foreach (int v in kv.Value)
                r.Add(I(v));
            rows.Add(r.ToArray());
        }
        return rows;
    }

    public static List<string[]> Gimmicks(QABoss103RunMeta m, QABoss103RunStats s)
    {
        var rows = new List<string[]>();
        if (s == null || !s.Started)
            return rows;
        foreach (QABoss103GimmickEvent g in s.Gimmicks)
        {
            var r = new List<string>(m.Key()) { g.Gimmick, g.Result, D(g.FightT, 1), D(g.ServerT, 3), g.Source, g.Note };
            rows.Add(r.ToArray());
        }
        return rows;
    }

    public static double PerSecond(double amount, double seconds) => seconds > 0.0 && !double.IsNaN(seconds) ? amount / seconds : double.NaN;
    public static double PerMinute(double amount, double seconds) => seconds > 0.0 && !double.IsNaN(seconds) ? amount * 60.0 / seconds : double.NaN;

    private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);
    private static string L(long v) => v.ToString(CultureInfo.InvariantCulture);
    private static string D(double v, int decimals) =>
        double.IsNaN(v) || double.IsInfinity(v) ? "" : v.ToString("F" + decimals, CultureInfo.InvariantCulture);
}

/// <summary>CSV 파일 쓰기(순수 System.IO). UTF-8 BOM(엑셀 한글) · CRLF · 파일이 없으면 헤더부터 · 판마다 append + flush.</summary>
public static class QABoss103Csv
{
    public const string RunsFile = "runs.csv";
    public const string CharsFile = "run_chars.csv";
    public const string PatternsFile = "run_patterns.csv";
    public const string GimmicksFile = "run_gimmicks.csv";

    private static readonly UTF8Encoding NoBom = new UTF8Encoding(false);

    /// <summary>RFC 4180 — 쉼표·따옴표·줄바꿈·앞뒤 공백이 있으면 따옴표로 감싸고 따옴표는 두 번. null = 빈칸.</summary>
    public static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        bool quote = s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 || char.IsWhiteSpace(s[0]) || char.IsWhiteSpace(s[s.Length - 1]);
        return quote ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    public static string Line(IReadOnlyList<string> cells)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < cells.Count; i++)
        {
            if (i > 0)
                sb.Append(',');
            sb.Append(Escape(cells[i]));
        }
        return sb.ToString();
    }

    /// <summary>행들을 붙인다. 파일이 없거나 비었으면 BOM + 헤더부터. 쓴 행 수.</summary>
    public static int Append(string path, IReadOnlyList<string> header, IReadOnlyList<string[]> rows)
    {
        string dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        bool isNew = !File.Exists(path) || new FileInfo(path).Length == 0;
        var sb = new StringBuilder();
        if (isNew)
            sb.Append(Line(header)).Append("\r\n");
        foreach (string[] row in rows)
            sb.Append(Line(row)).Append("\r\n");
        using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            if (isNew)
            {
                byte[] bom = Encoding.UTF8.GetPreamble();
                fs.Write(bom, 0, bom.Length);
            }
            byte[] bytes = NoBom.GetBytes(sb.ToString());
            fs.Write(bytes, 0, bytes.Length);
            fs.Flush(true);
        }
        return rows.Count;
    }
}

/// <summary>
/// 봇 판 파일 — 각 봇 인스턴스(호스트·MPPM 클론)가 <c>&lt;출력 폴더&gt;/parts/run&lt;n&gt;_r&lt;retry&gt;_s&lt;자리&gt;.txt</c> 에 한 줄씩 쓰고(즉시 flush),
/// 호스트가 판 끝에 읽어 합친다. 줄 = <c>종류 key=value …</c>(값에 공백·'=' 없음 — <see cref="QABoss103Plan.Token"/>).
/// 종류: <c>threat</c>(회피 굴림 1건) · <c>counter</c>(간파 시도 1회) · <c>done</c>(봇 종료). 판 끝 신호 = 같은 폴더 <c>run&lt;n&gt;_r&lt;retry&gt;_end.txt</c>(호스트가 씀).
/// </summary>
public static class QABoss103PartFile
{
    public const string Folder = "parts";

    public static string PartPath(string outDir, int run, int retry, int slot) =>
        Path.Combine(outDir, Folder, $"run{run}_r{retry}_s{slot}.txt");

    public static string EndPath(string outDir, int run, int retry) =>
        Path.Combine(outDir, Folder, $"run{run}_r{retry}_end.txt");

    public static bool TryParse(string line, out string kind, out Dictionary<string, string> kv)
    {
        kind = null;
        kv = null;
        if (string.IsNullOrWhiteSpace(line))
            return false;
        string[] parts = line.Trim().Split(' ');
        kind = parts[0];
        kv = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 1; i < parts.Length; i++)
        {
            int eq = parts[i].IndexOf('=');
            if (eq <= 0)
                continue;
            kv[parts[i].Substring(0, eq)] = parts[i].Substring(eq + 1);
        }
        return kind.Length > 0;
    }

    /// <summary>key=value 줄 만들기(값은 토큰화).</summary>
    public static string Format(string kind, params string[] keyValues)
    {
        var sb = new StringBuilder(kind);
        for (int i = 0; i + 1 < keyValues.Length; i += 2)
            sb.Append(' ').Append(keyValues[i]).Append('=').Append(QABoss103Plan.Token(keyValues[i + 1]));
        return sb.ToString();
    }

    /// <summary>다른 프로세스가 쓰는 중이어도 읽는다(FileShare.ReadWrite). 없으면 null.</summary>
    public static List<string> ReadShared(string path)
    {
        if (!File.Exists(path))
            return null;
        var lines = new List<string>();
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var sr = new StreamReader(fs, Encoding.UTF8))
        {
            string l;
            while ((l = sr.ReadLine()) != null)
                lines.Add(l);
        }
        return lines;
    }
}

/// <summary>봇 판 파일 쓰기 — 줄마다 flush(판 중간에 끊겨도 쓴 데까지 남는다). 다른 프로세스가 읽는 중이어도 쓴다.</summary>
public sealed class QABoss103PartWriter : IDisposable
{
    private StreamWriter _w;
    public string Path { get; }
    public bool Closed => _w == null;

    public QABoss103PartWriter(string path)
    {
        Path = path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
        var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        _w = new StreamWriter(fs, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };
    }

    public void Write(string line)
    {
        if (_w == null)
            return;
        _w.WriteLine(line);
    }

    /// <summary><c>done</c> 줄을 쓰고 닫는다(두 번째부터 무시).</summary>
    public void Close(int slot, string reason)
    {
        if (_w == null)
            return;
        _w.WriteLine(QABoss103PartFile.Format("done", "slot", slot.ToString(CultureInfo.InvariantCulture), "reason", reason));
        Dispose();
    }

    public void Dispose()
    {
        _w?.Dispose();
        _w = null;
    }
}

/// <summary>
/// 23호 전투 기록(<c>[23호/전투기록]</c> 로그 또는 보스 <c>_fightLog</c>) → 기믹 발생·결과(C4 임시 출처 source=log, C5 에서 게임 알림으로 교체).
/// 줄 = <c>  [  12.3s] 메시지</c>. 시작만 있고 결과가 없으면 종료 줄로 ended, 기록 끝까지 없으면 timeout(판이 먼저 끝남).
/// </summary>
public static class QABoss103FightLog
{
    private static readonly Regex LineRx = new Regex(@"^\s*\[\s*([0-9]+(?:\.[0-9]+)?)s\]\s*(.*)$", RegexOptions.Compiled);
    private static readonly Regex PushNorthRx = new Regex(@"^북진 ([0-9]+)회차(.*)$", RegexOptions.Compiled);
    private static readonly Regex ChainRoundRx = new Regex(@"^연쇄 간파 ([0-9]+)회 (성공|실패)", RegexOptions.Compiled);
    private static readonly Regex ChainResultRx = new Regex(@"^연쇄 간파 결과 ([0-9]+)/([0-9]+) → (\w+)", RegexOptions.Compiled);

    public const string Relay = "간파 릴레이";
    public const string Chain = "연쇄 간파";
    public const string PushNorth = "북진 압박";

    /// <summary>기록 → 기믹 목록(순서 유지). <paramref name="suppressions"/> = '제압 —' 줄 수.</summary>
    public static List<QABoss103GimmickEvent> Parse(string text, out int suppressions)
    {
        suppressions = 0;
        var list = new List<QABoss103GimmickEvent>();
        if (string.IsNullOrEmpty(text))
            return list;
        var open = new Dictionary<string, double>(StringComparer.Ordinal);
        double lastT = double.NaN;
        foreach (string raw in text.Split('\n'))
        {
            Match m = LineRx.Match(raw.TrimEnd('\r'));
            if (!m.Success)
                continue;
            double t = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            string msg = m.Groups[2].Value.Trim();
            lastT = t;

            if (msg.StartsWith("제압 —", StringComparison.Ordinal))
            {
                suppressions++;
                continue;
            }
            if (msg.StartsWith(Relay + " 시작", StringComparison.Ordinal)) { open[Relay] = t; continue; }
            if (msg.StartsWith(Chain + " 시작", StringComparison.Ordinal)) { open[Chain] = t; continue; }
            if (msg.StartsWith(PushNorth + " 시작", StringComparison.Ordinal)) { open[PushNorth] = t; continue; }

            string g = null, result = null, note = "";
            Match pm, cm;
            if (msg.StartsWith("송전기 성공", StringComparison.Ordinal)) { g = "송전기"; result = "success"; }
            else if (msg.StartsWith("송전기 실패", StringComparison.Ordinal)) { g = "송전기"; result = "fail"; }
            else if (msg.StartsWith("② 전력 공급 성공", StringComparison.Ordinal)) { g = "전력 공급"; result = "success"; }
            else if (msg.StartsWith("② 전력 공급 실패", StringComparison.Ordinal)) { g = "전력 공급"; result = "fail"; }
            else if (msg.StartsWith(Relay + " 성공", StringComparison.Ordinal)) { g = Relay; result = "success"; }
            else if (msg.StartsWith(Relay + " 실패", StringComparison.Ordinal)) { g = Relay; result = "fail"; }
            else if (msg.StartsWith(Relay + " — 제한 시간 초과", StringComparison.Ordinal)) { g = Relay; result = "timeout"; }
            else if ((cm = ChainResultRx.Match(msg)).Success)
            {
                g = Chain;
                string o = cm.Groups[3].Value;
                result = o == "Full" ? "success" : o == "Partial" ? "partial" : "fail";
                note = $"{cm.Groups[1].Value}/{cm.Groups[2].Value}";
            }
            else if ((cm = ChainRoundRx.Match(msg)).Success)
            {
                list.Add(new QABoss103GimmickEvent { Gimmick = $"{Chain} {cm.Groups[1].Value}회", Result = cm.Groups[2].Value == "성공" ? "success" : "fail", FightT = t });
                continue;
            }
            else if ((pm = PushNorthRx.Match(msg)).Success)
            {
                string rest = pm.Groups[2].Value;
                string name = $"북진 {pm.Groups[1].Value}회차";
                if (rest.Contains("탑 파괴")) { g = name; result = "success"; }
                else if (rest.Contains("실패")) { g = name; result = "fail"; }
                else if (rest.Contains("간파")) { g = name + " 간파"; result = "success"; }
                if (g != null)
                {
                    list.Add(new QABoss103GimmickEvent { Gimmick = g, Result = result, FightT = t });
                    if (g == name)
                        open.Remove(PushNorth);   // 회차 결과가 났다 — 북진 압박 전체는 회차 줄로 본다
                }
                continue;
            }
            else if (msg.StartsWith("③ 새 가족 성공", StringComparison.Ordinal)) { g = "새 가족"; result = "success"; }
            else if (msg.StartsWith("③ 새 가족 실패", StringComparison.Ordinal)) { g = "새 가족"; result = "fail"; }
            else if (msg.StartsWith("④ 폐기 처분 저지", StringComparison.Ordinal)) { g = "자폭 저지"; result = "success"; }
            else if (msg.StartsWith("④ 폐기 처분 실패", StringComparison.Ordinal)) { g = "자폭 저지"; result = "fail"; }
            else if (msg.StartsWith("0줄 자폭 저지", StringComparison.Ordinal)) { g = "0줄 자폭"; result = "success"; }
            else
            {
                // "<기믹> 종료 — 사유": 결과 없이 끝난 기믹만 ended 로 남긴다.
                foreach (string name in new[] { Relay, Chain, PushNorth })
                    if (msg.StartsWith(name + " 종료", StringComparison.Ordinal) && open.ContainsKey(name))
                    {
                        open.Remove(name);
                        int dash = msg.IndexOf('—');
                        list.Add(new QABoss103GimmickEvent { Gimmick = name, Result = "ended", FightT = t, Note = dash >= 0 ? msg.Substring(dash + 1).Trim() : "" });
                    }
                continue;
            }

            open.Remove(g);
            list.Add(new QABoss103GimmickEvent { Gimmick = g, Result = result, FightT = t, Note = note });
        }
        foreach (KeyValuePair<string, double> kv in open)
            list.Add(new QABoss103GimmickEvent { Gimmick = kv.Key, Result = "timeout", FightT = lastT, Note = "판 종료까지 결과 없음" });
        return list;
    }
}
