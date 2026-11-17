using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 거너 Q 차징 이펙트 세부 조절(할 일 295) — [이 피어] 표시 전용. 프리팹(SVN)은 고치지 않고
/// 재생 중인 <b>풀 인스턴스</b>의 ParticleSystem 값을 배율로 덮었다가 <see cref="RestoreAll"/> 에서 처음 값으로 되돌린다.
/// <para>
/// 왜 루트 스케일이 아니라 값인가 — FX_GunnerCharge 파트 셋은 <c>scalingMode = Local</c> 이라 부모(루트) 스케일을 안 받는다.
/// 294 의 루트 <c>localScale</c> 은 그래서 보이지 않았다. 구체 크기는 여기서 파트 크기·모양 반경·시작 속도에 곱한다(균일 스케일과 같은 모양).
/// </para>
/// <para>
/// 파트 = 렌더 모드(<see cref="PartOf"/>): Stretch = 빛줄기(line), Mesh = 입자(particle), 그 밖 = 코어(core).
/// 프리팹 오브젝트 이름(ChargeLine 이 Mesh, ChargeParticle 이 Stretch)과 화면에서 보이는 모양이 반대라 이름 대신 렌더 모드로 가른다.
/// </para>
/// <para>
/// 다른 곳도 같은 칸을 쓴다 — 재생 속도(<c>ShurikenEffectSystem.SetPlayRate</c> = simulationSpeed)·색(<c>EffectRecolor</c> = MaterialPropertyBlock).
/// 그 칸은 "마지막에 내가 쓴 값"과 지금 값이 다르면 밖에서 바뀐 것으로 보고 그 값을 새 기준으로 삼는다(배율은 그 위에 곱함).
/// 밝기 배율 1 이면 MaterialPropertyBlock 을 <b>걸지 않는다</b> — 같은 값이라도 블록을 걸면 이 파티클 셰이더들이 다르게 그려진다(EffectRecolor 11-03 메모).
/// </para>
/// 순수 계산(<see cref="PartOf"/>·<see cref="ScaleCurve"/>·<see cref="TrackedBase"/>)은 테스트 <c>BossVictoryChargeFxTests</c>.
/// </summary>
public sealed class BossVictoryChargeFxApplier
{
    public enum Part { Line, Particle, Core }

    /// <summary>파트 가르기 — Stretch = 빛줄기, Mesh = 입자, 그 밖(Billboard 등) = 코어.</summary>
    public static Part PartOf(ParticleSystemRenderMode mode) => mode switch
    {
        ParticleSystemRenderMode.Stretch => Part.Line,
        ParticleSystemRenderMode.Mesh => Part.Particle,
        _ => Part.Core,
    };

    /// <summary>MinMaxCurve 에 배율 — 상수는 값, 곡선은 곡선 배율에 곱한다.</summary>
    public static ParticleSystem.MinMaxCurve ScaleCurve(ParticleSystem.MinMaxCurve c, float k)
    {
        switch (c.mode)
        {
            case ParticleSystemCurveMode.Constant:
                c.constant *= k;
                break;
            case ParticleSystemCurveMode.TwoConstants:
                c.constantMin *= k;
                c.constantMax *= k;
                break;
            default:
                c.curveMultiplier *= k;
                break;
        }
        return c;
    }

    /// <summary>
    /// 밖에서도 쓰는 칸의 기준값 — 내가 쓴 적 없거나(<paramref name="hasWritten"/> = false) 지금 값이 마지막에 쓴 값과 다르면(밖에서 바뀜) 지금 값이 기준, 아니면 기준 그대로.
    /// </summary>
    public static float TrackedBase(float current, float written, float oldBase, bool hasWritten) =>
        !hasWritten || !Mathf.Approximately(current, written) ? current : oldBase;

    static readonly int[] ColorProps =
    {
        Shader.PropertyToID("_TintColor"),
        Shader.PropertyToID("_Color"),
        Shader.PropertyToID("_BaseColor"),
    };

    sealed class Home
    {
        public ParticleSystem ps;
        public ParticleSystemRenderer renderer;
        public Part part;

