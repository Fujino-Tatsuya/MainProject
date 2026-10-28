using UnityEngine;

/// <summary>
/// 어쌔신 평타 한 타의 방향 규칙. 이 방향 하나로 몸 회전·판정·전진이 함께 간다.
/// 일반 타·강타·변신 묶음 모두 조준(마우스) 방향 — 팔라딘·거너 기본 공격과 같은 공용 규칙이다(할 일 137).
/// 할 일 96 의 "일반 타는 이동 입력 우선"은 폐기했다. 이동 입력 인자는 호출·RPC 시그니처를 그대로 두려고 남겨 둔 것으로 쓰지 않는다.
/// 일반 타 전진(할 일 96)은 그대로 — 방향만 조준이다. 강타는 기존 강타 전진, 변신 묶음은 전진이 없다.
/// MonoBehaviour 아님 — EditMode 테스트 대상.
/// </summary>
public static class AssassinAttackDirectionRules
{
    /// <summary>모드와 무관하게 조준 그대로를 반환한다(호출자가 편다). <paramref name="moveDirection"/> 은 쓰지 않는다.</summary>
    public static Vector3 Resolve(AssassinBasicAttackMode mode, Vector3 moveDirection, Vector3 aimDirection)
    {
        return aimDirection;
    }
}
