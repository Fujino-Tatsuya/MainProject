using System.IO;
using System.Linq;
using NUnit.Framework;

/// <summary>
/// 간파 스킬 캐릭터 VFX 개별 토글(할 일 238, <see cref="InterruptVfxToggles"/>) 규칙 — 기본 켬·하나만 끔·묶음·클립 이벤트 id.
/// 정적 상태라 매 테스트 전후로 전부 켬으로 되돌린다.
/// </summary>
public sealed class InterruptVfxTogglesTests
{
    [SetUp]
    public void SetUp() => InterruptVfxToggles.ResetAll();

    [TearDown]
    public void TearDown() => InterruptVfxToggles.ResetAll();

    [Test]
    public void 기본은_전부_켬()
    {
        Assert.IsTrue(InterruptVfxToggles.GateActive, "에디터에서는 게이트가 동작해야 한다");
        foreach (InterruptVfx vfx in InterruptVfxToggles.All)
            Assert.IsTrue(InterruptVfxToggles.IsOn(vfx), vfx.ToString());
    }

    [Test]
    public void 하나를_끄면_그것만_꺼진다()
    {
        InterruptVfxToggles.Set(InterruptVfx.AssassinPop, false);

        foreach (InterruptVfx vfx in InterruptVfxToggles.All)
            Assert.AreEqual(vfx != InterruptVfx.AssassinPop, InterruptVfxToggles.IsOn(vfx), vfx.ToString());

        InterruptVfxToggles.Set(InterruptVfx.AssassinPop, true);
        Assert.IsTrue(InterruptVfxToggles.IsOn(InterruptVfx.AssassinPop));
    }

    [Test]
    public void 전체_끔_전체_켬()
    {
        InterruptVfxToggles.SetAll(false);
        Assert.IsTrue(InterruptVfxToggles.All.All(v => !InterruptVfxToggles.IsOn(v)));

        InterruptVfxToggles.SetAll(true);
        Assert.IsTrue(InterruptVfxToggles.All.All(InterruptVfxToggles.IsOn));
    }

    [Test]
    public void 캐릭터_묶음만_바뀐다()
    {
        InterruptVfxToggles.SetCharacter(InterruptVfxCharacter.Paladin, false);

        foreach (InterruptVfx vfx in InterruptVfxToggles.All)
        {
            bool paladin = InterruptVfxToggles.CharacterOf(vfx) == InterruptVfxCharacter.Paladin;
            Assert.AreEqual(!paladin, InterruptVfxToggles.IsOn(vfx), vfx.ToString());
        }
    }

    [Test]
    public void 묶음_개수_팔라딘4_거너1_어쌔신4()
    {
        int Count(InterruptVfxCharacter c) => InterruptVfxToggles.All.Count(v => InterruptVfxToggles.CharacterOf(v) == c);
        Assert.AreEqual(4, Count(InterruptVfxCharacter.Paladin));
        Assert.AreEqual(1, Count(InterruptVfxCharacter.Gunner));
        Assert.AreEqual(4, Count(InterruptVfxCharacter.Assassin));
    }

    [Test]
    public void 랩_글자는_모두_따로_있다()
    {
        foreach (InterruptVfx vfx in InterruptVfxToggles.All)
            Assert.AreNotEqual(vfx.ToString(), InterruptVfxToggles.Label(vfx), vfx.ToString());
        Assert.AreEqual(InterruptVfxToggles.All.Length, InterruptVfxToggles.All.Select(InterruptVfxToggles.Label).Distinct().Count());
    }

    [TestCase(InterruptProductionRules.PaladinShieldChargeId, InterruptVfx.PaladinShieldCharge)]
    [TestCase(InterruptProductionRules.AssassinInsightWindupId, InterruptVfx.AssassinWindup)]
    [TestCase(InterruptProductionRules.AssassinInsightPopId, InterruptVfx.AssassinPop)]
    [TestCase(InterruptProductionRules.AssassinInsightSlashId, InterruptVfx.AssassinSlash)]
    public void 클립_이벤트_id_는_그_VFX_토글을_따른다(string id, InterruptVfx vfx)
    {
        Assert.IsTrue(InterruptVfxToggles.TryGetAnimEffect(id, out InterruptVfx mapped));
        Assert.AreEqual(vfx, mapped);
        Assert.IsTrue(InterruptVfxToggles.AllowsAnimEffect(id));

        InterruptVfxToggles.Set(vfx, false);
        Assert.IsFalse(InterruptVfxToggles.AllowsAnimEffect(id));
    }

