#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;

/// <summary>할 일 103 C1 순수 규칙 — 판 종료 판정·30분 상한·시드 → 특성 선택·파티 자리·발판 배치·설정 파일·판 기록 한 줄.</summary>
public sealed class QABoss103PlanTests
{
    // ── 판 종료 판정 ─────────────────────────────────────────

    [Test]
    public void 진행_중이면_None()
    {
        Assert.AreEqual(QABoss103Outcome.None, QABoss103Plan.Judge(false, false, 10.0));
    }

    [Test]
    public void 보스_처치는_클리어()
    {
        Assert.AreEqual(QABoss103Outcome.Clear, QABoss103Plan.Judge(true, false, 300.0));
    }

    [Test]
    public void 전멸은_실패()
    {
        Assert.AreEqual(QABoss103Outcome.Wipe, QABoss103Plan.Judge(false, true, 300.0));
    }

    [Test]
    public void 같은_틱이면_클리어가_전멸_시간초과보다_먼저()
    {
        Assert.AreEqual(QABoss103Outcome.Clear, QABoss103Plan.Judge(true, true, QABoss103Plan.CombatLimitSeconds + 1));
        Assert.AreEqual(QABoss103Outcome.Wipe, QABoss103Plan.Judge(false, true, QABoss103Plan.CombatLimitSeconds + 1));
    }

    [Test]
    public void 시간초과는_Combat_부터_30분_경계_포함()
    {
        Assert.AreEqual(1800.0, QABoss103Plan.CombatLimitSeconds);
        Assert.AreEqual(QABoss103Outcome.None, QABoss103Plan.Judge(false, false, 1799.9));
        Assert.AreEqual(QABoss103Outcome.Timeout, QABoss103Plan.Judge(false, false, 1800.0));
        Assert.AreEqual(QABoss103Outcome.Timeout, QABoss103Plan.Judge(false, false, 5000.0));
    }

    [Test]
    public void Combat_전_음수_경과는_시간초과가_아니다()
    {
        Assert.IsFalse(QABoss103Plan.IsTimedOut(-1.0));
        Assert.IsFalse(QABoss103Plan.IsTimedOut(-1.0, 0.0));
        Assert.AreEqual(QABoss103Outcome.None, QABoss103Plan.Judge(false, false, -1.0, 0.0));
    }

    [Test]
    public void 상한을_바꿔도_같은_규칙()
    {
        Assert.IsTrue(QABoss103Plan.IsTimedOut(60.0, 60.0));
        Assert.IsFalse(QABoss103Plan.IsTimedOut(59.0, 60.0));
    }

    [Test]
    public void 최종_전멸은_목숨_0_그리고_생존_0()
    {
        Assert.IsTrue(QABoss103Plan.IsFinalWipe(0, 0));
        Assert.IsFalse(QABoss103Plan.IsFinalWipe(1, 0), "목숨이 남으면 부활");
        Assert.IsFalse(QABoss103Plan.IsFinalWipe(0, 1), "한 명이라도 살아 있으면 계속");
    }

    // ── 시드 → 특성 ─────────────────────────────────────────

    [Test]
    public void 같은_시드는_같은_특성()
    {
        var offers = new List<int> { 0, 3, 5 };
        for (int nth = 1; nth <= QABoss103Plan.TraitRounds; nth++)
            foreach (QABoss103Character c in QABoss103Plan.PartyOrder)
                Assert.AreEqual(QABoss103Plan.PickOffer(777, nth, c, offers), QABoss103Plan.PickOffer(777, nth, c, offers));
        Assert.AreEqual(QABoss103Plan.RunSeed(103, 5), QABoss103Plan.RunSeed(103, 5));
    }

    // ── 고정 후보(할 일 125) ────────────────────────────────

