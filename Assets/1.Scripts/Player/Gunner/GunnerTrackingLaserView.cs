using System.Collections;
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
///       레이저가 사라지면 <see cref="zoneOutroSeconds"/> 동안 줄어들며 퇴장한다.</item>
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
/// 디스폰과 함께 장판이 뚝 끊긴다. 그래서 위치 고정으로 띄우고 매 프레임 직접 옮기다가, 디스폰 때 떼어 내 줄인다.
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

    [Header("장판")]
    [Tooltip("레이저가 사라질 때 장판이 줄어드는 시간(초)")]
    [SerializeField, Min(0f)] private float zoneOutroSeconds = 0.3f;

    [Header("조준 표식 크기")]
    [Tooltip("배율 = 대상 몸 반경 × 이 값(아래 상·하한)")]
    [SerializeField, Min(0.1f)] private float lockScalePerRadius = 2f;
    [SerializeField, Min(0.1f)] private float lockScaleMin = 0.8f;
    [SerializeField, Min(0.1f)] private float lockScaleMax = 3f;
    [Tooltip("몸 반경을 못 읽었을 때(NavMeshAgent·CapsuleCollider 없음)")]
    [SerializeField, Min(0.05f)] private float fallbackBodyRadius = 0.5f;

    [Header("레이저")]
    [SerializeField, Min(0.01f)] private float strikeScale = 1f;

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
        if (strikeEffect == null || !EffectManager.TryGet(out EffectManager effects, this)) return;

        for (int i = 0; i < positions.Length; i++)
            effects.Play(strikeEffect, positions[i], Quaternion.identity, strikeScale);
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

        if (!zone.IsSet)
            zone = effects.PlayLooping(zoneEffect, transform.position, Quaternion.identity, Radius * 2f);

        effects.GetInstances(zone, instances);
        for (int i = 0; i < instances.Count; i++)
            instances[i].transform.position = transform.position;
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

    /// <summary>표식은 바로 거두고, 장판은 떼어 내 줄이다가 반납한다. 여러 번 불려도 한 번만 한다.</summary>
    private void TearDown()
    {
        if (tornDown) return;
        tornDown = true;

        EffectManager effects = EffectManager.Instance;
        if (effects == null) return;

        foreach (Lock l in locks.Values) effects.Release(l.handle);
        locks.Clear();

        if (!zone.IsSet) return;
        if (zoneOutroSeconds > 0f && effects.isActiveAndEnabled)
            effects.StartCoroutine(ShrinkThenRecycle(effects, zone, zoneOutroSeconds));
        else
            effects.ReleaseImmediate(zone);
        zone = EffectHandle.None;
    }

    // 이 레이저는 곧 파괴되므로 EffectManager 위에서 돈다. 매 프레임 핸들이 아직 살아 있는지 확인한다 —
    // 그 사이 다른 경로로 회수돼 재대출된 인스턴스를 줄이면 안 된다.
    private static IEnumerator ShrinkThenRecycle(EffectManager effects, EffectHandle handle, float seconds)
    {
        var buffer = new List<GameObject>();
        if (effects.GetInstances(handle, buffer) == 0) yield break;

        var from = new Vector3[buffer.Count];
        for (int i = 0; i < buffer.Count; i++) from[i] = buffer[i].transform.localScale;

        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            if (effects.GetInstances(handle, buffer) != from.Length) yield break;
            float k = 1f - t / seconds;
            for (int i = 0; i < buffer.Count; i++) buffer[i].transform.localScale = from[i] * k;
            yield return null;
        }

        effects.ReleaseImmediate(handle);
    }
}
