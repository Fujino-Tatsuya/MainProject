using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using BeaverLobby.Player.Dash;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 할 일 156 — 간파 판정 범위 기즈모. 형상·포즈 = 시도 예측 오버랩과 같은 함수(<see cref="HitboxAnchorShape.PlacedAt"/>,
/// <see cref="PlayerInterruptSkillBase.TryGetPlacedJudgeShape"/>), 선분 = <see cref="AttackShapeDrawing.CollectAnchorSegments"/>,
/// 색·잔상 = <see cref="InterruptJudgeShapeGizmoRules"/>.
/// </summary>
public sealed class InterruptJudgeShapeGizmoTests
{
    const float Eps = 1e-4f;
    static readonly Vector3 Pivot = new Vector3(10f, 0.5f, -4f);
    // 어쌔신·거너 간파 상자 저작값(HitboxAnchorShapeTests 와 같다).
    static readonly Vector3 GunnerCenter = new Vector3(0f, 1.3f, 0.9f);
    static readonly Vector3 GunnerHalf = new Vector3(0.7f, 1.2f, 0.7f);

    static HitboxAnchorShape BoxFacing(Vector3 forward)
    {
        Quaternion facing = Quaternion.LookRotation(forward, Vector3.up);
        return HitboxAnchorShape.FromBox(Pivot + facing * GunnerCenter, GunnerHalf, facing);
    }

    // ─── 포즈(판정 위치·공격 방향) ─────────────────────────────────────

    [Test]
    public void 근접_판정_위치_지금_자리면_공격_방향으로만_돈다()
    {
        // 지금 -X 를 보다가(회전 스냅 전) 조준 +Z 로 시전 — 상자 중심 = 시전 위치 정면 0.9.
        HitboxAnchorShape placed = BoxFacing(Vector3.left).PlacedAt(Pivot, Vector3.left, Vector3.forward, Pivot);
        Assert.That(Vector3.Distance(Pivot + GunnerCenter, placed.Box.center), Is.LessThan(Eps));
        Assert.That(Vector3.Angle(Vector3.forward, placed.Box.orientation * Vector3.forward), Is.LessThan(0.01f));
        Assert.AreEqual(GunnerHalf, placed.Box.halfExtents);
    }

    [Test]
    public void 대쉬_판정_위치_예상_대시_끝으로_옮긴다()
    {
        Vector3 dash = new Vector3(1f, 0f, 1f).normalized;
        Vector3 end = Pivot + dash * 3f;
        HitboxAnchorShape placed = BoxFacing(Vector3.forward).PlacedAt(Pivot, Vector3.forward, dash, end);
        Vector3 expected = end + Quaternion.LookRotation(dash) * GunnerCenter;
        Assert.That(Vector3.Distance(expected, placed.Box.center), Is.LessThan(Eps));
        Assert.That(Vector3.Angle(dash, placed.Box.orientation * Vector3.forward), Is.LessThan(0.01f));
    }

    [Test]
    public void PlacedAt_은_예측의_Aimed_Offset_과_같다_상자_구_캡슐()
    {
        Vector3 forward = Vector3.left;
        Vector3 attack = new Vector3(0.3f, 0f, -0.8f);
        Vector3 resolve = Pivot + new Vector3(1.2f, 0.01f, 2.5f);
        HitboxAnchorShape[] shapes =
        {
            BoxFacing(forward),
            HitboxAnchorShape.FromSphere(Pivot + new Vector3(-1f, 1f, 0f), 0.6f),
            HitboxAnchorShape.FromCapsule(Pivot + new Vector3(-0.5f, 0.5f, 0f), Pivot + new Vector3(-1.5f, 1.5f, 0f), 0.4f),
        };

        foreach (HitboxAnchorShape s in shapes)
        {
            HitboxAnchorShape a = s.PlacedAt(Pivot, forward, attack, resolve);
            HitboxAnchorShape b = s.Aimed(Pivot, forward, attack).Offset(resolve - Pivot);
            Assert.That(Vector3.Distance(a.Box.center, b.Box.center), Is.LessThan(1e-6f));
            if (s.Kind == OverlapCollider.Box) // 구·캡슐의 Box.orientation 은 0 쿼터니언(안 쓴다)
                Assert.That(Quaternion.Angle(a.Box.orientation, b.Box.orientation), Is.LessThan(1e-3f));
            Assert.That(Vector3.Distance(a.Sphere.center, b.Sphere.center), Is.LessThan(1e-6f));
            Assert.That(Vector3.Distance(a.Capsule.point0, b.Capsule.point0), Is.LessThan(1e-6f));
            Assert.That(Vector3.Distance(a.Capsule.point1, b.Capsule.point1), Is.LessThan(1e-6f));
        }
    }

    [Test]
    public void 예측_오버랩과_기즈모가_같은_형상_함수를_쓴다()
    {
        // 판정과 따로 노는 근사 도형 금지 — 예측 대상 탐색과 기즈모 둘 다 TryGetPlacedJudgeShape 하나를 지나고, 기즈모는 형상을 직접 돌리거나 옮기지 않는다.
        string skill = File.ReadAllText("Assets/1.Scripts/Player/Skill/PlayerInterruptSkillBase.cs");
        Match find = Regex.Match(skill, @"private Unit FindPredictTarget\([\s\S]*?\.Overlap\(");
        Assert.IsTrue(find.Success);
        StringAssert.Contains("TryGetPlacedJudgeShape(resolvePosition, attackDirection, out HitboxAnchorShape shape)", find.Value);
        StringAssert.Contains(".PlacedAt(", skill);

        string gizmo = File.ReadAllText("Assets/1.Scripts/Dev/InterruptLab/InterruptJudgeShapeGizmos.cs");
        StringAssert.Contains("TryGetPlacedJudgeShape(", gizmo);
        StringAssert.Contains("FindOwnerViewTarget(", gizmo);
        Assert.IsFalse(Regex.IsMatch(gizmo, @"\.(Aimed|Offset|PlacedAt)\("), "기즈모가 형상을 따로 계산한다");
    }

    // ─── 선분 ──────────────────────────────────────────────────────────

    [Test]
    public void 상자_선분은_모서리_12개이고_끝점은_꼭짓점이다()
    {
        HitboxAnchorShape box = BoxFacing(new Vector3(1f, 0f, 2f));
        var segments = new List<Vector3>();
        AttackShapeDrawing.CollectAnchorSegments(box, segments);
        Assert.AreEqual(24, segments.Count);

        for (int i = 0; i < segments.Count; i += 2)
        {
            Vector3 a = Quaternion.Inverse(box.Box.orientation) * (segments[i] - box.Box.center);
            Vector3 b = Quaternion.Inverse(box.Box.orientation) * (segments[i + 1] - box.Box.center);
            foreach (Vector3 local in new[] { a, b })
            {
                Assert.AreEqual(GunnerHalf.x, Mathf.Abs(local.x), Eps);
                Assert.AreEqual(GunnerHalf.y, Mathf.Abs(local.y), Eps);
                Assert.AreEqual(GunnerHalf.z, Mathf.Abs(local.z), Eps);
            }
            // 모서리 = 한 축만 다르다
            int differ = (Mathf.Abs(a.x - b.x) > Eps ? 1 : 0) + (Mathf.Abs(a.y - b.y) > Eps ? 1 : 0) + (Mathf.Abs(a.z - b.z) > Eps ? 1 : 0);
            Assert.AreEqual(1, differ);
        }
    }

    [Test]
    public void 구_선분은_원_3개이고_모두_반지름_위다()
    {
        HitboxAnchorShape sphere = HitboxAnchorShape.FromSphere(Pivot, 0.75f);
        var segments = new List<Vector3>();
        AttackShapeDrawing.CollectAnchorSegments(sphere, segments);
        Assert.AreEqual(3 * AttackShapeDrawing.AnchorCircleSegments * 2, segments.Count);
        foreach (Vector3 p in segments)
            Assert.AreEqual(0.75f, Vector3.Distance(Pivot, p), Eps);
    }

    [Test]
    public void 캡슐_선분은_양끝_구와_옆선_4개다()
    {
        Vector3 p0 = Pivot, p1 = Pivot + new Vector3(0f, 0f, 2f);
        HitboxAnchorShape capsule = HitboxAnchorShape.FromCapsule(p0, p1, 0.5f);
        var segments = new List<Vector3>();
        AttackShapeDrawing.CollectAnchorSegments(capsule, segments);
        Assert.AreEqual((6 * AttackShapeDrawing.AnchorCircleSegments + 4) * 2, segments.Count);

        // 옆선 = 마지막 4개, 축과 평행하고 축에서 반지름만큼 떨어져 있다.
        for (int i = segments.Count - 8; i < segments.Count; i += 2)
        {
            Assert.That(Vector3.Angle(p1 - p0, segments[i + 1] - segments[i]), Is.LessThan(0.01f));
            Assert.AreEqual(0.5f, Vector3.Distance(p0, segments[i]), Eps);
        }
    }

    [Test]
    public void 형상이_없으면_선분도_없다()
    {
        var segments = new List<Vector3>();
        AttackShapeDrawing.CollectAnchorSegments(new HitboxAnchorShape { Kind = (OverlapCollider)99 }, segments);
        Assert.AreEqual(0, segments.Count);
    }

    // ─── 색 ────────────────────────────────────────────────────────────

    [Test]
    public void 시도_색은_예측_결과마다_다르다()
    {
        Color none = InterruptJudgeShapeGizmoRules.ForForecast(InterruptAttemptForecast.NoTarget);
        Color weak = InterruptJudgeShapeGizmoRules.ForForecast(InterruptAttemptForecast.Weak);
        Color strong = InterruptJudgeShapeGizmoRules.ForForecast(InterruptAttemptForecast.Strong);
        Assert.AreEqual(InterruptJudgeShapeGizmoRules.AttemptNoTargetColor, none);
        Assert.AreEqual(InterruptJudgeShapeGizmoRules.AttemptWeakColor, weak);
        Assert.AreEqual(InterruptJudgeShapeGizmoRules.AttemptStrongColor, strong);
        Assert.AreNotEqual(none, weak);
        Assert.AreNotEqual(weak, strong);
        Assert.AreNotEqual(none, strong);
    }

    [TestCase(false, false, InterruptJudgePreviewState.NoTarget)]
    [TestCase(false, true, InterruptJudgePreviewState.NoTarget)]
    [TestCase(true, false, InterruptJudgePreviewState.TargetClosed)]
    [TestCase(true, true, InterruptJudgePreviewState.TargetOpen)]
    public void 미리보기_상태는_대상과_창_열림으로_정한다(bool hasTarget, bool open, InterruptJudgePreviewState expected)
    {
        Assert.AreEqual(expected, InterruptJudgeShapeGizmoRules.PreviewStateOf(hasTarget, open));
    }

    [Test]
    public void 미리보기_색은_시도_색과_겹치지_않는다()
    {
        Color[] preview =
        {
            InterruptJudgeShapeGizmoRules.ForPreview(InterruptJudgePreviewState.NoTarget),
            InterruptJudgeShapeGizmoRules.ForPreview(InterruptJudgePreviewState.TargetClosed),
            InterruptJudgeShapeGizmoRules.ForPreview(InterruptJudgePreviewState.TargetOpen),
        };
        Color[] attempt =
        {
            InterruptJudgeShapeGizmoRules.AttemptNoTargetColor,
            InterruptJudgeShapeGizmoRules.AttemptWeakColor,
            InterruptJudgeShapeGizmoRules.AttemptStrongColor,
        };
        for (int i = 0; i < preview.Length; i++)
        {
            for (int j = i + 1; j < preview.Length; j++)
                Assert.AreNotEqual(preview[i], preview[j]);
            foreach (Color a in attempt)
                Assert.AreNotEqual(a, preview[i]);
        }
    }

    // ─── 잔상 ──────────────────────────────────────────────────────────

    [Test]
    public void 잔상_불투명도는_1에서_최소까지_줄고_수명_뒤_0()
    {
        Assert.AreEqual(1f, InterruptJudgeShapeGizmoRules.AfterimageAlpha(0f, 3f), Eps);
        Assert.AreEqual(Mathf.Lerp(1f, InterruptJudgeShapeGizmoRules.AfterimageMinAlpha, 0.5f),
            InterruptJudgeShapeGizmoRules.AfterimageAlpha(1.5f, 3f), Eps);
        Assert.AreEqual(0f, InterruptJudgeShapeGizmoRules.AfterimageAlpha(3f, 3f), Eps);
        Assert.IsFalse(InterruptJudgeShapeGizmoRules.IsExpired(2.99f, 3f));
        Assert.IsTrue(InterruptJudgeShapeGizmoRules.IsExpired(3f, 3f));
    }

    [Test]
    public void 잔상은_실시간으로_늙고_시간이_멈추면_멈춘다()
    {
        Assert.AreEqual(1.1f, InterruptJudgeShapeGizmoRules.AdvanceAge(1f, 0.05f, 0.1f), Eps, "슬로우 중에도 실시간");
        Assert.AreEqual(1.1f, InterruptJudgeShapeGizmoRules.AdvanceAge(1f, 1f, 0.1f), Eps);
        Assert.AreEqual(1f, InterruptJudgeShapeGizmoRules.AdvanceAge(1f, 0f, 0.1f), Eps, "보정 질문 창 정지(timeScale 0)");
    }

    [Test]
    public void 알파_배율은_원래_알파에_곱한다()
    {
        Color c = InterruptJudgeShapeGizmoRules.WithAlphaScale(new Color(0.2f, 0.4f, 0.6f, 0.8f), 0.5f);
        Assert.AreEqual(new Color(0.2f, 0.4f, 0.6f, 0.4f), c);
    }
}
