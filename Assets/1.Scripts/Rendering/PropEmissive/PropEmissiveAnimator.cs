using UnityEngine;

/// <summary>
/// 프랍 이미시브·라이트 은은한 애니메이션(할 일 277 후속) — 표시 전용, 각 피어 로컬(네트워크 없음).
/// <list type="bullet">
/// <item>대상 렌더러 이미시브 색 = 공유 머티리얼 원래 색 × 세기 곡선. <see cref="MaterialPropertyBlock"/> 으로만 바꾼다(머티리얼 인스턴스·공유 머티리얼 수정 없음).</item>
/// <item>같은 프리팹의 Light 세기도 같은 곡선으로 곱한다(<see cref="lightsFollow"/>).</item>
/// <item>인스턴스마다 시작 위상·노이즈 시드를 랜덤으로 둬 같은 프랍 여러 개가 동시에 깜빡이지 않는다.</item>
/// <item>렌더러가 화면 밖이면 이미시브 갱신을 건너뛴다(라이트는 바닥·벽을 비추므로 계속).</item>
/// </list>
/// 곡선은 순수 함수 <see cref="Evaluate"/> — EditMode 테스트 대상.
/// </summary>
[DisallowMultipleComponent]
public sealed class PropEmissiveAnimator : MonoBehaviour
{
    public enum Wave
    {
        /// <summary>최대값 고정(노이즈·딥만).</summary>
        Constant,
        /// <summary>사인 숨쉬기 min↔max.</summary>
        Breathe,
        /// <summary>빠르게 켜지고 천천히 꺼지는 펄스(경광등).</summary>
        Pulse,
    }

    [System.Serializable]
    public struct Settings
    {
        [Tooltip("기본 파형")] public Wave wave;
        [Tooltip("Breathe·Pulse 주기(초)")] [Min(0.05f)] public float period;
        [Tooltip("파형 최저 배율")] [Range(0f, 1f)] public float min;
        [Tooltip("파형 최고 배율")] [Range(0f, 2f)] public float max;
        [Tooltip("Pulse: 켜지는 구간 비율(0~1, 나머지는 꺼지는 구간)")] [Range(0.01f, 0.9f)] public float pulseAttack;
        [Tooltip("노이즈 세기(±비율, 예 0.06 = ±6%)")] [Range(0f, 0.5f)] public float noiseAmount;
        [Tooltip("노이즈 빠르기(Hz)")] [Min(0f)] public float noiseFrequency;
        [Tooltip("딥(지직·깜빡) 평균 간격(초). 0 = 끔")] [Min(0f)] public float dipInterval;
        [Tooltip("딥 길이(초)")] [Min(0f)] public float dipDuration;
        [Tooltip("딥 중 배율(0 = 완전히 꺼짐)")] [Range(0f, 1f)] public float dipDepth;
    }

    [SerializeField] private Settings settings = new Settings
    {
        wave = Wave.Constant, period = 1f, min = 1f, max = 1f, pulseAttack = 0.15f,
        noiseAmount = 0f, noiseFrequency = 10f, dipInterval = 0f, dipDuration = 0.1f, dipDepth = 0.4f,
    };
    [Tooltip("같은 프리팹 안 Light 세기도 같은 곡선으로 곱한다")]
    [SerializeField] private bool lightsFollow = true;
    [Tooltip("시간 기준을 timeScale 무시로(슬로우·히트스톱 중에도 같은 속도)")]
    [SerializeField] private bool useUnscaledTime;

    private Renderer[] _renderers;
    private int[] _propIds;
    private Color[] _baseColors;
    private Light[] _lights;
    private float[] _baseIntensities;
    private MaterialPropertyBlock _mpb;
    private float _timeOffset;
    private int _seed;
    private bool _initialized;

    public Settings CurrentSettings { get => settings; set => settings = value; }

    private void Awake() => Init();

    private void OnEnable()
    {
        Init();
        _timeOffset = Random.Range(0f, 1000f);
        _seed = Random.Range(1, int.MaxValue);
    }

    private void OnDisable() => Restore();

    private void Update() => ApplyAt((useUnscaledTime ? Time.unscaledTime : Time.time) + _timeOffset, false);

    /// <summary>시각 <paramref name="t"/> 의 곡선을 적용한다. <paramref name="force"/> = 화면 밖이어도 이미시브 갱신(오프라인 렌더용).</summary>
    public void ApplyAt(float t, bool force)
    {
        Init();
        float f = Evaluate(settings, t, _seed);
        for (int i = 0; i < _renderers.Length; i++)
        {
            var r = _renderers[i];
            if (!r || _propIds[i] == 0 || (!force && !r.isVisible)) continue;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(_propIds[i], _baseColors[i] * f);
            r.SetPropertyBlock(_mpb);
        }
        if (!lightsFollow) return;
        for (int i = 0; i < _lights.Length; i++)
            if (_lights[i]) _lights[i].intensity = _baseIntensities[i] * f;
    }

