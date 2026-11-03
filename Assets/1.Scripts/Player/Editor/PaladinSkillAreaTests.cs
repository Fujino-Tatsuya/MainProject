using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 153 5a단계 — 가붕이 Q·E 판정 도형. Q 직사각형이 예전 프리팹 앵커(<c>Armature/MainSkill</c>)와 같은 영역인지(모서리 안/밖·진행 방향 회전·높이·돌파의 방패 표본),
/// E 반격 부채꼴이 예전 규칙(표면 거리 ≤ 3 m · 바닥 평면 각 ≤ 60°)과 격자에서 같은지·몸 겹침(발밑이 몸 안·큰 보스·옆 변 걸침), 수호의 파동 원 유무, SO 값, 프리팹 앵커 삭제.
/// </summary>
public sealed class PaladinSkillAreaTests
{
    const string QDataPath = "Assets/9.ScriptableObject/Player/Garen/FirstMeleeMainSkillData.asset";
    const string EDataPath = "Assets/9.ScriptableObject/Player/Garen/FirstMeleeSubSkillData.asset";
    const string PrefabPath = "Assets/2.Prefabs/Player/Paladin/Player_Paladin.prefab";

    // 예전 Q 앵커(Armature 기준 축): BoxCollider center (0, 0, 0.8)·size (2, 1, 2), MainSkill 로컬 y −0.03°, Armature 높이 0.039.
    static readonly Vector3 OldQCenter = new Vector3(0f, 0.039f, 0.8f);
    static readonly Vector3 OldQSize = new Vector3(2f, 1f, 2f);
    const float OldQYaw = -0.03f;
    const float Margin = 0.01f;

    static IEnumerable<Vector3> Headings()
    {
        yield return Vector3.forward;
        yield return Vector3.right;
        yield return new Vector3(-1f, 0f, 1f).normalized;
        yield return new Vector3(0.3f, 0f, -1f).normalized;
    }

    static AttackShapeSet QArea() => new AttackShapeSet(AttackShapeOrigin.CasterAim, FirstMeleeMainSkillData.ShieldPushRect());

    // 예전 Q 상자 바닥 평면 모서리 근처 점 — 상자 축(진행 방향 + OldQYaw)에서 inset 만큼 안(음수면 밖).
    static Vector3 OldQPoint(Vector3 caster, Vector3 heading, float sx, float sz, float inset, float height)
    {
        Quaternion axes = Quaternion.LookRotation(heading) * Quaternion.Euler(0f, OldQYaw, 0f);
        float x = OldQCenter.x + sx * (OldQSize.x * 0.5f - inset);
        float z = OldQCenter.z + sz * (OldQSize.z * 0.5f - inset);
        return caster + axes * new Vector3(x, 0f, z) + Vector3.up * height;
    }

