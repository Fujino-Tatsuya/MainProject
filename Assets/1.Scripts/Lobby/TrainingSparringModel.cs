using System;
using System.Collections.Generic;

/// <summary>TRAIN_03 훈련 상대 단계(할 일 86 T5a). 진행기 단계(<see cref="LobbyTutorialActivity"/>)와 별개 — 상대 쪽 하위 상태다. 복제용이라 byte, 값은 끝에만 추가.</summary>
public enum TrainingSparringStage : byte
{
    /// <summary>TRAIN_03 시작 전(진행기가 Train03A 진행을 아직 안 알렸다).</summary>
    Idle = 0,
    /// <summary>A 간파 소개 — 처치 불가·칸 전이 중지·칸 UI 숨김. 간파 성공 그로기가 끝나면 완료.</summary>
    CounterIntro = 1,
    /// <summary>B 취약 상태의 일반 CC — 학습용 취약 유지·자발 행동 없음. 유효 CC 1회 적용→종료면 완료.</summary>
    CrowdControlIntro = 2,
    /// <summary>B→C 명시 초기화 대기 — 상대가 <see cref="TrainingSparringModel.TryConsumeResetRequest"/> 로 받아 실행하고 <see cref="TrainingSparringModel.OnResetExecuted"/> 로 알린다.</summary>
    Resetting = 3,
    /// <summary>C 벽 활용·제압 — 실제 칸 규칙. 지정 벽 유효 충돌 기록 뒤 제압 진입 = D.</summary>
    WallAndSuppress = 4,
    /// <summary>D 집중 공격·마무리 — 제압 중 유효 피해 + 제압 종료 → 마무리 체력·처치 방지 해제 → 사망 = 완료.</summary>
    Finisher = 5,
    /// <summary>TRAIN_03 완료(상대 처치).</summary>
    Completed = 6,
}

/// <summary>B 에서 인정하는 일반 CC 종류(사용자 10-22). 간파 성공 그로기·넉백은 CC 가 아니다(이벤트 자체가 따로).</summary>
public enum TrainingSparringCrowdControl : byte
{
    /// <summary>취약 중 우클릭(간파 스킬) 적중 넉백 — 23호 vulnerableKnockbackDistance/Time, 방향 무관.</summary>
    VulnerableKnockback = 1,
    /// <summary>취약 중 팔라딘 Q — 밀림 + 경직.</summary>
    PushStagger = 2,
}

/// <summary>총 칸 판정 결과(명세 §9.5 [확인]). 2 미만이면 벽 보상을 배울 수 없다 — 칸을 몰래 늘리지 않고 오류만 낸다.</summary>
public readonly struct TrainingSparringSegmentCheck
{
    public const int MinimumSegments = 2;

    /// <summary>게이지 최대에서 0 까지 깎는 간파 성공 수(= 칸 UI 의 칸 수). 감소량이 0 이하면 0.</summary>
    public int TotalSegments { get; }

    public bool IsValid => TotalSegments >= MinimumSegments;

    /// <summary>오류 로그 문구. 정상이면 null.</summary>
    public string Error { get; }

    TrainingSparringSegmentCheck(int totalSegments, string error) => (TotalSegments, Error) = (totalSegments, error);

    /// <param name="gaugeMax">간파 게이지 최대(<see cref="BossCounterProgress.GaugeMax"/>).</param>
    /// <param name="step">간파 성공 1회 감소량(23호 데이터 counterGaugeStep).</param>
    public static TrainingSparringSegmentCheck Evaluate(float gaugeMax, float step)
    {
        // 부동소수 오차로 100/20 = 5.0000001 이 6칸이 되지 않게 조금 깎고 올림한다.
        int total = step > 0f && gaugeMax > 0f ? (int)Math.Ceiling(gaugeMax / step - 0.0001f) : 0;
        string error = total >= MinimumSegments
            ? null
            : $"TRAIN_03 총 칸 {total} (게이지 {gaugeMax} / 감소량 {step}) — {MinimumSegments} 이상이어야 벽 보상을 배울 수 있다. 전투 데이터를 조정할 것(실행 중 칸을 추가하지 않는다).";
        return new TrainingSparringSegmentCheck(total, error);
    }
}

