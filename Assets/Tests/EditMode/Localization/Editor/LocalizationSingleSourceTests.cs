using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// 툴팁 문구 단일 원본(Strings) — 옛 수치 시트 문구 칸 이관(TableTextMigration)·자동 키 행 {필드} 검사(StringTableValidator ownerOf).
// 러너 = 데이터 테이블 EditMode 테스트.

public sealed class TableTextMigrationTests
{
    private const string Key = "SkillA/tooltip.description";

    private static readonly DataTableTextKeys.Slot Description =
        new DataTableTextKeys.Slot("SkillA", "SkillA", "tooltip.description", Key, "인라인 문구");

    private static XlsxSheet Numeric(string description, string sheetName = "Paladin", string id = "SkillA") =>
        new XlsxSheet(sheetName, new[]
        {
            new[] { DataTableSchema.VerticalFieldHeader, DataTableSchema.DescriptionHeader, id },
            new[] { "cooldownTime", "", "8" },
            new[] { "tooltip.description", "", description },
        });

    private static XlsxSheet Strings(params (string key, string ko)[] rows) =>
        new XlsxSheet("Strings", new[] { new[] { "key", "#설명", "ko", "en" } }
            .Concat(rows.Select(r => new[] { r.key, "", r.ko, "" }))
            .ToArray());

    private static XlsxSheet Read(XlsxWriteSheet sheet) =>
        new XlsxSheet(sheet.Name, sheet.Rows.Select(r => r.Select(c => c?.ToString() ?? string.Empty).ToArray()).ToArray());

    [Test]
    public void NoStringsRow_MovesSheetValue_AsNewRow()
    {
        TableTextMigration plan = TableTextMigration.Plan(new[] { Numeric("기획이 고친 문구") }, new[] { Description });

        Assert.That(plan.Ok, Is.True);
        Assert.That(plan.Rows.Select(r => (r.Key, r.Ko, r.Description)),
            Is.EqualTo(new[] { (Key, "기획이 고친 문구", "SkillA · tooltip.description") }), "인라인 값이 아니라 수치 시트 값");
        Assert.That(plan.Moved, Is.EqualTo(new[] { ("SkillA", "tooltip.description") }), "숫자 칸(cooldownTime)은 옮기지 않는다");
        Assert.That(plan.Warnings, Is.Empty);
    }

    [Test]
    public void ExistingStringsRow_SameValue_NoDuplicate_NoWarning()
    {
        TableTextMigration plan = TableTextMigration.Plan(new[] { Numeric("같은 문구"), Strings((Key, "같은 문구")) }, new[] { Description });

        Assert.That(plan.Rows, Is.Empty);
        Assert.That(plan.Moved, Has.Count.EqualTo(1));
        Assert.That(plan.Warnings, Is.Empty);
    }

    [Test]
    public void ExistingStringsRow_DifferentValue_StringsWins_WithWarning()
    {
        TableTextMigration plan = TableTextMigration.Plan(new[] { Numeric("수치 시트 문구"), Strings((Key, "Strings 문구")) }, new[] { Description });

        Assert.That(plan.Rows, Is.Empty, "Strings 행 유지");
        Assert.That(plan.Moved, Has.Count.EqualTo(1), "수치 시트 칸은 빼도 된다(값은 경고·Export 백업에)");
        Assert.That(plan.Warnings, Has.Count.EqualTo(1));
        Assert.That(plan.Warnings[0], Does.Contain(Key));
    }

    [Test]
    public void UnreadableStringsHeader_IsError_NothingMoved()
    {
        var broken = new XlsxSheet("Strings", new[] { new[] { "키?", "한국어" }, new[] { "X", "y" } });

        TableTextMigration plan = TableTextMigration.Plan(new[] { Numeric("문구"), broken }, new[] { Description });

        Assert.That(plan.Ok, Is.False);
        Assert.That(plan.Rows, Is.Empty);
        Assert.That(plan.Moved, Is.Empty, "옮기지 못했으면 아무 칸도 빼지 않는다");
    }

    [Test]
    public void NoTextCells_IsNoOp_EvenWithUnreadableStrings()
    {
        var broken = new XlsxSheet("Strings", new[] { new[] { "키?" }, new[] { "X" } });
        var numbersOnly = new XlsxSheet("Paladin", new[]
        {
            new[] { DataTableSchema.VerticalFieldHeader, DataTableSchema.DescriptionHeader, "SkillA" },
            new[] { "cooldownTime", "", "8" },
        });

        TableTextMigration plan = TableTextMigration.Plan(new[] { numbersOnly, broken }, new[] { Description });

        Assert.That(plan.Ok, Is.True);
        Assert.That(plan.Rows, Is.Empty);
        Assert.That(plan.Moved, Is.Empty);
    }

