using System;

// 존1(기본 조작 + 공격·스킬 사용 + 패시브) 순수 규칙 — Unity·NGO 비의존(EditMode 테스트 대상).
// 원본: Docs/design/tutorial-scenario.md §3.0·§3.1(v8) · PLAN-tutorial.md S7·V1. 배선은 TutorialZone1Flow.
// 🔴 시간은 전부 인자로 받는다(호출자가 실제 시간 — 슬로우모션 무관, §2.1).

/// <summary>존1 단계 상수.</summary>
public static class TutorialZone1Rules
{
    /// <summary>Z1-1 이동 누적 거리(m).</summary>
    public const float RequiredMoveMeters = 5f;

    /// <summary>Z1-4 패시브 툴팁이 떠 있어야 하는 시간(초).</summary>
    public const float PassiveTooltipHoldSeconds = 1f;

    /// <summary>S0 착지 뒤 환영 자막까지(초).</summary>
    public const float WelcomeDelaySeconds = 1f;

    /// <summary>
    /// 이동 보고 상한 검증용 최대 속도(m/s). 기본 이동 10 · 대시 20(PlayerDashData) 에 이동속도 강화 여유를 더한 값 —
    /// 정상 플레이로는 넘을 수 없는 값만 거른다(거짓 보고 방지, 판정을 빡빡하게 하려는 게 아니다).
    /// </summary>
    public const float MaxPlausibleSpeed = 30f;

    /// <summary>이동 보고 상한 여유(m) — 오너·서버 시계 차이·복제 지연.</summary>
    public const float MoveReportSlackMeters = 3f;

    /// <summary>패시브 툴팁 출처 키(<c>SkillTooltipView.TooltipShown</c> 인자 — PassiveHUD).</summary>
    public const string PassiveTooltipKey = "Passive";

    /// <summary>
    /// [서버] 오너가 보고한 누적 이동 거리가 그럴듯한가.
    /// 목표(<paramref name="requiredMeters"/>) 이상이어야 하고, 튜토리얼 시작 뒤 지난 시간 동안 최대 속도로 갈 수 있는 거리 + 여유를 넘으면 거짓.
    /// NaN·무한·음수 경과는 거부.
    /// </summary>
    public static bool IsMoveReportPlausible(
        float reportedMeters, double elapsedSeconds,
        float requiredMeters = RequiredMoveMeters, float maxSpeed = MaxPlausibleSpeed, float slackMeters = MoveReportSlackMeters)
    {
        if (float.IsNaN(reportedMeters) || float.IsInfinity(reportedMeters)) return false;
        if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds) || elapsedSeconds < 0d) return false;
        if (reportedMeters < requiredMeters) return false;
        return reportedMeters <= maxSpeed * elapsedSeconds + slackMeters;
    }

    /// <summary>
    /// 존1 허수아비를 거둘 때인가(S12) — G1 이 열렸고, 살아 있는 사람이 아무도 존1 에 없거나 파티가 이미 존2 단계로 넘어갔다.
    /// G1 직후 존1 에 남은 사람 눈앞에서 사라지지 않게 떠난 뒤에 거둔다.
    /// </summary>
    public static bool ShouldDespawnDummies(bool g1Open, bool anyAlivePlayerInZone1, TutorialStep step) =>
        g1Open && (!anyAlivePlayerInZone1 || step >= TutorialStep.Zone2);
}

/// <summary>
/// 존1 공격·스킬 실제 사용 판정(v8 Z1-3·Z1-5~Z1-8). 서버 신호를 체크 항목으로 바꾼다.
/// <list type="bullet">
/// <item>기본 공격 = 기본 공격 1타 실제 시작(<c>Player.ServerBasicAttackStarted</c> — 적중 무관, R 대상 확정 좌클릭은 그 경로를 안 탄다).</item>
/// <item>Q·E·R·우클릭 = 서버 승인(<c>PlayerSkillController.ServerSkillUsed</c>) — 쿨타임 중 거부·<c>CanUse</c> 실패(분노 부족)·조준 취소는 승인이 없어 안 센다.
///   R 대상 지정 스킬은 대상 확정 뒤에 승인되고, 어쌔신 R 승인 = 변신 진입. 일반·변신 Q/E 는 같은 슬롯 = 같은 항목.</item>
/// <item>예외 — 발사형 스킬(거너 Q: 승인 = 충전 시작)은 승인으로 세지 않고 <b>발사 신호</b>(<c>GunnerChargeLaserSkill.ServerFired</c>)로 센다.</item>
/// </list>
/// </summary>
public static class TutorialZone1SkillRules
{
    /// <summary>스킬 슬롯 → 존1 체크 항목. 모르는 슬롯이면 false.</summary>
    public static bool TryGetSkillCheck(PlayerSkillSlot slot, out TutorialCheck check)
    {
        switch (slot)
        {
            case PlayerSkillSlot.Main: check = TutorialCheck.SkillQ; return true;
            case PlayerSkillSlot.Sub: check = TutorialCheck.SkillE; return true;
            case PlayerSkillSlot.Ultimate: check = TutorialCheck.SkillR; return true;
            case PlayerSkillSlot.Interrupt: check = TutorialCheck.SkillInterrupt; return true;
            default: check = default; return false;
        }
    }

