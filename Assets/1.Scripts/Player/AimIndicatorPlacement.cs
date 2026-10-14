using UnityEngine;

/// <summary>
/// 에임 인디케이터(바닥에서 띄운 얇은 쿼드) 배치 순수 계산. <see cref="PlayerAimIndicator"/> 가 매 프레임 쓴다.
/// 인디케이터는 플레이어 자식이지만 월드 위치·회전·크기를 여기서 다시 계산해 부모 회전과 무관하게 둔다.
/// </summary>
public static class AimIndicatorPlacement
{
    /// <summary>쿼드를 바닥에 눕히는 X 회전. 쿼드 앞면(-Z)이 위를 보게 한다.</summary>
    public const float LayFlatPitch = 90f;

    /// <summary>수평 조준 방향 → 월드 yaw(도). +Z = 0, +X = 90.</summary>
    public static float YawDegrees(Vector3 aimDirection) =>
        Mathf.Atan2(aimDirection.x, aimDirection.z) * Mathf.Rad2Deg;

    /// <summary>수평 유지 회전 — Y축만 돈다. rotOffset 은 텍스처 화살표 방향 보정(기존 90).</summary>
    public static Quaternion Rotation(Vector3 aimDirection, float rotOffset) =>
        Quaternion.Euler(LayFlatPitch, YawDegrees(aimDirection) + rotOffset, 0f);

    /// <summary>발밑 지면 Y(레이 미스면 캐릭터 Y) + 띄움 높이.</summary>
    public static float Height(bool hasGround, float groundY, float fallbackY, float heightOffset) =>
        (hasGround ? groundY : fallbackY) + heightOffset;

    /// <summary>발밑 기준점에서 조준 방향으로 forwardOffset 만큼 민 쿼드 중심. Y 는 height 로 고정(경사에서도 수평).</summary>
    public static Vector3 Position(Vector3 footPosition, float height, Vector3 aimDirection, float forwardOffset)
    {
        Vector3 flat = aimDirection;
        flat.y = 0f;
        flat = flat.sqrMagnitude > 0f ? flat.normalized : Vector3.zero;

        Vector3 position = footPosition + flat * forwardOffset;
        position.y = height;
        return position;
    }

    /// <summary>월드 크기 arrowScale(m) 정사각 쿼드가 되도록 부모 스케일(균등 가정)을 상쇄한 localScale.</summary>
    public static Vector3 LocalScale(float arrowScale, float parentUniformScale)
    {
        float parent = Mathf.Abs(parentUniformScale) < 1e-5f ? 1f : parentUniformScale;
        return new Vector3(arrowScale / parent, arrowScale / parent, 1f);
    }
}
