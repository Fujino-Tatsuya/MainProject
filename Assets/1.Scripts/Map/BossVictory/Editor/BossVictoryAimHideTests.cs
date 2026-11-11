using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>보스 처치 연출 동안 조준 표시 숨김(267 후속) — 이유 합치기·연출 상태.</summary>
public class BossVictoryAimHideTests
{
    [TestCase(false, false, false)]
    [TestCase(true, false, true)]
    [TestCase(false, true, true)]
    [TestCase(true, true, true)]
    public void 숨김_이유는_간파와_처치_연출의_OR(bool interrupt, bool victory, bool expected)
    {
        Assert.AreEqual(expected, PlayerAimIndicator.ShouldHide(interrupt, victory));
    }

    [Test]
    public void 처치_연출_중에만_숨기고_끝나거나_없어지면_보인다()
    {
        Assert.IsFalse(BossVictoryGlitchPresentation.HidesCombatAim, "연출 없음");
        var go = new GameObject("BossVictoryAimHideTest");
        try
        {
            var p = go.AddComponent<BossVictoryGlitchPresentation>();
            SetInstance(p);   // EditMode 에선 OnEnable 이 안 불린다
            Assert.IsFalse(BossVictoryGlitchPresentation.HidesCombatAim, "있지만 시작 전");
            FieldInfo running = typeof(BossVictoryGlitchPresentation).GetField("_running", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(running, "_running");
            running.SetValue(p, true);
            Assert.IsTrue(BossVictoryGlitchPresentation.HidesCombatAim, "연출 중");
            running.SetValue(p, false);
            Assert.IsFalse(BossVictoryGlitchPresentation.HidesCombatAim, "끝·취소(Teardown)");
            running.SetValue(p, true);
        }
        finally { Object.DestroyImmediate(go); }
        Assert.IsFalse(BossVictoryGlitchPresentation.HidesCombatAim, "파괴(씬 전환) — 다시 보임");
        SetInstance(null);
    }

    static void SetInstance(BossVictoryGlitchPresentation p) =>
        typeof(BossVictoryGlitchPresentation).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public).SetValue(null, p);

    [Test]
    public void 쿼드_렌더러는_두_이유가_다_끝나야_켜진다()
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        try
        {
            var r = quad.GetComponent<MeshRenderer>();
            PlayerAimIndicator.ApplyInterruptHide(r, PlayerAimIndicator.ShouldHide(false, true));
            Assert.IsFalse(r.enabled, "처치 연출");
            PlayerAimIndicator.ApplyInterruptHide(r, PlayerAimIndicator.ShouldHide(true, true));
            Assert.IsFalse(r.enabled, "겹침");
            PlayerAimIndicator.ApplyInterruptHide(r, PlayerAimIndicator.ShouldHide(true, false));
            Assert.IsFalse(r.enabled, "처치 연출 끝 — 간파가 남아 계속 숨김");
            PlayerAimIndicator.ApplyInterruptHide(r, PlayerAimIndicator.ShouldHide(false, false));
            Assert.IsTrue(r.enabled, "둘 다 끝");
        }
        finally { Object.DestroyImmediate(quad); }
    }
}
