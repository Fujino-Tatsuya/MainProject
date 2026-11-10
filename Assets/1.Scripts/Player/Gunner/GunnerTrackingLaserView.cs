using System.Collections.Generic;
using BaseNetCode;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 거너 R 추적 레이저의 <b>연출 전부</b>(2026-10-21 민경 grill — PLAN.md 「거너 R 추적 레이저 연출 재제작」).
/// 판정·이동·수명은 <see cref="GunnerTrackingLaser"/>(서버)가 하고, 이 컴포넌트는 그 결과를 전 피어에서 그리기만 한다.
///
/// <list type="number">
/// <item><b>장판</b>(<see cref="zoneEffect"/>, 루프): 레이저를 따라다닌다. 배율 = 반경 × 2(지름 1유닛 저작).
///       레이저가 사라지면 해제(<see cref="EffectManager.Release"/>)한다 — 퇴장 모습은 장판 엔트리의 outro 설정을 따른다.</item>
/// <item><b>대상별 조준</b>(<see cref="lockEffect"/>, 루프): 범위 안 적마다 하나 붙어 따라다닌다.
///       각 피어가 매 프레임 직접 찾는다(새로 들어온 적도 다음 발사 전에 예고가 뜬다).
///       원 진행도 = 마지막 피해 이후 시간 ÷ 피해 간격 → <see cref="TelegraphRing.SetProgress"/>. 0 이 되는 순간이 피해 순간이다.</item>
/// <item><b>위성 레이저 + 땅 파문</b>(<see cref="strikeEffect"/>, 원샷): 서버가 피해를 준 순간 <b>실제로 맞은 적의 위치</b>마다.</item>
/// </list>
///
/// <b>왜 맞은 위치를 RPC 로 받나.</b> 판정은 서버 전용이라 클라는 누가 맞았는지 모른다. 레이저는 피해 숫자와 함께 떠야 하므로
/// 추측하지 않고 서버가 알려 준 위치에만 꽂는다(순간 타격이라 위치면 충분 — 네트워크 오브젝트가 없는 대상도 그려진다).
/// 조준 표식은 예고일 뿐이라 각 피어 계산으로 충분하다.
///
/// <b>왜 장판을 추종(follow)으로 걸지 않나.</b> <see cref="EffectManager"/> 는 추종 대상이 파괴되는 즉시 회수한다 —
/// 디스폰과 함께 장판이 뚝 끊긴다. 그래서 위치 고정으로 띄우고 매 프레임 직접 옮기다가, 디스폰 때 해제한다.
///
/// 반경·간격·적 레이어는 서버가 <see cref="ServerConfigure"/> 로 NetworkVariable 에 써 둔다 — 늦게 들어온 클라도 맞다.
/// 네트워크 없이(테스트 씬) 돌면 같은 값을 로컬에 들고 같은 연출을 낸다.
/// </summary>
[DisallowMultipleComponent]
public class GunnerTrackingLaserView : BaseNetworkBehaviour
{
    [Header("이펙트")]
    [Tooltip("범위 장판(루프). FX_GunnerUlt_Zone_Entry")]
    [SerializeField] private EffectEntry zoneEffect;
    [Tooltip("대상별 조준(루프, TelegraphRing 포함). FX_GunnerUlt_Lock_Entry")]
    [SerializeField] private EffectEntry lockEffect;
    [Tooltip("위성 레이저 + 땅 파문(원샷). FX_GunnerUlt_Strike_Entry")]
    [SerializeField] private EffectEntry strikeEffect;

    [Header("조준 표식 크기")]
    [Tooltip("배율 = 대상 몸 반경 × 이 값(아래 상·하한)")]
    [SerializeField, Min(0.1f)] private float lockScalePerRadius = 2f;
    [SerializeField, Min(0.1f)] private float lockScaleMin = 0.8f;
    [SerializeField, Min(0.1f)] private float lockScaleMax = 3f;
    [Tooltip("몸 반경을 못 읽었을 때(NavMeshAgent·CapsuleCollider 없음)")]
    [SerializeField, Min(0.05f)] private float fallbackBodyRadius = 0.5f;

