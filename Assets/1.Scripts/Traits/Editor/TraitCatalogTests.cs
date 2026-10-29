using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

/// <summary>할 일 125 (1/5) — 특성 ID 고정 해시·명세 고정표·풀 조회표(검증·고정 후보)·목록 버전 비교.</summary>
public sealed class TraitCatalogTests
{
    static readonly TutorialCharacter[] Characters = { TutorialCharacter.Paladin, TutorialCharacter.Gunner, TutorialCharacter.Assassin };

    static string[] AllSpecIds() => TraitCatalog.Spec.Select(s => s.Id).ToArray();

    // ── 해시 ────────────────────────────────────────────────────────────

    [Test]
    public void Hash_IsFnv1aOverUtf8_AndStableAcrossRuns()
    {
        // FNV-1a 32 기준값 — 값이 바뀌면 저장·네트워크 키가 전부 바뀐다(회귀 고정).
        Assert.AreEqual(-468965076, TraitIdHash.Of("a"));
        Assert.AreEqual(-1847907914, TraitIdHash.Of("PAL_1_Q_DAMAGE"));
        Assert.AreEqual(705525573, TraitIdHash.Of("GUN_3_VOLLEY"));
        Assert.AreEqual(TraitIdHash.Of("PAL_1_Q_DAMAGE"), TraitIdHash.Of(new string("PAL_1_Q_DAMAGE".ToCharArray())));
    }

    [Test]
    public void Hash_NullOrEmpty_IsNone()
    {
        Assert.AreEqual(TraitIdHash.None, TraitIdHash.Of(null));
        Assert.AreEqual(TraitIdHash.None, TraitIdHash.Of(""));
    }

    // ── 명세 고정표 ──────────────────────────────────────────────────────

    [Test]
    public void Spec_Has36_12PerCharacter_3PerTier()
    {
        Assert.AreEqual(36, TraitCatalog.Spec.Count);
        foreach (TutorialCharacter character in Characters)
        {
            Assert.AreEqual(12, TraitCatalog.Spec.Count(s => s.Character == character), character.ToString());
            for (int tier = 1; tier <= TraitCatalog.TierCount; tier++)
                Assert.AreEqual(TraitCatalog.CandidatesPerTier,
                    TraitCatalog.Spec.Count(s => s.Character == character && s.Tier == tier), $"{character} {tier}차");
        }
    }

    [Test]
    public void Spec_IdsAndHashes_AreUniqueAndNonZero()
    {
        string[] ids = AllSpecIds();
        Assert.AreEqual(ids.Length, ids.Distinct().Count(), "ID 중복");
        int[] hashes = ids.Select(TraitIdHash.Of).ToArray();
        Assert.AreEqual(hashes.Length, hashes.Distinct().Count(), "해시 충돌");
        Assert.That(hashes, Has.None.EqualTo(TraitIdHash.None));
    }

    [Test]
    public void Spec_FullCatalog_HasNoErrors()
    {
        var catalog = new TraitCatalogIndex(AllSpecIds());
        Assert.That(catalog.Errors, Is.Empty);
        Assert.AreEqual(36, catalog.Count);
    }

    // ── 고정 후보 ────────────────────────────────────────────────────────

    [Test]
    public void Candidates_AreSpecOrder_PerCharacterAndTier()
    {
        var catalog = new TraitCatalogIndex(AllSpecIds().Reverse().ToArray()); // 풀 순서와 무관하게 표 순서
        var result = new List<int>();

        catalog.GetCandidates(TutorialCharacter.Paladin, 1, result);
        Assert.That(result, Is.EqualTo(new[]
        {
            TraitIdHash.Of("PAL_1_Q_DAMAGE"), TraitIdHash.Of("PAL_1_E_COOLDOWN"), TraitIdHash.Of("PAL_1_PASSIVE_HEAL"),
        }));

        catalog.GetCandidates(TutorialCharacter.Gunner, 3, result);
        Assert.That(result, Is.EqualTo(new[]
        {
            TraitIdHash.Of("GUN_3_Q_PUSH"), TraitIdHash.Of("GUN_3_PIERCE"), TraitIdHash.Of("GUN_3_VOLLEY"),
        }));

        catalog.GetCandidates(TutorialCharacter.Assassin, 4, result);
        Assert.That(result, Is.EqualTo(new[]
        {
            TraitIdHash.Of("ASN_4_NORMAL_E_PUSH"), TraitIdHash.Of("ASN_4_Q_TRAIL"), TraitIdHash.Of("ASN_4_COMBO_EXTRA"),
        }));
    }

