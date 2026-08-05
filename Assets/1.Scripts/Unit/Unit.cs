using BaseNetCode;
using System;
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
    public int CurrentShield { get { return _health != null ? _health.CurrentShield : _currentShield.Value; } }
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
    bool _deathNotified;

    /// <summary>
    /// 서버에서 생존 상태의 체력이 0으로 전환될 때 한 번 발생한다.
    /// Revive 호출 전까지 같은 사망에 대해 다시 발생하지 않는다.
    /// </summary>
    public event Action Died;

    /// <summary>
    /// damage만큼 방어력을 반영하여 쉴드와 체력을 감소시키는 함수
    /// </summary>
    /// <param name="damage">감소시킬 피해 값</param>
    protected void TakeDamage(int damage)
    {
        ApplyHealthDamage(damage, false);
    }

    /// <summary>
    /// 피해 적용 전에 파생 Unit이 무적 등 공통 차단 규칙을 검사하는 지점.
    /// </summary>
    /// <param name="damage">방어력 적용 전 피해량</param>
    protected virtual bool CanApplyHealthDamage(int damage)
    {
        return true;
    }

    void ApplyHealthDamage(int damage, bool ignoreDefenseAndShield)
    {
        if (!IsServer || _health == null || damage <= 0) return;

        // 진단 — 여기서 조용히 버려지는 피해가 "때려도 안 맞는다"로 보인다(2026-07-30).
        // ReceiveAttack 은 무조건 true 를 반환하므로 공격 측은 [Attack] … 적중 을 찍고,
        // 피해만 사라져서 로그상 성공처럼 보인다. 누가 무엇을 거부했는지 남긴다.
        if (!CanApplyHealthDamage(damage))
        {
            Edit.LogWarning(
                $"[Unit/진단] {name} 이 피해 {damage} 를 거부했다 — CanApplyHealthDamage=false " +
                $"(현재 체력 {_health.CurrentHealth}/{FinalMaxHp})", this);
            return;
        }

        int previousHealth = _health.CurrentHealth;

        if (ignoreDefenseAndShield)
        {
            _health.TakeHpDamage(damage);
        }
        else
        {
            ApplyMitigatedHealthDamage(damage);
        }

        _currentHp.Value = _health.CurrentHealth;

        // 진단 — Health 의 기존 로그는 대상 이름이 없어서 누구의 체력이 줄었는지 알 수 없었다.
        // 요청값과 실제 감소량을 함께 남긴다(경감으로 1까지 깎이는 경우를 가른다).
        Edit.Log(
            $"[Unit/진단] {name} 피해 적용 — 요청 {damage}, 실제 감소 {previousHealth - _health.CurrentHealth}, " +
            $"체력 {previousHealth} → {_health.CurrentHealth}/{FinalMaxHp}", this);

        NotifyDeathTransition(previousHealth);
    }

    void ApplyMitigatedHealthDamage(int damage)
    {
        int remainingDamage = damage;

        //// 방어력 경감률 적용: 최종 피해 = 피해 x 100 / (100 + 방어력), 방어력 100당 50% 경감
        remainingDamage = Mathf.RoundToInt(remainingDamage * 100f / (100f + _health.CurrentDefense));

        // 쉴드가 있으면 쉴드로 피해를 처리하고 남은 데미지 계산
        if (_health.HasShield)
        {
            int shieldDamage = Mathf.Min(remainingDamage, _health.CurrentShield);
            _health.TakeShieldDamage(shieldDamage);

            UpdateNetworkShield();

            remainingDamage -= shieldDamage;
        }

        // 남은 피해는 체력으로 처리
        if (remainingDamage > 0)
        {
            _health.TakeHpDamage(remainingDamage);
        }
    }

    void NotifyDeathTransition(int previousHealth)
    {
        if (_deathNotified || previousHealth <= 0 || _health.CurrentHealth > 0)
            return;

        _deathNotified = true;
        Died?.Invoke();
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
    /// 방어력과 쉴드를 무시하고 HP에 직접 피해를 적용한다.
    /// </summary>
    public void ApplyDirectHealthDamage(int damage)
    {
        ApplyHealthDamage(damage, true);
    }

    /// <summary>
    /// 최종 최대 체력 비율만큼 계산한 피해를 일반 방어력/쉴드 경로로 적용한다.
    /// </summary>
    public void ApplyMaxHealthPercentDamage(float ratio)
    {
        int damage = Mathf.CeilToInt(FinalMaxHp * Mathf.Max(0f, ratio));
        ApplyHealthDamage(damage, false);
    }

    /// <summary>
    /// 현재 체력 비율만큼 계산한 피해를 일반 방어력/쉴드 경로로 적용한다.
    /// </summary>
    public void ApplyCurrentHealthPercentDamage(float ratio)
    {
        int damage = Mathf.CeilToInt(CurrentHealth * Mathf.Max(0f, ratio));
        ApplyHealthDamage(damage, false);
    }

    /// <summary>
    /// 현재 쉴드를 모두 제거한다.
    /// </summary>
    public void BreakShield()
    {
        if (!IsServer || _health == null) return;

        _health.SetShield(0);
        UpdateNetworkShield();
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
        _deathNotified = false;
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

    // 읽기 파사드 — 플레이어(StatusEffectController)와 몹(MonsterStatusEffect)을 공통으로 잡는다.
    // Unit이 소비하는 표면(HasSuperArmor/GetStatMultiplier)은 이 파사드 경유 → 몹 슈퍼아머도 Knockback 가드에 걸림.
    // (StatusEffects 공개 프로퍼티는 플레이어 전용 쓰기 API(Apply/Remove) 호출부가 있어 구체 타입 유지.)
    IStatusEffectFacade _statusFacade;
    bool _statusFacadeCached;
    IStatusEffectFacade StatusFacade
    {
        get
        {
            if (!_statusFacadeCached)
            {
                _statusFacade = GetComponent<IStatusEffectFacade>();
                _statusFacadeCached = true;
            }
            return _statusFacade;
        }
    }

    float GetStatMultiplier(StatusEffectType statType)
    {
        return StatusFacade != null ? StatusFacade.GetStatMultiplier(statType) : 1f;
    }

    // 미부착 유닛은 슈퍼아머 없음으로 동작
    public bool HasSuperArmor => StatusFacade != null && StatusFacade.HasSuperArmor;

    // 최종 스탯 = base(불변) × 활성 modifier 배율의 곱. 소비처는 base 대신 이 값을 읽는다
    public int FinalAttackDamage => Mathf.Max(0, Mathf.RoundToInt(_attackDamage * GetStatMultiplier(StatusEffectType.AttackDamageModifier)));
    public float FinalMoveSpeed => Mathf.Max(0f, _moveSpeed * GetStatMultiplier(StatusEffectType.MoveSpeedModifier));
    public float FinalAttackSpeed => Mathf.Max(0f, _attackSpeed * GetStatMultiplier(StatusEffectType.AttackSpeedModifier));
    // 방어력은 _health가 서버에서만 생성되므로 서버에서만 유효
    public int FinalDefense => Mathf.Max(0, Mathf.RoundToInt((_health != null ? _health.CurrentDefense : 0) * GetStatMultiplier(StatusEffectType.DefenseModifier)));
    public int FinalMaxHp => Mathf.Max(0, Mathf.RoundToInt(MaxHp * GetStatMultiplier(StatusEffectType.MaxHpModifier)));
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
    public void Initialize(int attackDamage, float moveSpeed, float attackSpeed, int maxHp, int defense)
    {
        _attackDamage = attackDamage;
        _moveSpeed = moveSpeed;
        _attackSpeed = attackSpeed;

        _health = new Health(maxHp, defense);
        _currentHp.Value = maxHp;
        _maxHp.Value = maxHp;
        _deathNotified = false;

        UpdateNetworkShield();

        _knockback = GetComponent<IKnockbackable>();
    }

    #region 클라이언트 피격 알림 (복제 기반 — 피격 플래시/HUD 등 로컬 연출용)
    /// <summary>
    /// 모든 피어에서 HP 또는 쉴드가 "감소"했을 때 발생(NetworkVariable 복제 기반 → RPC 불필요).
    /// 피격 플래시(HitFlash) 등 판정과 무관한 로컬 연출이 구독한다.
    /// </summary>
    public event System.Action ClientDamaged;

    /// <summary>
    /// 모든 피어에서 HP 복제값이 바뀔 때 발생(감소·증가 전부). 인자는 (previous, next).
    /// 지연 체력바처럼 피해량과 회복 시점을 모두 알아야 하는 로컬 연출이 구독한다.
    /// 실드 감소는 여기서 나오지 않는다(ClientDamaged 참조).
    /// </summary>
    public event System.Action<int, int> ClientHpChanged;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _currentHp.OnValueChanged += OnHpReplicated;
        _currentShield.OnValueChanged += OnShieldReplicated;

        // 피격 플래시 자동 부착 — Unit 계열 전체 공통(프리팹에 미리 붙어 있으면 그대로 사용).
        if (GetComponent<HitFlash>() == null)
            gameObject.AddComponent<HitFlash>();
    }

    public override void OnNetworkDespawn()
    {
        _currentHp.OnValueChanged -= OnHpReplicated;
        _currentShield.OnValueChanged -= OnShieldReplicated;
        base.OnNetworkDespawn();
    }

    void OnHpReplicated(int previous, int next)
    {
        ClientHpChanged?.Invoke(previous, next);
        if (next < previous) ClientDamaged?.Invoke();
    }

    void OnShieldReplicated(int previous, int next)
    {
        if (next < previous) ClientDamaged?.Invoke();
    }
    #endregion

    #region 넉백
    IKnockbackable _knockback;

    /// <summary>
    /// 넉백 진입점. 서버 가드·슈퍼아머 등 공통 규칙은 여기서만 처리한다.
    /// 기본 동작은 IKnockbackable 컴포넌트 위임, 예외는 OnKnockback을 override.
    /// </summary>
    public void Knockback(Vector3 direction, float strength)
    {
        if (!IsServer) return;
        if (HasSuperArmor) return;

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
