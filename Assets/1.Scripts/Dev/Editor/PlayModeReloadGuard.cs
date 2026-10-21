using UnityEditor;

/// <summary>
/// 할 일 56.2. Preferences ▸ General ▸ "Script Changes While Playing" 을 Recompile After Finished Playing 으로 맞춘다.
/// Play 도중 도메인 리로드가 일어나면 [Serializable] 인 NGO NetworkSceneManager 가 생성자 없이 복원돼
/// SceneEventDataStore(Dictionary, 미직렬화)가 null 로 남고, 정지 때 Dispose NRE 로 ShutdownInternal 이 끊겨
/// UDP 포트가 에디터에 남는다. 기본값 Recompile And Continue Playing(0)일 때만 바꾸고, 사용자가 고른 1·2 는 둔다.
/// LockReloadAssemblies 방식은 버렸다 — Play 중 컴파일이 나면 에디터가 '컴파일 중'으로 고정돼 MCP 가 전부 거부되고 정지도 못 한다.
///
/// 6000.3.16f1 에디터에서 확인: enum UnityEditor.ScriptChangesDuringPlayOptions
/// (RecompileAndContinuePlaying=0, RecompileAfterFinishedPlaying=1, StopPlayingAndRecompile=2, internal),
/// EditorPrefs 키 "ScriptCompilationDuringPlay"(키 없음 = 0). C# 쪽 캐시는 열린 Preferences 창의
/// PreferencesProvider.m_ScriptCompilationDuringPlay 뿐이라 EditorPrefs.SetInt 만 쓴다.
/// </summary>
[InitializeOnLoad]
public static class PlayModeReloadGuard
{
    public const string PrefKey = "ScriptCompilationDuringPlay";

    public const int RecompileAndContinuePlaying = 0;
    public const int RecompileAfterFinishedPlaying = 1;
    public const int StopPlayingAndRecompile = 2;

    static PlayModeReloadGuard()
    {
        int current = EditorPrefs.GetInt(PrefKey, RecompileAndContinuePlaying);
        int desired = Decide(current);
        if (desired == current)
            return;

        EditorPrefs.SetInt(PrefKey, desired);
        UnityEngine.Debug.Log("[ReloadGuard] Script Changes While Playing 을 Recompile After Finished Playing 으로 바꿈 — " +
                              "Play 중 핫 리로드가 NGO 를 깨 정지 때 NRE·포트 누수");
    }

    // ── 순수 함수 (EditMode 테스트 대상) ──

    /// <summary>현재 값 → 둘 값. 1·2 는 그대로, 0 과 범위 밖 값은 1.</summary>
    public static int Decide(int current)
    {
        return current == RecompileAfterFinishedPlaying || current == StopPlayingAndRecompile
            ? current
            : RecompileAfterFinishedPlaying;
    }
}
