using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 할 일 125 5/5 — 2차 상충 확인창 규칙(<see cref="TraitPickConfirmModel"/>, 명세 §2)과 콘텐츠 등록부(<see cref="TraitContentRegistry"/>) 규칙.
/// 상충이면 확인창 → [확인] 1회 전송 / [취소] 전송 없음, 상충 없으면 바로 전송, 창이 닫히면(60초 자동 선택 등) 열린 확인창은 보내지 않는다.
/// </summary>
public class TraitPickConfirmTests
{
    private const string Restraint = "ASN_2_RESTRAINT";
    private const string Frenzy = "ASN_1_TRANS_E_DAMAGE";
    private static readonly int RestraintHash = TraitIdHash.Of(Restraint);
    private static readonly int ReturnHash = TraitIdHash.Of("ASN_2_RETURN");

    private static TraitContentRegistry RegistryWithRestraint()
    {
        var registry = new TraitContentRegistry();
        registry.For(TutorialCharacter.Assassin).Conflict(Restraint, Frenzy, "변신 불가 ‘{traits}’ 선택?");
        return registry;
    }

    [Test]
    public void ProjectRegistry_HasSpecConflict_RestraintDisablesFrenzy()
    {
        var result = new List<string>();
        TraitContent.Build().GetDisabledBy(Restraint, new[] { "ASN_1_Q_COOLDOWN", Frenzy }, result);
        CollectionAssert.AreEqual(new[] { Frenzy }, result);
    }

    [Test]
    public void NoConflict_SendsImmediately()
    {
        var model = new TraitPickConfirmModel();
        Assert.AreEqual(ReturnHash, model.Click(ReturnHash, "ASN_2_RETURN", new[] { Frenzy }, RegistryWithRestraint()));
        Assert.IsFalse(model.IsAsking);
    }

    [Test]
    public void ConflictButNotOwned_SendsImmediately()
    {
        var model = new TraitPickConfirmModel();
        Assert.AreEqual(RestraintHash, model.Click(RestraintHash, Restraint, new[] { "ASN_1_Q_COOLDOWN" }, RegistryWithRestraint()));
        Assert.IsFalse(model.IsAsking);
    }

    [Test]
    public void Conflict_AsksThenConfirmSendsOnce()
    {
        var model = new TraitPickConfirmModel();
        Assert.AreEqual(TraitIdHash.None, model.Click(RestraintHash, Restraint, new[] { Frenzy }, RegistryWithRestraint()));
        Assert.IsTrue(model.IsAsking);
        CollectionAssert.AreEqual(new[] { Frenzy }, model.DisabledIds);

        Assert.AreEqual(RestraintHash, model.Confirm());
        Assert.AreEqual(TraitIdHash.None, model.Confirm(), "두 번 보내지 않는다");
        Assert.IsFalse(model.IsAsking);
    }

    [Test]
    public void Conflict_CancelSendsNothing_CanPickAgain()
    {
        var model = new TraitPickConfirmModel();
        TraitContentRegistry registry = RegistryWithRestraint();
        model.Click(RestraintHash, Restraint, new[] { Frenzy }, registry);
        model.Cancel();
        Assert.IsFalse(model.IsAsking);
        Assert.AreEqual(TraitIdHash.None, model.Confirm());

        Assert.AreEqual(ReturnHash, model.Click(ReturnHash, "ASN_2_RETURN", new[] { Frenzy }, registry), "취소 뒤 다른 카드는 바로");
    }

    [Test]
    public void WhileAsking_OtherClicksIgnored()
    {
        var model = new TraitPickConfirmModel();
        TraitContentRegistry registry = RegistryWithRestraint();
        model.Click(RestraintHash, Restraint, new[] { Frenzy }, registry);
        Assert.AreEqual(TraitIdHash.None, model.Click(ReturnHash, "ASN_2_RETURN", new[] { Frenzy }, registry));
        Assert.AreEqual(RestraintHash, model.PendingHash, "확인창이 떠 있는 동안 다른 클릭은 무시");
    }

    [Test]
    public void WindowClosed_AutoSelect_DropsPendingWithoutSending()
    {
        // 60초 출구 자동 선택은 서버가 첫 후보를 고른다 — 창이 닫히면 열린 확인창은 보내지 않는다
        var model = new TraitPickConfirmModel();
        model.Click(RestraintHash, Restraint, new[] { Frenzy }, RegistryWithRestraint());
        model.Reset();
        Assert.IsFalse(model.IsAsking);
        Assert.AreEqual(TraitIdHash.None, model.Confirm());
    }

    [Test]
    public void NullRegistryOrNoneHash_NeverAsks()
    {
        var model = new TraitPickConfirmModel();
        Assert.AreEqual(RestraintHash, model.Click(RestraintHash, Restraint, new[] { Frenzy }, null));
        Assert.AreEqual(TraitIdHash.None, model.Click(TraitIdHash.None, Restraint, new[] { Frenzy }, RegistryWithRestraint()));
        Assert.IsFalse(model.IsAsking);
    }

    [Test]
    public void Message_UsesRegisteredText_ThenDefault()
    {
        string registered = TraitPickConfirmModel.FormatMessage(Restraint, new[] { "광란 강화" }, RegistryWithRestraint());
        Assert.AreEqual("변신 불가 ‘광란 강화’ 선택?", registered);

        string fallback = TraitPickConfirmModel.FormatMessage("ASN_2_RETURN", new[] { "가", "나" }, new TraitContentRegistry());
        StringAssert.Contains("가" + TraitUiStrings.NameSeparator.Text + "나", fallback);
        StringAssert.DoesNotContain("{traits}", fallback);
    }

    // ── 등록부 규칙 ──────────────────────────────────────────────────

    [Test]
    public void Registry_OtherCharacterId_IsError()
    {
        var registry = new TraitContentRegistry();
        registry.For(TutorialCharacter.Paladin).Damage("GUN_4_EXTRA_BASIC", (t, p) => default);
        Assert.AreEqual(1, registry.Errors.Count);
        StringAssert.StartsWith("Paladin", registry.Errors[0]);
        Assert.IsFalse(registry.TryGetDamage("GUN_4_EXTRA_BASIC", out _));
    }

    [Test]
    public void Registry_UnknownId_Duplicate_Null_SelfConflict_AreErrors()
    {
        var registry = new TraitContentRegistry();
        TraitContentRegistry.Registrar r = registry.For(TutorialCharacter.Paladin);
        r.Damage("PAL_9_NOPE", (t, p) => default);
        r.Damage("PAL_2_DASH", (t, p) => default);
        r.Damage("PAL_2_DASH", (t, p) => default);
        r.Damage("PAL_4_Q_BLAST", null);
        r.Conflict("PAL_2_DASH", "PAL_2_DASH");
        r.Value("PAL_2_DASH", "", null);
        Assert.AreEqual(5, registry.Errors.Count, string.Join("\n", registry.Errors));
        Assert.IsTrue(registry.TryGetDamage("PAL_2_DASH", out _), "첫 등록은 유지");
    }

    [Test]
    public void Registry_ConflictAcrossCharacters_IsError()
    {
        var registry = new TraitContentRegistry();
        registry.For(TutorialCharacter.Assassin).Conflict(Restraint, "PAL_1_Q_DAMAGE");
        Assert.AreEqual(1, registry.Errors.Count);
    }
}
