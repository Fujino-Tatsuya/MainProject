#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using System;
using System.Globalization;
using System.Linq;
using System.Text;

/// <summary>
/// 간파 예측 보정 표본의 CSV 한 줄(할 일 139). 헤더와 행은 같은 <see cref="Columns"/> 표에서 나온다.
/// 숫자는 InvariantCulture, 값 없음(null) = 빈 칸, 쉼표·따옴표·줄바꿈이 든 칸은 따옴표로 감싸고 따옴표는 두 번 쓴다.
/// </summary>
public static class InterruptCalibrationCsv
{
    public const string NewLine = "\r\n";

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static readonly (string Name, Func<InterruptCalibrationSample, string> Value)[] Columns =
    {
        ("seq", s => s.Seq.ToString(Inv)),
        ("real_time", s => F(s.RealTime)),
        ("window_mode", s => s.WindowMode?.ToString() ?? ""),
        ("window_open_len", s => F(s.WindowOpenLength)),
        ("window_closed_len", s => F(s.WindowClosedLength)),
        ("press_age", s => F(s.PressAge)),

        ("cast_time", s => F(s.PredictInput?.CastTime)),
        ("cast_pos_x", s => F(s.PredictInput?.CastPosition.x)),
        ("cast_pos_y", s => F(s.PredictInput?.CastPosition.y)),
        ("cast_pos_z", s => F(s.PredictInput?.CastPosition.z)),
        ("dash_dir_x", s => F(s.PredictInput?.DashDirection.x)),
        ("dash_dir_y", s => F(s.PredictInput?.DashDirection.y)),
        ("dash_dir_z", s => F(s.PredictInput?.DashDirection.z)),
        ("dash_speed", s => F(s.PredictInput?.DashSpeed)),
        ("dash_remaining", s => F(s.PredictInput?.DashRemaining)),
        ("skill_start_time", s => F(s.PredictInput?.SkillStartTime)),
        ("prep_delay", s => F(s.PredictInput?.PrepDelay)),
        ("has_target_in_range", s => B(s.PredictInput?.HasTargetInRange)),
        ("target_window_open", s => B(s.PredictInput?.TargetWindowOpen)),
        ("window_open_elapsed", s => F(s.PredictInput?.WindowOpenElapsed)),
        ("window_expected_duration", s => F(s.PredictInput?.WindowExpectedDuration)),

        ("resolve_pos_x", s => F(s.ResolvePosition?.x)),
        ("resolve_pos_y", s => F(s.ResolvePosition?.y)),
        ("resolve_pos_z", s => F(s.ResolvePosition?.z)),
        ("target_distance", s => F(s.TargetDistance)),
        ("target_angle", s => F(s.TargetAngle)),

        ("predicted_resolve_time", s => F(s.PredictedResolveTime)),
        ("predicted_window_remaining", s => F(s.PredictedWindowRemaining)),
        ("owner_forecast", s => s.OwnerForecast?.ToString() ?? ""),
        ("owner_reason", s => s.OwnerReason?.ToString() ?? ""),
        ("owner_repredicted", s => B(s.OwnerRepredicted)),
        ("owner_final_forecast", s => s.OwnerFinalForecast?.ToString() ?? ""),
        ("server_forecast", s => s.ServerForecast?.ToString() ?? ""),
        ("server_repredicted", s => B(s.ServerRepredicted)),
        ("server_final_forecast", s => s.ServerFinalForecast?.ToString() ?? ""),
        ("server_succeeded", s => B(s.ServerSucceeded)),

        ("actual_window_open", s => B(s.ActualWindowOpen)),
        ("actual_open_elapsed", s => F(s.ActualOpenElapsed)),
        ("actual_remaining", s => F(s.ActualRemaining)),

        ("user_answer", s => B(s.UserAnswer)),
    };

    public static readonly string Header = string.Join(",", Columns.Select(c => c.Name));

    public static int ColumnCount => Columns.Length;

    public static string FormatRow(InterruptCalibrationSample sample) =>
        string.Join(",", Columns.Select(c => Escape(c.Value(sample))));

    /// <summary>쉼표·따옴표·줄바꿈이 있으면 따옴표로 감싸고 안의 따옴표는 두 번 쓴다.</summary>
    public static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            return value;
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"').Append(value.Replace("\"", "\"\"")).Append('"');
        return sb.ToString();
    }

    static string F(float? v) => v.HasValue ? v.Value.ToString("0.#####", Inv) : "";
    static string B(bool? v) => v.HasValue ? (v.Value ? "true" : "false") : "";
}
#endif