    [Header("레이저")]
    [SerializeField, Min(0.01f)] private float strikeScale = 1f;

    // 한계 돌파 과열 색(2026-11-03 민경 — PLAN.md 「한계 돌파 VFX」). 장판·낙뢰를 시전 시작 저장 단계 색으로 칠한다(조준 표식은 원래 주황 그대로).
    // 클라는 이 레이저의 시전자·단계를 모르므로 서버가 스폰 전에 단계를 써 둔다(netOverdriveStage — 스폰 메시지에 같이 실린다).
    [Header("한계 돌파 과열 색")]
    [Tooltip("과열 단계별 색 표. 9.ScriptableObject/Effects/GunnerOverdriveVfxColors. 비우면 색을 바꾸지 않는다")]
    [SerializeField] private GunnerOverdriveVfxColors overdriveColors;

    private readonly NetworkVariable<byte> netOverdriveStage = new NetworkVariable<byte>();
    private int localOverdriveStage;

    // 다른 클라 화면 알파(2026-11-06 민경 — PLAN.md 「거너 R 다른 클라 화면 알파」). R 을 쏜 거너 본인 화면은 원래대로,
    // 나머지 화면(호스트 포함)은 아래 머티리얼의 알파에 배율을 곱한다. 장판·낙뢰에만 건다(조준 표식 제외).
    // 클라는 시전자를 모르므로 서버가 스폰 전에 시전자 ClientId 를 써 둔다(netCasterClientId — 한계 돌파 단계와 같은 경로).
    [Header("다른 클라 화면 알파")]
    [Tooltip("시전자가 아닌 화면에서 이 머티리얼을 쓰는 렌더러의 속성에 배율을 곱한다(같은 머티리얼 여러 줄 = 함께).\n" +
             "Color 면 성분(Alpha/Rgb), Float 면 값. 예: M_GUlt_StrikeBeam · _TintColor Alpha(인스펙터 \"Color\" — 셰이더가 알파를 제곱하므로 0.6 → 체감 0.36)\n" +
             "/ GunnerUlt_FloorLine · _Color Rgb(인스펙터 \"Color\" — a 는 셰이더가 안 씀, HDR 이 커서 밝기를 낮춰야 흐려진다) + _Opacity(알파)")]
    [SerializeField] private EffectMaterialAlphaRule[] otherClientAlpha =
    {
        new EffectMaterialAlphaRule(null, "_TintColor", 0.6f),
        new EffectMaterialAlphaRule(null, "_Color", 0.6f, EffectMaterialColorChannel.Rgb),
        new EffectMaterialAlphaRule(null, "_Opacity", 0.6f),
    };

    /// <summary>시전자 미지정(서버가 아직 안 썼음) — 원래대로 보여 준다.</summary>
    public const ulong NoCaster = ulong.MaxValue;

    private readonly NetworkVariable<ulong> netCasterClientId = new NetworkVariable<ulong>(NoCaster);

    private readonly NetworkVariable<float> netRadius = new NetworkVariable<float>();
    private readonly NetworkVariable<float> netInterval = new NetworkVariable<float>();
    private readonly NetworkVariable<int> netEnemyMask = new NetworkVariable<int>();

    // 오프라인(네트워크 없이) — 같은 값을 로컬에 든다
    private float localRadius;
    private float localInterval;
    private int localEnemyMask;

    private struct Lock
    {
        public EffectHandle handle;
        public TelegraphRing ring;
    }

    private readonly Dictionary<Unit, Lock> locks = new Dictionary<Unit, Lock>();
    private readonly HashSet<Unit> inside = new HashSet<Unit>();
    private readonly List<Unit> stale = new List<Unit>();
    private readonly List<GameObject> instances = new List<GameObject>();
    private readonly Collider[] overlap = new Collider[64];

