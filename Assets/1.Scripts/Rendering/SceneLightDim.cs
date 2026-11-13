using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>씬 조명 대상(할 일 194) — 실제 = 씬의 Directional Light·RenderSettings 환경광. 테스트는 가짜를 넣는다.</summary>
public interface ISceneLightTarget
{
    /// <summary>씬의 태양광(Directional Light). 없으면 null.</summary>
    Light FindSun();
    /// <summary>환경광(구면 조화) — URP 가 주변광으로 쓰는 값.</summary>
    SphericalHarmonicsL2 AmbientProbe { get; set; }
}

/// <summary>
/// 씬 태양광·환경광 배율 공유 창구(할 일 194) — 여러 연출이 각자 배율(0~1)을 넣으면 '원래 값 × 배율 곱' 으로 적용한다.
/// 23호 차징의 태양광 끄기(<see cref="BossChargeTetherView"/>)와 간파 적중 암순응 나(<see cref="InterruptHitLightPlayer"/>)가 함께 쓴다 —
/// 서로 원래 값을 따로 저장하지 않으므로 누가 먼저 끝나도 복구 순서가 꼬이지 않는다(어두운 값을 원래 값으로 잘못 저장하지 않는다).
/// <list type="bullet">
/// <item>원래 값은 배율 곱이 처음 1 아래로 내려갈 때 한 번 저장하고, 모든 배율이 1 로 돌아오면 그 값으로 되돌린다.</item>
/// <item>씬이 바뀌면 저장값·배율을 버린다(새 씬은 자기 값을 쓴다).</item>
/// <item>각 피어 로컬(네트워크 없음).</item>
/// </list>
/// </summary>
public sealed class SceneLightDim
{
    private static SceneLightDim _shared;

    /// <summary>실제 씬용 공유 인스턴스.</summary>
    public static SceneLightDim Shared => _shared ??= CreateShared();

    private readonly ISceneLightTarget _target;
    private readonly Dictionary<object, float> _sunFactors = new Dictionary<object, float>();
    private readonly Dictionary<object, float> _ambientFactors = new Dictionary<object, float>();

    private Light _sun;
    private float _sunBaseIntensity;
    private bool _sunBaseEnabled;
    private bool _sunCaptured;
    private SphericalHarmonicsL2 _ambientBase;
    private bool _ambientCaptured;

    public SceneLightDim(ISceneLightTarget target)
    {
        _target = target;
    }

    /// <summary>지금 태양광 배율 곱(넣은 것이 없으면 1).</summary>
    public float SunFactor => Product(_sunFactors);
    /// <summary>지금 환경광 배율 곱(넣은 것이 없으면 1).</summary>
    public float AmbientFactor => Product(_ambientFactors);

    /// <summary><paramref name="owner"/> 의 태양광 배율을 넣는다. 1 이상이면 뺀다.</summary>
    public void SetSun(object owner, float factor)
    {
        Set(_sunFactors, owner, factor);
        ApplySun();
    }

    /// <summary><paramref name="owner"/> 의 환경광 배율을 넣는다. 1 이상이면 뺀다.</summary>
    public void SetAmbient(object owner, float factor)
    {
        Set(_ambientFactors, owner, factor);
        ApplyAmbient();
    }

    /// <summary><paramref name="owner"/> 의 배율을 모두 뺀다.</summary>
    public void Release(object owner)
    {
        SetSun(owner, 1f);
        SetAmbient(owner, 1f);
    }

    /// <summary>저장값·배율을 버린다(되돌리지 않음) — 씬 전환.</summary>
    public void Forget()
    {
        _sunFactors.Clear();
        _ambientFactors.Clear();
        _sun = null;
        _sunCaptured = false;
        _ambientCaptured = false;
    }

    private void ApplySun()
    {
        float factor = SunFactor;
        if (_sunCaptured && _sun == null)
            _sunCaptured = false; // 저장한 태양이 사라졌다(씬 언로드)

        if (factor >= 1f)
        {
            if (_sunCaptured)
            {
                _sun.intensity = _sunBaseIntensity;
                _sun.enabled = _sunBaseEnabled;
                _sunCaptured = false;
            }

            return;
        }

        if (!_sunCaptured)
        {
            _sun = _target?.FindSun();
            if (_sun == null)
                return;

            _sunBaseIntensity = _sun.intensity;
            _sunBaseEnabled = _sun.enabled;
            _sunCaptured = true;
        }

        _sun.intensity = _sunBaseIntensity * factor;
        _sun.enabled = _sunBaseEnabled && factor > 0f;
    }

    private void ApplyAmbient()
    {
        if (_target == null)
            return;

        float factor = AmbientFactor;
        if (factor >= 1f)
        {
            if (_ambientCaptured)
            {
                _target.AmbientProbe = _ambientBase;
                _ambientCaptured = false;
            }

            return;
        }

        if (!_ambientCaptured)
        {
            _ambientBase = _target.AmbientProbe;
            _ambientCaptured = true;
        }

        _target.AmbientProbe = _ambientBase * factor;
    }

    private static void Set(Dictionary<object, float> factors, object owner, float factor)
    {
        if (owner == null)
            return;

        if (float.IsNaN(factor) || factor >= 1f)
            factors.Remove(owner);
        else
            factors[owner] = Mathf.Max(0f, factor);
    }

    private static float Product(Dictionary<object, float> factors)
    {
        float product = 1f;
        foreach (float value in factors.Values)
            product *= value;
        return product;
    }

    private static SceneLightDim CreateShared()
    {
        var dim = new SceneLightDim(new RenderSettingsTarget());
        SceneManager.activeSceneChanged += (_, _) => dim.Forget();
        return dim;
    }

    // 도메인 리로드를 끈 Play 진입에서도 이전 세션 값이 남지 않게.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetShared() => _shared = null;

    private sealed class RenderSettingsTarget : ISceneLightTarget
    {
        public Light FindSun()
        {
            if (RenderSettings.sun != null && RenderSettings.sun.type == LightType.Directional)
                return RenderSettings.sun;
            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (light.type == LightType.Directional && light.isActiveAndEnabled)
                    return light;
            return null;
        }

        public SphericalHarmonicsL2 AmbientProbe
        {
            get => RenderSettings.ambientProbe;
            set => RenderSettings.ambientProbe = value;
        }
    }
}
