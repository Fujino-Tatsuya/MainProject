using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>할 일 119 C3 — 송전탑 한 차례(탑이 올라와 참여한 구간). 시각은 계측기 시계(호스트 ServerTime, 초).</summary>
public sealed class QABoss103PylonWave
{
    /// <summary>기믹 이름(송전기·북진 압박·전력 공급). 판 끝에 기믹 기록으로 정한다(<see cref="QABoss103FailReason.LabelWaves"/>).</summary>
    public string Label = "";
    public double StartT = double.NaN, EndT = double.NaN;
    /// <summary>참여한 탑 수(구간 최대) · 그중 부서진 수(구간 최대).</summary>
    public int Engaged, Destroyed;
    /// <summary>구간 중 탑에 들어간 피해 합.</summary>
    public long Damage;
    /// <summary>판 끝까지 안 끝난 구간.</summary>
    public bool Open = true;

    public bool Failed => Destroyed < Engaged;
}

/// <summary>할 일 119 C3 — 송전탑 구간 추적(순수). 계측기가 프레임마다 참여·파괴 수를 넣는다.</summary>
public sealed class QABoss103PylonWaveTracker
{
    public readonly List<QABoss103PylonWave> Waves = new List<QABoss103PylonWave>();
    private QABoss103PylonWave _open;

    /// <summary>참여 탑이 0 → 1 이상이면 새 구간, 다시 0 이면 닫는다. <paramref name="label"/> 이 비어 있지 않으면 구간 이름으로 쓴다.</summary>
    public void Tick(double t, int engaged, int destroyed, string label)
    {
        if (engaged > 0)
        {
            if (_open == null)
            {
                _open = new QABoss103PylonWave { StartT = t };
                Waves.Add(_open);
            }
            _open.Engaged = Math.Max(_open.Engaged, engaged);
            _open.Destroyed = Math.Max(_open.Destroyed, Math.Min(destroyed, engaged));
            _open.EndT = t;
            if (!string.IsNullOrEmpty(label) && _open.Label.Length == 0)
                _open.Label = label;
        }
        else if (_open != null)
        {
            _open.Open = false;
            _open.EndT = t;
            _open = null;
        }
    }

    /// <summary>탑에 들어간 피해 — 열린 구간에 더한다(없으면 버린다).</summary>
    public void Damage(int amount)
    {
        if (_open != null && amount > 0)
            _open.Damage += amount;
    }
}

/// <summary>할 일 119 C3 — 판정 순간 상태(실패 사유 근거). 모름 = -1 · NaN · 빈칸.</summary>
public sealed class QABoss103FailSnapshot
{
    /// <summary>진행 중이던 보스 공격 id(공격 상태가 아니면 빈칸).</summary>
    public string BossAttack = "";
    /// <summary>다단계 공격 단계(<c>BossAttackPhase</c>) — 공격 중일 때만.</summary>
    public string BossAttackPhase = "";
    /// <summary>보스 상태(<c>MonsterState</c>).</summary>
    public string BossState = "";
    /// <summary>마지막으로 시작한 보스 공격 id.</summary>
    public string LastAttack = "";
    /// <summary>보스 페이즈(0 = 1페이즈).</summary>
    public int BossPhase = -1;
    public int Alive = -1, Lives = -1;
    public double BossHpPct = double.NaN;
    public double ServerT = double.NaN;
}

/// <summary>
/// 할 일 119 C3 클리어 실패 사유(순수, EditMode <c>QABoss103FailTests</c>) — 전멸·시간초과(조기 포함) 판마다 run_fail.csv 1행.
/// 열 = 판 키 + 결과·실패 패턴(집계 키)·보스 패턴·페이즈·진행 중 기믹·남은 인원·남은 공용 목숨·보스 HP·마지막 사망·송전탑·비고(자동 생성).
/// </summary>
public static class QABoss103FailReason
{
    public const string File = "run_fail.csv";

    /// <summary>송전탑 구간이 판 끝 이 안(초)에 끝났으면 '실패 순간 기믹'으로 본다(실패 벌칙 → 전멸까지의 시간).</summary>
    public const double RecentWaveSeconds = 120.0;

    public const string PylonCharge = "송전기";
    public const string PushNorth = "북진 압박";
    public const string PowerSupply = "전력 공급";
    public const string PylonGeneric = "송전탑";

