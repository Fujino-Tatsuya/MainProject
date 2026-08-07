using Unity.Netcode;
using UnityEngine;

// 보스 폭탄 — **수평 당구 모델**. 서버 권한 + 물리.
//
// 정본: Docs/tech/boss-fsm-detailed-spec.md §10.5.1 / §10.5.1.1 (팀장 확정 2026-08-06).
//
// ⚠️ **"중력 포물선"이 아니라 2단계 모델이다.**
//    단계 1(Thrown) = Wells 손에서 대각선 투척, 중력 on, 포물선.
//    단계 2(Sliding/Resting) = 바닥 최초 접촉 이후. 중력 off, **Y 고정**, 방향벡터 일직선.
//    폭탄끼리 당구처럼 부딪히고 서로 밀리는 것이 이 기믹의 핵심 재미다.
//
// ─── 되쳐내기 경로 ─────────────────────────────────────────────────────────
// 플레이어 기본공격 → (공격 판정) → 폭탄 자식의 `Hurtbox` → `IAttackReceiver.ReceiveAttack`.
// 이 컴포넌트가 `IAttackReceiver` 를 직접 구현한다(레거시는 별도 `Bomb.cs` 가 중계했다 — 통합).
// 🔴 **`deflectAttackType` 으로 기본공격만 받는다.** 스킬로는 되쳐낼 수 없다(기획 확정).
// 🔴 **되쳐내기는 `Resting` 에서만** 받는다 — 상태로 판단하고 속도로 판단하지 않는다.
//
// ─── 물리 설정 (프리팹) ────────────────────────────────────────────────────
//   Rigidbody: useGravity = **true**(투척용. 바닥 접촉에서 코드가 끈다) / FreezeRotation
//              collisionDetectionMode = **ContinuousDynamic** (되쳐낸 폭탄은 빠르다 — 관통 방지)
//   ⚠️ 정지해도 **논키네마틱 Dynamic 을 유지**한다. isKinematic 으로 재우면 당구가 안 된다 —
//      Sleep() 에 맡기고, 충돌하면 자동으로 깬다. 클라 쪽 kinematic 은 NetworkRigidbody 가 처리한다.
//   ⚠️ 무회전은 **FreezeRotation** 으로. angularDrag 로 잡으려 하면 미세하게 돈다.
//
// 🔴 **남은 프로젝트 설정 작업**: 물리 충돌 매트릭스에서 폭탄 레이어가 **유닛과 물리 응답을 갖지
//    않게** 해야 한다(정본 §10.5.4). 유닛 접촉은 어차피 즉시 폭발이라 응답이 불필요하고,
//    응답이 남아 있으면 "보스는 안 밀린다"가 깨진다. 유닛 감지는 트리거(허트박스)로 하므로
//    매트릭스를 끊어도 폭발 판정은 살아 있다.
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
[DisallowMultipleComponent]
public class BossBomb : NetworkBehaviour, IAttackReceiver
{
    [Header("폭발")]
    [SerializeField, Min(0.1f)]
    [Tooltip("정지(Resting) 상태에서 폭발까지 걸리는 시간(초). 비행 중에는 흐르지 않는다.")]
    float bombTimer = 4f;
    [SerializeField, Min(0)]
    [Tooltip("폭발 데미지.")]
    int bombDamage = 20;
    [SerializeField, Min(0.1f)]
    [Tooltip("폭발 판정 반경(m).")]
    float bombRadius = 2.5f;
    [SerializeField]
    [Tooltip("폭발 피해 대상 레이어. 되쳐낸 폭탄이 몹을 때려야 하므로 보스/몹도 포함할 수 있다.")]
    LayerMask damageMask;
    [SerializeField, Min(1)]
    [Tooltip("폭발 판정 버퍼 크기.")]
    int maxTargets = 12;

    [Header("장판")]
    [SerializeField]
    [Tooltip("폭발 시 스폰할 장판 프리팹(AreaZone + NetworkObject).\n" +
             "🔴 같은 자리에 이미 같은 타입 장판이 있으면 SpawnOrGrow 가 **성장으로 갈음**한다 — " +
             "정본 §10.5.1.1 규칙1('장판이 두 개 겹쳐 스폰되지 않게')이 여기서 자동 충족된다.")]
    GameObject zonePrefab;

