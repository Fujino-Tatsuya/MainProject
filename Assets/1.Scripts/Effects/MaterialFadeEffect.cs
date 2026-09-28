using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 렌더러 머티리얼의 <b>float 프로퍼티 하나</b>를 0 ↔ 저작값 사이로 페이드시키는 범용 컴포넌트.
/// 켜고 끄는 시점은 밖에서 정한다 — 스킬이 <see cref="FadeIn"/> / <see cref="FadeOut"/>만 부른다.
///
/// <b>왜 <see cref="HolyShieldEffect"/>를 안 쓰나.</b> 그쪽은 <c>EffectManager</c>가 대출한
/// <b>풀 파트 전용</b>이고(<c>ResetForPool</c>·<c>SetPlayRate</c>가 드라이버 계약이다),
/// 프로퍼티 이름이 <c>_ColorFactor</c>/<c>_MaskCutOut</c>으로 굳어 있다. 그 둘을 같이 쓰면
/// <c>Shader_IntegratedEffect</c>에서 <b>알파가 두 번 곱해져</b>(<c>mask_a = tex.a * _MaskCutOut * …</c>,
/// <c>alpha = mask_a * _ColorFactor</c>) 페이드가 과하게 어두워진다.
///
/// <b>어떤 셰이더든 된다.</b> 프로퍼티 이름이 인스펙터 값이라
/// <c>_ColorFactor</c>(Shader_IntegratedEffect) · <c>_Opacity</c>(Alpha Blended) 등에 그대로 붙는다.
///
/// ⚠️ <b>기준값은 <see cref="Collect"/>에서 한 번만 읽는다.</b> <see cref="Apply"/>가 한 번이라도 돌면
/// 인스턴스의 값은 저작값이 아니라 마지막으로 쓴 값이다 — 다시 읽으면 그게 새 기준이 되어
/// 페이드할 때마다 점점 어두워진다. (<see cref="HolyShieldEffect"/>가 같은 함정을 같은 방식으로 피한다)
///
/// ⚠️ <b>시작은 항상 꺼진 상태다.</b> <c>OnEnable</c>에서 0을 찍는다 —
/// 안 그러면 스킬을 쓰기 전부터 오버레이가 켜진 채로 보인다.
/// </summary>
[DisallowMultipleComponent]
public class MaterialFadeEffect : MonoBehaviour
{
    private enum Phase { Idle, In, Hold, Out }

    [Tooltip("대상 렌더러. 비워두면 이 오브젝트와 자식에서 자동 수집한다")]
    [SerializeField] private Renderer[] targets;

    [Tooltip("페이드할 float 프로퍼티 이름.\n" +
             "Shader_IntegratedEffect = _ColorFactor / Alpha Blended = _Opacity")]
    [SerializeField] private string propertyName = "_ColorFactor";

    [Tooltip("켜지는 시간(초)")]
    [SerializeField, Min(0f)] private float fadeInDuration = 0.08f;

    [Tooltip("꺼지는 시간(초)")]
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.25f;

