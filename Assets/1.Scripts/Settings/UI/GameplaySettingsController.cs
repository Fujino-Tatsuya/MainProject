using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 설정창 게임플레이 탭(PLAN-settings S8·S9·S15·S17). 값 = <see cref="GameplayConfig"/> · <see cref="LanguageConfig"/>.
/// - 데미지 숫자: 전체 / 내 것만 / 전체+내 것 강조 / 끄기(드롭다운 순서 = <see cref="FloatingDamageDisplayFilter"/> 값).
/// - 화면 흔들림: 0~100%.
/// - 언어: 버튼 한 번 = English ↔ 한국어. 버튼 글자 = 지금 언어 이름(번역하지 않음).
/// - 성능 표시: 끄기 / FPS / FPS+핑(순서 = <see cref="PerformanceDisplay"/>).
/// </summary>
public sealed class GameplaySettingsController : MonoBehaviour
{
    // 저작값을 못 읽을 때(스포너 없음 — 타이틀) 보여 줄 기본. FloatingDamageSettings 의 저작 기본과 같다.
    const FloatingDamageDisplayFilter FallbackDamageFilter = FloatingDamageDisplayFilter.OwnDealtOnly;

    [SerializeField] TMP_Dropdown damageNumbersDropdown;
    [SerializeField] Slider screenShakeSlider;
    [SerializeField] TMP_Text screenShakeValue;
    [SerializeField] Button languageButton;
    [SerializeField] TMP_Text languageStatus;
    [SerializeField] TMP_Dropdown performanceDropdown;

    void Awake()
    {
        if (damageNumbersDropdown != null) damageNumbersDropdown.onValueChanged.AddListener(OnDamageNumbersChanged);
        if (screenShakeSlider != null)
        {
            screenShakeSlider.minValue = 0f;
            screenShakeSlider.maxValue = 1f;
            screenShakeSlider.onValueChanged.AddListener(OnScreenShakeChanged);
        }
        if (languageButton != null) languageButton.onClick.AddListener(OnLanguageClicked);
        if (performanceDropdown != null) performanceDropdown.onValueChanged.AddListener(i => GameplayConfig.Performance = (PerformanceDisplay)i);
    }

    void OnEnable()
    {
        LanguageConfig.Changed += Refresh;
        Refresh();
    }

    void OnDisable() => LanguageConfig.Changed -= Refresh;

    void Refresh()
    {
        FloatingDamageSpawner spawner = FloatingDamageSpawner.Instance;
        FloatingDamageDisplayFilter current = spawner != null && spawner.Settings != null
            ? spawner.Settings.DisplayFilter
            : GameplayConfig.DamageNumbers(FallbackDamageFilter);
        Fill(damageNumbersDropdown, (int)current,
            "settings.gameplay.damageNumbers.all", "settings.gameplay.damageNumbers.mine",
            "settings.gameplay.damageNumbers.allEmphasized", "settings.common.off");

        float shake = GameplayConfig.ScreenShake;
        if (screenShakeSlider != null) screenShakeSlider.SetValueWithoutNotify(shake);
        UpdateShakeLabel(shake);

        TMP_Text languageLabel = languageButton != null ? languageButton.GetComponentInChildren<TMP_Text>(true) : null;
        if (languageLabel != null) languageLabel.text = LanguageConfig.NativeName(LanguageConfig.Current);
        if (languageStatus != null) languageStatus.text = string.Empty;

        Fill(performanceDropdown, (int)GameplayConfig.Performance,
            "settings.common.off", "settings.gameplay.performance.fps", "settings.gameplay.performance.fpsPing");
    }

    // 🔴 항목을 비우고 다시 채운 뒤 값을 맞춘다 — ClearOptions/AddOptions 가 보이는 글자를 새로 그린다
    //    (값이 같으면 SetValueWithoutNotify 만으로는 다시 그리지 않는다).
    static void Fill(TMP_Dropdown dropdown, int value, params string[] keys)
    {
        if (dropdown == null) return;
        var labels = new List<string>(keys.Length);
        foreach (string k in keys) labels.Add(SettingsStrings.Get(k));
        dropdown.ClearOptions();
        dropdown.AddOptions(labels);
        dropdown.SetValueWithoutNotify(value);
    }

    void OnDamageNumbersChanged(int index) => GameplayConfig.SetDamageNumbers((FloatingDamageDisplayFilter)index);

    void OnScreenShakeChanged(float value)
    {
        GameplayConfig.ScreenShake = value;
        UpdateShakeLabel(value);
    }

    void UpdateShakeLabel(float value)
    {
        if (screenShakeValue != null) screenShakeValue.text = $"{Mathf.RoundToInt(value * 100f)}%";
    }

    // 언어를 바꾸면 Changed → 이 탭(Refresh)·고정 라벨(LocalizedText)·다른 탭(켜질 때) 이 다시 채운다.
    void OnLanguageClicked() => LanguageConfig.Set(LanguageConfig.Next(LanguageConfig.Current));
}
