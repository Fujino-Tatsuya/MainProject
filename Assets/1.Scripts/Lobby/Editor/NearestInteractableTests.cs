using NUnit.Framework;
using UnityEngine;
using Candidate = NearestInteractable.Candidate;

/// <summary>로비 상호작용 대상 고르기·서버 거리 재검증(평면 거리) — PLAN-48 §4.6, C10.</summary>
public sealed class NearestInteractableTests
{
    static Candidate At(float x, float z, float radius = 2f, bool usable = true) =>
        new Candidate(new Vector3(x, 0f, z), radius, usable);

    [Test]
    public void 빈_목록이나_null_이면_없다()
    {
        Assert.That(NearestInteractable.Pick(Vector3.zero, new Candidate[0]), Is.EqualTo(-1));
        Assert.That(NearestInteractable.Pick(Vector3.zero, null), Is.EqualTo(-1));
    }

    [Test]
    public void 반경_안에서_가장_가까운_것을_고른다()
    {
        var candidates = new[] { At(1.5f, 0f), At(0f, 0.5f), At(-1f, 0f) };

        Assert.That(NearestInteractable.Pick(Vector3.zero, candidates), Is.EqualTo(1));
    }

    [Test]
    public void 전부_반경_밖이면_없다()
    {
        var candidates = new[] { At(3f, 0f), At(0f, -2.5f) };

        Assert.That(NearestInteractable.Pick(Vector3.zero, candidates), Is.EqualTo(-1));
    }

    [Test]
    public void 반경_경계는_안이다()
    {
        Assert.That(NearestInteractable.Pick(Vector3.zero, new[] { At(2f, 0f) }), Is.EqualTo(0));
    }

    [Test]
    public void 사용_불가는_더_가까워도_건너뛴다()
    {
        var candidates = new[] { At(0.2f, 0f, usable: false), At(1.8f, 0f) };

        Assert.That(NearestInteractable.Pick(Vector3.zero, candidates), Is.EqualTo(1));
    }

    [Test]
    public void 전부_사용_불가면_없다()
    {
        var candidates = new[] { At(0.2f, 0f, usable: false), At(1f, 0f, usable: false) };

        Assert.That(NearestInteractable.Pick(Vector3.zero, candidates), Is.EqualTo(-1));
    }

    [Test]
    public void 동률이면_앞_인덱스()
    {
        var candidates = new[] { At(1f, 0f), At(-1f, 0f), At(0f, 1f) };

        Assert.That(NearestInteractable.Pick(Vector3.zero, candidates), Is.EqualTo(0));
    }

    [Test]
    public void 후보마다_자기_반경을_쓴다()
    {
        // 가까운 쪽은 반경이 작아 밖, 먼 쪽은 반경이 커서 안.
        var candidates = new[] { At(1f, 0f, radius: 0.5f), At(0f, 3f, radius: 4f) };

        Assert.That(NearestInteractable.Pick(Vector3.zero, candidates), Is.EqualTo(1));
    }

    [Test]
    public void 높이_차는_거리에_넣지_않는다()
    {
        var candidates = new[] { new Candidate(new Vector3(1f, 10f, 0f), 2f, true) };

        Assert.That(NearestInteractable.Pick(Vector3.zero, candidates), Is.EqualTo(0));
        Assert.That(NearestInteractable.PlanarDistance(Vector3.zero, new Vector3(3f, 7f, 4f)), Is.EqualTo(5f).Within(1e-5f));
    }

    [Test]
    public void 서버_재검증은_반경_더하기_여유까지_허용한다()
    {
        var target = Vector3.zero;

        Assert.That(NearestInteractable.IsWithinReach(new Vector3(2f, 0f, 0f), target, 2f, 0.5f), Is.True);
        Assert.That(NearestInteractable.IsWithinReach(new Vector3(2.5f, 0f, 0f), target, 2f, 0.5f), Is.True, "경계 포함");
        Assert.That(NearestInteractable.IsWithinReach(new Vector3(2.6f, 0f, 0f), target, 2f, 0.5f), Is.False);
        Assert.That(NearestInteractable.IsWithinReach(new Vector3(0f, 5f, 2.4f), target, 2f, 0.5f), Is.True, "높이 무시");
    }

    [Test]
    public void 서버_재검증_음수_반경_여유는_0으로_본다()
    {
        Assert.That(NearestInteractable.IsWithinReach(Vector3.zero, Vector3.zero, -1f, -1f), Is.True);
        Assert.That(NearestInteractable.IsWithinReach(new Vector3(0.01f, 0f, 0f), Vector3.zero, -1f, -1f), Is.False);
    }

    [TestCase(5d, 5)]
    [TestCase(4.2d, 5)]
    [TestCase(4d, 4)]
    [TestCase(0.01d, 1)]
    [TestCase(0d, 0)]
    [TestCase(-1d, 0)]
    public void 카운트다운_표시_초는_올림(double remaining, int expected)
    {
        Assert.That(LobbyUiStrings.CountdownDisplaySeconds(remaining), Is.EqualTo(expected));
    }

    [Test]
    public void 준비_수_문구에_숫자가_들어간다()
    {
        var text = LobbyUiStrings.FormatReadyCount(2, 3);

        StringAssert.Contains("2", text);
        StringAssert.Contains("3", text);
    }
}
