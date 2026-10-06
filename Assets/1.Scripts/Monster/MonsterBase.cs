using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using static EffectCatalog;

// 몬스터 코드 FSM 두뇌. 서버 권한.
//
// 설계 목적: BT↔Animator desync 회피 → BehaviorGraphAgent를 쓰지 않고 순수 코드 FSM으로
// 서버가 상태를 소유(_state NetworkVariable). 클라는 상태 변경 콜백에서 Animator만 재생한다.
//
// - UnitBase = Unit 직접 상속(Enemy 상속 금지: Enemy는 BT 결합).
// - 데미지는 BaseAttack→ReceiveAttack→TakeDamage(AttackInfo) 서버 경로로만 유입된다.
//   (오너→서버 직접 데미지 RPC 미사용.)
// - 자산(NavMesh/Animator/디졸브)이 없어도 컴파일되고 예외로 죽지 않도록 전부 널가드.
[RequireComponent(typeof(NetworkObject))]
public class MonsterBase : Unit
{
    [Header("데이터")]
    [SerializeField] protected MonsterDataSO data;

    public override MonsterRank Rank => data != null ? data.rank : MonsterRank.Normal;

    [Header("컴포넌트 참조(비우면 자동 탐색)")]
    [SerializeField] protected NavMeshAgent agent;
    [SerializeField] protected Animator animator;
    [SerializeField] protected MonsterStatusEffect status;
    [SerializeField] protected MonsterMeleeAttack meleeAttack;
    [SerializeField] protected MonsterRangedAttack rangedAttack;

    [Header("타게팅")]
    [SerializeField] protected LayerMask playerMask;         // 인지 대상(플레이어) 레이어
    [DataTableIgnore] [SerializeField] protected int maxDetectionResults = 16;

    // hitPointMode는 여기 없다 — 전 유닛 공통이라 EffectManager로 올렸다(런타임 교체도 거기서).
    [Header("피격 이펙트 제어")]
    [SerializeField] Collider hitVFXCollider;
    [SerializeField] HitVFXType hitVFXType;

    [Header("사망 시 비활성화할 콜라이더(선택)")]
    [SerializeField] protected Collider bodyCollider;

