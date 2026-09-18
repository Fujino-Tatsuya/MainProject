using System.Collections.Generic;
using UnityEngine;

public enum ShieldDismissMode
{
    /// <summary>자연 소멸. 배리어가 투명해지면서 흐려진다.</summary>
    Fade,

    /// <summary>파괴. 조각이 터져 흩어지고 섬광이 한 번 번쩍인다.</summary>
    Break,
}

/// <summary>
/// 방어막이 사라지는 순간을 밖에서 정할 수 있게 한다.
///
/// <b>왜 필요한가</b>: 원래 이 방어막은 <see cref="NewMaterialChange"/>가 m_timeToReduce
/// 초 뒤에 스스로 Destroy 해 버려서, 언제 걷힐지를 게임 로직이 고를 수 없었다.
/// 이 컴포넌트는 그 타이머를 무력화하고 소멸을 <see cref="Dismiss()"/> 호출로 옮긴다.
/// </summary>
public class ShieldDismiss : MonoBehaviour
{
    [Header("테스트")]
    [Tooltip("플레이 중에 체크하면 testMode 로 즉시 소멸한다. 체크는 자동으로 풀린다.")]
    [SerializeField] bool dismissNow;
    [SerializeField] ShieldDismissMode testMode = ShieldDismissMode.Break;
    [Tooltip("읽기 전용. 지금 소멸 연출이 도는 중인지 보여 준다.")]
    [SerializeField] bool isDismissing;

    [Header("공통")]
    [Tooltip("0보다 크면 이 시간 뒤 알아서 사라진다. 0이면 호출할 때까지 유지된다.")]
    [SerializeField] float autoDismissAfter;
    [SerializeField] ShieldDismissMode autoDismissMode = ShieldDismissMode.Fade;
    [Tooltip("연출이 끝나면 이 오브젝트를 파괴한다. 끄면 투명해진 채로 남는다.")]
    [SerializeField] bool destroyWhenDone = true;

