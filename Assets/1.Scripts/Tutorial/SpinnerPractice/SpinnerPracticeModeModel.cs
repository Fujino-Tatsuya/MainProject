/// <summary>튜토리얼 스피너봇의 모드. 전 피어에 복제된다(<see cref="TutorialSpinnerPractice.Mode"/>).</summary>
public enum SpinnerPracticeMode : byte
{
    /// <summary>휴면 — 인지·추격·공격 안 함 + 무적(피해·넉백·CC 무시).</summary>
    Dormant = 0,
    /// <summary>연습 — 죽지 않고, 9초마다 간파 창을 여는 회전 공격만 한다. 돌진은 피해 없이 밀쳐내기만.</summary>
    Practice = 1,
    /// <summary>본 모드 전환 대기 — 잠깐 정지(기본 2초). 아직 체력은 잠겨 있다.</summary>
    Transition = 2,
    /// <summary>본 모드 — 원래 스피너봇 그대로(모든 공격·피해·체력 감소·창 40%·쿨 6초).</summary>
    Real = 3,
}

/// <summary>
/// 튜토리얼 스피너봇 모드 상태 기계(순수 C# — EditMode 로 고정). 판정에 쓰는 질의는 전부 여기서 나온다.
///
/// 휴면은 <b>모드와 직교하는 깃발</b>이다 — 연습 중에 휴면을 걸었다 풀면 연습으로, 본 모드 중이면 본 모드로 돌아온다.
/// 전환 대기 타이머는 휴면 중에는 흐르지 않는다.
/// </summary>
public sealed class SpinnerPracticeModeModel
{
    readonly float _transitionSeconds;

    bool _dormant;
    SpinnerPracticeMode _phase = SpinnerPracticeMode.Practice;   // Practice · Transition · Real 중 하나
    float _transitionRemaining;

    /// <param name="transitionSeconds">본 모드 전환 전 정지 시간(초). 0 이하면 즉시 본 모드.</param>
    /// <param name="startDormant">휴면으로 시작하는가. 휴면이 풀리면 연습 모드다.</param>
    public SpinnerPracticeModeModel(float transitionSeconds, bool startDormant)
    {
        _transitionSeconds = transitionSeconds > 0f ? transitionSeconds : 0f;
        _dormant = startDormant;
    }

    public SpinnerPracticeMode Mode => _dormant ? SpinnerPracticeMode.Dormant : _phase;

    /// <summary>전환 대기의 남은 시간(초). 전환 중이 아니면 0.</summary>
    public float TransitionRemaining => _phase == SpinnerPracticeMode.Transition ? _transitionRemaining : 0f;

    /// <summary>서버 FSM 을 세워 둬야 하는가(휴면 · 전환 대기).</summary>
    public bool SuspendsServerLogic =>
        Mode == SpinnerPracticeMode.Dormant || Mode == SpinnerPracticeMode.Transition;

    /// <summary>넉백·CC 까지 막는 무적인가(휴면만). 연습 중에는 피격 반응이 살아 있다.</summary>
    public bool BlocksKnockbackAndCc => Mode == SpinnerPracticeMode.Dormant;

    /// <summary>체력이 줄지 않는가 = 체력 바 잠금 표시. 본 모드 전까지 전부.</summary>
    public bool BlocksHealthDamage => Mode != SpinnerPracticeMode.Real;

    /// <summary>
    /// 스피너봇의 공격 선택을 덮어쓰는가. 본 모드만 원래 선택(확률·쿨)이다.
    /// 휴면·전환 대기도 true 다 — FSM 정지가 걸리기 전 한 틱에 원래 공격이 새지 않게(그때는 창 스케줄이 거절한다).
    /// </summary>
    public bool OverridesAttack => Mode != SpinnerPracticeMode.Real;

    /// <summary>회전 공격(간파 창)을 지금 시작할 수 있는 모드인가.</summary>
    public bool AllowsSpin => Mode == SpinnerPracticeMode.Practice;

    /// <summary>스폰 반경 클램프를 거는가. 본 모드는 원래 스피너봇이라 걸지 않는다.</summary>
    public bool ClampsPosition => Mode != SpinnerPracticeMode.Real;

    public void SetDormant(bool dormant) => _dormant = dormant;

    /// <summary>연습 모드로 들어간다(휴면도 푼다). 본 모드·전환 대기에서 불러도 연습으로 되돌린다.</summary>
    public void EnterPractice()
    {
        _dormant = false;
        _phase = SpinnerPracticeMode.Practice;
        _transitionRemaining = 0f;
    }

    /// <summary>
    /// 본 모드 전환을 시작한다(휴면도 푼다). 이미 전환 대기·본 모드면 아무것도 하지 않는다 — 타이머를 다시 세지 않는다.
    /// </summary>
    public void EnterRealMode()
    {
        _dormant = false;
        if (_phase != SpinnerPracticeMode.Practice) return;

        if (_transitionSeconds <= 0f)
        {
            _phase = SpinnerPracticeMode.Real;
            return;
        }

        _phase = SpinnerPracticeMode.Transition;
        _transitionRemaining = _transitionSeconds;
    }

    /// <summary>시간을 흘린다. <b>이번 호출에서 본 모드에 들어갔으면 true</b>.</summary>
    public bool Tick(float deltaTime)
    {
        if (_dormant || _phase != SpinnerPracticeMode.Transition) return false;

        _transitionRemaining -= deltaTime > 0f ? deltaTime : 0f;
        if (_transitionRemaining > 0f) return false;

        _transitionRemaining = 0f;
        _phase = SpinnerPracticeMode.Real;
        return true;
    }
}