    public static readonly string[] Header = Concat(QABoss103Metrics.KeyHeader, new[]
    {
        "outcome", "outcome_source", "combat_sec", "fail_pattern", "boss_attack", "boss_state", "boss_phase", "last_attack", "active_gimmick",
        "alive", "lives_left", "boss_hp_pct", "last_death_character", "last_death_pattern", "deaths_by_pattern",
        "pylon_gimmick", "pylon_total", "pylon_destroyed", "pylon_damage", "pylon_waves_failed", "pylon_waves", "gimmick_fails", "note",
    });

    /// <summary>실패 사유를 남기는 판인가 — 전멸·시간초과(조기 시간초과 포함).</summary>
    public static bool IsFail(QABoss103Outcome o) => o == QABoss103Outcome.Wipe || o == QABoss103Outcome.Timeout;

    /// <summary>기믹 이름 → 송전탑 기믹 이름(송전탑을 쓰지 않는 기믹이면 null).</summary>
    public static string PylonGimmickOf(string gimmick)
    {
        if (string.IsNullOrEmpty(gimmick))
            return null;
        if (gimmick.StartsWith("북진", StringComparison.Ordinal))
            return PushNorth;
        if (gimmick.StartsWith(PylonCharge, StringComparison.Ordinal))
            return PylonCharge;
        if (gimmick.StartsWith(PowerSupply, StringComparison.Ordinal))
            return PowerSupply;
        return null;
    }

    /// <summary>
    /// 이름 없는 구간에 기믹 기록으로 이름을 붙인다 — 구간 [시작−5초, 끝+5초] 안 송전탑 기믹 행 중 구간 끝에 가장 가까운 것. 없으면 '송전탑'.
    /// </summary>
    public static void LabelWaves(IList<QABoss103PylonWave> waves, IReadOnlyList<QABoss103GimmickEvent> gimmicks)
    {
        if (waves == null)
            return;
        foreach (QABoss103PylonWave w in waves)
        {
            if (w.Label.Length > 0)
                continue;
            string best = null;
            double bestD = double.MaxValue;
            if (gimmicks != null)
                foreach (QABoss103GimmickEvent g in gimmicks)
                {
                    string name = PylonGimmickOf(g.Gimmick);
                    if (name == null || double.IsNaN(g.ServerT) || g.ServerT < w.StartT - 5.0 || g.ServerT > w.EndT + 5.0)
                        continue;
                    double d = Math.Abs(g.ServerT - w.EndT);
                    if (d < bestD)
                    {
                        bestD = d;
                        best = name;
                    }
                }
            w.Label = best ?? PylonGeneric;
        }
    }

    /// <summary>판 끝까지 결과가 없던 기믹(전투 기록 timeout) + 열린 송전탑 구간 이름. 겹치면 1번.</summary>
    public static List<string> ActiveGimmicks(IReadOnlyList<QABoss103GimmickEvent> gimmicks, IReadOnlyList<QABoss103PylonWave> waves)
    {
        var list = new List<string>();
        if (waves != null)
            foreach (QABoss103PylonWave w in waves)
                if (w.Open && !list.Contains(w.Label))
                    list.Add(w.Label);
        if (gimmicks != null)
            foreach (QABoss103GimmickEvent g in gimmicks)
                if (g.Result == "timeout" && !string.IsNullOrEmpty(g.Gimmick))
                {
                    string name = PylonGimmickOf(g.Gimmick) ?? g.Gimmick;
                    if (!list.Contains(name))
                        list.Add(name);
                }
        return list;
    }

    /// <summary>실패 순간과 관련된 송전탑 구간 — 열린 구간, 아니면 판 끝 <see cref="RecentWaveSeconds"/> 안에 끝난 실패 구간(마지막 것). 없으면 null.</summary>
    public static QABoss103PylonWave RelevantWave(IReadOnlyList<QABoss103PylonWave> waves, double endT)
    {
        if (waves == null)
            return null;
        for (int i = waves.Count - 1; i >= 0; i--)
        {
            QABoss103PylonWave w = waves[i];
            if (w.Open)
                return w;
            if (w.Failed && (double.IsNaN(endT) || endT - w.EndT <= RecentWaveSeconds))
                return w;
        }
        return null;
    }

