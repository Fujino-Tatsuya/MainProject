using System;
using System.Collections.Generic;

// 튜토리얼 진행 상태의 순수 규칙(Unity·NGO 비의존 — EditMode 테스트 대상).
// 원본: Docs/design/tutorial-scenario.md §2.1·§4, PLAN-tutorial.md S1. 배선은 MapSceneDirector(서버)가 한다.
// 🔴 시간은 전부 인자로 받는다(서버 실제 시간 = ServerTime — 슬로우모션·솔로 일시정지 영향 없음, §2.1).

/// <summary>진행 단계(목표 패널이 보여 줄 "지금 어디"). 앞으로만 간다.</summary>
public enum MapSceneStep : byte
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
public enum MapSceneGate : byte
{
    G1_Corridor1Entry = 0,
    G2_Zone2Entry = 1,
    G3_Corridor2Entry = 2,
    G4_Zone3Entry = 3,
    /// <summary>스피너봇 연습 모드 → 본 모드(v8: 연계 3회 + 마지막 회복 끝 — 존3 흐름이 연다).</summary>
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
public enum MapSceneCheck : byte
{
    Move = 0,
    Dash = 1,
    /// <summary>기본 공격 <b>실제 실행</b> 1회(v8 Z1-3 — 적중 불필요, R 대상 확정 좌클릭 제외).</summary>
    BasicAttack = 2,
    PassiveTooltip = 3,
    /// <summary>Q 실제 사용 1회(v8 Z1-5, 거너는 발사까지) — G1 조건.</summary>
    SkillQ = 4,
    /// <summary>E 실제 사용 1회(v8 Z1-6) — G1 조건.</summary>
    SkillE = 5,
    TraitCorridor1 = 6,
    TraitCorridor2 = 7,
    TraitCorridor3 = 8,
    TraitCorridor4 = 9,
    /// <summary>R 실제 사용 1회(v8 Z1-8 — 대상 확정 뒤 발동·어쌔신 변신 진입) — G1 조건.</summary>
    SkillR = 10,
    /// <summary>우클릭(간파) 실제 사용 1회(v8 Z1-7 — 성공 불필요) — G1 조건.</summary>
    SkillInterrupt = 11,
    /// <summary>복도1 첫 진입 쿨타임 초기화를 받았다(v8 C1-0 — 개인 1회, 체크리스트 아님·게이트 아님).</summary>
    Corridor1CooldownReset = 12,
}

/// <summary>파티 집계 카운터(범용). 목표값은 모델 생성 때 정한다.</summary>
public enum MapSceneCounter : byte
{
    /// <summary>존2 처치 n/목표(목표 = 존 장부의 존2 수, 기본 9) — 표시용. G3(전멸) 판정은 S4 존 장부가 한다.</summary>
    Zone2Kills = 0,
    /// <summary>
    /// 존3 연계(간파 → 취약 밀치기 → 유효 벽 충돌) 파티 합계 n/3(v8 D2·D2-1 — 이름은 이전 판 그대로). 목표 도달만으로는 G5 를 열지 않는다 —
    /// 마지막 벽 충돌 뒤 회복이 끝나야 존3 흐름이 G5 를 연다(v8 Z3-9 · 원위치 복귀 없음 — 사용자 변경).
    /// </summary>
    Zone3Interrupts = 1,
    /// <summary>존4 제어 패널 n/4 — 표시용(다리는 ZoneBridgeGateManager 가 연다).</summary>
    Zone4Panels = 2,
}

public enum MapSceneGateOpenReason : byte
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
public struct MapScenePlayerProgress
{
    public ulong ClientId;
    /// <summary>bit = <see cref="MapSceneCheck"/>.</summary>
    public int CheckMask;
    /// <summary>접속 중(이탈하면 false — 재접속 없음).</summary>
    public bool Connected;
    public bool PermanentDead;

    /// <summary>'전원'에 드는가 = 접속 중이고 완전 사망이 아님(§2.1).</summary>
    public bool IsParticipant => Connected && !PermanentDead;