    [Test]
    public void SameKeyFromTwoCells_FirstOnly_WarnOnlyWhenDifferent()
    {
        // 같은 컴포넌트 타입이 두 프리팹에 — 키가 같다(LocKey = 타입 이름).
        var first = new DataTableTextKeys.Slot("Player_A", "Player_A › Passive", "tooltip.description", "Passive/tooltip.description", "");
        var second = new DataTableTextKeys.Slot("Player_B", "Player_B › Passive", "tooltip.description", "Passive/tooltip.description", "");

        TableTextMigration same = TableTextMigration.Plan(
            new[] { Numeric("문구", "Paladin", "Player_A"), Numeric("문구", "Gunner", "Player_B") }, new[] { first, second });
        Assert.That(same.Rows, Has.Count.EqualTo(1));
        Assert.That(same.Warnings, Is.Empty);
        Assert.That(same.Moved, Has.Count.EqualTo(2));

        TableTextMigration different = TableTextMigration.Plan(
            new[] { Numeric("문구 A", "Paladin", "Player_A"), Numeric("문구 B", "Gunner", "Player_B") }, new[] { first, second });
        Assert.That(different.Rows.Single().Ko, Is.EqualTo("문구 A"));
        Assert.That(different.Warnings, Has.Count.EqualTo(1));
    }

    [Test]
    public void IgnoresStringsAndMemoSheets_AndUnknownTargets()
    {
        var memo = new XlsxSheet("#옛 값", new[]
        {
            new[] { DataTableSchema.VerticalFieldHeader, DataTableSchema.DescriptionHeader, "SkillA" },
            new[] { "tooltip.description", "", "메모 시트" },
        });

        TableTextMigration plan = TableTextMigration.Plan(new[] { memo, Numeric("다른 대상", "Paladin", "Gone") }, new[] { Description });

        Assert.That(plan.Rows, Is.Empty);
        Assert.That(plan.Moved, Is.Empty, "모르는 대상의 칸은 이관이 아니라 기존 병합 규칙(버림 보고)");
    }

    [Test]
    public void RunningTwice_GivesSameResult()
    {
        XlsxSheet numeric = Numeric("기획 문구");
        TableTextMigration firstRun = TableTextMigration.Plan(new[] { numeric, Strings() }, new[] { Description });
        XlsxSheet stringsAfter = Read(StringsSheet.Strings.BuildSheet(Strings(), firstRun.Rows, null));

        // 수치 시트 칸이 아직 남아 있어도(예: 쓰기 전 중단) 두 번째는 새 행·경고가 없다.
        TableTextMigration again = TableTextMigration.Plan(new[] { numeric, stringsAfter }, new[] { Description });
        Assert.That(again.Rows, Is.Empty);
        Assert.That(again.Warnings, Is.Empty);
        Assert.That(stringsAfter.Rows.Count(r => r[0] == Key), Is.EqualTo(1));

        // 칸을 뺀 뒤엔 할 일이 없다.
        var removed = new XlsxSheet("Paladin", numeric.Rows.Where(r => r[0] != "tooltip.description").ToArray());
        TableTextMigration third = TableTextMigration.Plan(new[] { removed, stringsAfter }, new[] { Description });
        Assert.That(third.Rows, Is.Empty);
        Assert.That(third.Moved, Is.Empty);
    }

    [Test]
    public void MigratedRow_BeatsInlineValue_InBuildSheet()
    {
        TableTextMigration plan = TableTextMigration.Plan(new[] { Numeric("기획 문구") }, new[] { Description });
        var inline = new StringKeyInfo(Key, "SkillA · tooltip.description", "인라인 문구");

        XlsxWriteSheet sheet = StringsSheet.Strings.BuildSheet(null, plan.Rows.Concat(new[] { inline }), null);

        Assert.That(sheet.Rows.Skip(1).Single()[2], Is.EqualTo("기획 문구"));
    }

    [Test]
    public void Merge_MovedCells_RemovedFromNumericSheet_NotReportedAsLost()
    {
        XlsxSheet old = Numeric("기획 문구");
        var fresh = new XlsxWriteSheet("Paladin", new List<object[]>
        {
            new object[] { DataTableSchema.VerticalFieldHeader, DataTableSchema.DescriptionHeader, "SkillA" },
            new object[] { "cooldownTime", "", 8L },
        }, frozenRows: 0, boldRows: 0);
        TableTextMigration plan = TableTextMigration.Plan(new[] { old }, new[] { Description });
        var report = new List<string>();

        List<XlsxWriteSheet> merged = DataTableMerge.Merge(new[] { old }, new[] { fresh }, report, null, plan.Moved);

        Assert.That(merged.Single().Rows.Any(r => r.Length > 0 && Equals(r[0], "tooltip.description")), Is.False);
        Assert.That(report, Has.None.Contains("버려졌다"), string.Join("\n", report));
        Assert.That(report, Has.Some.Contains("Strings 시트로 옮겨"));
    }
}

