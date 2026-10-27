using System.Collections.Generic;

// 튜토리얼 구간 스킵(QA·테스트용, 할 일 34)의 순수 계획 모델(Unity·NGO 비의존 — EditMode 테스트 대상).
// 실행(서버)은 MapSceneSkipController. 원본: Docs/tech/tutorial-director.md §10.5 · Docs/tech/tutorial-qa.md 0.4.

/// <summary>
/// 스킵 목적 구간 — 값 = 동선 순서(존1 0 → 보스방 9). 단축키 Alt+1~9 = 값 1~9, Alt+0 = <see cref="Restart"/>.
/// 존1 은 단축키가 없다 — 지금 구간(뒤로 가기 판정) 기준으로만 쓴다.
/// </summary>
public enum MapSceneSkipTarget : byte
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
    /// <summary>
    /// Alt+0 — 처음부터(판 재시작). 구간이 아니라 동작이다: 맵 씬을 로딩 씬을 거쳐 다시 불러와
    /// 처음 로드한 것처럼 전부 초기화한다(<c>NetworkLoadingFlowController.TryRestartTargetScene</c>). 어느 구간에서든 허용.
    /// </summary>
    Restart = 10,
}

/// <summary>목적 구간에 가기 전에 할 일 하나 — 앞 게이트를 정식 경로로 여는 단위.</summary>
public enum MapSceneSkipActionKind : byte
{
    /// <summary>G1 — 파티 전원에게 G1 개인 체크(<see cref="MapSceneFlowModel.G1Mask"/>) 보고.</summary>
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
public readonly struct MapSceneSkipAction
{
    public readonly MapSceneSkipActionKind Kind;
    /// <summary>이 단계로 열릴 게이트.</summary>
    public readonly MapSceneGate Gate;
    /// <summary><see cref="MapSceneSkipActionKind.AutoChooseTraits"/> 의 복도 번호(1~4), 아니면 0.</summary>
    public readonly int Corridor;
    /// <summary><see cref="MapSceneSkipActionKind.ClearZone"/> 의 존, 아니면 None.</summary>
    public readonly MapSceneArea Zone;

    public MapSceneSkipAction(MapSceneSkipActionKind kind, MapSceneGate gate, int corridor = 0, MapSceneArea zone = MapSceneArea.None)
    {
        Kind = kind;
        Gate = gate;
        Corridor = corridor;
        Zone = zone;
    }

    public override string ToString() => Kind switch
    {
        MapSceneSkipActionKind.AutoChooseTraits => $"{Gate}(복도{Corridor} 특성 자동 획득)",
        MapSceneSkipActionKind.ClearZone => $"{Gate}({Zone} 처치)",
        _ => $"{Gate}({Kind})",
    };
}

/// <summary>스킵 요청 판정.</summary>
public enum MapSceneSkipVerdict : byte
{
    Ok,
    /// <summary>지금보다 앞(뒤쪽) 구간 — 뒤로 가기는 지원하지 않는다.</summary>
    Backward,
    /// <summary>이미 보스방 도착(단계 Finished) — 튜토리얼 안내가 끝났다.</summary>
    Finished,
    /// <summary>판 재시작(맵 재로드)이 진행 중 — 끝날 때까지 어떤 스킵·재시작도 받지 않는다.</summary>
    RestartInProgress,
}

/// <summary>
/// 구간 스킵 계획 — "목적 구간 앞의 모든 단계를 정식 규칙대로 끝낸 것처럼" 만들 일의 목록.
///  · 게이트는 G1 → G10 순서로만 연다(디렉터 순서 규칙). 이미 열린 게이트의 일은 넣지 않는다.
///  · 목적 구간에 들어가는 데 필요한 마지막 게이트 = 그 구간의 입구 배리어 게이트(복도 n 입구 · 다음 존 = 복도 출구).
///    복도 목적이면 그 복도의 특성 라운드는 건드리지 않는다 — 도착한 뒤 중간 지점에서 정상으로 열린다.
///  · 보스방 = 보스 진입과 같은 계획 + 패드 이동.
///  · 처음부터(<see cref="MapSceneSkipTarget.Restart"/>)는 계획이 없다 — 게이트를 여는 대신 맵 씬을 다시 불러온다.
/// </summary>
public static class MapSceneSkipPlan
{
    public const int TargetCount = 10;

