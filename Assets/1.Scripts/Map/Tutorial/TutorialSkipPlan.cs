using System.Collections.Generic;

// 튜토리얼 구간 스킵(QA·테스트용, 할 일 34)의 순수 계획 모델(Unity·NGO 비의존 — EditMode 테스트 대상).
// 실행(서버)은 TutorialSkipController. 원본: Docs/tech/tutorial-director.md §10.5 · Docs/tech/tutorial-qa.md 0.4.

/// <summary>스킵 목적 구간 — 값 = 동선 순서(존1 0 → 보스방 9). 단축키 Alt+1~9 = 값 1~9, Alt+0 = 존1.</summary>
public enum TutorialSkipTarget : byte
{
    Zone1 = 0,
    Corridor1 = 1,
    Zone2 = 2,
    Corridor2 = 3,
    Zone3 = 4,
    Corridor3 = 5,
    Zone4 = 6,
    Corridor4 = 7,
    BossEnter = 8,
    /// <summary>보스방 — 보스 진입 존으로 옮긴 뒤 패드 이동 경로(<c>BossTeleportManager</c>)를 탄다.</summary>
    BossRoom = 9,
}

/// <summary>목적 구간에 가기 전에 할 일 하나 — 앞 게이트를 정식 경로로 여는 단위.</summary>
public enum TutorialSkipActionKind : byte
{
    /// <summary>G1 — 파티 전원에게 G1 개인 체크(<see cref="TutorialFlowModel.G1Mask"/>) 보고.</summary>
    CompleteZone1Checks,
    /// <summary>G2·G4·G7·G10 — 그 복도 특성 라운드를 전원 무작위 자동 획득 → 특성 체크.</summary>
    AutoChooseTraits,
    /// <summary>G3·G6·G9 — 그 존 몬스터를 깨워 전부 처치 → 존 장부 클리어가 게이트를 연다.</summary>
    ClearZone,
    /// <summary>G5 — 존3 스피너봇 연계 연습 완료(카운터 목표 + <c>RequestGateServer(G5)</c>).</summary>
    CompleteSpinnerPractice,
    /// <summary>G8 — 존4 보스 타이머 시작(<c>StartBossTimerServer</c>).</summary>
    StartBossTimer,
}

/// <summary>계획의 한 단계 — 무엇을 해서 어느 게이트가 열리기를 기다리는가.</summary>
public readonly struct TutorialSkipAction
{
    public readonly TutorialSkipActionKind Kind;
    /// <summary>이 단계로 열릴 게이트.</summary>
    public readonly TutorialGate Gate;
    /// <summary><see cref="TutorialSkipActionKind.AutoChooseTraits"/> 의 복도 번호(1~4), 아니면 0.</summary>
    public readonly int Corridor;
    /// <summary><see cref="TutorialSkipActionKind.ClearZone"/> 의 존, 아니면 None.</summary>
    public readonly TutorialArea Zone;

    public TutorialSkipAction(TutorialSkipActionKind kind, TutorialGate gate, int corridor = 0, TutorialArea zone = TutorialArea.None)
    {
        Kind = kind;
        Gate = gate;
        Corridor = corridor;
        Zone = zone;
    }

    public override string ToString() => Kind switch
    {
        TutorialSkipActionKind.AutoChooseTraits => $"{Gate}(복도{Corridor} 특성 자동 획득)",
        TutorialSkipActionKind.ClearZone => $"{Gate}({Zone} 처치)",
        _ => $"{Gate}({Kind})",
    };
}

/// <summary>스킵 요청 판정.</summary>
public enum TutorialSkipVerdict : byte
{
    Ok,
    /// <summary>지금보다 앞(뒤쪽) 구간 — 뒤로 가기는 지원하지 않는다.</summary>
    Backward,
    /// <summary>이미 보스방 도착(단계 Finished) — 튜토리얼 안내가 끝났다.</summary>
    Finished,
}

/// <summary>
/// 구간 스킵 계획 — "목적 구간 앞의 모든 단계를 정식 규칙대로 끝낸 것처럼" 만들 일의 목록.
///  · 게이트는 G1 → G10 순서로만 연다(디렉터 순서 규칙). 이미 열린 게이트의 일은 넣지 않는다.
///  · 목적 구간에 들어가는 데 필요한 마지막 게이트 = 그 구간의 입구 배리어 게이트(복도 n 입구 · 다음 존 = 복도 출구).
///    복도 목적이면 그 복도의 특성 라운드는 건드리지 않는다 — 도착한 뒤 중간 지점에서 정상으로 열린다.
///  · 보스방 = 보스 진입과 같은 계획 + 패드 이동.
/// </summary>
public static class TutorialSkipPlan
{
    public const int TargetCount = 10;

    /// <summary>보스방 스킵의 패드 이동 카운트다운(초) — 보스 진입 패드 기본값과 같다.</summary>
    public const float BossRoomCountdownSeconds = 3f;

