using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace BeaverLobby.Player.Dash
{
    /// <summary>
    /// 이 어셈블리 전용 <c>Edit</c> 로그 사본.
    ///
    /// BeaverLobby.Player.Dash asmdef은 Assembly-CSharp을 참조할 수 없어(역방향 = 순환 참조)
    /// Utility/Edit.cs를 쓸 수 없다. 동작·의미는 Edit과 동일하다 —
    /// [Conditional("UNITY_EDITOR")]로 빌드에서는 호출과 문자열 보간 비용까지 컴파일 단계에서 제거되고,
    /// [HideInCallstack]으로 콘솔 더블클릭 시 래퍼가 아닌 실제 호출 지점으로 이동한다.
    /// </summary>
    internal static class DashLog
    {
        [HideInCallstack]
        [Conditional("UNITY_EDITOR")]
        internal static void Log(object message, Object context = null)
        {
            Debug.Log(message, context);
        }

        [HideInCallstack]
        [Conditional("UNITY_EDITOR")]
        internal static void LogWarning(object message, Object context = null)
        {
            Debug.LogWarning(message, context);
        }
    }
}
