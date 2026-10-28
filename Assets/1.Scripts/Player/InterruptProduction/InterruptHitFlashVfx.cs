using UnityEngine;

/// <summary>
/// 월드 섬광 실시간 상한 시계(할 일 132 §16) — 순수 로직. 스케일 시간 파티클이 슬로우(0.25)·히트스톱 중에도 실시간 <see cref="RealCap"/> 안에 끝나도록
/// 프레임마다 파티클 시뮬레이션 배속을 정한다(남은 효과 길이 ÷ 남은 실시간 비율만큼, 최소 1배 — 평소 빠르기보다 느려지지 않는다).
/// 슬로우가 약하면(배율 1 에 가까우면) 배속 1 그대로 = 원래 길이. 상한 0 = 상한 없음.
/// </summary>
public struct InterruptHitFlashVfxClock
{
    /// <summary>효과 길이(파티클 시계 초 — 가장 긴 입자의 지연 + 수명).</summary>
    public float EffectLength { get; private set; }
    /// <summary>실시간 상한(초). 0 이하 = 상한 없음.</summary>
    public float RealCap { get; private set; }
    public float EffectElapsed { get; private set; }
    public float RealElapsed { get; private set; }
    public bool Running { get; private set; }

    public void Begin(float effectLength, float realCap)
    {
        EffectLength = Mathf.Max(0f, effectLength);
        RealCap = realCap;
        EffectElapsed = 0f;
        RealElapsed = 0f;
        Running = true;
    }

    /// <summary>
    /// 한 프레임 — <paramref name="effectDelta"/> = 이번 프레임 파티클 시계 dt(스케일 = deltaTime, 실시간 = unscaledDeltaTime), <paramref name="realDelta"/> = unscaledDeltaTime.
    /// 이번 프레임 파티클 배속(≥ 1)을 돌려준다. 끝났으면 1.
    /// </summary>
    public float Advance(float effectDelta, float realDelta)
    {
        if (!Running)
            return 1f;

        float speed = 1f;
        float remainingEffect = EffectLength - EffectElapsed;
        if (RealCap > 0f && effectDelta > 0f && remainingEffect > 0f)
        {
            // 남은 실시간에 남은 효과를 고르게 나눈다 — 이번 프레임 몫 = 남은 효과 × (이번 프레임 실시간 ÷ 남은 실시간).
            float remainingReal = RealCap - RealElapsed;
            float needed = remainingReal > realDelta ? remainingEffect * realDelta / remainingReal : remainingEffect;
            speed = Mathf.Max(1f, needed / effectDelta);
        }

        EffectElapsed += effectDelta * speed;
        RealElapsed += Mathf.Max(0f, realDelta);
        if (EffectElapsed >= EffectLength || (RealCap > 0f && RealElapsed >= RealCap))
            Running = false;
        return speed;
    }

    /// <summary>실시간 상한에 걸려 끝났다(효과가 덜 끝났다 — 시간이 멈춘 경우 등). 출력이 남은 입자를 지운다.</summary>
    public bool CutByCap => !Running && RealCap > 0f && RealElapsed >= RealCap && EffectElapsed < EffectLength;

    public void Stop() => Running = false;
}

