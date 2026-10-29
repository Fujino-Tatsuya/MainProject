using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>도형 판정에 걸린 대상 1개(할 일 153).</summary>
public readonly struct AttackShapeHit
{
    public readonly Collider Collider;
    /// <summary>1회 피격 키(Unit, 없으면 Hurtbox 등 — 호출자의 해석 함수가 정한다).</summary>
    public readonly Object Target;
    /// <summary>겹친 점 — 몸(표면 또는 안)의 점 중 그 도형 안인 점.</summary>
    public readonly Vector3 Point;

    public AttackShapeHit(Collider collider, Object target, Vector3 point)
    {
        Collider = collider;
        Target = target;
        Point = point;
    }
}

/// <summary>
/// 판정 대상 몸(볼록). 실제 판정은 <see cref="AttackShapeColliderBody"/>, 테스트는 Physics 없는 해석 몸을 넣는다.
/// 구조체로 넘겨(제네릭 제약) 질의마다 할당하지 않는다.
/// </summary>
public interface IAttackShapeBody
{
    /// <summary>몸 중심(경계 상자 중심) — 탐색 첫 방향·겹친 점 근사의 출발점.</summary>
    Vector3 Center { get; }

    /// <summary>지지점 — 월드 방향으로 가장 먼 몸의 점. 겹침 판정(GJK)이 쓴다.</summary>
    Vector3 Support(Vector3 direction);

    /// <summary>주어진 점에 가장 가까운 몸의 점(안이면 그 점 그대로). 빈 곳 있는 도형 다듬기·겹친 점 근사가 쓴다.</summary>
    Vector3 ClosestPoint(Vector3 point);
}

