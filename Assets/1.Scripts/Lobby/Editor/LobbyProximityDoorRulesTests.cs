using NUnit.Framework;
using UnityEngine;

/// <summary>로비 근접 문 열림/닫힘 히스테리시스·진행도·평면 거리 — 할 일 78.</summary>
public sealed class LobbyProximityDoorRulesTests
{
    const float Open = 2f;
    const float Close = 3f;

    static float Sqr(float d) => d * d;

    [Test]
    public void 닫힌_문은_열림_반경_안_경계_포함에서_열린다()
    {
        Assert.That(LobbyProximityDoorRules.NextOpen(false, Sqr(1f), Open, Close), Is.True);
        Assert.That(LobbyProximityDoorRules.NextOpen(false, Sqr(2f), Open, Close), Is.True);
    }

    [Test]
    public void 닫힌_문은_두_반경_사이에서_닫힌_채로_있다()
    {
        Assert.That(LobbyProximityDoorRules.NextOpen(false, Sqr(2.5f), Open, Close), Is.False);
    }

    [Test]
    public void 열린_문은_두_반경_사이에서_열린_채로_있다()
    {
        Assert.That(LobbyProximityDoorRules.NextOpen(true, Sqr(2.5f), Open, Close), Is.True);
        Assert.That(LobbyProximityDoorRules.NextOpen(true, Sqr(3f), Open, Close), Is.True);
    }

    [Test]
    public void 열린_문은_닫힘_반경_밖에서_닫힌다()
    {
        Assert.That(LobbyProximityDoorRules.NextOpen(true, Sqr(3.01f), Open, Close), Is.False);
    }

    [Test]
    public void 대상이_없으면_닫힌다()
    {
        Assert.That(LobbyProximityDoorRules.NextOpen(true, float.PositiveInfinity, Open, Close), Is.False);
        Assert.That(LobbyProximityDoorRules.NextOpen(false, float.PositiveInfinity, Open, Close), Is.False);
    }

    [Test]
    public void 경계를_오가도_한_번만_바뀐다()
    {
        var isOpen = false;
        var changes = 0;
        foreach (var d in new[] { 2.6f, 1.9f, 2.1f, 1.95f, 2.4f, 2.9f, 2.2f, 3.2f, 2.9f, 2.5f })
        {
            var next = LobbyProximityDoorRules.NextOpen(isOpen, Sqr(d), Open, Close);
            if (next != isOpen) changes++;
            isOpen = next;
        }

        Assert.That(changes, Is.EqualTo(2)); // 1.9 에서 열림, 3.2 에서 닫힘
        Assert.That(isOpen, Is.False);
    }

    [Test]
    public void 닫힘_반경이_더_작으면_열림_반경으로_올려_쓴다()
    {
        Assert.That(LobbyProximityDoorRules.NextOpen(true, Sqr(1.9f), Open, 1f), Is.True);
        Assert.That(LobbyProximityDoorRules.NextOpen(true, Sqr(2.1f), Open, 1f), Is.False);
    }

    [Test]
    public void 진행도는_여닫는_시간이_따로다()
    {
        Assert.That(LobbyProximityDoorRules.StepProgress(0f, true, 0.5f, 1f, 0.25f), Is.EqualTo(0.5f).Within(1e-5f));
        Assert.That(LobbyProximityDoorRules.StepProgress(1f, false, 0.5f, 1f, 0.25f), Is.EqualTo(0.75f).Within(1e-5f));
    }

    [Test]
    public void 진행도는_0과_1을_넘지_않고_0초면_즉시다()
    {
        Assert.That(LobbyProximityDoorRules.StepProgress(0.9f, true, 0.5f, 0.5f, 1f), Is.EqualTo(1f));
        Assert.That(LobbyProximityDoorRules.StepProgress(0.1f, false, 0.5f, 0.5f, 1f), Is.EqualTo(0f));
        Assert.That(LobbyProximityDoorRules.StepProgress(0f, true, 0f, 0.5f, 0f), Is.EqualTo(1f));
        Assert.That(LobbyProximityDoorRules.StepProgress(1f, false, 0.5f, 0f, 0f), Is.EqualTo(0f));
    }

