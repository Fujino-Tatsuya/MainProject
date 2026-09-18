using System.Collections.Generic;
using UnityEngine;

// 몬스터 근접 공격 판정. BaseAttack(기존 계약)을 상속해 데미지/타깃레이어/AttackInfo 파이프라인을 그대로 쓴다.
// OverlapAttack과 동일한 ColliderInfo 오버랩 패턴을 따르되, 넉백까지 책임진다는 점이 다르다.
//
// 히트 판정 진입점 Hit():
//  - 애니메이션 이벤트 OnAttackHit → MonsterBase.NotifyAttackHit → PerformAttackHit 경로가 기본이다.
//    🔴 **타이머 폴백은 없다** — 클립에 이벤트가 없으면 이 공격은 데미지를 내지 못한다
//    (attackWindup 은 더 이상 base 가 소비하지 않는다. MonsterBase.HandleAttack 참조).
//  - 그 외에 파생 클래스가 자기 틱에서 직접 부르는 경로가 있다(지속 판정용 —
//    SpinnerBot 스핀, 23호 Dash/RageDash. BeginHitWindow 로 유닛당 1회를 보장한다).
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

    /// <summary>프리팹에 배선된 판정 형상. 파생이 앵커를 교체하기 전에 원본을 보관하는 데 쓴다.</summary>
    public ColliderInfo ColliderInfo { get { return colliderInfo; } }

    /// <summary>
    /// 판정 형상(앵커)을 교체한다. 공격 종류마다 히트박스가 다른 보스가 히트 직전에 갈아끼운다.
    /// 기존 몬스터는 호출하지 않으므로 인스펙터 배선 그대로 동작한다(SetDamageSnapshot 과 같은 계약).
    /// </summary>
    public void SetColliderInfo(ColliderInfo value)
    {
        if (value != null)
            colliderInfo = value;
    }

    private void Awake()
    {
        InitializeAttackInfo();
        _results = new Collider[Mathf.Max(1, maxHitCount)];
    }

    /// <summary>
    /// 서버 오버랩 판정 → 대상 Hurtbox/Unit.ReceiveAttack. 넉백 옵션이면 추가로 Unit.Knockback.
    /// </summary>
    /// <returns>
    /// <b>실제로 피해가 들어간 대상 수.</b> 0 이면 헛스윙이다(범위에 아무도 없었거나 전부 걸러졌다).
    ///
    /// 값을 돌려주는 이유: "때렸다"와 "맞았다"는 다른 사건인데 지금까지 구분할 방법이 없었다.
    /// 타격 연출·사운드·경직 같은 <b>명중했을 때만</b> 일어나야 하는 것들이 이 값을 본다.
    /// 안 쓰는 호출부는 그대로 문장으로 두면 된다 — 기존 호출 10곳은 손대지 않았다.
    ///
    /// ⚠️ 서버에서만 유효하다. 클라에서는 판정 자체를 하지 않으므로 <b>항상 0</b>이다 —
    /// 이 값으로 연출을 켜려면 반드시 RPC 로 내보낼 것. 여기서 직접 재생하면 호스트에서만 보인다.
    /// </returns>
    public int Hit()
    {
        if (!IsServer)
            return 0;

        if (colliderInfo == null)
        {
            Debug.LogError("MonsterMeleeAttack에 ColliderInfo가 필요합니다.", this);
            return 0;
        }

        return ApplyHits(Overlap());
    }

    /// <summary>
    /// <b>부채꼴 판정.</b> 구 오버랩 + 각도 필터 — 물리 질의에는 원뿔 형상이 없으므로 이것이 표준 방식이다.
    /// <see cref="Hit"/> 와 <b>같은 파이프라인</b>(히트 윈도우 · 데미지 스냅샷 · 넉백)을 쓰므로,
    /// 전진 경로(<see cref="Hit"/>)와 부채꼴 끝점이 섞여도 <b>한 사람은 한 번만</b> 맞는다.
    ///
    /// 🔴 <paramref name="angleDegrees"/> 는 <b>전체 각</b>이다(90 이면 정면 기준 ±45).
    ///    예고 데칼도 이 값을 그대로 먹여야 한다 — 두 값이 갈라지면 예고가 판정에 대해 거짓말한다.
    ///
    /// ⚠️ 판정은 <b>수평면</b>에서 한다(y 무시). 높이 차이로 빠져나가는 일이 없도록 —
    ///    반경 안이면 위아래는 묻지 않는다. 기존 구/박스 오버랩과 같은 전제다.
    /// </summary>
    /// <param name="origin">부채꼴 꼭짓점(보통 보스 위치).</param>
    /// <param name="forward">부채꼴 중심 방향. 정규화하지 않아도 된다.</param>
    /// <param name="radius">반경(m). 0 이하면 아무 일도 하지 않는다.</param>
    /// <param name="angleDegrees">전체 각(도). 360 이상이면 각도 필터 없이 구 전체가 된다.</param>
    /// <returns>실제로 피해가 들어간 대상 수. <see cref="Hit"/> 와 같은 계약이다.</returns>
    public int HitCone(Vector3 origin, Vector3 forward, float radius, float angleDegrees)
    {
        if (!IsServer)
            return 0;

        if (radius <= 0f)
            return 0;

        Vector3 flatForward = forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.0001f)
            flatForward = Vector3.forward;
        flatForward.Normalize();

        _coneOrigin = origin;
        _coneForward = flatForward;
        // 전체 각 → 반각의 코사인. 360 이상이면 -1 보다 작게 두어 모든 방향을 통과시킨다.
        _coneCos = angleDegrees >= 360f ? -2f : Mathf.Cos(angleDegrees * 0.5f * Mathf.Deg2Rad);
        _coneRadiusSq = radius * radius;
        _coneActive = true;

        int hitCount = Physics.OverlapSphereNonAlloc(
            origin, radius, _results, targetLayer, QueryTriggerInteraction.Collide);

        int applied = ApplyHits(hitCount);

        _coneActive = false;
        return applied;
    }

    /// <summary>
    /// 보스 정면으로 뻗은 <b>네모</b> 판정. <paramref name="origin"/> 이 뒤끕 중앙이다.
    /// 🔴 예고 띄와 <b>같은 사각형</b>을 그려야 한다 — 한쪽만 고치면 예고가 거짓말이 된다.
    /// </summary>
    public int HitBox(Vector3 origin, Vector3 forward, float width, float length)
    {
        if (!IsServer) return 0;
        if (width <= 0f || length <= 0f) return 0;

        Vector3 fwd = forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
        fwd.Normalize();

        _boxOrigin = origin;
        _boxForward = fwd;
        _boxHalfWidth = width * 0.5f;
        _boxLength = length;
        _boxActive = true;

        // 높이는 넓게 잡는다 — 바닥 장판과 같은 전제(수평면 판정)라 높이로 걸러내지 않는다.
        Vector3 center = origin + fwd * (length * 0.5f);
        int hitCount = Physics.OverlapBoxNonAlloc(
            center, new Vector3(width * 0.5f, 5f, length * 0.5f), _results,
            Quaternion.LookRotation(fwd, Vector3.up), targetLayer, QueryTriggerInteraction.Collide);

        int applied = ApplyHits(hitCount);

        _boxActive = false;
        return applied;
    }

    // 부채꼴 필터 상태. HitCone 이 여는 동안만 유효하다(재진입 없음 — 판정은 서버 단일 스레드).
    // 네모 필터 상태. HitBox 가 여는 동안만 유효하다(부채꼴과 같은 규약).
    private bool _boxActive;
    private Vector3 _boxOrigin;    // 네모 뒤끕 중앙(= 전진 끝점)
    private Vector3 _boxForward;
    private float _boxHalfWidth;
    private float _boxLength;

    private bool _coneActive;
    private Vector3 _coneOrigin;
    private Vector3 _coneForward;
    private float _coneCos;
    private float _coneRadiusSq;

    /// <summary>
    /// 수평면 부채꼴 안인가. <b>반경과 각도를 같은 기준점으로</b> 본다.
    ///
    /// 🔴 앞의 <c>OverlapSphere</c> 는 **콜라이더 겹침**(3D)이라 기준이 다르다 — 그것만 믿으면
    ///    "몸통 끝자락만 걸쳐도 반경 안"이 되어 <b>바닥에 그린 예고보다 넓게 맞는다</b>.
    ///    오버랩은 후보를 추리는 브로드페이즈로만 쓰고, 최종 판정은 여기서 한다.
    ///    그래야 판정 도형이 데칼과 같아진다(예고가 판정에 대해 거짓말하지 않는다).
    ///
    /// ⚠️ 수평면 판정이라 높이는 묻지 않는다 — 바닥 장판과 같은 전제다.
    /// </summary>
    private bool PassesConeFilter(Collider hit)
    {
        // 🔴 네모가 열려 있으면 네모로 판정한다. 부채꼴과 같은 이유로 오버랩은
        //    브로드페이즈로만 쓰고, 최종 판정은 **수평면**에서 다시 한다 —
        //    그래야 바닥에 그린 예고와 판정 도형이 같아진다.
        if (_boxActive)
        {
            Vector3 d = hit.bounds.center - _boxOrigin;
            d.y = 0f;
            float along = Vector3.Dot(d, _boxForward);
            if (along < 0f || along > _boxLength) return false;
            Vector3 right = Vector3.Cross(Vector3.up, _boxForward);
            return Mathf.Abs(Vector3.Dot(d, right)) <= _boxHalfWidth;
        }

        if (!_coneActive)
            return true;

        Vector3 to = hit.bounds.center - _coneOrigin;
        to.y = 0f;

        // 꼭짓점에 겹쳐 선 대상은 방향이 없다 — 안쪽으로 본다(빠져나가는 편보다 낫다).
        float distSq = to.sqrMagnitude;
        if (distSq < 0.0001f)
            return true;

        if (distSq > _coneRadiusSq)
            return false;

        return Vector3.Dot(_coneForward, to / Mathf.Sqrt(distSq)) >= _coneCos;
    }

    // Hit / HitCone 공용 — 오버랩 결과에 히트 윈도우·데미지·넉백을 적용한다.
    // 🔴 두 진입점이 이 하나를 공유해야 **같은 히트 윈도우**가 걸린다(1인 1회의 근거).
    private int ApplyHits(int hitCount)
    {
        int applied = 0;
        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = _results[i];
            if (hit == null)
                continue;

            if (!PassesConeFilter(hit))
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

            bool resolved = TryResolveHit(hit);
            if (resolved)
                applied++;

            if (!resolved || !applyKnockback || hit == null)
                continue;

            Unit unit = hit.GetComponentInParent<Unit>();
            if (unit == null)
                continue;

            Vector3 dir = unit.transform.position - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
                unit.Knockback(dir.normalized, knockbackStrength);
        }

        return applied;
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