    public bool Has(MapSceneCheck c) => (CheckMask & (1 << (int)c)) != 0;
}

/// <summary>
/// 튜토리얼 진행 상태 + 규칙. 서버 한 곳에서만 돌리고 결과를 복제한다.
///
/// 규칙:
///  · 게이트는 <see cref="MapSceneGate"/> 순서대로만 열린다(앞이 닫혀 있으면 뒤를 열지 않는다 — 건너뛰기 방지).
///  · 개인 게이트(G1·G2·G4·G7·G10) = 참가자 전원 완료면 열림. 특성 게이트(G2·G4·G7·G10)는 첫 완료자가 나온 시각부터
///    <see cref="DeadlockSeconds"/> 가 지나면 자동으로 열림(교착 방지 D9). 시각 기록은 그 게이트가
///    <b>열 수 있는 차례</b>가 된 뒤부터 — 앞 게이트 전에 미리 끝낸 사람이 있으면 차례가 온 순간이 기준.
///    <b>G1 은 교착 방지 제외</b>(v8 §2.1 — 공격·스킬 5종 사용이 필수라 시간으로 넘기지 않는다).
///  · 첫 완료자가 그 뒤 이탈·완전 사망해도 교착 방지 시계는 멈추지 않는다(파티가 멈추지 않는 쪽).
///  · 참가자가 0명이면 아무 게이트도 자동으로 열지 않는다(전원 이탈 = 세션 종료, 전원 완전 사망 = 결과 화면 — 기존 흐름이 처리).
///  · 같은 보고가 두 번 와도 한 번만 센다. 모르는 clientId 의 보고는 버린다.
/// </summary>
public sealed class MapSceneFlowModel
{
    public const int GateCount = 11;
    public const int CounterCount = 3;
    public const double DefaultDeadlockSeconds = 60d;

    /// <summary>G1 = 이동·대시·패시브 툴팁 + 기본 공격·Q·E·R·우클릭 각각 실제 사용 1회(v8 §3.1·§4).</summary>
    public const int G1Mask = (1 << (int)MapSceneCheck.Move) | (1 << (int)MapSceneCheck.Dash)
                            | (1 << (int)MapSceneCheck.PassiveTooltip)
                            | (1 << (int)MapSceneCheck.BasicAttack) | (1 << (int)MapSceneCheck.SkillQ)
                            | (1 << (int)MapSceneCheck.SkillE) | (1 << (int)MapSceneCheck.SkillR)
                            | (1 << (int)MapSceneCheck.SkillInterrupt);

    private readonly Dictionary<ulong, MapScenePlayerProgress> _players = new Dictionary<ulong, MapScenePlayerProgress>();
    private readonly List<ulong> _order = new List<ulong>();   // 등록 순서(복제 목록 순서를 결정적으로)
    private readonly int[] _counters = new int[CounterCount];
    private readonly int[] _counterTargets;
    private readonly double[] _firstCompleteAt = new double[GateCount];

    public MapSceneFlowModel(double deadlockSeconds = DefaultDeadlockSeconds, int zone2Kills = 9, int zone3Interrupts = 3, int zone4Panels = 4)
    {
        DeadlockSeconds = deadlockSeconds;
        _counterTargets = new[] { zone2Kills, zone3Interrupts, zone4Panels };
        for (int i = 0; i < GateCount; i++) _firstCompleteAt[i] = -1d;
    }

    public double DeadlockSeconds { get; }
    public MapSceneStep Step { get; private set; } = MapSceneStep.None;
    /// <summary>bit = <see cref="MapSceneGate"/>.</summary>
    public int OpenedGateMask { get; private set; }
    /// <summary>교착 방지로 열린 게이트(bit = <see cref="MapSceneGate"/>) — 자동 획득 대상 판단용.</summary>
    public int TimeoutGateMask { get; private set; }
    /// <summary>
    /// 열기 요청이 왔지만 앞 게이트가 닫혀 있어 <b>보류 중</b>인 게이트(bit = <see cref="MapSceneGate"/>).
    /// 차례가 오면 <see cref="Evaluate"/> 가 연다(<see cref="RequestGate"/>).
    /// </summary>
    public int RequestedGateMask { get; private set; }

    public event Action<MapSceneStep, MapSceneStep> StepChanged;
    public event Action<MapSceneGate, MapSceneGateOpenReason> GateOpened;

    public IReadOnlyList<ulong> PlayerIds => _order;

    // ─── 게이트 정의 ──────────────────────────────────────────────────────

