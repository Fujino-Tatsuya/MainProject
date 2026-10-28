using System;
using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 할 일 125 (1/5) — 고정 후보 방식의 <see cref="TraitSelectionModel"/>: 차수 = 획득 수 + 1, 표 순서 제시, 자동 선택 = 첫 번째,
/// 후보 0 = 기존 Empty 흐름, 재선택(초기화 뒤 1차부터)·재합류 복원(모르는 키 거름) 유지.
/// </summary>
public sealed class TraitFixedSelectionTests
{
    const ulong Pal = 1;
    const ulong Gun = 2;
    const ulong Nobody = 3; // 캐릭터 모름

    static readonly Func<int, int> Rng = _ => throw new InvalidOperationException("고정 방식은 난수를 쓰면 안 된다");

    TraitCatalogIndex catalog;
    Dictionary<ulong, TutorialCharacter> characters;

    [SetUp]
    public void SetUp()
    {
        var ids = new List<string>();
        foreach (TraitCatalog.Slot slot in TraitCatalog.Spec)
            ids.Add(slot.Id);
        catalog = new TraitCatalogIndex(ids);
        characters = new Dictionary<ulong, TutorialCharacter>
        {
            { Pal, TutorialCharacter.Paladin },
            { Gun, TutorialCharacter.Gunner },
        };
    }

    TraitSelectionModel NewModel() => new TraitSelectionModel(
        (clientId, tier, result) => catalog.GetCandidates(
            characters.TryGetValue(clientId, out TutorialCharacter c) ? c : TutorialCharacter.None, tier, result),
        catalog.Contains);

    static int H(string id) => TraitIdHash.Of(id);

    [Test]
    public void Round1_OffersTier1_InSpecOrder_PerCharacter()
    {
        TraitSelectionModel model = NewModel();
        Assert.IsTrue(model.IsFixed);
        Assert.IsTrue(model.StartRound(new[] { Pal, Gun }, 3, Rng));

        Assert.That(model.GetOffered(Pal), Is.EqualTo(new[] { H("PAL_1_Q_DAMAGE"), H("PAL_1_E_COOLDOWN"), H("PAL_1_PASSIVE_HEAL") }));
        Assert.That(model.GetOffered(Gun), Is.EqualTo(new[] { H("GUN_1_BASIC_DAMAGE"), H("GUN_1_CHARGE"), H("GUN_1_WIDTH") }));
    }

    [Test]
    public void FourRounds_TierFollowsAcquiredCount()
    {
        TraitSelectionModel model = NewModel();
        string[] picks = { "PAL_1_E_COOLDOWN", "PAL_2_SHARE_HEAL", "PAL_3_Q_STUN", "PAL_4_HEAL_SHIELD" };
        for (int round = 0; round < 4; round++)
        {
            Assert.AreEqual(round + 1, model.NextTier(Pal));
            model.StartRound(new[] { Pal }, 3, Rng);
            Assert.AreEqual(TraitChoiceResult.Accepted, model.TryChoose(Pal, H(picks[round])), picks[round]);
        }

        Assert.That(model.GetAcquired(Pal), Is.EqualTo(Array.ConvertAll(picks, H)));

        // 5번째 라운드 = 5차 없음 → 0장(바로 완료)
        Assert.IsFalse(model.StartRound(new[] { Pal }, 3, Rng));
        Assert.IsFalse(model.HasPendingChoice(Pal));
        Assert.IsTrue(model.IsRoundComplete());
    }

    [Test]
    public void ChoosingOtherTiersCandidate_IsNotOffered()
    {
        TraitSelectionModel model = NewModel();
        model.StartRound(new[] { Pal }, 3, Rng);
        Assert.AreEqual(TraitChoiceResult.NotOffered, model.TryChoose(Pal, H("PAL_2_DASH")));
        Assert.AreEqual(TraitChoiceResult.NotOffered, model.TryChoose(Pal, H("GUN_1_CHARGE")));
    }

    [Test]
    public void AutoChoice_IsFirstCandidate()
    {
        TraitSelectionModel model = NewModel();
        model.StartRound(new[] { Pal, Gun }, 3, Rng);
        Assert.AreEqual(H("PAL_1_Q_DAMAGE"), model.PickAutoChoice(Pal, Rng));
        Assert.AreEqual(H("GUN_1_BASIC_DAMAGE"), model.PickAutoChoice(Gun, Rng));
    }

    [Test]
    public void GrantWithoutOffer_TakesFirstCandidate()
    {
        TraitSelectionModel model = NewModel();
        model.StartRound(Array.Empty<ulong>(), 3, Rng);
        Assert.AreEqual(TraitJoinResult.Granted, model.AddParticipantWithoutOffer(Gun, Rng, out int granted));
        Assert.AreEqual(H("GUN_1_BASIC_DAMAGE"), granted);
        Assert.AreEqual(0, model.GetOfferSeq(Gun), "제시 번호가 안 올라 창이 열리지 않는다");
    }

