/// <summary>로비 안내 경로 스텝(할 일 111) — 지금 바닥에 켤 경로 하나. None = 전부 끔.</summary>
public enum LobbyGuideStep : byte
{
    None = 0,
    CharacterSelect = 1,   // 캐릭터 선택 방으로
    Tutorial = 2,          // 훈련장 입구 집결로
    Sortie = 3,            // 출격 집결로
}

/// <summary>스텝 판정 입력 — 전부 로컬 피어가 읽는 값(복제된 진행기 상태 + 내 플레이어 위치 판정).</summary>
public struct LobbyGuideStepInput
{
    /// <summary>스폰된 진행기와 내 플레이어 오브젝트가 둘 다 있다.</summary>
    public bool Ready;
    /// <summary>출격 확정·로딩(메인 진입 시작 뒤).</summary>
    public bool Launching;
    /// <summary>훈련 명단 고정 뒤(L10 중앙 집결 확정 뒤).</summary>
    public bool RosterLocked;
    /// <summary>지금 단계가 훈련장 입구 집결(L20).</summary>
    public bool EntranceOpen;
    /// <summary>지금 출격 집결 구역이 켜져 있다(L40 — 시연 즉시 출격 플래그면 명단 고정 전에도).</summary>
    public bool SortieOpen;
    /// <summary>내가 훈련 명단(L10 확정 때 고정)에 있다.</summary>
    public bool InTrainingRoster;
    /// <summary>내 튜토리얼 클리어 플래그 — Alt+1(할 일 112)로 켜고 끈다.</summary>
    public bool TrainingCleared;
    /// <summary>출격 집결 카운트다운 중(캐릭터 변경 잠금 입력).</summary>
    public bool SortieCountdownActive;
    /// <summary>이 세션에서 캐릭터 선택 상호작용 반경에 한 번이라도 들어갔다(래치).</summary>
    public bool CharacterStepDone;
    /// <summary>내 플레이어가 훈련장 입구 집결 구역 박스 안이다.</summary>
    public bool InEntranceZone;
    /// <summary>내 플레이어가 출격 집결 구역 박스 안이다.</summary>
    public bool InSortieZone;
}

/// <summary>
/// 로비 안내 경로 스텝 판정(할 일 111) — NGO·MonoBehaviour 의존 없는 순수 함수(EditMode 테스트 대상).
/// 위에서부터 처음 맞는 줄. 할 일 157 ②에서 단계 입력을 단계표 기준(명단 고정·입구/출격 집결 활성)으로 옮겼다 — 158 이 단계별 PathId 경로로 대체할 때까지의 과도기.
/// <list type="number">
/// <item>준비 안 됨(진행기·내 플레이어 없음) · 출격 확정 · 출격 구역 안 → None.</item>
/// <item>명단으로 훈련 중(명단 고정 뒤, 입구·출격 집결이 아닌 단계) → None — 훈련 안 집결 구역이 안내한다.</item>
/// <item>명단 밖 + 캐릭터 스텝 안 끝남 + 지금 캐릭터를 바꿀 수 있음(<see cref="LobbyTutorialRules.CharacterChangeBlock"/> = None) → CharacterSelect.
/// (157 ③에서 훈련 명단 캐릭터 잠금은 없앴지만 명단은 L00 라커 방문을 이미 마쳤으므로 이 경로를 다시 켜지 않는다.)</item>
/// <item>명단으로 입구 집결(L20) 중 → 입구 구역 안이면 None(집결 대기), 아니면 Tutorial.</item>
/// <item>명단 고정 전(L00·L10) + 클리어 플래그 꺼짐 → None(라커·중앙 경로는 158).</item>
/// <item>나머지(클리어 플래그, 늦은 참가자, 출격 집결) → Sortie.</item>
/// </list>
/// </summary>
public static class LobbyGuideStepRules
{
    public static LobbyGuideStep Evaluate(in LobbyGuideStepInput input)
    {
        if (!input.Ready || input.Launching || input.InSortieZone)
            return LobbyGuideStep.None;

        var training = input.InTrainingRoster && input.RosterLocked;
        if (training && !input.SortieOpen && !input.EntranceOpen)
            return LobbyGuideStep.None;

        if (!input.CharacterStepDone && !training && LobbyTutorialRules.CharacterChangeBlock(
                input.Launching, input.SortieCountdownActive) == LobbyCharacterChangeReject.None)
            return LobbyGuideStep.CharacterSelect;

        if (training && input.EntranceOpen)
            return input.InEntranceZone ? LobbyGuideStep.None : LobbyGuideStep.Tutorial;

        if (!input.RosterLocked && !input.TrainingCleared && !input.SortieOpen)
            return LobbyGuideStep.None;

        return LobbyGuideStep.Sortie;
    }
}
