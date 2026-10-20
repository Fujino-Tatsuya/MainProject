using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 슬라이더 옆 "100%" 표시(PLAN-settings-art). 볼륨 슬라이더 3개용 — 값 표시를 따로 채우는 컨트롤러가 없다.
/// 다른 슬라이더(흔들림·렌더 스케일·비네트)는 각 컨트롤러가 자기 값 텍스트를 채우므로 붙이지 않는다.
/// </summary>
public sealed class SettingsSliderPercent : MonoBehaviour
{
    [SerializeField] private Slider slider;
    [SerializeField] private TMP_Text text;

    private int _shown = -1;

    private void LateUpdate()
    {
        if (slider == null || text == null) return;
        int percent = Mathf.RoundToInt(slider.normalizedValue * 100f);
        if (percent == _shown) return;
        _shown = percent;
        text.SetText("{0}%", percent);
    }
}