    [Test]
    public void 고정_후보는_캐릭터_차수마다_표_3개()
    {
        foreach (QABoss103Character c in QABoss103Plan.PartyOrder)
            for (int tier = 1; tier <= QABoss103Plan.TraitRounds; tier++)
                Assert.AreEqual(TraitCatalog.CandidatesPerTier, QABoss103Plan.FixedOfferIds(c, tier).Count, $"{c} {tier}차");
        CollectionAssert.AreEqual(new[] { "PAL_1_Q_DAMAGE", "PAL_1_E_COOLDOWN", "PAL_1_PASSIVE_HEAL" },
            QABoss103Plan.FixedOfferIds(QABoss103Character.Paladin, 1));
        Assert.AreEqual(0, QABoss103Plan.FixedOfferIds(QABoss103Character.Gunner, 5).Count);
    }

    [Test]
    public void 제시가_고정표와_순서까지_같아야_고정_후보()
    {
        var ok = new List<string> { "GUN_2_OVERDRIVE", "GUN_2_HEAT_SHOT", "GUN_2_COOL_MODE" };
        Assert.IsTrue(QABoss103Plan.IsFixedOffer(QABoss103Character.Gunner, 2, ok));
        Assert.IsFalse(QABoss103Plan.IsFixedOffer(QABoss103Character.Gunner, 3, ok), "다른 차수");
        Assert.IsFalse(QABoss103Plan.IsFixedOffer(QABoss103Character.Paladin, 2, ok), "다른 캐릭터");
        Assert.IsFalse(QABoss103Plan.IsFixedOffer(QABoss103Character.Gunner, 2, new List<string> { "GUN_2_OVERDRIVE", "GUN_2_HEAT_SHOT" }), "빠진 후보");
        Assert.IsFalse(QABoss103Plan.IsFixedOffer(QABoss103Character.Gunner, 2, null));
    }

    [Test]
    public void 획득_ID_는_그_캐릭터_차수의_후보여야_한다()
    {
        Assert.IsTrue(QABoss103Plan.IsFixedPick(QABoss103Character.Assassin, 4, "ASN_4_Q_TRAIL"));
        Assert.IsFalse(QABoss103Plan.IsFixedPick(QABoss103Character.Assassin, 3, "ASN_4_Q_TRAIL"), "차수 다름");
        Assert.IsFalse(QABoss103Plan.IsFixedPick(QABoss103Character.Gunner, 4, "ASN_4_Q_TRAIL"), "캐릭터 다름");
        Assert.IsFalse(QABoss103Plan.IsFixedPick(QABoss103Character.Paladin, 1, "#12345"), "모르는 ID");
    }

    [Test]
    public void 봇은_고정_후보_해시_중_하나를_고른다()
    {
        var offers = new List<int>();
        foreach (string id in QABoss103Plan.FixedOfferIds(QABoss103Character.Paladin, 3))
            offers.Add(TraitIdHash.Of(id));
        for (int seed = 0; seed < 50; seed++)
            CollectionAssert.Contains(offers, QABoss103Plan.PickOffer(seed, 3, QABoss103Character.Paladin, offers));
    }

    [Test]
    public void 고른_특성은_항상_제시_목록_안()
    {
        var offers = new List<int> { 2, 4, 1 };
        for (int seed = 0; seed < 200; seed++)
            CollectionAssert.Contains(offers, QABoss103Plan.PickOffer(seed, 1 + seed % 4, (QABoss103Character)(seed % 3), offers));
    }

    [Test]
    public void 제시가_비면_마이너스1()
    {
        Assert.AreEqual(-1, QABoss103Plan.PickOffer(1, 1, QABoss103Character.Gunner, new List<int>()));
        Assert.AreEqual(-1, QABoss103Plan.PickOffer(1, 1, QABoss103Character.Gunner, null));
    }

    [Test]
    public void 시드가_바뀌면_선택이_고루_퍼진다()
    {
        var offers = new List<int> { 0, 1, 2 };
        var counts = new int[3];
        for (int seed = 0; seed < 300; seed++)
            counts[QABoss103Plan.PickOffer(QABoss103Plan.RunSeed(seed, 1), 1, QABoss103Character.Paladin, offers)]++;
        foreach (int n in counts)
            Assert.Greater(n, 60, $"치우침: {string.Join(",", counts)}");
    }

