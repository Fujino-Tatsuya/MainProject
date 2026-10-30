using System;
using System.Collections.Generic;

/// <summary>
/// 간파 훈련 상대 하위 단계(할 일 157 ⑦ — 명세 §5 T31~T40). 진행기 단계(<see cref="LobbyTutorialActivity"/>)를 따라가는 상대 쪽 상태다.
/// 복제용이라 byte, 숫자는 옛 A~D(86) 와 같은 자리 — 값은 끝에만 추가.
/// </summary>
public enum TrainingSparringStage : byte
{
    /// <summary>T31 시작 전(진행기가 Counter 를 아직 안 알렸다).</summary>
    Idle = 0,
    /// <summary>T31 간파 — 처치 불가·칸 전이 중지·칸 UI 숨김. 간파 창을 늘린 같은 공격 반복. 간파 성공 순간 완료 + 취약 무기한 유지 시작.</summary>
    Counter = 1,
    /// <summary>T32 취약 중 CC — 취약 유지·자발 행동 없음. 단계 진입 뒤 새 시전의 넉백/기절 실제 적용 1회(판정은 <see cref="LobbyCastWindow"/>) = 완료.</summary>
    VulnerableCc = 2,
    /// <summary>T33 위치 복귀 — 앞 CC 반응이 끝나면 상대만 C 위치로(취약·칸 보존, 체력만 복구). 실행 = 완료. 이 동안 새 CC 반응 없음.</summary>
    Resetting = 3,
    /// <summary>T34 벽 — 지정 벽 유효 넉백 충돌 자체(새 시전) = 완료. 칸 감소는 실제 규칙대로 일어나되 조건 아님. 빗나간 넉백 = 위치 복구 후 재시도.</summary>
    Wall = 4,
    /// <summary>T40 제압·처치 — 남은 칸 보존. 칸 0 이면 즉시 제압, 남았으면 패턴 재개 → 간파/벽으로 소진 → 제압. 제압은 처치할 때까지 유지, 제압 중 사망 = 완료.</summary>
    SuppressKill = 5,
    /// <summary>T40 완료(상대 처치).</summary>
    Completed = 6,
}

/// <summary>상대가 실제로 적용한 CC 종류(비트). 팔라딘 Q 처럼 밀림 + 경직이면 둘 다. 간파 성공 그로기는 CC 가 아니다.</summary>
[Flags]
public enum TrainingSparringCrowdControl : byte
{
    None = 0,
    /// <summary>취약 중 밀림 — 간파 스킬 적중(23호 고정 거리) 또는 넉백 세기가 있는 공격.</summary>
    Knockback = 1,
    /// <summary>취약 중 기절(경직) — 공격의 staggerDuration(밀림 뒤 또는 단독).</summary>
    Stun = 2,
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
/// [서버] 간파 훈련 상대 상태기계 — 할 일 157 ⑦·⑧(PLAN §5 ⑦·⑧, 명세 §5 T30~T40·§6). NGO·MonoBehaviour 의존 없는 순수 클래스(EditMode 테스트 대상).
/// <para>시간은 갖지 않는다 — 그로기·취약·제압 시간은 23호 데이터로 상대가 잰다. "새 시전인가"는 여기서 보지 않는다 —
/// <see cref="LobbyCastWindow"/> 가 거른 뒤 <see cref="OnCrowdControlAccepted"/>·<see cref="OnWallAccepted"/> 로 들어온다.</para>
/// <list type="bullet">
/// <item>T31 Counter: 처치 불가·칸 전이 중지. 간파 성공 = 그 순간 완료(보고 1회, 그로기 종료를 기다리지 않음) + 취약 무기한 유지 시작 + 자발 행동 멈춤.</item>
/// <item>T32 VulnerableCc: 취약 유지·자발 행동 없음. 걸러진 CC 1회 = 완료(적용 순간 — 반응 종료는 T33 이 기다린다). 진입 프레임 이하 사건 불인정(같은 프레임 연쇄 금지).</item>
/// <item>T33 Resetting: 초기화 요청 1회 — 상대가 CC 반응이 끝난 뒤에만 꺼내 실행(<see cref="TryConsumeResetRequest"/>)하고 <see cref="OnResetExecuted"/> = 완료. 이 동안 새 CC 반응 없음.</item>
/// <item>T34 Wall: 걸러진 지정 벽 충돌 1회 = 완료. 칸 감소·제압은 실제 규칙(완료 조건 아님). 완료 전 빗나간 넉백은 <see cref="NeedsRetryAfterMiss"/> → 상대가 위치 복구.</item>
/// <item>T40 SuppressKill(⑧): 칸(게이지) 보존. 진입 때 칸 0·제압 아님이면 즉시 제압(<see cref="ShouldSuppressOnEntry"/>), 남았으면 취약 무기한 보정 해제·자발 행동 재개 →
/// 실제 규칙(간파·벽)으로 칸 소진 → 제압. 제압 전엔 처치 방지, 제압 중엔 공격 대기·사망 허용. 제압은 지속시간으로 끝나지 않는다(<see cref="HoldsSuppress"/> — 상대가 매 틱 연장, Q17 A).
/// 사망 = 완료(마지막 타격 귀속 검사 없음). 옛 D 의 '제압 중 피해 → 제압 종료 → 마무리 체력' 단계는 없앴다.</item>
/// </list>
/// 이벤트 메서드는 상태가 바뀌었거나 기록했으면 true — 현재 단계에 맞지 않는 이벤트·중복·역순은 무시(false)라 호출측이 로그만 남긴다.
/// </summary>
public sealed class TrainingSparringModel
{
    const int NoFrame = int.MinValue;

