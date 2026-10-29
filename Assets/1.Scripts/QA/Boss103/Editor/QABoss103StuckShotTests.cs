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

    // ── 할 일 140: 임시 카메라 3시점 + 설명 파일 ─────────────────

    [Test]
    public void 설명_파일은_같은_이름_txt()
    {
        string png = System.IO.Path.Combine("C:/QA/b", "stuck", "run1_r0_010203_wall_Paladin.png");
        Assert.AreEqual(System.IO.Path.Combine("C:/QA/b", "stuck", "run1_r0_010203_wall_Paladin.txt"), QABoss103StuckShot.InfoPathFor(png));
    }

    [Test]
    public void 설명_파일_필드()
    {
        string text = QABoss103StuckShot.InfoText(7, 1, 2, "Gunner", 1.234, 3.75, -5, "Boundary_N(Wall)", "Restrained_carry", "wall", "Floor(Env)@3.60", "carry@-1.20");
        StringAssert.Contains("run=7\n", text);
        StringAssert.Contains("retry=1\n", text);
        StringAssert.Contains("client=2\n", text);
        StringAssert.Contains("character=Gunner\n", text);
        StringAssert.Contains("from=1.23,3.75,-5.00\n", text);
        StringAssert.Contains("what=Boundary_N(Wall)\n", text);
        StringAssert.Contains("state=Restrained_carry\n", text);
        StringAssert.Contains("reason=wall\n", text);
        StringAssert.Contains("grab=carry@-1.20\n", text);
        StringAssert.Contains("character=na\n", QABoss103StuckShot.InfoText(1, 0, 0, null, 0, 0, 0, "", null, "stuck", null, null));
    }

    [Test]
    public void 시점_1은_위에서_수직_직교()
    {
        var target = new UnityEngine.Vector3(10f, 3.7f, -4f);
        QABoss103StuckCamera.View v = QABoss103StuckCamera.ViewFor(0, target);
        Assert.IsTrue(v.Orthographic);
        Assert.AreEqual(target.y + QABoss103StuckCamera.TopHeight, v.Position.y, 1e-4);
        Assert.AreEqual(target.x, v.Position.x, 1e-4);
        Assert.AreEqual(target.z, v.Position.z, 1e-4);
        UnityEngine.Vector3 fwd = v.Rotation * UnityEngine.Vector3.forward;
        Assert.AreEqual(-1f, fwd.y, 1e-4, "아래를 본다");
    }

    [Test]
    public void 시점_2_3은_옆_45도_서로_반대편_거리6_높이2()
    {
        var target = new UnityEngine.Vector3(1f, 3.5f, 2f);
        QABoss103StuckCamera.View a = QABoss103StuckCamera.ViewFor(1, target);
        QABoss103StuckCamera.View b = QABoss103StuckCamera.ViewFor(2, target);
        Assert.IsFalse(a.Orthographic);
        Assert.IsFalse(b.Orthographic);
        foreach (QABoss103StuckCamera.View v in new[] { a, b })
        {
            UnityEngine.Vector3 d = v.Position - target;
            Assert.AreEqual(QABoss103StuckCamera.SideHeight, d.y, 1e-4);
            Assert.AreEqual(QABoss103StuckCamera.SideDistance, new UnityEngine.Vector2(d.x, d.z).magnitude, 1e-4);
        }
        UnityEngine.Vector3 da = a.Position - target, db = b.Position - target;
        Assert.AreEqual(da.x, da.z, 1e-4, "45° = x·z 같은 쪽");
        Assert.AreEqual(-da.x, db.x, 1e-4, "반대편");
        Assert.AreEqual(-da.z, db.z, 1e-4, "반대편");
        UnityEngine.Vector3 look = target + UnityEngine.Vector3.up * QABoss103StuckCamera.LookHeight - a.Position;
        Assert.Greater(UnityEngine.Vector3.Dot(a.Rotation * UnityEngine.Vector3.forward, look.normalized), 0.9999f, "대상을 본다");
    }
}
#endif
