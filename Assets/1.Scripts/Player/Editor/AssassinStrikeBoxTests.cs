using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 어쌔신 평타 판정 도형(할 일 153 2단계 — 할 일 144 상자 대체). 클래스 이름은 테스트 러너 목록 때문에 그대로 둔다.
/// 예전 코드 부채꼴(f11f5d84 의 AssassinConeAttack.IsInsideCone — 발밑 꼭짓점, 반경 r, 전체 각 θ)과 경계 안/밖이 같은지,
/// 조준 방향으로 도는지, SO 에 그 값이 들었는지, 프리팹에서 옛 상자가 빠졌는지.
/// </summary>
public class AssassinStrikeBoxTests
{
    private const float Eps = 1e-4f;
    private const string PrefabPath = "Assets/2.Prefabs/Player/Assassin/Player_Assassin.prefab";
    private const string DataPath = "Assets/9.ScriptableObject/Player/Assassin/AssassinBasicAttackData.asset";
    private static readonly string[] OldHitboxNames = { "Normal1", "Normal2", "Normal3", "Normal4", "Enhanced", "Transformed" };

    // 예전 부채꼴 판정(바닥 평면): 거리 ≤ r 이고 정면과의 각 ≤ θ/2. 거리 0 은 안.
    private static bool OldCone(Vector3 origin, Vector3 forward, float range, float angle, Vector3 point)
    {
        Vector3 to = point - origin;
        to.y = 0f;
        if (to.sqrMagnitude > range * range)
            return false;
        return to.sqrMagnitude < 0.0001f || Vector3.Angle(forward, to) <= angle * 0.5f;
    }

    [TestCase(1.5f, 90f)]
    [TestCase(1.5f, 100f)]
    [TestCase(1.6f, 100f)]
    [TestCase(1.7f, 120f)]
    [TestCase(1.8f, 120f)]
    public void 도형은_예전_부채꼴과_경계_안밖이_같다(float range, float angle)
    {
        var area = new AttackShapeSet(AttackShapeOrigin.CasterAim, AssassinBasicAttackStepData.StrikeSector(range, angle));
        var origin = new Vector3(2f, 0f, -3f);
        AttackShapePose pose = area.ResolvePose(origin, Vector3.forward, Vector3.zero, Vector3.zero);

        // 반경 안쪽·바깥쪽 0.02m, 각 경계 안쪽·바깥쪽 1° — 예전 판정과 같은 답이어야 한다.
        foreach (float r in new[] { 0.3f, range - 0.02f, range + 0.02f })
        {
            for (float a = -180f; a < 180f; a += 1f)
            {
                foreach (float offset in new[] { -1f, 1f })
                {
                    float deg = Mathf.Abs(a) == angle * 0.5f ? a + offset * Mathf.Sign(a) : a;
                    Vector3 point = origin + Quaternion.Euler(0f, deg, 0f) * Vector3.forward * r;
                    Assert.AreEqual(OldCone(origin, Vector3.forward, range, angle, point), area.Contains(pose, point),
                        $"r={range} θ={angle} 점 거리 {r} 각 {deg}");
                }
            }
        }

        Assert.IsTrue(area.Contains(pose, origin), "발밑(꼭짓점)은 안 — 예전과 같다");
    }

    [Test]
    public void 높이는_발밑_위아래_range_까지()
    {
        var area = new AttackShapeSet(AttackShapeOrigin.CasterAim, AssassinBasicAttackStepData.StrikeSector(1.5f, 90f));
        var pose = new AttackShapePose(Vector3.zero, Vector3.forward);
        Assert.IsTrue(area.Contains(pose, new Vector3(0f, 1.49f, 1f)));
        Assert.IsTrue(area.Contains(pose, new Vector3(0f, -1.49f, 1f)));
        Assert.IsFalse(area.Contains(pose, new Vector3(0f, 1.6f, 1f)), "예전 구(반경 r) 위로는 안 닿는다");
    }

