using BaseNetCode;
using Unity.Netcode;
using UnityEngine;

public class Unit : BaseNetworkBehaviour, IAttackReceiver
{
    #region 공격력
    int _attackDamage;
    public int AttackDamage { get { return _attackDamage; } }
    /// <summary>
    /// attackDamage 값을 변경하는 함수
    /// </summary>
    /// <param name="newAttackDamage">변경할 새로운 공격력 값</param>
    public void ChangeAttackDamageValue(int newAttackDamage)
    {
        if(!IsServer) return; // 서버에서만 공격력 변경 처리
        _attackDamage = newAttackDamage;
    }
    #endregion

    #region 체력과 방어력
    protected Health _health;
    // _health는 서버에서만 생성됨(Initialize) — 클라이언트는 복제된 NetworkVariable을 읽는다
    public int CurrentHealth { get { return _health != null ? _health.CurrentHealth : _currentHp.Value; } }
    public int MaxHp { get { return _health != null ? _health.MaxHp : _maxHp.Value; } }
    protected NetworkVariable<int> _currentHp = new NetworkVariable<int>(
    0,
    NetworkVariableReadPermission.Everyone,
    NetworkVariableWritePermission.Server
);
    protected NetworkVariable<int> _maxHp = new NetworkVariable<int>(
    0,
    NetworkVariableReadPermission.Everyone,
    NetworkVariableWritePermission.Server
);
    protected NetworkVariable<int> _currentShield = new NetworkVariable<int>(
    0,
    NetworkVariableReadPermission.Everyone,
    NetworkVariableWritePermission.Server
);
    protected NetworkVariable<bool> _hasShield = new NetworkVariable<bool>(
    false,
    NetworkVariableReadPermission.Everyone,
    NetworkVariableWritePermission.Server
);

    /// <summary>
    /// damage만큼 방어력을 반영하여 쉴드와 체력을 감소시키는 함수
    /// </summary>
    /// <param name="damage">감소시킬 피해 값</param>
    protected void TakeDamage(int damage)
    {
        if (!IsServer) return; // 서버에서만 피해 처리
        int remainingDamage = damage;

        // 방어력 경감률 적용: 최종 피해 = 피해 x 100 / (100 + 방어력), 방어력 100당 50% 경감
        remainingDamage = Mathf.RoundToInt(remainingDamage * 100f / (100f + _health.CurrentDefense));

        // 쉴드가 있으면 쉴드로 피해를 처리하고 남은 데미지 계산
        if (_health.HasShield)
        {
            int shieldDamage = remainingDamage - _health.CurrentShield;
            // 남은 데미지가 쉴드보다 작은 경우, 쉴드로 모든 피해를 처리하도록 shieldDamage를 조정
            if (shieldDamage < 0)
            {
                shieldDamage = remainingDamage;
            }
            _health.TakeShieldDamage(shieldDamage);

            UpdateNetworkShield();

            remainingDamage -= shieldDamage;
        }

        // 남은 피해는 체력으로 처리
        if (remainingDamage > 0)
        {
            _health.TakeHpDamage(remainingDamage);
        }

        _currentHp.Value = _health.CurrentHealth;
    }

    public virtual void TakeDamage(AttackInfo attackInfo)
    {
        TakeDamage(attackInfo.damage);
    }

    public virtual bool ReceiveAttack(AttackInfo attackInfo, AttackHitContext hitContext)
    {
        TakeDamage(attackInfo);
        return true;
    }

    /// <summary>
    /// healAmount만큼 체력을 회복시키는 함수
    /// </summary>
    /// <param name="healAmount">회복시킬 체력 양</param>
    public void HealHp(int healAmount)
    {
        if (!IsServer) return; // 서버에서만 체력 회복 처리
        _health.HealHp(healAmount);
        _currentHp.Value = _health.CurrentHealth;
    }

    /// <summary>
    /// 체력을 최대치로 회복시키는 함수
    /// </summary>
    public void Revive()
    {
        if (!IsServer) return; // 서버에서만 체력 회복 처리
        _health.Revive();
        _currentHp.Value = _health.CurrentHealth;
    }

