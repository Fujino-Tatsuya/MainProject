using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

/// <summary>xlsx 시트 하나 — 셀 텍스트 격자. 비어 있는 셀은 빈 문자열.</summary>
public sealed class XlsxSheet
{
    public XlsxSheet(string name, IReadOnlyList<string[]> rows)
    {
        Name = name;
        Rows = rows;
    }

    public string Name { get; }

    /// <summary>행 → 열. 행마다 길이가 다를 수 있다(뒤쪽 빈 셀은 없을 수 있음). <see cref="Cell"/> 로 읽을 것.</summary>
    public IReadOnlyList<string[]> Rows { get; }

    public string Cell(int row, int column)
    {
        if (row < 0 || row >= Rows.Count)
        {
            return string.Empty;
        }

        string[] cells = Rows[row];
        return column >= 0 && column < cells.Length ? cells[column] ?? string.Empty : string.Empty;
    }

    /// <summary>0 기반 (행, 열) → Excel 표기 "B3".</summary>
    public static string Address(int row, int column)
    {
        string letters = string.Empty;
        for (int c = column + 1; c > 0; c = (c - 1) / 26)
        {
            letters = (char)('A' + (c - 1) % 26) + letters;
        }

        return letters + (row + 1);
    }
}

/// <summary>
/// 의존성 없는 최소 xlsx 리더. xlsx = zip 안의 XML 이라 <see cref="ZipArchive"/> + <see cref="XDocument"/> 로 충분하다
/// (수치 테이블은 작아서 스트리밍이 필요 없다).
/// 읽는 것: 공유 문자열·인라인 문자열·숫자·불리언·<b>수식은 Excel 이 저장해 둔 결과값</b>.
/// 날짜·서식·병합 셀은 해석하지 않는다. 숫자는 xlsx 안에 서식 없이 저장돼 있어 로케일 문제가 없다.
/// 파일은 FileShare.ReadWrite 로 열어 Excel 이 열고 있어도 읽힌다.
/// </summary>
public static class XlsxReader
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static IReadOnlyList<XlsxSheet> Read(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Read(file);
    }

    public static IReadOnlyList<XlsxSheet> Read(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        XDocument workbook = LoadPart(zip, "xl/workbook.xml")
            ?? throw new InvalidDataException("xlsx 가 아니다: xl/workbook.xml 이 없다.");
        List<string> sharedStrings = ReadSharedStrings(zip);
        Dictionary<string, string> targets = ReadWorkbookRelationships(zip);

        var sheets = new List<XlsxSheet>();
        foreach (XElement sheet in workbook.Descendants(Main + "sheet"))
        {
            string name = (string)sheet.Attribute("name");
            string relationId = (string)sheet.Attribute(Rel + "id");
            if (relationId == null || !targets.TryGetValue(relationId, out string target))
            {
                throw new InvalidDataException($"xlsx 시트 '{name}' 의 관계({relationId})를 찾지 못했다.");
            }

            XDocument part = LoadPart(zip, ResolvePartPath(target))
                ?? throw new InvalidDataException($"xlsx 시트 '{name}' 의 파트({target})가 없다.");
            sheets.Add(new XlsxSheet(name, ReadRows(part, sharedStrings)));
        }

        return sheets;
    }

    private static XDocument LoadPart(ZipArchive zip, string path)
    {
        ZipArchiveEntry entry = zip.GetEntry(path);
        if (entry == null)
        {
            return null;
        }

        using Stream stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static Dictionary<string, string> ReadWorkbookRelationships(ZipArchive zip)
    {
        XDocument rels = LoadPart(zip, "xl/_rels/workbook.xml.rels");
        if (rels == null)
        {
            return new Dictionary<string, string>();
        }

        return rels.Descendants(PackageRel + "Relationship")
            .ToDictionary(r => (string)r.Attribute("Id"), r => (string)r.Attribute("Target"), StringComparer.Ordinal);
    }

    // 관계 Target 은 보통 "worksheets/sheet1.xml"(xl/ 기준)이고, 가끔 "/xl/worksheets/sheet1.xml"(패키지 루트 기준)이다.
    private static string ResolvePartPath(string target)
    {
        return target.StartsWith("/", StringComparison.Ordinal) ? target.Substring(1) : "xl/" + target;
    }

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        XDocument doc = LoadPart(zip, "xl/sharedStrings.xml");
        return doc == null
            ? new List<string>()
            : doc.Root.Elements(Main + "si").Select(JoinTextRuns).ToList();
    }

    /// <summary>si/is 안의 &lt;t&gt; 를 이어 붙인다 — 서식 있는 문자열은 run(r) 여러 개로 쪼개진다. 발음 표기(rPh)는 뺀다.</summary>
    private static string JoinTextRuns(XElement container)
    {
        var text = new StringBuilder();
        foreach (XElement t in container.Descendants(Main + "t"))
        {
            if (t.Ancestors(Main + "rPh").Any())
            {
                continue;
            }

            text.Append(t.Value);
        }

        return text.ToString();
    }

    private static List<string[]> ReadRows(XDocument sheet, List<string> sharedStrings)
    {
        var rows = new List<string[]>();
        XElement data = sheet.Root.Element(Main + "sheetData");
        if (data == null)
        {
            return rows;
        }

        foreach (XElement row in data.Elements(Main + "row"))
        {
            // r 속성이 있으면 그 행 번호, 없으면 이전 행 다음. 사이에 빠진 행은 빈 행으로 채운다.
            int rowIndex = ParseRowNumber((string)row.Attribute("r"), rows.Count);
            while (rows.Count < rowIndex)
            {
                rows.Add(Array.Empty<string>());
            }

            var cells = new List<string>();
            foreach (XElement cell in row.Elements(Main + "c"))
            {
                int column = ParseColumn((string)cell.Attribute("r"), cells.Count);
                while (cells.Count < column)
                {
                    cells.Add(string.Empty);
                }

                cells.Add(ReadCellValue(cell, sharedStrings));
            }

            rows.Add(cells.ToArray());
        }

        return rows;
    }

    private static string ReadCellValue(XElement cell, List<string> sharedStrings)
    {
        string raw = (string)cell.Element(Main + "v");
        switch ((string)cell.Attribute("t"))
        {
            case "s":
                return int.TryParse(raw, out int index) && index >= 0 && index < sharedStrings.Count
                    ? sharedStrings[index]
                    : throw new InvalidDataException($"xlsx 공유 문자열 인덱스가 잘못됐다: {raw}");
            case "inlineStr":
                XElement inline = cell.Element(Main + "is");
                return inline == null ? string.Empty : JoinTextRuns(inline);
            case "b":
                return raw == "1" ? "TRUE" : "FALSE";
            default:
                // 숫자(t 없음 / "n") · 수식 문자열 결과("str") · 오류("e", 예: #DIV/0!) — 저장된 텍스트 그대로.
                return raw ?? string.Empty;
        }
    }

    private static int ParseRowNumber(string reference, int fallback)
    {
        return int.TryParse(reference, out int number) && number > 0 ? number - 1 : fallback;
    }

    /// <summary>"BC12" → 54(0 기반). 참조가 없으면 fallback.</summary>
    private static int ParseColumn(string reference, int fallback)
    {
        if (string.IsNullOrEmpty(reference) || !char.IsLetter(reference[0]))
        {
            return fallback;
        }

        int column = 0;
        foreach (char ch in reference.TakeWhile(char.IsLetter))
        {
            column = column * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }

        return column - 1;
    }
}
