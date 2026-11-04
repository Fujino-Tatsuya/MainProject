#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using System;
using System.Globalization;
using System.Linq;

/// <summary>
/// 간파 보정 표본의 CSV 한 줄(할 일 139 → 191 5단계). 열 = 랩 앞 칸(seq·실시간·허수아비 창 설정) + QA 기록 열 전부(<see cref="InterruptQaCsv"/>, 그 seq 는 qa_seq)
/// + 랩 뒤 칸(대상 거리·각도·실제 창·사람 답). 숫자는 InvariantCulture, 값 없음(null) = 빈 칸, 쉼표·따옴표·줄바꿈 칸은 따옴표로 감싼다.
/// </summary>
public static class InterruptCalibrationCsv
{
    public const string NewLine = "\r\n";

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static readonly (string Name, Func<InterruptCalibrationSample, string> Value)[] Prefix =
    {
        ("seq", s => s.Seq.ToString(Inv)),
        ("real_time", s => F(s.RealTime)),
        ("window_mode", s => s.WindowMode?.ToString() ?? ""),
        ("window_open_len", s => F(s.WindowOpenLength)),
        ("window_closed_len", s => F(s.WindowClosedLength)),
    };

    static readonly (string Name, Func<InterruptCalibrationSample, string> Value)[] Suffix =
    {
        ("target_distance", s => F(s.TargetDistance)),
        ("target_angle", s => F(s.TargetAngle)),
        ("actual_window_open", s => B(s.ActualWindowOpen)),
        ("actual_open_elapsed", s => F(s.ActualOpenElapsed)),
        ("actual_remaining", s => F(s.ActualRemaining)),
        ("user_answer", s => B(s.UserAnswer)),
    };

    public static readonly string Header = string.Join(",",
        Prefix.Select(c => c.Name)
            .Concat(InterruptQaCsv.ColumnNames.Select(n => n == "seq" ? "qa_seq" : n))
            .Concat(Suffix.Select(c => c.Name)));

    public static int ColumnCount => Prefix.Length + InterruptQaCsv.ColumnCount + Suffix.Length;

    public static string FormatRow(InterruptCalibrationSample sample)
    {
        string qa = sample.Record != null
            ? InterruptQaCsv.FormatRow(sample.Record)
            : new string(',', InterruptQaCsv.ColumnCount - 1);
        return string.Join(",", Prefix.Select(c => Escape(c.Value(sample)))) + "," + qa + "," +
               string.Join(",", Suffix.Select(c => Escape(c.Value(sample))));
    }

    /// <summary>쉼표·따옴표·줄바꿈이 있으면 따옴표로 감싸고 안의 따옴표는 두 번 쓴다.</summary>
    public static string Escape(string value) => InterruptQaCsv.Escape(value);

    // 무기한 창 길이·남은 창(+∞) = "inf".
    static string F(float? v) =>
        !v.HasValue ? "" : float.IsPositiveInfinity(v.Value) ? "inf" : v.Value.ToString("0.#####", Inv);
    static string B(bool? v) => v.HasValue ? (v.Value ? "true" : "false") : "";
}
#endif
