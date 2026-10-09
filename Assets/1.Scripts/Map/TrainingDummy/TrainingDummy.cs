using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 연습장 표적(허수아비). 죽지 않고, 맞으면 명목 피해를 띄우고, 잠시 안 맞으면 체력과 자리를 되돌리고,
/// 제 자리에서 너무 멀어지면 즉시 돌아온다.
///
/// 몬스터가 아니다 — MonsterBase / MonsterDataSO / MonsterStatusEffect / FSM / NavMeshAgent 를 쓰지 않는다.
/// (MonsterBase 의 리쉬 복귀는 Revive() 로 체력을 최대로 되돌려 이 클래스의 회복 규칙과 정면 충돌한다.)
/// 상속하는 것은 Unit 하나뿐이고, 그 이유는 데미지 파이프라인·HP 복제·스킬 조준이 전부
/// Unit 구체 타입을 요구하기 때문이다. 설계 근거는 PLAN-training-dummy.md.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class TrainingDummy : Unit
{
    /// <summary>
    /// 죽지 않는 표적이라 체력은 여기서 멈춘다.
    /// 0 을 허용하면 PlayerSkillTargeting 이 CurrentHealth &lt;= 0 을 InvalidTarget 으로 처리해
    /// (PlayerSkillTargeting.cs:201 / :293 / :387) 회복 전까지 궁극기·타겟팅 스킬 조준이 끊긴다.
    /// </summary>
    const int MinHealth = 1;

    /// <summary>이보다 덜 밀려났으면 제자리로 본다(m). 물리 오차로 매번 순간이동하지 않게.</summary>
    const float IdleReturnThreshold = 0.05f;

    [Header("스탯")]
    [SerializeField, Min(1)] int maxHp = 100;
    [SerializeField, Min(0)] int defense = 0;
    [Tooltip("데미지 숫자 강도 구간. 일반/엘리트/보스 더미가 실제 몬스터와 같은 구간으로 보이게 한다.")]
    [SerializeField] MonsterRank rank = MonsterRank.Normal;

    public override MonsterRank Rank => rank;

    [Header("회복 — 이 시간 동안 안 맞으면 되돌아온다")]
    [SerializeField, Min(0f)] float regenDelay = 3f;
    [SerializeField, Min(0.01f)] float regenDuration = 1f;

    [Header("자리 복귀 — 이만큼 벗어나면 즉시, 덜 벗어났으면 regenDelay 동안 안 맞을 때 되돌아온다")]
    [SerializeField, Min(0.1f)] float resetDistance = 5f;

    /// <summary>
    /// 모든 피어에서 명목 피해(방어 경감 후, 체력 하한으로 잘리기 전 값)와 공격 메타데이터를 알린다.
    /// 공격자가 플레이어가 아니면 attackerClientId는 ulong.MaxValue.
    /// TrainingDummyDamagePresenter 가 구독해 데미지 숫자와 타격 쉐이크를 낸다.
    /// </summary>
    public event Action<DamageDealtInfo> NominalDamaged;

    TrainingDummyRegen _regen;
    TrainingDummyInterrupt _interrupt; // 없으면 인터럽트는 데미지만
    Rigidbody _rigidbody;
    bool _spawnKinematic;
    Vector3 _anchorPosition;
    Quaternion _anchorRotation;

    // 지속 넉백 상태 (서버 전용)
    Vector3 _knockbackDir;
    float _knockbackSpeed;
    float _knockbackTimer;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Unit 이 방금 자동 부착한 기본 연출 소비자들을 걷어낸다. 셋 다 "실제 HP 델타"를 소비하는데
        // 허수아비는 체력 하한 1 에 붙어도 계속 반응해야 한다 — 하한에서는 델타가 0 이라 전부 멈춘다.
        // 대역은 명목 피해(NominalDamaged)를 구독하는 허수아비 전용 컴포넌트들이 맡는다.
        // 함께 두면 같은 타격에 숫자가 두 번 뜬다.
        StripAutoComponent<FloatingDamagePresenter>();   // → TrainingDummyDamagePresenter
        StripAutoComponent<UnitCameraFeedbackReporter>(); // → TrainingDummyDamagePresenter
        StripAutoComponent<HitFlash>();                   // → TrainingDummyHitFlash

        _rigidbody = GetComponent<Rigidbody>();
        _spawnKinematic = _rigidbody != null && _rigidbody.isKinematic;
        _interrupt = GetComponent<TrainingDummyInterrupt>();

        if (!IsServer)
            return;

        // Initialize 를 부르지 않으면 _health 가 null 이라 모든 피해가 조용히 버려진다.
        // 허수아비는 공격하지 않으므로 공격력·속도는 전부 0.
        Initialize(0, 0f, 0f, maxHp, defense);

        _regen = new TrainingDummyRegen(regenDelay, regenDuration);
        _anchorPosition = transform.position;
        _anchorRotation = transform.rotation;
    }

    void StripAutoComponent<T>() where T : Component
    {
        T auto = GetComponent<T>();
        if (auto != null)
            Destroy(auto);
    }

    #region 피격
    public override bool ReceiveAttack(AttackInfo attackInfo, AttackHitContext hitContext)
    {
        // Unit 의 기본 구현이 쓰는 귀속 RPC 경로는 타지 않는다 — 공격자 clientId 는
        // 허수아비 전용 RPC 가 직접 싣고, 그 소비자(UnitCameraFeedbackReporter)는 스폰 때 제거했다.
        ulong attackerClientId = ResolveAttackerClientId(hitContext);
        ApplyDummyDamage(attackInfo.damage, attackerClientId,
            attackInfo.attackType, attackInfo.hitPattern);
        TryEnterKnockback(attackInfo, hitContext);
        TryServerInterrupt(attackInfo, attackerClientId, hitContext.sourceUnit);
        return true;
    }

    public override void TakeDamage(AttackInfo attackInfo)
    {
        ApplyDummyDamage(attackInfo.damage, ulong.MaxValue,
            attackInfo.attackType, attackInfo.hitPattern);
        TryServerInterrupt(attackInfo, ulong.MaxValue, null);
    }

    // 데미지 뒤에 판정한다(중간보스와 같은 순서). 인터럽트 가능 상태가 아니면 데미지만 들어간 것으로 끝난다.
    // 🔴 CombatStatsEvents 간파 통계는 올리지 않는다 — SessionStatsTracker 가 허수아비를 집계에서 뺀다.
    void TryServerInterrupt(AttackInfo attackInfo, ulong attackerClientId, Unit sourceUnit)
    {
        if (!IsServer || !attackInfo.isInterruptAttack || _interrupt == null)
            return;

        _interrupt.ServerTryInterrupt(attackerClientId, sourceUnit);
    }

    void ApplyDummyDamage(int rawDamage, ulong attackerClientId,
        AttackType attackType, AttackHitPattern hitPattern)
    {
        if (!IsServer || rawDamage <= 0)
            return;

        int nominal = MitigateByDefense(rawDamage);
        if (nominal <= 0)
            return;

        // 체력 하한까지만 깎는다. 경감은 위에서 이미 끝났으므로 직접 차감 경로를 쓴다.
        int allowed = Mathf.Max(0, CurrentHealth - MinHealth);
        if (allowed > 0)
            ApplyDirectHealthDamage(Mathf.Min(nominal, allowed));

        _regen?.NotifyDamaged();

        // 표시는 실제 감소량이 아니라 명목 피해다 — 체력 1 에 붙어 있어도 계속 뜬다.
        ShowNominalDamageRpc(nominal, attackerClientId, (byte)attackType, (byte)hitPattern);
    }

    /// <summary>
    /// 방어력 경감을 적용한다.
    /// </summary>
    /// <remarks>
    /// Unit.ApplyMitigatedHealthDamage 와 같은 공식의 사본이다. 명목 피해(경감 후, 하한으로 잘리기 전)를
    /// 표시에 써야 하는데 Unit 이 경감 결과만 돌려주는 API 를 제공하지 않는다.
    /// 코어 공식이 바뀌면 여기도 같이 고칠 것.
    /// </remarks>
    int MitigateByDefense(int damage)
    {
        int currentDefense = _health != null ? _health.CurrentDefense : 0;
        return Mathf.RoundToInt(damage * 100f / (100f + currentDefense));
    }

    static ulong ResolveAttackerClientId(AttackHitContext hitContext)
    {
        Player sourcePlayer = hitContext.sourceUnit as Player;
        if (sourcePlayer == null && hitContext.sourceTransform != null)
            sourcePlayer = hitContext.sourceTransform.GetComponentInParent<Player>();

        return sourcePlayer != null && sourcePlayer.IsSpawned
            ? sourcePlayer.OwnerClientId
            : ulong.MaxValue;
    }

    [Rpc(SendTo.ClientsAndHost)]
    void ShowNominalDamageRpc(int amount, ulong attackerClientId, byte attackType, byte hitPattern)
    {
        NominalDamaged?.Invoke(new DamageDealtInfo(amount, DamageChannel.Hp, attackerClientId,
            (AttackType)attackType, (AttackHitPattern)hitPattern));
    }
    #endregion

    #region 넉백 (서버 전용)
    /// <summary>
    /// `AttackInfo` 의 넉백 지시를 해석한다.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>이 해석기가 없으면 허수아비는 안 밀린다.</b> `Unit` 은 넉백을 전혀 처리하지 않고,
    /// `AttackInfo.knockbackStrength/Duration` 을 읽는 곳은 `MonsterBase.ReceiveAttack` 하나뿐이다.
    /// 여기 로직은 그 본문(방향 폴백 체인 포함)을 옮겨온 것이다.
    ///
    /// 프리팹의 `LinearKnockback` 은 <b>다른 경로</b>다 — `Unit.Knockback(방향, 세기)` 이 쓰는
    /// 임펄스 방식이고, 보스가 플레이어를 밀 때 쓴다. 플레이어 스킬은 그쪽을 호출하지 않는다.
    /// 붙여만 두는 이유는 보스가 허수아비를 밀 때 `Unit.OnKnockback` 이 LogError 를 찍지 않게 하려는 것.
    ///
    /// `MonsterBase` 와 달리 NavMesh 경계 클램프는 하지 않는다 — 허수아비는 NavMesh 밖에 놓일 수
    /// 있고, 밀려난 거리는 `resetDistance` 자리 복귀가 어차피 잡는다.
    /// </remarks>
    void TryEnterKnockback(AttackInfo attackInfo, AttackHitContext hitContext)
    {
        if (!IsServer || HasSuperArmor)
            return;

        if (attackInfo.knockbackStrength <= 0f || attackInfo.knockbackDuration <= 0f)
            return;

        // 공격이 방향을 명시하면 그대로(방향성 공격 — 견인 등),
        // 아니면 방사형(허수아비 - 공격자, 수평) 폴백.
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

        if (dir.sqrMagnitude < 0.0001f)
            return;

        _knockbackDir = dir.normalized;
        _knockbackSpeed = attackInfo.knockbackStrength;
        _knockbackTimer = attackInfo.knockbackDuration;
    }

    // 서버틱 지속 밀기. 임펄스가 아니라 "knockbackDuration 초 동안 knockbackStrength m/s 로 민다".
    void TickKnockback(float deltaTime)
    {
        if (_knockbackTimer <= 0f)
            return;

        _knockbackTimer -= deltaTime;
        transform.position += _knockbackDir * (_knockbackSpeed * deltaTime);
    }
    #endregion

    #region 회복 · 자리 복귀 (서버 전용)
    void Update()
    {
        if (!IsServer || _regen == null)
            return;

        int heal = _regen.Tick(Time.deltaTime, CurrentHealth, MaxHp);
        if (heal > 0)
            HealHp(heal);

        // 밀어낸 뒤에 거리를 재야 같은 프레임에 이탈을 잡는다.
        TickKnockback(Time.deltaTime);

        // 멀리 밀려나면 즉시, 조금 밀려났으면 회복과 같은 타이밍(마지막 피격 후 regenDelay)에 스폰 자리로 돌아간다.
        float displacement = (transform.position - _anchorPosition).sqrMagnitude;
        bool farAway = displacement > resetDistance * resetDistance;
        bool idleAway = displacement > IdleReturnThreshold * IdleReturnThreshold &&
                        _regen.IsIdle && _knockbackTimer <= 0f;
        if (farAway || idleAway)
            ReturnToAnchor();
    }

    void ReturnToAnchor()
    {
        // 남은 넉백을 안 지우면 복귀 → 남은 시간 동안 다시 밀림 → 재이탈 루프가 된다.
        _knockbackTimer = 0f;

        // LinearKnockback(임펄스 경로)이 남긴 속도도 같은 이유로 지운다.
        if (_rigidbody != null)
        {
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
            // LinearKnockback.EndKnockback 은 NavMeshAgent 가 있을 때만 isKinematic 을 되돌린다.
            // 허수아비엔 에이전트가 없으니 여기서 스폰 당시 값으로 직접 원복한다.
            _rigidbody.isKinematic = _spawnKinematic;
        }

        // 상태이상을 남겨두면 복귀한 자리에서 계속 걸려 있는 것처럼 보인다.
        StatusEffects?.ClearAllServer();

        transform.SetPositionAndRotation(_anchorPosition, _anchorRotation);

        if (_rigidbody != null && !_rigidbody.isKinematic)
        {
            _rigidbody.position = _anchorPosition;
            _rigidbody.rotation = _anchorRotation;
        }
    }
    #endregion
}
