using UnityEngine;

/// <summary>
/// 송전기 → 보스 가슴 전기줄(2026-10-22 민경 grill — MainProject PLAN.md 맨 위).
/// 지그재그 번개 줄(굵은 빛 + 가는 심선, 초당 <see cref="regenRate"/> 번 모양을 다시 뽑는다) 위로
/// 송전기 → 보스 방향의 빛 덩어리가 흐른다(공급 방향).
///
/// 흐름: <see cref="Connect"/>(뻗어 나감) → 연결 유지 → 셋 중 하나로 끝난다.
/// <list type="bullet">
/// <item><see cref="Snap"/> — 송전기가 부서짐. 가운데서 끊겨 두 토막이 양끝으로 말려 들어가고, 끊긴 자리 스파크 + 가슴 작은 섬광</item>
/// <item><see cref="Surge"/> — 시간 초과(과충전). 빛 덩어리가 몰려 보스로 들어가고 줄이 굵어졌다가 사라진다(가슴 큰 섬광은 쓰는 쪽이 한 번만)</item>
/// <item><see cref="Fade"/> — 그 밖의 끝(보스가 끊김·사망). 조용히 사라진다</item>
/// </list>
/// 끝나면 <see cref="IsIdle"/> 이 true — 쓰는 쪽이 다시 <see cref="Connect"/> 해서 재사용한다.
///
/// 조명: 프리팹의 <see cref="lightLeft"/>·<see cref="lightRight"/>(TetherLight_L/_R)를 줄 가운데 양옆에 두고 번갈아 번쩍이게 한다 — 밝기·범위·그림자는 각 Light 에 저장.
/// 토막 둘을 그리려고 층마다 LineRenderer 를 두 개(A = 송전기 쪽, B = 보스 쪽) 쓴다 — 연결 중에는 A 하나가 전체를 그린다.
/// MyProject 미리보기와 MainProject 가 <b>같은 guid</b> 로 공유한다.
/// </summary>
[DisallowMultipleComponent]
public class ElectricTether : MonoBehaviour
{
    enum Phase { Idle, Extending, Connected, Snapping, Surging, Fading }

    [Header("줄")]
    [SerializeField] LineRenderer glowA;
    [SerializeField] LineRenderer glowB;
    [SerializeField] LineRenderer coreA;
    [SerializeField] LineRenderer coreB;
    [SerializeField, Min(0.05f)] float segmentLength = 0.3f;
    [SerializeField, Min(0f)] float jitter = 0.16f;
    [SerializeField, Min(1f)] float regenRate = 20f;

    [Header("빛 덩어리")]
    [SerializeField] ParticleSystem pulses;
    [SerializeField, Min(0.1f)] float pulseSpeed = 5f;
    [SerializeField, Min(0.05f)] float pulseSpacing = 0.9f;
    [SerializeField, Min(0.01f)] float pulseSize = 0.22f;

    [Header("끝 연출")]
    [SerializeField] ParticleSystem breakSpark;
    [SerializeField, Min(1)] int breakSparkCount = 14;
    [Tooltip("보스 가슴 섬광 — 끊김은 작게(개수 × 0.5), 과충전은 전부")]
    [SerializeField] ParticleSystem endFlash;
    [SerializeField, Min(1)] int endFlashCount = 4;
    [SerializeField, Min(0.01f)] float extendTime = 0.15f;
    [SerializeField, Min(0.01f)] float snapTime = 0.25f;
    [SerializeField, Min(0.01f)] float surgeTime = 0.4f;
    [SerializeField, Min(1f)] float surgeSpeedMul = 4f;
    [SerializeField, Min(1f)] float surgeWidthMul = 1.8f;
    [SerializeField, Min(0.01f)] float fadeTime = 0.2f;

    [Header("조명 — 줄 가운데 양옆의 포인트 라이트 2개, 수명 다한 전구처럼 번갈아 번쩍(10-27 민경)")]
    [Tooltip("밝기·범위·색·그림자는 각 Light 에 저장한 값을 쓴다 — Light 의 Intensity 가 번쩍일 때 최대 밝기")]
    [SerializeField] Light lightLeft;
    [SerializeField] Light lightRight;
    [Tooltip("줄 가운데에서 양옆(줄과 수직인 수평 방향)으로 떨어진 거리(m)")]
    [SerializeField, Min(0f)] float lightSideOffset = 1f;
    [Tooltip("번쩍이지 않을 때 밝기(최대 대비)")]
    [SerializeField, Range(0f, 1f)] float lightIdleLevel = 0.15f;
    [Tooltip("번쩍 사이 쉬는 시간(초) 범위 — 쉴 때마다 반대쪽으로 넘어간다")]
    [SerializeField] Vector2 lightGap = new Vector2(0.05f, 0.4f);
    [Tooltip("번쩍 한 번의 길이(초) 범위")]
    [SerializeField] Vector2 lightFlash = new Vector2(0.03f, 0.1f);
    [Tooltip("번쩍일 때 같은 쪽에서 두세 번 떨듯이 연달아 번쩍일 확률")]
    [SerializeField, Range(0f, 1f)] float lightStutterChance = 0.3f;

