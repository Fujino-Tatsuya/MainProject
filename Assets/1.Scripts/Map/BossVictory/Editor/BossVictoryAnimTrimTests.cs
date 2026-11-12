using NUnit.Framework;

/// <summary>
/// 보스 처치 연출 — 애니 클립 노드 끝 자르기(PLAN-boss-victory-glitch 255 후속 7) — 클립 길이 → 프레임 · 자르기 값 자르기(늘릴 수 없음·끝까지 = 안 자름) ·
/// 멈춤 판정 · 잘린 끝에서 느려짐 판정 · 기본 = 안 자름.
/// 실행 = <c>Tools/Tests/전투 EditMode 테스트 실행</c>(필터 ^BossVictoryAnimTrim).
/// </summary>
public sealed class BossVictoryAnimTrimTests
{
    const float Eps = 1e-5f;

    [Test]
    public void 클립_길이를_프레임으로()
    {
        Assert.AreEqual(60, BossVictoryAnimTrim.Frames(2f, 30f));
        Assert.AreEqual(42, BossVictoryAnimTrim.Frames(1.4f, 30f));
        Assert.AreEqual(43, BossVictoryAnimTrim.Frames(1.43f, 30f), "반올림");
        Assert.AreEqual(30, BossVictoryAnimTrim.Frames(1f, 0f), "프레임 레이트 없음 = 30");
        Assert.AreEqual(0, BossVictoryAnimTrim.Frames(-1f, 30f));
        Assert.AreEqual(1.4f, BossVictoryAnimTrim.SnapToFrame(1.41f, 30f), Eps);
    }

    [Test]
    public void 자르기_값_자르기()
    {
        Assert.AreEqual(0f, BossVictoryAnimTrim.ClampTrim(0f, 2f, 30f), "0 = 안 자름");
        Assert.AreEqual(1.4f, BossVictoryAnimTrim.ClampTrim(1.41f, 2f, 30f), Eps, "프레임에 맞춤");
        Assert.AreEqual(0f, BossVictoryAnimTrim.ClampTrim(2.5f, 2f, 30f), "원래 끝보다 늘릴 수 없다 = 안 자름");
        Assert.AreEqual(0f, BossVictoryAnimTrim.ClampTrim(1.99f, 2f, 30f), "끝에서 반 프레임 안 = 안 자름");
        Assert.AreEqual(59f / 30f, BossVictoryAnimTrim.ClampTrim(59f / 30f, 2f, 30f), Eps, "끝 1프레임 앞은 잘림");
        Assert.AreEqual(1f / 30f, BossVictoryAnimTrim.ClampTrim(0.001f, 2f, 30f), Eps, "최소 1프레임");
        Assert.AreEqual(3f, BossVictoryAnimTrim.ClampTrim(3f, 0f, 30f), Eps, "원래 길이를 모르면 프레임만");
    }

    [Test]
    public void 재생_길이와_프레임_표시()
    {
        Assert.IsFalse(BossVictoryAnimTrim.IsTrimmed(0f, 2f));
        Assert.IsTrue(BossVictoryAnimTrim.IsTrimmed(1.4f, 2f));
        Assert.IsFalse(BossVictoryAnimTrim.IsTrimmed(2.5f, 2f), "다른 캐릭터 클립이 더 짧으면 그 캐릭터는 안 잘림");
        Assert.AreEqual(2f, BossVictoryAnimTrim.PlayedLength(0f, 2f), Eps);
        Assert.AreEqual(1.4f, BossVictoryAnimTrim.PlayedLength(1.4f, 2f), Eps);
        Assert.AreEqual(2f, BossVictoryAnimTrim.PlayedLength(2.5f, 2f), Eps);
        Assert.AreEqual("42/60f", BossVictoryAnimTrim.FrameLabel(1.4f, 2f, 30f));
        Assert.AreEqual("60/60f", BossVictoryAnimTrim.FrameLabel(0f, 2f, 30f));
    }

    [Test]
    public void 멈춤_판정()
    {
        Assert.IsFalse(BossVictoryAnimTrim.ShouldFreeze(0f, 5f), "안 자름 = 멈추지 않음(지금 동작)");
        Assert.IsFalse(BossVictoryAnimTrim.ShouldFreeze(1.4f, 1.39f));
        Assert.IsTrue(BossVictoryAnimTrim.ShouldFreeze(1.4f, 1.4f));
        Assert.IsTrue(BossVictoryAnimTrim.ShouldFreeze(1.4f, 1.5f), "한 프레임 넘쳐도 멈춘다(그 프레임으로 되돌림)");
    }

    [Test]
    public void 잘린_끝에서_느려짐()
    {
        Assert.IsTrue(BossVictoryAnimTrim.SlowAtTrimEnd(0f, 1f, 1.5f), "마커 끔 · 자른 끝이 End 앞 = 자른 끝에서");
        Assert.IsFalse(BossVictoryAnimTrim.SlowAtTrimEnd(0f, 1.6f, 1.5f), "End 가 먼저 = End 에서(지금 규칙)");
        Assert.IsFalse(BossVictoryAnimTrim.SlowAtTrimEnd(2f, 1f, 1.5f), "느려짐 시작 마커가 켜져 있으면 마커");
        Assert.IsFalse(BossVictoryAnimTrim.SlowAtTrimEnd(0f, 0f, 1.5f), "안 자름 = End 에서");
        Assert.IsTrue(BossVictoryAnimTrim.SlowAtTrimEnd(0f, 1f, -1f), "End 이벤트 없음 = 자른 끝에서(영영 안 느려지지 않게)");
    }

    [Test]
    public void 기본은_안_자름()
    {
        Assert.AreEqual(0f, BossVictoryTimeline.CreateDefault().finisherTrimSeconds);
        Assert.AreEqual(0f, new BossVictoryTimeline().finisherTrimSeconds);
    }
}
