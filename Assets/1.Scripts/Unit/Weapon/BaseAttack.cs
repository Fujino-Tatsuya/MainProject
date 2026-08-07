using Unity.Netcode;
using UnityEngine;

// 공격의 출처 분류. 슬롯(Q/E/R)까지 나누지 않는다 — 구분해서 읽는 코드가 없었고,
// "인터럽트인가"는 슬롯과 직교한 능력이라 AttackInfo.isInterruptAttack이 따로 싣는다.
//
// ⚠️ 값은 반드시 끝에만 추가할 것. BaseAttack.attackType과 Bomb.attackType이 [SerializeField]라
// 프리팹·SO에 정수로 직렬화돼 있다 — 중간 삽입/삭제는 기존 에셋의 공격 타입을 조용히 밀어버린다.
// (None=0·Default=1 의 정수값은 기존 에셋 25곳과 맞춰 고정된 것이다. 예: Bomb.attackType=1)
public enum AttackType
{
    None,     // 미지정 (BaseAttack 기본값)
    Default,  // 평타
    Skill     // 스킬 전반
}

public struct AttackInfo
{
    public int damage;
    public AttackType attackType;

    /// <summary>
    /// 이 히트가 인터럽트 공격인가. 공격자가 아는 사실만 싣는다 —
    /// <b>이걸로 무엇을 할지는 맞는 쪽이 정한다</b>: 몬스터/중간보스는 maxGroggyCount까지 누적해 그로기,
    /// 보스(No.23)는 카운터 창·정면 각도로 그로기/Break 판정.
    /// </summary>
    public bool isInterruptAttack;

    // 지속넉백/경직(CC) — 값은 공격(스킬) 측이 지정한다(PLAN C-1). 0 = 해당 효과 없음.
    // 수신측 반응 시퀀스(MonsterState.Knockback → Stunned)는 MonsterBase가 처리한다.
    public float knockbackStrength;   // 지속 밀기 속도(m/s). 0이면 넉백 없음.
    public float knockbackDuration;   // 지속 밀기 시간(초).
    public float staggerDuration;     // 넉백 종료 후 Stunned 경직 시간(초).
    // 밀기 방향(월드, 수평). zero면 수신측이 방사형(몹-공격자)으로 계산한다.
    // 방향성 공격(Q 전진 견인 등)은 반드시 명시할 것 — 이동하는 시전자의 방사형 계산은
    // 시전자가 대상을 따라잡는 순간 옆/뒤로 뒤집힌다.
    public Vector3 knockbackDirection;

    public AttackInfo(int damage, AttackType attackType = AttackType.None, bool isInterruptAttack = false,
        float knockbackStrength = 0f, float knockbackDuration = 0f, float staggerDuration = 0f,
        Vector3 knockbackDirection = default)
    {
        this.damage = Mathf.Max(0, damage);
        this.attackType = attackType;
        this.isInterruptAttack = isInterruptAttack;
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

    // 인터럽트 여부는 여기서 저작하지 않는다. 현재 인터럽트를 거는 주체는 스킬(단죄의 방패)뿐이고
    // 스킬은 BaseAttack을 타지 않는다 — 켤 수 없는 토글을 남기면 "체크했는데 왜 안 걸리지"가 된다.
    // 적 공격도 인터럽트를 걸어야 하면 그때 [SerializeField] bool + 생성자 인자를 되살린다.

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
        _attackInfo = new AttackInfo(damage, attackType);
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
            ? new AttackInfo(overrideDamage.Value, attackType)
            : _attackInfo;
    }

    private AttackHitContext CreateHitContext(Collider hit)
    {
        return new AttackHitContext(transform.position, transform, hit, AttackSourceUnit);
    }
}
