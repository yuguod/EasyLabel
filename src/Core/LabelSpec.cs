using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace EasyLabel.Core
{
    /// <summary>标签物理规格，尺寸单位为毫米。</summary>
    public sealed class LabelSpec
    {
        public string Name { get; private set; }
        public double WidthMm { get; private set; }
        public double HeightMm { get; private set; }

        public LabelSpec(string name, double widthMm, double heightMm)
        {
            if (widthMm <= 0 || heightMm <= 0) throw new ArgumentOutOfRangeException("尺寸必须大于 0");
            Name = string.IsNullOrWhiteSpace(name) ? FormatName(widthMm, heightMm) : name;
            WidthMm = widthMm;
            HeightMm = heightMm;
        }

        public static readonly LabelSpec Label40x15 = new LabelSpec("40×15 mm", 40, 15);
        public static readonly LabelSpec Label30x25 = new LabelSpec("30×25 mm", 30, 25);
        public static readonly LabelSpec Label30x20 = new LabelSpec("30×20 mm", 30, 20);

        public static IList<LabelSpec> All
        {
            get { return new[] { Label40x15, Label30x25, Label30x20 }; }
        }

        public int WidthDots(int dpi)
        {
            return MmToDots(WidthMm, dpi);
        }

        public int HeightDots(int dpi)
        {
            return MmToDots(HeightMm, dpi);
        }

        public static int MmToDots(double mm, int dpi)
        {
            if (dpi <= 0) throw new ArgumentOutOfRangeException("dpi");
            return (int)Math.Round(mm * dpi / 25.4, MidpointRounding.AwayFromZero);
        }

        public static double DotsToMm(int dots, int dpi)
        {
            return dots * 25.4 / dpi;
        }

        public static LabelSpec CreateCustom(double widthMm, double heightMm)
        {
            if (widthMm < 5 || widthMm > 300 || heightMm < 5 || heightMm > 300)
                throw new ArgumentOutOfRangeException("自定义尺寸需在 5–300 mm 之间");
            return new LabelSpec(FormatName(widthMm, heightMm), widthMm, heightMm);
        }

        public static LabelSpec FromName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return Label40x15;
            string n = name.Replace("×", "x").Replace("*", "x").Replace("mm", "").Replace("MM", "").Trim();
            Match match = Regex.Match(n, @"(\d+(?:\.\d+)?)\s*x\s*(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (!match.Success) return Label40x15;
            double w, h;
            if (!double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out w) ||
                !double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out h))
                return Label40x15;
            if (w <= 0 || h <= 0) return Label40x15;
            if (Near(w, 40) && Near(h, 15)) return Label40x15;
            if (Near(w, 30) && Near(h, 25)) return Label30x25;
            if (Near(w, 30) && Near(h, 20)) return Label30x20;
            return new LabelSpec(FormatName(w, h), w, h);
        }

        private static bool Near(double a, double b)
        {
            return Math.Abs(a - b) < 0.001;
        }

        public static string FormatName(double widthMm, double heightMm)
        {
            return widthMm.ToString("0.##", CultureInfo.InvariantCulture) + "×" +
                   heightMm.ToString("0.##", CultureInfo.InvariantCulture) + " mm";
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
