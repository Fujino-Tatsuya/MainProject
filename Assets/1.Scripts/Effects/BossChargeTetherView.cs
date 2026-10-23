using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 송전기 → 23호 가슴 전기줄(2026-10-22 민경 grill — PLAN.md 「23호 송전기(차징) 연출 교체」).
/// <c>TwentyThree.prefab</c> 의 <c>VFX</c> 에 붙는다(레이드는 Variant 라 같이 쓴다). 네트워크 없음 — 각 피어가 복제된 상태만 읽어 그린다.
///
/// <list type="bullet">
/// <item>송전기가 Live(다 올라옴) → 그 송전기 꼭대기에서 가슴으로 줄이 뻗는다(<see cref="ElectricTether.Connect"/>).
///       늦게 들어온 클라도 매 프레임 맞춰 본다.</item>
/// <item>송전기가 부서짐 → 그 줄이 끊긴다(<see cref="ElectricTether.Snap"/>).</item>
/// <item><b>시간 초과</b> → 남은 줄을 따라 과충전(<see cref="ElectricTether.Surge"/>) + 첫 줄이 가슴에 닿는 순간 큰 섬광 한 번(<see cref="overcharge"/>).
///       신호 = 차징 구슬(<see cref="chargeBall"/>)이 <b>사그라짐(Stop)</b>으로 끝남 — 보스는 시간 초과일 때만 Stop, 그 밖(전부 파괴·카운터·사망)은 Abort 를 보낸다.
///       경석 코드는 건드리지 않고 그 결과만 듣는다(<see cref="EffectStagePlayer.Finished"/>).</item>
/// <item>그 밖으로 내려감(끊김·사망) → 조용히 사라진다. 구슬 신호(RPC)와 송전기 상태(NetworkVariable)는 도착 순서가 갈릴 수 있어
///       <see cref="fadeGrace"/> 동안 기다렸다가 사라지게 한다 — 그 사이 Stop 이 오면 과충전으로 바꾼다.</item>
/// </list>
///
/// 가슴 소켓: <see cref="chest"/> 를 지정하면 그것, 비우면 뼈 <see cref="chestBone"/> 를 이름으로 찾아 <see cref="chestOffset"/>(보스 기준) 만큼 띄운 점.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-50)]   // 가슴 점을 ElectricTether.LateUpdate 보다 먼저 옮긴다
public class BossChargeTetherView : MonoBehaviour
{
    [Header("이펙트")]
    [Tooltip("송전기마다 하나씩 만드는 전기줄(ElectricTether). FX_PylonTether")]
    [SerializeField] GameObject tetherPrefab;
    [Tooltip("과충전 가슴 큰 섬광(원샷). FX_PylonOvercharge_Entry")]
    [SerializeField] EffectEntry overcharge;
    [SerializeField, Min(0.01f)] float overchargeScale = 1f;

    [Header("신호")]
    [Tooltip("차징 구슬(VFX/ChargeBall). 사그라짐(Stop)으로 끝나면 시간 초과 = 과충전")]
    [SerializeField] EffectStagePlayer chargeBall;
    [Tooltip("송전기가 끊김으로 내려갈 때 사라지기 전 기다리는 시간(초) — 그 사이 시간 초과 신호가 오면 과충전")]
    [SerializeField, Min(0f)] float fadeGrace = 0.2f;

    [Header("가슴")]
    [Tooltip("줄이 닿는 점. 비우면 아래 뼈 + 오프셋")]
    [SerializeField] Transform chest;
    [SerializeField] string chestBone = "spine_02.x";
    [Tooltip("뼈 위치에서 보스 기준(앞 = +Z)으로 띄우는 거리")]
    [SerializeField] Vector3 chestOffset = new Vector3(0f, 0f, 0.4f);

    class Link
    {
        public ElectricTether tether;
        public bool connected;      // 이번 Live 동안 이미 연결했다(과충전·끊김 뒤 다시 잇지 않게)
        public float fadeAt = -1f;
    }

    readonly Dictionary<PylonChargeView, Link> _links = new Dictionary<PylonChargeView, Link>();
    readonly List<PylonChargeView> _stale = new List<PylonChargeView>();
    Transform _bone;
    Transform _chestPoint;
    bool _overchargePending;

