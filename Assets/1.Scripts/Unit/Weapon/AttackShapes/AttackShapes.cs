using System;
using System.Collections.Generic;
using UnityEngine;

// 공격 판정 도형 4종(할 일 153). 로컬 바닥 평면 = x 오른쪽, y 정면, 원점 = 도형 포즈(기준점 + 오프셋).
// 판정(ContainsLocal)·경계(GetLocalBounds)·외곽선(AppendLocalOutlines)이 같은 칸으로 계산한다. 캡슐은 없다.

/// <summary>부채꼴 — 반경 <see cref="range"/>, 전체 각 <see cref="angle"/>(360 = 원). <see cref="minOffset"/> &gt; 0 이면 안쪽이 빈 도넛 부채꼴.</summary>
[Serializable]
public sealed class AttackSectorShape : AttackShape
{
    // 각 경계 허용 오차(도) — 외곽선 호 끝점의 삼각함수 오차를 덮는다.
    private const float AngleEpsilon = 1e-3f;

    [Tooltip("바깥 반경(m)")]
    [Min(0f)] [SerializeField] private float range = 2f;

    [Tooltip("전체 각도(도, 360 = 원)")]
    [Range(0f, 360f)] [SerializeField] private float angle = 90f;

    [Tooltip("안쪽 빈 반경(m) — 0 이면 꼭짓점까지 찬 부채꼴")]
    [Min(0f)] [SerializeField] private float minOffset;

    public AttackSectorShape() { }

    public AttackSectorShape(float range, float angle, float minOffset = 0f)
    {
        this.range = range;
        this.angle = angle;
        this.minOffset = minOffset;
    }

    public float Range { get => range; set => range = value; }
    public float Angle { get => angle; set => angle = value; }
    public float MinOffset { get => minOffset; set => minOffset = value; }

    public override bool ContainsLocal(Vector2 local)
    {
        float distance = local.magnitude;
        if (distance > range + Epsilon || distance < minOffset - Epsilon)
            return false;
        // 꼭짓점은 방향이 없다 — 찬 부채꼴이면 안.
        if (angle >= 360f || distance < Epsilon)
            return true;

        float half = Mathf.Clamp(angle, 0f, 360f) * 0.5f;
        return Vector2.Angle(Vector2.up, local) <= half + AngleEpsilon;
    }

    public override Vector2 ClosestLocal(Vector2 local)
    {
        if (ContainsLocal(local))
            return local;

        float r = Mathf.Max(0f, range);
        float inner = Mathf.Clamp(minOffset, 0f, r);
        float half = Mathf.Clamp(angle, 0f, 360f) * 0.5f;
        // 각 안 — 거리만 [minOffset, range] 로.
        if (angle >= 360f || (local.sqrMagnitude >= 1e-12f && Vector2.Angle(Vector2.up, local) <= half))
            return ClosestInRing(local, inner, r);

        // 각 밖 — 두 옆 변(minOffset → range, 호 끝점 포함) 중 가까운 쪽.
        float rad = half * Mathf.Deg2Rad;
        var right = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
        var left = new Vector2(-right.x, right.y);
        Vector2 onRight = ClosestOnSegment(local, right * inner, right * r);
        Vector2 onLeft = ClosestOnSegment(local, left * inner, left * r);
        return (onRight - local).sqrMagnitude <= (onLeft - local).sqrMagnitude ? onRight : onLeft;
    }

    // 180° 넘는(360 미만) 부채꼴은 볼록이 아니라 정면 기준 오른쪽·왼쪽 반으로 나눈다.
    public override int ConvexPieceCount => angle > 180f && angle < 360f ? 2 : 1;

    public override bool HasHole => minOffset > Epsilon;

    public override Vector2 SupportLocal(int piece, Vector2 direction)
    {
        float r = Mathf.Max(0f, range);
        if (angle >= 360f)
            return CircleSupport(direction, r);

        float half = Mathf.Clamp(angle, 0f, 360f) * 0.5f;
        float from = -half, to = half;
        if (ConvexPieceCount == 2)
        {
            if (piece == 0) from = 0f;
            else to = 0f;
        }

        // 꼭짓점·호 두 끝, 방향이 호 안이면 그 방향 호 위 점.
        Vector2 best = FarthestOf(direction, Vector2.zero, Edge(from) * r, Edge(to) * r);
        if (direction.sqrMagnitude >= 1e-12f)
        {
            float bearing = Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg; // + = 오른쪽
            if (bearing >= from && bearing <= to)
                best = direction.normalized * r;
        }
        return best;
    }

    private static Vector2 Edge(float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
    }

    public override void GetLocalBounds(out Vector2 min, out Vector2 max)
    {
        float r = Mathf.Max(0f, range);
        float half = Mathf.Clamp(angle, 0f, 360f) * 0.5f * Mathf.Deg2Rad;
        float inner = Mathf.Clamp(minOffset, 0f, r);
        if (half <= Mathf.PI * 0.5f)
        {
            float x = r * Mathf.Sin(half);
            min = new Vector2(-x, inner * Mathf.Cos(half));
            max = new Vector2(x, r);
        }
        else
        {
            min = new Vector2(-r, r * Mathf.Cos(half));
            max = new Vector2(r, r);
        }
    }

