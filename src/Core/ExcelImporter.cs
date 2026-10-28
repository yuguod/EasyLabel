using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ExcelDataReader;
using Microsoft.VisualBasic.FileIO;

namespace EasyLabel.Core
{
    public sealed class SheetData
    {
        public string Name { get; set; }
        public List<string[]> Rows { get; private set; }
        public int ColumnCount { get; set; }
        public bool Truncated { get; set; }

        public SheetData(string name)
        {
            Name = name ?? "";
            Rows = new List<string[]>();
        }

        public string[] Header
        {
            get { return Rows.Count > 0 ? Rows[0] : new string[0]; }
        }

        public string ColumnName(int index, bool hasHeader)
        {
            if (hasHeader && Rows.Count > 0 && index < Rows[0].Length)
            {
                string v = (Rows[0][index] ?? "").Trim();
                if (v.Length > 0) return v;
            }
            return "第 " + (index + 1) + " 列";
        }
    }

    public sealed class WorkbookData
    {
        public string FilePath { get; set; }
        public bool IsCsv { get; set; }
        public List<SheetData> Sheets { get; private set; }

        public WorkbookData()
        {
            Sheets = new List<SheetData>();
        }
    }

    public static class ExcelImporter
    {
        public const int MaxRowsPerSheet = 50000;

        public static WorkbookData LoadWorkbook(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("文件路径为空");
            if (!File.Exists(path)) throw new FileNotFoundException("找不到文件", path);

            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".csv")
            {
                var wb = new WorkbookData { FilePath = path, IsCsv = true };
                wb.Sheets.Add(LoadCsv(path));
                return wb;
            }

            if (ext != ".xls" && ext != ".xlsx" && ext != ".xlsm")
                throw new NotSupportedException("仅支持 .xlsx、.xls、.xlsm 或 .csv 文件");

            var workbook = new WorkbookData { FilePath = path, IsCsv = false };
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = ExcelReaderFactory.CreateReader(stream, new ExcelReaderConfiguration
            {
                FallbackEncoding = Encoding.GetEncoding(936),
                LeaveOpen = false
            }))
            {
                do
                {
                    var sheet = new SheetData(reader.Name);
                    int colCount = Math.Max(0, reader.FieldCount);
                    while (reader.Read())
                    {
                        if (sheet.Rows.Count >= MaxRowsPerSheet)
                        {
                            sheet.Truncated = true;
                            break;
                        }
                        int count = Math.Max(colCount, reader.FieldCount);
                        var row = new string[count];
                        for (int c = 0; c < count; c++)
                        {
                            row[c] = c < reader.FieldCount ? GetCellText(reader, c) : "";
                        }
                        colCount = Math.Max(colCount, count);
                        if (row.Any(v => !string.IsNullOrWhiteSpace(v)))
                            sheet.Rows.Add(row);
                    }
                    sheet.ColumnCount = colCount;
                    workbook.Sheets.Add(sheet);
                } while (reader.NextResult());
            }

