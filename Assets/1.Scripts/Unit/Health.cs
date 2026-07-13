using Unity.Netcode;
using UnityEngine;

public class Health
{
    #region �� ü��
    int _currentHp;
    public int CurrentHealth { get { return _currentHp; } }
    int _maxHp;
    public int MaxHp { get { return _maxHp; } }
    /// <summary>
    /// damage��ŭ ü���� ���ҽ�Ű�� �Լ�
    /// </summary>
    /// <param name="damage">���ҽ�ų ü�� ��</param>
    public void TakeHpDamage(int damage)
    {
        //if(!isServer) return; // ���������� ü�� ���� ó��

        _currentHp -= damage;
        _currentHp = Mathf.Max(_currentHp, 0); // ü���� 0 ���Ϸ� �������� �ʵ��� ����
        Edit.Log($"���ط�: {damage}   /   ���� ü��: {_currentHp}");
    }
    /// <summary>
    /// healAmount��ŭ ü���� ȸ����Ű�� �Լ�
    /// </summary>
    /// <param name="healAmount">ȸ����ų ü�� ��</param>
    public void HealHp(int healAmount)
    {
        _currentHp += healAmount;
        _currentHp = Mathf.Min(_currentHp, _maxHp); // ü���� �ִ� ü���� �ʰ����� �ʵ��� ����

        Edit.Log($"ü�� ������: {healAmount}   /   ���� ü��: {_currentHp}");
    }
    /// <summary>
    /// ü���� �ִ�ġ�� ȸ����Ű�� �Լ�
    /// </summary>
    public void Revive()
    {
        _currentHp = _maxHp;
    }
    #endregion

    #region ����
    int _currentDefense;
    public int CurrentDefense { get { return _currentDefense; } }

    /// <summary>
    /// decreaseAmount��ŭ ������ ���ҽ�Ű�� �Լ�
    /// </summary>
    /// <param name="decreaseAmount">���ҽ�ų ü�� ��</param>
    public void DecreaseDefense(int decreaseAmount)
    {
        _currentDefense -= decreaseAmount;
        _currentDefense = Mathf.Max(_currentDefense, 0); // ������ 0 ���Ϸ� �������� �ʵ��� ����
    }

    /// <summary>
    /// defenseAmount��ŭ ������ ������Ű�� �Լ�
    /// </summary>
    /// <param name="increaseAmount">������ų ���� ��</param>
    public void IncreaseDefense(int increaseAmount)
    {
        _currentDefense += increaseAmount;
    }
    #endregion

    #region ����
    int _currentShield;
    public int CurrentShield { get { return _currentShield; } }
    int _maxShield;
    public int MaxShield { get { return _maxShield; } }
    bool _hasShield = false;
    public bool HasShield { get { return _hasShield; } }

    /// <summary>
    /// damage��ŭ ���带 ���ҽ�Ű�� ���尡 0 ���Ϸ� �������� �ʵ��� �����ϴ� �Լ�
    /// </summary>
    /// <param name="damage">���ҽ�ų ���� ��</param>
    public void TakeShieldDamage(int damage)
    {
        _currentShield -= damage;
        _currentShield = Mathf.Max(_currentShield, 0); // ���尡 0 ���Ϸ� �������� �ʵ��� ����
        _hasShield = (_currentShield > 0)? true : false;
    }

    /// <summary>
    /// shieldValue�� ���� ���� �����ϰ� ���� ���� 0���� ũ�� hasShield�� true��, �׷��� ������ false�� �����ϴ� �Լ�
    /// </summary>
    /// <param name="shieldValue">������ ���� ��</param>
    public void SetShield(int shieldValue)
    {
        _currentShield = shieldValue;
        _hasShield = (shieldValue > 0)? true : false;
    }

    /// <summary>
    /// shieldAmount��ŭ ���� ���� ������Ű�� ���� ���� 0���� ũ�� hasShield�� true��, �׷��� ������ false�� �����ϴ� �Լ�
    /// </summary>
    /// <param name="shieldAmount">������ų ���� ��</param>
    public void IncreaseShield(int shieldAmount)
    {
        _currentShield += shieldAmount;
        _currentShield = Mathf.Min(_currentShield, _maxShield); // ���尡 �ִ� ���带 �ʰ����� �ʵ��� ����
        _hasShield = (_currentShield > 0)? true : false;

        Edit.Log($"���� ������: {shieldAmount}   /   ���� ����: {_currentShield}");
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