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
public class TwentyThreeBoss : MonsterBase
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

    // ─── Grab 체인 (서버 전용) ───────────────────────────────────────
    BossAttackPhase _attackPhase = BossAttackPhase.None;
    float _attackPhaseTimer;
    float _grabTickTimer;
    Player _grabbed;                 // 붙잡고 있는 플레이어(없으면 null)
    Collider[] _grabBuffer;
    bool _warnedThrowDisplacement;

    // ─── JumpAttack (서버 + 각 피어 연출) ─────────────────────────────
    Vector3 _jumpArrivePoint;
    Collider[] _aoeBuffer;                          // 최원거리 탐색 · 착지 AoE 공용
    readonly HashSet<Unit> _aoeHits = new HashSet<Unit>();
    Renderer[] _modelRenderers;                     // 체공 중 숨길 모델 렌더러(animator 하위만)
    AoeTelegraph _telegraphFixed;                   // 착지 위치(고정 크기)
    AoeTelegraph _telegraphGrowing;                 // 착지 타이밍(0.1 → AoE 점증)
    bool _warnedNoJumpTelegraph;

    // ─── 페이즈 시퀀스 (송전기 / 레이지) ──────────────────────────────
    bool _pendingPhaseSequence;                     // 페이즈 통과 후 "행동이 끝나면 시작할 것"
    IBossChargeSequence _charge;
    AreaZone _chargeZone;
    bool _warnedNoCharge;
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
    bool _warnedNoBombPrefab;

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
        // 예고 장판은 씬 루트에 띄운 로컬 인스턴스라 보스와 함께 자동 소멸하지 않는다 — 직접 지운다.
        if (_telegraphFixed != null) Destroy(_telegraphFixed.gameObject);
        if (_telegraphGrowing != null) Destroy(_telegraphGrowing.gameObject);

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

    // hitboxAnchorName 도 문자열 규약이라 오타가 조용히 무시된다(정본 §10.3 경고).
    void ValidateHitboxAnchors()
    {
        if (_boss.attacks == null) return;

        for (int i = 0; i < _boss.attacks.Length; i++)
        {
            BossAttackEntry e = _boss.attacks[i];
            if (e == null || string.IsNullOrEmpty(e.hitboxAnchorName)) continue;
            if (_anchors != null && _anchors.ContainsKey(e.hitboxAnchorName)) continue;

            Debug.LogError(
                $"{name}: attacks[{i}]({e.attackId}).hitboxAnchorName=\"{e.hitboxAnchorName}\" 에 해당하는 " +
                "ColliderInfo 자식이 없다 — 앵커 지정이 조용히 무시된다.",
                this);
        }
    }
    #endregion

    #region 공격 선택기 (거리창 → 가중치 → 연속 감쇠 → 폴백)
    // base 의 SeekBoss 가 매 틱 부른다. NoAttack(-1)을 돌리면 base 가 접근/대기로 폴백한다
    // (= 전부 쿨이어도 제자리에 멈춰 서지 않는다 — 수용기준 #1).
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

        if (_weightBuffer == null || _weightBuffer.Length != rows.Length)
            _weightBuffer = new float[rows.Length];

        // 1) 게이트 3단: 페이즈 → 거리창 → 쿨다운. 셋 다 통과 + 가중치 > 0 인 것만 후보다.
        int candidates = 0;
        int fallbackSlot = NoAttack;
        for (int i = 0; i < rows.Length; i++)
        {
            _weightBuffer[i] = 0f;

            BossAttackEntry e = rows[i];
            if (e == null || e.weight <= 0f) continue;
            if (CurrentPhase < e.allowedFromPhase) continue;
            if (!e.ignoreDistanceWindow && (dist < e.minDistance || dist > e.maxDistance)) continue;
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
                    _attackPhase = BossAttackPhase.Windup;
                    _attackPhaseTimer = data.attackDuration;
                    _stateTimer = data.attackDuration + GrabHold + GrabThrowTime + GrabRecovery;
                    break;
                case BossAttackId.Jump:
                    _stateTimer = JumpHover + JumpLanding + JumpRecovery + data.attackDuration;
                    break;
                case BossAttackId.ChargeSequence:
                    _stateTimer = ChargeTimeLimit + data.attackDuration;
                    break;
                case BossAttackId.RageDash:
                    _stateTimer = RageTotalTime + data.attackDuration;
                    break;
                case BossAttackId.Dash:
                    // 돌진 본체 + 복귀(= attackDuration). 슈퍼아머 길이이자 데드락 안전망이다.
                    _stateTimer = DashDuration + data.attackDuration;
                    break;
            }
        }

        // 공격별 슈퍼아머. SO 전역 플래그(hasSuperArmorWhileAttacking)는 꺼진 상태를 전제한다
        // — 켜져 있으면 ValidateContract 가 LogError 로 잡는다.
        // 체인 공격은 전체 길이 동안 유지해야 중간에 경직으로 끊기지 않는다.
        if (e != null && e.superArmor && status != null)
            status.ApplyStatus(StatusEffectType.SuperArmor, _stateTimer);

        // 카운터 창: 창을 여는 공격(Grab·Dash)이면 지금부터 **히트 순간까지** 열어 둔다.
        // Grab = 잡기 판정 직전까지 / Dash = 돌진 시작 직전까지 — 확정 스펙의 두 구간이 모두
        // "공격 시작 → 히트"와 같으므로 phase 기계 없이 이 한 줄로 성립한다.
        SetCounterWindow(e != null && e.opensCounterWindow);

        // 관용구 2: 다지선다 애니는 상태 복제로 실을 수 없다 → ClientRpc 로 CrossFade.
        // 문자열이 아니라 슬롯 번호를 보낸다(각 피어가 같은 SO 에서 상태명을 조회한다 — GauntletBot 선례).
        PlayAttackAnimClientRpc(CurrentAttackSlot);

        // 애니가 나간 **뒤에** 각 체인의 진입 처리를 한다(클립이 먼저 보여야 한다).
        if (e == null) return;
        switch (e.attackId)
        {
            case BossAttackId.Jump: BeginJump(); break;
            case BossAttackId.ChargeSequence: BeginCharge(); break;
            case BossAttackId.RageDash: BeginRage(); break;
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
        SetCounterWindow(false);

        ApplyAttackProfile(e);

        switch (e.attackId)
        {
            case BossAttackId.LeftHook:
            case BossAttackId.RightHook:
                meleeAttack?.Hit();
                break;

            case BossAttackId.Upper:
                meleeAttack?.Hit();
                OnUpperHit();
                break;

            case BossAttackId.Grab:
                // 잡기 판정 순간 = Acquire. 이후 Hold → Throw → Recovery 는 HandleAttack 이 몬다.
                AcquireGrab();
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
        if (_boss != null && s == MonsterState.Hit && !string.IsNullOrEmpty(_boss.hitReactionState))
        {
            SafeSetBool(data.groggyBool, false); // Groggy 로 넘어갈 때 base 가 다시 true 로 올린다
            SafeCrossFade(_boss.hitReactionState);
            return;
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
    #endregion

    #region Grab 체인 (Attack 안의 AttackPhase)
    // 관용구 3: Attack 안에 서브 시퀀스를 접을 때는 base 의 종료 타이머(_stateTimer)를 통째로
    // 덮어쓰고 자체 elapsed 로 단계를 나눈다(SpinnerBot 선례).
    //
    // 🔴 Grab 체인은 **커밋**이다 — 시작하면 타깃이 범위를 벗어나도 중단하지 않는다.
    //    그래서 base 의 선딜 취소(cancelWindupIfTargetLeavesRange) 경로를 타지 않는다.
    protected override void HandleAttack(float dt)
    {
        if (_attackPhase == BossAttackPhase.None)
        {
            base.HandleAttack(dt); // 단타 공격 — 히트는 애니 이벤트, 종료는 이벤트 + 타이머 폴백
            return;
        }

        _stateTimer -= dt;      // 데드락 안전망(단계 합보다 넉넉하게 잡아 둔다)
        _attackPhaseTimer -= dt;
        FaceChainTarget();

        switch (_attackPhase)
        {
            case BossAttackPhase.Windup:
                // 판정은 애니 이벤트(OnAttackHit → PerformAttackHit)가 만든다.
                // 이벤트가 유실되면 여기 타이머가 만료돼 아래 안전망으로 빠진다.
                break;

            case BossAttackPhase.Hold:
                TickGrabHold(dt);
                if (_attackPhaseTimer <= 0f) BeginGrabThrow();
                break;

            case BossAttackPhase.Throw:
                if (_attackPhaseTimer <= 0f) ReleaseGrabThrow();
                break;

            // ── JumpAttack ────────────────────────────────────────────
            case BossAttackPhase.Leap:
                if (_attackPhaseTimer <= 0f) ArriveJump();
                break;

            case BossAttackPhase.Land:
                // 착지 데미지는 애니 이벤트(OnAttackHit)가 만든다. 이벤트가 없으면 데미지가 없다
                // (폴백을 넣으면 이벤트 추가 후 두 번 맞는다 — 정본 §3.3 비대칭 규칙).
                if (_attackPhaseTimer <= 0f) EnterPhase(BossAttackPhase.Recovery, JumpRecovery);
                break;

            // ── 페이즈 시퀀스 ─────────────────────────────────────────
            case BossAttackPhase.ChargeWait:
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
            Debug.LogWarning($"[23호] Grab 체인이 {_attackPhase} 에서 타임아웃 — 강제 종료한다.", this);
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

    // 잡기 판정 순간(Acquire). 반경 안 최근접 플레이어를 잡는다.
    void AcquireGrab()
    {
        _attackPhase = BossAttackPhase.Acquire;

        Player target = FindGrabTarget();
        if (target == null || !target.BeginGrabbedByInstigator(gameObject))
        {
            // 헛잡기 — 복귀 경직만 지고 끝낸다(창에 실패 대가가 붙는 것과 대칭).
            _grabbed = null;
            EnterPhase(BossAttackPhase.Recovery, GrabRecovery);
            return;
        }

        _grabbed = target;
        _grabTickTimer = 0f;
        EnterPhase(BossAttackPhase.Hold, GrabHold);
        CrossFadeGrabStateClientRpc(false);
    }

    void TickGrabHold(float dt)
    {
        // 잡힌 대상이 사라지면(사망·디스폰) 체인을 정리하고 복귀한다.
        if (!IsGrabbedValid())
        {
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
    }

    void BeginGrabThrow()
    {
        EnterPhase(BossAttackPhase.Throw, GrabThrowTime);
        CrossFadeGrabStateClientRpc(true);
    }

    void ReleaseGrabThrow()
    {
        if (IsGrabbedValid())
        {
            Player thrown = _grabbed;
            Vector3 dir = transform.forward;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
            dir.Normalize();

            thrown.EndGrabbedByInstigator();

            if (_boss != null && _boss.grabThrowDamage > 0)
            {
                var info = new AttackInfo(_boss.grabThrowDamage, AttackType.Default);
                var ctx = new AttackHitContext(transform.position, transform, null);
                thrown.ReceiveAttack(info, ctx);
            }

            OnGrabThrowRelease(thrown, dir, _boss != null ? _boss.grabThrowDistance : 0f);
        }

        _grabbed = null;
        EnterPhase(BossAttackPhase.Recovery, GrabRecovery);
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
        HideJumpTelegraphClientRpc();

        // 🔴 Rage: 돌진 중 끊기면 **에이전트 속도가 8배로 고정되고 히트 윈도우가 열린 채 남는다**
        //    (그 뒤 모든 이동이 초고속이 되고, 다음 공격이 유닛당 1회 제한을 물려받는다).
        if (_rageDashing) StopRageDash();
        _rageRemaining = 0;

        // 🔴 송전기: 전기 장판과 송전탑이 남는다 — 보스가 죽어도 아레나에 계속 피해를 준다.
        EndChargeZone();
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

    // 붙잡은 대상이 있으면 그쪽을, 없으면 base 타깃을 본다(체인 중 몸이 엉뚱한 곳을 보지 않게).
    void FaceChainTarget()
    {
        if (!IsGrabbedValid()) { FaceTarget(); return; }

        Vector3 dir = _grabbed.transform.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.LookRotation(dir.normalized);
    }

    Player FindGrabTarget()
    {
        if (_grabBuffer == null) _grabBuffer = new Collider[8];

        float radius = _boss != null ? _boss.grabRadius : 2.2f;
        int count = Physics.OverlapSphereNonAlloc(
            transform.position, radius, _grabBuffer, playerMask, QueryTriggerInteraction.Collide);

        Player nearest = null;
        float best = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            Collider c = _grabBuffer[i];
            if (c == null) continue;
            if (!MonsterTargeting.IsAttackable(c)) continue; // 유령은 잡지 않는다

            Player p = c.GetComponentInParent<Player>();
            if (p == null) continue;

            float sqr = (p.transform.position - transform.position).sqrMagnitude;
            if (sqr >= best) continue;
            best = sqr;
            nearest = p;
        }
        return nearest;
    }

    float GrabHold => _boss != null ? _boss.grabHoldDuration : 2f;
    float GrabThrowTime => _boss != null ? _boss.grabThrowDuration : 0.6f;
    float GrabRecovery => _boss != null ? _boss.grabRecoveryDuration : 0.8f;

    // Hold/Throw 애니. 상태 복제로는 단계를 실을 수 없어(전부 Attack 이다) ClientRpc 로 보낸다.
    [ClientRpc]
    void CrossFadeGrabStateClientRpc(bool throwPhase)
    {
        if (_boss == null) return;
        string state = throwPhase ? _boss.grabThrowState : _boss.grabHoldState;
        if (!string.IsNullOrEmpty(state))
            SafeCrossFade(state);
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
    // 🔴 **타겟은 최원거리 플레이어**다. 거리 무관 + 쿨만이 게이트면 10초마다 기계적으로 나와 읽히므로,
    //    타겟 규칙이 이 공격의 의도를 만든다(팀장 확정).
    void BeginJump()
    {
        // 착지점 = 최원거리 플레이어의 발밑(바닥에 투영). 없으면 제자리.
        Vector3 point = transform.position;
        Player farthest = FindFarthestPlayer();
        if (farthest != null) point = farthest.transform.position;

        if (GroundProbe.TryFindGround(point, 0, out RaycastHit ground, out _))
            point = new Vector3(point.x, ground.point.y, point.z);

        _jumpArrivePoint = point;

        // 예고 2개: 고정 크기(어디에 떨어지는가) + 0.1 → AoE 점증(언제 떨어지는가).
        ShowJumpTelegraphClientRpc(point, JumpAoeRadius, JumpHover);

        // 체공 동안 메시를 감춘다 — 착지점으로 순간이동하는 것이 보이지 않게.
        SetModelVisibleClientRpc(false);

        CrossFadeJumpStateClientRpc(landing: false);

        EnterPhase(BossAttackPhase.Leap, JumpHover);
    }

    // 체공 종료 — 착지점으로 이동하고 메시를 되살린 뒤 착지 클립으로 넘어간다.
    void ArriveJump()
    {
        WarpTo(_jumpArrivePoint);
        SetModelVisibleClientRpc(true);

        CrossFadeJumpStateClientRpc(landing: true);

        EnterPhase(BossAttackPhase.Land, JumpLanding);
    }

    // 착지 AoE — 애니 이벤트(OnAttackHit)에서 호출된다. 예고 장판과 **같은 반경**을 쓴다
    // (예고가 판정에 대해 거짓말하지 않게 — 방향 표시기와 같은 원칙).
    void ApplyJumpLandingDamage(BossAttackEntry entry)
    {
        HideJumpTelegraphClientRpc();

        int dmg = _boss.jumpLandingDamage > 0
            ? _boss.jumpLandingDamage
            : (entry != null && entry.damage > 0 ? entry.damage : AttackDamage);
        dmg = Mathf.Max(0, Mathf.RoundToInt(dmg * PhaseDamageMultiplier));
        if (dmg <= 0) return;

        if (_aoeBuffer == null) _aoeBuffer = new Collider[16];

        int count = Physics.OverlapSphereNonAlloc(
            transform.position, JumpAoeRadius, _aoeBuffer, playerMask, QueryTriggerInteraction.Collide);

        var info = new AttackInfo(dmg, AttackType.Default);
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

            var ctx = new AttackHitContext(transform.position, transform, hit);
            if (hurtbox != null) hurtbox.ReceiveAttack(info, ctx);
            else unit.ReceiveAttack(info, ctx);
        }
    }

    // 최원거리 플레이어(서버). base 의 _target 은 최근접 락온이라 쓸 수 없어 직접 훑는다.
    Player FindFarthestPlayer()
    {
        if (_aoeBuffer == null) _aoeBuffer = new Collider[16];

        float radius = _boss != null ? _boss.jumpSearchRadius : 30f;
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
    float JumpAoeRadius => _boss != null ? Mathf.Max(0.1f, _boss.jumpAoeRadius) : 3.5f;

    // ─── 표현(각 피어 로컬) ────────────────────────────────────────────
    // 🔴 예고 장판은 **보스 자식이 아니다.** 보스가 체공 중 착지점으로 이동하므로 자식이면 따라가 버린다.
    //    그래서 각 피어가 프리팹을 착지점에 로컬로 띄운다(복제할 상태가 없는 순수 연출).
    [ClientRpc]
    void ShowJumpTelegraphClientRpc(Vector3 point, float radius, float growTime)
    {
        if (_boss == null || _boss.jumpTelegraphPrefab == null)
        {
            WarnNoJumpTelegraphOnce();
            return;
        }

        if (_telegraphFixed == null) _telegraphFixed = SpawnLocalTelegraph();
        if (_telegraphGrowing == null) _telegraphGrowing = SpawnLocalTelegraph();

        if (_telegraphFixed != null)
        {
            _telegraphFixed.transform.position = point + Vector3.up * 0.01f;
            _telegraphFixed.Show(radius, growTime);
        }
        if (_telegraphGrowing != null)
        {
            _telegraphGrowing.transform.position = point + Vector3.up * 0.02f;
            _telegraphGrowing.ShowGrowing(0.1f, radius, growTime, 0f);
        }
    }

    [ClientRpc]
    void HideJumpTelegraphClientRpc()
    {
        _telegraphFixed?.Hide();
        _telegraphGrowing?.Hide();
    }

    AoeTelegraph SpawnLocalTelegraph()
    {
        // 부모 없이(씬 루트) 만들어 보스 이동에 끌려가지 않게 한다. 재사용하므로 점프마다 할당이 없다.
        GameObject go = Instantiate(_boss.jumpTelegraphPrefab);
        if (go.TryGetComponent(out AoeTelegraph t)) return t;

        Debug.LogError(
            $"{name}: jumpTelegraphPrefab({go.name}) 에 AoeTelegraph 컴포넌트가 없다 — 예고가 표시되지 않는다.", this);
        Destroy(go);
        return null;
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
    [ClientRpc]
    void CrossFadeJumpStateClientRpc(bool landing)
    {
        if (_boss == null) return;
        string state = landing ? _boss.jumpLandingState : _boss.jumpHoverState;
        if (!string.IsNullOrEmpty(state))
            SafeCrossFade(state);
    }

    void WarnNoJumpTelegraphOnce()
    {
        if (_warnedNoJumpTelegraph) return;
        _warnedNoJumpTelegraph = true;
        Debug.LogWarning(
            $"{name}: jumpTelegraphPrefab 이 없어 착지 예고가 표시되지 않는다 — 플레이어가 피할 근거가 없다. " +
            "AoeTelegraph 프리팹을 배선할 것(NetworkObject 는 붙이지 말 것).", this);
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
        _wells.ThrowCycleElapsed = OnWellsThrowCycle;   // 주기 만료(서버) → 투척 애니 브로드캐스트
        _wells.ThrowRequested = SpawnAndThrowBomb;      // 클립 이벤트(서버) → 폭탄 실물 스폰
        _wells.ValidateContract(name);
    }

    // 서버: 투척 주기가 돌았다 → 전 피어에서 투척 애니를 재생한다.
    // 실제 폭탄은 클립의 ThrowBombEvent 프레임에 서버가 스폰한다(손을 떠나는 타이밍과 일치).
    void OnWellsThrowCycle()
    {
        if (!IsServer || State == MonsterState.Dead) return;
        PlayWellsThrowClientRpc();
    }

    [ClientRpc]
    void PlayWellsThrowClientRpc() => _wells?.PlayState(BossWellsState.Throw);

    // 클립 이벤트 시점(서버) — 손 소켓에서 대각선 임펄스로 던진다.
    void SpawnAndThrowBomb()
    {
        if (!IsServer || _boss == null) return;
        if (_boss.bombPrefab == null)
        {
            WarnNoBombPrefabOnce();
            return;
        }

        Transform socket = _wells != null ? _wells.BombSocket : transform;

        GameObject go = Instantiate(_boss.bombPrefab, socket.position, Quaternion.identity);
        if (!go.TryGetComponent(out NetworkObject netObj))
        {
            Debug.LogError($"{name}: bombPrefab({go.name}) 에 NetworkObject 가 없다 — 스폰할 수 없다.", this);
            Destroy(go);
            return;
        }
        netObj.Spawn();

        // 대각선 임펄스 — 좌우 분산(spreadAngle)과 상향각(bombThrowPitch)을 함께 준다.
        // 🔴 소켓 회전에 의존하지 않는다(아트 임포트 회전에 따라 뒤집힌 전례가 있다) — 보스 전방 기준으로 만든다.
        float spread = _boss.spreadAngle > 0f ? Random.Range(-_boss.spreadAngle, _boss.spreadAngle) : 0f;
        Vector3 flat = transform.forward;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.0001f) flat = Vector3.forward;
        Vector3 dir = Quaternion.Euler(-_boss.bombThrowPitch, spread, 0f) * flat.normalized;

        if (go.TryGetComponent(out BossBomb bomb))
            bomb.Throw(dir.normalized * Mathf.Max(0.1f, _boss.throwImpulse));
        else
            Debug.LogError($"{name}: bombPrefab({go.name}) 에 BossBomb 이 없다 — 던질 수 없다.", this);
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

    void WarnNoBombPrefabOnce()
    {
        if (_warnedNoBombPrefab) return;
        _warnedNoBombPrefab = true;
        Debug.LogWarning($"{name}: bombPrefab 이 비어 있어 Wells 가 빈손으로 던진다.", this);
    }
    #endregion

    #region 페이즈 시퀀스 — 송전기(차징) → 실패 시 레이지 돌진
    // 정본 §9.1: ① Charging 진입(중앙 이동 후 대기) ② 전기 장판 on ③ 실드 점증
    //            ④ 송전탑 활성(1인 1 / 2인 2 / **3인 이상 4**) ⑤ 전멸 → Groggy / 시간초과 → Rage
    //
    // ⚠️ 송전탑 구현(아레나 오브젝트)은 IBossChargeSequence 로 분리했다. 구현이 없어도 시퀀스는
    //    **일관되게 돈다** — 제한시간이 끝나면 스펙대로 Rage 로 넘어간다(실패 취급).
    void BeginCharge()
    {
        StopAgentHard();
        SetCounterWindow(false); // 차징 중엔 카운터 창 없음(확정 스펙)

        int players = CountAlivePlayers();
        int pylons = PylonCountFor(players);

        if (_charge == null) _charge = GetComponentInChildren<IBossChargeSequence>(true);
        if (_charge != null)
        {
            _charge.Begin(pylons, ChargeTimeLimit);
        }
        else if (!_warnedNoCharge)
        {
            _warnedNoCharge = true;
            Debug.LogWarning(
                $"{name}: IBossChargeSequence 구현이 없다 — 송전탑이 활성되지 않고 제한시간 뒤 " +
                "그대로 레이지로 넘어간다(스펙상 '실패'와 같은 경로).", this);
        }

        SpawnChargeZone();

        Debug.Log($"[23호] 송전기 시작 — 인원 {players}명 → 송전탑 {pylons}개, 제한시간 {ChargeTimeLimit:0.#}초", this);
        EnterPhase(BossAttackPhase.ChargeWait, ChargeTimeLimit);
    }

    void TickCharge()
    {
        BossChargeResult result = _charge != null ? _charge.Poll() : BossChargeResult.InProgress;

        // 제한시간 만료 = 실패(정본 §9.1). 구현이 없을 때도 이 경로로 빠진다.
        if (result == BossChargeResult.InProgress && _attackPhaseTimer > 0f)
            return;

        bool cleared = result == BossChargeResult.AllPylonsDestroyed;
        EndChargeZone();
        _charge?.Cancel();

        if (cleared)
        {
            // 🔴 송전기 그로기는 카운트를 올리되 **Break 로 승격하지 않는다** —
            //    페이즈 전환 직후 5초 무력화가 겹치면 페이즈 연출이 죽는다(확정 스펙).
            _attackPhase = BossAttackPhase.None;
            Debug.Log("[23호] 송전기 전멸 — 그로기(Break 승격 없음)", this);
            EnterCounterGroggy(allowBreak: false);
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
        FaceTarget();
        _rageDashDir = transform.forward;
        _rageDashDir.y = 0f;
        if (_rageDashDir.sqrMagnitude < 0.0001f) _rageDashDir = Vector3.forward;
        _rageDashDir.Normalize();

        _rageDashing = true;
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

    void StopRageDash()
    {
        _rageDashing = false;
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
        FaceTarget();
        _dashDir = transform.forward;
        _dashDir.y = 0f;
        if (_dashDir.sqrMagnitude < 0.0001f) _dashDir = Vector3.forward;
        _dashDir.Normalize();

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
        return (a - b).sqrMagnitude <= DashArriveEpsilon * DashArriveEpsilon;
    }
    #endregion

    // NavMesh 경계까지 클램프한 목표로 돌진(SpinnerBot 선례 — 낭떠러지 진입 불가, 가장자리에서 정지).
    // 반환값 = **목적지가 경계에서 잘렸나**(true 면 그 끝이 벽/낭떠러지다). 돌진이 벽 충돌을 판정하는 근거다.
    // clearance > 0 이면 그 지점에서 그만큼 **앞당겨** 멈춘다(캐리 대상이 벽에 끼지 않게).
    bool StartDashMove(Vector3 dir, float speedMultiplier, float maxDistance, float clearance = 0f)
    {
        _dashDestination = transform.position;
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return false;

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
        agent.isStopped = false;
        agent.speed = Mathf.Max(0.1f, MoveSpeed * speedMultiplier);
        agent.SetDestination(desired);
        _dashDestination = desired;
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
        }
        _dashPrevStopDistance = -1f;
        StopAgentHard();
    }

    // base 의 StopAgent 는 private 이라 파생이 못 부른다 — 같은 일을 하는 최소 구현.
    void StopAgentHard()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
        agent.isStopped = true;
        agent.velocity = Vector3.zero;
    }

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

        float radius = _boss != null ? _boss.jumpSearchRadius : 30f;
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

        // 실패든 성공이든 데미지는 정상 처리된다 — 카운터 실패에 패널티는 없다(확정 스펙).
        bool resolved = base.ReceiveAttack(attackInfo, hitContext);

        if (counter && resolved && State != MonsterState.Dead)
            EnterCounterGroggy(allowBreak: true);

        return resolved;
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
    protected void EnterCounterGroggy(bool allowBreak)
    {
        if (!IsServer) return;

        _counterGroggyCount++;

        int max = data != null ? Mathf.Max(1, data.maxGroggyCount) : 5;
        bool breakNow = allowBreak && _counterGroggyCount >= max;
        if (breakNow) _counterGroggyCount = 0;

        float groggy = breakNow
            ? (_boss != null ? _boss.breakDuration : 5f)
            : (data != null ? data.groggyDuration : 2f);

        SetCounterWindow(false);

        // Hit(리액션) → 타이머 종료 후 Groggy/Break. base 의 ForceHitReaction 이 진행 중 공격을 취소한다.
        ForceHitReaction(HitReactionDuration, groggy);

        Debug.Log(
            $"[23호] 카운터 성공 — 그로기 카운트 {(breakNow ? max : _counterGroggyCount)}/{max}" +
            (breakNow ? $" → BREAK {groggy:0.#}초" : $" → 그로기 {groggy:0.#}초"),
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
