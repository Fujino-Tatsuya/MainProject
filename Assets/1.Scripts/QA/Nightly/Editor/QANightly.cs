#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// 할 일 108 팀원별 야간 QA 시드 — 레포 루트 <c>QANightly/nightly-seed.json</c> 한 파일. 팀원 브랜치(<c>qa/Nightly-&lt;이름&gt;</c>)는 이 파일만 바꾼다.
/// 순수 파싱·검증(EditMode <c>QANightlyTests</c>). 103 설정(<see cref="QABoss103Config"/>)으로 바꿔 판마다 호스트·클론 루트에 쓴다.
/// </summary>
public sealed class QANightlySeed
{
    public const string RelativePath = "QANightly/nightly-seed.json";
    public const int MaxRuns = QABoss103Batch.TotalRuns;

    /// <summary>시드 이름 = 날짜별 공유 폴더 이름(영문·숫자·-·_ 1~32자).</summary>
    public string Name = "";
    /// <summary>기준 시드 — 판 시드(봇 전투 난수)의 원천(<see cref="QABoss103Plan.RunSeed"/>).</summary>
    public int BaseSeed = 108;
    /// <summary>특성 선택 시드. 0 = 기준 시드를 같이 쓴다.</summary>
    public int TraitSeed;
    /// <summary>봇 단계. null = 자동(판 번호 mod 3 — 보통→숙련→초보).</summary>
    public QABoss103BotTier? Tier;
    /// <summary>자리(호스트·가상 2·가상 3) → 캐릭터.</summary>
    public QABoss103Character[] Order = (QABoss103Character[])QABoss103Plan.PartyOrder.Clone();
    /// <summary>한 밤 판 수(오류 판 1회 재실행은 별도).</summary>
    public int Runs = 10;
    public string Note = "";

    [Serializable]
    private sealed class Json
    {
        public string name = "";
        public int baseSeed = 108;
        public int traitSeed;
        public string botTier = "auto";
        public string[] characterOrder;
        public int runs = 10;
        public string note = "";
    }

    private static readonly Regex NamePattern = new Regex("^[A-Za-z0-9_-]{1,32}$");

    public static bool IsValidName(string name) => name != null && NamePattern.IsMatch(name);

    /// <summary>JSON → 시드. 틀린 칸은 <paramref name="errors"/> 에 하나씩(한국어). 오류가 하나라도 있으면 false.</summary>
    public static bool TryParse(string json, out QANightlySeed seed, out List<string> errors)
    {
        seed = new QANightlySeed();
        errors = new List<string>();
        if (string.IsNullOrWhiteSpace(json))
        {
            errors.Add("시드 파일이 비어 있다");
            return false;
        }
        var j = new Json();
        try
        {
            JsonUtility.FromJsonOverwrite(json, j);
        }
        catch (Exception e)
        {
            errors.Add($"JSON 형식 오류: {e.Message}");
            return false;
        }

        seed.Name = (j.name ?? "").Trim();
        if (!IsValidName(seed.Name))
            errors.Add($"name '{seed.Name}' — 영문·숫자·-·_ 1~32자(공유 폴더 이름이 된다)");

        seed.BaseSeed = j.baseSeed;
        if (seed.BaseSeed == 0)
            errors.Add("baseSeed 는 0 이 아닌 정수");
        seed.TraitSeed = j.traitSeed;

        string tier = (j.botTier ?? "").Trim();
        if (tier.Length == 0 || string.Equals(tier, "auto", StringComparison.OrdinalIgnoreCase))
            seed.Tier = null;
        else if (!int.TryParse(tier, out _) && Enum.TryParse(tier, true, out QABoss103BotTier t) && Enum.IsDefined(typeof(QABoss103BotTier), t))
            seed.Tier = t;
        else
            errors.Add($"botTier '{tier}' — auto·Beginner·Normal·Skilled 중 하나");

        if (j.characterOrder != null && j.characterOrder.Length > 0)
        {
            if (QABoss103Plan.TryParseOrder(string.Join(",", j.characterOrder), out QABoss103Character[] order))
                seed.Order = order;
            else
                errors.Add($"characterOrder [{string.Join(",", j.characterOrder)}] — Paladin·Assassin·Gunner 를 한 번씩 3칸(자리 0 = 호스트)");
        }

        seed.Runs = j.runs;
        if (seed.Runs < 1 || seed.Runs > MaxRuns)
            errors.Add($"runs {seed.Runs} — 1~{MaxRuns}");
        seed.Note = j.note ?? "";
        return errors.Count == 0;
    }

