using Unity.Netcode;
using UnityEngine;

public class Health
{
    #region 피 체력
    int _currentHp;
    public int CurrentHealth { get { return _currentHp; } }
    int _maxHp;
    public int MaxHp { get { return _maxHp; } }
    /// <summary>
    /// damage만큼 체력을 감소시키는 함수
    /// </summary>
    /// <param name="damage">감소시킬 체력 값</param>
    public void TakeHpDamage(int damage)
    {
        //if(!isServer) return; // 서버에서만 체력 감소 처리

        _currentHp -= damage;
        _currentHp = Mathf.Max(_currentHp, 0); // 체력이 0 이하로 떨어지지 않도록 보장
        Edit.Log($"[Unit] 피해량: {damage}   /   현재 체력: {_currentHp}");
    }
    /// <summary>
    /// healAmount만큼 체력을 회복시키는 함수
    /// </summary>
    /// <param name="healAmount">회복시킬 체력 값</param>
    public void HealHp(int healAmount)
    {
        _currentHp += healAmount;
        _currentHp = Mathf.Min(_currentHp, _maxHp); // 체력이 최대 체력을 초과하지 않도록 보장

        Edit.Log($"[Unit] 체력 증가량: {healAmount}   /   현재 체력: {_currentHp}");
    }
    /// <summary>
    /// 체력을 최대치로 회복시키는 함수
    /// </summary>
    public void Revive()
    {
        _currentHp = _maxHp;
    }
    #endregion

    #region 방어력
    int _currentDefense;
    public int CurrentDefense { get { return _currentDefense; } }

    /// <summary>
    /// decreaseAmount만큼 방어력을 감소시키는 함수
    /// </summary>
    /// <param name="decreaseAmount">감소시킬 체력 값</param>
    public void DecreaseDefense(int decreaseAmount)
    {
        _currentDefense -= decreaseAmount;
        _currentDefense = Mathf.Max(_currentDefense, 0); // 방어력이 0 이하로 떨어지지 않도록 보장
    }

    /// <summary>
    /// defenseAmount만큼 방어력을 증가시키는 함수
    /// </summary>
    /// <param name="increaseAmount">증가시킬 방어력 값</param>
    public void IncreaseDefense(int increaseAmount)
    {
        _currentDefense += increaseAmount;
    }
    #endregion

    #region 쉴드
    int _currentShield;
    public int CurrentShield { get { return _currentShield; } }
    int _maxShield;
    public int MaxShield { get { return _maxShield; } }
    bool _hasShield = false;
    public bool HasShield { get { return _hasShield; } }

    /// <summary>
    /// damage만큼 쉴드를 감소시키고 쉴드가 0 이하로 떨어지지 않도록 보장하는 함수
    /// </summary>
    /// <param name="damage">감소시킬 쉴드 값</param>
    public void TakeShieldDamage(int damage)
    {
        _currentShield -= damage;
        _currentShield = Mathf.Max(_currentShield, 0); // 쉴드가 0 이하로 떨어지지 않도록 보장
        _hasShield = (_currentShield > 0)? true : false;
        Edit.Log($"[Unit] 쉴드 피해량: {damage}   /   현재 쉴드: {_currentShield}");
    }

    /// <summary>
    /// shieldValue로 쉴드 값을 설정하고 쉴드 값이 0보다 크면 hasShield를 true로, 그렇지 않으면 false로 설정하는 함수
    /// </summary>
    /// <param name="shieldValue">설정할 쉴드 값</param>
    public void SetShield(int shieldValue)
    {
        _currentShield = shieldValue;
        _hasShield = (shieldValue > 0)? true : false;
    }

    /// <summary>
    /// shieldAmount만큼 쉴드 값을 증가시키고 쉴드 값이 0보다 크면 hasShield를 true로, 그렇지 않으면 false로 설정하는 함수
    /// </summary>
    /// <param name="shieldAmount">증가시킬 쉴드 값</param>
    public void IncreaseShield(int shieldAmount)
    {
        _currentShield += shieldAmount;
        _currentShield = Mathf.Min(_currentShield, _maxShield); // 쉴드가 최대 쉴드를 초과하지 않도록 보장
        _hasShield = (_currentShield > 0)? true : false;

        Edit.Log($"[Unit] 쉴드 증가량: {shieldAmount}   /   현재 쉴드: {_currentShield}");
    }
    #endregion

    public Health(int maxHp, int defense, int maxShield)
    {
        _maxHp = maxHp;
        _currentHp = maxHp;
        _currentDefense = defense;
        _maxShield = maxShield;
        _currentShield = 0;
        _hasShield = false;
    }
}