/// <summary>
/// 콜라이더 몸 — 지지점을 콜라이더 치수에서 직접 계산한다(Physics 호출 없음): 구·캡슐·상자·CharacterController(세운 캡슐)·
/// 볼록 MeshCollider(읽을 수 있는 메시면 정점, 정점 배열은 메시마다 한 번 캐시). 그 밖(비볼록 MeshCollider·Terrain 등)은 경계 상자(AABB).
/// 최근접 점은 <see cref="AttackShapeQuery.ClosestPoint(Collider, Vector3)"/>.
/// </summary>
public readonly struct AttackShapeColliderBody : IAttackShapeBody
{
    private enum Kind { Box, Capsule, Points }

    private static readonly Dictionary<Mesh, Vector3[]> MeshPoints = new Dictionary<Mesh, Vector3[]>();

    private readonly Collider collider;
    private readonly Kind kind;
    private readonly Vector3 center;
    // 상자 = 세 반축(월드), 캡슐 = 선분 반쪽(axisX)·반경.
    private readonly Vector3 axisX, axisY, axisZ;
    private readonly float radius;
    private readonly Vector3[] points;
    private readonly Matrix4x4 localToWorld;

    public AttackShapeColliderBody(Collider collider)
    {
        this.collider = collider;
        axisX = axisY = axisZ = Vector3.zero;
        radius = 0f;
        points = null;
        localToWorld = Matrix4x4.identity;
        Transform t = collider.transform;
        Vector3 scale = Abs(t.lossyScale);

        switch (collider)
        {
            case SphereCollider sphere:
                kind = Kind.Capsule;
                center = t.TransformPoint(sphere.center);
                radius = sphere.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
                break;
            case CapsuleCollider capsule:
            {
                kind = Kind.Capsule;
                center = t.TransformPoint(capsule.center);
                int axis = Mathf.Clamp(capsule.direction, 0, 2);
                float axisScale = scale[axis];
                float sideScale = Mathf.Max(scale[(axis + 1) % 3], scale[(axis + 2) % 3]);
                radius = capsule.radius * sideScale;
                Vector3 local = Vector3.zero;
                local[axis] = 1f;
                axisX = t.rotation * local * Mathf.Max(0f, capsule.height * axisScale * 0.5f - radius);
                break;
            }
            case CharacterController controller:
                kind = Kind.Capsule;
                center = t.TransformPoint(controller.center);
                radius = controller.radius * Mathf.Max(scale.x, scale.z);
                axisX = Vector3.up * Mathf.Max(0f, controller.height * scale.y * 0.5f - radius);
                break;
            case BoxCollider box:
                kind = Kind.Box;
                center = t.TransformPoint(box.center);
                Vector3 half = box.size * 0.5f;
                axisX = t.TransformVector(new Vector3(half.x, 0f, 0f));
                axisY = t.TransformVector(new Vector3(0f, half.y, 0f));
                axisZ = t.TransformVector(new Vector3(0f, 0f, half.z));
                break;
            case MeshCollider mesh when mesh.convex && TryMeshPoints(mesh.sharedMesh, out Vector3[] vertices):
                kind = Kind.Points;
                center = collider.bounds.center;
                points = vertices;
                localToWorld = t.localToWorldMatrix;
                break;
            default:
            {
                Bounds bounds = collider.bounds;
                kind = Kind.Box;
                center = bounds.center;
                axisX = new Vector3(bounds.extents.x, 0f, 0f);
                axisY = new Vector3(0f, bounds.extents.y, 0f);
                axisZ = new Vector3(0f, 0f, bounds.extents.z);
                break;
            }
        }
    }

    public Vector3 Center => center;

    public Vector3 Support(Vector3 direction)
    {
        switch (kind)
        {
            case Kind.Capsule:
            {
                Vector3 end = Vector3.Dot(direction, axisX) >= 0f ? center + axisX : center - axisX;
                float length = direction.magnitude;
                return length < 1e-12f ? end : end + direction * (radius / length);
            }
            case Kind.Points:
            {
                Vector3 local = localToWorld.transpose.MultiplyVector(direction);
                Vector3 best = points[0];
                float bestDot = Vector3.Dot(best, local);
                for (int i = 1; i < points.Length; i++)
                {
                    float dot = Vector3.Dot(points[i], local);
                    if (dot > bestDot)
                    {
                        bestDot = dot;
                        best = points[i];
                    }
                }
                return localToWorld.MultiplyPoint3x4(best);
            }
            default:
                return center
                       + (Vector3.Dot(direction, axisX) >= 0f ? axisX : -axisX)
                       + (Vector3.Dot(direction, axisY) >= 0f ? axisY : -axisY)
                       + (Vector3.Dot(direction, axisZ) >= 0f ? axisZ : -axisZ);
        }
    }

    public Vector3 ClosestPoint(Vector3 point) => AttackShapeQuery.ClosestPoint(collider, point);

    private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    // 읽을 수 없는 메시(빌드에서 Read/Write 꺼짐)·빈 메시는 경계 상자로.
    private static bool TryMeshPoints(Mesh mesh, out Vector3[] vertices)
    {
        vertices = null;
        if (mesh == null)
            return false;
        if (!MeshPoints.TryGetValue(mesh, out vertices))
        {
            vertices = mesh.isReadable ? mesh.vertices : null;
            MeshPoints[mesh] = vertices;
        }
        return vertices != null && vertices.Length > 0;
    }
}

/// <summary>
/// [서버] 도형 판정 질의(할 일 153). 규칙 = <b>도형 영역(높이 포함 3D)과 대상 몸이 조금이라도 겹치면 맞음</b>(예전 콜라이더 겹침과 같다).
/// 흐름: 도형마다 넓은 상자 <c>Physics.OverlapBoxNonAlloc</c> → 걸린 콜라이더마다 <see cref="Overlaps{TBody}"/>(볼록 조각 × 몸 GJK, 빈 곳 있는 도형은 교대 투영으로 다듬기)
/// → 도형 하나라도 겹치면 피격 → 같은 대상은 한 공격에 1회.
/// 순수 부분(<see cref="Overlaps{TBody}"/>·<see cref="AnyOverlaps{TBody}"/>·<see cref="AnyContains"/>·<see cref="TryAccept{TKey,TBody}"/>)은 Physics 없이 테스트한다.
/// </summary>
public static class AttackShapeQuery
{
    // GJK 반복 상한 — 볼록 두 집합은 보통 10 회 안에 끝난다. 넘으면(퇴화) 겹치지 않음으로 본다.
    private const int GjkMaxIterations = 32;

    // 빈 곳 있는 도형(도넛)의 교대 투영 상한 — 몸 ClosestPoint 호출 수.
    private const int HoleIterations = 8;

    // 도형 쪽 점이 이만큼도 안 움직이면 수렴 — 거리 제곱(m²).
    private const float ConvergedSqr = 1e-10f;