/// <summary>
/// [서버] TRAIN_03 훈련 상대 상태기계 — 할 일 86 PLAN §3.5·§7, 명세 v0.2 §6(훈련 보정)·§9. NGO·MonoBehaviour 의존 없는 순수 클래스(EditMode 테스트 대상).
/// <para>시간은 갖지 않는다 — 그로기·취약·제압 시간은 23호 데이터(groggyDuration·vulnerableDuration·breakDuration)로 상대가 재고,
/// 여기엔 "시작·끝" 이벤트만 들어온다. 게이지 값도 상대가 <see cref="BossCounterProgress"/> 로 갖는다 — 여기는 칸 감소가 있었는지만 받는다.</para>
/// <list type="bullet">
/// <item>A: 처치 불가·칸 전이 중지·칸 UI 숨김. 간파 성공 1회 → 그 그로기 종료 = 완료(보고 1회). 간파 성공 자체엔 넉백 없음.</item>
/// <item>B: 학습용 취약 유지·자발 공격/이동 없음. 유효 일반 CC(취약 넉백 / Q 밀림경직) 가 B 에서 적용되고 끝나면 완료.
/// A 에서 걸린 CC·간파 성공 그로기는 제외. B 의 벽 충돌은 C 로 이월하지 않는다.</item>
/// <item>B→C 초기화: 진행기가 Train03C 진행을 알리면 1회만 요청(재접속·중복 메시지로 반복 안 됨). 실행 보고 뒤 보정 제거·칸 UI 켬.</item>
/// <item>C: 칸 감소를 동반한 지정 벽 유효 충돌 1회 기록 → 그 뒤 제압 진입 = D(보고 1회). 같은 넉백의 벽+마지막 칸은 상대가 벽을 먼저 알린다.
/// 이미 0칸이라 감소가 거부된 접촉은 불인정. 벽 목표 전 제압은 정상 실행·종료 후 C 계속. D 전까지 처치 불가.</item>
/// <item>D: 제압 시작 순간 진입. 제압 종료까지 처치 방지. 제압 중 유효 피해 1회 + 제압 종료 → 마무리 체력 요청(1회)·처치 방지 해제 → 사망 = 완료(보고 1회).
/// 피해 없이 끝난 제압은 다음 사이클(간파·벽·제압)에서 재확인 — 무기한 제압·강제 쿨 반환 없음.</item>
/// </list>
/// 이벤트 메서드는 상태가 바뀌었거나 기록했으면 true — 현재 단계에 맞지 않는 이벤트·중복·역순은 무시(false)라 호출측이 로그만 남긴다.
/// <para>할 일 157 ②: 진행기 단계 = 단계표 Activity. 옛 Train03A→Counter(T31), B→VulnerableCc(T32), C 진입(초기화)→ResetPosition(T33),
/// C 완료(벽 기록 뒤 제압 진입)→Wall(T34), D→SuppressKill(T40) 로 이름만 옮겼다 — 판정 내용은 커밋 ⑦·⑧에서 바꾼다.</para>
/// </summary>
public sealed class TrainingSparringModel
{
    readonly Queue<LobbyTutorialActivity> _pendingReports = new Queue<LobbyTutorialActivity>();
    readonly bool _suppressAccelerationEnabled;

    bool _counterSucceededInA, _reportedA;
    bool _reportedB;
    int _pendingCrowdControl;          // B 에서 적용된 CC 종류 비트(1 << kind)
    bool _resetRequested, _resetConsumed;
    bool _wallRecorded;
    bool _suppressActive;
    bool _damagedDuringSuppress;
    bool _finishing;                    // D: 제압 피해 확인 + 제압 종료 — 처치 방지 해제됨
    bool _finishHealthRequested, _finishHealthConsumed;

