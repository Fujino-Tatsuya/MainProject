#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>할 일 120 이벤트 표 한 줄 — 호스트(메인 에디터) 로그 한 줄이 이 이벤트인가. 바늘(<see cref="Needles"/>)은 모두 들어 있어야 한다.</summary>
public sealed class QANightlyEventRule
{
    public readonly string Name;
    public readonly string Label;
    /// <summary>에러·예외·Assert 로그에만.</summary>
    public readonly bool ErrorLogOnly;
    public readonly string[] Needles;
    /// <summary>
    /// 기록 전용 — 시각표(events.tsv)에만 남긴다: 클립 예약·5초 반복 거르기 없음, 리포트 이벤트 요약에서 빠진다(문제 신호가 아니라 진행 기록).
    /// </summary>
    public bool Trace;
    /// <summary>상세 칸 최대 길이(<see cref="QANightlyEvents.Detail"/>).</summary>
    public int DetailMax = QANightlyEvents.DefaultDetailMax;

    public QANightlyEventRule(string name, string label, bool errorLogOnly, params string[] needles)
    {
        Name = name;
        Label = label;
        ErrorLogOnly = errorLogOnly;
        Needles = needles ?? Array.Empty<string>();
    }

    public bool Matches(string condition, LogType type)
    {
        if (string.IsNullOrEmpty(condition))
            return false;
        if (ErrorLogOnly && type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
            return false;
        foreach (string n in Needles)
            if (condition.IndexOf(n, StringComparison.Ordinal) < 0)
                return false;
        return true;
    }
}

/// <summary>
/// 할 일 120 야간 QA 이벤트 — 이벤트 표(<see cref="Table"/>, 코드 한 곳)와 로그 매칭(순수, EditMode <c>QANightlyTests</c>).
/// 메인 에디터 = 호스트라 호스트 로그만 본다(가상 플레이어 클론 로그는 따로 프로세스). 새 이벤트는 <see cref="Table"/> 에 한 줄 더한다.
/// </summary>
public static class QANightlyEvents
{
    public const string Error = "error";
    public const string Stuck = "stuck";
    public const string Stall = "stall";
    public const string Death = "death";
    public const string Wipe = "wipe";
    public const string Timeout = "timeout";
    /// <summary>할 일 140 — 특성 라운드 진행(기록 전용 <see cref="QANightlyEventRule.Trace"/>).</summary>
    public const string Trait = "trait";

    public const int DefaultDetailMax = 160;
    /// <summary>계측 필드가 붙는 줄(재배치·특성 라운드) — 뒤쪽 필드가 잘리지 않게.</summary>
    public const int LongDetailMax = 800;

    /// <summary>위에서부터 처음 맞는 줄. 같은 이름 여러 줄 = 같은 이벤트의 여러 근거.</summary>
    public static readonly QANightlyEventRule[] Table =
    {
        new QANightlyEventRule(Error, "QA 오류 — 에러·예외 로그", true),
        new QANightlyEventRule(Error, "QA 오류 — QA 발견 Error", false, "[QA][Error]["),
        new QANightlyEventRule(Error, "QA 오류 — QA 발견 Critical", false, "[QA][Critical]["),
        new QANightlyEventRule(Error, "QA 오류 — 판 Error(run_end)", false, "event=run_end ", "outcome=Error "),
        new QANightlyEventRule(Stall, "정체 감시 — Combat 90초 전투 활동 0", false, "event=stall "),
        new QANightlyEventRule(Stuck, "끼임 — 20초 진척 없음 재배치(호스트)", false, "event=qa_relocate "),
        new QANightlyEventRule(Stuck, "끼임 — 벽 관통·벽 타기(GameplayInvariant 발견)", false, "][GameplayInvariant]"),
        new QANightlyEventRule(Stuck, "끼임 — 이동 불능(Softlock 발견)", false, "][Softlock]"),
        new QANightlyEventRule(Death, "사망(Alive → 그 밖)", false, "event=death "),
        new QANightlyEventRule(Wipe, "전멸(run_end Wipe)", false, "event=run_end ", "outcome=Wipe "),
        new QANightlyEventRule(Timeout, "시간초과 — 30분(run_end Timeout)", false, "event=run_end ", "outcome=Timeout "),
        new QANightlyEventRule(Timeout, "시간초과 — 조기(목숨 0·보스 HP 5분 불변)", false, "event=early_timeout "),
        new QANightlyEventRule(Trait, "특성 라운드 — 라운드·완료·자동 선택·획득 ID(기록 전용)", false, "event=trait_round ") { Trace = true, DetailMax = LongDetailMax },
    };

