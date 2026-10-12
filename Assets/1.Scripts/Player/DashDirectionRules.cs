using UnityEngine;

/// <summary>
/// 대시 방향 선택 규칙(순수 계산). 입력값은 호출자가 모은다 — 조준·이동 입력·캐릭터 정면·Transform 정면.
/// 0 벡터는 "없음"으로 본다. 반환은 항상 평면(y=0) 단위 벡터다.
/// - <see cref="DashDirectionMode.Cursor"/>: 조준 → 이동 입력 → 캐릭터 정면 → Transform 정면.
/// - <see cref="DashDirectionMode.CharacterFacing"/>: 이동 입력 → 캐릭터 정면 → Transform 정면. 조준은 보지 않는다.
///   이동 입력을 정면보다 먼저 보는 이유 — 회전은 몇 프레임에 걸쳐 따라가므로, 방향을 꺾자마자 대시하면
///   아직 덜 돈 armature 정면이 아니라 플레이어가 의도한 정면으로 나가야 한다.
/// </summary>
public static class DashDirectionRules
{
    private const float MinSqrMagnitude = 0.0001f;

    public static Vector3 Resolve(
        DashDirectionMode mode,
        Vector3 aimDirection,
        Vector3 moveInputDirection,
        Vector3 characterFacing,
        Vector3 transformForward)
    {
        if (mode == DashDirectionMode.Cursor && TryPlanar(aimDirection, out Vector3 aim))
            return aim;

        if (TryPlanar(moveInputDirection, out Vector3 move))
            return move;

        if (TryPlanar(characterFacing, out Vector3 facing))
            return facing;

        return TryPlanar(transformForward, out Vector3 forward) ? forward : Vector3.forward;
    }

    private static bool TryPlanar(Vector3 direction, out Vector3 planar)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude > MinSqrMagnitude)
        {
            planar = direction.normalized;
            return true;
        }

        planar = Vector3.zero;
        return false;
    }
}
