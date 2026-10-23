using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>할 일 86 T6a — 튜토리얼 문구 출처 = SO + Strings(Loc). 시트 우선 · SO 폴백 · 변형 선택 · Export 수집 · 검증기.</summary>
public sealed class TutorialGuideTextSourceTests
{
    sealed class FakeSource : ITutorialGuideTextSource
    {
        readonly Dictionary<string, TutorialGuideTextRow> rows = new Dictionary<string, TutorialGuideTextRow>();

        public FakeSource Add(string id, string text, float duration = 6f)
        {
            rows[id] = new TutorialGuideTextRow(id, text, duration);
            return this;
        }

        public bool TryGetRow(string id, out TutorialGuideTextRow row) => rows.TryGetValue(id, out row);
    }

    StringTable table;
    TutorialGuideTextTable soTable;

    [SetUp]
    public void SetUp()
    {
        table = ScriptableObject.CreateInstance<StringTable>();
        Loc.SetTable(table);
    }

    [TearDown]
    public void TearDown()
    {
        Loc.SetTable(null);
        Object.DestroyImmediate(table);
        if (soTable != null)
            Object.DestroyImmediate(soTable);
    }

    void Sheet(params (string id, string ko)[] rows) =>
        table.SetEntries(rows.Select(r => new StringTable.Entry(TutorialGuideTextSource.KeyOf(r.id), r.ko, string.Empty)));

    [Test]
    public void Key_IsFixedPrefixGeneralKey()
    {
        Assert.AreEqual("Tutorial.Guide.TUT_Z2_Q", TutorialGuideTextSource.KeyOf("TUT_Z2_Q"));
        Assert.IsFalse(LocKey.TrySplit(TutorialGuideTextSource.KeyOf("TUT_Z2_Q"), out _, out _), "자동 키(주인ID/필드경로)가 아니다");
    }

    [Test]
    public void SheetRow_WinsOverSo_KeepsSoDuration()
    {
        Sheet(("TUT_A", "시트 문구"));
        var source = new TutorialGuideTextSource(new FakeSource().Add("TUT_A", "SO 문구", 7f));

        Assert.AreEqual(TutorialGuideTextStatus.Found,
            TutorialGuideText.Resolve(source, "TUT_A", TutorialCharacter.None, out TutorialGuideTextRow row));
        Assert.AreEqual("시트 문구", row.Text);
        Assert.AreEqual(7f, row.DurationSeconds);
    }

    [Test]
    public void NoSheetRow_FallsBackToSoText()
    {
        Sheet(("TUT_OTHER", "딴 문구"));
        var source = new TutorialGuideTextSource(new FakeSource().Add("TUT_A", "SO 문구").Add("TUT_EMPTY", ""));

        TutorialGuideText.Resolve(source, "TUT_A", TutorialCharacter.Gunner, out TutorialGuideTextRow row);
        Assert.AreEqual("SO 문구", row.Text, "#키 가 아니라 SO 문구");
        Assert.AreEqual(TutorialGuideTextStatus.Empty,
            TutorialGuideText.Resolve(source, "TUT_EMPTY", TutorialCharacter.None, out _));
        Assert.AreEqual(TutorialGuideTextStatus.Missing,
            TutorialGuideText.Resolve(source, "TUT_NOPE", TutorialCharacter.None, out _));
    }

    [Test]
    public void SheetOnlyId_IsFound_WithDefaultDuration()
    {
        Sheet(("LOBBY_NEW", "새 로비 대사"));
        var source = new TutorialGuideTextSource(null);

        Assert.AreEqual(TutorialGuideTextStatus.Found,
            TutorialGuideText.Resolve(source, "LOBBY_NEW", TutorialCharacter.None, out TutorialGuideTextRow row));
        Assert.AreEqual("새 로비 대사", row.Text);
        Assert.AreEqual(0f, row.DurationSeconds);
        Assert.IsNull(row.Sfx);
    }

    [Test]
    public void Variant_SheetOnlyVariant_IsSelected_InheritsBaseDuration()
    {
        Sheet(("TUT_Z2_Q_GUNNER", "거너 시트"));
        var source = new TutorialGuideTextSource(new FakeSource().Add("TUT_Z2_Q", "기본", 9f));

        Assert.AreEqual("TUT_Z2_Q_GUNNER", TutorialGuideText.SelectVariantId(source, "TUT_Z2_Q", TutorialCharacter.Gunner));
        TutorialGuideText.Resolve(source, "TUT_Z2_Q", TutorialCharacter.Gunner, out TutorialGuideTextRow row);
        Assert.AreEqual("거너 시트", row.Text);
        Assert.AreEqual(9f, row.DurationSeconds);

        TutorialGuideText.Resolve(source, "TUT_Z2_Q", TutorialCharacter.Paladin, out row);
        Assert.AreEqual("기본", row.Text);
    }