    [TestCase("Slash")]
    [TestCase("Stick")]
    [TestCase("")]
    [TestCase(null)]
    public void 간파가_아닌_클립_이벤트는_전부_꺼도_허용(string id)
    {
        InterruptVfxToggles.SetAll(false);
        Assert.IsFalse(InterruptVfxToggles.TryGetAnimEffect(id, out _));
        Assert.IsTrue(InterruptVfxToggles.AllowsAnimEffect(id));
    }

    // ─── 디렉터 칸(B1·B2·B3·C1·C2)과 함께 — 칸이 꺼져 있으면 토글 켬이어도 안 나왔다(11-06 제보) ───

    [Test]
    public void 거너만_디렉터_칸이_없다()
    {
        foreach (InterruptVfx vfx in InterruptVfxToggles.All)
            Assert.AreEqual(vfx != InterruptVfx.GunnerMuzzleBlast, InterruptVfxToggles.TryGetDirectorCue(vfx, out _), vfx.ToString());
    }

    [Test]
    public void 디렉터_칸이_꺼져_있으면_토글_켬이어도_안_나오고_랩에서_켜면_나온다()
    {
        var go = new UnityEngine.GameObject("Director");
        try
        {
            var director = go.AddComponent<InterruptProductionDirector>(); // 새 컴포넌트 = 칸 전부 꺼짐(코드 기본값 — 프리팹은 11-06부터 켬)
            foreach (InterruptVfx vfx in InterruptVfxToggles.All)
            {
                bool hasCue = InterruptVfxToggles.TryGetDirectorCue(vfx, out InterruptProductionCue cue);
                Assert.IsTrue(InterruptVfxToggles.IsOn(vfx));
                Assert.AreEqual(!hasCue, InterruptVfxToggles.IsEffective(vfx, director), "칸 꺼짐 = 안 나옴: " + vfx);

                InterruptVfxToggles.SetEffective(vfx, true, director);
                Assert.IsTrue(InterruptVfxToggles.IsEffective(vfx, director), "랩 켬 = 칸도 켜서 나옴: " + vfx);
                if (hasCue)
                    Assert.IsTrue(director.Get(cue), vfx.ToString());

                InterruptVfxToggles.SetEffective(vfx, false, director);
                Assert.IsFalse(InterruptVfxToggles.IsEffective(vfx, director), vfx.ToString());
                if (hasCue)
                    Assert.IsTrue(director.Get(cue), "끌 때 디렉터 칸은 그대로(같은 칸의 다른 VFX): " + vfx);
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void 디렉터가_없으면_칸_있는_VFX_는_안_나온다()
    {
        Assert.IsFalse(InterruptVfxToggles.IsEffective(InterruptVfx.PaladinShieldGlowLoop, null));
        Assert.IsTrue(InterruptVfxToggles.IsEffective(InterruptVfx.GunnerMuzzleBlast, null));
    }

    [Test]
    public void 클립_이벤트는_토글과_디렉터_칸_둘_다_켜져야_허용()
    {
        var go = new UnityEngine.GameObject("Director");
        try
        {
            var director = go.AddComponent<InterruptProductionDirector>();
            string id = InterruptProductionRules.PaladinShieldChargeId;
            Assert.IsFalse(InterruptProductionRules.AllowsAnimEffect(director, id) && InterruptVfxToggles.AllowsAnimEffect(id), "칸 꺼짐");
            InterruptVfxToggles.SetEffective(InterruptVfx.PaladinShieldCharge, true, director);
            Assert.IsTrue(InterruptProductionRules.AllowsAnimEffect(director, id) && InterruptVfxToggles.AllowsAnimEffect(id), "랩 켬");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    // 클립 이벤트 id 가 실제 Variant 소켓에 있는지 — 이름이 바뀌면 토글이 조용히 안 먹는다(프리팹 YAML 텍스트만 읽음).
    [TestCase("Assets/2.Prefabs/Player/Paladin/Player_Paladin.prefab", InterruptProductionRules.PaladinShieldChargeId)]
    [TestCase("Assets/2.Prefabs/Player/Assassin/Player_Assassin.prefab", InterruptProductionRules.AssassinInsightWindupId)]
    [TestCase("Assets/2.Prefabs/Player/Assassin/Player_Assassin.prefab", InterruptProductionRules.AssassinInsightPopId)]
    [TestCase("Assets/2.Prefabs/Player/Assassin/Player_Assassin.prefab", InterruptProductionRules.AssassinInsightSlashId)]
    public void 클립_이벤트_id_는_Variant_소켓에_있다(string prefabPath, string id)
    {
        StringAssert.Contains("  id: " + id + "\n", File.ReadAllText(prefabPath).Replace("\r\n", "\n"));
    }
}
