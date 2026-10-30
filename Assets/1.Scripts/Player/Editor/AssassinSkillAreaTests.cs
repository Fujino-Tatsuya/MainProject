using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 153 5c단계 — 어쌔신 Q·E 스킬 판정 도형.
/// Q 돌진 경로 직사각형(기준점 = 구간 시작 + 돌진 방향, 구간 길이로 맞춤)이 예전 구간 상자(<c>PathBox</c>: 폭 pathWidth 1.2 · 높이 pathHeight 2 를 발부터 위로)와 격자에서 같은지,
/// 추격의 궤적이 같은 도형을 지나온 선분 길이로 재사용하는지, 변신 E 원(기준점 = 지면 조준점)이 예전 원기둥(반경 2 · 높이 3, AABB 원 거름)과 같은지·칼날의 영역 ×1.25,
/// SO 값, 실제 Overlap 수집(몸 겹침 — 긴 구간 옆면 걸침·출발점 뒤 몸은 예전 상자와 같음, 변신 E 는 원 중심이 큰 몸 안이면 맞음·AABB 모서리만 걸친 몸은 빠짐).
/// 일반 E(단검 강화)는 판정이 없다(강타는 평타 2단계).
/// </summary>
public sealed class AssassinSkillAreaTests
{
    const string QDataPath = "Assets/9.ScriptableObject/Player/Assassin/AssassinDashStrikeSkillData.asset";
    const string TransformedQDataPath = "Assets/9.ScriptableObject/Player/Assassin/AssassinTransformedDashStrikeSkillData.asset";
    const string EDataPath = "Assets/9.ScriptableObject/Player/Assassin/AssassinCircleStrikeSkillData.asset";
    const string TraitFolder = "Assets/9.ScriptableObject/Traits/Assassin/";
    const float OldPathWidth = 1.2f;
    const float OldPathHeight = 2f;
    const float OldAreaHeight = 3f;
    const float Margin = 0.01f;

