using UnityEngine;

/// <summary>
/// 거너 기본 공격의 방향 고정 규칙(할 일 27). 순수 클래스 — <see cref="GunnerBasicAttack"/> 가 쓴다.
///
/// · 잠금 시작 = 공격 시작(준비 동작 진입) 순간의 조준 방향을 확정한다.
/// · 그동안 몸과 모든 발(첫 발·홀드·입력 창)의 빔 방향은 그 방향이다 — 나중 조준은 무시한다.
/// · 잠금 해제 = 공격 런타임이 끝날 때. 정상 종료(마지막 발 + 후딜 <c>ShotRecovery</c> 경과)든
///   끊김(대시·피격 상태 전이·사망 → <c>PlayerAttackState.Exit</c> → <c>CancelCurrentAttack</c>)이든 같은 경로다.
///   후딜 시작이 아니라 끝인 이유: 후딜 안의 입력 창 클릭이 다음 발을 같은 방향으로 이어 쏠 수 있다.
/// </summary>
public sealed class GunnerAttackFacingModel
{
    public bool IsLocked { get; private set; }
    public Vector3 Direction { get; private set; }

    /// <summary>공격 시작 — 조준을 평면으로 눌러 고정한다. 조준이 없으면 <paramref name="fallback"/>(현재 정면).</summary>
    public Vector3 Begin(Vector3 aim, Vector3 fallback)
    {
        Direction = Flatten(aim, fallback);
        IsLocked = true;
        return Direction;
    }

    /// <summary>이번 발의 방향. 잠겨 있으면 고정 방향, 아니면(시작 전) <paramref name="fallback"/>.</summary>
    public Vector3 ShotDirection(Vector3 fallback) => IsLocked ? Direction : Flatten(fallback, Vector3.forward);

    /// <returns>잠겨 있었으면 true.</returns>
    public bool End()
    {
        bool wasLocked = IsLocked;
        IsLocked = false;
        return wasLocked;
    }

    public static Vector3 Flatten(Vector3 direction, Vector3 fallback)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude >= 0.001f)
            return direction.normalized;

        fallback.y = 0f;
        return fallback.sqrMagnitude >= 0.001f ? fallback.normalized : Vector3.forward;
    }
}
