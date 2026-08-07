using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

public static class UnityServicesBootstrap
{
    private const string DefaultProfile = "Player1";
    private const string PlayerNameArgument = "-name";

    private static Task _initializationTask;
    private static string _failureReason = string.Empty;

    public static bool IsReady =>
        UnityServices.State == ServicesInitializationState.Initialized &&
        AuthenticationService.Instance.IsSignedIn;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void BeginInitialization()
    {
        _initializationTask = InitializeAsync();
    }

    public static bool IsAvailable(out string unavailableReason)
    {
        if (string.IsNullOrWhiteSpace(Application.cloudProjectId))
        {
            unavailableReason =
                "Unity Services 프로젝트 연결(cloudProjectId)이 설정되지 않았습니다.";
            return false;
        }

        if (!string.IsNullOrEmpty(_failureReason))
        {
            unavailableReason = _failureReason;
            return false;
        }

        if (UnityServices.State == ServicesInitializationState.Uninitialized)
        {
            unavailableReason = "Unity Services가 초기화되지 않았습니다.";
            return false;
        }

        if (UnityServices.State == ServicesInitializationState.Initializing ||
            (_initializationTask != null && !_initializationTask.IsCompleted))
        {
            unavailableReason = "Unity Services 초기화 중입니다. 잠시 후 다시 시도하세요.";
            return false;
        }

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            unavailableReason = "Unity Services 익명 로그인이 완료되지 않았습니다.";
            return false;
        }

        unavailableReason = string.Empty;
        return true;
    }

    private static async Task InitializeAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Application.cloudProjectId))
            {
                _failureReason =
                    "Unity Services 프로젝트 연결(cloudProjectId)이 설정되지 않았습니다.";
                Debug.LogError($"[Relay] {_failureReason}");
                return;
            }

            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
            }

            var authentication = AuthenticationService.Instance;
            if (!authentication.IsSignedIn)
            {
                var profile = ResolvePlayerProfile();
                authentication.SwitchProfile(profile);
                await authentication.SignInAnonymouslyAsync();
                Debug.Log(
                    $"[Relay] Unity Services 익명 로그인 완료 profile={profile} playerId={authentication.PlayerId}");
            }

            _failureReason = string.Empty;
        }
        catch (Exception exception)
        {
            _failureReason = $"Unity Services 초기화 또는 익명 로그인에 실패했습니다: {exception.Message}";
            Debug.LogError($"[Relay] {_failureReason}\n{exception}");
        }
    }

    private static string ResolvePlayerProfile()
    {
        var arguments = Environment.GetCommandLineArgs();
        for (var index = 0; index < arguments.Length - 1; index++)
        {
            if (!string.Equals(arguments[index], PlayerNameArgument, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var candidate = SanitizeProfile(arguments[index + 1]);
            if (!string.IsNullOrEmpty(candidate))
            {
                return candidate;
            }
        }

        return DefaultProfile;
    }

    private static string SanitizeProfile(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var buffer = new char[Math.Min(value.Length, 30)];
        var length = 0;
        foreach (var character in value)
        {
            var isAsciiLetter = character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
            var isAsciiDigit = character is >= '0' and <= '9';
            if (!isAsciiLetter && !isAsciiDigit && character != '-' && character != '_')
            {
                continue;
            }

            buffer[length++] = character;
            if (length == buffer.Length)
            {
                break;
            }
        }

        return length == 0 ? string.Empty : new string(buffer, 0, length);
    }
}