        // 처음 값(되돌리기)
        public float sizeX, sizeY, sizeZ, size, speed, radius, lifetime, rate, lengthScale;
        public bool size3D, shape;
        public ParticleSystem.Burst[] bursts;

        // 밖에서도 쓰는 칸 — 재생 속도
        public float simBase, simWritten;
        public bool simTouched;

        // 밖에서도 쓰는 칸 — 색(MaterialPropertyBlock 슬롯 0)
        public int colorProp = -1;
        public Color colorBase, colorWritten;
        public bool colorTouched, colorFromBlock;

        // 마지막에 쓴 배율(같으면 다시 안 씀)
        public bool applied;
        public float aSize = 1f, aSphere = 1f, aAmount = 1f, aInterval, aLifetime = 1f, aLength = 1f;
    }

    readonly Dictionary<ParticleSystem, Home> _homes = new Dictionary<ParticleSystem, Home>();
    readonly List<GameObject> _instances = new List<GameObject>();
    readonly List<ParticleSystem> _systems = new List<ParticleSystem>();
    MaterialPropertyBlock _block;

    /// <summary>
    /// 차징 루프 파트 배율 — 파트별 크기·양(rate·버스트 수)·반복 간격(0 = 프리팹)·수명·재생 속도·밝기·길이(빛줄기만) ×
    /// 구체 크기(<paramref name="sphereScale"/> — 크기·모양 반경·시작 속도). 재생 중인 루프(<see cref="EffectSocketPlayer.Play"/>)만.
    /// </summary>
    public void ApplyParts(EffectSocketPlayer player, in BossVictoryGunnerCharge.ChargeFxSample fx, float sphereScale)
    {
        if (!Collect(player)) return;
        for (int i = 0; i < _systems.Count; i++)
        {
            Home h = HomeOf(_systems[i]);
            BossVictoryGunnerCharge.PartSample p = h.part switch
            {
                Part.Line => fx.Line,
                Part.Particle => fx.Particle,
                _ => fx.Core,
            };
            WriteShape(h, p.Size, sphereScale, p.Lifetime, h.part == Part.Line ? p.Length : 1f);
            WriteAmount(h, p.Amount, p.RepeatInterval);
            WriteSpeed(h, p.Speed);
            WriteBrightness(h, p.Brightness, 1f);
        }
    }

    /// <summary>
    /// 이펙트 전체에 양(rate·버스트 수)·밝기 배율과 알파(<paramref name="alpha"/> — 레이저 페이드아웃) — 최대 충전 알림·과충전·레이저.
    /// 재생 중인 루프만(<see cref="EffectSocketPlayer.Play"/>). 배율 1·알파 1 이면 그 칸은 건드리지 않는다(이미 바꿨으면 되돌림).
    /// </summary>
    public void ApplyUniform(EffectSocketPlayer player, float amount, float brightness, float alpha)
    {
        if (!Collect(player)) return;
        for (int i = 0; i < _systems.Count; i++)
        {
            Home h = HomeOf(_systems[i]);
            WriteAmount(h, amount, 0f);
            WriteBrightness(h, brightness, alpha);
        }
    }

    /// <summary>덮은 값 전부 처음 값으로(정리·취소·파괴). 풀에 돌아간 인스턴스도 되돌린다(파괴된 것은 건너뜀).</summary>
    public void RestoreAll()
    {
        foreach (Home h in _homes.Values)
        {
            if (h.ps == null) continue;
            if (h.applied)
            {
                ParticleSystem.MainModule main = h.ps.main;
                if (h.size3D)
                {
                    main.startSizeXMultiplier = h.sizeX;
                    main.startSizeYMultiplier = h.sizeY;
                    main.startSizeZMultiplier = h.sizeZ;
                }
                else main.startSizeMultiplier = h.size;
                main.startSpeedMultiplier = h.speed;
                main.startLifetimeMultiplier = h.lifetime;
                if (h.shape) { ParticleSystem.ShapeModule shape = h.ps.shape; shape.radius = h.radius; }
                ParticleSystem.EmissionModule emission = h.ps.emission;
                emission.rateOverTimeMultiplier = h.rate;
                if (h.bursts != null) emission.SetBursts(h.bursts);
                if (h.renderer != null && h.renderer.renderMode == ParticleSystemRenderMode.Stretch) h.renderer.lengthScale = h.lengthScale;
            }
            if (h.simTouched)
            {
                ParticleSystem.MainModule main = h.ps.main;
                if (Mathf.Approximately(main.simulationSpeed, h.simWritten)) main.simulationSpeed = h.simBase;
            }
            if (h.colorTouched) RestoreColor(h);
        }
        _homes.Clear();
    }

