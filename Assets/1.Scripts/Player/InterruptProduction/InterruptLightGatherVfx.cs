using UnityEngine;

/// <summary>
/// 빛 모임 VFX 루트(할 일 132 단계 4) — <c>Assets/5.VFX/Interrupt/LightGather/FX_InterruptLightGather.prefab</c>.
/// 흰 빛 기둥(루트 파티클) + 사방에서 모여드는 입자 + 중심 빛(자식 파티클) + 작은 Point Light(§16). 재질은 절차 생성 셰이더
/// (<c>BeaverLobby/VFX/InterruptLightGather</c>) — 재질의 '덮어쓸 텍스처' 칸을 채우면 그 텍스처를 쓴다.
///
/// <list type="bullet">
/// <item>재생 = 붙일 곳 위치로 옮겨 처음부터 튼다. 정지 = 방출만 멈춘다(남은 입자는 자기 수명대로 사라진다).</item>
/// <item>매 프레임(LateUpdate) 붙일 곳 위치만 따라가고 회전은 월드 기준이다 — 소켓 본이 돌아도 기둥은 위로 선다.
///       입자는 로컬 시뮬레이션이라 움직이는 소켓 쪽으로 모인다.</item>
/// <item>시간 기준 칸(<see cref="timeBase"/>)은 재생할 때마다 모든 파티클에 넣는다(Play 중 바꾸면 다음 재생부터).</item>
/// <item>라이트(§16): 재생(차지 시작)부터 <see cref="lightRiseDuration"/>(같은 시간 기준) 동안 곡선대로 세지고 그 값을 유지, 정지(= 발동자 Hit)에서 바로 끈다.
///       첫 재생 때 자식으로 한 번 만들어 재사용(재생마다 생성 없음), 그림자 기본 끔.</item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
public sealed class InterruptLightGatherVfx : MonoBehaviour
{
    [Tooltip("빛 모임 재생 시간 기준 — 스케일(기본): 슬로우를 따라 느려진다 / 실시간: 강 슬로우 0.15·히트스톱 중에도 같은 빠르기")]
    [SerializeField] private InterruptEffectTimeBase timeBase = InterruptEffectTimeBase.Scaled;

    [Tooltip("재생할 루트 파티클(자식 포함). 비우면 이 오브젝트의 ParticleSystem")]
    [SerializeField] private ParticleSystem root;

    [Header("소켓 라이트(§16)")]
    [Tooltip("소켓 Point Light 색")]
    [SerializeField] private Color lightColor = new Color(0.85f, 0.92f, 1f, 1f);
    [Tooltip("라이트 최대 세기(차지 끝). 0 = 라이트 없음")]
    [SerializeField, Min(0f)] private float lightIntensity = 3f;
    [Tooltip("라이트 반경(m)")]
    [SerializeField, Min(0.01f)] private float lightRange = 2.5f;
    [Tooltip("라이트 그림자 — 기본 끔")]
    [SerializeField] private LightShadows lightShadows = LightShadows.None;
    [Tooltip("최대 세기까지 오르는 시간(초, 위 시간 기준). 그 뒤 Hit 까지 유지")]
    [SerializeField, Min(0f)] private float lightRiseDuration = 0.5f;
    [Tooltip("라이트 상승 곡선 — 가로 = 진행(0~1), 세로 = 최대 세기 대비 배율")]
    [SerializeField] private AnimationCurve lightRiseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private ParticleSystem[] _systems;
    private Transform _anchor;
    private Light _light;
    private float _lightElapsed = -1f;

    public InterruptEffectTimeBase TimeBase => timeBase;
    public float LightIntensityPeak => lightIntensity;
    public float LightRiseDuration => lightRiseDuration;
    public AnimationCurve LightRiseCurve => lightRiseCurve;
    /// <summary>소켓 라이트(첫 재생 전 null) — 확인용.</summary>
    public Light GatherLight => _light;

    /// <summary>방출 중(정지 전).</summary>
    public bool IsEmitting => root != null && root.isEmitting;

    public void Play(Transform anchor)
    {
        if (root == null && !TryGetComponent(out root))
            return;

        _systems ??= GetComponentsInChildren<ParticleSystem>(true);
        bool unscaled = InterruptEffectTime.UsesUnscaledTime(timeBase);
        for (int i = 0; i < _systems.Length; i++)
        {
            ParticleSystem.MainModule main = _systems[i].main;
            main.useUnscaledTime = unscaled;
        }

        _anchor = anchor;
        Follow();
        root.Clear(true);
        root.Play(true);

        // 라이트 — 첫 재생 때 한 번 만들고 재사용. 칸 값은 재생마다 넣는다. 0 에서 시작해 차지 동안 오른다.
        _light = InterruptEffectLight.Ensure(_light, transform, "GatherLight");
        InterruptEffectLight.Apply(_light, lightColor, lightRange, lightShadows);
        _lightElapsed = 0f;
        InterruptEffectLight.SetIntensity(_light, 0f);
    }

    /// <summary>정지(발동자 Hit·실패·리셋) — 방출을 멈추고 라이트를 바로 끈다.</summary>
    public void Stop()
    {
        if (root != null)
            root.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        _lightElapsed = -1f;
        InterruptEffectLight.SetIntensity(_light, 0f);
    }

    private void LateUpdate()
    {
        Follow();
        TickLight(InterruptEffectTime.UsesUnscaledTime(timeBase) ? Time.unscaledDeltaTime : Time.deltaTime);
    }

    /// <summary>라이트 한 프레임(시간 기준 칸의 dt). 정지 뒤엔 아무것도 안 한다. 테스트는 직접 부른다.</summary>
    public void TickLight(float delta)
    {
        if (_lightElapsed < 0f)
            return;

        _lightElapsed += Mathf.Max(0f, delta);
        InterruptEffectLight.SetIntensity(_light,
            InterruptEffectLight.Rise(lightRiseCurve, lightIntensity, _lightElapsed, lightRiseDuration));
    }

    private void Follow()
    {
        if (_anchor != null)
            transform.SetPositionAndRotation(_anchor.position, Quaternion.identity);
    }
}
