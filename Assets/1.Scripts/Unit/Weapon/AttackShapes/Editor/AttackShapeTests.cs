using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 할 일 153 1/6 — 공격 판정 도형 코어. 도형별 안/밖 경계 · 도넛(minOffset·minRadius) · 오프셋(앞·옆·회전) · 포즈 회전 ·
/// 배율 Scale · 높이 범위 · 넓은 상자 ⊇ 판정 · 외곽선 = 판정 경계 · 최근접 점(무차별 대입 대조) · 몸 겹침(구 해석 몸 — 스침·도넛·기준점이 몸 안) ·
/// 합집합·1회 피격 · 실제 Overlap 수집 · 직사각형 = Physics.OverlapBox 대조(무작위 상자·캡슐·구) · 데이터 테이블 행.
/// </summary>
public class AttackShapeTests
{
    private static readonly AttackShapePose Origin = new AttackShapePose(Vector3.zero, Vector3.forward);
    private readonly List<Object> created = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (Object o in created)
            Object.DestroyImmediate(o);
        created.Clear();
    }

    private static Vector3 P(float x, float z, float y = 0f) => new Vector3(x, y, z);

    private static Vector3 AtAngle(float degrees, float distance) =>
        new Vector3(Mathf.Sin(degrees * Mathf.Deg2Rad) * distance, 0f, Mathf.Cos(degrees * Mathf.Deg2Rad) * distance);

    // ── 도형별 경계 ─────────────────────────────────────────────────────

    [Test]
    public void Sector_RangeAndAngleBoundaries()
    {
        var sector = new AttackSectorShape(range: 2f, angle: 90f);
        Assert.IsTrue(sector.Contains(Origin, P(0f, 1.99f)));
        Assert.IsTrue(sector.Contains(Origin, P(0f, 2f)), "경계 포함");
        Assert.IsFalse(sector.Contains(Origin, P(0f, 2.01f)));
        Assert.IsTrue(sector.Contains(Origin, AtAngle(44f, 1.5f)));
        Assert.IsTrue(sector.Contains(Origin, AtAngle(-44f, 1.5f)));
        Assert.IsFalse(sector.Contains(Origin, AtAngle(46f, 1.5f)));
        Assert.IsFalse(sector.Contains(Origin, P(0f, -1f)), "뒤");
        Assert.IsTrue(sector.Contains(Origin, Vector3.zero), "꼭짓점(발밑) = 찬 부채꼴이면 안 — 예전 QBlastInSector 와 같다");
    }

    [Test]
    public void Sector_FullCircleAndDonut()
    {
        var full = new AttackSectorShape(2f, 360f);
        Assert.IsTrue(full.Contains(Origin, P(0f, -1.9f)), "360° = 뒤도 안");

        var donut = new AttackSectorShape(2f, 90f, minOffset: 1f);
        Assert.IsFalse(donut.Contains(Origin, P(0f, 0.99f)), "안쪽 빈 반경");
        Assert.IsTrue(donut.Contains(Origin, P(0f, 1f)), "안쪽 경계 포함");
        Assert.IsTrue(donut.Contains(Origin, P(0f, 1.5f)));
        Assert.IsFalse(donut.Contains(Origin, Vector3.zero), "도넛이면 꼭짓점은 밖");
        Assert.IsFalse(donut.Contains(Origin, AtAngle(50f, 1.5f)));
    }

    [Test]
    public void Circle_RadiusAndDonut()
    {
        var circle = new AttackCircleShape(2f);
        Assert.IsTrue(circle.Contains(Origin, P(-1.4f, -1.4f)));
        Assert.IsTrue(circle.Contains(Origin, Vector3.zero));
        Assert.IsFalse(circle.Contains(Origin, P(1.5f, 1.5f)));

        var ring = new AttackCircleShape(2f, minRadius: 1f);
        Assert.IsFalse(ring.Contains(Origin, P(0.5f, 0f)));
        Assert.IsTrue(ring.Contains(Origin, P(0f, -1.5f)));
        Assert.IsFalse(ring.Contains(Origin, P(0f, 2.1f)));
    }

    [Test]
    public void Rect_LengthWidthBackOffset()
    {
        var rect = new AttackRectShape(length: 2f, width: 1f, backOffset: 0.5f);
        Assert.IsTrue(rect.Contains(Origin, P(0.49f, 1.9f)));
        Assert.IsTrue(rect.Contains(Origin, P(0.5f, 2f)), "모서리 포함");
        Assert.IsFalse(rect.Contains(Origin, P(0.51f, 1f)));
        Assert.IsFalse(rect.Contains(Origin, P(0f, 2.01f)));
        Assert.IsTrue(rect.Contains(Origin, P(0f, -0.49f)), "뒤로 들어오는 길이");
        Assert.IsFalse(rect.Contains(Origin, P(0f, -0.51f)));
    }

    [Test]
    public void Triangle_ApexAtOriginBaseAtLength()
    {
        var tri = new AttackTriangleShape(length: 2f, width: 2f);
        Assert.IsTrue(tri.Contains(Origin, Vector3.zero), "꼭짓점 = 기준점");
        Assert.IsTrue(tri.Contains(Origin, P(0.5f, 1f)), "y=1 에서 반폭 0.5");
        Assert.IsFalse(tri.Contains(Origin, P(0.6f, 1f)));
        Assert.IsTrue(tri.Contains(Origin, P(-0.9f, 1.9f)));
        Assert.IsFalse(tri.Contains(Origin, P(0f, 2.01f)));
        Assert.IsFalse(tri.Contains(Origin, P(0f, -0.01f)));
        Assert.IsFalse(new AttackTriangleShape(0f, 2f).Contains(Origin, Vector3.zero), "길이 0 = 판정 없음");
    }

    // ── 오프셋·포즈 ─────────────────────────────────────────────────────

    [Test]
    public void Offsets_ForwardSideYaw()
    {
        var forward = new AttackRectShape(1f, 1f) { ForwardOffset = 3f };
        Assert.IsTrue(forward.Contains(Origin, P(0f, 3.5f)));
        Assert.IsFalse(forward.Contains(Origin, P(0f, 0.5f)));

        var side = new AttackRectShape(1f, 1f) { SideOffset = 2f };
        Assert.IsTrue(side.Contains(Origin, P(2f, 0.5f)), "+ = 오른쪽");
        Assert.IsFalse(side.Contains(Origin, P(0f, 0.5f)));

        var yawed = new AttackRectShape(1f, 0.5f) { YawOffset = 90f };
        Assert.IsTrue(yawed.Contains(Origin, P(0.5f, 0f)), "+90° = 오른쪽을 본다(위에서 시계 방향)");
        Assert.IsFalse(yawed.Contains(Origin, P(0f, 0.5f)));

        // 오프셋 뒤 회전 — 도형 자체 포즈 기준으로 돈다.
        var both = new AttackSectorShape(1f, 60f) { ForwardOffset = 2f, YawOffset = -90f };
        Assert.IsTrue(both.Contains(Origin, P(-0.8f, 2f)), "앞 2m 지점에서 왼쪽으로");
        Assert.IsFalse(both.Contains(Origin, P(0.8f, 2f)));
    }

    [Test]
    public void Pose_RotatesAndMovesShape()
    {
        var pose = new AttackShapePose(new Vector3(10f, 0f, 5f), new Vector3(2f, 7f, 0f)); // 높이 성분은 버린다
        Assert.AreEqual(Vector3.right, pose.Forward);
        var sector = new AttackSectorShape(2f, 60f);
        Assert.IsTrue(sector.Contains(pose, new Vector3(11.5f, 0f, 5f)));
        Assert.IsFalse(sector.Contains(pose, new Vector3(10f, 0f, 6.5f)));

        Assert.AreEqual(Vector3.forward, new AttackShapePose(Vector3.zero, Vector3.up).Forward, "정면 0 = +z");

        AttackShapePose point = AttackShapePose.Resolve(AttackShapeOrigin.Point, Vector3.zero, Vector3.forward, new Vector3(3f, 0f, 0f), Vector3.left);
        Assert.AreEqual(new Vector3(3f, 0f, 0f), point.Position);
        Assert.AreEqual(Vector3.left, point.Forward);
        AttackShapePose caster = AttackShapePose.Resolve(AttackShapeOrigin.CasterAim, Vector3.one, Vector3.back, Vector3.zero, Vector3.left);
        Assert.AreEqual(Vector3.one, caster.Position);
        Assert.AreEqual(Vector3.back, caster.Forward);
    }

    [Test]
    public void Height_RangeRelativeToPose()
    {
        var pose = new AttackShapePose(new Vector3(0f, 10f, 0f), Vector3.forward);
        var circle = new AttackCircleShape(2f) { MinHeight = -1f, MaxHeight = 3f };
        Assert.IsTrue(circle.Contains(pose, P(0f, 1f, 12.9f)));
        Assert.IsTrue(circle.Contains(pose, P(0f, 1f, 9f)), "하한 경계 포함");
        Assert.IsFalse(circle.Contains(pose, P(0f, 1f, 13.1f)));
        Assert.IsFalse(circle.Contains(pose, P(0f, 1f, 8.9f)));
        Assert.IsFalse(circle.Contains(Origin, P(0f, 1f, 3.5f)), "기본 상한 3");
    }

    // ── 배율 ─────────────────────────────────────────────────────────────

    [Test]
    public void Scaled_CopiesWithMultipliers_OriginalUnchanged()
    {
        var sector = new AttackSectorShape(2f, 90f, 0.5f) { ForwardOffset = 1f };
        var scaled = (AttackSectorShape)sector.Scaled(2f, 2f);
        Assert.AreEqual(4f, scaled.Range, 1e-5f, "range × 길이 배율");
        Assert.AreEqual(180f, scaled.Angle, 1e-5f, "angle × 폭 배율");
        Assert.AreEqual(0.5f, scaled.MinOffset, 1e-5f, "minOffset 그대로");
        Assert.AreEqual(1f, scaled.ForwardOffset, 1e-5f, "오프셋 그대로");
        Assert.AreEqual(2f, sector.Range, 1e-5f, "원본 그대로");
        Assert.AreEqual(360f, ((AttackSectorShape)sector.Scaled(1f, 10f)).Angle, 1e-5f, "각도 상한 360");

        Assert.AreEqual(3f, ((AttackCircleShape)new AttackCircleShape(2f).Scaled(1.5f, 9f)).Radius, 1e-5f, "원 = 길이 배율만");

        var rect = (AttackRectShape)new AttackRectShape(2f, 1f, 0.5f).Scaled(2f, 0.5f);
        Assert.AreEqual(4f, rect.Length, 1e-5f);
        Assert.AreEqual(0.5f, rect.Width, 1e-5f);
        Assert.AreEqual(0.5f, rect.BackOffset, 1e-5f);

        var tri = (AttackTriangleShape)new AttackTriangleShape(2f, 2f).Scaled(1.5f, 3f);
        Assert.AreEqual(3f, tri.Length, 1e-5f);
        Assert.AreEqual(6f, tri.Width, 1e-5f);

        Assert.AreEqual(0f, ((AttackCircleShape)new AttackCircleShape(2f).Scaled(-1f, 1f)).Radius, "음수 배율 = 0");

        var set = new AttackShapeSet(AttackShapeOrigin.Point, new AttackCircleShape(1f), null);
        AttackShapeSet scaledSet = set.Scaled(2f, 1f);
        Assert.AreEqual(AttackShapeOrigin.Point, scaledSet.Origin);
        Assert.AreEqual(2f, ((AttackCircleShape)scaledSet.Shapes[0]).Radius, 1e-5f);
        Assert.IsNull(scaledSet.Shapes[1], "빈 칸 유지");
        Assert.AreEqual(1f, ((AttackCircleShape)set.Shapes[0]).Radius, 1e-5f);
    }

    // ── 경계 상자·외곽선 = 판정 ─────────────────────────────────────────

    private static IEnumerable<AttackShape> Samples()
    {
        yield return new AttackSectorShape(2f, 60f) { ForwardOffset = 0.5f, SideOffset = -0.3f, YawOffset = 20f };
        yield return new AttackSectorShape(3f, 270f, 1f) { YawOffset = -45f };
        yield return new AttackSectorShape(2f, 360f, 0.5f);
        yield return new AttackSectorShape(2.5f, 150f, 1.2f) { SideOffset = 1f };
        yield return new AttackCircleShape(1.5f, 0.5f) { ForwardOffset = 2f };
        yield return new AttackRectShape(3f, 1.2f, 0.7f) { YawOffset = 33f, SideOffset = 0.4f };
        yield return new AttackTriangleShape(2f, 3f) { ForwardOffset = -0.5f, YawOffset = 170f };
    }

    private static bool InBox(Vector3 point, Vector3 center, Vector3 half, Quaternion rotation)
    {
        Vector3 local = Quaternion.Inverse(rotation) * (point - center);
        return Mathf.Abs(local.x) <= half.x + 1e-4f && Mathf.Abs(local.y) <= half.y + 1e-4f && Mathf.Abs(local.z) <= half.z + 1e-4f;
    }

    [Test]
    public void BroadBox_CoversEveryContainedPoint()
    {
        var pose = new AttackShapePose(new Vector3(1f, 0.5f, -2f), new Vector3(1f, 0f, 1f));
        foreach (AttackShape shape in Samples())
        {
            shape.GetBroadBox(pose, out Vector3 center, out Vector3 half, out Quaternion rotation);
            int inside = 0;
            for (float x = -4f; x <= 6f; x += 0.1f)
            for (float z = -6f; z <= 4f; z += 0.1f)
            {
                Vector3 point = new Vector3(x, pose.Position.y + 1f, z);
                if (!shape.Contains(pose, point))
                    continue;
                inside++;
                Assert.IsTrue(InBox(point, center, half, rotation), $"{shape} 판정 안인데 넓은 상자 밖: {point}");
            }

            Assert.Greater(inside, 0, $"{shape} 표본이 하나도 안이 아니다");
        }
    }

    [Test]
    public void Outlines_LieOnJudgedBoundary()
    {
        var pose = new AttackShapePose(new Vector3(-3f, 2f, 4f), new Vector3(-1f, 0f, 0.3f));
        foreach (AttackShape shape in Samples())
        {
            var loops = new List<Vector3[]>();
            shape.AppendOutlines(pose, loops);
            Assert.Greater(loops.Count, 0, shape.ToString());
            foreach (Vector3[] loop in loops)
            foreach (Vector3 point in loop)
            {
                Assert.AreEqual(pose.Position.y, point.y, 1e-4f, "바닥 높이");
                Assert.IsTrue(shape.Contains(pose, point), $"{shape} 외곽선 점이 판정 밖: {point}");
            }
        }

        var donut = new List<Vector3[]>();
        new AttackCircleShape(2f, 1f).AppendOutlines(Origin, donut);
        Assert.AreEqual(2, donut.Count, "도넛 = 고리 2개");
    }

    // ── 최근접 점(도형 영역 3D) ─────────────────────────────────────────

    [Test]
    public void ClosestPoint_IsInsideAndNearest_BruteForce()
    {
        var pose = new AttackShapePose(new Vector3(1f, 0.5f, -2f), new Vector3(1f, 0f, 1f));
        var random = new System.Random(153);
        foreach (AttackShape shape in Samples().Append(new AttackTriangleShape(2f, 1f)).Append(new AttackSectorShape(2f, 200f, 0.8f) { ForwardOffset = -1f }))
        {
            // 바닥 평면 안 표본(격자 0.02 m) — 최근접 후보의 정답 근사.
            var inside = new List<Vector3>();
            for (float x = -5f; x <= 7f; x += 0.02f)
            for (float z = -7f; z <= 5f; z += 0.02f)
            {
                var point = new Vector3(x, pose.Position.y, z);
                if (shape.Contains(pose, point))
                    inside.Add(point);
            }
            Assert.Greater(inside.Count, 0, shape.ToString());

            for (int q = 0; q < 150; q++)
            {
                var query = new Vector3((float)random.NextDouble() * 12f - 5f, pose.Position.y, (float)random.NextDouble() * 12f - 7f);
                Vector3 closest = shape.ClosestPoint(pose, query);
                Assert.IsTrue(shape.Contains(pose, closest), $"{shape} 최근접 점이 도형 밖: {query} → {closest}");
                float best = inside.Min(p => (p - query).sqrMagnitude);
                Assert.LessOrEqual(Vector3.Distance(closest, query), Mathf.Sqrt(best) + 1e-3f, $"{shape} 더 가까운 안 점이 있다: {query}");
                if (shape.Contains(pose, query))
                    Assert.AreEqual(query, closest, $"{shape} 안 점은 그대로");
            }
        }
    }

    [Test]
    public void ClosestPoint_ClampsHeight()
    {
        var pose = new AttackShapePose(new Vector3(0f, 10f, 0f), Vector3.forward);
        var circle = new AttackCircleShape(2f) { MinHeight = -1f, MaxHeight = 3f };
        Assert.AreEqual(P(0f, 2f, 13f), circle.ClosestPoint(pose, P(0f, 5f, 20f)), "위 → 상한, 바닥 평면은 반경으로");
        Assert.AreEqual(P(1f, 0f, 9f), circle.ClosestPoint(pose, P(1f, 0f, 0f)), "아래 → 하한");
        Assert.AreEqual(P(0f, 1f, 11f), circle.ClosestPoint(pose, P(0f, 1f, 11f)), "안 = 그대로");
        Assert.AreEqual(P(0f, 1f, 10f), new AttackCircleShape(2f, 1f).ClosestPoint(pose, P(0f, 0f, 10f)), "도넛 중심 = 정면 안쪽 경계");
    }

    [Test]
    public void Support_CoversEveryContainedPoint()
    {
        var pose = new AttackShapePose(new Vector3(1f, 0.5f, -2f), new Vector3(1f, 0f, 1f));
        var random = new System.Random(1532);
        foreach (AttackShape shape in Samples().Append(new AttackTriangleShape(2f, 1f)).Append(new AttackSectorShape(2f, 200f, 0.8f) { ForwardOffset = -1f }))
        {
            var inside = new List<Vector3>();
            for (float x = -5f; x <= 7f; x += 0.05f)
            for (float z = -7f; z <= 5f; z += 0.05f)
            {
                var point = new Vector3(x, pose.Position.y, z);
                if (shape.Contains(pose, point))
                    inside.Add(point);
            }

            for (int q = 0; q < 40; q++)
            {
                var direction = new Vector3((float)random.NextDouble() * 2f - 1f, 0f, (float)random.NextDouble() * 2f - 1f);
                float support = float.MinValue;
                for (int piece = 0; piece < shape.ConvexPieceCount; piece++)
                    support = Mathf.Max(support, Vector3.Dot(shape.Support(pose, piece, direction), direction));
                float farthest = inside.Max(p => Vector3.Dot(p, direction));
                Assert.GreaterOrEqual(support, farthest - 1e-3f, $"{shape} 지지점보다 먼 안 점: {direction}");
                if (!shape.HasHole) // 빈 곳 있는 도형의 조각은 빈 곳을 채운 껍질이라 더 멀 수 있다
                    Assert.LessOrEqual(support, farthest + 0.08f * direction.magnitude, $"{shape} 지지점이 도형보다 너무 멀다: {direction}");
            }
        }
    }

    [Test]
    public void Overlaps_RandomSpheres_MatchExactDistance_EveryShape()
    {
        // 구 몸 정답 = 몸 중심에서 도형 최근접 점까지 거리 ≤ 반경(최근접 점은 무차별 대입으로 따로 검증). 경계 ±0.01 m 는 건너뛴다.
        var pose = new AttackShapePose(new Vector3(1f, 0.5f, -2f), new Vector3(1f, 0f, 1f));
        var random = new System.Random(1533);
        float R(float min, float max) => min + (float)random.NextDouble() * (max - min);
        foreach (AttackShape shape in Samples().Append(new AttackTriangleShape(2f, 1f)).Append(new AttackSectorShape(2f, 200f, 0.8f) { ForwardOffset = -1f }))
        {
            int hit = 0, miss = 0;
            for (int n = 0; n < 600; n++)
            {
                var center = new Vector3(R(-5f, 7f), pose.Position.y + R(-3f, 5f), R(-7f, 5f));
                float radius = R(0.1f, 1.5f);
                float gap = Vector3.Distance(center, shape.ClosestPoint(pose, center)) - radius;
                if (Mathf.Abs(gap) < 0.01f)
                    continue;
                bool expected = gap < 0f;
                Assert.AreEqual(expected, AttackShapeQuery.Overlaps(shape, pose, new AttackShapeTestSphere(center, radius), out _),
                    $"{shape} 구 {center} r{radius:0.###} 틈 {gap:0.####}");
                if (expected) hit++; else miss++;
            }
            Assert.Greater(hit, 10, shape + " 맞음 표본");
            Assert.Greater(miss, 10, shape + " 빠짐 표본");
        }
    }

    // ── 몸 겹침 판정 ─────────────────────────────────────────────────────

    [Test]
    public void Overlaps_SphereBody_ExactGrazeAndMiss()
    {
        var rect = new AttackRectShape(16f, 1.2f) { MinHeight = -1f, MaxHeight = 1f };
        Assert.IsTrue(AttackShapeQuery.Overlaps(rect, Origin, new AttackShapeTestSphere(P(1.6f, 10f), 1f), out Vector3 point), "옆면이 폭 경계에 닿음 = 맞음");
        Assert.IsTrue(rect.Contains(Origin, point), "겹친 점은 도형 안");
        Assert.IsTrue(AttackShapeQuery.Overlaps(rect, Origin, new AttackShapeTestSphere(P(1.59f, 10f), 1f), out _));
        Assert.IsFalse(AttackShapeQuery.Overlaps(rect, Origin, new AttackShapeTestSphere(P(1.61f, 10f), 1f), out _), "0.01 m 떨어짐");
        Assert.IsTrue(AttackShapeQuery.Overlaps(rect, Origin, new AttackShapeTestSphere(P(0f, 10f, 1.9f), 1f), out _), "위로 높이 경계 걸침");
        Assert.IsFalse(AttackShapeQuery.Overlaps(rect, Origin, new AttackShapeTestSphere(P(0f, 10f, 2.1f), 1f), out _), "높이 위 밖");
        Assert.IsTrue(AttackShapeQuery.Overlaps(rect, Origin, new AttackShapeTestSphere(P(0f, -0.9f), 1f), out _), "시작 뒤 중심 — 몸 앞쪽이 걸침");
    }

    [Test]
    public void Overlaps_OriginOrCenterInsideBody_NoFootSpecialCase()
    {
        // 기준점(찬 부채꼴 꼭짓점)이 몸 안이면 몸 중심이 등 뒤여도 겹친다 — 예전 '발밑 겹침 → 중심 x·z' 특례 없음.
        var front = new AttackSectorShape(3f, 90f);
        Assert.IsTrue(AttackShapeQuery.Overlaps(front, Origin, new AttackShapeTestSphere(P(0f, -0.5f), 1f), out _), "꼭짓점이 몸 안 = 맞음");
        Assert.IsFalse(AttackShapeQuery.Overlaps(front, Origin, new AttackShapeTestSphere(P(0f, -1.2f), 1f), out _), "등 뒤로 떨어짐");
        Assert.IsTrue(AttackShapeQuery.Overlaps(front, Origin, new AttackShapeTestSphere(P(2.8f, 0f), 2f), out _), "옆 변(45°)에 걸친 큰 몸");

        // 원 중심이 큰 몸 안 — 몸 중심이 반경 밖이어도 맞는다.
        var circle = new AttackCircleShape(2f) { MinHeight = 0f, MaxHeight = 3f };
        Assert.IsTrue(AttackShapeQuery.Overlaps(circle, Origin, new AttackShapeTestSphere(P(2.5f, 0f, 0.5f), 3f), out _));

        // 부채꼴·원 도넛 — 안쪽 빈 곳의 몸은 안쪽 경계에 닿아야 맞는다.
        var donut = new AttackSectorShape(3f, 90f, minOffset: 1.5f);
        Assert.IsTrue(AttackShapeQuery.Overlaps(donut, Origin, new AttackShapeTestSphere(P(0f, 0.8f), 0.75f), out _), "빈 곳에서 안쪽 경계 걸침");
        Assert.IsFalse(AttackShapeQuery.Overlaps(donut, Origin, new AttackShapeTestSphere(P(0f, 0.6f), 0.75f), out _), "빈 곳 안에만");
        var ring = new AttackCircleShape(3f, minRadius: 1.5f);
        Assert.IsTrue(AttackShapeQuery.Overlaps(ring, Origin, new AttackShapeTestSphere(Vector3.zero, 1.6f), out _), "빈 곳 중심, 반경이 안쪽 경계를 넘음");
        Assert.IsFalse(AttackShapeQuery.Overlaps(ring, Origin, new AttackShapeTestSphere(Vector3.zero, 1.4f), out _));
    }

    [Test]
    public void Union_AnyShapeOverlaps_SameKeyOnce()
    {
        var shapes = new AttackShape[] { new AttackSectorShape(2f, 90f), null, new AttackCircleShape(1f) { ForwardOffset = 5f } };
        var seen = new HashSet<string>();

        Assert.IsTrue(AttackShapeQuery.TryAccept(shapes, Origin, new AttackShapeTestSphere(P(0f, 6.2f), 0.3f), "A", seen, out _), "두 번째 도형과 겹침 = 피격");
        Assert.IsFalse(AttackShapeQuery.TryAccept(shapes, Origin, new AttackShapeTestSphere(P(0f, 1f), 0.3f), "A", seen, out _), "같은 대상은 한 공격에 1회");

        Assert.IsFalse(AttackShapeQuery.TryAccept(shapes, Origin, new AttackShapeTestSphere(P(0f, 3f), 0.4f), "B", seen, out _), "도형 사이 빈틈 = 밖");
        Assert.IsFalse(seen.Contains("B"), "밖이면 기록하지 않는다 — 같은 대상의 다른 콜라이더가 안이면 맞는다");
        Assert.IsTrue(AttackShapeQuery.TryAccept(shapes, Origin, new AttackShapeTestSphere(P(0f, 3f), 1.1f), "B", seen, out _), "두 도형 사이를 덮는 큰 몸");

        Assert.IsFalse(AttackShapeQuery.TryAccept(shapes, Origin, new AttackShapeTestSphere(P(0f, 1f), 0.3f), (string)null, seen, out _), "키 없음 = 대상 아님");
        Assert.IsFalse(AttackShapeQuery.AnyContains(null, Origin, Vector3.zero));
        Assert.IsFalse(AttackShapeQuery.AnyOverlaps(null, Origin, new AttackShapeTestSphere(Vector3.zero, 1f), out _));
    }

    // ── 실제 Overlap 수집 ───────────────────────────────────────────────

    private GameObject Box(string name, Vector3 position, Vector3 size, Transform parent = null)
    {
        var go = new GameObject(name);
        if (parent != null)
            go.transform.SetParent(parent, false);
        else
            created.Add(go);
        go.transform.position = position;
        go.AddComponent<BoxCollider>().size = size;
        return go;
    }

    [Test]
    public void Collect_RealOverlap_UnionOncePerTarget()
    {
        Vector3 far = new Vector3(5000f, 0f, 5000f); // 열린 씬 콜라이더와 떨어진 곳
        var pose = new AttackShapePose(far, Vector3.forward);

        GameObject boss = Box("Boss", far + new Vector3(0f, 1f, 2.3f), Vector3.one);
        Box("BossArm", far + new Vector3(0.3f, 1f, 2.2f), Vector3.one * 0.5f, boss.transform); // 같은 대상의 두 번째 콜라이더
        GameObject side = Box("Side", far + new Vector3(0f, 1f, 6f), Vector3.one * 0.4f);
        GameObject behind = Box("Behind", far + new Vector3(0f, 1f, -2f), Vector3.one);
        GameObject tooHigh = Box("High", far + new Vector3(0f, 6f, 1f), Vector3.one * 0.4f);
        GameObject owner = Box("Owner", far + new Vector3(0f, 1f, 0f), Vector3.one * 0.6f); // 기준점이 몸 안
        Physics.SyncTransforms();

        var roots = new HashSet<GameObject> { boss, side, behind, tooHigh };
        var set = new AttackShapeSet(AttackShapeOrigin.CasterAim, new AttackSectorShape(2.5f, 90f), new AttackCircleShape(0.5f) { ForwardOffset = 6f });
        var seen = new HashSet<Object>();
        var hits = new List<AttackShapeHit>();
        int added = AttackShapeQuery.Collect(set, pose, ~0, QueryTriggerInteraction.Collide, new Collider[32],
            c => roots.Contains(c.transform.root.gameObject) ? c.transform.root.gameObject : null, seen, hits);

        CollectionAssert.AreEquivalent(new Object[] { boss, side }, hits.Select(h => h.Target).ToArray(), "부채꼴 + 원 합집합, 뒤·높이 밖·자기 자신 제외");
        Assert.AreEqual(2, added, "같은 대상(Boss 두 콜라이더)은 1회");
        AttackShapeHit bossHit = hits.First(h => h.Target == boss);
        Assert.IsTrue(set.Contains(pose, bossHit.Point), "겹친 점 = 도형 안");
        Assert.Less(Vector3.Distance(bossHit.Collider.ClosestPoint(bossHit.Point), bossHit.Point), 1e-4f, "겹친 점 = 몸(표면 또는 안)");

        Assert.AreEqual(0, AttackShapeQuery.Collect(set, pose, ~0, QueryTriggerInteraction.Collide, new Collider[32],
            c => roots.Contains(c.transform.root.gameObject) ? c.transform.root.gameObject : null, seen, hits), "같은 seen = 다음 틱에도 1회");
        Assert.IsNotNull(owner);
    }

    // 상자·캡슐·구 하나(열린 씬에 저장 안 함). 지우기 전까지 TearDown 목록에 둔다.
    private GameObject RandomBody(int kind, Vector3 position, Quaternion rotation, System.Func<float, float, float> range)
    {
        var go = new GameObject("AttackShapeTests_Random") { hideFlags = HideFlags.DontSave };
        created.Add(go);
        go.transform.SetPositionAndRotation(position, rotation);
        if (kind == 0)
            go.AddComponent<BoxCollider>().size = new Vector3(range(0.2f, 2f), range(0.2f, 2f), range(0.2f, 2f));
        else if (kind == 1)
        {
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.radius = range(0.2f, 0.9f);
            capsule.height = range(0.5f, 3f);
        }
        else
            go.AddComponent<SphereCollider>().radius = range(0.2f, 1.5f);
        Physics.SyncTransforms();
        return go;
    }

    private static bool OverlapBoxHas(Vector3 center, Vector3 half, Quaternion rotation, Collider[] buffer, Collider body)
    {
        int count = Physics.OverlapBoxNonAlloc(center, half, buffer, rotation, ~0, QueryTriggerInteraction.Collide);
        bool found = false;
        for (int i = 0; i < count; i++)
            found |= buffer[i] == body;
        return found;
    }

    [Test]
    public void Collect_RectMatchesPhysicsOverlapBox_RandomBodies()
    {
        // 직사각형 기둥은 넓은 질의 상자와 같은 영역이다 → Physics.OverlapBox 결과가 '몸이 겹치면 맞음'의 정답.
        // 무작위 상자·캡슐·구(돌림 포함)를 경계 근처에 두고, 경계에서 ±Band 안(허용 오차 차이)인 몸은 건너뛴다.
        const float Band = 0.01f;
        Vector3 far = new Vector3(-5000f, 0f, 5000f);
        var pose = new AttackShapePose(far, new Vector3(0.6f, 0f, 1f));
        var rect = new AttackRectShape(10f, 1.2f, 0.5f) { YawOffset = 12f, SideOffset = 0.3f, MinHeight = -1f, MaxHeight = 1f };
        var set = new AttackShapeSet(AttackShapeOrigin.Point, rect);
        rect.GetBroadBox(pose, out Vector3 center, out Vector3 half, out Quaternion rotation);
        var random = new System.Random(1531);
        float R(float min, float max) => min + (float)random.NextDouble() * (max - min);
        var buffer = new Collider[8];
        int compared = 0, hitsCount = 0;

        for (int n = 0; n < 300; n++)
        {
            GameObject go = RandomBody(n % 3, center + rotation * new Vector3(R(-2.5f, 2.5f), R(-2.2f, 2.2f), R(-6f, 6f)),
                Quaternion.Euler(R(-40f, 40f), R(0f, 360f), R(-40f, 40f)), R);
            Collider body = go.GetComponent<Collider>();
            bool inner = OverlapBoxHas(center, half - Vector3.one * Band, rotation, buffer, body);
            bool outer = OverlapBoxHas(center, half + Vector3.one * Band, rotation, buffer, body);

            if (inner == outer)
            {
                var hits = new List<AttackShapeHit>();
                AttackShapeQuery.Collect(set, pose, ~0, QueryTriggerInteraction.Collide, new Collider[8],
                    c => c == body ? go : null, new HashSet<Object>(), hits);
                Assert.AreEqual(inner, hits.Count == 1, $"#{n} {body.GetType().Name} at {go.transform.position - far} rot {go.transform.eulerAngles}");
                compared++;
                if (inner) hitsCount++;
            }

            created.Remove(go);
            Object.DestroyImmediate(go);
        }

        Assert.Greater(compared, 250, "비교 수");
        Assert.Greater(hitsCount, 40, "맞음 표본");
        Assert.Greater(compared - hitsCount, 40, "빠짐 표본");
    }

    // ── 데이터 테이블 행 ───────────────────────────────────────────────

    [Test]
    public void DataTable_SerializeReferenceShapeFields_ExportAsRows()
    {
        var probe = ScriptableObject.CreateInstance<AttackShapeTableProbe>();
        created.Add(probe);
        probe.hitArea = new AttackShapeSet(AttackShapeOrigin.CasterAim,
            new AttackSectorShape(2f, 90f, 0.5f), new AttackRectShape(3f, 1f, 0.2f));

        List<DataTableTemplate.Field> fields = DataTableTemplate.CollectFields(probe);
        List<string> paths = fields.Select(f => f.Path).ToList();
        CollectionAssert.IsSubsetOf(new[]
        {
            "hitArea.shapes[0].forwardOffset", "hitArea.shapes[0].sideOffset", "hitArea.shapes[0].yawOffset",
            "hitArea.shapes[0].minHeight", "hitArea.shapes[0].maxHeight",
            "hitArea.shapes[0].range", "hitArea.shapes[0].angle", "hitArea.shapes[0].minOffset",
            "hitArea.shapes[1].length", "hitArea.shapes[1].width", "hitArea.shapes[1].backOffset",
        }, paths, string.Join(", ", paths));
        CollectionAssert.DoesNotContain(paths, "hitArea.origin", "enum 은 수치 시트에 나가지 않는다");
        Assert.AreEqual("hitArea.shapes[0] · AttackSectorShape", fields.First(f => f.Path == "hitArea.shapes[0].range").Element);
        Assert.AreEqual(2f, (float)fields.First(f => f.Path == "hitArea.shapes[0].range").Value, 1e-5f);

        FieldInfo range = DataTableFields.Resolve(probe, "hitArea.shapes.Array.data[0].range");
        Assert.IsNotNull(range, "적용기·범위 검사가 실제 타입 필드를 찾는다");
        Assert.IsNotNull(range.GetCustomAttribute<MinAttribute>());
        Assert.IsNotNull(DataTableFields.Resolve(probe, "hitArea.shapes.Array.data[1].forwardOffset"), "기반 클래스 칸");
    }

    [Test]
    public void Validate_ReportsBadData()
    {
        var errors = new List<string>();
        new AttackShapeSet(AttackShapeOrigin.CasterAim,
            new AttackSectorShape(1f, 90f, 2f), null, new AttackCircleShape(float.NaN), new AttackRectShape(1f, 1f) { MinHeight = 2f, MaxHeight = 1f })
            .Validate(errors);
        Assert.AreEqual(4, errors.Count, string.Join("\n", errors));
        Assert.IsTrue(errors[0].StartsWith("shapes[0]"));
        Assert.IsTrue(errors[1].StartsWith("shapes[1]"));

        errors.Clear();
        new AttackShapeSet(AttackShapeOrigin.Point, new AttackTriangleShape(2f, 1f)).Validate(errors);
        Assert.AreEqual(0, errors.Count, string.Join("\n", errors));
    }
}

/// <summary>테스트용 해석 몸(구) — Physics 없이 <see cref="AttackShapeQuery.Overlaps{TBody}"/> 를 본다.</summary>
public readonly struct AttackShapeTestSphere : IAttackShapeBody
{
    private readonly Vector3 center;
    private readonly float radius;

    public AttackShapeTestSphere(Vector3 center, float radius)
    {
        this.center = center;
        this.radius = radius;
    }

    public Vector3 Center => center;

    public Vector3 Support(Vector3 direction) => center + direction.normalized * radius;

    public Vector3 ClosestPoint(Vector3 point)
    {
        Vector3 offset = point - center;
        float distance = offset.magnitude;
        return distance <= radius ? point : center + offset * (radius / distance);
    }
}
