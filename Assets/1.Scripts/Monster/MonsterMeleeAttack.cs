using System.Collections.Generic;
using UnityEngine;

// 몬스터 근접 공격 판정. BaseAttack(기존 계약)을 상속해 데미지/타깃레이어/AttackInfo 파이프라인을 그대로 쓴다.
// OverlapAttack과 동일한 ColliderInfo 오버랩 패턴을 따르되, 넉백까지 책임진다는 점이 다르다.
//
// 히트 판정 진입점 Hit():
//  - 서버 애니메이션 이벤트 프레임에서 호출하거나(권장, Animator 확정 후),
//  - Animator/이벤트가 아직 없으면 MonsterBase가 선딜(attackWindup) 후 코드로 직접 호출한다.
// 두 경로 모두 서버에서만 실효(BaseAttack.IsServer 가드 + TryResolveHit 내부 가드).
public class MonsterMeleeAttack : BaseAttack
{
    [SerializeField] private ColliderInfo colliderInfo; // 오버랩 형태/크기(자식 콜라이더에서 추출)
    [SerializeField] private int maxHitCount = 16;

    [Header("넉백(선택)")]
    [SerializeField] private bool applyKnockback = false;
    [SerializeField] private float knockbackStrength = 5f;

    private Collider[] _results;

    // 히트 윈도우: 열려 있는 동안 같은 Unit은 1회만 피격(대시/지속 공격의 유닛당 1틱 보장).
    private bool _hitWindowOpen;
    private readonly HashSet<Unit> _windowHits = new HashSet<Unit>();

    // 지속 공격(예: SpinnerBot 스핀 대시) 시작 시 열고, 끝나면 닫는다.
    public void BeginHitWindow() { _hitWindowOpen = true; _windowHits.Clear(); }
    public void EndHitWindow() { _hitWindowOpen = false; _windowHits.Clear(); }

    private void Awake()
    {
        InitializeAttackInfo();
        _results = new Collider[Mathf.Max(1, maxHitCount)];
    }

    // 서버 오버랩 판정 → 대상 Hurtbox/Unit.ReceiveAttack. 넉백 옵션이면 추가로 Unit.Knockback.
    public void Hit()
    {
        if (!IsServer)
            return;

        if (colliderInfo == null)
        {
            Debug.LogError("MonsterMeleeAttack에 ColliderInfo가 필요합니다.", this);
            return;
        }

        int hitCount = Overlap();
        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = _results[i];
            if (hit == null)
                continue;

            // 히트 윈도우 중이면 이미 맞은 Unit은 스킵(유닛당 1틱).
            if (_hitWindowOpen)
            {
                Unit already = hit.GetComponentInParent<Unit>();
                if (already != null)
                {
                    if (_windowHits.Contains(already))
                        continue;
                    _windowHits.Add(already);
                }
            }

            bool applied = TryResolveHit(hit);
            if (!applied || !applyKnockback || hit == null)
                continue;

            Unit unit = hit.GetComponentInParent<Unit>();
            if (unit == null)
                continue;

            Vector3 dir = unit.transform.position - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
                unit.Knockback(dir.normalized, knockbackStrength);
        }
    }

    private int Overlap()
    {
        switch (colliderInfo.OverlapCollider)
        {
            case OverlapCollider.Box:
            {
                BoxColliderInfo info = default;
                colliderInfo.GetBoxColliderInfo(ref info);
                return Physics.OverlapBoxNonAlloc(
                    info.center,
                    info.halfExtents,
                    _results,
                    info.orientation,
                    targetLayer,
                    QueryTriggerInteraction.Collide);
            }

            case OverlapCollider.Sphere:
            {
                SphereColliderInfo info = default;
                colliderInfo.GetSphereColliderInfo(ref info);
                return Physics.OverlapSphereNonAlloc(
                    info.center,
                    info.radius,
                    _results,
                    targetLayer,
                    QueryTriggerInteraction.Collide);
            }

            case OverlapCollider.Capsule:
            {
                CapsuleColliderInfo info = default;
                colliderInfo.GetCapsuleColliderInfo(ref info);
                return Physics.OverlapCapsuleNonAlloc(
                    info.point0,
                    info.point1,
                    info.radius,
                    _results,
                    targetLayer,
                    QueryTriggerInteraction.Collide);
            }

            default:
                return 0;
        }
    }
}
