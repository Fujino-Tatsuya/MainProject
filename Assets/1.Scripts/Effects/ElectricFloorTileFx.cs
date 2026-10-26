using UnityEngine;

/// <summary>
/// 23호 전기 장판 칸 하나의 연출(2026-10-23 민경 grill — MainProject PLAN.md 맨 위 「23호 전기 장판 VFX」).
/// 칸마다 하나씩 만들어 재사용한다(<see cref="ElectricFloorFxPool"/>). 프리팹은 1m 칸 기준으로 저작하고 <see cref="Setup"/> 이 실제 칸 크기로 맞춘다.
///
/// 흐름:
/// <list type="bullet">
/// <item><see cref="Warn"/> — ① 예고. 칸 안 전기 튐이 예고 시간에 맞춰 0 → <see cref="warnRate"/> 로 잦아진다.
///   마지막 <see cref="preFireTime"/> 초(예고 길이와 무관)는 (a) 몰아치기 — <see cref="preFireRate"/> + 바닥이 <see cref="preFireGlow"/> 까지 밝아진다</item>
/// <item><see cref="Fire"/> — ② 발동(천둥 번개). 바닥 번쩍 + 갈라지는 번개(<see cref="ElectricFloorBolts"/>)가 시작점에서 <see cref="growTime"/> 초에
///   칸 끝까지 뻗고, <see cref="restrikeInterval"/> 초마다 모양을 바꿔 다시 친다(밝기가 들쭉날쭉 — 번쩍임). 지정 시간 뒤 (b) 잔광 <see cref="afterglow"/> 초에 꺼진다.
///   칸 가장자리를 따라 전류가 흐른다(<see cref="ElectricFloorEdges"/> — 떨리는 선 위로 빛 줄기가 빠르게 미끄러짐, 번개와 같은 박자로 떨림이 바뀜).
///   즉사 방전은 번개 모양 수치(<see cref="ElectricFloorBolts"/> 의 즉사 모양)와 밝기(<see cref="lethalBoost"/>)만 다르다 — 추가 연출 없음(10-23·26 민경)</item>
/// <item><see cref="Clear"/> — 즉시 끔(보스 사망·제압·기믹 종료)</item>
/// </list>
/// 예고와 발동은 따로 돈다 — 기믹 B 처럼 발동 잔광이 남은 칸에 바로 다음 예고가 와도 둘 다 보인다.
///
/// MyProject 미리보기와 MainProject 가 <b>같은 guid</b> 로 공유한다 — 네트워크·보스는 모른다.
/// </summary>
[DisallowMultipleComponent]
public class ElectricFloorTileFx : MonoBehaviour
{
    [Header("구성(1m 칸 기준)")]
    [Tooltip("갈라지는 번개(발동)")]
    [SerializeField] ElectricFloorBolts bolts;
    [Tooltip("가장자리 전류(발동)")]
    [SerializeField] ElectricFloorEdges edges;
    [Tooltip("바닥 밝아짐 판(발동 직전·발동 번쩍)")]
    [SerializeField] Renderer glow;
    [SerializeField] ParticleSystem warnSparks;
    [SerializeField] ParticleSystem flash;
    [SerializeField] ParticleSystem arcs;
    [SerializeField] ParticleSystem fireSparks;
    [Tooltip("칸 크기에 맞춰 방출 범위를 늘릴 파티클(Box 모양). 비우면 warnSparks·arcs·fireSparks")]
    [SerializeField] ParticleSystem[] areaEmitters;
    [Tooltip("칸 크기만큼 크기를 키울 파티클")]
    [SerializeField] ParticleSystem[] sizeEmitters;

    [Header("① 예고")]
    [SerializeField, Min(0f)] float warnRate = 24f;
    [SerializeField, Min(0.01f)] float preFireTime = 0.25f;
    [SerializeField, Min(0f)] float preFireRate = 90f;
    [SerializeField, Min(0f)] float preFireGlow = 0.35f;

    [Header("② 발동 — 천둥 번개")]
    [SerializeField, Min(0f)] float fireGlow = 0.6f;
    [SerializeField, Min(0.01f)] float fireGlowTime = 0.12f;
    [Tooltip("첫 방전이 시작점에서 칸 끝까지 뻗는 시간(초) — 레퍼런스의 3~4배 빠르기")]
    [SerializeField, Min(0.01f)] float growTime = 0.1f;
    [Tooltip("다시 치는 간격(초) — 칠 때마다 모양이 바뀐다")]
    [SerializeField, Min(0.02f)] float restrikeInterval = 0.07f;
    [Tooltip("다시 칠 때 밝기 범위(첫 방전 = 1). 방전 하나 안에서는 이 값에서 decay 만큼 어두워진다")]
    [SerializeField] Vector2 restrikeLevel = new Vector2(0.45f, 1f);
    [SerializeField, Range(0f, 1f)] float strikeDecay = 0.45f;
    [Tooltip("발동 동안 바닥 번개 조각을 내는 시각(발동 시작 기준, 초)")]
    [SerializeField] float[] arcTimes = { 0f, 0.2f };
    [SerializeField, Min(0f)] float afterglow = 0.25f;
    [Tooltip("가장자리 전류 밝기 — 방전 밝기를 이만큼만 따라 출렁인다(0 = 일정)")]
    [SerializeField, Min(0f)] float edgeLevel = 0.9f;
    [SerializeField, Range(0f, 1f)] float edgeFollow = 0.3f;

