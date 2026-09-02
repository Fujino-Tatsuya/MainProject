using UnityEngine;

/// <summary>
/// 카운터 가능 공격의 <b>선딜 게이트</b>. 공격이 실제로 나가는 시점을 두 사건의 논리곱으로 잡는다 —
/// ① 카운터 창 타이머 만료, ② 애니메이션이 공격 준비 자세에 도달(<c>OnAttackHit</c> 이벤트).
///
/// 왜 둘 다 필요한가 —
/// <list type="bullet">
/// <item>타이머만 보면: 클립 이벤트가 아직 안 왔는데 공격이 나가 <b>보이지 않는 판정</b>이 생긴다.
///       (Dash 이벤트는 정규화 시간 약 0.15 로 의도한 창 1.5초보다 훨씬 이르다.)</item>
/// <item>이벤트만 보면: 창 길이가 데이터로 조절되지 않는다. 지금이 그 상태다.</item>
/// </list>
///
/// 서버 전용 판정이며 순수 C# 이다(MonoBehaviour 아님) — EditMode 에서 순서 조합을 전부 고정하기 위해서.
/// 애니메이터 정지·복구와 실제 공격 실행은 이 클래스가 하지 않는다. 호출측(<c>TwentyThreeBoss</c>)의 몫이다.
/// </summary>
public sealed class BossCounterWindupGate
{
    float _remaining;

    /// <summary>게이트가 열려 대기 중인가. <c>Begin</c> 과 <c>Reset</c> 사이에서 true.</summary>
    public bool IsActive { get; private set; }

    /// <summary>애니메이션이 공격 준비 지점에 도달했는가(래치 — 중복 도착해도 한 번만).</summary>
    public bool IsAnimationReady { get; private set; }

    /// <summary>카운터 창 타이머가 끝났는가.</summary>
    public bool IsTimerElapsed { get; private set; }

    /// <summary>
    /// 타이머가 애니메이션 준비보다 <b>먼저</b> 끝났는가 = 데이터가 클립 이벤트보다 짧은 비정상 조합.
    /// 호출측은 이걸 보고 진단 경고를 남긴다(동작은 막지 않는다 — 이벤트를 기다렸다가 즉시 발사).
    /// </summary>
    public bool TimerElapsedBeforeAnimationReady { get; private set; }

    /// <summary>지금 공격을 내보내도 되는가.</summary>
    public bool ShouldRelease => IsActive && IsAnimationReady && IsTimerElapsed;

    /// <param name="duration">이 공격의 카운터 창 길이(초).</param>
    public void Begin(float duration)
    {
        Reset();
        IsActive = true;
        _remaining = Mathf.Max(0f, duration);
        IsTimerElapsed = _remaining <= 0f;

        // 🔴 길이가 0 이면 Tick 을 한 번도 안 거치고 즉시 만료다. 순서 플래그를 Tick 에서만 세우면
        //    이 경로에서 조용히 false 로 남아, 같은 상황(타이머가 먼저 끝남)인데 진단이 안 뜬다.
        //    인스펙터 범위가 0~2 라 0 은 저작 가능한 값이므로 실제로 밟을 수 있다.
        //    Reset 직후라 IsAnimationReady 는 항상 false — 만료됐다면 곧 "먼저 끝난 것"이다.
        if (IsTimerElapsed) TimerElapsedBeforeAnimationReady = true;
    }

    /// <param name="deltaTime">서버 틱 간격. 음수는 0 으로 눌러 시간이 되감기지 않게 한다.</param>
    public void Tick(float deltaTime)
    {
        if (!IsActive || IsTimerElapsed) return;

        _remaining = Mathf.Max(0f, _remaining - Mathf.Max(0f, deltaTime));
        IsTimerElapsed = _remaining <= 0f;

        if (IsTimerElapsed && !IsAnimationReady)
            TimerElapsedBeforeAnimationReady = true;
    }

    /// <summary>애니메이션 준비 지점 도달. 게이트가 닫혀 있으면 무시한다(늦게 온 이벤트).</summary>
    public void MarkAnimationReady()
    {
        if (IsActive) IsAnimationReady = true;
    }

    /// <summary>
    /// 게이트를 비운다. 카운터 성공·보스 사망·공격 상태 강제 이탈·체인 타임아웃·디스폰에서 호출한다.
    /// 멱등이어야 한다 — 이미 비어 있어도 부작용이 없다.
    /// </summary>
    public void Reset()
    {
        _remaining = 0f;
        IsActive = false;
        IsAnimationReady = false;
        IsTimerElapsed = false;
        TimerElapsedBeforeAnimationReady = false;
    }
}
