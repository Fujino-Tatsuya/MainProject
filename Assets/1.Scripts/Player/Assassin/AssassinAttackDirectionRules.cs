using UnityEngine;

/// <summary>
/// 어쌔신 평타 한 타의 방향 규칙(할 일 96). 이 방향 하나로 몸 회전·판정·전진이 함께 간다.
/// 일반 타 = 이동 입력이 있으면 그 방향(카메라 기준 월드, <see cref="PlayerMovement.GetInputWorldDirection"/>), 없으면 조준.
/// 강타·변신 묶음 = 늘 조준 — 강타는 기존 강타 전진만 쓰고 변신 묶음은 전진이 없다.
/// MonoBehaviour 아님 — EditMode 테스트 대상.
/// </summary>
public static class AssassinAttackDirectionRules
{
    /// <summary>이 길이 제곱 미만의 이동 입력은 "입력 없음"으로 본다.</summary>
    public const float MinMoveSqrMagnitude = 0.001f;

    /// <summary>모드별 이 타의 방향. 반환값은 수평으로 편 단위 벡터이거나, 조준을 고르면 조준 그대로다(호출자가 편다).</summary>
    public static Vector3 Resolve(AssassinBasicAttackMode mode, Vector3 moveDirection, Vector3 aimDirection)
    {
        if (mode != AssassinBasicAttackMode.Normal)
            return aimDirection;

        moveDirection.y = 0f;
        return moveDirection.sqrMagnitude >= MinMoveSqrMagnitude ? moveDirection.normalized : aimDirection;
    }
}
