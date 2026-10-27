#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>할 일 88 파티 배치·봇 낭떠러지의 순수 계산 — 링 후보·간격 선택·우회 경로·안전한 한 걸음·일반몹 재우기 대기.</summary>
public sealed class QAPartyPlacement88Tests
{
    [Test]
    public void 링_후보는_반경_오름차순_각도순이고_높이는_중심()
    {
        var into = new List<Vector3>();
        var center = new Vector3(10f, 2f, -110f);
        QAPartyPlacement88.RingCandidates(center, new[] { 3f, 6f }, 4, into);

        Assert.AreEqual(8, into.Count);
        for (int i = 0; i < 4; i++)
            Assert.AreEqual(3f, QAPartyPlacement88.HorizontalDistance(into[i], center), 1e-4f, $"첫 링 i={i}");
        for (int i = 4; i < 8; i++)
            Assert.AreEqual(6f, QAPartyPlacement88.HorizontalDistance(into[i], center), 1e-4f, $"둘째 링 i={i}");
        foreach (Vector3 p in into)
            Assert.AreEqual(2f, p.y, 1e-6f);
        Assert.AreEqual(13f, into[0].x, 1e-4f, "각도 0 = +x");
        Assert.AreEqual(-110f, into[0].z, 1e-4f);
    }

    [Test]
    public void 링_후보는_0이하_반경과_칸수를_건너뛴다()
    {
        var into = new List<Vector3> { Vector3.one };
        QAPartyPlacement88.RingCandidates(Vector3.zero, new[] { 0f, -1f, 2f }, 3, into);
        Assert.AreEqual(3, into.Count, "이전 내용은 지우고 반경 2 만");
        QAPartyPlacement88.RingCandidates(Vector3.zero, new[] { 2f }, 0, into);
        Assert.AreEqual(0, into.Count);
    }

    [Test]
    public void 간격_선택은_가까운_순서를_지키며_너무_붙은_후보를_건너뛴다()
    {
        var accepted = new List<Vector3>
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(1f, 0f, 0f),     // 1m — 1.5m 미만이라 건너뜀
            new Vector3(2f, 5f, 0f),     // 수평 2m — 높이는 안 본다
            new Vector3(0f, 0f, 3f),
            new Vector3(9f, 0f, 9f),
        };
        var into = new List<Vector3>();
        int n = QAPartyPlacement88.SelectSpread(accepted, 3, 1.5f, into);
        Assert.AreEqual(3, n);
        CollectionAssert.AreEqual(new[] { accepted[0], accepted[2], accepted[3] }, into);
    }

    [Test]
    public void 간격_선택은_모자라면_고른_만큼만()
    {
        var accepted = new List<Vector3> { Vector3.zero, new Vector3(0.5f, 0f, 0f) };
        var into = new List<Vector3>();
        Assert.AreEqual(1, QAPartyPlacement88.SelectSpread(accepted, 3, 1.5f, into));
        Assert.AreEqual(0, QAPartyPlacement88.SelectSpread(null, 3, 1.5f, into));
        Assert.AreEqual(0, QAPartyPlacement88.SelectSpread(accepted, 0, 1.5f, into));
    }

    [Test]
    public void 우회_경로_판정()
    {
        Assert.IsTrue(QAPartyPlacement88.IsDirectPath(3f, 3f), "직선 그대로");
        Assert.IsTrue(QAPartyPlacement88.IsDirectPath(5.7f, 3f), "3 × 1.6 + 1 = 5.8 안");
        Assert.IsFalse(QAPartyPlacement88.IsDirectPath(5.9f, 3f), "5.8 밖");
        Assert.IsFalse(QAPartyPlacement88.IsDirectPath(20f, 3f), "섬을 돌아가는 길");
        Assert.IsFalse(QAPartyPlacement88.IsDirectPath(-1f, 3f), "경로 없음");
    }

    [Test]
    public void 봇_한걸음_바닥이_없거나_많이_낮으면_멈춘다()
    {
        Assert.IsTrue(QAPartyPlacement88.IsSafeStep(true, 1f, 1f), "평지");
        Assert.IsTrue(QAPartyPlacement88.IsSafeStep(true, 1f, 1.5f), "턱이 높은 것은 걸을 수 있다(벽이면 물리가 막는다)");
        Assert.IsTrue(QAPartyPlacement88.IsSafeStep(true, 1f, 1f - QAPartyPlacement88.EdgeMaxDrop + 0.05f), "허용 낙차 안");
        Assert.IsFalse(QAPartyPlacement88.IsSafeStep(true, 1f, 1f - QAPartyPlacement88.EdgeMaxDrop - 0.1f), "낭떠러지");
        Assert.IsFalse(QAPartyPlacement88.IsSafeStep(false, 1f, 0f), "바닥 없음(틈)");
    }

    [Test]
    public void 런_전_위치_검사는_수평_반경과_높이_차를_본다()
    {
        var boss = new Vector3(0f, 10f, 0f);
        float r = QAPartyPlacement88.NearRadius(QAInterruptTargetKind.Midboss);
        Assert.AreEqual(QAPartyPlacement88.MidbossNearMeters, r);
        Assert.AreEqual(QAPartyPlacement88.TwentyThreeNearMeters, QAPartyPlacement88.NearRadius(QAInterruptTargetKind.TwentyThree));
        Assert.IsTrue(QAPartyPlacement88.IsNearTarget(new Vector3(r - 0.1f, 10f, 0f), boss, r), "반경 안");
        Assert.IsFalse(QAPartyPlacement88.IsNearTarget(new Vector3(r + 0.1f, 10f, 0f), boss, r), "반경 밖");
        Assert.IsTrue(QAPartyPlacement88.IsNearTarget(new Vector3(1f, 10f - QAPartyPlacement88.NearMaxDy + 0.1f, 0f), boss, r), "허용 높이 차 안");
        Assert.IsFalse(QAPartyPlacement88.IsNearTarget(new Vector3(1f, 10f - QAPartyPlacement88.NearMaxDy - 0.1f, 0f), boss, r), "섬 아래로 떨어짐");
        Assert.IsFalse(QAPartyPlacement88.IsNearTarget(new Vector3(0f, 10f, 80f), boss, QAPartyPlacement88.TwentyThreeNearMeters), "보스방으로 끌려간 중간보스 파티 등");
    }

    [Test]
    public void 보스_제한시간_정지_리플렉션_대상이_있다()
    {
        Assert.IsTrue(QABossTimerHold88.ReflectionOk, "BossTimerManager._expiresAt(NetworkVariable<double>)·TimerNow(NetworkManager) 이름이 바뀌면 스위트 동안 제한시간을 못 멈춘다");
    }

    [Test]
    public void 일반몹_재우기는_G8_지연_기상_뒤()
    {
        Assert.Greater(QABossStart88.NormalWakeWaitSeconds, TutorialZone4Model.DefaultWakeDelaySeconds,
            "G8 직후 재우면 3초 뒤 지연 기상이 덮어쓴다(스모크 4차)");
    }
}
#endif
