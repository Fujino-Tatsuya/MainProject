using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

// 코드 FSM 보스 두뇌(23호/TwentyThree 스켈레톤). 서버 권한.
//
// 설계 목적: MonsterBase.cs와 동일한 "순수 코드 FSM + 서버 상태소유 + 클라 애니재생" 모델을 보스에 적용.
//  - Unit 직접 상속(Enemy/MonsterBase 상속 금지). 데미지 유입 = ReceiveAttack→TakeDamage(AttackInfo) 서버 경로.
//  - 서버가 _state(NetworkVariable)를 소유, 클라는 OnStateChanged 콜백에서 Animator만 재생.
//  - 자산(NavMesh/Animator/status/choice/melee)이 없어도 컴파일·실행되고 예외로 죽지 않도록 전부 널가드.
//
// 이 슬라이스 = "격리+뼈대": FSM + 거리창 가중치 공격선택 + 페이즈 골격 + 근접공격 2개(Slam/Sweep).
// 잡기/폭탄/송전탑/차징 전체/Dash/Jump는 이후 슬라이스로 연기 — 아래 virtual 훅만 남긴다.
[RequireComponent(typeof(NetworkObject))]
public class BossBase : Unit
{
    [Header("데이터(스탯/타이밍/애니 파라미터명 재사용)")]
    [SerializeField] protected MonsterDataSO data;

    [Header("보스 전용 참조(비우면 자동 탐색)")]
    [SerializeField] protected BaseAttackChoice attackChoice;   // 거리창+가중치 공격 선택기
    [SerializeField] protected MonsterMeleeAttack meleeAttack;  // 근접 히트 실행(Hit() 재사용)
    [SerializeField] protected NavMeshAgent agent;
    [SerializeField] protected Animator animator;
    [SerializeField] protected MonsterStatusEffect status;
    [SerializeField] protected Collider bodyCollider;           // 사망 시 비활성(선택)

    [Header("타게팅")]
    [SerializeField] protected LayerMask playerMask;            // 인지 대상(플레이어) 레이어
    [SerializeField] protected int maxDetectionResults = 16;

    [Header("페이즈 임계(현재 HP 비율 하향 통과 시 강제 Charging)")]
    [SerializeField, Range(0f, 1f)] protected float phase2HpPercent = 0.66f;
    [SerializeField, Range(0f, 1f)] protected float phase3HpPercent = 0.33f;
    [SerializeField] protected float chargingDuration = 1.5f;   // Charging 상태 지속(연출 골격)

    [Header("공격 타이밍(타입별 windup/duration)")]
    [SerializeField] protected float slamWindup = 0.45f;
    [SerializeField] protected float slamDuration = 1.1f;
    [SerializeField] protected float sweepWindup = 0.35f;
    [SerializeField] protected float sweepDuration = 1.0f;

    [Header("보스 애니메이터 파라미터명(없으면 graceful)")]
    [SerializeField] protected string slamTrigger = "Slam";
    [SerializeField] protected string sweepTrigger = "Sweep";
    [SerializeField] protected string chargeTrigger = "Charge";
    [SerializeField] protected string breakTrigger = "Break";