    private EffectHandle zone;
    private float phaseStart;
    private bool tornDown;

    private float Radius => IsSpawned ? netRadius.Value : localRadius;
    private float Interval => IsSpawned ? netInterval.Value : localInterval;
    private int EnemyMask => IsSpawned ? netEnemyMask.Value : localEnemyMask;
    private EffectTint OverdriveTint =>
        overdriveColors != null ? overdriveColors.TintFor(IsSpawned ? netOverdriveStage.Value : localOverdriveStage) : EffectTint.None;

    /// <summary>
    /// 이 화면이 시전자가 아닌가(= 알파를 낮춰 보여 주나). 순수 규칙 — 오프라인(스폰 안 됨)·시전자 미지정·로컬 = 시전자면 false.
    /// 호스트도 시전자가 아니면 true(다른 클라와 같다).
    /// </summary>
    public static bool IsOtherClientView(bool spawned, ulong localClientId, ulong casterClientId) =>
        spawned && casterClientId != NoCaster && localClientId != casterClientId;

    private IReadOnlyList<EffectMaterialAlphaRule> ViewAlphaRules =>
        otherClientAlpha != null && otherClientAlpha.Length > 0 && NetworkManager != null &&
        IsOtherClientView(IsSpawned, NetworkManager.LocalClientId, netCasterClientId.Value)
            ? otherClientAlpha
            : null;

