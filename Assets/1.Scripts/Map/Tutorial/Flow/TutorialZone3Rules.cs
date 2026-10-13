using System;

// 튜토리얼 존3(스피너봇 간파 연습) 흐름의 순수 규칙(Unity·NGO 비의존 — EditMode 테스트 대상).
// 원본: Docs/design/tutorial-scenario.md §3.5(Z3-1~Z3-8)·§4 G4~G6 · PLAN-tutorial.md S9 · Docs/tech/tutorial-zone3.md.
// 배선(서버)은 TutorialZone3Flow, 표시(모든 피어)는 TutorialZone3Guide.

/// <summary>존3 진행 단계(서버 흐름 기준).</summary>
public enum TutorialZone3Phase : byte
{
    /// <summary>G4 열림 + 파티 첫 진입 대기 — 스피너봇 휴면.</summary>
    Waiting,
    /// <summary>연습 모드 — 간파 파티 합계 n/3.</summary>
    Practice,
    /// <summary>G5 뒤 본 모드 — 처치 대기.</summary>
    Real,
    /// <summary>스피너봇 처치(존3 클리어).</summary>
    Cleared,
}

/// <summary>간파 판정 1회에 대한 개인 피드백 자막.</summary>
public enum TutorialZone3Feedback : byte
{
    None,
    /// <summary>아무것도 못 맞혔다 — <c>TUT_Z3_MISS</c>.</summary>
    Miss,
    /// <summary>노랑(간파 창)이 아닐 때 스피너봇을 맞혔다 — <c>TUT_Z3_EARLY</c>.</summary>
    Early,
}

/// <summary>
/// 존3 흐름 상태 기계. 시작 = G4 열림 ∧ 파티 첫 진입(순서 무관) · 간파 성공은 연습 중에만 센다 · 첫 성공만 자막 ·
/// 연속 놓침은 <see cref="HintEvery"/> 배수마다 힌트 · G5 → 본 모드 · 처치 → 클리어. 같은 신호 반복은 무시한다.
/// </summary>
public sealed class TutorialZone3Model
{
    public const int DefaultHintEvery = 4;

    private bool _entryGateOpen;
    private bool _partyEntered;

    public TutorialZone3Model(int hintEvery = DefaultHintEvery)
    {
        HintEvery = Math.Max(1, hintEvery);
    }

    public int HintEvery { get; }
    public TutorialZone3Phase Phase { get; private set; } = TutorialZone3Phase.Waiting;
    public bool FirstSuccessShown { get; private set; }

    /// <summary>G4(존3 입구) 열림. 연습을 지금 시작해야 하면 true.</summary>
    public bool NotifyEntryGateOpen()
    {
        _entryGateOpen = true;
        return TryStartPractice();
    }

    /// <summary>파티 첫 존3 진입. 연습을 지금 시작해야 하면 true.</summary>
    public bool NotifyPartyEntered()
    {
        _partyEntered = true;
        return TryStartPractice();
    }

    private bool TryStartPractice()
    {
        if (Phase != TutorialZone3Phase.Waiting || !_entryGateOpen || !_partyEntered) return false;
        Phase = TutorialZone3Phase.Practice;
        return true;
    }

    /// <summary>
    /// 튜토리얼 스피너봇 간파 성공. 집계(Zone3Interrupts +1)해야 하면 true — 연습 중에만.
    /// <paramref name="showFirstSuccess"/> = 판 전체 첫 성공(<c>TUT_Z3_FIRST_SUCCESS</c>).
    /// </summary>
    public bool NotifyCounterSucceeded(out bool showFirstSuccess)
    {
        showFirstSuccess = false;
        if (Phase != TutorialZone3Phase.Practice) return false;
        if (!FirstSuccessShown)
        {
            FirstSuccessShown = true;
            showFirstSuccess = true;
        }
        return true;
    }

    /// <summary>
    /// G5 열림 → 본 모드. 바뀌었으면 true. 연습 시작 전(디버그로 G5 를 연 경우)이어도 본 모드로 간다 —
    /// 그때는 호출자가 휴면부터 풀어야 한다(<paramref name="skippedPractice"/>).
    /// </summary>
    public bool NotifyRealModeGate(out bool skippedPractice)
    {
        skippedPractice = Phase == TutorialZone3Phase.Waiting;
        if (Phase != TutorialZone3Phase.Waiting && Phase != TutorialZone3Phase.Practice) return false;
        Phase = TutorialZone3Phase.Real;
        return true;
    }

    /// <summary>스피너봇 처치(존3 클리어). 처음이면 true.</summary>
    public bool NotifyCleared()
    {
        if (Phase == TutorialZone3Phase.Cleared) return false;
        Phase = TutorialZone3Phase.Cleared;
        return true;
    }

