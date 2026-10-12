/// <summary>
/// 튜토리얼 스피너봇의 모드. 전 피어에 복제된다(<see cref="TutorialSpinnerPractice.Mode"/>).
/// 휴면은 모드가 아니다 — 모든 몬스터 공통 <see cref="MonsterBase.SetDormantServer"/> 하나로 건다(handoff-tutorial.md §2).
/// </summary>
public enum SpinnerPracticeMode : byte
{
    /// <summary>연습 — 죽지 않고, 9초마다 간파 창을 여는 회전 공격만 한다. 돌진은 피해 없이 밀쳐내기만.</summary>
    Practice = 1,
    /// <summary>본 모드 전환 대기 — 잠깐 정지(기본 2초). 아직 체력은 잠겨 있다.</summary>
    Transition = 2,
    /// <summary>본 모드 — 원래 스피너봇 그대로(모든 공격·피해·체력 감소·창 40%·쿨 6초).</summary>
    Real = 3,
}

/// <summary>
/// 튜토리얼 스피너봇 모드 상태 기계(순수 C# — EditMode 로 고정). 판정에 쓰는 질의는 전부 여기서 나온다.
/// 연습으로 시작한다. 휴면(base 휴면) 중에는 호출 측이 <see cref="Tick"/> 을 부르지 않아 전환 대기 타이머가 흐르지 않는다.
/// </summary>
public sealed class SpinnerPracticeModeModel
{
    readonly float _transitionSeconds;

    SpinnerPracticeMode _mode = SpinnerPracticeMode.Practice;
    float _transitionRemaining;

    /// <param name="transitionSeconds">본 모드 전환 전 정지 시간(초). 0 이하면 즉시 본 모드.</param>
    public SpinnerPracticeModeModel(float transitionSeconds)
    {
        _transitionSeconds = transitionSeconds > 0f ? transitionSeconds : 0f;
    }

    public SpinnerPracticeMode Mode => _mode;

    /// <summary>전환 대기의 남은 시간(초). 전환 중이 아니면 0.</summary>
    public float TransitionRemaining => _mode == SpinnerPracticeMode.Transition ? _transitionRemaining : 0f;

    /// <summary>서버 FSM 을 세워 둬야 하는가(전환 대기).</summary>
    public bool SuspendsServerLogic => _mode == SpinnerPracticeMode.Transition;

    /// <summary>체력이 줄지 않는가 = 체력 바 잠금 표시. 본 모드 전까지 전부.</summary>
    public bool BlocksHealthDamage => _mode != SpinnerPracticeMode.Real;

    /// <summary>
    /// 스피너봇의 공격 선택을 덮어쓰는가. 본 모드만 원래 선택(확률·쿨)이다.
    /// 전환 대기도 true 다 — FSM 정지가 걸리기 전 한 틱에 원래 공격이 새지 않게(그때는 <see cref="AllowsSpin"/> 가 거절한다).
    /// </summary>
    public bool OverridesAttack => _mode != SpinnerPracticeMode.Real;

    /// <summary>회전 공격(간파 창)을 지금 시작할 수 있는 모드인가.</summary>
    public bool AllowsSpin => _mode == SpinnerPracticeMode.Practice;

    /// <summary>스폰 반경 클램프를 거는가. 본 모드는 원래 스피너봇이라 걸지 않는다.</summary>
    public bool ClampsPosition => _mode != SpinnerPracticeMode.Real;

    /// <summary>연습 모드로 들어간다. 본 모드·전환 대기에서 불러도 연습으로 되돌린다.</summary>
    public void EnterPractice()
    {
        _mode = SpinnerPracticeMode.Practice;
        _transitionRemaining = 0f;
    }

    /// <summary>본 모드 전환을 시작한다. 이미 전환 대기·본 모드면 아무것도 하지 않는다 — 타이머를 다시 세지 않는다.</summary>
    public void EnterRealMode()
    {
        if (_mode != SpinnerPracticeMode.Practice) return;

        if (_transitionSeconds <= 0f)
        {
            _mode = SpinnerPracticeMode.Real;
            return;
        }

        _mode = SpinnerPracticeMode.Transition;
        _transitionRemaining = _transitionSeconds;
    }

    /// <summary>시간을 흘린다. <b>이번 호출에서 본 모드에 들어갔으면 true</b>.</summary>
    public bool Tick(float deltaTime)
    {
        if (_mode != SpinnerPracticeMode.Transition) return false;

        _transitionRemaining -= deltaTime > 0f ? deltaTime : 0f;
        if (_transitionRemaining > 0f) return false;

        _transitionRemaining = 0f;
        _mode = SpinnerPracticeMode.Real;
        return true;
    }
}