    [Header("자연 소멸")]
    [Tooltip("투명해지는 데 걸리는 시간.")]
    [SerializeField] float fadeDuration = 1.2f;
    [Tooltip("시간에 따른 남은 밝기. 왼쪽이 시작(1), 오른쪽이 끝(0).")]
    [SerializeField] AnimationCurve fadeCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Header("파괴")]
    [Tooltip("배리어가 걷히는 시간. 조각이 터져 나가는 순간이라 짧아야 한다.")]
    [SerializeField] float breakDuration = 0.18f;
    [Tooltip("깨지는 연출. 조각 파티클과 섬광이 한 프리팹에 같이 들어 있다.")]
    [SerializeField] GameObject breakEffect;
    [Tooltip("생성한 연출을 자동 삭제하기까지의 시간. 0 이하면 삭제하지 않는다.")]
    [SerializeField] float effectLifetime = 3f;

    /// <summary>소멸 연출이 이미 시작됐는지.</summary>
    public bool IsDismissing { get { return isDismissing; } }

    //rgb 와 alpha 양쪽에 곱해지는 값이라(Shader_IntegratedEffect 의 res.rgb 와 alpha)
    //이 하나만 줄이면 "투명해지면서 흐려진다"가 그대로 나온다. _MaskCutOut 은
    //IS_MASK_FADE 키워드가 없는 Effect_09_Shield_2 머티리얼에서 아무 일도 하지 않아서
    //두 겹을 같이 걷을 수 없다.
    static readonly int ColorFactorId = Shader.PropertyToID("_ColorFactor");

    readonly List<Material> targets = new List<Material>();
    readonly List<float> baseColorFactor = new List<float>();
    ParticleSystem[] particles;

    float elapsed;
    float duration;
    ShieldDismissMode mode;

    void Awake()
    {
        //NewMaterialChange 는 m_timeToReduce 초 뒤 스스로 Destroy 한다. 컴포넌트째
        //꺼 버리면 등장 페이드인(m_upFactor)까지 같이 죽으므로 타이머만 밀어 둔다.
        NewMaterialChange[] changers = GetComponentsInChildren<NewMaterialChange>(true);
        for (int i = 0; i < changers.Length; i++)
            changers[i].m_timeToReduce = float.PositiveInfinity;
    }

    void Start()
    {
        //NewMaterialChange.Awake 가 renderer.material 에 인스턴스를 만들어 두므로 Start
        //에서 읽어야 같은 인스턴스를 잡는다. sharedMaterial 을 쓰면 프로젝트의 머티리얼
        //에셋이 영구히 바뀐다.
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Material[] mats = renderers[i].materials;
            for (int k = 0; k < mats.Length; k++)
            {
                if (mats[k] == null || mats[k].HasProperty(ColorFactorId) == false)
                    continue;

                targets.Add(mats[k]);
                baseColorFactor.Add(mats[k].GetFloat(ColorFactorId));
            }
        }

        particles = GetComponentsInChildren<ParticleSystem>(true);
    }

    void Update()
    {
        if (dismissNow)
        {
            dismissNow = false;
            Dismiss(testMode);
        }

        elapsed += Time.deltaTime;

        if (isDismissing == false)
        {
            if (autoDismissAfter > 0f && elapsed >= autoDismissAfter)
                Dismiss(autoDismissMode);
            return;
        }

        float t = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;

        //파괴는 커브를 타지 않는다. 조각이 이미 튀어나간 뒤에 배리어가 남아 있으면
        //깨진 것으로 안 보이기 때문에 곧장 걷어낸다.
        float remaining = mode == ShieldDismissMode.Fade
            ? Mathf.Clamp01(EvaluateFade(t))
            : 1f - t;

        SetColorFactor(remaining);

        if (t < 1f)
            return;

        SetColorFactor(0f);
        isDismissing = false;
        enabled = false;

        if (destroyWhenDone)
            Destroy(gameObject);
    }

    /// <summary>autoDismissMode 로 소멸시킨다. UnityEvent 에 그대로 걸 수 있다.</summary>
    public void Dismiss()
    {
        Dismiss(autoDismissMode);
    }

    /// <summary>자연 소멸시킨다.</summary>
    public void DismissFade()
    {
        Dismiss(ShieldDismissMode.Fade);
    }

    /// <summary>깨뜨린다.</summary>
    public void DismissBreak()
    {
        Dismiss(ShieldDismissMode.Break);
    }

    /// <summary>소멸을 시작한다. 이미 도는 중이면 무시한다.</summary>
    public void Dismiss(ShieldDismissMode dismissMode)
    {
        if (isDismissing)
            return;

        isDismissing = true;
        mode = dismissMode;
        elapsed = 0f;
        duration = Mathf.Max(0f, dismissMode == ShieldDismissMode.Fade ? fadeDuration : breakDuration);

        //떠 있는 파티클은 제 수명대로 사라지게 두고 새로 내보내는 것만 멈춘다.
        //Clear 로 한 번에 지우면 툭 끊겨 보인다.
        if (particles != null)
            for (int i = 0; i < particles.Length; i++)
                particles[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);

        if (dismissMode != ShieldDismissMode.Break)
            return;

        SpawnEffect(breakEffect);
    }

    /// 연출은 일부러 이 오브젝트 밑에 넣지 않는다. destroyWhenDone 으로 방어막이
    /// 파괴될 때 자식이면 조각이 나오다 말고 같이 사라진다.
    void SpawnEffect(GameObject prefab)
    {
        if (prefab == null)
            return;

        GameObject spawned = Instantiate(prefab, transform.position, transform.rotation);
        if (effectLifetime > 0f)
            Destroy(spawned, effectLifetime);
    }

    //커브가 비어 있으면 Evaluate 가 0을 돌려줘서 페이드가 아니라 한 프레임 만에 꺼진다.
    //인스펙터에서 키를 다 지웠거나 프리팹에 커브가 직렬화되지 않은 경우에 대한 대비.
    float EvaluateFade(float t)
    {
        if (fadeCurve == null || fadeCurve.length < 2)
            return 1f - t;

        return fadeCurve.Evaluate(t);
    }

    void SetColorFactor(float remaining)
    {
        for (int i = 0; i < targets.Count; i++)
            if (targets[i] != null)
                targets[i].SetFloat(ColorFactorId, baseColorFactor[i] * remaining);
    }
}
