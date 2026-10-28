#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using System.Text;

/// <summary>
/// 할 일 138-2 — QA 재배치(끼임·벽) 직전 호스트 화면 스크린샷의 파일 이름·쿨다운(순수, EditMode <c>QABoss103StuckShotTests</c>).
/// 저장 = <c>&lt;배치 폴더&gt;/stuck/run&lt;n&gt;_r&lt;retry&gt;_&lt;HHmmss&gt;_&lt;원인&gt;_&lt;플레이어&gt;.png</c>. 캡처는 <see cref="QABoss103Relocator"/> 한 곳.
/// </summary>
public static class QABoss103StuckShot
{
    public const string Folder = "stuck";
    /// <summary>같은 플레이어 연속 이벤트 쿨다운(초) — 경계 = 정확히 10초 지나면 다시 찍는다.</summary>
    public const double CooldownSeconds = 10.0;
    public const string CooldownToken = "cooldown";

    private const string Forbidden = "\\/:*?\"<>|";

    /// <summary>파일 이름 조각 — 경로·Windows 금지 문자·공백·제어 문자를 지운다. 비면 <c>na</c>.</summary>
    public static string Clean(string part)
    {
        if (string.IsNullOrEmpty(part))
            return "na";
        var sb = new StringBuilder(part.Length);
        foreach (char c in part)
            if (Forbidden.IndexOf(c) < 0 && !char.IsWhiteSpace(c) && !char.IsControl(c))
                sb.Append(c);
        return sb.Length > 0 ? sb.ToString() : "na";
    }

    public static string FileName(int run, int retry, DateTime at, string reason, string player) =>
        $"run{run}_r{retry}_{at.ToString("HHmmss", CultureInfo.InvariantCulture)}_{Clean(reason)}_{Clean(player)}.png";

    /// <summary>배치 폴더가 없으면(출력 폴더 미지정 Play) null = 찍지 않는다.</summary>
    public static string PathFor(string outDir, string fileName) =>
        string.IsNullOrEmpty(outDir) ? null : Path.Combine(outDir, Folder, fileName);

    /// <summary>이 플레이어의 마지막 캡처(<paramref name="lastShot"/>, 없으면 null) 뒤 아직 쿨다운이면 true.</summary>
    public static bool InCooldown(double now, double? lastShot) =>
        lastShot.HasValue && now - lastShot.Value < CooldownSeconds;
}
#endif
