using UnityEngine;

public class KnockbackAttack : BaseAttack, IKnockbackSettable
{
    [SerializeField] float knockbackStrength = 5f;

    [Header("피격 연출 (선택)")]
    [Tooltip("적중 시 재생할 이펙트. 비워두면 아무 일도 하지 않는다 — 이 컴포넌트는 공용이므로 " +
             "특정 이펙트를 코드에 박지 않고 인스턴스마다 인스펙터로 지정한다")]
    [SerializeField] EffectEntry hitEffect;

    [Tooltip("전 피어 전파용 중계자(보스 루트). 없으면 연출을 재생하지 않는다.\n" +
             "이 컴포넌트는 NetworkBehaviour가 아니라 직접 RPC를 쏠 수 없다")]
    [SerializeField] AttackEffectRelay effectRelay;

    // 자기 넉백 콜라이더. 이펙트는 이 표면 위에서 재생된다.
    Collider _selfCollider;

    void Awake()
    {
        InitializeAttackInfo();
        _selfCollider = GetComponent<Collider>();
    }

    /// <summary>
    /// 넉백 세기 값만 설정한다.
    /// </summary>
    public void SetKnockbackStrength(float value)
    {
        knockbackStrength = Mathf.Max(0f, value);
    }

    public void ApplyKnockbackAttack(GameObject collidedObject)
    {
        if (!IsServer) return;

        GameObject root = collidedObject.transform.root.gameObject;
        if ((targetLayer.value & (1 << root.layer)) != 0)
        {
            Unit unit = root.GetComponent<Unit>();
            if (unit == null)
            {
                Edit.LogError($"[No.23] 해당 오브젝트, {root.name}에 Unit 컴포넌트가 부착되어있지 않습니다.", this);
                return;
            }

            unit.TakeDamage(new AttackInfo(damage, attackType));
            Vector3 dir = GetDirection(root);
            unit.Knockback(dir, knockbackStrength);
            Edit.Log($"[No.23] {name} 넉백 공격 적중: {unit.name} (피해 {damage})", this);

            // 연출은 여기서 직접 재생하지 않는다 — 이 스코프는 서버에서만 실행된다.
            // 중계자가 전 피어에 요청하고, 각 피어가 자기 로컬 콜라이더로 위치를 만든다.
            if (hitEffect != null && effectRelay != null)
                effectRelay.Broadcast(this, root.transform.position);
        }
    }

    /// <summary>
    /// [모든 피어] 자기 넉백 콜라이더 <b>표면 위</b>에서 피격 연출을 재생한다.
    /// <see cref="AttackEffectRelay"/>가 부른다.
    /// </summary>
    /// <param name="targetPosition">피격자 위치. 표면 위 어느 지점인지를 고르는 방향 힌트다</param>
    public void PlayHitEffectLocal(Vector3 targetPosition)
    {
        if (hitEffect == null || EffectManager.Instance == null) return;
        if (!TryGetSurfacePoint(targetPosition, out Vector3 point, out Quaternion rotation)) return;

        EffectManager.Instance.Play(hitEffect, point, rotation);
    }

    /// <summary>
    /// 넉백 콜라이더 표면에서 <paramref name="target"/>에 가장 가까운 점과, 바깥을 향하는 회전.
    ///
    /// ⚠️ <b>구는 수식으로 직접 푼다.</b> <c>Collider.ClosestPoint</c>는 비활성 콜라이더에서
    /// 신뢰할 수 없는데, <see cref="EnableCollider"/>가 서버에서만 콜라이더를 켜므로
    /// <b>클라이언트에서는 항상 꺼진 상태</b>다. 구는 중심·반지름만으로 정확히 계산되므로
    /// 활성 여부와 무관하게 옳다.
    /// </summary>
    bool TryGetSurfacePoint(Vector3 target, out Vector3 point, out Quaternion rotation)
    {
        point = default;
        rotation = Quaternion.identity;
        if (_selfCollider == null) return false;

        Vector3 center;
        Vector3 dir;

        if (_selfCollider is SphereCollider sphere)
        {
            center = sphere.transform.TransformPoint(sphere.center);

            // 유니티는 스케일이 축마다 다르면 가장 큰 축으로 구를 만든다.
            Vector3 s = sphere.transform.lossyScale;
            float radius = sphere.radius *
                Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));

            dir = target - center;
            if (dir.sqrMagnitude <= Mathf.Epsilon) dir = transform.forward;
            dir.Normalize();

            point = center + dir * radius;
        }
        else
        {
            // 구가 아닌 콜라이더는 정확한 대안이 없다. 활성 상태에서만 신뢰할 수 있다.
            center = _selfCollider.bounds.center;
            point = _selfCollider.ClosestPoint(target);

            dir = target - center;
            if (dir.sqrMagnitude <= Mathf.Epsilon) dir = transform.forward;
            dir.Normalize();
        }

        rotation = Quaternion.LookRotation(dir);
        return true;
    }

    Vector3 GetDirection(GameObject target)
    {
        Vector3 direction;

        Vector3 start = transform.position;
        start.y = target.transform.position.y;
        direction = target.transform.position - start;

        if (direction.sqrMagnitude <= Mathf.Epsilon)
        {
            return target.transform.TransformDirection(Vector3.back);
        }

        return direction.normalized;
    }
}