    [Test]
    public void 플레이어_반대쪽으로_연다()
    {
        Assert.That(LobbyProximityDoorRules.SwingSign(0.5f), Is.EqualTo(-1));
        Assert.That(LobbyProximityDoorRules.SwingSign(-0.5f), Is.EqualTo(1));
        Assert.That(LobbyProximityDoorRules.SwingSign(0f), Is.EqualTo(-1));
    }

    [Test]
    public void 양의_각도는_문짝을_로컬_플러스Z_쪽으로_넘긴다()
    {
        // 문짝은 힌지(원점)에서 로컬 -X 로 뻗는다 — SwingSign +1 이 정말 +Z 쪽인지.
        LobbyProximityDoorRules.LeafPose(Vector3.zero, Quaternion.identity, 90f, Vector3.zero, out var pos, out var rot);
        var tip = pos + rot * new Vector3(-1.27f, 0f, 0f);

        Assert.That(tip.z, Is.GreaterThan(1.2f));
    }

    [Test]
    public void 회전_중심은_제자리에_있다()
    {
        var closedPos = new Vector3(3f, 0.5f, -2f);
        var closedRot = Quaternion.Euler(0f, 270f, 0f);
        var pivot = new Vector3(0f, 0f, -0.22f);

        foreach (var angle in new[] { 0f, 45f, -90f, 90f })
        {
            LobbyProximityDoorRules.LeafPose(closedPos, closedRot, angle, pivot, out var pos, out var rot);
            var before = closedPos + closedRot * pivot;
            var after = pos + rot * pivot;
            Assert.That(Vector3.Distance(before, after), Is.LessThan(1e-4f), $"angle {angle}");
        }
    }

    [Test]
    public void 각도_0이면_닫힌_자세_그대로다()
    {
        var closedPos = new Vector3(1f, 2f, 3f);
        var closedRot = Quaternion.Euler(0f, 30f, 0f);
        LobbyProximityDoorRules.LeafPose(closedPos, closedRot, 0f, new Vector3(0f, 0f, 0.06f), out var pos, out var rot);

        Assert.That(Vector3.Distance(pos, closedPos), Is.LessThan(1e-5f));
        Assert.That(Quaternion.Angle(rot, closedRot), Is.LessThan(1e-3f));
    }

    [Test]
    public void 넘어가는_쪽_면을_축으로_돌면_문짝이_힌지_밖_문틀로_파고들지_않는다()
    {
        // 문짝 두께 Z -0.22~0.06, 힌지 모서리 X=0. 문틀(벽)은 X>0 쪽. 열린 뒤 문짝의 모든 모서리가 X<=0 이어야 한다.
        foreach (var sign in new[] { 1, -1 })
        {
            var pivot = new Vector3(0f, 0f, sign > 0 ? 0.06f : -0.22f);
            LobbyProximityDoorRules.LeafPose(Vector3.zero, Quaternion.identity, sign * 90f, pivot, out var pos, out var rot);
            foreach (var x in new[] { 0f, -1.27f })
            foreach (var z in new[] { -0.22f, 0.06f })
            {
                var corner = pos + rot * new Vector3(x, 0f, z);
                Assert.That(corner.x, Is.LessThanOrEqualTo(1e-4f), $"sign {sign} corner ({x},{z})");
            }
        }
    }

    // 할 일 99 — 다음 자세가 플레이어 몸과 겹치면 그 프레임은 멈춘다.
    static float Frame(float progress, bool targetOpen, bool blocked) =>
        LobbyProximityDoorRules.ResolveProgress(progress,
            LobbyProximityDoorRules.StepProgress(progress, targetOpen, 0.5f, 0.5f, 0.1f), blocked);

    [Test]
    public void 다음_자세가_막히면_진행도가_그대로고_풀리면_다시_연다()
    {
        var p = Frame(0f, true, false);
        Assert.That(p, Is.EqualTo(0.2f).Within(1e-5f));

        for (var i = 0; i < 5; i++)
            p = Frame(p, true, true);
        Assert.That(p, Is.EqualTo(0.2f).Within(1e-5f));

        p = Frame(p, true, false);
        Assert.That(p, Is.EqualTo(0.4f).Within(1e-5f));
    }

