using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>지금 조작 장치 종류(할 일 278). 마우스·키보드 한 묶음, 게임패드 한 묶음.</summary>
public enum InputDeviceKind
{
    MouseKeyboard,
    Gamepad,
}

/// <summary>
/// 장치 모드 전환 순수 규칙(할 일 278) — 마지막 입력 장치가 이긴다. 같은 프레임에 둘 다 있으면 마우스·키보드가 이긴다
/// (마우스를 움직이거나 누르면 즉시 마우스 모드로 돌아간다). 아무 입력도 없으면 그대로.
/// </summary>
public static class InputDeviceModeRules
{
    /// <summary>패드 스틱·트리거가 이 값을 넘는 순간을 패드 입력으로 본다.</summary>
    public const float GamepadAxisThreshold = 0.35f;
    /// <summary>마우스가 한 프레임에 이 픽셀 이상 움직이면 마우스 입력으로 본다(책상 위 미세 떨림 무시).</summary>
    public const float MouseMovePixels = 2f;

    public static InputDeviceKind Next(InputDeviceKind current, bool mouseKeyboardActivity, bool gamepadActivity)
    {
        if (mouseKeyboardActivity)
            return InputDeviceKind.MouseKeyboard;
        if (gamepadActivity)
            return InputDeviceKind.Gamepad;
        return current;
    }

    /// <summary>
    /// 축 값이 문턱을 <b>넘는 순간</b>만 참(엣지). 계속 기울이고 있으면 거짓 — 스틱을 든 채 마우스를 움직여도 깜빡이지 않는다.
    /// <paramref name="wasOver"/> 는 이번 값으로 갱신된다.
    /// </summary>
    public static bool AxisEdge(ref bool wasOver, float magnitude, float threshold = GamepadAxisThreshold)
    {
        bool over = magnitude >= threshold;
        bool edge = over && !wasOver;
        wasOver = over;
        return edge;
    }

    public static bool IsMouseMove(Vector2 delta, float pixels = MouseMovePixels) => delta.sqrMagnitude >= pixels * pixels;
}

/// <summary>
/// 장치 모드 결정 지점 하나(할 일 278, Docs/tech/gamepad-support.md §1). 조준·커서·HUD 호버·외곽선·UI 포커스는 전부 여기를 조회한다.
/// 매 프레임 갱신은 <see cref="InputDeviceModeDriver"/>(런타임 자동 생성)가 한다. 모드가 바뀔 때만 OS 커서를 숨기거나 되살린다.
/// </summary>
public static class InputDeviceMode
{
    public static InputDeviceKind Current { get; private set; } = InputDeviceKind.MouseKeyboard;
    public static bool IsGamepad => Current == InputDeviceKind.Gamepad;

    /// <summary>모드가 바뀌었다(인자 = 새 모드).</summary>
    public static event Action<InputDeviceKind> Changed;

    private static bool s_leftStickOver, s_rightStickOver, s_leftTriggerOver, s_rightTriggerOver;
    private static int s_tickedFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Current = InputDeviceKind.MouseKeyboard;
        Changed = null;
        s_leftStickOver = s_rightStickOver = s_leftTriggerOver = s_rightTriggerOver = false;
        s_tickedFrame = -1;
    }

    /// <summary>모드를 직접 정한다(테스트·디버그용). 바뀌면 <see cref="Changed"/> 를 알린다.</summary>
    public static void Set(InputDeviceKind kind)
    {
        if (Current == kind)
            return;

        Current = kind;
        Changed?.Invoke(kind);
    }

    /// <summary>이번 프레임 장치를 보고 모드를 갱신한다. 프레임당 한 번만 실제로 돈다.</summary>
    internal static void Tick()
    {
        if (s_tickedFrame == Time.frameCount)
            return;

        s_tickedFrame = Time.frameCount;
        Set(InputDeviceModeRules.Next(Current, SampleMouseKeyboard(), SampleGamepad()));
    }

    private static bool SampleMouseKeyboard()
    {
        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            if (InputDeviceModeRules.IsMouseMove(mouse.delta.ReadValue()))
                return true;
            if (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame ||
                mouse.middleButton.wasPressedThisFrame || mouse.scroll.ReadValue().sqrMagnitude > 0f)
                return true;
        }

        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.anyKey.wasPressedThisFrame;
    }

    private static bool SampleGamepad()
    {
        Gamepad pad = Gamepad.current;
        if (pad == null)
            return false;

        // 엣지 상태는 매 프레임 갱신해야 하므로 단락 평가 없이 모두 계산한다.
        bool axes = InputDeviceModeRules.AxisEdge(ref s_leftStickOver, pad.leftStick.ReadValue().magnitude);
        axes |= InputDeviceModeRules.AxisEdge(ref s_rightStickOver, pad.rightStick.ReadValue().magnitude);
        axes |= InputDeviceModeRules.AxisEdge(ref s_leftTriggerOver, pad.leftTrigger.ReadValue());
        axes |= InputDeviceModeRules.AxisEdge(ref s_rightTriggerOver, pad.rightTrigger.ReadValue());
        if (axes)
            return true;

        return pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame ||
               pad.buttonWest.wasPressedThisFrame || pad.buttonNorth.wasPressedThisFrame ||
               pad.leftShoulder.wasPressedThisFrame || pad.rightShoulder.wasPressedThisFrame ||
               pad.startButton.wasPressedThisFrame || pad.selectButton.wasPressedThisFrame ||
               pad.leftStickButton.wasPressedThisFrame || pad.rightStickButton.wasPressedThisFrame ||
               pad.dpad.up.wasPressedThisFrame || pad.dpad.down.wasPressedThisFrame ||
               pad.dpad.left.wasPressedThisFrame || pad.dpad.right.wasPressedThisFrame;
    }
}

/// <summary>
/// <see cref="InputDeviceMode"/> 를 매 프레임 갱신하고 모드가 바뀔 때 OS 커서를 숨기거나 되살린다(할 일 278).
/// 첫 씬 로드 전에 DDOL 로 하나 만든다 — 씬·프리팹 수정 없음. 다른 스크립트 Update 보다 먼저 돈다.
/// </summary>
[DefaultExecutionOrder(-2000)]
[AddComponentMenu("")]
public sealed class InputDeviceModeDriver : MonoBehaviour
{
    private static InputDeviceModeDriver s_instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateAtBoot()
    {
        if (s_instance != null)
            return;

        var go = new GameObject(nameof(InputDeviceModeDriver)) { hideFlags = HideFlags.HideInHierarchy };
        DontDestroyOnLoad(go);
        s_instance = go.AddComponent<InputDeviceModeDriver>();
    }

    private void OnEnable()
    {
        InputDeviceMode.Changed += ApplyCursor;
        InputDeviceMode.Changed += GamepadUiFocus.HandleModeChanged;
    }

    private void OnDisable()
    {
        InputDeviceMode.Changed -= ApplyCursor;
        InputDeviceMode.Changed -= GamepadUiFocus.HandleModeChanged;
    }

    private void OnDestroy()
    {
        if (s_instance == this)
            s_instance = null;
    }

    private void Update() => InputDeviceMode.Tick();

    // 패널들이 Update 에서 열고 닫은 뒤에 첫 선택을 건다(할 일 278 2단계).
    private void LateUpdate() => GamepadUiFocus.Tick();

    // 바뀔 때만 쓴다 — 다른 곳(설정창 등)이 정한 커서 상태를 매 프레임 덮지 않는다.
    private static void ApplyCursor(InputDeviceKind kind) => Cursor.visible = kind != InputDeviceKind.Gamepad;
}