    /// <summary>개인 체크리스트 게이트면 필요한 항목 마스크, 아니면 0.</summary>
    public static int RequiredMaskOf(MapSceneGate g) => g switch
    {
        MapSceneGate.G1_Corridor1Entry => G1Mask,
        MapSceneGate.G2_Zone2Entry => 1 << (int)MapSceneCheck.TraitCorridor1,
        MapSceneGate.G4_Zone3Entry => 1 << (int)MapSceneCheck.TraitCorridor2,
        MapSceneGate.G7_Zone4Entry => 1 << (int)MapSceneCheck.TraitCorridor3,
        MapSceneGate.G10_BossEnterAndPad => 1 << (int)MapSceneCheck.TraitCorridor4,
        _ => 0,
    };

    public static bool IsPersonalGate(MapSceneGate g) => RequiredMaskOf(g) != 0;

    /// <summary>개인 게이트 중 교착 방지(첫 완료자 뒤 자동 진행)를 쓰는가 — 특성 게이트만. G1 은 제외(v8 §2.1).</summary>
    public static bool HasDeadlockTimeout(MapSceneGate g) => IsPersonalGate(g) && g != MapSceneGate.G1_Corridor1Entry;

    /// <summary>
    /// 카운터로 <b>자동으로</b> 열리는 게이트면 그 카운터. v8 부터 없다 — G5 는 연계 3회 뒤 <b>마지막 회복</b>까지 기다려야 해서
    /// 존3 흐름이 직접 연다(<see cref="RequestGate"/>). 카운터 게이트 자리는 확장용으로 남긴다.
    /// </summary>
    public static bool TryGetGateCounter(MapSceneGate g, out MapSceneCounter counter)
    {
        counter = MapSceneCounter.Zone3Interrupts;
        return false;
    }

    public bool IsGateOpen(MapSceneGate g) => (OpenedGateMask & (1 << (int)g)) != 0;

    /// <summary>앞 게이트가 모두 열려 이 게이트를 열 수 있는 차례인가.</summary>
    public bool IsGateEligible(MapSceneGate g) => (int)g == 0 || IsGateOpen((MapSceneGate)((int)g - 1));

    // ─── 인원 ────────────────────────────────────────────────────────────

    /// <summary>플레이어 등록(접속 중). 이미 있으면 무시(이탈한 id 는 되살리지 않는다 — 판 도중 재합류는 새 id 가 <see cref="RebindPlayer"/> 로 이어 받는다).</summary>
    public bool AddPlayer(ulong clientId)
    {
        if (_players.ContainsKey(clientId)) return false;
        _players[clientId] = new MapScenePlayerProgress { ClientId = clientId, Connected = true };
        _order.Add(clientId);
        return true;
    }

    /// <summary>이탈. 진행 기록은 남기고 '전원'에서만 뺀다. 호출 뒤 <see cref="Evaluate"/> 로 재판정.</summary>
    public bool MarkDisconnected(ulong clientId) => Mutate(clientId, (ref MapScenePlayerProgress p) =>
    {
        if (!p.Connected) return false;
        p.Connected = false;
        return true;
    });

    /// <summary>
    /// [판 도중 재합류 — 할 일 48 C14 R4c] 같은 사람이 새 clientId 로 돌아왔다. 옛 기록의 개인 체크·완전 사망을 새 기록이 이어 받고
    /// 새 기록은 접속 중이 된다(옛 기록은 이탈 그대로 남는다 — 복제 목록이 등록 순서 인덱스라 지우지 않는다).
    /// 새 기록이 없으면 만든다(접속 콜백이 먼저 만든 CheckMask 0 기록이 있으면 거기에 합친다 — 체크는 되돌리지 않으므로 OR).
    /// 같은 쌍을 두 번 불러도 결과가 같다. 옛 id 를 모르거나 같은 id 면 false. 호출 뒤 <see cref="Evaluate"/> 로 재판정.
    /// 개인 뒤 배리어 마스크는 이 모델 밖(MapSceneDirector) — 재합류자는 첫 스폰에서 다시 출발하므로 디렉터가 0 으로 둔다.
    /// </summary>
    public bool RebindPlayer(ulong oldClientId, ulong newClientId)
    {
        if (oldClientId == newClientId || !_players.TryGetValue(oldClientId, out MapScenePlayerProgress old)) return false;

        old.Connected = false;
        _players[oldClientId] = old;

        if (!_players.TryGetValue(newClientId, out MapScenePlayerProgress p))
        {
            p = new MapScenePlayerProgress { ClientId = newClientId };
            _order.Add(newClientId);
        }

        p.CheckMask |= old.CheckMask;
        p.PermanentDead = old.PermanentDead;
        p.Connected = true;
        _players[newClientId] = p;
        return true;
    }