    // 상태 복제. 서버 write / 모두 read.
    readonly NetworkVariable<BossState> _state = new NetworkVariable<BossState>(
        BossState.Idle,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    public BossState State => _state.Value;

    // 이동 블렌드용 속도 복제(클라 Animator Speed 파라미터 구동).
    readonly NetworkVariable<float> _animSpeed = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // 서버 전용 런타임 상태
    Transform _target;
    Collider[] _detectBuffer;
    float _lastAttackTime = -999f;
    float _stateTimer;
    bool _attackFired;
    float _curAttackWindup;
    float _curAttackDuration;
    BossBasicAttackType _curAttackType = BossBasicAttackType.None;
    int _groggyCount;
    int _phaseIndex;          // 0/1/2 — 하향으로만 증가
    bool _pendingCharging;    // 페이즈 임계 통과 → 현재 행동 종료 후 강제 Charging
    int _maxHp;               // Unit.MaxHp는 _health 생성 전 무효 → Initialize 인자를 자체 보관(페이즈 계산용)
    bool _isDead;
    bool _initialized;
    Coroutine _deathFxRoutine;
    const float DeathPlaceholderDuration = 1f;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // 참조 자동 보강(인스펙터 미할당 대비).
        if (attackChoice == null) attackChoice = GetComponentInChildren<BaseAttackChoice>();
        if (meleeAttack == null) meleeAttack = GetComponentInChildren<MonsterMeleeAttack>();
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (status == null) status = GetComponent<MonsterStatusEffect>();

        _state.OnValueChanged += OnStateChanged;

        if (IsServer)
            ServerInitialize();
        else if (agent != null)
            agent.enabled = false; // 클라는 이동 권한 없음(NetworkTransform 복제로 위치 반영).

        // 스폰 시점 상태를 즉시 애니에 반영(뒤늦게 접속한 클라 포함).
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

        // Unit 스탯 주입(파라미터 순서 = Unit.Initialize 계약).
        Initialize(data.attackDamage, data.moveSpeed, data.attackSpeed, data.maxHp, data.defense, data.maxShield);
        _maxHp = Mathf.Max(1, data.maxHp); // 페이즈 임계 계산용 자체 보관.

        // 근접 공격기에 데미지/타깃레이어 스냅샷 반영.
        if (meleeAttack != null)
        {
            meleeAttack.SetDamageSnapshot(data.attackDamage);
            meleeAttack.SetTargetLayer(playerMask);
        }

        _detectBuffer = new Collider[Mathf.Max(1, maxDetectionResults)];

        if (agent != null)
        {
            agent.enabled = true;
            agent.speed = data.moveSpeed;
            agent.stoppingDistance = Mathf.Max(0f, data.attackRange * 0.8f);
        }

        // 스폰 슈퍼아머(무한) 옵션.
        if (data.startsWithSuperArmor && status != null)
            status.ApplyStatus(StatusEffectType.SuperArmor, 0f);

        _phaseIndex = 0;
        _initialized = true;
        SetState(BossState.Idle);
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
        // 이동 봉쇄 상태이상이면 에이전트 정지.
        if (status != null && status.BlocksMovement)
            StopAgent();

        switch (_state.Value)
        {
            case BossState.Idle:
            case BossState.Chase:
                HandleSeekAndCombat();
                break;
            case BossState.Attack:
                HandleAttack(dt);
                break;
            case BossState.Charging:
                HandleCharging(dt);
                break;
            case BossState.Groggy:
                HandleGroggy(dt);
                break;
            case BossState.Break:
                HandleBreak(dt);
                break;
            case BossState.Dead:
                break;
        }
    }

    void HandleSeekAndCombat()
    {
        // 페이즈 임계 통과가 대기 중이고 지금 공격 중이 아니면 즉시 Charging 강제 진입(의도 #4).
        if (_pendingCharging)
        {
            _pendingCharging = false;
            EnterCharging();
            return;
        }

        // 타겟 락온: 유효 타겟 유지, 없거나 무효일 때만 재탐색.
        if (!IsTargetValid(_target))
            _target = FindNearestTarget();

        if (_target == null)
        {
            StopAgent();
            SetState(BossState.Idle);
            return;
        }

        float dist = Vector3.Distance(transform.position, _target.position);
        FaceTarget();

        bool attackBlocked = status != null && status.BlocksAttack;
        bool movementBlocked = status != null && status.BlocksMovement;

        // 쿨다운 준비되면 거리창+가중치로 공격 선택. None이면 접근/대기.
        if (!attackBlocked && CooldownReady())
        {
            int chosen = attackChoice != null ? attackChoice.GetRandomAttack(dist) : (int)BossBasicAttackType.None;
            if (chosen != (int)BossBasicAttackType.None)
            {
                StartAttack((BossBasicAttackType)chosen);
                return;
            }
        }

        // 공격 미선택: 사거리 밖이면 추격, 안이면 교전 대기.
        if (dist > data.attackRange)
        {
            SetState(BossState.Chase);
            if (!movementBlocked)
                MoveAgentTo(_target.position, data.chaseSpeed);
        }
        else
        {
            StopAgent();
            if (_state.Value != BossState.Chase)
                SetState(BossState.Chase);
        }
    }

    void StartAttack(BossBasicAttackType type)
    {
        _curAttackType = type;
        GetAttackTiming(type, out _curAttackWindup, out _curAttackDuration);

        _lastAttackTime = Time.time;
        _stateTimer = _curAttackDuration;
        _attackFired = false;
        StopAgent();
        FaceTarget();

        // 공격 중 슈퍼아머(경직 무시) 옵션.
        if (data.hasSuperArmorWhileAttacking && status != null)
            status.ApplyStatus(StatusEffectType.SuperArmor, _curAttackDuration);

        // 연기된 특수 공격(잡기/폭탄/차징 히트 등) 확장점 — 슬라이스1은 no-op.
        ExecuteSpecialAttack(type);

        SetState(BossState.Attack);
    }

    void HandleAttack(float dt)
    {
        _stateTimer -= dt;
        FaceTarget();

        // 선딜 경과 후 히트 판정 1회. (Animator 이벤트 확정 시 meleeAttack.Hit()를 이벤트로 옮긴다.)
        float elapsed = _curAttackDuration - _stateTimer;
        if (!_attackFired && elapsed >= _curAttackWindup)
        {
            meleeAttack?.Hit();
            _attackFired = true;
        }

        if (_stateTimer <= 0f)
            DecideNextAfterAction();
    }

