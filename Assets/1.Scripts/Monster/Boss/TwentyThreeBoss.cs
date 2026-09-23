using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// 보스 23호 — MonsterBase 코드 FSM 위에 "공격 6종 선택기 + 페이즈"만 얹는다.
//
// 이동/추격/타게팅/피격/그로기/사망/디스폰/상태복제/HitFlash 는 전부 base 그대로다.
// 확장 표면은 훅뿐이고, MonsterState 에 값을 추가하지 않는다 —
// 중간보스 3종(WallBot·GauntletBot·SpinnerBot)이 전부 이 안에서 끝냈다(WallBot 은 C# 0줄).
//
// 정본: Docs/tech/boss-rebuild-standard.md (§2 훅 / §2.1 관용구 3 / §10 SO 설계)
//
// 현재 슬라이스 = S1(FSM 골격 + 공격 선택기). 각 공격의 실제 기믹은 뒤 슬라이스다:
//   S2 근접 3종 히트 정밀화(앵커 전환·어퍼 Airborne) / S3 카운터 창 / S4 Grab 체인 /
//   S5 Dash 캐리-푸시 / S6 Jump 장판 / S7 페이즈 시퀀스(송전기)
// 미구현 공격은 애니만 재생되고 히트 시 **1회 경고**를 남긴다(조용한 실패 금지).
public class TwentyThreeBoss : MonsterBase, IBossEntranceAnimation
{
    // 서버·클라 공통(프리팹에 직렬화된 data 를 캐스팅) — 클라도 애니 상태명을 조회해야 한다.
    BossDataSO _boss;

    // 서버 전용 런타임.
    BossAttackEntry _currentEntry;   // 지금 수행 중인 공격 행
    int _lastSlot = NoAttack;        // 직전에 실제로 쓴 슬롯(선택기 감쇠용)
    int _consecutive;                // 같은 슬롯 연속 사용 횟수
    float[] _weightBuffer;           // 룰렛 가중치(매 틱 재사용 — 할당 없음)
    int _warnedAttackMask;           // 미구현 공격 경고 1회 가드

    // 히트박스 앵커 — 이름 → ColliderInfo. 공격마다 판정 형상이 다르므로 히트 직전에 갈아끼운다.
    Dictionary<string, ColliderInfo> _anchors;
    ColliderInfo _defaultAnchor;     // 프리팹에 배선된 원본(앵커 미지정 공격이 되돌아갈 자리)

    // 카운터 창 — Server write / Everyone read. **판정은 서버, 표현은 각 피어**(정본 §6).
    // 클라 예측 없음: 오판정하면 그로기가 클라마다 갈린다.
    readonly NetworkVariable<bool> _counterWindow = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // [G6] 인터럽트 성공 리액션이 **오른쪽인가**. 잡기는 항상 오른쪽, 돌진은 L·R 난수다(팀장 확정 R1).
    // 🔴 RPC 가 아니라 **상태 복제**로 보낸다 — 난수를 피어마다 뽑으면 화면이 갈리고,
    //    RPC 는 늦게 들어온 클라에게 재전달되지 않는다.
    readonly NetworkVariable<bool> _hitReactionRight = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>지금 카운터로 끊을 수 있는가(표현·디버그용 읽기 전용).</summary>
    public bool CounterWindowOpen => _counterWindow.Value;

    // 🔴 배열이다. 텔레그래프는 **여러 개가 동시에** 붙는다(방향 표시 링 + 전신 틴트 + 나중에 VFX).
    //    하나만 집으면(GetComponentInChildren<T> 단일) 나머지가 조용히 안 돈다.
    IBossTelegraph[] _telegraphs;
    int _counterGroggyCount;         // 보스 자체 그로기 카운트 — base 것은 AutoHitReactions=false 라 안 돈다

    /// <summary>보스 데이터(읽기 전용). 방향 표시기가 각도를 판정과 **같은 출처**에서 읽기 위해 노출한다.</summary>
    public BossDataSO Data => _boss;

    // 🔴 base 의 자동 피격 반응 3종을 전부 끈다: Hit 경직 · isInterruptAttack 그로기 누적 · Knockback.
    //    정본 §1.1 · §4 — Hit 은 카운터 성공 전용이고, 그로기는 인터럽트 스킬·송전기만 유발하며,
    //    보스는 밀리지 않는다. 데미지·사망 판정과 HitFlash(피격 색)는 그대로 돈다.
    protected override bool AutoHitReactions => false;

    // 카운터 리액션(getowned) 길이. AutoHitReactions=false 라 base 의 hitStunDuration 은 보스에서
    // 달리 쓰이지 않으므로 그 값을 리액션 길이로 재사용한다(죽은 필드를 재사용한다).
    float HitReactionDuration => data != null ? Mathf.Max(0.05f, data.hitStunDuration) : 0.4f;

    // ─── 카운터 선딜 게이트 (서버 전용 판정 + 전 피어 자세 홀드) ───────────────
    // 공격 발사는 "창 타이머 만료 AND 애니 준비 도달" 두 사건이 다 서야 일어난다(BossCounterWindupGate).
    readonly BossCounterWindupGate _counterWindup = new BossCounterWindupGate();
    bool _warnedCounterTimerBeforeAnimation;
    float _counterWindupStartedAt;   // 진단용 — 준비 신호가 실제로 몇 초 만에 오는지 재려고 둔다

    // 자세 홀드는 **각 피어의 로컬 상태**다(애니메이터는 복제되지 않는다).
    // 서버가 RPC 로 걸고 풀며, 아래 두 값은 그 피어에서만 의미가 있다.
    bool _counterAnimatorHeldLocally;
    float _counterAnimatorResumeSpeed = 1f;

    // 체인 예산(_stateTimer)의 여유분. 예산은 **데드락 안전망**이지 정밀 종료 기준이 아니다 —
    // 실제 길이와 정확히 같게 잡으면 프레임 오차 한 번에 안전망이 터진다.
    // 🔴 실측(2026-09-03): 창 1.5초 + 홀드 1.5초로 여유가 0 이 되자 Dash 가 Recovery 에서
    //    타임아웃하기 시작했다(2회). 정상 종료는 애니 이벤트가 만들고, 이 값은 그 뒤를 받친다.
    const float ChainBudgetSlack = 0.5f;

    /// 지금 공격 행의 카운터 창 길이. 창을 여는 공격이 아니면 0.
    float CounterWindowDuration => _currentEntry != null && _currentEntry.opensCounterWindow
        ? Mathf.Clamp(_currentEntry.counterWindowDuration, 0f, 2f)
        : 0f;

    // ─── Grab 체인 (서버 전용) ───────────────────────────────────────
    BossAttackPhase _attackPhase = BossAttackPhase.None;
    float _attackPhaseTimer;
    float _grabTickTimer;
    Player _grabbed;                 // 붙잡고 있는 플레이어(없으면 null)

    // [G5] 끌어당겨 **붙잡지는 않은** 플레이어들. 🔴 모든 이탈 경로에서 반드시 놓아 줘야 한다 —
    //      안 놓으면 이동 권한을 잃은 채 영구히 갇힌다(Grab·돌진 캐리와 정확히 같은 사고).
    readonly System.Collections.Generic.List<Player> _pulledPlayers =
        new System.Collections.Generic.List<Player>();
    int _grabSlamsLeft;              // 남은 내려치기 횟수
    Collider[] _grabBuffer;
    bool _warnedThrowDisplacement;

    // ─── JumpAttack (서버 + 각 피어 연출) ─────────────────────────────
    Vector3 _jumpArrivePoint;
    Collider[] _aoeBuffer;                          // 최원거리 탐색 · 착지 AoE 공용
    readonly HashSet<Unit> _aoeHits = new HashSet<Unit>();
    Renderer[] _modelRenderers;                     // 체공 중 숨길 모델 렌더러(animator 하위만)
    // 착지 예고 2개. 둘 다 AoeTelegraph 프리팹이 아니라 EffectManager 루프 이펙트다(카탈로그 엔트리).
    //
    // ⚠️ 루프 핸들은 버리면 풀 인스턴스가 영원히 돌아오지 않는다. 핸들은 피어마다 자기 EffectManager 에서
    //    발급받으므로 이 필드도 피어 로컬이다 — 재생 전·해제 시·파괴 시 세 곳에서 모두 회수한다.
    EffectHandle _boundaryHandle = EffectHandle.None;    // 경계 원 — 어디에 떨어지는가(고정 크기)
    EffectHandle _indicatorHandle = EffectHandle.None;   // 차오르는 원 — 언제 떨어지는가(0.1 → AoE 점증)
    bool _warnedNoBoundaryEntry;
    bool _warnedNoIndicatorEntry;
    bool _warnedNoImpactEntry;

    [Header("이펙트 재생기")]
    // ─── Grab 팔 전기 펄스 ────────────────────────────────────────────
    // 🔴 켜고 끄는 것은 **이 코드**다(애니 클립 이벤트가 아니다). 그랩 체인이 카운터·그로기·사망으로
    //    끊기는 경로가 여럿이라, 끄는 책임을 클립에 맡기면 하나만 빠져도 팔에 전기가 영영 남는다
    //    (SpinnerBot 에서 실제로 겪은 함정 — 컴파일도 테스트도 안 깨진다).
    //    그래서 시작·종료를 그랩 상태 전이와 **같은 자리**에 둔다.
    [Tooltip("Grab 동안 팔(어깨→팔꿈치→손)을 훑는 전기 펄스. 비워두면 연출만 빠지고 나머지는 그대로 돈다")]
    [SerializeField] EffectPathPlayer grabPulse;
    bool _warnedNoGrabPulse;

    // ─── 근접 명중 타격 연출 ──────────────────────────────────────────
    // 🔴 **맞았을 때만** 나온다. 헛스윙에는 안 나온다 — 애니 이벤트로는 못 가른다(클립은 맞았는지 모른다).
    //    손마다 소켓이 다르므로 공격별로 따로 문다. 어퍼가 어느 손이면 그 손 것을 그대로 물리면 된다.
    [Tooltip("좌훅 명중 시 타격 연출(왼손 소켓)")]
    [SerializeField] EffectSocketPlayer leftHookHit;
    [Tooltip("우훅 명중 시 타격 연출(오른손 소켓)")]
    [SerializeField] EffectSocketPlayer rightHookHit;
    [Tooltip("어퍼 명중 시 타격 연출. 훅과 같은 손이면 위와 같은 컴포넌트를 물리면 된다")]
    [SerializeField] EffectSocketPlayer upperHit;
    bool _warnedNoHitEffect;

    // ─── 카운터 성공 연출 ─────────────────────────────────────────────
    // 🔴 애니메이션 이벤트로는 못 낸다. 카운터 성공은 클립이 아니라 **플레이어의 인터럽트 공격**이
    //    만드는 사건이라, 어느 프레임에 일어날지 클립이 알 수 없다 — 코드가 직접 몬다
    //    (중간보스 3종과 같은 규약).
    [Tooltip("카운터(인터럽트) 성공 순간의 섬광. 비워두면 연출만 빠진다")]
    [SerializeField] EffectSocketPlayer interruptFlash;
    bool _warnedNoInterruptFlash;

    // ─── 레이지 돌진 루프 연출 ────────────────────────────────────────
    // 🔴 명중과 무관하다 — 헛돌진에도 나온다. **돌진 1회 단위**로 켜고 끈다:
    //    레이지는 rageDashCount 번 연타인데, 연타 사이 간격은 서 있는 구간이라 연출도 끊긴다.
    [Tooltip("레이지 돌진 중 재생할 루프 연출(FX_Rage_Smash). 비워두면 연출만 빠진다")]
    [SerializeField] EffectSocketPlayer rageSmash;
    bool _warnedNoRageSmash;
    GrabController _grabController;   // 그랩 소켓 보유자(레거시). 연출 좌표만 빌린다
    bool _warnedNoGrabSocket;
    bool _warnedNoGrabbedElectric;
    bool _warnedNoThrowEntry;

    // ─── 페이즈 시퀀스 (송전기 / 레이지) ──────────────────────────────
    bool _pendingPhaseSequence;                     // 페이즈 통과 후 "행동이 끝나면 시작할 것"
    IBossChargeSequence _charge;
    AreaZone _chargeZone;
    bool _warnedNoCharge;

    // 차징 번개구슬(인트로 → 지속 → 사그라짐/깨짐). 🔴 보스가 아니라 이 컴포넌트가 단계를 몬다 —
    // MonsterBase.Update 가 서버에서만 돌아(`if (!IsServer) return`) 인트로→지속 전환 타이머를
    // 보스 안에 둘 수 없다. 여기서는 시작·종료만 RPC 로 알린다.
    [Tooltip("송전기 차징 번개구슬. 비워두면 연출만 빠지고 차징 자체는 그대로 돈다")]
    [SerializeField] EffectStagePlayer chargeBall;
    bool _warnedNoChargeBall;
    int _rageRemaining;
    Vector3 _rageDashDir;
    bool _rageDashing;                              // RageDash phase 안의 구간 구분(돌진 중 / 간격 대기)

    // ─── 돌진(S5) ─────────────────────────────────────────────────────
    // 🔴 끌고 가는 대상은 **1명뿐**이다(라인하르트 핀과 같은 규칙). 여러 명을 끌면 각자의
    //    followTarget 이 같은 지점을 가리켜 겹쳐 쌓이고, 해제 누락 위험도 인원수만큼 늘어난다.
    Player _dashCarried;
    Vector3 _dashDir;
    bool _dashBlockedAhead;                         // 목적지가 보행면 끝에서 잘렸나(= 벽에 처박는다)
    Vector3 _dashDestination;                       // 클램프된 목적지. 도착 판정의 기준
    float _dashPrevStopDistance = -1f;              // 돌진 전 stoppingDistance(복원용). -1 = 저장 안 됨
    float _dashPrevAcceleration = -1f;              // 돌진 전 acceleration(복원용). -1 = 저장 안 됨
    bool _dashPrevAutoBraking;                      // 돌진 전 autoBraking(복원용)

    // ─── 전진 공격(Lunge, G1) ─────────────────────────────────────────
    // 훅·어퍼가 "전진하면서" 때리게 하는 공용 인프라. 돌진(S5)과 **같은 이동 경로**를 쓴다
    // (StartDashMove / EndDashMove / DashDestinationReached) — 둘은 서로 다른 공격이라 겹치지 않는다.
    // 그래서 `_dashDir`·`_dashDestination` 을 그대로 쓰고, 여기서는 진행 여부만 따로 든다.
    //
    // 🔴 히트 윈도우를 **경로와 끝점이 공유**한다. 그래서 여는 것은 BeginLunge 하나뿐이고
    //    닫는 것은 PerformAttackHit(끝점 판정 **뒤**)과 AbortAttackChain 둘이다.
    //    끝점 판정 전에 닫으면 경로에서 맞은 사람이 끝점에서 또 맞는다(팀장 확정: 1인 1회).
    bool _lunging;                                  // 전진 **이동**이 진행 중인가
    bool _lungeHitWindowOpen;                        // 히트 윈도우를 이 공격이 열었나(중복 close 방지)

    // 🔴 **방향 고정은 이동 여부와 다른 축이다**(2026-09-16, Codex 교차검증에서 발견).
    //    `_lunging` 으로 회전을 막았더니, 전진이 히트보다 **먼저 끝나는 순간** 고정이 풀려
    //    base 의 선딜 조준이 다시 돌아 **끝점 부채꼴이 타깃 쪽으로 회전**했다.
    //    예고를 아무리 정확히 그려도 판정이 그 뒤에 돌아 버리므로 예고가 거짓말이 된다.
    //    전진 시작 ~ 히트까지 **통째로** 잠근다.
    bool _attackFacingLocked;

    // 이번 공격에서 **전진 경로로** 피해를 준 수. 끝점 판정과 합쳐 "이 공격이 맞았는가"를 판단한다
    // (경로로만 맞힌 공격이 헛스윙으로 보이지 않게). EndLunge 가 0 으로 되돌린다.
    int _lungePathHits;

    // 🔴 돌진 중 가속도. 레거시 BT 의 `SetAgentDashModeAction` 이 쓰던 값과 같다(999 / autoBraking off).
    //    FSM 재작성판이 speed 와 stoppingDistance 만 승계하고 **이 둘을 빠뜨려서 돌진이 전진하지 않았다.**
    //    산수: 프리팹 acceleration 8m/s² 로는 0.7초 동안 5.6m/s 까지밖에 못 올라가 약 1.96m 만 간다
    //    (목표는 moveSpeed 2.5 × 6 = 15m/s, 최대 16m). autoBraking 까지 켜져 있어 목적지 근처에서 더 준다.
    //    → "애니메이션만 돌고 안 나간다"로 보인다.
    const float DashAcceleration = 999f;

    const float DashCarryProbeRadius = 1.2f;        // 캐리 판정 구 반경(보스 정면 offset 지점 기준)
    const float DashCarryWallMargin = 0.6f;         // 벽 앞 추가 여유 — 플레이어 캡슐 반경분
    const float DashArriveEpsilon = 0.35f;          // 목적지 도착으로 볼 수평 거리

    // ─── Wells (23호에 탑승) ──────────────────────────────────────────
    // 🔴 Wells 는 **스폰되지 않는 중첩 NetworkObject** 라 자기 NetworkVariable 을 가질 수 없다.
    //    그래서 지속 상태(Idle/Groggy/Dead)를 **23호의 NetworkObject 에 실어** 복제한다(정본 §10.1).
    //    투척은 **일회성 이벤트**라 NetworkVariable 로 못 싣는다(같은 값이면 OnValueChanged 가 안 뜬다)
    //    → ClientRpc 로 보낸다. 이 프로젝트의 "지속=복제 / 일회성=RPC" 분리와 같다.
    readonly NetworkVariable<BossWellsState> _wellsState = new NetworkVariable<BossWellsState>(
        BossWellsState.Idle,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    BossWells _wells;

    /// <summary>현재 다단계 공격 단계(디버그·확장용 읽기 전용).</summary>
    public BossAttackPhase AttackPhase => _attackPhase;

    /// <summary>0 = 1페이즈(개전). 임계를 통과할 때마다 1 오른다. 회복해도 내려가지 않는다.</summary>
    public int CurrentPhase { get; private set; }

    // 현재 페이즈에 해당하는 배수 행. CurrentPhase 0 이면 배수 없음(null).
    BossPhaseEntry ActivePhase =>
        _boss != null && _boss.phases != null && CurrentPhase > 0 && CurrentPhase <= _boss.phases.Length
            ? _boss.phases[CurrentPhase - 1]
            : null;

    float PhaseDamageMultiplier => ActivePhase != null ? Mathf.Max(0f, ActivePhase.damageMultiplier) : 1f;

    // 페이즈 이동속도 배수 — base 의 SeekBoss 가 chaseSpeed 에 곱한다.
    protected override float ChaseSpeedMultiplier =>
        ActivePhase != null ? Mathf.Max(0.1f, ActivePhase.speedMultiplier) : 1f;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn(); // 참조 자동 탐색 + (서버)ServerInitialize + 스폰 시점 상태 애니 반영

        _boss = data as BossDataSO;
        if (_boss == null)
        {
            Debug.LogError(
                $"{name}: BossDataSO 가 필요하다(현재 {(data == null ? "null" : data.GetType().Name)}) — 보스 로직을 끈다.",
                this);
            enabled = false;
            return;
        }

        // 카운터 창 표현은 모든 피어에서 돈다(서버가 창을 쓰고, 각 피어가 텔레그래프를 구동).
        _counterWindow.OnValueChanged += OnCounterWindowChanged;
        ResolveTelegraphs();

        // Wells 는 모든 피어에서 로컬 애니메이터를 구동한다(상태는 이 NetworkObject 가 복제).
        _wells = GetComponentInChildren<BossWells>(true);
        _wellsState.OnValueChanged += OnWellsStateChanged;
        if (_wells != null)
            _wells.PlayState(_wellsState.Value); // 늦게 접속한 클라도 현재 상태를 받는다

        if (!IsServer)
            return;

        // 공격 슬롯 수 = 테이블 행 수. 행마다 쿨을 등록한다(0 이면 base 의 1/attackSpeed 로 폴백).
        int count = _boss.attacks != null ? _boss.attacks.Length : 0;
        ConfigureAttackSlots(count);
        for (int i = 0; i < count; i++)
            SetAttackCooldown(i, _boss.attacks[i].cooldown);

        CacheHitboxAnchors();
        SetupWellsServer();
        ValidateContract();
    }

    public override void OnNetworkDespawn()
    {
        _counterWindow.OnValueChanged -= OnCounterWindowChanged;
        _wellsState.OnValueChanged -= OnWellsStateChanged;

        // Wells 콜백이 파괴된 보스를 붙잡지 않게 끊는다(Wells 는 MonoBehaviour 라 수명이 다르다).
        if (_wells != null)
        {
            _wells.ThrowCycleElapsed = null;
            _wells.ThrowRequested = null;
            _wells.SetSuppressed(true);
        }

        // 디스폰 시 잡고 있던 플레이어를 반드시 놓는다 — 안 놓으면 풀어 줄 주체가 사라져 영구 구속된다.
        // 체공 중이었다면 메시도 되살린다(꺼진 채 남으면 다음 스폰까지 투명하다).
        AbortAttackChain();

        base.OnNetworkDespawn();
    }

    // NetworkBehaviour.OnDestroy 는 virtual 이고 자체 정리를 한다 — 반드시 override + base 호출.
    public override void OnDestroy()
    {
        // 🔴 예고 장판은 풀 인스턴스라 파괴가 아니라 **반납**이다(보스 자식도 아니라 함께 죽지도 않는다).
        //    여기서 안 돌려주면 보스가 죽을 때마다 풀에서 두 칸씩 새어 나가고, 결국 예고가 아예 안 뜬다.
        ReleaseJumpTelegraphs();

        base.OnDestroy();
    }

    #region 계약 검증 (죽은 설정값 방지)
    // 🔴 애니메이터·앵커 접근이 전부 graceful 이라 이름이 틀려도 예외가 안 난다.
    //    그래서 이 프로젝트에는 이미 조용히 무시되는 설정값이 9건 쌓여 있다(정본 §6).
    //    같은 함정을 또 파지 않기 위해 스폰 시 전수 검증해 LogError 를 남긴다.
    //
    // ⚠️ 정본 §3.2 는 "Awake 에서 검증"이라고 적혀 있으나 Awake 에는 animator 참조가 아직 없다
    //    (base 가 OnNetworkSpawn 에서 GetComponentInChildren 으로 채운다). 그래서 여기서 한다.
    void ValidateContract()
    {
        if (data.archetype != MonsterArchetype.Boss)
            Debug.LogError(
                $"{name}: archetype 이 {data.archetype} 이다 — Boss 여야 거리창+가중치 선택기(SeekBoss)가 돈다.",
                this);

        if (data.hasSuperArmorWhileAttacking)
            Debug.LogError(
                $"{name}: hasSuperArmorWhileAttacking 이 켜져 있다 — base 가 전 공격에 슈퍼아머를 걸어 " +
                "공격별 superArmor 플래그가 무의미해진다. SO 에서 끄고 테이블에서 공격별로 제어할 것.",
                this);

        if (_boss.attacks == null || _boss.attacks.Length == 0)
        {
            Debug.LogError($"{name}: 공격 테이블이 비어 있다 — 보스가 아무 공격도 못 한다.", this);
            return;
        }

        // 타겟 규칙 계약 — FarthestPlayer 는 Jump 전용이다(확정 스펙).
        // 🔴 다른 행에 켜면 그 공격이 조용히 후열을 노리게 되는데, 거리창까지 함께 보므로
        //    "가까운 사람만 후보인데 먼 사람을 노린다"는 앞뒤 안 맞는 조합이 된다.
        foreach (BossAttackEntry e in _boss.attacks)
        {
            if (e == null || e.attackTargeting != BossAttackTargeting.FarthestPlayer) continue;
            if (e.attackId == BossAttackId.Jump) continue;

            Debug.LogError(
                $"{name}: {e.attackId} 행의 attackTargeting 이 FarthestPlayer 다 — 최원거리 타겟은 " +
                "Jump 전용이다(확정 스펙). 거리창을 쓰는 공격과 조합하면 앞뒤가 안 맞는다.", this);
        }

        // 카운터 창 계약 — 창을 여는 행은 Grab·Dash 뿐이고, 길이는 (0, 2] 여야 한다.
        // 🔴 길이 0 은 "창이 있다"고 저작해 놓고 실제로는 게이트가 붙잡을 시간이 0 인 상태라
        //    조용히 무효가 된다. 그래서 opensCounterWindow 와 짝이 맞는지 여기서 잡는다.
        foreach (BossAttackEntry e in _boss.attacks)
        {
            if (e == null || !e.opensCounterWindow) continue;

            if (e.attackId != BossAttackId.Grab && e.attackId != BossAttackId.Dash)
                Debug.LogError(
                    $"{name}: {e.attackId} 행에 opensCounterWindow 가 켜져 있다 — 카운터 창은 " +
                    "Grab·Dash 만 연다(확정 스펙). 훅·어퍼까지 열면 카운터가 상시 자원이 된다.", this);

            if (e.counterWindowDuration <= 0f || e.counterWindowDuration > 2f)
                Debug.LogError(
                    $"{name}: {e.attackId} 의 counterWindowDuration 이 {e.counterWindowDuration:0.##} 다 — " +
                    "(0, 2] 이어야 한다. 0 이면 창이 열려 있다고 저작해 놓고 실제로는 아무 효과가 없다.", this);
        }

        // 페이즈 임계는 내림차순(0.66 → 0.33)이어야 페이즈 계산이 성립한다.
        if (_boss.phases != null)
        {
            for (int i = 1; i < _boss.phases.Length; i++)
            {
                if (_boss.phases[i].hpThreshold < _boss.phases[i - 1].hpThreshold) continue;
                Debug.LogError(
                    $"{name}: phases[{i}].hpThreshold({_boss.phases[i].hpThreshold}) 가 앞 페이즈보다 크거나 같다 — " +
                    "내림차순으로 저작할 것(0.66 → 0.33).",
                    this);
            }
        }

        // 애니메이터 컨트롤러가 아직 없으면 전수 검증이 전부 오류로 도배된다 — 한 줄만 남기고 건너뛴다.
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            Debug.LogWarning($"{name}: Animator 컨트롤러가 없어 애니 계약 검증을 건너뛴다.", this);
            ValidateHitboxAnchors();
            return;
        }

        // 보스가 실제로 쓰는 것만 검증한다.
        // attackTrigger 는 검증 대상이 아니다 — 보스의 공격 애니는 ClientRpc CrossFade 로 재생하므로
        // PlayStateAnimation(Attack) 을 스킵한다(관용구 2). 그래서 값이 있으면 오해의 소지가 있다.
        if (!string.IsNullOrEmpty(data.attackTrigger))
            Debug.LogWarning(
                $"{name}: attackTrigger(\"{data.attackTrigger}\") 는 보스에서 쓰이지 않는다 " +
                "(공격 애니 = CrossFade 경로). 비워 두는 것이 맞다.",
                this);

        ValidateParam(data.animSpeedParam, nameof(data.animSpeedParam));
        ValidateParam(data.hitTrigger, nameof(data.hitTrigger));
        ValidateParam(data.groggyBool, nameof(data.groggyBool));
        ValidateParam(data.deathTrigger, nameof(data.deathTrigger));
        ValidateState(data.locomotionState, nameof(data.locomotionState));
        ValidateState(_boss.hitReactionState, nameof(_boss.hitReactionState));
        ValidateState(_boss.hitReactionStateRight, nameof(_boss.hitReactionStateRight));
        ValidateState(_boss.grabCatchState, nameof(_boss.grabCatchState));
        ValidateState(_boss.grabEndState, nameof(_boss.grabEndState));
        ValidateState(_boss.jumpTakeoffState, nameof(_boss.jumpTakeoffState));   // [G4]

        for (int i = 0; i < _boss.attacks.Length; i++)
        {
            BossAttackEntry e = _boss.attacks[i];
            if (e == null)
            {
                Debug.LogError($"{name}: attacks[{i}] 가 null 이다.", this);
                continue;
            }

            // 공격 행의 상태명은 **비어 있어도 에러**다. ValidateState 는 빈 값을 "의도적 미사용"으로
            // 건너뛰므로(SO 의 선택 필드용 규칙) 여기서 따로 잡아야 미저작 공격이 애니 없이 도는 것을 막는다.
            if (string.IsNullOrEmpty(e.animatorStateName))
            {
                Debug.LogError(
                    $"{name}: attacks[{i}]({e.attackId}).animatorStateName 이 비어 있다 — " +
                    "이 공격은 애니가 재생되지 않는다(판정만 나간다). 애니메이터 상태명을 저작할 것.",
                    this);
                continue;
            }
            ValidateState(e.animatorStateName, $"attacks[{i}]({e.attackId}).animatorStateName");

            // [G2-P] 준비동작은 **선택**이다 — 비어 있으면 얼리기 폴백이므로 에러가 아니다.
            //    다만 값이 있는데 상태가 없으면 예고가 통째로 안 보인다(조용한 실패) → ValidateState 가 잡는다.
            // 🔴 클립은 SVN(FBX) · 컨트롤러는 git 이라 **한쪽만 받은 사람에게는 여기서 걸린다.**
            //    그게 이 검증의 존재 이유다.
            ValidateState(e.prepStateName, $"attacks[{i}]({e.attackId}).prepStateName");
        }

