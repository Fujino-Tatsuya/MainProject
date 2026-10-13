using System.Collections.Generic;

/// <summary>
/// 세션 시작·연결 실패 하나 — 오류 코드(<see cref="ErrorCatalog"/>) + 자리표시자 인자 + 기술 상세.
/// 화면은 <see cref="DisplayText"/>(현재 언어 문구 + 작은 코드 표기), 로그는 <see cref="LogText"/>(코드 + 상세).
/// <see cref="Detail"/>(예외 메시지 등)은 화면 문구로 쓰지 않는다.
/// </summary>
public readonly struct SessionError
{
    public readonly ErrorCode Code;
    public readonly IReadOnlyDictionary<string, string> Args;
    public readonly string Detail;

    public SessionError(ErrorCode code, IReadOnlyDictionary<string, string> args = null, string detail = null)
    {
        Code = code.IsNone ? ErrorCatalog.NetStartFailed : code;
        Args = args;
        Detail = detail ?? string.Empty;
    }

    public bool IsNone => Code.IsNone;

    public string DisplayText => Code.Display(Args);

    public string LogText
    {
        get
        {
            if (Code.IsNone)
                return string.Empty;

            string args = Args == null || Args.Count == 0
                ? string.Empty
                : " " + string.Join(" ", EnumerateArgs());
            return string.IsNullOrEmpty(Detail) ? Code.Code + args : $"{Code.Code}{args} detail='{Detail}'";
        }
    }

    private IEnumerable<string> EnumerateArgs()
    {
        foreach (KeyValuePair<string, string> pair in Args)
            yield return $"{pair.Key}={pair.Value}";
    }

    public override string ToString() => LogText;
}

public readonly struct SessionStartResult
{
    public readonly bool Success;
    public readonly SessionError Error;
    public readonly string ShareCode;

    private SessionStartResult(bool success, SessionError error, string shareCode)
    {
        Success = success;
        Error = error;
        ShareCode = shareCode ?? string.Empty;
    }

    public static SessionStartResult Succeeded(string shareCode = "")
    {
        return new SessionStartResult(true, default, shareCode);
    }

    /// <summary>코드가 비면 <see cref="ErrorCatalog.NetStartFailed"/>.</summary>
    public static SessionStartResult Failed(SessionError error)
    {
        return new SessionStartResult(false, error.IsNone ? new SessionError(ErrorCatalog.NetStartFailed) : error, string.Empty);
    }

    public static SessionStartResult Failed(
        ErrorCode code,
        IReadOnlyDictionary<string, string> args = null,
        string detail = null)
    {
        return Failed(new SessionError(code, args, detail));
    }
}
