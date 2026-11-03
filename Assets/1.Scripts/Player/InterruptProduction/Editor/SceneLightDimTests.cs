using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// 씬 조명 배율 공유(할 일 194 — 암순응 나 × 23호 차징 태양광 끄기) — 배율 곱, 원래 값 한 번 저장·복구, 누가 먼저 끝나도 원래 값.
/// </summary>
public sealed class SceneLightDimTests
{
    private sealed class FakeTarget : ISceneLightTarget
    {
        public Light Sun;
        public Light FindSun() => Sun;
        public SphericalHarmonicsL2 AmbientProbe { get; set; }
    }

    private readonly List<Object> _objects = new List<Object>();
    private FakeTarget _target;
    private SceneLightDim _dim;
    private readonly object _boss = new object();
    private readonly object _interrupt = new object();

    [SetUp]
    public void SetUp()
    {
        var go = new GameObject("Sun");
        _objects.Add(go);
        Light sun = go.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 2f;
        var probe = new SphericalHarmonicsL2();
        probe.AddAmbientLight(Color.white);
        _target = new FakeTarget { Sun = sun, AmbientProbe = probe };
        _dim = new SceneLightDim(_target);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Object o in _objects)
            if (o != null)
                Object.DestroyImmediate(o);
        _objects.Clear();
    }

    private float AmbientR => _target.AmbientProbe[0, 0];

    [Test]
    public void 배율은_원래_값에_곱하고_1이면_원래로()
    {
        _dim.SetSun(_interrupt, 0.5f);
        Assert.AreEqual(1f, _target.Sun.intensity, 1e-5f);

        _dim.SetSun(_interrupt, 1f);
        Assert.AreEqual(2f, _target.Sun.intensity, 1e-5f);
        Assert.IsTrue(_target.Sun.enabled);
    }

    [Test]
    public void 두_연출은_곱으로_겹치고_0이면_태양을_끈다()
    {
        _dim.SetSun(_boss, 0.5f);
        _dim.SetSun(_interrupt, 0.5f);
        Assert.AreEqual(0.5f, _target.Sun.intensity, 1e-5f);

        _dim.SetSun(_boss, 0f);
        Assert.IsFalse(_target.Sun.enabled);
    }

    [Test]
    public void 차징_중_간파_뒤_차징이_먼저_끝나도_원래_밝기로()
    {
        _dim.SetSun(_boss, 0.2f);       // 차징 — 어두워짐
        _dim.SetSun(_interrupt, 0.5f);  // 그 사이 간파 적중
        _dim.SetSun(_boss, 1f);         // 차징 끝
        Assert.AreEqual(1f, _target.Sun.intensity, 1e-5f, "간파 배율만 남는다(어두운 값을 원래 값으로 잘못 저장하지 않음)");

        _dim.SetSun(_interrupt, 1f);
        Assert.AreEqual(2f, _target.Sun.intensity, 1e-5f);
    }

    [Test]
    public void 간파_중_차징이_시작돼도_원래_밝기로()
    {
        _dim.SetSun(_interrupt, 0.5f);
        _dim.SetSun(_boss, 0.5f);
        _dim.SetSun(_interrupt, 1f);
        Assert.AreEqual(1f, _target.Sun.intensity, 1e-5f);

        _dim.SetSun(_boss, 1f);
        Assert.AreEqual(2f, _target.Sun.intensity, 1e-5f);
    }

    [Test]
    public void 환경광도_배율_곱_후_원래로()
    {
        float original = AmbientR;
        _dim.SetAmbient(_interrupt, 0.25f);
        Assert.AreEqual(original * 0.25f, AmbientR, 1e-5f);

        _dim.Release(_interrupt);
        Assert.AreEqual(original, AmbientR, 1e-5f);
        Assert.AreEqual(1f, _dim.AmbientFactor);
        Assert.AreEqual(1f, _dim.SunFactor);
    }

    [Test]
    public void 태양이_없으면_아무것도_안_한다()
    {
        _target.Sun = null;
        Assert.DoesNotThrow(() => _dim.SetSun(_interrupt, 0.5f));
        Assert.DoesNotThrow(() => _dim.SetSun(_interrupt, 1f));
    }

    [Test]
    public void Forget_은_되돌리지_않고_저장값을_버린다()
    {
        _dim.SetSun(_interrupt, 0.5f);
        _dim.Forget();
        Assert.AreEqual(1f, _dim.SunFactor);
        Assert.AreEqual(1f, _target.Sun.intensity, 1e-5f, "새 씬 값으로 친다");

        _dim.SetSun(_interrupt, 0.5f);
        Assert.AreEqual(0.5f, _target.Sun.intensity, 1e-5f, "다시 저장한 값 기준");
    }
}
