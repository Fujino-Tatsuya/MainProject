using System.Collections.Generic;
using NUnit.Framework;

public sealed class XlsxReaderTests
{
    [Test]
    public void Read_SheetsInOrder_WithSharedStringsAndNumbers()
    {
        using var xlsx = TestXlsx.Build(
            ("MonsterDataSO", new[]
            {
                new[] { "Id", "maxHp", "moveSpeed" },
                new[] { "설명", "최대 체력", "속도" },
                new[] { "GauntletBotData", "300", "2.5" },
            }),
            ("#메모", new[] { new[] { "아무거나" } }));

        IReadOnlyList<XlsxSheet> sheets = XlsxReader.Read(xlsx);

        Assert.That(sheets, Has.Count.EqualTo(2));
        Assert.That(sheets[0].Name, Is.EqualTo("MonsterDataSO"));
        Assert.That(sheets[0].Cell(2, 0), Is.EqualTo("GauntletBotData"));
        Assert.That(sheets[0].Cell(2, 1), Is.EqualTo("300"));
        Assert.That(sheets[0].Cell(2, 2), Is.EqualTo("2.5"));
        Assert.That(sheets[0].Cell(1, 1), Is.EqualTo("최대 체력"));
        Assert.That(sheets[1].Name, Is.EqualTo("#메모"));
    }

    [Test]
    public void Read_MissingCellsAndRows_AreEmptyStrings()
    {
        // B1 없음(A1 → C1), 2행 통째로 없음(1행 → 3행).
        using var xlsx = TestXlsx.BuildRaw(
            "<row r=\"1\"><c r=\"A1\"><v>1</v></c><c r=\"C1\"><v>3</v></c></row>" +
            "<row r=\"3\"><c r=\"B3\"><v>7</v></c></row>");

        XlsxSheet sheet = XlsxReader.Read(xlsx)[0];

        Assert.That(sheet.Cell(0, 0), Is.EqualTo("1"));
        Assert.That(sheet.Cell(0, 1), Is.EqualTo(string.Empty));
        Assert.That(sheet.Cell(0, 2), Is.EqualTo("3"));
        Assert.That(sheet.Cell(1, 0), Is.EqualTo(string.Empty));
        Assert.That(sheet.Cell(2, 1), Is.EqualTo("7"));
        Assert.That(sheet.Cell(99, 99), Is.EqualTo(string.Empty));
    }

    [Test]
    public void Read_InlineStringBooleanFormulaAndRichText()
    {
        using var xlsx = TestXlsx.BuildRaw(
            "<row r=\"1\">" +
            "<c r=\"A1\" t=\"inlineStr\"><is><t>인라인</t></is></c>" +
            "<c r=\"B1\" t=\"b\"><v>1</v></c>" +
            "<c r=\"C1\"><f>A2*2</f><v>8</v></c>" +       // 수식 → 저장된 결과값
            "<c r=\"D1\" t=\"s\"><v>0</v></c>" +
            "<c r=\"E1\" t=\"s\"><v>1</v></c>" +
            "</row>",
            "<si><r><t>굵</t></r><r><t>은글자</t></r></si>" +   // 서식 run 두 개
            "<si><t>漢字</t><rPh><t>かんじ</t></rPh></si>");     // 발음 표기는 뺀다

        XlsxSheet sheet = XlsxReader.Read(xlsx)[0];

        Assert.That(sheet.Cell(0, 0), Is.EqualTo("인라인"));
        Assert.That(sheet.Cell(0, 1), Is.EqualTo("TRUE"));
        Assert.That(sheet.Cell(0, 2), Is.EqualTo("8"));
        Assert.That(sheet.Cell(0, 3), Is.EqualTo("굵은글자"));
        Assert.That(sheet.Cell(0, 4), Is.EqualTo("漢字"));
    }

    [Test]
    public void Read_ConsecutiveSharedStrings_NoneSkipped()
    {
        using var xlsx = TestXlsx.BuildRaw(
            "<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\" t=\"s\"><v>1</v></c><c r=\"C1\" t=\"s\"><v>2</v></c></row>",
            "<si><t>a</t></si><si><t>b</t></si><si><t>c</t></si>");

        XlsxSheet sheet = XlsxReader.Read(xlsx)[0];

        Assert.That(new[] { sheet.Cell(0, 0), sheet.Cell(0, 1), sheet.Cell(0, 2) }, Is.EqualTo(new[] { "a", "b", "c" }));
    }

    [TestCase(0, 0, "A1")]
    [TestCase(2, 25, "Z3")]
    [TestCase(0, 26, "AA1")]
    [TestCase(9, 54, "BC10")]
    public void Address_IsExcelNotation(int row, int column, string expected)
    {
        Assert.That(XlsxSheet.Address(row, column), Is.EqualTo(expected));
    }
}
