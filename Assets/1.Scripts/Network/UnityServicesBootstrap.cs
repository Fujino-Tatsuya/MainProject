using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

public static class UnityServicesBootstrap
{
    private const string DefaultProfile = "Player1";
    private const string PlayerNameArgument = "-name";
    private const string DiagTag = "UnityServicesBootstrap";

    private static Task _initializationTask;
    private static SessionError _failure;

    public static bool IsReady =>
        UnityServices.State == ServicesInitializationState.Initialized &&
        AuthenticationService.Instance.IsSignedIn;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void BeginInitialization()
    {
        _initializationTask = InitializeAsync();
    }

    public static bool IsAvailable(out SessionError unavailable)
    {
        if (string.IsNullOrWhiteSpace(Application.cloudProjectId))
        {
            unavailable = new SessionError(ErrorCatalog.UgsProjectNotLinked);
            return false;
        }

        if (!_failure.IsNone)
        {
            unavailable = _failure;
            return false;
        }

        if (UnityServices.State == ServicesInitializationState.Uninitialized)
        {
            unavailable = new SessionError(ErrorCatalog.UgsNotInitialized);
            return false;
        }

        if (UnityServices.State == ServicesInitializationState.Initializing ||
            (_initializationTask != null && !_initializationTask.IsCompleted))
        {
            unavailable = new SessionError(ErrorCatalog.UgsInitializing);
            return false;
        }

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            unavailable = new SessionError(ErrorCatalog.UgsNotSignedIn);
            return false;
        }

        unavailable = default;
        return true;
    }

    private static async Task InitializeAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Application.cloudProjectId))
            {
                _failure = new SessionError(ErrorCatalog.UgsProjectNotLinked);
                Debug.LogError($"[Relay] {_failure.LogText}");
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

            _failure = default;
            NetworkDiagnosticsLog.Log(
                $"{DiagTag}.InitializeAsync",
                $"완료 state={UnityServices.State} signedIn={authentication.IsSignedIn} " +
                $"profile={authentication.Profile} playerId={authentication.PlayerId} " +
                $"cloudProjectId='{Application.cloudProjectId}'");
        }
        catch (Exception exception)
        {
            _failure = new SessionError(ErrorCatalog.UgsInitFailed, detail: exception.Message);
            Debug.LogError($"[Relay] {_failure.LogText}\n{exception}");
            NetworkDiagnosticsLog.LogError($"{DiagTag}.InitializeAsync", exception.ToString());
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