    /// <summary>판 설정 — 연속 루프 판(<c>batch=1</c>, 호스트가 런타임 데이터 출처가 테이블인지 확인).</summary>
    public QABoss103Config ToConfig(int run, int retry, string outDir) => new QABoss103Config
    {
        Seed = BaseSeed,
        Run = Math.Max(1, run),
        Tier = Tier,
        OutDir = outDir ?? "",
        Retry = retry,
        Batch = 1,
        Order = (QABoss103Character[])Order.Clone(),
        TraitSeed = TraitSeed,
        Nightly = Name,
    };

    public string TierText => Tier.HasValue ? Tier.Value.ToString() : "auto";

    public string Describe() =>
        $"{Name} · 기준 시드 {BaseSeed} · 특성 시드 {(TraitSeed != 0 ? TraitSeed.ToString(CultureInfo.InvariantCulture) : "기준 시드와 같음")} · 봇 단계 {TierText} · " +
        $"자리 순서 {QABoss103Plan.FormatOrder(Order)} · {Runs}판";
}

/// <summary>할 일 108 야간 QA 폴더·파일 이름(순수).</summary>
public static class QANightlyPaths
{
    public const string BatchPrefix = "N108-";
    public const string BatchPattern = "N108-2*";
    public const string RecordingsFile = "recordings.txt";
    /// <summary>할 일 120 — 이벤트 시각표(<see cref="QANightlyEvent"/>).</summary>
    public const string EventsFile = "events.tsv";
    /// <summary>할 일 120 — 리플레이 클립 목록(<see cref="QANightlyRecording"/> 형식, 메모 = 이벤트 이름).</summary>
    public const string ClipsFile = "clips.txt";
    public const string SummaryFile = "nightly-summary.md";
    public const string ScenarioReportPrefix = "48-";

    /// <summary>배치 폴더 이름 — <c>N108-&lt;yyyyMMdd_HHmm&gt;-&lt;시드 이름&gt;</c>(이름순 = 시각순).</summary>
    public static string BatchFolderName(string seedName, DateTime at) =>
        BatchPrefix + at.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture) + "-" + seedName;

    /// <summary>날짜별 공유 폴더 — <c>&lt;루트&gt;/&lt;yyyy-MM-dd&gt;/&lt;시드 이름&gt;</c>. 루트가 비면 null(복사 안 함). 날짜 = 배치 시작 날(자정을 넘겨도 한 밤 = 한 폴더).</summary>
    public static string ShareDir(string root, DateTime nightStart, string seedName)
    {
        if (string.IsNullOrWhiteSpace(root))
            return null;
        return Path.Combine(root.Trim(), nightStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), seedName);
    }

    /// <summary>48 시나리오 리포트 폴더(<c>48-yyyyMMdd_HHmmss[_n][-접미]</c>)의 시각. 형식이 아니면 null.</summary>
    public static DateTime? ScenarioReportTime(string folderName)
    {
        if (folderName == null || !folderName.StartsWith(ScenarioReportPrefix, StringComparison.Ordinal) || folderName.Length < ScenarioReportPrefix.Length + 15)
            return null;
        string stamp = folderName.Substring(ScenarioReportPrefix.Length, 15);
        return DateTime.TryParseExact(stamp, "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime t) ? t : (DateTime?)null;
    }

    /// <summary>판 시작(로컬 시각) 뒤에 생긴 48 리포트 폴더인가 — 같은 초 여유 1초.</summary>
    public static bool IsScenarioReportOf(string folderName, DateTime attemptStartLocal)
    {
        DateTime? t = ScenarioReportTime(folderName);
        return t.HasValue && t.Value >= attemptStartLocal.AddSeconds(-1);
    }

    /// <summary>svn info 출력 → 리비전 숫자. 숫자가 아니면(실패·na·빈 값) <c>unknown</c>.</summary>
    public static string NormalizeRevision(string svnOutput)
    {
        string s = (svnOutput ?? "").Trim();
        if (s.Length == 0)
            return "unknown";
        foreach (char c in s)
            if (c < '0' || c > '9')
                return "unknown";
        return s;
    }
}

/// <summary>할 일 108 녹화 기록 한 줄 — <c>recordings.txt</c>(탭 구분: 판·재실행·영상 경로·공유 복사 대상·메모).</summary>
public sealed class QANightlyRecording
{
    public int Run;
    public int Retry;
    public string Path = "";
    public string Shared = "";
    public string Note = "";

    public string Format() => $"{Run}\t{Retry}\t{Clean(Path)}\t{Clean(Shared)}\t{Clean(Note)}";

