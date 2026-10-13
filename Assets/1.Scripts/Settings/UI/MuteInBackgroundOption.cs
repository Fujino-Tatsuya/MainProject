using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 설정창 오디오 탭 "Mute When Unfocused"(PLAN-settings S11, 기본 켬). <see cref="SoundConfig.MuteInBackground"/> 를 읽고 쓴다.
/// 패턴은 <see cref="DashDirectionOption"/> 과 같다.
/// </summary>
[RequireComponent(typeof(Toggle))]
public sealed class MuteInBackgroundOption : MonoBehaviour
{
    Toggle _toggle;

    void Awake()
    {
        _toggle = GetComponent<Toggle>();
        _toggle.onValueChanged.AddListener(OnValueChanged);
    }

    void OnEnable() => _toggle.SetIsOnWithoutNotify(SoundConfig.MuteInBackground);

    void OnDestroy()
    {
        if (_toggle != null) _toggle.onValueChanged.RemoveListener(OnValueChanged);
    }

    static void OnValueChanged(bool isOn) => SoundConfig.MuteInBackground = isOn;
}
