using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 송전기(차징 기둥) 연출 — 몸의 전류·심지 발광 · 타격 스파크 · 부서짐 방전(2026-10-22 민경 grill — PLAN.md 「23호 송전기(차징) 연출 교체」).
/// 아레나(<c>bossroom.prefab</c>)의 송전기 4개에 <see cref="BossChargingPylon"/> 과 나란히 붙는다.
///
/// <b>왜 상태를 따로 복제하나.</b> <see cref="BossChargingPylon"/>(경석)의 상태는 서버 전용이고 클라에는 Live 여부(아웃라인)만 간다 —
/// 클라는 "부서져서 내려감"과 "시간 초과로 내려감"을 못 가른다. 그래서 서버가 매 프레임 그 상태를 읽어
/// <see cref="Phase"/> 하나로 복제한다. 경석 코드는 건드리지 않는다(읽기만).
///
/// <list type="bullet">
/// <item>Rising → <see cref="PylonChargeVisual"/> 약하게 · Live → 확 밝아짐 · Lowering(시간 초과·끊김) → 0.3초에 꺼짐</item>
/// <item>Destroyed → 거의 즉시 꺼짐 + 방전 원샷(<see cref="discharge"/>)</item>
/// <item>타격(<see cref="Unit.ClientDamagedAttributed"/>, 전 피어) → 송전기 전체 림 번쩍 + 심지에서 사방으로 스파크(<see cref="hitSpark"/>), 송전기마다 최소 간격</item>
/// </list>
/// 보스 쪽 전기줄은 <see cref="BossChargeTetherView"/> 가 <see cref="All"/>·<see cref="PhaseChanged"/> 로 읽는다.
///
/// 늦게 들어온 클라는 스폰 때 지금 상태로 맞춘다(섬광·방전 없이).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BossChargingPylon))]
public class PylonChargeView : NetworkBehaviour
{
    public enum Phase : byte { Hidden, Rising, Live, Destroyed, Lowering }

    /// <summary>전 피어 — 스폰된 송전기 연출 목록.</summary>
    public static readonly List<PylonChargeView> All = new List<PylonChargeView>();

    /// <summary>전 피어 — 상태가 바뀐 순간(늦은 접속의 첫 동기화는 제외). (송전기, 이전, 다음)</summary>
    public static event Action<PylonChargeView, Phase, Phase> PhaseChanged;

    [Header("이펙트")]
    [Tooltip("송전기 몸 연출(PylonChargeVisual). FX_PylonCharge — 송전기 메시 자식으로 하나 만든다")]
    [SerializeField] GameObject chargePrefab;
    [Tooltip("타격 스파크(원샷). 심지 가운데서 퍼진다. FX_PylonHitSpark_Entry")]
    [SerializeField] EffectEntry hitSpark;
    [Tooltip("부서짐 방전(원샷). FX_PylonDischarge_Entry")]
    [SerializeField] EffectEntry discharge;

    [Header("타격 스파크")]
    [Tooltip("송전기마다 최소 간격(초) — 다단히트·광역기가 겹쳐도 폭주하지 않게(몸 번쩍도 같은 간격)")]
    [SerializeField, Min(0f)] float hitSparkInterval = 0.08f;
    [SerializeField, Min(0.01f)] float hitSparkScale = 1f;
    [SerializeField, Min(0.01f)] float dischargeScale = 1f;

    readonly NetworkVariable<byte> _phase = new NetworkVariable<byte>((byte)Phase.Hidden);

    BossChargingPylon _pylon;
    PylonChargeVisual _visual;
    float _lastSpark = float.NegativeInfinity;

    public Phase Current => (Phase)_phase.Value;

    /// <summary>전기줄이 뻗어 나가는 점(송전기 꼭대기).</summary>
    public Transform TetherAnchor => _visual != null ? _visual.TetherAnchor : transform;

    void Awake()
    {
        _pylon = GetComponent<BossChargingPylon>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        EnsureVisual();

        _phase.OnValueChanged += OnPhaseChanged;
        _pylon.ClientDamagedAttributed += OnDamaged;
        if (!All.Contains(this)) All.Add(this);

        if (IsServer) _phase.Value = (byte)ServerPhase();
        ApplyVisual(Phase.Hidden, Current, firstSync: true);
    }