    protected override void ApplyScale(float lengthMultiplier, float widthMultiplier)
    {
        range *= lengthMultiplier;
        angle = Mathf.Min(360f, angle * widthMultiplier);
    }

    protected override void AppendLocalOutlines(List<Vector2[]> loops, int arcSegments)
    {
        float r = Mathf.Max(0f, range);
        float inner = Mathf.Clamp(minOffset, 0f, r);
        if (angle >= 360f)
        {
            loops.Add(Circle(r, arcSegments));
            if (inner > 0f)
                loops.Add(Circle(inner, arcSegments));
            return;
        }

        float half = Mathf.Clamp(angle, 0f, 360f) * 0.5f;
        var points = new List<Vector2>(arcSegments * 2 + 2);
        AddArc(points, r, -half, half, arcSegments);
        if (inner > 0f)
            AddArc(points, inner, half, -half, arcSegments);
        else
            points.Add(Vector2.zero);
        loops.Add(points.ToArray());
    }

    public override void Validate(List<string> errors)
    {
        base.Validate(errors);
        Check(errors, nameof(range), range);
        Check(errors, nameof(angle), angle);
        Check(errors, nameof(minOffset), minOffset);
        if (minOffset > range)
            errors.Add($"{nameof(AttackSectorShape)}: minOffset({minOffset}) > range({range})");
    }
}

/// <summary>원 — 반경 <see cref="radius"/>. <see cref="minRadius"/> &gt; 0 이면 도넛.</summary>
[Serializable]
public sealed class AttackCircleShape : AttackShape
{
    [Tooltip("반경(m)")]
    [Min(0f)] [SerializeField] private float radius = 2f;

    [Tooltip("안쪽 빈 반경(m) — 0 보다 크면 도넛")]
    [Min(0f)] [SerializeField] private float minRadius;

    public AttackCircleShape() { }

    public AttackCircleShape(float radius, float minRadius = 0f)
    {
        this.radius = radius;
        this.minRadius = minRadius;
    }

    public float Radius { get => radius; set => radius = value; }
    public float MinRadius { get => minRadius; set => minRadius = value; }

    public override bool ContainsLocal(Vector2 local)
    {
        float distance = local.magnitude;
        return distance <= radius + Epsilon && distance >= minRadius - Epsilon;
    }

    public override Vector2 ClosestLocal(Vector2 local)
    {
        float r = Mathf.Max(0f, radius);
        return ClosestInRing(local, Mathf.Clamp(minRadius, 0f, r), r);
    }

    public override bool HasHole => minRadius > Epsilon;

    public override Vector2 SupportLocal(int piece, Vector2 direction) => CircleSupport(direction, Mathf.Max(0f, radius));

    public override void GetLocalBounds(out Vector2 min, out Vector2 max)
    {
        float r = Mathf.Max(0f, radius);
        min = new Vector2(-r, -r);
        max = new Vector2(r, r);
    }

    protected override void ApplyScale(float lengthMultiplier, float widthMultiplier) => radius *= lengthMultiplier;

    protected override void AppendLocalOutlines(List<Vector2[]> loops, int arcSegments)
    {
        loops.Add(Circle(Mathf.Max(0f, radius), arcSegments));
        if (minRadius > 0f)
            loops.Add(Circle(Mathf.Min(minRadius, Mathf.Max(0f, radius)), arcSegments));
    }

    public override void Validate(List<string> errors)
    {
        base.Validate(errors);
        Check(errors, nameof(radius), radius);
        Check(errors, nameof(minRadius), minRadius);
        if (minRadius > radius)
            errors.Add($"{nameof(AttackCircleShape)}: minRadius({minRadius}) > radius({radius})");
    }
}

/// <summary>직사각형 — 정면으로 <see cref="length"/>, 가로 <see cref="width"/>(좌우 반씩), 기준점 뒤로 <see cref="backOffset"/> 만큼 더.</summary>
[Serializable]
public sealed class AttackRectShape : AttackShape
{
    [Tooltip("기준점에서 앞으로 뻗는 길이(m)")]
    [Min(0f)] [SerializeField] private float length = 2f;

    [Tooltip("가로 폭(m, 좌우 반씩)")]
    [Min(0f)] [SerializeField] private float width = 1f;

    [Tooltip("기준점 뒤로 들어오는 길이(m)")]
    [Min(0f)] [SerializeField] private float backOffset;

    public AttackRectShape() { }

    public AttackRectShape(float length, float width, float backOffset = 0f)
    {
        this.length = length;
        this.width = width;
        this.backOffset = backOffset;
    }

    public float Length { get => length; set => length = value; }
    public float Width { get => width; set => width = value; }
    public float BackOffset { get => backOffset; set => backOffset = value; }

    public override bool ContainsLocal(Vector2 local) =>
        local.y <= length + Epsilon && local.y >= -backOffset - Epsilon && Mathf.Abs(local.x) <= width * 0.5f + Epsilon;

