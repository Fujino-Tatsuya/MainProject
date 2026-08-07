using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// 타입 있는 **지속 영역(장판)**. 서버 권한.
//
// 정본: Docs/tech/boss-fsm-detailed-spec.md §10.5.2 — "화염 장판 전용이 아니라 일반 AreaZone 으로 짓는다".
// 폭탄은 폭발 시 AreaZone(Fire)을 **별도 스폰**하고 자기는 즉시 despawn 한다. 같은 타입 장판이
// 이미 있으면 새로 스폰하지 않고 **기존 것을 성장**시킨다 → <see cref="SpawnOrGrow"/>.
//
// ─── 설계 결정 ─────────────────────────────────────────────────────────────
//
// 🔴 **효과를 zoneType 으로 분기하지 않는다.** 타입의 역할은 "중첩 성장을 가르는 기준"뿐이고,
//    데미지·둔화 수치는 프리팹 필드로 저작한다. 그래서 Fire/Swamp/Poison 프리팹이 각자 다른 숫자를
//    갖되 코드에 밸런스가 박히지 않는다(타입별 switch = 밸런스 하드코딩 + 죽은 분기의 원천).
//
// 🔴 **판정은 트리거가 아니라 한 순간의 OverlapSphere** 다(몬스터 규약 §3.4).
//    콜라이더가 필요 없으므로 이 오브젝트에는 콜라이더를 붙이지 않는다 —
//    붙이면 GroundProbe·NavMesh·투사체 스윕에 끼어든다.
//
// 🔴 **바닥에 눕히는 것은 GroundProbe 하나로 통일한다.** 절대 Y 상수 금지
//    (보스룸 보행면 0.50 / BossScene 0). 경사면에서는 바닥 법선에 맞춰 눕힌다 —
//    폐기된 `MakeFloor()` 는 경사 회전을 계산해 놓고 `Quaternion.identity` 로 덮어쓰는 버그가 있었다.
//    **그 버그를 같이 옮기지 않는다.**
//
// ⚠️ 성장 축은 **중첩 성장(OnOverlap)만** 넣었다. 정본이 언급한 "시간 성장"은 JumpAttack 의
//    빨간 예고 표시가 담당하고(그건 지속 영역이 아니라 텔레그래프 — `AoeTelegraph`),
//    AreaZone 쪽에는 소비자가 없어서 필드를 만들지 않았다.
[RequireComponent(typeof(NetworkObject))]
[DisallowMultipleComponent]
public class AreaZone : NetworkBehaviour
{
    /// <summary>서버 전용 활성 장판 목록. 같은 타입 중첩 성장 판정에 쓴다.</summary>
    public static readonly List<AreaZone> Active = new List<AreaZone>();

    [Header("정체 — 같은 타입끼리만 합쳐진다")]
    [SerializeField]
    [Tooltip("원소 타입. 효과가 아니라 **중첩 성장을 가르는 기준**이다. 같은 타입만 합쳐 성장한다.")]
    AreaZoneType zoneType = AreaZoneType.Fire;

    [Header("크기 / 수명")]
    [SerializeField, Min(0.1f)]
    [Tooltip("스폰 시 반경(m).")]
    float radius = 2f;
    [SerializeField, Min(0f)]
    [Tooltip("수명(초). 0 이면 수동 제거 전까지 유지한다.")]
    float lifetime = 6f;
    [SerializeField, Min(0f)]
    [Tooltip("같은 타입이 겹쳐 스폰될 때 늘어나는 반경(m). 0 이면 성장하지 않는다.")]
    float growPerOverlap = 0.5f;
    [SerializeField, Min(0.1f)]
    [Tooltip("성장 상한 반경(m).")]
    float maxRadius = 5f;
    [SerializeField]
    [Tooltip("같은 타입이 겹쳐 성장할 때 수명을 다시 채운다(폭탄이 불 위에 또 떨어지면 불이 유지되도록).")]
    bool refreshLifetimeOnGrow = true;

    [Header("피해 / 효과 — 타입별 수치는 프리팹으로 저작")]
    [SerializeField]
    [Tooltip("피해 대상 레이어(보스 장판이면 Player).")]
    LayerMask targetMask;
    [SerializeField, Min(0f)]
    [Tooltip("피해 간격(초). 0 이면 피해 없음.")]
    float tickInterval = 0.5f;
    [SerializeField, Min(0)]
    [Tooltip("1회 피해량. 0 이면 피해 없음.")]
    int tickDamage = 4;
    [SerializeField, Min(1)]
    [Tooltip("피해 판정 OverlapSphere 버퍼 크기.")]
    int maxTargets = 12;

