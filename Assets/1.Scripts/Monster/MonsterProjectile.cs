using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// 몬스터 원거리 투사체. 서버 권한 NetworkObject.
// - 서버가 Spawn → NetworkTransform(서버 권한)로 위치 복제 → 모든 피어에서 이동이 보인다.
// - 히트 판정/데미지는 서버에서만. ReceiveAttack 경로만 사용(TakeDamageRpc 미사용, AGENTS.md 서버권한 원칙).
// - MonoBehaviour인 BaseAttack을 상속할 수 없어(NetworkBehaviour 필요) 히트 로직을 자체 구현하되,
//   AttackInfo/AttackHitContext(BaseAttack.cs 정의)와 Hurtbox/Unit.ReceiveAttack 계약은 동일하게 쓴다.
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Collider))]
public class MonsterProjectile : NetworkBehaviour
{
    Unit _owner;
    Vector3 _direction;
    float _speed;
    int _damage;
    LayerMask _targetLayer;
    float _despawnTime;
    bool _launched;
    Vector3 _launchPosition;

    [SerializeField, Min(0f)]
    [Tooltip("무장 거리(m). 발사 원점에서 이 거리 안에서는 벽·지면 충돌을 무시한다. " +
             "총구가 몸통·받침대에 겹쳐 있어도 발사와 동시에 죽지 않게 하는 가드.")]
    float armingDistance = 0.7f;

    // 포물선(탄도) 모드 — MortarBot 등. arcHeight > 0일 때 MonsterRangedAttack.Fire가 사용.
    bool _ballistic;
    Vector3 _velocity;
    float _splashRadius;

    static readonly Collider[] SplashBuffer = new Collider[16];
    static readonly HashSet<Unit> SplashHitUnits = new HashSet<Unit>();

    // 서버에서 발사. 설정값은 서버 전용(복제 불필요 — 이동 결과는 NetworkTransform이 복제한다).
    public void Launch(Unit owner, Vector3 direction, float speed, int damage, LayerMask targetLayer, float lifetime)
    {
        _owner = owner;
        _direction = direction.sqrMagnitude >= 0.0001f ? direction.normalized : transform.forward;
        _speed = Mathf.Max(0f, speed);
        _damage = Mathf.Max(0, damage);
        _targetLayer = targetLayer;
        _despawnTime = Time.time + Mathf.Max(0.1f, lifetime);
        _launched = true;
        _launchPosition = transform.position;
        _ballistic = false;
        transform.rotation = Quaternion.LookRotation(_direction);
    }

