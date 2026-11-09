using System;
using UnityEngine;

/// <summary>
/// Windows 빌드 전용 하드웨어 커서(할 일 250). Unity <c>Cursor.SetCursor(Auto)</c> 는 Windows 에서 시스템 크기로
/// 줄여 그려 배율이 안 먹고, ForceSoftware 는 렌더 프레임만큼 늦다 — 그래서 확대한 픽셀로 HCURSOR 를 직접 만들고
/// 게임 창 WndProc 를 서브클래싱해 <c>WM_SETCURSOR</c>(클라이언트 영역)에서 그 커서를 세운다. 지연 0, 크기는 픽셀 그대로.
///
/// 네이티브 코드는 <c>UNITY_STANDALONE_WIN &amp;&amp; !UNITY_EDITOR</c> 에서만 컴파일된다 — 에디터(Windows 프로필이어도)·
/// MPPM·Mac/Linux 는 <see cref="IsSupported"/> = false 인 빈 껍데기만 남고, <see cref="SkillCursorView"/> 가 기존
/// <c>Cursor.SetCursor</c> 경로를 탄다. 에디터 창 WndProc 에 끼어들지 않기 위함.
/// </summary>
public static class WindowsNativeCursor
{
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    public static bool IsSupported => true;

    /// <summary>top-down BGRA(스트레이트 알파) 픽셀로 HCURSOR 를 만든다. 실패하면 <see cref="IntPtr.Zero"/>.</summary>
    public static IntPtr Create(byte[] bgraTopDown, int width, int height, Vector2Int hotspot)
    {
        var header = new BITMAPV5HEADER
        {
            bV5Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(BITMAPV5HEADER)),
            bV5Width = width,
            bV5Height = -height, // 음수 = top-down
            bV5Planes = 1,
            bV5BitCount = 32,
            bV5Compression = BI_BITFIELDS,
            bV5RedMask = 0x00FF0000,
            bV5GreenMask = 0x0000FF00,
            bV5BlueMask = 0x000000FF,
            bV5AlphaMask = 0xFF000000
        };

        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr color = CreateDIBSection(screenDc, ref header, DIB_RGB_COLORS, out IntPtr bits, IntPtr.Zero, 0);
        ReleaseDC(IntPtr.Zero, screenDc);
        if (color == IntPtr.Zero)
            return IntPtr.Zero;
        System.Runtime.InteropServices.Marshal.Copy(bgraTopDown, 0, bits, width * height * 4);

        // 단색 마스크 — 전부 0(AND 마스크 0 = 색 비트맵의 알파로 그림). 1bpp 행은 WORD 정렬.
        var maskBits = new byte[(width + 15) / 16 * 2 * height];
        IntPtr mask = CreateBitmap(width, height, 1, 1, maskBits);

        var info = new ICONINFO
        {
            fIcon = 0, // 커서(핫스팟 사용)
            xHotspot = (uint)hotspot.x,
            yHotspot = (uint)hotspot.y,
            hbmMask = mask,
            hbmColor = color
        };
        IntPtr cursor = CreateIconIndirect(ref info);

