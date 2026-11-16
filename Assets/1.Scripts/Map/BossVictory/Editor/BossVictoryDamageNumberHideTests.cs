using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>보스 처치 연출 동안 피해·힐 숫자 제거 — 시작이면 새 숫자 막고 떠 있던 숫자 지움, 끝·취소·파괴면 해제.</summary>
public class BossVictoryDamageNumberHideTests
{
    [Test]
    public void 연출_중엔_새_숫자를_안_만들고_떠_있던_숫자를_지운다()
    {
        Assert.IsTrue(FloatingDamageSpawner.AcceptsNewPopups(false), "연출 없음 = 평소");
        Assert.IsFalse(FloatingDamageSpawner.AcceptsNewPopups(true), "연출 중 = 피해·힐 숫자 없음");
        Assert.IsTrue(FloatingDamageSpawner.ShouldClearLive(true, 3), "시작 순간 떠 있던 숫자(막타 포함) 지움");
        Assert.IsFalse(FloatingDamageSpawner.ShouldClearLive(true, 0), "지울 것 없음");
        Assert.IsFalse(FloatingDamageSpawner.ShouldClearLive(false, 3), "연출 밖 = 그대로");
    }

    [Test]
    public void 시작하면_숨기고_끝나거나_취소되거나_없어지면_풀린다()
    {
        Assert.IsFalse(BossVictoryGlitchPresentation.HidesDamageNumbers, "연출 없음");
        var go = new GameObject("BossVictoryDamageNumberHideTest");
        try
        {
            var p = go.AddComponent<BossVictoryGlitchPresentation>();
            SetInstance(p);   // EditMode 에선 OnEnable 이 안 불린다
            Assert.IsFalse(BossVictoryGlitchPresentation.HidesDamageNumbers, "있지만 시작 전");
            FieldInfo running = typeof(BossVictoryGlitchPresentation).GetField("_running", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(running, "_running");
            running.SetValue(p, true);   // Begin
            Assert.IsTrue(BossVictoryGlitchPresentation.HidesDamageNumbers, "연출 중");
            Assert.IsFalse(FloatingDamageSpawner.AcceptsNewPopups(BossVictoryGlitchPresentation.HidesDamageNumbers));
            running.SetValue(p, false);  // Teardown(끝·취소·랩 F2/F7)
            Assert.IsFalse(BossVictoryGlitchPresentation.HidesDamageNumbers, "끝·취소");
            Assert.IsTrue(FloatingDamageSpawner.AcceptsNewPopups(BossVictoryGlitchPresentation.HidesDamageNumbers));
            running.SetValue(p, true);
        }
        finally { Object.DestroyImmediate(go); }
        Assert.IsFalse(BossVictoryGlitchPresentation.HidesDamageNumbers, "파괴(씬 전환) — 누수 없음");
        SetInstance(null);
    }

    static void SetInstance(BossVictoryGlitchPresentation p) =>
        typeof(BossVictoryGlitchPresentation).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public).SetValue(null, p);
}