        ValidateHitboxAnchors();
    }

    void ValidateParam(string param, string field)
    {
        if (string.IsNullOrEmpty(param)) return; // 비움 = 의도적 미사용
        AnimatorControllerParameter[] ps = animator.parameters;
        for (int i = 0; i < ps.Length; i++)
            if (ps[i].name == param) return;

        Debug.LogError($"{name}: {field}=\"{param}\" 파라미터가 애니메이터에 없다 — 조용히 무시된다.", this);
    }

    void ValidateState(string stateName, string field)
    {
        if (string.IsNullOrEmpty(stateName)) return;
        if (animator.HasState(0, Animator.StringToHash(stateName))) return;

        Debug.LogError($"{name}: {field}=\"{stateName}\" 상태가 애니메이터에 없다 — CrossFade 가 조용히 무시된다.", this);
    }

    // 앵커를 이름으로 1회 색인한다(히트마다 GetComponentsInChildren 을 돌리지 않기 위해).
    void CacheHitboxAnchors()
    {
        _defaultAnchor = meleeAttack != null ? meleeAttack.ColliderInfo : null;

        ColliderInfo[] found = GetComponentsInChildren<ColliderInfo>(true);
        _anchors = new Dictionary<string, ColliderInfo>(found.Length);

        for (int i = 0; i < found.Length; i++)
        {
            ColliderInfo ci = found[i];
            if (ci == null) continue;

            // 같은 이름이 둘이면 어느 쪽이 잡힐지 모른다 — 설정 결함이므로 소리를 낸다.
            if (_anchors.ContainsKey(ci.name))
            {
                Debug.LogError(
                    $"{name}: ColliderInfo 자식 이름 \"{ci.name}\" 이 중복이다 — 앵커 지정이 모호해진다. 이름을 유일하게 할 것.",
                    this);
                continue;
            }
            _anchors.Add(ci.name, ci);
        }
    }

    // ─── 어그로 주기 재선정 ────────────────────────────────────────────
    //
    // 🔴 시계는 **두 사건**에 묶여 있다(2026-09-03) — 그러지 않으면 "8초 주기"가 성립하지 않는다.
    //    ① 전투 시작(OnServerLogicResumed) ② 어그로가 실제로 갈아탄 순간(AdoptAggro / ShouldReacquireTarget).
    //    ⚠️ 초기값 0 을 그대로 두면 착지 시점에 `Time.time - 0` 이 이미 8초를 넘어 있어서
    //       FSM 이 깨어난 **첫 틱**에 재선정이 돈다("내려오자마자 어그로가 튄다" — 팀장 관찰).
    //
    // 🔴 2026-09-16 — 여기서 `float _lastRetargetTime;` 을 **다시 선언하고 있었다.**
    //    base(MonsterBase)에 같은 이름이 있어 Unity 가 "같은 필드가 두 번 직렬화된다" 에러를 냈고,
    //    실제로 시계가 둘로 갈려 있었다: base 는 `-1f`(교전 전) 센티넬에서 출발하는데 파생은 `0`
    //    이었고, base 가 세우는 갱신(MonsterBase 의 재선정·타깃획득)을 파생 오버라이드가 못 읽었다.
    //    이제 base 의 protected 필드 하나만 쓴다. 센티넬을 물려받으므로 아래 `< 0f` 가드가 필수다.

    /// <summary>
    /// 주기가 지났고 교전 중 대기/추격이면 타깃을 다시 고른다.
    /// 판정은 <see cref="BossAggroPolicy"/> 에 있다(EditMode 로 경계·억제를 고정).
    /// </summary>
    /// <summary>같은 사람을 다시 고르지 않게 할지 — SO 노브. MPPM 검증 후 확정할 값이다.</summary>
    protected override bool RetargetAvoidsCurrentTarget =>
        _boss != null && _boss.aggroAvoidsRepeatTarget;

    protected override bool ShouldReacquireTarget()
    {
        // 🔴 교전 시작 전(-1)에는 재선정하지 않는다 — base 와 같은 규약.
        //    이 가드가 없으면 `Time.time - (-1)` 이 항상 주기를 넘겨 첫 틱에 어그로가 튄다.
        if (_lastRetargetTime < 0f) return false;

        float interval = _boss != null ? _boss.aggroRetargetInterval : 0f;
        if (!BossAggroPolicy.ShouldRetarget(State, Time.time - _lastRetargetTime, interval))
            return false;

        // 🔴 성립한 순간 시계를 리셋한다. 여기서 안 하면 base 가 매 틱 재탐색해
        //    "주기 재선정"이 "상시 최근접 추적"이 된다 — 의도와 정반대다.
        _lastRetargetTime = Time.time;
        return true;
    }

    /// <summary>
    /// 이 공격이 고른 대상을 <b>어그로로 승계</b>한다 — 점프·돌진 전용(서버).
    ///
    /// 🔴 왜 시계를 함께 리셋하는가 (2026-09-03 팀장 관찰 — "점프로 후열을 때리고 원래 대상으로
    ///    돌아온다"): 승계만 하고 시계를 그대로 두면, 착지해 Idle 로 돌아온 순간 이미 만료된
    ///    주기 재선정이 즉시 최근접(= 원래 대상)을 다시 물어 <b>승계가 한 프레임짜리</b>가 된다.
    ///    "어그로가 바뀌었으면 그때부터 다시 8초"가 이 기능의 규약이다.
    /// </summary>
    void AdoptAggro(Transform t, string reason)
    {
        if (!AdoptTarget(t)) return;

        _lastRetargetTime = Time.time;
        Edit.Log($"[23호/어그로] {reason} → {t.name} 로 승계 (다음 주기 재선정까지 " +
                 $"{(_boss != null ? _boss.aggroRetargetInterval : 0f)}초)", this);
    }

    // ─── 접촉 사거리 진입/이탈 ─────────────────────────────────────────
    // 판정은 BossContactReachPolicy 에 있다(EditMode 로 경계를 고정). 여기는 상태만 들고 있다.
    bool _inContactReach;

    // 이탈 경계 = 접촉 행 거리창의 최댓값. 저작이 바뀌면 함께 따라와야 하므로 첫 사용 시 1회 계산한다
    // (data·_boss 가 배선되기 전에 계산하면 0 이 박힌다 — 스폰 순서에 기대지 않는 편이 안전하다).
    float _contactReachExit;

    float ContactReachExit(BossAttackEntry[] rows)
    {
        if (_contactReachExit > 0f) return _contactReachExit;

        // 하한은 attackRange 다 — 접촉 행이 하나도 없어도 이탈 경계가 진입 경계보다 좁아지지 않게.
        float exit = data.attackRange;
        for (int i = 0; i < rows.Length; i++)
        {
            if (!BossContactReachPolicy.IsContactRow(rows[i], data.attackRange)) continue;
            if (rows[i].maxDistance > exit) exit = rows[i].maxDistance;
        }

        _contactReachExit = exit;
        return _contactReachExit;
    }

    // hitboxAnchorName 도 문자열 규약이라 오타가 조용히 무시된다(정본 §10.3 경고).
    void ValidateHitboxAnchors()
    {
        if (_boss.attacks == null) return;

        for (int i = 0; i < _boss.attacks.Length; i++)
        {
            BossAttackEntry e = _boss.attacks[i];
            if (e == null) continue;

            ValidateAnchorName(i, e.attackId, e.hitboxAnchorName, nameof(e.hitboxAnchorName),
                               "앵커 지정이 조용히 무시된다");

            // [G1] 전진은 있는데 경로 반경이 0 이면 "전진만 하고 아무도 안 맞는" 조합이다.
            // ⚠️ 네모 공격(boxWidth > 0)은 **일부러** 경로를 비운다 — 예고가 끝점 네모 하나라,
            //    그리지 않은 경로에 데미지가 있으면 그것이야말로 과소 표시다. 경고 대상이 아니다.
            // 의도일 수 있으므로(연출성 전진) 에러가 아니라 경고로 남긴다.
            if (e.lungeDistance > 0f && e.lungePathRadius <= 0f && e.boxWidth <= 0f)
                Debug.LogWarning(
                    $"{name}: attacks[{i}]({e.attackId}) 는 {e.lungeDistance:0.##}m 전진하는데 " +
                    "lungePathRadius 가 0 이다 — 전진 경로에 데미지가 없다.", this);

            // 🔴 [G2] 전진이 히트보다 **늦게** 끝나면 예고를 그린 자리와 실제 판정 자리가 어긋난다.
            //    정확한 히트 시각은 클립(SVN)에 있어 코드가 모르므로, 최소한 "전진이 공격 길이 안에
            //    끝나는가"만 본다. 넘으면 튜닝이 필요하다는 신호다.
            if (e.lungeDistance > 0f && data != null)
            {
                float travelTime = e.lungeDistance / Mathf.Max(0.01f, MoveSpeed * e.lungeSpeedMultiplier);
                if (travelTime > data.attackDuration)
                    Debug.LogWarning(
                        $"{name}: attacks[{i}]({e.attackId}) 전진이 {travelTime:0.###}초 걸리는데 " +
                        $"attackDuration 은 {data.attackDuration:0.###}초다 — 이동이 끝나기 전에 판정이 나서 " +
                        "예고 위치와 실제 판정 위치가 어긋난다. lungeSpeedMultiplier 를 올릴 것.", this);
            }
        }
    }

    void ValidateAnchorName(int index, BossAttackId id, string anchorName, string field, string consequence)
    {
        if (string.IsNullOrEmpty(anchorName)) return;
        if (_anchors != null && _anchors.ContainsKey(anchorName)) return;

        Debug.LogError(
            $"{name}: attacks[{index}]({id}).{field}=\"{anchorName}\" 에 해당하는 " +
            $"ColliderInfo 자식이 없다 — {consequence}.",
            this);
    }
    #endregion

    #region 공격 선택기 (거리창 → 가중치 → 연속 감쇠 → 폴백)
    // base 의 SeekBoss 가 매 틱 부른다. NoAttack(-1)을 돌리면 base 가 접근/대기로 폴백한다
    // (= 전부 쿨이어도 제자리에 멈춰 서지 않는다 — 수용기준 #1).
    /// <summary>
    /// 개전 직후 원거리 진입기(Dash·Jump)에 쿨을 걸어 <b>걸어 들어가서 때리게</b> 한다.
    ///
    /// 왜 필요한지와 왜 새 상태를 안 만들었는지는 <see cref="BossOpeningAttackPolicy"/> 참조.
    /// 요지 — 개전엔 플레이어가 멀어 거리창을 통과하는 행이 그 둘뿐이라 룰렛이 반드시 그중
    /// 하나를 뽑는다(구조적이라 가중치로 못 막는다). 쿨은 시간이 지나면 스스로 풀린다.
    /// </summary>
    protected override void OnServerLogicResumed()
    {
        base.OnServerLogicResumed();
        if (!IsServer) return;

        // 🔴 **어그로 주기의 0초는 여기다**(2026-09-03). 이 훅은 BossEncounterDirector 가 착지·NavMesh
        //    스냅을 끝낸 뒤 부르는 단일 전환점이라 "착지 완료 = 전투 시작"과 정확히 일치한다.
        //    안 하면 초기값 0 때문에 첫 틱에 재선정이 돌아 어그로가 착지하자마자 튄다.
        _lastRetargetTime = Time.time;

        // 연출 동안의 접촉 상태는 의미가 없다 — 전투가 새로 시작되므로 "안 붙은" 상태에서 출발한다.
        _inContactReach = false;

        BossAttackEntry[] rows = _boss != null ? _boss.attacks : null;
        if (rows == null) return;

        for (int i = 0; i < rows.Length; i++)
            if (BossOpeningAttackPolicy.IsRangedOpener(rows[i]))
                MarkAttackJustUsed(i);
    }

    protected override int SelectAttackSlot(float dist)
    {
        BossAttackEntry[] rows = _boss != null ? _boss.attacks : null;
        if (rows == null || rows.Length == 0)
            return base.SelectAttackSlot(dist);

        // 🔴 페이즈 시퀀스 소비 지점. 이 함수는 Idle/Walk 에서만 불리므로 여기가 "행동이 끝난 직후"다
        //    (정본 §9 — 행동 도중 강제 중단 금지).
        if (_pendingPhaseSequence)
        {
            _pendingPhaseSequence = false;
            int seq = FindSlot(BossAttackId.ChargeSequence);
            if (seq != NoAttack) return seq;

            Debug.LogError(
                $"{name}: 페이즈 시퀀스를 시작해야 하는데 공격 테이블에 ChargeSequence 행이 없다 — " +
                "SO 에 weight 0 행으로 추가할 것. 이번 시퀀스는 건너뛴다.", this);
        }

        // 🔴 **전역 공격 간격**(팀장 확정 2026-08-13: "다음 공격까지가 너무 빠르다").
        //    쿨다운이 행마다 따로라 훅L(2.5s)·훅R(2.5s)·어퍼(3s)를 번갈아 쓰면 **쉬는 구간이 0** 이었다.
        //    행 쿨다운과 별개로, 공격이 끝난 뒤 이 시간만큼은 아무것도 고르지 않는다.
        //    ⚠️ 페이즈 시퀀스 진입(위)보다 **뒤에** 둔다 — 연출 전환은 기다리게 하면 안 된다.
        if (Time.time - _lastAttackTickTime < GlobalAttackInterval)
            return NoAttack;

        if (_weightBuffer == null || _weightBuffer.Length != rows.Length)
            _weightBuffer = new float[rows.Length];

        // 🔴 접촉 사거리 진입/이탈을 여기서 갱신한다 — 이 함수는 Idle/Chase 마다 불리므로
        //    "지금 붙어 있는가"를 관측할 수 있는 유일한 지점이다. 공격 도중에는 안 불리는데,
        //    그 구간에 상태가 굳는 것은 의도한 것이다(후속타가 붙은 상태에서 이어져야 한다).
        _inContactReach = BossContactReachPolicy.StaysInReach(
            _inContactReach, dist, data.attackRange, ContactReachExit(rows));

        // 1) 게이트 3단: 페이즈 → 거리창 → 쿨다운. 셋 다 통과 + 가중치 > 0 인 것만 후보다.
        int candidates = 0;
        int fallbackSlot = NoAttack;
        for (int i = 0; i < rows.Length; i++)
        {
            _weightBuffer[i] = 0f;

            BossAttackEntry e = rows[i];
            if (e == null || e.weight <= 0f) continue;
            if (CurrentPhase < e.allowedFromPhase) continue;

            // 🔴 상한은 저작값이 아니라 **개시 상한**이다 — 접촉 공격은 attackRange 안까지 걸어
            //    들어간 뒤에만 시작한다(허공 훅 차단). 붙은 뒤에는 저작값까지 계속 때린다.
            //    돌진·점프는 접촉 행이 아니라 영향을 받지 않는다(BossContactReachPolicy 주석).
            if (!e.ignoreDistanceWindow)
            {
                float max = BossContactReachPolicy.EffectiveMaxDistance(e, data.attackRange, _inContactReach);
                if (dist < e.minDistance || dist > max) continue;
            }

            if (!CooldownReady(i)) continue;

            _weightBuffer[i] = e.weight;
            candidates++;
            fallbackSlot = i;
        }
        if (candidates == 0)
            return NoAttack;

        // 2) 연속 감쇠. 직전에 쓴 공격은 가중치를 깎고, repeatBlockAfter 회 연속이면 후보에서 아예 뺀다.
        //    확률 감쇠(repeatPenalty)만으로는 "연속 N회 금지"를 보장할 수 없어 하드 제외가 따로 필요하다.
        //
        // 🔴 여기서 수용기준 두 개가 충돌한다 — #2 "같은 공격 연속 3회 금지" vs
        //    #1 "쿨이어도 멈추지 않는다". 후보가 그 공격 하나뿐이면 둘 중 하나를 깨야 한다.
        //    멈춰 서는 쪽이 더 나쁜 버그이므로 **대안이 하나도 없을 때만(candidates > 1) 제외**한다.
        if (_lastSlot >= 0 && _lastSlot < rows.Length && _weightBuffer[_lastSlot] > 0f)
        {
            if (_boss.repeatBlockAfter > 0 && _consecutive >= _boss.repeatBlockAfter && candidates > 1)
            {
                _weightBuffer[_lastSlot] = 0f;
                candidates--;
            }
            else
            {
                _weightBuffer[_lastSlot] *= _boss.repeatPenalty;
            }
        }

        // 3) 가중치 룰렛.
        float total = 0f;
        for (int i = 0; i < rows.Length; i++)
            total += _weightBuffer[i];

        // repeatPenalty 가 0 이고 후보가 직전 공격 하나뿐이면 합이 0 이 된다 — 멈추지 않도록 그 후보를 쓴다.
        if (total <= 0f)
            return fallbackSlot;

        float roll = Random.value * total;
        for (int i = 0; i < rows.Length; i++)
        {
            roll -= _weightBuffer[i];
            if (roll < 0f) return i;
        }
        return fallbackSlot; // 부동소수 잔차 안전망
    }
    #endregion

    #region 훅 — StartAttack / PerformAttackHit / PlayStateAnimation
    // 관용구 1: 선택 결과를 base.StartAttack() **전에** 확정한다.
    //           base 가 SetState(Attack)까지 하므로 그 시점에 _currentEntry 가 이미 있어야 한다.
    protected override void StartAttack()
    {
        BossAttackEntry e = EntryFor(CurrentAttackSlot);
        _currentEntry = e;

        // 연속 카운트는 "실제로 쓴 것" 기준으로 센다(SelectAttackSlot 은 NoAttack 을 돌릴 수 있다).
        if (CurrentAttackSlot == _lastSlot)
        {
            _consecutive++;
        }
        else
        {
            _lastSlot = CurrentAttackSlot;
            _consecutive = 1;
        }

        // 데미지·히트박스 앵커는 여기서 세팅하지 않는다 — 히트 직전(PerformAttackHit)에 확정한다.
        // meleeAttack 하나를 공격 6종이 돌려 쓰므로, 히트 시점에 세팅하는 편이 항상 _currentEntry 와 일치한다.

        // [G3] 🔴 **조준 대상을 먼저 확정한다.** `base.StartAttack()` 의 FaceTarget 은 어그로 대상을
        //    보므로, 최원거리 대상을 노리는 공격은 그 **전에** 어그로를 옮겨야 엉뚱한 데를 향해 나간다.
        //    (점프는 체공 중 착지점을 따로 잡아 티가 안 났지만, 돌진은 방향이 곧 공격이라 치명적이다.)
        //    노린 사람이 어그로를 가져가는 규약은 점프와 같다 — 응징 대상이 압박을 계속 받는다.
        if (e != null && e.attackTargeting == BossAttackTargeting.FarthestPlayer)
        {
            Transform aimed = ResolveAttackTarget(e);
            if (aimed != null) AdoptAggro(aimed, $"{e.attackId} 조준");
        }

        // 쿨 기록(CurrentAttackSlot) + _stateTimer(데드락 타임아웃) + StopAgent + FaceTarget + SetState(Attack).
        base.StartAttack();

        // 다단계 체인은 관용구 3 대로 base 의 종료 타이머를 **체인 전체 길이**로 덮어쓴다
        // — 안 늘리면 attackDuration 이 만료돼 다음 단계에 들어가기도 전에 Attack 을 벗어난다.
        // 여기서 세팅한 _stateTimer 가 곧 슈퍼아머 길이이자 데드락 안전망이 된다.
        if (e != null)
        {
            switch (e.attackId)
            {
                case BossAttackId.Grab:
                    // [G5] 새 사이클 = 예고 → 끌어당김 → 붙잡기 → 지짐이 → 내려치기 ×N → 놓아주기 → 복귀.
                    // 🔴 단계가 6개로 늘었으니 예산도 전부 더한다. 빠뜨리면 체인 도중 데드락 안전망이
                    //    터져 보스가 공격을 중간에 버린다 — 돌진이 선딜 몫을 빠뜨려 매번 터졌던 사고와 같다.
                    //
                    // 🔴 애니 배수는 **여기서 먼저** 건다. 자세 홀드(BeginTelegraph)가 "걸 때의 속도"를
                    //    저장했다가 되돌리므로, 홀드보다 늦게 걸면 복원이 1.0 으로 돌아가 버린다.
                    if (IsSpawned) SetGrabCycleSpeedClientRpc(GrabCycleSpeed);
                    if (animator != null) animator.speed = GrabCycleSpeed;

                    _stateTimer = e.telegraphDuration
                                  + GrabPullDuration + GrabCatchDuration + GrabHold
                                  + GrabThrowTime * GrabSlamCount
                                  + GrabEndDuration + GrabRecovery + ChainBudgetSlack;
                    break;

                case BossAttackId.Jump:
                    // 🔴 [G4] 이륙 몴을 반드시 더한다 — 빼면 체인 도중 데드락 안전망이 터진다
                    //    (돌진이 선딜 몫을 빼뜨려 매번 타임아웃하던 것과 같은 종류).
                    _stateTimer = JumpTakeoffDuration + JumpHover + JumpLanding + JumpRecovery
                                + data.attackDuration;
                    break;
                case BossAttackId.ChargeSequence:
                    // 🔴 **진입 구간을 예산에 넣는다**(2026-08-13). 차징은 자리로 이동한 뒤 시작하므로
                    //    체인 길이 = 진입 + 제한시간 + 복귀다. 돌진에서 선딜 몫을 빠뜨려 매번 안전망이
                    //    터졌던 것과 **같은 종류의 실수**라 여기서 미리 닫는다.
                    // ⚠️ 2026-09-21 — 진입이 걸어가기(`ChargeMoveTimeout`)에서 **점프**로 바뀌었다.
                    //    이륙 + 체공 + 착지가 그 자리를 대신한다(BeginCharge 주석 참조).
                    _stateTimer = JumpTakeoffDuration + JumpHover + JumpLanding
                                + ChargeTimeLimit + data.attackDuration;
                    break;
                case BossAttackId.RageDash:
                    _stateTimer = RageTotalTime + data.attackDuration;
                    break;
                case BossAttackId.Dash:
                    // 선딜 + 돌진 본체 + 복귀. 슈퍼아머 길이이자 데드락 안전망이다.
                    //
                    // 🔴 **선딜 몫을 반드시 넣어야 한다**(2026-08-13 수정). 돌진은 애니 이벤트
                    //    `OnAttackHit`(dash 클립 0.15초)이 와야 시작하는데, 이전 판은 예산을
                    //    `DashDuration + attackDuration`(0.7+0.9=1.6초)으로만 잡았다. 실제 체인은
                    //    0.15 + 0.7 + 0.9 = 1.75초라 **매번 0.15초씩 초과**해 Recovery 도중 안전망이
                    //    터졌다(4/4 재현). Grab 이 attackDuration 을 선딜 몫으로 이미 잡아 두는 것과
                    //    같은 규약인데, 돌진은 그 값을 Recovery(StopDash)가 쓰므로 몫이 통째로 없었다.
                    //
                    // 🔴 카운터 창(2026-09-02): 선딜 몫을 max(창, attackDuration) 으로 올린다.
                    //    창으로 교체하지 않는 이유는 Grab 쪽 주석과 같다 — 창이 이벤트보다 짧으면
                    //    실제 선딜이 예산을 넘는다.
                    _attackPhaseTimer = Mathf.Max(CounterWindowDuration, data.attackDuration);
                    _attackPhase = BossAttackPhase.Windup;
                    _stateTimer = _attackPhaseTimer + DashDuration + data.attackDuration + ChainBudgetSlack;
                    break;
            }
        }

        // 공격별 슈퍼아머. SO 전역 플래그(hasSuperArmorWhileAttacking)는 꺼진 상태를 전제한다
        // — 켜져 있으면 ValidateContract 가 LogError 로 잡는다.
        // 체인 공격은 전체 길이 동안 유지해야 중간에 경직으로 끊기지 않는다.
        if (e != null && e.superArmor && status != null)
            status.ApplyStatus(StatusEffectType.SuperArmor, _stateTimer);

        // 카운터 창: 창을 여는 공격(Grab·Dash)이면 지금 연다.
        // ⚠️ 정정(2026-09-02): 예전엔 "공격 시작 → 히트"가 곧 창이라 phase 기계가 필요 없었다.
        //    이제 창 길이가 데이터(counterWindowDuration)로 정해지고 히트와 분리됐다 —
        //    닫는 것은 히트가 아니라 아래 게이트의 발사(TryReleaseCounterAttack)다.
        // 🔴 **잡기는 예외다**(팀장 확정 C10). 기획: "플레이어를 끌어당기는 순간까지는 인터럽트할 수
        //    없음. 실제로 한 명을 붙잡은 뒤부터 인터럽트가 가능해짐."
        //    그래서 여기서 열지 않고 `AcquireGrab` 성공 시점에 연다.
        bool opensNow = e != null && e.opensCounterWindow && e.attackId != BossAttackId.Grab;
        SetCounterWindow(opensNow);

        // 카운터 선딜 게이트 시작. 창을 여는 공격이면 창 길이로, 아니면 0(비활성)이다.
        // 🔴 창이 열린 공격은 애니 이벤트가 와도 **즉시 발사하지 않는다** — NotifyAttackHit 이
        //    준비만 래치하고, 창 타이머가 끝나야 TryReleaseCounterAttack 이 실제 발사를 한다.
        _warnedCounterTimerBeforeAnimation = false;
        _counterWindupStartedAt = Time.time;
        // ⚠️ 잡기는 선딜 게이트를 쓰지 않는다 — 타이밍을 예고 구간(Telegraph)이 이미 잡는다.
        //    여기서 Begin 하면 게이트가 애니 이벤트를 기다리며 열린 채 남는다.
        if (opensNow) _counterWindup.Begin(CounterWindowDuration);
        else _counterWindup.Reset();

        // 관용구 2: 다지선다 애니는 상태 복제로 실을 수 없다 → ClientRpc 로 CrossFade.
        // 문자열이 아니라 슬롯 번호를 보낸다(각 피어가 같은 SO 에서 상태명을 조회한다 — GauntletBot 선례).
        PlayAttackAnimClientRpc(CurrentAttackSlot);

        // 애니가 나간 **뒤에** 각 체인의 진입 처리를 한다(클립이 먼저 보여야 한다).
        if (e == null) return;

        // [G2] 예고 구간이 있는 공격은 여기서 갈린다 —
        //   ① 준비 자세에서 멈추고 부채꼴이 차오른다(Telegraph 단계)
        //   ② 다 차면 ReleaseTelegraph 가 자세를 풀고 **전진 + 공격**을 낸다
        // 🔴 예고가 판정보다 **먼저 끝나야** 피할 시간이 생긴다. 둘을 같이 시작하면 예고가 아니다.
        if (!BeginTelegraph(e))
        {
            // 예고가 없는 공격(telegraphDuration 0 = 기존 즉발). 전진과 표시를 지금 시작한다.
            //
            // [G1] 🔴 `base.StartAttack()` 이 이미 agent 를 세워 놨으므로 **반드시 그 뒤**여야 한다.
            //    체인 공격(잡기·점프·차징·레이지)은 lungeDistance 를 0 으로 두어 여기 걸리지 않는다.
            BeginLunge(e);
            if (HasTelegraphShape(e)) ShowAttackConeClientRpc(CurrentAttackSlot, 0f);
        }

        switch (e.attackId)
        {
            // 팔 전기는 **잡기 판정보다 먼저** 흐른다 — 판정(AcquireGrab)은 히트 프레임이라
            // 거기서 켜면 팔을 뻗는 동안 아무 예고가 없다. 끄는 곳은 넷이다(ReleaseGrabThrow ·
            // 헛잡기 · 대상 소멸 · AbortAttackChain) — 켜는 자리를 앞당긴 만큼 헛잡기 경로가 특히 중요하다.
            case BossAttackId.Grab: StartGrabPulseClientRpc(); break;

            case BossAttackId.Jump: BeginJump(); break;
            case BossAttackId.ChargeSequence: BeginCharge(); break;
            case BossAttackId.RageDash: BeginRage(); break;

            // [G3] 돌진은 카운터 창 동안 경로 띠를 채운다. 끄는 곳은 히트(PerformAttackHit)와
            // 이탈(AbortAttackChain) 둘 — 훅·어퍼 예고와 같은 규약이다.
            case BossAttackId.Dash: ShowDashTelegraph(e); break;
        }
    }

    // 애니 이벤트 OnAttackHit → base.NotifyAttackHit → FireAttackHitOnce 경로로 들어온다.
    // 히트 이벤트가 없는 클립은 데미지가 나가지 않는다(타이머 폴백 없음 — 넣으면 이벤트 추가 후 두 번 맞는다).
    protected override void PerformAttackHit()
    {
        BossAttackEntry e = _currentEntry;
        if (e == null)
        {
            base.PerformAttackHit();
            return;
        }

        // 히트 순간에 카운터 창이 닫힌다 — 못 끊으면 잡힌다/밀린다(창에 실패 대가가 붙는다).
        //
        // 🔴 **잡기는 예외다**(2026-09-21 수정 · G6). 잡기의 창은 히트가 아니라 **잡기 사이클이
        //    소유한다** — `AcquireGrab` 이 붙잡기에 성공한 순간 열고, `AdvanceGrabSlam` 이
        //    마지막 타 직전에 닫는다. 그런데 `Boss_23_grab` 클립에는 `OnAttackHit` 이
        //    **정규화 0.354** 에 박혀 있어(fbx.meta 확인), 붙잡는 모션 중 이 함수가 반드시 한 번
        //    불린다. 여기서 무조건 닫으면 **창이 Acquire 중에 0.25초만 열렸다 닫히고**
        //    정작 플레이어가 끊으려는 Hold·Throw 구간에는 이미 닫혀 있다.
        //    (2026-09-21 MPPM 실측 로그: `페이즈=Hold/Throw · 창열림=False · 정면=True`)
        //    ⚠️ `StartAttack` 에는 같은 예외가 이미 있었는데(`e.attackId != Grab`) 여기만 빠져 있었다.
        //       창을 여닫는 지점을 늘릴 때는 **양쪽을 같이** 볼 것.
        if (e.attackId != BossAttackId.Grab)
            SetCounterWindow(false);

        // [G1] 주먹이 닿는 순간 전진을 멈춘다 — "전진하며 휘두르다 닿으면 선다".
        // 🔴 **이동만** 멈추고 히트 윈도우는 열어 둔다. 아래 끝점 판정이 같은 윈도우를 써야
        //    경로에서 맞은 사람이 끝점에서 또 맞지 않는다(팀장 확정: 1인 1회).
        StopLungeMove();

        // [G2] 예고는 히트와 함께 사라진다 — 판정이 끝났는데 남아 있으면 "아직 온다"로 읽힌다.
        HideAttackConeClientRpc();   // 멱등 — 모양이 없던 공격이어도 안전하다

        ApplyAttackProfile(e);

        switch (e.attackId)
        {
            // 🔴 근접 3종은 **맞았을 때만** 타격 연출을 낸다. Hit() 가 실제로 피해가 들어간 수를
            //    돌려주므로 헛스윙(0)과 명중을 여기서 가른다 — 애니 이벤트로는 못 가른다.
            // 🔴 명중 판정은 **경로 + 끝점의 합**이다. 끝점만 보면 경로로만 맞힌 공격이 헛스윙으로
            //    처리돼 타격 연출이 빠진다(같은 대상이 히트 윈도우에 이미 걸려 끝점이 0 을 돌려준다).
            case BossAttackId.LeftHook:
            case BossAttackId.RightHook:
                if (StrikeMelee(e) + _lungePathHits > 0)
                    PlayAttackHitEffectRpc(e.attackId);
                EndLunge();   // 끝점 판정 **뒤에** 윈도우를 닫는다(위 주석의 1인 1회 규약)
                break;

            case BossAttackId.Upper:
                if (StrikeMelee(e) + _lungePathHits > 0)
                    PlayAttackHitEffectRpc(e.attackId);
                OnUpperHit();
                EndLunge();
                break;

            case BossAttackId.Grab:
                // 🔴 **아무것도 하지 않는다.** [G5] 이후 잡기 사이클은 단계 타이머가 몬다
                //    (예고 → 끌어당김 → 붙잡기 …). 그런데 `Boss_23_grab` 클립에는 `OnAttackHit` 이
                //    정규화 0.354 에 박혀 있어, 붙잡는 모션 중 이 이벤트가 **반드시 한 번 온다.**
                //    예전처럼 여기서 AcquireGrab 을 부르면 **사이클 도중 붙잡기가 재실행**된다.
                break;

            case BossAttackId.Jump:
                // 착지 클립의 히트 프레임. Land 단계가 아니면 도약 클립의 오발동이므로 무시한다.
                if (_attackPhase == BossAttackPhase.Land)
                    ApplyJumpLandingDamage(e);
                break;

            case BossAttackId.Dash:
                // 돌진 시작. 여기가 카운터 창이 닫히는 순간이기도 하다(위에서 이미 닫았다).
                BeginDash();
                break;

            // 🔴 이 둘은 히트 이벤트로 시작하지 않는다 — StartAttack 이 이미 BeginRage/BeginCharge 를 했다.
            //    그런데 **Rage 는 DashAttack 과 같은 클립(Boss_23_dash)을 쓴다.** 그 클립에 심은
            //    OnAttackHit 이 레이지 중에도 여기로 들어오므로, 명시적으로 받아 두지 않으면
            //    "미구현" 경고가 뜬다(경고는 신호를 덮는다 — 교훈 #8).
            case BossAttackId.RageDash:
            case BossAttackId.ChargeSequence:
                break;

            default:
                WarnUnimplementedOnce(e.attackId);
                break;
        }
    }

    // 이 공격의 데미지·판정 형상을 근접 판정기에 반영한다.
    //
    // 🔴 StartAttack 이 아니라 **히트 직전**에 하는 이유: meleeAttack 하나를 공격 6종이 돌려 쓰므로
    //    값을 히트 시점에 확정하면 애니 이벤트가 늦게 도착해도 항상 _currentEntry 와 일치한다.
    void ApplyAttackProfile(BossAttackEntry e)
    {
        if (meleeAttack == null) return;

        // 데미지: 0 이면 SO 의 attackDamage, 페이즈 배수를 곱한다.
        int dmg = e.damage > 0 ? e.damage : AttackDamage;
        meleeAttack.SetDamageSnapshot(Mathf.Max(0, Mathf.RoundToInt(dmg * PhaseDamageMultiplier)));

        // 앵커: 지정이 없으면 프리팹에 배선된 원본으로 되돌린다(직전 공격의 앵커가 남지 않게).
        if (string.IsNullOrEmpty(e.hitboxAnchorName))
        {
            meleeAttack.SetColliderInfo(_defaultAnchor);
            return;
        }
        if (_anchors != null && _anchors.TryGetValue(e.hitboxAnchorName, out ColliderInfo anchor))
            meleeAttack.SetColliderInfo(anchor);
        // 못 찾으면 직전 형상을 유지한다 — 이름 오타는 스폰 시 ValidateHitboxAnchors 가 LogError 로 잡는다.
    }

    /// <summary>
    /// [G2] 근접 끝점 판정. 행에 부채꼴이 저작돼 있으면 <b>부채꼴</b>로, 아니면 기존 앵커 형상으로 때린다.
    /// 반환값은 <see cref="MonsterMeleeAttack.Hit"/> 와 같은 계약(실제 피해가 들어간 수)이라
    /// 헛스윙/명중을 가르는 기존 연출 분기가 그대로 살아 있다.
    ///
    /// ⚠️ 데미지 스냅샷은 호출 전에 <see cref="ApplyAttackProfile"/> 이 이미 세팅한다 — 부채꼴이어도 같다.
    /// </summary>
    int StrikeMelee(BossAttackEntry e)
    {
        if (meleeAttack == null) return 0;
        // 🔴 **네모가 부채꼴보다 우선이다**(T4). 예고도 같은 조건으로 네모 하나만 그린다 —
        //    둘이 같은 칸을 보는 것이 예고가 거짓말하지 않는 유일한 방법이다.
        //    원점은 **현재 위치**다 — 히트 시점에는 이미 전진을 마쳤거나 마쳤어야 한다.
        if (e != null && e.boxWidth > 0f && e.boxLength > 0f)
        {
            // 예고와 **같은 치우침**을 판정에도 먹인다 — 한쪽만 옮기면 예고가 거짓말이 된다.
            Vector3 fwd = transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 origin = transform.position
                           + Vector3.Cross(Vector3.up, fwd) * e.boxLateralOffset
                           - fwd * e.boxBackOffset;
            return meleeAttack.HitBox(origin, fwd, e.boxWidth, e.boxLength);
        }

        if (e == null || e.coneRadius <= 0f) return meleeAttack.Hit();

        return meleeAttack.HitCone(transform.position, ConeForward(e), e.coneRadius, e.coneAngle);
    }

    /// <summary>
    /// 부채꼴 중심 방향 = 보스 정면을 <c>coneOffsetAngle</c> 만큼 Y 축으로 돌린 것.
    /// 🔴 <b>예고 데칼도 반드시 이 함수를 써야 한다</b> — 방향을 각자 계산하면 예고와 판정이 갈라진다.
    /// </summary>
    Vector3 ConeForward(BossAttackEntry e)
    {
        Vector3 fwd = transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
        if (e == null) return fwd.normalized;

        return Quaternion.AngleAxis(e.coneOffsetAngle, Vector3.up) * fwd.normalized;
    }

    // ─── [G2] 공격 예고 부채꼴 ────────────────────────────────────────
    // 🔴 **클라 비주얼이다** — 판정은 서버가 하지만 예고는 전 피어에서 보여야 한다. 서버에서만
    //    켜면 호스트 화면에만 뜬다(이 프로젝트에서 반복된 실수라 RPC 로 못박는다).
    // 🔴 상태명과 같은 이유로 **값을 보내지 않고 슬롯만 보낸다** — 각 피어가 같은 SO 에서 조회하면
    //    예고와 판정이 같은 칸에서 나오는 것이 보장된다(CrossFadeJumpStateClientRpc 와 같은 관용구).

    BossAttackConeTelegraph _attackCone;

    // ⚠️ `??` 를 쓰지 않는다 — UnityEngine.Object 의 **가짜 null**(파괴된 컴포넌트)을 `??` 는
    //    통과시킨다. 반드시 Unity 가 오버로드한 `==`/`!=` 로 판정할 것.
    BossAttackConeTelegraph AttackCone
    {
        get
        {
            if (_attackCone != null) return _attackCone;

            _attackCone = GetComponent<BossAttackConeTelegraph>();
            if (_attackCone == null) _attackCone = gameObject.AddComponent<BossAttackConeTelegraph>();
            return _attackCone;
        }
    }

    /// <param name="slot">공격 슬롯 — 각 피어가 SO 에서 반경·각도·치우침을 조회한다.</param>
    /// <param name="growTime">부채꼴이 다 차는 데 걸리는 시간(초). 0 이면 즉시 가득 찬 상태로 뜬다.</param>
    [ClientRpc]
    void ShowAttackConeClientRpc(int slot, float growTime)
    {
        BossAttackEntry e = EntryFor(slot);
        if (e == null) return;
        if (e.coneRadius <= 0f && e.lungePathRadius <= 0f && e.boxWidth <= 0f) return;

        // 🔴 부채꼴 꼭짓점은 **전진 끝점**이다 — 판정도 전진을 마친 자리에서 나가기 때문이다.
        //    보스 현재 위치에 그리면 예고와 판정이 전진 거리만큼 어긋난다.
        // 🔴 네모 공격은 **사각형 하나**로 그린다(팀장 확정 2026-09-18).
        //    네모는 **전진 끝점**(lungeDistance 앞)에 놓는다 — 판정도 거기서 나가기 때문이다.
        //    전진 경로는 그리지 않으므로 네모 공격은 lungePathRadius 를 0 으로 두어
        //    **그리지 않은 경로에서 맞는 일**(과소 표시)이 없게 한다.
        if (e.boxWidth > 0f && e.boxLength > 0f)
        {
            AttackCone.Show(coneRadius: 0f, coneAngleDeg: 0f, coneOffsetAngleDeg: 0f,
                            coneForwardOffset: 0f,
                            pathRadius: e.boxWidth * 0.5f,
                            pathLength: e.boxLength,
                            pathForwardOffset: e.lungeDistance - e.boxBackOffset,
                            pathLateralOffset: e.boxLateralOffset,
                            growTime: growTime);
            return;
        }

        AttackCone.Show(e.coneRadius, e.coneAngle, e.coneOffsetAngle,
                        coneForwardOffset: e.lungeDistance,
                        pathRadius: e.lungePathRadius,
                        pathLength: e.lungeDistance,
                        growTime: growTime);
    }

    [ClientRpc]
    void HideAttackConeClientRpc() => AttackCone.Hide();

    // 예고로 그릴 도형이 하나라도 있는가(끝점 부채꼴 또는 전진 경로 띠).
    static bool HasTelegraphShape(BossAttackEntry e) =>
        e != null && (e.coneRadius > 0f || e.lungePathRadius > 0f || e.boxWidth > 0f);

    // ─── [G3] 돌진 예고 — 채워지는 직선 띠 ─────────────────────────────
    // 기획: "돌진 전에 이동 경로를 보여주기 때문에 대상과 주변 플레이어가 경로 밖으로 피할 수 있다."
    // 레퍼런스(팀장 제공): 겉 테두리가 먼저 뜨고 안쪽이 채워지며, **다 찬 순간이 발동**이다.
    //
    // 🔴 돌진은 **이미 예고 구간을 갖고 있다** — 카운터 창(1.5초) 동안 준비 자세에서 멈춰 대기한다.
    //    그래서 새 단계를 만들지 않고 그 창 길이에 맞춰 띠를 채운다.

    /// <summary>
    /// 돌진이 **실제로 도달할** 거리. 예고가 여기보다 길면 오지도 않을 곳을 위험하다고 말하게 된다.
    ///
    /// 🔴 <c>dashMaxDistance</c> 는 구속 조건이 아닌 경우가 많다 — 실측상 지속시간이 먼저 끝난다
    ///    (0.7초 × 15m/s = 10.5m &lt; 16m). 둘 중 **작은 쪽**을 쓰고, 벽까지 한 번 더 자른다.
    /// </summary>
    float DashTelegraphReach(Vector3 dir)
    {
        float byTime = DashDuration * MoveSpeed * DashSpeedMul;
        float reach = Mathf.Min(DashMaxDistance, byTime);

        Vector3 origin = transform.position;
        if (UnityEngine.AI.NavMesh.Raycast(origin, origin + dir * reach,
                                           out UnityEngine.AI.NavMeshHit hit,
                                           UnityEngine.AI.NavMesh.AllAreas))
        {
            reach = Mathf.Min(reach, Vector3.Distance(origin, hit.position));
        }
        return Mathf.Max(0.1f, reach);
    }

    /// <summary>
    /// 돌진 히트박스(<c>DashBody</c>)의 실측 발자국 — 반폭과 **보스 앞으로 뻗은 거리**.
    ///
    /// 🔴 상수로 적지 않고 앵커에서 읽는다. 모델이 바뀌면(23호는 최근 1.7배로 교체됐다) 콜라이더가
    ///    따라 바뀌는데, 예고만 상수로 남으면 조용히 어긋난다.
    /// </summary>
    bool TryGetDashFootprint(BossAttackEntry e, Vector3 dir, out float halfWidth, out float forwardReach)
    {
        halfWidth = 0f;
        forwardReach = 0f;

        string anchorName = e != null ? e.hitboxAnchorName : null;
        if (string.IsNullOrEmpty(anchorName) || _anchors == null) return false;
        if (!_anchors.TryGetValue(anchorName, out ColliderInfo anchor) || anchor == null) return false;
        if ((anchor.OverlapCollider & OverlapCollider.Box) == 0) return false;

        BoxColliderInfo box = default;
        anchor.GetBoxColliderInfo(ref box);

        // 🔴 **폭은 x 만 읽는다.** 예전에 Max(x, z) 였는데 z 는 앞으로 뻗은 **깊이**라,
        //    깊이가 폭보다 크면(DashBody = 3.4 × 4.42) 예고 띠가 판정보다 넓게 그려졌다.
        //    깊이는 아래 forwardReach 가 따로 처리한다 — 여기서 두 번 세면 안 된다.
        halfWidth = box.halfExtents.x;
        // 박스 중심이 보스 앞으로 얼마나 나가 있는지 + 그 방향 반길이.
        forwardReach = Vector3.Dot(box.center - transform.position, dir) + box.halfExtents.z;
        return halfWidth > 0f;
    }

    void ShowDashTelegraph(BossAttackEntry e)
    {
        Vector3 dir = transform.forward;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
        dir.Normalize();

        float reach = DashTelegraphReach(dir);

        // 히트박스가 보스보다 앞으로 뻗어 있으면 그만큼 띠도 길어야 한다 — 그 앞쪽도 판정이다.
        float halfWidth = 1.7f;                       // 앵커를 못 읽을 때의 보수적 기본값
        if (TryGetDashFootprint(e, dir, out float w, out float pad))
        {
            halfWidth = w;
            reach += Mathf.Max(0f, pad);
        }

        ShowDashTelegraphClientRpc(reach, halfWidth, CounterWindowDuration);

        // [G2-P] 돌진 준비동작(2026-09-23). 돌진은 `telegraphDuration: 0` 이라 BeginTelegraph 를
        //    타지 않으므로 여기가 prep 을 트는 유일한 자리다.
        if (string.IsNullOrEmpty(e.prepStateName)) return;

        PlayPrepPoseClientRpc(CurrentAttackSlot);

        // 🔴 **여기가 이 변경의 핵심이다.** 돌진의 "애니 준비 완료" 신호는 TickHitEventFallback 이
        //    내는데, 그 함수는 `st.IsName(e.animatorStateName)` 으로 **DashAttack 상태일 때만**
        //    통과시킨다. prep 을 틀면 그 검사가 false 라 신호가 영영 안 오고,
        //    _counterWindup 은 **창 타이머 + 애니 준비** 둘 다 서야 발사하므로
        //    **돌진이 아예 안 나가고 안전망 타임아웃까지 간다.**
        //    → prep 경로에서는 준비 신호를 즉시 세워, 발사 시점을 **카운터 창(1.5초)이 단독으로**
        //      결정하게 한다(팀장 확정 2026-09-23). 타이밍은 기존과 같다 —
        //      0.57 × 2.633초 ≈ 1.5초로 어차피 창 길이에 맞춰 둔 값이었다.
        // ⚠️ 그래서 돌진에서는 `hitEventFallbackNormalized` 가 **더 이상 읽히지 않는다.**
        //    다른 공격은 그대로 쓰므로 필드를 지우지는 않는다(SO 툴팁에 명시).
        _counterWindup.MarkAnimationReady();
    }

    /// <summary>돌진 경로 띠. 길이·폭은 서버가 실측해 실어 보낸다(각 피어가 따로 계산하면 갈라진다).</summary>
    [ClientRpc]
    void ShowDashTelegraphClientRpc(float length, float halfWidth, float growTime)
    {
        AttackCone.Show(coneRadius: 0f, coneAngleDeg: 0f, coneOffsetAngleDeg: 0f,
                        coneForwardOffset: 0f,
                        pathRadius: halfWidth, pathLength: length, growTime: growTime);
    }

    // ─── [G2] 예고 구간 ──────────────────────────────────────────────

    /// <summary>
    /// 예고를 시작한다 — <b>준비 자세에서 멈추고</b> 바닥 부채꼴을 채운다. 전진·판정은 아직 없다.
    /// </summary>
    /// <returns>예고 구간에 들어갔는가. false 면 예고 없는 즉발 공격이다(기존 동작).</returns>
    bool BeginTelegraph(BossAttackEntry e)
    {
        if (e == null || e.telegraphDuration <= 0f) return false;

        _attackPhase = BossAttackPhase.Telegraph;
        _attackPhaseTimer = e.telegraphDuration;

        // 🔴 **예고 시작에 한 번에 스냅해 조준을 확정한다**(팀장 확정 2026-09-18).
        //    예전에는 예고 동안 매 틱 FaceTarget() 으로 계속 돌았고, 그래서 장판이
        //    플레이어를 끝까지 따라돌아 **피할 수가 없었다.** 방향은 여기서 끝이다.
        //    ⚠️ Slerp(FaceTarget) 가 아니라 **즉시 회전**이어야 한다 — turnSpeed 10 으로는 한
        //    프레임에 몇 도밖에 못 돌아 보스가 엉뚱한 데를 때린다(2026-08-18 사고).
        FaceTargetImmediate();

        // 🔴 예산에 예고 몫을 넣는다. 안 넣으면 예고가 끝나기도 전에 안전망이 터진다 —
        //    돌진이 선딜 몫을 빠뜨려 매번 타임아웃하던 사고(2026-08-13)와 **같은 종류**다.
        // 🔴 **더 긴 예산을 덮어쓰지 않는다.** 잡기처럼 예고 뒤에 긴 체인이 이어지는 공격은
        //    위 switch 가 이미 전체 길이를 잡아 뒀다 — 여기서 갈아치우면 체인 도중 안전망이 터진다.
        _stateTimer = Mathf.Max(
            _stateTimer,
            e.telegraphDuration + (data != null ? data.attackDuration : 0.9f) + ChainBudgetSlack);

        // 슈퍼아머도 늘어난 길이로 다시 건다 — 위에서 옛 _stateTimer 로 걸렸기 때문이다.
        if (e.superArmor && status != null)
            status.ApplyStatus(StatusEffectType.SuperArmor, _stateTimer);

        // [G2-P] 준비동작 클립이 저작돼 있으면 **얼리는 대신 그걸 재생**한다(2026-09-23).
        //    클립이 예고보다 짧으면 마지막 프레임에서 스스로 멈춘다(loopTime 0 + 나가는 전이 없음).
        //    비어 있으면 기존 경로 — 잡기가 그렇다(grab_prep 클립이 아직 없다).
        if (!string.IsNullOrEmpty(e.prepStateName))
            PlayPrepPoseClientRpc(CurrentAttackSlot);
        else
            HoldAttackPoseClientRpc(CurrentAttackSlot, e.telegraphPoseNormalized);

        if (HasTelegraphShape(e)) ShowAttackConeClientRpc(CurrentAttackSlot, e.telegraphDuration);
        return true;
    }

    /// <summary>
    /// 예고가 다 찼다 — 자세를 풀고 <b>전진 + 공격</b>을 낸다.
    ///
    /// 🔴 데미지는 여기서 내지 않는다. 자세를 풀면 클립이 준비 자세부터 이어 재생되고,
    ///    그 뒤 <c>OnAttackHit</c> 이 평소대로 판정을 낸다 — <b>히트는 끝까지 애니 이벤트 전용</b>이다.
    ///    그래서 <c>telegraphPoseNormalized</c> 는 반드시 그 이벤트 시점보다 앞이어야 한다.
    /// </summary>
    void ReleaseTelegraph()
    {
        BossAttackEntry e = _currentEntry;

        // 자세 홀드를 푼다(잡기·돌진의 카운터 선딜과 같은 기구·같은 복원 경로).
        // ⚠️ prep 경로는 애초에 홀드를 안 걸지만 **그래도 부른다** — 멱등이고, 안 걸려 있으면
        //    아무 일도 안 한다. 경로마다 해제를 가르면 한쪽이 빠졌을 때 보스가 굳는다.
        if (IsSpawned) SetCounterPoseHeldClientRpc(false);
        RestoreCounterPose();

        // [G2-P] prep 을 틀었으면 애니메이터가 **준비동작 상태**에 멈춰 있다 — 공격 클립이 재생 중이
        //    아니므로 홀드를 풀어 봐야 이어지지 않는다. 재개 지점에서 공격 클립을 다시 튼다.
        // 🔴 재개 지점은 telegraphPoseNormalized 그대로다(0 이 아니다). 0 으로 내리면 공격 클립의
        //    앞부분(그 클립 자체의 준비동작)이 prep 과 겹치고, 무엇보다 **OnAttackHit 이 그만큼
        //    늦게 나가 데미지 타이밍이 밀린다** — 컴파일도 테스트도 안 깨지는 종류의 사고다.
        if (e != null && !string.IsNullOrEmpty(e.prepStateName))
            ResumeAttackFromPoseClientRpc(CurrentAttackSlot, e.telegraphPoseNormalized);

        // 다 찼으니 예고의 역할은 끝났다.
        HideAttackConeClientRpc();

        // [G5] 잡기는 전진이 아니라 **끌어당김**으로 이어진다.
        if (e != null && e.attackId == BossAttackId.Grab)
        {
            BeginGrabPull();
            return;
        }

        BeginLunge(e);

        // 단계 기계에서 내려와 base 의 단타 경로로 넘긴다(히트 = 애니 이벤트, 종료 = 이벤트 + 타이머).
        _attackPhase = BossAttackPhase.None;
    }

    /// <summary>
    /// 공격 클립을 <paramref name="poseNormalized"/> 지점에서 <b>정지된 채</b> 보여 준다.
    /// 자세 홀드 래치는 카운터 선딜과 공유하므로, 푸는 경로(<c>AbortAttackChain</c>)도 그대로 쓴다.
    /// </summary>
    /// <summary>
    /// [G2-P] 예고가 끝나 <b>공격 클립을 재개 지점부터</b> 다시 튼다(prep 경로 전용).
    ///
    /// 🔴 <c>CrossFade</c> 가 아니라 <c>Play</c> 다. 블렌딩하면 재개 지점이 흐려져
    ///    <c>OnAttackHit</c> 이 도달하는 시각이 프레임 단위로 흔들린다 — 히트는 애니 이벤트
    ///    전용이고 타이머 폴백이 없어서(정본 §3.3) 그 흔들림이 곧 데미지 유실이 된다.
    /// </summary>
    [ClientRpc]
    void ResumeAttackFromPoseClientRpc(int slot, float poseNormalized)
    {
        BossAttackEntry e = EntryFor(slot);
        if (e == null || animator == null || animator.runtimeAnimatorController == null) return;

        int hash = Animator.StringToHash(e.animatorStateName);
        if (!animator.HasState(0, hash)) return;

        animator.speed = 1f;                       // prep 은 얼리지 않지만 다른 경로가 남겼을 수 있다
        animator.Play(hash, 0, Mathf.Clamp01(poseNormalized));
        animator.Update(0f);
    }

    /// <summary>
    /// [G2-P] 예고 구간에 <b>준비동작 클립</b>을 재생한다(2026-09-23 · SVN r322 아트 반입분).
    ///
    /// 🔴 <b>자세 홀드 래치를 쓰지 않는다.</b> 기존 방식은 <c>animator.speed = 0</c> 으로 얼리고
    ///    <c>RestoreCounterPose</c> 로 되돌리는데, prep 은 클립이 **스스로 끝까지 가서 멈춘다**
    ///    (`loopTime: 0` + 나가는 전이 없음). 그래서 복원 대상이 아니다.
    ///    ⚠️ 그 조건이 깨지면(아트가 loopTime 을 켜거나 전이를 그리면) prep 이 <b>무한 반복</b>한다 —
    ///       컴파일도 테스트도 안 깨지고 화면만 이상해진다(교훈 #87 과 같은 부류).
    ///
    /// ⚠️ 예고 <b>길이</b>는 여기서 정하지 않는다. <c>telegraphDuration</c> 이 정하고, 클립이 짧으면
    ///    남은 시간은 마지막 자세로 채워진다. 클립이 길이를 정하게 두면 아트가 클립을 다시 올릴 때마다
    ///    반응 시간(= 난이도)이 조용히 바뀐다.
    /// </summary>
    [ClientRpc]
    void PlayPrepPoseClientRpc(int slot)
    {
        BossAttackEntry e = EntryFor(slot);
        if (e == null || animator == null || animator.runtimeAnimatorController == null) return;
        if (string.IsNullOrEmpty(e.prepStateName)) return;

        int hash = Animator.StringToHash(e.prepStateName);
        if (!animator.HasState(0, hash)) return;   // 오타는 스폰 시 ValidateState 가 이미 크게 울린다

        animator.Play(hash, 0, 0f);
        // 🔴 즉시 평가한다 — 안 하면 직전 자세가 한 프레임 보인다(HoldAttackPoseClientRpc 와 같은 이유).
        animator.Update(0f);
    }

    [ClientRpc]
    void HoldAttackPoseClientRpc(int slot, float poseNormalized)
    {
        BossAttackEntry e = EntryFor(slot);
        if (e == null || animator == null || animator.runtimeAnimatorController == null) return;

        int hash = Animator.StringToHash(e.animatorStateName);
        if (!animator.HasState(0, hash)) return;

        animator.Play(hash, 0, Mathf.Clamp01(poseNormalized));
        // 🔴 즉시 평가한다. 안 하면 속도를 0 으로 만든 뒤 **직전 자세가 한 프레임 보인다**
        //    (다음 평가가 영영 오지 않으므로 그 자세로 굳는다).
        animator.Update(0f);

        HoldCounterPoseLocal();
    }

    // 어퍼 Airborne CC 훅 — **의도적으로 비어 있다.**
    // 팀장 판단(2026-08-07): 어퍼 에어본은 아직 넣지 않는다. 플레이어 수신측이 AttackInfo 의 CC 필드를
    // 읽지 않기 때문이다(정본 §3.4 — 실제로 `knockbackStrength`/`staggerDuration` 을 읽는 곳이
    // 플레이어 쪽에 0건이다. 채우는 쪽만 있다: FirstMeleeMainSkill).
    // 되살릴 때는 이 훅에서 서버가 직접 status 를 걸면 된다 — 대상은 meleeAttack 의 히트 목록에서 받는다.
    protected virtual void OnUpperHit() { }

    // 공격 애니는 PlayAttackAnimClientRpc 가 CrossFade 로 담당하므로 base 의 attackTrigger 발동을 건너뛴다.
    // 그 외 상태는 base 매핑 유지 — 단 Hit 은 카운터 전용이라 전용 리액션으로 갈아탄다.
    protected override void PlayStateAnimation(MonsterState s)
    {
        // Attack 을 벗어나면 카운터 창을 닫고 Grab 체인을 끊는다(공격 취소·카운터·사망 전 경로 포함).
        // base 에 Exit 훅이 없어서, 파생이 상태 전이를 관측할 수 있는 지점은 여기뿐이다.
        //
        // 🔴 여기서 AbortAttackChain 을 부르지 않으면 카운터로 잡기를 끊거나 보스가 죽었을 때
        //    잡힌 플레이어가 이동 권한을 잃은 채 **영구히 갇힌다**(풀어 줄 주체가 사라진다).
        if (IsServer && s != MonsterState.Attack)
        {
            SetCounterWindow(false);
            AbortAttackChain();
        }

        // 23호 → Wells **단방향 푸시**(그로기/사망 동반 정지). 상태 전이를 관측할 수 있는 유일한 지점이다.
        if (IsServer)
            PushWellsState(s);

        if (s == MonsterState.Attack) return;

        // Hit = 카운터 성공 전용. base 의 hitTrigger 대신 지정된 리액션 상태(getowned)로 CrossFade 한다.
        // 🔴 RPC 가 아니라 **상태 복제**로 돌기 때문에 늦게 접속한 클라도 같은 애니를 본다.
        if (_boss != null && s == MonsterState.Hit)
        {
            // [G6] 방향은 서버가 정해 복제한 값을 읽는다. 오른쪽 상태가 비어 있으면 왼쪽으로 폴백한다.
            string reaction = _hitReactionRight.Value && !string.IsNullOrEmpty(_boss.hitReactionStateRight)
                ? _boss.hitReactionStateRight
                : _boss.hitReactionState;

            if (!string.IsNullOrEmpty(reaction))
            {
                SafeSetBool(data.groggyBool, false); // Groggy 로 넘어갈 때 base 가 다시 true 로 올린다
                SafeCrossFade(reaction);
                return;
            }
        }

        base.PlayStateAnimation(s);
    }

    [ClientRpc]
    void PlayAttackAnimClientRpc(int slot)
    {
        BossAttackEntry e = EntryFor(slot);
        if (e != null)
            SafeCrossFade(e.animatorStateName);
    }

    /// <summary>
    /// 명중 타격 연출. <b>서버가 판정 결과를 보고</b> 부른다 — 애니 이벤트로 걸면 허공을 때려도 터진다
    /// (클립은 맞았는지 모른다). 그래서 이것만은 코드가 낸다.
    ///
    /// 🔴 RPC 여야 한다. 호출부(<c>PerformAttackHit</c>)가 서버 전용이라
    /// 여기서 직접 재생하면 <b>호스트에서만 보인다</b>.
    ///
    /// Unreliable: 순수 연출이라 한 대 분이 빠져도 상태가 발산하지 않는다.
    /// </summary>
    [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Unreliable)]
    void PlayAttackHitEffectRpc(BossAttackId attackId)
    {
        EffectSocketPlayer player = HitEffectFor(attackId);
        if (player == null)
        {
            WarnNoHitEffectOnce(attackId);
            return;
        }

        player.PlayOnce();
    }

    /// <summary>
    /// 레이지 루프 시작. 호출부(<c>BeginRage</c>)가 서버 전용이라 RPC 로 나가야 한다 —
    /// 직접 재생하면 호스트에서만 보인다. Reliable(기본).
    /// </summary>
    [ClientRpc]
    void StartRageSmashClientRpc()
    {
        if (rageSmash == null)
        {
            WarnNoRageSmashOnce();
            return;
        }

        rageSmash.Play();
    }

    /// <summary>
    /// 레이지 루프 종료. 🔴 <b>Reliable 이어야 한다</b> — 유실되면 연출이 보스에 영구히 붙는다.
    /// 재생 중이 아니면 <see cref="EffectSocketPlayer.Stop"/> 이 조용한 no-op 이라 여러 경로에서 불려도 안전하다.
    /// </summary>
    [ClientRpc]
    void StopRageSmashClientRpc()
    {
        if (rageSmash != null) rageSmash.Stop();   // 미배선은 시작 시점에 이미 1회 알렸다
    }

    EffectSocketPlayer HitEffectFor(BossAttackId attackId)
    {
        switch (attackId)
        {
            case BossAttackId.LeftHook: return leftHookHit;
            case BossAttackId.RightHook: return rightHookHit;
            case BossAttackId.Upper: return upperHit;
            default: return null;
        }
    }
    #endregion

    #region Grab 체인 (Attack 안의 AttackPhase)
    // 관용구 3: Attack 안에 서브 시퀀스를 접을 때는 base 의 종료 타이머(_stateTimer)를 통째로
    // 덮어쓰고 자체 elapsed 로 단계를 나눈다(SpinnerBot 선례).
    //
    // 🔴 Grab 체인은 **커밋**이다 — 시작하면 타깃이 범위를 벗어나도 중단하지 않는다.
    //    그래서 base 의 선딜 취소(cancelWindupIfTargetLeavesRange) 경로를 타지 않는다.
    protected override void HandleAttack(float dt)
    {
        // 🔴 공격이 **끝난 시각**을 이렇게 잡는다 — 이 함수는 Attack 상태 동안 매 틱 도는데,
        //    체인이든 단타든 전부 여기를 지난다. 그래서 마지막으로 갱신된 값이 곧 "공격 종료 시각"이다.
        //    (`DecideNextAfterAction` 은 virtual 이 아니라 훅을 걸 수 없고, 종료 경로가 4곳으로 흩어져 있다.)
        _lastAttackTickTime = Time.time;

        // [G1] 전진 공격은 **단계(AttackPhase)를 쓰지 않는다** — 훅·어퍼는 단타 공격이고, 전진은
        // 애니 히트 이벤트가 끝을 알리므로 단계 기계가 필요 없다. 그래서 아래 분기보다 앞에 둔다.
        TickLunge();

        if (_attackPhase == BossAttackPhase.None)
        {
            base.HandleAttack(dt); // 단타 공격 — 히트는 애니 이벤트, 종료는 이벤트 + 타이머 폴백
            return;
        }

        _stateTimer -= dt;      // 데드락 안전망(단계 합보다 넉넉하게 잡아 둔다)
        _attackPhaseTimer -= dt;

        // 🔴 **체인 중에는 회전하지 않는다**(팀장 확정 2026-08-13). 이전 판은 여기서 매 틱
        //    `FaceChainTarget()` 으로 타깃을 향해 돌았다 — 그래서 돌진이 플레이어를 밀고 지나가지
        //    못하고 대상을 따라 맴돌았다. 방향은 `StartAttack` 직전 조준 1회로 확정된다.

        switch (_attackPhase)
        {
            // [G2] 예고 — 준비 자세에서 멈춘 채 부채꼴이 차오른다. 다 차면 전진 + 공격.
            case BossAttackPhase.Telegraph:
                // 🔴 **예고 중에는 돌지 않는다**(팀장 확정 2026-09-18 — 2026-08-18 확정의 뒤집기).
                //    예전에는 여기서 FaceTarget() 을 돌려 "장판이 도는 걸 보고 피한다"로 보았는데,
                //    실제로는 장판이 플레이어를 끝까지 따라돌아 **피할 수가 없었다.**
                //    조준은 BeginTelegraph 의 스냅 1회로 끝난다 — 돌진과 같은 성질이 됐다.
                //    ⚠️ 대가: 훅·잡기가 움직이는 플레이어를 더 자주 놓친다. 그게 맞다(팀장 확정).
                if (_attackPhaseTimer <= 0f) ReleaseTelegraph();
                break;

            case BossAttackPhase.Windup:
                // 판정은 애니 이벤트(OnAttackHit → NotifyAttackHit)가 **준비**를 알리고,
                // 실제 발사는 아래 게이트가 창 타이머 만료까지 미룬다(2026-09-02).
                // 이벤트가 유실되면 게이트가 안 열리고 _stateTimer 안전망으로 빠진다.
                //
                // 🔴 선딜 조준(2026-08-18)은 **잡기 전용으로 유지한다.** 돌진도 Windup 을 쓰게
                //    됐지만(카운터 창), 돌진이 창 1.5초 동안 타깃을 계속 쫓으면 밀고 지나가야 할
                //    돌진이 유도탄이 되어 회피 난이도가 통째로 바뀐다 — 이번 스코프 밖의 밸런스
                //    변경이라 넣지 않는다. 잡기는 성립 순간 Hold 로 넘어가 되먹임이 없다.
                // 🔴 잡기 전용 선딜 조준도 **제거했다**(2026-09-18). 예고를 보고 옆으로
                //    빠졌는데 Windup 에서 다시 따라오면 예고가 거짓말이 된다 — 훅·어퍼와 같은 규칙.

                // 🔴 [T5] 클립에 OnAttackHit 이벤트가 없으면 게이트가 **영영 안 열린다**
                //    (녹리곱이라 IsAnimationReady 가 false 로 남는다). 실제로 Boss_23_dash.001 로
                //    갈아끼우자 돌진이 애니만 잠깐 나오고 전진을 안 했다(팀장 관찰 2026-09-18).
                //    이벤트를 .meta 에 심지 않고(SVN 에서 날아간다) 정규화 시간으로 대신한다.
                TickHitEventFallback();

                _counterWindup.Tick(dt);

                if (_counterWindup.TimerElapsedBeforeAnimationReady && !_warnedCounterTimerBeforeAnimation)
                {
                    _warnedCounterTimerBeforeAnimation = true;
                    Debug.LogWarning(
                        $"[23호] {_currentEntry?.attackId} 카운터 창({CounterWindowDuration:0.##}초)이 " +
                        "애니 준비 이벤트보다 먼저 끝났다 — 이벤트를 기다린다. " +
                        "창을 늘리거나 클립 이벤트를 앞당길 것.", this);
                }

                TryReleaseCounterAttack();
                break;

            // [G5] 끌어당김 — 부채꼴 안의 전원이 보스 앞으로 끌려오는 구간. 끝나면 붙잡는다.
            case BossAttackPhase.GrabPull:
                if (_attackPhaseTimer <= 0f) AcquireGrab();
                break;

            // 붙잡는 모션 구간. 끝나면 지짐이.
            case BossAttackPhase.Acquire:
                if (!IsGrabbedValid()) { ReleaseGrabbedPlayer(); break; }
                if (_attackPhaseTimer <= 0f) BeginGrabShock();
                break;

            // 지짐이 — 붙잡은 대상에게 전기 틱 데미지.
            case BossAttackPhase.Hold:
                TickGrabHold(dt);
                if (_attackPhaseTimer <= 0f) BeginGrabSlam();
                break;

            // 내려치기 ×N — 쉼 없이 연속(팀장 확정 S3). 마지막 타에서 놓아준다.
            case BossAttackPhase.Throw:
                if (_attackPhaseTimer <= 0f) AdvanceGrabSlam();
                break;

            // 놓아주기 — 여기서부터는 인터럽트가 안 통한다(G6).
            case BossAttackPhase.GrabRelease:
                if (_attackPhaseTimer <= 0f) EnterPhase(BossAttackPhase.Recovery, GrabRecovery);
                break;

            // ── JumpAttack ────────────────────────────────────────────
            // [G4] 이륙 — 올라가는 동안. 다 오르면 체공으로 넘긴다.
            case BossAttackPhase.JumpTakeoff:
                if (_attackPhaseTimer <= 0f) BeginJumpHover();
                break;

            case BossAttackPhase.Leap:
                if (_attackPhaseTimer <= 0f) ArriveJump();
                break;

            case BossAttackPhase.Land:
                // 착지 데미지는 애니 이벤트(OnAttackHit)가 만든다. 이벤트가 없으면 데미지가 없다
                // (폴백을 넣으면 이벤트 추가 후 두 번 맞는다 — 정본 §3.3 비대칭 규칙).
                if (_attackPhaseTimer > 0f) break;

                // 🔴 **여기가 점프어택과 차징 진입이 갈리는 유일한 지점이다**(2026-09-21).
                //    차징은 복귀 경직 없이 곧바로 차징을 시작한다.
                //    ⚠️ `FinishChain()` 을 건너뛰는 것은 **의도다.** 그 함수는 자원을 정리하지 않고
                //       다음 행동만 고르므로, 차징으로 이어 갈 때는 부르면 안 된다
                //       (Codex 교차검증 2026-09-21 확인).
                if (_chargeJump)
                {
                    _chargeJump = false;
                    StartChargingInPlace();
                    break;
                }

                EnterPhase(BossAttackPhase.Recovery, JumpRecovery);
                break;

            // ── 페이즈 시퀀스 ─────────────────────────────────────────
            case BossAttackPhase.ChargeWait:
                TickChargeAura(dt);   // 접근 차단 오라는 차징 대기 구간에서만 돈다
                TickCharge();
                break;

            case BossAttackPhase.RageDash:
                TickRage(dt);
                break;

            // ── 돌진(S5) ──────────────────────────────────────────────
            case BossAttackPhase.Dash:
                TickDash();
                break;

            case BossAttackPhase.Recovery:
                if (_attackPhaseTimer <= 0f) FinishChain();
                break;
        }

        // 안전망: 이벤트 유실·예상 밖 지연으로 체인이 고착되면 강제 종료한다(조용히 멈추지 않게).
        if (_stateTimer <= 0f && _attackPhase != BossAttackPhase.None)
        {
            // 🔴 예전엔 이 문구가 "Grab 체인"으로 **하드코딩**돼 있었다. 실제로는 Dash·Jump·Charge·Rage
            //    체인도 같은 안전망을 쓰기 때문에, 돌진의 예산 부족이 **grab 버그로 오진**됐다(한 세션 소모).
            //    어느 공격인지 반드시 같이 찍는다 — 진단은 자기가 무엇을 봤는지 말해야 한다.
            string chain = _currentEntry != null ? _currentEntry.attackId.ToString() : "(엔트리 없음)";
            Debug.LogWarning($"[23호] {chain} 체인이 {_attackPhase} 에서 타임아웃 — 강제 종료한다.", this);
            AbortAttackChain();
            DecideNextAfterAction();
        }
    }

    // 애니 이벤트 종료로 체인을 끊지 않는다 — 체인이 자기 종료를 소유한다.
    // (잡기 클립의 OnAttackEnd 가 base 로 가면 Hold 에 들어가기도 전에 Attack 을 벗어난다.)
    public override void NotifyAttackEnd()
    {
        if (IsServer && _attackPhase != BossAttackPhase.None) return;
        base.NotifyAttackEnd();
    }

    // ─── [G5] 끌어당김 ────────────────────────────────────────────────
    // 기획: "전방에 부채꼴 범위를 표시한 뒤, 범위 안에 있는 플레이어를 자신의 앞으로 **한 번에** 끌어당김."
    //
    // 🔴 끌어오는 수단은 `RestraintMode.Push` 다 — 오너가 "시전자 정면 offset 지점"을 같은 규칙으로
    //    계산하므로 서버가 좌표를 쓰지 않고도 복제된다(돌진 캐리와 같은 경로).
    //    ⚠️ Push 는 **슈퍼아머 대상에게 걸리지 않는다**(Unit.Knockback 과 같은 규칙) — 안 끌려온다.
    void BeginGrabPull()
    {
        _attackPhase = BossAttackPhase.GrabPull;
        _attackPhaseTimer = GrabPullDuration;

        ReleasePulledPlayers(knockback: false);   // 재진입 방어

        BossAttackEntry e = _currentEntry;
        float radius = e != null && e.coneRadius > 0f ? e.coneRadius : GrabRadius;

        if (_grabBuffer == null) _grabBuffer = new Collider[16];
        int count = Physics.OverlapSphereNonAlloc(
            transform.position, radius, _grabBuffer, playerMask, QueryTriggerInteraction.Collide);

        for (int i = 0; i < count; i++)
        {
            Collider c = _grabBuffer[i];
            if (c == null || !MonsterTargeting.IsAttackable(c)) continue;

            Player p = c.GetComponentInParent<Player>();
            if (p == null || _pulledPlayers.Contains(p)) continue;

            // 🔴 부채꼴 판정은 **예고와 같은 기준**이어야 한다 — FindGrabTarget 과 같은 헬퍼를 쓴다.
            //    두 곳이 각자 식을 갖고 있어서 갈라졌던 것이 이번 버그의 원인이다.
            if (!InAttackCone(p.transform.position, e, 90f)) continue;

            if (p.BeginRestrainedByInstigator(gameObject, RestraintMode.Push, GrabPullFrontOffset))
                _pulledPlayers.Add(p);
        }
    }

    // 잡기 성립(Acquire). **끌려온 사람 중 보스와 가장 가까운 1명**만 붙잡고 나머지는 튕겨낸다.
    void AcquireGrab()
    {
        _attackPhase = BossAttackPhase.Acquire;

        Player target = FindNearestPulled();

        // 🔴 2026-09-21 — **붙잡는 순간의 재탐색을 없앴다**(팀장 확정).
        //    예전에는 끌려온 사람이 없으면 여기서 반경(grabRadius)을 다시 훑어 잡았다.
        //    그런데 예고(부채꼴)는 **끌어당김이 시작될 때 이미 꺼진다** — 그 뒤 GrabPull 구간
        //    약 1.2초 동안은 화면에 아무 경고가 없다. 재탐색은 그 창에 **걸어 들어온 사람**을
        //    잡았고, 그 사람은 예고를 한 번도 본 적이 없었다(팀장 관찰: "장판이 사라지고
        //    다가가서 공격하려는데 잡힌다").
        //
        //    이제 잡히는 대상은 **예고된 부채꼴 안에서 실제로 끌려온 사람뿐**이다.
        //    그래서 "예고가 판정에 대해 거짓말하지 않는다"는 이 레포의 규약이 지켜진다.
        //    대가: 전원이 슈퍼아머거나 범위 밖이면 헛잡기가 된다 — 그게 의도다.

        if (target != null)
        {
            // Push 로 끌고 있던 대상을 Carry 로 **바꿔 잡는다.** 먼저 풀지 않으면 중복 구속이 된다.
            _pulledPlayers.Remove(target);
            target.EndRestrainedByInstigator();
        }

        // 붙잡히지 않은 나머지는 여기서 놓아주고 보스 바깥으로 살짝 밀어낸다(팀장 확정 C9).
        ReleasePulledPlayers(knockback: true);

        if (target == null || !target.BeginGrabbedByInstigator(gameObject))
        {
            // 헛잡기 — 복귀 경직만 지고 끝낸다(창에 실패 대가가 붙는 것과 대칭).
            // 🔴 전기는 StartAttack 에서 이미 켜졌다. 여기서 안 끄면 헛잡은 팔에 영영 남는다.
            StopGrabPulseClientRpc();
            _grabbed = null;
            EnterPhase(BossAttackPhase.Recovery, GrabRecovery);
            return;
        }

        _grabbed = target;
        _grabTickTimer = 0f;

        // 🔴 [G6] **여기서 인터럽트 창이 열린다.** 끌어당기는 동안에는 못 끊고, 실제로 붙잡은
        //    뒤부터 3번째 내려치기 직전까지만 열려 있다(팀장 확정 C10).
        //    창을 닫는 곳은 둘이다 — 마지막 내려치기(AdvanceGrabSlam)와 이탈(AbortAttackChain).
        //    ⚠️ 데이터가 창을 끈 공격이면 열지 않는다(`opensCounterWindow`).
        if (_currentEntry != null && _currentEntry.opensCounterWindow) SetCounterWindow(true);

        // 붙잡는 모션 → (Acquire 구간) → 지짐이.
        CrossFadeGrabCycleStateClientRpc(GrabCycleState.Catch);
        EnterPhase(BossAttackPhase.Acquire, GrabCatchDuration);
    }

    // 붙잡기 모션이 끝났다 — 지짐이(전기)로 넘어간다.
    void BeginGrabShock()
    {
        _grabTickTimer = 0f;
        CrossFadeGrabCycleStateClientRpc(GrabCycleState.Shock);
        EnterPhase(BossAttackPhase.Hold, GrabHold);
    }

    Player FindNearestPulled()
    {
        Player nearest = null;
        float best = float.MaxValue;
        for (int i = 0; i < _pulledPlayers.Count; i++)
        {
            Player p = _pulledPlayers[i];
            if (p == null || !p.gameObject.activeInHierarchy) continue;

            // 🔴 **유령은 잡지 않는다.** 끌려온 뒤 붙잡히기까지 약 1.2초가 있어서 그 사이에
            //    죽을 수 있다. Soul 은 같은 오브젝트라 activeInHierarchy 가 그대로 true 이고,
            //    구속 진입부(CanReceiveServerInteraction)도 생명 상태를 보지 않는다 —
            //    즉 여기서 거르지 않으면 **유령이 붙잡힌다.**
            //    (폴백 재탐색 FindGrabTarget 에는 이 검사가 있었는데 이쪽에는 없었다.)
            if (!MonsterTargeting.IsAttackable(p.transform)) continue;

            float d = (p.transform.position - transform.position).sqrMagnitude;
            if (d >= best) continue;
            best = d;
            nearest = p;
        }
        return nearest;
    }

    /// <summary>
    /// 끌려온(붙잡히지 않은) 전원을 놓아준다.
    ///
    /// 🔴 <b>모든 이탈 경로가 여기를 지나야 한다.</b> 안 놓으면 플레이어가 이동 권한을 잃은 채
    ///    영구히 갇힌다 — 보스가 죽으면 아무도 풀어 줄 수 없다(Grab·돌진 캐리와 정확히 같은 이유).
    /// </summary>
    void ReleasePulledPlayers(bool knockback)
    {
        for (int i = 0; i < _pulledPlayers.Count; i++)
        {
            Player p = _pulledPlayers[i];
            if (p == null || !p.gameObject.activeInHierarchy) continue;

            p.EndRestrainedByInstigator();

            if (knockback && GrabPullKnockback > 0f)
                p.Knockback(AwayFromBoss(p.transform.position), GrabPullKnockback);
        }
        _pulledPlayers.Clear();
    }

    void TickGrabHold(float dt)
    {
        // 잡힌 대상이 사라지면(사망·디스폰) 체인을 정리하고 복귀한다.
        if (!IsGrabbedValid())
        {
            StopGrabPulseClientRpc();
            _grabbed = null;
            EnterPhase(BossAttackPhase.Recovery, GrabRecovery);
            return;
        }

        if (_boss == null || _boss.grabTickInterval <= 0f || _boss.grabTickDamage <= 0) return;

        _grabTickTimer -= dt;
        if (_grabTickTimer > 0f) return;
        _grabTickTimer = _boss.grabTickInterval;

        // 전기 데미지 — 서버 경로(ReceiveAttack)로 넣어 방어/쉴드 계산을 우회하지 않는다.
        var info = new AttackInfo(_boss.grabTickDamage, AttackType.Default);
        var ctx = new AttackHitContext(transform.position, transform, null);
        _grabbed.ReceiveAttack(info, ctx);

        // 틱마다 감전 연출. 🔴 이 메서드는 서버 전용이라 RPC 로 나가야 한다 —
        //    여기서 직접 재생하면 호스트에서만 보인다.
        PlayGrabbedElectricClientRpc();
    }

    // ─── [G5] 내려치기 ×N ─────────────────────────────────────────────
    // 기획: 지짐이 뒤 **내려치기 3회**, 3번째에서 플레이어를 놓아준다. 사이에 멈추는 구간은 없다(S3).

    void BeginGrabSlam()
    {
        _grabSlamsLeft = GrabSlamCount;
        EnterPhase(BossAttackPhase.Throw, GrabThrowTime);
        CrossFadeGrabCycleStateClientRpc(GrabCycleState.Slam);
        ApplyGrabSlamDamage();
    }

    // 한 타가 끝났다 — 남았으면 **쉼 없이** 다음 타, 마지막이었으면 놓아준다.
    void AdvanceGrabSlam()
    {
        _grabSlamsLeft--;

        // 🔴 [G6] **마지막 타부터는 못 끊는다**(팀장 확정: "3번째 직전까지가 인터럽트 가능").
        //    남은 타가 1 이면 지금 시작할 타가 마지막이다.
        if (_grabSlamsLeft <= 1) SetCounterWindow(false);

        if (_grabSlamsLeft > 0)
        {
            EnterPhase(BossAttackPhase.Throw, GrabThrowTime);
            // 같은 상태를 다시 재생해야 타격이 반복으로 읽힌다(CrossFade 만으로는 이어 재생된다).
            ReplayGrabSlamClientRpc();
            ApplyGrabSlamDamage();
            return;
        }

        ReleaseGrabbedPlayer();
    }

    void ApplyGrabSlamDamage()
    {
        if (!IsGrabbedValid() || _boss == null) return;

        int dmg = _boss.grabSlamDamage > 0 ? _boss.grabSlamDamage : _boss.grabThrowDamage;
        if (dmg <= 0) return;

        var info = new AttackInfo(Mathf.RoundToInt(dmg * PhaseDamageMultiplier), AttackType.Default);
        var ctx = new AttackHitContext(transform.position, transform, null);
        _grabbed.ReceiveAttack(info, ctx);
    }

    // 마지막 내려치기 — 놓아준다. 🔴 여기부터 인터럽트가 안 통한다(G6 의 창 종료 지점).
    void ReleaseGrabbedPlayer()
    {
        if (IsGrabbedValid())
        {
            Player released = _grabbed;
            Vector3 dir = transform.forward;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
            dir.Normalize();

            released.EndGrabbedByInstigator();
            OnGrabThrowRelease(released, dir, _boss != null ? _boss.grabThrowDistance : 0f);

            // 내려놓는 자리 연출. **잡고 있던 대상이 실제로 있을 때만** 낸다 —
            // 헛돈 단계(대상이 이미 사라진 경우)에 바닥이 번쩍이면 거짓 신호다.
            PlayThrowGroundVFX();
        }

        // 손에서 대상이 떠나는 순간 전기도 끊는다.
        StopGrabPulseClientRpc();

        _grabbed = null;
        CrossFadeGrabCycleStateClientRpc(GrabCycleState.End);
        EnterPhase(BossAttackPhase.GrabRelease, GrabEndDuration);
    }

    /// <summary>
    /// 던진 대상을 실제로 날리는 지점. **의도적으로 비어 있다.**
    ///
    /// 🔴 플레이어에게 변위·CC 를 적용할 경로가 아직 없다(PLAN §5.1 G1 — `AttackInfo` 의 CC 필드를
    /// 읽는 코드가 플레이어 쪽에 0건이고, 플레이어 이동 권한은 오너에게 있어 서버가 위치를 써도
    /// 복제되지 않는다). CC 적용 주체가 정해지면 여기 한 곳만 채우면 된다.
    /// </summary>
    protected virtual void OnGrabThrowRelease(Player thrown, Vector3 direction, float distance)
    {
        if (_warnedThrowDisplacement) return;
        _warnedThrowDisplacement = true;

        Debug.LogWarning(
            $"[23호] Grab Throw 의 변위({distance:0.#}m)가 아직 적용되지 않는다 — 데미지만 나간다. " +
            "플레이어 CC 수신 경로 결정 후 OnGrabThrowRelease 를 채울 것(PLAN §5.1 G1).",
            this);
    }

    void FinishChain()
    {
        _attackPhase = BossAttackPhase.None;
        DecideNextAfterAction();
    }

    // 체인을 즉시 끊고 잡은 대상을 반드시 놓는다.
    // 🔴 카운터 성공·사망·디스폰 등 **모든 이탈 경로**에서 불러야 한다 — 안 놓으면 플레이어가
    //    이동 권한을 잃은 채 영구히 갇힌다(보스가 죽으면 아무도 풀어 줄 수 없다).
    void AbortAttackChain()
    {
        if (!IsServer) return;
        // 🔴 조기 반환 **앞**이다. 체인이 이미 None 이어도 자세 홀드는 남아 있을 수 있다
        //    (준비 자세에서 멈춘 채 카운터를 맞으면 phase 는 정리됐는데 애니는 0 속도다).
        //    뒤에 두면 그 경우 보스가 영구히 굳는다.
        ResetCounterWindup();

        // [G1] 전진도 조기 반환 **앞**이다 — 훅·어퍼는 단타라 `_attackPhase` 가 항상 None 이므로
        // 🔴 뒤에 두면 **한 번도 실행되지 않는다.** 그러면 히트 이벤트 전에 카운터를 맞았을 때
        //    ① agent 속도·가속도가 돌진값(×3, 999)으로 남아 이후 **모든 추격이 초고속**이 되고
        //    ② 히트 윈도우가 열린 채 남아 다음 공격이 **같은 대상을 못 때린다.**
        EndLunge();

        // [G2] 예고도 같은 이유로 여기서 끈다 — 히트 전에 카운터·그로기·사망으로 끊기면
        // 🔴 부채꼴이 **바닥에 영구히 남는다**(점프 예고가 같은 자리에서 같은 이유로 꺼진다).
        HideAttackConeClientRpc();

        // 🔴 [G5] 끌어당긴 전원도 **조기 반환 앞**에서 놓는다. 끌려온 채로 보스가 죽으면 아무도
        //    풀어 줄 수 없다(Grab·돌진 캐리와 정확히 같은 사고). 넉백은 없다 — 정상 종료가 아니다.
        ReleasePulledPlayers(knockback: false);

        // 🔴 잡기 사이클 배수로 돌던 애니메이터도 **여기서** 되돌린다. 뒤에 두면 조기 반환 경로에서
        //    **이후 모든 애니가 1.4배**로 남는다(돌진이 agent 속도를 안 되돌려 초고속이 됐던 것과 같은 종류).
        if (IsSpawned) SetGrabCycleSpeedClientRpc(1f);
        if (animator != null && !_counterAnimatorHeldLocally) animator.speed = 1f;

        // 🔴 차징 진입 점프도 **조기 반환 앞**에서 끈다. 남겨 두면 다음 점프어택이 착지하는 순간
        //    Recovery 대신 차징을 시작한다 — 잡기 배수·전진이 여기 앞에 있는 것과 정확히 같은 이유다.
        _chargeJump = false;

        if (_attackPhase == BossAttackPhase.None && _grabbed == null && _dashCarried == null) return;

        // Grab: 잡은 대상을 놓는다.
        if (IsGrabbedValid())
            _grabbed.EndGrabbedByInstigator();
        _grabbed = null;

        // 🔴 Dash: 끌고 가던 대상도 반드시 놓는다. Grab 과 **정확히 같은 이유** —
        //    카운터·사망으로 돌진이 끊기면 플레이어가 이동 권한을 잃은 채 영구히 갇힌다.
        //    (해제 없이 보스가 죽으면 아무도 풀어 줄 수 없다.)
        ReleaseDashCarry(applyImpact: false);

        // 🔴 Jump: 체공 중 끊기면(카운터·사망) **메시가 꺼진 채로 남아 보스가 투명해진다.**
        //    예고 장판도 바닥에 영구히 남는다. 둘 다 여기서 되돌린다.
        SetModelVisibleClientRpc(true);
        // 🔴 피격 콜라이더도 반드시 되살린다 — 안 하면 보스가 **영구 무적**으로 남는다.
        //    메시 복구와 정확히 같은 이유이고, 빠뜨리면 훨씬 치명적이다(전투가 끝나지 않는다).
        SetHurtableClientRpc(true);
        HideJumpTelegraphClientRpc();
        // 같은 이유로 앞뒤 표식 억제도 되돌린다 — 안 하면 표식이 **영구히 숨은 채** 남는다.
        ReleaseDirectionIndicatorClientRpc();
        // 🔴 Grab: 팔 전기 펄스는 그랩 애니가 나갈 때 켜진다(StartAttack). 정상 종료 경로 셋
        //    (던지기·헛잡기·대상 소멸)은 각자 끄지만, 카운터·그로기·사망으로 끊기면 그 셋을 다 건너뛴다
        //    → **팔에 전기가 영영 남는다.** 여기가 그 마지막 그물이다.
        StopGrabPulseClientRpc();
        // 🔴 점프가 착지 없이 끊기면 Wells 투척 억제가 **영구히 걸린 채** 남는다 → 폭탄이 영영 안 나온다.
        ReleaseWellsSuppression();

        // 🔴 Rage: 돌진 중 끊기면 **에이전트 속도가 8배로 고정되고 히트 윈도우가 열린 채 남는다**
        //    (그 뒤 모든 이동이 초고속이 되고, 다음 공격이 유닛당 1회 제한을 물려받는다).
        // StopRageDash 가 돌진 루프 연출까지 함께 회수한다(돌진이 끝나는 모든 길이 그 함수를 지난다).
        if (_rageDashing) StopRageDash();
        _rageRemaining = 0;

        // 🔴 송전기: 전기 장판과 송전탑이 남는다 — 보스가 죽어도 아레나에 계속 피해를 준다.
        //    구슬도 같이 걷는다. 카운터·그로기·사망으로 끊긴 것이므로 **깨지는** 마무리다
        //    (레거시에는 없던 경로다 — ChargeController 는 EndCharge 하나뿐이었다).
        EndChargeBallClientRpc(broken: true);
        EndChargeZone();
        EndChargeAura();     // 접근 차단 오라도 함께 끈다(카운터·사망으로 끊길 때)
        _charge?.Cancel();

        _attackPhase = BossAttackPhase.None;
        _attackPhaseTimer = 0f;
    }

    void EnterPhase(BossAttackPhase phase, float duration)
    {
        _attackPhase = phase;
        _attackPhaseTimer = Mathf.Max(0f, duration);
    }

    bool IsGrabbedValid() => _grabbed != null && _grabbed.gameObject.activeInHierarchy;

    // 🔴 **공격 중에는 회전하지 않는다**(팀장 확정 2026-08-13). base 의 매 틱 회전도 함께 끈다.
    //
    //    확정 스펙: 돌진은 플레이어를 **밀고 지나가고**, 돌진이 **끝나야** 다시 플레이어를 본다.
    //    회전을 남겨 두면 어떤 공격이든 보스가 대상을 따라 돌아 제자리에서 맴돈다.
    //    조준은 `StartAttack` 직전의 `FaceTarget()` 1회 — 그 방향이 공격이 끝날 때까지 유지된다.
    //    공격이 끝나면 Chase/Idle 로 돌아가며 base 가 다시 타깃을 본다.
    //
    //    ⚠️ 잡기 되먹임(2026-08-10 수정)도 이 규칙이 함께 덮는다 — 잡힌 플레이어가 손 소켓을
    //       따라가는데 그 플레이어를 향해 `LookRotation` 을 걸면 끝없이 돌던 문제.
    //    ⚠️ 대가: 훅·잡기가 움직이는 플레이어를 놓치기 쉬워진다. 확정 스펙이므로 그대로 둔다.
    protected override bool FaceTargetWhileAttacking => false;

    // 🔴 **선딜 동안에는 돈다**(팀장 확정 2026-08-18). 위 규칙(공격 중 회전 없음)은 그대로다 —
    //    바뀐 것은 회전이 감속(No23 = turnSpeed 10)이 되면서 "조준 1회"가 성립하지 않게 된 점이다.
    //    한 프레임 Slerp 로는 몇 도밖에 못 돌아 보스가 엉뚱한 데를 때린다. 그래서 히트 이벤트가
    //    나가기 전까지를 조준 구간으로 쓴다.
    //    ⚠️ 잡기 되먹임은 여전히 안전하다 — 되먹임은 플레이어가 손 소켓에 붙은 **뒤**(AcquireGrab,
    //       즉 히트 이벤트 시점) 생긴다. 그 순간부터는 이 값이 false 인 것과 같다.
    //
    // 🔴 **[G1/G2] 전진 공격은 방향을 잠근다.** 전진은 `agent.SetDestination` 으로 가는데 여기서
    //    매 틱 `FaceTarget()` 이 돌면 둘이 회전을 두고 싸워 경로가 휜다. 그리고 기획 문서가 요구하는
    //    회피법("23호의 진행 방향 옆으로 피한다")은 **경로가 예측 가능할 때만** 성립한다 — 돌면 유도탄이다.
    //
    //    ⚠️ 조건이 `_lunging` 이 아니라 `_attackFacingLocked` 인 이유가 있다. 전진이 히트보다 먼저
    //       끝나면 `_lunging` 은 그 순간 false 가 되고, 그러면 **히트 직전에 다시 조준이 돌아**
    //       끝점 부채꼴이 예고와 다른 곳을 때린다. 잠금은 히트까지 유지해야 한다.
    // 🔴 **예고가 있는 공격은 선딜에도 돌지 않는다**(2026-09-18). 예고 시작에 스냅했으니
    //    그 방향을 히트까지 가져간다. ⚠️ 플래그를 따로 두지 않고 `_currentEntry` 에서
    //    파생시킨다 — `_attackFacingLocked` 는 EndLunge() 가 푸는데 **잡기는 EndLunge 를 안 타서**
    //    잡기에 같이 쓰면 잠금이 영원히 남는다.
    protected override bool FaceTargetDuringWindup =>
        !_attackFacingLocked && !(_currentEntry != null && _currentEntry.telegraphDuration > 0f);

    /// <summary>
    /// 수평면 기준으로 <paramref name="worldPos"/> 가 이 공격의 부채꼴 안인가.
    ///
    /// 🔴 <b>예고·끌어당김·붙잡기가 전부 이 함수 하나를 써야 한다.</b> 예전에는
    ///    <c>BeginGrabPull</c> 만 각도를 보고 <c>FindGrabTarget</c> 은 360° 구였다 —
    ///    그래서 <b>예고 밖에 서 있는데 잡히는</b> 버그가 났다(2026-09-18).
    /// </summary>
    bool InAttackCone(Vector3 worldPos, BossAttackEntry e, float fallbackAngleDeg)
    {
        float angle = e != null && e.coneAngle > 0f ? e.coneAngle : fallbackAngleDeg;
        if (angle >= 360f) return true;   // 원형 — 방향 무관

        Vector3 fwd = transform.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
        fwd.Normalize();

        Vector3 to = worldPos - transform.position; to.y = 0f;
        if (to.sqrMagnitude < 0.0001f) return true;   // 발밑 — 방향을 정할 수 없다

        float cos = Mathf.Cos(Mathf.Clamp(angle, 0f, 360f) * 0.5f * Mathf.Deg2Rad);
        return Vector3.Dot(fwd, to.normalized) >= cos;
    }

    // ⚠️ 2026-09-21 — **미사용이 됐다.** 붙잡는 순간의 재탐색을 없애면서(위 AcquireGrab 주석)
    //    유일한 호출부가 사라졌다. 죽은 채로 두면 다음 사람이 "이게 판정이겠지" 하고 읽는다.
    //    재탐색을 되살릴 일이 있으면 **예고를 그 구간까지 유지하는 것과 세트로** 해야 한다.
    // Player FindGrabTarget()
    // {
    // if (_grabBuffer == null) _grabBuffer = new Collider[8];
    //
    // float radius = _boss != null ? _boss.grabRadius : 2.2f;
    // int count = Physics.OverlapSphereNonAlloc(
    // transform.position, radius, _grabBuffer, playerMask, QueryTriggerInteraction.Collide);
    //
    // Player nearest = null;
    // float best = float.MaxValue;
    // for (int i = 0; i < count; i++)
    // {
    // Collider c = _grabBuffer[i];
    // if (c == null) continue;
    // if (!MonsterTargeting.IsAttackable(c)) continue; // 유령은 잡지 않는다
    //
    // Player p = c.GetComponentInParent<Player>();
    // if (p == null) continue;
    //
    // // 🔴 **예고와 같은 부채꼴 안에서만 고른다**(2026-09-18 버그 수정).
    // //    이전에는 각도 판정이 없는 **360° 구**였다 — 그래서 예고가 안 그려진
    // //    보스 뒤·옆에 서 있어도 grabRadius 안이면 잡혔다(팀장 관찰).
    // if (!InAttackCone(p.transform.position, _currentEntry, 90f)) continue;
    //
    // float sqr = (p.transform.position - transform.position).sqrMagnitude;
    // if (sqr >= best) continue;
    // best = sqr;
    // nearest = p;
    // }
    // return nearest;
    // }

    // 🔴 잡기 단계 길이는 **전부 `GrabCycleSpeed` 로 나눈다** — 애니 재생속도에도 같은 배수가 걸리므로
    //    한쪽만 적용하면 애니와 FSM 이 어긋난다(그러면 이 프로젝트에선 조용한 데미지 0 이 된다).
    //    나누는 지점을 여기 한 곳으로 모아 두는 것이 그 규약의 전부다.
    float GrabCycleSpeed => _boss != null ? Mathf.Clamp(_boss.grabCycleSpeed, 0.25f, 3f) : 1f;
    float ScaledGrab(float seconds) => seconds / GrabCycleSpeed;

    float GrabHold => ScaledGrab(_boss != null ? _boss.grabHoldDuration : 2f);
    float GrabThrowTime => ScaledGrab(_boss != null ? _boss.grabThrowDuration : 0.6f);
    float GrabRecovery => ScaledGrab(_boss != null ? _boss.grabRecoveryDuration : 0.8f);
    float GrabPullDuration => ScaledGrab(_boss != null ? _boss.grabPullDuration : 1.65f);
    float GrabCatchDuration => ScaledGrab(_boss != null ? _boss.grabCatchDuration : 1.1f);
    float GrabEndDuration => ScaledGrab(_boss != null ? _boss.grabEndDuration : 1.37f);
    int GrabSlamCount => _boss != null ? Mathf.Max(1, _boss.grabSlamCount) : 3;
    float GrabPullKnockback => _boss != null ? Mathf.Max(0f, _boss.grabPullKnockback) : 4f;
    float GrabRadius => _boss != null ? Mathf.Max(0.1f, _boss.grabRadius) : 2.2f;

    // 끌려온 사람이 서는 자리 = 보스 정면 이만큼 앞. 돌진 캐리와 같은 규약(정면 offset)이다.
    float GrabPullFrontOffset => Mathf.Max(0.5f, GrabRadius * 0.6f);

    /// <summary>잡기 사이클의 애니 단계. 상태명 문자열은 각 피어가 SO 에서 조회한다.</summary>
    enum GrabCycleState : byte { Catch = 0, Shock = 1, Slam = 2, End = 3 }

    // 단계 애니. 상태 복제로는 단계를 실을 수 없어(전부 Attack 이다) ClientRpc 로 보낸다.
    // 🔴 NGO 는 RPC 파라미터로 string 을 못 싣는다 — 단계 번호만 보내고 이름은 각 피어가 SO 에서 읽는다.
    [ClientRpc]
    void CrossFadeGrabCycleStateClientRpc(GrabCycleState stage)
    {
        if (_boss == null) return;

        string state = stage switch
        {
            GrabCycleState.Catch => _boss.grabCatchState,
            GrabCycleState.Shock => _boss.grabHoldState,
            GrabCycleState.Slam => _boss.grabThrowState,
            GrabCycleState.End => _boss.grabEndState,
            _ => null,
        };

        if (!string.IsNullOrEmpty(state))
            SafeCrossFade(state);
    }

    /// <summary>
    /// 같은 내려치기 상태를 **처음부터 다시** 재생한다.
    /// 🔴 <c>CrossFade</c> 로는 안 된다 — 이미 그 상태에 있으면 이어 재생돼 2·3타가 보이지 않는다.
    /// </summary>
    [ClientRpc]
    void ReplayGrabSlamClientRpc()
    {
        if (_boss == null || animator == null || animator.runtimeAnimatorController == null) return;
        if (string.IsNullOrEmpty(_boss.grabThrowState)) return;

        int hash = Animator.StringToHash(_boss.grabThrowState);
        if (animator.HasState(0, hash)) animator.Play(hash, 0, 0f);
    }

    /// <summary>
    /// 잡기 사이클 동안 애니메이터를 <c>grabCycleSpeed</c> 배로 돌린다. 1 을 주면 원래대로.
    ///
    /// 🔴 자세 홀드(<c>SetCounterPoseHeldClientRpc</c>)와 **같은 `animator.speed` 를 만진다.**
    ///    홀드는 걸 때의 속도를 저장했다가 되돌리므로, 배수를 **홀드보다 먼저** 걸어야 복원이 맞는다.
    /// </summary>
    [ClientRpc]
    void SetGrabCycleSpeedClientRpc(float speed)
    {
        if (animator == null) return;

        // 홀드 중이면 지금 속도를 바꾸면 안 된다(0 이어야 한다) — 복원될 값만 갈아 끼운다.
        if (_counterAnimatorHeldLocally) _counterAnimatorResumeSpeed = speed;
        else animator.speed = speed;
    }
    #endregion

    #region JumpAttack (Attack 안의 AttackPhase)
    // 시퀀스: Leap(도약+체공, 착지점 확정·예고 장판·메시 off) → 착지점으로 이동 + 메시 on
    //         → Land(착지 클립, OnAttackHit 에 AoE) → Recovery → 재판단.
    //
    // ⚠️ **애니메이터 `Jump` Int 를 쓰지 않는다.** 정본 §7 의 🔴 함정("Jump 를 0 으로 되돌리지 않으면
    //    다음 JumpAttack 이 영원히 안 나온다")은 그 Int 로 클립을 넘기는 구조 때문에 생긴다.
    //    단계별 상태명 CrossFade(관용구 2)로 가면 그 함정이 아예 성립하지 않는다.
    //
    // 🔴 **타겟은 행의 attackTargeting 이 정한다.** 거리 무관 + 쿨만이 게이트면 10초마다 기계적으로
    //    나와 읽히므로, 타겟 규칙이 이 공격의 의도를 만든다(팀장 확정).
    //    2026-09-03 이전에는 여기가 FindFarthestPlayer() 하드코딩이었고 SO 의 규칙 필드는
    //    아무도 안 읽는 죽은 데이터였다 — 의도를 데이터로 되돌린 것이 그 정리다.
    void BeginJump()
    {
        // 착지점 = 이 공격이 노리는 대상의 발밑(바닥에 투영). 없으면 제자리.
        Vector3 point = transform.position;
        Transform aimed = ResolveAttackTarget(_currentEntry);
        if (aimed != null)
        {
            // 🔴 **노린 사람이 어그로를 가져간다**(2026-09-03 확정). 여기서 승계하지 않으면 착지 후
            //    보스가 원래 대상에게 되돌아 달려가 "멀리 때리고 돌아온다"가 된다 — 후열을 응징하려고
            //    후열을 노리게 만든 공격인데 압박이 안 남는다.
            //    ⚠️ 체공 중에는 모델이 숨겨져 있어(SetModelVisibleClientRpc) 선딜 조준이 새 대상으로
            //       돌아도 보이지 않는다. 착지점은 아래에서 이 aimed 로 이미 확정된다.
            AdoptAggro(aimed, "점프 조준");

            point = aimed.position;

            // 🔴 **플레이어 정확히 위에 내려앉으면 안 된다**(팀장 관찰 2026-08-13:
            //    "플레이어가 가만히 있으면 띄워지고 그 위로 올라가짐").
            //    `agent.Warp` 로 보스 캡슐을 플레이어 캡슐 안에 꽂아 넣으면 물리 디페네트레이션이
            //    두 캡슐을 밀어내는데, 수평으로 막히면 **위로** 빠진다 → 플레이어가 떠오른다.
            //    → 착지점을 **보스가 오던 방향으로** 조금 당겨 캡슐이 겹치지 않게 한다.
            //    ⚠️ 예고 장판도 이 지점을 쓴다(아래) — 판정과 예고가 어긋나지 않는다.
            //    ⚠️ 착지 AoE 반경(3.5)이 이 간격보다 훨씬 커서 **데미지는 그대로 들어간다.**
            Vector3 back = transform.position - point;
            back.y = 0f;
            if (back.sqrMagnitude < 0.0001f) back = -transform.forward;
            point += back.normalized * JumpLandSeparation;
        }

        if (GroundProbe.TryFindGround(point, 0, out RaycastHit ground, out _))
            point = new Vector3(point.x, ground.point.y, point.z);

        _jumpArrivePoint = point;

        // 🔴 [G4] 착지 예고는 **체공이 시작할 때** 띄운다(BeginJumpHover).
        //    이륙 구간에 띄우면 보스가 아직 땅에 서 있는데 착지 표식이 먼저 나온다
        //    (팀장 관찰 2026-09-18: "이펙트가 바로 생성된다").

        // 🔴 **공중에서는 폭탄을 던지지 않는다**(팀장 확정 2026-08-13).
        //    해제는 착지(ArriveJump)와 체인 중단(AbortAttackChain).
        _wells?.SetSuppressed(true);

        BeginJumpTakeoff();
    }

    /// <summary>
    /// [G4] <b>이륙 구간 개시.</b> 여기서는 모델도 피격 콜라이더도 살아 있다 — 보이고 맞는다(E19).
    /// 숨김·무적은 이륙이 끝난 뒤(<see cref="BeginJumpHover"/>)로 미룬다.
    ///
    /// 🔴 <b>점프어택과 차징 진입이 공유한다</b>(2026-09-21). 차징도 "올라갔다 사라졌다 떨어진다"라
    ///    같은 그림인데, 전용 단계를 새로 파면 이륙 배속 역산·모델 숨김·무적 복구·
    ///    <c>AbortAttackChain</c> 회수가 <b>전부 복제</b>된다. 그 복제본이 한쪽만 고쳐지는 것이
    ///    이 레포에서 반복된 사고라 <b>같은 경로를 쓰고 종료 분기만</b> <c>_chargeJump</c> 로 가른다.
    ///
    /// ⚠️ 부르기 전에 <c>_jumpArrivePoint</c> 가 확정돼 있어야 한다.
    /// </summary>
    void BeginJumpTakeoff()
    {
        if (JumpTakeoffDuration > 0f && !string.IsNullOrEmpty(JumpTakeoffState))
        {
            CrossFadeJumpTakeoffClientRpc();
            EnterPhase(BossAttackPhase.JumpTakeoff, JumpTakeoffDuration);
            return;
        }

        BeginJumpHover();   // 이륙 없음(저작값 0) = 기존 동작
    }

    /// <summary>
    /// 체공 시작 — 여기서부터 보스는 <b>안 보이고 안 맞는다.</b>
    /// 🔴 이륙 배속을 반드시 되돌린다 — 빼먹으면 이후 모든 애니가 그 배수로 남는다.
    /// </summary>
    void BeginJumpHover()
    {
        RestoreAnimatorSpeedClientRpc();

        // 체공 동안 메시를 감춘다 — 착지점으로 순간이동하는 것이 보이지 않게.
        SetModelVisibleClientRpc(false);

        // 메시만 끄면 **보이지 않는 보스가 맞는다** — 피격 콜라이더도 함께 끔다(2026-08-13).
        SetHurtableClientRpc(false);

        // 예고 2개: 고정 크기(어디에 떨어지는가) + 차오르는 원(언제 떨어지는가).
        // 성장시간은 **체공 길이**다 — 여기서 띄우므로 이륙 몴을 더하지 않는다.
        //    그래야 원이 **착지 순간**에 가득 찬다.
        // 🔴 차징 진입은 **예고를 띄우지 않는다**(팀장 확정 2026-09-21). 착지에 판정이 없어서다 —
        //    예고는 "곧 여기가 위험하다"는 약속인데, 안 아픈 착지에 띄우면 그 약속이 거짓이 된다.
        if (!_chargeJump)
            ShowJumpTelegraphClientRpc(_jumpArrivePoint, JumpAoeRadius, JumpHover);

        CrossFadeJumpStateClientRpc(landing: false);

        EnterPhase(BossAttackPhase.Leap, JumpHover);
    }

    // 체공 종료 — 착지점으로 이동하고 메시를 되살린 뒤 착지 클립으로 넘어간다.
    void ArriveJump()
    {
        WarpTo(_jumpArrivePoint);
        SetModelVisibleClientRpc(true);
        SetHurtableClientRpc(true);   // 착지했으니 다시 맞는다(BeginJump 의 짝)

        CrossFadeJumpStateClientRpc(landing: true);

        // 땅에 닿았으니 투척 억제를 푼다(BeginJump 의 짝). 🔴 그로기 중이면 그쪽이 다시 억제하므로
        //    여기서 무조건 풀어도 안전하다 — PushWellsState 가 상태를 매번 다시 밀어 준다.
        ReleaseWellsSuppression();

        EnterPhase(BossAttackPhase.Land, JumpLanding);
    }

    // 점프 억제 해제 — 지금 보스 상태가 다시 억제를 요구하면(그로기·사망) 그대로 유지한다.
    void ReleaseWellsSuppression()
    {
        if (_wells == null) return;
        PushWellsState(State);
    }

    #region 입장 연출 (IBossEntranceAnimation) — BossEncounterDirector 가 시점만 알려 준다
    // 🔴 **새 RPC 를 만들지 않는다.** 점프어택의 체공·착지 전환과 정확히 같은 그림이라
    //    `CrossFadeJumpStateClientRpc` 를 그대로 재사용한다. 두 벌을 두면 한쪽만 고쳐진다.
    //    덤으로 그 RPC 가 앞뒤 표식 억제(`DirectionIndicator.SetSuppressed`)까지 같이 처리한다 —
    //    입장에도 그게 맞다(하강 중 숨고, 착지 후 나온다).

    /// <summary>하강 중 체공 포즈. <see cref="IBossEntranceAnimation"/> 참조.</summary>
    public void PlayEntranceDescentServer()
    {
        if (!IsServer || !IsSpawned) return;
        CrossFadeJumpStateClientRpc(landing: false);
    }

    /// <summary>착지 클립. 데미지는 안 나간다 — 근거는 인터페이스 주석.</summary>
    public void PlayEntranceLandingServer()
    {
        if (!IsServer || !IsSpawned) return;
        CrossFadeJumpStateClientRpc(landing: true);
    }

    /// <summary>연출 종료 — 로코모션 복귀. FSM 을 깨우기 <b>직전</b>에 불린다.</summary>
    public void EndEntranceAnimationServer()
    {
        if (!IsServer || !IsSpawned) return;

        // 🔴 표식 억제를 **명시적으로** 되돌린다. `CrossFadeJumpStateClientRpc(true)` 가 이미
        //    풀어 주지만, 착지 호출이 실패·생략된 경로(연출 중단 등)에서도 여기가 마지막 그물이다.
        ReleaseDirectionIndicatorClientRpc();

        if (data != null && !string.IsNullOrEmpty(data.locomotionState))
            CrossFadeStateClientRpc(data.locomotionState);
    }
    #endregion

    // 착지 AoE — 애니 이벤트(OnAttackHit)에서 호출된다. 예고 장판과 **같은 반경**을 쓴다
    // (예고가 판정에 대해 거짓말하지 않게 — 방향 표시기와 같은 원칙).
    void ApplyJumpLandingDamage(BossAttackEntry entry)
    {
        // 🔴 **여기가 "예고 → 착지 이펙트" 인수인계 지점이다**(VFX 배선 자리 · 팀장 확정 2026-09-04).
        //    예고는 이 줄에서 사라지고, 착지 이펙트는 이 시점부터 시작해야 겹치지 않는다.
        //    같은 프레임에 데미지 판정도 나가므로(아래) 이펙트·판정·예고 종료가 한 지점에 모인다.
        //    ⚠️ 이펙트를 다른 지점(애니 클립 이벤트 등)에 걸면 예고와 겹치거나 빈 프레임이 생긴다.
        HideJumpTelegraphClientRpc();

        // 🔴 착지 충돌 이펙트는 **이 메서드가 서버 전용이라** 반드시 RPC 로 나가야 한다
        //    (`NotifyAttackHit` 이 `IsServer` 게이트다). 여기서 직접 재생하면 **호스트에서만 보인다** —
        //    이 레포가 이미 여러 번 겪은 버그다. 아래 데미지 0 조기 반환보다 **위**인 것도 의도다:
        //    데미지가 0 이어도 착지는 일어났고, 연출이 빠지면 판정과 화면이 어긋난다.
        PlayJumpImpactRpc(transform.position);

        int dmg = _boss.jumpLandingDamage > 0
            ? _boss.jumpLandingDamage
            : (entry != null && entry.damage > 0 ? entry.damage : AttackDamage);
        dmg = Mathf.Max(0, Mathf.RoundToInt(dmg * PhaseDamageMultiplier));
        if (dmg <= 0) return;

        if (_aoeBuffer == null) _aoeBuffer = new Collider[16];

        int count = Physics.OverlapSphereNonAlloc(
            transform.position, JumpAoeRadius, _aoeBuffer, playerMask, QueryTriggerInteraction.Collide);

        _aoeHits.Clear();
        for (int i = 0; i < count; i++)
        {
            Collider hit = _aoeBuffer[i];
            if (hit == null) continue;
            if (!MonsterTargeting.IsAttackable(hit)) continue;

            Hurtbox hurtbox = hit.GetComponentInParent<Hurtbox>();
            Unit unit = hurtbox != null ? hurtbox.OwnerUnit : hit.GetComponentInParent<Unit>();
            if (unit == null || unit == this) continue;
            if (!_aoeHits.Add(unit)) continue; // 유닛당 1회

            var info = new AttackInfo(dmg, AttackType.Default);
            var ctx = new AttackHitContext(transform.position, transform, hit);
            if (hurtbox != null) hurtbox.ReceiveAttack(info, ctx);
            else unit.ReceiveAttack(info, ctx);

            // 🔴 **넉백은 따로 불러야 한다**(2026-08-13 실측). `Unit.ReceiveAttack` 은 `TakeDamage` 만
            //    하고 `AttackInfo.knockback*` 를 **읽지 않는다** — 그 필드를 채워 보냈더니 아무 일도
            //    일어나지 않았다. 넉백 진입점은 `Unit.Knockback(방향, 강도)` 하나뿐이다.
            //    ⚠️ 그 안에서 서버 가드와 **슈퍼아머 차단**을 이미 처리한다(중복 검사 불필요).
            if (JumpKnockback > 0f)
                unit.Knockback(AwayFromBoss(unit.transform.position), JumpKnockback);
        }

        DetonateBombsInJumpRange();
    }

    // 🔴 점프어택 범위 안의 폭탄은 함께 터진다(팀장 확정 2026-08-10).
    //
    // 위의 데미지 판정은 `playerMask` 로 훑기 때문에 폭탄(layer 10)이 **한 건도 걸리지 않는다** —
    // 마스크를 넓히는 대신 폭탄의 정적 레지스트리를 쓴다(`BossBomb.Active`, 서버 전용).
    // ⚠️ `Explode()` 가 레지스트리에서 자기를 빼므로 **역순 순회**한다.
    void DetonateBombsInJumpRange()
    {
        float r = JumpAoeRadius;
        float sqr = r * r;

        for (int i = BossBomb.Active.Count - 1; i >= 0; i--)
        {
            BossBomb bomb = BossBomb.Active[i];
            if (bomb == null) continue;
            if ((bomb.transform.position - transform.position).sqrMagnitude > sqr) continue;

            bomb.Explode();
        }
    }

    /// <summary>
    /// 이 공격이 <b>노릴 대상</b>을 행의 <c>attackTargeting</c> 으로 고른다(서버 전용).
    ///
    /// ⚠️ "누가 맞는가"는 여기서 안 정해진다 — 실제 피해는 히트박스·반경 판정이 따로 고른다
    ///    (훅은 겹친 전원, Grab 은 포획 순간 반경 내 최근접, Dash 는 경로에 먼저 걸린 사람).
    ///    이 함수가 정하는 것은 보스가 어디를 노리고 어디로 가느냐뿐이다.
    ///
    /// 대상을 못 찾으면 <c>null</c> 을 돌려준다 — 호출측이 폴백을 정한다(Jump 는 제자리).
    /// </summary>
    Transform ResolveAttackTarget(BossAttackEntry entry)
    {
        if (entry != null && entry.attackTargeting == BossAttackTargeting.FarthestPlayer)
        {
            Player farthest = FindFarthestPlayer();
            return farthest != null ? farthest.transform : null;
        }

        // AggroTarget — base 가 물고 있는 대상. 주기 재선정은 ShouldReacquireTarget 이 돌린다.
        return Target;
    }

    // 최원거리 플레이어(서버). 어그로 대상은 최근접 락온이라 이 규칙엔 쓸 수 없어 직접 훑는다.
    Player FindFarthestPlayer()
    {
        if (_aoeBuffer == null) _aoeBuffer = new Collider[16];

        // ⚠️ `playerScanRadius` 는 **여기 전용이 아니다** — CountAlivePlayers() 가 같은 값으로
        //    차징 송전탑 개수를 정한다. 이 값을 만지면 그쪽 판정 반경도 같이 움직인다.
        float radius = _boss != null ? _boss.playerScanRadius : 30f;
        int count = Physics.OverlapSphereNonAlloc(
            transform.position, radius, _aoeBuffer, playerMask, QueryTriggerInteraction.Collide);

        Player farthest = null;
        float best = -1f;
        for (int i = 0; i < count; i++)
        {
            Collider c = _aoeBuffer[i];
            if (c == null) continue;
            if (!MonsterTargeting.IsAttackable(c)) continue; // 유령은 타겟이 아니다

            Player p = c.GetComponentInParent<Player>();
            if (p == null) continue;

            float sqr = (p.transform.position - transform.position).sqrMagnitude;
            if (sqr <= best) continue;
            best = sqr;
            farthest = p;
        }
        return farthest;
    }

    // NavMeshAgent 를 쓰는 몸을 순간이동시킨다. Warp 를 안 쓰고 transform 만 옮기면
    // 에이전트 내부 위치가 갱신되지 않아 다음 이동에서 원래 자리로 튄다.
    void WarpTo(Vector3 position)
    {
        if (agent != null && agent.enabled)
        {
            agent.Warp(position);
            return;
        }
        transform.position = position;
    }

    float JumpHover => _boss != null ? Mathf.Max(0.1f, _boss.jumpHoverTime) : 1.2f;
    float JumpLanding => _boss != null ? Mathf.Max(0.1f, _boss.jumpLandingDuration) : 1f;
    float JumpRecovery => _boss != null ? Mathf.Max(0f, _boss.jumpRecoveryDuration) : 0.4f;
    float JumpTakeoffDuration => _boss != null ? Mathf.Max(0f, _boss.jumpTakeoffDuration) : 0f;
    string JumpTakeoffState => _boss != null ? _boss.jumpTakeoffState : null;
    float JumpAoeRadius => _boss != null ? Mathf.Max(0.1f, _boss.jumpAoeRadius) : 3.5f;
    float JumpLandSeparation => _boss != null ? Mathf.Max(0f, _boss.jumpLandSeparation) : 1.2f;
    // 🔴 강도만 있다. `Unit.Knockback(방향, 강도)` 이 받는 것이 그것뿐이라 지속·경직 노브는 두지 않는다
    //    (지속을 노출해 두면 조절해도 아무 일이 없어 "고장난 노브"가 된다).
    float JumpKnockback => _boss != null ? Mathf.Max(0f, _boss.jumpKnockbackStrength) : 9f;
    float DashKnockback => _boss != null ? Mathf.Max(0f, _boss.dashKnockbackStrength) : 12f;

    // 보스에게서 **멀어지는** 수평 방향. 겹쳐 서 있으면 보스 전방으로 민다(0 벡터 금지).
    Vector3 AwayFromBoss(Vector3 targetPosition)
    {
        Vector3 d = targetPosition - transform.position;
        d.y = 0f;
        if (d.sqrMagnitude < 0.0001f) return transform.forward;
        return d.normalized;
    }

    // ─── 표현(각 피어 로컬) ────────────────────────────────────────────
    // 🔴 예고 장판은 **보스 자식이 아니다.** 보스가 체공 중 착지점으로 이동하므로 자식이면 따라가 버린다.
    //    EffectManager 는 SetParent 를 쓰지 않고 좌표만 찍으므로 이 조건을 그냥 만족한다
    //    (복제할 상태가 없는 순수 연출이라 각 피어가 자기 매니저에서 따로 빌려 쓴다).
    [ClientRpc]
    void ShowJumpTelegraphClientRpc(Vector3 point, float radius, float growTime)
    {
        // 예고 2개 모두 카탈로그 루프 이펙트다. 수명이 시간이 아니라 "착지"라는 **사건**이라 원샷이 아니라
        // 루프다 — 끝은 HideJumpTelegraphClientRpc 가 정한다(ApplyJumpLandingDamage / AbortAttackChain).
        // 크기는 scale(= 판정 반경), 성장 시간은 partDuration 이 정한다. 둘 다 서버가 매번 계산해
        // 이 RPC 로 실어 보내는 런타임 값이다.
        ReleaseJumpTelegraphs();   // 이전 점프의 핸들이 남아 있으면 먼저 회수한다(재진입 방어)

        // TryGet 은 매니저와 **카탈로그**가 둘 다 있을 때만 true 다(둘 다 자체 경고를 남긴다).
        if (!EffectManager.TryGet(out EffectManager effects, this)) return;
        EffectCatalog catalog = effects.Catalog;

        // 크기는 **판정 반경**이 정한다 — 예고가 판정에 대해 거짓말하지 않게(방향 표시기와 같은 원칙).
        // 🔴 회전은 identity 다. 경사면 정렬을 하려면 서버가 착지점 노멀을 RPC 에 함께 실어야 한다
        //    (GroundProbe 는 서버에서만 돌았다). 지금 아레나는 평지라 뒤로 미뤘다.
        // 경계에는 partDuration 을 넘기지 않는다 — 자라지 않으므로 드라이버에 줄 시간축이 없다.
        if (catalog.Drop_Charge_Boundary != null)
        {
            _boundaryHandle = effects.PlayLooping(
                catalog.Drop_Charge_Boundary, point + Vector3.up * 0.01f, Quaternion.identity, radius);
        }
        else WarnNoBoundaryEntryOnce();

        // 차오르는 원은 경계(+1cm)보다 1cm 위 — 둘의 **상대** 순서만 이 1cm 가 정한다.
        // ⚠️ 이 2cm 로는 아레나 중앙 바닥판(보행면 +6cm)을 못 넘어 예고가 판에 묻힌다.
        //    표준 간격까지 올려 봤다가 되돌렸다 — 시차 때문이다(GroundProbe.SurfaceOffset 주석).
        //    데칼·스텐실 작업에서 함께 0 으로 간다.
        if (catalog.Drop_Charge_Indicator != null)
        {
            _indicatorHandle = effects.PlayLooping(
                catalog.Drop_Charge_Indicator, point + Vector3.up * 0.02f, Quaternion.identity,
                radius, growTime);
        }
        else WarnNoIndicatorEntryOnce();
    }

    [ClientRpc]
    void HideJumpTelegraphClientRpc()
    {
        ReleaseJumpTelegraphs();
    }

    /// <summary>
    /// 내려놓는 자리 바닥 연출 — <b>서버가 좌표를 정한다.</b>
    ///
    /// 감전 연출(<see cref="PlayGrabbedElectricRpc"/>)이 좌표를 안 싣는 것과 정반대인데, 이유가 있다:
    /// 저쪽은 <b>움직이는 손</b> 위라 서버 좌표를 박으면 클라에서 몸을 벗어나지만,
    /// 이쪽은 <b>정지한 바닥</b> 위이고 "플레이어가 어디에 떨어졌는가"라는 게임플레이 사실을 표시한다 —
    /// 각 피어가 따로 계산하면 표시가 조금씩 갈린다.
    ///
    /// 놓는 위치는 <b>그랩 소켓 아래 바닥</b>이다. <c>OnGrabThrowRelease</c> 가 아직 비어 있어
    /// (플레이어 변위 경로 미구현 — PLAN §5.1 G1) 대상은 날아가지 않고 소켓 자리에서 떨어진다.
    /// 🔴 변위가 구현되면 이 좌표도 **날아가 닿는 지점**으로 함께 옮겨야 한다 —
    /// 안 그러면 연출이 착지 지점에 대해 거짓말한다.
    /// </summary>
    void PlayThrowGroundVFX()
    {
        Transform socket = GrabSocket;
        Vector3 from = socket != null ? socket.position : transform.position;

        // 바닥을 못 찾으면 소켓 위치에 그대로 재생한다 — 이펙트가 통째로 사라지는 것보다 낫다
        // (레거시 GrabController.PlayThrowLightningVFXClientRpc 와 같은 정책).
        Vector3 point = from;
        Quaternion slope = Quaternion.identity;
        if (GroundProbe.TryFindGround(from, 0, out RaycastHit ground, out _))
        {
            point = ground.point;
            slope = Quaternion.FromToRotation(Vector3.up, ground.normal);
        }

        PlayThrowGroundClientRpc(point, slope);
    }

    /// <summary>Unreliable: 순수 연출이라 유실돼도 상태가 발산하지 않는다.</summary>
    [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Unreliable)]
    void PlayThrowGroundClientRpc(Vector3 point, Quaternion slope)
    {
        if (!EffectManager.TryGet(out EffectManager effects, this)) return;

        EffectEntry entry = effects.Catalog.Throw;
        if (entry == null)
        {
            WarnNoThrowEntryOnce();
            return;
        }
        float effectScale = 2f;
        effects.Play(entry, point, slope, effectScale);
    }

    /// <summary>
    /// 잡힌 대상 감전 연출 — 그랩 홀드 틱마다 원샷.
    ///
    /// 🔴 <b>좌표를 실어 보내지 않는다.</b> 각 피어가 자기 그랩 소켓을 읽는다 —
    /// 소켓은 움직이는 보스의 본이라, 서버 좌표를 박아 보내면 클라에서 그만큼 몸에서 떨어져 뜬다
    /// (<c>MonsterBase.PlayHitVFXRpc</c> 가 같은 이유로 같은 선택을 한다).
    /// ⚠️ 호스트는 곧 서버라 이 어긋남이 0이다 — 검증은 반드시 MPPM 클라이언트 창에서 한다.
    ///
    /// Unreliable: 반복되는 순수 연출이라 한 틱이 빠져도 상태가 발산하지 않는다.
    /// </summary>
    [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Unreliable)]
    void PlayGrabbedElectricClientRpc()
    {
        if (!EffectManager.TryGet(out EffectManager effects, this)) return;

        EffectEntry entry = effects.Catalog.Grabbed_Electric;
        if (entry == null)
        {
            WarnNoGrabbedElectricOnce();
            return;
        }

        // 소켓이 없으면 보스 위치로 떨어뜨린다 — 이펙트가 통째로 사라지는 것보다 낫다
        // (레거시 GrabController.PlayThrowLightningVFXClientRpc 와 같은 정책).
        Transform socket = GrabSocket;
        Vector3 point = socket != null ? socket.position : transform.position;

        effects.Play(entry, point, Quaternion.identity);
    }

    /// <summary>
    /// 잡힌 플레이어가 매달리는 소켓. 레거시 <see cref="GrabController"/> 가 들고 있는 것을 그대로 쓴다 —
    /// <c>PlayerStateController</c> 의 캐리 추종 대상도 같은 트랜스폼이라, 여기서 별도 필드를 두면
    /// <b>연출과 실제로 잡혀 있는 위치가 갈릴 수 있다.</b>
    /// </summary>
    Transform GrabSocket
    {
        get
        {
            if (_grabController == null) _grabController = GetComponent<GrabController>();
            if (_grabController != null && _grabController.GrabSocket != null)
                return _grabController.GrabSocket;

            WarnNoGrabSocketOnce();
            return null;
        }
    }

    /// <summary>
    /// 팔 전기 펄스 시작. <b>각 피어가 로컬로 재생한다</b> — 호출부(<c>AcquireGrab</c>)가
    /// 서버 전용이라 RPC 로 나가지 않으면 <b>호스트에서만 보인다</b>(이 레포의 단골 버그).
    ///
    /// Reliable(기본)이다. 유실되면 이후 Stop 이 껐다고 착각하는 것이 아니라 **아예 안 켜지는** 것이라
    /// 상태가 새지는 않지만, 그랩 연출이 통째로 빠지는 것은 원샷 하나가 빠지는 것과 무게가 다르다.
    /// </summary>
    [ClientRpc]
    void StartGrabPulseClientRpc()
    {
        if (grabPulse == null)
        {
            WarnNoGrabPulseOnce();
            return;
        }

        grabPulse.Play();
    }

    /// <summary>
    /// 팔 전기 펄스 종료. <b>이미 떠 있는 펄스는 손까지 가고 사라진다</b>(새 펄스만 멈춘다).
    ///
    /// 🔴 <b>Reliable 이어야 한다.</b> 이게 유실되면 팔에 전기가 영영 남는다 —
    /// 착지 충돌 이펙트(<see cref="PlayJumpImpactRpc"/>)가 Unreliable 인 것과 정반대 이유다.
    /// 그랩이 끝나는 길이 여럿이라(성공 던지기 · 헛잡기 · 대상 소멸 · 체인 중단) 전부에서 부른다.
    /// </summary>
    [ClientRpc]
    void StopGrabPulseClientRpc()
    {
        // 여기서는 경고하지 않는다 — 미배선은 시작 시점에 이미 1회 알렸고,
        // 끝나는 길이 넷이라 여기서 또 뿌리면 진짜 신호가 묻힌다(교훈 #8).
        if (grabPulse != null) grabPulse.Stop();
    }

    /// <summary>
    /// 착지 충돌 이펙트 — <b>원샷</b>이다. 예고와 달리 수명이 사건이 아니라 시간이라
    /// (엔트리의 duration) 핸들도 회수 책임도 없다.
    ///
    /// Unreliable: 순수 연출이라 유실돼도 상태가 발산하지 않는다(펑 소리 한 번이 빠질 뿐).
    /// 예고 해제(<see cref="HideJumpTelegraphClientRpc"/>)가 Reliable 인 것과 대비된다 —
    /// 그쪽은 유실되면 <b>장판이 바닥에 영구히 남는다.</b>
    /// </summary>
    [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Unreliable)]
    void PlayJumpImpactRpc(Vector3 point)
    {
        if (!EffectManager.TryGet(out EffectManager effects, this)) return;

        EffectEntry entry = effects.Catalog.Drop_Collision;
        if (entry == null)
        {
            WarnNoImpactEntryOnce();
            return;
        }

        // 예고와 **같은 반경**을 scale 로 넘긴다 — 착지 이펙트가 예고보다 크거나 작으면
        // "예고가 판정에 대해 거짓말한" 것처럼 보인다.
        effects.Play(entry, point, JumpAoeRadius);
    }

    /// <summary>
    /// 예고 루프 핸들 2개를 회수한다. 미발급·이미 해제된 핸들은 매니저 쪽에서 조용한 no-op 이라
    /// 여러 번 불려도 안전하다.
    /// </summary>
    void ReleaseJumpTelegraphs()
    {
        ReleaseHandle(ref _boundaryHandle);
        ReleaseHandle(ref _indicatorHandle);
    }

    static void ReleaseHandle(ref EffectHandle handle)
    {
        if (!handle.IsSet) return;

        if (EffectManager.Instance != null) EffectManager.Instance.Release(handle);
        handle = EffectHandle.None;
    }

    // 체공 중 메시 숨김. 🔴 animator.transform 하위만 토글한다 —
    // 보스 루트 하위에는 방향 표시기(BossDirectionIndicator)도 있어서 전체를 끄면 그것까지 사라진다.
    [ClientRpc]
    void SetModelVisibleClientRpc(bool visible)
    {
        if (_modelRenderers == null) CacheModelRenderers();
        if (_modelRenderers == null) return;

        for (int i = 0; i < _modelRenderers.Length; i++)
            if (_modelRenderers[i] != null)
                _modelRenderers[i].enabled = visible;
    }

    // 🔴 **체공 중에는 보스를 때릴 수 없다**(팀장 확정 2026-08-13).
    //    증상: 점프어택 중 화면에는 예고 장판만 있는데 **보이지 않는 보스가 맞았다.**
    //    `SetModelVisibleClientRpc` 는 **메시만** 끈다 — 콜라이더는 그대로 남아 판정이 살아 있었다.
    //
    // 🔴 끄는 대상이 **둘**이다. 하나만 끄면 여전히 맞는다:
    //    ① `HurtBox`(layer EnemyHurtBox=14) — 정상 피격 경로
    //    ② **보스 루트의 몸 콜라이더**(layer Enemy=8) — 플레이어 공격 마스크(17664 = 8·10·14)에
    //       이것도 들어 있다. 루트 콜라이더에서 `GetComponentInParent<Hurtbox>()` 는 **자식인
    //       HurtBox 를 찾지 못하므로**(부모 방향 탐색) `Unit` 폴백 경로로 데미지가 그대로 들어간다.
    //
    // ⚠️ 전 피어에서 끈다. 판정은 서버만 하지만, 콜라이더가 클라에 남아 있으면 플레이어가
    //    **보이지 않는 몸에 막힌다**(체공 중 보스는 이륙 지점에 그대로 서 있다).
    [ClientRpc]
    void SetHurtableClientRpc(bool hurtable)
    {
        if (_hurtColliders == null) CacheHurtColliders();
        if (_hurtColliders == null) return;

        for (int i = 0; i < _hurtColliders.Length; i++)
            if (_hurtColliders[i] != null)
                _hurtColliders[i].enabled = hurtable;
    }

    void CacheHurtColliders()
    {
        var list = new List<Collider>(4);

        // ① Hurtbox 가 붙은 오브젝트의 콜라이더(Hurtbox 는 RequireComponent(Collider) 다)
        foreach (Hurtbox h in GetComponentsInChildren<Hurtbox>(true))
            if (h != null && h.TryGetComponent(out Collider c)) list.Add(c);

        // ② 루트 몸 콜라이더. 무기 히트박스(layer Weapon)는 **넣지 않는다** — 보스의 공격 판정이라
        //    이걸 끄면 착지 공격이 죽는다.
        foreach (Collider c in GetComponents<Collider>())
            if (c != null && !c.isTrigger && !list.Contains(c)) list.Add(c);

        _hurtColliders = list.ToArray();
        if (_hurtColliders.Length == 0)
            Debug.LogWarning($"{name}: 끌 피격 콜라이더를 하나도 못 찾았다 — 체공 중에도 맞는다.", this);
    }

    Collider[] _hurtColliders;

    void CacheModelRenderers()
    {
        Transform model = animator != null ? animator.transform : null;
        if (model == null) { _modelRenderers = System.Array.Empty<Renderer>(); return; }

        Renderer[] all = model.GetComponentsInChildren<Renderer>(true);
        var keep = new List<Renderer>(all.Length);
        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r == null) continue;
            // 연출용(장판 등)은 모델이 아니다 — HitFlash 와 같은 제외 규칙.
            if (r.GetComponentInParent<AoeTelegraph>() != null) continue;
            keep.Add(r);
        }
        _modelRenderers = keep.ToArray();
    }

    // 🔴 NGO 는 RPC 파라미터로 System.String 을 지원하지 않는다 — 상태명을 보내지 말고
    //    각 피어가 같은 SO 에서 조회하게 한다(Grab 의 CrossFadeGrabStateClientRpc 와 동일 패턴).
    /// <summary>
    /// [G4] 이륙 클립을 틀고 <b>재생속도를 클립 길이에서 역산</b>한다.
    ///
    /// 🔴 속도를 상수로 박지 않는다 — 아트가 클립을 다시 올려 길이가 바뀜어도
    ///    여기가 자동으로 따라간다. 상수면 조용히 어긋난다.
    /// ⚠️ <c>animator.speed</c> 는 자세 홀드·잡기 배수와 **공유**하는 값이다.
    ///    홀드 중이면 지금 속도를 바꾸지 않고 복원될 값만 갈아 끼운다(잡기와 같은 규약).
    /// </summary>
    [ClientRpc]
    void CrossFadeJumpTakeoffClientRpc()
    {
        if (animator == null || _boss == null) return;

        string state = _boss.jumpTakeoffState;
        if (string.IsNullOrEmpty(state)) return;

        SafeCrossFade(state);
        animator.Update(0f);   // 상태 정보가 이번 프레임에 즉시 갱신되게

        float len = animator.GetCurrentAnimatorStateInfo(0).length;
        float dur = Mathf.Max(0.01f, _boss.jumpTakeoffDuration);
        float speed = len > 0.01f ? len / dur : 1f;

        if (_counterAnimatorHeldLocally) _counterAnimatorResumeSpeed = speed;
        else animator.speed = speed;
    }

    /// <summary>[G4] 이륙 배속을 1 로 되돌린다. 잡기 배수 복원과 같은 규약.</summary>
    [ClientRpc]
    void RestoreAnimatorSpeedClientRpc()
    {
        if (animator == null) return;
        if (_counterAnimatorHeldLocally) _counterAnimatorResumeSpeed = 1f;
        else animator.speed = 1f;
    }

    [ClientRpc]
    void CrossFadeJumpStateClientRpc(bool landing)
    {
        // 🔴 앞뒤 표식은 **착지 후에만** 보인다(팀장 확정 2026-08-10).
        //    이 RPC 가 점프 비행 구간을 전 피어에서 정확히 감싸므로 여기서 켜고 끈다.
        //    · 서버에서만 끄면 클라 화면에는 그대로 보인다 — 표식은 클라 비주얼이다.
        //    · 표시기의 높이 기반 숨김(airborneHideHeight)만으로는 부족하다: 도약 **준비** 동안
        //      보스는 아직 땅에 있어서 표식이 미리 나온다(Play 에서 관찰된 증상).
        //    · `SetModelVisibleClientRpc` 는 표시기를 일부러 건드리지 않는다(그쪽 주석 참조) —
        //      그래서 별도 억제가 필요하다.
        DirectionIndicator?.SetSuppressed(!landing);

        if (_boss == null) return;
        string state = landing ? _boss.jumpLandingState : _boss.jumpHoverState;
        if (!string.IsNullOrEmpty(state))
            SafeCrossFade(state);
    }

    // 점프가 착지 없이 끊겼을 때(카운터·사망) 억제를 되돌린다.
    [ClientRpc]
    void ReleaseDirectionIndicatorClientRpc() => DirectionIndicator?.SetSuppressed(false);

    // 방향 표시기(앞뒤 링). 클라에도 있어야 하므로 지연 캐시로 잡는다.
    BossDirectionIndicator _dirIndicator;
    bool _dirIndicatorSearched;
    BossDirectionIndicator DirectionIndicator
    {
        get
        {
            if (!_dirIndicatorSearched)
            {
                _dirIndicator = GetComponentInChildren<BossDirectionIndicator>(true);
                _dirIndicatorSearched = true;
            }
            return _dirIndicator;
        }
    }

    void WarnNoBoundaryEntryOnce()
    {
        if (_warnedNoBoundaryEntry) return;
        _warnedNoBoundaryEntry = true;
        Debug.LogWarning(
            $"{name}: EffectCatalog 에 Drop_Charge_Boundary 가 비어 있어 착지 예고의 **경계 원**" +
            "(어디에 떨어지는가)이 표시되지 않는다 — 플레이어가 피할 근거가 없다. " +
            "EffectCatalog.asset 에 FX_Drop_Charge_Boundary_Entry 를 배선할 것.", this);
    }

    void WarnNoRageSmashOnce()
    {
        if (_warnedNoRageSmash) return;
        _warnedNoRageSmash = true;
        Debug.LogWarning(
            $"{name}: rageSmash 가 비어 있어 레이지 돌진 연출이 재생되지 않는다 — " +
            "보스가 그냥 빠르게 걸어오는 것처럼 보인다. " +
            "FX_Rage_Smash_Entry 를 물린 EffectSocketPlayer 를 이 필드에 연결할 것.", this);
    }

    // 공격별로 나눠 세지 않는다 — 첫 한 번이 어느 공격인지 알려 주면 배선을 찾아가기에 충분하고,
    // 타격마다 경고가 쌓이면 진짜 신호가 묻힌다(교훈 #8).
    void WarnNoHitEffectOnce(BossAttackId attackId)
    {
        if (_warnedNoHitEffect) return;
        _warnedNoHitEffect = true;
        Debug.LogWarning(
            $"{name}: {attackId} 명중 타격 연출이 배선되지 않았다 — 때려도 손에서 아무 일도 안 일어난다. " +
            "보스 인스펙터의 leftHookHit / rightHookHit / upperHit 에 EffectSocketPlayer 를 물릴 것 " +
            "(타격 연출이 필요 없는 공격이면 무시해도 된다).", this);
    }

    void WarnNoChargeBallOnce()
    {
        if (_warnedNoChargeBall) return;
        _warnedNoChargeBall = true;
        Debug.LogWarning(
            $"{name}: chargeBall 이 비어 있어 차징 번개구슬이 재생되지 않는다 — " +
            "송전기 페이즈에 장판만 깔리고 무엇을 막아야 하는지 보이지 않는다. " +
            "보스 프리팹에 EffectStagePlayer 를 붙이고(intro/sustain/outro/abortOutro = " +
            "ChargeBall_Grow/Loop/FadeOut/Break) 이 필드에 연결할 것.", this);
    }

    void WarnNoThrowEntryOnce()
    {
        if (_warnedNoThrowEntry) return;
        _warnedNoThrowEntry = true;
        Debug.LogWarning(
            $"{name}: EffectCatalog 에 Throw 가 비어 있어 **내려놓는 자리 연출**이 재생되지 않는다. " +
            "EffectCatalog.asset 에 배선할 것 — 이 슬롯은 예전 이름이 Throw_Lightning 이었다.", this);
    }

    void WarnNoGrabSocketOnce()
    {
        if (_warnedNoGrabSocket) return;
        _warnedNoGrabSocket = true;
        Debug.LogWarning(
            $"{name}: 그랩 소켓을 찾지 못해 감전 연출이 **보스 루트**에서 재생된다(발밑에 뜬다). " +
            "보스 프리팹에 GrabController 가 붙어 있고 grabSocket 이 채워져 있는지 확인할 것 — " +
            "Tools 의 잡기소켓 저작 도구가 채워 준다.", this);
    }

    void WarnNoGrabbedElectricOnce()
    {
        if (_warnedNoGrabbedElectric) return;
        _warnedNoGrabbedElectric = true;
        Debug.LogWarning(
            $"{name}: EffectCatalog 에 Grabbed_Electric 이 비어 있어 **잡힌 대상 감전 연출**이 " +
            "재생되지 않는다 — 피해는 들어가는데 화면에는 아무 일도 없어 보인다. " +
            "EffectCatalog.asset 에 배선할 것.", this);
    }

    void WarnNoGrabPulseOnce()
    {
        if (_warnedNoGrabPulse) return;
        _warnedNoGrabPulse = true;
        Debug.LogWarning(
            $"{name}: grabPulse 가 비어 있어 그랩 팔 전기 연출이 재생되지 않는다. " +
            "보스 프리팹에 EffectPathPlayer 를 붙이고(path = 어깨→팔꿈치→손, " +
            "effect = FX_Grab_ArmElectric_Entry) 이 필드에 연결할 것.", this);
    }

    void WarnNoImpactEntryOnce()
    {
        if (_warnedNoImpactEntry) return;
        _warnedNoImpactEntry = true;
        Debug.LogWarning(
            $"{name}: EffectCatalog 에 Drop_Collision 이 비어 있어 **착지 충돌 이펙트**가 재생되지 않는다 — " +
            "예고만 사라지고 아무 일도 안 일어난 것처럼 보인다. " +
            "EffectCatalog.asset 에 FX_Drop_Collision_Entry 를 배선할 것.", this);
    }

    void WarnNoIndicatorEntryOnce()
    {
        if (_warnedNoIndicatorEntry) return;
        _warnedNoIndicatorEntry = true;
        Debug.LogWarning(
            $"{name}: EffectCatalog 에 Drop_Charge_Indicator 가 비어 있어 착지 예고의 **차오르는 원**" +
            "(언제 떨어지는가)이 표시되지 않는다 — 경계만 보이고 타이밍을 읽을 수 없다. " +
            "EffectCatalog.asset 에 FX_Drop_Charge_Indicator_Entry 를 배선할 것.", this);
    }
    #endregion

    #region Wells (폭탄 살포 + 23호 그로기 동반 정지)
    void SetupWellsServer()
    {
        if (_wells == null)
        {
            // 웰즈 없는 보스도 성립하므로 에러가 아니다. 다만 폭탄이 안 나가는 건 알려 준다.
            Debug.LogWarning($"{name}: BossWells 자식이 없어 폭탄 살포가 돌지 않는다.", this);
            return;
        }

        _wells.ConfigureCycle(_boss != null ? _boss.bombThrowInterval : 6f);

        // [G7] 주기 만료(서버) → **드론이 들어올 자리**. 폭탄 투척은 제거됐다.
        _wells.ThrowCycleElapsed = OnWellsAttackCycle;

        // 🔴 `ThrowRequested` 는 **구독하지 않는다.** 클립 이벤트(`ThrowBombEvent`)는 fbx(SVN)에
        //    박혀 있어 지울 수 없지만, 구독자가 없으면 아무 일도 일어나지 않는다.
        //    수신부(BossWells.ThrowBombEvent)를 지우면 AnimationEvent 미싱 경고가 뜨므로 남겨 둔다.
        _wells.ValidateContract(name);
    }

    // ─── [G7] Wells 공격 슬롯 — 드론이 들어올 자리 ────────────────────
    // 🔴 **폭탄 투척은 제거됐다**(2026-09-16 팀장 확정). Wells 는 23호 위에 앉은 **장식**이고,
    //    자폭 드론 공격이 리소스와 함께 들어오면 **여기에** 넣는다.
    //
    // 주기(`bombThrowInterval`)와 그로기·사망 억제는 그대로 살려 뒀다 — 드론도 "웰즈가 자기 주기로
    // 살포한다"는 같은 규약을 쓰고, 23호 그로기 중에는 함께 멈춰야 하기 때문이다(정본 §10).
    //
    // ⚠️ 지웠던 폭탄 경로(착지점 추첨·탄도 역산·`IsInsideRoom`)는 이 변경 직전 커밋에 있다.
    //    드론이 "방 안에만 떨어진다" 같은 요구를 갖게 되면 거기서 되살리면 된다.
    void OnWellsAttackCycle()
    {
        if (!IsServer || State == MonsterState.Dead) return;

        WarnWellsAttackMissingOnce();
    }

    bool _warnedWellsAttackMissing;

    // 🔴 조용히 아무 일도 안 하면 "웰즈가 왜 안 때리지"를 다음 사람이 처음부터 판다.
    //    한 번만 찍어 "비어 있는 것이 의도"임을 남긴다(고빈도 로그는 정작 필요한 진단을 밀어낸다).
    void WarnWellsAttackMissingOnce()
    {
        if (_warnedWellsAttackMissing) return;
        _warnedWellsAttackMissing = true;
        Debug.Log($"{name}: Wells 공격 주기가 돌았지만 **아직 공격이 없다** — " +
                  "폭탄 투척은 제거됐고 자폭 드론은 미구현이다(의도된 공백). " +
                  "드론 리소스가 들어오면 OnWellsAttackCycle 에 배선할 것.", this);
    }


    // 23호 → Wells **단방향 푸시**. 🔴 Wells 가 23호를 폴링하면 순서 의존이 생긴다(정본 §10).
    void PushWellsState(MonsterState bossState)
    {
        if (!IsServer) return;

        BossWellsState next = bossState switch
        {
            MonsterState.Dead => BossWellsState.Dead,
            // Hit 은 카운터 성공 리액션이고 곧 Groggy 로 이어지므로 함께 멈춘다.
            MonsterState.Groggy or MonsterState.Hit => BossWellsState.Groggy,
            _ => BossWellsState.Idle,
        };

        _wells?.SetSuppressed(next != BossWellsState.Idle);

        if (_wellsState.Value != next)
            _wellsState.Value = next;
    }

    void OnWellsStateChanged(BossWellsState previous, BossWellsState next) => _wells?.PlayState(next);

    #endregion

    #region 페이즈 시퀀스 — 송전기(차징) → 실패 시 레이지 돌진
    // 정본 §9.1: ① Charging 진입(중앙 이동 후 대기) ② 전기 장판 on ③ 실드 점증
    //            ④ 송전탑 활성(1인 1 / 2인 2 / **3인 이상 4**) ⑤ 전멸 → Groggy / 시간초과 → Rage
    //
    // ⚠️ 송전탑 구현(아레나 오브젝트)은 IBossChargeSequence 로 분리했다. 구현이 없어도 시퀀스는
    //    **일관되게 돈다** — 제한시간이 끝나면 스펙대로 Rage 로 넘어간다(실패 취급).
    // 🔴 확정 스펙(2026-08-13): 차징은 **송전탑들의 중심으로 이동한 뒤**에 애니메이션을 한다.
    //    그래서 이 함수는 더 이상 차징을 시작하지 않는다 — **이동 구간(ChargeMove)** 을 연다.
    //    실제 시작은 도착(또는 이동 타임아웃) 뒤의 `StartChargingInPlace()` 다.
    void BeginCharge()
    {
        SetCounterWindow(false); // 차징 중엔 카운터 창 없음(확정 스펙)

        _chargePlayers = CountAlivePlayers();
        _chargePylons = PylonCountFor(_chargePlayers);

        if (_charge == null) _charge = GetComponentInChildren<IBossChargeSequence>(true);

        // 🔴 **차징 위치 = `BossLandingPoint`**(팀장 확정 2026-08-13).
        //    처음엔 "송전탑들의 중심"으로 갔는데 Play 에서 중앙으로 가지 않았다. 송전탑 선택 시점과
        //    등록 상태에 얽혀 있어(런타임 정적 레지스트리) 확정적이지 않다 →
        //    팀장 대안대로 **아레나에 이미 있는 고정 마커**로 돌아가서 차징한다.
        //    ⚠️ 참여 송전탑은 여전히 미리 골라 둔다(`TryPrepareCenter`) — Begin 이 그 집합을 재사용해
        //       이동 후 다시 고르는 것을 막는다. 중심 좌표는 마커가 없을 때만 폴백으로 쓴다.
        _chargeMoveTarget = transform.position;
        // 🔴 `out` 을 `&&` 오른쪽에 두면 단축평가로 **대입되지 않는 경로**가 생긴다(컴파일 에러).
        Vector3 pylonCenter = transform.position;
        bool havePylonCenter = _charge != null && _charge.TryPrepareCenter(_chargePylons, out pylonCenter);

        Transform landing = FindChargeLandingPoint();
        if (landing != null)
        {
            _chargeMoveTarget = landing.position;
        }
        else if (havePylonCenter)
        {
            _chargeMoveTarget = pylonCenter;
            Debug.LogWarning($"{name}: '{ChargeLandingName}' 을 못 찾아 송전탑 중심으로 간다.", this);
        }
        else
        {
            if (!_warnedNoCharge)
            {
                _warnedNoCharge = true;
                Debug.LogWarning(
                    $"{name}: 차징 위치를 잡지 못했다('{ChargeLandingName}' 없음 + 송전탑 0개) — " +
                    "제자리에서 차징한다.", this);
            }
            StartChargingInPlace();
            return;
        }

        // 보행 가능한 지점으로 스냅한다 — 중심이 탑 위·틈이면 에이전트가 영영 도착하지 못한다.
        if (UnityEngine.AI.NavMesh.SamplePosition(_chargeMoveTarget, out UnityEngine.AI.NavMeshHit hit,
                                                  ChargeCenterSampleRadius, UnityEngine.AI.NavMesh.AllAreas))
            _chargeMoveTarget = hit.position;

        // 🔴 **걸어가지 않는다 — 점프로 간다**(팀장 확정 2026-09-21: "charging 하러 갈 때
        //    사라졌다가 점프어택처럼 나타나는 걸로"). 예전에는 NavMesh 로 이동했고(ChargeMove 단계 ·
        //    `chargeMoveSpeedMultiplier` · `chargeMoveTimeout` · 못 가면 워프) 그 셋을 이번에 제거했다.
        //    되살릴 일이 있으면 git 이력(이 커밋 직전)에 그대로 있다.
        //
        //    **점프어택과 같은 경로를 쓴다** — `BeginJumpTakeoff` 주석의 이유. 도착점만 갈아끼우고
        //    종료 분기를 `_chargeJump` 로 가른다(`case Land:` 참조).
        _chargeJump = true;
        _jumpArrivePoint = _chargeMoveTarget;

        // 🔴 공중에서는 폭탄을 던지지 않는다 — 점프어택과 같은 규약(BeginJump 의 짝).
        //    해제는 착지(`ArriveJump`)와 체인 중단(`AbortAttackChain`)이 한다.
        _wells?.SetSuppressed(true);

        Debug.Log($"[23호] 송전기 — {_chargeMoveTarget} 으로 **점프** " +
                  $"(기준 {(landing != null ? ChargeLandingName : "송전탑 중심")} · " +
                  $"인원 {_chargePlayers}명 → 송전탑 {_chargePylons}개 · " +
                  $"거리 {Vector3.Distance(transform.position, _chargeMoveTarget):0.#}m)", this);

        BeginJumpTakeoff();
    }

    // 실제 차징 시작 — 송전탑을 올리고, 장판·오라를 켜고, 차징 클립을 튼다.
    void StartChargingInPlace()
    {
        // 이동용으로 올려 뒀던 속도·가속도·정지거리를 되돌리고 멈춘다(돌진과 같은 복원 경로).
        EndDashMove();

        FaceScreenSouthForCharge();   // EndDashMove 뒤에 — 에이전트가 멈춘 다음 방향을 확정한다

        _charge?.Begin(_chargePylons, ChargeTimeLimit);
        SpawnChargeZone();
        BeginChargeAura();

        // 🔴 구슬 크기는 **판정 장판과 같은 반경**이다 — 예고가 판정에 대해 거짓말하지 않게
        //    (레거시 ChargeController 가 floor 의 SphereCollider 를 매번 읽던 것과 같은 규약).
        //    SpawnChargeZone 뒤여야 반경을 읽을 수 있다.
        StartChargeBallClientRpc(transform.position, ChargeZoneRadius);

        // 도착했으니 이제 차징 애니메이션을 튼다.
        BossAttackEntry e = _currentEntry;
        if (e != null && !string.IsNullOrEmpty(e.animatorStateName))
            CrossFadeStateClientRpc(e.animatorStateName);

        Debug.Log($"[23호] 송전기 시작 — 인원 {_chargePlayers}명 → 송전탑 {_chargePylons}개, " +
                  $"제한시간 {ChargeTimeLimit:0.#}초", this);
        EnterPhase(BossAttackPhase.ChargeWait, ChargeTimeLimit);
    }

    /// <summary>
    /// 차징 자세를 <b>화면 아래(남쪽)</b>로 고정한다. 규칙과 톱다운 경계 처리는
    /// <see cref="BossChargeFacingPolicy"/> 참조.
    ///
    /// 회전은 서버 권한이고 <c>NetworkTransform</c> 이 복제하므로 RPC 가 필요 없다.
    /// 차징 대기 페이즈는 오라·차징만 돌려 재조준이 없어 한 번 돌려놓으면 유지된다.
    /// </summary>
    void FaceScreenSouthForCharge()
    {
        Camera cam = Camera.main;
        Vector3 dir = cam != null
            ? BossChargeFacingPolicy.ScreenSouth(
                cam.transform.forward, cam.transform.up, BossChargeFacingPolicy.DefaultFallback)
            : BossChargeFacingPolicy.DefaultFallback;

        transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

        if (cam == null)
            Edit.LogWarning($"{name}: 차징 방향 — 활성 카메라를 못 찾아 월드 −Z 로 섰다.", this);
    }

    float _lastAttackTickTime = -999f;   // 마지막으로 공격 중이던 시각(= 공격 종료 시각)
    float GlobalAttackInterval => _boss != null ? Mathf.Max(0f, _boss.globalAttackInterval) : 1.5f;

    // 차징 위치 마커. bossroom 에 이미 있는 것을 쓴다(`BossEncounterDirector` 도 같은 이름을 찾는다).
    const string ChargeLandingName = "BossLandingPoint";
    Transform _chargeLanding;

    Transform FindChargeLandingPoint()
    {
        // 한 번 찾으면 캐시한다. 씬이 바뀌면 파괴되므로 null 검사로 다시 찾는다.
        if (_chargeLanding != null) return _chargeLanding;

        GameObject go = GameObject.Find(ChargeLandingName);
        _chargeLanding = go != null ? go.transform : null;
        return _chargeLanding;
    }

    Vector3 _chargeMoveTarget;
    int _chargePylons;
    int _chargePlayers;
    const float ChargeCenterSampleRadius = 4f;   // 중심을 보행면으로 스냅할 때 허용 반경

    // 🔴 지금 도는 점프가 **차징 진입용**인가(2026-09-21). true 면 착지(`case Land:`)에서
    //    Recovery 로 가지 않고 곧바로 `StartChargingInPlace()` 로 넘어간다.
    //    예고 장판도 이 플래그로 억제한다(`BeginJumpHover`).
    // ⚠️ **끄는 곳이 두 군데뿐이다** — 정상 종료(`case Land:`)와 체인 중단(`AbortAttackChain`).
    //    빠뜨리면 다음 점프어택이 착지하자마자 차징을 시작한다.
    bool _chargeJump;

    // ⚠️ `ChargeArriveDistance`/`ChargeMoveTimeout`/`ChargeMoveSpeedMul` 은 2026-09-21 에 지웠다.
    //    차징이 걸어가지 않고 점프로 가게 되면서 읽는 곳이 사라졌다(BeginCharge 주석 참조).
    //    SO 필드 3종(`chargeMoveArriveDistance`/`chargeMoveSpeedMultiplier`/`chargeMoveTimeout`)도 함께.

    // 임의의 상태명을 전 피어에 CrossFade 한다(관용구 2 — 다지선다 애니는 상태 복제로 못 싣는다).
    [ClientRpc]
    void CrossFadeStateClientRpc(string stateName) => SafeCrossFade(stateName);

    void TickCharge()
    {
        BossChargeResult result = _charge != null ? _charge.Poll() : BossChargeResult.InProgress;

        // 제한시간 만료 = 실패(정본 §9.1). 구현이 없을 때도 이 경로로 빠진다.
        if (result == BossChargeResult.InProgress && _attackPhaseTimer > 0f)
            return;

        bool cleared = result == BossChargeResult.AllPylonsDestroyed;

        // 🔴 EndChargeZone 보다 **먼저** — 끝나는 방식이 결과에 따라 갈린다.
        //    기둥을 전부 부쉈다 = 플레이어가 저지했다 → 구슬이 **깨진다**(Abort).
        //    제한시간 초과 = 차징 완주 → 서서히 **사그라진다**(Stop).
        EndChargeBallClientRpc(broken: cleared);

        EndChargeZone();
        EndChargeAura();     // 🔴 성공·실패 **양쪽 모두** 여기를 지난다 — 오라가 남으면 영구 장판이 된다
        _charge?.Cancel();

        if (cleared)
        {
            // 🔴 송전기 그로기는 카운트를 올리되 **Break 로 승격하지 않는다** —
            //    페이즈 전환 직후 5초 무력화가 겹치면 페이즈 연출이 죽는다(확정 스펙).
            _attackPhase = BossAttackPhase.None;
            Debug.Log("[23호] 송전기 전멸 — 그로기(Break 승격 없음)", this);
            EnterCounterGroggy(
                allowBreak: false,
                durationOverride: _boss != null ? _boss.chargeClearGroggyDuration : 1f);
            return;
        }

        Debug.Log("[23호] 송전기 실패 — 레이지 돌진으로 넘어간다", this);
        StartRageAfterCharge();
    }

    // 차징 실패 → 레이지. 같은 Attack 상태를 이어 쓰지 않고 공격을 새로 시작한다
    // (레이지는 별도 행이라 쿨·슈퍼아머·애니를 자기 것으로 받아야 한다).
    void StartRageAfterCharge()
    {
        _attackPhase = BossAttackPhase.None;

        int slot = FindSlot(BossAttackId.RageDash);
        if (slot == NoAttack)
        {
            Debug.LogError(
                $"{name}: 공격 테이블에 RageDash 행이 없다 — 레이지를 건너뛴다. SO 에 weight 0 행으로 추가할 것.", this);
            DecideNextAfterAction();
            return;
        }

        CurrentAttackSlot = slot;
        StartAttack();
    }

    void BeginRage()
    {
        _rageRemaining = _boss != null ? Mathf.Max(1, _boss.rageDashCount) : 3;
        SetCounterWindow(false); // 레이지는 카운터 창 없음(실패 벌칙이 쉽게 풀려선 안 된다)
        BeginRageDash();
    }

    void BeginRageDash()
    {
        // 🔴 **즉시 조준이어야 한다.** 바로 다음 줄에서 transform.forward 를 돌진 방향으로 굳히기
        //    때문이다 — 감속 회전을 쓰면 그 프레임의 어중간한 각도가 그대로 방향이 된다.
        //    레이지는 연타(rageDashCount)라 매 회 새로 조준한다. (2026-08-18 감속 회전 도입)
        FaceTargetImmediate();
        _rageDashDir = transform.forward;
        _rageDashDir.y = 0f;
        if (_rageDashDir.sqrMagnitude < 0.0001f) _rageDashDir = Vector3.forward;
        _rageDashDir.Normalize();

        _rageDashing = true;
        // 연출도 돌진과 수명을 맞춘다 — 매 회 새로 켜고 StopRageDash 가 끈다.
        // (Play 는 재진입 시 먼저 회수하므로 연타로 불려도 핸들이 새지 않는다)
        StartRageSmashClientRpc();
        meleeAttack?.BeginHitWindow(); // 경로상 유닛당 1회 보장(SpinnerBot 선례)
        ApplyRageDamageSnapshot();
        StartDashMove(_rageDashDir, RageDashSpeedMul, RageDashMaxDistance);

        EnterPhase(BossAttackPhase.RageDash, RageDashDuration);
    }

    // 한 phase(RageDash) 안에서 **돌진 중 / 간격 대기 중** 두 구간이 번갈아 돈다.
    // 구간 구분은 _rageDashing 이 한다 — 타이머만으로는 둘을 가를 수 없다.
    void TickRage(float dt)
    {
        if (_rageDashing)
        {
            meleeAttack?.Hit(); // 히트 윈도우가 중복 피격을 막는다
            if (_attackPhaseTimer > 0f) return;

            StopRageDash();
            _rageRemaining--;

            if (_rageRemaining > 0)
            {
                _attackPhaseTimer = RageDashInterval; // 같은 phase 로 간격 대기
                return;
            }

            EnterPhase(BossAttackPhase.Recovery, RageDashInterval);
            return;
        }

        // 간격 대기 중 — 끝나면 다음 돌진.
        if (_attackPhaseTimer > 0f) return;
        BeginRageDash();
    }

    // 🔴 돌진이 끝나는 **모든** 길이 여기를 지난다 — TickRage(정상 종료)와
    //    AbortAttackChain(카운터·그로기·사망). 그래서 연출 회수를 여기 한 곳에 둔다.
    void StopRageDash()
    {
        _rageDashing = false;
        StopRageSmashClientRpc();
        meleeAttack?.EndHitWindow();
        EndDashMove();
    }

    void ApplyRageDamageSnapshot()
    {
        if (meleeAttack == null) return;

        BossAttackEntry e = _currentEntry;
        int dmg = _boss != null && _boss.rageDashDamage > 0
            ? _boss.rageDashDamage
            : (e != null && e.damage > 0 ? e.damage : AttackDamage);
        meleeAttack.SetDamageSnapshot(Mathf.Max(0, Mathf.RoundToInt(dmg * PhaseDamageMultiplier)));
    }

    #region 돌진 (S5 — 캐리-푸시)
    // 설계 참조 = 오버워치 라인하르트 돌진. 가져온 규칙 3가지는 BossDataSO 의 Dash 헤더에 적어 뒀다.
    //
    // 🔴 **왜 콜라이더가 아니라 NavMesh 클램프인가** (팀 논의에서 콜라이더 안이 먼저 나왔다):
    //    `Restrained.Push` 는 서버가 매 틱 "보스위치 + forward × offset" 으로 플레이어 **위치를 강제**한다.
    //    즉 끌려가는 플레이어의 콜라이더는 벽을 막아 주지 못하고, 보스 콜라이더가 벽에 닿을 때면
    //    플레이어는 이미 벽 **안**이다. 그래서 목적지를 offset + 여유만큼 앞당겨 보스가 먼저 멈추게 한다.
    //    벽 콜라이더 대신 NavMesh 를 기준으로 삼은 이유:
    //      · 기준이 "보행 가능 영역의 끝"이라 **낭떠러지로 밀어넣는 사고까지 함께 막힌다**(이 맵엔 낙하 구역이 있다)
    //      · 속도배수 6짜리 고속 이동에서 트리거 콜라이더는 프레임 사이를 건너뛴다(터널링). 레이캐스트는 안 놓친다
    //      · 프리팹에 콜라이더·레이어·충돌 매트릭스를 더 얹지 않아도 된다
    void BeginDash()
    {
        // 🔴 여기서 다시 조준하지 않는다(2026-08-13). 돌진은 애니 이벤트(클립 0.15초)로 시작하는데
        //    그 순간 타깃 쪽으로 한 번 더 돌면 "공격 중 회전 없음" 규칙이 깨지고, 선딜을 보고 피한
        //    플레이어를 다시 따라잡는 꼴이 된다. 방향은 StartAttack 조준에서 이미 확정됐다.
        _dashDir = transform.forward;
        _dashDir.y = 0f;
        if (_dashDir.sqrMagnitude < 0.0001f) _dashDir = Vector3.forward;
        _dashDir.Normalize();

        // [G2-P] prep 을 틀었으면 애니메이터가 **DashPrep 마지막 프레임에 멈춰** 있다.
        //    돌진이 시작되는 지금 공격 클립을 이어서 튼다.
        // 🔴 재개 지점이 훅·어퍼와 **다르다.** 훅·어퍼는 telegraphPoseNormalized(0.15)가 선딜 완료
        //    지점이지만, 돌진은 그 역할을 `hitEventFallbackNormalized`(0.57)가 한다 —
        //    클립 0~0.57 이 돌진 자신의 선딜이고 0.57 부터가 실제 돌진 구간이다.
        //    0.15 로 재개하면 **선딜을 두 번** 보여 주고 돌진이 늦게 시작하는 것처럼 보인다.
        if (_currentEntry != null && !string.IsNullOrEmpty(_currentEntry.prepStateName))
        {
            float resume = _currentEntry.hitEventFallbackNormalized > 0f
                ? _currentEntry.hitEventFallbackNormalized
                : _currentEntry.telegraphPoseNormalized;
            ResumeAttackFromPoseClientRpc(CurrentAttackSlot, resume);
        }

        _dashCarried = null;
        meleeAttack?.BeginHitWindow();   // 경로상 유닛당 1회 보장 — 스침 데미지가 중복되지 않는다
        ApplyDashDamageSnapshot();

        // 아직 아무도 안 끌고 있으니 여유 0. 캐리가 성립하는 순간 다시 잡는다.
        _dashBlockedAhead = StartDashMove(_dashDir, DashSpeedMul, DashMaxDistance);

        EnterPhase(BossAttackPhase.Dash, DashDuration);
    }

    void TickDash()
    {
        meleeAttack?.Hit();              // 경로상 스침 데미지(히트 윈도우가 중복을 막는다)

        if (_dashCarried == null)
            TryCarryDashTarget();

        bool arrived = DashDestinationReached();
        if (_attackPhaseTimer > 0f && !arrived) return;

        // 목적지에 **닿아서** 멈췄고 그 목적지가 보행면 끝이었으면 벽 충돌이다.
        // 시간이 먼저 끝났으면 거리를 소진한 것이라 데미지가 없다(라인하르트 규칙 ②).
        StopDash(hitWall: _dashBlockedAhead && arrived);
        EnterPhase(BossAttackPhase.Recovery, data != null ? data.attackDuration : 0.9f);
    }

    // 라인하르트 규칙 ① — 직접 충돌한 **첫 1명**만 끌고 간다. 나머지는 스침 데미지만 받는다.
    void TryCarryDashTarget()
    {
        if (_grabBuffer == null) _grabBuffer = new Collider[8];

        Vector3 probe = transform.position + _dashDir * DashCarryFrontOffset;
        int count = Physics.OverlapSphereNonAlloc(
            probe, DashCarryProbeRadius, _grabBuffer, playerMask, QueryTriggerInteraction.Collide);

        for (int i = 0; i < count; i++)
        {
            Player p = _grabBuffer[i] != null ? _grabBuffer[i].GetComponentInParent<Player>() : null;
            if (p == null) continue;

            // 🔴 bool 반환이 계약이다 — 슈퍼아머면 **밀리지 않는다**(확정 스펙: 밀림✕ 기절✕ 데미지○).
            //    데미지는 히트 윈도우가 따로 처리하므로, 여기서 거부돼도 그 대상은 맞긴 맞는다.
            if (!p.BeginRestrainedByInstigator(gameObject, RestraintMode.Push, DashCarryFrontOffset))
                continue;

            _dashCarried = p;

            // 🔴 **밀고 간 사람이 어그로를 가져간다**(2026-09-03 확정). 돌진은 조준 기준으로는 이미
            //    어그로 대상에게 가므로 승계할 새 대상이 없다 — 실제로 압박을 받은 사람은 경로에
            //    걸린 이 플레이어다. 돌진이 끝나면 보스가 그 옆에 서 있으니 어그로도 거기 남는다.
            //    ⚠️ 헛돌면(캐리 미성립) 여기까지 오지 않으므로 어그로는 그대로다.
            //    ⚠️ 방향(_dashDir)은 이미 고정돼 있고 히트도 나간 뒤라 조준이 흔들리지 않는다.
            AdoptAggro(p.transform, "돌진 캐리");

            // 이제 끌고 가므로 목적지를 앞당겨 다시 잡는다 — 안 하면 대상이 벽 안에 낀다.
            _dashBlockedAhead = StartDashMove(
                _dashDir, DashSpeedMul, DashMaxDistance, DashCarryFrontOffset + DashCarryWallMargin);
            return;
        }
    }

    void StopDash(bool hitWall)
    {
        meleeAttack?.EndHitWindow();
        EndDashMove();
        ReleaseDashCarry(applyImpact: hitWall);
    }

    // 라인하르트 규칙 ② — **벽에 처박혔을 때만** 충돌 데미지와 기절을 준다.
    // 거리를 소진하고 멈추면 놓아주기만 한다(위치 선정에 보상을 주는 설계).
    void ReleaseDashCarry(bool applyImpact)
    {
        if (_dashCarried == null) return;

        Player carried = _dashCarried;
        _dashCarried = null;

        if (carried == null || !carried.gameObject.activeInHierarchy) return;

        carried.EndRestrainedByInstigator();
        if (!applyImpact) return;

        int dmg = _boss != null && _boss.dashDamage > 0
            ? _boss.dashDamage
            : (_currentEntry != null && _currentEntry.damage > 0 ? _currentEntry.damage : AttackDamage);
        dmg = Mathf.Max(0, Mathf.RoundToInt(dmg * PhaseDamageMultiplier));

        if (dmg > 0)
        {
            var info = new AttackInfo(dmg, AttackType.Default);
            var ctx = new AttackHitContext(transform.position, transform);
            Hurtbox hurtbox = carried.GetComponentInChildren<Hurtbox>();
            if (hurtbox != null) hurtbox.ReceiveAttack(info, ctx);
            else carried.ReceiveAttack(info, ctx);
        }

        // 🔴 **벽에 처박은 충격으로 밀어낸다**(팀장 확정 2026-08-13). 방향 = 보스 → 대상 바깥쪽(벽 쪽).
        //    데미지와 **별개 호출**이다 — ReceiveAttack 은 AttackInfo 의 넉백 필드를 읽지 않는다.
        //    데미지가 0 이어도 밀리는 것이 맞으므로 위 `dmg > 0` 블록 밖에 둔다.
        if (DashKnockback > 0f)
            carried.Knockback(AwayFromBoss(carried.transform.position), DashKnockback);

        // 실제로 밀린 대상만 기절한다 — 슈퍼아머로 캐리를 거부한 대상은 여기 오지 않는다.
        if (DashStunDuration > 0f && carried.StatusEffects != null)
            carried.StatusEffects.Apply(StatusEffectType.Stunned, DashStunDuration, NetworkObjectId);
    }

    void ApplyDashDamageSnapshot()
    {
        if (meleeAttack == null) return;

        // 경로 스침 데미지. 벽 충돌 데미지(ReleaseDashCarry)와 달리 공격 행 값을 그대로 쓴다.
        BossAttackEntry e = _currentEntry;
        int dmg = e != null && e.damage > 0 ? e.damage : AttackDamage;
        meleeAttack.SetDamageSnapshot(Mathf.Max(0, Mathf.RoundToInt(dmg * PhaseDamageMultiplier)));
    }

    bool DashDestinationReached()
    {
        Vector3 a = transform.position; a.y = 0f;
        Vector3 b = _dashDestination;   b.y = 0f;
        if ((a - b).sqrMagnitude <= DashArriveEpsilon * DashArriveEpsilon) return true;

        // 🔴 **지나쳤으면 도착이다.** 가속도를 제대로 올린 뒤에야 문제가 되는 판정 —
        //    15m/s 면 60fps 에서 프레임당 0.25m 라 epsilon(0.35m) 안에 걸리지만, 30fps 면 0.5m 라
        //    목적지를 건너뛴다. 그러면 arrived 가 영원히 false 라 **벽 충돌 데미지·기절이 죽는다**
        //    (StopDash 의 hitWall 인자가 arrived 를 요구한다).
        Vector3 flat = _dashDir; flat.y = 0f;
        return Vector3.Dot(a - b, flat) > 0f;
    }
    #endregion

    // NavMesh 경계까지 클램프한 목표로 돌진(SpinnerBot 선례 — 낭떠러지 진입 불가, 가장자리에서 정지).
    // 반환값 = **목적지가 경계에서 잘렸나**(true 면 그 끝이 벽/낭떠러지다). 돌진이 벽 충돌을 판정하는 근거다.
    // clearance > 0 이면 그 지점에서 그만큼 **앞당겨** 멈춘다(캐리 대상이 벽에 끼지 않게).
    bool StartDashMove(Vector3 dir, float speedMultiplier, float maxDistance, float clearance = 0f)
    {
        _dashDestination = transform.position;

        // 🔴 **조용히 실패하지 않는다**(2026-08-18). 여기서 그냥 return 하면 목적지가 제자리로 남아
        //    TickDash 의 도착 판정이 즉시 성립하고, 보스는 클립만 재생하며 한 발도 안 나간다.
        //    실제로 그 증상으로 한 세션이 소모됐다(연출 중 FSM 이 돌아 에이전트가 꺼진 채 시작한 돌진).
        //    원인은 MonsterBase.SetServerLogicSuspended 에서 닫았지만, 다른 경로로 또 들어오면
        //    무엇이 없었는지 이 줄이 말해 준다 — 진단은 자기가 무엇을 봤는지 말해야 한다.
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            string why = agent == null ? "agent 없음"
                       : !agent.enabled ? "agent 꺼짐(연출·넉백 구간에서 시작했나?)"
                       : "NavMesh 밖";
            Debug.LogWarning($"[23호/돌진] 이동을 시작하지 못했다 — {why}. 목적지가 제자리로 남아 " +
                             "애니메이션만 돌고 변위가 0 이 된다.", this);
            return false;
        }

        Vector3 origin = transform.position;
        Vector3 desired = origin + dir * maxDistance;
        bool blocked = UnityEngine.AI.NavMesh.Raycast(origin, desired, out UnityEngine.AI.NavMeshHit hit,
                                                      UnityEngine.AI.NavMesh.AllAreas);
        if (blocked) desired = hit.position;

        if (clearance > 0f)
        {
            Vector3 pulled = desired - dir * clearance;
            // 앞당긴 지점이 출발점보다 뒤면 이미 벽에 붙어 있는 것 — 제자리에 선다(뒷걸음질 금지).
            desired = Vector3.Dot(pulled - origin, dir) > 0f ? pulled : origin;
        }

        // 🔴 stoppingDistance 를 0 으로 내린다. base 기본값(attackRange × 0.8 ≈ 1.6m)이면
        //    목적지에서 그만큼 앞에 멈춰 "도착"이 영원히 성립하지 않는다 → 벽 충돌 판정이 죽는다.
        if (_dashPrevStopDistance < 0f) _dashPrevStopDistance = agent.stoppingDistance;
        agent.stoppingDistance = 0f;

        // 🔴 속도만 올려서는 안 나간다 — 가속도가 그 속도에 **도달할 시간을 주지 않는다**(위 상수 주석).
        //    stoppingDistance 와 같은 저장/복원 규약을 따른다(재진입 시 원본을 덮어쓰지 않게 -1 가드).
        if (_dashPrevAcceleration < 0f)
        {
            _dashPrevAcceleration = agent.acceleration;
            _dashPrevAutoBraking = agent.autoBraking;
        }
        agent.acceleration = DashAcceleration;
        agent.autoBraking = false;      // 목적지 근처 감속 금지 — 벽에 처박는 판정이 이 속도를 전제한다

        agent.isStopped = false;
        agent.speed = Mathf.Max(0.1f, MoveSpeed * speedMultiplier);
        agent.SetDestination(desired);
        _dashDestination = desired;

        // 클램프가 목적지를 출발점까지 끌어당겼으면 이번 돌진은 변위가 0 이다. 벽에 코를 박고
        // 시전한 정상 상황일 수도 있지만, NavMesh.Raycast 가 **출발점이 메시 밖일 때** 같은 결과를
        // 내므로 조용히 넘기면 위와 똑같이 "제자리 돌진"으로 보인다. 값을 찍어 둘을 가른다.
        if ((desired - origin).sqrMagnitude <= DashArriveEpsilon * DashArriveEpsilon)
            Debug.LogWarning($"[23호/돌진] 목적지가 출발점과 같다 — 변위 0. origin {origin}, " +
                             $"desired {desired}, blocked {blocked}, clearance {clearance:F2}. " +
                             "벽에 붙어 시전했거나 출발점이 NavMesh 밖이다.", this);

        return blocked;
    }

    // 돌진 종료 공통 — 속도·정지거리를 되돌리고 멈춘다. 되돌리지 않으면 이후 **모든 이동이 초고속**이 되고
    // 정지거리가 0 인 채로 남아 추격이 대상에 파고든다.
    void EndDashMove()
    {
        if (agent != null && agent.enabled)
        {
            agent.speed = MoveSpeed;
            if (_dashPrevStopDistance >= 0f) agent.stoppingDistance = _dashPrevStopDistance;

            // 가속도·자동감속도 되돌린다 — 안 되돌리면 이후 **모든 추격이 즉시 최고속**이 되고
            // 목적지 앞에서 멈추지 않아 대상에 파고든다(speed 를 되돌리는 것과 같은 이유).
            if (_dashPrevAcceleration >= 0f)
            {
                agent.acceleration = _dashPrevAcceleration;
                agent.autoBraking = _dashPrevAutoBraking;
            }
        }
        _dashPrevStopDistance = -1f;
        _dashPrevAcceleration = -1f;
        StopAgentHard();
    }

    // base 의 StopAgent 는 private 이라 파생이 못 부른다 — 같은 일을 하는 최소 구현.
    void StopAgentHard()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
        agent.isStopped = true;
        agent.velocity = Vector3.zero;
    }

    #region 전진 공격(Lunge) — G1
    // 기획 문서: 훅·어퍼는 제자리가 아니라 **플레이어 방향으로 전진하면서** 때린다.
    // 전진 구간부터 이미 공격이라 경로에 닿은 사람도 맞는다(팀장 확정 B5/B6).
    //
    // 🔴 **끝나는 시점을 타이머로 잡지 않는다.** 전진은 ① 거리를 다 쓰거나 ② 애니 히트
    //    이벤트(OnAttackHit)가 도착하면 멈춘다. 후자가 핵심이다 — 그래야 "전진하며 휘두르다가
    //    주먹이 닿는 순간 멈춘다"가 되고, **클립 길이가 바뀌어도 코드가 따라간다.**
    //    (이 프로젝트에서 애니와 코드가 어긋나면 컴파일도 테스트도 안 깨지고 데미지만 0 이 된다.)
    //
    // 🔴 전진 중에는 **돌지 않는다**(FaceTargetDuringWindup 이 _lunging 을 본다). 문서가 요구하는
    //    회피법("진행 방향 옆으로 피한다")이 성립하려면 경로가 예측 가능해야 한다. 돌면 유도탄이 된다.

    void BeginLunge(BossAttackEntry e)
    {
        if (!IsServer || e == null || e.lungeDistance <= 0f) return;

        Vector3 dir = transform.forward;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
        dir.Normalize();

        // 돌진과 같은 이동 경로를 쓴다 — `_dashDir` 은 도착 판정(DashDestinationReached)이 읽는다.
        _dashDir = dir;

        // 히트 윈도우를 먼저 연다. 경로 판정과 끝점 판정이 이 하나를 공유해 1인 1회가 보장된다.
        if (meleeAttack != null)
        {
            meleeAttack.BeginHitWindow();
            _lungeHitWindowOpen = true;
        }

        // 경로 데미지 값. 형상은 앵커가 아니라 `lungePathRadius` 구다(아래 TickLunge).
        ApplyLungePathProfile(e);

        // 🔴 **clearance 를 주지 않는다(0).** `StartDashMove` 는 clearance 를 벽에 막혔을 때만이 아니라
        //    **항상** 목적지에서 빼므로, 값을 주면 전진 거리가 조용히 그만큼 짧아진다
        //    (0.6 을 줬더니 2.5m 가 1.9m 가 됐다). 평상시 돌진도 여기서는 0 을 쓴다.
        //    벽은 `NavMesh.Raycast` 클램프가 이미 막아 준다.
        //
        // ⚠️ 반환값은 **"벽에서 잘렸나"이지 성공 여부가 아니다** — 전진은 벽 충돌 판정이 없으므로 쓰지 않는다.
        //    이동을 못 시작한 경우(agent 없음·NavMesh 밖)는 그쪽이 LogWarning 을 남기고 목적지를 제자리로
        //    두므로, 다음 틱의 도착 판정이 즉시 성립해 안전하게 끝난다.
        StartDashMove(dir, Mathf.Max(0.1f, e.lungeSpeedMultiplier), e.lungeDistance);
        _lunging = true;

        // 🔴 방향은 여기서 잠가 **히트까지** 유지한다(EndLunge 가 푼다). `_lunging` 과 같이 두면
        //    도착하는 순간 풀려 부채꼴이 돌아간다 — 위 필드 선언부 주석 참조.
        _attackFacingLocked = true;
    }

    // 경로 데미지 값만 세팅한다. 형상은 앵커가 아니라 구(球)이므로 여기서 정하지 않는다.
    void ApplyLungePathProfile(BossAttackEntry e)
    {
        if (meleeAttack == null || e == null) return;

        int dmg = e.damage > 0 ? e.damage : AttackDamage;
        meleeAttack.SetDamageSnapshot(Mathf.Max(0, Mathf.RoundToInt(dmg * PhaseDamageMultiplier)));
    }

    // 매 틱 — 경로 데미지 + 도착 판정. Attack 상태 동안만 불린다.
    //
    // 🔴 경로 판정은 **보스 중심 구**다(360도 = 각도 제한 없음). 매 틱 때리므로 지나간 자리가
    //    캡슐(띠)이 되고, 그게 예고로 그리는 도형과 정확히 같다.
    //    ⚠️ 예전엔 `DashBody` 앵커를 썼는데 그건 **3.4m 폭 × 앞 4.42m** 짜리 돌진 히트박스라,
    //       전진 내내 그걸로 때리면 앞쪽 전체가 맞아 **부채꼴 모양이 무의미해졌다.**
    void TickLunge()
    {
        if (!_lunging) return;

        float pathRadius = _currentEntry != null ? _currentEntry.lungePathRadius : 0f;
        if (pathRadius > 0f && meleeAttack != null)
        {
            // 히트 윈도우가 중복을 막는다 — 돌진 TickDash 와 같은 구조.
            //
            // 🔴 **결과를 버리면 안 된다.** 경로에서 맞은 사람은 윈도우에 기록되므로 끝점 판정에서
            //    빠진다 → `StrikeMelee()` 가 0 을 돌려주고 **명중 연출이 통째로 안 나간다**
            //    (경로로만 맞힌 공격이 헛스윙처럼 보인다). 공격 단위로 누적해 둔다.
            _lungePathHits += meleeAttack.HitCone(transform.position, transform.forward, pathRadius, 360f);
        }

        if (DashDestinationReached())
            StopLungeMove();
    }

    // 전진 **이동만** 멈춘다. 히트 윈도우는 끝점 판정이 써야 하므로 여기서 닫지 않는다.
    void StopLungeMove()
    {
        if (!_lunging) return;
        _lunging = false;
        EndDashMove();
    }

    // 공격 종료 — 이동과 히트 윈도우를 모두 정리한다.
    // 🔴 끝점 판정(meleeAttack.Hit())이 **끝난 뒤에** 불러야 한다. 먼저 부르면 경로에서 맞은 사람이
    //    끝점에서 또 맞는다(윈도우가 비워지므로). 호출부는 PerformAttackHit 과 AbortAttackChain 둘이다.
    void EndLunge()
    {
        StopLungeMove();

        // 방향 고정은 **여기서** 푼다 — 도착(StopLungeMove)이 아니라 히트/이탈 시점이다.
        _attackFacingLocked = false;

        if (_lungeHitWindowOpen)
        {
            meleeAttack?.EndHitWindow();
            _lungeHitWindowOpen = false;
        }

        _lungePathHits = 0;
    }

    #endregion

    #region 차징 오라 — 접근 차단 (H1/H2)
    // 🔴 확정 스펙(2026-08-13): 차징 동안 보스 **주변 원형 범위**가 **주기적으로** 데미지 + 넉백을
    //    줘서 플레이어가 다가와 때리지 못하게 한다. 크기는 점프어택과 비슷(SO 기본 3.5m).
    //
    // ⚠️ `chargeZonePrefab`(AreaZone) 과는 별개다. 그쪽은 배선이 비어 있고 **밀치기 경로가 없다**
    //    (SpawnChargeZone 주석 참조). 여기는 **넉백까지** 필요하므로 보스가 직접 판정한다.
    // ⚠️ 판정은 서버 전용, 예고 비주얼은 전 피어. 예고가 판정에 대해 거짓말하지 않게
    //    **같은 반경**을 쓴다(점프 예고·방향 표시기와 같은 원칙).
    void BeginChargeAura()
    {
        if (ChargeAuraRadius <= 0f) return;

        _chargeAuraActive = true;
        _chargeAuraTimer = 0f;   // 시작하자마자 1회 — "다가와 있으면 즉시 밀린다"
        ShowChargeAuraClientRpc(ChargeAuraRadius);
    }

    void TickChargeAura(float dt)
    {
        if (!_chargeAuraActive) return;

        _chargeAuraTimer -= dt;
        if (_chargeAuraTimer > 0f) return;
        _chargeAuraTimer = ChargeAuraInterval;

        int dmg = Mathf.Max(0, Mathf.RoundToInt(ChargeAuraDamage * PhaseDamageMultiplier));
        if (_aoeBuffer == null) _aoeBuffer = new Collider[16];

        int count = Physics.OverlapSphereNonAlloc(
            transform.position, ChargeAuraRadius, _aoeBuffer, playerMask, QueryTriggerInteraction.Collide);

        _aoeHits.Clear();
        for (int i = 0; i < count; i++)
        {
            Collider hit = _aoeBuffer[i];
            if (hit == null) continue;

            Player p = hit.GetComponentInParent<Player>();
            if (p == null || !_aoeHits.Add(p)) continue;   // 콜라이더 여러 개짜리 대상 중복 방지

            // 넉백 방향 = **보스 → 대상** 바깥쪽. 겹쳐 서 있으면 대상의 전방으로 민다(0 벡터 금지).
            Vector3 push = p.transform.position - transform.position;
            push.y = 0f;
            if (push.sqrMagnitude < 0.0001f) push = p.transform.forward;
            push.Normalize();

            var info = new AttackInfo(dmg, AttackType.Default);
            var ctx = new AttackHitContext(transform.position, transform);

            // Hurtbox 를 우선한다(방어·쉴드 계산을 우회하지 않는 서버 경로).
            Hurtbox hurtbox = p.GetComponentInChildren<Hurtbox>();
            if (hurtbox != null) hurtbox.ReceiveAttack(info, ctx);
            else p.ReceiveAttack(info, ctx);

            // 🔴 넉백은 **별도 호출**이다 — ReceiveAttack 은 AttackInfo 의 넉백 필드를 읽지 않는다.
            //    이걸 몰라서 오라·점프·돌진 세 곳 모두 "밀리지 않는" 상태였다(2026-08-13).
            if (ChargeAuraKnockback > 0f)
                p.Knockback(push, ChargeAuraKnockback);
        }
    }

    void EndChargeAura()
    {
        if (!_chargeAuraActive) return;
        _chargeAuraActive = false;
        HideChargeAuraClientRpc();
    }

    // ─── 오라 예고(클라 비주얼) ──────────────────────────────────────────
    // 🔴 표식·예고는 **클라 비주얼**이다 — 서버에서만 끄면 클라 화면에 그대로 남는다(점프 예고와 같은 함정).
    [ClientRpc]
    void ShowChargeAuraClientRpc(float radius)
    {
        if (_boss == null || _boss.chargeAuraTelegraphPrefab == null) return;

        if (_chargeAuraTelegraph == null)
        {
            GameObject go = Instantiate(_boss.chargeAuraTelegraphPrefab);
            go.TryGetComponent(out _chargeAuraTelegraph);

            // 🔴 조용히 실패하지 않는다. 오라 프리팹을 이펙트로 갈아끼울 때(VFX 로드맵) 그 프리팹에
            //    AoeTelegraph 가 없으면 여기서 아무 일도 안 일어나고 **아무 신호도 없다** —
            //    "차징 범위가 안 보인다"만 남아 원인 찾기가 어려워진다. 점프 예고 쪽과 같은 규약이다.
            if (_chargeAuraTelegraph == null)
            {
                Debug.LogError(
                    $"{name}: chargeAuraTelegraphPrefab({go.name}) 에 AoeTelegraph 컴포넌트가 없다 — " +
                    "차징 범위 예고가 표시되지 않는다. 이펙트로 교체하려면 그 프리팹도 같은 컴포넌트를 " +
                    "갖거나(Show/Hide 규약), 이 호출부를 함께 바꿀 것.", this);
                Destroy(go);
            }
        }
        if (_chargeAuraTelegraph == null) return;

        // 보스 발밑에 눕힌다. 절대 Y 금지 — 찾은 바닥 + 표준 간격(GroundProbe 규약).
        Vector3 p = transform.position;
        if (GroundProbe.TryFindGround(p, 0, out RaycastHit ground, out _))
            p = new Vector3(p.x, GroundProbe.SurfaceY(ground), p.z);

        // p 는 이미 표준 간격(GroundProbe.SurfaceY)이 들어간 값이다 — 여기서 더 올리지 않는다.
        // 예전엔 +0.03 을 덧붙여 "표식보다 위"를 만들려 했지만, 그 순서는 높이가 아니라
        // 표식 억제(아래)로 정한다(같은 위치의 투명 메시는 거리 정렬이 불안정하다).
        _chargeAuraTelegraph.transform.position = p;

        // 🔴 **보스를 따라가게 붙인다**(2026-08-13). 판정은 매 틱 `transform.position` 기준인데
        //    그림은 한 번만 놓으면 보스가 움직인 만큼 **예고가 판정과 어긋난다**(차징 시작 직후
        //    아직 미끄러지는 구간이 있다). 예고가 판정에 대해 거짓말하지 않게 하는 것이 이 프로젝트 규약이다.
        //    ⚠️ 점프 예고를 자식으로 두면 안 되는 이유(보스가 체공 중 순간이동한다)는 여기 해당하지 않는다
        //       — 오라는 차징 동안만 살고, 그 사이 보스는 워프하지 않는다.
        _chargeAuraTelegraph.transform.SetParent(transform, worldPositionStays: true);

        // 🔴 **차징 동안 앞뒤 표식을 숨긴다**(팀장 확정 2026-09-03: "빨간 장판만 보여야 한다").
        //    ⚠️ 높이로 순서를 정하려 하면 안 된다 — 오라(바닥+0.08)가 표식(+0.04)보다 위인데도
        //       표식이 보였다. 투명 메시 정렬은 **오브젝트 단위 카메라 거리**로 갈리고 이 둘은
        //       **같은 위치(보스)** 에 있어 순서가 불안정하며, 장판이 반투명이라 아래가 비친다.
        //    차징 중에는 카운터 창도 안 열리므로 표식이 주는 정보도 없다.
        //    점프에서 쓰는 억제 경로와 같다(CrossFadeJumpStateClientRpc) — 둘은 동시에 못 일어난다.
        DirectionIndicator?.SetSuppressed(true);

        // 차징은 최대 chargeTimeLimit 초 유지된다 — 그동안 계속 보여야 하므로 넉넉히 잡고,
        // 실제 종료는 HideChargeAuraClientRpc 가 한다.
        _chargeAuraTelegraph.Show(radius, ChargeTimeLimit + 2f);
    }

    [ClientRpc]
    void HideChargeAuraClientRpc()
    {
        // 🔴 **억제 해제는 아래 early return 보다 앞이다.** 예고가 이미 없는 경로(중복 종료·프리팹
        //    미배선)에서 return 뒤에 두면 표식이 영구히 숨은 채로 남는다 — 켜는 쪽은 예고가 실제로
        //    생겼을 때만, 끄는 쪽은 **항상** 돈다.
        DirectionIndicator?.SetSuppressed(false);

        if (_chargeAuraTelegraph == null) return;
        Destroy(_chargeAuraTelegraph.gameObject);
        _chargeAuraTelegraph = null;
    }

    bool _chargeAuraActive;
    float _chargeAuraTimer;
    AoeTelegraph _chargeAuraTelegraph;

    float ChargeAuraRadius => _boss != null ? Mathf.Max(0f, _boss.chargeAuraRadius) : 3.5f;
    float ChargeAuraInterval => _boss != null ? Mathf.Max(0.1f, _boss.chargeAuraInterval) : 1f;
    int ChargeAuraDamage => _boss != null ? Mathf.Max(0, _boss.chargeAuraDamage) : 20;
    float ChargeAuraKnockback => _boss != null ? Mathf.Max(0f, _boss.chargeAuraKnockbackStrength) : 8f;
    #endregion

    void SpawnChargeZone()
    {
        if (_boss == null || _boss.chargeZonePrefab == null) return;
        // ⚠️ 정본의 zonePushForce(밀치기)는 플레이어 변위 경로가 없어 아직 적용되지 않는다 — 데미지만.
        _chargeZone = AreaZone.SpawnOrGrow(_boss.chargeZonePrefab, transform.position);
    }

    void EndChargeZone()
    {
        if (_chargeZone == null) return;
        _chargeZone.Despawn();
        _chargeZone = null;
    }

    /// <summary>
    /// 차징 장판의 실효 반경. 구슬 크기가 여기에 묶인다.
    ///
    /// ⚠️ <see cref="AreaZone.SpawnOrGrow"/> 는 같은 자리에 같은 타입 장판이 있으면 **그것을 성장시켜**
    /// 돌려준다(폭탄 장판 등). 그래서 저작값이 아니라 <b>실제 인스턴스</b>에서 읽는다 —
    /// 구슬이 예상보다 커 보이면 그건 여기가 아니라 장판이 자란 것이다.
    /// </summary>
    float ChargeZoneRadius => _chargeZone != null ? _chargeZone.Radius : ChargeAuraRadius;

    /// <summary>
    /// 구슬 시작. <b>좌표와 반경을 서버가 싣는다</b> — 장판은 보스 자식이 아니라 별도 NetworkObject 라
    /// 클라에 아직 스폰되지 않았을 수 있고, 그때 각 피어가 스스로 재면 크기가 갈린다
    /// (레거시가 페이로드 없이 각자 계산할 수 있었던 건 floor 가 보스 자식이었기 때문이다).
    ///
    /// Reliable(기본): 유실되면 구슬이 아예 안 뜬다.
    /// </summary>
    [ClientRpc]
    void StartChargeBallClientRpc(Vector3 center, float radius)
    {
        if (chargeBall == null)
        {
            WarnNoChargeBallOnce();
            return;
        }

        chargeBall.PlayAt(center, radius);
    }

    /// <summary>
    /// 구슬 종료. <paramref name="broken"/> 이면 깨지고, 아니면 사그라진다.
    ///
    /// 🔴 Reliable(기본)이어야 한다 — 유실되면 <b>구슬이 아레나에 영구히 남는다.</b>
    /// 재생 중이 아니면 컴포넌트 쪽에서 조용한 no-op 이라 여러 경로에서 불려도 안전하다.
    /// </summary>
    [ClientRpc]
    void EndChargeBallClientRpc(bool broken)
    {
        if (chargeBall == null) return;   // 미배선은 시작 시점에 이미 1회 알렸다

        if (broken) chargeBall.Abort();
        else chargeBall.Stop();
    }

    // 🔴 1인 1 / 2인 2 / **3인 이상 4**. 레거시의 Clamp(playerCount,1,3)+player3=3 버그를 여기서 닫는다.
    int PylonCountFor(int playerCount)
    {
        if (_boss == null) return Mathf.Clamp(playerCount, 1, 4);
        if (playerCount <= 1) return Mathf.Max(1, _boss.chargePylonsSolo);
        if (playerCount == 2) return Mathf.Max(1, _boss.chargePylonsDuo);
        return Mathf.Max(1, _boss.chargePylonsTrioPlus);
    }

    int CountAlivePlayers()
    {
        if (_aoeBuffer == null) _aoeBuffer = new Collider[16];

        // ⚠️ 🔴 **여기가 송전탑 개수를 정한다**(1인 1 / 2인 2 / 3인+ 4). 그런데 반경은
        //    FindFarthestPlayer() 와 **공용**이다 — 점프 사거리 때문에 30 → 45 로 올렸을 때
        //    이 판정 반경도 같이 올라갔다(2026-09-21 확인. 의도였는지는 기록이 없다).
        //    송전탑 개수가 인원과 안 맞으면 여기를 먼저 의심할 것.
        float radius = _boss != null ? _boss.playerScanRadius : 30f;
        int count = Physics.OverlapSphereNonAlloc(
            transform.position, radius, _aoeBuffer, playerMask, QueryTriggerInteraction.Collide);

        _aoeHits.Clear();
        for (int i = 0; i < count; i++)
        {
            Collider c = _aoeBuffer[i];
            if (c == null) continue;
            if (!MonsterTargeting.IsAttackable(c)) continue; // 유령은 인원수에 안 넣는다
            Unit u = c.GetComponentInParent<Unit>();
            if (u != null) _aoeHits.Add(u);
        }
        return Mathf.Max(1, _aoeHits.Count);
    }

    int FindSlot(BossAttackId id)
    {
        BossAttackEntry[] rows = _boss != null ? _boss.attacks : null;
        if (rows == null) return NoAttack;
        for (int i = 0; i < rows.Length; i++)
            if (rows[i] != null && rows[i].attackId == id) return i;
        return NoAttack;
    }

    float ChargeTimeLimit => _boss != null ? Mathf.Max(1f, _boss.chargeTimeLimit) : 20f;
    float RageDashDuration => _boss != null ? Mathf.Max(0.1f, _boss.rageDashDuration) : 0.7f;
    float RageDashInterval => _boss != null ? Mathf.Max(0f, _boss.rageDashInterval) : 0.5f;
    float RageDashSpeedMul => _boss != null ? Mathf.Max(1f, _boss.rageDashSpeedMultiplier) : 8f;

    float DashDuration => _boss != null ? Mathf.Max(0.1f, _boss.dashDuration) : 0.7f;
    float DashSpeedMul => _boss != null ? Mathf.Max(1f, _boss.dashSpeedMultiplier) : 6f;
    float DashMaxDistance => _boss != null ? Mathf.Max(1f, _boss.dashMaxDistance) : 16f;
    float DashCarryFrontOffset => _boss != null ? Mathf.Max(0f, _boss.dashCarryFrontOffset) : 1.8f;
    float DashStunDuration => _boss != null ? Mathf.Max(0f, _boss.dashStunDuration) : 1f;
    float RageDashMaxDistance => _boss != null ? Mathf.Max(1f, _boss.rageDashMaxDistance) : 16f;
    float RageTotalTime =>
        (_boss != null ? Mathf.Max(1, _boss.rageDashCount) : 3) * (RageDashDuration + RageDashInterval);
    #endregion

    #region 카운터 (창 + 정면 판정 + 그로기/Break)

    // ─── 선딜 게이트: 애니 준비 래치 + 창 만료 후 발사 ────────────────────────

    /// <summary>
    /// 애니 <c>OnAttackHit</c> 수신. 카운터 창이 열린 공격은 여기서 <b>발사하지 않는다</b> —
    /// "애니가 준비 자세에 도달했다"만 래치하고, 실제 발사는 창 타이머가 끝난 뒤
    /// <see cref="TryReleaseCounterAttack"/> 이 한다(설계 §4.2).
    ///
    /// 창이 없는 공격은 base 그대로 — 이벤트 즉시 발사다.
    /// </summary>
    /// <summary>
    /// 클립에 <c>OnAttackHit</c> 이벤트가 없는 공격을 위해 <b>정규화 시간으로</b> 준비 신호를 대신 낸다.
    /// 값이 0 이면 아무것도 하지 않는다 — 이벤트가 있는 클립은 기존 경로 그대로다.
    /// </summary>
    void TickHitEventFallback()
    {
        BossAttackEntry e = _currentEntry;
        if (e == null || e.hitEventFallbackNormalized <= 0f) return;
        if (animator == null || _counterWindup.IsAnimationReady) return;

        AnimatorStateInfo st = animator.GetCurrentAnimatorStateInfo(0);
        if (!string.IsNullOrEmpty(e.animatorStateName) && !st.IsName(e.animatorStateName)) return;

        if (st.normalizedTime >= e.hitEventFallbackNormalized)
            _counterWindup.MarkAnimationReady();
    }

    public override void NotifyAttackHit()
    {
        if (!IsServer || State != MonsterState.Attack) return;

        // 🔴 **예고 구간에서는 절대 때리지 않는다.** 예고는 판정보다 먼저 끝나는 것이 존재 이유인데,
        //    여기서 통과시키면 "예고가 차는 중에 이미 맞았다"가 되어 구간이 통째로 무의미해진다.
        //    훅·어퍼는 카운터 게이트가 없어(opensCounterWindow 0) 아래 분기를 그냥 지나가므로
        //    이 가드가 없으면 막을 것이 하나도 없다.
        //    정상 경로에서는 자세가 멈춰 있어 이벤트가 오지 않지만, `animator.Play` + `Update(0)` 가
        //    이벤트를 흘릴 여지가 문서로 보장되지 않아 방어한다(Codex 교차검증 지적).
        if (_attackPhase == BossAttackPhase.Telegraph || _attackPhase == BossAttackPhase.JumpTakeoff)
        {
            Debug.LogWarning(
                $"[23호] {_currentEntry?.attackId} 예고/이륙 중에 OnAttackHit 이 도착했다 — 무시한다. " +
                "자세 정지 지점(telegraphPoseNormalized)이 클립의 히트 프레임보다 뒤인지 확인할 것.", this);
            return;
        }

        if (!_counterWindup.IsActive)
        {
            base.NotifyAttackHit();
            return;
        }

        // 준비 신호가 창보다 늦게 온 경우에만 실제 도달 시각을 남긴다 — 창 값을 정하는 근거다.
        // (정상 경로에서는 아무것도 찍지 않는다. 매 공격 로그는 정작 필요한 진단을 밀어낸다.)
        if (_counterWindup.TimerElapsedBeforeAnimationReady)
        {
            Debug.LogWarning(
                $"[23호] {_currentEntry?.attackId} 애니 준비 신호가 공격 시작 후 " +
                $"{Time.time - _counterWindupStartedAt:0.###}초에 도달했다 " +
                $"(창 {CounterWindowDuration:0.##}초). 창을 이 값 이상으로 잡아야 한다.", this);
        }

        // 중복 이벤트가 와도 래치라 한 번만 선다(FireAttackHitOnce 의 1회 가드와 이중 방어).
        _counterWindup.MarkAnimationReady();
        SetCounterPoseHeldClientRpc(true);   // 준비 자세에서 정지 — 창이 끝날 때까지 붙잡는다
        TryReleaseCounterAttack();
    }

    /// <summary>
    /// 모든 피어의 보스 애니메이터를 준비 자세에서 <b>정지/재개</b>한다.
    ///
    /// 애니메이터는 복제되지 않으므로 각 피어가 자기 것을 멈춘다. 서버가 진실의 원천이고
    /// 이 RPC 는 표현만 옮긴다(정본 §6).
    ///
    /// 🔴 <b>멱등이어야 한다.</b> 같은 값이 두 번 와도, 원래 정상 속도였어도 부작용이 없어야 한다 —
    ///    풀 때 "저장해 둔 값"이 아니라 0 을 복원하면 보스가 영구히 얼어붙는다.
    ///    그래서 걸 때만 현재 속도를 저장하고(`_counterAnimatorHeldLocally` 가 그 래치다),
    ///    풀 때는 저장본을 되돌린다.
    /// </summary>
    [ClientRpc]
    void SetCounterPoseHeldClientRpc(bool held)
    {
        if (animator == null) return;

        if (held)
        {
            HoldCounterPoseLocal();
            return;
        }

        RestoreCounterPose();
    }

    /// <summary>
    /// 자세 홀드를 건다(로컬). RPC 안에서 또 RPC 를 부를 수 없어 본문을 따로 뺐다 —
    /// <see cref="HoldAttackPoseClientRpc"/> 가 같은 래치를 써야 푸는 경로도 하나로 남는다.
    /// 멱등이다: 이미 잡고 있으면 아무 일도 하지 않는다(0 을 저장해 영구히 얼어붙는 사고 방지).
    /// </summary>
    void HoldCounterPoseLocal()
    {
        if (animator == null) return;
        if (_counterAnimatorHeldLocally) return;

        _counterAnimatorResumeSpeed = animator.speed;
        animator.speed = 0f;
        _counterAnimatorHeldLocally = true;
    }

    /// <summary>
    /// 카운터 선딜 상태를 통째로 비운다 — 게이트 · 창 · 자세 홀드.
    ///
    /// 🔴 <b>모든 중단 경로가 여기를 지나야 한다.</b> 자세 홀드는 애니메이터 속도를 0 으로 만드는
    ///    조작이라, 푸는 걸 한 곳이라도 빠뜨리면 보스가 그 자세로 <b>영구히 굳는다</b>.
    ///    호출처를 늘리지 말고 <see cref="AbortAttackChain"/> 한 곳에 모아 뒀다 —
    ///    상태 이탈 · 사망 · 체인 타임아웃 · 디스폰 · 카운터 성공이 전부 그리로 흐른다.
    ///
    /// 멱등이다. 이미 비어 있어도 부작용이 없다.
    /// </summary>
    void ResetCounterWindup()
    {
        if (!IsServer) return;

        _counterWindup.Reset();
        SetCounterWindow(false);

        // 자세는 각 피어의 로컬 상태라 RPC 로 푼다. 디스폰 중에는 RPC 가 못 나가므로
        // 로컬 복원도 함께 부른다(호스트가 멈춘 채 남지 않게). 둘 다 멱등이라 중복 호출이 안전하다.
        if (IsSpawned) SetCounterPoseHeldClientRpc(false);
        RestoreCounterPose();
    }

    /// <summary>자세 홀드를 푼다. 안 잡고 있으면 아무 일도 하지 않는다(멱등).</summary>
    void RestoreCounterPose()
    {
        if (!_counterAnimatorHeldLocally || animator == null) return;

        animator.speed = _counterAnimatorResumeSpeed;
        _counterAnimatorHeldLocally = false;
    }

    /// <summary>
    /// 두 사건(창 만료 + 애니 준비)이 다 섰을 때만 실제 공격을 내보낸다.
    /// 멱등이다 — 게이트를 먼저 비우므로 두 번 불려도 한 번만 발사된다.
    /// </summary>
    void TryReleaseCounterAttack()
    {
        if (!_counterWindup.ShouldRelease) return;

        _counterWindup.Reset();
        SetCounterWindow(false);   // 발사 순간 창이 닫힌다 — 이후 히트는 카운터로 인정되지 않는다
        SetCounterPoseHeldClientRpc(false);  // 자세를 풀어 클립을 이어 재생한다
        FireAttackHitOnce();
    }
    // 데미지 유입 단일 진입점. 카운터 판정을 여기에 얹는다 —
    // 플레이어 인터럽트 스킬의 히트가 **서버 경로**(BaseAttack → ReceiveAttack)로 들어온 시점에
    // 보스의 창 상태 + 정면 각도를 서버가 본다. 클라 예측 없음(정본 §6).
    public override bool ReceiveAttack(AttackInfo attackInfo, AttackHitContext hitContext)
    {
        // 🔴 조건은 base 호출 **전에** 스냅샷한다. base 가 사망·상태를 바꿀 수 있어서,
        //    뒤에서 읽으면 이미 닫힌 창을 보게 된다.
        bool counter = IsServer
                       && _counterWindow.Value
                       && IsInterruptAttack(attackInfo)
                       && IsCounterFromFront(hitContext);

        // 진단(2026-09-02): 인터럽트가 들어왔는데 카운터로 성립하지 않으면 **어느 조건이 거짓인지** 찍는다.
        // 인터럽트 히트에서만 돌므로 조용하다. 성립하면 EnterCounterGroggy 가 따로 로그를 남긴다.
        if (IsInterruptAttack(attackInfo) && !counter)
        {
            Debug.LogWarning(
                $"[23호] 인터럽트가 카운터로 성립하지 않았다 — " +
                $"서버={IsServer} · 창열림={_counterWindow.Value} · " +
                $"정면={IsCounterFromFront(hitContext)} (허용 {(_boss != null ? _boss.counterFrontAngle : 60f):0}°) · " +
                $"상태={State} · 페이즈={_attackPhase} · 공격={_currentEntry?.attackId}", this);
        }

        // 실패든 성공이든 데미지는 정상 처리된다 — 카운터 실패에 패널티는 없다(확정 스펙).
        bool resolved = base.ReceiveAttack(attackInfo, hitContext);

        if (counter && resolved && State != MonsterState.Dead)
        {
            EnterCounterGroggy(allowBreak: true);

            // 🔴 여기가 "인터럽트 성공"의 유일한 지점이다. EnterCounterGroggy 안에 넣지 않은 이유:
            //    그 메서드는 송전기 전멸(S7) 경로도 함께 쓰는데, 그건 플레이어가 끊어낸 게 아니라
            //    별개의 사건이다. 섞으면 연출이 "무엇을 칭찬하는지"가 흐려진다.
            PlayInterruptFlashRpc();
        }

        return resolved;
    }

    /// <summary>
    /// [전 피어] 카운터 성공 섬광.
    ///
    /// 🔴 <b>RPC 여야 한다.</b> 호출부는 <c>counter</c> 조건에 <c>IsServer</c> 가 들어 있어 서버에서만
    ///    도는 경로다 — 직접 재생하면 호스트 화면에서만 보인다.
    ///
    /// Unreliable: 순수 연출이라 한 번 빠져도 상태가 발산하지 않는다.
    /// (성공 자체는 그로기 상태 복제로 전 피어에 전달되므로 연출이 빠져도 결과는 보인다.)
    /// </summary>
    [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Unreliable)]
    void PlayInterruptFlashRpc()
    {
        if (interruptFlash == null)
        {
            WarnNoInterruptFlashOnce();
            return;
        }

        interruptFlash.PlayOnce();
    }

    void WarnNoInterruptFlashOnce()
    {
        if (_warnedNoInterruptFlash) return;
        _warnedNoInterruptFlash = true;

        Debug.LogWarning(
            $"{name}: 카운터 성공 섬광이 비어 있다 — 프리팹의 TwentyThreeBoss 에 interruptFlash 를 물릴 것.", this);
    }

    /// <summary>
    /// 이 히트가 인터럽트 스킬인가.
    ///
    /// ✅ R1 수령 완료(은희, `a75398c`) — 식별자는 <c>AttackType</c> enum 값이 아니라
    /// <c>AttackInfo.isInterruptAttack</c> **플래그**로 왔다. <c>AttackType</c> 은 "어느 출처가 쐈나"라
    /// 인터럽트와 직교하기 때문이다(Q 슬롯이면서 인터럽트인 스킬을 표현할 수 없게 된다).
    ///
    /// **플래그는 하나뿐이고 소비 방식은 수신측이 정한다** — 일반몹·중간보스는
    /// <c>maxGroggyCount</c> 누적으로 소비하고, 23호는 여기서 **카운터 창 + 정면 각도**로 소비한다.
    /// virtual 로 둔 것은 파생 보스가 판별을 좁힐 수 있게 하기 위해서다.
    /// </summary>
    protected virtual bool IsInterruptAttack(AttackInfo attackInfo) => attackInfo.isInterruptAttack;

    /// <summary>
    /// 보스 정면에서 들어온 히트인가(<c>counterFrontAngle</c> = 전방 기준 ±각도).
    /// 헤드어택(은희) 구현 후 그쪽 판정으로 **교체될 지점**이라 virtual 로 분리해 둔다.
    /// </summary>
    protected virtual bool IsCounterFromFront(AttackHitContext hitContext)
    {
        // 🔴 sourcePosition 이 아니라 공격자 **루트**를 우선 쓴다.
        //    BaseAttack.CreateHitContext 는 `transform.position` 을 담는데, 그 transform 은 플레이어
        //    루트가 아니라 무기/히트박스 자식이다(이미 보스 쪽으로 뻗어 있음). 그 점으로 각도를 재면
        //    "앞에서 쳤는가"가 무기 길이만큼 편향된다. 루트가 없을 때만 sourcePosition 으로 폴백한다.
        Vector3 origin = hitContext.sourceTransform != null
            ? hitContext.sourceTransform.root.position
            : hitContext.sourcePosition;

        Vector3 to = origin - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.0001f) return true; // 완전히 겹침 — 정면으로 본다

        float limit = _boss != null ? _boss.counterFrontAngle : 60f;
        return Vector3.Angle(transform.forward, to.normalized) <= limit;
    }

    /// <summary>
    /// 그로기 유발. 카운터 성공은 <paramref name="allowBreak"/> = true.
    /// 송전기 실패(S7)는 false — **카운트는 올리되 Break 로 승격하지 않는다**
    /// (페이즈 전환 직후 5초 무력화가 겹치면 페이즈 연출이 죽는다 — 확정 스펙).
    /// </summary>
    /// <param name="durationOverride">
    /// 0 보다 크면 이 시간을 전체 행동 불능 시간으로 쓴다(카운트 누적은 그대로).
    /// 송전기 전멸처럼 <b>보상의 무게가 다른</b> 경로가 쓴다 — 일반 카운터와 값을 나누기 위해서다.
    /// Break 는 임계 도달의 결과라 덮지 않는다.
    /// </param>
    /// <summary>
    /// 인터럽트 성공 리액션이 오른쪽인가 (팀장 확정 R1).
    /// 잡기 = **항상 오른쪽**(오른팔로 잡으므로) · 돌진 = **L·R 50:50 난수** · 그 외 = 왼쪽.
    /// </summary>
    static bool ResolveHitReactionRight(BossAttackEntry e)
    {
        if (e == null) return false;
        return e.attackId switch
        {
            BossAttackId.Grab => true,
            BossAttackId.Dash => UnityEngine.Random.value < 0.5f,
            _ => false,
        };
    }

    protected void EnterCounterGroggy(bool allowBreak, float durationOverride = -1f)
    {
        if (!IsServer) return;

        BossCounterOutcome outcome = BossCounterProgress.Resolve(
            _counterGroggyCount,
            data != null ? data.maxGroggyCount : 5,
            allowBreak,
            data != null ? data.groggyDuration : 0.5f,
            _boss != null ? _boss.breakDuration : 2f);

        _counterGroggyCount = outcome.NextCount;

        float duration = (!outcome.IsBreak && durationOverride > 0f) ? durationOverride : outcome.Duration;

        // 🔴 [G6] 리액션 방향을 **체인 정리보다 먼저** 확정한다 — AbortAttackChain 이 진행 중 공격을
        //    비우고 나면 "무엇을 끊었는지"를 알 수 없다.
        _hitReactionRight.Value = ResolveHitReactionRight(_currentEntry);

        // 예약된 공격·준비 래치·자세 홀드를 폐기하고 체인을 정리한다(설계 §4.4 1~4).
        // ResetCounterWindup 은 이 안에서 함께 돈다.
        AbortAttackChain();

        // 🔴 Hit 리액션을 **앞에 더하지 않는다**(설계 §3.3). 예전엔
        //    ForceHitReaction(HitReactionDuration, groggy) 라 SO 의 0.5 위에 0.4 가 얹혀
        //    실제 행동 불능이 0.9초였다 — SO 값이 곧 체감 시간이어야 튜닝이 성립한다.
        ForceGroggy(duration);

        int max = data != null ? Mathf.Max(1, data.maxGroggyCount) : 5;
        Debug.Log(
            $"[23호] 카운터 성공 — 그로기 카운트 {(outcome.IsBreak ? max : outcome.NextCount)}/{max}" +
            (outcome.IsBreak ? $" → BREAK {duration:0.#}초" : $" → 그로기 {duration:0.#}초"),
            this);
    }

    void SetCounterWindow(bool open)
    {
        if (!IsServer) return;
        if (_counterWindow.Value == open) return;
        _counterWindow.Value = open;
    }

    // 모든 피어에서 호출된다(서버 포함) — 표현만 담당. 붙어 있는 텔레그래프 **전부**를 구동한다.
    void OnCounterWindowChanged(bool previous, bool next)
    {
        if (_telegraphs == null || _telegraphs.Length == 0) ResolveTelegraphs();
        if (_telegraphs == null) return;

        for (int i = 0; i < _telegraphs.Length; i++)
            _telegraphs[i]?.SetCounterWindow(next);
    }

    void ResolveTelegraphs()
    {
        _telegraphs = GetComponentsInChildren<IBossTelegraph>(true);
        if (_telegraphs == null || _telegraphs.Length == 0)
        {
            Debug.LogWarning(
                $"{name}: IBossTelegraph 구현이 하나도 없다 — 카운터 창이 화면에 전혀 표시되지 않는다. " +
                "BossDirectionIndicator(방향 링) 또는 BossCounterTelegraph(전신 틴트)를 붙일 것.",
                this);
            return;
        }

        for (int i = 0; i < _telegraphs.Length; i++)
            _telegraphs[i]?.SetCounterWindow(_counterWindow.Value);
    }
    #endregion

    #region 페이즈 (HP 임계 전환)
    public override void TakeDamage(AttackInfo attackInfo)
    {
        base.TakeDamage(attackInfo); // 서버 가드 + 방어/체력 + 사망/그로기/피격경직 판정

        if (!IsServer || State == MonsterState.Dead)
            return;

        EvaluatePhase();
    }

    // 체력 비율이 임계를 넘어설 때마다 페이즈를 1 올린다.
    // 🔴 페이즈는 되돌아가지 않는다 — 회복(리쉬 리셋 등)이 페이즈 연출을 다시 트리거하면 안 된다.
    void EvaluatePhase()
    {
        BossPhaseEntry[] phases = _boss != null ? _boss.phases : null;
        if (phases == null || phases.Length == 0) return;

        int max = FinalMaxHp;
        if (max <= 0) return;

        float ratio = (float)CurrentHealth / max;
        int next = 0;
        for (int i = 0; i < phases.Length; i++)
            if (ratio <= phases[i].hpThreshold)
                next = i + 1;

        if (next <= CurrentPhase) return;

        CurrentPhase = next;

        // 🔴 시퀀스를 **여기서 시작하지 않는다.** 정본 §9: "_pendingCharging 은 현재 행동이 끝난 뒤
        //    소비한다 — 행동 도중 강제 중단하지 않는다." TakeDamage 는 공격 한복판에도 들어오므로
        //    여기서 바로 시작하면 진행 중인 잡기·점프를 끊어 버린다.
        //    소비 지점은 SelectAttackSlot — 그 함수는 Idle/Walk 에서만 호출되므로 곧 "행동 종료 직후"다.
        BossPhaseEntry entered = ActivePhase;
        if (entered != null && entered.sequence != BossPhaseSequence.None)
            _pendingPhaseSequence = true;

        OnPhaseEntered(next);
    }

    // 페이즈 진입 확장점. TODO(S7): sequence == ChargeSequence → 송전기 시퀀스(실패 시 레이지 돌진 3회).
    protected virtual void OnPhaseEntered(int phase)
    {
        BossPhaseEntry p = ActivePhase;
        Debug.Log(
            $"[23호] 페이즈 {phase} 진입 — 체력 {CurrentHealth}/{FinalMaxHp}, " +
            $"데미지 ×{PhaseDamageMultiplier:0.##}, 이동 ×{ChaseSpeedMultiplier:0.##}, " +
            $"시퀀스 {(p != null ? p.sequence.ToString() : "None")}",
            this);
    }
    #endregion

    #region 유틸
    BossAttackEntry EntryFor(int slot)
    {
        BossAttackEntry[] rows = _boss != null ? _boss.attacks : null;
        if (rows == null || slot < 0 || slot >= rows.Length) return null;
        return rows[slot];
    }

    // 미구현 공격은 조용히 지나가지 않게 1회만 경고한다(매 히트 로그는 신호를 덮는다).
    void WarnUnimplementedOnce(BossAttackId id)
    {
        int bit = 1 << (int)id;
        if ((_warnedAttackMask & bit) != 0) return;
        _warnedAttackMask |= bit;

        Debug.LogWarning(
            $"[23호] {id} 는 히트 판정이 아직 없다 — 애니만 재생된다. (Grab=S4 / Dash=S5 / Jump=S6)",
            this);
    }
    #endregion
}
