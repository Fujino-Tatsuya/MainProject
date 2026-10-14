using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 옵션 Controls 탭의 "Dash: Mouse Direction". 켜짐 = <see cref="DashDirectionMode.Cursor"/>,
/// 꺼짐(기본) = <see cref="DashDirectionMode.CharacterFacing"/>. <see cref="UserInputConfig.DashDirection"/>(PlayerPrefs)를 그대로 읽고 쓴다.
/// 배치 = <c>Tools/UI/Title/옵션 Controls — 대시 방향 토글 배치</c>.
/// </summary>
[RequireComponent(typeof(Toggle))]
public sealed class DashDirectionOption : MonoBehaviour
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
        _toggle.SetIsOnWithoutNotify(UserInputConfig.DashDirection == DashDirectionMode.Cursor);
    }

    private void OnDestroy()
    {
        if (_toggle != null)
            _toggle.onValueChanged.RemoveListener(OnValueChanged);
    }

    private static void OnValueChanged(bool isOn) =>
        UserInputConfig.DashDirection = isOn ? DashDirectionMode.Cursor : DashDirectionMode.CharacterFacing;
}