    [Test]
    public void Candidates_OnlyRegisteredDefinitions()
    {
        var catalog = new TraitCatalogIndex(new[] { "PAL_1_PASSIVE_HEAL", "PAL_1_Q_DAMAGE" });
        var result = new List<int>();

        catalog.GetCandidates(TutorialCharacter.Paladin, 1, result);
        Assert.That(result, Is.EqualTo(new[] { TraitIdHash.Of("PAL_1_Q_DAMAGE"), TraitIdHash.Of("PAL_1_PASSIVE_HEAL") }));

        catalog.GetCandidates(TutorialCharacter.Paladin, 2, result);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Candidates_EmptyForUnknownCharacterTierOrEmptyPool()
    {
        var full = new TraitCatalogIndex(AllSpecIds());
        var result = new List<int> { 99 };

        full.GetCandidates(TutorialCharacter.None, 1, result);
        Assert.That(result, Is.Empty);
        full.GetCandidates(TutorialCharacter.Paladin, 0, result);
        Assert.That(result, Is.Empty);
        full.GetCandidates(TutorialCharacter.Paladin, 5, result);
        Assert.That(result, Is.Empty);

        new TraitCatalogIndex(new string[0]).GetCandidates(TutorialCharacter.Gunner, 1, result);
        Assert.That(result, Is.Empty);
        new TraitCatalogIndex(null).GetCandidates(TutorialCharacter.Gunner, 1, result);
        Assert.That(result, Is.Empty);
    }

    // ── 검증 ─────────────────────────────────────────────────────────────

    [Test]
    public void Validate_DuplicateId_IsError_FirstSlotWins()
    {
        var catalog = new TraitCatalogIndex(new[] { "PAL_1_Q_DAMAGE", "GUN_1_WIDTH", "PAL_1_Q_DAMAGE" });
        Assert.AreEqual(1, catalog.Errors.Count);
        Assert.That(catalog.Errors[0], Does.Contain("PAL_1_Q_DAMAGE"));
        Assert.IsTrue(catalog.TryGetPoolSlot(TraitIdHash.Of("PAL_1_Q_DAMAGE"), out int slot));
        Assert.AreEqual(0, slot);
    }

    [Test]
    public void Validate_HashCollision_IsError()
    {
        // FNV-1a 32 에서 실제로 충돌하는 두 문자열(오프라인으로 찾음).
        Assert.AreEqual(TraitIdHash.Of("X53578"), TraitIdHash.Of("X1160192"));
        var catalog = new TraitCatalogIndex(new[] { "X53578", "X1160192" });
        Assert.That(catalog.Errors.Any(e => e.Contains("해시 충돌")), Is.True, string.Join(" / ", catalog.Errors));
    }

    [Test]
    public void Validate_UnknownAndEmptyId_AreErrors_AndNotOffered()
    {
        var catalog = new TraitCatalogIndex(new[] { "trait_attack_10", "", null, "PAL_1_E_COOLDOWN" });
        Assert.AreEqual(2, catalog.Errors.Count, string.Join(" / ", catalog.Errors)); // 표에 없는 ID + 빈 ID (null = 빈 칸은 풀이 알린다)
        Assert.IsFalse(catalog.Contains(TraitIdHash.Of("trait_attack_10")));
        Assert.IsTrue(catalog.Contains(TraitIdHash.Of("PAL_1_E_COOLDOWN")));
        Assert.IsTrue(catalog.TryGetPoolSlot(TraitIdHash.Of("PAL_1_E_COOLDOWN"), out int slot));
        Assert.AreEqual(3, slot, "빈 칸이 있어도 풀 칸 번호는 유지");
        Assert.AreEqual(1, catalog.Count);
    }

    // ── 버전 ─────────────────────────────────────────────────────────────

    [Test]
    public void Version_SameIds_SameVersion()
    {
        Assert.AreEqual(new TraitCatalogIndex(AllSpecIds()).Version, new TraitCatalogIndex(AllSpecIds()).Version);
    }

    [Test]
    public void Version_ChangesWithOrderCountAndContent()
    {
        int baseline = new TraitCatalogIndex(new[] { "PAL_1_Q_DAMAGE", "PAL_1_E_COOLDOWN" }).Version;
        Assert.AreNotEqual(baseline, new TraitCatalogIndex(new[] { "PAL_1_E_COOLDOWN", "PAL_1_Q_DAMAGE" }).Version, "순서");
        Assert.AreNotEqual(baseline, new TraitCatalogIndex(new[] { "PAL_1_Q_DAMAGE" }).Version, "개수");
        Assert.AreNotEqual(baseline, new TraitCatalogIndex(new[] { "PAL_1_Q_DAMAGE", "PAL_1_PASSIVE_HEAL" }).Version, "내용");
        Assert.AreNotEqual(new TraitCatalogIndex(new string[0]).Version, new TraitCatalogIndex(new[] { "PAL_1_Q_DAMAGE" }).Version, "빈 목록");
    }

    [Test]
    public void Version_IsComputedFromIdHashList()
    {
        string[] ids = { "GUN_1_CHARGE", "ASN_2_RETURN" };
        Assert.AreEqual(TraitIdHash.Version(ids.Select(TraitIdHash.Of).ToList()), new TraitCatalogIndex(ids).Version);
    }

    [Test]
    public void CompareVersion_UnknownMatchMismatch()
    {
        Assert.AreEqual(TraitVersionCheck.Unknown, TraitCatalog.CompareVersion(0, 123));
        Assert.AreEqual(TraitVersionCheck.Match, TraitCatalog.CompareVersion(123, 123));
        Assert.AreEqual(TraitVersionCheck.Mismatch, TraitCatalog.CompareVersion(123, 456));
    }
}