    /// <summary>
    /// increaseAmount만큼 방어력을 증가시키는 함수
    /// </summary>
    /// <param name="increaseAmount">증가시킬 방어력 양</param>
    public void IncreaseDefense(int increaseAmount)
    {
        if (!IsServer) return; // 서버에서만 방어력 회복 처리
        _health.IncreaseDefense(increaseAmount);
    }

    /// <summary>
    /// decreaseAmount만큼 방어력을 감소시키는 함수
    /// </summary>
    /// <param name="decreaseAmount">감소시킬 방어력 양</param>
    public void DecreaseDefense(int decreaseAmount)
    {
        if (!IsServer) return; // 서버에서만 방어력 감소 처리
        _health.DecreaseDefense(decreaseAmount);
    }

    /// <summary>
    /// shieldAmount만큼 쉴드를 회복시키는 함수
    /// </summary>
    /// <param name="shieldAmount">회복시킬 쉴드 양</param>
    public void IncreaseShield(int shieldAmount)
    {
        if (!IsServer) return; // 서버에서만 쉴드 회복 처리
        _health.IncreaseShield(shieldAmount);
        UpdateNetworkShield();
    }

    /// <summary>
    /// shieldValue만큼 쉴드를 설정하는 함수
    /// </summary>
    /// <param name="shieldValue">설정할 쉴드 값</param>
    public void SetShield(int shieldValue)
    {
        if (!IsServer) return; // 서버에서만 쉴드 설정 처리
        _health.SetShield(shieldValue);
        UpdateNetworkShield();
    }

    /// <summary>
    /// 쉴드 상태 갱신 함수
    /// </summary>
    void UpdateNetworkShield()
    {
        if (!IsServer) return;
        _currentShield.Value = _health.CurrentShield;
        _hasShield.Value = _health.HasShield;
    }
    #endregion

    #region 속도
    // 이동 속도
    float _moveSpeed;
    public float MoveSpeed { get { return _moveSpeed; } }
    /// <summary>
    /// moveSpeed 값을 변경하는 함수
    /// </summary>
    /// <param name="newMoveSpeed">변경할 새로운 이동 속도 값</param>
    public void ChangeMoveSpeedValue(float newMoveSpeed)
    {
        if (!IsServer) return; // 서버에서만 이동 속도 변경 처리
        _moveSpeed = newMoveSpeed;
    }

    // 공격 속도
    float _attackSpeed;
    public float AttackSpeed { get { return _attackSpeed; } }
    /// <summary>
    /// attackSpeed 값을 변경하는 함수
    /// </summary>
    /// <param name="newAttackSpeed">변경할 새로운 공격 속도 값</param>
    public void ChangeAttackSpeedValue(float newAttackSpeed)
    {
        if (!IsServer) return; // 서버에서만 공격 속도 변경 처리
        _attackSpeed = newAttackSpeed;
    }
    #endregion

    #region 상태이상 / 최종 스탯
    StatusEffectController _statusEffects;
    bool _statusEffectsCached;
    // 상태이상 장부의 유일한 창구. 미부착 유닛은 null (상태이상 없음으로 동작)
    public StatusEffectController StatusEffects
    {
        get
        {
            if (!_statusEffectsCached)
            {
                _statusEffects = GetComponent<StatusEffectController>();
                _statusEffectsCached = true;
            }
            return _statusEffects;
        }
    }

    float GetStatMultiplier(StatusEffectType statType)
    {
        return StatusEffects != null ? StatusEffects.GetStatMultiplier(statType) : 1f;
    }

