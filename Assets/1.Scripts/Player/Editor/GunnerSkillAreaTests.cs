using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 153 5b단계 — 거너 Q 충전 레이저 판정 도형. 직사각형(최대 충전 기준, 기준점 = 발사 원점 + 조준 방향)이 예전 발사 상자
/// (<c>SkillHitVolume.Beam</c>: 발사 원점에서 length · beamWidth · 높이 2 를 원점 높이 중심)와 격자에서 같은지 — 충전 최소/최대·지형 차단 길이·광폭 레이저 ×1.3,
/// 잔류 포격이 저장한 포즈·도형으로 같은 영역을 재판정하는지, SO 값, 실제 Overlap 수집과 알려진 차이(판정 점 = 발사 원점 쪽 가장 가까운 점).
/// 거너 E(냉각 백스텝)는 피해 판정이 없다.
/// </summary>
public sealed class GunnerSkillAreaTests
{
    const string QDataPath = "Assets/9.ScriptableObject/Player/Gunner/GunnerChargeLaserData.asset";
    const string TraitFolder = "Assets/9.ScriptableObject/Traits/Gunner/";
    const float OldBeamHeight = 2f;
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

    static GunnerChargeLaserData QData()
    {
        var data = AssetDatabase.LoadAssetAtPath<GunnerChargeLaserData>(QDataPath);
        Assert.IsNotNull(data, QDataPath);
        return data;
    }

    static IEnumerable<Vector3> Headings()
    {
        yield return Vector3.forward;
        yield return Vector3.right;
        yield return new Vector3(-1f, 0f, 1f).normalized;
        yield return new Vector3(0.3f, 0f, -1f).normalized;
    }

    // 예전 발사 상자 안(경계 포함) — GunnerBeamAttack.OverlapBeamBox 가 쓰던 SkillHitVolume.Beam 그대로.
    static bool OldBeamContains(Vector3 origin, Vector3 direction, float length, float width, Vector3 point)
    {
        SkillHitVolume old = SkillHitVolume.Beam(origin, direction, length, width, OldBeamHeight, ~0);
        Vector3 local = Quaternion.Inverse(old.Rotation) * (point - old.Center);
        return Mathf.Abs(local.x) <= old.HalfExtents.x && Mathf.Abs(local.y) <= old.HalfExtents.y && Mathf.Abs(local.z) <= old.HalfExtents.z;
    }

    // 예전 상자 축 격자 — 경계에서 Margin 안쪽 점은 건너뛴다(허용 오차 차이).
    static void AssertMatchesOldBeam(GunnerChargeLaserData data, Vector3 caster, Vector3 heading, float length, float width, string label)
    {
        Vector3 origin = GunnerChargeLaserSkill.FireOrigin(caster, data);
        AttackShapeSet area = data.FireArea(length, width);
        AttackShapePose pose = data.FirePose(origin, heading);
        Quaternion axes = Quaternion.LookRotation(heading);
        float halfWidth = width * 0.5f;
        float halfHeight = OldBeamHeight * 0.5f;
        int inside = 0, outside = 0;

        for (float x = -halfWidth - 0.5f; x <= halfWidth + 0.5f; x += 0.13f)
        for (float y = -halfHeight - 0.4f; y <= halfHeight + 0.4f; y += 0.17f)
        for (float z = -0.6f; z <= length + 0.6f; z += 0.29f)
        {
            if (Mathf.Abs(Mathf.Abs(x) - halfWidth) < Margin || Mathf.Abs(Mathf.Abs(y) - halfHeight) < Margin ||
                Mathf.Abs(z) < Margin || Mathf.Abs(z - length) < Margin)
                continue;

            Vector3 point = origin + axes * new Vector3(x, y, z);
            bool expected = OldBeamContains(origin, heading, length, width, point);
            Assert.AreEqual(expected, area.Contains(pose, point), $"{label} heading={heading} local=({x:0.00},{y:0.00},{z:0.00})");
            if (expected) inside++; else outside++;
        }

        Assert.Greater(inside, 0, label + " 안 표본");
        Assert.Greater(outside, 0, label + " 밖 표본");
    }

