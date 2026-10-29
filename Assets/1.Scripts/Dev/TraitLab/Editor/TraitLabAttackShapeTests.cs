using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 특성 랩 공격 범위 기즈모(할 일 152)를 위해 판정 코드에서 꺼낸 공용 형상 계산 — 판정과 기즈모가 같은 값을 쓰는지 고정한다.
/// (기즈모 그리기 자체는 Play 확인.)
/// </summary>
public sealed class TraitLabAttackShapeTests
{
    const float Eps = 1e-4f;

    [Test]
    public void QBlastApex_IsQEndPlusHeadingTimesOffset()
    {
        var end = new Vector3(1f, 0.5f, 2f);
        Assert.AreEqual(end, PaladinTraitRules.QBlastApex(end, Vector3.forward, 0f), "forwardOffset 0 = Q 끝 지점");
        Vector3 pushed = PaladinTraitRules.QBlastApex(end, Vector3.right, 1.5f);
        Assert.AreEqual(2.5f, pushed.x, Eps);
        Assert.AreEqual(0.5f, pushed.y, Eps);
        Assert.AreEqual(2f, pushed.z, Eps);
    }

    [Test]
    public void AssassinDashPathBox_CoversSegment_PathWidthHeight()
    {
        var data = ScriptableObject.CreateInstance<AssassinDashStrikeSkillData>();
        try
        {
            var origin = new Vector3(1f, 0f, 1f);
            AssassinDashStrikeSkill.PathBox(origin, Vector3.forward, 1f, 4f, data, out Vector3 center, out Vector3 half, out Quaternion rotation);

            Assert.AreEqual(1f, center.x, Eps);
            Assert.AreEqual(data.PathHeight * 0.5f, center.y, Eps, "높이 절반 올림");
            Assert.AreEqual(1f + 2.5f, center.z, Eps, "구간 [1, 4] 가운데");
            Assert.AreEqual(data.PathWidth * 0.5f, half.x, Eps);
            Assert.AreEqual(data.PathHeight * 0.5f, half.y, Eps);
            Assert.AreEqual(1.5f, half.z, Eps, "구간 길이 절반");
            Assert.AreEqual(1f, Vector3.Dot(rotation * Vector3.forward, Vector3.forward), Eps, "돌진 방향");
        }
        finally
        {
            Object.DestroyImmediate(data);
        }
    }

    [Test]
    public void GunnerChargeLaserFireOrigin_IsCasterPlusMuzzleHeight()
    {
        var data = ScriptableObject.CreateInstance<GunnerChargeLaserData>();
        try
        {
            var caster = new Vector3(3f, 1f, -2f);
            Vector3 origin = GunnerChargeLaserSkill.FireOrigin(caster, data);
            Assert.AreEqual(caster + Vector3.up * data.MuzzleHeight, origin);
        }
        finally
        {
            Object.DestroyImmediate(data);
        }
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
}