    [Header("즉사 방전")]
    [Tooltip("즉사 방전 밝기 배율 — 번개·가장자리 전류·바닥 번쩍에 곱한다. 셰이더가 밝기를 알파에도 넣어(제곱) 체감은 이보다 크다")]
    [SerializeField, Min(1f)] float lethalBoost = 1.5f;

    static readonly int ColorFactorId = Shader.PropertyToID("_ColorFactor");

    MaterialPropertyBlock _block;
    float _glowFactor = 1f;
    float[] _authoredSize;
    Vector3[] _authoredShape;
    bool _cached;

    // 예고
    bool _warning;
    float _warnStart, _warnTime;
    // 발동
    bool _firing;
    float _fireStart, _fireTime;
    bool _lethal;
    int _seed, _strike = -1, _arcIndex;
    float _strikeLevel = 1f;

    public bool IsIdle => !_warning && !_firing;

    void Awake()
    {
        Cache();
        SetLevels(0f, 0f);
    }

    void Cache()
    {
        if (_cached) return;
        _cached = true;
        _block = new MaterialPropertyBlock();
        Material gm = glow != null ? glow.sharedMaterial : null;
        _glowFactor = gm != null && gm.HasProperty(ColorFactorId) ? gm.GetFloat(ColorFactorId) : 1f;

        if (areaEmitters == null || areaEmitters.Length == 0) areaEmitters = new[] { warnSparks, arcs, fireSparks };
        _authoredShape = new Vector3[areaEmitters.Length];
        for (int i = 0; i < areaEmitters.Length; i++)
            if (areaEmitters[i] != null) _authoredShape[i] = areaEmitters[i].shape.scale;

        if (sizeEmitters == null) sizeEmitters = new ParticleSystem[0];
        _authoredSize = new float[sizeEmitters.Length];
        for (int i = 0; i < sizeEmitters.Length; i++)
            if (sizeEmitters[i] != null) _authoredSize[i] = sizeEmitters[i].main.startSizeMultiplier;
    }

    /// <summary>
    /// 실제 칸 크기(m, x = 열 방향 · y = 행 방향)로 번개·바닥 판·방출 범위를 맞춘다. 루트는 배율 1 로 둔다.
    /// <paramref name="row"/>·<paramref name="col"/> = 판 위 칸 위치 — 번개 끝점을 이웃 칸과 맞추는 데 쓴다.
    /// </summary>
    public void Setup(Vector2 extent, int row, int col)
    {
        Cache();
        if (bolts != null) bolts.Setup(extent, row, col);
        if (edges != null) edges.Setup(extent, row, col);
        if (glow != null) glow.transform.localScale = new Vector3(extent.x, extent.y, 1f);

        for (int i = 0; i < areaEmitters.Length; i++)
        {
            if (areaEmitters[i] == null) continue;
            ParticleSystem.ShapeModule s = areaEmitters[i].shape;
            Vector3 a = _authoredShape[i];
            s.scale = new Vector3(a.x * extent.x, a.y * extent.y, a.z);
        }

        float size = Mathf.Min(extent.x, extent.y);
        for (int i = 0; i < sizeEmitters.Length; i++)
        {
            if (sizeEmitters[i] == null) continue;
            ParticleSystem.MainModule m = sizeEmitters[i].main;
            m.startSizeMultiplier = _authoredSize[i] * size;
        }
    }

    /// <summary>① 예고 시작 — <paramref name="time"/> 초 뒤 발동이 온다.</summary>
    public void Warn(float time)
    {
        Cache();
        _warning = true;
        _warnStart = Time.time;
        _warnTime = Mathf.Max(0.01f, time);
        if (warnSparks != null)
        {
            SetRate(0f);
            if (!warnSparks.isPlaying) warnSparks.Play(true);
        }
    }

    /// <summary>
    /// ② 발동 — <paramref name="time"/> 초 유지 뒤 잔광. <paramref name="lethal"/> = 즉사 방전(번개 모양만 다름).
    /// <paramref name="seed"/> = 이번 발동 번호(판 전체 같은 값) — 이웃 칸과 번개 끝점이 맞는다.
    /// <paramref name="edgeFlags"/> = 가장자리 전류를 그릴 변(<see cref="ElectricFloorEdges.Top"/> 등 — 한 변은 한 칸만 그린다).
    /// </summary>
    public void Fire(float time, bool lethal = false, int seed = 0,
                     int edgeFlags = ElectricFloorEdges.Top | ElectricFloorEdges.Bottom | ElectricFloorEdges.Left | ElectricFloorEdges.Right)
    {
        if (edges != null) edges.Build(edgeFlags);
        Cache();
        StopWarn();
        _firing = true;
        _fireStart = Time.time;
        _fireTime = Mathf.Max(0.01f, time);
        _lethal = lethal;
        _seed = seed;
        _strike = -1;
        _arcIndex = 0;

        if (flash != null) flash.Play(true);
        if (fireSparks != null) fireSparks.Play(true);
        if (arcs != null && !arcs.isPlaying) arcs.Play(true);
    }