/// <summary>
/// 적중 섬광 월드 이펙트 루트(할 일 132 단계 5 — §15 섬광 순간 = ⑤ 시작, §16 부드러운 빛 덩어리) — <c>Assets/5.VFX/Interrupt/HitFlash/FX_InterruptHitFlash.prefab</c>.
/// 적중 지점에서 파랑·흰 섬광: 흰 중심 빛 덩어리 + 파란 번짐 + 튀는 불티(전부 가우시안 빛 덩어리 — 바퀴살 광선·고리는 없앴다) + 적중 지점 Point Light. 색은 HDR(블룸).
/// 재질은 절차 생성 셰이더(<c>BeaverLobby/VFX/InterruptHitFlash</c>) — 재질의 '덮어쓸 텍스처' 칸을 채우면 그 텍스처를 쓴다.
///
/// <list type="bullet">
/// <item>모든 피어가 각자 섬광 순간(성공 Started + 섬광 ②③④ 길이)에 튼다 — 디렉터가 한 번 만들어 재사용한다(매 재생 생성 없음).</item>
/// <item>시간 기준 칸(<see cref="timeBase"/>, 기본 스케일 — 성공 슬로우 0.25 를 따라 느리게 터진다)은 재생할 때마다 모든 파티클에 넣는다.
///       단 실시간 상한(<see cref="realTimeCap"/>, 기본 0.3초)을 넘지 않게 프레임마다 시뮬레이션 배속을 올린다(<see cref="InterruptHitFlashVfxClock"/>) — 슬로우 중 1초씩 남지 않는다.
///       셰이더는 시간을 쓰지 않는다(파티클 나이·색 수명 곡선).</item>
/// <item>라이트(§16): 섬광 순간 번쩍 켜져 <see cref="lightDuration"/>(실시간) 동안 곡선대로 꺼진다. 첫 재생 때 자식으로 한 번 만들어 재사용, 쉬는 동안 꺼짐. 그림자 기본 끔.</item>
/// <item>크기 = 루트 스케일(파티클 Scaling Mode = Hierarchy). 라이트 반경은 칸 값 × 루트 스케일.</item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
public sealed class InterruptHitFlashVfx : MonoBehaviour
{
    [Tooltip("월드 섬광 재생 시간 기준 — 스케일(기본): 성공 슬로우를 따라 느려진다 / 실시간: 슬로우 중에도 같은 빠르기")]
    [SerializeField] private InterruptEffectTimeBase timeBase = InterruptEffectTimeBase.Scaled;

    [Tooltip("실시간 상한(초) — 스케일 시간이어도 이 시간 안에 끝나게 파티클을 빨리 돌린다(슬로우 중 1초씩 남지 않게). 0 = 상한 없음")]
    [SerializeField, Min(0f)] private float realTimeCap = 0.3f;

    [Tooltip("재생할 루트 파티클(자식 포함). 비우면 이 오브젝트의 ParticleSystem")]
    [SerializeField] private ParticleSystem root;

    [Header("적중 지점 라이트(§16)")]
    [Tooltip("적중 지점 Point Light 색 — 파랑·흰")]
    [SerializeField] private Color lightColor = new Color(0.62f, 0.8f, 1f, 1f);
    [Tooltip("라이트 최대 세기(곡선 1 일 때). 0 = 라이트 없음")]
    [SerializeField, Min(0f)] private float lightIntensity = 12f;
    [Tooltip("라이트 반경(m, 루트 스케일을 곱한다)")]
    [SerializeField, Min(0.01f)] private float lightRange = 7f;
    [Tooltip("라이트 그림자 — 기본 끔(추가 라이트 그림자 비용·Mobile 은 추가 라이트 그림자 미지원)")]
    [SerializeField] private LightShadows lightShadows = LightShadows.None;
    [Tooltip("라이트가 켜져 있는 시간(초, 실시간) — 이 동안 곡선대로 꺼진다")]
    [SerializeField, Min(0f)] private float lightDuration = 0.25f;
    [Tooltip("라이트 세기 곡선 — 가로 = 진행(0~1), 세로 = 최대 세기 대비 배율. 끝에서 0 이 되게")]
    [SerializeField] private AnimationCurve lightCurve = new AnimationCurve(
        new Keyframe(0f, 1f, 0f, -2.5f), new Keyframe(0.35f, 0.35f, -1.2f, -1.2f), new Keyframe(1f, 0f, -0.2f, 0f));

    private ParticleSystem[] _systems;
    private InterruptHitFlashVfxClock _clock;
    private Light _light;
    private float _lightElapsed = -1f;
    private float _lightPeak;

    public InterruptEffectTimeBase TimeBase => timeBase;
    public float RealTimeCap => realTimeCap;
    public float LightDuration => lightDuration;
    /// <summary>적중 지점 라이트(첫 재생 전 null) — 확인용.</summary>
    public Light HitLight => _light;
    /// <summary>파티클 실시간 상한 시계 — 확인용.</summary>
    public InterruptHitFlashVfxClock Clock => _clock;

