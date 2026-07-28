using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

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
    float _lastAttackTime = -999f;
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
    Vector3 _knockbackDir;         // 지속넉백 방향(수평 정규화)
    float _knockbackSpeed;         // 지속넉백 속도(m/s) = AttackInfo.knockbackStrength
    float _staggerAfterKnockback;  // 넉백 종료 후 Stunned 경직 시간(초)
    bool _isDead;
    bool _initialized;
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
        // 클립에 OnAttackHit/OnAttackEnd 이벤트가 없으면 HandleAttack 타이머가 폴백이므로 무해.
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

    void HandleSeekAndCombat()
    {
        // 리쉬: 스폰 지점에서 leash 밖이면 복귀 우선(진입 즉시 상태 초기화 + 최대 체력 회복).
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
            default:
                SeekMelee(dist, movementBlocked, attackBlocked);
                break;
        }
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
        transform.rotation = Quaternion.LookRotation(v.normalized);
    }
    #endregion

    // 타겟 유효성: 존재 + 활성. (리쉬는 몹-스폰 거리로 별도 판정하므로 여기선 거리 제한 없음.)
    bool IsTargetValid(Transform t)
    {
        return t != null && t.gameObject.activeInHierarchy;
    }

    protected virtual void StartAttack()
    {
        _lastAttackTime = Time.time;
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
        if (_stateTimer <= 0f)
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

        if (IsServer && resolved && attackInfo.knockbackStrength > 0f && attackInfo.knockbackDuration > 0f)
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

        return resolved;
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

        // 그로기 누적.
        if (attackInfo.isGroggyAttack && data != null && data.maxGroggyCount > 0)
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
    // 공격 간격 = 1 / 공격속도(Unit.AttackSpeed, 초당 공격 횟수). 예: 0.5 → 2초당 1회.
    bool CooldownReady() => Time.time - _lastAttackTime >= 1f / Mathf.Max(0.01f, AttackSpeed);

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
        transform.rotation = Quaternion.LookRotation(dir);
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