    [Header("되쳐내기")]
    [SerializeField]
    [Tooltip("되쳐낼 수 있는 공격 종류. 🔴 기본공격(Default)만 — 스킬로는 못 친다(기획 확정).")]
    AttackType deflectAttackType = AttackType.Default;
    [SerializeField, Min(0f)]
    [Tooltip("되쳐내기 비례계수. 밀어내는 힘 = 데미지 × 이 값.\n" +
             "레거시는 `distance = damage` 로 계수 없이 하드코딩돼 있었다 — 그래서 노출한다.")]
    float knockCoef = 0.6f;

    [Header("당구 / 벽")]
    [SerializeField, Min(0)]
    [Tooltip("벽 쿠션 횟수. 🔴 확정값 1 — 벽에 한 번 튕기고, **그 다음** 벽 충돌에서 폭발한다. " +
             "기획서의 '벽에 부딪히면 터짐'은 이 규칙으로 대체된다.")]
    int wallBounceLimit = 1;
    [SerializeField, Min(0.001f)]
    [Tooltip("정지 판정 속도 임계(m/s). 이보다 느린 상태가 restHoldTime 동안 유지되면 정지로 본다.")]
    float restVelocityEpsilon = 0.15f;
    [SerializeField, Min(0.01f)]
    [Tooltip("정지 판정 유지 시간(초). OnCollisionEnter 만으로는 안 된다 — 부딪히고도 계속 밀려간다.")]
    float restHoldTime = 0.2f;
    [SerializeField, Min(0f)]
    [Tooltip("바닥 접촉 시 폭탄 중심을 접촉점보다 이만큼 띄운다(콜라이더 반경만큼).")]
    float restHeightOffset = 0.3f;

    [Header("레이어 구분")]
    [SerializeField]
    [Tooltip("벽 레이어(쿠션 판정). Wall(7).")]
    LayerMask wallMask;
    [SerializeField]
    [Tooltip("바닥 레이어(단계1→2 전이 판정). 생성맵 바닥은 Default 이므로 Default+Ground 를 함께 넣는다.")]
    LayerMask groundMask;

    // 서버 전용 런타임
    Rigidbody _rb;
    BossBombState _state = BossBombState.Thrown;
    float _fuse;
    float _slowFor;          // restVelocityEpsilon 미만이 유지된 시간
    int _bouncesLeft;
    Collider[] _buffer;