    void Awake()
    {
        if (chest == null)
        {
            _bone = FindDeep(BossRoot, chestBone);
            if (_bone == null)
                Debug.LogWarning($"{name}: 가슴 뼈 '{chestBone}' 을 못 찾았다 — 보스 위치로 줄을 잇는다. chest 를 지정할 것.", this);
            _chestPoint = new GameObject("ChargeTetherChest").transform;
            _chestPoint.SetParent(transform, false);
        }
    }

    void OnEnable()
    {
        PylonChargeView.PhaseChanged += OnPhaseChanged;
        if (chargeBall != null) chargeBall.Finished += OnBallFinished;
    }

    void OnDisable()
    {
        PylonChargeView.PhaseChanged -= OnPhaseChanged;
        if (chargeBall != null) chargeBall.Finished -= OnBallFinished;
        foreach (Link l in _links.Values) if (l.tether != null) l.tether.Fade();
    }

    Transform Chest => chest != null ? chest : _chestPoint;

    // 보스 루트 — 씬에서 다른 오브젝트 아래 놓여도 transform.root 를 쓰지 않는다.
    Transform BossRoot
    {
        get
        {
            Unit unit = GetComponentInParent<Unit>();
            return unit != null ? unit.transform : transform.root;
        }
    }

    void LateUpdate()
    {
        if (chest == null && _chestPoint != null)
        {
            Transform root = BossRoot;
            Vector3 basePos = _bone != null ? _bone.position : root.position + Vector3.up * 2f;
            _chestPoint.position = basePos + root.TransformVector(chestOffset);
        }

        // 연결 맞추기 — Live 인데 아직 안 이은 송전기(늦은 접속 포함).
        foreach (PylonChargeView view in PylonChargeView.All)
        {
            if (view == null) continue;
            Link l = Get(view);
            if (view.Current != PylonChargeView.Phase.Live) { l.connected = false; continue; }
            if (l.connected || l.tether == null) continue;
            l.connected = true;
            l.fadeAt = -1f;
            l.tether.Connect(view.TetherAnchor, Chest);
        }

        // 끊김으로 내려간 줄 — 유예가 지나도 과충전 신호가 없으면 조용히.
        _stale.Clear();
        foreach (KeyValuePair<PylonChargeView, Link> kv in _links)
        {
            if (kv.Key == null) { _stale.Add(kv.Key); continue; }
            Link l = kv.Value;
            if (l.fadeAt >= 0f && Time.time >= l.fadeAt)
            {
                l.fadeAt = -1f;
                if (l.tether != null) l.tether.Fade();
            }
        }
        foreach (PylonChargeView v in _stale)
        {
            if (_links[v].tether != null) Destroy(_links[v].tether.gameObject);
            _links.Remove(v);
        }
    }

    Link Get(PylonChargeView view)
    {
        if (_links.TryGetValue(view, out Link l)) return l;
        l = new Link();
        if (tetherPrefab != null)
        {
            l.tether = Instantiate(tetherPrefab, transform).GetComponent<ElectricTether>();
            if (l.tether != null) l.tether.SurgeArrived += OnSurgeArrived;
        }
        _links[view] = l;
        return l;
    }

    void OnPhaseChanged(PylonChargeView view, PylonChargeView.Phase previous, PylonChargeView.Phase next)
    {
        if (previous != PylonChargeView.Phase.Live && previous != PylonChargeView.Phase.Rising) return;
        Link l = Get(view);
        if (l.tether == null) return;

        if (next == PylonChargeView.Phase.Destroyed) { l.fadeAt = -1f; l.tether.Snap(); }
        else if (next != PylonChargeView.Phase.Live) l.fadeAt = Time.time + fadeGrace;
    }

    // 구슬 종료 — Stop(aborted = false) = 시간 초과.
    void OnBallFinished(bool aborted)
    {
        if (aborted) return;
        foreach (Link l in _links.Values)
        {
            if (l.tether == null || !l.tether.IsConnected) continue;
            l.fadeAt = -1f;
            l.tether.Surge();
            _overchargePending = true;
        }
    }

    // 첫 줄이 가슴에 닿는 순간 큰 섬광 한 번.
    void OnSurgeArrived(ElectricTether _)
    {
        if (!_overchargePending) return;
        _overchargePending = false;
        if (overcharge != null && EffectManager.TryGet(out EffectManager effects, this))
            effects.Play(overcharge, Chest.position, Quaternion.identity, overchargeScale);
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++)
        {
            Transform hit = FindDeep(t.GetChild(i), name);
            if (hit != null) return hit;
        }
        return null;
    }
}
