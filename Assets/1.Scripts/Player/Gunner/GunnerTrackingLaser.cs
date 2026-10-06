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
    [Tooltip("반경에 맞춰 스케일하는 연출 루트. 지름 1유닛으로 저작된 VFX 를 넣는다")]
    [SerializeField] private Transform visual;

    // 🔴 피해 펄스는 **각 피어가 자기 타이머로** 돌린다. 틱마다 RPC 를 쏘면
    //    6초에 12번, 인원수만큼 곱해진다 — 연출 하나에 쓸 대역이 아니다.
    //    간격이 데이터로 고정돼 있고 스폰 시각이 같으므로 그림은 충분히 맞는다
    //    (과열 단계를 각 피어가 계산하는 것과 같은 방식).
    [Tooltip("피해 펄스 연출(FX_GunnerUltTick_Entry). 비우면 연출 없이 피해만 들어간다")]
    [SerializeField] private EffectSocketPlayer tickPlayer;

    [Tooltip("그 펄스 간격(초). 데이터의 damageInterval 과 맞출 것")]
    [SerializeField, Min(0.05f)] private float tickVfxInterval = 0.5f;

    private float nextTickVfxTime = -1f;

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
    private AttackType attackType;
    private AttackHitPattern hitPattern;
    private float expireTime;
    private float nextDamageTime;
    private float nextRetargetTime;
    private bool initialized;

    /// <summary>[서버] 스폰 직후 1회.</summary>
    public void ServerInitialize(Player caster, Unit target, int damagePerTick, GunnerTrackingLaserData data,
                                 AttackType attackType, AttackHitPattern hitPattern)
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
        this.attackType = attackType;
        this.hitPattern = hitPattern;

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

    /// <summary>
    /// 연출을 반경에 맞춘다. VFX 는 <b>지름 1유닛</b>으로 저작돼 있으므로 배율이 곧 지름이다.
    ///
    /// 🔴 <b>균일 배율이다</b>(2026-10-05). 예전에는 Y 를 그대로 두고 X·Z 만 늘였는데,
    /// 파티클은 비균일 배율에서 깨진다 — 빌보드는 보는 각도마다 다르게 일그러지고
    /// Stretched 는 두께가 찌그러진다. 기둥 높이가 반경을 따라 같이 커지는 편이
    /// "넓은 존 = 굵은 기둥" 으로 읽혀 오히려 맞다.
    /// (반경은 데이터에서 한 번 정해지고 런타임에 변하지 않는다.)
    /// </summary>
    private void ApplyVisualRadius(float r)
    {
        if (visual != null)
            visual.localScale = Vector3.one * (r * 2f);
    }

    // 시전자가 게임을 나가면 즉시 제거(D12). 쓰러짐·사망은 해당하지 않는다.
    private void OnClientDisconnected(ulong clientId)
    {
        if (clientId == casterClientId)
            DespawnSelf("시전자 접속 종료");
    }

    private void Update()
    {
        // 🔴 **권한 가드 위**다. 피해 펄스는 전 피어가 봐야 한다 —
        //    여기 아래로 내리면 호스트에서만 보인다(이 레포가 여러 번 겪은 버그다).
        UpdateTickVfx();

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

    /// <summary>
    /// [전 피어] 피해 간격마다 펄스를 한 번 찍는다 — "지금 데미지가 들어갔다"는 신호다.
    /// 지속 기둥만 있으면 틱 리듬이 안 보여서 장판이 그냥 켜져 있는 것처럼 보인다.
    /// </summary>
    private void UpdateTickVfx()
    {
        if (tickPlayer == null || tickVfxInterval <= 0f)
            return;

        // 첫 틱은 생성 즉시(서버의 nextDamageTime 과 같은 규칙).
        if (nextTickVfxTime < 0f)
            nextTickVfxTime = Time.time;

        if (Time.time < nextTickVfxTime)
            return;

        nextTickVfxTime += tickVfxInterval;
        tickPlayer.PlayOnce();
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

            var info = new AttackInfo(damage, attackType, hitPattern: hitPattern);
            var context = new AttackHitContext(transform.position, transform, col, caster);
            bool resolved = hurtbox != null ? hurtbox.ReceiveAttack(info, context) : unit.ReceiveAttack(info, context);
            if (resolved)
                landed.Add(unit);
        }

        if (caster != null && landed.Count > 0)
            caster.RaiseServerAttackLanded(attackType, triggersOnHit, landed, this);
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
