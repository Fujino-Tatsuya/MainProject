using System;

/// <summary>
/// 특성 복도 1~4 의 순수 규칙(S8) — 게이트·체크·창 표시 매핑, 라운드 1회 규칙, 목표 패널 장면.
/// 원본: Docs/design/tutorial-scenario.md §3.2·§3.4·§3.6·§3.8·§4 · Docs/tech/tutorial-corridors-zone2.md.
/// </summary>
public static class TutorialCorridorRules
{
    public const int CorridorCount = 4;

    public static bool IsValid(int corridor) => corridor >= 1 && corridor <= CorridorCount;

    /// <summary>복도 입구(앞 존 쪽) 게이트 — G1·G3·G6·G9.</summary>
    public static TutorialGate EntryGate(int corridor) => corridor switch
    {
        1 => TutorialGate.G1_Corridor1Entry,
        2 => TutorialGate.G3_Corridor2Entry,
        3 => TutorialGate.G6_Corridor3Entry,
        _ => TutorialGate.G9_Corridor4Entry,
    };

    /// <summary>복도 출구(다음 존 쪽) 게이트 = 특성 전원 선택 개인 게이트 — G2·G4·G7·G10.</summary>
    public static TutorialGate ExitGate(int corridor) => corridor switch
    {
        1 => TutorialGate.G2_Zone2Entry,
        2 => TutorialGate.G4_Zone3Entry,
        3 => TutorialGate.G7_Zone4Entry,
        _ => TutorialGate.G10_BossEnterAndPad,
    };

    public static TutorialCheck CheckOf(int corridor) => (TutorialCheck)((int)TutorialCheck.TraitCorridor1 + Math.Max(0, corridor - 1));

    public static TutorialStep StepOf(int corridor) => corridor switch
    {
        1 => TutorialStep.Corridor1,
        2 => TutorialStep.Corridor2,
        3 => TutorialStep.Corridor3,
        _ => TutorialStep.Corridor4,
    };

    /// <summary>창 표시 옵션 — 복도1 = 상단 설명, 복도4 = 아래 한 줄(게이지 계속), 2·3 = 없음(D7).</summary>
    public static TutorialTraitRoundDisplay DisplayOf(int corridor) => corridor switch
    {
        1 => TutorialTraitRoundDisplay.Intro,
        4 => TutorialTraitRoundDisplay.LastNotice,
        _ => TutorialTraitRoundDisplay.None,
    };

    /// <summary>그 게이트가 복도 입구면 복도 번호, 아니면 0.</summary>
    public static int CorridorOfEntryGate(TutorialGate gate)
    {
        for (int c = 1; c <= CorridorCount; c++)
            if (EntryGate(c) == gate) return c;
        return 0;
    }

    /// <summary>그 게이트가 복도 출구면 복도 번호, 아니면 0.</summary>
    public static int CorridorOfExitGate(TutorialGate gate)
    {
        for (int c = 1; c <= CorridorCount; c++)
            if (ExitGate(c) == gate) return c;
        return 0;
    }

    /// <summary>지금 진행 중인 복도 = 입구가 열리고 출구가 아직 닫힌 복도(게이트는 순서대로만 열려 많아야 하나). 없으면 0.</summary>
    public static int ActiveCorridor(Func<TutorialGate, bool> isGateOpen)
    {
        if (isGateOpen == null) return 0;
        for (int c = CorridorCount; c >= 1; c--)
            if (isGateOpen(EntryGate(c)) && !isGateOpen(ExitGate(c))) return c;
        return 0;
    }
}

/// <summary>복도 중간 지점 도착 때 라운드를 여는가.</summary>
public enum TutorialCorridorRoundDecision : byte
{
    /// <summary>이 복도 라운드를 지금 연다(전원에게 제시).</summary>
    Start,
    /// <summary>이미 열었다(또는 건너뛰기로 처리했다) — 라운드당 1번.</summary>
    AlreadyHandled,
    /// <summary>출구 게이트가 이미 열렸다 — 낙오자에게 창을 띄우지 않는다(§3.2).</summary>
    GateAlreadyOpen,
    /// <summary>보스 타이머 만료 — 강제 이동이 진행 중이라 새 창을 띄우지 않는다(§3.8).</summary>
    TimerExpired,
    Invalid,
}

