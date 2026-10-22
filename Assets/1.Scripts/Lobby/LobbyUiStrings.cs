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
    public static readonly UiString GatherCount = new UiString("UI.Lobby.GatherCount", "집결 {gathered}/{total}", Where + " › 집결 구역 표시({gathered} = 구역 안 + 쓰러짐·사망 아님, {total} = 참여자, 할 일 86)");
    public static readonly UiString GatherCountdown = new UiString("UI.Lobby.GatherCountdown", "{time} 후 이동", Where + " › 집결 카운트다운({time} = UI.Unit.Seconds 로 만든 남은 초, 올림 정수, 할 일 86)");

    public static readonly UiString Train01Title = new UiString("UI.Lobby.Train01Title", "무기 점검", Where + " › TRAIN_01 개인 체크 제목(할 일 86 T3)");
    public static readonly UiString Train01BasicAttack = new UiString("UI.Lobby.Train01BasicAttack", "기본 공격", Where + " › TRAIN_01 체크 항목 — 기본 공격(할 일 86 T3)");
    public static readonly UiString Train01SkillQ = new UiString("UI.Lobby.Train01SkillQ", "Q", Where + " › TRAIN_01 체크 항목 — Q 스킬(할 일 86 T3)");
    public static readonly UiString Train01SkillE = new UiString("UI.Lobby.Train01SkillE", "E", Where + " › TRAIN_01 체크 항목 — E 스킬(할 일 86 T3)");
    public static readonly UiString Train01SkillR = new UiString("UI.Lobby.Train01SkillR", "R", Where + " › TRAIN_01 체크 항목 — R 스킬(할 일 86 T3)");
    public static readonly UiString Train01RightClick = new UiString("UI.Lobby.Train01RightClick", "우클릭 스킬", Where + " › TRAIN_01 체크 항목 — 우클릭 스킬(명세 '간파' 대신, 할 일 86 T3)");
    public static readonly UiString Train01Dash = new UiString("UI.Lobby.Train01Dash", "대시", Where + " › TRAIN_01 체크 항목 — 대시(할 일 86 T3)");
    public static readonly UiString Train01PartyDone = new UiString("UI.Lobby.Train01PartyDone", "완료 {done}/{total}", Where + " › TRAIN_01 파티 완료 인원({done} = 6항목 모두 마친 사람, {total} = 훈련 명단, 할 일 86 T3)");

    public static readonly UiString Train02Title = new UiString("UI.Lobby.Train02Title", "일반 전투", Where + " › TRAIN_02 제목(할 일 86 T4)");
    public static readonly UiString Train02Remaining = new UiString("UI.Lobby.Train02Remaining", "남은 상대 {remaining}/{total}", Where + " › TRAIN_02 남은 상대 수({remaining} = 아직 처치 안 한 상대, {total} = 이번 전투 상대 수 — 끼임 복구는 처치 아님, 할 일 86 T4)");

    public static readonly UiString Train03Segments = new UiString("UI.Lobby.Train03Segments", "칸 {current}/{total}", Where + " › TRAIN_03 훈련 상대 머리 위 바 이름 자리 — C·D 간파 게이지 칸({current} = 남은 칸, {total} = 전투 데이터 총 칸, 할 일 86 T5b 임시)");
    public static readonly UiString Train03SuppressAttack = new UiString("UI.Lobby.Train03SuppressAttack", "제압된 상대를 공격하세요 {time}", Where + " › TRAIN_03 D 제압 중 훈련 상대 머리 위 바 이름 자리({time} = UI.Unit.Seconds 로 만든 남은 제압 초, 올림 정수, 할 일 86 T5b 임시)");

    public static readonly IReadOnlyList<UiString> All = new[]
    {
        InteractReady, InteractUnready, InteractCharacterSelect, ReadyCount, Countdown, CountdownStarting,
        GatherCount, GatherCountdown,
        Train01Title, Train01BasicAttack, Train01SkillQ, Train01SkillE, Train01SkillR, Train01RightClick, Train01Dash, Train01PartyDone,
        Train02Title, Train02Remaining,
        Train03Segments, Train03SuppressAttack,
    };

    /// <summary>"남은 상대 n/m".</summary>
    public static string FormatTrain02Remaining(int remaining, int total) =>
        Train02Remaining.Format(new Dictionary<string, string> { ["remaining"] = remaining.ToString(), ["total"] = total.ToString() });

    /// <summary>TRAIN_01 체크 항목 이름.</summary>
    public static UiString Train01Item(LobbyWeaponCheckItem item)
    {
        switch (item)
        {
            case LobbyWeaponCheckItem.BasicAttack: return Train01BasicAttack;
            case LobbyWeaponCheckItem.SkillQ: return Train01SkillQ;
            case LobbyWeaponCheckItem.SkillE: return Train01SkillE;
            case LobbyWeaponCheckItem.SkillR: return Train01SkillR;
            case LobbyWeaponCheckItem.RightClick: return Train01RightClick;
            default: return Train01Dash;
        }
    }

    /// <summary>"완료 n/m".</summary>
    public static string FormatTrain01PartyDone(int done, int total) =>
        Train01PartyDone.Format(new Dictionary<string, string> { ["done"] = done.ToString(), ["total"] = total.ToString() });

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

    /// <summary>"집결 n/m".</summary>
    public static string FormatGatherCount(int gathered, int total) =>
        GatherCount.Format(new Dictionary<string, string> { ["gathered"] = gathered.ToString(), ["total"] = total.ToString() });

    /// <summary>집결 카운트다운 문구. 0초면 빈 문자열(확정 순간 구역 표시가 꺼진다).</summary>
    public static string FormatGatherCountdown(double remainingSeconds)
    {
        var seconds = CountdownDisplaySeconds(remainingSeconds);
        return seconds > 0
            ? GatherCountdown.Format(new Dictionary<string, string> { ["time"] = UiStrings.Seconds(seconds, "0") })
            : string.Empty;
    }

    /// <summary>"칸 n/m".</summary>
    public static string FormatTrain03Segments(int current, int total) =>
        Train03Segments.Format(new Dictionary<string, string> { ["current"] = current.ToString(), ["total"] = total.ToString() });

    /// <summary>D 제압 안내 + 남은 초(올림 정수). 0초면 안내만(제압 종료 직전 한 프레임).</summary>
    public static string FormatTrain03SuppressAttack(double remainingSeconds)
    {
        var seconds = CountdownDisplaySeconds(remainingSeconds);
        return Train03SuppressAttack.Format(new Dictionary<string, string> { ["time"] = seconds > 0 ? UiStrings.Seconds(seconds, "0") : string.Empty }).TrimEnd();
    }
}
