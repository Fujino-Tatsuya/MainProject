using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 송전기(차징 기둥) 몸에 얹는 전류·심지 발광(2026-10-22 민경 grill — MainProject PLAN.md 맨 위).
/// 송전기 메시 자식으로 붙는다 — 겹침 렌더러(심지·코일 전용 메시)는 원본 메시와 같은 로컬 공간이라 단위 Transform 이면 딱 겹친다.
///
/// 세기 하나(<see cref="Level"/>)로 전부 몬다 — 겹침 재질의 <c>_ColorFactor</c>(저작값 × 세기, MaterialPropertyBlock)와
/// 파티클 방출량(저작값 × 세기). 상태 → 목표 세기:
/// <list type="bullet">
/// <item>Off = 0 — 숨김·시간 초과로 내려감(0.3초에 꺼짐)</item>
/// <item>Rising = <see cref="risingLevel"/> — 올라오는 동안 약하게(0.5초에 켜짐, 팔라딘 검 발광과 같은 페이드)</item>
/// <item>Live = 1 — 다 올라와 때릴 수 있음. 들어오는 순간 <see cref="liveFlash"/> 만큼 확 밝아졌다가 내려앉는다</item>
/// </list>
/// <see cref="Kill"/> = 부서짐 — 거의 즉시 꺼진다(방전 연출이 따로 터진다).
/// <see cref="Pulse"/> = 맞음 — 송전기 전체 림(<see cref="pulseRenderer"/>)만 순간 밝아졌다가 돌아온다(Live 일 때만, 10-23 민경 grill).
///
/// MyProject 미리보기와 MainProject 가 <b>같은 guid</b> 로 공유한다 — 네트워크는 모른다(MainProject 의 PylonChargeView 가 상태를 넘긴다).
/// </summary>
[DisallowMultipleComponent]
public class PylonChargeVisual : MonoBehaviour
{
    public enum State { Off, Rising, Live }

    [Tooltip("세기를 _ColorFactor 로 받는 겹침 렌더러(심지·코일)")]
    [SerializeField] Renderer[] glowRenderers;
    [Tooltip("세기만큼 방출량이 줄고 느는 파티클(표면 전류·스파크·심지 후광)")]
    [SerializeField] ParticleSystem[] emitters;
    [Tooltip("전기줄이 뻗어 나가는 점(송전기 꼭대기)")]
    [SerializeField] Transform tetherAnchor;

    [SerializeField, Range(0f, 1f)] float risingLevel = 0.35f;
    [SerializeField, Min(1f)] float liveFlash = 1.8f;
    [SerializeField, Min(0.01f)] float liveFlashTime = 0.3f;
    [SerializeField, Min(0.01f)] float fadeIn = 0.5f;
    [SerializeField, Min(0.01f)] float fadeOut = 0.3f;
    [SerializeField, Min(0.01f)] float killTime = 0.08f;

    [Header("타격 반응")]
    [Tooltip("맞을 때 순간 밝아지는 렌더러(송전기 전체 림). 비우면 glowRenderers 중 이름이 Shell 인 것")]
    [SerializeField] Renderer pulseRenderer;
    [SerializeField, Min(1f)] float hitPulse = 2.5f;
    [SerializeField, Min(0.01f)] float hitPulseTime = 0.12f;
    [Tooltip("타격 스파크가 퍼져 나가는 점. 비우면 심지(이름이 Core 인 겹침 메시) 가운데")]
    [SerializeField] Transform sparkOrigin;

    static readonly int ColorFactorId = Shader.PropertyToID("_ColorFactor");

    readonly List<float> _authoredFactor = new List<float>();
    readonly List<float> _authoredRate = new List<float>();
    MaterialPropertyBlock _block;
    State _state = State.Off;
    float _level;
    float _target;
    float _speed;
    float _flash;     // Live 진입 섬광 — 1 에서 0 으로
    float _pulse;     // 타격 반응 — 1 에서 0 으로
    int _pulseIndex = -1;
    bool _cached;

    public Transform TetherAnchor => tetherAnchor != null ? tetherAnchor : transform;
    public State Current => _state;
    public float Level => _level;

