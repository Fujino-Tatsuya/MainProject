using NUnit.Framework;
using UnityEngine;

/// <summary>할 일 278 햅틱 — 겹침(센 쪽)·감쇠·종료·최소 간격·오너 전용·피해 배율.</summary>
public sealed class GamepadHapticsTests
{
    const float Eps = 1e-4f;

    static GamepadHapticPreset Preset(float low, float high, float duration, float decay = 1f, float minInterval = 0f)
        => new GamepadHapticPreset(low, high, duration, decay, minInterval);

    [Test]
    public void Overlap_StrongerVoiceWinsPerMotor_NotSum()
    {
        var mixer = new GamepadHapticsMixer();
        mixer.Add(0, Preset(0.2f, 0.8f, 1f, 0f), 1f, 0f);
        mixer.Add(1, Preset(0.6f, 0.3f, 1f, 0f), 1f, 0f);
        Assert.IsTrue(mixer.Evaluate(0.5f, out float low, out float high));
        Assert.AreEqual(0.6f, low, Eps);   // 모터마다 최대값
        Assert.AreEqual(0.8f, high, Eps);
    }

    [Test]
    public void Decay_LinearHalfwayIsHalf_AndStopsAfterDuration()
    {
        var mixer = new GamepadHapticsMixer();
        mixer.Add(0, Preset(1f, 0.5f, 0.4f, 1f), 1f, 10f);
        mixer.Evaluate(10f, out float low0, out float high0);
        Assert.AreEqual(1f, low0, Eps);
        Assert.AreEqual(0.5f, high0, Eps);

        mixer.Evaluate(10.2f, out float lowMid, out float highMid);
        Assert.AreEqual(0.5f, lowMid, Eps);
        Assert.AreEqual(0.25f, highMid, Eps);

        Assert.IsFalse(mixer.Evaluate(10.41f, out float lowEnd, out float highEnd)); // 길이 끝 지남 = 소리 지워짐(정확히 10.4 는 float 오차)
        Assert.AreEqual(0f, lowEnd, Eps);
        Assert.AreEqual(0f, highEnd, Eps);
        Assert.AreEqual(0, mixer.ActiveCount);
    }

    [Test]
    public void Envelope_PowerAndCurve()
    {
        Assert.AreEqual(1f, GamepadHapticsRules.Envelope(0.7f, 0f, null), Eps);        // 0 = 끝까지 일정
        Assert.AreEqual(0.25f, GamepadHapticsRules.Envelope(0.5f, 2f, null), Eps);    // (1-0.5)^2
        Assert.AreEqual(0f, GamepadHapticsRules.Envelope(1f, 1f, null), Eps);         // 끝
        Assert.AreEqual(0.3f, GamepadHapticsRules.Envelope(0.5f, 1f, AnimationCurve.Constant(0f, 1f, 0.3f)), Eps); // 곡선 우선
        Assert.AreEqual(0.5f, GamepadHapticsRules.Envelope(0.5f, 1f, new AnimationCurve()), Eps); // 빈 곡선 = 지수
    }

    [Test]
    public void Scale_ClampedToOne_AndZeroScaleIgnored()
    {
        var mixer = new GamepadHapticsMixer();
        Assert.IsFalse(mixer.Add(0, Preset(0.5f, 0.5f, 1f, 0f), 0f, 0f));
        Assert.IsTrue(mixer.Add(0, Preset(0.8f, 0.6f, 1f, 0f), 2f, 0f));
        mixer.Evaluate(0.1f, out float low, out float high);
        Assert.AreEqual(1f, low, Eps);
        Assert.AreEqual(1f, high, Eps);
    }

    [Test]
    public void MinInterval_SameEventIgnoredInsideWindow_OtherEventNot()
    {
        var mixer = new GamepadHapticsMixer();
        GamepadHapticPreset hit = Preset(0.1f, 0.2f, 0.05f, 1f, 0.08f);
        Assert.IsTrue(mixer.Add(6, hit, 1f, 1f));
        Assert.IsFalse(mixer.Add(6, hit, 1f, 1.05f));   // 다단 히트 — 0.08초 안
        Assert.IsTrue(mixer.Add(7, hit, 1f, 1.05f));    // 다른 사건은 따로 센다
        Assert.IsTrue(mixer.Add(6, hit, 1f, 1.09f));
    }

