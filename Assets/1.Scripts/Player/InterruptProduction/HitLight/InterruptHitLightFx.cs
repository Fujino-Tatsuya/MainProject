using UnityEngine;

/// <summary>
/// 적중 라이트 연출 월드 이펙트 루트(V1·V2, 할 일 194) — <c>Assets/5.VFX/Interrupt/HitLight/FX_InterruptHitLight.prefab</c>.
/// 적중 지점 포인트 라이트(따뜻한 흰주황, 실시간 곡선) + 자식 파티클(불꽃·메시 파편·빛줄기). 모든 피어가 성공 순간 각자 튼다.
/// <list type="bullet">
/// <item>디렉터가 첫 재생 때 한 번 만들어 재사용한다(재생마다 생성 없음). 라이트도 첫 재생 때 자식으로 한 번 만든다.</item>
/// <item>전부 실시간(Q8) — 재생할 때마다 모든 파티클의 useUnscaledTime 을 켠다(히트스톱 중에도 움직인다).</item>
/// <item>수치는 <see cref="InterruptHitLightLook"/> — 재생마다 넣는다.</item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
public sealed class InterruptHitLightFx : MonoBehaviour
{
    private ParticleSystem[] _systems;
    private Light _light;
    private InterruptHitLightLook _look;
    private float _lightElapsed = -1f;

    /// <summary>적중 지점 라이트(첫 재생 전 null) — 확인용.</summary>
    public Light HitLight => _light;
    /// <summary>라이트가 아직 꺼지는 중이다.</summary>
    public bool LightActive => _lightElapsed >= 0f;

    /// <summary>성공 순간 재생 — <paramref name="position"/> = 섬광 중심(대상 위치 + 높이), <paramref name="scale"/> = 루트 스케일.</summary>
    public void Play(Vector3 position, InterruptHitLightLook look, float scale = 1f)
    {
        float rootScale = Mathf.Max(0.01f, scale);
        transform.SetPositionAndRotation(position, Quaternion.identity);
        transform.localScale = Vector3.one * rootScale;
        _look = look;

        PlayParticles();

        _light = InterruptEffectLight.Ensure(_light, transform, "HitLight");
        if (look == null)
        {
            StopLight();
            return;
        }

        // 라이트 반경은 부모 스케일을 따르지 않는다 — 루트 스케일을 직접 곱한다.
        InterruptEffectLight.Apply(_light, look.LightColor, look.LightRange * rootScale, look.LightShadows);
        _lightElapsed = 0f;
        InterruptEffectLight.SetIntensity(_light, LightIntensityAt(look, 0f));
    }

    /// <summary>강제 리셋 — 남은 입자까지 지우고 라이트를 끈다.</summary>
    public void Stop()
    {
        if (_systems != null)
        {
            for (int i = 0; i < _systems.Length; i++)
                if (_systems[i] != null)
                    _systems[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        StopLight();
    }

    private void Update() => Tick(Time.unscaledDeltaTime);

    /// <summary>한 프레임(실시간) — 라이트 세기 곡선. 테스트는 직접 부른다.</summary>
    public void Tick(float unscaledDelta)
    {
        if (_lightElapsed < 0f)
            return;

        _lightElapsed += Mathf.Max(0f, unscaledDelta);
        float intensity = LightIntensityAt(_look, _lightElapsed);
        InterruptEffectLight.SetIntensity(_light, intensity);
        if (intensity <= 0f)
            _lightElapsed = -1f;
    }

    /// <summary>재생 후 <paramref name="elapsed"/> 초의 라이트 세기(실시간 곡선). 끝·데이터 없음 = 0.</summary>
    public static float LightIntensityAt(InterruptHitLightLook look, float elapsed) =>
        look != null ? InterruptEffectLight.Pulse(look.LightCurve, look.LightIntensity, elapsed, look.LightDuration) : 0f;

    private void PlayParticles()
    {
        _systems ??= GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < _systems.Length; i++)
        {
            ParticleSystem.MainModule main = _systems[i].main;
            main.useUnscaledTime = true;
            _systems[i].Clear(false);
            _systems[i].Play(false);
        }
    }

    private void StopLight()
    {
        _lightElapsed = -1f;
        InterruptEffectLight.SetIntensity(_light, 0f);
    }
}