    [Test]
    public void Variant_EmptySoVariant_UsesSheetVariantText()
    {
        Sheet(("TUT_Z2_E_ASSASSIN", "어새신 시트"));
        var source = new TutorialGuideTextSource(new FakeSource().Add("TUT_Z2_E", "기본").Add("TUT_Z2_E_ASSASSIN", ""));

        TutorialGuideText.Resolve(source, "TUT_Z2_E", TutorialCharacter.Assassin, out TutorialGuideTextRow row);
        Assert.AreEqual("TUT_Z2_E_ASSASSIN", row.Id);
        Assert.AreEqual("어새신 시트", row.Text);
    }

    [Test]
    public void Variant_SoVariantWithoutSheet_StillUsed()
    {
        Sheet(("TUT_Z2_Q", "기본 시트"));
        var source = new TutorialGuideTextSource(new FakeSource().Add("TUT_Z2_Q", "기본").Add("TUT_Z2_Q_PALADIN", "팔라딘 SO"));

        TutorialGuideText.Resolve(source, "TUT_Z2_Q", TutorialCharacter.Paladin, out TutorialGuideTextRow row);
        Assert.AreEqual("팔라딘 SO", row.Text);
        TutorialGuideText.Resolve(source, "TUT_Z2_Q", TutorialCharacter.Gunner, out row);
        Assert.AreEqual("기본 시트", row.Text);
    }

    [Test]
    public void SheetText_PlaceholdersStillFilled()
    {
        Sheet(("TUT_KEYS", "{Q} · {E} · {P} — {mm:ss}"));
        var source = new TutorialGuideTextSource(new FakeSource().Add("TUT_KEYS", "SO"));
        var args = new Dictionary<string, string> { { "Q", "방패" }, { "E", "돌진" }, { "P", "가호" }, { "mm:ss", "00:05" } };

        TutorialGuideText.Resolve(source, "TUT_KEYS", TutorialCharacter.None, out TutorialGuideTextRow row);
        Assert.AreEqual("방패 · 돌진 · 가호 — 00:05", TutorialGuideText.Fill(row.Text, args));
    }

    [Test]
    public void Collector_EmitsGeneralKeysWithSoKo_SkipsEmpty()
    {
        soTable = ScriptableObject.CreateInstance<TutorialGuideTextTable>();
        soTable.AddRowIfMissing("TUT_Z1_MOVE", "{Q} 로 이동", 5f);
        soTable.AddRowIfMissing("TUT_Z3_HINT", "", 0f);
        soTable.AddRowIfMissing("BOSS_RAID_INTRO", "웰즈다", 4f);

        List<StringKeyInfo> keys = TutorialGuideStringKeys.Collect(soTable);

        CollectionAssert.AreEqual(new[] { "Tutorial.Guide.TUT_Z1_MOVE", "Tutorial.Guide.BOSS_RAID_INTRO" }, keys.Select(k => k.Key));
        Assert.AreEqual("{Q} 로 이동", keys[0].Ko);
        StringAssert.Contains("TutorialGuideTexts", keys[0].Description);
        StringAssert.Contains("레이드", keys[1].Description);
    }

    [Test]
    public void Collector_IsRegisteredForExport()
    {
        string any = TutorialGuideStringKeys.Collect().Select(k => k.Key).FirstOrDefault();
        Assume.That(any, Is.Not.Null, "TutorialGuideTexts 에셋에 문구 행이 없다");
        Assert.IsTrue(StringKeyRegistry.Collect().Any(k => k.Key == any));
    }

    [Test]
    public void Validator_PlaceholderRows_AreNotErrors()
    {
        var rows = new List<StringRow>
        {
            Row(TutorialGuideTextSource.KeyOf("TUT_KEYS"), "{Q} 를 누르고 {E} · {P} — {mm:ss} · {n}/{total}",
                "Press {Q}, {E} · {P} — {mm:ss} · {n}/{total}"),
        };
        rows.AddRange(TutorialGuideStringKeys.Collect().Select(k => Row(k.Key, k.Ko, string.Empty)));

        var issues = new DataTableIssues();
        StringTableValidator.Validate(rows, issues, StringKeyOwners.Resolver(null));

        Assert.IsFalse(issues.HasErrors,
            string.Join("\n", issues.Items.Where(i => i.Kind == DataTableIssueKind.Error).Select(i => i.ToString())));
    }

    static StringRow Row(string key, string ko, string en) =>
        new StringRow(key, "test", ko, en, "Strings!A" + key, "Strings!C" + key, "Strings!D" + key);
}