    /// <summary>즉시 끔 — 남은 입자까지 지운다.</summary>
    public void Clear()
    {
        Cache();
        _warning = false;
        _firing = false;
        Halt(warnSparks);
        Halt(flash);
        Halt(arcs);
        Halt(fireSparks);
        SetLevels(0f, 0f);
        if (edges != null) edges.SetLevel(0f);
    }

    static void Halt(ParticleSystem ps)
    {
        if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    void StopWarn()
    {
        if (!_warning) return;
        _warning = false;
        if (warnSparks != null) warnSparks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    void Update()
    {
        float now = Time.time;
        float glowLevel = 0f, boltLevel = 0f, edge = 0f;

        if (_warning)
        {
            float elapsed = now - _warnStart;
            float remaining = _warnTime - elapsed;
            if (remaining <= preFireTime)
            {
                // (a) 몰아치기 — 예고가 짧아도 이 구간은 같은 길이.
                float k = 1f - Mathf.Clamp01(remaining / preFireTime);
                SetRate(preFireRate);
                glowLevel = preFireGlow * k;
            }
            else
                SetRate(warnRate * Mathf.Clamp01(elapsed / Mathf.Max(0.01f, _warnTime - preFireTime)));

            // 발동 신호가 늦거나 안 오면(예고만 걷힘) 조금 기다렸다가 스스로 멈춘다 — 지우기 신호가 따로 오면 그쪽이 먼저.
            if (remaining < -0.5f) StopWarn();
        }

        if (_firing)
        {
            float f = now - _fireStart;
            float boost = _lethal ? lethalBoost : 1f;
            if (f < _fireTime)
            {
                // 다시 치기 — 칠 때마다 새 모양 · 들쭉날쭉한 밝기(첫 방전은 최대).
                int strike = Mathf.FloorToInt(f / restrikeInterval);
                if (strike != _strike && bolts != null)
                {
                    _strike = strike;
                    bolts.Strike(_seed, strike, _lethal);
                    if (edges != null) edges.Strike(_seed, strike);
                    _strikeLevel = strike == 0 ? 1f : Mathf.Lerp(restrikeLevel.x, restrikeLevel.y, Hash01(_seed, strike));
                }
                float inStrike = (f - Mathf.Max(0, _strike) * restrikeInterval) / restrikeInterval;
                boltLevel = _strikeLevel * (1f - strikeDecay * inStrike);
                edge = edgeLevel * (1f - edgeFollow + edgeFollow * boltLevel);
                if (bolts != null)
                    bolts.SetReveal(_strike == 0 ? bolts.MaxDistance * Mathf.Clamp01(f / growTime) + 0.4f : float.PositiveInfinity);
                glowLevel = Mathf.Max(glowLevel, fireGlow * boost * (1f - Mathf.Clamp01(f / fireGlowTime)));
            }
            else if (afterglow > 0f && f < _fireTime + afterglow)
            {
                float k = 1f - (f - _fireTime) / afterglow;
                boltLevel = _strikeLevel * (1f - strikeDecay) * k;   // 마지막 모양이 식는다
                edge = edgeLevel * (1f - edgeFollow + edgeFollow * boltLevel) * k;
            }
            else
                _firing = false;

            boltLevel *= boost;
            edge *= boost;

            if (arcs != null && arcTimes != null)
                while (_arcIndex < arcTimes.Length && f >= arcTimes[_arcIndex] && arcTimes[_arcIndex] < _fireTime)
                {
                    arcs.Emit(1);
                    _arcIndex++;
                }
        }

        SetLevels(boltLevel, glowLevel);
        if (edges != null) edges.SetLevel(edge);
    }

    static float Hash01(int seed, int strike)
    {
        unchecked
        {
            uint h = (uint)(seed * 73856093) ^ (uint)(strike * 19349663);
            h ^= h >> 13;
            h *= 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFF) / 65535f;
        }
    }

    void SetRate(float perSecond)
    {
        if (warnSparks == null) return;
        ParticleSystem.EmissionModule e = warnSparks.emission;
        e.rateOverTimeMultiplier = perSecond;
    }

    void SetLevels(float boltLevel, float glowLevel)
    {
        if (bolts != null) bolts.SetLevel(boltLevel);
        if (glow == null) return;
        float factor = _glowFactor * glowLevel;
        bool on = factor > 0.001f;
        if (glow.enabled != on) glow.enabled = on;
        if (!on) return;
        glow.GetPropertyBlock(_block);
        _block.SetFloat(ColorFactorId, factor);
        glow.SetPropertyBlock(_block);
    }
}
