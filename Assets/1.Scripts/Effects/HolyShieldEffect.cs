using UnityEngine;

/// <summary>
/// 배리어 메쉬를 <b>코드로</b> 등장·유지·소멸시키는 파트 컴포넌트. 수호자의 의지(E)의 방어막이 대상이다.
///
/// <b>왜 코드 구동인가.</b> 유지 구간의 끝이 시간이 아니라 "보호막이 사라지는 순간"이라는 <b>이벤트</b>다.
/// 파티클의 Color over Lifetime은 수명에 묶인 커브라 무기한 유지를 표현할 수 없다 —
/// <see cref="FadeInHoldEffect"/>가 예고 장판에서 내린 것과 같은 판단이고, 이쪽은 대상이
/// 스프라이트가 아니라 메쉬 머티리얼이라 별도 컴포넌트가 됐다.
///
/// <b>무엇을 건드리나.</b> 셰이더(Shader_IntegratedEffect)의 두 프로퍼티만 본다.
/// <code>
/// _ColorFactor  rgb 와 alpha 양쪽에 곱해진다 → 이것만 줄이면 "투명해지면서 흐려진다"
/// _MaskCutOut   디졸브. IS_MASK_FADE 키워드가 있는 머티리얼에서만 동작한다
/// </code>
/// 두 겹(Effect_09_Shield / _Shield_2) 중 <b>_Shield_2 에는 IS_MASK_FADE 가 없어</b> _MaskCutOut이
/// 아무 일도 하지 않는다. 그래서 두 겹을 함께 걷는 역할은 _ColorFactor 가 맡고,
/// _MaskCutOut 은 디졸브가 되는 겹에만 얹히는 덤이다.
///
/// ⚠️ <b>풀 재사용 전제.</b> 원본(<c>ShieldDismiss</c> / 에셋 팩 <c>NewMaterialChange</c>)은
/// Awake·Start 1회 셋업 + 자기 Destroy + <c>enabled = false</c> 로 끝나서, 2회차 대출부터
/// 투명한 채로 다시 나오지 않았다. 여기서는 <b>모든 상태가 <see cref="ResetForPool"/>에서 돌아간다.</b>
///
/// ⚠️ <b>EffectEntry의 outroDuration이 <see cref="outroDuration"/>보다 짧으면 안 된다.</b>
///    <see cref="IEffectSystem.Stop"/>에는 시간 인자가 없어 드라이버가 값을 전달할 통로가 없고,
///    반납 시점은 엔트리의 타이머가 정한다. 엔트리 쪽이 짧으면 배리어가 <b>걷히다 말고 툭 사라진다.</b>
///    엔트리 값은 모든 파트 중 가장 늦게 끝나는 것에 맞춘다(입자 수명 포함).
/// </summary>
[DisallowMultipleComponent]
public class HolyShieldEffect : MonoBehaviour
{
    private enum Phase { Idle, Intro, Hold, Outro }

    [Tooltip("배리어 메쉬 렌더러들. 비워두면 자식에서 자동 수집한다")]
    [SerializeField] Renderer[] targets;

    [Tooltip("배리어가 차오르는 시간(초)")]
    [SerializeField, Min(0f)] float introDuration = 0.25f;

    [Tooltip("배리어가 걷히는 시간(초).\n" +
             "⚠️ EffectEntry 의 outroDuration 과 같은 값으로 맞출 것 — 드라이버가 전달할 통로가 없다")]
    [SerializeField, Min(0f)] float outroDuration = 0.8f;

    [Tooltip("소멸 중 남은 밝기. 왼쪽이 시작(1), 오른쪽이 끝(0)")]
    [SerializeField] AnimationCurve outroCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    static readonly int ColorFactorId = Shader.PropertyToID("_ColorFactor");
    static readonly int MaskCutOutId = Shader.PropertyToID("_MaskCutOut");

    // renderer.materials 가 만든 인스턴스. sharedMaterial 을 쓰면 프로젝트 에셋이 영구히 바뀐다.
    Material[] _materials;
    float[] _baseColorFactor;
    bool _collected;

    Phase _phase;
    float _timer;
    float _duration;
    float _playRate = 1f;

    /// <summary>지금 배리어가 떠 있는가(등장·유지·소멸 중 어느 것이든).</summary>
    public bool IsActive => _phase != Phase.Idle;

    /// <summary>
    /// 등장 시작. 드라이버가 대출 직후 부른다.
    /// </summary>
    /// <param name="duration">런타임으로 정해진 시간. 0이면 프리팹의 <see cref="introDuration"/>을 쓴다.</param>
    public void BeginIntro(float duration)
    {
        Collect();

        _duration = duration > 0f ? duration : introDuration;
        _timer = 0f;
        _phase = Phase.Intro;

        Apply(0f);
    }

