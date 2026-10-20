using System;
using System.Collections.Generic;

/// <summary>
/// 걷는 로비 문구 키(UI.Lobby.*) — PLAN-48 C10. Strings 시트 행은 아직 없다(ko 가 화면 폴백). <see cref="UiStrings.All"/> 이 모은다.
/// 상호작용 프롬프트 문구는 InteractPrompt.prefab 의 F 아이콘 옆 글자라 키 이름(F)은 넣지 않는다.
/// </summary>
public static class LobbyUiStrings
{
    private const string Where = "3.LobbyScene";

    public static readonly UiString InteractReady = new UiString("UI.Lobby.InteractReady", "준비", Where + " › 준비 오브젝트 프롬프트 — 준비 안 됨(코드)");
    public static readonly UiString InteractUnready = new UiString("UI.Lobby.InteractUnready", "준비 해제", Where + " › 준비 오브젝트 프롬프트 — 준비됨(코드)");
    public static readonly UiString InteractCharacterSelect = new UiString("UI.Lobby.InteractCharacterSelect", "캐릭터 선택", Where + " › 캐릭터 선택 오브젝트 프롬프트(코드, C12)");
    public static readonly UiString ReadyCount = new UiString("UI.Lobby.ReadyCount", "준비 {ready}/{total}", Where + " › 준비 오브젝트 머리 위({ready} = 준비한 사람, {total} = 접속자)");
    public static readonly UiString Countdown = new UiString("UI.Lobby.Countdown", "{time} 후 출발", Where + " › Slot_Countdown({time} = UI.Unit.Seconds 로 만든 남은 초, 올림 정수)");
    public static readonly UiString CountdownStarting = new UiString("UI.Lobby.CountdownStarting", "출발!", Where + " › Slot_Countdown — 0초(시작 래치 뒤 로딩 전까지)");

    public static readonly IReadOnlyList<UiString> All = new[]
    {
        InteractReady, InteractUnready, InteractCharacterSelect, ReadyCount, Countdown, CountdownStarting,
    };

    /// <summary>"준비 n/m".</summary>
    public static string FormatReadyCount(int ready, int total) =>
        ReadyCount.Format(new Dictionary<string, string> { ["ready"] = ready.ToString(), ["total"] = total.ToString() });

    /// <summary>남은 초 → 표시 정수(올림). 4.2초 = 5, 0초 = 0. 모든 피어가 같은 서버 시각으로 계산하므로 같은 숫자가 나온다.</summary>
    public static int CountdownDisplaySeconds(double remainingSeconds) =>
        remainingSeconds > 0d ? (int)Math.Ceiling(remainingSeconds) : 0;

    /// <summary>카운트다운 문구. 0초면 <see cref="CountdownStarting"/>.</summary>
    public static string FormatCountdown(double remainingSeconds)
    {
        var seconds = CountdownDisplaySeconds(remainingSeconds);
        return seconds > 0
            ? Countdown.Format(new Dictionary<string, string> { ["time"] = UiStrings.Seconds(seconds, "0") })
            : CountdownStarting.Text;
    }
}