    const int MaxPulses = 32;
    float _lightMaxL, _lightMaxR;
    float _flickerTimer;
    bool _flashOn;
    int _flashSide;      // 0 = 왼쪽, 1 = 오른쪽
    int _stutterLeft;

    Transform _from;
    Transform _to;
    Phase _phase = Phase.Idle;
    float _t;                       // 현재 단계 경과(초)
    float _regenTimer;
    int _seed;
    float _glowWidth = 1f, _coreWidth = 1f;
    float _fadeFrom = 1f;           // 사라지기 시작할 때의 굵기 배율(과충전 끝 = surgeWidthMul)
    readonly float[] _pulseT = new float[MaxPulses];
    int _pulseCount;
    float _spawnTimer;
    ParticleSystem.Particle[] _particles;
    Vector3[] _points = new Vector3[64];

    public bool IsIdle => _phase == Phase.Idle;

    /// <summary>뻗어 나가는 중이거나 이어져 있다 — Snap·Surge·Fade 가 먹는 상태.</summary>
    public bool IsConnected => IsActive();

    /// <summary>끝나며 가슴 섬광을 낸 순간(Surge 끝) — 쓰는 쪽이 한 번만 큰 섬광을 내는 데 쓴다.</summary>
    public event System.Action<ElectricTether> SurgeArrived;

    void Awake()
    {
        if (glowA != null) _glowWidth = glowA.widthMultiplier;
        if (coreA != null) _coreWidth = coreA.widthMultiplier;
        _particles = new ParticleSystem.Particle[MaxPulses];
        if (lightLeft != null) { _lightMaxL = lightLeft.intensity; lightLeft.enabled = false; }
        if (lightRight != null) { _lightMaxR = lightRight.intensity; lightRight.enabled = false; }
        Show(false, false);
    }

    public void Connect(Transform from, Transform to)
    {
        _from = from;
        _to = to;
        _phase = Phase.Extending;
        _t = 0f;
        _pulseCount = 0;
        _spawnTimer = 0f;
        _regenTimer = 0f;
        SetWidth(1f, 1f);
        Show(true, false);
        if (pulses != null && !pulses.isPlaying) pulses.Play(false);   // 방출 0 — SetParticles 로만 채운다
    }

    public void Snap()
    {
        if (!IsActive()) return;
        _phase = Phase.Snapping;
        _t = 0f;
        _pulseCount = 0;
        Vector3 a = From(), b = To();
        Emit(breakSpark, Vector3.Lerp(a, b, 0.5f), breakSparkCount);
        Emit(endFlash, b, Mathf.Max(1, endFlashCount / 2));
    }

    public void Surge()
    {
        if (!IsActive()) return;
        _phase = Phase.Surging;
        _t = 0f;
    }

    public void Fade()
    {
        if (!IsActive()) return;
        _phase = Phase.Fading;
        _t = 0f;
        _fadeFrom = 1f;
    }

    bool IsActive() => _phase == Phase.Extending || _phase == Phase.Connected;

