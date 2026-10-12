using System;
using System.Collections.Generic;

// 튜토리얼 진행 상태의 순수 규칙(Unity·NGO 비의존 — EditMode 테스트 대상).
// 원본: Docs/design/tutorial-scenario.md §2.1·§4, PLAN-tutorial.md S1. 배선은 TutorialDirector(서버)가 한다.
// 🔴 시간은 전부 인자로 받는다(서버 실제 시간 = ServerTime — 슬로우모션·솔로 일시정지 영향 없음, §2.1).

/// <summary>진행 단계(목표 패널이 보여 줄 "지금 어디"). 앞으로만 간다.</summary>
public enum TutorialStep : byte
{
    None = 0,
    Zone1 = 1,
    Corridor1 = 2,
    Zone2 = 3,
    Corridor2 = 4,
    Zone3Practice = 5,
    Zone3Fight = 6,
    Corridor3 = 7,
    Zone4Lore = 8,
    Zone4Combat = 9,
    Corridor4 = 10,
    BossEnter = 11,
    /// <summary>보스방 도착 — 튜토리얼 안내 종료(§3.9).</summary>
    Finished = 12,
}

/// <summary>파티 게이트(§4). 값 = 순서 — 앞 게이트가 열리기 전에는 뒤 게이트가 열리지 않는다.</summary>
public enum TutorialGate : byte
{
    G1_Corridor1Entry = 0,
    G2_Zone2Entry = 1,
    G3_Corridor2Entry = 2,
    G4_Zone3Entry = 3,
    /// <summary>스피너봇 연습 모드 → 본 모드.</summary>
    G5_SpinnerRealMode = 4,
    G6_Corridor3Entry = 5,
    G7_Zone4Entry = 6,
    /// <summary>타이머 시작(존4 설정 자막 종료).</summary>
    G8_TimerStart = 7,
    G9_Corridor4Entry = 8,
    /// <summary>보스 진입 존 입구 + 패드 활성.</summary>
    G10_BossEnterAndPad = 9,
    /// <summary>보스방 도착(패드·만료 강제 이동 공통) — 서버 판정 게이트, 배리어 없음. 목숨 차감 기준(S11 — 이 전 사망은 차감 없음).</summary>
    G11_BossRoom = 10,
}

/// <summary>개인 체크리스트 항목(§2.1). 값 = 비트 위치.</summary>
public enum TutorialCheck : byte
{
    Move = 0,
    Dash = 1,
    /// <summary>허수아비 기본 공격 적중 — 보고마다 1회, <see cref="TutorialFlowModel.AttackHitsRequired"/> 회면 완료.</summary>
    AttackHit = 2,
    PassiveTooltip = 3,
    /// <summary>존2 Q 사용 — 안내용(게이트 아님, D13).</summary>
    SkillQ = 4,
    /// <summary>존2 E 사용 — 안내용(게이트 아님, D13).</summary>
    SkillE = 5,
    TraitCorridor1 = 6,
    TraitCorridor2 = 7,
    TraitCorridor3 = 8,
    TraitCorridor4 = 9,
}

/// <summary>파티 집계 카운터(범용). 목표값은 모델 생성 때 정한다.</summary>
public enum TutorialCounter : byte
{
    /// <summary>존2 처치 n/목표(목표 = 존 장부의 존2 수, 기본 9) — 표시용. G3(전멸) 판정은 S4 존 장부가 한다.</summary>
    Zone2Kills = 0,
    /// <summary>존3 스피너봇 간파 파티 합계 n/3 — 목표 도달 = G5(D2-1).</summary>
    Zone3Interrupts = 1,
    /// <summary>존4 제어 패널 n/4 — 표시용(다리는 ZoneBridgeGateManager 가 연다).</summary>
    Zone4Panels = 2,
}

public enum TutorialGateOpenReason : byte
{
    /// <summary>서버가 직접 열었다(존 클리어·자막 종료 등).</summary>
    Manual = 0,
    /// <summary>개인 체크리스트 전원 완료.</summary>
    AllDone = 1,
    /// <summary>교착 방지 — 첫 완료자 후 제한 시간 경과. 미완료자 처리(특성 자동 획득 등)는 소비처 몫(D14).</summary>
    Timeout = 2,
    /// <summary>카운터 목표 도달(G5).</summary>
    CounterReached = 3,
}

/// <summary>개인 체크리스트 진행.</summary>
public struct TutorialPlayerProgress
{
    public ulong ClientId;
    /// <summary>bit = <see cref="TutorialCheck"/>.</summary>
    public int CheckMask;
    public int AttackHits;
    /// <summary>접속 중(이탈하면 false — 재접속 없음).</summary>
    public bool Connected;
    public bool PermanentDead;