    [Header("상태이상 (선택) — 서버가 StatusEffects.Apply 로 직접 건다")]
    [SerializeField]
    [Tooltip("영역 안에 있는 동안 걸 상태이상. None 이면 걸지 않는다.\n" +
             "예: 늪 = MoveSpeedModifier(magnitude 0.5 = 이동속도 50%) / 번개 = Stunned.")]
    StatusEffectType appliedStatus = StatusEffectType.None;
    [SerializeField, Min(0f)]
    [Tooltip("상태이상 배율(Modifier 계열만 의미 있음). 0.5 = 해당 스탯 50%.")]
    float statusMagnitude = 0.5f;
    [SerializeField, Min(0f)]
    [Tooltip("상태이상 지속(초). 피해 간격보다 살짝 길게 잡으면 영역을 벗어난 직후 자연히 풀린다 " +
             "(별도 이탈 판정이 필요 없다).")]
    float statusDuration = 0.8f;

    [Header("비주얼")]
    [SerializeField]
    [Tooltip("반경에 맞춰 스케일할 비주얼 자식(비우면 성장이 화면에 안 보인다). " +
             "AoeTelegraph 와 같은 규약 — 로컬 XY 지름 1 Quad/디스크.")]
    Transform visual;
    [SerializeField]
    [Tooltip("바닥 탐색에 추가로 포함할 레이어(Default+Ground 는 GroundProbe 가 항상 포함).")]
    LayerMask extraGroundMask;

    // 반경 복제 — 성장이 모든 피어의 비주얼에 반영돼야 한다.
    readonly NetworkVariable<float> _radius = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public AreaZoneType ZoneType => zoneType;
    public float Radius => _radius.Value > 0f ? _radius.Value : radius;

    // 서버 전용 런타임
    float _lifeTimer;
    float _tickTimer;
    Collider[] _buffer;
    readonly HashSet<Unit> _tickHits = new HashSet<Unit>();

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _radius.OnValueChanged += OnRadiusChanged;

        if (IsServer)
        {
            if (_radius.Value <= 0f) _radius.Value = radius;
            _lifeTimer = lifetime;
            _tickTimer = 0f;
            _buffer = new Collider[Mathf.Max(1, maxTargets)];
            Active.Add(this);
        }