    void LateUpdate()
    {
        if (_phase == Phase.Idle) return;
        if (_from == null || _to == null) { Stop(); return; }

        _t += Time.deltaTime;
        _regenTimer -= Time.deltaTime;
        if (_regenTimer <= 0f)
        {
            _regenTimer = 1f / regenRate;
            _seed = Random.Range(0, int.MaxValue);
        }

        Vector3 a = From(), b = To();
        float speedMul = 1f;
        // 조명 — 밝기 배율과 켜질 구간(송전기 쪽 0 ~ 보스 쪽 1, 두 토막).
        float lightLevel = 1f, litA = 1f, litB = 1f;

        switch (_phase)
        {
            case Phase.Extending:
            {
                float k = Mathf.Clamp01(_t / extendTime);
                Draw(glowA, coreA, a, b, 0f, k);
                litA = k;
                if (k >= 1f) { _phase = Phase.Connected; _t = 0f; }
                break;
            }
            case Phase.Connected:
                Draw(glowA, coreA, a, b, 0f, 1f);
                break;
            case Phase.Snapping:
            {
                // 가운데서 끊겨 두 토막이 양끝으로 말려 들어간다.
                float k = Mathf.Clamp01(_t / snapTime);
                float half = 0.5f * (1f - k * k);
                Show(true, true);
                Draw(glowA, coreA, a, b, 0f, half);
                Draw(glowB, coreB, a, b, 1f - half, 1f);
                litA = half; litB = 1f - half; lightLevel = 1f - k;
                if (k >= 1f) Stop();
                break;
            }
            case Phase.Surging:
            {
                float k = Mathf.Clamp01(_t / surgeTime);
                speedMul = surgeSpeedMul;
                SetWidth(1f + (surgeWidthMul - 1f) * k, 1f + (surgeWidthMul - 1f) * k);
                Draw(glowA, coreA, a, b, 0f, 1f);
                lightLevel = 1f + (surgeWidthMul - 1f) * k;
                if (k >= 1f)
                {
                    Emit(endFlash, b, endFlashCount);
                    SurgeArrived?.Invoke(this);
                    _phase = Phase.Fading;
                    _t = 0f;
                    _fadeFrom = surgeWidthMul;
                    _pulseCount = 0;
                }
                break;
            }
            case Phase.Fading:
            {
                float k = Mathf.Clamp01(_t / fadeTime);
                float w = (1f - k) * _fadeFrom;
                SetWidth(w, w);
                Draw(glowA, coreA, a, b, 0f, 1f);
                lightLevel = w;
                if (k >= 1f) Stop();
                break;
            }
        }

        bool flowing = _phase == Phase.Connected || _phase == Phase.Surging;
        TickPulses(a, b, flowing, speedMul);
        if (_phase != Phase.Idle) UpdateLights(a, b, lightLevel, litA, litB, speedMul);
    }

    // 조명 — 줄 가운데에서 양옆으로 lightSideOffset 만큼(높이는 줄과 같게). 가운데가 그려져 있을 때만 켠다.
    // 평소엔 lightIdleLevel 로 은은하다가 불규칙하게 한쪽씩 번갈아 번쩍(가끔 같은 쪽에서 두세 번 떨듯이). 과충전 땐 빨라진다(speedMul).
    void UpdateLights(Vector3 a, Vector3 b, float level, float litA, float litB, float speedMul)
    {
        if (lightLeft == null && lightRight == null) return;
        bool lit = 0.5f <= litA || 0.5f >= litB;

        _flickerTimer -= Time.deltaTime * speedMul;
        if (_flickerTimer <= 0f)
        {
            if (_flashOn)
            {
                _flashOn = false;
                if (_stutterLeft > 0) { _stutterLeft--; _flickerTimer = Random.Range(0.03f, 0.06f); }
                else { _flashSide = 1 - _flashSide; _flickerTimer = Random.Range(lightGap.x, lightGap.y); }
            }
            else
            {
                _flashOn = true;
                _flickerTimer = Random.Range(lightFlash.x, lightFlash.y);
                if (_stutterLeft == 0 && Random.value < lightStutterChance) _stutterLeft = Random.Range(1, 3);
            }
        }

        Vector3 mid = Vector3.Lerp(a, b, 0.5f);
        Vector3 side = Vector3.Cross(b - a, Vector3.up);
        side = side.sqrMagnitude > 0.0001f ? side.normalized : Vector3.right;
        SetLight(lightLeft, _lightMaxL, mid - side * lightSideOffset, lit, level, _flashOn && _flashSide == 0);
        SetLight(lightRight, _lightMaxR, mid + side * lightSideOffset, lit, level, _flashOn && _flashSide == 1);
    }

    void SetLight(Light l, float max, Vector3 at, bool lit, float level, bool flashing)
    {
        if (l == null) return;
        float intensity = lit ? max * level * (flashing ? 1f : lightIdleLevel) : 0f;
        l.enabled = intensity > 0.001f;
        if (!l.enabled) return;
        l.transform.position = at;
        l.intensity = intensity;
    }