    public BossBombState State => _state;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        // 무회전은 constraint 로. angularDrag 로 잡으면 미세하게 돈다.
        _rb.constraints |= RigidbodyConstraints.FreezeRotation;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsServer) return;

        _state = BossBombState.Thrown;
        _fuse = bombTimer;
        _bouncesLeft = wallBounceLimit;
        _buffer = new Collider[Mathf.Max(1, maxTargets)];

        // 단계 1 — 중력 켜고 Y 자유(포물선).
        _rb.useGravity = true;
        _rb.constraints = RigidbodyConstraints.FreezeRotation;
    }

    /// <summary>서버에서 투척한다(Wells 손 소켓 → 대각선 임펄스). 단계 1 로 들어간다.</summary>
    public void Throw(Vector3 impulse)
    {
        if (!IsServer) return;

        _state = BossBombState.Thrown;
        _rb.useGravity = true;
        _rb.constraints = RigidbodyConstraints.FreezeRotation;
        _rb.AddForce(impulse, ForceMode.Impulse);
    }

    void FixedUpdate()
    {
        if (!IsServer || _state == BossBombState.Exploded) return;

        switch (_state)
        {
            case BossBombState.Resting:
                // 🔴 타이머는 **정지 상태에서만** 흐른다(기획 그대로).
                _fuse -= Time.fixedDeltaTime;
                if (_fuse <= 0f) Explode();
                break;

            case BossBombState.Sliding:
                TickSlideRest();
                break;
        }
    }

    // 정지 판정: OnCollisionEnter 만으로는 안 된다 — 부딪히고도 계속 밀려간다.
    // 속도가 임계 미만인 상태가 restHoldTime 동안 유지되거나 Sleep 하면 정지로 본다.
    void TickSlideRest()
    {
        bool slow = _rb.IsSleeping() || _rb.linearVelocity.sqrMagnitude < restVelocityEpsilon * restVelocityEpsilon;
        if (!slow)
        {
            _slowFor = 0f;
            return;
        }

        _slowFor += Time.fixedDeltaTime;
        if (_slowFor < restHoldTime) return;

        EnterResting();
    }

    void EnterResting()
    {
        _state = BossBombState.Resting;
        _slowFor = 0f;
        // 타이머는 이어서 흐른다(리셋하지 않는다 — "비행 중 정지, 멈추면 재개"가 기획).
    }

    // ─── 충돌: 폭탄끼리 · 벽 · 바닥 ───────────────────────────────────
    void OnCollisionEnter(Collision collision)
    {
        if (!IsServer || _state == BossBombState.Exploded) return;

        // 1) 폭탄끼리 — 🔴 **같은 콜라이더 쌍인데 내 단계에 따라 결과가 다르다.**
        if (collision.rigidbody != null &&
            collision.rigidbody.TryGetComponent(out BossBomb other) && other != this)
        {
            if (_state == BossBombState.Thrown)
            {
                // 투척 중 기존 폭탄 위에 떨어졌다 → 당구가 아니라 충돌로 판정해 **둘 다** 터진다.
                other.Explode();
                Explode();
                return;
            }

            // 당구 — 물리가 밀어낸다. 우리는 상대의 타이머만 멈추면 된다(밀려난 폭탄도 비행 취급).
            if (_state == BossBombState.Sliding)
            {
                other.EnterSlidingFromCollision();
                return;
            }
        }

        // 2) 바닥 최초 접촉 — 단계 1 → 2 전이.
        if (_state == BossBombState.Thrown && IsInMask(collision.gameObject.layer, groundMask))
        {
            float contactY = collision.contactCount > 0 ? collision.GetContact(0).point.y : transform.position.y;
            EnterHorizontalPhase(contactY);
            return;
        }

        // 3) 벽 — 1쿠션 후 폭발.
        if (_state == BossBombState.Sliding && IsInMask(collision.gameObject.layer, wallMask))
        {
            if (_bouncesLeft > 0)
            {
                _bouncesLeft--;
                BounceOff(collision);
                return;
            }
            Explode();
        }
    }

    // 유닛 접촉 — 허트박스가 트리거라 OnTriggerEnter 로 들어온다.
    // 🔴 물리 응답이 아니라 트리거로 받는 이유: 유닛 충돌은 즉시 폭발이라 응답이 불필요하고,
    //    응답이 남아 있으면 "보스는 안 밀린다"가 깨진다(정본 §10.5.1).
    void OnTriggerEnter(Collider other)
    {
        if (!IsServer || _state == BossBombState.Exploded) return;
        if (_state == BossBombState.Thrown) return; // 투척 중 스쳐도 안 터진다(공중 통과)

        if (other.GetComponentInParent<Unit>() != null)
            Explode();
    }

    // 상대 폭탄이 나를 쳤을 때(당구) — 타이머를 멈추기 위해 Sliding 으로 올린다.
    void EnterSlidingFromCollision()
    {
        if (!IsServer || _state == BossBombState.Exploded || _state == BossBombState.Thrown) return;
        _state = BossBombState.Sliding;
        _slowFor = 0f;
    }

    // 🔴 세 가지를 **함께** 해야 한다 — 중력 off · FreezePositionY · y 속도 0 · 바닥 스냅.
    //    같이 하지 않으면 남은 y 속도가 constraint 와 싸워 떨린다(정본 §10.5.1).
    void EnterHorizontalPhase(float groundY)
    {
        _rb.useGravity = false;
        _rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;

        Vector3 v = _rb.linearVelocity;
        v.y = 0f;
        _rb.linearVelocity = v;

        // 절대 Y 상수 금지 — 접촉점(=실제 바닥)에서 콜라이더 반경만큼 띄운다.
        Vector3 p = transform.position;
        transform.position = new Vector3(p.x, groundY + restHeightOffset, p.z);

        // 바닥에 닿은 직후 남은 수평 속도가 있으면 당구(Sliding), 없으면 바로 정지.
        _state = v.sqrMagnitude > restVelocityEpsilon * restVelocityEpsilon
            ? BossBombState.Sliding
            : BossBombState.Resting;
        _slowFor = 0f;
    }

    // 벽 반사 — 속도를 접촉 법선으로 반사한다. 물리 바운시 머티리얼에 의존하지 않아 결정적이다.
    void BounceOff(Collision collision)
    {
        Vector3 normal = collision.contactCount > 0 ? collision.GetContact(0).normal : -transform.forward;
        normal.y = 0f;
        if (normal.sqrMagnitude < 0.0001f) return;

        Vector3 v = _rb.linearVelocity;
        v.y = 0f;
        _rb.linearVelocity = Vector3.Reflect(v, normal.normalized);
        _slowFor = 0f;
    }

    // ─── 되쳐내기 (IAttackReceiver) ───────────────────────────────────
    public bool ReceiveAttack(AttackInfo attackInfo, AttackHitContext hitContext)
    {
        if (!IsServer) return false;

        // 🔴 기본공격만. 그리고 **Resting 에서만** — 상태로 판단한다(속도로 판단하면 밀리다 느려진
        //    프레임에 예외가 생긴다).
        if (attackInfo.attackType != deflectAttackType) return false;
        if (_state != BossBombState.Resting) return false;

        Vector3 dir = transform.position - hitContext.sourcePosition;
        dir.y = 0f; // 수평 성분만 — Y 는 고정이다
        if (dir.sqrMagnitude < 0.0001f)
            dir = hitContext.sourceTransform != null ? hitContext.sourceTransform.forward : transform.forward;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return false;

        float force = Mathf.Max(0f, attackInfo.damage * knockCoef);
        _bouncesLeft = wallBounceLimit; // 새로 되쳐내면 쿠션 횟수를 다시 준다

        _state = BossBombState.Sliding;
        _slowFor = 0f;
        _rb.WakeUp();
        _rb.AddForce(dir.normalized * force, ForceMode.Impulse);
        return true;
    }

    // ─── 폭발 ─────────────────────────────────────────────────────────
    public void Explode()
    {
        if (!IsServer || _state == BossBombState.Exploded) return;
        _state = BossBombState.Exploded;

        ApplyExplosionDamage();

        // 장판을 별도 스폰하고 폭탄은 즉시 despawn(정본 §10.5.2).
        // 같은 타입 장판이 이미 있으면 SpawnOrGrow 가 성장으로 갈음한다 → 겹쳐 스폰되지 않는다.
        if (zonePrefab != null)
            AreaZone.SpawnOrGrow(zonePrefab, transform.position);

        if (NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn();
    }

    void ApplyExplosionDamage()
    {
        if (bombDamage <= 0) return;
        if (_buffer == null) _buffer = new Collider[Mathf.Max(1, maxTargets)];

        int count = Physics.OverlapSphereNonAlloc(
            transform.position, bombRadius, _buffer, damageMask, QueryTriggerInteraction.Collide);

        var info = new AttackInfo(bombDamage, AttackType.Default);
        var seen = new System.Collections.Generic.HashSet<Unit>();

        for (int i = 0; i < count; i++)
        {
            Collider hit = _buffer[i];
            if (hit == null) continue;
            if (!MonsterTargeting.IsAttackable(hit)) continue; // 유령 제외

            Hurtbox hurtbox = hit.GetComponentInParent<Hurtbox>();
            Unit unit = hurtbox != null ? hurtbox.OwnerUnit : hit.GetComponentInParent<Unit>();
            if (unit == null) continue;
            if (!seen.Add(unit)) continue; // 유닛당 1회

            var ctx = new AttackHitContext(transform.position, transform, hit);
            if (hurtbox != null) hurtbox.ReceiveAttack(info, ctx);
            else unit.ReceiveAttack(info, ctx);
        }
    }

    static bool IsInMask(int layer, LayerMask mask) => (mask.value & (1 << layer)) != 0;

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, bombRadius);
    }
#endif
}
