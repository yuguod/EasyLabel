using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace EasyLabel.Core
{
    /// <summary>
    /// 极简 xlsx 导出：按 OpenXML 结构直接打包一个工作表，不依赖 Excel / Office / 第三方库。
    /// 只支持“表头 + 纯文本单元格 + 列宽”，足够导出条码清单这类一维数据。
    /// </summary>
    public static class XlsxWriter
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        /// <summary>写一个工作表。headers 非空时首行加粗，用于表头。</summary>
        public static void WriteSheet(string path, string sheetName, IList<string> headers,
            IEnumerable<string[]> rows, int[] columnWidths = null)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException("path");

            var sheetRows = new List<string[]>();
            if (headers != null && headers.Count > 0)
            {
                var head = new string[headers.Count];
                for (int i = 0; i < headers.Count; i++) head[i] = headers[i];
                sheetRows.Add(head);
            }
            if (rows != null)
            {
                foreach (string[] row in rows)
                {
                    if (row != null) sheetRows.Add(row);
                }
            }

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string name = SafeSheetName(sheetName);
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                Add(zip, "[Content_Types].xml", ContentTypes);
                Add(zip, "_rels/.rels", RootRels);
                Add(zip, "xl/workbook.xml", WorkbookXml(name));
                Add(zip, "xl/_rels/workbook.xml.rels", WorkbookRels);
                Add(zip, "xl/styles.xml", StylesXml);
                Add(zip, "xl/worksheets/sheet1.xml", SheetXml(sheetRows, columnWidths, headers != null && headers.Count > 0));
            }
        }

        /// <summary>一列“序号 + 内容”的常用形态，列宽已调好。</summary>
        public static void WriteList(string path, string sheetName, string header, IList<string> values)
        {
            var rows = new List<string[]>(values == null ? 0 : values.Count);
            if (values != null)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    rows.Add(new[] { (i + 1).ToString(CultureInfo.InvariantCulture), values[i] ?? "" });
                }
            }
            WriteSheet(path, sheetName, new[] { "序号", header }, rows, new[] { 8, 36 });
        }

        // ---------------------------------------------------------------- 包内部件

        private const string ContentTypes =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
            "</Types>";

        private const string RootRels =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>";

        private const string WorkbookRels =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
            "</Relationships>";

        private const string StylesXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
            "<fonts count=\"2\">" +
            "<font><sz val=\"11\"/><name val=\"等线\"/><family val=\"2\"/></font>" +
            "<font><b/><sz val=\"11\"/><name val=\"等线\"/><family val=\"2\"/></font>" +
            "</fonts>" +
            "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
            "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
            "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
            "<cellXfs count=\"2\">" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
            "</cellXfs>" +
            "<cellStyles count=\"1\"><cellStyle name=\"常规\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
            "</styleSheet>";

        private static string WorkbookXml(string sheetName)
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
                   "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                   "<sheets><sheet name=\"" + Escape(sheetName) + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
                   "</workbook>";
        }

        private static string SheetXml(List<string[]> rows, int[] widths, bool hasHeader)
        {
            var sb = new StringBuilder(4096);
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");

            if (widths != null && widths.Length > 0)
            {
                sb.Append("<cols>");
                for (int i = 0; i < widths.Length; i++)
                {
                    sb.Append("<col min=\"").Append(i + 1).Append("\" max=\"").Append(i + 1)
                      .Append("\" width=\"").Append(widths[i].ToString(CultureInfo.InvariantCulture))
                      .Append("\" customWidth=\"1\"/>");
                }
                sb.Append("</cols>");
            }

            sb.Append("<sheetData>");
            for (int r = 0; r < rows.Count; r++)
            {
                string[] cells = rows[r];
                sb.Append("<row r=\"").Append(r + 1).Append("\">");
                for (int c = 0; c < cells.Length; c++)
                {
                    sb.Append("<c r=\"").Append(CellRef(c, r + 1)).Append("\"");
                    if (hasHeader && r == 0) sb.Append(" s=\"1\"");
                    sb.Append(" t=\"inlineStr\"><is><t xml:space=\"preserve\">");
                    sb.Append(Escape(cells[c]));
                    sb.Append("</t></is></c>");
                }
                sb.Append("</row>");
            }
            sb.Append("</sheetData></worksheet>");
            return sb.ToString();
        }

        private static void Add(ZipArchive zip, string entryName, string content)
        {
            ZipArchiveEntry entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using (Stream stream = entry.Open())
            using (var writer = new StreamWriter(stream, Utf8))
            {
                writer.Write(content);
            }
        }

        // ---------------------------------------------------------------- 工具

        /// <summary>列号转 Excel 列名：0 → A，25 → Z，26 → AA。</summary>
        private static string CellRef(int columnIndex, int rowNumber)
        {
            var sb = new StringBuilder(4);
            int value = columnIndex;
            while (true)
            {
                sb.Insert(0, (char)('A' + value % 26));
                value = value / 26 - 1;
                if (value < 0) break;
            }
            sb.Append(rowNumber.ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        private static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length + 8);
            foreach (char ch in text)
            {
                switch (ch)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '\'': sb.Append("&apos;"); break;
                    default:
                        // XML 1.0 不接受的字符直接丢掉，避免 Excel 打不开。
                        if (ch < 0x20 && ch != '\t' && ch != '\n' && ch != '\r') break;
                        sb.Append(ch);
                        break;
                }
            }
            return sb.ToString();
        }

        private static string SafeSheetName(string name)
        {
            string value = (name ?? "").Trim();
            if (value.Length == 0) value = "Sheet1";
            foreach (char bad in new[] { '\\', '/', '*', '?', ':', '[', ']' })
            {
                value = value.Replace(bad, '_');
            }
            if (value.Length > 31) value = value.Substring(0, 31);
            return value;
        }
    }
}
