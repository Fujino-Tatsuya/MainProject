using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;

/// <summary>쓸 시트 하나. 셀 = string(글자) · int/long/float/double(숫자) · bool · null(빈 칸).</summary>
public sealed class XlsxWriteSheet
{
    public XlsxWriteSheet(string name, List<object[]> rows, int frozenRows = 1, int boldRows = 1)
    {
        Name = name;
        Rows = rows;
        FrozenRows = frozenRows;
        BoldRows = boldRows;
    }

    public string Name { get; }
    public List<object[]> Rows { get; }

    /// <summary>스크롤해도 고정되는 위쪽 행 수(머리글).</summary>
    public int FrozenRows { get; }

    /// <summary>굵게 칠할 위쪽 행 수.</summary>
    public int BoldRows { get; }
}

/// <summary>
/// 의존성 없는 최소 xlsx 작성기 — Export Template 용. Excel 이 열 수 있는 최소 파트만 쓴다
/// (공유 문자열 대신 인라인 문자열, 머리글 굵게·고정, 열 너비). Excel 에서 한 번 저장하면 정식 형태로 바뀐다.
/// 실수는 "R" 서식으로 써서 float 0.1 이 0.100000001… 로 번지지 않는다 → 다시 읽으면 같은 float.
/// </summary>
public static class XlsxWriter
{
    public const int MaxSheetNameLength = 31;
    private static readonly char[] InvalidSheetNameChars = { '[', ']', ':', '*', '?', '/', '\\' };