    /// <summary>
    /// 섬광 순간 재생. <paramref name="particles"/>·<paramref name="light"/> = 디렉터 효과 칸(월드 섬광·적중 지점 라이트, 할 일 132 §17) —
    /// 끈 쪽은 그리지 않는다(돌던 것도 멈춘다).
    /// </summary>
    public void Play(Vector3 position, float scale, bool particles = true, bool light = true)
    {
        float rootScale = Mathf.Max(0.01f, scale);
        transform.SetPositionAndRotation(position, Quaternion.identity);
        transform.localScale = Vector3.one * rootScale;

        if (particles)
            PlayParticles();
        else
            StopParticles();

        if (light)
            PlayLight(rootScale);
        else
            StopLight();
    }

    private void PlayParticles()
    {
        if (root == null && !TryGetComponent(out root))
            return;

        _systems ??= GetComponentsInChildren<ParticleSystem>(true);
        bool unscaled = InterruptEffectTime.UsesUnscaledTime(timeBase);
        for (int i = 0; i < _systems.Length; i++)
        {
            ParticleSystem.MainModule main = _systems[i].main;
            main.useUnscaledTime = unscaled;
            main.simulationSpeed = 1f;
        }

        root.Clear(true);
        root.Play(true);
        _clock.Begin(EffectLength(_systems), realTimeCap);
    }

    private void StopParticles()
    {
        if (root != null)
            root.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        _clock.Stop();
    }

    private void PlayLight(float rootScale)
    {
        // 라이트 — 첫 재생 때 한 번 만들고 재사용. 칸 값은 재생마다 넣는다.
        _light = InterruptEffectLight.Ensure(_light, transform, "HitLight");
        // 라이트 반경은 부모 스케일을 따르지 않는다 — 루트 스케일을 직접 곱한다.
        InterruptEffectLight.Apply(_light, lightColor, lightRange * rootScale, lightShadows);
        _lightPeak = lightIntensity;
        _lightElapsed = 0f;
        InterruptEffectLight.SetIntensity(_light, InterruptEffectLight.Pulse(lightCurve, _lightPeak, 0f, lightDuration));
    }

    /// <summary>강제 리셋 — 남은 입자까지 지우고 라이트를 끈다.</summary>
    public void Stop()
    {
        StopParticles();
        StopLight();
    }

    private void StopLight()
    {
        _lightElapsed = -1f;
        InterruptEffectLight.SetIntensity(_light, 0f);
    }

    private void Update() => Tick(Time.unscaledDeltaTime, Time.deltaTime);

    /// <summary>
    /// 한 프레임(Update — 파티클 갱신 전). 파티클 배속(실시간 상한)·라이트 세기(실시간 곡선). 테스트는 직접 부른다.
    /// <paramref name="scaledDelta"/> = Time.deltaTime(스케일 시간 기준일 때 파티클 시계).
    /// </summary>
    public void Tick(float unscaledDelta, float scaledDelta)
    {
        if (_clock.Running)
        {
            float effectDelta = InterruptEffectTime.UsesUnscaledTime(timeBase) ? unscaledDelta : scaledDelta;
            float speed = _clock.Advance(effectDelta, unscaledDelta);
            SetSpeed(_clock.Running ? speed : 1f);
            if (_clock.CutByCap && root != null)
                root.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        if (_lightElapsed >= 0f)
        {
            _lightElapsed += unscaledDelta;
            float intensity = InterruptEffectLight.Pulse(lightCurve, _lightPeak, _lightElapsed, lightDuration);
            InterruptEffectLight.SetIntensity(_light, intensity);
            if (intensity <= 0f)
                _lightElapsed = -1f;
        }
    }

    private void SetSpeed(float speed)
    {
        if (_systems == null)
            return;

        for (int i = 0; i < _systems.Length; i++)
        {
            ParticleSystem.MainModule main = _systems[i].main;
            main.simulationSpeed = speed;
        }
    }

    // 효과 길이 = 가장 긴 입자의 지연 + 수명(버스트 원샷 — 프리팹 파티클은 전부 시간 0 버스트).
    private static float EffectLength(ParticleSystem[] systems)
    {
        float length = 0f;
        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem.MainModule main = systems[i].main;
            length = Mathf.Max(length, main.startDelay.constantMax + main.startLifetime.constantMax);
        }

        return length;
    }
}
