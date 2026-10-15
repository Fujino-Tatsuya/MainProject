using System.Collections.Generic;
using NUnit.Framework;

/// <summary>할 일 15 S6 — 문구 변형 선택 · 자리표시자 치환 · 대기 시간 포맷.</summary>
public sealed class TutorialGuideTextTests
{
    sealed class FakeSource : ITutorialGuideTextSource
    {
        readonly Dictionary<string, TutorialGuideTextRow> rows = new Dictionary<string, TutorialGuideTextRow>();

        public FakeSource Add(string id, string text)
        {
            rows[id] = new TutorialGuideTextRow(id, text, 6f);
            return this;
        }

        public bool TryGetRow(string id, out TutorialGuideTextRow row) => rows.TryGetValue(id, out row);
    }

    [Test]
    public void Variant_UsesCharacterRowWhenPresent()
    {
        var source = new FakeSource().Add("TUT_Z2_Q", "기본").Add("TUT_Z2_Q_GUNNER", "거너");

        Assert.AreEqual("TUT_Z2_Q_GUNNER", TutorialGuideText.SelectVariantId(source, "TUT_Z2_Q", TutorialCharacter.Gunner));
        Assert.AreEqual(TutorialGuideTextStatus.Found,
            TutorialGuideText.Resolve(source, "TUT_Z2_Q", TutorialCharacter.Gunner, out TutorialGuideTextRow row));
        Assert.AreEqual("거너", row.Text);
    }

    [Test]
    public void Variant_FallsBackToBaseWhenMissing()
    {
        var source = new FakeSource().Add("TUT_Z2_Q", "기본").Add("TUT_Z2_Q_GUNNER", "거너");

        Assert.AreEqual("TUT_Z2_Q", TutorialGuideText.SelectVariantId(source, "TUT_Z2_Q", TutorialCharacter.Paladin));
        Assert.AreEqual("TUT_Z2_Q", TutorialGuideText.SelectVariantId(source, "TUT_Z2_Q", TutorialCharacter.None));
    }

    [Test]
    public void Variant_EmptyVariantRowFallsBackToBase()
    {
        var source = new FakeSource().Add("TUT_Z2_E", "기본").Add("TUT_Z2_E_ASSASSIN", "");

        TutorialGuideText.Resolve(source, "TUT_Z2_E", TutorialCharacter.Assassin, out TutorialGuideTextRow row);

        Assert.AreEqual("TUT_Z2_E", row.Id);
        Assert.AreEqual("기본", row.Text);
    }

    [Test]
    public void Resolve_EmptyBaseIsEmpty_UnknownIsMissing()
    {
        var source = new FakeSource().Add("TUT_Z3_HINT", "").Add("TUT_Z3_HINT_GUNNER", "");

        Assert.AreEqual(TutorialGuideTextStatus.Empty,
            TutorialGuideText.Resolve(source, "TUT_Z3_HINT", TutorialCharacter.Gunner, out _));
        Assert.AreEqual(TutorialGuideTextStatus.Missing,
            TutorialGuideText.Resolve(source, "TUT_NOPE", TutorialCharacter.Gunner, out _));
    }

    [Test]
    public void Fill_ReplacesKnownPlaceholders()
    {
        var args = new Dictionary<string, string> { { "Q", "진격의 방패" }, { "n", "1" }, { "total", "3" }, { "mm:ss", "00:42" } };

        Assert.AreEqual("진격의 방패 — 1/3 · 00:42",
            TutorialGuideText.Fill("{Q} — {n}/{total} · {mm:ss}", args));
    }

    [Test]
    public void Fill_LeavesUnknownPlaceholdersAsIs()
    {
        var args = new Dictionary<string, string> { { "키", "F" } };

        Assert.AreEqual("{스킬명} 을 F 로 {E}", TutorialGuideText.Fill("{스킬명} 을 {키} 로 {E}", args));
        Assert.AreEqual("{Q} {", TutorialGuideText.Fill("{Q} {", null));
    }

    [Test]
    public void FormatWait_Boundaries()
    {
        Assert.AreEqual("00:00", TutorialGuideText.FormatWait(0f));
        Assert.AreEqual("00:59", TutorialGuideText.FormatWait(59f));
        Assert.AreEqual("01:00", TutorialGuideText.FormatWait(60f));
        Assert.AreEqual("00:00", TutorialGuideText.FormatWait(-3f));
        Assert.AreEqual("00:00", TutorialGuideText.FormatWait(float.NaN));
        Assert.AreEqual("00:02", TutorialGuideText.FormatWait(1.2f), "소수는 올림");
        Assert.AreEqual("02:05", TutorialGuideText.FormatWait(125f));
    }
}