    /// <summary>Excel 시트 이름 규칙(31자, 금지 문자) 위반이면 이유, 아니면 null.</summary>
    public static string ValidateSheetName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "시트 이름이 비어 있다.";
        }

        if (name.Length > MaxSheetNameLength)
        {
            return $"시트 이름 '{name}' 이 Excel 한도 {MaxSheetNameLength}자를 넘는다({name.Length}자).";
        }

        return name.IndexOfAny(InvalidSheetNameChars) >= 0 ? $"시트 이름 '{name}' 에 쓸 수 없는 문자([]:*?/\\)가 있다." : null;
    }

    public static void Write(string path, IReadOnlyList<XlsxWriteSheet> sheets)
    {
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        Write(file, sheets);
    }

    public static void Write(Stream stream, IReadOnlyList<XlsxWriteSheet> sheets)
    {
        foreach (XlsxWriteSheet sheet in sheets)
        {
            string error = ValidateSheetName(sheet.Name);
            if (error != null)
            {
                throw new ArgumentException(error);
            }
        }

        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);

        var types = new StringBuilder(
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
        var workbook = new StringBuilder(
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
            "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
        var rels = new StringBuilder(
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");

        for (int i = 0; i < sheets.Count; i++)
        {
            int id = i + 1;
            types.Append($"<Override PartName=\"/xl/worksheets/sheet{id}.xml\" " +
                         "ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            workbook.Append($"<sheet name=\"{Escape(sheets[i].Name)}\" sheetId=\"{id}\" r:id=\"rId{id}\"/>");
            rels.Append($"<Relationship Id=\"rId{id}\" " +
                        "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" " +
                        $"Target=\"worksheets/sheet{id}.xml\"/>");
            WriteEntry(zip, $"xl/worksheets/sheet{id}.xml", SheetXml(sheets[i]));
        }

        int stylesId = sheets.Count + 1;
        rels.Append($"<Relationship Id=\"rId{stylesId}\" " +
                    "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");

        WriteEntry(zip, "[Content_Types].xml", types.Append("</Types>").ToString());
        WriteEntry(zip, "_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" " +
            "Target=\"xl/workbook.xml\"/></Relationships>");
        WriteEntry(zip, "xl/workbook.xml", workbook.Append("</sheets></workbook>").ToString());
        WriteEntry(zip, "xl/_rels/workbook.xml.rels", rels.Append("</Relationships>").ToString());
        WriteEntry(zip, "xl/styles.xml", StylesXml);
    }

    // 스타일 0 = 기본, 1 = 굵게. Excel 이 요구하는 최소 구성(fill 2개는 필수).
    private const string StylesXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
        "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"맑은 고딕\"/></font>" +
        "<font><b/><sz val=\"11\"/><name val=\"맑은 고딕\"/></font></fonts>" +
        "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
        "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
        "<cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
        "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/></cellXfs>" +
        "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
        "</styleSheet>";

    private static string SheetXml(XlsxWriteSheet sheet)
    {
        var xml = new StringBuilder(
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");

        // 순서가 스키마로 정해져 있다: sheetViews → cols → sheetData.
        xml.Append("<sheetViews><sheetView workbookViewId=\"0\">");
        if (sheet.FrozenRows > 0)
        {
            string topLeft = XlsxSheet.Address(sheet.FrozenRows, 0);
            xml.Append($"<pane ySplit=\"{sheet.FrozenRows}\" topLeftCell=\"{topLeft}\" activePane=\"bottomLeft\" state=\"frozen\"/>");
        }

        xml.Append("</sheetView></sheetViews>");

        int width = sheet.Rows.Count == 0 ? 0 : sheet.Rows.Max(r => r.Length);
        if (width > 0)
        {
            xml.Append("<cols>");
            for (int c = 0; c < width; c++)
            {
                int chars = sheet.Rows.Where(r => c < r.Length).Select(r => DisplayLength(Text(r[c]))).DefaultIfEmpty(0).Max();
                double columnWidth = Math.Min(60, Math.Max(8, chars + 2));
                xml.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{columnWidth.ToString("0.#", CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
            }

            xml.Append("</cols>");
        }

        xml.Append("<sheetData>");
        for (int r = 0; r < sheet.Rows.Count; r++)
        {
            xml.Append($"<row r=\"{r + 1}\">");
            object[] cells = sheet.Rows[r];
            string style = r < sheet.BoldRows ? " s=\"1\"" : string.Empty;
            for (int c = 0; c < cells.Length; c++)
            {
                AppendCell(xml, XlsxSheet.Address(r, c), cells[c], style);
            }

            xml.Append("</row>");
        }

        return xml.Append("</sheetData></worksheet>").ToString();
    }

    private static void AppendCell(StringBuilder xml, string address, object value, string style)
    {
        switch (value)
        {
            case null:
                return;
            case bool b:
                xml.Append($"<c r=\"{address}\"{style} t=\"b\"><v>{(b ? 1 : 0)}</v></c>");
                return;
            case string s:
                if (s.Length > 0)
                {
                    xml.Append($"<c r=\"{address}\"{style} t=\"inlineStr\"><is><t xml:space=\"preserve\">{Escape(s)}</t></is></c>");
                }

                return;
            default:
                xml.Append($"<c r=\"{address}\"{style}><v>{Number(value)}</v></c>");
                return;
        }
    }

    private static string Number(object value)
    {
        switch (value)
        {
            case float f: return f.ToString("R", CultureInfo.InvariantCulture);
            case double d: return d.ToString("R", CultureInfo.InvariantCulture);
            case IFormattable formattable: return formattable.ToString(null, CultureInfo.InvariantCulture);
            default: throw new ArgumentException($"xlsx 셀로 쓸 수 없는 값: {value.GetType().Name}");
        }
    }

    private static string Text(object value) => value switch
    {
        null => string.Empty,
        string s => s,
        bool b => b ? "TRUE" : "FALSE",
        _ => Number(value),
    };

    // 한글은 영문 두 칸 정도 차지한다.
    private static int DisplayLength(string text) => text.Sum(ch => ch > 0x2E80 ? 2 : 1);

    private static void WriteEntry(ZipArchive zip, string path, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(path, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string Escape(string text) => SecurityElement.Escape(text);
}
