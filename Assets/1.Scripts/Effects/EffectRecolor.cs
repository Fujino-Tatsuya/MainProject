using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 풀 인스턴스 하나의 색을 <see cref="EffectTint"/> 로 바꾸고, 반납 때 원래대로 되돌린다(2026-11-03 민경).
/// <see cref="EffectManager"/> 만 쓴다 — 색 지정 재생일 때 인스턴스에 붙이고(<see cref="Apply"/>), 반납 직전 <see cref="Restore"/>.
///
/// <b>무엇을 바꾸나</b>(이펙트 색이 들어 있는 곳 전부):
/// - 파티클: main.startColor · colorOverLifetime · colorBySpeed · trails(colorOverLifetime·colorOverTrail) — 켜진 모듈만.
/// - 렌더러 재질 색: <c>_TintColor</c>·<c>_Color</c>·<c>_BaseColor</c> 중 재질에 있는 것 — 재질은 공유 에셋이라 직접 고치지 않고
///   MaterialPropertyBlock(재질 슬롯별)으로 덮는다.
///
/// <b>원래 색은 처음 칠할 때 한 번 기억한다.</b> 인스턴스는 늘 깨끗한 상태로 풀에 돌아오므로(반납 때 Restore) 그때 읽은 값이 저작값이다.
/// 🔴 재생 중에 다른 코드가 같은 모듈 색을 바꾸면(이펙트 자체 스크립트 등) 그 값은 Restore 가 저작값으로 덮는다 — 색 지정 재생 대상은
///    런타임에 색을 만지지 않는 이펙트여야 한다. 위치·크기·입자 배열(SetParticles)을 만지는 건 상관없다.
/// 🔴 같은 렌더러에 이미 MaterialPropertyBlock 을 쓰는 스크립트가 있으면 서로 덮는다 — 그런 이펙트(TelegraphRing 등)엔 쓰지 말 것.
/// </summary>
[DisallowMultipleComponent]
public sealed class EffectRecolor : MonoBehaviour
{
    static readonly int[] ColorProps =
    {
        Shader.PropertyToID("_TintColor"),
        Shader.PropertyToID("_Color"),
        Shader.PropertyToID("_BaseColor"),
    };

    struct SystemColors
    {
        public ParticleSystem ps;
        public ParticleSystem.MinMaxGradient start;
        public bool overLifetime, bySpeed, trailLife;
        public ParticleSystem.MinMaxGradient life, speed, trailLifeColor, trailTrailColor;
    }

    struct MaterialColor
    {
        public Renderer renderer;
        public int materialIndex;
        public int property;
        public Color original;
    }

    readonly List<SystemColors> _systems = new List<SystemColors>();
    readonly List<MaterialColor> _materials = new List<MaterialColor>();
    readonly HashSet<(Renderer, int)> _blocked = new HashSet<(Renderer, int)>();
    MaterialPropertyBlock _block;
    bool _captured, _applied;

    /// <summary>인스턴스를 이 색으로 칠한다. 색 없음(None)이면 원래대로 되돌린다.</summary>
    public static void Apply(GameObject instance, in EffectTint tint)
    {
        if (instance == null) return;

        EffectRecolor recolor = instance.GetComponent<EffectRecolor>();
        if (!tint.IsSet)
        {
            if (recolor != null) recolor.Restore();
            return;
        }

        if (recolor == null) recolor = instance.AddComponent<EffectRecolor>();
        recolor.ApplyTint(tint);
    }