    /// <summary>
    /// 간파 판정 1회(<c>PlayerInterruptSkillBase.ServerInterruptResolved</c>)의 개인 피드백. 연습 중 존3 안 시전만 본다.
    /// <list type="bullet">
    /// <item>적중 0 → <see cref="TutorialZone3Feedback.Miss"/>.</item>
    /// <item>적중 &gt; 0 인데 이번 판정에서 스피너봇 간파 성공이 없고, 간파 창이 닫혀 있고, 시전자가 스피너봇 곁 → <see cref="TutorialZone3Feedback.Early"/>
    ///   (적중 대상 목록이 이벤트에 없어 '곁'으로 스피너봇 적중을 추정 — 존3 의 수신자는 스피너봇뿐이라는 전제).</item>
    /// </list>
    /// </summary>
    public TutorialZone3Feedback ClassifyInterrupt(int hitCount, bool casterInZone3, bool succeededThisResolution,
                                                   bool windowOpen, bool casterNearSpinner)
    {
        if (Phase != TutorialZone3Phase.Practice || !casterInZone3) return TutorialZone3Feedback.None;
        if (hitCount <= 0) return TutorialZone3Feedback.Miss;
        if (succeededThisResolution || windowOpen || !casterNearSpinner) return TutorialZone3Feedback.None;
        return TutorialZone3Feedback.Early;
    }

    /// <summary>연속 놓침(이번 포함) 알림 — 연습 중 <see cref="HintEvery"/> 의 배수(4·8·…)마다 힌트.</summary>
    public bool ShouldShowHint(int consecutiveMisses) =>
        Phase == TutorialZone3Phase.Practice && consecutiveMisses > 0 && consecutiveMisses % HintEvery == 0;
}

/// <summary>
/// 존3 목표 패널·강조 — 복제 상태(단계·G6)만으로 정한다. 그래서 늦게 붙은 클라도 <c>StateChanged</c> 한 번으로 같은 화면이 된다.
/// </summary>
public static class TutorialZone3Display
{
    /// <summary>지금 띄울 존3 목표 id. 존3 단계가 아니거나 G6(복도3 입구)가 열렸으면 null.</summary>
    public static string ObjectiveFor(TutorialStep step, bool corridor3EntryOpen)
    {
        if (corridor3EntryOpen) return null;
        switch (step)
        {
            case TutorialStep.Zone3Practice: return TutorialGuideIds.Z3Goal;
            case TutorialStep.Zone3Fight: return TutorialGuideIds.Z3Boss;
            default: return null;
        }
    }

    /// <summary>스피너봇·우클릭 칸 강조 — 연습 중에만(Z3-1, 본 모드 전환 G5 에서 끈다).</summary>
    public static bool HighlightsFor(TutorialStep step, bool corridor3EntryOpen) =>
        step == TutorialStep.Zone3Practice && !corridor3EntryOpen;

    /// <summary>이 목표 id 가 존3 흐름이 띄운 것인가 — 존3 을 벗어날 때 남의 목표는 지우지 않는다.</summary>
    public static bool IsZone3Objective(string id) =>
        id == TutorialGuideIds.Z3Goal || id == TutorialGuideIds.Z3Boss;
}

/// <summary>존3 지오메트리 계산(순수).</summary>
public static class TutorialZone3Geometry
{
    /// <summary>스피너봇 몸 반경에 더 두는 여유(m) — 원격 위치 보간·클램프 한 프레임 늦음.</summary>
    public const float DefaultMargin = 0.5f;

    /// <summary>점에서 (회전된) 사각형까지 XZ 거리. 안이면 0.</summary>
    public static float DistanceToRect(float x, float z, TutorialRect rect)
    {
        rect.ToLocal(x, z, out float lx, out float lz);
        float ox = Math.Max(0f, Math.Abs(lx) - rect.HalfX);
        float oz = Math.Max(0f, Math.Abs(lz) - rect.HalfZ);
        return (float)Math.Sqrt(ox * ox + oz * oz);
    }

    /// <summary>
    /// 연습 반경 = min(인스펙터 최대, 기준점 → 출구 배리어(G6) 박스 거리 − 몸 반경 − 여유), 0 미만이면 0(기준점 고정 —
    /// <see cref="SpinnerPracticeArea"/> 의 '0 = 고정' 규칙). 몸 가장자리가 배리어 면에 닿기 전에 멈춘다.
    /// </summary>
    public static float ComputePracticeRadius(float anchorX, float anchorZ, TutorialRect exitBarrier,
                                              float bodyRadius, float margin, float maxRadius)
    {
        float d = DistanceToRect(anchorX, anchorZ, exitBarrier) - Math.Max(0f, bodyRadius) - Math.Max(0f, margin);
        return Math.Max(0f, Math.Min(Math.Max(0f, maxRadius), d));
    }
}