    [Test]
    public void 판_번호가_다르면_판_시드가_다르다()
    {
        var seen = new HashSet<int>();
        for (int run = 1; run <= 90; run++)
            Assert.IsTrue(seen.Add(QABoss103Plan.RunSeed(103, run)), $"판 {run} 시드 겹침");
    }

    [Test]
    public void 특성_라운드는_복도_4개()
    {
        Assert.AreEqual(4, QABoss103Plan.TraitRounds);
    }

    // ── 파티 자리 ───────────────────────────────────────────

    [Test]
    public void 자리는_clientId_순위이고_팔라딘_어쌔신_거너()
    {
        var ids = new List<ulong> { 5, 0, 2 };
        Assert.AreEqual(0, QABoss103Plan.SlotOf(0, ids));
        Assert.AreEqual(1, QABoss103Plan.SlotOf(2, ids));
        Assert.AreEqual(2, QABoss103Plan.SlotOf(5, ids));
        Assert.AreEqual(-1, QABoss103Plan.SlotOf(9, ids));
        Assert.AreEqual(QABoss103Character.Paladin, QABoss103Plan.CharacterForSlot(0));
        Assert.AreEqual(QABoss103Character.Assassin, QABoss103Plan.CharacterForSlot(1));
        Assert.AreEqual(QABoss103Character.Gunner, QABoss103Plan.CharacterForSlot(2));
        Assert.AreEqual(QABoss103Character.Paladin, QABoss103Plan.CharacterForSlot(3));
        Assert.AreEqual(QABoss103Character.Gunner, QABoss103Plan.CharacterForSlot(-1));
    }

    // ── 발판 배치 ───────────────────────────────────────────

    [Test]
    public void 발판_일렬_배치는_가운데_대칭이고_폭_80퍼센트_안()
    {
        Assert.AreEqual(0f, QABoss103Plan.PadLineOffset(0, 1, 3f));
        Assert.AreEqual(-1.4f, QABoss103Plan.PadLineOffset(0, 3, 3f), 1e-5f);
        Assert.AreEqual(0f, QABoss103Plan.PadLineOffset(1, 3, 3f), 1e-5f);
        Assert.AreEqual(1.4f, QABoss103Plan.PadLineOffset(2, 3, 3f), 1e-5f);
        // 좁은 발판(반폭 1m) — 양끝이 0.8m 안으로 줄어든다.
        Assert.AreEqual(-0.8f, QABoss103Plan.PadLineOffset(0, 3, 1f), 1e-5f);
        Assert.AreEqual(0.8f, QABoss103Plan.PadLineOffset(2, 3, 1f), 1e-5f);
    }

    [Test]
    public void 출격_구역_3인_자리는_서로_1m_이상_떨어지고_구역_안()
    {
        // Gather_Sortie 3m×2m — 긴 축 반폭 1.5m, 간격 1.1m.
        float a = QABoss103Plan.PadLineOffset(0, 3, 1.5f, 1.1f);
        float b = QABoss103Plan.PadLineOffset(1, 3, 1.5f, 1.1f);
        float c = QABoss103Plan.PadLineOffset(2, 3, 1.5f, 1.1f);
        Assert.GreaterOrEqual(b - a, 1f);
        Assert.GreaterOrEqual(c - b, 1f);
        Assert.LessOrEqual(System.Math.Abs(a), 1.5f - 0.3f, "걷기 도착 여유 0.3m 를 빼도 구역 안");
        Assert.LessOrEqual(System.Math.Abs(c), 1.5f - 0.3f);
    }