    [Test]
    public void QRect_MatchesOldAnchorCorners_ForEveryHeading()
    {
        AttackShapeSet area = QArea();
        var caster = new Vector3(2f, 0.3f, -1f);

        foreach (Vector3 heading in Headings())
        {
            AttackShapePose pose = FirstMeleeMainSkill.ResolvePose(area, caster, heading);
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Assert.IsTrue(area.Contains(pose, OldQPoint(caster, heading, sx, sz, Margin, 0f)), $"모서리 안 heading={heading} ({sx},{sz})");
                Assert.IsFalse(area.Contains(pose, OldQPoint(caster, heading, sx, sz, -Margin, 0f)), $"모서리 밖 heading={heading} ({sx},{sz})");
            }

            Assert.IsFalse(area.Contains(pose, OldQPoint(caster, heading, 0, 1, -Margin, 0f)), "앞 끝(1.8) 밖");
            Assert.IsFalse(area.Contains(pose, OldQPoint(caster, heading, 0, -1, -Margin, 0f)), "뒤 끝(0.2) 밖");
            Assert.IsTrue(area.Contains(pose, caster), "발밑(뒤 0.2 안)");
        }
    }

    [Test]
    public void QRect_Height_OldBoxBand()
    {
        AttackShapeSet area = QArea();
        var caster = new Vector3(0f, 1f, 0f);
        AttackShapePose pose = FirstMeleeMainSkill.ResolvePose(area, caster, Vector3.forward);
        Vector3 front = caster + Vector3.forward;

        Assert.IsTrue(area.Contains(pose, front), "발 높이");
        Assert.IsTrue(area.Contains(pose, front + Vector3.up * 0.53f), "상자 위 끝(0.039 + 0.5) 안");
        Assert.IsFalse(area.Contains(pose, front + Vector3.up * 0.55f), "상자 위 끝 밖");
        Assert.IsTrue(area.Contains(pose, front + Vector3.down * 0.45f), "상자 아래 끝(0.039 − 0.5) 안");
        Assert.IsFalse(area.Contains(pose, front + Vector3.down * 0.47f), "상자 아래 끝 밖");
    }

    [Test]
    public void QRect_ShieldDashSample_MovesToPathPoint_AtCasterHeight()
    {
        // 예전: 앵커 상자를 (표본 지점 − 지금 위치)의 바닥 평면 성분만큼 옮겼다 → 높이는 지금 시전자 높이, 축은 돌진 방향.
        AttackShapeSet area = QArea();
        var caster = new Vector3(1f, 0.5f, 1f);
        Vector3 heading = Vector3.right;
        Vector3 pathPoint = new Vector3(4f, 2f, 1f); // 경로 높이는 무시된다
        AttackShapePose pose = FirstMeleeMainSkill.ShieldDashSamplePose(area, pathPoint, caster.y, heading);
        var sampleFoot = new Vector3(pathPoint.x, caster.y, pathPoint.z);

        Assert.AreEqual(sampleFoot, pose.Position, "표본 지점 + 시전자 높이");
        for (int sx = -1; sx <= 1; sx += 2)
        for (int sz = -1; sz <= 1; sz += 2)
        {
            Assert.IsTrue(area.Contains(pose, OldQPoint(sampleFoot, heading, sx, sz, Margin, 0f)), $"표본 모서리 안 ({sx},{sz})");
            Assert.IsFalse(area.Contains(pose, OldQPoint(sampleFoot, heading, sx, sz, -Margin, 0f)), $"표본 모서리 밖 ({sx},{sz})");
        }
    }

    // 예전 반격 규칙(FirstMeleeGuardRules.IsInsideCounterSector, 할 일 153 에서 삭제) — 표면 거리(평면) ≤ range, 평면 각 ≤ angle/2(+0.001°),
    // 표면이 발밑이면 중심 방향. 넓은 질의는 3D 구(반경 range)였다.
    static bool OldCounter(Vector3 origin, Vector3 forward, Vector3 closest, Vector3 center, float range, float angle)
    {
        if ((closest - origin).sqrMagnitude > range * range)
            return false; // 3D 구 넓은 질의
        Vector3 to = closest - origin;
        to.y = 0f;
        if (to.sqrMagnitude > range * range)
            return false;
        if (to.sqrMagnitude < 0.0001f)
        {
            to = center - origin;
            to.y = 0f;
        }

        return to.sqrMagnitude < 0.0001f || Vector3.Angle(forward, to) <= angle * 0.5f + 0.001f;
    }

    [Test]
    public void ECounterSector_MatchesOldRule_OnGrid()
    {
        AttackShapeSet area = new AttackShapeSet(AttackShapeOrigin.CasterAim, FirstMeleeSubSkillData.CounterSector());
        var origin = new Vector3(-2f, 0f, 3f);
        Vector3 forward = new Vector3(1f, 0f, 1f).normalized;
        AttackShapePose pose = FirstMeleeSubSkill.CounterPose(area, origin, forward);
        int checkedPoints = 0;

        for (float x = -3.5f; x <= 3.5f; x += 0.25f)
        for (float z = -3.5f; z <= 3.5f; z += 0.25f)
        {
            Vector3 point = origin + new Vector3(x, 0f, z);
            Vector3 flat = point - origin;
            float distance = flat.magnitude;
            float angle = distance > 1e-3f ? Vector3.Angle(forward, flat) : 0f;
            // 경계 띠(거리 3 ± 0.02, 각 60 ± 0.5)는 허용 오차 차이라 건너뛴다.
            if (Mathf.Abs(distance - 3f) < 0.02f || Mathf.Abs(angle - 60f) < 0.5f)
                continue;

            bool expected = OldCounter(origin, forward, point, point, 3f, 120f);
            Assert.AreEqual(expected, area.Contains(pose, point), $"점 ({x}, {z}) 거리 {distance:0.##} 각 {angle:0.#}");
            checkedPoints++;
        }

        Assert.Greater(checkedPoints, 700);
    }

    [Test]
    public void ECounterSector_BodyOverlap_FootInsideAndBigBoss()
    {
        AttackShapeSet area = new AttackShapeSet(AttackShapeOrigin.CasterAim, FirstMeleeSubSkillData.CounterSector());
        AttackShapePose pose = FirstMeleeSubSkill.CounterPose(area, Vector3.zero, Vector3.forward);
        var seen = new HashSet<string>();
        bool Hit(Vector3 center, float radius)
        {
            seen.Clear();
            return AttackShapeQuery.TryAccept(area.Shapes, pose, new AttackShapeTestSphere(center, radius), "target", seen, out _);
        }

        Assert.IsTrue(Hit(new Vector3(0f, 0f, 2f), 2.5f), "발밑이 몸 안 + 중심 앞 = 안");
        // 발밑이 몸 안이면 부채꼴 꼭짓점이 몸과 겹친다 — 중심이 뒤여도 맞음(예전 '중심 방향' 특례 없음, 겹치면 맞음 규칙).
        Assert.IsTrue(Hit(new Vector3(0f, 0f, -2f), 2.5f), "발밑이 몸 안 + 중심 뒤 = 안");
        Assert.IsFalse(Hit(new Vector3(0f, 0f, -2f), 1.9f), "몸이 발밑에 못 닿는 뒤 = 밖");
        // 큰 보스 — 중심이 10 m 밖이어도 표면이 3 m 안이면 맞는다.
        Assert.IsTrue(Hit(new Vector3(0f, 0f, 10f), 7.1f), "표면 2.9 m");
        Assert.IsFalse(Hit(new Vector3(0f, 0f, 10f), 6.9f), "표면 3.1 m");
        // 부채꼴 옆 변(60°) 바깥에서 몸 옆면만 걸침.
        Vector3 edgeOutward = Quaternion.Euler(0f, 150f, 0f) * Vector3.forward; // 오른쪽 변(60°)의 바깥 법선
        Vector3 onEdge = Quaternion.Euler(0f, 60f, 0f) * Vector3.forward * 2f;
        Assert.IsTrue(Hit(onEdge + edgeOutward * 0.49f, 0.5f), "옆 변 걸침");
        Assert.IsFalse(Hit(onEdge + edgeOutward * 0.51f, 0.5f), "옆 변 떨어짐");
    }

    [Test]
    public void ECounterSector_Height_OldSphereReach()
    {
        AttackShapeSet area = new AttackShapeSet(AttackShapeOrigin.CasterAim, FirstMeleeSubSkillData.CounterSector());
        AttackShapePose pose = FirstMeleeSubSkill.CounterPose(area, Vector3.zero, Vector3.forward);
        Assert.IsTrue(area.Contains(pose, new Vector3(0f, 2.5f, 1f)), "각도는 바닥 평면(3D 로 68° 위여도 안)");
        Assert.IsFalse(area.Contains(pose, new Vector3(0f, 3.1f, 0.5f)), "반경보다 높으면 밖(예전 3D 구 질의에도 안 걸림)");
    }

    [Test]
    public void EPushArea_OnlyWithTrait()
    {
        Assert.IsNull(PaladinTraitRules.EPushArea(0f), "특성 없음 = 원 없음");
        AttackShapeSet push = PaladinTraitRules.EPushArea(3f);
        AttackShapePose pose = FirstMeleeSubSkill.CounterPose(push, Vector3.zero, Vector3.forward);
        Assert.IsTrue(push.Contains(pose, new Vector3(0f, 0f, -2.9f)), "뒤쪽도(주변 원형)");
        Assert.IsFalse(push.Contains(pose, new Vector3(0f, 0f, -3.1f)), "반경 밖");

        AttackShapeSet counter = new AttackShapeSet(AttackShapeOrigin.CasterAim, FirstMeleeSubSkillData.CounterSector());
        Assert.IsFalse(counter.Contains(FirstMeleeSubSkill.CounterPose(counter, Vector3.zero, Vector3.forward), new Vector3(0f, 0f, -2.9f)),
            "같은 점은 반격 부채꼴 밖 — 밀치기만");
    }

    [Test]
    public void DataAssets_HaveMigratedAreas()
    {
        var q = AssetDatabase.LoadAssetAtPath<FirstMeleeMainSkillData>(QDataPath);
        Assert.IsNotNull(q, QDataPath);
        AssertSameShape(FirstMeleeMainSkillData.ShieldPushRect(), q.HitArea, "Q");

        var e = AssetDatabase.LoadAssetAtPath<FirstMeleeSubSkillData>(EDataPath);
        Assert.IsNotNull(e, EDataPath);
        AssertSameShape(FirstMeleeSubSkillData.CounterSector(), e.HitArea, "E");
        var sector = (AttackSectorShape)e.HitArea.Shapes[0];
        Assert.AreEqual(3f, sector.Range, 1e-4f, "E range = 예전 반격 사거리 3");
        Assert.AreEqual(120f, sector.Angle, 1e-4f, "E angle = 예전 반격 각도 120");
    }

    static void AssertSameShape(AttackShape expected, AttackShapeSet area, string label)
    {
        Assert.IsNotNull(area, $"{label} hitArea");
        Assert.AreEqual(AttackShapeOrigin.CasterAim, area.Origin, $"{label} 기준점");
        Assert.AreEqual(1, area.Shapes.Count, $"{label} 도형 수");
        AttackShape actual = area.Shapes[0];
        Assert.AreEqual(expected.GetType(), actual?.GetType(), $"{label} 도형 종류");
        Assert.AreEqual(JsonUtility.ToJson(expected), JsonUtility.ToJson(actual), $"{label} 도형 칸");

        var errors = new List<string>();
        area.Validate(errors);
        CollectionAssert.IsEmpty(errors, $"{label} 검증");
    }

    [Test]
    public void Prefab_QAnchorRemoved_InterruptAnchorKept()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.IsNotNull(prefab, PrefabPath);

        Assert.IsNull(prefab.transform.Find("Armature/MainSkill"), "옛 Q 앵커 없음");
        Assert.IsNull(prefab.GetComponent<FirstMeleeMainSkill>().HitboxAnchor, "Q hitboxAnchor 비움");

        Transform interrupt = prefab.transform.Find("Armature/InterruptAttack");
        Assert.IsNotNull(interrupt, "간파 상자");
        Assert.IsNotNull(interrupt.GetComponent<BoxCollider>(), "간파 상자 BoxCollider");
        Assert.IsNotNull(interrupt.GetComponent<ColliderInfo>(), "간파 상자 ColliderInfo");
        Assert.AreSame(interrupt.GetComponent<ColliderInfo>(), prefab.GetComponent<FirstMeleeInterruptSkill>().HitboxAnchor, "간파 앵커 연결 그대로");
    }
}
