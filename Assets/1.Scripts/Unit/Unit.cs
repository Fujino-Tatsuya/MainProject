using UnityEngine;
using Unity.Netcode;
using BaseNetCode;

public class Unit : BaseNetworkBehaviour
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
    public int CurrentHealth { get { return _health.CurrentHealth; } }
    public int MaxHp { get { return _health.MaxHp; } }
    protected NetworkVariable<int> _currentHp = new NetworkVariable<int>(
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
    public virtual void TakeDamage(int damage)
    {
        if (!IsServer) return; // 서버에서만 피해 처리
        int remainingDamage = damage;

        // 방어력으로 피해를 먼저 처리하고 남은 데미지 계산
        remainingDamage = Mathf.Max(remainingDamage - _health.CurrentDefense, 0);

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

    #region 상태 이상
    StatusEffectType _statusEffectType = StatusEffectType.None;
    public StatusEffectType StatusEffectType { get { return _statusEffectType; } }
    /// <summary>
    /// statusEffectType 값을 변경하는 함수
    /// BitMaskHelper를 사용하여 나온 값을 newStatusEffectType으로 전달하여 상태 이상 효과를 변경할 수 있도록 함
    /// </summary>
    /// <param name="newStatusEffectType">변경할 새로운 상태 이상 타입 값</param>
    public void ChangeStatusEffectType(StatusEffectType newStatusEffectType)
    {
        if (!IsServer) return; // 서버에서만 상태 이상 변경 처리
        _statusEffectType = newStatusEffectType;
    }
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
    public void TakeDamageRpc(int damage, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        TakeDamage(damage);
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

    [Rpc(SendTo.Server)]
    public void ChangeStatusEffectTypeRpc(StatusEffectType newStatusEffectType, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        ChangeStatusEffectType(newStatusEffectType);
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

        UpdateNetworkShield();

        _knockback = GetComponent<IKnockbackable>();
    }

    #region 넉백
    IKnockbackable _knockback;
    public void Knockback(Vector3 direction, float strength)
    {
        if (!IsServer) return;

        _knockback?.ApplyKnockback(direction, strength);
    }
    #endregion
}