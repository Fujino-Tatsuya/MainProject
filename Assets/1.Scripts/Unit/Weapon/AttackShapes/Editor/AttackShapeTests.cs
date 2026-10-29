using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 할 일 153 1/6 — 공격 판정 도형 코어. 도형별 안/밖 경계 · 도넛(minOffset·minRadius) · 오프셋(앞·옆·회전) · 포즈 회전 ·
/// 배율 Scale · 높이 범위 · 넓은 상자 ⊇ 판정 · 외곽선 = 판정 경계 · 합집합·1회 피격 · 발밑 겹침 판정 점 · 실제 Overlap 수집 · 데이터 테이블 행.
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

    // ── 합집합·1회 피격 ─────────────────────────────────────────────────

    [Test]
    public void Union_AnyShapeHits_SameKeyOnce()
    {
        var shapes = new AttackShape[] { new AttackSectorShape(2f, 90f), null, new AttackCircleShape(1f) { ForwardOffset = 5f } };
        var seen = new HashSet<string>();

        Assert.IsTrue(AttackShapeQuery.TryAccept(shapes, Origin, P(0f, 5.5f), P(0f, 6f), "A", seen, out _), "두 번째 도형 안 = 피격");
        Assert.IsFalse(AttackShapeQuery.TryAccept(shapes, Origin, P(0f, 1f), P(0f, 1f), "A", seen, out _), "같은 대상은 한 공격에 1회");

        Assert.IsFalse(AttackShapeQuery.TryAccept(shapes, Origin, P(0f, 3.5f), P(0f, 3.5f), "B", seen, out _), "도형 사이 빈틈 = 밖");
        Assert.IsFalse(seen.Contains("B"), "밖이면 기록하지 않는다 — 같은 대상의 다른 콜라이더가 안이면 맞는다");
        Assert.IsTrue(AttackShapeQuery.TryAccept(shapes, Origin, P(0.3f, 1f), P(0.3f, 1.5f), "B", seen, out _));

        Assert.IsFalse(AttackShapeQuery.TryAccept(shapes, Origin, P(0f, 1f), P(0f, 1f), null, seen, out _), "키 없음 = 대상 아님");
        Assert.IsFalse(AttackShapeQuery.AnyContains(null, Origin, Vector3.zero));
    }

    [Test]
    public void ProbePoint_FootOverlapUsesBoundsCenterPlanarOnly()
    {
        Vector3 origin = new Vector3(1f, 0f, 1f);
        Assert.AreEqual(P(1f, 2f, 0.5f), AttackShapeQuery.ProbePoint(origin, P(1f, 2f, 0.5f), P(5f, 5f, 5f)), "보통 = 가장 가까운 점");

        Vector3 probe = AttackShapeQuery.ProbePoint(origin, P(1f, 1f, 0.2f), P(1f, -1f, 2f));
        Assert.AreEqual(P(1f, -1f, 0.2f), probe, "발밑 겹침 = x·z 는 중심, 높이는 가장 가까운 점");

        // 기준점이 몸 안이고 몸 중심이 등 뒤 = 정면 부채꼴 밖(예전 QBlastInSector 와 같은 동작).
        var front = new AttackShape[] { new AttackSectorShape(3f, 90f) };
        var seen = new HashSet<string>();
        Assert.IsFalse(AttackShapeQuery.TryAccept(front, Origin, Vector3.zero, P(0f, -0.5f, 1f), "behind", seen, out _));
        Assert.IsTrue(AttackShapeQuery.TryAccept(front, Origin, Vector3.zero, P(0f, 0.5f, 1f), "ahead", seen, out _));
        Assert.IsTrue(AttackShapeQuery.TryAccept(front, Origin, Vector3.zero, Vector3.zero, "exact", seen, out _), "중심도 기준점 = 찬 부채꼴 꼭짓점");
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
        Assert.AreEqual(bossHit.Collider.ClosestPoint(far), bossHit.Point, "판정 점 = 몸 표면에서 기준점에 가장 가까운 점");

        Assert.AreEqual(0, AttackShapeQuery.Collect(set, pose, ~0, QueryTriggerInteraction.Collide, new Collider[32],
            c => roots.Contains(c.transform.root.gameObject) ? c.transform.root.gameObject : null, seen, hits), "같은 seen = 다음 틱에도 1회");
        Assert.IsNotNull(owner);
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
