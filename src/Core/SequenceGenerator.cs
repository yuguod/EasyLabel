using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EasyLabel.Core
{
    /// <summary>
    /// 批量序列的参数：固定前缀 + 数字序列 + 可选后缀。
    /// 变动部分是纯数字，按 <see cref="Digits"/> 补前导零；超出位数时按实际位数输出，不截断。
    /// </summary>
    public sealed class SequenceSpec
    {
        public string Prefix = "";
        public string Suffix = "";
        public long Start = 1;
        public long Step = 1;
        public int Digits = 1;
        public int Count = 1;

        public SequenceSpec Clone()
        {
            return new SequenceSpec
            {
                Prefix = Prefix,
                Suffix = Suffix,
                Start = Start,
                Step = Step,
                Digits = Digits,
                Count = Count
            };
        }
    }

    /// <summary>序列计算：格式化、由数量算末值、由末值反推数量、生成整段。</summary>
    public static class SequenceGenerator
    {
        /// <summary>一次最多生成多少行，防止误填 99999 把界面卡死。</summary>
        public const int MaxCount = 20000;

        public const int MaxDigits = 12;

        /// <summary>按位数补零；数值比位数长时原样输出（10000 在 4 位下仍是 10000）。</summary>
        public static string Format(long value, int digits)
        {
            if (value < 0) throw new ArgumentOutOfRangeException("value", "序列值不能为负数");
            if (digits < 1) digits = 1;
            string text = value.ToString(CultureInfo.InvariantCulture);
            if (text.Length >= digits) return text;
            return new string('0', digits - text.Length) + text;
        }

        /// <summary>把一段序列值拼成完整内容：前缀 + 补零数字 + 后缀。</summary>
        public static string Compose(SequenceSpec spec, long value)
        {
            var sb = new StringBuilder();
            sb.Append(spec.Prefix ?? "");
            sb.Append(Format(value, spec.Digits));
            sb.Append(spec.Suffix ?? "");
            return sb.ToString();
        }

        /// <summary>第 count 个（从 1 起）的值。</summary>
        public static long ValueAt(long start, long step, int index)
        {
            if (index < 1) throw new ArgumentOutOfRangeException("index");
            return start + step * (index - 1);
        }

        /// <summary>由“开始 + 数量”算最后一个值。</summary>
        public static long LastValue(long start, long step, int count)
        {
            if (count < 1) throw new ArgumentOutOfRangeException("count");
            if (step < 1) throw new ArgumentOutOfRangeException("step");
            return start + step * (count - 1);
        }

        /// <summary>
        /// 由“开始 + 最后一个 + 步长”反推数量；除不尽（末值不是序列上的一站）时返回 -1。
        /// </summary>
        public static long CountFor(long start, long last, long step)
        {
            if (step < 1) throw new ArgumentOutOfRangeException("step");
            if (last < start) return 0;
            long span = last - start;
            if (span % step != 0) return -1;
            return span / step + 1;
        }

        /// <summary>
        /// 解析起始/结束编号：只允许数字（可以带前导零），位数取输入的长度。
        /// 例如 "0001" → 1，位数 4。
        /// </summary>
        public static bool TryParseNumber(string text, out long value, out int digits)
        {
            value = 0;
            digits = 1;
            if (text == null) return false;
            string trimmed = text.Trim();
            if (trimmed.Length == 0 || trimmed.Length > MaxDigits * 2) return false;
            for (int i = 0; i < trimmed.Length; i++)
            {
                if (trimmed[i] < '0' || trimmed[i] > '9') return false;
            }
            if (!long.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out value)) return false;
            digits = Math.Max(1, Math.Min(MaxDigits, trimmed.Length));
            return true;
        }

        /// <summary>
        /// 把整串条码拆成“前缀 + 末尾数字”，例如 "20261117DLX0001" → ("20261117DLX", "0001")。
        /// 末尾不是数字时返回 false；整串都是数字时 head 为空。
        /// </summary>
        public static bool TrySplitTrailingNumber(string text, out string head, out string number)
        {
            head = "";
            number = "";
            if (text == null) return false;
            string trimmed = text.Trim();
            if (trimmed.Length == 0) return false;

            int i = trimmed.Length;
            while (i > 0 && trimmed[i - 1] >= '0' && trimmed[i - 1] <= '9') i--;
            if (i == trimmed.Length) return false;

            head = trimmed.Substring(0, i);
            number = trimmed.Substring(i);
            return number.Length > 0;
        }

        /// <summary>参数是否可生成：数量在 1..MaxCount、步长 ≥1、数值不溢出。</summary>
        public static string Validate(SequenceSpec spec)
        {
            if (spec == null) return "没有生成参数";
            if (spec.Count < 1) return "数量至少为 1";
            if (spec.Count > MaxCount) return "一次最多生成 " + MaxCount + " 行";
            if (spec.Step < 1) return "步长至少为 1";
            if (spec.Start < 0) return "起始编号不能为负数";
            if (spec.Digits < 1 || spec.Digits > MaxDigits) return "位数应在 1–" + MaxDigits + " 之间";
            long last = spec.Start + spec.Step * (spec.Count - 1);
            if (last < spec.Start) return "编号太大，已超出范围";
            return "";
        }

        /// <summary>生成整段内容；参数不合法时抛 <see cref="ArgumentException"/>。</summary>
        public static List<string> Build(SequenceSpec spec)
        {
            string error = Validate(spec);
            if (error.Length > 0) throw new ArgumentException(error, "spec");

            var list = new List<string>(spec.Count);
            for (int i = 0; i < spec.Count; i++)
            {
                list.Add(Compose(spec, spec.Start + spec.Step * i));
            }
            return list;
        }

        /// <summary>示例用：取前几个和后几个，中间用省略号；行数少时全给。</summary>
        public static List<string> Preview(SequenceSpec spec, int head, int tail)
        {
            var result = new List<string>();
            string error = Validate(spec);
            if (error.Length > 0 || spec.Count == 0) return result;
            if (head < 0) head = 0;
            if (tail < 0) tail = 0;
            if (spec.Count <= head + tail)
            {
                for (int i = 0; i < spec.Count; i++) result.Add(Compose(spec, spec.Start + spec.Step * i));
                return result;
            }
            for (int i = 0; i < head; i++) result.Add(Compose(spec, spec.Start + spec.Step * i));
            result.Add("…");
            for (int i = spec.Count - tail; i < spec.Count; i++) result.Add(Compose(spec, spec.Start + spec.Step * i));
            return result;
        }
    }
}