    private void Init()
    {
        if (_initialized) return;
        _initialized = true;
        _mpb = new MaterialPropertyBlock();
        _renderers = GetComponentsInChildren<Renderer>(true);
        _propIds = new int[_renderers.Length];
        _baseColors = new Color[_renderers.Length];
        for (int i = 0; i < _renderers.Length; i++)
        {
            var m = _renderers[i].sharedMaterial; // 읽기만 — 인스턴스 생성 없음
            if (!m) continue;
            string prop = m.HasProperty("_Emission_Color") ? "_Emission_Color" : m.HasProperty("_EmissionColor") ? "_EmissionColor" : null;
            if (prop == null) continue;
            _propIds[i] = Shader.PropertyToID(prop);
            _baseColors[i] = m.GetColor(_propIds[i]);
        }
        _lights = GetComponentsInChildren<Light>(true);
        _baseIntensities = new float[_lights.Length];
        for (int i = 0; i < _lights.Length; i++) _baseIntensities[i] = _lights[i].intensity;
    }

    private void Restore()
    {
        if (!_initialized) return;
        foreach (var r in _renderers) if (r) r.SetPropertyBlock(null);
        for (int i = 0; i < _lights.Length; i++) if (_lights[i]) _lights[i].intensity = _baseIntensities[i];
    }

    // ---------------- 순수 곡선

    /// <summary>시각 t(초)·시드의 세기 배율. 파형 × (1 ± 노이즈) × 딥. 같은 입력이면 같은 값.</summary>
    public static float Evaluate(in Settings s, float t, int seed)
    {
        float v = WaveValue(s, t);
        if (s.noiseAmount > 0f && s.noiseFrequency > 0f)
            v *= 1f + s.noiseAmount * (ValueNoise(t * s.noiseFrequency, seed) * 2f - 1f);
        v *= DipFactor(s, t, seed);
        return Mathf.Max(0f, v);
    }

    public static float WaveValue(in Settings s, float t)
    {
        float p = Frac(t / Mathf.Max(0.05f, s.period));
        float x;
        switch (s.wave)
        {
            case Wave.Breathe: x = 0.5f - 0.5f * Mathf.Cos(p * 2f * Mathf.PI); break;
            case Wave.Pulse:
                float a = Mathf.Clamp(s.pulseAttack, 0.01f, 0.9f);
                if (p < a) x = Mathf.SmoothStep(0f, 1f, p / a);
                else { float d = 1f - (p - a) / (1f - a); x = d * d; }
                break;
            default: x = 1f; break;
        }
        return Mathf.Lerp(s.min, s.max, x);
    }

    /// <summary>딥 배율 — 평균 간격마다 한 칸, 칸 안 무작위 위치에 길이만큼. 딥 중엔 잘게 끊기는(지직) 꺼짐.</summary>
    public static float DipFactor(in Settings s, float t, int seed)
    {
        if (s.dipInterval <= 0f || s.dipDuration <= 0f) return 1f;
        float slotLen = Mathf.Max(s.dipInterval, s.dipDuration * 2f);
        int k = Mathf.FloorToInt(t / slotLen);
        float start = k * slotLen + Hash01(k, seed) * (slotLen - s.dipDuration);
        if (t < start || t >= start + s.dipDuration) return 1f;
        // 25Hz 칸으로 끊어 켜짐/꺼짐 섞기 — 첫 칸은 늘 꺼짐(짧은 딥도 보이게)
        int step = Mathf.FloorToInt((t - start) * 25f);
        bool off = step == 0 || Hash01(k * 131 + step, seed ^ 0x5bd1e995) < 0.65f;
        return off ? s.dipDepth : 1f;
    }

    /// <summary>1D 값 노이즈 0~1(정수 격자 해시 + smoothstep 보간).</summary>
    public static float ValueNoise(float x, int seed)
    {
        int i = Mathf.FloorToInt(x);
        float f = x - i;
        return Mathf.Lerp(Hash01(i, seed), Hash01(i + 1, seed), f * f * (3f - 2f * f));
    }

    public static float Hash01(int n, int seed)
    {
        unchecked
        {
            uint h = (uint)n * 0x9E3779B1u ^ (uint)seed * 0x85EBCA77u;
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }

    private static float Frac(float x) => x - Mathf.Floor(x);
}