    /// <summary>'전원'에 드는가 = 접속 중이고 완전 사망이 아님(§2.1).</summary>
    public bool IsParticipant => Connected && !PermanentDead;

    public bool Has(TutorialCheck c) => (CheckMask & (1 << (int)c)) != 0;
}

/// <summary>
/// 튜토리얼 진행 상태 + 규칙. 서버 한 곳에서만 돌리고 결과를 복제한다.
///
/// 규칙:
///  · 게이트는 <see cref="TutorialGate"/> 순서대로만 열린다(앞이 닫혀 있으면 뒤를 열지 않는다 — 건너뛰기 방지).
///  · 개인 게이트(G1·G2·G4·G7·G10) = 참가자 전원 완료면 열림. 첫 완료자가 나온 시각부터
///    <see cref="DeadlockSeconds"/> 가 지나면 자동으로 열림(교착 방지 D9). 시각 기록은 그 게이트가
///    <b>열 수 있는 차례</b>가 된 뒤부터 — 앞 게이트 전에 미리 끝낸 사람이 있으면 차례가 온 순간이 기준.
///  · 첫 완료자가 그 뒤 이탈·완전 사망해도 교착 방지 시계는 멈추지 않는다(파티가 멈추지 않는 쪽).
///  · 참가자가 0명이면 아무 게이트도 자동으로 열지 않는다(전원 이탈 = 세션 종료, 전원 완전 사망 = 결과 화면 — 기존 흐름이 처리).
///  · 같은 보고가 두 번 와도 한 번만 센다. 모르는 clientId 의 보고는 버린다.
/// </summary>
public sealed class TutorialFlowModel
{
    public const int GateCount = 11;
    public const int CounterCount = 3;
    public const int AttackHitsRequired = 5;
    public const double DefaultDeadlockSeconds = 60d;

    private const int G1Mask = (1 << (int)TutorialCheck.Move) | (1 << (int)TutorialCheck.Dash)
                             | (1 << (int)TutorialCheck.AttackHit) | (1 << (int)TutorialCheck.PassiveTooltip);

    private readonly Dictionary<ulong, TutorialPlayerProgress> _players = new Dictionary<ulong, TutorialPlayerProgress>();
    private readonly List<ulong> _order = new List<ulong>();   // 등록 순서(복제 목록 순서를 결정적으로)
    private readonly int[] _counters = new int[CounterCount];
    private readonly int[] _counterTargets;
    private readonly double[] _firstCompleteAt = new double[GateCount];

    public TutorialFlowModel(double deadlockSeconds = DefaultDeadlockSeconds, int zone2Kills = 9, int zone3Interrupts = 3, int zone4Panels = 4)
    {
        DeadlockSeconds = deadlockSeconds;
        _counterTargets = new[] { zone2Kills, zone3Interrupts, zone4Panels };
        for (int i = 0; i < GateCount; i++) _firstCompleteAt[i] = -1d;
    }

    public double DeadlockSeconds { get; }
    public TutorialStep Step { get; private set; } = TutorialStep.None;
    /// <summary>bit = <see cref="TutorialGate"/>.</summary>
    public int OpenedGateMask { get; private set; }
    /// <summary>교착 방지로 열린 게이트(bit = <see cref="TutorialGate"/>) — 자동 획득 대상 판단용.</summary>
    public int TimeoutGateMask { get; private set; }
    /// <summary>
    /// 열기 요청이 왔지만 앞 게이트가 닫혀 있어 <b>보류 중</b>인 게이트(bit = <see cref="TutorialGate"/>).
    /// 차례가 오면 <see cref="Evaluate"/> 가 연다(<see cref="RequestGate"/>).
    /// </summary>
    public int RequestedGateMask { get; private set; }

    public event Action<TutorialStep, TutorialStep> StepChanged;
    public event Action<TutorialGate, TutorialGateOpenReason> GateOpened;

    public IReadOnlyList<ulong> PlayerIds => _order;

    // ─── 게이트 정의 ──────────────────────────────────────────────────────

    /// <summary>개인 체크리스트 게이트면 필요한 항목 마스크, 아니면 0.</summary>
    public static int RequiredMaskOf(TutorialGate g) => g switch
    {
        TutorialGate.G1_Corridor1Entry => G1Mask,
        TutorialGate.G2_Zone2Entry => 1 << (int)TutorialCheck.TraitCorridor1,
        TutorialGate.G4_Zone3Entry => 1 << (int)TutorialCheck.TraitCorridor2,
        TutorialGate.G7_Zone4Entry => 1 << (int)TutorialCheck.TraitCorridor3,
        TutorialGate.G10_BossEnterAndPad => 1 << (int)TutorialCheck.TraitCorridor4,
        _ => 0,
    };

    public static bool IsPersonalGate(TutorialGate g) => RequiredMaskOf(g) != 0;