    // 서버에서 포물선 발사(MonsterRangedAttack.Fire의 탄도 경로). 유도 없음 — 발사 시점 초기 속도만 세팅.
    public void LaunchBallistic(Unit owner, Vector3 initialVelocity, int damage, LayerMask targetLayer, float lifetime, float splashRadius)
    {
        _owner = owner;
        _damage = Mathf.Max(0, damage);
        _targetLayer = targetLayer;
        _despawnTime = Time.time + Mathf.Max(0.1f, lifetime);
        _launched = true;
        _launchPosition = transform.position;
        _ballistic = true;
        _velocity = initialVelocity;
        _splashRadius = Mathf.Max(0f, splashRadius);
        if (_velocity.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(_velocity);
    }

    void Update()
    {
        if (!IsServer || !_launched)
            return;

        if (_ballistic)
        {
            _velocity += Physics.gravity * Time.deltaTime;
            transform.position += _velocity * Time.deltaTime;
            if (_velocity.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(_velocity);
        }
        else
        {
            transform.position += _direction * (_speed * Time.deltaTime);
        }

        if (Time.time >= _despawnTime)
            Despawn();
    }

    void OnTriggerEnter(Collider other)
    {
        if (!IsServer || !_launched || other == null)
            return;

        if (IsInTargetLayer(other))
        {
            AttackInfo info = new AttackInfo(_damage, AttackType.Default, false);
            AttackHitContext ctx = new AttackHitContext(transform.position, transform, other);

            // Hurtbox 우선(소유자 자기 자신 무시).
            Hurtbox hurtbox = other.GetComponentInParent<Hurtbox>();
            if (hurtbox != null)
            {
                if (hurtbox.TryGetOwner(out Unit hbOwner) && hbOwner == _owner)
                    return;
                if (hurtbox.ReceiveAttack(info, ctx))
                {
                    if (_ballistic && _splashRadius > 0f)
                        Detonate();
                    else
                        Despawn();
                }
                return;
            }

            // Hurtbox가 없으면 Unit 직접.
            Unit unit = other.GetComponentInParent<Unit>();
            if (unit == null || unit == _owner)
                return;
            if (unit.ReceiveAttack(info, ctx))
            {
                if (_ballistic && _splashRadius > 0f)
                    Detonate();
                else
                    Despawn();
            }
            return;
        }

        // 환경 착탄(벽·프롭·지면 등 타깃이 아닌 콜라이더) — **직선도 여기서 죽는다**.
        //
        // 🔴 예전에는 `_ballistic` 일 때만 처리해서 직선 투사체가 **벽을 통과**했다
        //    (2026-09-08 팀장 관찰). 원거리 공격이 벽 뒤를 때리면 엄폐가 의미를 잃는다.
        //
        // 규칙: `Unit` 이 아니고 트리거도 아니면 장애물이다.
        //  - 몬스터·플레이어는 `Unit` 이라 그대로 통과한다 → "다른 몹이 대신 맞지 않는다"는 사양 유지.
        //  - 장판·구역·다른 투사체는 `isTrigger` 라 무시된다.
        //  ⚠️ 존 프리팹의 벽·통로·바닥이 전부 `Default` 레이어라(Wall/Env 를 쓰지 않는다) 레이어로
        //     "벽만" 고를 수 없다 → 바닥·경사도 장애물이다. 오르막에 맞으면 사라진다(층 분리와 같은 방향).
        if (other.GetComponentInParent<Unit>() == null && !other.isTrigger)
        {
            // 🔴 **무장 거리** — 발사 직후 이 거리 안에서는 환경 충돌을 무시한다.
            //    `MonsterRangedAttack` 은 `muzzle` 이 비어 있으면 발사 원점으로 **봇 루트(발밑)** 를 쓴다.
            //    PeekABot·TeslaBot 이 그 상태라 반경 0.5 트리거가 받침대·지면과 겹친 채 태어난다 →
            //    무장 거리가 없으면 발사와 동시에 죽어 **공격이 아예 안 나간다**(2026-09-08 실측).
            //    근본은 muzzle 배선이지만, 벽에 붙어 쏘는 경우까지 덮으려면 이 가드가 있어야 한다.
            if ((transform.position - _launchPosition).sqrMagnitude < armingDistance * armingDistance)
                return;

            if (_ballistic && _splashRadius > 0f) Detonate();
            else Despawn();
        }
    }

    bool IsInTargetLayer(Collider c)
    {
        int self = c.gameObject.layer;
        int root = c.transform.root.gameObject.layer;
        return (_targetLayer.value & (1 << self)) != 0 || (_targetLayer.value & (1 << root)) != 0;
    }

    // 착탄 처리 — 스플래시 반경 내 타깃에 데미지(Unit당 1회) 후 디스폰.
    void Detonate()
    {
        if (_splashRadius > 0f)
        {
            SplashHitUnits.Clear();
            int count = Physics.OverlapSphereNonAlloc(transform.position, _splashRadius, SplashBuffer, _targetLayer, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                Collider c = SplashBuffer[i];
                if (c == null)
                    continue;

                AttackInfo info = new AttackInfo(_damage, AttackType.Default, false);
                AttackHitContext ctx = new AttackHitContext(transform.position, transform, c);

                Hurtbox hurtbox = c.GetComponentInParent<Hurtbox>();
                if (hurtbox != null)
                {
                    if (!hurtbox.TryGetOwner(out Unit hbOwner) || hbOwner == _owner || SplashHitUnits.Contains(hbOwner))
                        continue;
                    if (hurtbox.ReceiveAttack(info, ctx))
                        SplashHitUnits.Add(hbOwner);
                    continue;
                }

                Unit unit = c.GetComponentInParent<Unit>();
                if (unit == null || unit == _owner || SplashHitUnits.Contains(unit))
                    continue;
                if (unit.ReceiveAttack(info, ctx))
                    SplashHitUnits.Add(unit);
            }
        }

        Despawn();
    }

    void Despawn()
    {
        _launched = false;
        if (NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn();
    }
}
