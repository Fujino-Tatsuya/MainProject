public readonly struct SessionStartResult
{
    public readonly bool Success;
    public readonly string FailureReason;
    public readonly string ShareCode;

    public SessionStartResult(bool success, string failureReason, string shareCode)
    {
        Success = success;
        FailureReason = failureReason ?? string.Empty;
        ShareCode = shareCode ?? string.Empty;
    }

    public static SessionStartResult Succeeded(string shareCode = "")
    {
        return new SessionStartResult(true, string.Empty, shareCode);
    }

    public static SessionStartResult Failed(string failureReason)
    {
        if (string.IsNullOrWhiteSpace(failureReason))
        {
            failureReason = "세션 시작에 실패했습니다.";
        }

        return new SessionStartResult(false, failureReason, string.Empty);
    }
}
