using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using NUnit.Framework;

public sealed class XlsxWriterTests
{
    [Test]
    public void Write_ThenRead_RoundTripsCellText()
    {
        var stream = new MemoryStream();
        XlsxWriter.Write(stream, new[]
        {
            new XlsxWriteSheet("First", new List<object[]>
            {
                new object[] { "Id", "hp", "speed", "flag", "note" },
                new object[] { "Goblin", 300L, 0.1f, true, null },
                new object[] { "한글 & <특수>", -5, 2.5d, false, "끝" },
            }),
            new XlsxWriteSheet("Second", new List<object[]> { new object[] { "x" } }),
        });

        stream.Position = 0;
        IReadOnlyList<XlsxSheet> sheets = XlsxReader.Read(stream);

        Assert.That(sheets[0].Name, Is.EqualTo("First"));
        Assert.That(sheets[1].Name, Is.EqualTo("Second"));
        Assert.That(sheets[0].Rows[1], Is.EqualTo(new[] { "Goblin", "300", "0.1", "TRUE" }));
        Assert.That(sheets[0].Rows[2], Is.EqualTo(new[] { "한글 & <특수>", "-5", "2.5", "FALSE", "끝" }));
    }

    [Test]
    public void Write_HasPartsExcelRequires()
    {
        var stream = new MemoryStream();
        XlsxWriter.Write(stream, new[] { new XlsxWriteSheet("S", new List<object[]> { new object[] { 1 } }) });

        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (string part in new[] { "[Content_Types].xml", "_rels/.rels", "xl/workbook.xml", "xl/_rels/workbook.xml.rels", "xl/styles.xml", "xl/worksheets/sheet1.xml" })
        {
            Assert.That(zip.GetEntry(part), Is.Not.Null, part);
        }
    }

    [TestCase("PlayerDashData", true)]
    [TestCase("ThisSheetNameIsLongerThanThirtyOne", false)] // 34자
    [TestCase("Bad/Name", false)]
    [TestCase("", false)]
    public void ValidateSheetName_FollowsExcelRules(string name, bool valid)
    {
        Assert.That(XlsxWriter.ValidateSheetName(name) == null, Is.EqualTo(valid));
    }
}
