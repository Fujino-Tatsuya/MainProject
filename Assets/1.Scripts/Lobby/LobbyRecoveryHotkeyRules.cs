/// <summary>로비 예외 상황 단축키 동작(할 일 112) — RPC 로 보내므로 byte. 새 동작 = 여기 한 값 + <see cref="LobbyRecoveryHotkeyRules.Table"/> 한 줄.</summary>
public enum LobbyRecoveryAction : byte
{
    None = 0,
    ToggleTrainingClear = 1,   // 튜토리얼 클리어 플래그 켜기/끄기 — 누른 사람만
    Unstuck = 2,               // 멈춤·갇힘 탈출 — 누른 사람만
    ReloadLobby = 3,           // 로비 씬 다시 불러오기 — 호스트만
}

/// <summary>서버가 요청한 사람에게 돌려주는 결과(토스트 문구 = <see cref="LobbyRecoveryHotkeyRules.Describe"/>).</summary>
public enum LobbyRecoveryResult : byte
{
    TrainingClearOn = 0,
    TrainingClearOff = 1,
    TrainingClearSkippedToSortie = 2,   // 켰더니 훈련 명단이 비어 출격 집결로 넘어갔다
    Unstuck = 3,
    Reloading = 4,
    HostOnly = 5,
    BriefingLocked = 6,
    NoPlayer = 7,
    SceneTransition = 8,                // 출격(메인 진입)·씬 전환 중
    Failed = 9,
}

/// <summary>단축키 표 한 줄 — Alt+<see cref="Digit"/> → <see cref="Action"/>.</summary>
public readonly struct LobbyRecoveryHotkey
{
    public LobbyRecoveryHotkey(int digit, LobbyRecoveryAction action, bool hostOnly, string label)
    {
        Digit = digit;
        Action = action;
        HostOnly = hostOnly;
        Label = label;
    }

    public int Digit { get; }
    public LobbyRecoveryAction Action { get; }

    /// <summary>호스트만 — 클라가 누르면 보내지 않고 '호스트만' 토스트(서버도 다시 거부한다).</summary>
    public bool HostOnly { get; }

    public string Label { get; }
}

/// <summary>
/// 로비(3.LobbyScene) 예외 상황 단축키 Alt+숫자 규칙(할 일 112) — 키 표·판정. Unity·NGO 비의존(EditMode 테스트 대상).
/// 입력·토스트는 <c>LobbyRecoveryHotkeys</c>, 실행은 <c>LobbyTrainingDirector.Recovery</c>(둘 다 DEV_HOTKEYS 게이트). 원본 Docs/tech/debug-hotkeys.md §3.
/// </summary>
public static class LobbyRecoveryHotkeyRules
{
    /// <summary>키 표 — 키 추가 = 한 줄 추가(숫자는 0~9, 겹치지 않게).</summary>
    public static readonly LobbyRecoveryHotkey[] Table =
    {
        new LobbyRecoveryHotkey(1, LobbyRecoveryAction.ToggleTrainingClear, false, "튜토리얼 클리어 켜기/끄기"),
        new LobbyRecoveryHotkey(2, LobbyRecoveryAction.Unstuck, false, "멈춤 탈출"),
        new LobbyRecoveryHotkey(3, LobbyRecoveryAction.ReloadLobby, true, "로비 다시 불러오기(호스트)"),
    };

    public static bool TryFromDigit(int digit, out LobbyRecoveryHotkey hotkey)
    {
        foreach (var row in Table)
        {
            if (row.Digit != digit)
                continue;

            hotkey = row;
            return true;
        }

        hotkey = default;
        return false;
    }

    public static bool TryGet(LobbyRecoveryAction action, out LobbyRecoveryHotkey hotkey)
    {
        foreach (var row in Table)
        {
            if (row.Action != action)
                continue;

            hotkey = row;
            return true;
        }

        hotkey = default;
        return false;
    }

    /// <summary>이 피어가 보낼 수 있는가(표에 있고, 호스트 전용이면 호스트).</summary>
    public static bool CanRequest(LobbyRecoveryAction action, bool isHost) =>
        TryGet(action, out var hotkey) && (!hotkey.HostOnly || isHost);

    /// <summary>멈춤 탈출 판정 — 플레이어 없음 → 브리핑 연출 잠금 중(풀지 않는다) → 출격·씬 전환 중 → 탈출.</summary>
    public static LobbyRecoveryResult UnstuckVerdict(bool hasPlayer, bool briefingRunning, bool launchedOrTransition)
    {
        if (!hasPlayer)
            return LobbyRecoveryResult.NoPlayer;
        if (briefingRunning)
            return LobbyRecoveryResult.BriefingLocked;
        if (launchedOrTransition)
            return LobbyRecoveryResult.SceneTransition;
        return LobbyRecoveryResult.Unstuck;
    }

    /// <summary>로비 리로드 판정 — 호스트만, 출격(메인 진입)·씬 전환 중이면 거부.</summary>
    public static LobbyRecoveryResult ReloadVerdict(bool isHost, bool launchedOrTransition)
    {
        if (!isHost)
            return LobbyRecoveryResult.HostOnly;
        if (launchedOrTransition)
            return LobbyRecoveryResult.SceneTransition;
        return LobbyRecoveryResult.Reloading;
    }

    /// <summary>토스트 문구.</summary>
    public static string Describe(LobbyRecoveryAction action, LobbyRecoveryResult result)
    {
        var digit = TryGet(action, out var hotkey) ? hotkey.Digit.ToString() : "?";
        var prefix = $"Alt+{digit}: ";
        switch (result)
        {
            case LobbyRecoveryResult.TrainingClearOn: return prefix + "튜토리얼 클리어 켜짐 — 훈련에서 빠지고 출격 집결에만 참여";
            case LobbyRecoveryResult.TrainingClearOff: return prefix + "튜토리얼 클리어 꺼짐 — 다음 회차부터 훈련 명단에 포함";
            case LobbyRecoveryResult.TrainingClearSkippedToSortie: return prefix + "튜토리얼 클리어 켜짐 — 훈련 인원이 없어 출격 집결로 넘어감";
            case LobbyRecoveryResult.Unstuck: return prefix + "멈춤 탈출 — 안전 지점으로 이동";
            case LobbyRecoveryResult.Reloading: return prefix + "로비 다시 불러오는 중…";
            case LobbyRecoveryResult.HostOnly: return prefix + "호스트만 쓸 수 있다";
            case LobbyRecoveryResult.BriefingLocked: return prefix + "브리핑 중에는 풀 수 없다 — 브리핑이 끝나면 다시 눌러라";
            case LobbyRecoveryResult.NoPlayer: return prefix + "플레이어가 아직 없다";
            case LobbyRecoveryResult.SceneTransition: return prefix + "출격·씬 전환 중이라 무시";
            default: return prefix + "실패 — 로그 확인";
        }
    }
}
