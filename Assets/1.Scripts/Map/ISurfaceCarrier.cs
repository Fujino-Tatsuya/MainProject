using UnityEngine;

/// <summary>
/// 밟고 있는 라이더에게 프레임별 이동량을 제공하는 표면입니다.
/// </summary>
public interface ISurfaceCarrier
{
    Vector3 GetCarryDelta(Vector3 riderWorldPos, float dt);
}
