using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 연습장 표적(허수아비). 죽지 않고, 맞으면 명목 피해를 띄우고, 잠시 안 맞으면 체력을 되돌리고,
/// 제 자리에서 너무 멀어지면 돌아온다.
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

    [Header("스탯")]
    [SerializeField, Min(1)] int maxHp = 500;
    [SerializeField, Min(0)] int defense = 0;

    [Header("회복 — 이 시간 동안 안 맞으면 되돌아온다")]
    [SerializeField, Min(0f)] float regenDelay = 3f;
    [SerializeField, Min(0.01f)] float regenDuration = 1f;

    [Header("자리 복귀 — 스폰 지점에서 이만큼 벗어나면 즉시 되돌아온다")]
    [SerializeField, Min(0.1f)] float resetDistance = 5f;

    /// <summary>
    /// 모든 피어에서 명목 피해(방어 경감 후, 체력 하한으로 잘리기 전 값)를 알린다.
    /// 인자는 (피해량, 공격자 clientId). 공격자가 플레이어가 아니면 ulong.MaxValue.
    /// TrainingDummyDamagePresenter 가 구독해 데미지 숫자와 타격 쉐이크를 낸다.
    /// </summary>
    public event Action<int, ulong> NominalDamaged;

    TrainingDummyRegen _regen;
    Rigidbody _rigidbody;
    bool _spawnKinematic;
    Vector3 _anchorPosition;
    Quaternion _anchorRotation;

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
        ApplyDummyDamage(attackInfo.damage, ResolveAttackerClientId(hitContext));
        return true;
    }

    public override void TakeDamage(AttackInfo attackInfo)
    {
        ApplyDummyDamage(attackInfo.damage, ulong.MaxValue);
    }

    void ApplyDummyDamage(int rawDamage, ulong attackerClientId)
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
        ShowNominalDamageRpc(nominal, attackerClientId);
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
    void ShowNominalDamageRpc(int amount, ulong attackerClientId)
    {
        NominalDamaged?.Invoke(amount, attackerClientId);
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

        if ((transform.position - _anchorPosition).sqrMagnitude > resetDistance * resetDistance)
            ReturnToAnchor();
    }

    void ReturnToAnchor()
    {
        // 남은 넉백 속도를 안 지우면 복귀 → 그 속도로 재이탈 → 복귀 루프가 된다.
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
