using System;
using System.Collections.Generic;

/// <summary>
/// 로컬 UI 모달들이 공유하는 Player 입력 차단 상태를 관리한다.
/// 각 모달은 고유 토큰을 획득/해제하며, 마지막 토큰이 해제될 때만 입력이 복원된다.
/// </summary>
public static class UiInputGateManager
{
    private static readonly HashSet<object> ActiveBlockReasons = new();

    public static bool IsInputBlocked => ActiveBlockReasons.Count > 0;

    public static event Action<bool> BlockedChanged;

    public static void Acquire(object token)
    {
        if (token == null)
            return;

        bool wasBlocked = IsInputBlocked;
        if (!ActiveBlockReasons.Add(token))
            return;

        if (!wasBlocked)
            BlockedChanged?.Invoke(true);
    }

    public static void Release(object token)
    {
        if (token == null || !ActiveBlockReasons.Remove(token))
            return;

        if (!IsInputBlocked)
            BlockedChanged?.Invoke(false);
    }
}
