# 부스 대기 영상 단축키 (289) — Alt+\ 를 누르면 팟플레이어 화면 보호기(지정 영상)를 띄운다. 입력이 오면 화면 보호기가 스스로 꺼진다.
# 윈도우 바로가기 단축키는 전체 화면 게임이 앞에 있으면 전달되지 않아서, RegisterHotKey 로 직접 받는다.
# 실행: Start-BoothVideoHotkey.bat (창 없이 상주). 끄기: 작업 관리자에서 powershell(BoothVideoHotkey) 종료.
param([string]$Scr = 'C:\Program Files\DAUM\PotPlayer\PotScreenSaver64.scr')

$mutex = New-Object System.Threading.Mutex($false, 'Local\BoothVideoHotkey289')
if (-not $mutex.WaitOne(0)) { exit }   # 이미 실행 중

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class BoothHotkey {
    [StructLayout(LayoutKind.Sequential)] public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int x; public int y; }
    [DllImport("user32.dll", SetLastError=true)] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mod, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] public static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000, VK_OEM_5 = 0xDC, VK_OEM_102 = 0xE2, VK_V = 0x56, WM_HOTKEY = 0x312;
}
'@

$log = Join-Path $PSScriptRoot 'BoothVideoHotkey.log'
function Log($m) { "$(Get-Date -f 'yyyy-MM-dd HH:mm:ss') $m" | Out-File -Append -Encoding unicode $log }

$keys = @(
    @{ id = 1; name = "Alt+\(OEM_5)"; mod = [BoothHotkey]::MOD_ALT; vk = [BoothHotkey]::VK_OEM_5 },
    @{ id = 2; name = "Alt+\(OEM_102)"; mod = [BoothHotkey]::MOD_ALT; vk = [BoothHotkey]::VK_OEM_102 },
    @{ id = 3; name = "Ctrl+Alt+V"; mod = [BoothHotkey]::MOD_ALT -bor [BoothHotkey]::MOD_CONTROL; vk = [BoothHotkey]::VK_V }
)
$ok = 0
foreach ($k in $keys) {
    if ([BoothHotkey]::RegisterHotKey([IntPtr]::Zero, $k.id, $k.mod -bor [BoothHotkey]::MOD_NOREPEAT, $k.vk)) { $ok++; Log "등록 $($k.name)" }
    else { Log "등록 실패 $($k.name) err=$([Runtime.InteropServices.Marshal]::GetLastWin32Error())" }
}
if ($ok -eq 0) { exit 1 }
Log "시작 — Alt+\ 대기 ($Scr)"
try {
    $msg = New-Object BoothHotkey+MSG
    while ([BoothHotkey]::GetMessage([ref]$msg, [IntPtr]::Zero, 0, 0) -gt 0) {
        if ($msg.message -ne [BoothHotkey]::WM_HOTKEY) { continue }
        if (Get-Process -Name 'PotScreenSaver64.scr','PotScreenSaver64' -ErrorAction SilentlyContinue) { continue }   # 이미 재생 중
        try { Log "단축키 id=$($msg.wParam)"
            # 단축키를 뗄 때의 키 입력(Alt· 떼기)이 화면 보호기를 바로 끄지 않게, 모든 키를 뗀 뒤 잠깐 기다렸다 띄운다.
            $t0 = [DateTime]::Now
            while (([DateTime]::Now - $t0).TotalSeconds -lt 3 -and (([BoothHotkey]::GetAsyncKeyState(0x12) -band 0x8000) -or ([BoothHotkey]::GetAsyncKeyState(0x11) -band 0x8000) -or ([BoothHotkey]::GetAsyncKeyState(0xDC) -band 0x8000) -or ([BoothHotkey]::GetAsyncKeyState(0xE2) -band 0x8000) -or ([BoothHotkey]::GetAsyncKeyState(0x56) -band 0x8000))) { Start-Sleep -Milliseconds 30 }
            Start-Sleep -Milliseconds 400
            Start-Process -FilePath $Scr -ArgumentList '/s' -WorkingDirectory (Split-Path $Scr); Log "재생" } catch { Log "실행 실패: $_" }
    }
} finally { foreach ($k in $keys) { [void][BoothHotkey]::UnregisterHotKey([IntPtr]::Zero, $k.id) }; $mutex.ReleaseMutex() }
