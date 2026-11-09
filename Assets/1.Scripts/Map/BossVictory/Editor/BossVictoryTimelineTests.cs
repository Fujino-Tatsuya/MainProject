using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 타임라인(PLAN-boss-victory-glitch "타임라인 에디터 창") — 클립 평가·정지 유지·TV 꺼짐·마커,
/// 기본 클립(코드·에셋)이 옛 기본 곡선(교체 직전 에셋 값)과 같은 화면인지.
/// 실행 = <c>Tools/Tests/전투 EditMode 테스트 실행</c>(필터 ^BossVictoryTimeline).
/// </summary>
public sealed class BossVictoryTimelineTests
{
    const string AssetPath = "Assets/Resources/BossVictory/BossVictoryGlitchSettings.asset";
    const float SampleEnd = 5.6f;   // 옛 연출 끝(3.5 + 유지 1 + CRT 꺼짐 1.1)
    const float Tolerance = 1e-3f;

    // 교체 직전 에셋 곡선 키(시각, 값) — 기울기 0(옛 Ramp/Pulses 기본값).
    static readonly (BossVictoryTrack track, float[] keys)[] OldCurves =
    {
        (BossVictoryTrack.Master, new[] { 0f, 0f, 0.08f, 0f, 0.15f, 1f, 10f, 1f }),
        (BossVictoryTrack.Desaturate, new[] { 0f, 0f, 0.1f, 0f, 0.2f, 1f, 10f, 1f }),
        (BossVictoryTrack.Amber, new[] { 0f, 0f, 0.3f, 0f, 0.45f, 1f, 10f, 1f }),
        (BossVictoryTrack.Scanline, new[] { 0f, 0f, 0.1f, 0f, 0.2f, 1f, 10f, 1f }),
        (BossVictoryTrack.BossNoise, new[] { 0f, 0f, 0.12f, 0f, 0.17f, 1f, 10f, 1f }),
        (BossVictoryTrack.HudHide, new[] { 0f, 0f, 0.08f, 0f, 0.12f, 1f, 10f, 1f }),
        (BossVictoryTrack.Tear, new[]
        {
            0f, 0f, 0.08f, 0f, 0.2f, 1f, 0.25f, 0f, 0.32f, 0f, 0.33f, 1f, 0.41f, 0f,
            1.42f, 0f, 1.52f, 1f, 1.61f, 0f, 1.62f, 0f, 1.67f, 1f, 1.73f, 0f, 2.43f, 0f, 2.55f, 1f, 2.67f, 0f,
        }),
        (BossVictoryTrack.Smear, new[]
        {
            0f, 0f, 0.08f, 0f, 0.2f, 1f, 0.25f, 0f, 0.32f, 0f, 0.33f, 1f, 0.41f, 0f,
            1.42f, 0f, 1.52f, 1f, 1.62f, 0f, 2.43f, 0f, 2.55f, 1f, 2.67f, 0f,
        }),
    };

    static AnimationCurve Curve(float[] kv)
    {
        var keys = new Keyframe[kv.Length / 2];
        for (int i = 0; i < keys.Length; i++) keys[i] = new Keyframe(kv[i * 2], kv[i * 2 + 1], 0f, 0f);
        return new AnimationCurve(keys);
    }

    static void AssertSameAsOldCurves(BossVictoryTimeline tl, string label)
    {
        foreach ((BossVictoryTrack track, float[] keys) in OldCurves)
        {
            AnimationCurve old = Curve(keys);
            for (float t = 0f; t <= SampleEnd + 1e-4f; t += 0.0025f)
                Assert.AreEqual(old.Evaluate(t), tl.Evaluate(track, tl.EffectTime(t)), Tolerance, $"{label} {track} t={t:0.0000}");
        }
    }

    [Test]
    public void 기본_클립은_옛_기본_곡선과_같은_화면()
    {
        AssertSameAsOldCurves(BossVictoryTimeline.CreateDefault(), "코드 기본값");
    }