    // 상태 복제. 서버 write / 모두 read.
    readonly NetworkVariable<MonsterState> _state = new NetworkVariable<MonsterState>(
        MonsterState.Idle,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    public MonsterState State => _state.Value;

    // 이동 블렌드용 속도 복제(클라 Animator Speed 파라미터 구동).
    readonly NetworkVariable<float> _animSpeed = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // 간파(카운터) 창 **표시** 복제 — 판정(MonsterCounterWindow)과 따로 간다.
    // ⚠️ 예전엔 ClientRpc 한 번이라 창 도중 합류한 클라는 표시를 못 받았다(09-28 전수조사 #10).
    readonly NetworkVariable<bool> _counterVisual = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>
    /// 간파 표시를 판정 창보다 **이만큼 먼저** 끈다(초). 간파 스킬은 서버에서 시작하고 이 시간 뒤에 판정되므로
    /// (<c>FirstMeleeInterruptSkillData.hitDelay</c> 0.15) "보일 때 누르면 성공"이 보장된다 — 팀장 09-28: 유예 없음, 표시만 일찍.
    /// </summary>
    public const float CounterVisualLeadSeconds = 0.15f;
    float _counterVisualOffAt = -1f;   // 서버 Time.time. < 0 = 예약 없음

    // 서버 전용 런타임 상태
    Vector3 _spawnPosition;
    Quaternion _spawnRotation;
    Transform _target;

    /// 고정 터렛 조준 예고 게이트(선택). <see cref="SeekTurret"/> 만 쓴다.
    ITurretAimGate _aimGate;
    Collider[] _detectBuffer;

    // 공격 슬롯별 쿨다운.
    // 일반 몬스터는 공격이 1종이라 슬롯 0 하나만 쓰고, 그 경우 동작은 단일 쿨다운 시절과 완전히 같다.
    // 보스처럼 공격이 여러 종류면 ConfigureAttackSlots(n)으로 슬롯을 늘리고 슬롯마다 쿨을 따로 돌린다.
    float[] _lastUsedByAttack = { -999f };
    float[] _cooldownByAttack = { 0f };   // 0 이하면 base 쿨(data.attackCooldown)로 폴백

    protected const int DefaultAttackSlot = 0;
    protected const int NoAttack = -1;     // SelectAttackSlot 반환값: "지금 쓸 공격이 없다"

    /// <summary>StartAttack 이 쿨을 기록할 슬롯. 파생이 공격을 고른 뒤 세팅한다(기본 0).</summary>
    protected int CurrentAttackSlot { get; set; } = DefaultAttackSlot;
    protected float _stateTimer;   // 서브클래스(콤보 보스 등)가 공격 커밋 길이를 덮어쓸 수 있게 protected.
    bool _attackFired;
    bool _commitFired;             // attackFinishTrigger 1회 발동 가드(커밋 이벤트/히트 중 먼저 온 쪽만)
    Vector3 _repositionDest;       // 전투 이동(후퇴/재배치) 목적지
    bool _hasRepositionDest;
    float _defaultStoppingDistance; // 전투 이동 중 임시로 낮춘 stoppingDistance 복원용
    float _combatMoveUntil;        // 최소 이동 커밋 만료 시각(이 시각까지 공격/정지 억제)
    bool _combatMoveCommitted;     // 전투 이동 커밋 중
    float _combatMoveSpeed;        // 커밋 이동 속도(후퇴=chaseSpeed, 재배치=MoveSpeed)
    bool _combatMoveRepick;        // 도착 시 다음 지점 재선택 여부(재배치=true, 후퇴=false)
    int _groggyCount;
    float _groggyAfterHit;         // Hit 종료 후 이어붙일 Groggy 길이(0 = 평소대로 Idle 재평가). ForceHitReaction 이 세팅.
    Vector3 _knockbackDir;         // 지속넉백 방향(수평 정규화)
    float _knockbackSpeed;         // 지속넉백 속도(m/s) = AttackInfo.knockbackStrength
    float _staggerAfterKnockback;  // 넉백 종료 후 Stunned 경직 시간(초)
    bool _isDead;
    bool _initialized;
    bool _inAttackRange;           // 사거리 안에 들어와 있나(히스테리시스 적용). 진입 순간에 첫 공격 지연을 건다
    // 🔴 protected — 파생(23호)이 같은 이름을 다시 선언하면 시계가 갈린다(중복 직렬화 에러).
    protected float _lastRetargetTime = -1f; // 마지막 주기 재선정 시각(초). **-1 = 아직 교전 전**
    float _heightLostSince = -1f;  // 물고 있는 대상이 높이 조건을 벗어난 시각(-1 = 정상)
    // 경사·계단을 오르내리는 동안 판정이 깜빡여 타깃이 튀는 것을 막는 유예(초).
    const float HeightLossGrace = 0.5f;
    bool _animatorHeldLocally;     // 이 피어에서 애니메이터를 정지시켜 뒀나(자세 홀드 래치)
    float _animatorResumeSpeed = 1f; // 홀드 전 애니메이터 속도 — 풀 때 이 값을 되돌린다
    Coroutine _actionPoseHoldRoutine; // 클립 끝에서 자세를 잡으려고 대기 중인 예약(피어 로컬)
    IBossTelegraph _telegraph;     // 카운터 창 표현(있으면). 지연 해석 — 런타임 부착일 수 있다
    bool _serverLogicSuspended;    // 연출 구간 게이트(SetServerLogicSuspended). true 면 서버 FSM 이 안 돈다
    Coroutine _deathFxRoutine;              // 임시 사망 표시 코루틴(모든 피어)
    const float DeathPlaceholderDuration = 1f; // 임시 사망 표시 지속(디졸브/애니 도입 시 제거)
    // 사거리 이탈 판정에 더하는 여유(m). 경계에서 진입/이탈이 깜빡이는 것을 막는다.
    // 공격 중 재조준 이탈 판정도 같은 값을 쓴다 — 두 곳이 어긋나면 한쪽만 깜빡인다.
    const float AttackRangeExitMargin = 0.5f;

    // NavMeshAgent 회피 우선순위(낮을수록 우선 = 남이 비켜감). 정지(공격/피격 등) 중인 몹이
    // 이동 중인 다른 몹에게 밀려나지 않도록 정지 시 우선순위를 높인다(값을 낮춘다).
    const int HoldAvoidancePriority = 20;
    const int MoveAvoidancePriority = 50;

    /// <summary>
    /// 🔴 (실험 10-06) 아트 팩(Robot Sentries) 프리팹의 **본마다 붙은 kinematic Rigidbody 의 보간/외삽을 끈다.**
    /// 보간/외삽이 켜진 Rigidbody 의 Transform 은 물리가 관리한다. 클라(NGO)는 프리팹을 기본 위치에 만든 뒤 루트를
    /// 스폰 위치로 옮기는데(NetworkSpawnManager), AutoSyncTransforms=0 이라 물리가 "옮기기 전" 자세를 본에 다시 써서
    /// 본이 루트 높이 × (거치는 Rigidbody 수)만큼 아래로 굳는다 — MPPM 지연 재현에서 높은 곳 PeekABot 머리 −7.86
    /// (예측 −7.85), 기둥 0.11(예측 0.11). 호스트는 처음부터 스폰 위치에 Instantiate 해서 안 생긴다.
    /// 이 Rigidbody 들은 우리 코드가 쓰지 않는다(래그돌·피격 경로 없음, 붕괴 사망은 별도 조각을 만든다).
    /// 아트 프리팹(SVN)은 그대로 두고 런타임에서만 끈다.
    /// </summary>
    void DisableBoneRigidbodyInterpolation()
    {
        foreach (var rb in GetComponentsInChildren<Rigidbody>(true))
        {
            if (rb.transform == transform || rb.interpolation == RigidbodyInterpolation.None) continue;
            rb.interpolation = RigidbodyInterpolation.None;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        DisableBoneRigidbodyInterpolation();

        // 참조 자동 보강(인스펙터 미할당 대비).
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (animator == null) animator = GetComponentInChildren<Animator>();

        // 데이터가 컨트롤러를 지정했으면 그것으로 덮는다(비어 있으면 프리팹 배선을 그대로 쓴다).
        // 🔴 왜 데이터로 넣는가 — Animator 가 2단 중첩 프리팹(우리 프리팹 → 아트 프리팹 → FBX) 안에
        //    있어서 외부 프리팹에서 `m_Controller` 를 오버라이드하면 **타깃이 해석되지 않는다**
        //    (2026-09-10 실측: 저장 성공 + YAML 에 엔트리 존재 + 로드하면 null = 조용한 실패).
        //    아트 프리팹을 고치면 SVN 이고 팩 업데이트에 덮인다. 그래서 git 쪽 데이터에 둔다.
        //    쓰는 곳: 고정 터렛(PeekABot·TeslaBot) — 아트 컨트롤러에 우리가 빠져나올 수 없는
        //    상태(Hide/Raise, Charge)가 있어 몸체가 분리돼 보였다.
        if (animator != null && data != null && data.animatorControllerOverride != null)
            animator.runtimeAnimatorController = data.animatorControllerOverride;

        if (status == null) status = GetComponent<MonsterStatusEffect>();
        if (meleeAttack == null) meleeAttack = GetComponentInChildren<MonsterMeleeAttack>();
        if (rangedAttack == null) rangedAttack = GetComponentInChildren<MonsterRangedAttack>();

        // 고정 터렛의 조준 예고 게이트(선택). 없으면 SeekTurret 이 예전대로 바로 쏜다.
        _aimGate = GetComponent<ITurretAimGate>();

        // 공격 애니 이벤트 릴레이 자동 부착(Animator 오브젝트에 — 이벤트는 같은 GO의 메서드만 호출 가능).
        // 🔴 부착이 곧 동작은 아니다. OnAttackHit 은 **폴백이 없어서**, 클립에 그 이벤트가 없으면
        //    그 공격은 데미지를 내지 못한다(OnAttackEnd 만 attackDuration 타이머가 폴백한다).
        //    비대칭인 이유는 HandleAttack 주석 참조.
        if (animator != null && !animator.TryGetComponent(out MonsterAnimationEventRelay _))
            animator.gameObject.AddComponent<MonsterAnimationEventRelay>();

        _state.OnValueChanged += OnStateChanged;
        _counterVisual.OnValueChanged += OnCounterVisualChanged;
        ApplyCounterWindowVisual(_counterVisual.Value);   // 늦게 합류한 클라도 지금 값으로

        if (IsServer)
        {
            ServerInitialize();
        }
        else
        {
            // 클라는 이동 권한 없음 — NavMeshAgent 비활성(NetworkTransform 복제로 위치 반영).
            if (agent != null) agent.enabled = false;
        }

        // 🔴 이전 생애가 남긴 자세 홀드를 먼저 푼다. 사망 시 애니메이터를 멈추므로(아래 Dead 케이스),
        //    이 오브젝트가 풀로 재사용되면 **얼어붙은 채로 되살아난다.** 스폰은 멱등해야 한다.
        ApplyReleaseActionPose();

        // 스폰 시점의 상태를 즉시 애니메이션에 반영(뒤늦게 접속한 클라 포함).
        PlayStateAnimation(_state.Value);
    }

    public override void OnNetworkDespawn()
    {
        _state.OnValueChanged -= OnStateChanged;
        _counterVisual.OnValueChanged -= OnCounterVisualChanged;
        base.OnNetworkDespawn();
    }

    void ServerInitialize()
    {
        if (data == null)
        {
            Debug.LogError($"{name}: MonsterDataSO가 할당되지 않아 초기화할 수 없습니다.", this);
            enabled = false;
            return;
        }

        // Unit 스탯 주입(파라미터 순서 = Unit.Initialize 계약. MaxShield는 PlayerSkill 머지에서 개념 제거됨).
        Initialize(data.attackDamage, data.moveSpeed, data.attackSpeed, data.maxHp, data.defense);

        // 근접 공격 컴포넌트에 데미지/타깃레이어 스냅샷 반영.
        if (meleeAttack != null)
        {
            meleeAttack.SetDamageSnapshot(data.attackDamage);
            meleeAttack.SetTargetLayer(playerMask);
        }

        // 원거리 공격기(있으면) — 데미지/타깃레이어 스냅샷 + 투사체 설정 주입.
        if (rangedAttack != null)
        {
            rangedAttack.SetDamageSnapshot(data.attackDamage);
            rangedAttack.SetTargetLayer(playerMask);
            rangedAttack.ConfigureProjectile(data.projectilePrefab, data.projectileSpeed, data.projectileLifetime, data.projectileArcHeight, data.projectileSplashRadius);
        }

        _spawnPosition = transform.position;
        _spawnRotation = transform.rotation;
        _detectBuffer = new Collider[Mathf.Max(1, maxDetectionResults)];

        if (agent != null)
        {
            agent.enabled = true;
            agent.speed = data.moveSpeed;
            _baseAgentAcceleration = agent.acceleration;   // 돌진 가속 배수의 기준(프리팹값)
            agent.stoppingDistance = Mathf.Max(0f, data.attackRange * 0.8f);
            _defaultStoppingDistance = agent.stoppingDistance;
            // 부분 겹침: 회피 반경을 콜라이더보다 작게(데이터값). 물리 대신 회피로만 겹침량 조절(저비용).
            // ⚠️ 몸이 큰 몹(23호 — 캡슐 1.53)은 프리팹 반경을 지킨다. 0.3 으로 덮으면 좁은 통로에서 벽을 파고든다(09-28 전수조사).
            if (!KeepPrefabAgentRadius)
                agent.radius = Mathf.Max(0.01f, data.avoidanceRadius);
            agent.obstacleAvoidanceType = data.obstacleAvoidance;
        }

        // 스폰 슈퍼아머(무한).
        if (data.startsWithSuperArmor && status != null)
            status.ApplyStatus(StatusEffectType.SuperArmor, 0f);

        _initialized = true;
        SetState(MonsterState.Idle);
    }

    void Update()
    {
        // 애니메이션 이동 블렌드는 모든 피어에서 반영(복제된 _animSpeed).
        // (Chomp 처럼 fullBlendWhileMoving 이면 움직이는 동안 블렌드를 이동 클립 100% 로 보낸다 — LocomotionData 참조.)
        // 🔴 즉시 쓰지 않는다 — 공격 시작(StopAgent) 프레임에 블렌드가 0 으로 꺾여 전이 동안 출발 포즈가 **대기 자세**가
        //    된다("달리다 Idle 한 번 찍고 공격", 팀장 10-06). 감쇠·전이 고정·복귀 준비는 WriteLocomotionBlend 참조.
        if (data != null)
            WriteLocomotionBlend();

        // 애니 재생 속도 — 모든 피어, 이 한 곳에서만 쓴다(자세 홀드 중엔 홀드가 0 을 쥔다).
        if (ManagesAnimatorSpeed && !_animatorHeldLocally && animator != null)
            animator.speed = ManagedAnimatorSpeed();

        if (!IsServer || !_initialized || _isDead)
            return;

        // 간파 표시 조기 소등 — 판정 창이 닫히기 CounterVisualLeadSeconds 전에 표시만 끈다.
        if (_counterVisualOffAt >= 0f && Time.time >= _counterVisualOffAt)
        {
            _counterVisualOffAt = -1f;
            SetCounterVisual(false);
        }

        // 연출이 몸을 몰고 있는 동안에는 FSM 을 돌리지 않는다(SetServerLogicSuspended 주석 참조).
        if (_serverLogicSuspended)
        {
            if (!Mathf.Approximately(0f, _animSpeed.Value))
                _animSpeed.Value = 0f;
            return;
        }

        TickServer(Time.deltaTime);

        // 이동 속도 복제 갱신(에이전트 실제 속도 기반).
        float speed = (agent != null && agent.enabled && !agent.isStopped) ? agent.velocity.magnitude : 0f;
        if (!Mathf.Approximately(speed, _animSpeed.Value))
            _animSpeed.Value = speed;
    }

    #region 서버 FSM
    /// <summary>
    /// [서버] 매 틱 FSM **앞에서** 도는 파생 확장점(상태 무관). true 를 돌려주면 이번 틱 FSM 을 건너뛴다.
    /// 23호 취약 넉백처럼 "어느 상태에서든 몸을 미는" 동작이 추격·공격 결정과 싸우지 않게 쓴다.
    /// </summary>
    protected virtual bool OnServerPreTick(float dt) => false;

    void TickServer(float dt)
    {
        if (OnServerPreTick(dt)) return;

        // 이동 봉쇄 상태이상(에어본/기절/속박)이면 에이전트 정지.
        if (status != null && status.BlocksMovement)
            StopAgent();

        switch (_state.Value)
        {
            case MonsterState.Idle:
            case MonsterState.Chase:
                HandleSeekAndCombat();
                break;
            case MonsterState.Attack:
                HandleAttack(dt);
                break;
            case MonsterState.Hit:
                HandleTimedResume(dt);
                break;
            case MonsterState.Groggy:
                HandleGroggy(dt);
                break;
            case MonsterState.Return:
                HandleReturn();
                break;
            case MonsterState.Knockback:
                HandleKnockback(dt);
                break;
            case MonsterState.Dead:
                break;
        }
    }

    /// <summary>
    /// 리쉬·복귀·재배치의 <b>기준점</b>을 옮긴다. 연출로 몬스터를 이동시키는 스포너는
    /// <b>연출이 끝난 뒤</b> 이걸 호출해야 한다.
    ///
    /// 🔴 왜 있는가 (2026-08-18 실제 사고): 보스는 착지점 <b>18m 위</b>에서 Instantiate 된 뒤
    /// <c>BossEncounterDirector</c> 가 1.2초간 내려보낸다. 그런데 기준점은 <c>Awake</c> 시점 위치라
    /// 착지하는 순간 <c>leashRadius</c>(No23 = 15m) <b>밖</b>이 되고, <see cref="HandleSeekAndCombat"/>
    /// 첫 줄에서 <b>매 프레임</b> 리쉬 복귀가 걸린다 → <c>EnterReturn</c> 의 <c>Revive()</c> 로 체력이
    /// 최대로 되돌아가고 공격 체인이 계속 끊긴다.
    /// 겉으로는 "데미지가 안 박히고 애니메이션이 안 나온다"로 보여 원인을 찾기 어렵다.
    ///
    /// 기준점 하나가 리쉬 판정·복귀 목표·재배치 클램프를 모두 결정하므로 여기만 맞으면 전부 맞는다.
    /// leashRadius 를 키워 가리는 것은 답이 아니다 — spawnHeight 가 바뀌면 그대로 재발한다.
    /// </summary>
    /// <summary>
    /// 서버 FSM 을 <b>일시 정지</b>한다. 연출로 몬스터의 위치를 직접 모는 스포너가
    /// <b>연출 동안</b> 켜 두고, 전투로 넘길 때 끈다.
    ///
    /// 🔴 왜 있는가 (2026-08-18 실제 사고 — "착지 직후 첫 돌진이 제자리에서 애니만 돈다"):
    /// 보스는 <c>Spawn()</c> 되는 순간부터 <see cref="Update"/> 가 돌아 FSM 이 <b>살아 있다.</b>
    /// 그런데 <c>BossEncounterDirector</c> 는 하강 연출과 싸우지 않으려고 스폰 직후
    /// <c>NavMeshAgent</c> 를 <b>꺼 둔다</b>(그리고 착지 후 <c>impactHoldSeconds</c> 0.9초 동안도
    /// 꺼진 채다). 즉 <b>FSM 은 켜져 있는데 다리는 없는 구간이 1초 넘게</b> 존재한다.
    /// 하강 막바지에 플레이어가 <c>detectionRadius</c>(No23 = 8m) 안에 들어오면 보스는 그 구간에서
    /// 공격을 고르고 시작한다. 돌진이 걸리면 <c>StartDashMove</c> 가 에이전트를 못 찾아 조용히
    /// 아무것도 하지 않고, 클립만 재생돼 <b>"제자리 돌진"</b> 이 된다.
    /// 에이전트가 없어도 도는 공격(훅·잡기)은 그 구간에 <b>허공에 대고 나간다.</b>
    ///
    /// 그래서 고칠 자리는 돌진이 아니라 <b>연출 중에 FSM 이 도는 것</b> 자체다.
    /// 호출처는 보스 Director 뿐이라 몹 8종·중간보스 3종의 경로는 그대로다.
    ///
    /// ⚠️ 이건 <b>정지</b>이지 무적이 아니다. 피격·사망 경로(<see cref="TakeDamage"/>)는 그대로 살아 있다.
    /// </summary>
    public void SetServerLogicSuspended(bool suspended)
    {
        if (_serverLogicSuspended == suspended) return;
        _serverLogicSuspended = suspended;

        if (!IsServer) return;

        if (suspended)
        {
            // 진행 중이던 이동·공격을 정리하고 대기 자세로 내려놓는다 — 연출이 끝난 뒤
            // 반쯤 진행된 공격 체인이 되살아나지 않게.
            ClearReposition();
            StopAgent();
            SetState(MonsterState.Idle);
        }

        // 연출이 끝나고 FSM 이 깨어나는 시점 — 파생이 개전 처리를 얹을 수 있게 훅을 연다.
        if (!suspended) OnServerLogicResumed();

        Edit.Log($"[Monster] {name} 서버 FSM {(suspended ? "정지" : "재개")} — 연출 구간 게이트", this);
    }

    /// <summary>
    /// 서버 FSM 이 연출 정지에서 <b>깨어난</b> 직후 1회. 파생이 개전 처리를 얹는 자리다.
    /// (정지로 들어갈 때는 불리지 않는다 — 그쪽은 위에서 이미 정리한다.)
    /// </summary>
    protected virtual void OnServerLogicResumed() { }

    public void SetSpawnAnchor(Vector3 worldPosition)
    {
        Vector3 previous = _spawnPosition;
        _spawnPosition = worldPosition;
        Edit.Log($"[Monster] {name} 리쉬 기준점 이동 — {previous} → {worldPosition} " +
                 $"(leash {data?.leashRadius ?? 0}m)", this);
    }

    void HandleSeekAndCombat()
    {
        // 리쉬: 스폰 지점에서 leash 밖이면 복귀 우선(진입 즉시 상태 초기화 + 최대 체력 회복).
        // ⚠️ 연출로 내려오는 보스처럼 스폰 위치와 전투 원점이 다른 경우는 스포너가 착지 후
        //    SetSpawnAnchor 로 기준점을 옮겨야 한다. 안 옮기면 여기서 매 프레임 걸린다.
        if (Vector3.Distance(transform.position, _spawnPosition) > data.leashRadius)
        {
            EnterReturn();
            return;
        }

        // 타겟 락온: 유효한 타겟이 있으면 계속 유지한다(인지반경 밖으로 나가도 리쉬 전까진 추격).
        // 타겟이 없거나 무효(디스폰/비활성)일 때만 새로 탐색한다.
        //
        // 🔴 파생이 ShouldReacquireTarget 으로 **주기 재선정**을 얹을 수 있다(23호 어그로).
        //    기본값이 false 라 몹 8종·중간보스 3종의 락온 동작은 그대로다.
        // 층이 갈렸으면 물고 있던 대상도 놓는다.
        //
        // 🔴 왜 리쉬로는 안 풀리는가 — 리쉬는 **몬스터 자신이 스폰 지점에서 멀어졌는지**만 본다.
        //    고정 포탑(PeekABot·TeslaBot)은 움직이지 않으니 그 거리가 영원히 0 이다 → 한 번 문 대상을
        //    **영원히** 물고 위층·아래층으로 계속 쏜다(2026-09-08 실측: Δy 2.93m 인데도 공격).
        //    그래서 유지 조건에도 높이를 넣는다. 인지와 같은 임계값을 쓰고, 경사에서 깜빡이는 것을
        //    막기 위해 유예를 둔다.
        if (_target != null && !MonsterPerceptionPolicy.WithinHeight(
                transform.position.y, _target.position.y, data.detectionHeightTolerance))
        {
            if (_heightLostSince < 0f) _heightLostSince = Time.time;
            if (Time.time - _heightLostSince >= HeightLossGrace)
            {
                _target = null;
                _heightLostSince = -1f;
                _lastRetargetTime = -1f;   // 다음 교전에서 시계를 다시 센다
            }
        }
        else
        {
            _heightLostSince = -1f;
        }

        if (!IsTargetValid(_target))
        {
            // 빈손 탐색 주기 — 타깃 없는 몹 전부가 매 프레임 OverlapSphere 를 돌던 것(PLAN-cleanup-optimization S2-2).
            // 첫 탐색·타깃을 잃은 직후는 즉시, **실패한 뒤에만** 간격을 둔다(몹마다 위상을 흩뜨린다). 어그로 지연 ≤ 0.25초.
            _target = Time.time >= _nextAcquireTime ? FindNearestTarget() : null;
            if (_target == null && Time.time >= _nextAcquireTime)
                _nextAcquireTime = Time.time + AcquireRetryInterval + Random.Range(0f, AcquireRetryJitter);

            // 교전 시작 시각 = 주기 재선정 시계의 0 점. 여기서 세워야 첫 타깃을 잡은 틱에
            // 곧바로 재선정이 도는 것을 막는다.
            if (_target != null && _lastRetargetTime < 0f) _lastRetargetTime = Time.time;
        }
        else if (ShouldReacquireTarget())
        {
            // 🔴 **빈손이면 기존 타깃을 유지한다.** FindNearestTarget 은 detectionRadius 안만
            //    훑는데, 락온 규약은 "인지반경 밖으로 나가도 리쉬 전까진 추격"이다.
            //    빈손을 그대로 대입하면 인지반경~리쉬 사이(23호 기준 8~15m)를 달리던 추격이
            //    재선정 순간 조용히 끊기고 보스가 Idle 로 빠진다.
            // 같은 사람을 다시 고르지 않게 할지는 파생이 정한다(23호는 SO 노브).
            // 대안이 없으면 candidate 가 null 이라 아래 가드가 기존 타깃을 지킨다 —
            // 1대1 에서는 자동으로 현행(계속 물기)과 같아진다.
            Transform candidate = RetargetAvoidsCurrentTarget
                ? FindNearestTarget(exclude: _target)
                : FindNearestTarget();
            if (candidate != null) _target = candidate;

            // 🔴 후보를 못 찾았어도 시계를 다시 돌린다. 안 그러면 다음 틱에 또 재선정 조건이 서서
            //    빈손 탐색(FindNearestTarget)이 매 틱 돈다.
            _lastRetargetTime = Time.time;
        }

        if (_target == null)
        {
            StopAgent();
            _inAttackRange = false;
            SetState(MonsterState.Idle);
            return;
        }

        float dist = Vector3.Distance(transform.position, _target.position);
        bool movementBlocked = status != null && status.BlocksMovement;
        bool attackBlocked = status != null && status.BlocksAttack;

        UpdateAttackRangeEntry(dist);

        // 아키타입별 이동/교전 분기.
        switch (data.archetype)
        {
            case MonsterArchetype.RangedTurret:
                SeekTurret(dist, attackBlocked);
                break;
            case MonsterArchetype.RangedMobile:
                SeekMobile(dist, movementBlocked, attackBlocked);
                break;
            case MonsterArchetype.Boss:
                SeekBoss(dist, movementBlocked, attackBlocked);
                break;
            default:
                SeekMelee(dist, movementBlocked, attackBlocked);
                break;
        }
    }

    // 보스: 공격이 여러 종류다. 어떤 공격을 쓸지는 SelectAttackSlot(파생이 override)이 정하고,
    // 여기서는 "고를 게 있으면 친다 / 없으면 접근한다"는 이동 정책만 담당한다.
    //
    // 🔴 고를 게 없을 때 제자리에 서 있지 않는 것이 핵심이다.
    //    먼 거리에서 돌진·점프가 전부 쿨이면 걸어서 접근하고, 가까워지면 근접 공격의 거리창이
    //    열리므로 "전부 쿨"이 자연히 풀린다. (사거리 안에서 전부 쿨인 구간은 짧게 지나간다.)
    void SeekBoss(float dist, bool movementBlocked, bool attackBlocked)
    {
        int slot = attackBlocked ? NoAttack : SelectAttackSlot(dist);

        if (slot != NoAttack)
        {
            StopAgent();
            FaceTarget();
            CurrentAttackSlot = slot;
            StartAttack();
            return;
        }

        // 쓸 공격이 없다 — 사거리 밖이면 접근, 안이면 자세만 잡고 쿨을 기다린다.
        if (dist > data.attackRange)
        {
            SetState(MonsterState.Chase);
            if (!movementBlocked)
                ChaseTarget(data.chaseSpeed * ChaseSpeedMultiplier);
        }
        else
        {
            StopAgent();
            if (_state.Value != MonsterState.Chase)
                SetState(MonsterState.Chase);
        }

        FaceTarget();
    }

    /// <summary>
    /// 추격 이동속도 배수(보스 페이즈용). 기본 1 = 변화 없음.
    /// 🔴 <see cref="SeekBoss"/> 분기에서만 곱해진다 — 일반 몬스터 경로(SeekMelee/SeekMobile/SeekTurret)는
    /// 이 값을 거치지 않으므로 기존 8종에 회귀 위험이 없다.
    /// (MoveAgentTo 가 매 틱 agent.speed 를 덮어쓰기 때문에 파생이 agent 를 직접 만져서는 유지되지 않는다.)
    /// </summary>
    protected virtual float ChaseSpeedMultiplier => 1f;

    /// <summary>
    /// 이 거리에서 쓸 공격 슬롯을 고른다. <see cref="NoAttack"/>(-1)이면 "지금 쓸 게 없다"(→ 접근).
    /// 기본 구현은 일반 몬스터와 같은 단일 공격이고, 보스 파생이 거리창+가중치로 override 한다.
    /// </summary>
    protected virtual int SelectAttackSlot(float dist)
    {
        if (dist > data.attackRange) return NoAttack;
        return CooldownReady(DefaultAttackSlot) ? DefaultAttackSlot : NoAttack;
    }

    // 근접: 사거리 안이면 멈춰서 쿨마다 공격, 밖이면 추격.
    void SeekMelee(float dist, bool movementBlocked, bool attackBlocked)
    {
        if (dist <= data.attackRange)
        {
            StopAgent();
            FaceTarget();
            if (!attackBlocked && CooldownReady())
                StartAttack();
            else if (_state.Value != MonsterState.Chase)
                SetState(MonsterState.Chase);
            return;
        }

        SetState(MonsterState.Chase);
        if (!movementBlocked)
            ChaseTarget(data.chaseSpeed);
        FaceTarget();
    }

    // 고정 포탑: 이동 없음. 사거리(attackRange) 안이면 조준·사격, 밖이면 대기.
    //
    // 🔴 조준 예고(2026-09-14 팀장 확정): 쏠 준비가 됐다고 바로 쏘지 않는다.
    //    ITurretAimGate 가 붙어 있으면 조준선을 켜고 잠깐 타깃을 따라간 뒤, 게이트가 열릴 때 쏜다.
    //    게이트가 없으면(컴포넌트 미부착) 예전 동작 그대로다 — 여기서만 갈린다.
    void SeekTurret(float dist, bool attackBlocked)
    {
        StopAgent();
        FaceTarget();

        bool ready = dist <= data.attackRange && !attackBlocked && CooldownReady();

        if (ready && _aimGate != null)
        {
            _aimGate.BeginAiming();
            if (!_aimGate.IsAimReady)
            {
                // 조준 중 — 아직 쏘지 않는다. 상태는 Idle 로 둔다(정지 포즈 유지).
                if (_state.Value != MonsterState.Idle) SetState(MonsterState.Idle);
                return;
            }
        }
        else if (!ready)
        {
            _aimGate?.CancelAiming();
        }

        if (ready)
            StartAttack();
        else if (_state.Value != MonsterState.Idle)
            SetState(MonsterState.Idle);
    }

    // 원거리 이동형(카이팅): minStandoff보다 가까우면 후퇴(리쉬 안에서만), attackRange보다 멀면 접근, 사이면 사격.
    // 쿨다운 대기 중에는(옵션 repositionBetweenAttacks) 제자리에 앉는 대신 타깃 주변 링을 걸어 재배치한다(전투 상태 연출).
    // 후퇴/재배치는 최소 이동 시간(retreat/repositionMinDuration) 동안 "커밋" — 공격·정지를 미루고 계속 걷는다(찔끔 이동 방지).
    void SeekMobile(float dist, bool movementBlocked, bool attackBlocked)
    {
        bool moving = false;
        bool faceMoveDir = false;

        bool committed = _combatMoveCommitted && Time.time < _combatMoveUntil;
        if (movementBlocked)
        {
            _combatMoveCommitted = false;
            committed = false;
        }

        if (!movementBlocked)
        {
            if (committed)
            {
                // 커밋 이동 지속. 목적지 도착 시: 재배치는 다음 지점으로 계속, 후퇴는 커밋 조기 종료.
                committed = DriveCombatMove(dist);
                if (committed)
                {
                    moving = true;
                    faceMoveDir = true;
                }
            }

            if (!committed)
            {
                if (dist < data.minStandoff)
                {
                    // 후퇴: 최소 이동 시간을 채울 만큼 넉넉한 거리로(속도×시간). 리쉬 초과 시 기존 짧은 후퇴로 폴백.
                    Vector3 away = transform.position - _target.position;
                    away.y = 0f;
                    Vector3 dir = away.sqrMagnitude > 0.0001f ? away.normalized : -transform.forward;
                    float retreatDist = Mathf.Max(
                        data.minStandoff - dist + 0.5f,
                        data.chaseSpeed * Mathf.Max(0f, data.retreatMinDuration));
                    Vector3 dest = transform.position + dir * retreatDist;
                    if (Vector3.Distance(dest, _spawnPosition) > data.leashRadius)
                        dest = transform.position + dir * (data.minStandoff - dist + 0.5f);

                    if (Vector3.Distance(dest, _spawnPosition) <= data.leashRadius)
                    {
                        StartCombatMove(dest, data.chaseSpeed, data.retreatMinDuration, repick: false);
                        moving = true;
                        faceMoveDir = true;
                    }
                    else
                    {
                        StopAgent(); // 리쉬에 몰림 → 제자리 사수(리쉬 리셋 방지)
                    }
                }
                else if (dist > data.attackRange)
                {
                    ClearReposition();
                    ChaseTarget(data.chaseSpeed);
                    moving = true;
                }
                else if (data.repositionBetweenAttacks && !CooldownReady())
                {
                    PickRepositionDest(dist);
                    if (_hasRepositionDest)
                    {
                        StartCombatMove(_repositionDest, MoveSpeed, data.repositionMinDuration, repick: true);
                        moving = true;
                        faceMoveDir = true;
                    }
                    else
                    {
                        StopAgent();
                    }
                }
                else
                {
                    ClearReposition();
                    StopAgent();
                }
            }
        }

        // 전투 이동 중엔 이동 방향을 바라본다(타깃을 본 채 게걸음하면 어색). 공격 진입 시 StartAttack이 다시 타깃 스냅.
        if (faceMoveDir)
            FaceVelocity();
        else
            FaceTarget();

        // 커밋 이동 중에는 공격을 미룬다(이동을 최소 시간만큼 완결 → 어색한 찔끔 이동 방지).
        if (!committed && dist <= data.attackRange && !attackBlocked && CooldownReady())
        {
            ClearReposition();
            StartAttack();
            return;
        }
        SetState(moving ? MonsterState.Chase : MonsterState.Idle);
    }

    #region 전투 이동 (후퇴/재배치 최소 시간 커밋 — RangedMobile)
    // 전투 이동 시작: 목적지·속도·최소 지속 시간을 커밋. repick=도착 시 다음 지점 재선택(재배치) 여부.
    void StartCombatMove(Vector3 dest, float speed, float duration, bool repick)
    {
        dest.y = transform.position.y;
        _repositionDest = dest;
        _hasRepositionDest = true;
        _combatMoveSpeed = Mathf.Max(0.1f, speed);
        _combatMoveRepick = repick;
        _combatMoveUntil = Time.time + Mathf.Max(0f, duration);
        _combatMoveCommitted = true;

        // 근거리 목적지가 stoppingDistance(attackRange*0.8)에 먹혀 "이미 도착"으로 무시되지 않게
        // 전투 이동 동안 임시로 낮춘다(ClearReposition에서 복원).
        if (agent != null && agent.enabled)
            agent.stoppingDistance = 0.1f;
        MoveAgentTo(_repositionDest, _combatMoveSpeed);
    }

    // 커밋 이동 1틱 구동. 반환 = 커밋 유지 여부.
    bool DriveCombatMove(float dist)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            _combatMoveCommitted = false;
            return false;
        }

        Vector3 arrivedDelta = transform.position - _repositionDest;
        arrivedDelta.y = 0f; // 타깃/몹의 y 차이로 도착 판정이 영원히 안 잡히는 것 방지
        bool arrived = !_hasRepositionDest || arrivedDelta.sqrMagnitude <= 0.36f; // 0.6m 도착 판정
        if (arrived)
        {
            if (_combatMoveRepick)
            {
                PickRepositionDest(dist); // 재배치: 시간 남았으면 다음 지점으로 계속 걷기
                if (!_hasRepositionDest)
                {
                    _combatMoveCommitted = false;
                    return false;
                }
            }
            else
            {
                _combatMoveCommitted = false; // 후퇴: 목적지 도달 → 커밋 조기 종료
                return false;
            }
        }

        agent.stoppingDistance = 0.1f;
        MoveAgentTo(_repositionDest, _combatMoveSpeed);
        return true;
    }

