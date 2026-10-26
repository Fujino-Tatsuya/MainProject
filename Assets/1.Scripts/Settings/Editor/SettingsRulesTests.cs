using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 설정창 순수 규칙(PLAN-settings.md) — 해상도 후보·기본값·폴백, 프레임 제한 정리, 창 모드 매핑, 데미지 숫자 끄기.
/// PlayerPrefs 를 건드리지 않는 함수만 검사한다(테스트가 실제 저장값을 오염시키지 않게).
/// </summary>
public sealed class SettingsRulesTests
{
    static readonly Vector2Int Native4K = new Vector2Int(3840, 2160);
    static readonly Vector2Int Native1440 = new Vector2Int(2560, 1440);
    static readonly Vector2Int Native1080 = new Vector2Int(1920, 1080);
    static readonly Vector2Int NativeLaptop = new Vector2Int(1366, 768);

    [Test]
    public void Candidates_HideLargerThanMonitor_S13()
    {
        CollectionAssert.AreEqual(
            new[] { ResolutionChoice.Recommended2560x1440, ResolutionChoice.Recommended1920x1080, ResolutionChoice.MonitorNative },
            DisplayConfig.Candidates(Native4K));
        CollectionAssert.AreEqual(
            new[] { ResolutionChoice.Recommended1920x1080, ResolutionChoice.MonitorNative },
            DisplayConfig.Candidates(Native1080));
        CollectionAssert.AreEqual(new[] { ResolutionChoice.MonitorNative }, DisplayConfig.Candidates(NativeLaptop));
    }

    [Test]
    public void DefaultResolution_Is1440_UnlessMonitorSmaller_S3_S13()
    {
        Assert.AreEqual(ResolutionChoice.Recommended2560x1440, DisplayConfig.DefaultResolution(Native4K));
        Assert.AreEqual(ResolutionChoice.Recommended2560x1440, DisplayConfig.DefaultResolution(Native1440));
        Assert.AreEqual(ResolutionChoice.MonitorNative, DisplayConfig.DefaultResolution(Native1080));
    }

    [Test]
    public void Resolve_FallsBackToNative_WhenSavedChoiceNoLongerFits()
    {
        Assert.AreEqual(new Vector2Int(2560, 1440), DisplayConfig.Resolve(ResolutionChoice.Recommended2560x1440, Native4K));
        Assert.AreEqual(new Vector2Int(1920, 1080), DisplayConfig.Resolve(ResolutionChoice.Recommended1920x1080, Native4K));
        Assert.AreEqual(Native4K, DisplayConfig.Resolve(ResolutionChoice.MonitorNative, Native4K));
        // 1440 을 저장해 둔 채 1080 모니터로 바꾸면 모니터 기본으로 내린다.
        Assert.AreEqual(Native1080, DisplayConfig.Resolve(ResolutionChoice.Recommended2560x1440, Native1080));
    }

    [Test]
    public void FrameLimit_UnknownValue_FallsBackTo144_S6()
    {
        Assert.AreEqual(60, DisplayConfig.SanitizeFrameLimit(60));
        Assert.AreEqual(0, DisplayConfig.SanitizeFrameLimit(0));
        Assert.AreEqual(144, DisplayConfig.SanitizeFrameLimit(75));
        Assert.AreEqual(144, DisplayConfig.SanitizeFrameLimit(-1));
    }

    [Test]
    public void WindowMode_MapsToUnity_S4()
    {
        Assert.AreEqual(FullScreenMode.ExclusiveFullScreen, DisplayConfig.ToUnity(WindowModeChoice.Fullscreen));
        Assert.AreEqual(FullScreenMode.FullScreenWindow, DisplayConfig.ToUnity(WindowModeChoice.Borderless));
        Assert.AreEqual(FullScreenMode.Windowed, DisplayConfig.ToUnity(WindowModeChoice.Windowed));
    }

    [Test]
    public void ChangesScreen_OnlyResolutionOrWindowMode_S5()
    {
        var a = new DisplaySelection { resolution = ResolutionChoice.Recommended2560x1440, windowMode = WindowModeChoice.Borderless, frameLimit = 144 };
        var fps = a; fps.frameLimit = 60; fps.vSync = true;
        var res = a; res.resolution = ResolutionChoice.Recommended1920x1080;
        var win = a; win.windowMode = WindowModeChoice.Windowed;
        Assert.IsFalse(DisplayConfig.ChangesScreen(a, fps));
        Assert.IsTrue(DisplayConfig.ChangesScreen(a, res));
        Assert.IsTrue(DisplayConfig.ChangesScreen(a, win));
    }