    /// <summary>
    /// 승인 신호(<c>ServerSkillUsed</c>)로 셀 것인가. <paramref name="countsOnFire"/> = 그 스킬이 발사 신호로 세는 스킬(거너 Q)이면 승인은 무시한다.
    /// </summary>
    public static bool CountsOnApproval(PlayerSkillSlot slot, bool countsOnFire) =>
        TryGetSkillCheck(slot, out _) && !countsOnFire;
}

/// <summary>
/// [오너] 수평 이동 거리 누적. 한 프레임 이동이 최대 속도로 갈 수 있는 거리보다 크면 순간이동(스폰·추락 복구·보스방 이동)으로 보고 버린다.
/// 높이는 보지 않는다(스폰 낙하 10m 가 이동으로 잡히지 않게).
/// </summary>
public sealed class TutorialMoveAccumulator
{
    private readonly float _maxSpeed;
    private readonly float _stepSlack;
    private bool _hasLast;
    private float _lastX, _lastZ;

    public TutorialMoveAccumulator(float maxSpeed = TutorialZone1Rules.MaxPlausibleSpeed, float stepSlackMeters = 0.25f)
    {
        _maxSpeed = Math.Max(0f, maxSpeed);
        _stepSlack = Math.Max(0f, stepSlackMeters);
    }

    public float TotalMeters { get; private set; }

    /// <summary>다음 샘플을 기준점으로만 쓴다(사망·영혼 동안 끊었다가 다시 잴 때).</summary>
    public void Break() => _hasLast = false;

    /// <summary>위치 샘플(XZ)과 그 사이 시간(초). 누적에 더한 거리를 돌려준다(버리면 0).</summary>
    public float Sample(float x, float z, float deltaSeconds)
    {
        if (float.IsNaN(x) || float.IsNaN(z)) return 0f;
        if (!_hasLast)
        {
            _hasLast = true;
            _lastX = x;
            _lastZ = z;
            return 0f;
        }

        float dx = x - _lastX, dz = z - _lastZ;
        _lastX = x;
        _lastZ = z;
        float step = (float)Math.Sqrt(dx * dx + dz * dz);
        float dt = Math.Max(0f, deltaSeconds);
        if (step > _maxSpeed * dt + _stepSlack) return 0f;   // 순간이동

        TotalMeters += step;
        return step;
    }
}

/// <summary>
/// [오너] Z1-4 "패시브 툴팁이 <b>표시된 채</b> 1초 유지". 뜬 시각부터 재고, 그 전에 닫히면 처음부터.
/// 완료는 한 번만 알린다.
/// </summary>
public sealed class TutorialTooltipHold
{
    private readonly double _holdSeconds;
    private double _shownAt = -1d;

    public TutorialTooltipHold(double holdSeconds = TutorialZone1Rules.PassiveTooltipHoldSeconds)
    {
        _holdSeconds = Math.Max(0d, holdSeconds);
    }

    public bool IsShowing => _shownAt >= 0d;
    public bool IsComplete { get; private set; }

    public void Show(double now)
    {
        if (!IsShowing) _shownAt = now;
    }

    public void Hide() => _shownAt = -1d;

    /// <summary>방금 1초를 채웠으면 true(한 번만).</summary>
    public bool Tick(double now)
    {
        if (IsComplete || !IsShowing || now - _shownAt < _holdSeconds) return false;
        IsComplete = true;
        return true;
    }
}

/// <summary>
/// [오너] S0 "착지 후 1초" — 연속으로 땅에 닿아 있는 시간을 잰다(낙하 중·튕김은 0 으로).
/// 한 번만 알린다.
/// </summary>
public sealed class TutorialLandingTimer
{
    private readonly float _delay;
    private float _grounded;

    public TutorialLandingTimer(float delaySeconds = TutorialZone1Rules.WelcomeDelaySeconds)
    {
        _delay = Math.Max(0f, delaySeconds);
    }

    public bool Fired { get; private set; }

    public bool Tick(bool grounded, float deltaSeconds)
    {
        if (Fired) return false;
        if (!grounded)
        {
            _grounded = 0f;
            return false;
        }

        _grounded += Math.Max(0f, deltaSeconds);
        if (_grounded < _delay) return false;
        Fired = true;
        return true;
    }
}