    void PickRepositionDest(float dist)
    {
        Vector3 toSelf = transform.position - _target.position;
        toSelf.y = 0f;
        if (toSelf.sqrMagnitude < 0.0001f) { _hasRepositionDest = false; return; }

        // 타깃 중심 링(스탠드오프~사거리 사이) 위에서 좌/우 40~100° 떨어진 지점.
        float radius = Mathf.Clamp(dist, data.minStandoff + 0.5f, Mathf.Max(data.minStandoff + 0.5f, data.attackRange - 0.5f));
        float angle = Random.Range(40f, 100f) * (Random.value < 0.5f ? -1f : 1f);
        Vector3 dest = _target.position + (Quaternion.Euler(0f, angle, 0f) * toSelf.normalized) * radius;

        // 리쉬 밖이면 반대쪽 시도, 그래도 밖이면 이번 틱은 포기(다음 틱 재시도).
        if (Vector3.Distance(dest, _spawnPosition) > data.leashRadius)
        {
            dest = _target.position + (Quaternion.Euler(0f, -angle, 0f) * toSelf.normalized) * radius;
            if (Vector3.Distance(dest, _spawnPosition) > data.leashRadius) { _hasRepositionDest = false; return; }
        }

        dest.y = transform.position.y; // 타깃 y(콜라이더 중심 등)와의 높이 차 제거
        _repositionDest = dest;
        _hasRepositionDest = true;
    }

    void ClearReposition()
    {
        _hasRepositionDest = false;
        _combatMoveCommitted = false;
        if (agent != null && agent.enabled)
            agent.stoppingDistance = _defaultStoppingDistance;
    }

    // 이동 방향(에이전트 속도)을 바라본다. 속도가 거의 없으면 타깃을 본다.
    void FaceVelocity()
    {
        Vector3 v = agent != null && agent.enabled ? agent.velocity : Vector3.zero;
        v.y = 0f;
        if (v.sqrMagnitude < 0.04f) { FaceTarget(); return; }
        RotateToward(v.normalized);
    }
    #endregion

    // 타겟 유효성: 존재 + 활성. (리쉬는 몹-스폰 거리로 별도 판정하므로 여기선 거리 제한 없음.)
    // Soul(유령) 플레이어는 활성 상태로 남으므로 null·active 검사만으로는 걸러지지 않는다.
    // 생명주기까지 봐야 사망 직전에 잡힌 타겟이 즉시 풀린다(MonsterTargeting).
    bool IsTargetValid(Transform t) => MonsterTargeting.IsAttackable(t);

    /// <summary>
    /// 지금 어그로가 물린 대상(읽기 전용). 파생이 조준·타겟 규칙에 쓴다.
    /// 🔴 교체는 base 만 한다 — 파생이 직접 대입하면 락온·리쉬 규칙이 두 곳으로 갈린다.
    /// 파생이 갈아타야 할 때 쓰는 진입점은 <see cref="AdoptTarget"/> 하나뿐이다.
    /// </summary>
    protected Transform Target => _target;

    /// <summary>
    /// 현재 타깃의 <b>읽기 전용 공개</b> 접근자. 같은 오브젝트에 붙는 <b>시각 전용</b> 컴포넌트가
    /// 조준 방향을 구하려고 읽는다(<c>TurretHeadAim</c>).
    /// 🔴 쓰기는 열지 않는다 — 타깃 교체는 <see cref="AdoptTarget"/> 하나뿐이라는 규칙을 깨지 않는다.
    /// </summary>
    public Transform CurrentTarget => _target;

    /// <summary>선딜 길이(초). 조준 예고선처럼 <b>시각 전용</b> 요소가 표시 구간을 잡을 때 읽는다.</summary>
    public float AttackWindupSeconds => data != null ? data.attackWindup : 0f;

    /// <summary>사거리(m). 예고선 길이 산출용 — 값이 없으면 0 이다.</summary>
    public float AttackRangeMeters => data != null ? data.attackRange : 0f;

    /// <summary>
    /// 이 몬스터가 <b>몸통을 돌리지 않는</b>가. 고정 터렛(<c>RangedTurret</c>)이 그렇다.
    ///
    /// 🔴 왜(2026-09-14 팀장 확정): 고정 포탑은 자리를 지키는 설계인데 <c>turnSpeed: 10</c> 으로
    /// 몸통 전체가 타깃을 따라 돌고 있었다("몸통이 다 틀어진다"). 조준은 <c>TurretHeadAim</c> 이
    /// <b>머리 본만</b> 돌려서 한다. 발사 방향은 몸통과 무관하다 —
    /// <c>MonsterRangedAttack</c> 은 <c>targetPoint - origin</c> 으로 쏘므로 영향이 없다
    /// (<c>transform.forward</c> 는 타깃이 원점과 겹칠 때의 폴백일 뿐이다).
    /// </summary>
    protected bool BodyRotationLocked =>
        data != null && data.archetype == MonsterArchetype.RangedTurret;

    /// <summary>
    /// 어그로 대상을 <b>지금 이 대상으로 갈아탄다</b>(서버 전용). 파생이 쓰는 유일한 교체 진입점이다.
    ///
    /// 🔴 왜 base 에 있는가 — <c>_target</c> 대입을 한 곳에 모아 락온·리쉬 규칙이 갈리지 않게 한다.
    ///    파생은 "언제 갈아탈지"만 정하고, 유효성 판정(<see cref="IsTargetValid"/> = 사망·유령·디스폰)은
    ///    base 가 갖는다.
    ///
    /// 23호가 <b>점프·돌진</b>에서 쓴다(2026-09-03). 그 둘은 어그로 대상이 아닌 사람을 노리거나
    /// 밀고 가는데, 끝난 뒤 압박이 원래 대상으로 되돌아가면 <b>"멀리 때리고 돌아온다"</b>가 된다.
    ///
    /// ⚠️ 주기 재선정과 달리 <b>여기서는 상태를 보지 않는다.</b> 공격 도중에 불리는 것이 정상이다
    ///    (그 공격이 대상을 고른 순간이 곧 승계 시점이다). 조준이 흔들리지 않는 것은 호출처가
    ///    보장한다 — 23호는 <c>FaceTargetWhileAttacking = false</c> 이고 방향이 이미 확정된 뒤에 부른다.
    /// </summary>
    /// <returns>실제로 갈아탔으면 true. null·같은 대상·무효한 대상이면 아무 일도 하지 않고 false.</returns>
    protected bool AdoptTarget(Transform t)
    {
        if (!IsServer) return false;
        if (t == null || t == _target) return false;
        if (!IsTargetValid(t)) return false;

        _target = t;
        return true;
    }

    /// <summary>
    /// 타겟이 <b>아직 유효한데도</b> 다시 고를 것인가. 기본 <c>false</c> = 지금까지의 락온 동작.
    ///
    /// 여기서 true 를 돌려주면 위 락온 분기가 타깃을 새로 탐색한다 — 파생은 "언제 바꿀지"만
    /// 정하고 "어떻게 고를지"는 base 가 갖는다.
    ///
    /// 기본 구현은 <see cref="MonsterDataSO.retargetInterval"/> 이 <b>0 보다 클 때만</b> 돈다.
    /// 저작 안 한 몹(일반몹 8종 = 0)은 예전처럼 항상 false 다. 23호는 자기 override 로 우선되므로
    /// 이 경로를 타지 않는다.
    /// </summary>
    protected virtual bool ShouldReacquireTarget()
    {
        if (data == null) return false;

        // 🔴 교전 시작 전(_combatStartedAt 미설정)에는 재선정하지 않는다. 0 으로 두면 "아주 오래전"이
        //    되어 첫 타깃을 잡은 그 틱에 곧바로 재선정이 돌아 버린다(교훈 #85 와 같은 뿌리).
        if (_lastRetargetTime < 0f) return false;

        return BossAggroPolicy.ShouldRetarget(
            _state.Value, Time.time - _lastRetargetTime, data.retargetInterval);
    }

    /// <summary>
    /// 주기 재선정에서 <b>지금 물고 있는 대상을 후보에서 뺄</b> 것인가. 기본 <c>false</c>.
    ///
    /// false 면 최근접을 다시 고르므로, 같은 사람이 계속 최근접이면 어그로가 실질적으로 안 돈다
    /// (근접이 탱킹하는 구도에서 그렇다). true 면 8초마다 실제로 다른 사람에게 압박이 간다.
    /// ⚠️ 대신 후열이 과하게 압박받을 수 있다 — MPPM 으로 보고 정할 값이라 노브로 뺐다.
    /// </summary>
    protected virtual bool RetargetAvoidsCurrentTarget => false;

    protected virtual void StartAttack()
    {
        // 쿨은 "지금 쓰는 슬롯"에 기록한다. 파생이 CurrentAttackSlot 을 안 건드리면 항상 0 —
        // 즉 공격이 1종인 몬스터는 단일 쿨다운 시절과 동작이 같다.
        int slot = Mathf.Clamp(CurrentAttackSlot, 0, _lastUsedByAttack.Length - 1);
        _lastUsedByAttack[slot] = Time.time;
        _attackingSlot = slot;   // 종료 기준 쿨이면 Attack 을 빠져나갈 때 이 슬롯을 다시 찍는다(SetState)

        _stateTimer = AttackTime(data.attackDuration);
        _attackFired = false;
        _commitFired = false;
        StopAgent();
        FaceTarget();

        // 공격 중 슈퍼아머(경직 무시) 옵션.
        if (data.hasSuperArmorWhileAttacking && status != null)
            status.ApplyStatus(StatusEffectType.SuperArmor, AttackTime(data.attackDuration));

        SetState(MonsterState.Attack);
    }

    /// <summary>
    /// 공격이 진행되는 <b>동안</b>에도 매 틱 타깃을 향해 몸을 돌리는가.
    ///
    /// 기본 <c>true</c> = 지금까지의 동작(일반 몹 8종·중간보스 3종은 그대로다).
    /// 🔴 23호 보스만 <c>false</c> 다 — 팀장 확정(2026-08-13): <b>공격을 시도 중일 때는 회전이 없다.</b>
    ///    특히 돌진은 플레이어를 밀고 <b>지나가야</b> 하는데, 매 틱 타깃을 향해 돌면 보스가 대상을
    ///    계속 따라 돌아 제자리에서 맴돈다.
    /// 조준은 <see cref="StartAttack"/> 직전의 <c>FaceTarget()</c> 1회로 확정된다.
    /// </summary>
    protected virtual bool FaceTargetWhileAttacking => true;

