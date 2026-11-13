using UnityEngine;

/// <summary>
/// 활성화된 UI 모달을 로컬 Player 입력 차단 reason으로 등록한다.
/// 패드 모드 첫 선택(<see cref="GamepadUiFocus"/>, 할 일 278)도 이 루트를 패널로 등록한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class UiModalBlocker : MonoBehaviour
{
    private void OnEnable()
    {
        UiInputGateManager.Acquire(this);
        GamepadUiFocus.Push(this, gameObject);
    }

    private void OnDisable()
    {
        UiInputGateManager.Release(this);
        GamepadUiFocus.Pop(this);
    }
}
