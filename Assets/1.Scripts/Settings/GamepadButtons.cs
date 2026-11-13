using UnityEngine.InputSystem;

/// <summary>
/// 패드 고정 버튼 조회(할 일 278, Docs/tech/gamepad-support.md §2·§5) — 취소/뒤로·메뉴·확인·십자키.
/// 리바인드 대상이 아닌 UI·조준 공용 버튼만 여기서 읽는다(전투 액션은 입력 에셋 → <see cref="PlayerInputReader"/>).
/// </summary>
public static class GamepadButtons
{
    /// <summary>ESC 를 쓰던 사슬(조준 취소·패널 닫기·메뉴 뒤로)이 보는 값 — 키보드 ESC 또는 패드 B.</summary>
    public static bool BackPressedThisFrame => EscapePressedThisFrame || PadBackPressedThisFrame;

    public static bool EscapePressedThisFrame
    {
        get
        {
            Keyboard keyboard = Keyboard.current;
            return keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
        }
    }

    /// <summary>패드 B(오른쪽 버튼).</summary>
    public static bool PadBackPressedThisFrame
    {
        get
        {
            Gamepad pad = Gamepad.current;
            return pad != null && pad.buttonEast.wasPressedThisFrame;
        }
    }

    /// <summary>패드 Start(메뉴).</summary>
    public static bool PadMenuPressedThisFrame
    {
        get
        {
            Gamepad pad = Gamepad.current;
            return pad != null && pad.startButton.wasPressedThisFrame;
        }
    }

    /// <summary>패드 A(아래 버튼) — 조준 확정·UI 확인.</summary>
    public static bool PadConfirmPressedThisFrame
    {
        get
        {
            Gamepad pad = Gamepad.current;
            return pad != null && pad.buttonSouth.wasPressedThisFrame;
        }
    }

    /// <summary>패드 A 누르고 있음 — 연출 스킵 홀드.</summary>
    public static bool PadConfirmHeld
    {
        get
        {
            Gamepad pad = Gamepad.current;
            return pad != null && pad.buttonSouth.isPressed;
        }
    }

    /// <summary>십자키 좌·우 누른 순간 — 왼쪽 -1, 오른쪽 +1, 없으면 0.</summary>
    public static int PadDpadHorizontalPressedThisFrame
    {
        get
        {
            Gamepad pad = Gamepad.current;
            if (pad == null)
                return 0;
            int step = 0;
            if (pad.dpad.left.wasPressedThisFrame) step -= 1;
            if (pad.dpad.right.wasPressedThisFrame) step += 1;
            return step;
        }
    }
}