    /// <summary>계획 단계마다 게이트가 열리기를 기다리는 최대 시간(실제 시간 초). 넘으면 강제 개방(경고).</summary>
    public const float GateWaitSeconds = 5f;

    /// <summary>숫자 키 → 목적 구간. 1~9 = 복도1~보스방, 0 = 존1. 범위 밖이면 false.</summary>
    public static bool TryFromDigit(int digit, out TutorialSkipTarget target)
    {
        target = TutorialSkipTarget.Zone1;
        if (digit < 0 || digit > 9) return false;
        target = (TutorialSkipTarget)digit;
        return true;
    }

    /// <summary>목적 구간 → 단축키 숫자(존1 = 0).</summary>
    public static int DigitOf(TutorialSkipTarget target) => (int)target;

    public static string NameOf(TutorialSkipTarget target) => target switch
    {
        TutorialSkipTarget.Zone1 => "존1",
        TutorialSkipTarget.Corridor1 => "복도1",
        TutorialSkipTarget.Zone2 => "존2",
        TutorialSkipTarget.Corridor2 => "복도2",
        TutorialSkipTarget.Zone3 => "존3",
        TutorialSkipTarget.Corridor3 => "복도3",
        TutorialSkipTarget.Zone4 => "존4",
        TutorialSkipTarget.Corridor4 => "복도4",
        TutorialSkipTarget.BossEnter => "보스 진입",
        _ => "보스방",
    };

    /// <summary>순간이동할 구간. 보스방은 보스 진입 존에 내린 뒤 패드 이동이 데려간다.</summary>
    public static TutorialArea AreaOf(TutorialSkipTarget target) => target switch
    {
        TutorialSkipTarget.Zone1 => TutorialArea.Zone1,
        TutorialSkipTarget.Corridor1 => TutorialArea.Corridor1,
        TutorialSkipTarget.Zone2 => TutorialArea.Zone2,
        TutorialSkipTarget.Corridor2 => TutorialArea.Corridor2,
        TutorialSkipTarget.Zone3 => TutorialArea.Zone3,
        TutorialSkipTarget.Corridor3 => TutorialArea.Corridor3,
        TutorialSkipTarget.Zone4 => TutorialArea.Zone4,
        TutorialSkipTarget.Corridor4 => TutorialArea.Corridor4,
        _ => TutorialArea.BossEnter,
    };

    /// <summary>디렉터 단계 — 존은 첫 진입 단계(존3 연습 · 존4 설정 자막). 보스방은 BossEnter(도착이 Finished 로 바꾼다).</summary>
    public static TutorialStep StepOf(TutorialSkipTarget target) => target switch
    {
        TutorialSkipTarget.Zone1 => TutorialStep.Zone1,
        TutorialSkipTarget.Corridor1 => TutorialStep.Corridor1,
        TutorialSkipTarget.Zone2 => TutorialStep.Zone2,
        TutorialSkipTarget.Corridor2 => TutorialStep.Corridor2,
        TutorialSkipTarget.Zone3 => TutorialStep.Zone3Practice,
        TutorialSkipTarget.Corridor3 => TutorialStep.Corridor3,
        TutorialSkipTarget.Zone4 => TutorialStep.Zone4Lore,
        TutorialSkipTarget.Corridor4 => TutorialStep.Corridor4,
        _ => TutorialStep.BossEnter,
    };

    /// <summary>지금 단계 → 지금 구간(뒤로 가기 판정 기준). None(활성 전) = 존1, Finished = 보스방.</summary>
    public static TutorialSkipTarget CurrentOf(TutorialStep step) => step switch
    {
        TutorialStep.Corridor1 => TutorialSkipTarget.Corridor1,
        TutorialStep.Zone2 => TutorialSkipTarget.Zone2,
        TutorialStep.Corridor2 => TutorialSkipTarget.Corridor2,
        TutorialStep.Zone3Practice or TutorialStep.Zone3Fight => TutorialSkipTarget.Zone3,
        TutorialStep.Corridor3 => TutorialSkipTarget.Corridor3,
        TutorialStep.Zone4Lore or TutorialStep.Zone4Combat => TutorialSkipTarget.Zone4,
        TutorialStep.Corridor4 => TutorialSkipTarget.Corridor4,
        TutorialStep.BossEnter => TutorialSkipTarget.BossEnter,
        TutorialStep.Finished => TutorialSkipTarget.BossRoom,
        _ => TutorialSkipTarget.Zone1,
    };

    /// <summary>
    /// 스킵을 받아들이는가. 뒤로 가기(목적 &lt; 지금)는 거부, 이미 Finished 면 거부.
    /// 지금과 같은 구간은 허용 — 열 게이트 없이 그 구간 입구 안쪽으로 다시 옮기기만 한다(Alt+0 은 존1 에서만 이 경우).
    /// </summary>
    public static TutorialSkipVerdict Check(TutorialStep currentStep, TutorialSkipTarget target)
    {
        if (currentStep == TutorialStep.Finished) return TutorialSkipVerdict.Finished;
        return target < CurrentOf(currentStep) ? TutorialSkipVerdict.Backward : TutorialSkipVerdict.Ok;
    }

