using UnityEngine;

/// <summary>
/// 빛 알갱이를 대상에게 끌어당긴다 — 팔라딘 패시브(불굴의 의지) 추가 피해 연출 <c>FX_Paladin_Smite</c> 의 Motes
/// (2026-10-28 민경 grill — MainProject PLAN.md 「팔라딘 패시브 추가 피해 VFX 재제작」 Q2-A "정화된 에너지 흡수").
///
/// - 같은 오브젝트의 입자(월드 시뮬레이션)가 <see cref="pullDelay"/> 초 동안 제멋대로 솟았다가, 그 뒤로는 대상 쪽으로 방향을 틀어 날아간다.
/// - 대상 반경 <see cref="arriveRadius"/> 안에 들어오면 곧바로 사라진다(회복 연출은 대상 쪽이 따로 낸다).
/// - 대상은 재생하는 쪽이 <see cref="SetTarget"/> 으로 넘긴다. 없으면 끌어당기지 않고 제자리에서 흐려진다.
///   풀에서 꺼낼 때마다 쓰이므로 꺼질 때 대상을 지운다.
/// 네트워크 없음 — 각 피어가 자기 화면의 대상 위치로 날린다. MyProject 미리보기와 MainProject 가 같은 guid 로 공유한다.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class SiphonMotes : MonoBehaviour
{
    [Tooltip("태어난 뒤 끌려가기 시작할 때까지(초) — 그 전엔 처음 속도대로 솟는다")]
    [SerializeField, Min(0f)] float pullDelay = 0.12f;
    [Tooltip("끌려갈 때 최고 속도(m/s)")]
    [SerializeField, Min(0.1f)] float pullSpeed = 9f;
    [Tooltip("방향을 트는 빠르기 — 클수록 곧장 날아간다")]
    [SerializeField, Min(0.1f)] float steer = 10f;
    [Tooltip("이 거리 안에 들어오면 사라진다(m)")]
    [SerializeField, Min(0.01f)] float arriveRadius = 0.25f;
    [Tooltip("대상 위치에서 띄우는 거리(대상 기준) — 가슴 높이")]
    [SerializeField] Vector3 targetOffset = new Vector3(0f, 1.1f, 0f);

    ParticleSystem _ps;
    ParticleSystem.Particle[] _buffer;
    Transform _target;

    public void SetTarget(Transform target) => _target = target;

    void Awake() => _ps = GetComponent<ParticleSystem>();

    void OnDisable() => _target = null;

    void LateUpdate()
    {
        if (_target == null || _ps == null) return;
        int max = _ps.main.maxParticles;
        if (_buffer == null || _buffer.Length < max) _buffer = new ParticleSystem.Particle[max];
        int count = _ps.GetParticles(_buffer);
        if (count == 0) return;

        Vector3 goal = _target.TransformPoint(targetOffset);
        float k = 1f - Mathf.Exp(-steer * Time.deltaTime);
        for (int i = 0; i < count; i++)
        {
            ref ParticleSystem.Particle p = ref _buffer[i];
            float age = p.startLifetime - p.remainingLifetime;
            if (age < pullDelay) continue;
            Vector3 to = goal - p.position;
            float dist = to.magnitude;
            if (dist < arriveRadius) { p.remainingLifetime = Mathf.Min(p.remainingLifetime, 0.03f); continue; }
            // 끌려가기 시작한 직후엔 느리게 → 점점 빨라진다.
            float ramp = Mathf.Clamp01((age - pullDelay) / 0.12f);
            p.velocity = Vector3.Lerp(p.velocity, to / dist * pullSpeed * Mathf.Lerp(0.4f, 1f, ramp), k);
        }
        _ps.SetParticles(_buffer, count);
    }
}