    /// <summary>
    /// 집계 키(실패 패턴) — 진행 중 기믹 → 최근 실패한 송전탑 구간 → (전멸이면) 마지막 사망 패턴 → 진행 중 보스 공격 → 보스 상태. 모두 없으면 unknown.
    /// </summary>
    public static string FailPattern(QABoss103Outcome outcome, IReadOnlyList<string> active, QABoss103PylonWave wave, string lastDeathPattern, QABoss103FailSnapshot snap)
    {
        if (active != null && active.Count > 0)
            return active[0];
        if (wave != null)
            return wave.Label;
        if (outcome == QABoss103Outcome.Wipe && !string.IsNullOrEmpty(lastDeathPattern) && lastDeathPattern != QABoss103Metrics.Unknown)
            return lastDeathPattern;
        if (snap != null && snap.BossAttack.Length > 0)
            return snap.BossAttack;
        if (snap != null && snap.BossState.Length > 0)
            return snap.BossState;
        return QABoss103Metrics.Unknown;
    }

    /// <summary>run_fail.csv 1행. 실패 판이 아니거나 계측이 없으면 null.</summary>
    public static string[] Row(QABoss103RunMeta m, QABoss103RunStats s, QABoss103FailSnapshot snap, IReadOnlyList<QABoss103PylonWave> waves)
    {
        if (m == null || !IsFail(m.Outcome) || s == null || !s.Started)
            return null;
        snap = snap ?? new QABoss103FailSnapshot();
        waves = waves ?? new List<QABoss103PylonWave>();
        double endT = double.IsNaN(snap.ServerT) ? s.CombatEnd : snap.ServerT;
        List<string> active = ActiveGimmicks(s.Gimmicks, waves);
        QABoss103PylonWave wave = RelevantWave(waves, endT);
        QABoss103Hit lastDeath = s.Deaths.Count > 0 ? s.Deaths[s.Deaths.Count - 1] : null;
        string lastPattern = lastDeath != null ? lastDeath.Pattern : "";
        int failedWaves = 0;
        foreach (QABoss103PylonWave w in waves)
            if (w.Failed && !w.Open)
                failedWaves++;
        double hp = !double.IsNaN(m.BossHpEndPct) ? m.BossHpEndPct : snap.BossHpPct;

        var r = new List<string>(m.Key())
        {
            m.Outcome.ToString(),
            m.OutcomeSource ?? "",
            m.CombatSeconds < 0.0 ? "" : D(m.CombatSeconds, 1),
            FailPattern(m.Outcome, active, wave, lastPattern, snap),
            snap.BossAttack,
            snap.BossState,
            snap.BossPhase >= 0 ? I(snap.BossPhase + 1) : "",
            snap.LastAttack,
            string.Join(";", active),
            snap.Alive >= 0 ? I(snap.Alive) : "",
            snap.Lives >= 0 ? I(snap.Lives) : "",
            D(hp, 1),
            lastDeath != null ? CharacterOf(lastDeath.Slot) : "",
            lastPattern,
            DeathsByPattern(s),
            wave != null ? wave.Label : "",
            wave != null ? I(wave.Engaged) : "",
            wave != null ? I(wave.Destroyed) : "",
            wave != null ? wave.Damage.ToString(CultureInfo.InvariantCulture) : "",
            I(failedWaves),
            I(waves.Count),
            string.Join(";", GimmickFails(s.Gimmicks)),
            Note(m, s, snap, waves, active, wave, hp),
        };
        return r.ToArray();
    }

    /// <summary>사망 귀속 패턴별 수 — 많은 순(같으면 이름순) <c>Dash:3;floor:2</c>.</summary>
    public static string DeathsByPattern(QABoss103RunStats s)
    {
        var count = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (QABoss103Hit d in s.Deaths)
            count[d.Pattern ?? QABoss103Metrics.Unknown] = (count.TryGetValue(d.Pattern ?? QABoss103Metrics.Unknown, out int n) ? n : 0) + 1;
        var keys = new List<string>(count.Keys);
        keys.Sort((a, b) =>
        {
            int c = count[b].CompareTo(count[a]);
            return c != 0 ? c : string.CompareOrdinal(a, b);
        });
        var parts = new List<string>();
        foreach (string k in keys)
            parts.Add(k + ":" + I(count[k]));
        return string.Join(";", parts);
    }