/// <summary>존1 목표 패널의 지금 할 일 — 값 순서 = 안내 순서(v8 Z1-1~Z1-8).</summary>
public enum TutorialZone1Task : byte
{
    Move,
    Dash,
    Attack,
    Passive,
    SkillQ,
    SkillE,
    Interrupt,
    SkillR,
    /// <summary>개인 체크 완료 — 복도로(동료 대기 줄은 따로).</summary>
    Go,
}

/// <summary>목표 패널 한 장면(그리기는 TutorialZone1Flow → TutorialGuideUI).</summary>
public readonly struct TutorialZone1View
{
    public TutorialZone1View(TutorialZone1Task task, int checkMask)
    {
        Task = task;
        CheckMask = checkMask;
    }

    public TutorialZone1Task Task { get; }
    /// <summary>개인 체크 마스크(bit = <see cref="TutorialCheck"/>) — 체크리스트 □/■.</summary>
    public int CheckMask { get; }

    public bool Has(TutorialCheck c) => (CheckMask & (1 << (int)c)) != 0;
    public bool MoveDone => Has(TutorialCheck.Move);
    public bool DashDone => Has(TutorialCheck.Dash);
    public bool AttackDone => Has(TutorialCheck.BasicAttack);
    public bool PassiveDone => Has(TutorialCheck.PassiveTooltip);

    /// <summary>P 칸 펄스 — 패시브 차례에만.</summary>
    public bool HighlightPassiveSlot => Task == TutorialZone1Task.Passive && !PassiveDone;
}

/// <summary>존1 진행 → 목표 패널 장면.</summary>
public static class TutorialZone1Progress
{
    /// <summary>체크리스트 순서 = 안내 순서(이동·대시·기본 공격·패시브·Q·E·우클릭·R).</summary>
    public static readonly TutorialCheck[] ChecklistOrder =
    {
        TutorialCheck.Move, TutorialCheck.Dash, TutorialCheck.BasicAttack, TutorialCheck.PassiveTooltip,
        TutorialCheck.SkillQ, TutorialCheck.SkillE, TutorialCheck.SkillInterrupt, TutorialCheck.SkillR,
    };

    /// <summary>
    /// Z1-4 패시브 툴팁을 셀 차례인가 — 이동·대시·기본 공격을 마친 뒤(§3.1 순서: 팔라딘 패시브는 첫 평타 적중에서 발동하고,
    /// 방금 본 효과를 툴팁으로 확인시키는 흐름). 그 전에 호버한 것은 세지 않는다.
    /// </summary>
    public static bool IsPassiveTurn(TutorialPlayerProgress p) =>
        p.Has(TutorialCheck.Move) && p.Has(TutorialCheck.Dash) && p.Has(TutorialCheck.BasicAttack);

    /// <summary>
    /// 지금 할 일 = 이동 → 대시 → 기본 공격 → 패시브 → Q → E → 우클릭 → R 중 처음 남은 것.
    /// 순서와 무관하게 먼저 쓴 항목은 체크된 채 건너뛴다(v8 — 다시 시키지 않는다).
    /// 패시브를 마쳤어도 <paramref name="passiveInfoPending"/>(호버가 아직 안 끝나 캐릭터별 패시브 자막 전)이면 패시브에 머문다 — Z1-4 → 자막.
    /// </summary>
    public static TutorialZone1View Resolve(TutorialPlayerProgress p, bool passiveInfoPending)
    {
        TutorialZone1Task task =
            !p.Has(TutorialCheck.Move) ? TutorialZone1Task.Move :
            !p.Has(TutorialCheck.Dash) ? TutorialZone1Task.Dash :
            !p.Has(TutorialCheck.BasicAttack) ? TutorialZone1Task.Attack :
            !p.Has(TutorialCheck.PassiveTooltip) || passiveInfoPending ? TutorialZone1Task.Passive :
            !p.Has(TutorialCheck.SkillQ) ? TutorialZone1Task.SkillQ :
            !p.Has(TutorialCheck.SkillE) ? TutorialZone1Task.SkillE :
            !p.Has(TutorialCheck.SkillInterrupt) ? TutorialZone1Task.Interrupt :
            !p.Has(TutorialCheck.SkillR) ? TutorialZone1Task.SkillR :
            TutorialZone1Task.Go;

        return new TutorialZone1View(task, p.CheckMask);
    }

    /// <summary>
    /// 동료 대기 줄을 띄우는가 — 지금 열 차례인 개인 게이트가 G1 이고 나는 끝냈다(§2.1 "동료 대기 중 (1/2)" — G1 은 자동 진행 없음).
    /// </summary>
    public static bool ShowsWait(bool hasWait, TutorialGate waitGate, TutorialPlayerProgress p) =>
        hasWait && waitGate == TutorialGate.G1_Corridor1Entry
        && (p.CheckMask & TutorialFlowModel.RequiredMaskOf(TutorialGate.G1_Corridor1Entry))
           == TutorialFlowModel.RequiredMaskOf(TutorialGate.G1_Corridor1Entry);
}