    readonly List<GameObject> created = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject go in created)
            if (go != null)
                Object.DestroyImmediate(go);
        created.Clear();
    }

    static T Load<T>(string path) where T : Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        Assert.IsNotNull(asset, path);
        return asset;
    }

    static IEnumerable<Vector3> Headings()
    {
        yield return Vector3.forward;
        yield return Vector3.right;
        yield return new Vector3(-1f, 0f, 1f).normalized;
        yield return new Vector3(0.3f, 0f, -1f).normalized;
    }

    // 예전 구간 상자 안(경계 포함) — AssassinDashStrikeSkill.PathBox(origin, dir, from, to) 그대로: 구간 가운데 · 높이 절반 올림 · LookRotation(dir).
    static bool OldPathBoxContains(Vector3 origin, Vector3 direction, float from, float to, Vector3 point)
    {
        float length = to - from;
        Vector3 center = origin + direction * (from + length * 0.5f) + Vector3.up * (OldPathHeight * 0.5f);
        var half = new Vector3(OldPathWidth * 0.5f, OldPathHeight * 0.5f, length * 0.5f);
        Vector3 local = Quaternion.Inverse(Quaternion.LookRotation(direction)) * (point - center);
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
    }

    // 예전 구간 상자 축 격자 — 경계에서 Margin 안쪽 점은 건너뛴다(허용 오차 차이).
    static void AssertMatchesOldPath(AssassinDashStrikeSkillData data, Vector3 origin, Vector3 heading, float from, float to, string label)
    {
        AttackShapeSet area = AssassinDashStrikeSkill.PathSegment(data, origin, origin, heading, from, to, out AttackShapePose pose);
        Quaternion axes = Quaternion.LookRotation(heading);
        float halfWidth = OldPathWidth * 0.5f;
        int inside = 0, outside = 0;

        for (float x = -halfWidth - 0.5f; x <= halfWidth + 0.5f; x += 0.13f)
        for (float y = -0.4f; y <= OldPathHeight + 0.4f; y += 0.17f)
        for (float z = from - 0.6f; z <= to + 0.6f; z += 0.11f)
        {
            if (Mathf.Abs(Mathf.Abs(x) - halfWidth) < Margin || Mathf.Abs(y) < Margin || Mathf.Abs(y - OldPathHeight) < Margin ||
                Mathf.Abs(z - from) < Margin || Mathf.Abs(z - to) < Margin)
                continue;

            Vector3 point = origin + axes * new Vector3(x, y, z);
            bool expected = OldPathBoxContains(origin, heading, from, to, point);
            Assert.AreEqual(expected, area.Contains(pose, point), $"{label} heading={heading} [{from},{to}] local=({x:0.00},{y:0.00},{z:0.00})");
            if (expected) inside++; else outside++;
        }

        Assert.Greater(inside, 0, label + " 안 표본");
        Assert.Greater(outside, 0, label + " 밖 표본");
    }

    // 예전 변신 E 판정 — 원기둥 외접 상자(발 높이 ~ +3) 겹침 + AABB 수평 투영이 원에 걸림. 점(아주 작은 AABB)으로 본다.
    static bool OldCircleContains(Vector3 areaCenter, float radius, Vector3 point)
    {
        float dy = point.y - areaCenter.y;
        return dy >= 0f && dy <= OldAreaHeight &&
               AssassinCircleStrikeRules.OverlapsCircle(areaCenter, radius, new Bounds(point, Vector3.zero));
    }

    static void AssertMatchesOldCircle(AssassinCircleStrikeSkillData data, Vector3 caster, Vector3 areaCenter, float multiplier, string label)
    {
        float radius = 2f * multiplier;
        AttackShapeSet area = data.StrikeArea(multiplier);
        AttackShapePose pose = data.StrikePose(caster, areaCenter);
        int inside = 0, outside = 0;

        for (float x = -radius - 0.5f; x <= radius + 0.5f; x += 0.13f)
        for (float y = -0.4f; y <= OldAreaHeight + 0.4f; y += 0.23f)
        for (float z = -radius - 0.5f; z <= radius + 0.5f; z += 0.13f)
        {
            float planar = Mathf.Sqrt(x * x + z * z);
            if (Mathf.Abs(planar - radius) < Margin || Mathf.Abs(y) < Margin || Mathf.Abs(y - OldAreaHeight) < Margin)
                continue;

            Vector3 point = areaCenter + new Vector3(x, y, z);
            bool expected = OldCircleContains(areaCenter, radius, point);
            Assert.AreEqual(expected, area.Contains(pose, point), $"{label} local=({x:0.00},{y:0.00},{z:0.00})");
            if (expected) inside++; else outside++;
        }

        Assert.Greater(inside, 0, label + " 안 표본");
        Assert.Greater(outside, 0, label + " 밖 표본");
    }

    // ── SO 값 ──────────────────────────────────────────────────────────

    [TestCase(QDataPath)]
    [TestCase(TransformedQDataPath)]
    public void QArea_SoValues_PathRect(string path)
    {
        var data = Load<AssassinDashStrikeSkillData>(path);
        Assert.AreEqual(AttackShapeOrigin.Point, data.HitArea.Origin, "기준점 = 구간 시작 + 돌진 방향");
        Assert.AreEqual(1, data.HitArea.Shapes.Count);
        var rect = data.HitArea.Shapes[0] as AttackRectShape;
        Assert.IsNotNull(rect, "직사각형");
        Assert.AreEqual(5f, rect.Length, 1e-6f, "기준 길이 = SO 돌진 거리");
        Assert.AreEqual(data.DashDistance, rect.Length, 1e-6f);
        Assert.AreEqual(OldPathWidth, rect.Width, 1e-6f, "예전 pathWidth");
        Assert.AreEqual(0f, rect.BackOffset, 1e-6f);
        Assert.AreEqual(0f, rect.MinHeight, 1e-6f, "예전 pathHeight 2 를 발부터 위로");
        Assert.AreEqual(OldPathHeight, rect.MaxHeight, 1e-6f);
        Assert.AreEqual(0f, rect.ForwardOffset, 1e-6f);
        Assert.AreEqual(0f, rect.SideOffset, 1e-6f);
        Assert.AreEqual(0f, rect.YawOffset, 1e-6f);
        Assert.AreEqual(OldPathWidth, data.PathWidth, 1e-6f, "경로 폭 = 도형 width");

        var errors = new List<string>();
        data.HitArea.Validate(errors);
        Assert.IsEmpty(errors, string.Join("\n", errors));
    }

    [Test]
    public void EArea_SoValues_Circle()
    {
        var data = Load<AssassinCircleStrikeSkillData>(EDataPath);
        Assert.AreEqual(AttackShapeOrigin.Point, data.HitArea.Origin, "기준점 = 지면 조준점");
        Assert.AreEqual(1, data.HitArea.Shapes.Count);
        var circle = data.HitArea.Shapes[0] as AttackCircleShape;
        Assert.IsNotNull(circle, "원");
        Assert.AreEqual(2f, circle.Radius, 1e-6f, "예전 aoeRadius");
        Assert.AreEqual(0f, circle.MinRadius, 1e-6f);
        Assert.AreEqual(0f, circle.MinHeight, 1e-6f, "예전 areaHeight 3 을 발부터 위로");
        Assert.AreEqual(OldAreaHeight, circle.MaxHeight, 1e-6f);
        Assert.AreEqual(0f, circle.ForwardOffset, 1e-6f);
        Assert.AreEqual(0f, circle.SideOffset, 1e-6f);
        Assert.AreEqual(circle.Radius, data.AoeRadius, 1e-6f, "조준 원 반경 = 도형 radius");
        Assert.AreEqual(circle.Radius, data.AreaRadius, 1e-6f, "VFX 반경 = 도형 radius");
        Assert.AreEqual(1.5f, data.CenterDistance, 1e-6f, "원 중심 거리는 그대로 castRange");
        Assert.AreEqual(circle.Radius, PlayerSkillTargeting.GroundMarkerRadius(data, 1f), 1e-6f, "조준 원 = 판정 원");

        var errors = new List<string>();
        data.HitArea.Validate(errors);
        Assert.IsEmpty(errors, string.Join("\n", errors));
    }

    // ── Q 돌진 경로 ────────────────────────────────────────────────────

    [Test]
    public void QPath_Segments_MatchOldPathBox_ForEveryHeading()
    {
        var data = Load<AssassinDashStrikeSkillData>(QDataPath);
        var origin = new Vector3(2f, 0.3f, -1f);
        foreach (Vector3 heading in Headings())
        {
            AssertMatchesOldPath(data, origin, heading, 0f, 1f, "첫 틱 구간");
            AssertMatchesOldPath(data, origin, heading, 1.3f, 2.4f, "중간 구간");
            AssertMatchesOldPath(data, origin, heading, 0f, data.DashDistance, "End 몰아치기 전체");
            AssertMatchesOldPath(data, origin, heading, 3.7f, 3.95f, "벽 보고 직전 짧은 구간");
        }
    }

    [Test]
    public void QPath_TransformedQ_SameShape()
    {
        var data = Load<AssassinTransformedDashStrikeSkillData>(TransformedQDataPath);
        AssertMatchesOldPath(data, new Vector3(-4f, 0f, 3f), new Vector3(1f, 0f, -0.4f).normalized, 0.5f, 2f, "변신 Q");
    }

    [Test]
    public void QTrail_ReusesPathShape_WithTraveledLength()
    {
        // 추격의 궤적 정의는 판정 도형을 비운다 = 원래 공격(Q 경로) 치수 재사용(4단계 규칙).
        var def = Load<AssassinTraitDefinition>(TraitFolder + "ASN_4_Q_TRAIL.asset");
        TraitDerivedAttackEffect effect = def.Effects.OfType<TraitDerivedAttackEffect>()
            .FirstOrDefault(e => e.DerivedKey == AssassinDerivedAttackKeys.QTrail);
        Assert.IsNotNull(effect, "ASN_4_Q_TRAIL 파생 공격");
        Assert.IsTrue(effect.HitArea == null || effect.HitArea.IsEmpty, "궤적 도형은 비움 = Q 경로 재사용");

        var data = Load<AssassinDashStrikeSkillData>(QDataPath);
        var start = new Vector3(1f, 0.2f, 2f);
        foreach (Vector3 heading in Headings())
        {
            // 궤적 = 출발점 → 실제 이동 거리(벽 보고로 줄어든 상한). 판정 = PathArea(이동 거리) + PathPose(선분 시작, 선분 방향).
            foreach (float traveled in new[] { 1.5f, data.DashDistance })
            {
                Vector3 end = AssassinTrailRules.SegmentEnd(start, heading, traveled);
                AttackShapeSet area = data.PathArea(Vector3.Distance(start, end));
                AttackShapePose pose = data.PathPose(Vector3.zero, start, end - start);
                AttackShapeSet segment = AssassinDashStrikeSkill.PathSegment(data, Vector3.zero, start, heading, 0f, traveled, out AttackShapePose segmentPose);
                Assert.AreEqual(((AttackRectShape)segment.Shapes[0]).Length, ((AttackRectShape)area.Shapes[0]).Length, 1e-5f, "같은 도형 = 경로 [0, 이동 거리]");
                Assert.That(Vector3.Distance(segmentPose.Position, pose.Position), Is.LessThan(1e-5f));
                AssertMatchesOldPath(data, start, heading, 0f, traveled, $"추격의 궤적 {traveled}m");
            }
        }
    }

    // ── 변신 E 원 ──────────────────────────────────────────────────────

    [Test]
    public void ECircle_MatchesOldCircleColumn()
    {
        var data = Load<AssassinCircleStrikeSkillData>(EDataPath);
        var caster = new Vector3(2f, 0.5f, -1f);
        AssertMatchesOldCircle(data, caster, AssassinCircleStrikeSkill.AreaCenter(caster, caster + new Vector3(1.5f, 3f, 0f)), 1f, "특성 없음");
        AssertMatchesOldCircle(data, caster, caster, 1f, "중심 = 시전자(방향 없음)");
    }

    [Test]
    public void ECircle_BladeArea_RadiusTimes125_MatchesOldScaledColumn()
    {
        var data = Load<AssassinCircleStrikeSkillData>(EDataPath);
        var def = Load<AssassinTraitDefinition>(TraitFolder + "ASN_3_E_AREA.asset");
        var set = new TraitModifierSet();
        set.Rebuild(new List<int> { def.IdHash }, _ => def);
        float multiplier = set.AreaMultiplier(AssassinTraitAreaRules.CircleStrike, TraitAreaAxis.Radius);
        Assert.AreEqual(1.25f, multiplier, 1e-5f, "칼날의 영역 반경 ×1.25");

        var caster = new Vector3(-3f, 0f, 4f);
        Vector3 center = caster + new Vector3(0f, 0f, 1.5f);
        AssertMatchesOldCircle(data, caster, center, multiplier, "칼날의 영역");

        var circle = (AttackCircleShape)data.StrikeArea(multiplier).Shapes[0];
        Assert.AreEqual(2.5f, circle.Radius, 1e-5f, "예전 ScaledRadius = 2 × 1.25");
        Assert.AreEqual(0f, circle.MinHeight, 1e-6f, "높이 그대로");
        Assert.AreEqual(OldAreaHeight, circle.MaxHeight, 1e-6f);
        Assert.AreEqual(2f, data.AreaRadius, 1e-6f, "SO 원본 불변");
        Assert.AreSame(data.HitArea, data.StrikeArea(1f), "배율 1 = 원본 그대로(복사 없음)");

        // 미리보기 원기둥도 같은 반경·높이(외곽선 = 판정).
        SkillHitVolume preview = AssassinCircleStrikeSkill.PreviewVolume(data, caster, center, multiplier);
        Assert.IsTrue(preview.IsCircle);
        Assert.AreEqual(2.5f, preview.Radius, 1e-5f);
        Assert.That(Vector3.Distance(center, preview.CircleCenter), Is.LessThan(1e-5f));
        Assert.AreEqual(OldAreaHeight * 0.5f, preview.HalfExtents.y, 1e-5f);
    }

    // ── 실제 Overlap 수집 ───────────────────────────────────────────────

    GameObject Sphere(string name, Vector3 position, float radius)
    {
        var go = new GameObject("AssassinSkillAreaTest_" + name) { hideFlags = HideFlags.DontSave };
        created.Add(go);
        go.transform.position = position;
        go.AddComponent<SphereCollider>().radius = radius;
        return go;
    }

    static List<Object> CollectPath(AssassinDashStrikeSkillData data, Vector3 origin, Vector3 heading, float[] cuts, HashSet<GameObject> roots)
    {
        var hits = new List<AttackShapeHit>();
        var seen = new HashSet<Object>();
        for (int i = 0; i + 1 < cuts.Length; i++)
        {
            AttackShapeSet area = AssassinDashStrikeSkill.PathSegment(data, origin, origin, heading, cuts[i], cuts[i + 1], out AttackShapePose pose);
            AttackShapeQuery.Collect(area, pose, ~0, QueryTriggerInteraction.Collide, new Collider[32],
                c => roots.Contains(c.gameObject) ? c.gameObject : null, seen, hits);
        }
        return hits.Select(h => h.Target).ToList();
    }

    static bool OldPathOverlaps(AssassinDashStrikeSkillData data, Vector3 origin, Vector3 heading, float from, float to, GameObject target)
    {
        float length = to - from;
        Vector3 center = origin + heading * (from + length * 0.5f) + Vector3.up * (OldPathHeight * 0.5f);
        var buffer = new Collider[32];
        int count = Physics.OverlapBoxNonAlloc(center, new Vector3(OldPathWidth * 0.5f, OldPathHeight * 0.5f, length * 0.5f), buffer,
            Quaternion.LookRotation(heading), ~0, QueryTriggerInteraction.Collide);
        return buffer.Take(count).Any(c => c.gameObject == target);
    }

    [Test]
    public void QPath_RealOverlap_SideGrazeAndBehindStart_SameAsOldBox_TickOrLongSegment()
    {
        var data = Load<AssassinDashStrikeSkillData>(QDataPath);
        var origin = new Vector3(5000f, 0f, 5000f); // 열린 씬 콜라이더와 떨어진 곳
        Vector3 heading = Vector3.forward;

        GameObject center = Sphere("Center", origin + new Vector3(0f, 0.5f, 3f), 0.3f);
        // 3 m 앞 반경 1 m 몸 — 옆면만 경로 폭(±0.6)에 걸침: 중심 옆 1.45·1.59 m 맞음, 1.7 m 빠짐(0.6 + 1 = 1.6 까지).
        GameObject graze = Sphere("Graze", origin + new Vector3(1.45f, 0.5f, 3f), 1f);
        GameObject edge = Sphere("Edge", origin + new Vector3(-1.59f, 0.5f, 3f), 1f);
        GameObject beside = Sphere("Beside", origin + new Vector3(1.7f, 0.5f, 3f), 1f);
        GameObject behind = Sphere("Behind", origin + new Vector3(0f, 0.5f, -0.6f), 1f); // 중심이 출발점 뒤 — 몸 앞쪽이 경로에 걸침
        Physics.SyncTransforms();
        var roots = new HashSet<GameObject> { center, graze, edge, beside, behind };
        var expected = new Object[] { center, graze, edge, behind };

        foreach (GameObject go in roots)
            Assert.AreEqual(expected.Contains(go), OldPathOverlaps(data, origin, heading, 0f, data.DashDistance, go), "예전 상자 " + go.name);

        // 매 틱 구간(60 m/s · 60fps ≈ 1m)이든 한 구간으로 몰아 훑든(End 몰아치기·추격의 궤적 5m) 같다.
        CollectionAssert.AreEquivalent(expected, CollectPath(data, origin, heading, new[] { 0f, 1f, 2f, 3f, 4f, 5f }, roots), "틱 구간 1m");
        CollectionAssert.AreEquivalent(expected, CollectPath(data, origin, heading, new[] { 0f, data.DashDistance }, roots), "한 구간 5m");
    }

    [Test]
    public void ECircle_RealOverlap_BodyOverlap_FootInsideAndDiagonalGraze()
    {
        var data = Load<AssassinCircleStrikeSkillData>(EDataPath);
        var caster = new Vector3(6000f, 0f, 6000f);
        Vector3 areaCenter = caster + new Vector3(0f, 0f, 1.5f);

        GameObject inside = Sphere("Inside", areaCenter + new Vector3(0.5f, 0.5f, 0f), 0.3f);
        GameObject rim = Sphere("Rim", areaCenter + new Vector3(2.4f, 0.5f, 0f), 0.5f);         // 표면이 반경 2 안(1.9)
        GameObject diagonal = Sphere("Diagonal", areaCenter + new Vector3(1.8f, 0.5f, 1.8f), 0.6f); // 대각선 표면 1.95 — 맞음
        GameObject corner = Sphere("Corner", areaCenter + new Vector3(1.8f, 0.5f, 1.8f), 0.5f);    // 대각선 표면 2.05 — 몸은 원에 안 닿음(AABB 모서리만 원 안)
        GameObject big = Sphere("Big", areaCenter + new Vector3(2.5f, 0.5f, 0f), 3f);            // 원 중심이 몸 안, 몸 중심은 2.5m — 맞음
        Physics.SyncTransforms();
        var roots = new HashSet<GameObject> { inside, rim, diagonal, corner, big };

        var hits = new List<AttackShapeHit>();
        AttackShapeQuery.Collect(data.StrikeArea(1f), data.StrikePose(caster, areaCenter), ~0, QueryTriggerInteraction.Collide,
            new Collider[32], c => roots.Contains(c.gameObject) ? c.gameObject : null, new HashSet<Object>(), hits);
        CollectionAssert.AreEquivalent(new Object[] { inside, rim, diagonal, big }, hits.Select(h => h.Target).ToArray());

        // 예전 = 원기둥 외접 상자 겹침 + AABB 원 거름 — 몸이 원에 안 닿는 AABB 모서리(corner)도 맞혔다. 남은 차이는 이것 하나(attack-shapes.md §9).
        SkillHitVolume old = SkillHitVolume.CircleColumn(areaCenter, 2f, OldAreaHeight, ~0);
        var buffer = new Collider[32];
        int count = old.Overlap(buffer, ~0, QueryTriggerInteraction.Collide);
        var oldAccepted = buffer.Take(count).Where(c => old.Accepts(c.bounds)).Select(c => c.gameObject).ToList();
        CollectionAssert.IsSubsetOf(new[] { inside, rim, diagonal, corner, big }, oldAccepted);
    }
}