    /// <summary>쉼표 목록 → 켤 이벤트 이름(소문자). 비면 null(= 표 전체).</summary>
    public static HashSet<string> ParseEnabled(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return null;
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (string part in csv.Split(',', ';', ' '))
        {
            string n = part.Trim().ToLowerInvariant();
            if (n.Length > 0)
                set.Add(n);
        }
        return set.Count > 0 ? set : null;
    }

    /// <summary>로그 한 줄 → 이벤트 표 줄. <paramref name="enabled"/> null = 전체. 없으면 null.</summary>
    public static QANightlyEventRule Match(string condition, LogType type, HashSet<string> enabled)
    {
        foreach (QANightlyEventRule r in Table)
            if ((enabled == null || enabled.Contains(r.Name)) && r.Matches(condition, type))
                return r;
        return null;
    }

    /// <summary>기록 전용 이벤트 이름인가(<see cref="QANightlyEventRule.Trace"/>) — 리포트 요약에서 뺀다.</summary>
    public static bool IsTrace(string name)
    {
        foreach (QANightlyEventRule r in Table)
            if (r.Trace && r.Name == name)
                return true;
        return false;
    }

    /// <summary>시각표 상세 칸 — 첫 줄, 탭·줄바꿈 없이, 최대 <paramref name="max"/> 자.</summary>
    public static string Detail(string condition, int max = DefaultDetailMax)
    {
        string s = condition ?? "";
        int nl = s.IndexOf('\n');
        if (nl >= 0)
            s = s.Substring(0, nl);
        s = s.Replace('\t', ' ').Replace('\r', ' ').Trim();
        return s.Length > max ? s.Substring(0, max) : s;
    }
}

/// <summary>할 일 120 이벤트 시각표 한 줄 — <c>events.tsv</c>(탭: 판·재실행·이벤트·시각·게임 시각(초)·영상 타임코드(초)·상세).</summary>
public sealed class QANightlyEvent
{
    public const string TimeFormat = "yyyy-MM-dd HH:mm:ss.fff";

    public int Run;
    public int Retry;
    public string Name = "";
    /// <summary>로컬 벽시계.</summary>
    public DateTime At;
    /// <summary>Play 의 <c>Time.timeAsDouble</c>. 모르면 NaN.</summary>
    public double GameSeconds = double.NaN;
    /// <summary>그 판 영상 안의 위치(초). 모르면(녹화 없음·리플레이 모드) NaN.</summary>
    public double VideoSeconds = double.NaN;
    public string Detail = "";

    public string Format() =>
        $"{Run}\t{Retry}\t{Clean(Name)}\t{At.ToString(TimeFormat, CultureInfo.InvariantCulture)}\t{Num(GameSeconds)}\t{Num(VideoSeconds)}\t{Clean(Detail)}";

    public static bool TryParse(string line, out QANightlyEvent ev)
    {
        ev = null;
        if (string.IsNullOrWhiteSpace(line))
            return false;
        string[] f = line.TrimEnd('\r').Split('\t');
        if (f.Length < 4 || !int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int run) ||
            !int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int retry) ||
            !DateTime.TryParseExact(f[3], TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime at))
            return false;
        ev = new QANightlyEvent
        {
            Run = run, Retry = retry, Name = f[2], At = at,
            GameSeconds = f.Length > 4 ? ParseNum(f[4]) : double.NaN,
            VideoSeconds = f.Length > 5 ? ParseNum(f[5]) : double.NaN,
            Detail = f.Length > 6 ? f[6] : "",
        };
        return true;
    }

    public static List<QANightlyEvent> ParseAll(string text)
    {
        var list = new List<QANightlyEvent>();
        if (string.IsNullOrEmpty(text))
            return list;
        foreach (string line in text.Split('\n'))
            if (TryParse(line, out QANightlyEvent e))
                list.Add(e);
        return list;
    }

    private static string Num(double v) => double.IsNaN(v) ? "" : v.ToString("F1", CultureInfo.InvariantCulture);

    private static double ParseNum(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;

    private static string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
}

/// <summary>할 일 120 — 같은 이벤트가 몇 초 안에 쏟아질 때(에러 로그 폭주) 시각표를 덮지 않게 거른다. 판이 바뀌면 다시 센다.</summary>
public sealed class QANightlyEventThrottle
{
    public const double RepeatSeconds = 5.0;
    public const int MaxPerAttempt = 300;

    private readonly Dictionary<string, DateTime> _last = new Dictionary<string, DateTime>(StringComparer.Ordinal);
    private string _attempt;
    private int _count;

