/// <summary>로비 결과 패널이 닫힌 이유.</summary>
public enum LobbyResultCloseReason
{
    Api = 0,       // 호출 측 Hide()
    Button = 1,    // 닫기 버튼
    Escape = 2,    // ESC
    AutoClose = 3  // 열린 뒤 자동 닫힘 시간 경과
}

/// <summary>
/// 로비 결과 패널의 열고 닫기 판단(MonoBehaviour 밖 — EditMode 테스트 대상).
/// 시간은 호출 측이 넘긴다(패널은 <c>Time.unscaledTime</c> — 슬로모션 영향 없음).
/// 결과 데이터는 모른다 — "결과가 있는가"만 인자로 받는다. 결과 비우기는 호출 측 책임.
/// </summary>
public sealed class LobbyResultPanelRules
{
    /// <summary>기본 자동 닫힘 시간(초). 0 이하면 자동으로 닫지 않는다.</summary>
    public const float DefaultAutoCloseSeconds = 180f;

    public LobbyResultPanelRules(float autoCloseSeconds = DefaultAutoCloseSeconds)
    {
        AutoCloseSeconds = autoCloseSeconds;
    }

    public float AutoCloseSeconds { get; }

    public bool IsOpen { get; private set; }

    /// <summary>마지막으로 연 시각. 닫혀 있으면 의미 없다.</summary>
    public float OpenedAt { get; private set; }

    public bool AutoCloseEnabled => AutoCloseSeconds > 0f;

    /// <summary>자동으로 닫힐 시각. 자동 닫힘이 꺼져 있으면 <see cref="float.PositiveInfinity"/>.</summary>
    public float AutoCloseAt => AutoCloseEnabled ? OpenedAt + AutoCloseSeconds : float.PositiveInfinity;

    public static bool CanOpen(bool hasResult) => hasResult;

    /// <summary>
    /// 연다. 결과가 없으면 아무것도 안 하고 false. 이미 열려 있으면 자동 닫힘 타이머만 처음부터 다시 잰다.
    /// 닫은 뒤 다시 열 수 있다(결과가 남아 있는 한).
    /// </summary>
    public bool TryOpen(bool hasResult, float now)
    {
        if (!CanOpen(hasResult))
            return false;

        IsOpen = true;
        OpenedAt = now;
        return true;
    }

    /// <summary>닫는다. 열려 있었으면 true(닫힘 이벤트를 낼지 판단용).</summary>
    public bool Close()
    {
        if (!IsOpen)
            return false;

        IsOpen = false;
        return true;
    }

    /// <summary>ESC 입력. 열려 있으면 닫고 true — 이 프레임 ESC 를 패널이 썼다는 뜻.</summary>
    public bool HandleEscape() => Close();

    /// <summary>닫기 버튼. 열려 있으면 닫고 true.</summary>
    public bool HandleCloseButton() => Close();

    public bool ShouldAutoClose(float now) => IsOpen && AutoCloseEnabled && now >= AutoCloseAt;

    /// <summary>매 프레임. 자동 닫힘 시각이 지났으면 닫고 true.</summary>
    public bool Tick(float now) => ShouldAutoClose(now) && Close();
}
