using System.Collections;
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

    [Header("컴포넌트 참조(비우면 자동 탐색)")]
    [SerializeField] protected NavMeshAgent agent;
    [SerializeField] protected Animator animator;
    [SerializeField] protected MonsterStatusEffect status;
    [SerializeField] protected MonsterMeleeAttack meleeAttack;
    [SerializeField] protected MonsterRangedAttack rangedAttack;

    [Header("타게팅")]
    [SerializeField] protected LayerMask playerMask;         // 인지 대상(플레이어) 레이어
    [SerializeField] protected int maxDetectionResults = 16;

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

    // 서버 전용 런타임 상태
    Vector3 _spawnPosition;
    Quaternion _spawnRotation;
    Transform _target;
    Collider[] _detectBuffer;

    // 공격 슬롯별 쿨다운.
    // 일반 몬스터는 공격이 1종이라 슬롯 0 하나만 쓰고, 그 경우 동작은 단일 쿨다운 시절과 완전히 같다.
    // 보스처럼 공격이 여러 종류면 ConfigureAttackSlots(n)으로 슬롯을 늘리고 슬롯마다 쿨을 따로 돌린다.
    float[] _lastUsedByAttack = { -999f };
    float[] _cooldownByAttack = { 0f };   // 0 이하면 base 쿨(1/AttackSpeed)로 폴백

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
    bool _serverLogicSuspended;    // 연출 구간 게이트(SetServerLogicSuspended). true 면 서버 FSM 이 안 돈다
    Coroutine _deathFxRoutine;              // 임시 사망 표시 코루틴(모든 피어)
    const float DeathPlaceholderDuration = 1f; // 임시 사망 표시 지속(디졸브/애니 도입 시 제거)

    // NavMeshAgent 회피 우선순위(낮을수록 우선 = 남이 비켜감). 정지(공격/피격 등) 중인 몹이
    // 이동 중인 다른 몹에게 밀려나지 않도록 정지 시 우선순위를 높인다(값을 낮춘다).
    const int HoldAvoidancePriority = 20;
    const int MoveAvoidancePriority = 50;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // 참조 자동 보강(인스펙터 미할당 대비).
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (status == null) status = GetComponent<MonsterStatusEffect>();
        if (meleeAttack == null) meleeAttack = GetComponentInChildren<MonsterMeleeAttack>();
        if (rangedAttack == null) rangedAttack = GetComponentInChildren<MonsterRangedAttack>();

        // 공격 애니 이벤트 릴레이 자동 부착(Animator 오브젝트에 — 이벤트는 같은 GO의 메서드만 호출 가능).
        // 🔴 부착이 곧 동작은 아니다. OnAttackHit 은 **폴백이 없어서**, 클립에 그 이벤트가 없으면
        //    그 공격은 데미지를 내지 못한다(OnAttackEnd 만 attackDuration 타이머가 폴백한다).
        //    비대칭인 이유는 HandleAttack 주석 참조.
        if (animator != null && !animator.TryGetComponent(out MonsterAnimationEventRelay _))
            animator.gameObject.AddComponent<MonsterAnimationEventRelay>();

        _state.OnValueChanged += OnStateChanged;

        if (IsServer)
        {
            ServerInitialize();
        }
        else
        {
            // 클라는 이동 권한 없음 — NavMeshAgent 비활성(NetworkTransform 복제로 위치 반영).
            if (agent != null) agent.enabled = false;
        }

        // 스폰 시점의 상태를 즉시 애니메이션에 반영(뒤늦게 접속한 클라 포함).
        PlayStateAnimation(_state.Value);
    }

    public override void OnNetworkDespawn()
    {
        _state.OnValueChanged -= OnStateChanged;
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
            agent.stoppingDistance = Mathf.Max(0f, data.attackRange * 0.8f);
            _defaultStoppingDistance = agent.stoppingDistance;
            // 부분 겹침: 회피 반경을 콜라이더보다 작게(데이터값). 물리 대신 회피로만 겹침량 조절(저비용).
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
        SafeSetFloat(data != null ? data.animSpeedParam : null, _animSpeed.Value);

        if (!IsServer || !_initialized || _isDead)
            return;

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
    void TickServer(float dt)
    {
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

        Edit.Log($"[Monster] {name} 서버 FSM {(suspended ? "정지" : "재개")} — 연출 구간 게이트", this);
    }

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
        if (!IsTargetValid(_target))
            _target = FindNearestTarget();

        if (_target == null)
        {
            StopAgent();
            SetState(MonsterState.Idle);
            return;
        }

        float dist = Vector3.Distance(transform.position, _target.position);
        bool movementBlocked = status != null && status.BlocksMovement;
        bool attackBlocked = status != null && status.BlocksAttack;

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
                MoveAgentTo(_target.position, data.chaseSpeed * ChaseSpeedMultiplier);
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
            MoveAgentTo(_target.position, data.chaseSpeed);
        FaceTarget();
    }

    // 고정 포탑: 이동 없음. 사거리(attackRange) 안이면 조준·사격, 밖이면 대기.
    void SeekTurret(float dist, bool attackBlocked)
    {
        StopAgent();
        FaceTarget();
        if (dist <= data.attackRange && !attackBlocked && CooldownReady())
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
                    MoveAgentTo(_target.position, data.chaseSpeed);
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

    protected virtual void StartAttack()
    {
        // 쿨은 "지금 쓰는 슬롯"에 기록한다. 파생이 CurrentAttackSlot 을 안 건드리면 항상 0 —
        // 즉 공격이 1종인 몬스터는 단일 쿨다운 시절과 동작이 같다.
        int slot = Mathf.Clamp(CurrentAttackSlot, 0, _lastUsedByAttack.Length - 1);
        _lastUsedByAttack[slot] = Time.time;

        _stateTimer = data.attackDuration;
        _attackFired = false;
        _commitFired = false;
        StopAgent();
        FaceTarget();

        // 공격 중 슈퍼아머(경직 무시) 옵션.
        if (data.hasSuperArmorWhileAttacking && status != null)
            status.ApplyStatus(StatusEffectType.SuperArmor, data.attackDuration);

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

    protected virtual void HandleAttack(float dt)
    {
        // 선딜(준비) 중 타깃이 사거리+여유를 벗어나면 공격 취소 → 추격 복귀.
        // (원거리 준비-취소 설계, MortarBot.) 커밋(OnAttackCommit) 또는 히트가 이미 발생했으면 취소하지 않는다
        // — 커밋 후 Strike 재생 중 취소되면 발사가 씹히므로, 취소 창 = 준비(조준) 구간까지만.
        if (data.cancelWindupIfTargetLeavesRange && !_attackFired && !_commitFired)
        {
            float d = _target != null ? Vector3.Distance(transform.position, _target.position) : float.MaxValue;
            if (!IsTargetValid(_target) || d > data.attackRange + 0.5f) // +0.5 히스테리시스(경계 깜빡임 방지)
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
            SafeSetTrigger(data.attackFinishTrigger);
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
        SafeSetTrigger(data.attackFinishTrigger);
        _commitFired = true;
    }

    // 공격 히트 실행(선딜 경과 시점). 아키타입에 따라 근접 오버랩 또는 투사체 발사로 분기.
    protected virtual void PerformAttackHit()
    {
        switch (data.archetype)
        {
            case MonsterArchetype.RangedTurret:
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
        // 직접 Play하면 호스트에서만 보인다.
        if (IsServer)
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
            if (!agent.isOnNavMesh &&
                NavMesh.SamplePosition(transform.position, out NavMeshHit navHit, 2f, NavMesh.AllAreas))
                agent.Warp(navHit.position);
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
        bool superArmor = status != null && status.BlocksInterrupt;
        if (!superArmor && _state.Value != MonsterState.Groggy && _state.Value != MonsterState.Return
            && _state.Value != MonsterState.Knockback)
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

        SetState(MonsterState.Dead);

        // 드롭/보상 확장 훅 — 사망 단일 지점에서만 호출(은희가 채움).
        OnDeath();

        // 세션 통계(처치 수)용 통보. 구독자가 없으면 아무 일도 하지 않는다.
        MonsterDeathEvents.RaiseServerMonsterDied(this);

        // 디졸브 연출이 있으면 재생 후 디스폰, 없으면 지연 후 디스폰.
        IDeathEffect fx = GetComponent<IDeathEffect>();
        if (fx != null)
            fx.Play(DespawnNow);
        else
            StartCoroutine(DespawnAfter(data != null ? data.despawnDelay : 2f));
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
        NetworkObject netObj = NetworkObject;
        if (netObj != null && netObj.IsSpawned)
            netObj.Despawn();
    }
    #endregion

    #region 유틸
    // 공격 슬롯의 쿨다운이 돌았는가.
    // 슬롯 쿨(_cooldownByAttack)이 0 이하면 base 간격 = 1 / 공격속도로 폴백한다.
    // (AttackSpeed = 초당 공격 횟수. 0.5 → 2초당 1회.)
    // 인자를 안 주면 슬롯 0 — 공격이 1종인 일반 몬스터는 이 경로만 탄다.
    protected bool CooldownReady(int attackSlot = DefaultAttackSlot)
    {
        if (attackSlot < 0 || attackSlot >= _lastUsedByAttack.Length)
            attackSlot = DefaultAttackSlot;

        float cooldown = _cooldownByAttack[attackSlot];
        if (cooldown <= 0f)
            cooldown = 1f / Mathf.Max(0.01f, AttackSpeed);

        return Time.time - _lastUsedByAttack[attackSlot] >= cooldown;
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

    /// <summary>슬롯별 쿨 길이(초)를 설정한다. 0 이하면 base 간격(1/AttackSpeed)을 쓴다.</summary>
    protected void SetAttackCooldown(int attackSlot, float seconds)
    {
        if (attackSlot < 0 || attackSlot >= _cooldownByAttack.Length) return;
        _cooldownByAttack[attackSlot] = Mathf.Max(0f, seconds);
    }

    // 인지 반경 내 최근접 플레이어 Transform 탐색(서버 전용).
    Transform FindNearestTarget()
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

            // 루트 오브젝트 기준 거리(콜라이더가 자식일 수 있음).
            Transform root = c.transform.root;
            float sqr = (root.position - transform.position).sqrMagnitude;
            if (sqr < best)
            {
                best = sqr;
                nearest = root;
            }
        }
        return nearest;
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
        _state.Value = next; // 같은 값이면 NGO가 콜백을 발생시키지 않음.
    }
    #endregion

    #region 애니메이션(상태→Animator 매핑 단일 지점)
    void OnStateChanged(MonsterState previous, MonsterState next)
    {
        // 액션(공격/피격/그로기)에서 이동계열(대기/추격/복귀)로 전이 시, 진행 중이던 액션 클립을
        // 끊고 로코모션으로 강제 복귀. (공격 도중 리쉬 복귀 등으로 애니가 공격 클립에 눌러앉는 문제 해결.)
        if (IsActionAnimState(previous) && IsLocomotionAnimState(next))
            ResetToLocomotion();
        PlayStateAnimation(next);
    }

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
        SafeCrossFade(data.locomotionState);
    }

    void SafeResetTrigger(string param)
    {
        if (HasParameter(animator, param)) animator.ResetTrigger(param);
    }

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
                PlayDeathPlaceholder();   // 임시 사망 표시(디졸브/Death 애니 도입 전, 각 피어 로컬 연출)
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

    static bool HasParameter(Animator anim, string param)
    {
        if (anim == null || anim.runtimeAnimatorController == null || string.IsNullOrEmpty(param))
            return false;
        AnimatorControllerParameter[] ps = anim.parameters;
        for (int i = 0; i < ps.Length; i++)
            if (ps[i].name == param) return true;
        return false;
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