    /// <summary>보스방 스킵의 패드 이동 카운트다운(초) — 보스 진입 패드 기본값과 같다.</summary>
    public const float BossRoomCountdownSeconds = 3f;

    /// <summary>계획 단계마다 게이트가 열리기를 기다리는 최대 시간(실제 시간 초). 넘으면 강제 개방(경고).</summary>
    public const float GateWaitSeconds = 5f;

    /// <summary>숫자 키 → 목적. 1~9 = 복도1~보스방, 0 = 처음부터(판 재시작). 범위 밖이면 false.</summary>
    public static bool TryFromDigit(int digit, out MapSceneSkipTarget target)
    {
        target = MapSceneSkipTarget.Restart;
        if (digit < 0 || digit > 9) return false;
        target = digit == 0 ? MapSceneSkipTarget.Restart : (MapSceneSkipTarget)digit;
        return true;
    }

    /// <summary>목적 → 단축키 숫자(처음부터 = 0). 존1 은 단축키가 없다(-1).</summary>
    public static int DigitOf(MapSceneSkipTarget target) => target switch
    {
        MapSceneSkipTarget.Restart => 0,
        MapSceneSkipTarget.Zone1 => -1,
        _ => (int)target,
    };

    public static bool IsRestart(MapSceneSkipTarget target) => target == MapSceneSkipTarget.Restart;

    public static string NameOf(MapSceneSkipTarget target) => target switch
    {
        MapSceneSkipTarget.Restart => "처음부터",
        MapSceneSkipTarget.Zone1 => "존1",
        MapSceneSkipTarget.Corridor1 => "복도1",
        MapSceneSkipTarget.Zone2 => "존2",
        MapSceneSkipTarget.Corridor2 => "복도2",
        MapSceneSkipTarget.Zone3 => "존3",
        MapSceneSkipTarget.Corridor3 => "복도3",
        MapSceneSkipTarget.Zone4 => "존4",
        MapSceneSkipTarget.Corridor4 => "복도4",
        MapSceneSkipTarget.BossEnter => "보스 진입",
        _ => "보스방",
    };

    /// <summary>순간이동할 구간. 보스방은 보스 진입 존에 내린 뒤 패드 이동이 데려간다. 처음부터 = 존1(재로드 뒤 처음 스폰).</summary>
    public static MapSceneArea AreaOf(MapSceneSkipTarget target) => target switch
    {
        MapSceneSkipTarget.Restart or MapSceneSkipTarget.Zone1 => MapSceneArea.Zone1,
        MapSceneSkipTarget.Corridor1 => MapSceneArea.Corridor1,
        MapSceneSkipTarget.Zone2 => MapSceneArea.Zone2,
        MapSceneSkipTarget.Corridor2 => MapSceneArea.Corridor2,
        MapSceneSkipTarget.Zone3 => MapSceneArea.Zone3,
        MapSceneSkipTarget.Corridor3 => MapSceneArea.Corridor3,
        MapSceneSkipTarget.Zone4 => MapSceneArea.Zone4,
        MapSceneSkipTarget.Corridor4 => MapSceneArea.Corridor4,
        _ => MapSceneArea.BossEnter,
    };