    /// <param name="segments">총 칸 판정(<see cref="TrainingSparringSegmentCheck.Evaluate"/>). 오류여도 진행은 그대로 — 호출측이 <see cref="TrainingSparringSegmentCheck.Error"/> 를 로그한다.</param>
    /// <param name="suppressAccelerationEnabled">
    /// 제압 중 쿨타임 가속(명세 §9.5·§9.6). 🔴 보류(사용자 10-22 확인 중) — 실전 코드에 없다. 지금은 false 로 만들고,
    /// 실전 규칙이 생기면 그 데이터 값으로 켠다. 켜도 모델은 <see cref="AppliesSuppressAcceleration"/> 만 낸다(적용은 상대·플레이어 쪽).
    /// </param>
    public TrainingSparringModel(TrainingSparringSegmentCheck segments, bool suppressAccelerationEnabled = false)
    {
        Segments = segments;
        _suppressAccelerationEnabled = suppressAccelerationEnabled;
    }

    public TrainingSparringStage Stage { get; private set; } = TrainingSparringStage.Idle;

    public TrainingSparringSegmentCheck Segments { get; }

    /// <summary>C 에서 지정 벽 유효 충돌을 기록했다(D 진입 조건의 앞 절반).</summary>
    public bool HasRecordedWall => _wallRecorded;

    /// <summary>지금 제압 중인가(상대가 알린 시작·종료 사이).</summary>
    public bool IsSuppressActive => _suppressActive;

    // ── 출력 ──

    /// <summary>처치 방지 — A~D 동안 체력이 0 이 되지 않게 한다. D 에서 제압 피해 확인 + 제압 종료 뒤에만 해제.</summary>
    public bool PreventsDeath => Stage != TrainingSparringStage.Completed && !_finishing;

    /// <summary>간파·벽의 칸 감소와 제압 전이를 허용 — C·D(실제 규칙). A/B 는 중지(첫 성공의 벽/칸이 C/D 를 미리 완료하지 않게).</summary>
    public bool AllowsGaugeTransition => Stage == TrainingSparringStage.WallAndSuppress || Stage == TrainingSparringStage.Finisher;

    /// <summary>B 학습용 취약 유지 — 취약 시간이 끝나도 풀지 않는다. 일반 CC 반응은 정상(영구 경직 금지).</summary>
    public bool HoldsVulnerable => Stage == TrainingSparringStage.CrowdControlIntro;

    /// <summary>칸 UI(중간보스 체력바 재사용) 표시 — 초기화 뒤 C·D.</summary>
    public bool ShowsSegmentUi => Stage == TrainingSparringStage.WallAndSuppress || Stage == TrainingSparringStage.Finisher;

    /// <summary>D 마무리 중 — 제압 피해 확인 + 제압 종료 뒤 사망 전까지. 바 이름이 칸 대신 '마무리하세요'(할 일 86 T5c 후속).</summary>
    public bool IsFinishing => Stage == TrainingSparringStage.Finisher && _finishing;

    /// <summary>자발 공격·이동 허용 — A(간파 가능한 직접 공격 반복)·C·D. B·초기화 중·시작 전·완료는 없음.</summary>
    public bool AllowsSelfAction => Stage == TrainingSparringStage.CounterIntro
        || Stage == TrainingSparringStage.WallAndSuppress
        || Stage == TrainingSparringStage.Finisher;

    /// <summary>제압 중 쿨타임 가속을 적용할지 — 🔴 보류 플래그. 생성자에서 켠 경우에만 C·D 제압 중 true.</summary>
    public bool AppliesSuppressAcceleration => _suppressAccelerationEnabled && _suppressActive && AllowsGaugeTransition;

    /// <summary>진행기에 보낼 단계 완료 보고를 하나 꺼낸다(완료마다 정확히 1회). 없으면 false.</summary>
    public bool TryConsumeStageReport(out LobbyTutorialActivity completed)
    {
        if (_pendingReports.Count == 0)
        {
            completed = default;
            return false;
        }

        completed = _pendingReports.Dequeue();
        return true;
    }

