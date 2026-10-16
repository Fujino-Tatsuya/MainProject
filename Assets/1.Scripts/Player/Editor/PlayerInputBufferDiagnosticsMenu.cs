using UnityEditor;

/// <summary>선입력 버퍼 진단 로그 켜기/끄기(할 일 44 후속). EditorPrefs 에 남아 다음 Play 에도 유지된다(기본 켜짐).</summary>
internal static class PlayerInputBufferDiagnosticsMenu
{
    const string MenuPath = "Tools/Debug/선입력 버퍼 진단 로그";

    [MenuItem(MenuPath)]
    static void Toggle()
    {
        bool enabled = !EditorPrefs.GetBool(PlayerInputBufferDiagnostics.EnabledPrefKey, true);
        EditorPrefs.SetBool(PlayerInputBufferDiagnostics.EnabledPrefKey, enabled);
        PlayerInputBufferDiagnostics.Enabled = enabled;
    }

    [MenuItem(MenuPath, true)]
    static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, EditorPrefs.GetBool(PlayerInputBufferDiagnostics.EnabledPrefKey, true));
        return true;
    }
}
