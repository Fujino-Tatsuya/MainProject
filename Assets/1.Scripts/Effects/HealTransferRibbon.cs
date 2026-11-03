using System;
using UnityEngine;

/// <summary>
/// 회복 전이 리본 — 나누는 불굴(PAL_2_SHARE_HEAL)에서 팔라딘 → 아군으로 빛 리본 몇 가닥이 휘어 흘러간다
/// (2026-11-02 민경 grill — PLAN.md 「나누는 불굴 전이 VFX」, 참고 = 알리스타 E). 리본 프리팹 루트에 붙는다.
///
/// - 가닥 = 이 오브젝트의 자식 중 TrailRenderer 를 가진 것. 가닥마다 좌우·위로 휘는 정도와 물결 위상을 무작위로 받는다.
///   가닥 아래 입자(월드 시뮬레이션, 시간당 방출)는 날아가는 동안만 뿌리고 멈추면 끈다 — 거리당 방출은 풀에서 꺼낼 때
///   출발점으로 순간 이동하는 거리만큼 한 줄로 뿜어서 쓰지 않는다.
/// - <see cref="travelSeconds"/> 뒤 <b>모든 가닥이 동시에</b> 아군 몸에 닿는다 — 그 순간 <c>onArrived</c> 를 부른다
///   (재생하는 쪽이 아군 힐 원샷을 낸다). 아군이 움직여도 매 프레임 지금 위치로 꽂힌다.
/// - 날아가는 중 대상이 사라지면(<c>targetValid</c> false) 가닥이 그 자리에서 멈춰 흐려지고 <c>onArrived</c> 는 부르지 않는다.
/// - 풀에서 꺼낼 때마다 쓰이므로 <see cref="Begin"/> 이 가닥 위치·트레일을 처음으로 되돌리고, 꺼질 때 콜백을 지운다.
/// 네트워크 없음 — 각 피어가 자기 화면의 위치로 날린다.
/// </summary>
public sealed class HealTransferRibbon : MonoBehaviour
{
    [Tooltip("팔라딘 → 아군 도착 시간(초). 거리와 무관하게 이 시간에 닿는다(12m 끝이면 빨리 날아간다).\n" +
             "엔트리 길이(duration)는 이 값 + 트레일 Time 보다 길어야 꼬리까지 보인다")]
    [SerializeField, Min(0.05f)] float travelSeconds = 0.4f;

    [Header("출발·도착 높이")]
    [Tooltip("시전자 위치에서 띄우는 거리(월드) — 몸통 높이")]
    [SerializeField] Vector3 sourceOffset = new Vector3(0f, 1.0f, 0f);
    [Tooltip("대상 위치에서 띄우는 거리(월드) — 가슴 높이")]
    [SerializeField] Vector3 targetOffset = new Vector3(0f, 1.0f, 0f);

