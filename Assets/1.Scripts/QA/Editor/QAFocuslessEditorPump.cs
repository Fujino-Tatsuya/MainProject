#if UNITY_EDITOR
using UnityEditor;

/// <summary>
/// 48.3 3차 — 에디터가 포커스를 잃어도 QA 시나리오 Play 가 스로틀되지 않게 매 에디터 업데이트마다 플레이어 루프를 요청한다.
/// 48 시나리오 모드(QASettings.Scenarios 가 있음)이면서 Play 중일 때만 켜지고, Play 를 벗어나면 해제한다.
/// 사용자 전역 설정(Preferences ▸ Interaction Mode 등 EditorPrefs)은 건드리지 않는다.
/// MPPM 가상 플레이어도 각자 에디터 프로세스라 같은 훅이 따로 돈다.
/// </summary>
[InitializeOnLoad]
public static class QAFocuslessEditorPump
{
    /// <summary>QA 전용 키(사용자 전역 설정 아님) — 펌프 없이 비교 실행할 때만 끈다. 기본 켬.</summary>
    public const string EnabledKey = "QA.FocuslessPump";
    private const string MenuPath = "Tools/QA/48/Focusless Play Pump";

    private static bool _hooked;

    [MenuItem(MenuPath, priority = 43)]
    private static void Toggle()
    {
        EditorPrefs.SetBool(EnabledKey, !EditorPrefs.GetBool(EnabledKey, true));
        UnityEngine.Debug.Log($"[QA] 포커스 없는 Play 펌프 설정 = {EditorPrefs.GetBool(EnabledKey, true)}(다음 Play 부터)");
    }

    [MenuItem(MenuPath, true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, EditorPrefs.GetBool(EnabledKey, true));
        return true;
    }

    static QAFocuslessEditorPump()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        // Enter Play Mode 옵션으로 도메인 리로드 없이 들어왔거나 Play 중 리로드된 경우
        if (EditorApplication.isPlaying)
            Sync(true);
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
            Sync(true);
        else if (change == PlayModeStateChange.ExitingPlayMode || change == PlayModeStateChange.EnteredEditMode)
            Sync(false);
    }

    private static void Sync(bool playing)
    {
        bool want = playing && EditorPrefs.GetBool(EnabledKey, true) && !string.IsNullOrWhiteSpace(QASettings.Scenarios);
        if (want == _hooked)
            return;
        _hooked = want;
        if (want)
            EditorApplication.update += Pump;
        else
            EditorApplication.update -= Pump;
        UnityEngine.Debug.Log($"[QA] 포커스 없는 Play 펌프 {(want ? "켬(48 시나리오 모드)" : "끔")}");
    }

    private static void Pump()
    {
        if (!EditorApplication.isPlaying)
        {
            Sync(false);
            return;
        }
        EditorApplication.QueuePlayerLoopUpdate();
    }
}
#endif
