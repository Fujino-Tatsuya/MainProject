using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 설정창 그래픽 탭(PLAN-settings S2~S6). 값은 <see cref="DisplayConfig"/> 가 저장·적용한다 — 여기는 UI 만.
/// - 해상도·창 모드: 고른 뒤 **Apply** 로 적용 → 10초 "유지할까요?" 확인 창, 시간 초과·되돌림이면 이전 값(S5).
/// - 프레임 제한·VSync: 바꾸는 즉시 적용(VSync 를 켜면 프레임 제한은 비활성).
/// 배치·참조 연결 = <c>Tools/UI/Settings/설정 패널 구성</c>.
/// </summary>
public sealed class GraphicsSettingsController : MonoBehaviour
{
    const float ConfirmSeconds = 10f;

    [SerializeField] TMP_Dropdown resolutionDropdown;
    [SerializeField] TMP_Dropdown windowModeDropdown;
    [SerializeField] TMP_Dropdown frameLimitDropdown;
    [SerializeField] Toggle vSyncToggle;
    [SerializeField] Button applyButton;

    [Header("확인 창")]
    [SerializeField] GameObject confirmDialog;
    [SerializeField] TMP_Text confirmText;
    [SerializeField] Button keepButton;
    [SerializeField] Button revertButton;

    readonly List<ResolutionChoice> _candidates = new List<ResolutionChoice>();
    DisplaySelection _pending;
    DisplaySelection _previous;
    float _confirmLeft = -1f;

    void Awake()
    {
        if (resolutionDropdown != null) resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);
        if (windowModeDropdown != null) windowModeDropdown.onValueChanged.AddListener(OnWindowModeChanged);
        if (frameLimitDropdown != null) frameLimitDropdown.onValueChanged.AddListener(OnFrameLimitChanged);
        if (vSyncToggle != null) vSyncToggle.onValueChanged.AddListener(OnVSyncChanged);
        if (applyButton != null) applyButton.onClick.AddListener(ApplyScreen);
        if (keepButton != null) keepButton.onClick.AddListener(Keep);
        if (revertButton != null) revertButton.onClick.AddListener(Revert);
    }

    void OnEnable()
    {
        LanguageConfig.Changed += Refresh;
        HideConfirm();
        Refresh();
    }

    // 확인 중에 탭·패널을 닫으면 "유지"를 누르지 않은 것 — 되돌린다(화면이 안 보여 닫은 경우도 복구된다).
    void OnDisable()
    {
        LanguageConfig.Changed -= Refresh;
        if (_confirmLeft >= 0f) Revert();
    }

    void Update()
    {
        if (_confirmLeft < 0f) return;
        _confirmLeft -= Time.unscaledDeltaTime;   // 일시정지·슬로우와 무관하게 10초
        if (_confirmLeft <= 0f) { Revert(); return; }
        UpdateConfirmText();
    }

    void UpdateConfirmText()
    {
        if (confirmText != null)
            confirmText.text = SettingsStrings.Format("settings.graphics.confirm", Mathf.CeilToInt(_confirmLeft));
    }

    void Refresh()
    {
        _pending = DisplayConfig.Current;
        Vector2Int native = DisplayConfig.NativeSize;

        _candidates.Clear();
        _candidates.AddRange(DisplayConfig.Candidates(native));
        if (resolutionDropdown != null)
        {
            resolutionDropdown.ClearOptions();
            var labels = new List<string>(_candidates.Count);
            foreach (ResolutionChoice c in _candidates) labels.Add(Label(c, native));
            resolutionDropdown.AddOptions(labels);
            int index = _candidates.IndexOf(_pending.resolution);
            resolutionDropdown.SetValueWithoutNotify(index >= 0 ? index : _candidates.Count - 1);
        }

        if (windowModeDropdown != null)
        {
            windowModeDropdown.ClearOptions();
            windowModeDropdown.AddOptions(new List<string>
            {
                SettingsStrings.Get("settings.graphics.windowMode.fullscreen"),
                SettingsStrings.Get("settings.graphics.windowMode.borderless"),
                SettingsStrings.Get("settings.graphics.windowMode.windowed"),
            });
            windowModeDropdown.SetValueWithoutNotify((int)_pending.windowMode);
        }

        if (frameLimitDropdown != null)
        {
            frameLimitDropdown.ClearOptions();
            var labels = new List<string>();
            int selected = 0;
            for (int i = 0; i < DisplayConfig.FrameLimitOptions.Length; i++)
            {
                int v = DisplayConfig.FrameLimitOptions[i];
                labels.Add(v > 0 ? v.ToString() : SettingsStrings.Get("settings.graphics.frameLimit.unlimited"));
                if (v == _pending.frameLimit) selected = i;
            }
            frameLimitDropdown.AddOptions(labels);
            frameLimitDropdown.SetValueWithoutNotify(selected);
            frameLimitDropdown.interactable = !_pending.vSync;
        }

        if (vSyncToggle != null) vSyncToggle.SetIsOnWithoutNotify(_pending.vSync);
        UpdateApplyButton();
    }

    static string Label(ResolutionChoice c, Vector2Int native)
    {
        switch (c)
        {
            case ResolutionChoice.Recommended2560x1440: return SettingsStrings.Get("settings.graphics.resolution.1440");
            case ResolutionChoice.Recommended1920x1080: return SettingsStrings.Get("settings.graphics.resolution.1080");
            default: return SettingsStrings.Format("settings.graphics.resolution.native", native.x, native.y);
        }
    }

    void OnResolutionChanged(int index)
    {
        if (index >= 0 && index < _candidates.Count) _pending.resolution = _candidates[index];
        UpdateApplyButton();
    }

    void OnWindowModeChanged(int index)
    {
        _pending.windowMode = (WindowModeChoice)index;
        UpdateApplyButton();
    }

    // 프레임·VSync 는 화면이 꺼질 일이 없어 즉시 적용한다(확인 창 없음 — S5).
    void OnFrameLimitChanged(int index)
    {
        if (index < 0 || index >= DisplayConfig.FrameLimitOptions.Length) return;
        DisplaySelection s = DisplayConfig.Current;
        s.frameLimit = DisplayConfig.FrameLimitOptions[index];
        DisplayConfig.Set(s);
        _pending.frameLimit = s.frameLimit;
    }

    void OnVSyncChanged(bool on)
    {
        DisplaySelection s = DisplayConfig.Current;
        s.vSync = on;
        DisplayConfig.Set(s);
        _pending.vSync = on;
        if (frameLimitDropdown != null) frameLimitDropdown.interactable = !on;
    }

    void UpdateApplyButton()
    {
        if (applyButton != null) applyButton.interactable = DisplayConfig.ChangesScreen(DisplayConfig.Current, _pending);
    }

    void ApplyScreen()
    {
        if (!DisplayConfig.ChangesScreen(DisplayConfig.Current, _pending)) return;
        _previous = DisplayConfig.Current;
        DisplayConfig.Set(_pending);
        _confirmLeft = ConfirmSeconds;
        if (confirmDialog != null) confirmDialog.SetActive(true);
        UpdateConfirmText();   // 첫 프레임부터 지금 언어로
        UpdateApplyButton();
    }

    void Keep()
    {
        HideConfirm();
        Refresh();
    }

    void Revert()
    {
        if (_confirmLeft < 0f) return;
        _confirmLeft = -1f;
        DisplayConfig.Set(_previous);
        HideConfirm();
        if (isActiveAndEnabled) Refresh();
    }

    void HideConfirm()
    {
        _confirmLeft = -1f;
        if (confirmDialog != null) confirmDialog.SetActive(false);
    }
}
