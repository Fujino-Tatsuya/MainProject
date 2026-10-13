using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 설정창 그래픽 탭의 품질 항목(팀장 10-12) — 프리셋(하/중/상/사용자 지정)·렌더 스케일·그림자·안티앨리어싱·후처리·시야 디밍 크기/경계.
/// 값 = <see cref="GraphicsQualityConfig"/>. 모두 즉시 적용(화면이 꺼질 일이 없어 확인 창 없음).
/// 프리셋을 고르면 렌더 스케일·그림자·AA·후처리가 한꺼번에 바뀌고, 그중 하나를 따로 바꾸면 "Custom" 으로 표시된다.
/// 해상도·창 모드·프레임은 같은 탭의 <see cref="GraphicsSettingsController"/>.
/// </summary>
public sealed class GraphicsQualityController : MonoBehaviour
{
    [SerializeField] TMP_Dropdown presetDropdown;
    [SerializeField] Slider renderScaleSlider;
    [SerializeField] TMP_Text renderScaleValue;
    [SerializeField] TMP_Dropdown shadowDropdown;
    [SerializeField] TMP_Dropdown antiAliasingDropdown;
    [SerializeField] Toggle postEffectsToggle;
    [SerializeField] Slider vignetteSizeSlider;
    [SerializeField] TMP_Text vignetteSizeValue;
    [SerializeField] Slider vignetteFadeSlider;
    [SerializeField] TMP_Text vignetteFadeValue;

    bool _refreshing;

    void Awake()
    {
        Range(renderScaleSlider, GraphicsQualityConfig.MinRenderScale, 1f);
        // 비네트는 화면에 0~100(50 = 기본)으로 보인다 — 슬라이더는 0~1, 배율 변환은 GraphicsQualityConfig(팀장 10-13).
        Range(vignetteSizeSlider, 0f, 1f);
        Range(vignetteFadeSlider, 0f, 1f);

        if (presetDropdown != null) presetDropdown.onValueChanged.AddListener(OnPreset);
        if (renderScaleSlider != null) renderScaleSlider.onValueChanged.AddListener(v => Change(b => b.q.renderScale = Snap(v)));
        if (shadowDropdown != null) shadowDropdown.onValueChanged.AddListener(i => Change(b => b.q.shadows = (ShadowSetting)i));
        if (antiAliasingDropdown != null) antiAliasingDropdown.onValueChanged.AddListener(i => Change(b => b.q.antiAliasing = (AntiAliasingSetting)i));
        if (postEffectsToggle != null) postEffectsToggle.onValueChanged.AddListener(on => Change(b => b.q.postEffects = on));
        if (vignetteSizeSlider != null) vignetteSizeSlider.onValueChanged.AddListener(v => Change(b => b.q.vignetteSize = GraphicsQualityConfig.VignetteSizeFromSlider(Step(v))));
        if (vignetteFadeSlider != null) vignetteFadeSlider.onValueChanged.AddListener(v => Change(b => b.q.vignetteFade = GraphicsQualityConfig.VignetteFadeFromSlider(Step(v))));
    }

    void OnEnable()
    {
        LanguageConfig.Changed += OnLanguageChanged;
        OnLanguageChanged();
    }

    void OnDisable() => LanguageConfig.Changed -= OnLanguageChanged;

    // 항목 글자를 지금 언어로 다시 채운 뒤 값을 맞춘다(Refresh).
    void OnLanguageChanged()
    {
        _refreshing = true;
        Fill(presetDropdown, L("settings.graphics.preset.low"), L("settings.graphics.preset.medium"),
            L("settings.graphics.preset.high"), L("settings.graphics.preset.custom"));
        Fill(shadowDropdown, L("settings.common.off"), L("settings.graphics.preset.low"),
            L("settings.graphics.preset.medium"), L("settings.graphics.preset.high"));
        Fill(antiAliasingDropdown, L("settings.common.off"), "FXAA", "SMAA");   // 10-13 MSAA → 후처리 AA
        _refreshing = false;
        Refresh();
    }

    static string L(string key) => SettingsStrings.Get(key);

    void Change(System.Action<Box> edit)
    {
        if (_refreshing) return;
        var box = new Box { q = GraphicsQualityConfig.Current };
        edit(box);
        GraphicsQualityConfig.Set(box.q);
        Refresh();
    }

    // 람다에서 구조체를 고치려고 감싼다.
    sealed class Box { public GraphicsQuality q; }

    void OnPreset(int index)
    {
        if (_refreshing) return;
        var preset = (GraphicsPreset)index;
        if (preset == GraphicsPreset.Custom) { Refresh(); return; }   // Custom 은 고르는 항목이 아니다
        GraphicsQualityConfig.Set(GraphicsQualityConfig.PresetValues(preset, GraphicsQualityConfig.Current));
        Refresh();
    }

    void Refresh()
    {
        _refreshing = true;
        GraphicsQuality q = GraphicsQualityConfig.Current;
        if (presetDropdown != null) presetDropdown.SetValueWithoutNotify((int)q.preset);
        if (renderScaleSlider != null) renderScaleSlider.SetValueWithoutNotify(q.renderScale);
        if (shadowDropdown != null) shadowDropdown.SetValueWithoutNotify((int)q.shadows);
        if (antiAliasingDropdown != null) antiAliasingDropdown.SetValueWithoutNotify((int)q.antiAliasing);
        if (postEffectsToggle != null) postEffectsToggle.SetIsOnWithoutNotify(q.postEffects);
        float sizeSlider = GraphicsQualityConfig.VignetteSizeToSlider(q.vignetteSize);
        float fadeSlider = GraphicsQualityConfig.VignetteFadeToSlider(q.vignetteFade);
        if (vignetteSizeSlider != null) vignetteSizeSlider.SetValueWithoutNotify(sizeSlider);
        if (vignetteFadeSlider != null) vignetteFadeSlider.SetValueWithoutNotify(fadeSlider);
        SetText(renderScaleValue, Percent(q.renderScale));
        SetText(vignetteSizeValue, Mathf.RoundToInt(sizeSlider * 100f).ToString());
        SetText(vignetteFadeValue, Mathf.RoundToInt(fadeSlider * 100f).ToString());
        _refreshing = false;
    }

    // 렌더 스케일은 5% 단위 — 프리셋 판정(정확히 같음)이 슬라이더 미세값에 흔들리지 않게.
    static float Snap(float v) => Mathf.Round(v * 20f) / 20f;
    static string Percent(float v) => $"{Mathf.RoundToInt(v * 100f)}%";
    // 0~100 정수 단위로 끊는다 — 표시 숫자와 저장값이 어긋나지 않게.
    static float Step(float s01) => Mathf.Round(s01 * 100f) / 100f;

    static void SetText(TMP_Text t, string s) { if (t != null) t.text = s; }

    static void Fill(TMP_Dropdown d, params string[] labels)
    {
        if (d == null) return;
        d.ClearOptions();
        d.AddOptions(new List<string>(labels));
    }

    static void Range(Slider s, float min, float max)
    {
        if (s == null) return;
        s.minValue = min;
        s.maxValue = max;
        s.wholeNumbers = false;
    }
}
