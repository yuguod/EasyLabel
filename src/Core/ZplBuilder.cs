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

        public static byte[] ToAscii(string zpl)
        {
            return Encoding.ASCII.GetBytes(zpl ?? "");
        }
    }
}
