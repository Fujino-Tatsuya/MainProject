using UnityEngine;

/// <summary>
/// 자식 컨베이어 타일이 공유하는 이동 속도를 보유합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ConveyorGroup : MonoBehaviour
{
    [SerializeField, Min(0f)] private float beltSpeed = 3f;

    public float BeltSpeed => Mathf.Max(0f, beltSpeed);

    private void OnValidate()
    {
        beltSpeed = Mathf.Max(0f, beltSpeed);
    }
}
