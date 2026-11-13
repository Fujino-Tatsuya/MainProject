using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 설정창 게임플레이 탭(PLAN-settings S8·S9·S15·S17). 값 = <see cref="GameplayConfig"/> · <see cref="Loc.Current"/>.
/// - 데미지 숫자: 전체 / 내 것만 / 전체+내 것 강조 / 끄기(드롭다운 순서 = <see cref="FloatingDamageDisplayFilter"/> 값).
/// - 화면 흔들림: 0~100%.
/// - 패드 진동: 0~100%(0 = 끔, 할 일 278 햅틱). 프리팹에 행이 없어 Awake 에서 화면 흔들림 행을 복제해 만든다(직렬화 칸 없음).
/// - 언어: 버튼 한 번 = 한국어 ↔ English. 버튼 글자 = 지금 언어 이름(번역하지 않음 — <see cref="LocText.NativeName"/>).
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

    // 런타임 복제 행(패드 진동) — 프리팹 저작 없이 화면 흔들림 행을 복제한다.
    Slider _hapticsSlider;
    TMP_Text _hapticsValue;
    TMP_Text _hapticsLabel;

    void Awake()
    {
        if (damageNumbersDropdown != null) damageNumbersDropdown.onValueChanged.AddListener(OnDamageNumbersChanged);
        if (screenShakeSlider != null)
        {
            screenShakeSlider.minValue = 0f;
            screenShakeSlider.maxValue = 1f;
            screenShakeSlider.onValueChanged.AddListener(OnScreenShakeChanged);
        }
        BuildHapticsRow();
        if (languageButton != null) languageButton.onClick.AddListener(OnLanguageClicked);
        if (performanceDropdown != null) performanceDropdown.onValueChanged.AddListener(i => GameplayConfig.Performance = (PerformanceDisplay)i);
    }

    void OnEnable()
    {
        Loc.Changed += Refresh;
        Refresh();
    }

    void OnDisable() => Loc.Changed -= Refresh;

    void Refresh()
    {
        FloatingDamageSpawner spawner = FloatingDamageSpawner.Instance;
        FloatingDamageDisplayFilter current = spawner != null && spawner.Settings != null
            ? spawner.Settings.DisplayFilter
            : GameplayConfig.DamageNumbers(FallbackDamageFilter);
        Fill(damageNumbersDropdown, (int)current,
            SettingsUiStrings.GameplayDamageNumbersAll, SettingsUiStrings.GameplayDamageNumbersMine,
            SettingsUiStrings.GameplayDamageNumbersAllEmphasized, SettingsUiStrings.CommonOff);

        float shake = GameplayConfig.ScreenShake;
        if (screenShakeSlider != null) screenShakeSlider.SetValueWithoutNotify(shake);
        UpdateShakeLabel(shake);

        float haptics = GameplayConfig.Haptics;
        if (_hapticsSlider != null) _hapticsSlider.SetValueWithoutNotify(haptics);
        if (_hapticsValue != null) _hapticsValue.text = $"{Mathf.RoundToInt(haptics * 100f)}%";
        if (_hapticsLabel != null) _hapticsLabel.text = SettingsUiStrings.GameplayHaptics.Text;

        TMP_Text languageLabel = languageButton != null ? languageButton.GetComponentInChildren<TMP_Text>(true) : null;
        if (languageLabel != null) languageLabel.text = LocText.NativeName(Loc.Current);
        if (languageStatus != null) languageStatus.text = string.Empty;

        Fill(performanceDropdown, (int)GameplayConfig.Performance,
            SettingsUiStrings.CommonOff, SettingsUiStrings.GameplayPerformanceFps, SettingsUiStrings.GameplayPerformanceFpsPing);
    }

    // 🔴 항목을 비우고 다시 채운 뒤 값을 맞춘다 — ClearOptions/AddOptions 가 보이는 글자를 새로 그린다
    //    (값이 같으면 SetValueWithoutNotify 만으로는 다시 그리지 않는다).
    static void Fill(TMP_Dropdown dropdown, int value, params UiString[] texts)
    {
        if (dropdown == null) return;
        var labels = new List<string>(texts.Length);
        foreach (UiString t in texts) labels.Add(t.Text);
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

    // 패드 진동 행(할 일 278 햅틱) — 화면 흔들림 행(레이아웃 그룹 바로 아래 조상)을 복제해 바로 아래 끼운다.
    // 라벨의 LocalizedText 는 지운다(화면 흔들림 키로 덮어쓰지 않게) — 글자는 Refresh 가 SettingsUiStrings 로 채운다.
    void BuildHapticsRow()
    {
        if (screenShakeSlider == null) return;
        Transform row = screenShakeSlider.transform;
        while (row.parent != null && row.parent != transform && row.parent.GetComponent<LayoutGroup>() == null)
            row = row.parent;
        if (row.parent == null || row == screenShakeSlider.transform) return;

        GameObject clone = Instantiate(row.gameObject, row.parent, false);
        clone.name = row.name.Replace("ScreenShake", "Haptics");
        clone.transform.SetSiblingIndex(row.GetSiblingIndex() + 1);

        _hapticsSlider = clone.GetComponentInChildren<Slider>(true);
        foreach (TMP_Text t in clone.GetComponentsInChildren<TMP_Text>(true))
        {
            if (_hapticsSlider != null && t.transform.IsChildOf(_hapticsSlider.transform)) continue;
            if (t.name == "Value") { _hapticsValue = t; continue; }
            if (_hapticsLabel == null) _hapticsLabel = t;
        }
        foreach (LocalizedText loc in clone.GetComponentsInChildren<LocalizedText>(true))
            Destroy(loc);

        if (_hapticsSlider == null) return;
        _hapticsSlider.minValue = 0f;
        _hapticsSlider.maxValue = 1f;
        _hapticsSlider.onValueChanged.AddListener(OnHapticsChanged);
    }

    void OnHapticsChanged(float value)
    {
        GameplayConfig.Haptics = value;
        if (_hapticsValue != null) _hapticsValue.text = $"{Mathf.RoundToInt(value * 100f)}%";
        if (value <= 0f) GamepadHaptics.ResetHaptics();
        // 세기를 바꾸면 그 세기로 한 번 맛보기(패드 모드일 때만 울린다).
        else GamepadHaptics.Play(GamepadHapticEvent.SkillCast);
    }

    // 언어를 바꾸면 Changed → 이 탭(Refresh)·고정 라벨(LocalizedText)·다른 탭(켜질 때) 이 다시 채운다.
    void OnLanguageClicked() => Loc.Current = LocText.Next(Loc.Current);
}