    [Test]
    public void DamageNumbers_Off_HidesEverything_S8()
    {
        Assert.IsFalse(FloatingDamageDisplayPolicy.Evaluate(FloatingDamageDisplayFilter.Off, true, 0.75f, 0.8f).shouldDisplay);
        Assert.IsFalse(FloatingDamageDisplayPolicy.Evaluate(FloatingDamageDisplayFilter.Off, false, 0.75f, 0.8f).shouldDisplay);
        // 기존 3종은 그대로.
        Assert.IsTrue(FloatingDamageDisplayPolicy.Evaluate(FloatingDamageDisplayFilter.OwnDealtOnly, true, 0.75f, 0.8f).shouldDisplay);
        Assert.IsFalse(FloatingDamageDisplayPolicy.Evaluate(FloatingDamageDisplayFilter.OwnDealtOnly, false, 0.75f, 0.8f).shouldDisplay);
    }

    [Test]
    public void GraphicsPreset_HighIsCurrentLook_AndLowerTiersStepDown()
    {
        GraphicsQuality d = GraphicsQualityConfig.Defaults();
        Assert.AreEqual(GraphicsPreset.High, d.preset);
        Assert.AreEqual(1f, d.renderScale);
        Assert.AreEqual(ShadowSetting.High, d.shadows);
        Assert.AreEqual(AntiAliasingSetting.Smaa, d.antiAliasing);   // 맵 카메라(MainCamera.prefab) 원래 SMAA
        Assert.IsTrue(d.postEffects);

        GraphicsQuality low = GraphicsQualityConfig.PresetValues(GraphicsPreset.Low, d);
        Assert.Less(low.renderScale, GraphicsQualityConfig.PresetValues(GraphicsPreset.Medium, d).renderScale);
        Assert.IsFalse(low.postEffects);
    }

    [Test]
    public void GraphicsPreset_ChangingOneItem_BecomesCustom()
    {
        GraphicsQuality q = GraphicsQualityConfig.Defaults();
        Assert.AreEqual(GraphicsPreset.High, GraphicsQualityConfig.Classify(q));
        q.shadows = ShadowSetting.Low;
        Assert.AreEqual(GraphicsPreset.Custom, GraphicsQualityConfig.Classify(q));
        // 비네트는 프리셋과 무관 — 바꿔도 프리셋이 유지된다.
        GraphicsQuality v = GraphicsQualityConfig.Defaults();
        v.vignetteSize = 1.3f;
        Assert.AreEqual(GraphicsPreset.High, GraphicsQualityConfig.Classify(v));
    }

    [Test]
    public void GraphicsSanitize_ClampsRanges()
    {
        var q = GraphicsQualityConfig.Defaults();
        q.renderScale = 0.1f; q.vignetteSize = 9f; q.vignetteFade = 0f;
        q = GraphicsQualityConfig.Sanitize(q);
        Assert.AreEqual(GraphicsQualityConfig.MinRenderScale, q.renderScale);
        Assert.AreEqual(GraphicsQualityConfig.MaxVignetteSize, q.vignetteSize);
        Assert.AreEqual(GraphicsQualityConfig.MinVignetteFade, q.vignetteFade);
        Assert.AreEqual(UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing,
            GraphicsQualityConfig.CameraMode(AntiAliasingSetting.Smaa));
        Assert.AreEqual(UnityEngine.Rendering.Universal.AntialiasingMode.FastApproximateAntialiasing,
            GraphicsQualityConfig.CameraMode(AntiAliasingSetting.Fxaa));
        Assert.AreEqual(UnityEngine.Rendering.Universal.AntialiasingMode.None,
            GraphicsQualityConfig.CameraMode(AntiAliasingSetting.Off));
    }

    [Test]
    public void DamageFilterEnum_KeepsSerializedOrder()
    {
        // SO 에 정수로 저장된다 — 값이 밀리면 저작값이 조용히 바뀐다.
        Assert.AreEqual(0, (int)FloatingDamageDisplayFilter.AllDamage);
        Assert.AreEqual(1, (int)FloatingDamageDisplayFilter.OwnDealtOnly);
        Assert.AreEqual(2, (int)FloatingDamageDisplayFilter.AllWithOwnEmphasis);
        Assert.AreEqual(3, (int)FloatingDamageDisplayFilter.Off);
    }

    // ─── 언어(S15·S16) — 로컬라이징(Loc·UserLanguageConfig)으로 통합(할 일 38) ─────────

    [Test]
    public void Language_DefaultFollowsSystem_NextToggles_NamesNotTranslated_S16()
    {
        Assert.AreEqual(GameLanguage.Ko, UserLanguageConfig.DefaultFor(SystemLanguage.Korean));
        Assert.AreEqual(GameLanguage.En, UserLanguageConfig.DefaultFor(SystemLanguage.English));
        Assert.AreEqual(GameLanguage.En, UserLanguageConfig.DefaultFor(SystemLanguage.Japanese));
        Assert.AreEqual(GameLanguage.En, LocText.Next(GameLanguage.Ko));
        Assert.AreEqual(GameLanguage.Ko, LocText.Next(GameLanguage.En));
        Assert.AreEqual("한국어", LocText.NativeName(GameLanguage.Ko));
        Assert.AreEqual("English", LocText.NativeName(GameLanguage.En));
    }