    /// <summary>목적 구간에 들어가려면 열려 있어야 하는 마지막 게이트. 존1 은 없다(false).</summary>
    public static bool TryGetRequiredGate(TutorialSkipTarget target, out TutorialGate gate)
    {
        gate = default;
        switch (target)
        {
            case TutorialSkipTarget.Corridor1: gate = TutorialGate.G1_Corridor1Entry; return true;
            case TutorialSkipTarget.Zone2: gate = TutorialGate.G2_Zone2Entry; return true;
            case TutorialSkipTarget.Corridor2: gate = TutorialGate.G3_Corridor2Entry; return true;
            case TutorialSkipTarget.Zone3: gate = TutorialGate.G4_Zone3Entry; return true;
            case TutorialSkipTarget.Corridor3: gate = TutorialGate.G6_Corridor3Entry; return true;
            case TutorialSkipTarget.Zone4: gate = TutorialGate.G7_Zone4Entry; return true;
            case TutorialSkipTarget.Corridor4: gate = TutorialGate.G9_Corridor4Entry; return true;
            case TutorialSkipTarget.BossEnter:
            case TutorialSkipTarget.BossRoom: gate = TutorialGate.G10_BossEnterAndPad; return true;
            default: return false;
        }
    }

    /// <summary>게이트 하나를 정식 경로로 여는 일. G11(보스방 도착)은 계획에 없다 — 도착이 연다.</summary>
    public static TutorialSkipAction ActionFor(TutorialGate gate) => gate switch
    {
        TutorialGate.G1_Corridor1Entry => new TutorialSkipAction(TutorialSkipActionKind.CompleteZone1Checks, gate),
        TutorialGate.G2_Zone2Entry => new TutorialSkipAction(TutorialSkipActionKind.AutoChooseTraits, gate, corridor: 1),
        TutorialGate.G3_Corridor2Entry => new TutorialSkipAction(TutorialSkipActionKind.ClearZone, gate, zone: TutorialArea.Zone2),
        TutorialGate.G4_Zone3Entry => new TutorialSkipAction(TutorialSkipActionKind.AutoChooseTraits, gate, corridor: 2),
        TutorialGate.G5_SpinnerRealMode => new TutorialSkipAction(TutorialSkipActionKind.CompleteSpinnerPractice, gate),
        TutorialGate.G6_Corridor3Entry => new TutorialSkipAction(TutorialSkipActionKind.ClearZone, gate, zone: TutorialArea.Zone3),
        TutorialGate.G7_Zone4Entry => new TutorialSkipAction(TutorialSkipActionKind.AutoChooseTraits, gate, corridor: 3),
        TutorialGate.G8_TimerStart => new TutorialSkipAction(TutorialSkipActionKind.StartBossTimer, gate),
        TutorialGate.G9_Corridor4Entry => new TutorialSkipAction(TutorialSkipActionKind.ClearZone, gate, zone: TutorialArea.Zone4),
        _ => new TutorialSkipAction(TutorialSkipActionKind.AutoChooseTraits, gate, corridor: 4),
    };

    /// <summary>
    /// 계획 — 목적 구간까지 아직 닫힌 게이트를 G1 부터 순서대로 여는 일 목록(<paramref name="into"/> 를 비우고 채운다).
    /// <paramref name="openedGateMask"/> = 디렉터 열림 마스크(bit = 1 &lt;&lt; (int)TutorialGate). 이미 열린 게이트는 건너뛴다.
    /// </summary>
    public static void Build(TutorialSkipTarget target, int openedGateMask, List<TutorialSkipAction> into)
    {
        into.Clear();
        if (!TryGetRequiredGate(target, out TutorialGate last)) return;
        for (int i = 0; i <= (int)last; i++)
        {
            if ((openedGateMask & (1 << i)) != 0) continue;
            into.Add(ActionFor((TutorialGate)i));
        }
    }

    /// <summary>계획 중 특성 자동 획득 복도 번호(순서대로).</summary>
    public static void AutoChosenCorridors(IReadOnlyList<TutorialSkipAction> plan, List<int> into)
    {
        into.Clear();
        foreach (TutorialSkipAction a in plan)
            if (a.Kind == TutorialSkipActionKind.AutoChooseTraits) into.Add(a.Corridor);
    }

    /// <summary>게이트 마스크 → "G1·G2" 같은 짧은 이름(로그용). 비면 "없음".</summary>
    public static string DescribeGates(int mask)
    {
        var parts = new List<string>();
        for (int i = 0; i < TutorialFlowModel.GateCount; i++)
            if ((mask & (1 << i)) != 0) parts.Add($"G{i + 1}");
        return parts.Count > 0 ? string.Join("·", parts) : "없음";
    }
}