    public bool Allow(string attemptKey, string name, DateTime nowUtc)
    {
        if (_attempt != attemptKey)
        {
            _attempt = attemptKey;
            _last.Clear();
            _count = 0;
        }
        if (_count >= MaxPerAttempt)
            return false;
        if (_last.TryGetValue(name ?? "", out DateTime t) && (nowUtc - t).TotalSeconds < RepeatSeconds)
            return false;
        _last[name ?? ""] = nowUtc;
        _count++;
        return true;
    }
}

/// <summary>
/// 할 일 120 — 판 녹화의 기준점(<c>rec-active.txt</c>: 판·재실행·기준 UTC ticks·기준 때 녹화 경과 ms). 영상 타임코드 = 경과 ms + (이벤트 − 기준).
/// 기준 = StartRecord 직후 GetRecordStatus 응답 시각, 경과 = 그 응답의 <c>outputDuration</c>(없으면 <c>outputTimecode</c>, 둘 다 없으면 0 — 보정 없음).
/// </summary>
public sealed class QANightlyRecordAnchor
{
    public int Run;
    public int Retry;
    public DateTime AnchorUtc = DateTime.MinValue;
    public double OffsetMs;

    public string Format() =>
        $"{Run}\t{Retry}\t{(AnchorUtc == DateTime.MinValue ? "" : AnchorUtc.Ticks.ToString(CultureInfo.InvariantCulture))}\t{OffsetMs.ToString("F0", CultureInfo.InvariantCulture)}\n";

    /// <summary>108 형식(판·재실행만)도 읽는다 — 기준이 없으면 <see cref="AnchorUtc"/> = MinValue.</summary>
    public static QANightlyRecordAnchor Parse(string text)
    {
        string[] f = (text ?? "").Trim().Split('\t');
        var a = new QANightlyRecordAnchor();
        if (f.Length < 1 || !int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out a.Run))
            return null;
        int.TryParse(f.Length > 1 ? f[1] : "", NumberStyles.Integer, CultureInfo.InvariantCulture, out a.Retry);
        if (f.Length > 2 && long.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks) && ticks > 0)
            a.AnchorUtc = new DateTime(ticks, DateTimeKind.Utc);
        if (f.Length > 3 && double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double ms) && ms > 0)
            a.OffsetMs = ms;
        return a;
    }
}

/// <summary>할 일 120 영상 타임코드(순수).</summary>
public static class QANightlyTimecode
{
    /// <summary>GetRecordStatus 의 녹화 경과 — outputDuration(ms) 우선, 0 이면 outputTimecode, 둘 다 없으면 0.</summary>
    public static double OffsetMs(double outputDurationMs, string outputTimecode)
    {
        if (outputDurationMs > 0 && !double.IsNaN(outputDurationMs) && !double.IsInfinity(outputDurationMs))
            return outputDurationMs;
        double t = QAObsProtocol.ParseTimecodeMs(outputTimecode);
        return double.IsNaN(t) ? 0.0 : t;
    }

    /// <summary>이벤트의 영상 안 위치(초). 기준이 없으면 NaN, 기준보다 앞이면 0.</summary>
    public static double VideoSeconds(DateTime anchorUtc, double offsetMs, DateTime eventUtc)
    {
        if (anchorUtc == DateTime.MinValue)
            return double.NaN;
        double s = (eventUtc - anchorUtc).TotalSeconds + Math.Max(0.0, offsetMs) / 1000.0;
        return s < 0.0 ? 0.0 : s;
    }

    /// <summary><c>H:MM:SS</c>(영상 플레이어 탐색 입력용). NaN = <c>-</c>.</summary>
    public static string Format(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds))
            return "-";
        long total = (long)Math.Floor(Math.Max(0.0, seconds));
        return $"{total / 3600}:{total / 60 % 60:00}:{total % 60:00}";
    }
}

/// <summary>
/// 할 일 120 2단계 — 리플레이 클립 계획(순수, 상태는 <c>clip-pending.txt</c> 로 Play 진입·정지 도메인 리로드를 넘는다).
/// <list type="bullet">
/// <item>이벤트가 오면 <c>이벤트 + M초</c>(이벤트 뒤 구간까지 담게)에 저장 예약. 단 지난 저장 + 쿨다운보다 이르면 그때로 미룬다.</item>
/// <item>예약이 걸려 있는 동안 오는 이벤트(같은 시각대·쿨다운 안)는 새 클립을 만들지 않고 그 클립 이름에 합친다(이름 중복 없이 순서대로).</item>
/// </list>
/// 버퍼 길이 N 은 M + 쿨다운보다 길어야 첫 이벤트 앞 구간이 남는다(런북 §5.3).
/// </summary>
public sealed class QANightlyClipPlanner
{
    public DateTime DueUtc = DateTime.MinValue;
    public DateTime LastSaveUtc = DateTime.MinValue;
    public int Run;
    public int Retry;
    public readonly List<string> Names = new List<string>();