        ApplyVisualRadius(Radius);
    }

    public override void OnNetworkDespawn()
    {
        _radius.OnValueChanged -= OnRadiusChanged;
        Active.Remove(this);
        base.OnNetworkDespawn();
    }

    void Update()
    {
        if (!IsServer) return;

        if (lifetime > 0f)
        {
            _lifeTimer -= Time.deltaTime;
            if (_lifeTimer <= 0f)
            {
                Despawn();
                return;
            }
        }

        if (tickInterval <= 0f) return;

        _tickTimer -= Time.deltaTime;
        if (_tickTimer > 0f) return;
        _tickTimer = tickInterval;

        ApplyTick();
    }

    // 한 순간의 OverlapSphere → 유닛당 1회. 지속 트리거를 쓰지 않는 이유는 몬스터 규약 §3.4.
    void ApplyTick()
    {
        if (_buffer == null) _buffer = new Collider[Mathf.Max(1, maxTargets)];

        int count = Physics.OverlapSphereNonAlloc(
            transform.position, Radius, _buffer, targetMask, QueryTriggerInteraction.Collide);

        bool doDamage = tickDamage > 0;
        AttackInfo info = doDamage ? new AttackInfo(tickDamage, AttackType.Default) : default;

        _tickHits.Clear();
        for (int i = 0; i < count; i++)
        {
            Collider hit = _buffer[i];
            if (hit == null) continue;

            // 유령(Soul)은 대상이 아니다 — 레이어 마스크만으로는 걸러지지 않는다.
            if (!MonsterTargeting.IsAttackable(hit)) continue;

            Hurtbox hurtbox = hit.GetComponentInParent<Hurtbox>();
            Unit unit = hurtbox != null ? hurtbox.OwnerUnit : hit.GetComponentInParent<Unit>();
            if (unit == null) continue;
            if (!_tickHits.Add(unit)) continue; // 유닛당 1회

            if (doDamage)
            {
                var ctx = new AttackHitContext(transform.position, transform, hit);
                if (hurtbox != null) hurtbox.ReceiveAttack(info, ctx);
                else unit.ReceiveAttack(info, ctx);
            }

            ApplyStatus(unit);
        }
    }

    // 상태이상은 AttackInfo 경로가 아니라 StatusEffects 로 직접 건다.
    // 🔴 플레이어의 AttackInfo CC 필드(knockbackStrength 등)를 읽는 코드는 없지만,
    //    StatusEffectController.Apply 는 서버 적용 + 복제까지 완비돼 있다(PLAN §5.1 G1 정정).
    void ApplyStatus(Unit unit)
    {
        if (appliedStatus == StatusEffectType.None || statusDuration <= 0f) return;

        StatusEffectController status = unit.StatusEffects;
        if (status == null) return; // 몹(MonsterStatusEffect)은 이 API 를 갖지 않는다

        // maxStacks 1 → 매 틱 재적용이 스택을 쌓지 않고 지속시간만 갱신한다.
        status.Apply(appliedStatus, statusMagnitude, statusDuration, NetworkObjectId, maxStacks: 1);
    }

    /// <summary>같은 타입이 겹쳐 스폰될 때 성장시킨다(서버 전용). 상한에 닿으면 수명만 갱신된다.</summary>
    public void Grow()
    {
        if (!IsServer) return;

        if (growPerOverlap > 0f)
            _radius.Value = Mathf.Min(maxRadius, Radius + growPerOverlap);

        if (refreshLifetimeOnGrow)
            _lifeTimer = lifetime;
    }

    public void Despawn()
    {
        if (!IsServer) return;
        if (NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn();
    }

    void OnRadiusChanged(float previous, float next) => ApplyVisualRadius(next);

    void ApplyVisualRadius(float r)
    {
        if (visual == null) return;
        float diameter = Mathf.Max(0.01f, r) * 2f;
        // AoeTelegraph 와 같은 규약: 로컬 XY 지름 1 메시 → X/Y 가 월드 XZ 지름으로 매핑된다.
        visual.localScale = new Vector3(diameter, diameter, 1f);
    }

    // ─── 스폰 진입점 ──────────────────────────────────────────────────
    /// <summary>
    /// 장판을 스폰하거나, 같은 타입 장판이 이미 그 자리에 있으면 **그것을 성장**시킨다(서버 전용).
    /// 정본 §10.5.2 의 "같은 타입 장판이 이미 있으면 새로 스폰하지 않고 기존 것을 성장" 규칙.
    /// </summary>
    /// <param name="prefab">AreaZone + NetworkObject 프리팹. NetworkManager 의 NetworkPrefabs 에 등록돼 있어야 한다.</param>
    /// <returns>새로 스폰했거나 성장시킨 장판. 실패 시 null.</returns>
    public static AreaZone SpawnOrGrow(GameObject prefab, Vector3 position, int extraGroundMask = 0)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer)
            return null;

        if (prefab == null)
        {
            Debug.LogError("[AreaZone] 프리팹이 비어 있다 — 장판이 생성되지 않는다.");
            return null;
        }
        if (!prefab.TryGetComponent(out AreaZone template))
        {
            Debug.LogError($"[AreaZone] 프리팹 {prefab.name} 에 AreaZone 컴포넌트가 없다.", prefab);
            return null;
        }

        // 바닥에 눕힌다. 절대 Y 금지 — 찾은 바닥 + 표준 간격.
        int mask = extraGroundMask | template.extraGroundMask;
        Quaternion rotation = Quaternion.identity;
        if (GroundProbe.TryFindGround(position, mask, out RaycastHit ground, out string report))
        {
            position = new Vector3(position.x, GroundProbe.SurfaceY(ground), position.z);
            // 경사면 정렬 — 폐기된 MakeFloor 는 이 계산을 하고도 identity 로 덮어썼다(그 버그는 옮기지 않는다).
            rotation = Quaternion.FromToRotation(Vector3.up, ground.normal);
        }
        else
        {
            Debug.LogWarning($"[AreaZone] 바닥을 못 찾아 요청 지점에 그대로 둔다 — {report}");
        }

        // 같은 타입이 이미 그 자리에 있으면 성장으로 갈음한다.
        AreaZone existing = FindOverlapping(template.zoneType, position);
        if (existing != null)
        {
            existing.Grow();
            return existing;
        }

        GameObject go = Object.Instantiate(prefab, position, rotation);
        if (!go.TryGetComponent(out NetworkObject netObj))
        {
            Debug.LogError($"[AreaZone] 프리팹 {prefab.name} 에 NetworkObject 가 없다 — 스폰할 수 없다.", prefab);
            Object.Destroy(go);
            return null;
        }

        netObj.Spawn();
        return go.GetComponent<AreaZone>();
    }

    // 같은 타입이고, 그 장판 반경 안에 지점이 들어오면 "겹친다"로 본다.
    static AreaZone FindOverlapping(AreaZoneType type, Vector3 position)
    {
        for (int i = 0; i < Active.Count; i++)
        {
            AreaZone z = Active[i];
            if (z == null || z.zoneType != type) continue;

            Vector3 delta = z.transform.position - position;
            delta.y = 0f; // 높이 차는 무시 — 같은 바닥면 위 겹침만 본다
            if (delta.sqrMagnitude <= z.Radius * z.Radius)
                return z;
        }
        return null;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, Radius);
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, maxRadius);
    }
#endif
}
