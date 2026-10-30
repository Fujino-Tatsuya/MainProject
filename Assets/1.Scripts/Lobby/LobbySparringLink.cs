/// <summary>
/// [서버] 간파 훈련(T31~T40) 진행기 ↔ 훈련 상대 연결 규칙(할 일 86 T5c → 157 ⑦). NGO·MonoBehaviour 의존 없는 순수 클래스(EditMode 테스트 대상).
/// 실행은 <see cref="LobbyTrainingSparring"/> — 여기는 "언제 한 번만 하나"만 정한다.
/// <list type="bullet">
/// <item>회차: 진행기가 T31~T40 에 있는 동안 상대 1개(<see cref="BotPresent"/>). 회차 시작(<see cref="BeginRound"/>)마다 기록을 비운다 — 판 끝 로비 복귀·간파 훈련 이탈이 회차 끝.</item>
/// <item>단계 알림: 진행기 T31~T40 이 시작되면 상대에게 1회(<see cref="TryTakeFlowNotice"/>). 앞으로만 — 같은 단계 재알림·역순은 없음.</item>
/// <item>완료 보고: 상대 모델의 단계 완료를 진행기에 1회(<see cref="TryAcceptReport"/>). 진행기의 지금 단계와 같을 때만 — 어긋남·중복·역순은 무시.</item>
/// <item>T33 위치 복귀의 플레이어 쪽 리셋(<see cref="TrainingResetOptions.PracticeReset"/>, Q10 A): 회차당 1회(<see cref="TryConsumePracticeReset"/>).</item>
/// <item>단계 안 사건 무전(TRAIN_03_COUNTER/WALL/COMBAT)은 없앴다(157 Q6 A — 명세 §3.3 중복 대사 금지, 문구 키는 보존).</item>
/// </list>
/// </summary>
public sealed class LobbySparringLink
{
    LobbyTutorialActivity _notified = LobbyTutorialActivity.None;
    LobbyTutorialActivity _reported = LobbyTutorialActivity.None;
    bool _practiceResetDone;

    /// <summary>회차 진행 중(상대를 스폰한 뒤 ~ 정리 전).</summary>
    public bool IsRoundActive { get; private set; }

    /// <summary>이번 판(진행기 수명)에서 시작한 회차 수 — 로그용.</summary>
    public int Round { get; private set; }

    /// <summary>마지막으로 진행기에 보고한 상대 단계 완료(없으면 None).</summary>
    public LobbyTutorialActivity LastReported => _reported;

    /// <summary>간파 훈련 단계(T31~T40)인가.</summary>
    public static bool IsSparringStage(LobbyTutorialActivity activity) => LobbyTutorialRules.IsSparringActivity(activity);

    /// <summary>
    /// 훈련 상대가 있어야 하는가 — T31 시작(T30 집결 확정) 순간부터 T40 까지(사용자 10-22: 집결 대기 중엔 없다).
    /// T30 집결 대기·그 밖(T50·브리핑·출격·로비)은 없음.
    /// </summary>
    public static bool BotPresent(LobbyTutorialActivity activity) => IsSparringStage(activity);

    /// <summary>
    /// [전 피어] 지정 벽 표시(주황 띠, 임시 비주얼)를 켜는가 — 벽 목표가 있는 T33(위치 복귀)·T34 동안. T34 완료(지정 벽 충돌) 즉시 끈다 —
    /// 계약의 <c>ActiveTargetWallId</c> 가 비는 것과 같은 순간(명세 §5 T34). T31·T32·T40 이후·로비·진행기 없음(None)은 끈다.
    /// 클라는 복제 스냅샷의 현재 행 Activity(<see cref="LobbyTrainingDirector.CurrentActivity"/>)로 판정 — 늦게 붙은 클라도 같다.
    /// </summary>
    public static bool WallMarkerVisible(LobbyTutorialActivity activity) =>
        activity == LobbyTutorialActivity.ResetPosition || activity == LobbyTutorialActivity.Wall;

    /// <summary>
    /// 이 단계 알림에서 시작할 "새 시전" 판정(<see cref="LobbyCastWindow"/>) — T32 = CC, T34 = 벽, 그 밖 = 없음.
    /// </summary>
    public static LobbyCastWindowMode CastWindowModeFor(LobbyTutorialActivity activity) => activity switch
    {
        LobbyTutorialActivity.VulnerableCc => LobbyCastWindowMode.CrowdControl,
        LobbyTutorialActivity.Wall => LobbyCastWindowMode.Wall,
        _ => LobbyCastWindowMode.None,
    };

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
        return true;
    }

    /// <summary>회차 끝(간파 훈련 이탈·진행기 디스폰). 진행 중이 아니었으면 false.</summary>
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

    /// <summary>T33 위치 복귀의 플레이어 쪽 리셋 — 회차당 처음 한 번만 true.</summary>
    public bool TryConsumePracticeReset()
    {
        if (!IsRoundActive || _practiceResetDone)
            return false;

        _practiceResetDone = true;
        return true;
    }
}