    public bool Pending => Names.Count > 0;

    public void Add(DateTime nowUtc, string name, int run, int retry, double afterSeconds, double cooldownSeconds)
    {
        if (string.IsNullOrEmpty(name))
            return;
        if (Pending)
        {
            if (!Names.Contains(name))
                Names.Add(name);
            return;
        }
        DateTime due = nowUtc.AddSeconds(Math.Max(0.0, afterSeconds));
        if (LastSaveUtc != DateTime.MinValue)
        {
            DateTime gap = LastSaveUtc.AddSeconds(Math.Max(0.0, cooldownSeconds));
            if (gap > due)
                due = gap;
        }
        DueUtc = due;
        Run = run;
        Retry = retry;
        Names.Add(name);
    }

    /// <summary>예약 시각이 지났으면 꺼낸다(<paramref name="force"/> = 시각과 관계없이 — 배치 끝). 꺼내면 마지막 저장 = 지금.</summary>
    public bool TryTake(DateTime nowUtc, bool force, out List<string> names, out int run, out int retry)
    {
        names = null;
        run = Run;
        retry = Retry;
        if (!Pending || (!force && nowUtc < DueUtc))
            return false;
        names = new List<string>(Names);
        Names.Clear();
        DueUtc = DateTime.MinValue;
        LastSaveUtc = nowUtc;
        return true;
    }

    public string Format() =>
        $"{T(DueUtc)}\t{T(LastSaveUtc)}\t{Run}\t{Retry}\t{string.Join("+", Names)}\n";

    public static QANightlyClipPlanner Parse(string text)
    {
        var p = new QANightlyClipPlanner();
        // 줄바꿈만 지운다 — Trim() 은 예약이 없을 때 빈 이름 칸 앞 탭까지 지워 지난 저장 시각을 버린다.
        string[] f = (text ?? "").Trim('\r', '\n').Split('\t');
        if (f.Length < 5)
            return p;
        p.DueUtc = FromT(f[0]);
        p.LastSaveUtc = FromT(f[1]);
        int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out p.Run);
        int.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out p.Retry);
        foreach (string n in f[4].Split('+'))
            if (n.Trim().Length > 0 && !p.Names.Contains(n.Trim()))
                p.Names.Add(n.Trim());
        return p;
    }

    private static string T(DateTime t) => t == DateTime.MinValue ? "0" : t.Ticks.ToString(CultureInfo.InvariantCulture);

    private static DateTime FromT(string s) =>
        long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks) && ticks > 0 ? new DateTime(ticks, DateTimeKind.Utc) : DateTime.MinValue;
}

/// <summary>할 일 120 리플레이 클립 파일 이름(순수).</summary>
public static class QANightlyClipName
{
    public const int MaxEvents = 6;

    public const string DefaultExtension = ".mp4";

    /// <summary>
    /// <c>&lt;이름&gt;_&lt;HHmmss&gt;_r&lt;판&gt;[re]_&lt;이벤트+이벤트&gt;&lt;확장자&gt;</c> — 이름(시드 이름 = 팀원 이름)이 앞이라 공유 날짜 폴더에서 사람별로 모인다.
    /// 시각 = 클립 저장 시각(로컬). 재실행 판은 <c>r3re</c>. 이벤트가 많으면 앞 6개 + <c>+etc</c>. 확장자는 OBS 가 만든 그대로(없으면 .mp4).
    /// </summary>
    public static string FileName(string seed, DateTime savedAtLocal, int run, int retry, IReadOnlyList<string> events, string extension)
    {
        var names = new List<string>();
        if (events != null)
            foreach (string e in events)
            {
                string s = Safe(e);
                if (s.Length > 0 && !names.Contains(s))
                    names.Add(s);
            }
        string ev = names.Count == 0 ? "event"
            : names.Count > MaxEvents ? string.Join("+", names.GetRange(0, MaxEvents)) + "+etc"
            : string.Join("+", names);
        string ext = string.IsNullOrEmpty(extension) ? DefaultExtension : extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;
        string seedPart = Safe(seed);
        return $"{(seedPart.Length > 0 ? seedPart : "nightly")}_{savedAtLocal.ToString("HHmmss", CultureInfo.InvariantCulture)}_r{Math.Max(0, run)}{(retry > 0 ? "re" : "")}_{ev}{ext}";
    }