    /// <summary>디렉터 단계 — 존은 첫 진입 단계(존3 연습 · 존4 설정 자막). 보스방은 BossEnter(도착이 Finished 로 바꾼다).</summary>
    public static MapSceneStep StepOf(MapSceneSkipTarget target) => target switch
    {
        MapSceneSkipTarget.Restart or MapSceneSkipTarget.Zone1 => MapSceneStep.Zone1,
        MapSceneSkipTarget.Corridor1 => MapSceneStep.Corridor1,
        MapSceneSkipTarget.Zone2 => MapSceneStep.Zone2,
        MapSceneSkipTarget.Corridor2 => MapSceneStep.Corridor2,
        MapSceneSkipTarget.Zone3 => MapSceneStep.Zone3Practice,
        MapSceneSkipTarget.Corridor3 => MapSceneStep.Corridor3,
        MapSceneSkipTarget.Zone4 => MapSceneStep.Zone4Lore,
        MapSceneSkipTarget.Corridor4 => MapSceneStep.Corridor4,
        _ => MapSceneStep.BossEnter,
    };

    /// <summary>지금 단계 → 지금 구간(뒤로 가기 판정 기준). None(활성 전) = 존1, Finished = 보스방.</summary>
    public static MapSceneSkipTarget CurrentOf(MapSceneStep step) => step switch
    {
        MapSceneStep.Corridor1 => MapSceneSkipTarget.Corridor1,
        MapSceneStep.Zone2 => MapSceneSkipTarget.Zone2,
        MapSceneStep.Corridor2 => MapSceneSkipTarget.Corridor2,
        MapSceneStep.Zone3Practice or MapSceneStep.Zone3Fight => MapSceneSkipTarget.Zone3,
        MapSceneStep.Corridor3 => MapSceneSkipTarget.Corridor3,
        MapSceneStep.Zone4Lore or MapSceneStep.Zone4Combat => MapSceneSkipTarget.Zone4,
        MapSceneStep.Corridor4 => MapSceneSkipTarget.Corridor4,
        MapSceneStep.BossEnter => MapSceneSkipTarget.BossEnter,
        MapSceneStep.Finished => MapSceneSkipTarget.BossRoom,
        _ => MapSceneSkipTarget.Zone1,
    };

    /// <summary>
    /// 스킵을 받아들이는가.
    ///  · 판 재시작(맵 재로드) 진행 중이면 무엇이든 거부(<see cref="MapSceneSkipVerdict.RestartInProgress"/>) — 재입력 방지.
    ///  · 처음부터(Alt+0)는 뒤로 가기 규칙의 예외 — 어느 구간에서든, 보스방 도착(Finished) 뒤에도 허용.
    ///  · 그 밖엔 뒤로 가기(목적 &lt; 지금) 거부, 이미 Finished 면 거부.
    ///    지금과 같은 구간은 허용 — 열 게이트 없이 그 구간 입구 안쪽으로 다시 옮기기만 한다.
    /// </summary>
    public static MapSceneSkipVerdict Check(MapSceneStep currentStep, MapSceneSkipTarget target, bool restartInProgress = false)
    {
        if (restartInProgress) return MapSceneSkipVerdict.RestartInProgress;
        if (IsRestart(target)) return MapSceneSkipVerdict.Ok;
        if (currentStep == MapSceneStep.Finished) return MapSceneSkipVerdict.Finished;
        return target < CurrentOf(currentStep) ? MapSceneSkipVerdict.Backward : MapSceneSkipVerdict.Ok;
    }

    /// <summary>목적 구간에 들어가려면 열려 있어야 하는 마지막 게이트. 존1 은 없다(false).</summary>
    public static bool TryGetRequiredGate(MapSceneSkipTarget target, out MapSceneGate gate)
    {
        gate = default;
        switch (target)
        {
            case MapSceneSkipTarget.Corridor1: gate = MapSceneGate.G1_Corridor1Entry; return true;
            case MapSceneSkipTarget.Zone2: gate = MapSceneGate.G2_Zone2Entry; return true;
            case MapSceneSkipTarget.Corridor2: gate = MapSceneGate.G3_Corridor2Entry; return true;
            case MapSceneSkipTarget.Zone3: gate = MapSceneGate.G4_Zone3Entry; return true;
            case MapSceneSkipTarget.Corridor3: gate = MapSceneGate.G6_Corridor3Entry; return true;
            case MapSceneSkipTarget.Zone4: gate = MapSceneGate.G7_Zone4Entry; return true;
            case MapSceneSkipTarget.Corridor4: gate = MapSceneGate.G9_Corridor4Entry; return true;
            case MapSceneSkipTarget.BossEnter:
            case MapSceneSkipTarget.BossRoom: gate = MapSceneGate.G10_BossEnterAndPad; return true;
            default: return false;
        }
    }

