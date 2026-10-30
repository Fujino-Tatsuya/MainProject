#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;
using Debug = UnityEngine.Debug;

/// <summary>
/// 할 일 116 — QA 실행 중 메인 에디터·MPPM 가상 플레이어(클론 에디터) 창을 포커스를 뺏지 않고 최소화한다(사용자 요청).
/// Win32 <c>ShowWindow(SW_SHOWMINNOACTIVE)</c> — 활성 창을 바꾸지 않는다. 실패해도 예외를 던지지 않는다(배치는 계속).
/// 클론 찾기 = <c>Unity.exe</c> 중 명령줄에 이 프로젝트의 <c>Library/VP/</c> 가 들어간 프로세스(PowerShell CIM 1회, 상한 8초).
/// QA 창(Tools/QA) ▸ 103 보스전 ▸ 창 최소화 로 수동 실행.
/// </summary>
public static class QAWindowMinimizer
{
    private const string Tag = "[QA103/Window]";
    private const int SwShowMinNoActive = 7;
    private const int QueryTimeoutMs = 8000;

    public static void MinimizeAll()
    {
        MinimizeMainEditor("menu");
        MinimizeVirtualPlayers("menu");
    }

    /// <summary>이 에디터 프로세스의 보이는 최상위 창(소유자 없는 것)을 최소화. 최소화한 창 수.</summary>
    public static int MinimizeMainEditor(string why)
    {
        try
        {
            int n = MinimizeProcesses(new HashSet<int> { Process.GetCurrentProcess().Id });
            Debug.Log($"{Tag} event=minimize target=main why={why} windows={n}");
            return n;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"{Tag} event=minimize target=main why={why} ok=false error={e.GetType().Name}: {e.Message}");
            return 0;
        }
    }

    /// <summary>이 프로젝트 가상 플레이어(클론) 에디터 창을 최소화. 최소화한 창 수(클론이 없으면 0).</summary>
    public static int MinimizeVirtualPlayers(string why)
    {
        try
        {
            string root = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            List<int> pids = ParseClonePids(QueryUnityProcesses(), root);
            int n = MinimizeProcesses(new HashSet<int>(pids));
            Debug.Log($"{Tag} event=minimize target=virtual_players why={why} processes={pids.Count} windows={n}");
            return n;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"{Tag} event=minimize target=virtual_players why={why} ok=false error={e.GetType().Name}: {e.Message}");
            return 0;
        }
    }

    /// <summary>
    /// <c>pid|명령줄</c> 줄들에서 명령줄에 <paramref name="projectRoot"/>/Library/VP/ 가 있는 pid(순수 — EditMode 테스트).
    /// 경로 구분자·대소문자·따옴표는 무시한다.
    /// </summary>
    public static List<int> ParseClonePids(string output, string projectRoot)
    {
        var pids = new List<int>();
        if (string.IsNullOrEmpty(output) || string.IsNullOrEmpty(projectRoot))
            return pids;
        string needle = Normalize(projectRoot).TrimEnd('/') + "/library/vp/";
        foreach (string raw in output.Split('\n'))
        {
            string line = raw.Trim();
            int bar = line.IndexOf('|');
            if (bar <= 0 || !int.TryParse(line.Substring(0, bar), out int pid))
                continue;
            if (Normalize(line.Substring(bar + 1)).Contains(needle) && !pids.Contains(pid))
                pids.Add(pid);
        }
        return pids;
    }

    private static string Normalize(string s) => s.Replace('\\', '/').Replace("\"", "").ToLowerInvariant();

    private static string QueryUnityProcesses()
    {
        const string script =
            "Get-CimInstance Win32_Process -Filter \"Name='Unity.exe'\" | ForEach-Object { \"$($_.ProcessId)|$($_.CommandLine)\" }";
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var psi = new ProcessStartInfo("powershell", "-NoProfile -NonInteractive -EncodedCommand " + encoded)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using (Process p = Process.Start(psi))
        {
            if (p == null)
                return "";
            string output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(QueryTimeoutMs))
            {
                try { p.Kill(); } catch { }
                return "";
            }
            return output;
        }
    }

    // ── Win32 ────────────────────────────────────────────────

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmdShow);

    private const uint GwOwner = 4;

    private static int MinimizeProcesses(HashSet<int> pids)
    {
        if (pids.Count == 0 || UnityEngine.Application.platform != UnityEngine.RuntimePlatform.WindowsEditor)
            return 0;
        var targets = new List<IntPtr>();
        EnumWindowsProc callback = (hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pids.Contains((int)pid) && IsWindowVisible(hWnd) && !IsIconic(hWnd) && GetWindow(hWnd, GwOwner) == IntPtr.Zero)
                targets.Add(hWnd);
            return true;
        };
        EnumWindows(callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        foreach (IntPtr hWnd in targets)
            ShowWindow(hWnd, SwShowMinNoActive);
        return targets.Count;
    }
}
#endif
