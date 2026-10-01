using System.Collections.Generic;
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
    // 보호막은 종류·출처별 인스턴스 목록이다. 합계가 곧 "현재 쉴드"다.
    // 피해는 만료가 가장 빠른 인스턴스부터 깎는다(만료 없는 것은 맨 뒤). 시간은 호출자가 넘긴다(순수 로직 — EditMode 테스트 대상).
    readonly List<ShieldInstance> _shields = new List<ShieldInstance>();
    public IReadOnlyList<ShieldInstance> Shields => _shields;

    public int CurrentShield
    {
        get
        {
            long total = 0;
            for (int i = 0; i < _shields.Count; i++)
                total += _shields[i].amount;
            return (int)Mathf.Min(total, int.MaxValue);
        }
    }
    public bool HasShield => _shields.Count > 0;

    /// <summary>
    /// 보호막 인스턴스를 추가한다. 종류의 쌓임 규칙이 Replace 면 같은 종류·출처의 기존 인스턴스를 먼저 지운다.
    /// </summary>
    /// <param name="duration">0 이하면 시간 만료 없음</param>
    public void AddShield(ShieldType type, ulong sourceId, int amount, float duration, double now)
    {
        if (amount <= 0)
            return;

        if (ShieldTypePolicies.Of(type) == ShieldStackPolicy.Replace)
            _shields.RemoveAll(s => s.type == type && s.sourceId == sourceId);

        _shields.Add(new ShieldInstance
        {
            type = type,
            sourceId = sourceId,
            amount = amount,
            grantedAmount = amount,
            expireTime = duration > 0f ? now + duration : 0.0,
        });
    }

    /// <returns>제거했는가</returns>
    public bool RemoveShield(ShieldType type, ulong sourceId)
    {
        return _shields.RemoveAll(s => s.type == type && s.sourceId == sourceId) > 0;
    }

    public bool ContainsShield(ShieldType type, ulong sourceId)
    {
        for (int i = 0; i < _shields.Count; i++)
        {
            if (_shields[i].type == type && _shields[i].sourceId == sourceId)
                return true;
        }
        return false;
    }

    /// <summary>모든 보호막을 지운다. 지운 인스턴스를 removed 에 담는다(null 허용).</summary>
    public void ClearShields(List<ShieldInstance> removed)
    {
        removed?.AddRange(_shields);
        _shields.Clear();
    }

    /// <summary>
    /// damage 만큼 보호막을 깎는다 — 만료가 가장 빠른 인스턴스부터. 0 이 된 인스턴스는 depleted 에 담고 제거한다.
    /// </summary>
    /// <returns>보호막이 실제로 흡수한 양</returns>
    public int TakeShieldDamage(int damage, List<ShieldInstance> depleted)
    {
        int absorbed = 0;
        while (damage > 0 && _shields.Count > 0)
        {
            int index = EarliestExpiryIndex();
            ShieldInstance shield = _shields[index];

            int take = Mathf.Min(damage, shield.amount);
            shield.amount -= take;
            damage -= take;
            absorbed += take;

            if (shield.amount <= 0)
            {
                _shields.RemoveAt(index);
                depleted?.Add(shield);
            }
            else
            {
                _shields[index] = shield;
            }
        }

        Edit.Log($"[Unit] 쉴드 피해량: {absorbed}   /   현재 쉴드: {CurrentShield}");
        return absorbed;
    }

    /// <summary>now 기준으로 만료된 인스턴스를 제거해 expired 에 담는다.</summary>
    /// <returns>하나라도 제거했는가</returns>
    public bool RemoveExpiredShields(double now, List<ShieldInstance> expired)
    {
        bool removedAny = false;
        for (int i = _shields.Count - 1; i >= 0; i--)
        {
            if (_shields[i].HasExpiry && now >= _shields[i].expireTime)
            {
                expired?.Add(_shields[i]);
                _shields.RemoveAt(i);
                removedAny = true;
            }
        }
        return removedAny;
    }

    int EarliestExpiryIndex()
    {
        int best = 0;
        for (int i = 1; i < _shields.Count; i++)
        {
            ShieldInstance candidate = _shields[i];
            ShieldInstance current = _shields[best];
            // 만료 없는 보호막은 가장 늦게 소모한다. 같으면 먼저 생긴 것(앞 인덱스)이 먼저.
            if (!candidate.HasExpiry)
                continue;
            if (!current.HasExpiry || candidate.expireTime < current.expireTime)
                best = i;
        }
        return best;
    }
    #endregion

    public Health(int maxHp, int defense)
    {
        _maxHp = maxHp;
        _currentHp = maxHp;
        _currentDefense = defense;
    }
}