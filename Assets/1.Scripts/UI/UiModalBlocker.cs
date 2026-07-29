using UnityEngine;

/// <summary>
/// 활성화된 UI 모달을 로컬 Player 입력 차단 reason으로 등록한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class UiModalBlocker : MonoBehaviour
{
    private void OnEnable()
    {
        UiInputGateManager.Acquire(this);
    }

    private void OnDisable()
    {
        UiInputGateManager.Release(this);
    }
}