    [Test]
    public void DisabledOrEmptyPreset_NotPlayed()
    {
        var mixer = new GamepadHapticsMixer();
        GamepadHapticPreset off = Preset(1f, 1f, 1f);
        off.enabled = false;
        Assert.IsFalse(mixer.Add(0, off, 1f, 0f));
        Assert.IsFalse(mixer.Add(0, Preset(0f, 0f, 1f), 1f, 0f));
        Assert.IsFalse(mixer.Add(0, Preset(1f, 1f, 0f), 1f, 0f));
    }

    [Test]
    public void Clear_StopsEverything()
    {
        var mixer = new GamepadHapticsMixer();
        mixer.Add(0, Preset(1f, 1f, 5f), 1f, 0f);
        mixer.Clear();
        Assert.IsFalse(mixer.Evaluate(0.1f, out float low, out float high));
        Assert.AreEqual(0f, low);
        Assert.AreEqual(0f, high);
    }

    [Test]
    public void ShouldPlay_OnlyGamepad_LocalOwner_Focused_NotPaused_Enabled()
    {
        Assert.IsTrue(GamepadHapticsRules.ShouldPlay(true, true, 1f, true, true, false));
        Assert.IsFalse(GamepadHapticsRules.ShouldPlay(false, true, 1f, true, true, false), "마우스 모드");
        Assert.IsFalse(GamepadHapticsRules.ShouldPlay(true, false, 1f, true, true, false), "에셋 끔");
        Assert.IsFalse(GamepadHapticsRules.ShouldPlay(true, true, 0f, true, true, false), "사용자 0%");
        Assert.IsFalse(GamepadHapticsRules.ShouldPlay(true, true, 1f, false, true, false), "남의 플레이어 사건");
        Assert.IsFalse(GamepadHapticsRules.ShouldPlay(true, true, 1f, true, false, false), "포커스 잃음");
        Assert.IsFalse(GamepadHapticsRules.ShouldPlay(true, true, 1f, true, true, true), "일시정지");
    }

    [Test]
    public void DamageScale_ProportionalWithFloor()
    {
        Assert.AreEqual(0f, GamepadHapticsRules.DamageScale(0, 100, 0.25f, 0.3f), Eps);
        Assert.AreEqual(0.3f, GamepadHapticsRules.DamageScale(1, 100, 0.25f, 0.3f), Eps);   // 최소
        Assert.AreEqual(0.6f, GamepadHapticsRules.DamageScale(15, 100, 0.25f, 0.3f), Eps);  // 15 / 25
        Assert.AreEqual(1f, GamepadHapticsRules.DamageScale(80, 100, 0.25f, 0.3f), Eps);    // 상한
        Assert.AreEqual(1f, GamepadHapticsRules.DamageScale(5, 0, 0.25f, 0.3f), Eps);       // 최대 체력 모름
    }

    [Test]
    public void DefaultSettings_InterruptTiersAscend_AndAllPlayable()
    {
        var s = ScriptableObject.CreateInstance<GamepadHapticsSettings>();
        try
        {
            GamepadHapticPreset n = s.Get(GamepadHapticEvent.InterruptSuccessNormal);
            GamepadHapticPreset d = s.Get(GamepadHapticEvent.InterruptSuccessDash);
            GamepadHapticPreset p = s.Get(GamepadHapticEvent.InterruptSuccessSuppress);
            Assert.Less(n.high, d.high);
            Assert.Less(d.high, p.high);
            Assert.Less(n.low, d.low);
            Assert.Less(d.low, p.low);
            foreach (GamepadHapticEvent e in System.Enum.GetValues(typeof(GamepadHapticEvent)))
                Assert.IsTrue(s.Get(e).IsPlayable, e.ToString());
        }
        finally
        {
            Object.DestroyImmediate(s);
        }
    }

    [Test]
    public void ResourceAsset_LoadsFromResources()
    {
        Assert.IsNotNull(Resources.Load<GamepadHapticsSettings>(GamepadHapticsSettings.ResourcePath));
    }
}