    public bool SetPermanentDead(ulong clientId, bool dead) => Mutate(clientId, (ref MapScenePlayerProgress p) =>
    {
        if (p.PermanentDead == dead) return false;
        p.PermanentDead = dead;
        return true;
    });

    public bool TryGetPlayer(ulong clientId, out MapScenePlayerProgress progress) => _players.TryGetValue(clientId, out progress);

    public int ParticipantCount
    {
        get
        {
            int n = 0;
            foreach (MapScenePlayerProgress p in _players.Values) if (p.IsParticipant) n++;
            return n;
        }
    }

    // ─── 보고 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 체크 항목 완료 보고(항목마다 1번이면 완료 — 되돌리지 않는다, 사망·부활 무관).
    /// 바뀐 게 있으면 true(중복·모르는 id·이미 완료는 false). 게이트 판정은 <see cref="Evaluate"/> 에서.
    /// </summary>
    public bool ReportCheck(ulong clientId, MapSceneCheck check) => Mutate(clientId, (ref MapScenePlayerProgress p) =>
    {
        if (p.Has(check)) return false;
        p.CheckMask |= 1 << (int)check;
        return true;
    });

    public int GetCounter(MapSceneCounter c) => _counters[(int)c];
    public int GetCounterTarget(MapSceneCounter c) => _counterTargets[(int)c];

    /// <summary>카운터 증가(목표에서 멈춤). 바뀌면 true.</summary>
    public bool AddCounter(MapSceneCounter c, int amount = 1)
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
    public bool SetStep(MapSceneStep step)
    {
        if (step <= Step) return false;
        MapSceneStep old = Step;
        Step = step;
        StepChanged?.Invoke(old, step);
        return true;
    }