    void Capture()
    {
        _captured = true;

        foreach (ParticleSystem ps in GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.ColorOverLifetimeModule life = ps.colorOverLifetime;
            ParticleSystem.ColorBySpeedModule speed = ps.colorBySpeed;
            ParticleSystem.TrailModule trails = ps.trails;
            _systems.Add(new SystemColors
            {
                ps = ps,
                start = ps.main.startColor,
                overLifetime = life.enabled,
                life = life.color,
                bySpeed = speed.enabled,
                speed = speed.color,
                trailLife = trails.enabled,
                trailLifeColor = trails.colorOverLifetime,
                trailTrailColor = trails.colorOverTrail,
            });
        }

        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            Material[] shared = renderer.sharedMaterials;
            for (int i = 0; i < shared.Length; i++)
            {
                if (shared[i] == null) continue;
                foreach (int property in ColorProps)
                {
                    if (!shared[i].HasProperty(property)) continue;
                    // 🔴 무채색(흰·회색 — HDR 흰 _Color 16 등)은 색상을 바꿔도 그대로라 **블록을 아예 걸지 않는다**(11-03).
                    //    같은 값이라도 MaterialPropertyBlock 을 걸면 이 파티클 셰이더들이 원래와 다르게 그려졌다
                    //    (원래 색과 같은 색상으로 칠해도 심이 더 밝고 글로우가 짙어지던 원인 — Play 측정: 블록 건 쪽만 밝았다).
                    Color original = shared[i].GetColor(property);
                    Color.RGBToHSV(original, out _, out float saturation, out _);
                    if (saturation < 0.001f) continue;
                    _materials.Add(new MaterialColor { renderer = renderer, materialIndex = i, property = property, original = original });
                }
            }
        }
    }

    void ApplyTint(in EffectTint tint)
    {
        if (!_captured) Capture();
        _applied = true;

        foreach (SystemColors c in _systems)
        {
            if (c.ps == null) continue;
            ParticleSystem.MainModule main = c.ps.main;
            main.startColor = Recolor(c.start, tint);
            if (c.overLifetime) { ParticleSystem.ColorOverLifetimeModule m = c.ps.colorOverLifetime; m.color = Recolor(c.life, tint); }
            if (c.bySpeed) { ParticleSystem.ColorBySpeedModule m = c.ps.colorBySpeed; m.color = Recolor(c.speed, tint); }
            if (c.trailLife)
            {
                ParticleSystem.TrailModule m = c.ps.trails;
                m.colorOverLifetime = Recolor(c.trailLifeColor, tint);
                m.colorOverTrail = Recolor(c.trailTrailColor, tint);
            }
        }

        _block ??= new MaterialPropertyBlock();
        _blocked.Clear();
        foreach (MaterialColor m in _materials)
        {
            if (m.renderer == null) continue;
            // 같은 슬롯의 여러 색 칸을 한 블록에 모은다 — 슬롯마다 한 번 읽고 칸을 다 채운 뒤 쓴다.
            if (_blocked.Add((m.renderer, m.materialIndex)))
            {
                _block.Clear();
                foreach (MaterialColor same in _materials)
                    if (same.renderer == m.renderer && same.materialIndex == m.materialIndex)
                        _block.SetColor(same.property, tint.Apply(same.original));
                m.renderer.SetPropertyBlock(_block, m.materialIndex);
            }
        }
    }

    /// <summary>원래 색으로 되돌린다(반납 직전). 칠한 적 없으면 아무 일도 안 한다.</summary>
    public void Restore()
    {
        if (!_applied) return;
        _applied = false;

        foreach (SystemColors c in _systems)
        {
            if (c.ps == null) continue;
            ParticleSystem.MainModule main = c.ps.main;
            main.startColor = c.start;
            if (c.overLifetime) { ParticleSystem.ColorOverLifetimeModule m = c.ps.colorOverLifetime; m.color = c.life; }
            if (c.bySpeed) { ParticleSystem.ColorBySpeedModule m = c.ps.colorBySpeed; m.color = c.speed; }
            if (c.trailLife)
            {
                ParticleSystem.TrailModule m = c.ps.trails;
                m.colorOverLifetime = c.trailLifeColor;
                m.colorOverTrail = c.trailTrailColor;
            }
        }

        _block ??= new MaterialPropertyBlock();
        _block.Clear();
        foreach (MaterialColor m in _materials)
            if (m.renderer != null) m.renderer.SetPropertyBlock(_block, m.materialIndex);   // 빈 블록 = 재질 값 그대로
    }

    static ParticleSystem.MinMaxGradient Recolor(ParticleSystem.MinMaxGradient g, in EffectTint tint)
    {
        switch (g.mode)
        {
            case ParticleSystemGradientMode.Color:
                return new ParticleSystem.MinMaxGradient(tint.Apply(g.color));
            case ParticleSystemGradientMode.TwoColors:
                return new ParticleSystem.MinMaxGradient(tint.Apply(g.colorMin), tint.Apply(g.colorMax));
            case ParticleSystemGradientMode.Gradient:
                return new ParticleSystem.MinMaxGradient(Recolor(g.gradient, tint));
            case ParticleSystemGradientMode.TwoGradients:
                return new ParticleSystem.MinMaxGradient(Recolor(g.gradientMin, tint), Recolor(g.gradientMax, tint));
            case ParticleSystemGradientMode.RandomColor:
                return new ParticleSystem.MinMaxGradient(Recolor(g.gradient, tint)) { mode = ParticleSystemGradientMode.RandomColor };
            default:
                return g;
        }
    }

    static Gradient Recolor(Gradient source, in EffectTint tint)
    {
        if (source == null) return null;
        GradientColorKey[] keys = source.colorKeys;
        for (int i = 0; i < keys.Length; i++) keys[i].color = tint.Apply(keys[i].color);
        // 🔴 colorSpace 도 복사한다(11-03) — 새 Gradient 는 Uninitialized 라 감마로 저작한 키를 선형 값처럼 읽어
        //    중간 밝기 키가 2~3배 밝아졌다(원래 색과 같은 색상으로 칠해도 심·글로우가 더 밝고 짙게 나오던 원인).
        var result = new Gradient { mode = source.mode, colorSpace = source.colorSpace };
        result.SetKeys(keys, source.alphaKeys);
        return result;
    }
}