    [Test]
    public void 에셋_클립도_옛_기본_곡선과_같은_화면()
    {
        var settings = AssetDatabase.LoadAssetAtPath<BossVictoryGlitchSettings>(AssetPath);
        Assert.IsNotNull(settings, AssetPath);
        AssertSameAsOldCurves(settings.timeline, "에셋");
    }

    [Test]
    public void 기본_정지_유지_TV_꺼짐_마커()
    {
        BossVictoryTimeline tl = BossVictoryTimeline.CreateDefault();
        Assert.AreEqual(3.5f, tl.EffectTime(3.5f), 1e-5f);
        Assert.AreEqual(3.5f, tl.EffectTime(4.2f), 1e-5f, "정지 유지 안 = 시작에 고정");
        Assert.AreEqual(4.5f, tl.EffectTime(4.5f), 1e-5f, "끝 뒤는 그대로");
        Assert.AreEqual(1f, tl.EffectTime(1f), 1e-5f);

        Assert.IsTrue(tl.TryGetCrtOff(out BossVictoryClip crt));
        Assert.AreEqual(4.5f, crt.start, 1e-5f);
        Assert.AreEqual(1.1f, crt.length, 1e-5f);

        Assert.AreEqual(0.2f, tl.focusKillerTime, 1e-5f, "카메라 전환 = 첫 찢김 최고점");
        Assert.AreEqual(0.2f, tl.hideOthersTime, 1e-5f);
        Assert.AreEqual(5.6f, tl.lobbyTransitionTime, 1e-5f, "로비 전환 = TV 꺼짐 끝");
        Assert.AreEqual(1f, tl.Evaluate(BossVictoryTrack.Tear, tl.focusKillerTime), 1e-4f, "마커 순간 찢김 최고");
        Assert.AreEqual(5.6f, tl.Duration, 1e-4f);
    }

    [Test]
    public void 클립_페이드_세기_겹침()
    {
        BossVictoryClip c = BossVictoryClip.Make(1f, 2f, 0.5f, 0.5f, 0.8f);
        Assert.AreEqual(0f, c.Evaluate(0.99f));
        Assert.AreEqual(0f, c.Evaluate(3.01f));
        Assert.AreEqual(0.4f, c.Evaluate(1.25f), 1e-5f, "페이드 인 가운데 = smoothstep 0.5");
        Assert.AreEqual(0.8f, c.Evaluate(2f), 1e-5f);
        Assert.AreEqual(0.4f, c.Evaluate(2.75f), 1e-5f);
        Assert.AreEqual(0f, BossVictoryClip.Make(1f, 0f).Evaluate(1f), "길이 0 = 없음");

        var tl = new BossVictoryTimeline();
        tl.tear.Add(BossVictoryClip.Make(0f, 1f, 0f, 0f, 0.3f));
        tl.tear.Add(BossVictoryClip.Make(0.5f, 1f, 0f, 0f, 0.6f));
        Assert.AreEqual(0.6f, tl.Evaluate(BossVictoryTrack.Tear, 0.75f), 1e-5f, "겹치면 큰 값");
        Assert.AreEqual(0.3f, tl.Evaluate(BossVictoryTrack.Tear, 0.25f), 1e-5f);
        Assert.IsFalse(tl.TryGetCrtOff(out _), "TV 꺼짐 클립 없으면 false");
    }

    [Test]
    public void 마커_시각_설정은_음수를_막는다()
    {
        var tl = new BossVictoryTimeline();
        tl.SetMarkerTime(BossVictoryMarker.LobbyTransition, -1f);
        Assert.AreEqual(0f, tl.MarkerTime(BossVictoryMarker.LobbyTransition));
        tl.SetMarkerTime(BossVictoryMarker.FocusKiller, 1.23f);
        Assert.AreEqual(1.23f, tl.focusKillerTime, 1e-6f);
    }
}
