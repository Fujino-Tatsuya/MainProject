/// <summary>
/// [서버] TRAIN_03 진행기 ↔ 훈련 상대 연결 규칙(할 일 86 T5c, PLAN-86 §3.5, 명세 v0.2 §9). NGO·MonoBehaviour 의존 없는 순수 클래스(EditMode 테스트 대상).
/// 실행은 <see cref="LobbyTrainingSparring"/> — 여기는 "언제 한 번만 하나"만 정한다.
/// <list type="bullet">
/// <item>회차: 진행기가 Train03A 진행(집결 확정) ~ D 에 있는 동안 상대 1개(<see cref="BotPresent"/>). 회차 시작(<see cref="BeginRound"/>)마다 기록을 비운다 — 판 끝 로비 복귀·Train03 이탈이 회차 끝.</item>
/// <item>단계 알림: 진행기 Train03A~D 가 진행 중이 되면 상대에게 1회(<see cref="TryTakeFlowNotice"/>). 앞으로만 — 같은 단계 재알림·역순은 없음.</item>
/// <item>완료 보고: 상대 모델의 단계 완료를 진행기에 1회(<see cref="TryAcceptReport"/>). 진행기의 지금 단계(진행 중)와 같을 때만 — 어긋남·중복·역순은 무시.</item>
/// <item>B→C 초기화(플레이어 쪽 <see cref="TrainingResetOptions.PracticeReset"/>): 회차당 1회(<see cref="TryConsumePracticeReset"/>).</item>
/// <item>무전 연결 이벤트(Work1 T6 이 구독): A 첫 간파 성공(TRAIN_03_COUNTER)·C 첫 유효 벽 충돌(TRAIN_03_WALL) — 각각 회차당 1회.</item>
/// </list>
/// </summary>
public sealed class LobbySparringLink
{
    LobbyTrainingStage _notified = LobbyTrainingStage.Lobby;
    LobbyTrainingStage _reported = LobbyTrainingStage.Lobby;
    bool _practiceResetDone;
    bool _counterRaised;
    bool _wallRaised;

    /// <summary>회차 진행 중(상대를 스폰한 뒤 ~ 정리 전).</summary>
    public bool IsRoundActive { get; private set; }

    /// <summary>이번 판(진행기 수명)에서 시작한 회차 수 — 로그용.</summary>
    public int Round { get; private set; }

    /// <summary>마지막으로 진행기에 보고한 상대 단계 완료(없으면 Lobby).</summary>
    public LobbyTrainingStage LastReported => _reported;

    /// <summary>TRAIN_03 단계(Train03A~D)인가.</summary>
    public static bool IsSparringStage(LobbyTrainingStage stage) =>
        stage >= LobbyTrainingStage.Train03A && stage <= LobbyTrainingStage.Train03D;

    /// <summary>
    /// 훈련 상대가 있어야 하는가 — Train03A 진행(시작 집결 확정) 순간부터 Train03D 까지(사용자 10-22: 집결 대기 중엔 없다).
    /// Train03A 시작 집결 대기(Gathering)·그 밖(TrainEnd·브리핑·출격·로비)은 없음. A→B·B→C 영역 확인 대기(Completed)는 그대로 둔다.
    /// </summary>
    public static bool BotPresent(LobbyTrainingStage stage, LobbyTrainingPhase phase) =>
        IsSparringStage(stage) && !(stage == LobbyTrainingStage.Train03A && phase == LobbyTrainingPhase.Gathering);

    /// <summary>
    /// [전 피어] 지정 벽 표시(주황 띠, 임시 비주얼)를 켜는가 — 벽 목표가 있는 Train03C·D 동안(하위 상태 무관). A·B·TrainEnd 이후·로비·진행기 없음(Lobby 로 읽힘)은 끈다.
    /// 클라는 복제 단계(<see cref="LobbyTrainingDirector.Stage"/>)로 판정 — 늦게 붙은 클라도 같다.
    /// </summary>
    public static bool WallMarkerVisible(LobbyTrainingStage stage) =>
        stage == LobbyTrainingStage.Train03C || stage == LobbyTrainingStage.Train03D;