    public override void OnNetworkDespawn()
    {
        _phase.OnValueChanged -= OnPhaseChanged;
        if (_pylon != null) _pylon.ClientDamagedAttributed -= OnDamaged;
        All.Remove(this);
        base.OnNetworkDespawn();
    }

    void Update()
    {
        if (!IsSpawned || !IsServer) return;
        byte next = (byte)ServerPhase();
        if (_phase.Value != next) _phase.Value = next;
    }

    // [서버] 경석의 서버 전용 상태 → 복제용 단계.
    Phase ServerPhase()
    {
        switch (_pylon.State)
        {
            case BossChargingPylon.PylonState.Rising: return Phase.Rising;
            case BossChargingPylon.PylonState.Live: return Phase.Live;
            case BossChargingPylon.PylonState.Lowering: return _pylon.WasDestroyed ? Phase.Destroyed : Phase.Lowering;
            default: return Phase.Hidden;
        }
    }

    void OnPhaseChanged(byte previous, byte next)
    {
        ApplyVisual((Phase)previous, (Phase)next, firstSync: false);
        PhaseChanged?.Invoke(this, (Phase)previous, (Phase)next);
    }

    void ApplyVisual(Phase previous, Phase next, bool firstSync)
    {
        if (_visual == null) return;
        switch (next)
        {
            case Phase.Rising: _visual.SetState(PylonChargeVisual.State.Rising); break;
            case Phase.Live: _visual.SetState(PylonChargeVisual.State.Live); break;
            case Phase.Destroyed:
                _visual.Kill();
                if (!firstSync && (previous == Phase.Live || previous == Phase.Rising)) PlayDischarge();
                break;
            default: _visual.SetState(PylonChargeVisual.State.Off); break;
        }
    }

    // 송전기 메시 자식으로 한 번 만든다. 🔴 스폰 때(= 모든 Awake 뒤) — BossChargingPylon.Awake 가 렌더러를 긁어 아웃라인을 만드는데,
    // 그보다 먼저 만들면 심지·코일 겹침에도 노랑 아웃라인이 생긴다. NoHitFlash 로 피격 플래시·호버 아웃라인에서도 뺀다.
    void EnsureVisual()
    {
        if (_visual != null || chargePrefab == null) return;

        Transform meshT = transform;
        if (GetComponent<MeshFilter>() == null)
            foreach (MeshFilter mf in GetComponentsInChildren<MeshFilter>(true))
                if (mf.GetComponentInParent<NoHitFlash>() == null) { meshT = mf.transform; break; }

        GameObject go = Instantiate(chargePrefab, meshT, false);
        go.AddComponent<NoHitFlash>();
        _visual = go.GetComponent<PylonChargeVisual>();
        if (_visual == null)
            Debug.LogWarning($"{name}: chargePrefab 에 PylonChargeVisual 이 없다 — 몸 연출이 안 나온다.", this);
    }

    void PlayDischarge()
    {
        if (discharge == null || !EffectManager.TryGet(out EffectManager effects, this)) return;
        effects.Play(discharge, transform.position, Quaternion.identity, dischargeScale);
    }

    // 전 피어 — 서버가 피해를 확정할 때마다 온다. 맞은 방향은 보지 않는다(10-23 민경 grill):
    // 송전기 전체 림이 번쩍(PylonChargeVisual.Pulse) + 심지 가운데서 스파크가 사방(위쪽 반구, 수평 위주)으로 퍼진다.
    void OnDamaged(DamageDealtInfo info)
    {
        if (Current != Phase.Live) return;
        if (Time.time - _lastSpark < hitSparkInterval) return;
        _lastSpark = Time.time;

        if (_visual != null) _visual.Pulse();

        if (hitSpark == null || !EffectManager.TryGet(out EffectManager effects, this)) return;
        Vector3 at = _visual != null ? _visual.SparkOrigin : transform.position + Vector3.up * 0.3f;
        effects.Play(hitSpark, at, Quaternion.identity, hitSparkScale);
    }
}