    readonly Queue<LobbyTutorialActivity> _pendingReports = new Queue<LobbyTutorialActivity>();
    readonly bool _suppressAccelerationEnabled;

    int _stageFrame = NoFrame;
    bool _counterReported;
    bool _ccReported;
    bool _resetRequested, _resetConsumed, _resetReported;
    bool _wallReported;
    bool _suppressActive;

    /// <param name="segments">총 칸 판정(<see cref="TrainingSparringSegmentCheck.Evaluate"/>). 오류여도 진행은 그대로 — 호출측이 <see cref="TrainingSparringSegmentCheck.Error"/> 를 로그한다.</param>
    /// <param name="suppressAccelerationEnabled">
    /// 제압 중 쿨타임 가속(명세 §9.5·§9.6). 🔴 보류 — 실전 코드에 없다. 지금은 false 로 만들고,
    /// 실전 규칙이 생기면 그 데이터 값으로 켠다. 켜도 모델은 <see cref="AppliesSuppressAcceleration"/> 만 낸다(적용은 상대·플레이어 쪽).
    /// </param>
    public TrainingSparringModel(TrainingSparringSegmentCheck segments, bool suppressAccelerationEnabled = false)
    {
        Segments = segments;
        _suppressAccelerationEnabled = suppressAccelerationEnabled;
    }

    public TrainingSparringStage Stage { get; private set; } = TrainingSparringStage.Idle;

    public TrainingSparringSegmentCheck Segments { get; }

    /// <summary>T31 간파 성공을 기록했다(= Counter 보고함).</summary>
    public bool HasCounterSucceeded => _counterReported;

    /// <summary>T34 지정 벽 충돌을 인정했다(= Wall 보고함).</summary>
    public bool IsWallReported => _wallReported;

    /// <summary>지금 제압 중인가(상대가 알린 시작·종료 사이).</summary>
    public bool IsSuppressActive => _suppressActive;

    // ── 출력 ──

    /// <summary>처치 방지 — 완료 전까지 체력이 0 이 되지 않게 한다. T40 제압 중에만 해제(제압 진입 = 사망 허용).</summary>
    public bool PreventsDeath => Stage != TrainingSparringStage.Completed && !IsKillable;

    /// <summary>T40 제압 중 — 처치 가능(사망 = 완료).</summary>
    public bool IsKillable => Stage == TrainingSparringStage.SuppressKill && _suppressActive;

    /// <summary>
    /// 제압 무기한 유지 — T34·T40 제압 중. 제압 지속시간이 끝나도 풀지 않는다(상대가 매 틱 연장, PLAN Q17 A — 23호·공용 코드 무수정).
    /// T34 의 제압(지정 벽으로 칸 0)도 T40 이 이어받을 때까지 잡아 둔다.
    /// </summary>
    public bool HoldsSuppress => _suppressActive && AllowsGaugeTransition;

    /// <summary>간파·벽의 칸 감소와 제압 전이를 허용 — T34·T40. T31~T33 은 중지(간파 성공·CC 중 벽이 칸을 미리 깎지 않게).</summary>
    public bool AllowsGaugeTransition => Stage == TrainingSparringStage.Wall || Stage == TrainingSparringStage.SuppressKill;

