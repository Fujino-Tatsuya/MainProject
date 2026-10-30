using UnityEngine;

/// <summary>
/// 공격 판정 도형의 기준점 종류(할 일 153). 공격 데이터(<see cref="AttackShapeSet.Origin"/>)가 고르고, 스킬 코드가 그 종류에 맞는 포즈를 만든다.
/// ⚠️ 값은 끝에만 추가한다 — SO 에 정수로 직렬화된다.
/// </summary>
public enum AttackShapeOrigin
{
    /// <summary>시전자 발밑 + 그 공격의 조준 방향(평타·근접 스킬 대부분).</summary>
    CasterAim,
    /// <summary>스킬이 정한 지점 + 방향(예: 진격의 충격 = Q 끝 지점 + 마지막 진행 방향).</summary>
    Point
}

/// <summary>
/// 판정 기준 포즈(할 일 153) — 바닥 위치 + 바닥 평면 정면. 도형(<see cref="AttackShape"/>)은 이 포즈에서 상대로 정의된다.
/// 정면은 y 를 뺀 단위 벡터다. 0 이면 월드 +z 로 둔다(판정이 사라지지 않게).
/// </summary>
public readonly struct AttackShapePose
{
    public readonly Vector3 Position;
    public readonly Vector3 Forward;

    public AttackShapePose(Vector3 position, Vector3 forward)
    {
        Position = position;
        forward.y = 0f;
        Forward = forward.sqrMagnitude >= 1e-8f ? forward.normalized : Vector3.forward;
    }

    /// <summary>오른쪽(바닥 평면) = 정면을 y 축으로 +90°.</summary>
    public Vector3 Right => new Vector3(Forward.z, 0f, -Forward.x);

    /// <summary>
    /// 종류별 포즈. <see cref="AttackShapeOrigin.CasterAim"/> = (<paramref name="casterPosition"/>, <paramref name="aimDirection"/>),
    /// <see cref="AttackShapeOrigin.Point"/> = (<paramref name="point"/>, <paramref name="pointDirection"/>).
    /// </summary>
    public static AttackShapePose Resolve(AttackShapeOrigin origin, Vector3 casterPosition, Vector3 aimDirection,
        Vector3 point, Vector3 pointDirection) =>
        origin == AttackShapeOrigin.Point
            ? new AttackShapePose(point, pointDirection)
            : new AttackShapePose(casterPosition, aimDirection);

    /// <summary>월드 점 → 이 포즈의 바닥 평면 로컬(x = 오른쪽, y = 정면). 높이는 뺀다.</summary>
    public Vector2 ToPlanarLocal(Vector3 world)
    {
        Vector3 d = world - Position;
        return new Vector2(d.x * Forward.z - d.z * Forward.x, d.x * Forward.x + d.z * Forward.z);
    }

    /// <summary>바닥 평면 로컬(x 오른쪽, y 정면) + 높이 → 월드 점.</summary>
    public Vector3 FromPlanarLocal(Vector2 local, float height = 0f) =>
        Position + Right * local.x + Forward * local.y + Vector3.up * height;

    /// <summary>이 포즈에서 오른쪽 <paramref name="side"/>·앞 <paramref name="forward"/> 만큼 옮기고 <paramref name="yawDegrees"/> 돌린 포즈(+ = 시계 방향, 위에서 볼 때).</summary>
    public AttackShapePose Offset(float forward, float side, float yawDegrees)
    {
        Vector3 position = Position + Forward * forward + Right * side;
        Vector3 facing = Mathf.Approximately(yawDegrees, 0f) ? Forward : Quaternion.AngleAxis(yawDegrees, Vector3.up) * Forward;
        return new AttackShapePose(position, facing);
    }
}