    public override Vector2 ClosestLocal(Vector2 local)
    {
        GetLocalBounds(out Vector2 min, out Vector2 max);
        return new Vector2(Mathf.Clamp(local.x, min.x, max.x), Mathf.Clamp(local.y, min.y, max.y));
    }

    public override Vector2 SupportLocal(int piece, Vector2 direction)
    {
        GetLocalBounds(out Vector2 min, out Vector2 max);
        return new Vector2(direction.x >= 0f ? max.x : min.x, direction.y >= 0f ? max.y : min.y);
    }

    public override void GetLocalBounds(out Vector2 min, out Vector2 max)
    {
        float halfWidth = Mathf.Max(0f, width) * 0.5f;
        min = new Vector2(-halfWidth, -Mathf.Max(0f, backOffset));
        max = new Vector2(halfWidth, Mathf.Max(0f, length));
    }

    protected override void ApplyScale(float lengthMultiplier, float widthMultiplier)
    {
        length *= lengthMultiplier;
        width *= widthMultiplier;
    }

    protected override void AppendLocalOutlines(List<Vector2[]> loops, int arcSegments)
    {
        GetLocalBounds(out Vector2 min, out Vector2 max);
        loops.Add(new[] { new Vector2(min.x, min.y), new Vector2(min.x, max.y), new Vector2(max.x, max.y), new Vector2(max.x, min.y) });
    }

    public override void Validate(List<string> errors)
    {
        base.Validate(errors);
        Check(errors, nameof(length), length);
        Check(errors, nameof(width), width);
        Check(errors, nameof(backOffset), backOffset);
    }
}

/// <summary>삼각형 — 꼭짓점 = 기준점(도형 포즈), 정면으로 <see cref="length"/>, 끝 밑변 폭 <see cref="width"/>.</summary>
[Serializable]
public sealed class AttackTriangleShape : AttackShape
{
    [Tooltip("꼭짓점에서 밑변까지 길이(m)")]
    [Min(0f)] [SerializeField] private float length = 2f;

    [Tooltip("끝 밑변 폭(m)")]
    [Min(0f)] [SerializeField] private float width = 2f;

    public AttackTriangleShape() { }

    public AttackTriangleShape(float length, float width)
    {
        this.length = length;
        this.width = width;
    }

    public float Length { get => length; set => length = value; }
    public float Width { get => width; set => width = value; }

    public override bool ContainsLocal(Vector2 local)
    {
        if (!(length > 0f) || local.y < -Epsilon || local.y > length + Epsilon)
            return false;

        float halfAt = width * 0.5f * Mathf.Clamp01(local.y / length);
        return Mathf.Abs(local.x) <= halfAt + Epsilon;
    }

    public override bool IsEmpty => !(length > 0f);

    public override Vector2 SupportLocal(int piece, Vector2 direction)
    {
        float l = Mathf.Max(0f, length);
        float halfWidth = Mathf.Max(0f, width) * 0.5f;
        return FarthestOf(direction, Vector2.zero, new Vector2(-halfWidth, l), new Vector2(halfWidth, l));
    }

    public override Vector2 ClosestLocal(Vector2 local)
    {
        if (ContainsLocal(local))
            return local;

        // 세 변 중 가까운 쪽. 길이 0 이면 판정 없음(IsEmpty) — 꼭짓점을 돌려줘도 겹침이 아니다.
        float l = Mathf.Max(0f, length);
        float halfWidth = Mathf.Max(0f, width) * 0.5f;
        var baseLeft = new Vector2(-halfWidth, l);
        var baseRight = new Vector2(halfWidth, l);
        Vector2 best = ClosestOnSegment(local, Vector2.zero, baseLeft);
        Vector2 candidate = ClosestOnSegment(local, baseLeft, baseRight);
        if ((candidate - local).sqrMagnitude < (best - local).sqrMagnitude)
            best = candidate;
        candidate = ClosestOnSegment(local, baseRight, Vector2.zero);
        if ((candidate - local).sqrMagnitude < (best - local).sqrMagnitude)
            best = candidate;
        return best;
    }

    public override void GetLocalBounds(out Vector2 min, out Vector2 max)
    {
        float halfWidth = Mathf.Max(0f, width) * 0.5f;
        min = new Vector2(-halfWidth, 0f);
        max = new Vector2(halfWidth, Mathf.Max(0f, length));
    }

    protected override void ApplyScale(float lengthMultiplier, float widthMultiplier)
    {
        length *= lengthMultiplier;
        width *= widthMultiplier;
    }

    protected override void AppendLocalOutlines(List<Vector2[]> loops, int arcSegments)
    {
        float l = Mathf.Max(0f, length);
        float halfWidth = Mathf.Max(0f, width) * 0.5f;
        loops.Add(new[] { Vector2.zero, new Vector2(-halfWidth, l), new Vector2(halfWidth, l) });
    }

    public override void Validate(List<string> errors)
    {
        base.Validate(errors);
        Check(errors, nameof(length), length);
        Check(errors, nameof(width), width);
    }
}