    [Test]
    public void 선택대_둘레_자리_각도는_가운데_대칭으로_펼친다()
    {
        Assert.AreEqual(0f, QABoss103Plan.ArcAngle(0, 1));
        Assert.AreEqual(-50f, QABoss103Plan.ArcAngle(0, 3), 1e-4f);
        Assert.AreEqual(0f, QABoss103Plan.ArcAngle(1, 3), 1e-4f);
        Assert.AreEqual(50f, QABoss103Plan.ArcAngle(2, 3), 1e-4f);
        Assert.AreEqual(-30f, QABoss103Plan.ArcAngle(0, 2, 60f), 1e-4f);
        Assert.AreEqual(30f, QABoss103Plan.ArcAngle(1, 2, 60f), 1e-4f);
    }

    [Test]
    public void 선택대_반경_70퍼센트_호_자리는_서로_겹치지_않는다()
    {
        // 반경 2m × 0.7 = 1.4m, 50° 간격 → 현 길이 2·1.4·sin(25°) ≈ 1.18m(플레이어 캡슐 지름보다 넉넉).
        double r = 2.0 * 0.7;
        double gap = QABoss103Plan.ArcAngle(1, 3) - QABoss103Plan.ArcAngle(0, 3);
        double chord = 2 * r * System.Math.Sin(gap * System.Math.PI / 360.0);
        Assert.Greater(chord, 1.0);
    }

    // ── 설정·기록 ───────────────────────────────────────────

    [Test]
    public void 설정_파일_왕복과_기본값()
    {
        var cfg = new QABoss103Config { Seed = 9001, Run = 17 };
        Assert.IsTrue(QABoss103Config.TryParse(cfg.Format(), out QABoss103Config back));
        Assert.AreEqual(9001, back.Seed);
        Assert.AreEqual(17, back.Run);
        Assert.AreEqual(cfg.RunSeed, back.RunSeed);

        Assert.IsFalse(QABoss103Config.TryParse("", out QABoss103Config empty));
        Assert.AreEqual(QABoss103Config.DefaultSeed, empty.Seed);
        Assert.AreEqual(1, empty.Run);
        Assert.IsTrue(QABoss103Config.TryParse("junk\r\nrun=0\r\nseed=x", out QABoss103Config partial));
        Assert.AreEqual(1, partial.Run, "판 번호는 1 이상");
        Assert.AreEqual(QABoss103Config.DefaultSeed, partial.Seed, "깨진 값은 기본값");
    }

    [Test]
    public void 판_기록_한_줄은_QA103_접두와_고정_순서_특성()
    {
        var rec = new QABoss103RunRecord { Run = 3, BaseSeed = 103, RunSeed = 55, Outcome = QABoss103Outcome.Wipe, CombatSeconds = 612.34, Detail = "전멸 리스폰 (x=1)" };
        rec.AddTrait(QABoss103Character.Gunner, "GUN_1_CHARGE");
        rec.AddTrait(QABoss103Character.Paladin, "PAL_1_Q_DAMAGE");
        rec.AddTrait(QABoss103Character.Paladin, "PAL_2_DASH");
        string line = rec.ToLogLine();
        StringAssert.StartsWith("[QA103] event=run_end run=3 outcome=Wipe combatS=612.3 seed=103 runSeed=55 ", line);
        StringAssert.Contains("traits=Paladin:PAL_1_Q_DAMAGE+PAL_2_DASH;Gunner:GUN_1_CHARGE ", line);
        StringAssert.EndsWith("detail=전멸_리스폰_(x_1)", line);
        Assert.AreEqual(2, rec.TraitCount(QABoss103Character.Paladin));
        Assert.AreEqual(0, rec.TraitCount(QABoss103Character.Assassin));
    }

    [Test]
    public void Combat_전_종료는_전투시간_na()
    {
        var rec = new QABoss103RunRecord { Outcome = QABoss103Outcome.Aborted };
        StringAssert.Contains("combatS=na", rec.ToLogLine());
        StringAssert.Contains("traits=none", rec.ToLogLine());
    }
}
#endif