    [Test]
    public void QArea_SoValues_MaxChargeRect()
    {
        GunnerChargeLaserData data = QData();
        Assert.AreEqual(AttackShapeOrigin.Point, data.HitArea.Origin, "기준점 = 발사 원점 + 조준 방향");
        Assert.AreEqual(1, data.HitArea.Shapes.Count);
        var rect = data.HitArea.Shapes[0] as AttackRectShape;
        Assert.IsNotNull(rect, "직사각형");
        Assert.AreEqual(16f, rect.Length, 1e-6f, "예전 maxRange");
        Assert.AreEqual(1.2f, rect.Width, 1e-6f, "예전 beamWidth");
        Assert.AreEqual(0f, rect.BackOffset, 1e-6f);
        Assert.AreEqual(-1f, rect.MinHeight, 1e-6f, "예전 beamHeight 2 를 원점 높이 중심");
        Assert.AreEqual(1f, rect.MaxHeight, 1e-6f);
        Assert.AreEqual(0f, rect.ForwardOffset, 1e-6f);
        Assert.AreEqual(0f, rect.SideOffset, 1e-6f);
        Assert.AreEqual(0f, rect.YawOffset, 1e-6f);
        Assert.AreEqual(rect.Length, data.MaxRange, 1e-6f, "최대 충전 사거리 = 도형 length");
        Assert.AreEqual(rect.Width, data.BeamWidth, 1e-6f, "판정 폭 = 도형 width");
        Assert.AreEqual(6f, data.MinRange, 1e-6f, "최소 충전 사거리는 그대로");

        var errors = new List<string>();
        data.HitArea.Validate(errors);
        Assert.IsEmpty(errors, string.Join("\n", errors));
    }

    [Test]
    public void QArea_MinAndMaxCharge_MatchOldBeamBox_ForEveryHeading()
    {
        GunnerChargeLaserData data = QData();
        var caster = new Vector3(2f, 0.3f, -1f);
        foreach (Vector3 heading in Headings())
        {
            AssertMatchesOldBeam(data, caster, heading, data.RangeAt(0f), data.BeamWidth, "최소 충전");
            AssertMatchesOldBeam(data, caster, heading, data.RangeAt(1f), data.BeamWidth, "최대 충전");
            AssertMatchesOldBeam(data, caster, heading, data.RangeAt(0.5f), data.BeamWidth, "절반 충전");
            AssertMatchesOldBeam(data, caster, heading, 7.3f, data.BeamWidth, "지형 차단 7.3m");
        }
    }

    [Test]
    public void QArea_WideLaser_WidthTimes130_MatchesOldBeamBox()
    {
        GunnerChargeLaserData data = QData();
        TraitDefinition wide = AssetDatabase.LoadAssetAtPath<GunnerTraitDefinition>(TraitFolder + "GUN_1_WIDTH.asset");
        Assert.IsNotNull(wide, "GUN_1_WIDTH");
        var set = new TraitModifierSet();
        set.Rebuild(new List<int> { wide.IdHash }, _ => wide);
        float width = data.BeamWidth * set.AreaMultiplier(GunnerTraitRules.QAttackKey, TraitAreaAxis.Width);
        Assert.AreEqual(data.BeamWidth * 1.3f, width, 1e-5f);

        var caster = new Vector3(-3f, 0f, 4f);
        foreach (Vector3 heading in Headings())
        {
            AssertMatchesOldBeam(data, caster, heading, data.RangeAt(0f), width, "광폭 최소");
            AssertMatchesOldBeam(data, caster, heading, data.RangeAt(1f), width, "광폭 최대");
        }

        var rect = (AttackRectShape)data.FireArea(data.RangeAt(1f), width).Shapes[0];
        Assert.AreEqual(width, rect.Width, 1e-5f);
        Assert.AreEqual(data.MaxRange, rect.Length, 1e-5f, "사거리 그대로");
        Assert.AreEqual(-1f, rect.MinHeight, 1e-6f, "높이 그대로");
        Assert.AreEqual(1f, rect.MaxHeight, 1e-6f);
        Assert.AreEqual(1.2f, data.BeamWidth, 1e-6f, "SO 원본 불변");
    }

