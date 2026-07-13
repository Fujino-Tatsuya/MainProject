using System.Diagnostics;
using Debug = UnityEngine.Debug;

/// <summary>
/// 빌드 버전 스터터링 방지를 위한 로그 클래스.
/// 에디터에서만 동작하는 Debug.Log 래퍼 — [Conditional] 덕분에
/// 빌드에서는 호출 자체(문자열 보간 비용 포함)가 컴파일 단계에서 제거된다.
/// </summary>
public static class Edit
{
    [Conditional("UNITY_EDITOR")]
    public static void Log(object message, UnityEngine.Object context = null)
    {
        Debug.Log(message, context);
    }

    [Conditional("UNITY_EDITOR")]
    public static void LogWarning(object message, UnityEngine.Object context = null)
    {
        Debug.LogWarning(message, context);
    }

    [Conditional("UNITY_EDITOR")]
    public static void LogError(object message, UnityEngine.Object context = null)
    {
        Debug.LogError(message, context);
    }
}