    // 송전기 쪽 t0 ~ 보스 쪽 t1 구간을 지그재그로 그린다. 끝점(0·1)은 흔들지 않는다 — 소켓에 붙어 있어야 한다.
    void Draw(LineRenderer glow, LineRenderer core, Vector3 a, Vector3 b, float t0, float t1)
    {
        Vector3 d = b - a;
        float len = d.magnitude;
        if (len < 0.001f || t1 <= t0 + 0.0001f)
        {
            if (glow != null) glow.positionCount = 0;
            if (core != null) core.positionCount = 0;
            return;
        }

        Vector3 dir = d / len;
        Vector3 side = Vector3.Cross(dir, Vector3.up);
        if (side.sqrMagnitude < 0.0001f) side = Vector3.Cross(dir, Vector3.right);
        side.Normalize();
        Vector3 up = Vector3.Cross(side, dir);

        int total = Mathf.Clamp(Mathf.CeilToInt(len / segmentLength), 2, _points.Length - 1);
        Random.State saved = Random.state;
        Random.InitState(_seed);

        int n = 0;
        for (int i = 0; i <= total; i++)
        {
            float t = (float)i / total;
            Vector2 r = Random.insideUnitCircle * jitter;   // 구간 밖도 뽑아 둔다 — 잘라 그려도 같은 모양이 이어진다
            if (t < t0 - 0.0001f || t > t1 + 0.0001f) continue;
            float taper = Mathf.Sin(Mathf.PI * t);
            _points[n++] = a + d * t + (side * r.x + up * r.y) * taper;
        }
        Random.state = saved;

        // 끝이 정점 사이에 걸리면 마지막 점을 정확히 그 자리에 둔다(뻗어 나가기·말려 들어가기).
        if (n == 0) { _points[n++] = a + d * t0; }
        Vector3 tip = a + d * t1;
        if ((_points[n - 1] - tip).sqrMagnitude > 0.0001f && n < _points.Length) _points[n++] = tip;

        if (glow != null) { glow.positionCount = n; glow.SetPositions(_points); }
        if (core != null) { core.positionCount = n; core.SetPositions(_points); }
    }

    void TickPulses(Vector3 a, Vector3 b, bool flowing, float speedMul)
    {
        if (pulses == null) return;
        float len = Mathf.Max(0.01f, Vector3.Distance(a, b));
        float dt = Time.deltaTime * pulseSpeed * speedMul / len;

        int w = 0;
        for (int i = 0; i < _pulseCount; i++)
        {
            float t = _pulseT[i] + dt;
            if (t < 1f) _pulseT[w++] = t;
        }
        _pulseCount = w;

        if (flowing)
        {
            _spawnTimer -= Time.deltaTime * speedMul;
            if (_spawnTimer <= 0f && _pulseCount < MaxPulses)
            {
                _spawnTimer = pulseSpacing / pulseSpeed;
                _pulseT[_pulseCount++] = 0f;
            }
        }

        Color c = pulses.main.startColor.color;
        for (int i = 0; i < _pulseCount; i++)
        {
            float t = _pulseT[i];
            ref ParticleSystem.Particle p = ref _particles[i];
            p.position = Vector3.Lerp(a, b, t);
            p.startSize = pulseSize * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t)) * (speedMul > 1f ? 1.3f : 1f) + 0.02f;
            p.startColor = c;
            p.remainingLifetime = 1f;
            p.startLifetime = 1f;
        }
        pulses.SetParticles(_particles, _pulseCount);
    }

    Vector3 From() => _from.position;
    Vector3 To() => _to.position;

    void SetWidth(float glowMul, float coreMul)
    {
        if (glowA != null) glowA.widthMultiplier = _glowWidth * glowMul;
        if (glowB != null) glowB.widthMultiplier = _glowWidth * glowMul;
        if (coreA != null) coreA.widthMultiplier = _coreWidth * coreMul;
        if (coreB != null) coreB.widthMultiplier = _coreWidth * coreMul;
    }

    void Show(bool a, bool b)
    {
        if (glowA != null) glowA.enabled = a;
        if (coreA != null) coreA.enabled = a;
        if (glowB != null) glowB.enabled = b;
        if (coreB != null) coreB.enabled = b;
    }

    void Stop()
    {
        _phase = Phase.Idle;
        _pulseCount = 0;
        if (pulses != null) pulses.SetParticles(_particles, 0);
        Show(false, false);
        if (lightLeft != null) lightLeft.enabled = false;
        if (lightRight != null) lightRight.enabled = false;
    }

    // 월드 시뮬레이션이라 옮겨도 이미 나간 입자는 제자리에 남는다. 방출량 0·버스트 없음이라 Play 는 아무것도 안 낸다.
    static void Emit(ParticleSystem ps, Vector3 at, int count)
    {
        if (ps == null) return;
        ps.transform.position = at;
        if (!ps.isPlaying) ps.Play(false);
        ps.Emit(count);
    }
}