    bool Collect(EffectSocketPlayer player)
    {
        _systems.Clear();
        if (player == null) return false;
        int count = player.GetInstances(_instances);
        for (int i = 0; i < count; i++)
        {
            if (_instances[i] == null) continue;
            _instances[i].GetComponentsInChildren(true, ScratchSystems);
            _systems.AddRange(ScratchSystems);
        }
        return _systems.Count > 0;
    }

    static readonly List<ParticleSystem> ScratchSystems = new List<ParticleSystem>();

    Home HomeOf(ParticleSystem ps)
    {
        if (_homes.TryGetValue(ps, out Home h)) return h;
        ParticleSystem.MainModule main = ps.main;
        ParticleSystem.EmissionModule emission = ps.emission;
        ParticleSystem.ShapeModule shape = ps.shape;
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        h = new Home
        {
            ps = ps,
            renderer = renderer,
            part = renderer != null ? PartOf(renderer.renderMode) : Part.Core,
            size3D = main.startSize3D,
            sizeX = main.startSizeXMultiplier,
            sizeY = main.startSizeYMultiplier,
            sizeZ = main.startSizeZMultiplier,
            size = main.startSizeMultiplier,
            speed = main.startSpeedMultiplier,
            lifetime = main.startLifetimeMultiplier,
            shape = shape.enabled,
            radius = shape.radius,
            rate = emission.rateOverTimeMultiplier,
            lengthScale = renderer != null ? renderer.lengthScale : 1f,
        };
        int burstCount = emission.burstCount;
        if (burstCount > 0)
        {
            h.bursts = new ParticleSystem.Burst[burstCount];
            emission.GetBursts(h.bursts);
        }
        if (renderer != null && renderer.sharedMaterial != null)
            foreach (int prop in ColorProps)
                if (renderer.sharedMaterial.HasProperty(prop)) { h.colorProp = prop; break; }
        _homes[ps] = h;
        return h;
    }

    // 크기(파트 × 구체)·모양 반경·시작 속도(구체 — 균일 스케일과 같은 모양)·수명·길이(빛줄기). 배율이 그대로면 안 쓴다.
    static void WriteShape(Home h, float partSize, float sphereScale, float lifetime, float length)
    {
        float size = partSize * sphereScale;
        if (Same(h.aSize, size) && Same(h.aLifetime, lifetime) && Same(h.aLength, length) && Same(h.aSphere, sphereScale)) return;
        h.applied = true;
        h.aSize = size;
        h.aLifetime = lifetime;
        h.aLength = length;
        h.aSphere = sphereScale;

        ParticleSystem.MainModule main = h.ps.main;
        if (h.size3D)
        {
            main.startSizeXMultiplier = h.sizeX * size;
            main.startSizeYMultiplier = h.sizeY * size;
            main.startSizeZMultiplier = h.sizeZ * size;
        }
        else main.startSizeMultiplier = h.size * size;
        main.startSpeedMultiplier = h.speed * sphereScale;
        main.startLifetimeMultiplier = h.lifetime * lifetime;
        if (h.shape) { ParticleSystem.ShapeModule shape = h.ps.shape; shape.radius = h.radius * sphereScale; }
        if (h.renderer != null && h.renderer.renderMode == ParticleSystemRenderMode.Stretch) h.renderer.lengthScale = h.lengthScale * length;
    }

