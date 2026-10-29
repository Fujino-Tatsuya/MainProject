#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>할 일 140 — 끼임 계측(<see cref="QABoss103RestraintTrace"/>·<see cref="QABoss103StuckProbe"/>) 순수 부분.</summary>
public class QABoss103StuckProbeTests
{
    [Test]
    public void 전환이_없으면_none()
    {
        var t = new QABoss103RestraintTrace();
        Assert.AreEqual("none", t.Recent(10.0));
        t.Observe(5.0, QABoss103RestraintTrace.Free);   // 처음 = free, 같은 값은 전환 아님
        Assert.AreEqual("none", t.Recent(5.5));
    }

    [Test]
    public void 직전_2초_전환만_오래된_것부터()
    {
        var t = new QABoss103RestraintTrace();
        t.Observe(10.0, "push");
        t.Observe(11.0, "carry");
        t.Observe(11.0, "carry");   // 같은 값 반복은 한 건
        t.Observe(12.5, QABoss103RestraintTrace.Free);
        Assert.AreEqual("carry@-1.80+free@-0.30", t.Recent(12.8));
        Assert.AreEqual("free", t.Last);
        Assert.AreEqual("push@0.00", Fresh("push", 3.0).Recent(3.0), "경계 — 지금 순간 포함");
        Assert.AreEqual("none", Fresh("push", 3.0).Recent(5.01), "2초 넘으면 빠진다");
    }

    [Test]
    public void 용량을_넘으면_오래된_것부터_버린다()
    {
        var t = new QABoss103RestraintTrace();
        for (int i = 0; i < 40; i++)
            t.Observe(i * 0.01, i % 2 == 0 ? "carry" : QABoss103RestraintTrace.Free);
        string recent = t.Recent(0.40);
        Assert.AreEqual(16, recent.Split('+').Length);
        t.Clear();
        Assert.AreEqual("none", t.Recent(0.40));
        Assert.AreEqual(QABoss103RestraintTrace.Free, t.Last);
    }

    [Test]
    public void 구속_모드_리플렉션_필드가_있다()
    {
        // PlayerStateController.currentState · PlayerRestrainedState.mode 이름이 바뀌면 state 가 Restrained_na 로 떨어진다.
        Assert.IsTrue(QABoss103StuckProbe.Readable);
    }

    [Test]
    public void 플레이어가_없으면_na()
    {
        Assert.AreEqual("na", QABoss103StuckProbe.StateToken(null));
        Assert.AreEqual("na", QABoss103StuckProbe.Overlaps(null));
        Assert.AreEqual("na", QABoss103StuckProbe.Ground(null));
        Assert.AreEqual(QABoss103RestraintTrace.Free, QABoss103StuckProbe.RestraintLabel(null));
        StringAssert.Contains(" state=na what=na ground=na grab=none", QABoss103StuckProbe.Fields(null, "none"));
    }

    [Test]
    public void 봇_판_파일_wall_escape_줄은_왕복되고_합치기는_건너뛴다()
    {
        string line = QABoss103PartFile.Format("wall_escape", "slot", "1", "why", "wall_Boundary_N", "state", "Restrained_carry",
            "what", "Boundary_N(Wall)+Hand_R(Boss)", "ground", "Floor(Env)@3.60", "grab", "carry@-1.20", "at", "01:02:03.456");
        Assert.IsTrue(QABoss103PartFile.TryParse(line, out string kind, out Dictionary<string, string> kv));
        Assert.AreEqual("wall_escape", kind);
        Assert.AreEqual("Restrained_carry", kv["state"]);
        Assert.AreEqual("Boundary_N(Wall)+Hand_R(Boss)", kv["what"]);
        Assert.AreEqual("carry@-1.20", kv["grab"]);
        Assert.AreEqual("01:02:03.456", kv["at"]);

        var stats = new QABoss103RunStats();
        stats.Begin(0.0);
        Assert.DoesNotThrow(() => stats.AddPart(1, new[] { line }));
    }

    [Test]
    public void 재배치_줄은_시각표에서_계측_필드까지_안_잘린다()
    {
        string line = "[QA103] event=qa_relocate reason=wall character=Paladin client=1 ok=true state=Restrained_carry what=" + new string('x', 300) +
                      " ground=Floor(Env)@3.60 grab=carry@-1.20 from=(0,0,0) to=(1,0,1) shot=TAIL.png";
        QANightlyEventRule r = QANightlyEvents.Match(line, LogType.Log, null);
        Assert.AreEqual(QANightlyEvents.Stuck, r.Name);
        StringAssert.EndsWith("shot=TAIL.png", QANightlyEvents.Detail(line, r.DetailMax));
    }

    private static QABoss103RestraintTrace Fresh(string label, double at)
    {
        var t = new QABoss103RestraintTrace();
        t.Observe(at, label);
        return t;
    }
}
#endif