    [Test]
    public void 도형은_조준_방향으로_돈다()
    {
        var area = new AttackShapeSet(AttackShapeOrigin.CasterAim, AssassinBasicAttackStepData.StrikeSector(1.5f, 90f));
        var origin = new Vector3(5f, 0f, 5f);
        AttackShapePose right = area.ResolvePose(origin, Vector3.right, Vector3.zero, Vector3.zero);

        Assert.IsTrue(area.Contains(right, new Vector3(6.4f, 0f, 5f)), "오른쪽 앞 1.4m");
        Assert.IsFalse(area.Contains(right, new Vector3(3.6f, 0f, 5f)), "뒤쪽은 밖");
        Assert.IsFalse(area.Contains(right, new Vector3(6.6f, 0f, 5f)), "사거리 1.5 밖");
        Assert.IsFalse(area.Contains(right, new Vector3(5f, 0f, 6.4f)), "원래 정면(+z)은 밖 — 조준이 오른쪽");
        Assert.IsTrue(area.Contains(right, origin + Quaternion.Euler(0f, 90f + 44f, 0f) * Vector3.forward * 1.4f), "조준 기준 44°");
        Assert.IsFalse(area.Contains(right, origin + Quaternion.Euler(0f, 90f + 46f, 0f) * Vector3.forward * 1.4f), "조준 기준 46°");
    }

    // SO 에 예전 부채꼴 값(f11f5d84 AssassinBasicAttackData.asset 단별 range·angle)이 도형으로 들어 있다.
    [Test]
    public void SO_단별_도형은_예전_부채꼴_값()
    {
        var data = AssetDatabase.LoadAssetAtPath<AssassinBasicAttackData>(DataPath);
        Assert.IsNotNull(data, DataPath);
        float[] ranges = { 1.5f, 1.5f, 1.6f, 1.7f };
        float[] angles = { 90f, 100f, 100f, 120f };
        Assert.AreEqual(4, data.NormalSteps.Length);
        for (int i = 0; i < 4; i++)
            AssertSector(data.NormalSteps[i], ranges[i], angles[i], $"일반 {i + 1}타");
        AssertSector(data.EnhancedStep, 1.8f, 120f, "강타");
        AssertSector(data.TransformedStep, 1.8f, 120f, "변신 묶음");
    }

    [Test]
    public void 프리팹에_옛_판정_상자가_없다()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.IsNotNull(prefab, PrefabPath);
        Transform defaultAttack = prefab.transform.Find("Armature/DefaultAttack");
        if (defaultAttack != null)
        {
            foreach (string name in OldHitboxNames)
                Assert.IsNull(defaultAttack.Find(name), $"Armature/DefaultAttack/{name}");
        }

        Assert.IsNotNull(prefab.transform.Find("Armature/InterruptAttack"), "간파 상자는 그대로");
    }

    private static void AssertSector(AssassinBasicAttackStepData step, float range, float angle, string label)
    {
        Assert.IsNotNull(step, label);
        Assert.IsNotNull(step.HitArea, label);
        Assert.AreEqual(AttackShapeOrigin.CasterAim, step.HitArea.Origin, label);
        Assert.AreEqual(1, step.HitArea.Shapes.Count, label);
        var sector = step.HitArea.Shapes[0] as AttackSectorShape;
        Assert.IsNotNull(sector, label + " 부채꼴");
        Assert.AreEqual(range, sector.Range, Eps, label);
        Assert.AreEqual(angle, sector.Angle, Eps, label);
        Assert.AreEqual(0f, sector.MinOffset, Eps, label);
        Assert.AreEqual(0f, sector.ForwardOffset, Eps, label);
        Assert.AreEqual(0f, sector.SideOffset, Eps, label);
        Assert.AreEqual(0f, sector.YawOffset, Eps, label);
        Assert.AreEqual(-range, sector.MinHeight, Eps, label);
        Assert.AreEqual(range, sector.MaxHeight, Eps, label);
    }
}
