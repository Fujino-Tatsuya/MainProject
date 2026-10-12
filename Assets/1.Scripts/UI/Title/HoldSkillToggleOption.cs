using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 옵션 Controls 탭의 "Hold Skill: Toggle". <see cref="UserInputConfig.HoldSkillAsToggle"/>(PlayerPrefs)를 그대로 읽고 쓴다.
/// 빌드에서 조작 방식을 바꾸는 유일한 창구다 — 에디터와 빌드는 PlayerPrefs 저장 위치가 달라 에디터 설정이 넘어가지 않는다.
/// 배치 = <c>Tools/UI/Title/옵션 Controls — Hold 토글 배치</c>.
/// </summary>
[RequireComponent(typeof(Toggle))]
public sealed class HoldSkillToggleOption : MonoBehaviour
{
    private Toggle _toggle;

    private void Awake()
    {
        _toggle = GetComponent<Toggle>();
        _toggle.onValueChanged.AddListener(OnValueChanged);
    }

    private void OnEnable()
    {
        // 탭을 열 때마다 저장값으로 맞춘다. 리스너는 안 불러 같은 값을 다시 쓰지 않는다.
        _toggle.SetIsOnWithoutNotify(UserInputConfig.HoldSkillAsToggle);
    }

    private void OnDestroy()
    {
        if (_toggle != null)
            _toggle.onValueChanged.RemoveListener(OnValueChanged);
    }

    private static void OnValueChanged(bool isOn) => UserInputConfig.HoldSkillAsToggle = isOn;
}