    void HandleCharging(float dt)
    {
        _stateTimer -= dt;
        if (_stateTimer <= 0f)
            SetState(BossState.Idle);
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

    void HandleBreak(float dt)
    {
        // 파츠 파괴/무력화 골격 — 연기된 메커닉 진입점. 타이머 종료 시 복귀.
        _stateTimer -= dt;
        if (_stateTimer <= 0f)
            DecideNextAfterAction();
    }

    // 행동(공격/그로기/브레이크) 종료 후 다음 상태 결정.
    void DecideNextAfterAction()
    {
        if (_isDead) return;

        // 페이즈 임계 통과가 대기 중이면 무조건 Charging 강제 진입(현재 행동 완료 보장).
        if (_pendingCharging)
        {
            _pendingCharging = false;
            EnterCharging();
            return;
        }

        SetState(BossState.Idle); // 다음 Tick의 HandleSeekAndCombat가 재평가.
    }

    // 페이즈 전환 강제 진입 골격: Charging 상태 → 짧게 버프/쉴드 부여 + 상태 브로드캐스트 → 잠시 후 Idle 복귀.
    void EnterCharging()
    {
        _stateTimer = Mathf.Max(0.1f, chargingDuration);
        StopAgent();

        // 쉴드/버프 부여(있으면) — 슬라이스1은 슈퍼아머로 대체(폭탄/실드 슬라이스에서 실제 버프로 교체).
        if (status != null)
            status.ApplyStatus(StatusEffectType.SuperArmor, _stateTimer);

        BroadcastBossState(BossState.Charging);
        SetState(BossState.Charging);
    }

    void EnterGroggy()
    {
        _groggyCount = 0;
        _stateTimer = data != null ? data.groggyDuration : 3f;
        StopAgent();
        SetState(BossState.Groggy);
    }

    // 파츠 파괴/무력화 진입 골격(연기된 메커닉에서 호출). 슬라이스1은 미사용이지만 배선 진입점으로 유지.
    protected void EnterBreak(float duration)
    {
        if (_isDead) return;
        _stateTimer = Mathf.Max(0.1f, duration);
        StopAgent();
        SetState(BossState.Break);
    }
    #endregion

    #region 피격 / 페이즈 / 사망 (서버 경로)
    public override void TakeDamage(AttackInfo attackInfo)
    {
        base.TakeDamage(attackInfo); // 서버 가드 + 방어/쉴드/체력 + _currentHp 복제 갱신.

        if (!IsServer || _isDead)
            return;

        if (CurrentHealth <= 0)
        {
            EnterDead();
            return;
        }

        // 페이즈 임계 하향 통과 판정(처음 통과 시에만 pending 설정).
        EvaluatePhase();

        // 그로기 누적(옵션).
        if (attackInfo.isGroggyAttack && data != null && data.maxGroggyCount > 0)
        {
            _groggyCount++;
            if (_groggyCount >= data.maxGroggyCount)
                EnterGroggy();
        }
    }

    // 현재 HP 비율로 목표 페이즈 인덱스를 구해, 처음 하향 통과하면 pending Charging + OnPhaseChanged.
    void EvaluatePhase()
    {
        if (_maxHp <= 0) return;

        float hpPercent = CurrentHealth / (float)_maxHp;
        int target = 0;
        if (hpPercent <= phase3HpPercent) target = 2;
        else if (hpPercent <= phase2HpPercent) target = 1;

        if (target > _phaseIndex)
        {
            _phaseIndex = target;
            _pendingCharging = true;   // 현재 행동 완료 후 무조건 Charging(의도 #4 강제진입).
            OnPhaseChanged(_phaseIndex);
        }
    }

    void EnterDead()
    {
        if (_isDead) return;
        _isDead = true;

        StopAgent();
        if (agent != null) agent.enabled = false;
        if (bodyCollider != null) bodyCollider.enabled = false;

        SetState(BossState.Dead);

        OnDeath(); // 드롭/보상/처치연출 확장 훅(단일 지점).

        // 디졸브 연출이 있으면 재생 후 디스폰, 없으면 지연 후 디스폰.
        IDeathEffect fx = GetComponent<IDeathEffect>();
        if (fx != null)
            fx.Play(DespawnNow);
        else
            StartCoroutine(DespawnAfter(data != null ? data.despawnDelay : 2f));
    }

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

    #region 연기된 메커닉용 virtual 훅 (이후 슬라이스에서 override)
    // 페이즈 인덱스(0/1/2) 갱신 시 호출. 기본 no-op.
    protected virtual void OnPhaseChanged(int page) { }

    // 잡기/폭탄/송전탑/차징 전체/Dash/Jump 등 특수 공격 실행 확장점. 기본 no-op.
    protected virtual void ExecuteSpecialAttack(BossBasicAttackType type) { }

    // 보스 상태 브로드캐스트 확장점. 기본 no-op.
    // (Wells/폭탄 슬라이스에서 BT/Events/BossStateChanged 채널로 배선.)
    protected virtual void BroadcastBossState(BossState s) { }

    // 드롭 아이템/보상/처치 카운트 등 사망 후처리 확장점. 기본 no-op.
    protected virtual void OnDeath() { }
    #endregion

    #region 유틸
    void GetAttackTiming(BossBasicAttackType type, out float windup, out float duration)
    {
        switch (type)
        {
            case BossBasicAttackType.Slam:
                windup = slamWindup; duration = slamDuration; break;
            case BossBasicAttackType.Sweep:
                windup = sweepWindup; duration = sweepDuration; break;
            default:
                windup = data != null ? data.attackWindup : 0.35f;
                duration = data != null ? data.attackDuration : 0.9f;
                break;
        }
    }

    // 공격 간격 = 1 / 공격속도(Unit.AttackSpeed, 초당 공격 횟수).
    bool CooldownReady() => Time.time - _lastAttackTime >= 1f / Mathf.Max(0.01f, AttackSpeed);

    bool IsTargetValid(Transform t) => t != null && t.gameObject.activeInHierarchy;

    // 인지 반경 내 최근접 플레이어 Transform 탐색(서버 전용). MonsterBase.FindNearestTarget 그대로.
    Transform FindNearestTarget()
    {
        if (_detectBuffer == null || data == null) return null;

        int count = Physics.OverlapSphereNonAlloc(
            transform.position, data.detectionRadius, _detectBuffer, playerMask, QueryTriggerInteraction.Collide);

        Transform nearest = null;
        float best = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            Collider c = _detectBuffer[i];
            if (c == null) continue;

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
        agent.SetDestination(destination);
    }

    void StopAgent()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
            return;
        agent.isStopped = true;
        agent.ResetPath();
        // isStopped/ResetPath는 속도를 즉시 0으로 만들지 않는다 — acceleration에 따라 감속하며
        // 관성으로 미끄러진다("글라이딩"). 공격/차징/그로기 진입 후 완전 정지 보장을 위해 즉시 0으로.
        agent.velocity = Vector3.zero;
    }