    /// <summary>
    /// 소멸 시작. 등장이 아직 안 끝났어도 그 자리에서 걷기 시작한다
    /// (보호막이 생기자마자 깨지는 경우 — 등장이 끝날 때까지 기다리면 배리어가 남는다).
    /// </summary>
    /// <param name="duration">런타임으로 정해진 시간. 0이면 프리팹의 <see cref="outroDuration"/>을 쓴다.</param>
    public void BeginOutro(float duration)
    {
        if (_phase == Phase.Idle || _phase == Phase.Outro) return;

        Collect();

        _duration = duration > 0f ? duration : outroDuration;
        _timer = 0f;
        _phase = Phase.Outro;
    }

    /// <summary>히트스톱 배율. 0이면 등장·소멸이 멈춘다.</summary>
    public void SetPlayRate(float rate) => _playRate = Mathf.Max(0f, rate);

    /// <summary>
    /// 풀 반납 직전 초기화. <b>반드시 보이지 않는 상태로 되돌린다</b> —
    /// 안 되돌리면 다음 대출자가 이미 떠 있는 배리어를 물려받는다.
    /// </summary>
    public void ResetForPool()
    {
        _phase = Phase.Idle;
        _timer = 0f;
        _duration = 0f;
        _playRate = 1f;

        Collect();
        Apply(0f);
    }

    void Update()
    {
        // 유지 구간은 아무것도 하지 않는다 = 해제될 때까지 그대로 떠 있는다.
        if (_phase == Phase.Idle || _phase == Phase.Hold) return;

        // 시간이 0이면 곧장 끝 상태로. 나누기 전에 걸러 0 나눗셈을 피한다.
        if (_duration <= 0f)
        {
            if (_phase == Phase.Intro) { Apply(1f); _phase = Phase.Hold; }
            else { Apply(0f); _phase = Phase.Idle; }
            return;
        }

        _timer += (Time.deltaTime * _playRate) / _duration;
        float t = Mathf.Clamp01(_timer);

        if (_phase == Phase.Intro)
        {
            Apply(t);
            if (t >= 1f) _phase = Phase.Hold;
            return;
        }

        Apply(Mathf.Clamp01(EvaluateOutro(t)));
        if (t >= 1f) _phase = Phase.Idle;
    }

    // 커브가 비어 있으면 Evaluate 가 0을 돌려줘서 페이드가 아니라 한 프레임 만에 꺼진다.
    // 인스펙터에서 키를 다 지웠거나 프리팹에 커브가 직렬화되지 않은 경우에 대한 대비.
    float EvaluateOutro(float t)
    {
        if (outroCurve == null || outroCurve.length < 2) return 1f - t;

        return outroCurve.Evaluate(t);
    }

    /// <param name="k">0 = 안 보임, 1 = 프리팹에 저작된 그대로.</param>
    void Apply(float k)
    {
        if (_materials == null) return;

        for (int i = 0; i < _materials.Length; i++)
        {
            Material material = _materials[i];
            if (material == null) continue;

            material.SetFloat(ColorFactorId, _baseColorFactor[i] * k);

            // 이 프로퍼티가 없는 머티리얼에 쓰면 조용히 무시되지만, HasProperty 로 걸러 두면
            // 프로파일러에서 "왜 이 머티리얼에 쓰기가 잡히나"를 되묻지 않아도 된다.
            if (material.HasProperty(MaskCutOutId)) material.SetFloat(MaskCutOutId, k);
        }
    }

    /// <summary>
    /// 머티리얼 인스턴스와 저작 값을 한 번만 수집한다.
    ///
    /// ⚠️ <b>기준값은 여기서만 읽는다.</b> <see cref="Apply"/>가 이미 한 번이라도 돌면
    /// 인스턴스의 _ColorFactor 는 저작 값이 아니라 마지막으로 쓴 값이다 —
    /// 나중에 다시 읽으면 그 값이 새 기준이 되어 재사용마다 배리어가 어두워진다.
    /// </summary>
    void Collect()
    {
        if (_collected) return;
        _collected = true;

        if (targets == null || targets.Length == 0)
            targets = GetComponentsInChildren<Renderer>(true);

        var materials = new System.Collections.Generic.List<Material>();
        var bases = new System.Collections.Generic.List<float>();

        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null) continue;

            // materials(복수형)는 인스턴스를 만들어 돌려준다. 프리팹당 1회만 부르면 되도록 캐시한다.
            Material[] instanced = targets[i].materials;
            for (int k = 0; k < instanced.Length; k++)
            {
                if (instanced[k] == null || !instanced[k].HasProperty(ColorFactorId)) continue;

                materials.Add(instanced[k]);
                bases.Add(instanced[k].GetFloat(ColorFactorId));
            }
        }

        _materials = materials.ToArray();
        _baseColorFactor = bases.ToArray();

        if (_materials.Length == 0)
        {
            Edit.LogWarning($"[HolyShieldEffect] '{name}' 아래에 _ColorFactor 를 가진 머티리얼이 없다. " +
                            "배리어가 등장·소멸하지 않는다 — 셰이더가 Shader_IntegratedEffect 인지 확인할 것", this);
        }
    }
}