    /// <summary>
    /// 도형 하나와 몸이 겹치는가(경계 포함 — 도형을 <see cref="AttackShape.Epsilon"/> 만큼 부풀린 GJK).
    /// 볼록 조각(<see cref="AttackShape.ConvexPieceCount"/>) 하나라도 몸과 겹치면 겹침. 빈 곳 있는 도형(<see cref="AttackShape.HasHole"/>)은 조각이 빈 곳을 채운 껍질이라
    /// 껍질이 겹친 뒤 도형 최근접 ↔ 몸 최근접 교대 투영으로 빈 곳 밖 겹침을 다시 찾는다.
    /// 기준점·원 중심이 몸 안인 경우도 그냥 겹침이다(예전 '발밑 겹침' 특례 없음).
    /// <paramref name="point"/> = 겹친 점 근사(몸 중심에 가장 가까운 도형 점 → 그 점에 가장 가까운 몸 점, 도형 밖이면 도형 점).
    /// </summary>
    public static bool Overlaps<TBody>(AttackShape shape, in AttackShapePose pose, in TBody body, out Vector3 point)
        where TBody : IAttackShapeBody
    {
        point = default;
        if (shape == null || shape.IsEmpty)
            return false;

        bool hull = false;
        for (int piece = 0; piece < shape.ConvexPieceCount && !hull; piece++)
            hull = Gjk(shape, pose, piece, body);
        if (!hull)
            return false;

        if (shape.HasHole)
            return ProjectionOverlaps(shape, pose, body, HoleIterations, out point);

        Vector3 onShape = shape.ClosestPoint(pose, body.Center);
        Vector3 onBody = body.ClosestPoint(onShape);
        point = shape.Contains(pose, onBody) ? onBody : onShape;
        return true;
    }

    // 교대 투영 — 도형 최근접 ↔ 몸 최근접. 몸 최근접 = 도형 점 그대로(도형 점이 몸 안)거나 몸 점이 도형 안이면 겹침.
    // Contains 만 보지 않는 이유: 먼 월드 좌표(수천 m)에선 float 해상도가 경계 허용 오차보다 커서 경계 위 도형 점이 밖으로 읽힐 수 있다.
    private static bool ProjectionOverlaps<TBody>(AttackShape shape, in AttackShapePose pose, in TBody body, int iterations, out Vector3 point)
        where TBody : IAttackShapeBody
    {
        Vector3 onShape = shape.ClosestPoint(pose, body.Center);
        point = onShape;
        for (int i = 0; i < iterations; i++)
        {
            point = body.ClosestPoint(onShape);
            if ((point - onShape).sqrMagnitude <= AttackShape.Epsilon * AttackShape.Epsilon || shape.Contains(pose, point))
                return true;

            Vector3 next = shape.ClosestPoint(pose, point);
            if ((next - onShape).sqrMagnitude < ConvergedSqr)
                return false;
            onShape = next;
        }

        return false;
    }

    // ── GJK(불리언) — 민코프스키 차(도형 조각 − 몸)가 원점을 품는가 ─────────────────────

    private struct Simplex
    {
        public Vector3 A, B, C, D; // A = 가장 새 점
        public int Count;
    }

    private static Vector3 MinkowskiSupport<TBody>(AttackShape shape, in AttackShapePose pose, int piece, in TBody body, Vector3 direction)
        where TBody : IAttackShapeBody =>
        shape.Support(pose, piece, direction) + direction.normalized * AttackShape.Epsilon - body.Support(-direction);

    private static bool Gjk<TBody>(AttackShape shape, in AttackShapePose pose, int piece, in TBody body)
        where TBody : IAttackShapeBody
    {
        Vector3 direction = body.Center - pose.Position;
        if (direction.sqrMagnitude < 1e-12f)
            direction = Vector3.up;

        var simplex = new Simplex { A = MinkowskiSupport(shape, pose, piece, body, direction), Count = 1 };
        direction = -simplex.A;
        for (int i = 0; i < GjkMaxIterations; i++)
        {
            if (direction.sqrMagnitude < 1e-14f)
                return true; // 원점이 단체 위

            Vector3 next = MinkowskiSupport(shape, pose, piece, body, direction);
            if (Vector3.Dot(next, direction) <= 0f)
                return false; // 이 방향으로 원점을 못 넘는다 = 떨어짐

            simplex.D = simplex.C;
            simplex.C = simplex.B;
            simplex.B = simplex.A;
            simplex.A = next;
            simplex.Count++;
            if (NextSimplex(ref simplex, ref direction))
                return true;
        }

        return false;
    }

