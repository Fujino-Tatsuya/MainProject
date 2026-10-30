#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 할 일 164 — 봇 간파 누름 판단 = 서버와 같은 형상 겹침(<see cref="QABoss103CounterShape"/>). 허수아비 = 23호 HurtBox(세운 캡슐 r 1.7·높이 5.78·바닥부터),
/// 판정 상자 = 팔라딘(1.6×1×1.6, 중심 y 0·앞 0.9 — 발목 높이)·거너(1.4×1.8×1.4, 중심 y 1·앞 0.9). 수치는 테스트 허수아비일 뿐, 봇은 프리팹 앵커를 그대로 읽는다.
/// 팔라딘 상자 윗면(y 0.5)에서 캡슐 단면 반경 ≈ 1.204 → 가장 넓은 단면 기준 수평 거리(옛 봇 모델) 1.204 넘으면 실제로는 안 닿는다.
/// </summary>
public sealed class QABoss103CounterShapeTests
{
    private static readonly Vector3 Pivot = new Vector3(0f, 0f, 0f);
    private static readonly Vector3 PaladinCenter = new Vector3(0f, 0f, 0.9f);
    private static readonly Vector3 PaladinHalf = new Vector3(0.8f, 0.5f, 0.8f);
    private static readonly Vector3 GunnerCenter = new Vector3(0f, 1f, 0.9f);
    private static readonly Vector3 GunnerHalf = new Vector3(0.7f, 0.9f, 0.7f);
    private const float BossRadius = 1.7f;