    [Test]
    public void QEcho_ReusesStoredPoseAndShape()
    {
        GunnerChargeLaserData data = QData();
        var def = AssetDatabase.LoadAssetAtPath<GunnerTraitDefinition>(TraitFolder + "GUN_4_ECHO_Q.asset");
        Assert.IsNotNull(def, "GUN_4_ECHO_Q");
        TraitDerivedAttackEffect effect = GunnerTraitTooltip.FindDerived(def, GunnerTraitRules.QEchoDerivedKey);
        Assert.IsNotNull(effect);
        TraitDerivedAttack echo = effect.CreateRuntime(def.IdHash);

        Vector3 origin = GunnerChargeLaserSkill.FireOrigin(new Vector3(1f, 0f, 2f), data);
        Vector3 heading = new Vector3(1f, 0f, 1f).normalized;
        float range = data.RangeAt(0.7f);
        float width = data.BeamWidth * 1.3f;
        var plan = new GunnerEchoPlan();
        plan.AddOriginalShot(origin, heading, range, width, 100);
        // 집속 연사 둘째 발은 다른 자리에서 쏴도 잔류는 첫 발 포즈·도형.
        plan.AddOriginalShot(origin + Vector3.right * 3f, Vector3.left, data.RangeAt(0f), data.BeamWidth, 100);
        Assert.IsTrue(plan.TryBuildEcho(true, SkillEndReason.Completed, echo, out GunnerDerivedShot shot));

        AttackShapePose original = data.FirePose(origin, heading);
        AttackShapePose echoPose = data.FirePose(shot.Origin, shot.Direction);
        Assert.That(Vector3.Distance(original.Position, echoPose.Position), Is.LessThan(1e-5f));
        Assert.That(Vector3.Distance(original.Forward, echoPose.Forward), Is.LessThan(1e-5f));

        var rect = (AttackRectShape)data.FireArea(shot.Range, shot.Width).Shapes[0];
        Assert.AreEqual(range, rect.Length, 1e-5f, "저장한 충전 사거리(지형 차단 전 — 발사 때 다시 잰다)");
        Assert.AreEqual(width, rect.Width, 1e-5f, "저장한 광폭 폭");
        AssertMatchesOldBeam(data, new Vector3(1f, 0f, 2f), heading, shot.Range, shot.Width, "잔류 포격");
    }

    // ── 실제 Overlap 수집 ───────────────────────────────────────────────

    GameObject Sphere(string name, Vector3 position, float radius)
    {
        var go = new GameObject(name);
        created.Add(go);
        go.transform.position = position;
        go.AddComponent<SphereCollider>().radius = radius;
        return go;
    }

    [Test]
    public void QArea_RealOverlap_CenterHit_SideGrazeIsKnownDifference()
    {
        GunnerChargeLaserData data = QData();
        Vector3 caster = new Vector3(5000f, 0f, 5000f); // 열린 씬 콜라이더와 떨어진 곳
        Vector3 origin = GunnerChargeLaserSkill.FireOrigin(caster, data);
        float length = data.RangeAt(1f);

        GameObject center = Sphere("Center", origin + new Vector3(0f, 0f, 10f), 0.3f);
        GameObject farEnd = Sphere("FarEnd", origin + new Vector3(0f, 0f, length + 0.4f), 0.5f); // 끝을 넘어 걸침 — 가까운 면이 안
        GameObject beyond = Sphere("Beyond", origin + new Vector3(0f, 0f, length + 1f), 0.5f);
        GameObject graze = Sphere("Graze", origin + new Vector3(1.2f, 0f, 10f), 1f); // 몸 옆면만 레이저 폭에 걸침
        Physics.SyncTransforms();

        var roots = new HashSet<GameObject> { center, farEnd, beyond, graze };
        var hits = new List<AttackShapeHit>();
        AttackShapeQuery.Collect(data.FireArea(length, data.BeamWidth), data.FirePose(origin, Vector3.forward), ~0,
            QueryTriggerInteraction.Collide, new Collider[32],
            c => roots.Contains(c.gameObject) ? c.gameObject : null, new HashSet<Object>(), hits);
        CollectionAssert.AreEquivalent(new Object[] { center, farEnd }, hits.Select(h => h.Target).ToArray());

        // 예전 상자 겹침은 옆면만 걸친 큰 몸도 맞혔다 — 판정 점(발사 원점 쪽 가장 가까운 점)이 폭 밖이라 이제는 빠진다(attack-shapes.md §9).
        SkillHitVolume old = SkillHitVolume.Beam(origin, Vector3.forward, length, data.BeamWidth, OldBeamHeight, ~0);
        var buffer = new Collider[32];
        int count = old.Overlap(buffer, ~0, QueryTriggerInteraction.Collide);
        Assert.IsTrue(buffer.Take(count).Any(c => c.gameObject == graze), "예전 상자는 옆면 걸침을 맞혔다");
    }
}
