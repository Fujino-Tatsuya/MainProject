using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 특성 랩 공격 범위 기즈모(할 일 152·153 6단계) — 기즈모가 받는 (도형, 포즈) = 서버 판정이 쓰는 (도형, 포즈) 인지(캐릭터별 대표 공격),
/// 그리고 <see cref="AttackShapeDrawing"/> 외곽선(바닥·높이 범위·도넛)이 판정 경계(<see cref="AttackShape.Contains"/>) 위에 있는지 고정한다.
/// (기즈모 그리기 자체·라벨·색은 Play 확인.)
/// </summary>
public sealed class TraitLabAttackShapeTests
{
    const float Eps = 1e-4f;

    static readonly Vector3 Caster = new Vector3(3f, 0.5f, -2f);
    static readonly Vector3 Facing = new Vector3(0.6f, 0f, 0.8f);

    readonly List<Object> created = new List<Object>();

    T Create<T>() where T : ScriptableObject
    {
        var data = ScriptableObject.CreateInstance<T>();
        created.Add(data);
        return data;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Object o in created)
            Object.DestroyImmediate(o);
        created.Clear();
    }

    // ── 기즈모 = 판정 (도형, 포즈) ─────────────────────────────────────

    [Test]
    public void Paladin_BasicSteps_GizmoEqualsJudgment()
    {
        var steps = new[] { new DefaultAttackStep(), new DefaultAttackStep() };
        var shapes = new List<TraitLabGizmoShape>();
        TraitLabAttackShapes.AddDefaultSteps(steps, Caster, Facing, shapes);

        Assert.AreEqual(2, shapes.Count, "근접 단마다 하나");
        for (int i = 0; i < steps.Length; i++)
        {
            AttackShapeSet area = PlayerDefaultAttack.StepArea(steps[i], Caster, Facing, out AttackShapePose pose);
            AssertSame(area, pose, shapes[i], TraitLabGizmoCategory.Basic, $"평타 {i + 1}타");
        }
    }

    [Test]
    public void Paladin_Q_ShieldDash_QBlast_GizmoEqualsJudgment()
    {
        var data = Create<FirstMeleeMainSkillData>();
        var shapes = new List<TraitLabGizmoShape>();

        TraitLabAttackShapes.AddPaladinQ(data, Caster, Facing, shapes);
        AttackShapeSet q = FirstMeleeMainSkill.HoldArea(data, Caster, Facing, out AttackShapePose qPose);
        AssertSame(q, qPose, shapes[0], TraitLabGizmoCategory.Skill, "Q 판정");

        // 돌파의 방패 — 경로 끝 표본(판정과 같은 경로 장부·표본 포즈).
        var dash = new PaladinShieldDashSettings(3f, 18f, 0.1f, 0.2f, 1.2f, 2f, 0.3f);
        TraitLabAttackShapes.AddShieldDashEnd(data, dash, Caster, Facing, shapes);
        var path = new PaladinShieldDashPath();
        path.Begin(Caster, Facing, dash);
        AttackShapeSet end = FirstMeleeMainSkill.ShieldDashSampleArea(data, path.PointAt(path.Cap), Caster.y, path.Heading, out AttackShapePose endPose);
        AssertSame(end, endPose, shapes[1], TraitLabGizmoCategory.Trait, "돌파의 방패");
        Assert.AreEqual(Caster.z + Facing.z * 3f, endPose.Position.z, Eps, "경로 끝 = 출발 + 방향 × 돌진 거리");

        // 진격의 충격 — 파생 명세 hitArea + QBlastPose(Q 끝 지점 = 지금 위치).
        var blast = new AttackShapeSet(AttackShapeOrigin.Point, new AttackSectorShape(2.5f, 120f) { ForwardOffset = 1f });
        TraitLabAttackShapes.AddQBlast(blast, Caster, Facing, shapes);
        AssertSame(blast, PaladinTraitRules.QBlastPose(blast, Caster, Facing), shapes[2], TraitLabGizmoCategory.Trait, "진격의 충격");
    }

    [Test]
    public void Paladin_E_Counter_And_TraitPush_GizmoEqualsJudgment()
    {
        var data = Create<FirstMeleeSubSkillData>();
        var shapes = new List<TraitLabGizmoShape>();

        TraitLabAttackShapes.AddPaladinE(data, null, Caster, Facing, shapes);
        Assert.AreEqual(1, shapes.Count, "수호의 파동 없으면 반격만");
        AttackShapeSet counter = FirstMeleeSubSkill.CounterArea(data, Caster, Facing, out AttackShapePose pose);
        AssertSame(counter, pose, shapes[0], TraitLabGizmoCategory.Skill, "E 반격");

        shapes.Clear();
        AttackShapeSet push = PaladinTraitRules.EPushArea(3f);
        TraitLabAttackShapes.AddPaladinE(data, push, Caster, Facing, shapes);
        Assert.AreEqual(2, shapes.Count);
        AssertSame(push, pose, shapes[1], TraitLabGizmoCategory.Trait, "수호의 파동 = 반격과 같은 포즈");
        Assert.IsTrue(shapes[1].Area.Contains(shapes[1].Pose, Caster + new Vector3(-2.9f, 0f, 0f)), "특성 반경 3 — 등 뒤 2.9m 안");
    }

    [Test]
    public void Assassin_DashPath_And_CircleStrike_GizmoEqualsJudgment()
    {
        var dash = Create<AssassinDashStrikeSkillData>();
        var shapes = new List<TraitLabGizmoShape>();

        TraitLabAttackShapes.AddAssassinDash(dash, "Q", Caster, Facing, true, shapes);
        AttackShapeSet path = AssassinDashStrikeSkill.PathSegment(dash, Caster, Caster, Facing, 0f, dash.DashDistance, out AttackShapePose pathPose);
        AssertSame(path, pathPose, shapes[0], TraitLabGizmoCategory.Skill, "Q 돌진 경로");
        AssertSame(path, pathPose, shapes[1], TraitLabGizmoCategory.Trait, "추격의 궤적");
        Assert.IsTrue(shapes[0].Area.Contains(shapes[0].Pose, Caster + Facing * (dash.DashDistance - 0.01f)), "경로 = dashDistance 전체");

        // 변신 E — 칼날의 영역 반경 배율 1.3 이 도형 값에 반영.
        var circle = Create<AssassinCircleStrikeSkillData>();
        shapes.Clear();
        TraitLabAttackShapes.AddAssassinCircle(circle, 1.3f, "E", Caster, Facing, shapes);
        Vector3 aimPoint = Caster + Facing * circle.CenterDistance;
        AttackShapeSet strike = AssassinCircleStrikeSkill.ResolveStrikeArea(circle, Caster, aimPoint, 1.3f, out AttackShapePose strikePose);
        AssertSame(strike, strikePose, shapes[0], TraitLabGizmoCategory.Skill, "E 원형 5타");
        Vector3 rim = AssassinCircleStrikeSkill.AreaCenter(Caster, aimPoint) + Vector3.right * circle.AreaRadius * 1.25f;
        Assert.IsTrue(shapes[0].Area.Contains(shapes[0].Pose, rim), "배율 1.3 반영 — 데이터 반경 × 1.25 지점 안");
    }

    [Test]
    public void Gunner_ChargeLaser_MaxCharge_WideBeam_GizmoEqualsJudgment()
    {
        var data = Create<GunnerChargeLaserData>();
        var shapes = new List<TraitLabGizmoShape>();
        float width = data.BeamWidth * 1.3f;

        TraitLabAttackShapes.AddGunnerQ(null, data, width, "Q", Caster, Facing, true, shapes);
        Vector3 origin = GunnerChargeLaserSkill.FireOrigin(Caster, data);
        AttackShapeSet shot = GunnerChargeLaserSkill.ShotArea(null, data, origin, Facing, data.RangeAt(1f), width, out AttackShapePose pose, out float length);
        AssertSame(shot, pose, shapes[0], TraitLabGizmoCategory.Skill, "Q 최대 충전");
        AssertSame(shot, pose, shapes[1], TraitLabGizmoCategory.Trait, "잔류 포격");
        Assert.AreEqual(data.RangeAt(1f), length, Eps, "지형 차단 없음 = 최대 충전 사거리(판정과 같은 RangeAt)");

        Vector3 side = new Vector3(Facing.z, 0f, -Facing.x);
        Vector3 wide = shapes[0].Pose.Position + Facing * 1f + side * data.BeamWidth * 0.6f;
        Assert.IsTrue(shapes[0].Area.Contains(shapes[0].Pose, wide), "광폭 1.3 반영 — 데이터 폭 반 × 1.2 지점 안");
        Assert.IsFalse(data.FireArea(length, data.BeamWidth).Contains(pose, wide), "광폭 없으면 같은 지점 밖");
    }

    // ── 그리기 외곽선 = 판정 경계 ─────────────────────────────────────

    [Test]
    public void DrawingOutlines_FloorAndHeight_LieOnContainsBoundary()
    {
        var pose = new AttackShapePose(new Vector3(-3f, 2f, 4f), new Vector3(-1f, 0f, 0.3f));
        var shapes = new List<AttackShape>
        {
            new AttackSectorShape(3f, 100f) { MinHeight = 0f, MaxHeight = 2f },
            new AttackSectorShape(3f, 250f, 1f) { ForwardOffset = 0.5f, YawOffset = 20f, MinHeight = -1f, MaxHeight = 3f },
            new AttackCircleShape(2f, 0.8f) { SideOffset = 1f, MinHeight = 0f, MaxHeight = 1.5f },
            new AttackRectShape(4f, 1.2f, 0.5f) { MinHeight = 0f, MaxHeight = 2f },
            new AttackTriangleShape(3f, 2f) { MinHeight = 0f, MaxHeight = 2f },
        };

        foreach (AttackShape shape in shapes)
        {
            var floor = new List<Vector3[]>();
            var heights = new List<Vector3[]>();
            AttackShapeDrawing.CollectOutlines(new[] { shape }, pose, floor, heights);

            Assert.Greater(floor.Count, 0, shape.ToString());
            Assert.AreEqual(floor.Count * 2, heights.Count, $"{shape} 높이 고리 = 바닥 고리마다 아래·위");
            foreach (Vector3[] loop in floor)
            foreach (Vector3 point in loop)
                AssertOnPlanarBoundary(shape, pose, point);

            for (int l = 0; l < heights.Count; l++)
            {
                bool top = l >= floor.Count;
                float expected = pose.Position.y + (top ? shape.MaxHeight : shape.MinHeight);
                foreach (Vector3 point in heights[l])
                {
                    Assert.AreEqual(expected, point.y, Eps, $"{shape} 높이 고리 높이");
                    AssertOnPlanarBoundary(shape, pose, point);
                    Vector3 beyond = point + Vector3.up * (top ? 0.01f : -0.01f);
                    Assert.IsFalse(shape.Contains(pose, beyond), $"{shape} 높이 범위 밖으로 0.01m 나가면 판정 밖: {beyond}");
                }
            }
        }
    }

    [Test]
    public void DrawingOutlines_Donut_DrawsInnerLoop()
    {
        var floor = new List<Vector3[]>();
        AttackShapeDrawing.CollectOutlines(new AttackShape[] { new AttackCircleShape(2f, 1f) }, new AttackShapePose(Vector3.zero, Vector3.forward), floor, null);
        Assert.AreEqual(2, floor.Count, "원 도넛 = 고리 2개");

        floor.Clear();
        AttackShapeDrawing.CollectOutlines(new AttackShape[] { new AttackSectorShape(3f, 360f, 1.5f) }, new AttackShapePose(Vector3.zero, Vector3.forward), floor, null);
        Assert.AreEqual(2, floor.Count, "360° 부채꼴 도넛 = 고리 2개");
    }

    // ── 판정 쪽 공용 포즈 함수(152) ────────────────────────────────────

    [Test]
    public void QBlastPose_IsQEndPlusHeading_ShapeOffsetMovesApex()
    {
        var end = new Vector3(1f, 0.5f, 2f);
        var area = new AttackShapeSet(AttackShapeOrigin.Point, new AttackSectorShape(2f, 90f) { ForwardOffset = 1.5f });
        AttackShapePose pose = PaladinTraitRules.QBlastPose(area, end, Vector3.right);
        Assert.AreEqual(end, pose.Position, "기준 = Q 끝 지점");
        Assert.AreEqual(1f, pose.Forward.x, Eps, "정면 = 진행 방향");

        AttackShapePose apex = area.Shapes[0].ShapePose(pose);
        Assert.AreEqual(2.5f, apex.Position.x, Eps, "꼭짓점 = 끝 지점 + 정면 × 도형 forwardOffset(기즈모와 판정이 같은 값)");
        Assert.AreEqual(0.5f, apex.Position.y, Eps);
        Assert.AreEqual(2f, apex.Position.z, Eps);
    }

    [Test]
    public void GunnerChargeLaserFireOrigin_IsCasterPlusMuzzleHeight()
    {
        var data = Create<GunnerChargeLaserData>();
        var caster = new Vector3(3f, 1f, -2f);
        Vector3 origin = GunnerChargeLaserSkill.FireOrigin(caster, data);
        Assert.AreEqual(caster + Vector3.up * data.MuzzleHeight, origin);
    }

    [Test]
    public void DefaultAttackResolvePose_IsCasterFeetPlusAim()
    {
        var caster = new Vector3(3f, 0.5f, -2f);
        AttackShapePose pose = PlayerDefaultAttack.ResolvePose(new DefaultAttackStep(), caster, new Vector3(1f, 0.7f, 0f));
        Assert.AreEqual(caster, pose.Position, "기준점 = 시전자 발밑");
        Assert.AreEqual(1f, pose.Forward.x, Eps, "정면 = 조준 방향(바닥 평면)");
        Assert.AreEqual(0f, pose.Forward.y, Eps);

        AttackShapePose noStep = PlayerDefaultAttack.ResolvePose(null, caster, Vector3.back);
        Assert.AreEqual(caster, noStep.Position, "단계 없음도 같은 규칙(기즈모가 판정과 같은 포즈)");
        Assert.AreEqual(-1f, noStep.Forward.z, Eps);
    }

    // ── 도우미 ──────────────────────────────────────────────────────────

    // 같은 영역 = 같은 포즈 + 도형마다 같은 종류·높이 범위·외곽선(판정과 같은 수식이라 외곽선이 같으면 Contains 도 같다).
    static void AssertSame(AttackShapeSet expectedArea, in AttackShapePose expectedPose, in TraitLabGizmoShape actual,
        TraitLabGizmoCategory category, string what)
    {
        Assert.AreEqual(TraitLabGizmoShape.ShapeKind.Area, actual.Kind, $"{what}: 판정 도형으로 그린다");
        Assert.AreEqual(category, actual.Category, $"{what}: 분류");
        StringAssert.StartsWith(what.Split(' ')[0], actual.Label, $"{what}: 라벨");
        Assert.Less((expectedPose.Position - actual.Pose.Position).sqrMagnitude, Eps * Eps, $"{what}: 기준점");
        Assert.Less((expectedPose.Forward - actual.Pose.Forward).sqrMagnitude, Eps * Eps, $"{what}: 정면");
        Assert.AreEqual(expectedArea.Shapes.Count, actual.Area.Shapes.Count, $"{what}: 도형 수");

        for (int i = 0; i < expectedArea.Shapes.Count; i++)
        {
            AttackShape e = expectedArea.Shapes[i];
            AttackShape a = actual.Area.Shapes[i];
            Assert.AreEqual(e.GetType(), a.GetType(), $"{what}[{i}]: 종류");
            Assert.AreEqual(e.MinHeight, a.MinHeight, Eps, $"{what}[{i}]: minHeight");
            Assert.AreEqual(e.MaxHeight, a.MaxHeight, Eps, $"{what}[{i}]: maxHeight");

            var expectedLoops = new List<Vector3[]>();
            var actualLoops = new List<Vector3[]>();
            e.AppendOutlines(expectedPose, expectedLoops);
            a.AppendOutlines(actual.Pose, actualLoops);
            Assert.AreEqual(expectedLoops.Count, actualLoops.Count, $"{what}[{i}]: 고리 수");
            for (int l = 0; l < expectedLoops.Count; l++)
            {
                Assert.AreEqual(expectedLoops[l].Length, actualLoops[l].Length, $"{what}[{i}]: 고리 점 수");
                for (int p = 0; p < expectedLoops[l].Length; p++)
                    Assert.Less((expectedLoops[l][p] - actualLoops[l][p]).sqrMagnitude, Eps * Eps, $"{what}[{i}]: 외곽선 점 {p}");
            }
        }
    }

    // 경계 위 = 판정 안이고, 바닥 평면으로 0.01m 안 되는 곳 어딘가가 판정 밖.
    static void AssertOnPlanarBoundary(AttackShape shape, in AttackShapePose pose, Vector3 point)
    {
        Assert.IsTrue(shape.Contains(pose, point), $"{shape} 외곽선 점이 판정 밖: {point}");
        bool outsideNearby = false;
        for (int k = 0; k < 16 && !outsideNearby; k++)
        {
            float rad = k * Mathf.PI / 8f;
            outsideNearby = !shape.Contains(pose, point + new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * 0.01f);
        }
        Assert.IsTrue(outsideNearby, $"{shape} 외곽선 점 주변 0.01m 가 모두 판정 안 — 경계가 아니다: {point}");
    }
}
