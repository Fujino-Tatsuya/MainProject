using UnityEngine;

/// <summary>
/// 로딩 화면 필름 릴(진행률 옆 아이콘)을 로딩 중 계속 돌린다(할 일 290).
/// 로딩 화면이 끝나면 화면째 지워지므로 따로 멈추지 않는다. 시간 배율과 무관(unscaled).
/// </summary>
[DisallowMultipleComponent]
public sealed class LoadingReelSpin : MonoBehaviour
{
    [Tooltip("초당 회전 각도(도). 음수 = 시계 방향.")]
    [SerializeField] private float degreesPerSecond = -180f;

    private void Update()
    {
        transform.Rotate(0f, 0f, degreesPerSecond * Time.unscaledDeltaTime);
    }
}