    /// <summary>취약 무기한 유지 — T31 간파 성공부터 T34 끝(T40 알림)까지. 취약 시간이 끝나도 풀지 않는다(상대가 매 틱 연장, PLAN Q17).</summary>
    public bool HoldsVulnerable => _counterReported
        && (Stage == TrainingSparringStage.Counter || Stage == TrainingSparringStage.VulnerableCc
            || Stage == TrainingSparringStage.Resetting || Stage == TrainingSparringStage.Wall);

    /// <summary>새 CC 반응을 받는가 — T33 위치 복귀 중엔 받지 않는다(잔여 CC 정리, 명세 T33).</summary>
    public bool AcceptsCrowdControl => Stage != TrainingSparringStage.Resetting;

    /// <summary>칸 UI(중간보스 체력바 재사용) 표시 — T34·T40.</summary>
    public bool ShowsSegmentUi => Stage == TrainingSparringStage.Wall || Stage == TrainingSparringStage.SuppressKill;

    /// <summary>자발 공격·이동 허용 — T31 간파 성공 전(간파 가능한 같은 공격 반복)·T40 제압 전(패턴 재개). T32~T34·T40 제압 중(공격 대기)·시작 전·완료는 없음.</summary>
    public bool AllowsSelfAction => (Stage == TrainingSparringStage.Counter && !_counterReported)
        || (Stage == TrainingSparringStage.SuppressKill && !_suppressActive);

    /// <summary>훈련 간파 창 연장(PLAN Q11 A) — T31 간파 성공 전에만. 신호(기존 간파 예고)를 보고 입력해도 성공할 길이를 상대가 보장한다.</summary>
    public bool ExtendsCounterWindow => Stage == TrainingSparringStage.Counter && !_counterReported;

    /// <summary>제압 중 쿨타임 가속을 적용할지 — 🔴 보류 플래그. 생성자에서 켠 경우에만 T34·T40 제압 중 true.</summary>
    public bool AppliesSuppressAcceleration => _suppressAccelerationEnabled && _suppressActive && AllowsGaugeTransition;

    /// <summary>T34 완료 전 넉백이 지정 벽을 못 맞히고 끝났으면 위치 복구 후 재시도(명세 T34).</summary>
    public bool NeedsRetryAfterMiss => Stage == TrainingSparringStage.Wall && !_wallReported;

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

    /// <summary>
    /// T33 초기화 실행 요청 — 회차마다 처음 한 번만 true. 앞 CC 반응(밀림·기절)이 남아 있으면 기다린다(false, 다음 틱 재시도).
    /// </summary>
    public bool TryConsumeResetRequest(bool crowdControlActive)
    {
        if (!_resetRequested || _resetConsumed || crowdControlActive)
            return false;

        _resetConsumed = true;
        return true;
    }

    /// <summary>
    /// T40 진입 때 바로 제압할지 — 벽 충돌로 남은 칸이 0 인데 아직 제압이 아니면 true(명세 T40 '0칸이면 즉시 제압').
    /// 보통은 T34 의 그 벽 충돌이 이미 제압을 걸어 두어(<see cref="HoldsSuppress"/>) false 다.
    /// </summary>
    public bool ShouldSuppressOnEntry(int remainingSegments) =>
        Stage == TrainingSparringStage.SuppressKill && !_suppressActive && remainingSegments <= 0;

    // ── 입력 ──