    [Tooltip("켜지는 동안의 곡선. 왼쪽이 0(안 보임), 오른쪽이 1(저작값)")]
    [SerializeField] private AnimationCurve fadeInCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("꺼지는 동안의 곡선. 왼쪽이 1(저작값), 오른쪽이 0(안 보임)")]
    [SerializeField] private AnimationCurve fadeOutCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    private int _propertyId;
    private Material[] _materials;
    private float[] _baseValues;
    private bool _collected;

    private Phase _phase;
    private float _timer;
    private float _duration;

    /// <summary>지금 보이고 있는가(켜지는 중·유지·꺼지는 중 어느 것이든).</summary>
    public bool IsActive => _phase != Phase.Idle;

    /// <summary>켜기 시작한다. 이미 켜져 있으면 처음부터 다시 켠다(연타 대비).</summary>
    public void FadeIn()
    {
        Collect();
        if (_materials == null || _materials.Length == 0) return;

        _duration = fadeInDuration;
        _timer = 0f;
        _phase = Phase.In;

        // 시간이 0이면 이 프레임에 바로 최대치. 아래 Update의 0 나눗셈도 피한다.
        if (_duration <= 0f) { Apply(1f); _phase = Phase.Hold; return; }

        Apply(0f);
    }

    /// <summary>
    /// 끄기 시작한다. <b>켜지는 도중이어도 그 자리에서 끈다</b> —
    /// 다 켜질 때까지 기다리면 스킬이 끝났는데도 오버레이가 남는다.
    /// </summary>
    public void FadeOut()
    {
        if (_phase == Phase.Idle || _phase == Phase.Out) return;

        _duration = fadeOutDuration;
        _timer = 0f;
        _phase = Phase.Out;

        if (_duration <= 0f) { Apply(0f); _phase = Phase.Idle; }
    }

    /// <summary>즉시 끈다. 사망·디스폰처럼 연출을 볼 사람이 없을 때.</summary>
    public void HideImmediate()
    {
        Collect();
        _phase = Phase.Idle;
        _timer = 0f;
        _duration = 0f;
        Apply(0f);
    }

    private void OnEnable() => HideImmediate();

    private void Update()
    {
        if (_phase == Phase.Idle || _phase == Phase.Hold) return;

        _timer += Time.deltaTime / _duration;
        float t = Mathf.Clamp01(_timer);

        if (_phase == Phase.In)
        {
            Apply(Mathf.Clamp01(Evaluate(fadeInCurve, t, t)));
            if (t >= 1f) _phase = Phase.Hold;
            return;
        }

        Apply(Mathf.Clamp01(Evaluate(fadeOutCurve, t, 1f - t)));
        if (t >= 1f) _phase = Phase.Idle;
    }

    // 커브가 비어 있으면 Evaluate가 0을 돌려줘 한 프레임 만에 튄다.
    // 인스펙터에서 키를 다 지웠거나 프리팹에 커브가 직렬화되지 않은 경우 선형으로 떨어진다.
    private static float Evaluate(AnimationCurve curve, float t, float fallback)
        => curve == null || curve.length < 2 ? fallback : curve.Evaluate(t);

    /// <param name="k">0 = 안 보임, 1 = 머티리얼에 저작된 그대로.</param>
    private void Apply(float k)
    {
        if (_materials == null) return;

        for (int i = 0; i < _materials.Length; i++)
        {
            if (_materials[i] == null) continue;

            _materials[i].SetFloat(_propertyId, _baseValues[i] * k);
        }
    }

    private void Collect()
    {
        if (_collected) return;
        _collected = true;

        _propertyId = Shader.PropertyToID(propertyName);

        if (targets == null || targets.Length == 0)
            targets = GetComponentsInChildren<Renderer>(true);

        var materials = new List<Material>();
        var bases = new List<float>();

        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null) continue;

            // materials(복수형)는 인스턴스를 만들어 돌려준다 — sharedMaterial 을 쓰면
            // 프로젝트 에셋이 영구히 바뀐다. 컴포넌트당 1회만 부르도록 캐시한다.
            Material[] instanced = targets[i].materials;

            for (int k = 0; k < instanced.Length; k++)
            {
                // 이 프로퍼티가 없는 머티리얼은 건너뛴다 — 같은 렌더러에 섞여 있는
                // 본체 머티리얼(툰 등)까지 건드리지 않기 위한 유일한 방어선이다.
                if (instanced[k] == null || !instanced[k].HasProperty(_propertyId)) continue;

                materials.Add(instanced[k]);
                bases.Add(instanced[k].GetFloat(_propertyId));
            }
        }

        _materials = materials.ToArray();
        _baseValues = bases.ToArray();

        if (_materials.Length == 0)
        {
            Edit.LogWarning($"[MaterialFadeEffect] '{name}' 아래에 '{propertyName}' 를 가진 머티리얼이 없다. " +
                            "페이드가 아무 일도 하지 않는다 — 프로퍼티 이름과 머티리얼 슬롯을 확인할 것", this);
        }
    }
}
