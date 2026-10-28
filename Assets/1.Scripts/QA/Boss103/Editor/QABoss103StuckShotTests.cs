#if UNITY_EDITOR
using System;
using NUnit.Framework;

/// <summary>할 일 138-2 — 재배치 직전 스크린샷 파일 이름·금지 문자·쿨다운 경계(<see cref="QABoss103StuckShot"/>).</summary>
public class QABoss103StuckShotTests
{
    [Test]
    public void FileName_판_시각_원인_플레이어()
    {
        Assert.AreEqual("run12_r1_230507_wall_Paladin.png",
            QABoss103StuckShot.FileName(12, 1, new DateTime(2026, 10, 28, 23, 5, 7, 999), "wall", "Paladin"));
        Assert.AreEqual("run3_r0_000000_stuck_client2.png",
            QABoss103StuckShot.FileName(3, 0, new DateTime(2026, 10, 29, 0, 0, 0), "stuck", "client2"));
    }

    [TestCase("a/b\\c:d*e?f\"g<h>i|j", "abcdefghij")]
    [TestCase("Gun ner\t\n", "Gunner")]
    [TestCase("", "na")]
    [TestCase(null, "na")]
    [TestCase("/:*", "na")]
    public void Clean_금지_문자를_지운다(string raw, string want)
    {
        Assert.AreEqual(want, QABoss103StuckShot.Clean(raw));
    }

    [Test]
    public void FileName_에_금지_문자가_남지_않는다()
    {
        string name = QABoss103StuckShot.FileName(1, 0, new DateTime(2026, 10, 28, 1, 2, 3), "wa:ll", "c<1>");
        Assert.AreEqual("run1_r0_010203_wall_c1.png", name);
        Assert.AreEqual(-1, name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()));
    }

    [Test]
    public void PathFor_출력_폴더가_없으면_찍지_않는다()
    {
        Assert.IsNull(QABoss103StuckShot.PathFor("", "x.png"));
        Assert.IsNull(QABoss103StuckShot.PathFor(null, "x.png"));
        Assert.AreEqual(System.IO.Path.Combine("C:/UnityProject/QA/N108-20261028_2300-base", "stuck", "x.png"),
            QABoss103StuckShot.PathFor("C:/UnityProject/QA/N108-20261028_2300-base", "x.png"));
    }

    [Test]
    public void 쿨다운_경계_10초()
    {
        Assert.IsFalse(QABoss103StuckShot.InCooldown(100, null));
        Assert.IsTrue(QABoss103StuckShot.InCooldown(100, 100));
        Assert.IsTrue(QABoss103StuckShot.InCooldown(109.999, 100));
        Assert.IsFalse(QABoss103StuckShot.InCooldown(110, 100));
        Assert.IsFalse(QABoss103StuckShot.InCooldown(130, 100));
    }
}
#endif