    /// <summary>실패한 기믹 이름(fail·timeout, 순서 유지·중복 제거).</summary>
    public static List<string> GimmickFails(IReadOnlyList<QABoss103GimmickEvent> gimmicks)
    {
        var list = new List<string>();
        if (gimmicks != null)
            foreach (QABoss103GimmickEvent g in gimmicks)
                if ((g.Result == "fail" || g.Result == "timeout") && !string.IsNullOrEmpty(g.Gimmick) && !list.Contains(g.Gimmick))
                    list.Add(g.Gimmick);
        return list;
    }

    /// <summary>비고(자동 생성, 한국어 — 근거를 ' · ' 로 잇는다).</summary>
    public static string Note(QABoss103RunMeta m, QABoss103RunStats s, QABoss103FailSnapshot snap, IReadOnlyList<QABoss103PylonWave> waves,
        IReadOnlyList<string> active, QABoss103PylonWave wave, double hpPct)
    {
        var parts = new List<string>();
        if (m.Outcome == QABoss103Outcome.Wipe)
            parts.Add("전멸 — 공용 목숨 소진" + (snap.Alive >= 0 ? $"(생존 {I(snap.Alive)}·목숨 {(snap.Lives >= 0 ? I(snap.Lives) : "?")})" : ""));
        else if (m.OutcomeSource == QABoss103EarlyTimeout.Note || (m.Note ?? "").Contains(QABoss103EarlyTimeout.Note))
            parts.Add("조기 시간초과 — 공용 목숨 0·보스 HP 5분 불변(진척 없음)");
        else
            parts.Add($"시간초과 — Combat {QABoss103Plan.CombatLimitSeconds / 60:0}분 안에 처치 못 함");

        if (wave != null)
        {
            string tail = wave.Open ? "판 끝까지 진행 중" : wave.Failed ? "모든 스킬·공격을 퍼부어도 전부 못 부숨" : "전부 파괴";
            parts.Add($"{wave.Label}: 송전탑 {I(wave.Engaged)}개 중 {I(wave.Destroyed)}개 파괴(탑 피해 {wave.Damage.ToString(CultureInfo.InvariantCulture)}) — {tail}");
        }
        else if (active != null && active.Count > 0)
            parts.Add("진행 중 기믹 " + string.Join("·", active));

        int failed = 0, total = 0;
        if (waves != null)
            foreach (QABoss103PylonWave w in waves)
            {
                total++;
                if (w.Failed && !w.Open)
                    failed++;
            }
        if (total > 0)
            parts.Add($"송전탑 기믹 실패 {I(failed)}/{I(total)}회");

        if (s.Deaths.Count > 0)
        {
            QABoss103Hit last = s.Deaths[s.Deaths.Count - 1];
            parts.Add($"마지막 사망 {CharacterOf(last.Slot)} ← {last.Pattern}");
            parts.Add("사망 귀속 " + DeathsByPattern(s).Replace(";", " · ").Replace(":", " "));
        }
        List<string> fails = GimmickFails(s.Gimmicks);
        if (fails.Count > 0)
            parts.Add("기믹 실패 " + string.Join("·", fails.Count > 5 ? fails.GetRange(fails.Count - 5, 5) : fails));
        string attack = snap.BossAttack.Length > 0 ? snap.BossAttack + (snap.BossAttackPhase.Length > 0 ? "/" + snap.BossAttackPhase : "") : snap.BossState;
        if (!string.IsNullOrEmpty(attack))
            parts.Add($"판정 순간 보스 {attack}" + (snap.BossPhase >= 0 ? $" · 페이즈 {I(snap.BossPhase + 1)}" : ""));
        if (!double.IsNaN(hpPct))
            parts.Add($"보스 남은 HP {hpPct.ToString("F1", CultureInfo.InvariantCulture)}%");
        return string.Join(" · ", parts);
    }

    public static string CharacterOf(int slot) =>
        slot >= 0 && slot <= 2 ? ((QABoss103Character)slot).ToString() : "na";

    private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

    private static string D(double v, int decimals) =>
        double.IsNaN(v) || double.IsInfinity(v) ? "" : v.ToString("F" + decimals, CultureInfo.InvariantCulture);

    private static string[] Concat(string[] a, string[] b)
    {
        var r = new string[a.Length + b.Length];
        a.CopyTo(r, 0);
        b.CopyTo(r, a.Length);
        return r;
    }
}