public sealed class StringOwnerFieldTests
{
    private readonly List<Object> created = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (Object o in created)
            Object.DestroyImmediate(o);
        created.Clear();
    }

    private Func<string, object> Owner()
    {
        var data = ScriptableObject.CreateInstance<LocalizedTextTestData>();
        data.name = "SkillA";
        created.Add(data);
        return id => id == "SkillA" ? data : null;
    }

    private static StringRow Row(string key, string ko, string en) => new StringRow(key, "", ko, en, "A2", "C2", "D2");

    private static List<DataTableIssue> Errors(DataTableIssues issues) =>
        issues.Items.Where(i => i.Kind == DataTableIssueKind.Error).ToList();

    [Test]
    public void ValidFields_InKoAndEn_Pass()
    {
        var issues = new DataTableIssues();
        List<LocalizedString> result = StringTableValidator.Validate(
            new[] { Row("SkillA/tooltip.description", "{count}초 {dmg} {count:0.0}", "{count}s {dmg} {count:0.0}") }, issues, Owner());

        Assert.That(issues.Items, Is.Empty, string.Join("\n", issues.Items));
        Assert.That(result, Has.Count.EqualTo(1));
    }

    [Test]
    public void UnknownField_IsError_InKoAndEnEach()
    {
        var issues = new DataTableIssues();
        List<LocalizedString> result = StringTableValidator.Validate(
            new[] { Row("SkillA/tooltip.description", "{nope}초", "{nope}s") }, issues, Owner());

        Assert.That(Errors(issues).Select(i => i.Location), Is.EquivalentTo(new[] { "C2", "D2" }));
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void BadFormat_InEn_IsError()
    {
        var issues = new DataTableIssues();
        StringTableValidator.Validate(new[] { Row("SkillA/tooltip.description", "{count:%}", "{count:%}") }, issues, Owner());
        Assert.That(Errors(issues), Is.Empty, "정수 필드 백분율은 된다 — 기준선");

        issues = new DataTableIssues();
        StringTableValidator.Validate(new[] { Row("SkillA/tooltip.subtitle", "{title:0.0}", "{title:0.0}") }, issues, Owner());
        Assert.That(Errors(issues).Select(i => i.Location), Is.EquivalentTo(new[] { "C2", "D2" }), "문자열 필드엔 숫자 형식을 못 쓴다");
    }

    [Test]
    public void EmptyEn_ChecksKoOnly()
    {
        var issues = new DataTableIssues();
        StringTableValidator.Validate(new[] { Row("SkillA/tooltip.description", "{nope}", "") }, issues, Owner());

        Assert.That(Errors(issues).Select(i => i.Location), Is.EqualTo(new[] { "C2" }));
    }

    [Test]
    public void MissingOwnerOrField_IsWarningOnly()
    {
        var issues = new DataTableIssues();
        List<LocalizedString> result = StringTableValidator.Validate(new[]
        {
            Row("Gone/tooltip.description", "{nope}", "{nope}"),
            Row("SkillA/tooltip.renamed", "{nope}", "{nope}"),
        }, issues, Owner());

        Assert.That(Errors(issues), Is.Empty);
        Assert.That(issues.Items.Count(i => i.Kind == DataTableIssueKind.Warning), Is.EqualTo(2));
        Assert.That(result, Has.Count.EqualTo(2), "고아 행은 표에 남는다(화면은 인라인 폴백)");
    }

    [Test]
    public void NonAutoKeys_AreNotOwnerChecked()
    {
        var issues = new DataTableIssues();
        StringTableValidator.Validate(new[] { Row("UI.Lobby.Waiting", "{n}명", "{n} waiting") }, issues,
            id => throw new AssertionException($"주인 조회 금지: {id}"));

        Assert.That(issues.Items, Is.Empty);
    }

    [Test]
    public void WithoutOwnerResolver_FieldsAreNotChecked()
    {
        var issues = new DataTableIssues();
        StringTableValidator.Validate(new[] { Row("SkillA/tooltip.description", "{nope}", "{nope}") }, issues);

        Assert.That(issues.Items, Is.Empty);
    }

    [Test]
    public void Slots_IncludeEmptyValues_CollectDoesNot()
    {
        var data = ScriptableObject.CreateInstance<LocalizedTextTestData>();
        data.name = "SkillA";
        created.Add(data);

        List<DataTableTextKeys.Slot> slots = DataTableTextKeys.Slots(new[] { ("SkillA", (Object)data) });

        Assert.That(slots.Select(s => s.Key), Does.Contain("SkillA/emptyText"));
        Assert.That(slots.Select(s => s.Key), Has.None.EqualTo("SkillA/codeId"), "Localize = false 는 수치 시트 쪽");
        Assert.That(DataTableTextKeys.Collect(new[] { ("SkillA", (Object)data) }, null).Select(k => k.Key),
            Has.None.EqualTo("SkillA/emptyText"));
    }

    [Test]
    public void LocKey_TrySplit()
    {
        Assert.That(LocKey.TrySplit("FirstMeleePassive/tooltip.description", out string owner, out string path), Is.True);
        Assert.That((owner, path), Is.EqualTo(("FirstMeleePassive", "tooltip.description")));
        Assert.That(LocKey.TrySplit("UI.Lobby.Ready", out _, out _), Is.False);
        Assert.That(LocKey.TrySplit("/x", out _, out _), Is.False);
        Assert.That(LocKey.TrySplit("x/", out _, out _), Is.False);
        Assert.That(LocKey.TrySplit(null, out _, out _), Is.False);
    }
}
