using UnityEngine;
using Unity.Netcode;

public class Unit : NetworkBehaviour
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
    Health _health;
    NetworkVariable<int> _currentHp = new NetworkVariable<int>();
    NetworkVariable<int> _currentShield = new NetworkVariable<int>();
    NetworkVariable<bool> _hasShield = new NetworkVariable<bool>();

    /// <summary>
    /// damage만큼 방어력을 반영하여 쉴드와 체력을 감소시키는 함수
    /// </summary>
    /// <param name="damage">감소시킬 피해 값</param>
    public void TakeDamage(int damage)
    {
        if (!IsServer) return; // 서버에서만 피해 처리
        int remainingDamage = damage;

        // 방어력으로 피해를 먼저 처리하고 남은 데미지 계산
        remainingDamage = Mathf.Max(remainingDamage - _health.CurrentDefense, 0);

        // 쉴드가 있으면 쉴드로 피해를 처리하고 남은 데미지 계산
        if (_health.HasShield)
        {
            int shieldDamage = _health.CurrentShield - remainingDamage;
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

    public Unit(int attackDamage, float moveSpeed, float attackSpeed, int maxHp, int defense, int maxShield)
    {
        _attackDamage = attackDamage;
        _moveSpeed = moveSpeed;
        _attackSpeed = attackSpeed;

        _health = new Health(maxHp, defense, maxShield);
        _currentHp = new NetworkVariable<int>(maxHp);

        UpdateNetworkShield();
    }
}