    /// <summary>카운터로 열리는 게이트면 그 카운터.</summary>
    public static bool TryGetGateCounter(TutorialGate g, out TutorialCounter counter)
    {
        counter = TutorialCounter.Zone3Interrupts;
        return g == TutorialGate.G5_SpinnerRealMode;
    }

    public bool IsGateOpen(TutorialGate g) => (OpenedGateMask & (1 << (int)g)) != 0;

    /// <summary>앞 게이트가 모두 열려 이 게이트를 열 수 있는 차례인가.</summary>
    public bool IsGateEligible(TutorialGate g) => (int)g == 0 || IsGateOpen((TutorialGate)((int)g - 1));

    // ─── 인원 ────────────────────────────────────────────────────────────

    /// <summary>플레이어 등록(접속 중). 이미 있으면 무시(재접속은 없음 — 이탈한 id 는 되살리지 않는다).</summary>
    public bool AddPlayer(ulong clientId)
    {
        if (_players.ContainsKey(clientId)) return false;
        _players[clientId] = new TutorialPlayerProgress { ClientId = clientId, Connected = true };
        _order.Add(clientId);
        return true;
    }

    /// <summary>이탈. 진행 기록은 남기고 '전원'에서만 뺀다. 호출 뒤 <see cref="Evaluate"/> 로 재판정.</summary>
    public bool MarkDisconnected(ulong clientId) => Mutate(clientId, (ref TutorialPlayerProgress p) =>
    {
        if (!p.Connected) return false;
        p.Connected = false;
        return true;
    });

    public bool SetPermanentDead(ulong clientId, bool dead) => Mutate(clientId, (ref TutorialPlayerProgress p) =>
    {
        if (p.PermanentDead == dead) return false;
        p.PermanentDead = dead;
        return true;
    });

    public bool TryGetPlayer(ulong clientId, out TutorialPlayerProgress progress) => _players.TryGetValue(clientId, out progress);

    public int ParticipantCount
    {
        get
        {
            int n = 0;
            foreach (TutorialPlayerProgress p in _players.Values) if (p.IsParticipant) n++;
            return n;
        }
    }

    // ─── 보고 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 체크 항목 완료 보고. <see cref="TutorialCheck.AttackHit"/> 은 보고 1번 = 적중 1회.
    /// 바뀐 게 있으면 true(중복·모르는 id·이미 완료는 false). 게이트 판정은 <see cref="Evaluate"/> 에서.
    /// </summary>
    public bool ReportCheck(ulong clientId, TutorialCheck check) => Mutate(clientId, (ref TutorialPlayerProgress p) =>
    {
        if (p.Has(check)) return false;

        if (check == TutorialCheck.AttackHit)
        {
            p.AttackHits++;
            if (p.AttackHits >= AttackHitsRequired) p.CheckMask |= 1 << (int)check;
            return true;
        }

        p.CheckMask |= 1 << (int)check;
        return true;
    });

    public int GetCounter(TutorialCounter c) => _counters[(int)c];
    public int GetCounterTarget(TutorialCounter c) => _counterTargets[(int)c];

    /// <summary>카운터 증가(목표에서 멈춤). 바뀌면 true.</summary>
    public bool AddCounter(TutorialCounter c, int amount = 1)
    {
        if (amount <= 0) return false;
        int i = (int)c;
        int next = Math.Min(_counterTargets[i], _counters[i] + amount);
        if (next == _counters[i]) return false;
        _counters[i] = next;
        return true;
    }

    // ─── 단계 · 게이트 ────────────────────────────────────────────────────

    /// <summary>단계 변경 — 앞으로만(같거나 뒤로 가는 요청은 무시).</summary>
    public bool SetStep(TutorialStep step)
    {
        if (step <= Step) return false;
        TutorialStep old = Step;
        Step = step;
        StepChanged?.Invoke(old, step);
        return true;
    }

    /// <summary>
    /// 서버가 직접 여는 게이트(G3·G6·G8·G9·G11 — 존 클리어·자막 종료·보스방 도착 등). 개인·카운터 게이트도 강제로 열 수 있다
    /// (타이머 만료 자동 획득 같은 예외 경로). 순서가 안 맞으면(앞 게이트 닫힘) false.
    /// </summary>
    public bool TryOpenGate(TutorialGate g, TutorialGateOpenReason reason = TutorialGateOpenReason.Manual)
    {
        if (IsGateOpen(g) || !IsGateEligible(g)) return false;

        OpenedGateMask |= 1 << (int)g;
        RequestedGateMask &= ~(1 << (int)g);
        if (reason == TutorialGateOpenReason.Timeout) TimeoutGateMask |= 1 << (int)g;
        GateOpened?.Invoke(g, reason);
        return true;
    }