    // 양 — rate·버스트 수 × 배율, 반복 간격 = 값(> 0)·0 이면 프리팹.
    static void WriteAmount(Home h, float amount, float interval)
    {
        if (Same(h.aAmount, amount) && Same(h.aInterval, interval)) return;
        h.applied = true;
        h.aAmount = amount;
        h.aInterval = interval;

        ParticleSystem.EmissionModule emission = h.ps.emission;
        emission.rateOverTimeMultiplier = h.rate * amount;
        if (h.bursts == null) return;
        var bursts = new ParticleSystem.Burst[h.bursts.Length];
        for (int i = 0; i < bursts.Length; i++)
        {
            ParticleSystem.Burst b = h.bursts[i];
            b.count = ScaleCurve(b.count, amount);
            if (interval > 0f) b.repeatInterval = interval;
            bursts[i] = b;
        }
        emission.SetBursts(bursts);
    }

    // 재생 속도 — 기준(밖에서 쓴 값 따라감) × 배율. 배율 1 이고 안 건드렸으면 그대로.
    static void WriteSpeed(Home h, float speed)
    {
        ParticleSystem.MainModule main = h.ps.main;
        if (!h.simTouched && Same(speed, 1f)) return;
        h.simBase = TrackedBase(main.simulationSpeed, h.simWritten, h.simBase, h.simTouched);
        float value = h.simBase * speed;
        h.simTouched = true;
        h.simWritten = value;
        if (!Mathf.Approximately(main.simulationSpeed, value)) main.simulationSpeed = value;
    }

    // 밝기·알파 — 재질 색 칸(_TintColor·_Color·_BaseColor 중 처음 것)에 MaterialPropertyBlock. 배율 1·알파 1 이면 블록을 걸지 않는다(걸었으면 되돌림).
    void WriteBrightness(Home h, float brightness, float alpha)
    {
        if (h.renderer == null || h.colorProp < 0) return;
        bool identity = Same(brightness, 1f) && Same(alpha, 1f);
        if (identity)
        {
            if (h.colorTouched) RestoreColor(h);
            return;
        }

        _block ??= new MaterialPropertyBlock();
        h.renderer.GetPropertyBlock(_block, 0);
        bool inBlock = _block.HasColor(h.colorProp) || _block.HasVector(h.colorProp);
        Color current = inBlock ? _block.GetColor(h.colorProp) : h.renderer.sharedMaterial.GetColor(h.colorProp);
        if (!h.colorTouched || current != h.colorWritten)
        {
            // 처음이거나 밖(EffectRecolor 색 지정)에서 바뀜 — 지금 값이 새 기준.
            h.colorBase = current;
            h.colorFromBlock = inBlock;
        }
        Color c = h.colorBase;
        c.r *= brightness * alpha;
        c.g *= brightness * alpha;
        c.b *= brightness * alpha;
        c.a *= alpha;
        if (h.colorTouched && c == h.colorWritten) return;
        h.colorTouched = true;
        h.colorWritten = c;
        _block.SetColor(h.colorProp, c);
        h.renderer.SetPropertyBlock(_block, 0);
    }

    void RestoreColor(Home h)
    {
        h.colorTouched = false;
        if (h.renderer == null) return;
        _block ??= new MaterialPropertyBlock();
        h.renderer.GetPropertyBlock(_block, 0);
        Color current = _block.HasColor(h.colorProp) || _block.HasVector(h.colorProp) ? _block.GetColor(h.colorProp) : h.colorWritten;
        if (current != h.colorWritten) return;   // 그 뒤 밖에서 다시 칠함 — 그 값을 둔다
        if (h.colorFromBlock || HasOtherColor(_block, h.colorProp)) _block.SetColor(h.colorProp, h.colorBase);
        else _block.Clear();                       // 재질 값이 기준이고 블록에 다른 칸 없음 — 빈 블록 = 재질 값 그대로(EffectRecolor.Restore 와 같은 방식)
        h.renderer.SetPropertyBlock(_block, 0);
    }

    static bool HasOtherColor(MaterialPropertyBlock block, int except)
    {
        foreach (int prop in ColorProps)
            if (prop != except && (block.HasColor(prop) || block.HasVector(prop))) return true;
        return false;
    }

    static bool Same(float a, float b) => Mathf.Abs(a - b) < 1e-5f;
}