    [Test]
    public void UnknownCharacter_GetsZeroCards_EmptyFlow()
    {
        TraitSelectionModel model = NewModel();
        Assert.IsFalse(model.StartRound(new[] { Nobody }, 3, Rng));
        Assert.IsFalse(model.HasPendingChoice(Nobody));
        Assert.IsTrue(model.IsRoundComplete());

        model.StartRound(new[] { Pal }, 3, Rng);
        Assert.AreEqual(TraitJoinResult.Empty, model.AddParticipant(Nobody, Rng));
        Assert.AreEqual(TraitJoinResult.Empty, model.AddParticipantWithoutOffer(Nobody + 10, Rng, out int none));
        Assert.AreEqual(-1, none);
    }

    [Test]
    public void EmptyPool_EveryoneGetsZeroCards()
    {
        catalog = new TraitCatalogIndex(new string[0]);
        TraitSelectionModel model = NewModel();
        Assert.IsFalse(model.StartRound(new[] { Pal, Gun }, 3, Rng));
        Assert.IsTrue(model.IsRoundComplete());
        Assert.AreEqual(-1, model.PickAutoChoice(Pal, Rng));
    }

    [Test]
    public void PartialRegistration_OffersOnlyRegistered()
    {
        catalog = new TraitCatalogIndex(new[] { "PAL_1_PASSIVE_HEAL" });
        TraitSelectionModel model = NewModel();
        Assert.IsTrue(model.StartRound(new[] { Pal }, 3, Rng));
        Assert.That(model.GetOffered(Pal), Is.EqualTo(new[] { H("PAL_1_PASSIVE_HEAL") }));
    }

    [Test]
    public void OfferCount_LimitsCandidates()
    {
        TraitSelectionModel model = NewModel();
        model.StartRound(new[] { Pal }, 2, Rng);
        Assert.That(model.GetOffered(Pal), Is.EqualTo(new[] { H("PAL_1_Q_DAMAGE"), H("PAL_1_E_COOLDOWN") }));
    }

    [Test]
    public void ResetThenPersonalRound_StartsAgainFromTier1()
    {
        TraitSelectionModel model = NewModel();
        model.StartRound(new[] { Pal }, 3, Rng);
        model.TryChoose(Pal, H("PAL_1_Q_DAMAGE"));
        model.StartRound(new[] { Pal }, 3, Rng);
        model.TryChoose(Pal, H("PAL_2_DASH"));

        model.ResetPlayer(Pal);
        Assert.IsTrue(model.StartPersonalRound(Pal, 3, Rng));
        Assert.That(model.GetOffered(Pal), Is.EqualTo(new[] { H("PAL_1_Q_DAMAGE"), H("PAL_1_E_COOLDOWN"), H("PAL_1_PASSIVE_HEAL") }));
    }

    [Test]
    public void RestoreAcquired_KeepsKnownHashes_DropsUnknownAndNone()
    {
        TraitSelectionModel model = NewModel();
        int restored = model.RestoreAcquired(Pal, new[] { H("PAL_1_Q_DAMAGE"), TraitIdHash.None, H("not_a_trait"), H("PAL_2_DASH"), H("PAL_1_Q_DAMAGE") });
        Assert.AreEqual(2, restored);
        Assert.That(model.GetAcquired(Pal), Is.EqualTo(new[] { H("PAL_1_Q_DAMAGE"), H("PAL_2_DASH") }));

        // 복원 뒤 다음 라운드 = 3차
        model.StartRound(new[] { Pal }, 3, Rng);
        Assert.That(model.GetOffered(Pal), Is.EqualTo(new[] { H("PAL_3_Q_STUN"), H("PAL_3_E_SHIELD"), H("PAL_3_PASSIVE_CDR") }));
    }

    [Test]
    public void RejoinRoundTrip_UsesHashes()
    {
        TraitSelectionModel model = NewModel();
        var reset = new TraitResetModel();
        model.StartRound(new[] { Gun }, 3, Rng);
        model.TryChoose(Gun, H("GUN_1_WIDTH"));

        TraitRejoinSnapshot snapshot = TraitRejoin.Capture(model, reset, Gun);
        model.RemovePlayer(Gun);

        const ulong newGun = 7;
        characters[newGun] = TutorialCharacter.Gunner;
        TraitRejoin.Restore(model, reset, newGun, snapshot, allowReset: true);
        Assert.That(model.GetAcquired(newGun), Is.EqualTo(new[] { H("GUN_1_WIDTH") }));
        Assert.AreEqual(TraitJoinResult.AlreadyInRound, model.AddParticipant(newGun, Rng), "같은 라운드 두 번 획득 금지");
    }
}