    /// <summary>
    /// 공격의 <b>선딜 동안만</b>(= 히트 이벤트가 나가기 전까지) 타깃을 향해 계속 도는가.
    ///
    /// 기본 <c>false</c> = 지금까지의 동작. <see cref="FaceTargetWhileAttacking"/> 가 <c>true</c> 면
    /// 이미 매 틱 돌므로 이 값은 아무 일도 하지 않는다 — 즉 <b>몹 8종·중간보스 3종은 무영향</b>이다.
    ///
    /// 🔴 왜 생겼는가 (2026-08-18 팀장 확정): 보스 회전을 감속으로 바꾸면
    /// <see cref="StartAttack"/> 직전의 <c>FaceTarget()</c> <b>1회</b> 조준이 무력해진다 —
    /// 감속 회전은 한 프레임에 몇 도밖에 못 돌기 때문이다. 조준을 즉시 회전으로 남기면 그 순간만
    /// 뚝 끊겨 보이므로, <b>선딜 구간을 조준 구간으로 쓴다.</b> 히트가 나간 뒤부터는 회전이 없다
    /// (<see cref="FaceTargetWhileAttacking"/> 의 확정 스펙 — 돌진은 밀고 지나가야 한다).
    /// </summary>
    protected virtual bool FaceTargetDuringWindup => false;

    #region 공격 예고 (중간보스 — 2026-09-28 팀장: "범위를 보여 준 뒤 공격")
    [Header("공격 예고 (선택 — 중간보스)")]
    [Tooltip("공격 예고 바닥 데칼 재질. 비우면 예고 없음(일반 몹 기본). 23호 표식과 같은 재질을 쓴다\n" +
             "(23호는 자기 BossDirectionIndicator 가 재질을 대므로 여기를 비워 둔다)")]
    [SerializeField] Material attackTelegraphMaterial;

    BossAttackConeTelegraph _attackTelegraph;

    /// <summary>예고를 쓰는 몹인가(재질이 물려 있는가). 파생이 예고 단계를 넣을지 이걸로 가른다.</summary>
    protected bool HasAttackTelegraph => attackTelegraphMaterial != null;

    /// <summary>
    /// [서버] 판정 박스(<paramref name="hitbox"/>) **그대로** 바닥 예고를 띄운다. 채움이 <paramref name="growTime"/>
    /// 동안 차오르고, 다 찬 순간이 곧 공격이다. 🔴 예고와 판정이 같은 콜라이더에서 나오므로 어긋날 수 없다.
    /// </summary>
    /// <param name="lateralShift">몸 기준 옆으로 미는 거리(m, + = 오른쪽). 좌/우 공격은 판정도 <see cref="MeleeHitShifted"/> 에 같은 값을 준다.</param>
    protected void ShowHitboxTelegraph(ColliderInfo hitbox, float growTime, float extraLength = 0f, float lateralShift = 0f)
    {
        if (!IsServer || !HasAttackTelegraph || hitbox == null) return;
        if (!TryMeasureBox(hitbox, out float halfWidth, out float length, out float forward, out float lateral)) return;
        ShowBandTelegraphRpc(halfWidth, length + Mathf.Max(0f, extraLength), forward, lateral + lateralShift,
                             Mathf.Max(0f, growTime));
    }

    /// <summary>
    /// [서버] 근접 판정을 몸 기준 옆으로 <paramref name="lateralShift"/>(m) 옮겨서 한 번 낸다(좌/우 공격).
    /// 판정 박스는 트랜스폼으로 계산되므로(<c>ColliderInfo</c>) 그 순간만 옮겼다가 되돌린다.
    /// 🔴 예고(<see cref="ShowHitboxTelegraph"/>)에 **같은 값**을 줄 것 — 따로 적으면 예고가 판정에 대해 거짓말한다.
    /// </summary>
    protected int MeleeHitShifted(float lateralShift)
    {
        if (meleeAttack == null) return 0;
        ColliderInfo info = meleeAttack.ColliderInfo;
        if (info == null || Mathf.Approximately(lateralShift, 0f)) return meleeAttack.Hit();

        Transform t = info.transform;
        Vector3 original = t.position;
        Vector3 right = transform.right;
        right.y = 0f;
        t.position = original + right.normalized * lateralShift;
        try { return meleeAttack.Hit(); }
        finally { t.position = original; }
    }

    /// <summary>[서버] 예고를 끈다. 공격 상태를 벗어나면 <see cref="OnStateChanged"/> 가 전 피어에서 자동으로 끈다.</summary>
    protected void HideAttackTelegraph()
    {
        if (IsServer && HasAttackTelegraph) HideAttackTelegraphRpc();
    }

    // 박스 판정을 몸 기준 띠(반폭·길이·앞 오프셋·옆 오프셋)로 환산. 판정 박스는 몸과 같은 방향이라고 본다(요 회전 없음).
    bool TryMeasureBox(ColliderInfo hitbox, out float halfWidth, out float length, out float forward, out float lateral)
    {
        halfWidth = length = forward = lateral = 0f;
        BoxCollider box = hitbox.GetComponent<BoxCollider>();
        if (box == null) return false;

        Vector3 size = Vector3.Scale(box.size, box.transform.lossyScale);
        Vector3 local = transform.InverseTransformPoint(box.transform.TransformPoint(box.center));
        halfWidth = Mathf.Abs(size.x) * 0.5f;
        length = Mathf.Abs(size.z);
        forward = local.z - length * 0.5f;
        lateral = local.x;
        return halfWidth > 0f && length > 0f;
    }

    // 예고 표시는 순수 연출이지만 reliable — 빠지면 "예고 없이 맞는" 공격이 된다(이번 작업의 목적 자체가 깨진다).
    [Rpc(SendTo.ClientsAndHost)]
    void ShowBandTelegraphRpc(float halfWidth, float length, float forwardOffset, float lateralOffset, float growTime)
    {
        BossAttackConeTelegraph t = EnsureAttackTelegraph();
        if (t != null) t.Show(0f, 0f, 0f, 0f, halfWidth, length, growTime, forwardOffset, lateralOffset);
    }

    [Rpc(SendTo.ClientsAndHost)]
    void HideAttackTelegraphRpc()
    {
        if (_attackTelegraph != null) _attackTelegraph.Hide();
    }

    BossAttackConeTelegraph EnsureAttackTelegraph()
    {
        if (_attackTelegraph != null) return _attackTelegraph;
        if (attackTelegraphMaterial == null) return null;
        if (!TryGetComponent(out _attackTelegraph))
            _attackTelegraph = gameObject.AddComponent<BossAttackConeTelegraph>();
        _attackTelegraph.MaterialOverride = attackTelegraphMaterial;
        return _attackTelegraph;
    }
    #endregion

    protected virtual void HandleAttack(float dt)
    {
        // 선딜(준비) 중 타깃이 사거리+여유를 벗어나면 공격 취소 → 추격 복귀.
        // (원거리 준비-취소 설계, MortarBot.) 커밋(OnAttackCommit) 또는 히트가 이미 발생했으면 취소하지 않는다
        // — 커밋 후 Strike 재생 중 취소되면 발사가 씹히므로, 취소 창 = 준비(조준) 구간까지만.
        if (data.cancelWindupIfTargetLeavesRange && !_attackFired && !_commitFired)
        {
            float d = _target != null ? Vector3.Distance(transform.position, _target.position) : float.MaxValue;
            if (!IsTargetValid(_target) || d > data.attackRange + AttackRangeExitMargin) // 히스테리시스(경계 깜빡임 방지)
            {
                SetState(MonsterState.Chase); // Attack(액션)→Chase(로코) 전이 → ResetToLocomotion이 애니를 Movement로 복귀시킨다.
                return;
            }
        }

        _stateTimer -= dt;
        // 선딜 조준(FaceTargetDuringWindup)은 히트가 나가기 전까지만이다. _commitFired 까지 보는 것은
        // 위 취소 창과 같은 기준을 쓰기 위해서다 — 커밋한 공격은 이미 방향이 확정된 것으로 본다.
        if (FaceTargetWhileAttacking || (FaceTargetDuringWindup && !_attackFired && !_commitFired))
            FaceTarget();

        // 히트: 애니 OnAttackHit 이벤트(NotifyAttackHit) 전용 — 플레이어(DefaultAttackController)와 동일하게
        // 이벤트가 없으면 데미지가 나가지 않는다(타이머 폴백 제거). FireAttackHitOnce가 중복 발동만 막는다.

        // 종료: 애니 OnAttackEnd 이벤트(NotifyAttackEnd)가 1차. 아래 타이머는 이벤트 유실 시
        // 상태가 Attack에 영구 고착되는 것을 막는 안전망(폴백)일 뿐, attackDuration은 정밀 종료 기준이 아니다.
        if (_stateTimer <= 0f)
            DecideNextAfterAction();
    }

    // 공격 히트를 1회만 실행(이벤트/타이머 어느 쪽이 먼저 부르든 중복 금지).
    protected void FireAttackHitOnce()
    {
        if (_attackFired) return;
        PerformAttackHit();
        // 다단계 공격 2단계 트리거(예: WallBot AttackStart→AttackEnd). 타격 시점에 발동.
        // 단 커밋 이벤트(OnAttackCommit)가 이미 발동한 공격(예: MortarBot)은 재발동하지 않는다(트리거 재래치 방지).
        if (!_commitFired && data != null && !string.IsNullOrEmpty(data.attackFinishTrigger))
        {
            ServerSetFinishTrigger();
            _commitFired = true;
        }
        _attackFired = true;
    }

    // 애니메이션 이벤트(OnAttackHit) 수신 — 타격 프레임에 히트 1회. 타이머 폴백보다 우선.
    // 서버 전용 + Attack 상태에서만 유효(다른 상태에서 오발동 시 무시).
    public virtual void NotifyAttackHit()
    {
        if (!IsServer || _state.Value != MonsterState.Attack) return;
        FireAttackHitOnce();
    }

    // 애니메이션 이벤트(OnAttackEnd) 수신 — 공격 애니 종료 시 상태 이탈. 타이머 폴백보다 우선.
    public virtual void NotifyAttackEnd()
    {
        if (!IsServer || _state.Value != MonsterState.Attack) return;
        DecideNextAfterAction();
    }

    // 애니메이션 이벤트(OnAttackCommit) 수신 — 다단계 공격의 다음 단계 진입 트리거(attackFinishTrigger) 발동.
    // 예: MortarBot 조준루프(AttackLoop) 말미 이벤트 → "Attack" 트리거 → AttackStrike 전이.
    // 루프 클립에선 매 바퀴 발화하므로 _commitFired로 1회만. 서버+Attack 상태에서만 유효.
    public virtual void NotifyAttackCommit()
    {
        if (!IsServer || _state.Value != MonsterState.Attack || _commitFired) return;
        if (data == null || string.IsNullOrEmpty(data.attackFinishTrigger)) return;
        ServerSetFinishTrigger();
        _commitFired = true;
    }

    /// <summary>
    /// [서버] 다단계 공격 2단계 트리거(<c>attackFinishTrigger</c>)를 **전 피어**에 건다.
    /// 🔴 예전엔 서버 로컬 Animator 에만 쳤다 — 몬스터에 NetworkAnimator 가 없어 원격 클라는
    ///    Mortar 조준 루프에 머물고 WallBot 평타 2단이 안 나왔다(10-02 교차검증 공통 지적, 기존 버그).
    /// </summary>
    protected void ServerSetFinishTrigger()
    {
        if (!IsServer || data == null || string.IsNullOrEmpty(data.attackFinishTrigger)) return;
        SafeSetTrigger(data.attackFinishTrigger);   // 서버(호스트) 자신
        if (IsSpawned) SetFinishTriggerRpc();
    }

    [Rpc(SendTo.NotServer)]
    void SetFinishTriggerRpc() => SafeSetTrigger(data != null ? data.attackFinishTrigger : null);

    // 공격 히트 실행(선딜 경과 시점). 아키타입에 따라 근접 오버랩 또는 투사체 발사로 분기.
    protected virtual void PerformAttackHit()
    {
        switch (data.archetype)
        {
            case MonsterArchetype.RangedTurret:
                // 🔴 고정 터렛은 예고선이 가리키던 방향으로 쏜다 — 발사 순간의 플레이어 위치가 아니다.
                //    예전에는 아래 공통 경로를 탔고, 그래서 선은 고정인데 탄만 타깃을 따라가
                //    "피했는데 맞는" 상태가 됐다(2026-09-14 팀장 확인).
                //    게이트가 없으면(예고 기능 미부착) 예전 동작 그대로 간다.
                if (rangedAttack != null && _aimGate != null)
                    rangedAttack.FireDirection(_aimGate.LockedAimDirection, data.attackRange);
                else if (rangedAttack != null && _target != null)
                    rangedAttack.Fire(_target.position + Vector3.up * 0.8f);
                break;

            case MonsterArchetype.RangedMobile:
                if (rangedAttack != null && _target != null)
                    rangedAttack.Fire(_target.position + Vector3.up * 0.8f);
                break;
            default:
                meleeAttack?.Hit();
                break;
        }
    }

    void HandleTimedResume(float dt)
    {
        _stateTimer -= dt;
        if (_stateTimer > 0f) return;

        // ForceHitReaction 이 이어붙인 그로기가 있으면 Idle 대신 그쪽으로 간다(보스 카운터: Hit → Groggy/Break).
        if (_groggyAfterHit > 0f)
        {
            float groggy = _groggyAfterHit;
            _groggyAfterHit = 0f;
            ForceGroggy(groggy);
            return;
        }

        DecideNextAfterAction();
    }

    void HandleGroggy(float dt)
    {
        _stateTimer -= dt;
        if (_stateTimer <= 0f)
        {
            _groggyCount = 0;
            DecideNextAfterAction();
        }
    }

    // 복귀 진입: 즉시 상태 초기화 + 최대 체력 회복(일반 게임식 리쉬 리셋). 이동은 HandleReturn에서 5배속.
    void EnterReturn()
    {
        _target = null;
        _groggyCount = 0;
        status?.ClearAll();   // 버프/디버프 전부 제거
        Revive();             // 즉시 최대 체력 회복
        SetState(MonsterState.Return);
    }

    void HandleReturn()
    {
        // 복귀는 이동속도의 배수로 빠르게(MoveSpeed × returnSpeedMultiplier). 회복/초기화는 EnterReturn에서 이미 완료.
        float returnSpeed = MoveSpeed * Mathf.Max(1f, data.returnSpeedMultiplier);
        MoveAgentTo(_spawnPosition, returnSpeed);

        // 도착 판정: 스폰 지점이 NavMesh에서 벗어나 있으면 transform 거리로는 영원히 도달 못 해
        // Return에 갇힌다. 그래서 에이전트의 remainingDistance(클램프된 실제 도달점까지 거리)로 판정한다.
        bool arrived;
        if (agent != null && agent.enabled && agent.isOnNavMesh)
            arrived = !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.2f;
        else
            arrived = Vector3.Distance(transform.position, _spawnPosition) <= 1.5f;

        if (arrived)
        {
            StopAgent();
            SetState(MonsterState.Idle);
        }
    }

    // 행동(공격/피격/그로기) 종료 후 다음 상태 결정.
    protected void DecideNextAfterAction()
    {
        if (_isDead) return;
        SetState(MonsterState.Idle); // 다음 Tick의 HandleSeekAndCombat가 재평가.
    }
    #endregion

    #region 지속넉백 (PLAN C — AttackInfo 확장 수신측)
    // 공격 수신 단일 진입점 override — 데미지는 base(TakeDamage) 경로 그대로, 넉백 지시만 여기서 추가 해석한다.
    // 방향: 공격이 명시(knockbackDirection)하면 그대로(방향성 공격 — Q 전진 견인 등),
    // 아니면 방사형(몹 - 공격자, 수평) 폴백(장판/폭발형). 방사형은 시전자가 이동하며 대상을
    // 따라잡으면 옆/뒤로 뒤집히므로 이동형 공격에는 쓰지 않는다.
    public override bool ReceiveAttack(AttackInfo attackInfo, AttackHitContext hitContext)
    {
        bool resolved = base.ReceiveAttack(attackInfo, hitContext);

        if (IsServer && resolved && AutoHitReactions
            && attackInfo.knockbackStrength > 0f && attackInfo.knockbackDuration > 0f)
        {
            Vector3 dir = attackInfo.knockbackDirection;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f)
            {
                dir = transform.position - hitContext.sourcePosition;
                dir.y = 0f;
            }
            if (dir.sqrMagnitude < 0.0001f)
            {
                // 공격자와 겹침 — 공격자의 전방(있으면)으로 밀어낸다.
                dir = hitContext.sourceTransform != null ? hitContext.sourceTransform.forward : -transform.forward;
                dir.y = 0f;
            }
            TryEnterKnockback(dir.normalized, attackInfo);
        }

        // 피격 이펙트는 판정이 아니라 연출이다 — 서버는 위치만 알리고 재생은 각 피어가 로컬로 한다.
        // ReceiveAttack은 서버에서만 불리므로(BaseAttack.TryResolveHit의 IsServer 게이트) 여기서
        // 직접 Play하면 호스트에서만 보인다. 거절된 공격(이미 죽은 몹)에는 연출도 없다.
        if (IsServer && resolved)
            PlayHitVFXRpc(hitContext.sourcePosition);