        // CreateIconIndirect 는 비트맵을 복사한다 — 원본은 바로 지운다.
        DeleteObject(color);
        if (mask != IntPtr.Zero)
            DeleteObject(mask);
        return cursor;
    }

    public static void Destroy(IntPtr cursor)
    {
        if (cursor == IntPtr.Zero)
            return;
        if (cursor == s_current)
            Show(IntPtr.Zero);
        DestroyCursor(cursor);
    }

    /// <summary>
    /// 이 커서를 게임 창에 세운다. Zero = 해제(원래 WndProc 복원 → Unity/시스템 커서).
    /// 창을 못 찾거나 서브클래싱이 실패하면 false — 호출부가 <c>Cursor.SetCursor</c> 로 대체한다.
    /// </summary>
    public static bool Show(IntPtr cursor)
    {
        s_current = cursor;
        if (cursor == IntPtr.Zero)
        {
            Uninstall();
            return true;
        }

        if (!Install())
        {
            s_current = IntPtr.Zero;
            return false;
        }

        if (s_visible)
            SetCursor(cursor); // 마우스를 움직이기 전에도 바로 바뀌게
        return true;
    }

    /// <summary>
    /// 메인 스레드에서 매 프레임(LateUpdate). <c>Cursor.visible</c> 을 WndProc 쪽에 알리고(숨김이면 Unity 처리에 맡긴다),
    /// Unity 가 프레임 중에 자기 커서(시스템 화살표)로 덮었으면 다시 세운다.
    /// 진단: 덮여 있던 프레임 수·WndProc 에서 되세운 횟수를 5초마다 Player.log 에 한 줄 남긴다.
    /// </summary>
    public static void Tick(bool visible)
    {
        s_visible = visible;
        if (s_current == IntPtr.Zero)
            return;

        s_diagFrames++;
        if (ShouldOwnCursor() && GetCursor() != s_current)
        {
            s_diagOverwrittenFrames++;
            SetCursor(s_current);
        }

        float now = Time.realtimeSinceStartup;
        if (now - s_diagStart >= DiagIntervalSeconds)
        {
            Debug.Log($"[NativeCursor] {now - s_diagStart:F1}s — 프레임 {s_diagFrames} 중 덮인 프레임 {s_diagOverwrittenFrames} · WndProc 되세움 {s_diagWndProcFixes}");
            s_diagStart = now;
            s_diagFrames = 0;
            s_diagOverwrittenFrames = 0;
            s_diagWndProcFixes = 0;
        }
    }

    private const float DiagIntervalSeconds = 5f;
    private static float s_diagStart;
    private static int s_diagFrames;
    private static int s_diagOverwrittenFrames;
    private static int s_diagWndProcFixes;

    // 우리 커서를 세워도 되는 때 = 커서 있음·보임·게임 창이 앞·마우스가 게임 창 클라이언트 위. 창 밖·비활성·숨김에선 절대 안 세운다.
    private static bool ShouldOwnCursor()
    {
        if (s_current == IntPtr.Zero || !s_visible || s_hwnd == IntPtr.Zero)
            return false;
        if (GetForegroundWindow() != s_hwnd)
            return false;
        if (!GetCursorPos(out POINT screen) || WindowFromPoint(screen) != s_hwnd)
            return false;
        POINT client = screen;
        ScreenToClient(s_hwnd, ref client);
        GetClientRect(s_hwnd, out RECT rect);
        return client.x >= rect.left && client.x < rect.right && client.y >= rect.top && client.y < rect.bottom;
    }

    // ── 서브클래싱 ─────────────────────────────────────────────

    private static IntPtr s_hwnd;
    private static IntPtr s_originalProc;
    private static IntPtr s_ourProc;
    private static WndProcDelegate s_procDelegate; // GC 방지 — 네이티브가 들고 있는 함수 포인터의 주인
    private static EnumWindowsProc s_enumDelegate;
    private static IntPtr s_current;
    private static bool s_visible = true;
    private static bool s_quitHooked;
    private static IntPtr s_found;

    private static bool Install()
    {
        if (s_ourProc != IntPtr.Zero)
            return true;

        IntPtr hwnd = FindMainWindow();
        if (hwnd == IntPtr.Zero)
            return false;

        s_procDelegate ??= WndProc;
        IntPtr ours = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(s_procDelegate);
        IntPtr original = SetWndProc(hwnd, ours);
        if (original == IntPtr.Zero)
            return false;

        s_hwnd = hwnd;
        s_originalProc = original;
        s_ourProc = ours;

        if (!s_quitHooked)
        {
            s_quitHooked = true;
            Application.quitting += () => Show(IntPtr.Zero);
        }
        return true;
    }

    private static void Uninstall()
    {
        if (s_ourProc == IntPtr.Zero)
            return;

        // 우리 뒤에 다른 서브클래스(Steam 오버레이 등)가 붙었으면 사슬을 끊지 않게 그대로 둔다 —
        // s_current 가 Zero 라 WndProc 는 전부 원래 프로시저로 넘긴다.
        if (GetWndProc(s_hwnd) != s_ourProc)
            return;

        SetWndProc(s_hwnd, s_originalProc);
        s_hwnd = IntPtr.Zero;
        s_originalProc = IntPtr.Zero;
        s_ourProc = IntPtr.Zero;
    }

    [AOT.MonoPInvokeCallback(typeof(WndProcDelegate))]
    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_SETCURSOR
            && (lParam.ToInt64() & 0xFFFF) == HTCLIENT
            && s_current != IntPtr.Zero
            && s_visible)
        {
            SetCursor(s_current);
            return (IntPtr)1;
        }

        IntPtr result = CallWindowProc(s_originalProc, hWnd, msg, wParam, lParam);

        // Unity 가 WM_SETCURSOR 밖(마우스 이동·Raw Input 처리)에서 자기 커서를 다시 세운다 — 처리 직후 되세운다.
        if ((msg == WM_MOUSEMOVE || msg == WM_INPUT || msg == WM_SETCURSOR)
            && ShouldOwnCursor()
            && GetCursor() != s_current)
        {
            s_diagWndProcFixes++;
            SetCursor(s_current);
        }
        return result;
    }

    // 이 프로세스의 Unity 게임 창(클래스 UnityWndClass).
    private static IntPtr FindMainWindow()
    {
        s_found = IntPtr.Zero;
        s_enumDelegate ??= EnumCallback;
        EnumWindows(s_enumDelegate, IntPtr.Zero);
        return s_found;
    }

    [AOT.MonoPInvokeCallback(typeof(EnumWindowsProc))]
    private static bool EnumCallback(IntPtr hwnd, IntPtr lParam)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid != GetCurrentProcessId())
            return true;

        var name = new System.Text.StringBuilder(64);
        GetClassName(hwnd, name, name.Capacity);
        if (name.ToString() != "UnityWndClass")
            return true;

        s_found = hwnd;
        return false; // 찾았으니 중단
    }

    private static IntPtr SetWndProc(IntPtr hwnd, IntPtr proc) =>
        IntPtr.Size == 8
            ? SetWindowLongPtr64(hwnd, GWLP_WNDPROC, proc)
            : (IntPtr)SetWindowLong32(hwnd, GWLP_WNDPROC, proc.ToInt32());

    private static IntPtr GetWndProc(IntPtr hwnd) =>
        IntPtr.Size == 8
            ? GetWindowLongPtr64(hwnd, GWLP_WNDPROC)
            : (IntPtr)GetWindowLong32(hwnd, GWLP_WNDPROC);

    // ── Win32 ─────────────────────────────────────────────────

    private const int GWLP_WNDPROC = -4;
    private const uint WM_SETCURSOR = 0x0020;
    private const long HTCLIENT = 1;
    private const uint BI_BITFIELDS = 3;
    private const uint DIB_RGB_COLORS = 0;
    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_INPUT = 0x00FF;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetCursor();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT point);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct BITMAPV5HEADER
    {
        public uint bV5Size;
        public int bV5Width;
        public int bV5Height;
        public ushort bV5Planes;
        public ushort bV5BitCount;
        public uint bV5Compression;
        public uint bV5SizeImage;
        public int bV5XPelsPerMeter;
        public int bV5YPelsPerMeter;
        public uint bV5ClrUsed;
        public uint bV5ClrImportant;
        public uint bV5RedMask;
        public uint bV5GreenMask;
        public uint bV5BlueMask;
        public uint bV5AlphaMask;
        public uint bV5CSType;
        // CIEXYZTRIPLE bV5Endpoints (9 × FXPT2DOT30)
        public int e0, e1, e2, e3, e4, e5, e6, e7, e8;
        public uint bV5GammaRed;
        public uint bV5GammaGreen;
        public uint bV5GammaBlue;
        public uint bV5Intent;
        public uint bV5ProfileData;
        public uint bV5ProfileSize;
        public uint bV5Reserved;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public int fIcon;
        public uint xHotspot;
        public uint yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPV5HEADER pbmi, uint usage, out IntPtr bits, IntPtr hSection, uint offset);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitCount, byte[] bits);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref ICONINFO iconInfo);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyCursor(IntPtr hCursor);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SetCursor(IntPtr hCursor);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern IntPtr CallWindowProc(IntPtr prevProc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int index, IntPtr newLong);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr hWnd, int index, int newLong);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hWnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetCurrentProcessId();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder className, int maxCount);
#else
    public static bool IsSupported => false;
    public static IntPtr Create(byte[] bgraTopDown, int width, int height, Vector2Int hotspot) => IntPtr.Zero;
    public static void Destroy(IntPtr cursor) { }
    public static bool Show(IntPtr cursor) => false;
    public static void Tick(bool visible) { }
#endif
}