    /// <summary>
    /// 조건이 이미 충족된 게이트를 <b>열거나, 차례가 아니면 보류</b>한다(존 클리어 — S4).
    /// 차례(앞 게이트 열림)면 즉시 <see cref="TryOpenGate"/>(Manual), 아니면 <see cref="RequestedGateMask"/> 에 남겨
    /// 앞 게이트가 열리는 <see cref="Evaluate"/> 에서 연다 — 조건은 다시 보지 않는다(처치는 되돌릴 수 없다).
    /// 반환: 지금 열었으면 true, 보류했거나 이미 열림/이미 보류면 false.
    /// </summary>
    public bool RequestGate(TutorialGate g)
    {
        if (IsGateOpen(g)) return false;
        if (TryOpenGate(g)) return true;
        RequestedGateMask |= 1 << (int)g;
        return false;
    }

    public bool IsGateRequested(TutorialGate g) => (RequestedGateMask & (1 << (int)g)) != 0;

    /// <summary>
    /// 개인·카운터 게이트를 재판정한다. 보고·인원 변화 직후와 주기적으로(교착 방지 시계) 부른다.
    /// 한 번 부를 때 열 수 있는 게이트를 순서대로 모두 연다.
    /// </summary>
    public void Evaluate(double now)
    {
        for (int i = 0; i < GateCount; i++)
        {
            var g = (TutorialGate)i;
            if (IsGateOpen(g)) continue;
            if (!IsGateEligible(g)) return;   // 순서 — 이 뒤는 볼 필요가 없다

            // 보류된 요청(존 클리어가 차례보다 먼저 옴) — 차례가 왔으니 연다. 게이트 종류와 무관.
            if (IsGateRequested(g)) { TryOpenGate(g); continue; }

            if (TryGetGateCounter(g, out TutorialCounter counter))
            {
                if (GetCounter(counter) >= GetCounterTarget(counter)) TryOpenGate(g, TutorialGateOpenReason.CounterReached);
                else return;
                continue;
            }

            if (!IsPersonalGate(g)) return;   // 수동 게이트 — 서버가 열 때까지 대기

            CountCompletion(g, out int done, out int total);
            if (total == 0) return;   // 참가자 0명 — 자동으로 열지 않는다

            if (done == total) { TryOpenGate(g, TutorialGateOpenReason.AllDone); continue; }

            if (done > 0 && _firstCompleteAt[i] < 0d) _firstCompleteAt[i] = now;

            if (_firstCompleteAt[i] >= 0d && now >= _firstCompleteAt[i] + DeadlockSeconds)
            {
                TryOpenGate(g, TutorialGateOpenReason.Timeout);
                continue;
            }
            return;
        }
    }

    // ─── 대기 표시(§2.1 "동료 대기 중 (1/2) · 자동 진행 0:45") ──────────────────

    /// <summary>
    /// 지금 열 차례인 개인 게이트의 대기 정보. 그런 게이트가 없으면 false.
    /// <paramref name="autoAdvanceAt"/> = 교착 방지 자동 진행 시각(첫 완료자 전이면 -1).
    /// </summary>
    public bool TryGetPendingPersonalGate(out TutorialGate gate, out int done, out int total, out double autoAdvanceAt)
    {
        for (int i = 0; i < GateCount; i++)
        {
            var g = (TutorialGate)i;
            if (IsGateOpen(g)) continue;
            if (!IsGateEligible(g) || !IsPersonalGate(g)) break;

            gate = g;
            CountCompletion(g, out done, out total);
            autoAdvanceAt = _firstCompleteAt[i] >= 0d ? _firstCompleteAt[i] + DeadlockSeconds : -1d;
            return true;
        }

        gate = default;
        done = total = 0;
        autoAdvanceAt = -1d;
        return false;
    }

    /// <summary>개인 게이트 g 에 대해 그 플레이어가 할 일을 다 했는가.</summary>
    public bool IsComplete(ulong clientId, TutorialGate g)
    {
        int mask = RequiredMaskOf(g);
        return mask != 0 && _players.TryGetValue(clientId, out TutorialPlayerProgress p) && (p.CheckMask & mask) == mask;
    }

    private void CountCompletion(TutorialGate g, out int done, out int total)
    {
        int mask = RequiredMaskOf(g);
        done = total = 0;
        foreach (TutorialPlayerProgress p in _players.Values)
        {
            if (!p.IsParticipant) continue;
            total++;
            if ((p.CheckMask & mask) == mask) done++;
        }
    }

    private delegate bool Mutator(ref TutorialPlayerProgress p);

    private bool Mutate(ulong clientId, Mutator m)
    {
        if (!_players.TryGetValue(clientId, out TutorialPlayerProgress p)) return false;
        if (!m(ref p)) return false;
        _players[clientId] = p;
        return true;
    }
}