    [Test]
    public void Strings_58Keys_UnderSettingsPrefix_EnglishUniqueAndFilled()
    {
        Assert.AreEqual(58, SettingsUiStrings.All.Count);   // +1 틀 제목(PLAN-settings-art)
        var english = new System.Collections.Generic.HashSet<string>();
        foreach (UiString s in SettingsUiStrings.All)
        {
            StringAssert.StartsWith(SettingsUiStrings.KeyPrefix, s.Key);
            Assert.IsTrue(SettingsUiStrings.AuthoredEnglish.TryGetValue(s.Key, out string en), s.Key);
            Assert.IsFalse(string.IsNullOrWhiteSpace(en), $"영어 비어 있음: {s.Key}");
            // 영어 원문이 겹치면 프리팹 라벨 역조회(FindKeyByLabel)가 엉뚱한 키를 붙인다.
            Assert.IsTrue(english.Add(en), $"영어 원문 중복: {en}");
            Assert.IsTrue(LocText.SamePlaceholders(s.Ko, en), $"자리표시자 다름: {s.Key}");
        }
    }

    [Test]
    public void Strings_LookupByLabel_EnglishThenUniqueKo()
    {
        Assert.AreEqual(SettingsUiStrings.GraphicsResolution.Key, SettingsUiStrings.FindKeyByLabel("Resolution"));
        Assert.AreEqual(SettingsUiStrings.GraphicsResolution.Key, SettingsUiStrings.FindKeyByLabel("해상도"));
        Assert.IsNull(SettingsUiStrings.FindKeyByLabel("100%"));
        Assert.IsNull(SettingsUiStrings.FindKeyByLabel("전체"));   // ko 가 두 키(데미지 숫자 전체·오디오 전체)에 있다
        Assert.AreEqual(SettingsUiStrings.AudioMaster.Key, SettingsUiStrings.FindKeyByLabel("Master"));
    }

    [Test]
    public void Strings_ActionLabel_UsesControlsKey_ElseActionName()
    {
        Assert.IsTrue(SettingsUiStrings.TryGetAction("SkillMain", out UiString main));
        Assert.AreEqual("UI.Settings.Controls.SkillMain", main.Key);
        Assert.IsFalse(SettingsUiStrings.TryGetAction("NoSuchAction", out _));
        Assert.AreEqual("NoSuchAction", KeyRebindRow.ActionLabel("Player/NoSuchAction"));
    }

    // 비네트 슬라이더 0~100(팀장 10-13): 50 = 기본(배율 1 = 지금 화면), 0 = 최소, 100 = 최대, 왕복 일치.
    [Test]
    public void VignetteSlider_MidIsDefault_EndsAreLimits_RoundTrips()
    {
        Assert.AreEqual(1f, GraphicsQualityConfig.VignetteSizeFromSlider(0.5f), 1e-5f);
        Assert.AreEqual(1f, GraphicsQualityConfig.VignetteFadeFromSlider(0.5f), 1e-5f);
        Assert.AreEqual(GraphicsQualityConfig.MinVignetteSize, GraphicsQualityConfig.VignetteSizeFromSlider(0f), 1e-5f);
        Assert.AreEqual(GraphicsQualityConfig.MaxVignetteSize, GraphicsQualityConfig.VignetteSizeFromSlider(1f), 1e-5f);
        Assert.AreEqual(GraphicsQualityConfig.MinVignetteFade, GraphicsQualityConfig.VignetteFadeFromSlider(0f), 1e-5f);
        Assert.AreEqual(GraphicsQualityConfig.MaxVignetteFade, GraphicsQualityConfig.VignetteFadeFromSlider(1f), 1e-5f);
        foreach (float s in new[] { 0f, 0.13f, 0.5f, 0.77f, 1f })
        {
            Assert.AreEqual(s, GraphicsQualityConfig.VignetteSizeToSlider(GraphicsQualityConfig.VignetteSizeFromSlider(s)), 1e-4f);
            Assert.AreEqual(s, GraphicsQualityConfig.VignetteFadeToSlider(GraphicsQualityConfig.VignetteFadeFromSlider(s)), 1e-4f);
        }
        Assert.AreEqual(0.5f, GraphicsQualityConfig.VignetteSizeToSlider(1f), 1e-5f);   // 기존 저장값 1 → 50 으로 보인다
    }

    [Test]
    public void PerformanceDisplay_KeepsDropdownOrder_S17()
    {
        Assert.AreEqual(0, (int)PerformanceDisplay.Off);
        Assert.AreEqual(1, (int)PerformanceDisplay.Fps);
        Assert.AreEqual(2, (int)PerformanceDisplay.FpsAndPing);
    }
}
