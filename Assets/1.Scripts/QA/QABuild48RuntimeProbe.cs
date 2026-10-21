using System;
using System.IO;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 할 일 48 standalone QA 전용 탐침. 평소 실행에서는 명령행 스위치가 없어 즉시 끝난다.
/// <c>-qa48-initial-screenshot &lt;path&gt;</c>는 최초 연결 대기 화면, <c>-qa48-screenshot &lt;path&gt;</c>는
/// 로비를 한 번 본 뒤 연결 대기 씬으로 돌아왔을 때 증거 화면을 남긴다.
/// </summary>
public sealed class QABuild48RuntimeProbe : MonoBehaviour
{
    string _screenshotPath;
    string _initialScreenshotPath;
    string _commandFile;
    bool _sawLobby;
    bool _captured;
    float _waitSceneEnteredAt = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        string screenshot = ArgumentValue("-qa48-screenshot");
        string initialScreenshot = ArgumentValue("-qa48-initial-screenshot");
        string commandFile = ArgumentValue("-qa48-command-file");
        if (string.IsNullOrWhiteSpace(screenshot) && string.IsNullOrWhiteSpace(initialScreenshot) && string.IsNullOrWhiteSpace(commandFile))
            return;

        var go = new GameObject("QA48 Runtime Probe");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        var probe = go.AddComponent<QABuild48RuntimeProbe>();
        probe._screenshotPath = screenshot;
        probe._initialScreenshotPath = initialScreenshot;
        probe._commandFile = commandFile;
        Debug.Log($"[QA48-Probe] active screenshot='{screenshot}' initial='{initialScreenshot}' command='{commandFile}'");
    }

    void Update()
    {
        if (!string.IsNullOrWhiteSpace(_commandFile) && File.Exists(_commandFile))
        {
            string command = File.ReadAllText(_commandFile).Trim();
            File.Delete(_commandFile);
            if (string.Equals(command, "shutdown-transport", StringComparison.OrdinalIgnoreCase))
            {
                NetworkManager manager = NetworkManager.Singleton;
                if (manager != null && manager.IsListening)
                {
                    _sawLobby = true;
                    manager.Shutdown();
                    Debug.Log("[QA48-Probe] transport shutdown executed");
                }
                else
                {
                    Debug.LogWarning("[QA48-Probe] transport shutdown ignored — NetworkManager is not listening");
                }
            }
        }

        string scene = SceneManager.GetActiveScene().name;
        if (scene == "3.LobbyScene")
            _sawLobby = true;

        if (!_captured && !_sawLobby && scene == "6.DemoConnectScene" && !string.IsNullOrWhiteSpace(_initialScreenshotPath))
        {
            if (_waitSceneEnteredAt < 0f)
                _waitSceneEnteredAt = Time.realtimeSinceStartup;
            if (Time.realtimeSinceStartup - _waitSceneEnteredAt >= 2f)
                Capture(_initialScreenshotPath, "initial wait");
            return;
        }

        if (!_sawLobby || _captured || scene != "6.DemoConnectScene")
        {
            _waitSceneEnteredAt = -1f;
            return;
        }

        if (_waitSceneEnteredAt < 0f)
            _waitSceneEnteredAt = Time.realtimeSinceStartup;
        if (Time.realtimeSinceStartup - _waitSceneEnteredAt < 0.5f)
            return;

        Capture(_screenshotPath, "reconnect wait");
    }

    void Capture(string path, string kind)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        ScreenCapture.CaptureScreenshot(path);
        _captured = true;
        Debug.Log($"[QA48-Probe] {kind} screenshot requested '{path}'");
    }

    static string ArgumentValue(string key)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++)
            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return string.Empty;
    }
}
