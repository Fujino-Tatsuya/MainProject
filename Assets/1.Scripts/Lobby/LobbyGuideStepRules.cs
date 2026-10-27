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
    public LobbyTrainingStage Stage;
    public LobbyTrainingPhase Phase;
    /// <summary>내가 훈련 명단(입구 확정 때 고정)에 있다.</summary>
    public bool InTrainingRoster;
    /// <summary>내 튜토리얼 클리어 플래그(할 일 112 Alt+1).</summary>
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
/// 위에서부터 처음 맞는 줄. 판정 표 = Docs/tech/lobby-training.md "안내 스텝".
/// <list type="number">
/// <item>준비 안 됨(진행기·내 플레이어 없음) · 출격 단계 · 출격 구역 안 → None.</item>
/// <item>훈련 명단으로 훈련 중(입구 확정 ~ 브리핑 완료 전) → None — 훈련 안 집결 구역이 안내한다.</item>
/// <item>캐릭터 스텝 안 끝남 + 지금 캐릭터를 바꿀 수 있음(<see cref="LobbyTrainingFlow.CharacterChangeBlock"/> = None) → CharacterSelect.</item>
/// <item>Lobby 단계 + 클리어 플래그 꺼짐 → 입구 구역 안이면 None(집결 대기), 아니면 Tutorial.</item>
/// <item>나머지(클리어 플래그·늦은 참가자·브리핑 완료) → Sortie.</item>
/// </list>
/// </summary>
public static class LobbyGuideStepRules
{
    public static LobbyGuideStep Evaluate(in LobbyGuideStepInput input)
    {
        if (!input.Ready || input.Stage == LobbyTrainingStage.Sortie || input.InSortieZone)
            return LobbyGuideStep.None;

        var sortieOpen = input.Stage == LobbyTrainingStage.Brief && input.Phase == LobbyTrainingPhase.Completed;
        if (input.InTrainingRoster && input.Stage != LobbyTrainingStage.Lobby && !sortieOpen)
            return LobbyGuideStep.None;

        if (!input.CharacterStepDone && LobbyTrainingFlow.CharacterChangeBlock(
                input.Stage, input.InTrainingRoster, input.SortieCountdownActive) == LobbyCharacterChangeReject.None)
            return LobbyGuideStep.CharacterSelect;

        if (input.Stage == LobbyTrainingStage.Lobby && !input.TrainingCleared)
            return input.InEntranceZone ? LobbyGuideStep.None : LobbyGuideStep.Tutorial;

        return LobbyGuideStep.Sortie;
    }
}
