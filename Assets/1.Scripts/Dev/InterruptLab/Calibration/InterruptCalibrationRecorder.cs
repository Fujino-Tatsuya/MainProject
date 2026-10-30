#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using System;
using System.Globalization;
using System.IO;
using System.Text;

/// <summary>
/// 간파 예측 보정 세션 기록(할 일 139) — <c>&lt;root&gt;/139calib-&lt;yyyyMMdd_HHmmss&gt;/samples.csv</c> 에 표본마다 즉시 한 줄 덧붙이고,
/// 세션 끝에 <c>summary.txt</c> 를 쓴다. 루트는 주입한다(기본 <see cref="DefaultRoot"/>, 테스트는 임시 폴더).
///
/// 답을 받은 표본만 <see cref="Commit"/> 으로 기록한다. 답 건너뛰기(Esc)는 Commit 을 부르지 않으면 된다 — seq·일치율에 들어가지 않는다.
/// 폴더·파일은 첫 Commit 때 만든다(헤더 1줄, UTF-8 BOM). 표본 0 개 세션은 아무 파일도 남기지 않는다.
/// </summary>
public sealed class InterruptCalibrationRecorder
{
    public const string DefaultRoot = "C:/UnityProject/QA";
    public const string FolderPrefix = "139calib-";
    public const string SamplesFileName = "samples.csv";
    public const string SummaryFileName = "summary.txt";

    static readonly Encoding Utf8Bom = new UTF8Encoding(true);
    static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    public string SessionFolder { get; }
    public string SamplesPath => Path.Combine(SessionFolder, SamplesFileName);
    public string SummaryPath => Path.Combine(SessionFolder, SummaryFileName);

    public InterruptCalibrationStats Stats { get; } = new InterruptCalibrationStats();

    /// <summary>기록된 표본 수 = 마지막 seq.</summary>
    public int Count { get; private set; }

    public InterruptCalibrationRecorder(string root, DateTime startedAt)
    {
        if (string.IsNullOrEmpty(root))
            root = DefaultRoot;
        SessionFolder = Path.Combine(root, FolderName(startedAt));
    }

    public static string FolderName(DateTime startedAt) =>
        FolderPrefix + startedAt.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

    /// <summary>답을 정해 표본을 기록한다 — seq 를 매기고 CSV 한 줄 덧붙이고 일치율에 넣는다.</summary>
    public void Commit(InterruptCalibrationSample sample, bool userAnswer)
    {
        if (sample == null)
            throw new ArgumentNullException(nameof(sample));

        sample.Seq = Count + 1;
        sample.UserAnswer = userAnswer;

        if (!File.Exists(SamplesPath))
        {
            Directory.CreateDirectory(SessionFolder);
            File.WriteAllText(SamplesPath, InterruptCalibrationCsv.Header + InterruptCalibrationCsv.NewLine, Utf8Bom);
        }
        File.AppendAllText(SamplesPath, InterruptCalibrationCsv.FormatRow(sample) + InterruptCalibrationCsv.NewLine, Utf8NoBom);

        Count = sample.Seq;
        Stats.Add(sample);
    }

    /// <summary>summary.txt 를 (다시) 쓴다. 표본이 없으면 쓰지 않고 null.</summary>
    public string WriteSummary()
    {
        if (Count == 0)
            return null;
        Directory.CreateDirectory(SessionFolder);
        File.WriteAllText(SummaryPath, Stats.BuildSummary(Path.GetFileName(SessionFolder)), Utf8Bom);
        return SummaryPath;
    }
}
#endif