    /// <summary>
    /// [전멸 리스폰 — 할 일 30 (2)] 보스 재도전 되감기. 단계·게이트가 '앞으로만' 이라는 규칙의 유일한 예외.
    /// G10(보스 진입·패드)이 열려 있을 때만 — 단계 Finished/BossEnter → <see cref="MapSceneStep.BossEnter"/>, G11(보스방 도착) 닫기(열림·교착·보류 비트 모두).
    /// G1~G10·카운터·개인 체크는 그대로. 그래서 보스방에 다시 도착하면 G11 이 다시 열리고 그 뒤 사망은 다시 목숨 차감 대상이다(S11).
    /// G10 이 닫혀 있으면 아무것도 안 하고 false. 바뀐 게 없으면(이미 BossEnter·G11 닫힘) false.
    /// </summary>
    public bool RewindForBossRetry()
    {
        if (!IsGateOpen(MapSceneGate.G10_BossEnterAndPad)) return false;

        int g11 = 1 << (int)MapSceneGate.G11_BossRoom;
        bool changed = (OpenedGateMask & g11) != 0 || (RequestedGateMask & g11) != 0;
        OpenedGateMask &= ~g11;
        TimeoutGateMask &= ~g11;
        RequestedGateMask &= ~g11;
        _firstCompleteAt[(int)MapSceneGate.G11_BossRoom] = -1d;

        if (Step != MapSceneStep.BossEnter)
        {
            MapSceneStep old = Step;
            Step = MapSceneStep.BossEnter;
            StepChanged?.Invoke(old, Step);
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// 서버가 직접 여는 게이트(G3·G6·G8·G9·G11 — 존 클리어·자막 종료·보스방 도착 등). 개인·카운터 게이트도 강제로 열 수 있다
    /// (타이머 만료 자동 획득 같은 예외 경로). 순서가 안 맞으면(앞 게이트 닫힘) false.
    /// </summary>
    public bool TryOpenGate(MapSceneGate g, MapSceneGateOpenReason reason = MapSceneGateOpenReason.Manual)
    {
        if (IsGateOpen(g) || !IsGateEligible(g)) return false;

        OpenedGateMask |= 1 << (int)g;
        RequestedGateMask &= ~(1 << (int)g);
        HeldGateMask &= ~(1 << (int)g);        // 다른 길로 열렸으면 보류도 끝(연출 쪽이 GateOpened 로 정리한다)
        ReadyHeldGateMask &= ~(1 << (int)g);
        if (reason == MapSceneGateOpenReason.Timeout) TimeoutGateMask |= 1 << (int)g;
        GateOpened?.Invoke(g, reason);
        return true;
    }

    /// <summary>
    /// 조건이 이미 충족된 게이트를 <b>열거나, 차례가 아니면 보류</b>한다(존 클리어 — S4).
    /// 차례(앞 게이트 열림)면 즉시 <see cref="TryOpenGate"/>(Manual), 아니면 <see cref="RequestedGateMask"/> 에 남겨
    /// 앞 게이트가 열리는 <see cref="Evaluate"/> 에서 연다 — 조건은 다시 보지 않는다(처치는 되돌릴 수 없다).
    /// 반환: 지금 열었으면 true, 보류했거나 이미 열림/이미 보류면 false.
    /// </summary>
    public bool RequestGate(MapSceneGate g)
    {
        if (IsGateOpen(g)) return false;
        if (TryOpenGate(g)) return true;
        RequestedGateMask |= 1 << (int)g;
        return false;
    }

    public bool IsGateRequested(MapSceneGate g) => (RequestedGateMask & (1 << (int)g)) != 0;

    // ─── 보류(연출이 끝나야 여는 게이트 — PLAN-zone4-intro-cinematic) ─────────────

    /// <summary>보류 중인 게이트(bit = <see cref="MapSceneGate"/>). <see cref="Evaluate"/> 가 열 조건을 만나도 열지 않는다.</summary>
    public int HeldGateMask { get; private set; }
    /// <summary>보류 중에 열 조건이 찬 게이트(bit). 연 사유는 <see cref="_heldReason"/>.</summary>
    public int ReadyHeldGateMask { get; private set; }
    private readonly MapSceneGateOpenReason[] _heldReason = new MapSceneGateOpenReason[GateCount];

    /// <summary>보류 중인 게이트의 열 조건이 처음 찼다(한 번). 인자 = 게이트·원래 열렸을 사유(전원 완료·교착 방지 등).</summary>
    public event Action<MapSceneGate, MapSceneGateOpenReason> GateReadyWhileHeld;

    public bool IsGateHeld(MapSceneGate g) => (HeldGateMask & (1 << (int)g)) != 0;
    public bool IsGateReadyHeld(MapSceneGate g) => (ReadyHeldGateMask & (1 << (int)g)) != 0;

    /// <summary>
    /// 게이트를 보류한다(4구역 소개 연출 — G7). 이미 열렸으면 false. 보류해도 <see cref="TryOpenGate"/>(서버 직접·디버그 강제)는 막지 않는다 —
    /// 보류는 <see cref="Evaluate"/> 의 자동 열림만 멈춘다.
    /// </summary>
    public bool HoldGate(MapSceneGate g)
    {
        if (IsGateOpen(g) || IsGateHeld(g)) return false;
        HeldGateMask |= 1 << (int)g;
        return true;
    }

    /// <summary>
    /// 보류를 푼다. 열 조건이 이미 찼으면 그때 기억한 사유로 <b>지금 연다</b>(true). 아직 안 찼으면 보류만 풀고 false — 다음 <see cref="Evaluate"/> 가 평소처럼 연다.
    /// </summary>
    public bool ReleaseHeldGate(MapSceneGate g)
    {
        if (!IsGateHeld(g)) return false;
        int bit = 1 << (int)g;
        HeldGateMask &= ~bit;
        if ((ReadyHeldGateMask & bit) == 0) return false;
        ReadyHeldGateMask &= ~bit;
        return TryOpenGate(g, _heldReason[(int)g]);
    }

    // Evaluate 가 열려던 순간. 보류 중이면 준비됨만 기록하고 false(열지 않음), 아니면 연다.
    private bool OpenOrHold(MapSceneGate g, MapSceneGateOpenReason reason)
    {
        if (!IsGateHeld(g)) return TryOpenGate(g, reason);
        int bit = 1 << (int)g;
        if ((ReadyHeldGateMask & bit) == 0)
        {
            ReadyHeldGateMask |= bit;
            _heldReason[(int)g] = reason;
            GateReadyWhileHeld?.Invoke(g, reason);
        }
        return false;
    }

    /// <summary>
    /// 개인·카운터 게이트를 재판정한다. 보고·인원 변화 직후와 주기적으로(교착 방지 시계) 부른다.
    /// 한 번 부를 때 열 수 있는 게이트를 순서대로 모두 연다.
    /// </summary>
    public void Evaluate(double now)
    {
        for (int i = 0; i < GateCount; i++)
        {
            var g = (MapSceneGate)i;
            if (IsGateOpen(g)) continue;
            if (!IsGateEligible(g)) return;   // 순서 — 이 뒤는 볼 필요가 없다

            // 보류된 요청(존 클리어가 차례보다 먼저 옴) — 차례가 왔으니 연다. 게이트 종류와 무관.
            // OpenOrHold 가 false = 연출 보류(HoldGate) 중 — 뒤 게이트는 볼 필요가 없다.
            if (IsGateRequested(g)) { if (OpenOrHold(g, MapSceneGateOpenReason.Manual)) continue; return; }

            if (TryGetGateCounter(g, out MapSceneCounter counter))
            {
                if (GetCounter(counter) < GetCounterTarget(counter)) return;
                if (OpenOrHold(g, MapSceneGateOpenReason.CounterReached)) continue;
                return;
            }

            if (!IsPersonalGate(g)) return;   // 수동 게이트 — 서버가 열 때까지 대기

            CountCompletion(g, out int done, out int total);
            if (total == 0) return;   // 참가자 0명 — 자동으로 열지 않는다

            if (done == total) { if (OpenOrHold(g, MapSceneGateOpenReason.AllDone)) continue; return; }
            if (!HasDeadlockTimeout(g)) return;   // G1 — 시간으로 넘기지 않는다

            if (done > 0 && _firstCompleteAt[i] < 0d) _firstCompleteAt[i] = now;

            if (_firstCompleteAt[i] >= 0d && now >= _firstCompleteAt[i] + DeadlockSeconds)
            {
                if (OpenOrHold(g, MapSceneGateOpenReason.Timeout)) continue;
                return;
            }
            return;
        }
    }

    // ─── 대기 표시(§2.1 "동료 대기 중 (1/2) · 자동 진행 0:45") ──────────────────

    /// <summary>
    /// 지금 열 차례인 개인 게이트의 대기 정보. 그런 게이트가 없으면 false.
    /// <paramref name="autoAdvanceAt"/> = 교착 방지 자동 진행 시각(첫 완료자 전·교착 방지 없는 G1 이면 -1).
    /// </summary>
    public bool TryGetPendingPersonalGate(out MapSceneGate gate, out int done, out int total, out double autoAdvanceAt)
    {
        for (int i = 0; i < GateCount; i++)
        {
            var g = (MapSceneGate)i;
            if (IsGateOpen(g)) continue;
            if (!IsGateEligible(g) || !IsPersonalGate(g)) break;
            if (IsGateReadyHeld(g)) break;   // 전원 완료 — 연출(보류)을 기다리는 중이지 동료를 기다리는 게 아니다

            gate = g;
            CountCompletion(g, out done, out total);
            autoAdvanceAt = HasDeadlockTimeout(g) && _firstCompleteAt[i] >= 0d ? _firstCompleteAt[i] + DeadlockSeconds : -1d;
            return true;
        }

        gate = default;
        done = total = 0;
        autoAdvanceAt = -1d;
        return false;
    }

    /// <summary>개인 게이트 g 에 대해 그 플레이어가 할 일을 다 했는가.</summary>
    public bool IsComplete(ulong clientId, MapSceneGate g)
    {
        int mask = RequiredMaskOf(g);
        return mask != 0 && _players.TryGetValue(clientId, out MapScenePlayerProgress p) && (p.CheckMask & mask) == mask;
    }

    private void CountCompletion(MapSceneGate g, out int done, out int total)
    {
        int mask = RequiredMaskOf(g);
        done = total = 0;
        foreach (MapScenePlayerProgress p in _players.Values)
        {
            if (!p.IsParticipant) continue;
            total++;
            if ((p.CheckMask & mask) == mask) done++;
        }
    }

    private delegate bool Mutator(ref MapScenePlayerProgress p);

    private bool Mutate(ulong clientId, Mutator m)
    {
        if (!_players.TryGetValue(clientId, out MapScenePlayerProgress p)) return false;
        if (!m(ref p)) return false;
        _players[clientId] = p;
        return true;
    }
}
