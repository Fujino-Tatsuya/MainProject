using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 창 조작 보조(팀장 10-13). 첫 씬 로드 뒤 스스로 만들어 씬을 넘어 산다(씬 배치 없음).
/// - <b>Alt+Enter</b>: 창 모드 → 전체 화면(테두리 없는 전체 창), 전체 화면 → 창 모드. 바꾼 값은 <see cref="DisplayConfig"/> 에
///   저장한다 — 설정창 표시·다음 실행이 따라온다.
///   ⚠️ 플레이어 설정 allowFullscreenSwitch(Unity 자체 Alt+Enter, 창 ↔ FullScreenWindow)도 켜져 있다. 전체 화면 쪽을
///   같은 FullScreenWindow 로 맞춰 두 동작이 다른 모드로 다투지 않게 했다(독점 전체 화면은 설정창에서만 고른다).
/// - <b>커서 가두기</b>: 포커스가 있는 동안 커서를 게임 창 안에(<see cref="CursorLockMode.Confined"/>). 창 모드에서 커서가
///   창 밖으로 나가 클릭이 다른 창으로 새는 것을 막는다. 포커스를 잃으면 푼다(Alt+Tab 은 그대로 된다).
/// 🔴 에디터 Play 에서는 만들지 않는다 — 해상도는 DisplayConfig 도 에디터에서 안 건다, 커서는 에디터 조작을 막는다.
/// </summary>
public sealed class WindowFocusKeeper : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (Application.isEditor || FindAnyObjectByType<WindowFocusKeeper>() != null) return;
        var go = new GameObject(nameof(WindowFocusKeeper));
        DontDestroyOnLoad(go);
        go.AddComponent<WindowFocusKeeper>();
    }

    void Start() => OnApplicationFocus(Application.isFocused);

    void OnApplicationFocus(bool hasFocus)
    {
        Cursor.lockState = hasFocus ? CursorLockMode.Confined : CursorLockMode.None;
    }

    void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null || !kb.altKey.isPressed) return;
        if (!kb.enterKey.wasPressedThisFrame && !kb.numpadEnterKey.wasPressedThisFrame) return;

        DisplaySelection s = DisplayConfig.Current;
        s.windowMode = s.windowMode == WindowModeChoice.Windowed ? WindowModeChoice.Borderless : WindowModeChoice.Windowed;
        DisplayConfig.Set(s);   // 저장 + 적용(해상도는 저장된 선택 그대로)
    }
}