    private void OnEnable() => phaseStart = Time.time;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        phaseStart = Time.time;   // 첫 피해는 생성 즉시 — 그 순간을 박자의 시작으로 본다
    }

    public override void OnNetworkDespawn()
    {
        TearDown();
        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        TearDown();   // 오프라인·씬 정리 안전망. 이미 정리했으면 아무 일도 안 한다.
        base.OnDestroy();
    }

    #region 서버 → 전 피어

    /// <summary>[서버] 레이저 초기화 직후 1회. 반경·피해 간격·적 레이어를 연출에 넘긴다.</summary>
    public void ServerConfigure(float radius, float interval, LayerMask enemyLayers)
    {
        if (IsSpawned)
        {
            if (!IsServer) return;
            netRadius.Value = radius;
            netInterval.Value = interval;
            netEnemyMask.Value = enemyLayers.value;
        }
        else
        {
            localRadius = radius;
            localInterval = interval;
            localEnemyMask = enemyLayers.value;
        }
    }

    /// <summary>
    /// [서버] 한계 돌파 색 단계(= 시전 시작 저장 단계, 한계 돌파가 아니면 0). <b>스폰 전에</b> 부른다 — 초기값으로 스폰 메시지에 실려
    /// 스폰과 같은 프레임의 첫 낙뢰(StrikeRpc)보다 먼저 클라에 닿는다. 연출 색만 — 판정 무관.
    /// </summary>
    public void ServerSetOverdriveStage(int stage)
    {
        byte value = (byte)Mathf.Clamp(stage, 0, 255);
        localOverdriveStage = value;
        if (!IsSpawned || IsServer)
            netOverdriveStage.Value = value;
    }

    /// <summary>
    /// [서버] 시전자 ClientId. <b>스폰 전에</b> 부른다(<see cref="ServerSetOverdriveStage"/> 와 같은 이유 — 첫 낙뢰보다 먼저 닿는다).
    /// 다른 클라 화면 알파 판정에만 쓴다 — 판정 무관.
    /// </summary>
    public void ServerSetCaster(ulong casterClientId)
    {
        if (!IsSpawned || IsServer)
            netCasterClientId.Value = casterClientId;
    }

    /// <summary>[서버] 피해를 준 순간. 실제로 맞은 적의 위치마다 레이저를 꽂으라고 전 피어에 알린다.</summary>
    public void ServerStrike(List<Unit> landed)
    {
        if (landed == null || landed.Count == 0) return;

        var positions = new Vector3[landed.Count];
        for (int i = 0; i < landed.Count; i++)
            positions[i] = landed[i] != null ? landed[i].transform.position : transform.position;

        if (IsSpawned)
        {
            if (IsServer) StrikeRpc(positions);
        }
        else
        {
            PlayStrikes(positions);
        }
    }

    // Reliable — 첫 피해는 스폰과 같은 프레임에 난다. 비신뢰 채널은 스폰 메시지보다 먼저 도착해 버려질 수 있다.
    [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Reliable)]
    private void StrikeRpc(Vector3[] positions) => PlayStrikes(positions);

    private void PlayStrikes(Vector3[] positions)
    {
        phaseStart = Time.time;   // 원이 0 이 되는 순간 = 지금

        // 광선 발사음(사운드 명세 Gunner_R_Skill — 설정 간격마다 광선을 실제로 쏠 때마다, PLAN-sound-spec-apply 3.16).
        // 이 RPC 1번 = 낙뢰가 보이는 틱 1번 — 맞은 대상 수와 무관하게 1번(경석 Q6). 빈 틱은 서버가 RPC 를 안 보내 무음. VFX 배선과 무관하게 먼저.
        if (positions != null && positions.Length > 0)
            GameSoundHooks.Play(GameSoundHooks.GunnerR, positions[0]);

        if (strikeEffect == null || !EffectManager.TryGet(out EffectManager effects, this)) return;

        EffectTint tint = OverdriveTint;
        IReadOnlyList<EffectMaterialAlphaRule> alpha = ViewAlphaRules;
        for (int i = 0; i < positions.Length; i++)
        {
            // 맞은 적 발밑(루트)이 아니라 그 아래 바닥에 꽂는다 — 송전탑처럼 루트가 올라온 대상도 바닥 파문이 바닥에 눕는다.
            Vector3 position = FloorPoint(positions[i]);
            if (!tint.IsSet && alpha == null)
            {
                effects.Play(strikeEffect, position, Quaternion.identity, strikeScale);
                continue;
            }

            // 색·알파 지정 재생은 핸들이 있어야 한다 — 수명·반납은 Play 와 같다. 색 먼저, 알파 나중(EffectMaterialAlpha 참고).
            EffectHandle strike = effects.PlayRecallable(strikeEffect, position, Quaternion.identity, strikeScale);
            if (tint.IsSet) effects.SetTint(strike, tint);
            if (alpha != null) effects.SetMaterialAlpha(strike, alpha);
        }
    }

    #endregion

    private void LateUpdate()
    {
        if (tornDown || Radius <= 0f || Interval <= 0f) return;
        if (!EffectManager.TryGet(out EffectManager effects, this)) return;

        UpdateZone(effects);
        UpdateLocks(effects);
    }

    private void UpdateZone(EffectManager effects)
    {
        if (zoneEffect == null) return;

        // 레이저는 수평으로만 움직이고 높이는 스폰 높이(조준점·대상 위치) 그대로라, 올라온 송전탑을 노리면 공중에 뜬다.
        // 장판은 레이저 바로 아래 바닥에 눕힌다(이동하며 바닥 높이가 바뀌어도 따라가도록 매 프레임).
        Vector3 floor = FloorPoint(transform.position);

        if (!zone.IsSet)
        {
            zone = effects.PlayLooping(zoneEffect, floor, Quaternion.identity, Radius * 2f);
            effects.SetTint(zone, OverdriveTint);
            effects.SetMaterialAlpha(zone, ViewAlphaRules);   // 시전자 화면이면 null = 원래대로
        }

        effects.GetInstances(zone, instances);
        for (int i = 0; i < instances.Count; i++)
            instances[i].transform.position = floor;
    }

    /// <summary>
    /// <paramref name="point"/> 바로 아래 바닥 + <see cref="GroundProbe.SurfaceOffset"/>. 못 찾으면 그대로.
    /// <see cref="GroundProbe"/> 는 Unit 계층 콜라이더(송전탑·몹 몸)를 건너뛰므로 대상 자신은 바닥으로 잡히지 않는다.
    /// 팔라딘 R 표식·낙뢰(<c>PaladinSkillView.PlayTargetFloorLocal</c>)와 같은 규칙.
    /// </summary>
    private static Vector3 FloorPoint(Vector3 point)
    {
        if (GroundProbe.TryFindGround(point, 0, out RaycastHit ground, out _))
            point.y = GroundProbe.SurfaceY(ground);
        return point;
    }

    private void UpdateLocks(EffectManager effects)
    {
        if (lockEffect == null) return;

        // 지금 범위 안의 적 — 서버 판정(GunnerTrackingLaser.DamageTick)과 같은 규칙
        inside.Clear();
        int count = Physics.OverlapSphereNonAlloc(transform.position, Radius, overlap, EnemyMask, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            Unit unit = OwnerOf(overlap[i]);
            if (unit != null && !(unit is Player) && unit.CurrentHealth > 0)
                inside.Add(unit);
        }

        // 나간 적(또는 사라진 적) — 표식을 거둔다
        stale.Clear();
        foreach (KeyValuePair<Unit, Lock> pair in locks)
            if (pair.Key == null || !inside.Contains(pair.Key)) stale.Add(pair.Key);
        for (int i = 0; i < stale.Count; i++)
        {
            effects.Release(locks[stale[i]].handle);
            locks.Remove(stale[i]);
        }

        // 들어온 적 — 표식을 붙인다
        foreach (Unit unit in inside)
        {
            if (locks.ContainsKey(unit)) continue;

            EffectHandle handle = effects.PlayLooping(lockEffect, unit.transform, Vector3.zero, LockScale(unit));
            TelegraphRing ring = null;
            if (effects.GetInstances(handle, instances) > 0)
                ring = instances[0].GetComponentInChildren<TelegraphRing>(true);
            locks.Add(unit, new Lock { handle = handle, ring = ring });
        }

        // 원 진행도 — 마지막 발사 이후 '경과' 비율(0 = 막 쐈다, 1 = 다음 발사). 맞은 적이 없던 주기에도
        // 박자는 이어진다 — 서버는 nextDamageTime += damageInterval 로 고정 간격을 지킨다.
        float progress = Mathf.Repeat(Time.time - phaseStart, Interval) / Interval;
        foreach (Lock l in locks.Values)
            if (l.ring != null) l.ring.SetProgress(progress);
    }

    private static Unit OwnerOf(Collider col)
    {
        if (col == null) return null;
        Unit unit = null;
        Hurtbox hurtbox = col.GetComponentInParent<Hurtbox>();
        if (hurtbox != null) hurtbox.TryGetOwner(out unit);
        return unit != null ? unit : col.GetComponentInParent<Unit>();
    }

    private float LockScale(Unit unit)
    {
        float radius = fallbackBodyRadius;
        Vector3 s = unit.transform.lossyScale;
        float scale = Mathf.Max(s.x, s.z);
        if (unit.TryGetComponent(out NavMeshAgent agent)) radius = agent.radius * scale;
        else if (unit.TryGetComponent(out CapsuleCollider capsule)) radius = capsule.radius * scale;
        return Mathf.Clamp(radius * lockScalePerRadius, lockScaleMin, lockScaleMax);
    }

    /// <summary>
    /// 표식과 장판을 해제한다(장판은 위치 고정 재생이라 레이저가 파괴돼도 엔트리 outro 설정대로 퇴장한다).
    /// 여러 번 불려도 한 번만 한다.
    /// </summary>
    private void TearDown()
    {
        if (tornDown) return;
        tornDown = true;

        EffectManager effects = EffectManager.Instance;
        if (effects == null) return;

        foreach (Lock l in locks.Values) effects.Release(l.handle);
        locks.Clear();

        if (!zone.IsSet) return;
        effects.Release(zone);
        zone = EffectHandle.None;
    }
}