    /// <summary>B→C 초기화 실행 요청 — 회차마다 처음 한 번만 true.</summary>
    public bool TryConsumeResetRequest()
    {
        if (!_resetRequested || _resetConsumed)
            return false;

        _resetConsumed = true;
        return true;
    }

    /// <summary>마무리 체력(훈련 데이터) 적용 요청 — D 에서 제압 피해 확인 + 제압 종료 뒤 처음 한 번만 true.</summary>
    public bool TryConsumeFinishHealthRequest()
    {
        if (!_finishHealthRequested || _finishHealthConsumed)
            return false;

        _finishHealthConsumed = true;
        return true;
    }

    // ── 입력 ──

    /// <summary>
    /// 진행기 단계 시작 알림(T31~T40 Activity 진입). 앞으로만 — 같은 단계·역순·다른 단계는 무시.
    /// ResetPosition(T33) = 초기화 요청 1회(이미 요청했으면 무시). Wall(T34)·SuppressKill(T40) = 상대가 이미 그 상태라 확인만(false).
    /// </summary>
    public bool OnFlowStageStarted(LobbyTutorialActivity flowStage)
    {
        switch (flowStage)
        {
            case LobbyTutorialActivity.Counter:
                if (Stage != TrainingSparringStage.Idle) return false;
                Stage = TrainingSparringStage.CounterIntro;
                return true;

            case LobbyTutorialActivity.VulnerableCc:
                if (Stage != TrainingSparringStage.CounterIntro || !_reportedA)
                    return false;
                Stage = TrainingSparringStage.CrowdControlIntro;
                _pendingCrowdControl = 0;
                return true;

            case LobbyTutorialActivity.ResetPosition:
                if (Stage != TrainingSparringStage.CrowdControlIntro || _resetRequested) return false;
                Stage = TrainingSparringStage.Resetting;
                _resetRequested = true;
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// 초기화를 실행했다(상대 공격·CC 안전 종료, 위치·체력·취약·제압 리셋, C 위치 재배치, 칸 가득). C 시작 — 보정 제거·칸 UI 켬.
    /// 요청을 꺼낸 뒤에만 받는다.
    /// </summary>
    public bool OnResetExecuted()
    {
        if (Stage != TrainingSparringStage.Resetting || !_resetConsumed)
            return false;

        Stage = TrainingSparringStage.WallAndSuppress;
        _wallRecorded = false;
        _suppressActive = false;
        return true;
    }

    /// <summary>간파 성공(게이지가 남아 그로기·취약 진입). A 에서만 기록 — 나머지 단계에선 상대가 실제 규칙대로 처리하고 모델은 무관.</summary>
    public bool OnCounterSucceeded()
    {
        if (Stage != TrainingSparringStage.CounterIntro || _counterSucceededInA)
            return false;

        _counterSucceededInA = true;
        return true;
    }

    /// <summary>간파 성공 그로기 종료. A 에서 성공을 기록한 뒤면 A 완료(보고 1회). 성공 전 그로기 종료·중복은 무시.</summary>
    public bool OnCounterGroggyEnded()
    {
        if (Stage != TrainingSparringStage.CounterIntro || !_counterSucceededInA || _reportedA)
            return false;

        _reportedA = true;
        _pendingReports.Enqueue(LobbyTutorialActivity.Counter);
        return true;
    }

    /// <summary>일반 CC 가 적용됐다(면역 등으로 거부된 공격은 상대가 부르지 않는다). B 에서만 기록.</summary>
    public bool OnCrowdControlApplied(TrainingSparringCrowdControl kind)
    {
        if (Stage != TrainingSparringStage.CrowdControlIntro || _reportedB || !IsDefined(kind))
            return false;

        _pendingCrowdControl |= Bit(kind);
        return true;
    }

    /// <summary>일반 CC 가 끝났다. B 에서 적용된 같은 종류면 B 완료(보고 1회). A 에서 걸린 CC 의 종료·적용 없는 종료는 무시.</summary>
    public bool OnCrowdControlEnded(TrainingSparringCrowdControl kind)
    {
        if (Stage != TrainingSparringStage.CrowdControlIntro || _reportedB || !IsDefined(kind)
            || (_pendingCrowdControl & Bit(kind)) == 0)
            return false;

        _reportedB = true;
        _pendingCrowdControl = 0;
        _pendingReports.Enqueue(LobbyTutorialActivity.VulnerableCc);
        return true;
    }

    /// <summary>
    /// 지정 벽 유효 충돌(<c>UnitWallImpacts</c> 경로, 넉백 1회당 최대 1회는 상대가 보장). C 에서 칸 감소를 동반했을 때만 벽 목표 기록 —
    /// 이번 호출이 처음 기록이면 true(첫 벽 무전용). 이미 0칸이라 감소가 거부된 접촉·제압 중·A/B/D 의 충돌은 무시.
    /// 같은 넉백의 마지막 칸이면 상대는 이걸 <see cref="OnSuppressStarted"/> 보다 먼저 부른다.
    /// </summary>
    public bool OnWallImpact(bool reducedGauge)
    {
        // 제압 중엔 게이지가 0 이라 감소가 있을 수 없다 — 들어오면 상대 쪽 오류라 기록하지 않는다.
        if (Stage != TrainingSparringStage.WallAndSuppress || !reducedGauge || _wallRecorded || _suppressActive)
            return false;

        _wallRecorded = true;
        return true;
    }

    /// <summary>
    /// 제압 진입(게이지 0). C 에서 벽을 기록했으면 지금이 D 시작(Wall 완료 보고 1회). 벽 전이면 정상 제압만(C 계속).
    /// D 에서의 새 제압은 피해 확인을 새로 시작한다. A/B(전이 중지)·마무리 중·중복 시작은 무시.
    /// </summary>
    public bool OnSuppressStarted()
    {
        if (!AllowsGaugeTransition || _suppressActive || _finishing)
            return false;

        _suppressActive = true;
        _damagedDuringSuppress = false;

        if (Stage == TrainingSparringStage.WallAndSuppress && _wallRecorded)
        {
            Stage = TrainingSparringStage.Finisher;
            _pendingReports.Enqueue(LobbyTutorialActivity.Wall);
        }

        return true;
    }

    /// <summary>제압 중 유효 피해(실제로 체력에 들어간 플레이어 피해). D 의 제압 중에만 기록 — 처음이면 true.</summary>
    public bool OnSuppressDamage()
    {
        if (Stage != TrainingSparringStage.Finisher || !_suppressActive || _damagedDuringSuppress)
            return false;

        _damagedDuringSuppress = true;
        return true;
    }

    /// <summary>
    /// 제압 종료(게이지 가득 복원은 상대 몫). D 에서 이번 제압 중 피해가 있었으면 마무리 — 처치 방지 해제·마무리 체력 요청 1회.
    /// 피해가 없었으면 D 유지(다음 제압에서 재확인). C 에선 정상 종료만.
    /// </summary>
    public bool OnSuppressEnded()
    {
        if (!_suppressActive)
            return false;

        _suppressActive = false;
        if (Stage == TrainingSparringStage.Finisher && _damagedDuringSuppress)
        {
            _finishing = true;
            _finishHealthRequested = true;
        }

        _damagedDuringSuppress = false;
        return true;
    }

    /// <summary>상대 사망. D 마무리(처치 방지 해제 뒤)에서만 완료(SuppressKill 보고 1회). 그 밖의 사망은 처치 방지 누락 — false(호출측 경고).</summary>
    public bool OnDied()
    {
        if (Stage != TrainingSparringStage.Finisher || !_finishing)
            return false;

        Stage = TrainingSparringStage.Completed;
        _pendingReports.Enqueue(LobbyTutorialActivity.SuppressKill);
        return true;
    }

    static bool IsDefined(TrainingSparringCrowdControl kind) =>
        kind == TrainingSparringCrowdControl.VulnerableKnockback || kind == TrainingSparringCrowdControl.PushStagger;

    static int Bit(TrainingSparringCrowdControl kind) => 1 << (int)kind;
}