    public static bool TryParse(string line, out QANightlyRecording rec)
    {
        rec = null;
        if (string.IsNullOrWhiteSpace(line))
            return false;
        string[] f = line.TrimEnd('\r').Split('\t');
        if (f.Length < 3 || !int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int run) ||
            !int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int retry))
            return false;
        rec = new QANightlyRecording { Run = run, Retry = retry, Path = f[2], Shared = f.Length > 3 ? f[3] : "", Note = f.Length > 4 ? f[4] : "" };
        return true;
    }

    public static List<QANightlyRecording> ParseAll(string text)
    {
        var list = new List<QANightlyRecording>();
        if (string.IsNullOrEmpty(text))
            return list;
        foreach (string line in text.Split('\n'))
            if (TryParse(line, out QANightlyRecording r))
                list.Add(r);
        return list;
    }

    private static string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
}

/// <summary>할 일 108 리포트 머리말(순수) — batch.txt 키 목록과 아침 확인용 <c>nightly-summary.md</c>.</summary>
public static class QANightlyHeader
{
    public const string KeySeedName = "nightly_seed";

    /// <summary>배치 머리 키(batch.txt). 103 리포트가 읽는 키(title·seed·total·data_source_mode·xlsx_svn_rev·git_*·unity_version)에 야간 키를 더한다.</summary>
    public static List<KeyValuePair<string, string>> BatchKeys(QANightlySeed seed, DateTime startedAt, string dataSourceMode, string xlsxRev,
                                                              string gitBranch, string gitCommit, string unityVersion, string recordMode, string shareDir)
    {
        return new List<KeyValuePair<string, string>>
        {
            KV("title", $"할 일 108 야간 QA — {seed.Name}"),
            KV("started_at", startedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
            KV("seed", seed.BaseSeed.ToString(CultureInfo.InvariantCulture)),
            KV("total", seed.Runs.ToString(CultureInfo.InvariantCulture)),
            KV("data_source_mode", string.IsNullOrEmpty(dataSourceMode) ? "unknown" : dataSourceMode),
            KV("xlsx_svn_rev", QANightlyPaths.NormalizeRevision(xlsxRev)),
            KV("git_commit", string.IsNullOrWhiteSpace(gitCommit) ? "unknown" : gitCommit.Trim()),
            KV("git_branch", string.IsNullOrWhiteSpace(gitBranch) ? "unknown" : gitBranch.Trim()),
            KV("unity_version", unityVersion ?? ""),
            KV("scenario", QALobbyScenarios.Nightly108Id),
            KV(KeySeedName, seed.Name),
            KV("nightly_trait_seed", seed.TraitSeed.ToString(CultureInfo.InvariantCulture)),
            KV("nightly_tier", seed.TierText),
            KV("nightly_order", QABoss103Plan.FormatOrder(seed.Order)),
            KV("nightly_note", seed.Note.Replace('\n', ' ')),
            KV("record", string.IsNullOrEmpty(recordMode) ? QANightlyLocalSettings.RecordOff : recordMode),
            KV("share_dir", shareDir ?? ""),
        };
    }

    /// <summary>
    /// 아침 확인용 요약(마크다운) — 머리말 + 판 결과(runs.csv 의 판·재실행·결과) + 녹화 목록 + (할 일 120) 이벤트 시각표·리플레이 클립.
    /// </summary>
    public static string Summary(IReadOnlyDictionary<string, string> batch, IReadOnlyList<QANightlyRecording> recordings,
                                 IReadOnlyList<QABoss103Attempt> attempts, string loopState, string reportPath,
                                 IReadOnlyList<QANightlyEvent> events = null, IReadOnlyList<QANightlyRecording> clips = null)
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(Val(batch, "title", "할 일 108 야간 QA")).Append("\n\n");
        sb.Append("| 항목 | 값 |\n|---|---|\n");
        Row(sb, "시드 이름", Val(batch, KeySeedName, "unknown"));
        Row(sb, "데이터 출처(툴바)", Val(batch, "data_source_mode", "unknown") + " — 판마다 런타임 출처는 runs.csv data_source 열(테이블이 아니면 오류 판)");
        Row(sb, "GameData.xlsx SVN 리비전", Val(batch, "xlsx_svn_rev", "unknown"));
        Row(sb, "브랜치 / 커밋", Val(batch, "git_branch", "unknown") + " / " + Val(batch, "git_commit", "unknown"));
        Row(sb, "Unity", Val(batch, "unity_version", "unknown"));
        Row(sb, "기준 시드 / 특성 시드", Val(batch, "seed", "unknown") + " / " + Val(batch, "nightly_trait_seed", "0") + "(0 = 기준 시드)");
        Row(sb, "봇 단계 / 자리 순서", Val(batch, "nightly_tier", "auto") + " / " + Val(batch, "nightly_order", ""));
        Row(sb, "판 수", Val(batch, "total", ""));
        Row(sb, "시작", Val(batch, "started_at", ""));
        Row(sb, "루프 상태", string.IsNullOrEmpty(loopState) ? "-" : loopState);
        Row(sb, "녹화", Val(batch, "record", "off"));
        Row(sb, "공유 폴더", Val(batch, "share_dir", "(설정 없음 — 복사 안 함)"));
        Row(sb, "리포트", string.IsNullOrEmpty(reportPath) ? "-" : reportPath);

        sb.Append("\n## 판 결과\n\n| 판 | 재실행 | 결과 | 런타임 데이터 출처 |\n|---|---|---|---|\n");
        if (attempts == null || attempts.Count == 0)
            sb.Append("| - | - | 기록 없음 | - |\n");
        else
            foreach (QABoss103Attempt a in attempts)
                sb.Append("| ").Append(a.Run).Append(" | ").Append(a.Retry).Append(" | ").Append(Cell(a.OutcomeText)).Append(" | ").Append(Cell(a.DataSource)).Append(" |\n");

        sb.Append("\n## 녹화\n\n");
        if (recordings == null || recordings.Count == 0)
            sb.Append("- 없음(녹화 꺼짐·OBS 연결 실패는 loop.log 경고)\n");
        else
            foreach (QANightlyRecording r in recordings)
                sb.Append("- 판 ").Append(r.Run).Append(r.Retry > 0 ? " 재실행" : "").Append(": ").Append(r.Path.Length > 0 ? r.Path : "(경로 없음)")
                  .Append(r.Shared.Length > 0 ? " → " + r.Shared : "").Append(r.Note.Length > 0 ? " · " + r.Note : "").Append('\n');

        if (Val(batch, "record", "off") == QANightlyLocalSettings.RecordReplay || (clips != null && clips.Count > 0))
        {
            sb.Append("\n## 리플레이 클립\n\n");
            if (clips == null || clips.Count == 0)
                sb.Append("- 없음(이벤트 없음·OBS 저장 실패는 loop.log 경고)\n");
            else
                foreach (QANightlyRecording c in clips)
                    sb.Append("- 판 ").Append(c.Run).Append(c.Retry > 0 ? " 재실행" : "").Append(" [").Append(c.Note.Length > 0 ? c.Note : "-").Append("]: ")
                      .Append(c.Path.Length > 0 ? c.Path : "(경로 없음)").Append(c.Shared.Length > 0 ? " → " + c.Shared : "").Append('\n');
        }

        sb.Append("\n## 이벤트 시각표\n\n");
        sb.Append("영상 타임코드 = 그 판 영상(녹화 목록의 같은 판) 안의 위치(H:MM:SS). 게임 시각 = Play 의 Time.time(초). 같은 이벤트 5초 안 반복은 한 줄.\n\n");
        sb.Append("| 판 | 재실행 | 이벤트 | 시각 | 게임 시각(s) | 영상 타임코드 | 상세 |\n|---|---|---|---|---|---|---|\n");
        if (events == null || events.Count == 0)
            sb.Append("| - | - | 없음 | - | - | - | - |\n");
        else
            foreach (QANightlyEvent e in events)
                sb.Append("| ").Append(e.Run).Append(" | ").Append(e.Retry).Append(" | ").Append(Cell(e.Name)).Append(" | ")
                  .Append(e.At.ToString("HH:mm:ss", CultureInfo.InvariantCulture)).Append(" | ")
                  .Append(double.IsNaN(e.GameSeconds) ? "-" : e.GameSeconds.ToString("F1", CultureInfo.InvariantCulture)).Append(" | ")
                  .Append(QANightlyTimecode.Format(e.VideoSeconds)).Append(" | ").Append(Cell(e.Detail)).Append(" |\n");
        return sb.ToString();
    }

    private static void Row(StringBuilder sb, string k, string v) => sb.Append("| ").Append(k).Append(" | ").Append(Cell(v)).Append(" |\n");

    private static string Cell(string s) => string.IsNullOrEmpty(s) ? "-" : s.Replace("|", "\\|").Replace('\n', ' ');

    private static string Val(IReadOnlyDictionary<string, string> d, string k, string fallback) =>
        d != null && d.TryGetValue(k, out string v) && !string.IsNullOrEmpty(v) ? v : fallback;

    private static KeyValuePair<string, string> KV(string k, string v) => new KeyValuePair<string, string>(k, v ?? "");
}
#endif
