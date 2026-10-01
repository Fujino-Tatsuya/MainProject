using System.Collections.Generic;
using BaseNetCode;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 거너 R 이 생성하는 추적 레이저 — 서버 소유 NetworkObject (character_gunner.md §9, D12·D13, PLAN-gunner.md G7).
/// 서버가 이동·피해·수명을 전부 계산하고 위치만 NetworkTransform 으로 복제한다.
///
/// - 대상을 정해진 속도로 추적(순간이동 없음). 대상이 살아 있는 동안은 바꾸지 않는다.
/// - 대상이 죽거나 사라지면 레이저 주변 재탐색 범위에서 가장 가까운 살아 있는 적을 새 대상으로. 없으면 제자리에서 주기 재탐색.
/// - 피해 간격마다 원형 범위 안의 적에게 1회씩 피해 — 시작 때 저장한 과열 단계 배율, 피해는 시전자 명의.
/// - 지속시간 만료·시전자 접속 종료 시 제거. 시전자 쓰러짐·사망은 유지. 씬 전환은 destroyWithScene 으로 함께 사라진다.
/// - 대기 시간도 지속시간에 포함된다. 어그로는 건드리지 않는다(보스 어그로는 거리 기반 — D13).
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class GunnerTrackingLaser : BaseNetworkBehaviour
{
    [Tooltip("임시 표시 — 반경에 맞춰 스케일한다(민경 VFX 전까지).")]
    [SerializeField] private Transform visual;

    private readonly NetworkVariable<float> replicatedRadius = new NetworkVariable<float>(1f);

    private readonly Collider[] overlap = new Collider[64];
    private readonly HashSet<Unit> tickHits = new HashSet<Unit>();
    private readonly List<Unit> landed = new List<Unit>();

    // 서버
    private Player caster;
    private ulong casterClientId;
    private Unit target;
    private int damage;
    private float speed;
    private float radius;
    private float damageInterval;
    private float retargetRadius;
    private float retargetInterval;
    private LayerMask enemyLayers;
    private bool triggersOnHit;
    private float expireTime;
    private float nextDamageTime;
    private float nextRetargetTime;
    private bool initialized;

    /// <summary>[서버] 스폰 직후 1회.</summary>
    public void ServerInitialize(Player caster, Unit target, int damagePerTick, GunnerTrackingLaserData data)
    {
        this.caster = caster;
        casterClientId = caster != null ? caster.OwnerClientId : ulong.MaxValue;
        this.target = target;
        damage = Mathf.Max(0, damagePerTick);
        speed = data.MoveSpeed;
        radius = data.Radius;
        damageInterval = data.DamageInterval;
        retargetRadius = data.RetargetRadius;
        retargetInterval = data.RetargetInterval;
        enemyLayers = data.HittableLayers;
        triggersOnHit = data.TriggersOnHit;

        // 생성 시점부터 지속시간(§9.3). 첫 피해는 생성 즉시.
        expireTime = Time.time + data.LaserDuration;
        nextDamageTime = Time.time;
        nextRetargetTime = Time.time;
        initialized = true;

        if (IsSpawned)
        {
            replicatedRadius.Value = radius;
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
        }
        else
        {
            ApplyVisualRadius(radius);
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        replicatedRadius.OnValueChanged += (_, r) => ApplyVisualRadius(r);
        ApplyVisualRadius(replicatedRadius.Value);
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        base.OnNetworkDespawn();
    }

    private void ApplyVisualRadius(float r)
    {
        if (visual != null)
            visual.localScale = new Vector3(r * 2f, visual.localScale.y, r * 2f);
    }

    // 시전자가 게임을 나가면 즉시 제거(D12). 쓰러짐·사망은 해당하지 않는다.
    private void OnClientDisconnected(ulong clientId)
    {
        if (clientId == casterClientId)
            DespawnSelf("시전자 접속 종료");
    }

    private void Update()
    {
        if (!HasStateAuthority || !initialized)
            return;

        if (Time.time >= expireTime)
        {
            DespawnSelf("지속시간 만료");
            return;
        }

        if (!IsValid(target) && Time.time >= nextRetargetTime)
        {
            nextRetargetTime = Time.time + retargetInterval;
            target = FindNearestEnemy(retargetRadius);
        }

        if (IsValid(target))
        {
            Vector3 to = target.transform.position - transform.position;
            to.y = 0f;
            float step = speed * Time.deltaTime;
            transform.position += to.sqrMagnitude <= step * step ? to : to.normalized * step;
        }

        if (Time.time >= nextDamageTime)
        {
            nextDamageTime += damageInterval;
            DamageTick();
        }
    }

    private void DamageTick()
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, radius, overlap, enemyLayers, QueryTriggerInteraction.Collide);
        tickHits.Clear();
        landed.Clear();

        for (int i = 0; i < count; i++)
        {
            Collider col = overlap[i];
            if (col == null)
                continue;

            Hurtbox hurtbox = col.GetComponentInParent<Hurtbox>();
            Unit unit = null;
            if (hurtbox != null)
                hurtbox.TryGetOwner(out unit);
            if (unit == null)
                unit = col.GetComponentInParent<Unit>();

            // 적만(§9.5) — 플레이어·시체·중복 제외. 한 주기에 같은 대상은 1회.
            if (unit == null || unit is Player || unit.CurrentHealth <= 0 || !tickHits.Add(unit))
                continue;

            var info = new AttackInfo(damage, AttackType.Skill);
            var context = new AttackHitContext(transform.position, transform, col, caster);
            bool resolved = hurtbox != null ? hurtbox.ReceiveAttack(info, context) : unit.ReceiveAttack(info, context);
            if (resolved)
                landed.Add(unit);
        }

        if (caster != null && landed.Count > 0)
            caster.RaiseServerAttackLanded(AttackType.Skill, triggersOnHit, landed, this);
    }

    private Unit FindNearestEnemy(float range)
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, range, overlap, enemyLayers, QueryTriggerInteraction.Collide);
        Unit best = null;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            Collider col = overlap[i];
            if (col == null)
                continue;

            Unit unit = null;
            Hurtbox hurtbox = col.GetComponentInParent<Hurtbox>();
            if (hurtbox != null)
                hurtbox.TryGetOwner(out unit);
            if (unit == null)
                unit = col.GetComponentInParent<Unit>();
            if (!IsValid(unit))
                continue;

            float sqr = (unit.transform.position - transform.position).sqrMagnitude;
            // 거리가 같으면 먼저 확인된 대상(§9.4) — 엄격히 더 가까울 때만 교체
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = unit;
            }
        }
        return best;
    }

    private static bool IsValid(Unit unit) =>
        unit != null && !(unit is Player) && unit.CurrentHealth > 0 && unit.isActiveAndEnabled;

    private void DespawnSelf(string reason)
    {
        if (!HasStateAuthority || !initialized)
            return;

        initialized = false;
        Edit.Log($"[Gunner/R] 추적 레이저 제거 — {reason}", this);

        if (NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn(true);
        else
            Destroy(gameObject); // 오프라인(네트워크 없이 실행)
    }
}