    void FaceTarget()
    {
        if (_target == null) return;
        Vector3 dir = _target.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.LookRotation(dir);
    }

    void SetState(BossState next)
    {
        if (!IsServer) return;
        _state.Value = next; // 같은 값이면 NGO가 콜백을 발생시키지 않음.
    }
    #endregion

    #region 애니메이션(상태→Animator 매핑 단일 지점)
    void OnStateChanged(BossState previous, BossState next)
    {
        PlayStateAnimation(next);
    }

    void PlayStateAnimation(BossState s)
    {
        if (animator == null || data == null) return;

        SafeSetBool(data.groggyBool, s == BossState.Groggy);

        switch (s)
        {
            case BossState.Attack:
                // 공격 타입별 트리거(없으면 data.attackTrigger 폴백).
                SafeSetTrigger(SelectAttackTrigger(_curAttackType));
                break;
            case BossState.Charging:
                SafeSetTrigger(chargeTrigger);
                break;
            case BossState.Break:
                SafeSetTrigger(breakTrigger);
                break;
            case BossState.Dead:
                SafeSetTrigger(data.deathTrigger);
                PlayDeathPlaceholder();
                break;
        }
    }

    string SelectAttackTrigger(BossBasicAttackType type)
    {
        switch (type)
        {
            case BossBasicAttackType.Slam:
                return HasParameter(animator, slamTrigger) ? slamTrigger : data.attackTrigger;
            case BossBasicAttackType.Sweep:
                return HasParameter(animator, sweepTrigger) ? sweepTrigger : data.attackTrigger;
            default:
                return data.attackTrigger;
        }
    }

    void SafeSetTrigger(string param)
    {
        if (HasParameter(animator, param)) animator.SetTrigger(param);
    }

    void SafeSetBool(string param, bool value)
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

    // 임시 사망 표시(디졸브/Death 애니 도입 전, 각 피어 로컬 연출). MonsterBase 방식 그대로.
    void PlayDeathPlaceholder()
    {
        if (_deathFxRoutine != null) return;
        Transform model = animator != null ? animator.transform : null;
        if (model == null) return;
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
    }
#endif
}