    /// <summary>
    /// 이 이름의 클립 파일인가 — <c>&lt;이름&gt;_</c> 다음에 6자리 시각과 <c>_r</c> 이 와야 한다(이름이 <c>kim</c> 일 때 <c>kim_lee_…</c> 를 내 것으로 보지 않게).
    /// 보관 삭제는 이것만 지운다(다른 사람 파일·리포트는 건드리지 않는다).
    /// </summary>
    public static bool IsOwnClip(string fileName, string seed)
    {
        string f = Path.GetFileName(fileName ?? "");
        string prefix = Safe(seed);
        if (prefix.Length == 0 || !QANightlyRetention.IsVideoFile(f))
            return false;
        prefix += "_";
        if (!f.StartsWith(prefix, StringComparison.Ordinal) || f.Length < prefix.Length + 8)
            return false;
        for (int i = 0; i < 6; i++)
            if (f[prefix.Length + i] < '0' || f[prefix.Length + i] > '9')
                return false;
        return f[prefix.Length + 6] == '_' && f[prefix.Length + 7] == 'r';
    }

    /// <summary>이미 있으면 <c>_2</c>·<c>_3</c>… 을 붙인 경로.</summary>
    public static string Unique(string dir, string fileName, Func<string, bool> exists)
    {
        string path = Path.Combine(dir ?? "", fileName);
        if (exists == null || !exists(path))
            return path;
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);
        for (int i = 2; i < 1000; i++)
        {
            string p = Path.Combine(dir ?? "", $"{stem}_{i}{ext}");
            if (!exists(p))
                return p;
        }
        return Path.Combine(dir ?? "", $"{stem}_{Guid.NewGuid():N}{ext}");
    }

    private static string Safe(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s ?? "")
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_')
                sb.Append(c);
        return sb.ToString();
    }
}

/// <summary>
/// 할 일 120 — 공유 폴더 영상 보관(순수 고르기). <c>&lt;루트&gt;/&lt;MM-dd&gt;/</c> 바로 아래의 <b>자기 이름 클립</b>(<see cref="QANightlyClipName.IsOwnClip"/>)만 지운다 —
/// 날짜 폴더 이름이 정확히 <c>MM-dd</c> 가 아니면 건드리지 않고, 리포트·다른 사람 파일은 지우지 않는다.
/// 이름에 연도가 없으니 오늘 기준 과거로 읽는다(올해 그 날짜가 오늘보다 뒤면 작년).
/// </summary>
public static class QANightlyRetention
{
    public static readonly string[] VideoExtensions = { ".mkv", ".mp4", ".mov", ".flv", ".ts", ".m4v", ".webm", ".avi" };

    public static bool IsVideoFile(string fileName)
    {
        string ext = Path.GetExtension(fileName ?? "");
        foreach (string v in VideoExtensions)
            if (string.Equals(ext, v, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>
    /// 날짜 폴더 이름(<c>MM-dd</c>, 두 자리씩) → 오늘 이전의 가장 가까운 그 날짜. 올해 날짜가 미래면 작년, 2월 29일은 가장 가까운 윤년. 형식이 아니면 null.
    /// </summary>
    public static DateTime? FolderDate(string folderName, DateTime today)
    {
        string n = folderName ?? "";
        if (n.Length != 5 || n[2] != '-')
            return null;
        for (int y = today.Year; y >= today.Year - 4; y--)
            if (DateTime.TryParseExact(y.ToString("0000", CultureInfo.InvariantCulture) + "-" + n, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d) &&
                d.Date <= today.Date)
                return d.Date;
        return null;
    }

    /// <summary>보관 일수를 넘긴 날짜 폴더인가 — 오늘과의 차이 &gt; <paramref name="keepDays"/>. 0 이하 = 지우지 않음.</summary>
    public static bool IsExpired(string folderName, DateTime today, int keepDays)
    {
        if (keepDays <= 0)
            return false;
        DateTime? d = FolderDate(folderName, today);
        return d.HasValue && (today.Date - d.Value).TotalDays > keepDays;
    }

    /// <summary>날짜 폴더 이름들 중 지울 것(이름순).</summary>
    public static List<string> ExpiredFolders(IEnumerable<string> folderNames, DateTime today, int keepDays)
    {
        var list = new List<string>();
        if (folderNames != null)
            foreach (string n in folderNames)
                if (IsExpired(n, today, keepDays))
                    list.Add(n);
        list.Sort(StringComparer.Ordinal);
        return list;
    }
}
#endif