    /// <summary>타격 스파크가 퍼져 나가는 월드 위치 — 심지 가운데.</summary>
    public Vector3 SparkOrigin
    {
        get
        {
            if (sparkOrigin != null) return sparkOrigin.position;
            if (glowRenderers != null)
                foreach (Renderer r in glowRenderers)
                {
                    if (r == null || r.name != "Core") continue;
                    MeshFilter mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) return mf.transform.TransformPoint(mf.sharedMesh.bounds.center);
                }
            return transform.position;
        }
    }

    void Awake()
    {
        Cache();
        Apply(0f);
    }

    void Cache()
    {
        if (_cached) return;
        _cached = true;
        _block = new MaterialPropertyBlock();

        if (glowRenderers != null)
            for (int i = 0; i < glowRenderers.Length; i++)
            {
                Renderer r = glowRenderers[i];
                Material m = r != null ? r.sharedMaterial : null;
                _authoredFactor.Add(m != null && m.HasProperty(ColorFactorId) ? m.GetFloat(ColorFactorId) : 1f);
                if (r != null && (r == pulseRenderer || (pulseRenderer == null && r.name == "Shell"))) _pulseIndex = i;
            }

        if (emitters != null)
            foreach (ParticleSystem ps in emitters)
                _authoredRate.Add(ps != null ? ps.emission.rateOverTimeMultiplier : 0f);
    }

    public void SetState(State state)
    {
        Cache();
        if (state == _state) return;
        State previous = _state;
        _state = state;

        switch (state)
        {
            case State.Off:
                _target = 0f;
                _speed = 1f / fadeOut;
                break;
            case State.Rising:
                _target = risingLevel;
                _speed = 1f / fadeIn;
                break;
            case State.Live:
                _target = 1f;
                _speed = 1f / fadeIn;
                // 처음부터 Live 로 들어오면(늦은 접속) 섬광 없이 바로 켠다.
                if (previous == State.Rising) _flash = 1f;
                else _level = 1f;
                break;
        }
        SetEmitting(state != State.Off);
    }

    /// <summary>부서짐 — 거의 즉시 꺼진다. 남아 있던 파티클은 수명대로 사라진다.</summary>
    public void Kill()
    {
        Cache();
        _state = State.Off;
        _target = 0f;
        _speed = 1f / killTime;
        _flash = 0f;
        _pulse = 0f;
        SetEmitting(false);
    }

    /// <summary>맞음 — 송전기 전체 림이 순간 밝아졌다가 <see cref="hitPulseTime"/> 에 걸쳐 돌아온다. Live 가 아니면 무시.</summary>
    public void Pulse()
    {
        if (_state == State.Live) _pulse = 1f;
    }

    void Update()
    {
        _level = Mathf.MoveTowards(_level, _target, _speed * Time.deltaTime);
        if (_flash > 0f) _flash = Mathf.Max(0f, _flash - Time.deltaTime / liveFlashTime);
        if (_pulse > 0f) _pulse = Mathf.Max(0f, _pulse - Time.deltaTime / hitPulseTime);
        Apply(_level * (1f + (liveFlash - 1f) * _flash));
    }

    void Apply(float k)
    {
        if (glowRenderers != null)
            for (int i = 0; i < glowRenderers.Length; i++)
            {
                Renderer r = glowRenderers[i];
                if (r == null) continue;
                r.enabled = k > 0.001f;
                float pulse = i == _pulseIndex ? 1f + (hitPulse - 1f) * _pulse : 1f;
                r.GetPropertyBlock(_block);
                _block.SetFloat(ColorFactorId, _authoredFactor[i] * k * pulse);
                r.SetPropertyBlock(_block);
            }

        if (emitters != null)
            for (int i = 0; i < emitters.Length; i++)
            {
                if (emitters[i] == null) continue;
                ParticleSystem.EmissionModule e = emitters[i].emission;
                e.rateOverTimeMultiplier = _authoredRate[i] * Mathf.Min(k, 1.5f);
            }
    }

    void SetEmitting(bool on)
    {
        if (emitters == null) return;
        foreach (ParticleSystem ps in emitters)
        {
            if (ps == null) continue;
            if (on && !ps.isPlaying) ps.Play(true);
            else if (!on && ps.isEmitting) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }
}