/// <summary>
/// [서버] 복도별 특성 라운드 장부 — <b>복도당 1번만</b> 연다. 라운드 번호(특성 디렉터) → 복도 역매핑.
/// 건너뛴 복도(게이트가 이미 열림·만료·디렉터 없음)도 처리됨으로 기록해 다시 시도하지 않는다.
/// </summary>
public sealed class TutorialCorridorRounds
{
    private readonly bool[] _handled = new bool[TutorialCorridorRules.CorridorCount + 1];
    private readonly int[] _roundOf = new int[TutorialCorridorRules.CorridorCount + 1];

    public TutorialCorridorRoundDecision Decide(int corridor, bool exitGateOpen, bool timerExpired)
    {
        if (!TutorialCorridorRules.IsValid(corridor)) return TutorialCorridorRoundDecision.Invalid;
        if (_handled[corridor]) return TutorialCorridorRoundDecision.AlreadyHandled;
        if (exitGateOpen) return TutorialCorridorRoundDecision.GateAlreadyOpen;
        if (timerExpired) return TutorialCorridorRoundDecision.TimerExpired;
        return TutorialCorridorRoundDecision.Start;
    }

    /// <summary>처리됨으로 기록. <paramref name="round"/> = 특성 디렉터 라운드 번호(제시 못 했으면 0).</summary>
    public void MarkHandled(int corridor, int round)
    {
        if (!TutorialCorridorRules.IsValid(corridor)) return;
        _handled[corridor] = true;
        _roundOf[corridor] = Math.Max(0, round);
    }

    public bool IsHandled(int corridor) => TutorialCorridorRules.IsValid(corridor) && _handled[corridor];

    /// <summary>그 복도의 특성 라운드 번호(0 = 없음).</summary>
    public int RoundOf(int corridor) => TutorialCorridorRules.IsValid(corridor) ? _roundOf[corridor] : 0;

    /// <summary>특성 라운드 번호 → 복도(0 = 이 장부가 연 라운드가 아님).</summary>
    public int CorridorOfRound(int round)
    {
        if (round <= 0) return 0;
        for (int c = 1; c <= TutorialCorridorRules.CorridorCount; c++)
            if (_roundOf[c] == round) return c;
        return 0;
    }
}

/// <summary>복도 목표 패널에서 지금 보여 줄 것.</summary>
public enum TutorialCorridorTask : byte
{
    /// <summary>이 흐름이 목표를 쓰지 않는다(복도 밖 · 아직 라운드 전 — 앞 존 흐름의 "복도로" 유지).</summary>
    None,
    /// <summary>창이 떠 있다 — 특성 고르기.</summary>
    Pick,
    /// <summary>골랐다 — 동료 대기(대기 줄은 따로).</summary>
    Wait,
}

/// <summary>복도 목표 패널 한 장면.</summary>
public readonly struct TutorialCorridorView
{
    public TutorialCorridorView(int corridor, TutorialCorridorTask task, bool showWait)
    {
        Corridor = corridor;
        Task = task;
        ShowWait = showWait;
    }

    public int Corridor { get; }
    public TutorialCorridorTask Task { get; }
    public bool ShowWait { get; }
    public bool OwnsObjective => Task != TutorialCorridorTask.None;
}

public static class TutorialCorridorProgress
{
    /// <summary>
    /// 복도 목표 장면. <paramref name="activeCorridor"/> = <see cref="TutorialCorridorRules.ActiveCorridor"/>.
    /// 창이 떠 있으면(제시 행 있음) 고르기, 이 복도 체크가 켜졌으면 대기(+ 지금 열 차례인 게이트가 이 복도 출구면 대기 줄).
    /// 둘 다 아니면(중간 지점 전·라운드 전) 이 흐름은 목표를 건드리지 않는다.
    /// </summary>
    public static TutorialCorridorView Resolve(int activeCorridor, bool hasPendingChoice, TutorialPlayerProgress p,
        bool hasWait, TutorialGate waitGate)
    {
        if (!TutorialCorridorRules.IsValid(activeCorridor))
            return new TutorialCorridorView(0, TutorialCorridorTask.None, false);

        if (p.Has(TutorialCorridorRules.CheckOf(activeCorridor)))
        {
            bool wait = hasWait && waitGate == TutorialCorridorRules.ExitGate(activeCorridor);
            return new TutorialCorridorView(activeCorridor, TutorialCorridorTask.Wait, wait);
        }

        return hasPendingChoice
            ? new TutorialCorridorView(activeCorridor, TutorialCorridorTask.Pick, false)
            : new TutorialCorridorView(activeCorridor, TutorialCorridorTask.None, false);
    }
}