            if (workbook.Sheets.Count == 0)
                workbook.Sheets.Add(new SheetData("Sheet1"));
            return workbook;
        }

        private static SheetData LoadCsv(string path)
        {
            string text = ReadTextWithEncoding(path);
            char delimiter = DetectDelimiter(text);
            var sheet = new SheetData("CSV");
            int maxCols = 0;
            using (var parser = new TextFieldParser(new StringReader(text)))
            {
                parser.TextFieldType = FieldType.Delimited;
                parser.SetDelimiters(delimiter.ToString());
                parser.HasFieldsEnclosedInQuotes = true;
                parser.TrimWhiteSpace = true;
                while (!parser.EndOfData)
                {
                    if (sheet.Rows.Count >= MaxRowsPerSheet)
                    {
                        sheet.Truncated = true;
                        break;
                    }
                    string[] fields;
                    try { fields = parser.ReadFields(); }
                    catch (MalformedLineException ex)
                    {
                        throw new InvalidDataException("CSV 第 " + ex.LineNumber + " 行格式错误：" + ex.Message, ex);
                    }
                    if (fields == null) continue;
                    maxCols = Math.Max(maxCols, fields.Length);
                    if (fields.Any(v => !string.IsNullOrWhiteSpace(v)))
                        sheet.Rows.Add(fields);
                }
            }
            sheet.ColumnCount = maxCols;
            return sheet;
        }

        private static string ReadTextWithEncoding(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return Encoding.GetEncoding(936).GetString(bytes);
            }
        }

        private static char DetectDelimiter(string text)
        {
            string first = "";
            using (var sr = new StringReader(text ?? ""))
            {
                for (int i = 0; i < 5; i++)
                {
                    string line = sr.ReadLine();
                    if (line == null) break;
                    first += line + "\n";
                }
            }
            int comma = CountOutsideQuotes(first, ',');
            int tab = CountOutsideQuotes(first, '\t');
            int semi = CountOutsideQuotes(first, ';');
            if (tab > comma && tab >= semi) return '\t';
            if (semi > comma && semi > tab) return ';';
            return ',';
        }

        private static int CountOutsideQuotes(string text, char c)
        {
            int count = 0;
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '"')
                {
                    if (quoted && i + 1 < text.Length && text[i + 1] == '"') i++;
                    else quoted = !quoted;
                }
                else if (!quoted && ch == c) count++;
            }
            return count;
        }

        private static string GetCellText(IExcelDataReader reader, int column)
        {
            object value;
            try { value = reader.GetValue(column); }
            catch { return ""; }

            if (value == null) return "";
            if (value is DateTime)
            {
                DateTime dt = (DateTime)value;
                return dt.TimeOfDay.Ticks == 0
                    ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
            if (value is bool) return ((bool)value) ? "TRUE" : "FALSE";
            if (value is double)
            {
                double d = (double)value;
                string format = null;
                try { format = reader.GetNumberFormatString(column); } catch { }
                string padded;
                if (TryFormatZeroPattern(d, format, out padded)) return padded;
                if (Math.Abs(d - Math.Round(d)) < 0.0000001 && Math.Abs(d) < 1e15)
                    return d.ToString("0", CultureInfo.InvariantCulture);
                return d.ToString("0.############", CultureInfo.InvariantCulture);
            }
            if (value is float)
                return ((float)value).ToString("0.######", CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.CurrentCulture).Trim();
        }

        /// <summary>识别 Excel 的 000000 这类纯零格式，保留前导零。</summary>
        private static bool TryFormatZeroPattern(double value, string format, out string result)
        {
            result = null;
            if (string.IsNullOrEmpty(format)) return false;
            if (Math.Abs(value - Math.Round(value)) > 0.0000001 || value < 0) return false;

            string section = format.Split(';')[0];
            section = section.Replace("\"", "").Replace("\\", "");
            if (section.StartsWith("["))
            {
                int close = section.IndexOf(']');
                if (close >= 0) section = section.Substring(close + 1);
            }
            section = section.Replace("_", "").Replace("*", "").Trim();
            if (section.Length == 0) return false;
            foreach (char c in section)
            {
                if (c != '0') return false;
            }
            long n = (long)Math.Round(value);
            result = n.ToString("D" + section.Length, CultureInfo.InvariantCulture);
            return true;
        }

        public static void SuggestMapping(SheetData sheet, bool hasHeader, out int textCol, out int barcodeCol, out int qrCol)
        {
            textCol = -1;
            barcodeCol = -1;
            qrCol = -1;
            if (sheet == null || sheet.ColumnCount <= 0) return;

            if (hasHeader && sheet.Rows.Count > 0)
            {
                for (int c = 0; c < sheet.ColumnCount; c++)
                {
                    string name = (c < sheet.Rows[0].Length ? sheet.Rows[0][c] : "").ToLowerInvariant();
                    if (textCol < 0 && ContainsAny(name, "文本", "内容", "名称", "品名", "产品", "描述", "text", "content", "name", "desc"))
                        textCol = c;
                    if (barcodeCol < 0 && ContainsAny(name, "条码", "一维", "序列", "编号", "barcode", "code", "serial", "sn"))
                        barcodeCol = c;
                    if (qrCol < 0 && ContainsAny(name, "二维码", "qr", "qrcode", "链接", "url"))
                        qrCol = c;
                }
            }

            if (textCol < 0) textCol = 0;
            if (barcodeCol < 0 && sheet.ColumnCount > 1) barcodeCol = 1;
            if (qrCol < 0 && sheet.ColumnCount > 2) qrCol = 2;
        }

        private static bool ContainsAny(string text, params string[] keys)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (string key in keys)
            {
                if (text.Contains(key)) return true;
            }
            return false;
        }
    }
}
