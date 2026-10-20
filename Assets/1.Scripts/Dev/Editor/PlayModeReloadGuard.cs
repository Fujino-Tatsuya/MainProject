using UnityEditor;
using UnityEditor.Compilation;

/// <summary>
/// 할 일 56.2. Play 중에는 어셈블리 리로드를 잠근다. Play 도중 도메인 리로드가 일어나면 [Serializable] 인
/// NGO NetworkSceneManager 가 생성자 없이 복원돼 SceneEventDataStore(Dictionary, 미직렬화)가 null 로 남고,
/// 정지 때 Dispose NRE 로 ShutdownInternal 이 끊겨 UDP 포트가 에디터에 남는다.
/// Play 중 Refresh 는 컴파일까지만 하고, 리로드는 Play 정지 뒤에 한다. "Script Changes While Playing" 설정은 건드리지 않는다.
/// </summary>
[InitializeOnLoad]
public static class PlayModeReloadGuard
{
    public enum LockAction
    {
        None,
        Lock,
        Unlock,
    }

    private static bool s_Locked;

    static PlayModeReloadGuard()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        CompilationPipeline.compilationFinished -= OnCompilationFinished;
        CompilationPipeline.compilationFinished += OnCompilationFinished;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        switch (Decide(change, s_Locked))
        {
            case LockAction.Lock:
                EditorApplication.LockReloadAssemblies();
                s_Locked = true;
                break;
            case LockAction.Unlock:
                EditorApplication.UnlockReloadAssemblies();
                s_Locked = false;
                break;
        }
    }

    private static void OnCompilationFinished(object context)
    {
        if (EditorApplication.isPlaying)
            UnityEngine.Debug.LogWarning("[ReloadGuard] Play 중 스크립트 컴파일 — 리로드는 Play 정지 후");
    }

    // ── 순수 함수 (EditMode 테스트 대상) ──

    /// <summary>Play 진입 완료 때 잠그고, 정지 시작 때 푼다. 편집 모드 진입 때 아직 잠겨 있으면 안전망으로 푼다.</summary>
    public static LockAction Decide(PlayModeStateChange change, bool locked)
    {
        switch (change)
        {
            case PlayModeStateChange.EnteredPlayMode:
                return locked ? LockAction.None : LockAction.Lock;
            case PlayModeStateChange.ExitingPlayMode:
            case PlayModeStateChange.EnteredEditMode:
                return locked ? LockAction.Unlock : LockAction.None;
            default:
                return LockAction.None;
        }
    }
}
