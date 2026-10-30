/// <summary>
/// [서버] TRAIN_03 진행기 ↔ 훈련 상대 연결 규칙(할 일 86 T5c, PLAN-86 §3.5, 명세 v0.2 §9). NGO·MonoBehaviour 의존 없는 순수 클래스(EditMode 테스트 대상).
/// 실행은 <see cref="LobbyTrainingSparring"/> — 여기는 "언제 한 번만 하나"만 정한다.
/// 할 일 157 ②: 진행기 단계 = 단계표 Activity(T31 Counter ~ T40 SuppressKill). 옛 Train03A~D 와 1:1 이름만 옮겼다(Train03C 진입 = T33 ResetPosition).
/// <list type="bullet">
/// <item>회차: 진행기가 T31~T40 에 있는 동안 상대 1개(<see cref="BotPresent"/>). 회차 시작(<see cref="BeginRound"/>)마다 기록을 비운다 — 판 끝 로비 복귀·TRAIN_03 이탈이 회차 끝.</item>
/// <item>단계 알림: 진행기 T31~T40 이 시작되면 상대에게 1회(<see cref="TryTakeFlowNotice"/>). 앞으로만 — 같은 단계 재알림·역순은 없음.</item>
/// <item>완료 보고: 상대 모델의 단계 완료를 진행기에 1회(<see cref="TryAcceptReport"/>). 진행기의 지금 단계와 같을 때만 — 어긋남·중복·역순은 무시.</item>
/// <item>T33 초기화(플레이어 쪽 <see cref="TrainingResetOptions.PracticeReset"/>): 회차당 1회(<see cref="TryConsumePracticeReset"/>).</item>
/// <item>무전 이벤트: A 첫 간파 성공(TRAIN_03_COUNTER)·C 첫 유효 벽 충돌(TRAIN_03_WALL)·D 첫 마무리 진입(TRAIN_03_COMBAT) — 각각 회차당 1회.</item>
/// </list>
/// </summary>
public sealed class LobbySparringLink
{
    LobbyTutorialActivity _notified = LobbyTutorialActivity.None;
    LobbyTutorialActivity _reported = LobbyTutorialActivity.None;
    bool _practiceResetDone;
    bool _counterRaised;
    bool _wallRaised;
    bool _finishRaised;

    /// <summary>회차 진행 중(상대를 스폰한 뒤 ~ 정리 전).</summary>
    public bool IsRoundActive { get; private set; }

    /// <summary>이번 판(진행기 수명)에서 시작한 회차 수 — 로그용.</summary>
    public int Round { get; private set; }

    /// <summary>마지막으로 진행기에 보고한 상대 단계 완료(없으면 None).</summary>
    public LobbyTutorialActivity LastReported => _reported;

    /// <summary>TRAIN_03 단계(T31~T40)인가.</summary>
    public static bool IsSparringStage(LobbyTutorialActivity activity) => LobbyTutorialRules.IsSparringActivity(activity);

    /// <summary>
    /// 훈련 상대가 있어야 하는가 — T31 시작(T30 집결 확정) 순간부터 T40 까지(사용자 10-22: 집결 대기 중엔 없다).
    /// T30 집결 대기·그 밖(T50·브리핑·출격·로비)은 없음.
    /// </summary>
    public static bool BotPresent(LobbyTutorialActivity activity) => IsSparringStage(activity);

    /// <summary>
    /// [전 피어] 지정 벽 표시(주황 띠, 임시 비주얼)를 켜는가 — 벽 목표가 있는 T33~T40 동안(옛 Train03C·D). T31·T32·T50 이후·로비·진행기 없음(None)은 끈다.
    /// 클라는 복제 스냅샷의 현재 행 Activity(<see cref="LobbyTrainingDirector.CurrentActivity"/>)로 판정 — 늦게 붙은 클라도 같다.
    /// </summary>
    public static bool WallMarkerVisible(LobbyTutorialActivity activity) =>
        activity == LobbyTutorialActivity.ResetPosition || activity == LobbyTutorialActivity.Wall || activity == LobbyTutorialActivity.SuppressKill;

    /// <summary>새 회차 — 기록을 비운다. 이미 진행 중이면 false(그대로 — 상대 재사용).</summary>
    public bool BeginRound()
    {
        if (IsRoundActive)
            return false;

        IsRoundActive = true;
        Round++;
        _notified = LobbyTutorialActivity.None;
        _reported = LobbyTutorialActivity.None;
        _practiceResetDone = false;
        _counterRaised = false;
        _wallRaised = false;
        _finishRaised = false;
        return true;
    }

    /// <summary>회차 끝(TRAIN_03 이탈·진행기 디스폰). 진행 중이 아니었으면 false.</summary>
    public bool EndRound()
    {
        if (!IsRoundActive)
            return false;

        IsRoundActive = false;
        return true;
    }

    /// <summary>
    /// 진행기 단계를 상대에게 알릴 차례인가 — 회차 중이고 T31~T40 이며 지난 알림보다 뒤일 때 1회(<paramref name="notice"/> = 그 단계).
    /// 같은 단계 재평가·역순은 false.
    /// </summary>
    public bool TryTakeFlowNotice(LobbyTutorialActivity activity, out LobbyTutorialActivity notice)
    {
        notice = default;
        if (!IsRoundActive || !IsSparringStage(activity) || activity <= _notified)
            return false;

        _notified = activity;
        notice = activity;
        return true;
    }

    /// <summary>
    /// 상대 단계 완료(<paramref name="completed"/>)를 진행기에 보고할 것인가 — 회차 중, T31~T40, 지난 보고보다 뒤,
    /// 그리고 진행기가 지금 그 단계일 때만 true(보고한 것으로 기록). 어긋남·중복·역순은 false(호출측 경고).
    /// </summary>
    public bool TryAcceptReport(LobbyTutorialActivity completed, LobbyTutorialActivity flowActivity)
    {
        if (!IsRoundActive || !IsSparringStage(completed) || completed <= _reported)
            return false;
        if (completed != flowActivity)
            return false;

        _reported = completed;
        return true;
    }

    /// <summary>T33 초기화의 플레이어 쪽 리셋 — 회차당 처음 한 번만 true.</summary>
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

    /// <summary>
    /// 첫 마무리 진입 이벤트(TRAIN_03_COMBAT — "D 제압 중 유효 피해 경험 + 제압 종료")를 낼 차례인가. 상대 모델이 마무리 중이면(<see cref="TrainingSparringModel.IsFinishing"/> —
    /// 제압 피해 확인 + 제압 종료, 마무리 체력 설정 직후) 회차당 1회. 피해 없이 끝난 제압은 마무리가 아니므로 여기 오지 않는다.
    /// </summary>
    public bool TryRaiseFirstFinish(bool finishing)
    {
        if (!IsRoundActive || _finishRaised || !finishing)
            return false;

        _finishRaised = true;
        return true;
    }
}
