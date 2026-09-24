using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 타이틀 CRT 셰이더(<c>Title/CRTScreen</c>·<c>Title/CRTUI</c>)의 **시간·찢김·버스트 단일 구동자**.
/// </summary>
/// <remarks>
/// Codex 연출 검토(2026-09-23) 반영:
/// - 시간은 unscaled 로 <c>_FxTime</c> 에 직접 넘긴다. 셰이더가 <c>_Time</c> 을 쓰면 C# 커브와 갈라진다.
/// - 불규칙 가로 찢김은 **여기서 사건을 예약**한다(2~3초 간격, 0.05~0.10초). 셰이더가 매 프레임 확률로 뽑으면 프레임률에 따라 빈도가 변한다.
/// - 버스트는 겹치면 **최댓값**을 취한다(합산하면 무한 증폭).
/// - 🔴 <b>실제로 화면에 쓰이는 머티리얼 인스턴스</b>를 등록해야 한다. 원본 애셋이나 다른 복제본을 갱신하면 화면에 안 나온다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class TitleCrtFx : MonoBehaviour
{
    public static TitleCrtFx Active { get; private set; }

    [Header("상시 찢김")]
    [SerializeField] private Vector2 _tearInterval = new(2f, 3f);
    [SerializeField] private Vector2 _tearDuration = new(0.05f, 0.10f);
    [SerializeField] private Vector2 _tearHalfHeight = new(0.01f, 0.035f);
    [SerializeField] private Vector2 _tearOffset = new(0.012f, 0.035f);

    [Header("상시 글리치 버스트 (Idle·접근 — 메인 모니터 지직거림)")]
    [Tooltip("켜져 있으면 짧은 버스트를 불규칙하게 넣는다. 메뉴에서는 끈다(클릭 판정이 흔들리지 않게).")]
    [SerializeField] private bool _ambientBursts = true;
    [SerializeField] private Vector2 _ambientInterval = new(0.6f, 1.8f);
    [SerializeField] private Vector2 _ambientStrength = new(0.25f, 0.6f);
    [SerializeField] private Vector2 _ambientDuration = new(0.06f, 0.18f);

    private float _nextAmbient;

    public bool AmbientBursts
    {
        get => _ambientBursts;
        set => _ambientBursts = value;
    }

    private static readonly int IdTime = Shader.PropertyToID("_FxTime");
    private static readonly int IdGlitch = Shader.PropertyToID("_Glitch");
    private static readonly int IdTear = Shader.PropertyToID("_Tear");

    private readonly List<Material> _targets = new();
    private float _time;
    private float _nextTear;
    private float _tearStart;
    private float _tearEnd;
    private Vector4 _tear;

    private float _burstStart;
    private float _burstEnd;
    private float _burstPeak;

    private void OnEnable()
    {
        Active = this;
        _nextTear = Random.Range(_tearInterval.x, _tearInterval.y);
    }

    private void OnDisable()
    {
        if (Active == this) Active = null;
    }

    public void Register(Material m)
    {
        if (m != null && !_targets.Contains(m)) _targets.Add(m);
    }

    public void Unregister(Material m) => _targets.Remove(m);

    /// <summary>글리치 버스트. 즉시 피크 → 감쇠. 진행 중인 버스트보다 약하면 무시된다.</summary>
    public void Burst(float strength, float duration)
    {
        if (CurrentBurst() > strength) return;
        _burstStart = _time;
        _burstEnd = _time + Mathf.Max(0.01f, duration);
        _burstPeak = Mathf.Clamp01(strength);
    }

    private float CurrentBurst()
    {
        if (_time >= _burstEnd) return 0f;
        float k = Mathf.InverseLerp(_burstStart, _burstEnd, _time);
        return _burstPeak * (1f - k) * (1f - k); // 즉시 피크, 빠른 감쇠
    }

    private void Update()
    {
        _time += Time.unscaledDeltaTime;

        if (_time >= _nextTear)
        {
            _tearStart = _time;
            _tearEnd = _time + Random.Range(_tearDuration.x, _tearDuration.y);
            float sign = Random.value < 0.5f ? -1f : 1f;
            _tear = new Vector4(Random.Range(0.1f, 0.9f), Random.Range(_tearHalfHeight.x, _tearHalfHeight.y),
                                sign * Random.Range(_tearOffset.x, _tearOffset.y), 0f);
            _nextTear = _tearEnd + Random.Range(_tearInterval.x, _tearInterval.y);
        }

        if (_ambientBursts && _time >= _nextAmbient)
        {
            Burst(Random.Range(_ambientStrength.x, _ambientStrength.y), Random.Range(_ambientDuration.x, _ambientDuration.y));
            _nextAmbient = _time + Random.Range(_ambientInterval.x, _ambientInterval.y);
        }

        _tear.w = _time < _tearEnd ? 1f - Mathf.InverseLerp(_tearStart, _tearEnd, _time) : 0f;
        float glitch = CurrentBurst();

        for (int i = _targets.Count - 1; i >= 0; i--)
        {
            Material m = _targets[i];
            if (m == null) { _targets.RemoveAt(i); continue; }
            m.SetFloat(IdTime, _time);
            m.SetFloat(IdGlitch, glitch);
            m.SetVector(IdTear, _tear);
        }
    }

    public float Time01 => _time;
}