    private readonly List<GameObject> _made = new List<GameObject>();
    private readonly Collider[] _buf = new Collider[16];

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject go in _made)
            if (go != null)
                Object.DestroyImmediate(go);
        _made.Clear();
    }

    /// <summary>23호 HurtBox 허수아비 — 가장 넓은 단면 기준 수평 표면 거리 <paramref name="surface"/> 만큼 <paramref name="dir"/> 쪽에.</summary>
    private CapsuleCollider PlaceBoss(Vector3 dir, float surface)
    {
        var go = new GameObject("QA164_Boss");
        _made.Add(go);
        go.transform.position = Pivot + dir.normalized * (BossRadius + surface);
        var c = go.AddComponent<CapsuleCollider>();
        c.isTrigger = true;
        c.radius = BossRadius;
        c.height = 5.78f;
        c.center = new Vector3(0f, 2.89f, 0f);
        Physics.SyncTransforms();
        return c;
    }

    private static HitboxAnchorShape Box(Vector3 center, Vector3 half, Vector3 facing)
    {
        Quaternion q = Quaternion.LookRotation(facing, Vector3.up);
        return HitboxAnchorShape.FromBox(Pivot + q * center, half, q);
    }

    private static float HorizontalSurface(Vector3 p, Collider c)
    {
        Vector3 q = new Vector3(p.x, c.bounds.center.y, p.z);
        Vector3 cp = c.ClosestPoint(q);
        cp.y = q.y;
        return Vector3.Distance(q, cp);
    }

    [Test]
    public void 평면_거리는_안인데_높이상_안_닿으면_안_누른다()
    {
        CapsuleCollider boss = PlaceBoss(Vector3.forward, 1.4f);
        Assert.That(HorizontalSurface(Pivot, boss), Is.EqualTo(1.4f).Within(0.01f));
        Assert.Less(1.4f, 1.88f - 0.3f, "옛 2D 모델(꼭짓점 사거리 1.88 − 여유) 기준으로는 누름 자리");

        QABoss103CounterShape.Probe p = QABoss103CounterShape.ProbeShape(
            Box(PaladinCenter, PaladinHalf, Vector3.forward), Vector3.forward, Vector3.forward, boss, _buf);
        Assert.IsTrue(p.Valid);
        Assert.IsFalse(p.Overlap, "발목 높이 상자는 캡슐 아래 둥근 면(단면 ≈1.2)에 못 닿는다");
        Assert.IsFalse(p.InSpot);
        Assert.AreEqual(QABoss103PushNorthCounter.PressDecision.OutOfShape,
            QABoss103PushNorthCounter.Decide(10.0, 10.0, p.InSpot, p.Touches, p.Touches, QABoss103PushNorthCounter.FallbackLeadSeconds),
            "마감에도 누르지 않고 out_of_shape");
    }

    [Test]
    public void 같은_평면_거리라도_높이가_맞는_상자는_닿는다()
    {
        CapsuleCollider boss = PlaceBoss(Vector3.forward, 1.3f);
        QABoss103CounterShape.Probe paladin = QABoss103CounterShape.ProbeShape(
            Box(PaladinCenter, PaladinHalf, Vector3.forward), Vector3.forward, Vector3.forward, boss, _buf);
        Assert.IsFalse(paladin.Overlap, "팔라딘은 1.3 에서도 못 닿는다");
        QABoss103CounterShape.Probe p = QABoss103CounterShape.ProbeShape(
            Box(GunnerCenter, GunnerHalf, Vector3.forward), Vector3.forward, Vector3.forward, boss, _buf);
        Assert.IsTrue(p.Overlap, "거너 상자는 가장 넓은 단면 높이에 걸려 정면 1.6 안");
        Assert.IsTrue(p.Backoff, "0.2m 물려도 1.5 < 1.6");
        Assert.AreEqual(QABoss103PushNorthCounter.PressDecision.Press,
            QABoss103PushNorthCounter.Decide(5.0, 10.0, p.InSpot, p.Touches, p.Touches, 0.0));
    }

    [Test]
    public void 닿으면_누르고_끝자락은_여유_자리가_아니다()
    {
        CapsuleCollider near = PlaceBoss(Vector3.forward, 0.9f);
        QABoss103CounterShape.Probe p = QABoss103CounterShape.ProbeShape(
            Box(PaladinCenter, PaladinHalf, Vector3.forward), Vector3.forward, Vector3.forward, near, _buf);
        Assert.IsTrue(p.Overlap);
        Assert.IsTrue(p.Backoff, "0.9 + 0.2 = 1.1 < 1.2");
        Assert.AreEqual(QABoss103PushNorthCounter.PressDecision.Press,
            QABoss103PushNorthCounter.Decide(5.0, 10.0, p.InSpot, p.Touches, p.Touches, 0.0), "누름 자리 → 누른다");
        TearDown();

        CapsuleCollider edge = PlaceBoss(Vector3.forward, 1.1f);
        p = QABoss103CounterShape.ProbeShape(Box(PaladinCenter, PaladinHalf, Vector3.forward), Vector3.forward, Vector3.forward, edge, _buf);
        Assert.IsTrue(p.Overlap, "지금은 닿는다");
        Assert.IsFalse(p.Backoff, "0.2m 물리면 1.3 > 1.2 — 끝자락");
        Assert.AreEqual(QABoss103PushNorthCounter.PressDecision.Wait,
            QABoss103PushNorthCounter.Decide(5.0, 10.0, p.InSpot, p.Touches, p.Touches, 0.0), "여유 자리까지 더 붙는다");
        Assert.AreEqual(QABoss103PushNorthCounter.PressDecision.Press,
            QABoss103PushNorthCounter.Decide(10.0, 10.0, p.InSpot, p.Touches, p.Touches, 0.0), "마감엔 닿으니 누른다");
    }

    [Test]
    public void 돌려_놓은_형상으로_본다_몸_방향_오차는_계측만()
    {
        // 앵커는 오른쪽을 보고 있고 보스는 앞 — TryGetPlacedJudgeShape 처럼 보스 쪽으로 돌린(Aimed) 형상이면 닿는다.
        CapsuleCollider boss = PlaceBoss(Vector3.forward, 0.9f);
        HitboxAnchorShape side = Box(PaladinCenter, PaladinHalf, Vector3.right);
        QABoss103CounterShape.Probe raw = QABoss103CounterShape.ProbeShape(side, Vector3.forward, Vector3.right, boss, _buf);
        Assert.IsFalse(raw.Overlap, "옆을 보는 지금 상자는 안 닿는다(서버 계측 = 이 값)");
        Assert.AreEqual(90f, raw.YawErrDeg, 0.01f);

        HitboxAnchorShape aimed = side.PlacedAt(Pivot, Vector3.right, Vector3.forward, Pivot);
        QABoss103CounterShape.Probe p = QABoss103CounterShape.ProbeShape(aimed, Vector3.forward, Vector3.right, boss, _buf);
        Assert.IsTrue(p.Overlap && p.Backoff);
    }

    [Test]
    public void 상자_높이_범위와_틈()
    {
        QABoss103CounterShape.VerticalRange(Box(PaladinCenter, PaladinHalf, new Vector3(1f, 0f, 1f)), out float minY, out float maxY);
        Assert.AreEqual(-0.5f, minY, 1e-4f);
        Assert.AreEqual(0.5f, maxY, 1e-4f, "수평으로 돌려도 높이 그대로");
        QABoss103CounterShape.VerticalRange(Box(GunnerCenter, GunnerHalf, Vector3.forward), out minY, out maxY);
        Assert.AreEqual(0.1f, minY, 1e-4f);
        Assert.AreEqual(1.9f, maxY, 1e-4f);

        CapsuleCollider boss = PlaceBoss(Vector3.forward, 1.5f);
        float r = Mathf.Sqrt(BossRadius * BossRadius - (BossRadius - 0.5f) * (BossRadius - 0.5f));   // 상자 윗면 높이 단면 ≈ 1.204
        float gap = QABoss103CounterShape.GapM(Box(PaladinCenter, PaladinHalf, Vector3.forward), Vector3.forward, boss, _buf, 5f);
        Assert.That(gap, Is.EqualTo(1.5f - r).Within(0.03f), "틈 = 평면 거리 − 상자 높이 단면 반경");
        Assert.AreEqual(0f, QABoss103CounterShape.GapM(Box(GunnerCenter, GunnerHalf, Vector3.forward), Vector3.forward, boss, _buf, 5f), 1e-4f, "지금 닿으면 0");
        Assert.IsNaN(QABoss103CounterShape.GapM(Box(PaladinCenter, PaladinHalf, Vector3.forward), Vector3.forward, boss, _buf, 0.1f), "한도 안에 없으면 NaN");
    }
}
#endif
