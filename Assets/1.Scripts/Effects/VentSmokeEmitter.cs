using System;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// 벤트 구멍마다 연기를 랜덤하게 분출했다 멈췄다 반복시킨다 — 트렌치커버 바닥 <c>floor_metal_trenchcover</c>
/// (2026-10-29 민경 grill — MainProject PLAN.md 「트렌치커버 벤트 연기」).
///
/// - <see cref="holes"/> = 구멍마다 하나씩 놓인 <c>FX_Vent_Smoke</c> 의 루트 입자. 루트 Play 로 압력 퍼프(원샷)·기둥·뭉게(반복)가 함께 켜지고,
///   Stop(방출만 멈춤)으로 끊으면 이미 나온 연기는 위로 흘러 사라진다.
/// - FX 는 Play 직후 예고(구멍에서 가늘게 새어 나옴)를 먼저 내고 본 분출은 약 0.45초 뒤 터진다(입자 startDelay) — <see cref="BurstStarted"/> 는 예고 시작 시점.
/// - 구멍마다 독립 — 분출 <see cref="burstDuration"/> · 휴지 <see cref="pauseDuration"/>. 동시에 <see cref="maxConcurrent"/> 개까지만 분출하고,
///   자리가 없으면 <see cref="retryDelay"/> 뒤에 다시 본다. 켜질 때 구멍마다 휴지 시간을 무작위로 잡아 벤트끼리도 엇박이 된다.
/// - 같은 오브젝트의 렌더러가 어느 카메라에도 안 보이면 분출을 멈추고 시간도 세지 않는다(다시 보이면 새로 엇박을 잡는다).
/// - 네트워크 없음 — 클라마다 로컬 랜덤. 판정·충돌 없음. MyProject 미리보기와 MainProject 가 같은 guid 로 공유한다.
/// </summary>
public class VentSmokeEmitter : MonoBehaviour
{
    [Tooltip("구멍마다 놓인 FX_Vent_Smoke 루트 입자")]
    [SerializeField] ParticleSystem[] holes = Array.Empty<ParticleSystem>();
    [Tooltip("한 번 분출하는 시간(초) 최소~최대 — FX 의 분출 전 예고(새어 나옴 0.45초)를 포함한 길이")]
    [SerializeField] Vector2 burstDuration = new Vector2(1.25f, 2.05f);
    [Tooltip("분출 뒤 쉬는 시간(초) 최소~최대")]
    [SerializeField] Vector2 pauseDuration = new Vector2(2f, 6f);
    [Tooltip("이 벤트에서 동시에 분출하는 구멍 최대 수")]
    [SerializeField, Min(1)] int maxConcurrent = 3;
    [Tooltip("동시 분출이 꽉 찼을 때 다시 볼 때까지(초) 최소~최대")]
    [SerializeField] Vector2 retryDelay = new Vector2(0.3f, 1f);

    /// <summary>구멍이 분출을 시작할 때(구멍 번호, 월드 위치). 사운드 등을 붙이는 자리.</summary>
    public event Action<int, Vector3> BurstStarted;

    float[] _until;
    bool[] _active;
    int _activeCount;
    Renderer _renderer;
    bool _visible;

    void Awake()
    {
        _renderer = GetComponent<Renderer>();
        _until = new float[holes.Length];
        _active = new bool[holes.Length];
    }

    void OnEnable()
    {
        _visible = _renderer == null || _renderer.isVisible;
        Reseed(true);
    }

    void OnDisable() => StopAll(true);

    void OnBecameVisible()
    {
        if (_visible) return;
        _visible = true;
        Reseed(false);
    }

    void OnBecameInvisible()
    {
        _visible = false;
        StopAll(false);
    }

    void Update()
    {
        if (!_visible) return;
        float now = Time.time;
        for (int i = 0; i < holes.Length; i++)
        {
            if (now < _until[i] || holes[i] == null) continue;
            if (_active[i])
            {
                holes[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
                _active[i] = false;
                _activeCount--;
                _until[i] = now + Range(pauseDuration);
            }
            else if (_activeCount >= maxConcurrent)
            {
                _until[i] = now + Range(retryDelay);
            }
            else
            {
                holes[i].Play(true);
                _active[i] = true;
                _activeCount++;
                _until[i] = now + Range(burstDuration);
                BurstStarted?.Invoke(i, holes[i].transform.position);
            }
        }
    }

    // 모든 구멍을 쉬는 상태로 두고, 첫 분출까지 0~최대 휴지 시간 중 무작위로 기다린다.
    void Reseed(bool clear)
    {
        StopAll(clear);
        float now = Time.time;
        for (int i = 0; i < holes.Length; i++) _until[i] = now + Random.Range(0f, pauseDuration.y);
    }

    void StopAll(bool clear)
    {
        var behavior = clear ? ParticleSystemStopBehavior.StopEmittingAndClear : ParticleSystemStopBehavior.StopEmitting;
        for (int i = 0; i < holes.Length; i++)
        {
            if (holes[i] != null) holes[i].Stop(true, behavior);
            _active[i] = false;
        }
        _activeCount = 0;
    }

    static float Range(Vector2 r) => Random.Range(r.x, r.y);
}
