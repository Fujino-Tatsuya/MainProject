using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 할 일 238 — 거너 간파 적중 라이트 덮어쓰기(빛줄기·불꽃 숨김·포인트 라이트 색)와 NetworkManager 캐릭터 간파 VFX 칸(B1·B2·B3·C1·C2) 켬.
/// </summary>
public sealed class InterruptHitLightCasterOverrideTests
{
    private const string NetworkManagerPath = "Assets/2.Prefabs/Network/NetworkManager.prefab";
    private readonly List<Object> _objects = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (Object o in _objects)
            if (o != null)
                Object.DestroyImmediate(o);
        _objects.Clear();
    }

    [Test]
    public void 덮어쓰기_없음이면_아무것도_숨기지_않고_SO_색()
    {
        InterruptHitLightFxOverride none = default;
        Assert.IsFalse(none.Hides(InterruptHitLightFx.RaysName));
        Assert.AreEqual(Color.red, none.LightColorOr(Color.red));

        var disabled = new InterruptHitLightCasterOverride { enabled = false };
        Assert.IsFalse(InterruptHitLightCasterOverride.ToFx(disabled).Hides(InterruptHitLightFx.RaysName), "꺼진 칸 = 없음");
        Assert.IsFalse(InterruptHitLightCasterOverride.ToFx(null).HasLightColor);
    }

    [Test]
    public void 거너_기본값은_빛줄기_불꽃만_숨기고_파란_라이트()
    {
        InterruptHitLightFxOverride fx = InterruptHitLightCasterOverride.ToFx(new InterruptHitLightCasterOverride());
        Assert.IsTrue(fx.Hides(InterruptHitLightFx.RaysName));
        Assert.IsTrue(fx.Hides(InterruptHitLightFx.SparksName));
        Assert.IsFalse(fx.Hides(InterruptHitLightFx.StreakName));
        Assert.IsFalse(fx.Hides("Core"));
        Assert.IsFalse(fx.Hides(null));
        Color c = fx.LightColorOr(Color.white);
        Assert.Greater(c.b, c.r, "FX_Arrow_hail LightFlash 파랑");
    }

    [Test]
    public void 덮어쓰기로_재생하면_숨긴_파티클은_안_나오고_라이트는_덮어쓴_색()
    {
        InterruptHitLightFx fx = CreateFx();
        ParticleSystem rays = AddSystem(fx, InterruptHitLightFx.RaysName);
        ParticleSystem sparks = AddSystem(fx, InterruptHitLightFx.SparksName);
        ParticleSystem streak = AddSystem(fx, InterruptHitLightFx.StreakName);
        var look = ScriptableObject.CreateInstance<InterruptHitLightLook>();
        _objects.Add(look);

        var gunner = new InterruptHitLightCasterOverride();
        fx.Play(InterruptHitLightPlacement.At(Vector3.zero), look, InterruptHitLightCasterOverride.ToFx(gunner));
        Assert.IsFalse(rays.isPlaying, "빛줄기 숨김");
        Assert.IsFalse(sparks.isPlaying, "불꽃 숨김");
        Assert.IsTrue(streak.isPlaying, "가로 빛줄은 그대로");
        Assert.AreEqual(gunner.lightColor, fx.HitLight.color);

        // 다음 재생(다른 캐릭터 = 덮어쓰기 없음)은 다시 기본대로.
        fx.Play(InterruptHitLightPlacement.At(Vector3.zero), look);
        Assert.IsTrue(rays.isPlaying);
        Assert.IsTrue(sparks.isPlaying);
        Assert.AreEqual(look.LightColor, fx.HitLight.color);
    }

    // 사용자 결정(11-06): 팔라딘·어쌔신 간파 VFX 실전 적용 — 104 이후 꺼져 있던 칸을 프리팹 기본 켬으로.
    [TestCase("paladinShieldGlow")]
    [TestCase("paladinShieldCharge")]
    [TestCase("paladinWave")]
    [TestCase("assassinInsightAttempt")]
    [TestCase("assassinInsightSuccess")]
    public void NetworkManager_캐릭터_간파_VFX_칸은_켬(string field)
    {
        StringAssert.Contains("\n  " + field + ": 1\n", ReadNetworkManager());
    }

    [Test]
    public void NetworkManager_거너_적중_라이트_칸은_빛줄기_불꽃_숨김()
    {
        string text = ReadNetworkManager();
        int at = text.IndexOf("\n  gunnerHitLight:\n", System.StringComparison.Ordinal);
        Assert.GreaterOrEqual(at, 0);
        string block = text.Substring(at, System.Math.Min(260, text.Length - at));
        StringAssert.Contains("enabled: 1", block);
        StringAssert.Contains("- Rays", block);
        StringAssert.Contains("- Sparks", block);
        StringAssert.Contains("overrideLightColor: 1", block);
    }

    private static string ReadNetworkManager() => File.ReadAllText(NetworkManagerPath).Replace("\r\n", "\n");

    private InterruptHitLightFx CreateFx()
    {
        var go = new GameObject("FX_InterruptHitLight");
        _objects.Add(go);
        return go.AddComponent<InterruptHitLightFx>();
    }

    private static ParticleSystem AddSystem(InterruptHitLightFx fx, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(fx.transform, false);
        ParticleSystem system = go.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = system.main;
        main.playOnAwake = false;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        system.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 6) });
        return system;
    }
}
