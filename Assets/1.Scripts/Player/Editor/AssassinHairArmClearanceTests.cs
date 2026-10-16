using NUnit.Framework;
using UnityEngine;

// 할 일 47 — 머리카락 팔 밀어내기 후처리의 순수 판단: 겹치면 캡슐 밖으로, 안 겹치면 그대로, 상한에서 멈춤, 회전만(길이 보존).
public sealed class AssassinHairArmClearanceTests
{
    private const float Radius = .078f; // 상완 0.06 + hairRadius 0.018

    // 뿌리 (0,0,0) 에서 -Y 로 0.2m 내려가는 마디, 캡슐은 X 축과 나란히 y=-0.15 를 지난다.
    private static readonly Vector3 Pivot = Vector3.zero;
    private static readonly Vector3 End = new(0f, -.2f, 0f);

    private static AssassinHairArmClearance.Capsule[] CapsuleAt(float z)
    {
        return new[] { new AssassinHairArmClearance.Capsule(new Vector3(-.1f, -.15f, z), new Vector3(.1f, -.15f, z), Radius) };
    }

    private static float Clearance(Vector3 pivot, Vector3 end, AssassinHairArmClearance.Capsule cap)
    {
        float best = float.MaxValue;
        for (int i = 1; i <= 24; i++)
        {
            Vector3 p = Vector3.Lerp(pivot, end, i / 24f);
            Vector3 d = cap.b - cap.a;
            float t = Mathf.Clamp01(Vector3.Dot(p - cap.a, d) / d.sqrMagnitude);
            best = Mathf.Min(best, Vector3.Distance(p, cap.a + d * t) - cap.radius);
        }
        return best;
    }

    [Test]
    public void NoOverlapLeavesSegmentAlone()
    {
        Quaternion push = AssassinHairArmClearance.SolveSegment(Pivot, End, CapsuleAt(.2f), 1, 1, 45f, out float degrees, out bool clamped);

        Assert.That(degrees, Is.EqualTo(0f));
        Assert.That(clamped, Is.False);
        Assert.That(Quaternion.Angle(push, Quaternion.identity), Is.LessThan(1e-3f));
    }

    [Test]
    public void OverlapIsPushedOutOfCapsule()
    {
        var caps = CapsuleAt(.05f); // 마디가 캡슐 축에서 5cm — 2.8cm 파고듦
        Assume.That(Clearance(Pivot, End, caps[0]), Is.LessThan(-.02f));

        Quaternion push = AssassinHairArmClearance.SolveSegment(Pivot, End, caps, 1, 1, 60f, out float degrees, out bool clamped);
        Vector3 moved = Pivot + push * (End - Pivot);

        Assert.That(clamped, Is.False);
        Assert.That(degrees, Is.GreaterThan(0f).And.LessThan(60f));
        Assert.That(Clearance(Pivot, moved, caps[0]), Is.GreaterThan(-.002f), "캡슐 밖(표본 간격 오차 2mm 이내)");
        Assert.That(moved.z, Is.LessThan(0f), "캡슐 반대쪽(-Z)으로 밀려야 한다");
    }

    [Test]
    public void RotationStopsAtMaxDegrees()
    {
        var caps = CapsuleAt(.005f); // 거의 축 위 — 다 풀려면 큰 각이 필요하다

        Quaternion push = AssassinHairArmClearance.SolveSegment(Pivot, End, caps, 1, 1, 10f, out float degrees, out bool clamped);

        Assert.That(clamped, Is.True);
        Assert.That(degrees, Is.EqualTo(10f));
        Assert.That(Quaternion.Angle(Quaternion.identity, push), Is.EqualTo(10f).Within(.05f));
    }

    [Test]
    public void ZeroLimitDisablesPush()
    {
        Quaternion push = AssassinHairArmClearance.SolveSegment(Pivot, End, CapsuleAt(.05f), 1, 1, 0f, out float degrees, out _);

        Assert.That(degrees, Is.EqualTo(0f));
        Assert.That(Quaternion.Angle(push, Quaternion.identity), Is.LessThan(1e-3f));
    }

    [Test]
    public void PushIsRotationOnlySoLengthIsKept()
    {
        Quaternion push = AssassinHairArmClearance.SolveSegment(Pivot, End, CapsuleAt(.03f), 1, 1, 45f, out _, out _);
        Vector3 moved = Pivot + push * (End - Pivot);

        Assert.That(Vector3.Distance(Pivot, moved), Is.EqualTo(.2f).Within(1e-5f));
    }

    [Test]
    public void OnlyFourArmCapsulesAreUsed()
    {
        var capsules = new[]
        {
            new AssassinHairMotion.BodyCapsule { name = "Chest" },
            new AssassinHairMotion.BodyCapsule { name = "Waist" },
            new AssassinHairMotion.BodyCapsule { name = "Left upper arm" },
            new AssassinHairMotion.BodyCapsule { name = "Left forearm" },
            new AssassinHairMotion.BodyCapsule { name = "Left thigh" },
            new AssassinHairMotion.BodyCapsule { name = "Right upper arm" },
            new AssassinHairMotion.BodyCapsule { name = "Right forearm" },
            new AssassinHairMotion.BodyCapsule { name = "Right thigh" },
        };

        Assert.That(AssassinHairArmClearance.FindCapsules(capsules, AssassinHairArmClearance.ArmCapsuleNames), Is.EqualTo(new[] { 2, 3, 5, 6 }));
        Assert.That(AssassinHairArmClearance.FindCapsules(capsules, AssassinHairArmClearance.TorsoCapsuleNames), Is.EqualTo(new[] { 0, 1 }));
        Assert.That(AssassinHairArmClearance.FindCapsules(null, AssassinHairArmClearance.ArmCapsuleNames), Is.Empty);
    }

    [Test]
    public void TorsoGuardKeepsPushedSegmentFromGoingDeeper()
    {
        // 팔(+Z 쪽)이 마디를 -Z 로 미는데, 그쪽에 몸통이 있다. 몸통 반경 = 지금 거리(기준선) → 지금보다 가까이 못 간다.
        var arm = CapsuleAt(.05f)[0];
        Vector3 torsoA = new(-.1f, -.15f, -.11f), torsoB = new(.1f, -.15f, -.11f);
        float baseline = AssassinHairArmClearance.SampledDistance(Pivot, End, torsoA, torsoB);
        var caps = new[] { arm, new AssassinHairArmClearance.Capsule(torsoA, torsoB, Mathf.Min(.2f, baseline)) };

        Quaternion unguarded = AssassinHairArmClearance.SolveSegment(Pivot, End, caps, 1, 1, 45f, out _, out _);
        Assume.That(AssassinHairArmClearance.SampledDistance(Pivot, Pivot + unguarded * (End - Pivot), torsoA, torsoB), Is.LessThan(baseline - .005f),
            "몸통을 안 보면 몸통 쪽으로 더 들어가는 배치여야 한다");

        Quaternion guarded = AssassinHairArmClearance.SolveSegment(Pivot, End, caps, 2, 1, 45f, out _, out _);
        Vector3 moved = Pivot + guarded * (End - Pivot);

        Assert.That(AssassinHairArmClearance.SampledDistance(Pivot, moved, torsoA, torsoB), Is.GreaterThan(baseline - .002f));
    }
}