    [Header("휘는 모양 (가닥마다 범위 안 무작위)")]
    [Tooltip("옆으로 휘는 폭(m). 가닥마다 좌우 부호도 무작위")]
    [SerializeField] Vector2 sideBend = new Vector2(0.4f, 1.1f);
    [Tooltip("위로 솟는 높이(m)")]
    [SerializeField] Vector2 upBend = new Vector2(0.2f, 0.9f);
    [Tooltip("물결 폭(m) — 경로 위에 얹는 흔들림. 양끝에서는 0")]
    [SerializeField, Min(0f)] float wobbleAmplitude = 0.3f;
    [Tooltip("물결 수(경로 전체에서 몇 번 출렁이는가)")]
    [SerializeField, Min(0f)] float wobbleCount = 1.5f;
    [Tooltip("진행 곡선. 기본 = 처음 느리게, 끝에서 빠르게 꽂힘")]
    [SerializeField] AnimationCurve progress = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 0.6f), new Keyframe(1f, 1f, 1.8f, 0f));

    struct Strand
    {
        public Transform transform;
        public TrailRenderer trail;
        public float side;
        public float up;
        public float phase;
    }

    Strand[] _strands;
    ParticleSystem[] _particles;
    Transform _source;
    Transform _target;
    Func<bool> _targetValid;
    Action _onArrived;
    Vector3 _lastStart;
    Vector3 _lastEnd;
    float _elapsed;
    bool _flying;

    public float TravelSeconds => travelSeconds;

    void Awake() => CollectStrands();

    void OnDisable()
    {
        _flying = false;
        _source = null;
        _target = null;
        _targetValid = null;
        _onArrived = null;
    }

    /// <summary>
    /// [이 피어] 리본을 날린다. <paramref name="source"/> = 시전자, <paramref name="target"/> = 받는 아군.
    /// <paramref name="targetValid"/> 가 false 를 돌려주면 그 자리에서 흐려진다(null 이면 대상 오브젝트가 살아 있는지만 본다).
    /// </summary>
    public void Begin(Transform source, Transform target, Func<bool> targetValid, Action onArrived)
    {
        if (_strands == null) CollectStrands();

        _source = source;
        _target = target;
        _targetValid = targetValid;
        _onArrived = onArrived;
        _elapsed = 0f;
        _flying = true;

        _lastStart = source != null ? source.position + sourceOffset : transform.position;
        _lastEnd = target != null ? target.position + targetOffset : _lastStart;

        for (int i = 0; i < _strands.Length; i++)
        {
            ref Strand s = ref _strands[i];
            float sign = UnityEngine.Random.value < 0.5f ? -1f : 1f;
            // 첫 두 가닥은 좌우로 갈라 한쪽으로 몰리지 않게 한다.
            if (i < 2) sign = i == 0 ? -1f : 1f;
            s.side = sign * UnityEngine.Random.Range(sideBend.x, sideBend.y);
            s.up = UnityEngine.Random.Range(upBend.x, upBend.y);
            s.phase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);

            s.transform.position = _lastStart;
            if (s.trail != null)
            {
                s.trail.Clear();
                s.trail.emitting = true;
            }
        }

        // 순간 이동이 끝난 뒤에 처음부터 다시 튼다.
        foreach (ParticleSystem ps in _particles)
        {
            ps.Clear(false);
            ps.Play(false);
        }
    }

    void LateUpdate()
    {
        if (!_flying) return;

        if (!TargetValid())
        {
            Stop();
            return;
        }

        if (_source != null) _lastStart = _source.position + sourceOffset;
        _lastEnd = _target.position + targetOffset;

        _elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_elapsed / travelSeconds);
        float p = Mathf.Clamp01(progress.Evaluate(t));

        for (int i = 0; i < _strands.Length; i++)
            _strands[i].transform.position = PointOn(_strands[i], p);

        if (t >= 1f)
        {
            Action arrived = _onArrived;
            Stop();
            arrived?.Invoke();
        }
    }

    Vector3 PointOn(in Strand s, float p)
    {
        Vector3 along = _lastEnd - _lastStart;
        Vector3 flat = new Vector3(along.x, 0f, along.z);
        Vector3 side = flat.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, flat.normalized) : Vector3.right;

        // 양끝 0 · 가운데 1 — 휨·물결이 출발점과 도착점을 벗어나지 않게.
        float arc = Mathf.Sin(p * Mathf.PI);
        float wobble = wobbleAmplitude * Mathf.Sin(p * Mathf.PI * 2f * wobbleCount + s.phase) * arc;

        return _lastStart + along * p
               + side * (s.side * arc + wobble)
               + Vector3.up * (s.up * arc);
    }

    bool TargetValid()
    {
        if (_target == null || !_target.gameObject.activeInHierarchy) return false;
        return _targetValid == null || _targetValid();
    }

    // 가닥은 그 자리에 멈추고 트레일 꼬리만 흐려진다(반납은 엔트리 수명이 한다).
    void Stop()
    {
        _flying = false;
        _onArrived = null;
        _targetValid = null;
        if (_strands == null) return;
        for (int i = 0; i < _strands.Length; i++)
            if (_strands[i].trail != null) _strands[i].trail.emitting = false;
        foreach (ParticleSystem ps in _particles)
            ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
    }

    void CollectStrands()
    {
        TrailRenderer[] trails = GetComponentsInChildren<TrailRenderer>(true);
        _strands = new Strand[trails.Length];
        for (int i = 0; i < trails.Length; i++)
            _strands[i] = new Strand { transform = trails[i].transform, trail = trails[i] };
        _particles = GetComponentsInChildren<ParticleSystem>(true);
    }
}
