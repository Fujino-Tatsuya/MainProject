using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 설정창 Controls 탭의 "Guard Skill: Hold"(팔라딘 E, 할 일 95). <see cref="UserInputConfig.GuardSkillHold"/>(PlayerPrefs)를 그대로 읽고 쓴다.
/// 켜짐(기본) = 누르는 동안 방어, 꺼짐 = 탭 시작·1.5초 자동 해제·방어중 E 재입력 해제.
/// 배치 = <c>Tools/UI/Settings/조작 — 방어 스킬 홀드 토글 배치</c>(<see cref="HoldSkillToggleOption"/> 바로 아래 행).
/// </summary>
[RequireComponent(typeof(Toggle))]
public sealed class GuardSkillHoldOption : MonoBehaviour
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
        _toggle.SetIsOnWithoutNotify(UserInputConfig.GuardSkillHold);
    }

    private void OnDestroy()
    {
        if (_toggle != null)
            _toggle.onValueChanged.RemoveListener(OnValueChanged);
    }

    private static void OnValueChanged(bool isOn) => UserInputConfig.GuardSkillHold = isOn;
}
