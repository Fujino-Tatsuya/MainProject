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
/// 가슴 조명(10-27 민경): 차징 구슬이 재생되는 동안 가슴에 포인트 라이트 — 서서히 밝아지고 은은히 깜빡임,
/// 과충전은 첫 줄이 가슴에 닿는 순간 크게 번쩍 → 꺼짐, 끊김(Abort)은 <see cref="abortLightFadeTime"/> 에 꺼짐. 실행 중에 만든다(프리팹 무수정).
///
/// 전기 장판 칸(10-28 민경): 차징 구슬 재생 중엔 장판 예고·발동 칸을 <see cref="floorTileBrightnessWhileCharging"/> 로 흐리게(<see cref="BossElectricFloor.ClientTileBrightness"/>).
/// 맵 해(10-28 민경): 차징 구슬 재생 중엔 씬의 Directional Light 를 <see cref="sunFadeTime"/> 에 걸쳐 끄고, 끝나면 원래 밝기로.
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

    [Header("가슴 조명(차징 중)")]
    [SerializeField] Color chargeLightColor = new Color(0.45f, 0.85f, 1f, 1f);
    [SerializeField, Min(0f)] float chargeLightIntensity = 60f;
    [SerializeField, Min(0.1f)] float chargeLightRange = 6f;
    [Tooltip("구슬 재생 시작부터 다 밝아지기까지(초)")]
    [SerializeField, Min(0.01f)] float chargeLightRampTime = 0.6f;
    [Tooltip("차징 중 깜빡임 폭 — 0 이면 chargeLightIntensity 로 고정")]
    [SerializeField, Range(0f, 1f)] float chargeLightFlicker = 0f;
    [Tooltip("과충전 순간 밝기 — 이 값에서 overchargeLightTime 동안 0 으로")]
    [SerializeField, Min(0f)] float overchargeLightIntensity = 180f;
    [SerializeField, Min(0.01f)] float overchargeLightTime = 0.35f;
    [SerializeField, Min(0.01f)] float abortLightFadeTime = 0.2f;
    [SerializeField] LightShadows chargeLightShadows = LightShadows.Soft;

    [Header("전기 장판 칸 흐리게(차징 중)")]
    [Tooltip("차징 중 장판 예고·발동 칸 밝기(원래 대비). 1 이면 그대로")]
    [SerializeField, Range(0f, 1f)] float floorTileBrightnessWhileCharging = 0.4f;
    [Tooltip("흐려지고 돌아오는 데 걸리는 시간(초)")]
    [SerializeField, Min(0.01f)] float floorTileFadeTime = 0.3f;

    [Header("맵 해(Directional Light) 끄기(차징 중)")]
    [Tooltip("차징 구슬 재생 중 씬의 Directional Light 를 끈다(끝나면 원래 밝기로)")]
    [SerializeField] bool sunOffWhileCharging = true;
    [Tooltip("꺼지고 돌아오는 데 걸리는 시간(초)")]
    [SerializeField, Min(0.01f)] float sunFadeTime = 0.3f;

    enum LightMode { Off, Charging, AwaitSurge, Flash, FadeOut }

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
    Light _light;
    LightMode _lightMode;
    float _lightLevel;      // 차징 밝기 비율(0 → 1)
    float _lightT;          // 번쩍·꺼짐 경과(초)
    float _lightFrom;       // 꺼지기 시작할 때의 밝기
    BossElectricFloor _floor;   // 보스 루트에 실행 중 붙는다(TwentyThreeBoss) — 처음 쓸 때 찾는다
    float _floorBrightness = 1f;
    Light _sun;                 // 씬의 Directional Light — 차징이 처음 시작될 때 찾는다
    float _sunIntensity;        // 원래 밝기(끄기 시작할 때 저장)
    float _sunLevel = 1f;       // 0 = 꺼짐 · 1 = 원래

    void Awake()
    {
        _light = new GameObject("ChargeChestLight").AddComponent<Light>();
        _light.transform.SetParent(transform, false);
        _light.type = LightType.Point;
        _light.color = chargeLightColor;
        _light.range = chargeLightRange;
        _light.shadows = chargeLightShadows;
        _light.enabled = false;

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
        _lightMode = LightMode.Off;
        _lightLevel = 0f;
        if (_light != null) _light.enabled = false;
        _floorBrightness = 1f;
        if (_floor != null) _floor.ClientTileBrightness = 1f;
        RestoreSun();   // 보스가 사라져도 해가 꺼진 채 남지 않게
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

        UpdateChestLight();
        UpdateFloorBrightness();
        UpdateSun();
    }

    // 맵 해 — 차징 구슬 재생 중엔 sunFadeTime 에 걸쳐 0 까지 낮춰 끄고, 끝나면 원래 밝기로 돌린다(전 피어 각자, 네트워크 없음).
    void UpdateSun()
    {
        bool charging = sunOffWhileCharging && chargeBall != null && chargeBall.IsPlaying;
        if (charging && _sunLevel >= 1f)
        {
            if (_sun == null) _sun = FindSun();
            if (_sun != null) _sunIntensity = _sun.intensity;   // 매 차징 시작 때 원래 값을 다시 읽는다
        }
        if (_sun == null) return;
        float level = Mathf.MoveTowards(_sunLevel, charging ? 0f : 1f, Time.deltaTime / sunFadeTime);
        if (Mathf.Approximately(level, _sunLevel) && level >= 1f) return;
        _sunLevel = level;
        _sun.intensity = _sunIntensity * level;
        _sun.enabled = level > 0f;
    }

    static Light FindSun()
    {
        if (RenderSettings.sun != null && RenderSettings.sun.type == LightType.Directional) return RenderSettings.sun;
        foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional && l.isActiveAndEnabled) return l;
        return null;
    }

    void RestoreSun()
    {
        if (_sun != null && _sunLevel < 1f) { _sun.intensity = _sunIntensity; _sun.enabled = true; }
        _sunLevel = 1f;
    }

    // 전기 장판 칸 — 차징 구슬 재생 중엔 floorTileBrightnessWhileCharging 로 흐리게, 끝나면 원래대로(둘 다 floorTileFadeTime 에 걸쳐).
    void UpdateFloorBrightness()
    {
        if (_floor == null && !BossRoot.TryGetComponent(out _floor)) return;
        float target = chargeBall != null && chargeBall.IsPlaying ? floorTileBrightnessWhileCharging : 1f;
        float step = Mathf.Max(0.0001f, 1f - floorTileBrightnessWhileCharging) / floorTileFadeTime * Time.deltaTime;
        _floorBrightness = Mathf.MoveTowards(_floorBrightness, target, step);
        _floor.ClientTileBrightness = _floorBrightness;
    }

    // 가슴 조명 — 구슬 재생 중이면 차징(다시 재생되면 처음부터), 끝나면 OnBallFinished·OnSurgeArrived 가 정한 대로 꺼진다.
    void UpdateChestLight()
    {
        if (_light == null) return;
        bool playing = chargeBall != null && chargeBall.IsPlaying;
        if (playing && _lightMode != LightMode.Charging)
        {
            _lightMode = LightMode.Charging;
            _lightLevel = 0f;
        }
        else if (!playing && _lightMode == LightMode.Charging)
        {
            // 종료 신호 없이 멈춤(구슬 쪽 컴포넌트가 꺼짐 등) — 끊김처럼 끈다.
            _lightFrom = _light.enabled ? _light.intensity : 0f;
            _lightMode = LightMode.FadeOut;
            _lightT = 0f;
        }

        float intensity = 0f;
        switch (_lightMode)
        {
            case LightMode.Charging:
            case LightMode.AwaitSurge:   // 과충전 줄이 가슴에 닿을 때까지 그대로
                if (_lightMode == LightMode.Charging) _lightLevel = Mathf.Min(1f, _lightLevel + Time.deltaTime / chargeLightRampTime);
                intensity = chargeLightIntensity * _lightLevel * (1f - chargeLightFlicker * Mathf.PerlinNoise(Time.time * 8f, 0.37f));
                break;
            case LightMode.Flash:
                _lightT += Time.deltaTime;
                intensity = overchargeLightIntensity * Mathf.Clamp01(1f - _lightT / overchargeLightTime);
                if (_lightT >= overchargeLightTime) _lightMode = LightMode.Off;
                break;
            case LightMode.FadeOut:
                _lightT += Time.deltaTime;
                intensity = _lightFrom * Mathf.Clamp01(1f - _lightT / abortLightFadeTime);
                if (_lightT >= abortLightFadeTime) _lightMode = LightMode.Off;
                break;
        }
        _light.enabled = intensity > 0.001f;
        if (!_light.enabled) return;
        _light.transform.position = Chest.position;
        _light.intensity = intensity;
    }

    void StartLightFlash()
    {
        _lightMode = LightMode.Flash;
        _lightT = 0f;
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
        if (aborted)
        {
            // 끊김 — 가슴 조명만 빠르게 끈다.
            if (_light != null && (_lightMode == LightMode.Charging || _lightMode == LightMode.AwaitSurge))
            {
                _lightFrom = _light.enabled ? _light.intensity : 0f;
                _lightMode = LightMode.FadeOut;
                _lightT = 0f;
            }
            return;
        }
        foreach (Link l in _links.Values)
        {
            if (l.tether == null || !l.tether.IsConnected) continue;
            l.fadeAt = -1f;
            l.tether.Surge();
            _overchargePending = true;
        }
        // 이어진 줄이 있으면 줄이 가슴에 닿을 때, 없으면 바로 번쩍.
        if (_overchargePending) _lightMode = LightMode.AwaitSurge;
        else StartLightFlash();
    }

    // 첫 줄이 가슴에 닿는 순간 큰 섬광 한 번.
    void OnSurgeArrived(ElectricTether _)
    {
        if (!_overchargePending) return;
        _overchargePending = false;
        StartLightFlash();
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
