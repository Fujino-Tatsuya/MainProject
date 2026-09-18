using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 멀티 접속 경로 전용 진단 로그. 실행 파일 옆에 <c>network.log</c> 를 남긴다.
///
/// 왜 따로 만드나 — 빌드에는 콘솔이 없고 Player.log 는 엔진 로그에 묻힌다.
/// 게다가 접속 거부의 결정적 단서(NGO 의 "NetworkConfig mismatch" 경고)는
/// <b>호스트 쪽</b> 로그에만 찍혀서, 양쪽 파일을 나란히 놓고 비교해야 원인이 보인다.
///
/// 크래시로 죽어도 남아야 하므로 <see cref="StreamWriter.AutoFlush"/> 로 즉시 기록하고,
/// 종료 시점에는 꼬리말만 붙이고 닫는다.
/// </summary>
public static class NetworkDiagnosticsLog
{
    public const string FileName = "network.log";

    /// <summary>폭주하는 로그가 디스크를 채우지 않도록 하는 상한.</summary>
    private const int MaxLines = 50000;

    /// <summary>
    /// 이 태그가 들어간 <see cref="Debug"/> 로그는 직접 호출하지 않아도 함께 수집한다.
    /// (경고·에러·예외는 태그와 무관하게 전부 수집한다.)
    /// </summary>
    private static readonly string[] CapturedTags =
    {
        "[SceneFlow]", "[Relay]", "[NGO]",
        "Netcode", "NetworkConfig", "NetworkManager", "UnityTransport",
        "Relay", "Connection", "Disconnect", "Approval",
    };

    private const string SelfPrefix = "[NetDiag]";

    private static readonly object Gate = new object();

    private static StreamWriter _writer;
    private static string _filePath;
    private static int _lineCount;
    private static bool _initialized;
    private static bool _closed;
    private static bool _footerWritten;
    private static bool _truncationNoticeWritten;

    /// <summary>실제로 기록 중인 파일 경로. 열기에 실패했으면 빈 문자열.</summary>
    public static string FilePath => _filePath ?? string.Empty;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        lock (Gate)
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            _closed = false;
            _footerWritten = false;
            _lineCount = 0;
            _truncationNoticeWritten = false;

            if (!TryOpenWriter())
            {
                return;
            }