    private static bool NextSimplex(ref Simplex s, ref Vector3 direction)
    {
        switch (s.Count)
        {
            case 2: return Line(ref s, ref direction);
            case 3: return Triangle(ref s, ref direction);
            default: return Tetrahedron(ref s, ref direction);
        }
    }

    private static bool Line(ref Simplex s, ref Vector3 direction)
    {
        Vector3 ab = s.B - s.A;
        Vector3 ao = -s.A;
        if (Vector3.Dot(ab, ao) > 0f)
        {
            s.Count = 2;
            direction = Vector3.Cross(Vector3.Cross(ab, ao), ab); // 0 = 원점이 선분 위 → 다음 반복에서 겹침
        }
        else
        {
            s.Count = 1;
            direction = ao;
        }
        return false;
    }

    private static bool Triangle(ref Simplex s, ref Vector3 direction)
    {
        Vector3 ab = s.B - s.A;
        Vector3 ac = s.C - s.A;
        Vector3 ao = -s.A;
        Vector3 abc = Vector3.Cross(ab, ac);

        if (Vector3.Dot(Vector3.Cross(abc, ac), ao) > 0f)
        {
            if (Vector3.Dot(ac, ao) > 0f)
            {
                s.B = s.C;
                s.Count = 2;
                direction = Vector3.Cross(Vector3.Cross(ac, ao), ac);
                return false;
            }
            s.Count = 2;
            return Line(ref s, ref direction);
        }

        if (Vector3.Dot(Vector3.Cross(ab, abc), ao) > 0f)
        {
            s.Count = 2;
            return Line(ref s, ref direction);
        }

        s.Count = 3;
        float side = Vector3.Dot(abc, ao);
        if (side > 0f)
            direction = abc;
        else if (side < 0f)
        {
            (s.B, s.C) = (s.C, s.B);
            direction = -abc;
        }
        else
            return true; // 원점이 삼각형 안
        return false;
    }

    private static bool Tetrahedron(ref Simplex s, ref Vector3 direction)
    {
        Vector3 ab = s.B - s.A;
        Vector3 ac = s.C - s.A;
        Vector3 ad = s.D - s.A;
        Vector3 ao = -s.A;

        if (Vector3.Dot(Vector3.Cross(ab, ac), ao) > 0f)
        {
            s.Count = 3; // A B C
            return Triangle(ref s, ref direction);
        }
        if (Vector3.Dot(Vector3.Cross(ac, ad), ao) > 0f)
        {
            s.B = s.C; // A C D
            s.C = s.D;
            s.Count = 3;
            return Triangle(ref s, ref direction);
        }
        if (Vector3.Dot(Vector3.Cross(ad, ab), ao) > 0f)
        {
            s.C = s.B; // A D B
            s.B = s.D;
            s.Count = 3;
            return Triangle(ref s, ref direction);
        }
        return true; // 원점이 사면체 안
    }

    /// <summary>합집합 — 도형 하나라도 몸과 겹치는가. null 원소는 건너뛴다.</summary>
    public static bool AnyOverlaps<TBody>(IReadOnlyList<AttackShape> shapes, in AttackShapePose pose, in TBody body, out Vector3 point)
        where TBody : IAttackShapeBody
    {
        point = default;
        if (shapes == null)
            return false;

        for (int i = 0; i < shapes.Count; i++)
        {
            if (shapes[i] != null && Overlaps(shapes[i], pose, body, out point))
                return true;
        }

        return false;
    }

    /// <summary><see cref="AttackShapeSet"/> 판 — 콜라이더 하나가 영역과 겹치는가(예: 반격 대상이 수호의 파동 원에도 걸리는가).</summary>
    public static bool Overlaps(AttackShapeSet set, in AttackShapePose pose, Collider collider) =>
        set != null && collider != null && AnyOverlaps(set.Shapes, pose, new AttackShapeColliderBody(collider), out _);

