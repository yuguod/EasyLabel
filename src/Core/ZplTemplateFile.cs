using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace EasyLabel.Core
{
    /// <summary>从 ZPL 文本里读出来的概要，用于展示外部 .zpl 的信息。</summary>
    public sealed class ZplSummary
    {
        public int WidthDots { get; set; }
        public int HeightDots { get; set; }
        public int Copies { get; set; }
        public bool HasGraphic { get; set; }
        public int ByteCount { get; set; }
        public int LineCount { get; set; }
        public bool HasEmbeddedTemplate { get; set; }

        public string SizeText
        {
            get
            {
                if (WidthDots <= 0 || HeightDots <= 0) return "尺寸未知";
                return WidthDots.ToString(CultureInfo.InvariantCulture) + " × " +
                       HeightDots.ToString(CultureInfo.InvariantCulture) + " 点";
            }
        }

        public string ByteText
        {
            get
            {
                if (ByteCount >= 1024 * 1024)
                    return (ByteCount / 1024.0 / 1024.0).ToString("0.##", CultureInfo.InvariantCulture) + " MB";
                if (ByteCount >= 1024)
                    return (ByteCount / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
                return ByteCount.ToString(CultureInfo.InvariantCulture) + " B";
            }
        }
    }

    /// <summary>
    /// .zpl 模版文件：既能直接发给打印机（标准 ZPL 指令），又能在 EasyLabel 里重新打开继续编辑。
    /// <para>
    /// 做法是把模版的 XML 用 base64 塞进一串 ^FX 注释里（注释打印机会忽略），
    /// 这样同一个文件有两种身份：外部工具看到的是纯 ZPL，EasyLabel 看到的是完整模版。
    /// base64 只含 A-Z a-z 0-9 + / =，不会出现 ^ 或 ~，不会被当成指令终止符。
    /// </para>
    /// </summary>
    public static class ZplTemplateFile
    {
        public const string Extension = ".zpl";

        private const string BeginMarker = "^FX EasyLabel-Template v1";
        private const string EndMarker = "^FX EasyLabel-Template-End";
        private const string CommentPrefix = "^FX ";
        private const int ChunkSize = 76;
        private static readonly string LineBreak = "\r\n";

        /// <summary>粗判是不是 ZPL：至少有一个 ^XA。</summary>
        public static bool LooksLikeZpl(string text)
        {
            return !string.IsNullOrEmpty(text) &&
                   text.IndexOf("^XA", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 把模版信息写进 ZPL 注释；已有旧信息会被替换掉，所以重命名后不会残留旧名字。
        /// </summary>
        public static string Embed(LabelTemplate template, string zpl)
        {
            if (template == null) throw new ArgumentNullException("template");
            if (string.IsNullOrEmpty(zpl)) throw new ArgumentException("ZPL 内容为空", "zpl");

            string body = StripMetadata(zpl);
            string xml = TemplateStore.Serialize(template);
            string payload = Convert.ToBase64String(new UTF8Encoding(false).GetBytes(xml));

            var block = new StringBuilder(payload.Length + 128);
            block.Append(BeginMarker).Append(LineBreak);
            for (int i = 0; i < payload.Length; i += ChunkSize)
            {
                block.Append(CommentPrefix)
                     .Append(payload, i, Math.Min(ChunkSize, payload.Length - i))
                     .Append(LineBreak);
            }
            block.Append(EndMarker).Append(LineBreak);

            // 插在 ^XA 那一行之后：既在标签格式内部，又排在真正的指令前面。
            int start = body.IndexOf("^XA", StringComparison.OrdinalIgnoreCase);
            if (start < 0) return block + body;
            int lineEnd = body.IndexOf('\n', start);
            if (lineEnd < 0) return body + LineBreak + block;
            return body.Substring(0, lineEnd + 1) + block + body.Substring(lineEnd + 1);
        }

        /// <summary>去掉内嵌的模版信息，只留原始 ZPL。没有信息时原样返回。</summary>
        public static string StripMetadata(string zpl)
        {
            if (string.IsNullOrEmpty(zpl)) return zpl ?? "";
            int begin = zpl.IndexOf(BeginMarker, StringComparison.Ordinal);
            if (begin < 0) return zpl;
            int end = zpl.IndexOf(EndMarker, begin, StringComparison.Ordinal);
            if (end < 0) return zpl;
            int endLine = zpl.IndexOf('\n', end);
            if (endLine < 0) return zpl.Substring(0, begin);
            return zpl.Substring(0, begin) + zpl.Substring(endLine + 1);
        }

        /// <summary>尝试从 ZPL 注释里还原模版；外部生成的 ZPL 返回 false。</summary>
        public static bool TryReadTemplate(string zplText, out LabelTemplate template)
        {
            template = null;
            string payload = ExtractPayload(zplText);
            if (string.IsNullOrEmpty(payload)) return false;
            try
            {
                byte[] bytes = Convert.FromBase64String(payload);
                string xml = new UTF8Encoding(false).GetString(bytes);
                LabelTemplate parsed = TemplateStore.Deserialize(xml, "");
                if (parsed == null) return false;
                template = parsed;
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (Exception)
            {
                // 信息块损坏（比如被别的工具截断）：当作纯 ZPL 处理，不让整个文件失效。
                return false;
            }
        }

        private static string ExtractPayload(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int begin = text.IndexOf(BeginMarker, StringComparison.Ordinal);
            if (begin < 0) return null;

            var sb = new StringBuilder();
            int pos = begin + BeginMarker.Length;
            while (pos < text.Length)
            {
                int eol = text.IndexOf('\n', pos);
                string line = eol < 0 ? text.Substring(pos) : text.Substring(pos, eol - pos);
                pos = eol < 0 ? text.Length : eol + 1;
                line = line.Trim('\r', ' ', '\t');
                if (line.Length == 0) continue;
                if (line.StartsWith(EndMarker, StringComparison.Ordinal)) break;
                if (!line.StartsWith("^FX", StringComparison.Ordinal)) break;
                sb.Append(line.Substring(3).TrimStart(' '));
            }
            return sb.Length == 0 ? null : sb.ToString();
        }

        /// <summary>读出尺寸/份数/大小等概要，用于外部 .zpl 的说明文字。</summary>
        public static ZplSummary Inspect(string zplText)
        {
            var summary = new ZplSummary();
            string text = zplText ?? "";
            summary.ByteCount = Encoding.ASCII.GetByteCount(text);
            summary.LineCount = text.Length == 0 ? 0 : CountLines(text);
            summary.WidthDots = ReadNumber(text, "^PW");
            summary.HeightDots = ReadNumber(text, "^LL");
            summary.Copies = ReadNumber(text, "^PQ");
            if (summary.Copies < 1) summary.Copies = 1;
            summary.HasGraphic = text.IndexOf("^GFA,", StringComparison.OrdinalIgnoreCase) >= 0;
            summary.HasEmbeddedTemplate = text.IndexOf(BeginMarker, StringComparison.Ordinal) >= 0;
            return summary;
        }

        private static int CountLines(string text)
        {
            int lines = 1;
            for (int i = 0; i < text.Length; i++)
                if (text[i] == '\n') lines++;
            return lines;
        }

        /// <summary>取 ^PW/^LL/^PQ 后面的第一个整数。</summary>
        private static int ReadNumber(string text, string token)
        {
            int index = text.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return 0;
            int i = index + token.Length;
            while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
            int start = i;
            while (i < text.Length && char.IsDigit(text[i])) i++;
            if (i == start) return 0;
            int value;
            return int.TryParse(text.Substring(start, i - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                ? value : 0;
        }

        /// <summary>读 ZPL 文本：优先按 UTF-8 严格解码，失败再退回 ASCII，避免中文注释被吃掉。</summary>
        public static string ReadText(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0) return "";
            bool hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            int offset = hasBom ? 3 : 0;
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
            }
            catch (DecoderFallbackException)
            {
                return Encoding.ASCII.GetString(bytes, offset, bytes.Length - offset);
            }
        }

        public static void WriteText(string path, string text)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, text ?? "", new UTF8Encoding(false));
        }
    }
}