    // 최종 스탯 = base(불변) × 활성 modifier 배율의 곱. 소비처는 base 대신 이 값을 읽는다
    public int FinalAttackDamage => Mathf.Max(0, Mathf.RoundToInt(_attackDamage * GetStatMultiplier(StatusEffectType.AttackDamageModifier)));
    public float FinalMoveSpeed => Mathf.Max(0f, _moveSpeed * GetStatMultiplier(StatusEffectType.MoveSpeedModifier));
    public float FinalAttackSpeed => Mathf.Max(0f, _attackSpeed * GetStatMultiplier(StatusEffectType.AttackSpeedModifier));
    // 방어력/최대쉴드는 _health가 서버에서만 생성되므로 서버에서만 유효
    public int FinalDefense => Mathf.Max(0, Mathf.RoundToInt((_health != null ? _health.CurrentDefense : 0) * GetStatMultiplier(StatusEffectType.DefenseModifier)));
    public int FinalMaxHp => Mathf.Max(0, Mathf.RoundToInt(MaxHp * GetStatMultiplier(StatusEffectType.MaxHpModifier)));
    public int FinalMaxShield => Mathf.Max(0, Mathf.RoundToInt((_health != null ? _health.MaxShield : 0) * GetStatMultiplier(StatusEffectType.MaxShieldModifier)));
    #endregion

    #region RPC


    [Rpc(SendTo.Server)]
    public void ChangeAttackDamageValueRpc(int newAttackDamage, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        ChangeAttackDamageValue(newAttackDamage);
    }

    [Rpc(SendTo.Server)]
    public void HealHpRpc(int healAmount, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        HealHp(healAmount);
    }

    [Rpc(SendTo.Server)]
    public void IncreaseDefenseRpc(int increaseAmount, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        IncreaseDefense(increaseAmount);
    }

    [Rpc(SendTo.Server)]
    public void DecreaseDefenseRpc(int decreaseAmount, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        DecreaseDefense(decreaseAmount);
    }

    [Rpc(SendTo.Server)]
    public void IncreaseShieldRpc(int shieldAmount, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        IncreaseShield(shieldAmount);
    }

    [Rpc(SendTo.Server)]
    public void SetShieldRpc(int shieldValue, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        SetShield(shieldValue);
    }

    [Rpc(SendTo.Server)]
    public void ChangeMoveSpeedValueRpc(float newMoveSpeed, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        ChangeMoveSpeedValue(newMoveSpeed);
    }

    [Rpc(SendTo.Server)]
    public void ChangeAttackSpeedValueRpc(float newAttackSpeed, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        ChangeAttackSpeedValue(newAttackSpeed);
    }

    #endregion

    /// <summary>
    /// Unit을 상속받는 클래스에서 Unit의 기본 능력치들을 초기화하는 함수
    /// </summary>
    /// <param name="attackDamage">기본 공격력</param>
    /// <param name="moveSpeed">기본 이동 속도</param>
    /// <param name="attackSpeed">기본 공격 속도</param>
    /// <param name="maxHp">최대 체력</param>
    /// <param name="defense">기본 방어력</param>
    /// <param name="maxShield">최대 쉴드</param>
    public void Initialize(int attackDamage, float moveSpeed, float attackSpeed, int maxHp, int defense, int maxShield)
    {
        _attackDamage = attackDamage;
        _moveSpeed = moveSpeed;
        _attackSpeed = attackSpeed;

        _health = new Health(maxHp, defense, maxShield);
        _currentHp.Value = maxHp;
        _maxHp.Value = maxHp;

        UpdateNetworkShield();

        _knockback = GetComponent<IKnockbackable>();
    }

    #region 넉백
    IKnockbackable _knockback;

    /// <summary>
    /// 넉백 진입점. 서버 가드 등 공통 규칙은 여기서만 처리한다.
    /// 기본 동작은 IKnockbackable 컴포넌트 위임, 예외는 OnKnockback을 override.
    /// </summary>
    public void Knockback(Vector3 direction, float strength)
    {
        if (!IsServer) return;

        OnKnockback(direction, strength);
    }

    protected virtual void OnKnockback(Vector3 direction, float strength)
    {
        if (_knockback == null)
        {
            Debug.LogError($"[Unit] {name}: IKnockbackable 컴포넌트가 없어 넉백이 무시됩니다.", this);
            return;
        }

        _knockback.ApplyKnockback(direction, strength);
    }

    #endregion
}