    /// <summary>합집합 — 도형 하나라도 점을 품는가. null 원소는 건너뛴다.</summary>
    public static bool AnyContains(IReadOnlyList<AttackShape> shapes, in AttackShapePose pose, Vector3 point)
    {
        if (shapes == null)
            return false;

        for (int i = 0; i < shapes.Count; i++)
        {
            if (shapes[i] != null && shapes[i].Contains(pose, point))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 순수 판정 1건 — 몸이 합집합과 겹치고 <paramref name="key"/> 가 이 공격에서 처음이면 true(그리고 <paramref name="seen"/> 에 넣는다).
    /// 키가 null 이면 false(대상 아님 — 자기 자신 등은 해석 함수가 null 을 낸다). 밖이면 seen 에 넣지 않는다(같은 대상의 다른 콜라이더가 안이면 맞는다).
    /// </summary>
    public static bool TryAccept<TKey, TBody>(IReadOnlyList<AttackShape> shapes, in AttackShapePose pose, in TBody body,
        TKey key, HashSet<TKey> seen, out Vector3 point)
        where TBody : IAttackShapeBody
    {
        point = default;
        if (key == null || seen.Contains(key) || !AnyOverlaps(shapes, pose, body, out point))
            return false;

        return seen.Add(key);
    }

    /// <summary>
    /// [서버] 도형 영역과 겹치는 대상을 모은다(<paramref name="results"/> 에 덧붙임, 반환 = 이번에 더한 수).
    /// <paramref name="resolveTarget"/> = 콜라이더 → 1회 피격 키(Unit, 없으면 Hurtbox). 시전자 자신·대상 아님은 null.
    /// <paramref name="seen"/> = 이 공격에서 이미 맞은 키 — 공격 하나 동안 유지하고 새 공격에서 비운다(여러 틱·여러 도형에 걸쳐도 1회).
    /// <paramref name="buffer"/> 가 꽉 차면 넘친 콜라이더는 빠진다(크기는 호출자가 정한다).
    /// </summary>
    public static int Collect(IReadOnlyList<AttackShape> shapes, in AttackShapePose pose, int layerMask, QueryTriggerInteraction triggers,
        Collider[] buffer, Func<Collider, Object> resolveTarget, HashSet<Object> seen, List<AttackShapeHit> results)
    {
        if (shapes == null || buffer == null || resolveTarget == null || seen == null || results == null)
            return 0;

        int added = 0;
        for (int s = 0; s < shapes.Count; s++)
        {
            AttackShape shape = shapes[s];
            if (shape == null)
                continue;

            shape.GetBroadBox(pose, out Vector3 center, out Vector3 halfExtents, out Quaternion rotation);
            int count = Physics.OverlapBoxNonAlloc(center, halfExtents, buffer, rotation, layerMask, triggers);
            for (int i = 0; i < count; i++)
            {
                Collider hit = buffer[i];
                if (hit == null)
                    continue;

                Object target = resolveTarget(hit);
                if (target == null || seen.Contains(target))
                    continue;

                if (!TryAccept(shapes, pose, new AttackShapeColliderBody(hit), target, seen, out Vector3 point))
                    continue;

                results.Add(new AttackShapeHit(hit, target, point));
                added++;
            }
        }

        return added;
    }

    /// <summary><see cref="AttackShapeSet"/> 판. 특성 배율은 <see cref="AttackShapeSet.Scaled"/> 복사본을 넘긴다.</summary>
    public static int Collect(AttackShapeSet set, in AttackShapePose pose, int layerMask, QueryTriggerInteraction triggers,
        Collider[] buffer, Func<Collider, Object> resolveTarget, HashSet<Object> seen, List<AttackShapeHit> results) =>
        set == null ? 0 : Collect(set.Shapes, pose, layerMask, triggers, buffer, resolveTarget, seen, results);

    /// <summary>몸에서 <paramref name="point"/> 에 가장 가까운 점(안이면 그대로). 비볼록 MeshCollider 는 ClosestPoint 를 못 써 경계 상자로(<c>UnitWallImpacts</c> 와 같다).</summary>
    public static Vector3 ClosestPoint(Collider collider, Vector3 point) =>
        collider is MeshCollider mesh && !mesh.convex ? collider.ClosestPointOnBounds(point) : collider.ClosestPoint(point);
}