    /// <summary>게이트 하나를 정식 경로로 여는 일. G11(보스방 도착)은 계획에 없다 — 도착이 연다.</summary>
    public static MapSceneSkipAction ActionFor(MapSceneGate gate) => gate switch
    {
        MapSceneGate.G1_Corridor1Entry => new MapSceneSkipAction(MapSceneSkipActionKind.CompleteZone1Checks, gate),
        MapSceneGate.G2_Zone2Entry => new MapSceneSkipAction(MapSceneSkipActionKind.AutoChooseTraits, gate, corridor: 1),
        MapSceneGate.G3_Corridor2Entry => new MapSceneSkipAction(MapSceneSkipActionKind.ClearZone, gate, zone: MapSceneArea.Zone2),
        MapSceneGate.G4_Zone3Entry => new MapSceneSkipAction(MapSceneSkipActionKind.AutoChooseTraits, gate, corridor: 2),
        MapSceneGate.G5_SpinnerRealMode => new MapSceneSkipAction(MapSceneSkipActionKind.CompleteSpinnerPractice, gate),
        MapSceneGate.G6_Corridor3Entry => new MapSceneSkipAction(MapSceneSkipActionKind.ClearZone, gate, zone: MapSceneArea.Zone3),
        MapSceneGate.G7_Zone4Entry => new MapSceneSkipAction(MapSceneSkipActionKind.AutoChooseTraits, gate, corridor: 3),
        MapSceneGate.G8_TimerStart => new MapSceneSkipAction(MapSceneSkipActionKind.StartBossTimer, gate),
        MapSceneGate.G9_Corridor4Entry => new MapSceneSkipAction(MapSceneSkipActionKind.ClearZone, gate, zone: MapSceneArea.Zone4),
        _ => new MapSceneSkipAction(MapSceneSkipActionKind.AutoChooseTraits, gate, corridor: 4),
    };

    /// <summary>
    /// 계획 — 목적 구간까지 아직 닫힌 게이트를 G1 부터 순서대로 여는 일 목록(<paramref name="into"/> 를 비우고 채운다).
    /// <paramref name="openedGateMask"/> = 디렉터 열림 마스크(bit = 1 &lt;&lt; (int)MapSceneGate). 이미 열린 게이트는 건너뛴다.
    /// </summary>
    public static void Build(MapSceneSkipTarget target, int openedGateMask, List<MapSceneSkipAction> into)
    {
        into.Clear();
        if (!TryGetRequiredGate(target, out MapSceneGate last)) return;
        for (int i = 0; i <= (int)last; i++)
        {
            if ((openedGateMask & (1 << i)) != 0) continue;
            into.Add(ActionFor((MapSceneGate)i));
        }
    }

    /// <summary>계획 중 특성 자동 획득 복도 번호(순서대로).</summary>
    public static void AutoChosenCorridors(IReadOnlyList<MapSceneSkipAction> plan, List<int> into)
    {
        into.Clear();
        foreach (MapSceneSkipAction a in plan)
            if (a.Kind == MapSceneSkipActionKind.AutoChooseTraits) into.Add(a.Corridor);
    }

    /// <summary>게이트 마스크 → "G1·G2" 같은 짧은 이름(로그용). 비면 "없음".</summary>
    public static string DescribeGates(int mask)
    {
        var parts = new List<string>();
        for (int i = 0; i < MapSceneFlowModel.GateCount; i++)
            if ((mask & (1 << i)) != 0) parts.Add($"G{i + 1}");
        return parts.Count > 0 ? string.Join("·", parts) : "없음";
    }
}
