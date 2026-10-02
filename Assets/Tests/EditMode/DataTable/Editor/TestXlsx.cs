using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;

// 테스트용 최소 xlsx 생성기. Excel 이 쓰는 형태를 흉내 낸다: 숫자는 <v>, 글자는 공유 문자열(t="s"), 셀마다 r="B3" 참조.
// 바이너리 픽스처를 레포에 두지 않으려고 코드로 만든다.
internal static class TestXlsx
{
    public static MemoryStream Build(params (string name, string[][] rows)[] sheets)
    {
        var shared = new List<string>();
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var workbook = new StringBuilder(
                "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
                "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
            var rels = new StringBuilder(
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");

            for (int i = 0; i < sheets.Length; i++)
            {
                workbook.Append($"<sheet name=\"{Escape(sheets[i].name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
                rels.Append($"<Relationship Id=\"rId{i + 1}\" Type=\"worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>");
                Write(zip, $"xl/worksheets/sheet{i + 1}.xml", SheetXml(sheets[i].rows, shared));
            }

            workbook.Append("</sheets></workbook>");
            rels.Append("</Relationships>");
            Write(zip, "xl/workbook.xml", workbook.ToString());
            Write(zip, "xl/_rels/workbook.xml.rels", rels.ToString());
            Write(zip, "xl/sharedStrings.xml",
                "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                string.Concat(shared.Select(s => $"<si><t>{Escape(s)}</t></si>")) + "</sst>");
        }

        stream.Position = 0;
        return stream;
    }

    /// <summary>xml 을 직접 넣는 버전 — 리더의 특이 케이스(인라인 문자열·rPh·빠진 행) 검증용.</summary>
    public static MemoryStream BuildRaw(string sheetDataXml, string sharedStringsXml = null)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "xl/workbook.xml",
                "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
                "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                "<sheets><sheet name=\"S\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Write(zip, "xl/_rels/workbook.xml.rels",
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"worksheet\" Target=\"/xl/worksheets/s.xml\"/></Relationships>");
            Write(zip, "xl/worksheets/s.xml",
                "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>" +
                sheetDataXml + "</sheetData></worksheet>");
            if (sharedStringsXml != null)
            {
                Write(zip, "xl/sharedStrings.xml",
                    "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" + sharedStringsXml + "</sst>");
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static string SheetXml(string[][] rows, List<string> shared)
    {
        var xml = new StringBuilder("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        for (int r = 0; r < rows.Length; r++)
        {
            xml.Append($"<row r=\"{r + 1}\">");
            for (int c = 0; c < rows[r].Length; c++)
            {
                string value = rows[r][c];
                if (string.IsNullOrEmpty(value))
                {
                    continue; // Excel 은 빈 셀을 쓰지 않는다.
                }

                string address = XlsxSheet.Address(r, c);
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    xml.Append($"<c r=\"{address}\"><v>{value}</v></c>");
                }
                else
                {
                    int index = shared.IndexOf(value);
                    if (index < 0)
                    {
                        index = shared.Count;
                        shared.Add(value);
                    }

                    xml.Append($"<c r=\"{address}\" t=\"s\"><v>{index}</v></c>");
                }
            }

            xml.Append("</row>");
        }

        return xml.Append("</sheetData></worksheet>").ToString();
    }

    private static void Write(ZipArchive zip, string path, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string Escape(string text) => SecurityElement.Escape(text);
}
