using Unity.Netcode;
using UnityEngine;

public enum AttackType
{
    None,
    Default,
    Q,
    E,
    R
}

public struct AttackInfo
{
    public int damage;
    public AttackType attackType;
    public bool isGroggyAttack;

    // 지속넉백/경직(CC) — 값은 공격(스킬) 측이 지정한다(PLAN C-1). 0 = 해당 효과 없음.
    // 수신측 반응 시퀀스(MonsterState.Knockback → Stunned)는 MonsterBase가 처리한다.
    public float knockbackStrength;   // 지속 밀기 속도(m/s). 0이면 넉백 없음.
    public float knockbackDuration;   // 지속 밀기 시간(초).
    public float staggerDuration;     // 넉백 종료 후 Stunned 경직 시간(초).
    // 밀기 방향(월드, 수평). zero면 수신측이 방사형(몹-공격자)으로 계산한다.
    // 방향성 공격(Q 전진 견인 등)은 반드시 명시할 것 — 이동하는 시전자의 방사형 계산은
    // 시전자가 대상을 따라잡는 순간 옆/뒤로 뒤집힌다.
    public Vector3 knockbackDirection;

    public AttackInfo(int damage, AttackType attackType = AttackType.None, bool isGroggyAttack = false,
        float knockbackStrength = 0f, float knockbackDuration = 0f, float staggerDuration = 0f,
        Vector3 knockbackDirection = default)
    {
        this.damage = Mathf.Max(0, damage);
        this.attackType = attackType;
        this.isGroggyAttack = isGroggyAttack;
        this.knockbackStrength = Mathf.Max(0f, knockbackStrength);
        this.knockbackDuration = Mathf.Max(0f, knockbackDuration);
        this.staggerDuration = Mathf.Max(0f, staggerDuration);
        this.knockbackDirection = knockbackDirection;
    }
}

public struct AttackHitContext
{
    public Vector3 sourcePosition;
    public Transform sourceTransform;
    public Collider hitCollider;
    public Unit sourceUnit;

    public AttackHitContext(
        Vector3 sourcePosition,
        Transform sourceTransform = null,
        Collider hitCollider = null,
        Unit sourceUnit = null)
    {
        this.sourcePosition = sourcePosition;
        this.sourceTransform = sourceTransform;
        this.hitCollider = hitCollider;
        this.sourceUnit = sourceUnit;
    }
}

public class BaseAttack : MonoBehaviour, IDamageSettable
{
    [SerializeField] protected int damage = 0;
    public int Damage { get { return damage; } }

    [SerializeField] protected bool isGroggyAttack = false;
    public bool IsGroggyAttack { get { return isGroggyAttack; } }

    [SerializeField] protected LayerMask targetLayer;

    [SerializeField] protected AttackType attackType = AttackType.None;
    public AttackType AttackType { get { return attackType; } }

    protected AttackInfo _attackInfo;

    /// <summary>
    /// 공격자 식별용 메타데이터. 플레이어 계층에서 분리되는 투사체는 owner로 재정의한다.
    /// 피해 판정에는 사용하지 않는다.
    /// </summary>
    protected virtual Unit AttackSourceUnit => GetComponentInParent<Unit>();

    protected bool IsServer =>
        NetworkManager.Singleton == null ||
        !NetworkManager.Singleton.IsListening ||
        NetworkManager.Singleton.IsServer;

    protected void InitializeAttackInfo()
    {
        _attackInfo = new AttackInfo(damage, attackType, isGroggyAttack);
    }

    public void SetDamageSnapshot(int value)
    {
        damage = Mathf.Max(0, value);
        InitializeAttackInfo();
    }

    /// <summary>
    /// damage int 값만 설정한다. (_attackInfo 스냅샷은 갱신하지 않음)
    /// </summary>
    public void SetDamage(int value)
    {
        damage = Mathf.Max(0, value);
    }

    public void SetTargetLayer(LayerMask value)
    {
        targetLayer = value;
    }

    public void SetAttackType(AttackType value)
    {
        attackType = value;
        InitializeAttackInfo();
    }

    protected bool TryResolveHit(Collider hit, int? overrideDamage = null)
    {
        if (!IsServer || hit == null)
            return false;

        if (!IsInTargetLayer(hit))
            return false;

        Hurtbox hurtbox = hit.GetComponentInParent<Hurtbox>();
        if (hurtbox != null)
            return TryResolveHit(hurtbox, hit, overrideDamage);

        GameObject target = hit.transform.root.gameObject;
        Unit unit = hit.GetComponentInParent<Unit>();
        if (unit == null)
        {
            Debug.LogError($"[Unit] 해당 오브젝트, {target.name}에 Unit 컴포넌트가 부착되어있지 않습니다.", this);
            return false;
        }

        return TryResolveHit(unit, overrideDamage);
    }

    protected bool TryResolveHit(Unit unit, int? overrideDamage = null)
    {
        if (!IsServer || unit == null)
            return false;

        AttackInfo attackInfo = CreateAttackInfo(overrideDamage);

        bool resolved = unit.ReceiveAttack(attackInfo, CreateHitContext(null));
        LogHit(resolved, unit.name, attackInfo);
        return resolved;
    }

    protected bool TryResolveHit(Hurtbox hurtbox, int? overrideDamage = null)
    {
        if (!IsServer || hurtbox == null)
            return false;

        AttackInfo attackInfo = CreateAttackInfo(overrideDamage);
        bool resolved = hurtbox.ReceiveAttack(attackInfo, CreateHitContext(null));
        LogHit(resolved, GetTargetName(hurtbox), attackInfo);
        return resolved;
    }

    protected bool TryResolveHit(Hurtbox hurtbox, Collider hit, int? overrideDamage = null)
    {
        if (!IsServer || hurtbox == null)
            return false;

        AttackInfo attackInfo = CreateAttackInfo(overrideDamage);
        bool resolved = hurtbox.ReceiveAttack(attackInfo, CreateHitContext(hit));
        LogHit(resolved, GetTargetName(hurtbox), attackInfo);
        return resolved;
    }

    void LogHit(bool resolved, string targetName, AttackInfo attackInfo)
    {
        if (!resolved)
            return;

        Edit.Log($"[Attack] {name} -> {targetName} 적중 (피해 {attackInfo.damage}, {attackInfo.attackType})", this);
    }

    static string GetTargetName(Hurtbox hurtbox)
    {
        return hurtbox.TryGetOwner(out Unit owner) ? owner.name : hurtbox.name;
    }

    protected bool TryGetHurtbox(Collider hit, out Hurtbox hurtbox)
    {
        hurtbox = null;
        if (hit == null || !IsInTargetLayer(hit))
            return false;

        hurtbox = hit.GetComponentInParent<Hurtbox>();
        return hurtbox != null;
    }

    protected bool IsInTargetLayer(int layer)
    {
        return (targetLayer.value & (1 << layer)) != 0;
    }

    protected bool IsInTargetLayer(Collider hit)
    {
        if (hit == null)
            return false;

        if (IsInTargetLayer(hit.gameObject.layer))
            return true;

        return IsInTargetLayer(hit.transform.root.gameObject.layer);
    }

    private AttackInfo CreateAttackInfo(int? overrideDamage)
    {
        return overrideDamage.HasValue
            ? new AttackInfo(overrideDamage.Value, attackType, isGroggyAttack)
            : _attackInfo;
    }

    private AttackHitContext CreateHitContext(Collider hit)
    {
        return new AttackHitContext(transform.position, transform, hit, AttackSourceUnit);
    }
}
