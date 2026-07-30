#if UNITY_EDITOR
using UnityEditor;

/// <summary>
/// QA 자동화 설정(EditorPrefs 저장, 재컴파일 없이 조정). 설정 창(QASettingsWindow)과
/// 부트스트랩(QAAutoBootstrap)이 공유한다.
///
/// 배치 주의: QAAutoBootstrap이 Assembly-CSharp(에디터 패스)에 있으므로 이 파일도
/// Editor 폴더가 아닌 QA 루트에 둬 같은 어셈블리에 포함시켜야 참조된다.
/// (Assembly-CSharp은 Assembly-CSharp-Editor를 참조할 수 없음.) EditorPrefs는 에디터 전용이라 #if UNITY_EDITOR.
/// </summary>
public static class QASettings
{
    public const string AutoRunKey = "QA.AutoRunFromBootstrap";
    public const string DurationKey = "QA.SessionDuration";
    public const string RepeatKey = "QA.RepeatCount";
    public const string PlayerCountKey = "QA.PlayerCount";

    /// <summary>0.BootStrapScene에서 Play 시 QA 하네스를 자동 생성할지.</summary>
    public static bool AutoRun
    {
        get => EditorPrefs.GetBool(AutoRunKey, true);
        set => EditorPrefs.SetBool(AutoRunKey, value);
    }

    /// <summary>맵 도달 후 각 사이클을 돌릴 시간(초).</summary>
    public static float Duration
    {
        get => EditorPrefs.GetFloat(DurationKey, 180f);
        set => EditorPrefs.SetFloat(DurationKey, value);
    }

    /// <summary>QA 전체 사이클 반복 횟수. 0 이하이면 무한.</summary>
    public static int RepeatCount
    {
        get => EditorPrefs.GetInt(RepeatKey, 1);
        set => EditorPrefs.SetInt(RepeatKey, value);
    }

    /// <summary>멀티플레이 인원 수(호스트 포함). 3=호스트1+MPPM 가상 플레이어2. 1=단독.</summary>
    public static int PlayerCount
    {
        get => EditorPrefs.GetInt(PlayerCountKey, 3);
        set => EditorPrefs.SetInt(PlayerCountKey, value);
    }
}
#endif
