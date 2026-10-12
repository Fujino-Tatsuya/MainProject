using UnityEngine;

/// <summary>
/// 거너 기본 공격의 방향 규칙(할 일 27, 2026-10-12 발 단위로 좁힘). 순수 클래스 — <see cref="GunnerBasicAttack"/> 가 쓴다.
///
/// · 발 잠금 = 각 발의 준비 시작 ~ 그 발 사격 끝. 그동안 몸은 돌지 않고 그 발은 잠근 방향으로 나간다.
///   - 첫 발: 준비 시작 = 공격 시작(준비 동작 진입) 순간의 <b>조준</b>(커서) — 대기에서 바로 그쪽을 본다.
///   - 이어지는 발(홀드·입력 창): 준비 동작이 없어 준비 시작 = 발사 요청 순간의 <b>몸 방향</b>. 덜 돌았으면 덜 돈 방향으로 쏜다.
///   - 사격 끝 = 그 발이 이 피어에서 실제로 나간 때(<c>PlayShot</c>). 오너가 원격이면 왕복 지연만큼 잠겨 있다.
/// · 발 사이(홀드 연사 간격·입력 창으로 이은 다음 발 전·후딜) = 커서 쪽으로 <see cref="TurnBetweenShots"/> 의 제한 속도로 돈다.
/// · 공격 잠금(<see cref="IsActive"/>) = 공격 런타임 전체. 이동 입력 회전 등 다른 회전은 공격 내내 막힌다(<c>PlayerFacingLock</c>).
///   해제 = 정상 종료(마지막 발 + 후딜 <c>ShotRecovery</c> 경과)든 끊김(대시·피격 상태 전이·사망 → <c>CancelCurrentAttack</c>)이든 같은 경로다.
/// </summary>
public sealed class GunnerAttackFacingModel
{
    /// <summary>공격 런타임 중인가. 이 동안 이동 입력 회전 등 다른 회전 원인은 막힌다.</summary>
    public bool IsActive { get; private set; }

    /// <summary>발 잠금 중인가(준비 시작 ~ 사격 끝). 이 동안은 커서로도 돌지 않는다.</summary>
    public bool IsShotLocked { get; private set; }

    /// <summary>마지막으로 잠근 발의 방향.</summary>
    public Vector3 Direction { get; private set; }

    /// <summary>공격 시작 = 첫 발 준비 시작 — 조준을 평면으로 눌러 잠근다. 조준이 없으면 <paramref name="fallback"/>(현재 정면).</summary>
    public Vector3 Begin(Vector3 aim, Vector3 fallback)
    {
        IsActive = true;
        return LockShot(aim, fallback);
    }

    /// <summary>이어지는 발의 준비 시작 — 지금 몸 방향으로 잠근다(커서가 아니다).</summary>
    public Vector3 BeginShot(Vector3 bodyFacing) => LockShot(bodyFacing, Direction);

    /// <summary>이 발 사격 끝 — 다음 발 준비 전까지 발 사이 회전으로 넘어간다.</summary>
    public void EndShot() => IsShotLocked = false;

    /// <summary>
    /// 이번 프레임의 몸 방향. 발 잠금 중이거나 공격 밖이면 그대로, 발 사이면 <paramref name="aim"/> 쪽으로
    /// 최대 <paramref name="turnSpeed"/>(도/초) × <paramref name="deltaTime"/> 만큼만 돈다(0 = 사이에도 고정).
    /// </summary>
    public Vector3 TurnBetweenShots(Vector3 current, Vector3 aim, float turnSpeed, float deltaTime)
    {
        Vector3 from = Flatten(current, Vector3.forward);
        if (!IsActive || IsShotLocked)
            return from;

        return TurnToward(from, Flatten(aim, from), MaxTurnAngle(turnSpeed, deltaTime));
    }

    /// <returns>공격 중이었으면 true.</returns>
    public bool End()
    {
        bool wasActive = IsActive;
        IsActive = false;
        IsShotLocked = false;
        return wasActive;
    }

    /// <summary><paramref name="seconds"/> 동안 돌 수 있는 최대 각도(도). 음수 입력은 0.</summary>
    public static float MaxTurnAngle(float turnSpeed, float seconds) => Mathf.Max(0f, turnSpeed) * Mathf.Max(0f, seconds);

    /// <summary>평면 방향 <paramref name="from"/> 을 <paramref name="to"/> 쪽으로 최대 <paramref name="maxDegrees"/> 만큼 돌린다.</summary>
    public static Vector3 TurnToward(Vector3 from, Vector3 to, float maxDegrees)
    {
        if (maxDegrees <= 0f)
            return from;

        float angle = Vector3.SignedAngle(from, to, Vector3.up);
        float step = Mathf.Clamp(angle, -maxDegrees, maxDegrees);
        return Quaternion.AngleAxis(step, Vector3.up) * from;
    }

    public static Vector3 Flatten(Vector3 direction, Vector3 fallback)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude >= 0.001f)
            return direction.normalized;

        fallback.y = 0f;
        return fallback.sqrMagnitude >= 0.001f ? fallback.normalized : Vector3.forward;
    }

    private Vector3 LockShot(Vector3 direction, Vector3 fallback)
    {
        Direction = Flatten(direction, fallback);
        IsShotLocked = true;
        return Direction;
    }
}