    [Test]
    public void 닫는_중에도_막히면_멈추고_풀리면_닫힌다()
    {
        var p = Frame(1f, false, true);
        Assert.That(p, Is.EqualTo(1f));

        p = Frame(p, false, false);
        Assert.That(p, Is.EqualTo(0.8f).Within(1e-5f));
    }

    [Test]
    public void 닫히다_다시_열릴_때도_막히면_멈추고_풀리면_연다()
    {
        var p = Frame(1f, false, false);
        p = Frame(p, false, false);
        Assert.That(p, Is.EqualTo(0.6f).Within(1e-5f));

        var reopen = LobbyProximityDoorRules.NextOpen(false, Sqr(1f), Open, Close);
        Assert.That(reopen, Is.True);

        p = Frame(p, reopen, true);
        Assert.That(p, Is.EqualTo(0.6f).Within(1e-5f));

        p = Frame(p, reopen, false);
        Assert.That(p, Is.EqualTo(0.8f).Within(1e-5f));
    }

    [Test]
    public void 스폰된_살아_있는_플레이어만_문_판정_대상이다()
    {
        Assert.That(LobbyProximityDoorRules.IsDoorTarget(true, PlayerLifeState.Alive), Is.True);
        Assert.That(LobbyProximityDoorRules.IsDoorTarget(false, PlayerLifeState.Alive), Is.False);
        Assert.That(LobbyProximityDoorRules.IsDoorTarget(true, PlayerLifeState.DeadPresentation), Is.False);
        Assert.That(LobbyProximityDoorRules.IsDoorTarget(true, PlayerLifeState.Soul), Is.False);
        Assert.That(LobbyProximityDoorRules.IsDoorTarget(true, PlayerLifeState.PermanentDead), Is.False);
    }

    static readonly Bounds LeafBox = new Bounds(new Vector3(-0.635f, 1f, -0.08f), new Vector3(1.27f, 2f, 0.28f));

    [Test]
    public void 문짝이_몸을_관통하면_겹침이다()
    {
        // 반경 0.38 > 두께 0.28 — 캡슐이 문짝 양면을 다 넘어선다.
        Assert.That(LobbyProximityDoorRules.LeafBoxOverlapsUprightCapsule(LeafBox, Vector3.zero, Quaternion.identity,
            new Vector3(-0.6f, 0.9f, -0.08f), 1.79f, 0.38f), Is.True);
    }

    [Test]
    public void 문짝이_돌면_문짝_기준으로_본다()
    {
        // +90° 면 문짝이 +Z 로 넘어간다 — 그 자리의 몸과 겹치고, 닫힌 자세에서는 안 겹친다.
        var body = new Vector3(-0.1f, 0.9f, 0.7f);
        Assert.That(LobbyProximityDoorRules.LeafBoxOverlapsUprightCapsule(LeafBox, Vector3.zero, Quaternion.identity,
            body, 1.79f, 0.38f), Is.False);
        Assert.That(LobbyProximityDoorRules.LeafBoxOverlapsUprightCapsule(LeafBox, Vector3.zero, Quaternion.Euler(0f, 90f, 0f),
            body, 1.79f, 0.38f), Is.True);
    }

    [Test]
    public void 반경_밖이거나_높이가_어긋나면_겹침이_아니다()
    {
        // 문짝 +Z 면(0.06)에서 반경(0.38)보다 조금 더 떨어짐.
        Assert.That(LobbyProximityDoorRules.LeafBoxOverlapsUprightCapsule(LeafBox, Vector3.zero, Quaternion.identity,
            new Vector3(-0.6f, 0.9f, 0.45f), 1.79f, 0.38f), Is.False);
        // 문짝(Y 0~2) 위에 떠 있음.
        Assert.That(LobbyProximityDoorRules.LeafBoxOverlapsUprightCapsule(LeafBox, Vector3.zero, Quaternion.identity,
            new Vector3(-0.6f, 3f, -0.08f), 1.79f, 0.38f), Is.False);
    }

    [Test]
    public void 거리는_평면_거리다()
    {
        Assert.That(LobbyProximityDoorRules.PlanarSqrDistance(new Vector3(3f, 10f, 4f), Vector3.zero), Is.EqualTo(25f).Within(1e-5f));
    }
}