        return resolved;
    }

    // 서버가 보내는 것은 공격자 위치 하나뿐이다. 계산이 끝난 타격점(Pose)을 보내지 않는 이유:
    //
    // 클라이언트의 몹은 NetworkTransform 보간 때문에 서버보다 뒤에 그려진다(TickRate 30 + 보간
    // 버퍼 → 100ms 안팎, 4m/s면 0.3~0.4m = 몸통 반쯤). 서버가 계산한 월드 절대 좌표를 그대로
    // 재생하면 그 차이만큼 이펙트가 몸에서 떨어져 허공에 뜬다. 수신측이 자기 콜라이더로 다시
    // 계산하면 결과는 언제나 그 몹 표면 위다.
    //
    // 반대로 origin(공격자 위치)이 조금 틀리는 것은 무해하다 — origin은 "표면의 어느 쪽을
    // 고를지"만 정하지 이펙트를 몸에서 떼어내지 못한다. 그래서 origin만 서버 값을 쓴다.
    //
    // ⚠️ 호스트는 곧 서버라 이 어긋남이 0이다. 호스트 화면으로는 잘못된 구현도 정상으로 보인다 —
    // 검증은 반드시 MPPM 클라이언트 창에서, 몹이 이동 중일 때 한다.
    //
    // Unreliable: 순수 연출이라 유실돼도 상태가 발산하지 않는다(이펙트 하나가 빠질 뿐).
    [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Unreliable)]
    void PlayHitVFXRpc(Vector3 sourcePosition)
    {
        HitVFXPlayback.Play(this, hitVFXCollider, hitVFXType, sourcePosition);
    }

    // 넉백 진입/갱신. 슈퍼아머·사망·그로기·복귀 중에는 무시(기존 CC 무시 규칙 일관).
    // 이미 넉백 중이면 방향·속도·시간을 갱신한다(Q 홀드처럼 매 틱 재적용되는 지속 견인 대응).
    void TryEnterKnockback(Vector3 dir, AttackInfo attackInfo)
    {
        if (_isDead) return;
        if (status != null && status.BlocksInterrupt) return; // 슈퍼아머
        MonsterState s = _state.Value;
        if (s == MonsterState.Groggy || s == MonsterState.Return || s == MonsterState.Dead) return;

        // 고정 포탑(RangedTurret)은 자리를 지킨다 — 밀림 무효, 경직(Stunned)만 적용(팀장 확정).
        // 매 히트 갱신 = 지속 타격 중 스턴락 허용(일반 피격경직 hitStun도 갱신형이라 일관).
        if (data != null && data.archetype == MonsterArchetype.RangedTurret)
        {
            if (attackInfo.staggerDuration > 0f)
            {
                status?.ApplyStatus(StatusEffectType.Stunned, attackInfo.staggerDuration);
                if (s == MonsterState.Hit)
                {
                    // TakeDamage의 피격경직(hitStun)이 이미 진행 중이면 더 긴 쪽만 유지.
                    _stateTimer = Mathf.Max(_stateTimer, attackInfo.staggerDuration);
                }
                else
                {
                    _stateTimer = attackInfo.staggerDuration;
                    StopAgent();
                    SetState(MonsterState.Hit);
                }
            }
            return;
        }

        _knockbackDir = dir;
        _knockbackSpeed = attackInfo.knockbackStrength;
        _staggerAfterKnockback = attackInfo.staggerDuration;
        _stateTimer = attackInfo.knockbackDuration;

        if (s == MonsterState.Knockback)
            return; // 갱신만 — 이미 agent off + 상태 진입 완료

        // 서버틱 직접 이동과 충돌하지 않게 에이전트를 완전히 내려놓는다(off). 종료 시 재획득.
        ClearReposition();
        StopAgent();
        _knockbackOrigin = transform.position;   // 종료 Warp 가 같은 섬인지 확인하는 기준(ExitKnockback)
        if (agent != null) agent.enabled = false;
        SetState(MonsterState.Knockback);
    }

    // 서버틱 지속 밀기. NavMesh 경계 클램프 — 메시 밖(낭떠러지/벽 뒤)으로는 절대 밀리지 않는다.
    // (넉백으로 오프메시에 떨어지면 에이전트 재획득이 실패해 FSM 전체가 동결되는 것을 원천 차단.)
    void HandleKnockback(float dt)
    {
        _stateTimer -= dt;

        Vector3 next = transform.position + _knockbackDir * (_knockbackSpeed * dt);
        if (NavMesh.SamplePosition(next, out NavMeshHit navHit, 0.5f, NavMesh.AllAreas))
        {
            // 수평 밀기만 반영(y는 유지) — 종료 시 Warp가 메시 높이에 정착시킨다.
            transform.position = new Vector3(navHit.position.x, transform.position.y, navHit.position.z);
        }
        // 샘플 실패 = 경계 도달 → 이번 틱 이동 생략(그 자리에서 밀림 종료 대기)

        if (_stateTimer <= 0f)
            ExitKnockback();
    }

    // 넉백 종료: 에이전트 재획득(on-mesh 보장) → Stunned 경직(staggerDuration) → 기존 Hit 타이머 경로로 재개.
    void ExitKnockback()
    {
        if (agent != null)
        {
            agent.enabled = true;
            // ⚠️ 09-28: 반경 2m → 1m + **같은 섬 확인.** 2m 면 틈 건너 플랫폼·소품 윗면 섬으로 순간이동했다(전수조사 4순위).
            //    넉백 시작 자리(메시 위)에서 직선으로 막힘없이 닿는 점만 받는다. 아니면 시작 자리로 돌려놓는다.
            if (!agent.isOnNavMesh)
            {
                bool placed = false;
                if (NavMesh.SamplePosition(transform.position, out NavMeshHit navHit, 1f, NavMesh.AllAreas) &&
                    NavMesh.SamplePosition(_knockbackOrigin, out NavMeshHit originHit, 1f, NavMesh.AllAreas) &&
                    !NavMesh.Raycast(originHit.position, navHit.position, out _, NavMesh.AllAreas))
                {
                    agent.Warp(navHit.position);
                    placed = true;
                }
                if (!placed && NavMesh.SamplePosition(_knockbackOrigin, out NavMeshHit back, 1f, NavMesh.AllAreas))
                    agent.Warp(back.position);
            }
        }

        if (_staggerAfterKnockback > 0f)
        {
            status?.ApplyStatus(StatusEffectType.Stunned, _staggerAfterKnockback);
            _stateTimer = _staggerAfterKnockback;
            SetState(MonsterState.Hit); // HandleTimedResume이 만료 후 DecideNextAfterAction 호출
        }
        else
        {
            DecideNextAfterAction();
        }
    }
    #endregion

    #region 피격 / 사망 (서버 경로)
    public override void TakeDamage(AttackInfo attackInfo)
    {
        // base가 서버 가드 + 방어/쉴드/체력 + _currentHp 복제 갱신을 수행.
        base.TakeDamage(attackInfo);

        if (!IsServer || _isDead)
            return;

        // 사망 판정 단일 지점.
        if (CurrentHealth <= 0)
        {
            EnterDead();
            return;
        }

        // 자동 피격 반응을 쓰지 않는 파생(보스)은 여기서 끝 — 데미지·사망 판정만 받는다.
        // 🔴 조기 반환이 **먼저**다. 아래 누적을 지나면 보스가 base 그로기까지 이중으로 받는다.
        if (!AutoHitReactions)
            return;

        // 인터럽트 누적 → 그로기. "인터럽트를 어떻게 소비할지"는 수신측 결정이고, 이 계통은 누적식이다
        // (보스 No.23은 같은 플래그를 카운터 창 판정으로 소비한다).
        if (attackInfo.isInterruptAttack && data != null && data.maxGroggyCount > 0)
        {
            _groggyCount++;
            if (_groggyCount >= data.maxGroggyCount)
            {
                EnterGroggy();
                return;
            }
        }

        // 피격 경직: 공격 중 피격 시 공격 취소 + Hit. 단 슈퍼아머면 취소하지 않고 데미지만.
        // 지속넉백 중에는 Hit로 덮지 않는다(밀림 유지 — 데미지만 누적, ReceiveAttack이 넉백을 갱신).
        // 🔴 공격 커밋(2026-09-02): 공격 중 들어온 **평타**는 경직시키지 않는다. 평타 한 대마다
        //    EnterHit 이 걸려 일반 몬스터가 공격을 끝내지 못하던 문제. 판정은 전부
        //    MonsterHitReactionPolicy 에 있다(EditMode 로 전 조합 고정) — 조건을 여기서 늘리지 말 것.
        bool superArmor = status != null && status.BlocksInterrupt;
        if (MonsterHitReactionPolicy.ShouldEnterAutomaticHit(
                _state.Value, attackInfo.attackType, superArmor))
            EnterHit();
    }

    void EnterHit()
    {
        _stateTimer = data != null ? data.hitStunDuration : 0.4f;
        _groggyAfterHit = 0f; // 일반 피격 경직은 그로기로 이어지지 않는다(ForceHitReaction 전용 경로와 분리).
        StopAgent();
        SetState(MonsterState.Hit);
    }

    void EnterGroggy()
    {
        _groggyCount = 0;
        _stateTimer = data != null ? data.groggyDuration : 3f;
        StopAgent();
        SetState(MonsterState.Groggy);
    }

    /// <summary>
    /// 일반 피격이 자동으로 반응을 유발하는가. 기본 true — 기존 몬스터 8종·중간보스 3종은 그대로다.
    ///
    /// 🔴 보스는 false 다. 정본(boss-rebuild-standard.md §1.1 · §4)이 셋 다 부정하기 때문이다:
    ///   ① <c>Hit</c> 는 **카운터 성공 전용** — 일반 피격은 색 변경만(HitFlash)
    ///   ② 그로기는 **인터럽트 스킬·송전기만** 유발 — <c>isInterruptAttack</c> 누적을 쓰지 않는다
    ///   ③ **보스는 안 밀린다** — Knockback 상태를 만들지 않는다
    /// 데미지·사망 판정은 이 값과 무관하게 항상 돈다.
    /// </summary>
    protected virtual bool AutoHitReactions => true;

    /// <summary>
    /// 피격 리액션(<c>Hit</c>)을 강제 진입시킨다 — 진행 중 공격이 취소된다. <see cref="ForceGroggy"/> 의 형제.
    /// <paramref name="groggyAfter"/> 가 0보다 크면 타이머 종료 후 <c>Idle</c> 이 아니라 그 길이만큼
    /// <c>Groggy</c> 로 넘어간다(보스 카운터: Hit → Groggy/Break 가 확정 스펙).
    /// </summary>
    protected void ForceHitReaction(float duration, float groggyAfter = 0f)
    {
        if (!IsServer || _isDead) return;
        _stateTimer = Mathf.Max(0.05f, duration);
        _groggyAfterHit = Mathf.Max(0f, groggyAfter);
        StopAgent();
        SetState(MonsterState.Hit);
    }

    // 서브클래스가 특정 행동 뒤 스스로 그로기(취약)에 빠지게 하는 훅. 예: SpinnerBot 스핀 종료 → Dizzy.
    protected void ForceGroggy(float duration)
    {
        if (!IsServer || _isDead) return;
        _groggyCount = 0;
        _stateTimer = Mathf.Max(0.1f, duration);
        StopAgent();
        SetState(MonsterState.Groggy);
    }

    void EnterDead()
    {
        if (_isDead) return;
        _isDead = true;

        StopAgent();
        if (agent != null) agent.enabled = false;
        if (bodyCollider != null) bodyCollider.enabled = false;

        // 간파 표시도 끈다 — 사망 후 Update 는 돌지 않아 조기 소등 예약이 남는다.
        _counterVisualOffAt = -1f;
        SetCounterVisual(false);

        SetState(MonsterState.Dead);

        // 드롭/보상 확장 훅 — 사망 단일 지점에서만 호출(은희가 채움).
        OnDeath();

        // 세션 통계(처치 수)용 통보. 구독자가 없으면 아무 일도 하지 않는다.
        MonsterDeathEvents.RaiseServerMonsterDied(this);

        // 디졸브 연출이 있으면 **사망 클립이 끝난 뒤** 재생 → 디스폰, 없으면 지연 후 디스폰.
        // (2026-09-28 팀장: 죽는 애니가 끝나고 나서 디졸브. 예전엔 클립 시작과 동시에 녹아 끝까지 못 봤다.)
        IDeathEffect fx = GetComponent<IDeathEffect>();
        if (fx != null)
            StartCoroutine(PlayDeathEffectAfterClip(fx));
        else
            StartCoroutine(DespawnAfter(data != null ? data.despawnDelay : 2f));
    }

    /// <summary>
    /// [서버] 사망 연출이 끝나 곧 디스폰되는 순간(디졸브·지연 모두 끝). 보스 격파 후 결과 화면 전환이 여기에 맞춘다
    /// — <c>Died</c> 는 치명타 순간이라 그 기준 고정 타이머는 연출 길이가 바뀌면 어긋난다.
    /// </summary>
    public event System.Action ServerDeathSequenceCompleted;

    // 사망 상태 진입을 기다리는 한도. AnyState→사망 전이는 0.05초라 몇 프레임이면 잡힌다.
    const float DeathStateDetectTimeout = 0.5f;
    // 루프 클립·잘못 저작된 긴 클립이 디졸브를 영원히 막지 않게.
    const float MaxDeathClipHold = 5f;

    IEnumerator PlayDeathEffectAfterClip(IDeathEffect fx)
    {
        float hold = 0f;
        // 클립 없는 몹(deathTrigger 파라미터 없음)은 PlayStateAnimation 이 애니를 얼린다 → 기다릴 게 없다.
        if (animator != null && data != null && HasParameter(animator, data.deathTrigger))
        {
            // 🔴 길이는 **실제 사망 상태에서 잰다** — 클립 이름·상태 이름이 몹마다 다르다(Death / Defeat).
            //    트리거는 SetState 에서 이미 걸렸으므로 지금 상태는 아직 사망 전 상태다.
            int before = animator.GetCurrentAnimatorStateInfo(0).fullPathHash;
            float waited = 0f;
            while (waited < DeathStateDetectTimeout)
            {
                yield return null;
                waited += Time.deltaTime;
                AnimatorStateInfo st;
                if (animator.IsInTransition(0))
                    st = animator.GetNextAnimatorStateInfo(0);
                else
                    st = animator.GetCurrentAnimatorStateInfo(0);
                if (st.fullPathHash == before) continue;

                // length 는 상태 속도가 반영된 초. 이미 흐른 만큼 뺀다. 루프 클립은 한 바퀴로 친다.
                float remain = st.length * (1f - Mathf.Clamp01(st.normalizedTime));
                hold = Mathf.Max(0f, Mathf.Min(remain, MaxDeathClipHold) - fx.LeadBeforeClipEnd) + fx.DelayAfterClipEnd;
                // 사망은 몹당 한 번뿐인 사건이라 로그가 넘치지 않는다. "끝났는데 안 녹는다"를 숫자로 가리는 용도.
                Debug.Log($"[Death] {name}: 사망 클립 남은 {remain:0.00}초 − 앞당김 {fx.LeadBeforeClipEnd:0.00}초 " +
                          $"+ 끝난 뒤 대기 {fx.DelayAfterClipEnd:0.00}초 → {hold:0.00}초 뒤 디졸브", this);
                break;
            }
        }

        if (hold > 0f)
            yield return new WaitForSeconds(hold);
        fx.Play(DespawnNow);
    }

    // 드롭 아이템/보상/처치 카운트 등 사망 후처리 확장점(기본 no-op).
    protected virtual void OnDeath() { }

    IEnumerator DespawnAfter(float delay)
    {
        yield return new WaitForSeconds(Mathf.Max(0f, delay));
        DespawnNow();
    }

    void DespawnNow()
    {
        if (!IsServer) return;
        ServerDeathSequenceCompleted?.Invoke();
        NetworkObject netObj = NetworkObject;
        if (netObj != null && netObj.IsSpawned)
            netObj.Despawn();
    }
    #endregion

    #region 유틸
    // 공격 슬롯의 쿨다운이 돌았는가.
    // 슬롯 쿨(_cooldownByAttack)이 0 이하면 base 간격 = data.attackCooldown(초)으로 폴백한다.
    // (AttackSpeed 는 2026-10-02 부터 공격 애니 재생 배율이라 간격과 무관하다.)
    // 인자를 안 주면 슬롯 0 — 공격이 1종인 일반 몬스터는 이 경로만 탄다.
    protected bool CooldownReady(int attackSlot = DefaultAttackSlot)
    {
        if (attackSlot < 0 || attackSlot >= _lastUsedByAttack.Length)
            attackSlot = DefaultAttackSlot;

        return Time.time - _lastUsedByAttack[attackSlot] >= EffectiveCooldown(attackSlot);
    }

    // 슬롯의 실제 쿨 길이. 저작값이 0 이하면 base 간격(data.attackCooldown)으로 폴백한다.
    float EffectiveCooldown(int attackSlot)
    {
        float cooldown = _cooldownByAttack[attackSlot];
        return cooldown > 0f ? cooldown : Mathf.Max(0f, data.attackCooldown);
    }

    // 사거리 진입/이탈을 추적하고, **진입한 프레임에** 첫 공격 지연을 건다.
    //
    // 왜 조우(타깃 획득)가 아니라 진입인가 — 인지(10m)에서 사거리(2.5m)까지 걸어오는 데
    // 이미 2초 넘게 걸린다. 조우 시점에 걸면 도착할 때쯤 지연이 다 지나 있어 아무 효과가 없다.
    // 팀장이 본 것은 "사거리에 발 들이는 프레임에 때린다"이므로 기준점도 그 프레임이어야 한다.
    void UpdateAttackRangeEntry(float dist)
    {
        bool nowInRange = MonsterEngagePolicy.IsInAttackRange(
            _inAttackRange, dist, data.attackRange, AttackRangeExitMargin);

        if (nowInRange && !_inAttackRange)
            DelayFirstAttack(data.engageAttackDelay);

        _inAttackRange = nowInRange;
    }

    /// <summary>
    /// 사거리 진입 직후 첫 공격을 <paramref name="delaySeconds"/> 만큼 늦춘다(모든 슬롯).
    ///
    /// 새 게이트 없이 쿨다운 도장을 되감아 구현한다 — 규칙과 이유는
    /// <see cref="MonsterEngagePolicy"/> 참조.
    ///
    /// 🔴 나중에 <see cref="SetAttackCooldown"/> 로 쿨 길이를 바꾸면 남은 지연도 함께 변한다.
    ///    사거리 진입 시 한 번 걸고 잊는 용도로만 쓸 것.
    /// </summary>
    protected void DelayFirstAttack(float delaySeconds)
    {
        if (!MonsterEngagePolicy.ShouldDelay(delaySeconds)) return;

        for (int i = 0; i < _lastUsedByAttack.Length; i++)
            _lastUsedByAttack[i] = MonsterEngagePolicy.FirstAttackStamp(
                Time.time, EffectiveCooldown(i), delaySeconds);
    }

    /// <summary>
    /// 공격 슬롯 수를 확보한다. 보스처럼 공격이 여러 종류인 파생이 스폰 시 1회 호출한다.
    /// 호출하지 않으면 슬롯 1개(=단일 공격)로 남고 기존 동작과 동일하다.
    /// </summary>
    protected void ConfigureAttackSlots(int count)
    {
        count = Mathf.Max(1, count);
        if (_lastUsedByAttack.Length == count) return;

        _lastUsedByAttack = new float[count];
        _cooldownByAttack = new float[count];
        for (int i = 0; i < count; i++)
            _lastUsedByAttack[i] = -999f;   // 스폰 직후 첫 공격이 쿨에 걸리지 않게
    }

    /// <summary>슬롯별 쿨 길이(초)를 설정한다. 0 이하면 base 간격(data.attackCooldown)을 쓴다.</summary>
    /// <summary>
    /// 슬롯을 "방금 썼다"고 표시해 쿨다운을 지금부터 돌린다.
    ///
    /// 실제로 공격하지 않고 <b>쿨만 거는</b> 용도다 — 특정 공격을 일정 시간 후보에서 빼고 싶을 때
    /// 새 게이트를 만드는 대신 기존 쿨다운 기계를 재사용한다. 시간이 지나면 스스로 풀리므로
    /// 해제를 잊어 상태가 고착될 위험이 없다(23호 개전 억제가 이 경로를 쓴다).
    /// </summary>
    protected void MarkAttackJustUsed(int attackSlot)
    {
        if (attackSlot < 0 || attackSlot >= _lastUsedByAttack.Length) return;
        _lastUsedByAttack[attackSlot] = Time.time;
    }

    protected void SetAttackCooldown(int attackSlot, float seconds)
    {
        if (attackSlot < 0 || attackSlot >= _cooldownByAttack.Length) return;
        _cooldownByAttack[attackSlot] = Mathf.Max(0f, seconds);
    }

    // 인지 반경 내 최근접 플레이어 Transform 탐색(서버 전용).
    /// <param name="exclude">
    /// 후보에서 제외할 대상(보통 지금 물고 있는 타깃). null 이면 제외 없음.
    /// 주기 어그로 재선정에서 "같은 사람을 다시 고르는 것"을 막는 데 쓴다.
    /// </param>
    Transform FindNearestTarget(Transform exclude = null)
    {
        if (_detectBuffer == null) return null;

        int count = Physics.OverlapSphereNonAlloc(
            transform.position, data.detectionRadius, _detectBuffer, playerMask, QueryTriggerInteraction.Collide);

        Transform nearest = null;
        float best = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            Collider c = _detectBuffer[i];
            if (c == null) continue;

            // 유령은 인지 대상이 아니다 — 레이어 마스크만으로는 못 막는다(Soul 전환은 루트 레이어만
            // 바꾸고 자식 콜라이더는 그대로 남는다).
            if (!MonsterTargeting.IsAttackable(c)) continue;

            // 🔴 타깃 기준은 **Player 트랜스폼**이다(2026-09-18). 예전에는 `transform.root` 였는데,
            //    플레이어가 무언가의 자식이면 **그 부모의 피벗**을 쿫게 돼 보스가 엉뚱한 데로 간다.
            //    또 AdoptAggro·최원거리 탐색은 `Player.transform` 을 써서 기준이 갈라져 있었다 —
            //    exclude 비교가 어긋나는 원인이기도 했다. 한 곳으로 맞춘다.
            Player owner = c.GetComponentInParent<Player>();
            Transform root = owner != null ? owner.transform : c.transform.root;

            // 제외 대상은 건너뛴다. 루트로 비교하는 이유 — exclude 로 넘어오는 _target 도
            // 루트라서, 콜라이더 트랜스폼과 직접 비교하면 자식 콜라이더에서 안 걸린다.
            if (exclude != null && root == exclude) continue;

            // 층 분리 — 수직 차가 크면 인지하지 않는다(위층 몹이 아래를 때리던 문제).
            // 규칙과 한계는 MonsterPerceptionPolicy 참조. 데이터 값이 0 이면 예전처럼 높이를 무시한다.
            if (!MonsterPerceptionPolicy.WithinHeight(
                    transform.position.y, root.position.y, data.detectionHeightTolerance))
                continue;

            float sqr = (root.position - transform.position).sqrMagnitude;
            if (sqr < best)
            {
                best = sqr;
                nearest = root;
            }
        }
        return nearest;
    }

    /// <summary>몸이 큰 몹은 true — 데이터의 회피 반경(0.3)으로 프리팹 에이전트 반경을 덮지 않는다.</summary>
    protected virtual bool KeepPrefabAgentRadius => false;

    Vector3 _knockbackOrigin;

    // 추격 목적지를 NavMesh 위로 투영하는 반경. 플레이어가 가장자리 띠·소품 위에 있어도 그 아래 메시를 잡는 정도.
    const float ChaseProjectRadius = 2f;

    /// <summary>
    /// 추격 이동(보스·근접·이동형 공통). 🔴 목적지를 플레이어 좌표 그대로 주면, 플레이어가 메시 밖(가장자리 띠·상자 위·
    /// 끊긴 계단 너머)에 있을 때 **모든 몹이 가장 가까운 메시 점 — 낭떠러지·벽 가장자리의 같은 점으로 몰린다**
    /// (09-28 NavMesh 전수조사 1순위). 그래서 ① 목적지를 메시 위로 투영하고 ② 경로가 끝까지 닿지 않으면(Partial·Invalid)
    /// 가장자리로 몰려가지 않고 **그 자리에서 기다린다.** 복귀·재배치는 이 규칙을 타지 않는다(MoveAgentTo 직접).
    /// </summary>
    // ── 주기 상수 (PLAN-cleanup-optimization S2) ────────────────────────────────────
    const float AcquireRetryInterval = 0.2f;   // 빈손 재탐색 간격
    const float AcquireRetryJitter = 0.05f;
    const float ChaseRepathInterval = 0.2f;    // 추격 목적지 재계산 간격
    const float ChaseRepathJitter = 0.05f;
    const float ChaseRepathTargetMoveSq = 0.5f * 0.5f;   // 타깃이 이만큼 움직이면 주기 전이라도 즉시

    float _nextAcquireTime;
    float _chaseNextRepath;         // 다음 정기 재계산 시각
    float _chaseRetryAt;            // 경로 실패(메시 밖·Partial)로 멈춘 뒤 재시도 시각
    Vector3 _chaseLastTargetPos;
    Vector3 _chaseLastGoal;

    void ChaseTarget(float speed)
    {
        if (_target == null || agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        // 🔴 매 프레임 SamplePosition + SetDestination 하던 것을 주기로(S2-1). 단 아래면 즉시 다시 잡는다:
        //    타깃이 0.5m 넘게 움직임 · 다른 로직이 세웠거나(isStopped — 공격·재배치 뒤) 목적지를 바꿈.
        //    경로 실패로 **내가** 세운 경우는 재시도 간격을 지킨다(실패 상태에서 매 프레임 재시도하지 않게).
        Vector3 targetPos = _target.position;
        bool stoppedByOther = agent.isStopped && Time.time >= _chaseRetryAt;
        // 허용 오차 0.5m — destination 은 경로 계산 뒤 메시 위 점으로 살짝 보정될 수 있다(오차로 매 프레임 "바뀜" 판정 방지).
        bool goalChanged = !agent.isStopped && (agent.destination - _chaseLastGoal).sqrMagnitude > 0.25f;
        bool due = stoppedByOther || goalChanged
                   || (Time.time >= _chaseNextRepath && Time.time >= _chaseRetryAt)
                   || (targetPos - _chaseLastTargetPos).sqrMagnitude > ChaseRepathTargetMoveSq && Time.time >= _chaseRetryAt;

        if (!due)
        {
            if (!agent.isStopped) agent.speed = speed;   // 보스 페이즈 배수 등 속도는 매 틱 반영(MoveAgentTo 와 같게)
        }
        else
        {
            _chaseLastTargetPos = targetPos;
            _chaseNextRepath = Time.time + ChaseRepathInterval + Random.Range(0f, ChaseRepathJitter);

            if (!NavMesh.SamplePosition(targetPos, out NavMeshHit onMesh, ChaseProjectRadius, NavMesh.AllAreas))
            {
                StopAgent();
                _chaseRetryAt = Time.time + ChaseRepathInterval;
                return;
            }

            MoveAgentTo(onMesh.position, speed);
            _chaseLastGoal = agent.destination;
        }

        if (!agent.pathPending && !agent.isStopped && agent.pathStatus != NavMeshPathStatus.PathComplete)
        {
            StopAgent();
            _chaseRetryAt = Time.time + ChaseRepathInterval;
        }
    }

    void MoveAgentTo(Vector3 destination, float speed)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
            return;
        agent.isStopped = false;
        agent.speed = speed;
        agent.avoidancePriority = MoveAvoidancePriority; // 이동 중엔 기본 우선순위
        agent.SetDestination(destination);
    }

    void StopAgent()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
            return;
        agent.isStopped = true;
        agent.ResetPath();
        // isStopped/ResetPath는 속도를 즉시 0으로 만들지 않는다 — acceleration에 따라 감속하며
        // 몇 프레임 더 관성으로 미끄러진다("글라이딩"). 그래서 공격 진입(StartAttack) 직후에도
        // 몸이 앞으로 밀려 "이동하면서 때리는" 것처럼 보인다. 공격/피격/그로기 중 완전 정지를
        // 보장하기 위해 속도를 즉시 0으로 만든다.
        agent.velocity = Vector3.zero;
        // 정지 중엔 회피 우선순위를 높여(값↓) 이동 중인 다른 몹에게 밀려나지 않게 한다(공격 중 제자리 유지).
        agent.avoidancePriority = HoldAvoidancePriority;
    }

    protected void FaceTarget()
    {
        if (_target == null) return;
        Vector3 dir = _target.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        RotateToward(dir);
    }

    /// <summary>
    /// 타깃 방향으로 <b>한 프레임에</b> 스냅한다(<c>data.turnSpeed</c> 무시).
    ///
    /// 🔴 왜 따로 있는가: 감속 회전은 <b>매 틱 불러야</b> 목표에 도달한다. 그런데 회전 직후
    /// <c>transform.forward</c> 를 그대로 소비해 방향을 확정하는 자리가 있다(레이지 돌진 =
    /// <c>BeginRageDash</c>). 거기서 감속을 쓰면 그 프레임의 어중간한 각도가 돌진 방향으로
    /// 굳어 버린다. <b>"돌아본 뒤 그 방향을 즉시 쓰는" 자리에서만</b> 이걸 쓴다.
    /// </summary>
    protected void FaceTargetImmediate()
    {
        if (BodyRotationLocked) return;   // 고정 터렛 — 머리만 돈다(TurretHeadAim)
        if (_target == null) return;
        Vector3 dir = _target.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.LookRotation(dir);
    }

    /// <summary>
    /// 몬스터 회전의 <b>단일 지점</b>. <c>data.turnSpeed</c> 가 0 이면 기존처럼 즉시 스냅하고,
    /// >0 이면 플레이어(<c>PlayerMovement</c>)와 같은 규약으로 감속한다.
    ///
    /// 🔴 도달 클램프(<c>Dot &gt; 0.999f</c>)가 필요한 이유: Slerp 는 목표에 <b>점근</b>할 뿐
    /// 도달하지 않는다. 클램프가 없으면 거의 맞춘 상태에서 매 프레임 미세하게 계속 돌아
    /// 회전이 "끝났다"고 말할 수 있는 시점이 생기지 않는다(플레이어도 같은 처리를 한다).
    /// </summary>
    void RotateToward(Vector3 dir)
    {
        if (BodyRotationLocked) return;   // 고정 터렛 — 머리만 돈다(TurretHeadAim)

        Quaternion target = Quaternion.LookRotation(dir);

        float turnSpeed = data != null ? data.turnSpeed : 0f;
        if (turnSpeed <= 0f)
        {
            transform.rotation = target; // 0 = 즉시 회전(기존 동작)
            return;
        }

        if (Vector3.Dot(dir.normalized, transform.forward) > 0.999f)
        {
            transform.rotation = target;
            return;
        }

        transform.rotation = Quaternion.Slerp(transform.rotation, target, turnSpeed * Time.deltaTime);
    }

    void SetState(MonsterState next)
    {
        if (!IsServer) return;

        // 종료 기준 쿨 — Attack 을 **어느 경로로든** 빠져나가는 순간부터 센다(정상 종료·피격·그로기·리쉬).
        // 상태 쓰기는 여기 한 곳뿐이라 경로별로 찍을 필요가 없다.
        if (CooldownFromAttackEnd && _state.Value == MonsterState.Attack && next != MonsterState.Attack
            && _attackingSlot >= 0 && _attackingSlot < _lastUsedByAttack.Length)
            _lastUsedByAttack[_attackingSlot] = Time.time;

        _state.Value = next; // 같은 값이면 NGO가 콜백을 발생시키지 않음.
    }

    int _attackingSlot = DefaultAttackSlot;

    /// <summary>
    /// 쿨다운을 공격이 <b>끝난</b> 시점부터 세는가. 기본 true — <c>attackCooldown</c> = 공격 후 쉬는 시간이라
    /// 공격 길이(attackSpeed)를 바꿔도 쉬는 시간이 그대로다(팀장 10-02).
    /// 🔴 23호는 false — 행별 쿨은 시작 기준으로 튜닝돼 있다(PLAN 범위 밖).
    /// </summary>
    protected virtual bool CooldownFromAttackEnd => true;
    #endregion

    #region 애니메이션(상태→Animator 매핑 단일 지점)
    void OnStateChanged(MonsterState previous, MonsterState next)
    {
        // 자세를 붙잡는 상태는 Groggy 하나뿐이다(그로기 클립이 없는 몹의 표현).
        // 그 밖으로 나가면 무조건 푼다 — 해제를 개별 경로에 맡기면 하나만 빠져도 몹이 영구히 얼어붙는다.
        // 🔴 대기 중인 "클립 끝에서 잡기" 예약도 함께 취소한다. 홀드만 풀면 예약이 살아남아
        //    상태를 빠져나간 뒤에 뒤늦게 애니메이터를 얼려 버린다.
        if (next != MonsterState.Groggy)
            ApplyReleaseActionPose();

        // 블렌드 고정·준비는 CrossFade(ResetToLocomotion)·트리거(PlayStateAnimation)보다 먼저 정한다.
        UpdateLocomotionBlendOnStateChange(previous, next);

        // 액션(공격/피격/그로기)에서 이동계열(대기/추격/복귀)로 전이 시, 진행 중이던 액션 클립을
        // 끊고 로코모션으로 강제 복귀. (공격 도중 리쉬 복귀 등으로 애니가 공격 클립에 눌러앉는 문제 해결.)
        if (IsActionAnimState(previous) && IsLocomotionAnimState(next))
            ResetToLocomotion();

        // 🔴 예고 안전망 — 공격이 그로기·피격·사망·리쉬로 끊기면 "다 찼으니 끄라"는 신호가 안 온다.
        //    이 콜백은 _state 복제를 타고 전 피어에서 돌므로 여기서 끄면 어느 경로로 빠져도 남지 않는다.
        if (next != MonsterState.Attack && _attackTelegraph != null)
            _attackTelegraph.Hide();

        PlayStateAnimation(next);

        OnMonsterStateChanged(previous, next);
    }

    /// <summary>
    /// 상태 전이 <b>직후</b> 파생이 자기 런타임을 정리할 훅. 전 피어에서 불린다(서버 가드는 파생 몫).
    ///
    /// 🔴 왜 필요한가 (2026-09-08 WallBot 실측): 파생이 <see cref="HandleAttack"/> 안에서만 도는
    ///    다단 시퀀스를 들고 있으면, <c>Attack</c> 밖으로 튕기는 <b>다른</b> 경로(피격 경직 · 넉백 ·
    ///    리쉬 복귀 · 사망)에서 그 시퀀스가 <b>정리되지 않은 채 얼어붙는다</b> — 히트 윈도우가 열린
    ///    채 남고, 카운터 창이 안 닫혀 이후 근접 판정이 영구히 막힌다. 이탈 경로마다 정리를 심는
    ///    대신 전이 지점 하나에서 받는다(해제를 개별 경로에 맡기지 않는다 = 자세 홀드와 같은 원칙).
    /// </summary>
    protected virtual void OnMonsterStateChanged(MonsterState previous, MonsterState next) { }

    static bool IsActionAnimState(MonsterState s) =>
        s == MonsterState.Attack || s == MonsterState.Hit || s == MonsterState.Groggy
        || s == MonsterState.Knockback;

    static bool IsLocomotionAnimState(MonsterState s) =>
        s == MonsterState.Idle || s == MonsterState.Chase || s == MonsterState.Return;

    // 진행 중이던 액션 트리거를 지우고 이동(로코모션) 상태로 CrossFade — 액션 클립 눌러앉음 방지.
    void ResetToLocomotion()
    {
        if (animator == null || data == null) return;
        SafeResetTrigger(data.attackTrigger);
        SafeResetTrigger(data.hitTrigger);
        SafeResetTrigger(data.attackFinishTrigger);   // 🔴 래치 이유는 PlayStateAnimation 의 Attack 주석 참조
        SafeCrossFade(data.locomotionState);
    }

    void SafeResetTrigger(string param)
    {
        if (HasParameter(animator, param)) animator.ResetTrigger(param);
    }

    #region 카운터 창 표현 · 그로기 자세 정지 — 중간보스 전용 진입점
    // 🔴 애니메이터는 복제되지 않는다. 서버가 진실의 원천이고 아래 RPC 들은 표현만 옮긴다.
    //    정지/재개는 **멱등**이어야 한다 — 풀 때 0 을 복원하면 몹이 영구히 얼어붙는다. 그래서 걸 때만
    //    현재 속도를 저장하고(_animatorHeldLocally 가 그 래치), 풀 때 저장본을 되돌린다.
    //    (23호 SetCounterPoseHeldClientRpc 선례를 그대로 옮긴 것이다.)
    //
    // ⚠️ **창 동안에는 애니를 멈추지 않는다**(2026-09-08 구현 중 확정). 23호는 준비 자세를 붙잡지만
    //    중간보스는 예비동작 클립이 창을 이미 덮는다 — Spinner `AttackStart` 45프레임 = 1.5초(창과 동일,
    //    이어지는 `AttackLoop` 는 루프) · Gauntlet `Smash Anticipation` 60프레임 = 2.0초 > 창 1.5초.
    //    돌고 있는 스피너를 얼리면 오히려 고장난 것처럼 보이고, 피어별 정지 프레임 차이도 생긴다.
    //    자세 정지는 **그로기 클립이 없는 몹의 그로기 표현**에만 쓴다(Gauntlet).

    /// <summary>카운터 창 표현(텔레그래프)을 켜고 끈다. 서버에서 부른다.</summary>
    /// <summary>
    /// [서버] 간파 창 **표시**를 켜고 끈다. 판정은 <c>MonsterCounterWindow</c> 가 따로 한다.
    /// </summary>
    /// <param name="windowDuration">
    /// 열 때 넘기면 판정 창이 닫히기 <see cref="CounterVisualLeadSeconds"/> 전에 표시가 먼저 꺼진다.
    /// 0 이하 = 조기 소등 없음(닫을 때 끈다).
    /// </param>
    protected void ServerSetCounterWindow(bool open, float windowDuration = -1f)
    {
        if (!IsServer) return;

        _counterVisualOffAt = open && windowDuration > 0f
            // 🔴 여유(0.15초)는 배율로 나누지 않는다 — 몬스터가 아니라 **플레이어 간파 스킬의 판정 지연**(hitDelay)을
            //    보상하는 값이다. 나누면 공격속도 2 에서 "보일 때 눌렀는데 실패"가 생긴다(10-03 Codex 교차검증, 계획 뒤집음).
            ? Time.time + Mathf.Max(0f, windowDuration - CounterVisualLeadSeconds)
            : -1f;
        SetCounterVisual(open);
    }

    void SetCounterVisual(bool open)
    {
        if (IsSpawned)
        {
            if (_counterVisual.Value != open) _counterVisual.Value = open;   // 호스트도 OnValueChanged 로 받는다
        }
        else
        {
            ApplyCounterWindowVisual(open);   // 네트워크 없는 테스트 씬
        }
    }

    void OnCounterVisualChanged(bool previous, bool next) => ApplyCounterWindowVisual(next);

    /// <summary>
    /// 로코모션 첫 프레임 자세로 갈아탄 뒤 정지한다 — <b>그로기 클립이 없는 몹</b>의 그로기 표현.
    /// 서버에서 부른다. 해제는 상태 이탈이 알아서 한다(<see cref="OnStateChanged"/>).
    /// </summary>
    protected void ServerFreezeAtLocomotion()
    {
        if (!IsServer) return;

        ApplyLocomotionFreeze();
        if (IsSpawned) FreezeAtLocomotionClientRpc();
    }

    /// <summary>
    /// 지금 재생 중인 액션 클립이 <b>한 바퀴를 마치면</b> 그 마지막 자세에서 정지시킨다.
    /// 서버에서 부르면 전 피어가 <b>각자의 애니메이터 시간</b>으로 판정한다(지연에 안 흔들린다).
    /// 해제는 <see cref="ServerReleaseActionPose"/> 또는 Groggy 밖으로의 상태 이탈이 한다.
    ///
    /// 🔴 왜 "정지 전이가 없으니 마지막 프레임에서 알아서 멈춘다"에 기대면 안 되는가 (2026-09-08):
    ///    <c>A_WallBot_AttackStart</c> 는 <c>loopTime: 1</c> 이라 25프레임(≈0.8초)을 <b>계속 반복한다.</b>
    ///    클립 안의 <c>OnAttackHit</c> 이 루프마다 다시 발화해 2단 트리거까지 쳐 버렸다.
    ///    <b>클립이 멈추는지는 전이가 아니라 임포터의 loopTime 이 정한다</b> — 컨트롤러만 보고 단정하지 말 것.
    /// </summary>
    /// <param name="stateName">붙잡을 애니메이터 상태 이름. 다른 상태로 이미 넘어갔으면 잡지 않는다.</param>
    protected void ServerHoldActionPoseAtClipEnd(string stateName)
    {
        if (!IsServer) return;

        ApplyHoldAtClipEnd(stateName);
        if (IsSpawned) HoldActionPoseClientRpc(stateName);
    }

    /// <summary>붙잡아 둔 자세를 푼다(대기 중인 예약도 취소). 멱등이다.</summary>
    protected void ServerReleaseActionPose()
    {
        if (!IsServer) return;

        ApplyReleaseActionPose();
        if (IsSpawned) ReleaseActionPoseClientRpc();
    }

    [ClientRpc] void FreezeAtLocomotionClientRpc() => ApplyLocomotionFreeze();
    [ClientRpc] void HoldActionPoseClientRpc(string stateName) => ApplyHoldAtClipEnd(stateName);
    [ClientRpc] void ReleaseActionPoseClientRpc() => ApplyReleaseActionPose();

    // 클립 끝을 기다렸다 잡는다. 루프 클립은 normalizedTime 이 1 을 넘겨 계속 자라므로 임계값을
    // 1 직전(0.98)에 두고, 잡는 순간 그 시점으로 Play 해 **피어마다 같은 프레임**에 고정한다.
    // (1.0 을 기다리면 이미 다음 바퀴 초반을 한 프레임 그린 뒤라 시작 자세에서 얼어붙는다.)
    const float ActionPoseHoldNormalizedTime = 0.98f;
    const float ActionPoseHoldTimeout = 3f;   // 이 안에 대상 상태에 못 들어가면 포기하고 경고

    void ApplyHoldAtClipEnd(string stateName)
    {
        if (animator == null || string.IsNullOrEmpty(stateName)) return;

        ApplyReleaseActionPose();
        _actionPoseHoldRoutine = StartCoroutine(HoldAtClipEndRoutine(stateName));
    }

    void ApplyReleaseActionPose()
    {
        if (_actionPoseHoldRoutine != null)
        {
            StopCoroutine(_actionPoseHoldRoutine);
            _actionPoseHoldRoutine = null;
        }
        ApplyAnimatorHold(false);
    }

    IEnumerator HoldAtClipEndRoutine(string stateName)
    {
        int hash = Animator.StringToHash(stateName);

        // 전이 중이면 아직 그 상태가 아니다 — 들어올 때까지 기다린다.
        // 🔴 무한 대기 금지: 상태 이름이 틀렸거나 컨트롤러가 바뀌면 조용히 영원히 돈다. 못 잡으면
        //    자세를 안 잡은 채로 경고를 남기고 빠진다(안 잡히는 것보다 모르는 게 나쁘다).
        float deadline = Time.time + ActionPoseHoldTimeout;
        bool caught = false;
        while (Time.time < deadline)
        {
            if (animator == null || animator.runtimeAnimatorController == null) yield break;

            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
            if (info.shortNameHash == hash && info.normalizedTime >= ActionPoseHoldNormalizedTime)
            {
                caught = true;
                break;
            }
            yield return null;
        }

        if (!caught)
        {
            Debug.LogWarning(
                $"{name}: 애니메이터 상태 '{stateName}' 를 {ActionPoseHoldTimeout:0.#}초 안에 못 잡아 자세 홀드를 건너뛴다. " +
                "상태 **이름**(트리거 이름이 아니다)이 컨트롤러와 맞는지 확인할 것.", this);
            _actionPoseHoldRoutine = null;
            yield break;
        }

        animator.Play(hash, 0, ActionPoseHoldNormalizedTime);
        ApplyAnimatorHold(true);
        _actionPoseHoldRoutine = null;
    }

    void ApplyCounterWindowVisual(bool open)
    {
        if (_telegraph == null) _telegraph = GetComponentInChildren<IBossTelegraph>(true);
        _telegraph?.SetCounterWindow(open);
    }

    // 🔴 CrossFade 가 아니라 Play 다. 속도 0 에서는 블렌드가 진행되지 않아 CrossFade 로는
    //    자세가 바뀌지 않는다(창 홀드 → 그로기 전환에서 Smash 자세에 눌러앉는다).
    //    이동 블렌드 파라미터도 0 으로 눌러 대기 자세를 집는다 — 걷던 프레임에서 얼지 않게.
    void ApplyLocomotionFreeze()
    {
        if (animator == null || data == null) return;

        ApplyAnimatorHold(false);
        SafeSetFloat(data.animSpeedParam, 0f);
        SafeResetTrigger(data.attackTrigger);
        SafeResetTrigger(data.hitTrigger);

        int hash = Animator.StringToHash(data.locomotionState);
        if (animator.runtimeAnimatorController != null && animator.HasState(0, hash))
            animator.Play(hash, 0, 0f);

        ApplyAnimatorHold(true);
    }

    void ApplyAnimatorHold(bool held)
    {
        if (animator == null) return;

        if (held)
        {
            if (_animatorHeldLocally) return;   // 이미 잡고 있다 — 0 을 저장하는 사고를 막는다
            _animatorResumeSpeed = animator.speed;
            animator.speed = 0f;
            _animatorHeldLocally = true;
            return;
        }

        if (!_animatorHeldLocally) return;
        // 관리 대상이면 홀드 전 값이 아니라 **지금** 맞는 값으로 푼다(그사이 상태가 바뀌었을 수 있다).
        animator.speed = ManagesAnimatorSpeed ? ManagedAnimatorSpeed() : _animatorResumeSpeed;
        _animatorHeldLocally = false;
    }

    /// <summary>
    /// <c>animator.speed</c> 를 <see cref="MonsterAnimSpeedPolicy"/> 로 매 프레임 정하는가. 기본 true.
    /// 🔴 23호는 false — 잡기·점프·카운터가 자체적으로 animator.speed 를 쓴다(PLAN 범위 밖).
    /// </summary>
    protected virtual bool ManagesAnimatorSpeed => true;

    /// <summary>
    /// 공격 중 <b>코드 타이머</b>(예고·지속·창·돌진 시간)를 몬스터 고유 공격속도로 나눈다.
    /// 공격 애니가 attackSpeed 배로 재생되므로 타이머도 같이 줄어야 화면과 판정이 맞는다(PLAN S3).
    /// 🔴 공격 중 시간값을 새로 쓸 때 이걸 빠뜨리면 그 공격만 배율 ≠ 1 에서 어긋난다.
    /// </summary>
    protected float AttackTime(float seconds) => seconds / AttackAnimSpeed;

    /// <summary>공격 중 속도·가속 배수(= attackSpeed). 돌진 시간 ÷ 와 짝 — 거리가 그대로다.</summary>
    protected float AttackRate => AttackAnimSpeed;

    /// <summary>
    /// 몬스터 고유 공격속도(공격 애니 재생 배율) — <b>SO 값을 직접 읽는다.</b>
    /// 🔴 <c>Unit.AttackSpeed</c> 를 쓰면 안 된다: <c>Initialize</c> 가 서버에서만 불리고(ServerInitialize)
    ///    그 필드는 복제되지 않아 원격 클라에선 0 이다 → 클라 화면의 공격 애니가 0.05배로 멈춘다.
    ///    SO 는 전 피어가 같은 에셋을 가지므로 복제 없이 같은 값이 나온다.
    /// </summary>
    public float AttackAnimSpeed => data != null ? Mathf.Max(0.05f, data.attackSpeed) : 1f;

    float _baseAgentAcceleration = 8f;

    /// <summary>
    /// [서버] 공격 돌진용 에이전트 속도·가속을 건다 — 속도 = 기준 × r, <b>가속 = 기준 × r²</b> (r = attackSpeed).
    /// 같은 궤적을 시간만 1/r 로 압축하는 조건이다(x(t) → x(r·t) 를 두 번 미분하면 r²).
    /// 🔴 r 만 곱하면 가속 구간 거리 ½aT² 가 1/r 로 줄어든다 — 가속 8 로는 최고속에 못 닿아 이 구간이 거리의 전부다
    ///    (10-02 교차검증 Codex·Claude 공통 지적, 처음 계획은 ×r 이었다).
    /// 끝나면 <see cref="RestoreAttackDashAgent"/> 로 되돌린다.
    /// </summary>
    protected void ApplyAttackDashAgent(float baseSpeed)
    {
        if (agent == null) return;
        agent.speed = Mathf.Max(0.1f, baseSpeed * AttackRate);
        agent.acceleration = _baseAgentAcceleration * AttackRate * AttackRate;
    }

    /// <summary>
    /// [서버] 정지 → 가속(기준 가속·기준 최고속, 배율 1 기준)으로 <paramref name="seconds"/> 동안 가는 거리.
    /// 배율 r 에선 속도 ×r · 가속 ×r² · 시간 ÷r 이라 같은 값이 나온다 — 그래서 배율 없는 저작값으로 계산한다.
    /// 예고 길이를 실제 도달 거리에 맞추는 데 쓴다(SpinnerBot — 팀장 10-02: 예고를 실제에 맞춤).
    /// </summary>
    protected float AttackDashReach(float baseSpeed, float seconds)
    {
        float a = Mathf.Max(0.01f, _baseAgentAcceleration);
        float v = Mathf.Max(0.1f, baseSpeed);
        float tReach = v / a;                       // 최고속에 닿는 시각
        return seconds <= tReach
            ? 0.5f * a * seconds * seconds          // 끝까지 가속 중
            : v * seconds - v * v / (2f * a);       // 가속 뒤 등속
    }

    /// <summary>[서버] 돌진 속도·가속을 평소 값(MoveSpeed · 프리팹 가속)으로 되돌린다. 멱등.</summary>
    protected void RestoreAttackDashAgent()
    {
        if (agent == null) return;
        agent.speed = MoveSpeed;
        agent.acceleration = _baseAgentAcceleration;
    }

    int _locomotionHash;
    string _locomotionHashOf;

    // 지금 재생 중인 상태(전이 중이면 향하는 상태) 기준으로 재생 속도를 고른다.
    float ManagedAnimatorSpeed()
    {
        if (data == null || animator == null || animator.runtimeAnimatorController == null)
            return 1f;

        if (!ReferenceEquals(_locomotionHashOf, data.locomotionState))
        {
            _locomotionHashOf = data.locomotionState;
            _locomotionHash = Animator.StringToHash(data.locomotionState ?? "");
        }

        bool playingLocomotion = PlayingStateHash(0) == _locomotionHash && !UpperLayerActing();

        return MonsterAnimSpeedPolicy.Resolve(
            playingLocomotion, _animSpeed.Value, State == MonsterState.Attack, AttackAnimSpeed, LocomotionData);
    }

    // 블렌드 값과 재생 속도가 **같은** 측정값을 보게 한 곳에서 만든다(data != null 전제).
    MonsterAnimSpeedPolicy.Locomotion LocomotionData => new MonsterAnimSpeedPolicy.Locomotion
    {
        clipSpeed = data.locomotionClipSpeed,
        fullBlendSpeed = data.locomotionFullBlendSpeed,
        idleCycle = data.locomotionIdleCycleSeconds,
        moveCycle = data.locomotionMoveCycleSeconds,
        range = data.locomotionAnimSpeedRange,
        fullBlendWhileMoving = data.locomotionFullBlendWhileMoving,
        playbackScale = data.locomotionPlaybackScale,
    };

    int PlayingStateHash(int layer) => animator.IsInTransition(layer)
        ? animator.GetNextAnimatorStateInfo(layer).shortNameHash
        : animator.GetCurrentAnimatorStateInfo(layer).shortNameHash;

    // 레이어 1 이상의 "평소 상태" 해시 — 공격이 아닐 때 처음 본 상태로 기억한다(컨트롤러 무수정).
    int[] _layerRestHash;

    /// <summary>
    /// 0 번이 아닌 레이어가 평소 상태가 아닌 애니를 재생 중인가.
    /// 🔴 Peek·Tesla 터렛은 0 번 레이어가 늘 Idle(= locomotionState)이고 사격은 1 번 레이어다 —
    ///    0 번만 보면 사격 중에도 "이동 블렌드 재생 중"으로 읽혀 공격속도가 안 걸린다(Codex 교차검증 10-02).
    /// </summary>
    bool UpperLayerActing()
    {
        int count = animator.layerCount;
        if (count <= 1) return false;

        if (_layerRestHash == null || _layerRestHash.Length != count)
            _layerRestHash = new int[count];

        bool acting = false;
        for (int i = 1; i < count; i++)
        {
            if (_layerRestHash[i] == 0)
            {
                // 평소 상태 = 스폰 직후 이 레이어의 **현재** 상태(= 컨트롤러 기본 상태). 갓 스폰된 Animator 는
                // 레이어마다 기본 상태에서 시작하고, 공격 트리거로 전이 중이어도 '현재'는 아직 기본 상태다.
                // 🔴 로직 상태(State)로 거르지 않는다 — 공격 도중 합류한 클라가 영영 못 기억하거나(Codex),
                //    로직은 Attack 을 벗어났는데 사격 클립이 남은 순간을 평소로 오인했다(Claude) — 10-03 교차검증.
                int current = animator.GetCurrentAnimatorStateInfo(i).shortNameHash;
                if (current != 0) _layerRestHash[i] = current;
                continue;
            }
            int hash = PlayingStateHash(i);
            if (hash != 0 && hash != _layerRestHash[i] && animator.GetLayerWeight(i) > 0f) acting = true;
        }
        return acting;
    }
    #endregion

    protected void SafeCrossFade(string stateName)
    {
        if (animator == null || animator.runtimeAnimatorController == null || string.IsNullOrEmpty(stateName))
            return;
        int hash = Animator.StringToHash(stateName);
        if (animator.HasState(0, hash))
            animator.CrossFadeInFixedTime(hash, 0.1f);
    }

    // 상태 진입 시 애니메이션 재생. 파라미터가 없으면 graceful(예외 없음).
    protected virtual void PlayStateAnimation(MonsterState s)
    {
        if (animator == null || data == null) return;

        SafeSetBool(data.groggyBool, s == MonsterState.Groggy);

        switch (s)
        {
            case MonsterState.Attack:
                // 🔴 이전 공격이 남긴 2단 트리거를 **먼저 지운다.** Unity 트리거는 소비할 전이가
                //    없으면 사라지지 않고 래치된다 — WallBot 실측(2026-09-08): 충격파에서 친
                //    `AttackEnd` 가 갈 곳이 없어 남았고, 다음 공격이 `AttackStart` 에 들어간 바로
                //    그 프레임에 래치가 `AttackStart→AttackEnd`(전이시간 0)를 발동시켜 모으기
                //    자세가 0프레임이 됐다. 코드는 그동안 카운터 창을 열고 Gather 를 돌아
                //    "애니와 피격 처리가 전혀 안 맞는" 상태가 됐다.
                SafeResetTrigger(data.attackFinishTrigger);
                SafeSetTrigger(data.attackTrigger);
                break;
            case MonsterState.Hit:
                SafeSetTrigger(data.hitTrigger);
                break;
            case MonsterState.Knockback:
                SafeSetTrigger(data.hitTrigger); // 밀리는 동안 피격 리액션 재생(전용 클립 없음 — Hit 공용)
                break;
            case MonsterState.Dead:
                SafeSetTrigger(data.deathTrigger);
                // 임시 사망 표시(모델 축소 + 빨강 틴트)는 DissolveDeath 도입으로 역할이 끝났다.
                // 둘 다 디졸브와 충돌한다 — 축소는 디졸브 중인 메쉬를 함께 줄여버리고,
                // 틴트는 MaterialPropertyBlock으로 _BaseColor를 덮어써 디졸브 머티리얼까지 빨개진다.
                // 보스도 이 자리를 타므로 (BossBase가 MonsterBase로 통합됐다) 끄는 곳은 여기 한 곳이다.
                // 되살릴 일이 있을 수 있어 메서드 본체는 남겨 둔다(PlayDeathPlaceholder).
                //PlayDeathPlaceholder();

                // 🔴 사망 클립이 **없는 몹이 대부분이다** — 12종 중 7종이 `deathTrigger` 미저작
                //    (ChompBot·HumanoidBot·MortarBot·PeekABot·SpinnerBot·TeslaBot·WallBot).
                //    그대로 두면 죽는 동안 걷기·공격 애니가 계속 돌면서 디졸브된다(2026-09-09 Play 판정).
                //    클립이 있는 몹(Gauntlet `Defeat` · 23호/원거리 `Death`)은 그 클립이 끝까지 돌아야
                //    하므로 건드리지 않는다 — **파라미터 유무로 가른다.**
                //    ⚠️ 파라미터가 있어도 컨트롤러에 전이가 없으면 여전히 안 멈춘다(클립 존재 ≠ 상태 배치).
                //       그 경우는 감사 도구가 죽은 이름으로 잡아 준다.
                if (!HasParameter(animator, data.deathTrigger))
                    ApplyAnimatorHold(true);
                break;
        }
    }

    protected void SafeSetTrigger(string param)
    {
        if (HasParameter(animator, param)) animator.SetTrigger(param);
    }

    protected void SafeSetBool(string param, bool value)
    {
        if (HasParameter(animator, param)) animator.SetBool(param, value);
    }

    void SafeSetFloat(string param, float value)
    {
        if (HasParameter(animator, param)) animator.SetFloat(param, value);
    }

    /// <summary>이동 블렌드 감쇠 시간(초) — 평소 출발·정지를 부드럽게. 전이 구간은 아래 고정·준비가 맡는다.</summary>
    const float LocomotionBlendDamp = 0.12f;

    // ── 이동 블렌드 전이 포즈 제어 (팀장 10-06 MortarBot "앉는 프레임", Codex 설계 회의 반영) ──────────────
    // 블렌드 값(RunBlend)은 Movement 블렌드 트리 **안의** 대기↔이동 비중이다. 전이 중에도 출발/도착 쪽 Movement 가
    // 이 값으로 그려지므로, 전이 순간 값이 0 이면 그 사이에 대기 자세(Mortar = 접어 앉기)가 보인다.
    // 🔴 _animSpeed(복제 실제 속도)는 건드리지 않는다 — 발맞춤 재생 속도 계산도 같은 값을 쓴다.
    //    판단은 전 피어에 복제되는 로직 상태 전이(OnStateChanged)로만 한다(원격엔 NavMeshAgent 가 없다).

    /// <summary>진입 고정 상한(초). Cast 전이(0.15초)보다 넉넉히 — 전이가 끝나면 그 전에 풀린다.</summary>
    const float AttackEntryBlendHoldMax = 0.3f;
    /// <summary>복귀 준비 시간(초). 에이전트 가속(정지→걷기 ≈0.22초)을 덮는다.</summary>
    const float ActionExitBlendPrimeSeconds = 0.25f;

    float _blendHoldUntil = -1f;     // 이 시각까지 + Movement 에서 빠져나가는 중이면 블렌드 값을 고정
    int _blendHoldStartFrame = -1;   // 트리거가 아직 소비 전(전이 시작 전)인 첫 프레임들도 고정
    float _blendPrimeUntil = -1f;    // 이 시각까지 블렌드를 걷기 값 이상으로, 감쇠 없이

    void WriteLocomotionBlend()
    {
        if (animator == null || string.IsNullOrEmpty(data.animSpeedParam)) return;

        // ① 진입 고정: 이동 → 공격 전이 동안 출발 포즈를 그대로 둔다.
        //    GauntletBot 처럼 공격 클립 없이 Movement 에 머무는 몹은 전이가 없어 1~2 프레임만 고정된다.
        if (Time.time < _blendHoldUntil)
        {
            bool leavingLocomotion = animator.IsInTransition(0)
                && animator.GetCurrentAnimatorStateInfo(0).shortNameHash == LocomotionHash;
            if (leavingLocomotion || Time.frameCount <= _blendHoldStartFrame + 1)
                return;
            _blendHoldUntil = -1f;
        }

        float target = MonsterAnimSpeedPolicy.BlendParam(_animSpeed.Value, LocomotionData);

        // ② 복귀 준비: 공격이 끝나고 바로 걷는 몹은 처음부터 걷기 자세로 들어간다.
        if (Time.time < _blendPrimeUntil)
        {
            if (HasParameter(animator, data.animSpeedParam))
                animator.SetFloat(data.animSpeedParam, Mathf.Max(target, data.locomotionFullBlendSpeed));
            return;
        }

        SafeSetFloatDamped(data.animSpeedParam, target, LocomotionBlendDamp);
    }

    /// <summary>전 피어. <see cref="OnStateChanged"/> 에서 액션 CrossFade·트리거보다 <b>먼저</b> 부른다.</summary>
    void UpdateLocomotionBlendOnStateChange(MonsterState previous, MonsterState next)
    {
        if (data == null) return;

        // 공격뿐 아니라 피격·넉백·그로기 진입도 같다(10-06 실측: 걷다 맞으면 Hit 전이 후반이 대기 자세).
        if (IsActionAnimState(next) && IsLocomotionAnimState(previous))
        {
            _blendHoldUntil = Time.time + AttackEntryBlendHoldMax;
            _blendHoldStartFrame = Time.frameCount;
            _blendPrimeUntil = -1f;
            return;
        }

        // 🔴 원거리 이동형만 — SeekMobile 은 "실제로 걸을 때만" Chase 를 쓰고 공격 쿨 동안 거의 항상 재배치로 걷는다.
        //    근접(SeekMelee)은 사거리 안에서 서서 쿨을 기다리며 Chase 를 쓰므로 여기서 준비하면 제자리 걸음이 된다.
        //    공격 뒤 실제로 안 걸으면 준비 시간이 끝나는 대로 평소 감쇠로 대기에 내려간다.
        if (data.archetype == MonsterArchetype.RangedMobile
            && previous == MonsterState.Attack && IsLocomotionAnimState(next) && next != MonsterState.Return)
        {
            _blendHoldUntil = -1f;
            _blendPrimeUntil = Time.time + ActionExitBlendPrimeSeconds;
            if (animator != null && HasParameter(animator, data.animSpeedParam))
                animator.SetFloat(data.animSpeedParam, data.locomotionFullBlendSpeed);   // CrossFade 보다 먼저
            return;
        }

        if (!IsLocomotionAnimState(next))
        {
            _blendHoldUntil = -1f;
            _blendPrimeUntil = -1f;
        }
    }

    int LocomotionHash
    {
        get
        {
            if (!ReferenceEquals(_locomotionHashOf, data.locomotionState))
            {
                _locomotionHashOf = data.locomotionState;
                _locomotionHash = Animator.StringToHash(data.locomotionState ?? "");
            }
            return _locomotionHash;
        }
    }

    // 매 프레임 불러야 감쇠가 진행된다(Animator.SetFloat damp 규약). 강제 정지(자세 고정 등)는 SafeSetFloat 로 즉시 쓴다.
    void SafeSetFloatDamped(string param, float value, float dampTime)
    {
        if (HasParameter(animator, param)) animator.SetFloat(param, value, dampTime, Time.deltaTime);
    }

    // 🔴 `Animator.parameters` 는 호출마다 배열을 새로 할당한다 — Update 에서 매 프레임 불리므로(몬스터 수 × 피어 수)
    //    컨트롤러별로 이름 해시 집합을 한 번만 만든다. 키가 **현재 컨트롤러**라 교체(OnNetworkSpawn 의
    //    animatorControllerOverride 등)가 어느 경로로 일어나도 다음 호출에서 다시 만든다.
    //    파라미터가 0개로 읽히면(초기화 전) 캐시를 확정하지 않고 다음에 다시 읽는다 — 예전 동작과 같게.
    readonly HashSet<int> _animParamHashes = new HashSet<int>();
    RuntimeAnimatorController _animParamCacheFor;

    bool HasParameter(Animator anim, string param)
    {
        if (anim == null || string.IsNullOrEmpty(param)) return false;
        RuntimeAnimatorController rac = anim.runtimeAnimatorController;
        if (rac == null) return false;

        if (!ReferenceEquals(rac, _animParamCacheFor))
        {
            _animParamHashes.Clear();
            AnimatorControllerParameter[] ps = anim.parameters;
            for (int i = 0; i < ps.Length; i++)
                _animParamHashes.Add(ps[i].nameHash);
            _animParamCacheFor = ps.Length > 0 ? rac : null;
        }
        return _animParamHashes.Contains(Animator.StringToHash(param));
    }

    // 임시 사망 표시: 디졸브 셰이더/Death 애니 도입 전, 각 피어에서 로컬로 재생.
    // 모델 자식을 축소하고 렌더러를 빨강으로 틴트한다(루트는 NetworkTransform이 관여하므로 건드리지 않음).
    void PlayDeathPlaceholder()
    {
        if (_deathFxRoutine != null) return;
        Transform model = animator != null ? animator.transform : null;
        if (model == null) return; // 스케일할 모델이 없으면 연출 생략(디스폰은 서버가 처리).
        _deathFxRoutine = StartCoroutine(DeathPlaceholderRoutine(model));
    }

    IEnumerator DeathPlaceholderRoutine(Transform model)
    {
        Renderer[] rends = model.GetComponentsInChildren<Renderer>(true);
        MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        Vector3 startScale = model.localScale;
        float dur = Mathf.Max(0.1f, DeathPlaceholderDuration);
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            model.localScale = Vector3.Lerp(startScale, startScale * 0.05f, k);
            ApplyDeathTint(rends, mpb, Color.Lerp(Color.white, Color.red, k));
            yield return null;
        }
        model.localScale = startScale * 0.05f;
    }

    static void ApplyDeathTint(Renderer[] rends, MaterialPropertyBlock mpb, Color color)
    {
        for (int i = 0; i < rends.Length; i++)
        {
            Renderer r = rends[i];
            if (r == null || r.sharedMaterial == null) continue;
            r.GetPropertyBlock(mpb);
            if (r.sharedMaterial.HasProperty("_BaseColor")) mpb.SetColor("_BaseColor", color);
            else if (r.sharedMaterial.HasProperty("_Color")) mpb.SetColor("_Color", color);
            r.SetPropertyBlock(mpb);
        }
    }
    #endregion

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (data == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, data.detectionRadius);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, data.attackRange);
        Gizmos.color = Color.cyan;
        Vector3 origin = Application.isPlaying ? _spawnPosition : transform.position;
        Gizmos.DrawWireSphere(origin, data.leashRadius);
    }
#endif
}