            WriteHeader();
        }

        Application.logMessageReceivedThreaded += HandleUnityLog;
        Application.quitting += MarkSessionEnd;
        AppDomain.CurrentDomain.ProcessExit += HandleProcessExit;

        Debug.Log($"{SelfPrefix} network.log 기록 시작 path={FilePath}");
    }

    /// <summary>태그 없는 한 줄 기록.</summary>
    public static void Log(string message)
    {
        Write("INFO", message);
    }

    /// <summary>
    /// 접속 경로의 한 단계를 기록한다. <paramref name="tag"/> 는 호출 지점
    /// (예: <c>LobbySceneManager.StartRelayJoin</c>).
    /// </summary>
    public static void Log(string tag, string message)
    {
        Write("INFO", string.IsNullOrEmpty(tag) ? message : $"{tag} {message}");
    }

    public static void LogWarning(string tag, string message)
    {
        Write("WARN", string.IsNullOrEmpty(tag) ? message : $"{tag} {message}");
    }

    public static void LogError(string tag, string message)
    {
        Write("ERROR", string.IsNullOrEmpty(tag) ? message : $"{tag} {message}");
    }

    /// <summary>
    /// 파일에 남기면서 Unity 콘솔에도 같이 띄운다. 에디터에서 따라가기 위한 통로다.
    /// 콘솔로 나간 줄은 <see cref="HandleUnityLog"/> 가 접두어로 걸러 중복 기록하지 않는다.
    /// </summary>
    public static void LogAndEcho(string tag, string message)
    {
        Write("INFO", string.IsNullOrEmpty(tag) ? message : $"{tag} {message}");
        Debug.Log($"{SelfPrefix} {tag} {message}");
    }

    /// <summary>여러 줄짜리 스냅샷을 블록으로 남긴다.</summary>
    public static void LogBlock(string title, IEnumerable<string> lines)
    {
        Write("INFO", $"--- {title} ---");
        if (lines != null)
        {
            foreach (var line in lines)
            {
                Write("INFO", $"    {line}");
            }
        }

        Write("INFO", $"--- /{title} ---");
    }

    /// <summary>종료 전에 강제로 디스크에 밀어 넣는다.</summary>
    public static void Flush()
    {
        lock (Gate)
        {
            try
            {
                _writer?.Flush();
            }
            catch (Exception)
            {
                // 진단 로그가 게임을 죽이면 안 된다.
            }
        }
    }

    /// <summary>
    /// 종료 꼬리말을 한 번만 붙이고 디스크에 밀어 넣는다. 파일은 <b>닫지 않는다</b> —
    /// <see cref="Application.quitting"/> 와 <c>OnApplicationQuit</c> 의 호출 순서는
    /// 보장되지 않아서, 여기서 닫아버리면 종료 직전의 Shutdown 로그가 통째로 사라진다.
    /// 실제 닫기는 프로세스가 끝날 때 <see cref="Close"/> 가 한다.
    /// </summary>
    public static void MarkSessionEnd()
    {
        lock (Gate)
        {
            if (_footerWritten || _writer == null || _closed)
            {
                return;
            }

            _footerWritten = true;
            WriteLineUnlocked(string.Empty);
            WriteLineUnlocked($"=== 종료 시작 {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        }

        Flush();
    }

    /// <summary>파일을 닫는다. 종료 경로에서 여러 번 불려도 안전하다.</summary>
    public static void Close()
    {
        lock (Gate)
        {
            if (_closed || _writer == null)
            {
                return;
            }

            _closed = true;

            try
            {
                _writer.WriteLine($"=== 세션 종료 {DateTime.Now:yyyy-MM-dd HH:mm:ss} ({_lineCount}줄) ===");
                _writer.Flush();
                _writer.Dispose();
            }
            catch (Exception)
            {
                // 무시 — 종료 중이다.
            }
            finally
            {
                _writer = null;
            }
        }
    }

    private static void HandleProcessExit(object sender, EventArgs args)
    {
        Close();
    }

    private static bool TryOpenWriter()
    {
        foreach (var directory in CandidateDirectories())
        {
            if (string.IsNullOrEmpty(directory))
            {
                continue;
            }

            try
            {
                var path = Path.Combine(directory, FileName);
                var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                _filePath = path;
                return true;
            }
            catch (Exception)
            {
                // 다음 후보로 — 쓰기 권한이 없는 위치일 수 있다(Program Files 등).
            }
        }

        Debug.LogWarning($"{SelfPrefix} network.log 를 열 수 없어 파일 기록을 건너뜁니다.");
        return false;
    }

    /// <summary>
    /// 1순위는 실행 파일 폴더 — 빌드를 돌린 사람이 바로 찾을 수 있는 자리다.
    /// (에디터에서는 프로젝트 루트가 된다. <c>Application.dataPath</c> 가 <c>&lt;project&gt;/Assets</c> 이므로.)
    /// 쓰기가 막히면 persistentDataPath 로 물러선다.
    /// </summary>
    private static IEnumerable<string> CandidateDirectories()
    {
        string executableDirectory = null;
        try
        {
            executableDirectory = Path.GetDirectoryName(Application.dataPath);
        }
        catch (Exception)
        {
            // 무시 — persistentDataPath 로 간다.
        }

        yield return executableDirectory;
        yield return Application.persistentDataPath;
    }

    private static void WriteHeader()
    {
        string commandLine;
        try
        {
            commandLine = string.Join(" ", Environment.GetCommandLineArgs());
        }
        catch (Exception)
        {
            commandLine = "(읽기 실패)";
        }

        WriteLineUnlocked($"=== network.log · {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        WriteLineUnlocked($"    product        : {Application.companyName}/{Application.productName} {Application.version}");
        WriteLineUnlocked($"    unity          : {Application.unityVersion}");
        WriteLineUnlocked($"    platform       : {Application.platform} isEditor={Application.isEditor}");
        WriteLineUnlocked($"    cloudProjectId : '{Application.cloudProjectId}'");
        WriteLineUnlocked($"    dataPath       : {Application.dataPath}");
        WriteLineUnlocked($"    persistentPath : {Application.persistentDataPath}");
        WriteLineUnlocked($"    commandLine    : {commandLine}");
        WriteLineUnlocked($"    logFile        : {_filePath}");
        WriteLineUnlocked(string.Empty);
        WriteLineUnlocked("    ※ 호스트 쪽과 클라 쪽 network.log 를 나란히 놓고 NetworkConfig 해시를 비교할 것.");
        WriteLineUnlocked(string.Empty);
    }

    private static void Write(string level, string message)
    {
        lock (Gate)
        {
            if (_writer == null || _closed)
            {
                return;
            }

            if (_lineCount >= MaxLines)
            {
                if (!_truncationNoticeWritten)
                {
                    _truncationNoticeWritten = true;
                    WriteLineUnlocked($"=== {MaxLines}줄 상한에 도달해 이후 기록을 생략합니다 ===");
                }

                return;
            }

            WriteLineUnlocked($"[{DateTime.Now:HH:mm:ss.fff}][{level}] {message}");
        }
    }

    private static void WriteLineUnlocked(string line)
    {
        try
        {
            _writer.WriteLine(line);
            _lineCount++;
        }
        catch (Exception)
        {
            // 디스크가 꽉 찼거나 핸들이 죽었다 — 진단 로그 때문에 게임을 멈추지 않는다.
        }
    }

    /// <summary>
    /// Unity 콘솔로 나가는 모든 줄을 훑어, 접속과 관련된 것만 파일로 옮긴다.
    /// NGO 내부 경고(예: "NetworkConfig mismatch...")는 우리가 호출부를 갖고 있지 않으므로
    /// 이 통로가 유일한 수집 경로다.
    /// </summary>
    private static void HandleUnityLog(string condition, string stackTrace, LogType type)
    {
        if (condition == null)
        {
            return;
        }

        // 우리가 방금 내보낸 에코는 다시 담지 않는다.
        if (condition.StartsWith(SelfPrefix, StringComparison.Ordinal))
        {
            return;
        }

        var isProblem = type != LogType.Log;
        if (!isProblem && !MatchesCapturedTag(condition))
        {
            return;
        }

        var level = type switch
        {
            LogType.Warning => "WARN",
            LogType.Error => "ERROR",
            LogType.Assert => "ASSERT",
            LogType.Exception => "EXCEPTION",
            _ => "UNITY",
        };

        Write(level, condition);

        // 스택은 예외·에러에만 붙인다. 경고까지 붙이면 파일을 읽을 수 없게 된다.
        if ((type == LogType.Exception || type == LogType.Error) && !string.IsNullOrEmpty(stackTrace))
        {
            foreach (var line in stackTrace.Split('\n'))
            {
                var trimmed = line.TrimEnd('\r');
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    Write(level, $"    at {trimmed}");
                }
            }
        }
    }

    private static bool MatchesCapturedTag(string condition)
    {
        foreach (var tag in CapturedTags)
        {
            if (condition.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}