    /// <summary>새 회차 — 기록을 비운다. 이미 진행 중이면 false(그대로 — 상대 재사용).</summary>
    public bool BeginRound()
    {
        if (IsRoundActive)
            return false;

        IsRoundActive = true;
        Round++;
        _notified = LobbyTrainingStage.Lobby;
        _reported = LobbyTrainingStage.Lobby;
        _practiceResetDone = false;
        _counterRaised = false;
        _wallRaised = false;
        return true;
    }

    /// <summary>회차 끝(Train03 이탈·진행기 디스폰). 진행 중이 아니었으면 false.</summary>
    public bool EndRound()
    {
        if (!IsRoundActive)
            return false;

        IsRoundActive = false;
        return true;
    }

    /// <summary>
    /// 진행기 단계를 상대에게 알릴 차례인가 — 회차 중이고 Train03A~D 가 진행 중이며 지난 알림보다 뒤일 때 1회(<paramref name="notice"/> = 그 단계).
    /// 집결 대기·완료(영역 확인 대기)·같은 단계 재평가·역순은 false.
    /// </summary>
    public bool TryTakeFlowNotice(LobbyTrainingStage stage, LobbyTrainingPhase phase, out LobbyTrainingStage notice)
    {
        notice = default;
        if (!IsRoundActive || !IsSparringStage(stage) || phase != LobbyTrainingPhase.InProgress || stage <= _notified)
            return false;

        _notified = stage;
        notice = stage;
        return true;
    }

    /// <summary>
    /// 상대 모델 단계 완료(<paramref name="completed"/>)를 진행기에 보고할 것인가 — 회차 중, Train03A~D, 지난 보고보다 뒤,
    /// 그리고 진행기가 지금 그 단계를 진행 중일 때만 true(보고한 것으로 기록). 어긋남·중복·역순은 false(호출측 경고).
    /// </summary>
    public bool TryAcceptReport(LobbyTrainingStage completed, LobbyTrainingStage flowStage, LobbyTrainingPhase flowPhase)
    {
        if (!IsRoundActive || !IsSparringStage(completed) || completed <= _reported)
            return false;
        if (completed != flowStage || flowPhase != LobbyTrainingPhase.InProgress)
            return false;

        _reported = completed;
        return true;
    }

    /// <summary>B→C 초기화의 플레이어 쪽 리셋 — 회차당 처음 한 번만 true.</summary>
    public bool TryConsumePracticeReset()
    {
        if (!IsRoundActive || _practiceResetDone)
            return false;

        _practiceResetDone = true;
        return true;
    }

    /// <summary>
    /// 첫 간파 성공 이벤트(TRAIN_03_COUNTER — 명세 §14 "A 첫 간파 성공")를 낼 차례인가. 상대 단계가 A(<see cref="TrainingSparringStage.CounterIntro"/>)일 때의 성공만, 회차당 1회.
    /// </summary>
    public bool TryRaiseFirstCounter(TrainingSparringStage sparringStage)
    {
        if (!IsRoundActive || _counterRaised || sparringStage != TrainingSparringStage.CounterIntro)
            return false;

        _counterRaised = true;
        return true;
    }

    /// <summary>
    /// 첫 유효 벽 충돌 이벤트(TRAIN_03_WALL — "C 첫 유효 벽 충돌")를 낼 차례인가. 상대 모델이 C 벽 목표를 기록했으면(<see cref="TrainingSparringModel.HasRecordedWall"/> —
    /// 칸 감소를 동반한 지정 벽 충돌) 회차당 1회. A/B 의 벽·0칸 접촉·장식 벽은 모델이 기록하지 않으므로 여기 오지 않는다.
    /// </summary>
    public bool TryRaiseFirstWall(bool wallRecorded)
    {
        if (!IsRoundActive || _wallRaised || !wallRecorded)
            return false;

        _wallRaised = true;
        return true;
    }
}