    /// <summary>
    /// 진행기 단계 시작 알림(T31~T40 Activity, <paramref name="frame"/> = 알린 프레임). 앞으로만 — 앞 단계를 끝내지 않았거나 같은 단계·역순은 무시.
    /// 이 프레임 이하의 사건은 이 단계의 완료로 세지 않는다(한 프레임 연쇄 금지 — 명세 §6 끝).
    /// </summary>
    public bool OnFlowStageStarted(LobbyTutorialActivity flowStage, int frame)
    {
        switch (flowStage)
        {
            case LobbyTutorialActivity.Counter:
                if (Stage != TrainingSparringStage.Idle) return false;
                Enter(TrainingSparringStage.Counter, frame);
                return true;

            case LobbyTutorialActivity.VulnerableCc:
                if (Stage != TrainingSparringStage.Counter || !_counterReported) return false;
                Enter(TrainingSparringStage.VulnerableCc, frame);
                return true;

            case LobbyTutorialActivity.ResetPosition:
                if (Stage != TrainingSparringStage.VulnerableCc || !_ccReported) return false;
                Enter(TrainingSparringStage.Resetting, frame);
                _resetRequested = true;
                return true;

            case LobbyTutorialActivity.Wall:
                if (Stage != TrainingSparringStage.Resetting || !_resetReported) return false;
                Enter(TrainingSparringStage.Wall, frame);
                return true;

            case LobbyTutorialActivity.SuppressKill:
                if (Stage != TrainingSparringStage.Wall || !_wallReported) return false;
                Enter(TrainingSparringStage.SuppressKill, frame);   // 칸·T34 에서 걸린 제압은 그대로 이어받는다
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// 지정 상대의 실제 간파 성공(창 소비). T31 에서 처음이면 그 순간 완료(보고 1회) — 그로기 종료를 기다리지 않는다.
    /// 그 밖 단계의 간파는 상대가 실제 규칙대로 처리하고 모델은 무관.
    /// </summary>
    public bool OnCounterSucceeded()
    {
        if (Stage != TrainingSparringStage.Counter || _counterReported)
            return false;

        _counterReported = true;
        _pendingReports.Enqueue(LobbyTutorialActivity.Counter);
        return true;
    }

    /// <summary>
    /// 새 시전의 CC 실제 적용(<see cref="LobbyCastWindow"/> 통과, <paramref name="eventFrame"/> = 적용 프레임). T32 에서 진입 프레임 뒤 처음이면 완료(보고 1회).
    /// </summary>
    public bool OnCrowdControlAccepted(int eventFrame)
    {
        if (Stage != TrainingSparringStage.VulnerableCc || _ccReported || !IsAfterEntry(eventFrame))
            return false;

        _ccReported = true;
        _pendingReports.Enqueue(LobbyTutorialActivity.VulnerableCc);
        return true;
    }

    /// <summary>T33 초기화를 실행했다(CC 반응 끝 → 상대만 C 위치, 취약·칸 보존, 체력 복구, 잔여 CC 정리). 요청을 꺼낸 뒤 1회 = 완료(보고).</summary>
    public bool OnResetExecuted()
    {
        if (Stage != TrainingSparringStage.Resetting || !_resetConsumed || _resetReported)
            return false;

        _resetReported = true;
        _pendingReports.Enqueue(LobbyTutorialActivity.ResetPosition);
        return true;
    }

    /// <summary>새 시전 넉백의 지정 벽 유효 충돌(<see cref="LobbyCastWindow"/> 통과). T34 에서 진입 프레임 뒤 처음이면 완료(보고 1회).</summary>
    public bool OnWallAccepted(int eventFrame)
    {
        if (Stage != TrainingSparringStage.Wall || _wallReported || !IsAfterEntry(eventFrame))
            return false;

        _wallReported = true;
        _pendingReports.Enqueue(LobbyTutorialActivity.Wall);
        return true;
    }

    /// <summary>제압 진입(게이지 0). T34·T40 에서만 — T34 의 제압은 완료 조건이 아니다(T40 이 이어받는다). 중복 시작은 무시.</summary>
    public bool OnSuppressStarted()
    {
        if (!AllowsGaugeTransition || _suppressActive)
            return false;

        _suppressActive = true;
        return true;
    }

    /// <summary>
    /// 제압 종료(게이지 가득 복원은 상대 몫). T34·T40 제압은 <see cref="HoldsSuppress"/> 라 보통 오지 않는다 — 와도(외부 해제) 제압 전으로 돌아가 처치 방지·패턴 재개.
    /// </summary>
    public bool OnSuppressEnded()
    {
        if (!_suppressActive)
            return false;

        _suppressActive = false;
        return true;
    }

    /// <summary>상대 사망. T40 제압 중(처치 방지 해제)에서만 완료(SuppressKill 보고 1회) — 누가 마지막에 쳤는지는 보지 않는다. 그 밖의 사망은 처치 방지 누락 — false(호출측 경고).</summary>
    public bool OnDied()
    {
        if (!IsKillable)
            return false;

        Stage = TrainingSparringStage.Completed;
        _pendingReports.Enqueue(LobbyTutorialActivity.SuppressKill);
        return true;
    }

    void Enter(TrainingSparringStage stage, int frame)
    {
        Stage = stage;
        _stageFrame = frame;
    }

    bool IsAfterEntry(int eventFrame) => _stageFrame == NoFrame || eventFrame > _stageFrame;
}
