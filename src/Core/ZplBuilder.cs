using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EasyLabel.Core
{
    public static class ZplBuilder
    {
        private const string HexDigits = "0123456789ABCDEF";
        private const string LabelStart = "^XA";
        private const string LineBreak = "\r\n";

        public static string BuildLabel(LabelSpec spec, RenderedLabel image, PrintOptions printOptions, string comment)
        {
            return BuildLabel(spec, image, printOptions, comment, 1);
        }

        /// <summary>
        /// 生成一张标签的 ZPL。<paramref name="copies"/> 通过 ^PQ 交给打印机，
        /// 不再把整张 ^GFA 位图复制 N 遍——600 dpi 的 40×15 标签单张 hex 后约 80 KB，
        /// 打 100 份就是 8 MB 数据白跑一趟网络/串口。
        /// </summary>
        public static string BuildLabel(LabelSpec spec, RenderedLabel image, PrintOptions printOptions, string comment, int copies)
        {
            if (spec == null) throw new ArgumentNullException("spec");
            if (image == null) throw new ArgumentNullException("image");
            if (printOptions == null) throw new ArgumentNullException("printOptions");
            printOptions.Normalize();
            if (copies < 1) copies = 1;

            var sb = new StringBuilder(256 + image.Stride * image.HeightDots * 2);
            sb.Append(LabelStart).Append(LineBreak);
            sb.Append("^FX ").Append(string.IsNullOrEmpty(comment) ? "EasyLabel" : comment).Append(LineBreak);
            sb.Append("^PW").Append(image.WidthDots.ToString(CultureInfo.InvariantCulture)).Append(LineBreak);
            // ^LL 在 ^MNY（间隙纸）下多数固件会忽略，别把它当尺寸校准手段：
            // 真正决定打印尺寸的是 ^PW 和打印机分辨率。
            sb.Append("^LL").Append(image.HeightDots.ToString(CultureInfo.InvariantCulture)).Append(LineBreak);
            sb.Append("^LH0,0").Append(LineBreak);
            sb.Append(printOptions.MediaType == MediaType.Continuous ? "^MNN" : "^MNY").Append(LineBreak);
            sb.Append("^MD").Append(printOptions.Darkness.ToString(CultureInfo.InvariantCulture)).Append(LineBreak);
            sb.Append("^PR").Append(printOptions.Speed.ToString(CultureInfo.InvariantCulture)).Append(LineBreak);
            if (copies > 1) sb.Append("^PQ").Append(copies.ToString(CultureInfo.InvariantCulture)).Append(LineBreak);
            sb.Append("^FO0,0^GFA,")
              .Append(image.PackedBits.Length.ToString(CultureInfo.InvariantCulture)).Append(",")
              .Append(image.PackedBits.Length.ToString(CultureInfo.InvariantCulture)).Append(",")
              .Append(image.Stride.ToString(CultureInfo.InvariantCulture)).Append(",");

            byte[] bits = image.PackedBits;
            for (int i = 0; i < bits.Length; i++)
            {
                byte b = bits[i];
                sb.Append(HexDigits[(b >> 4) & 0x0F]);
                sb.Append(HexDigits[b & 0x0F]);
            }
            sb.Append("^FS").Append(LineBreak);
            sb.Append("^XZ").Append(LineBreak);
            return sb.ToString();
        }

        /// <summary>把多张已生成好的标签拼成一个作业，份数同样走 ^PQ。</summary>
        public static string Wrap(IEnumerable<string> labels, int copies)
        {
            if (labels == null) return "";
            if (copies < 1) copies = 1;
            var sb = new StringBuilder();
            foreach (var label in labels) sb.Append(WithCopies(label, copies));
            return sb.ToString();
        }

        private static string WithCopies(string label, int copies)
        {
            if (string.IsNullOrEmpty(label) || copies <= 1) return label ?? "";
            if (label.IndexOf("^PQ", StringComparison.Ordinal) >= 0) return label;

            int index = label.IndexOf(LineBreak, StringComparison.Ordinal);
            string head = index >= 0 ? label.Substring(0, index + LineBreak.Length) : LabelStart + LineBreak;
            string body = index >= 0 ? label.Substring(index + LineBreak.Length) : label;
            return head + "^PQ" + copies.ToString(CultureInfo.InvariantCulture) + LineBreak + body;
        }

        /// <summary>
        /// 强制把整份作业的打印份数改成 <paramref name="copies"/>。
        /// 和 <see cref="Wrap"/> 不同：这里会覆盖文件里已有的 ^PQ（导入外部 .zpl 时用户的份数优先），
        /// 一份文件里有多段 ^XA…^XZ 时逐段处理；没有 ^PQ 的段插在 ^XA 那一行之后。
        /// </summary>
        public static string OverrideCopies(string zpl, int copies)
        {
            if (string.IsNullOrEmpty(zpl)) return zpl ?? "";
            if (copies < 1) copies = 1;
            string value = copies.ToString(CultureInfo.InvariantCulture);

            var sb = new StringBuilder(zpl.Length + 16);
            int pos = 0;
            bool foundFormat = false;
            while (true)
            {
                int start = zpl.IndexOf("^XA", pos, StringComparison.OrdinalIgnoreCase);
                if (start < 0) break;

                int end = zpl.IndexOf("^XZ", start, StringComparison.OrdinalIgnoreCase);
                int blockEnd = end < 0 ? zpl.Length : end + 3;
                // ^XA 之前的注释和杂项原样保留。
                sb.Append(zpl, pos, start - pos);
                sb.Append(ApplyCopies(zpl.Substring(start, blockEnd - start), value));
                pos = blockEnd;
                foundFormat = true;
            }

            // 没有 ^XA 的片段（比如只截了一段指令）也照样处理，别悄悄丢掉用户的份数。
            if (!foundFormat) return ApplyCopies(zpl, value);
            sb.Append(zpl, pos, zpl.Length - pos);
            return sb.ToString();
        }

        /// <summary>单段 ZPL：已有 ^PQ 就改数字，没有就插在第一行之后。</summary>
        private static string ApplyCopies(string block, string value)
        {
            if (block.IndexOf("^PQ", StringComparison.OrdinalIgnoreCase) < 0)
            {
                int lineEnd = block.IndexOf('\n');
                if (lineEnd < 0) return block + "^PQ" + value;
                return block.Substring(0, lineEnd + 1) + "^PQ" + value + LineBreak + block.Substring(lineEnd + 1);
            }

            var sb = new StringBuilder(block.Length + 2);
            int pos = 0;
            while (true)
            {
                int index = block.IndexOf("^PQ", pos, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    sb.Append(block, pos, block.Length - pos);
                    break;
                }
                int i = index + 3;
                while (i < block.Length && (block[i] == ' ' || block[i] == '\t')) i++;
                int digits = i;
                while (i < block.Length && block[i] >= '0' && block[i] <= '9') i++;
                sb.Append(block, pos, digits - pos).Append(value);
                pos = i;
            }
            return sb.ToString();
        }

        public static byte[] ToAscii(string zpl)
        {
            return Encoding.ASCII.GetBytes(zpl ?? "");
        }
    }
}
