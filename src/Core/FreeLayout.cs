using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EasyLabel.Core
{
    /// <summary>自由排版里可以放置的元素类型。</summary>
    public enum FreeElementKind
    {
        Text = 0,
        Barcode = 1,
        Qr = 2
    }

    /// <summary>
    /// 自由排版里的一个元素：文字块、一维码或二维码。
    /// <para>
    /// 坐标单位是毫米，原点在 **安全区左上角**（安全区 = 标签去掉四边距后的区域），
    /// 与 <see cref="LayoutPlan"/> 一致，所以边距、打印偏移对两种排版都同样生效。
    /// </para>
    /// </summary>
    public sealed class FreeElement
    {
        public FreeElementKind Kind { get; set; }

        /// <summary>元素名，只用于界面显示（如“品名”“价格”）。</summary>
        public string Name { get; set; }

        /// <summary>位置与尺寸（mm，相对安全区）。</summary>
        public RectangleF Box { get; set; }

        /// <summary>
        /// 内容。文字块是显示的文字；一维码/二维码是数据。
        /// 都支持占位符：{text} {barcode} {qr} {date} {time} {datetime} 等。
        /// </summary>
        public string Content { get; set; }

        /// <summary>字号（mm）；0 表示按元素框自动适配。</summary>
        public double FontSizeMm { get; set; }

        public bool Bold { get; set; }

        public TextAlign Align { get; set; }

        /// <summary>字体名；空表示跟随全局字体。</summary>
        public string FontFamily { get; set; }

        public FreeElement()
        {
            Kind = FreeElementKind.Text;
            Name = "";
            Box = RectangleF.Empty;
            Content = "";
            FontSizeMm = 0;
            Align = TextAlign.Center;
            FontFamily = "";
        }

        public FreeElement Clone()
        {
            return new FreeElement
            {
                Kind = Kind,
                Name = Name,
                Box = Box,
                Content = Content,
                FontSizeMm = FontSizeMm,
                Align = Align,
                FontFamily = FontFamily
            };
        }

        public string KindName
        {
            get { return FreeLayout.KindName(Kind); }
        }

        public override string ToString()
        {
            string title = string.IsNullOrWhiteSpace(Name) ? KindName : Name.Trim();
            return title + " · " + KindName;
        }
    }

    /// <summary>
    /// 自由排版：在一张标签上任意摆放多个文字块、一维码和二维码。
    /// 与三区块的 <see cref="LayoutPlan"/> 互斥，由 <see cref="RenderOptions.FreeLayout"/> 选择。
    /// </summary>
    public sealed class FreeLayout
    {
        /// <summary>最多元素数：再多画布上也没法操作了。</summary>
        public const int MaxElements = 24;

        public const string SpaceMargins = "margins";

        public List<FreeElement> Elements { get; private set; }

        public FreeLayout()
        {
            Elements = new List<FreeElement>();
        }

        public bool HasElements
        {
            get { return Elements != null && Elements.Count > 0; }
        }

        public FreeLayout Clone()
        {
            var copy = new FreeLayout();
            if (Elements == null) return copy;
            foreach (FreeElement element in Elements)
            {
                if (element != null) copy.Elements.Add(element.Clone());
            }
            return copy;
        }

        public static string KindName(FreeElementKind kind)
        {
            switch (kind)
            {
                case FreeElementKind.Barcode: return "一维码";
                case FreeElementKind.Qr: return "二维码";
                default: return "文字";
            }
        }

        public int CountOf(FreeElementKind kind)
        {
            int n = 0;
            if (Elements == null) return 0;
            foreach (FreeElement element in Elements)
            {
                if (element != null && element.Kind == kind) n++;
            }
            return n;
        }

        /// <summary>一句摘要，界面上显示“3 个文字块 · 1 个一维码 · 1 个二维码”。</summary>
        public string Summary
        {
            get
            {
                if (!HasElements) return "尚未放置元素";
                var parts = new List<string>();
                int text = CountOf(FreeElementKind.Text);
                int barcode = CountOf(FreeElementKind.Barcode);
                int qr = CountOf(FreeElementKind.Qr);
                if (text > 0) parts.Add(text + " 个文字块");
                if (barcode > 0) parts.Add(barcode + " 个一维码");
                if (qr > 0) parts.Add(qr + " 个二维码");
                return string.Join(" · ", parts.ToArray());
            }
        }

        /// <summary>按可放置区域（安全区四边各放出越界余量）归整所有元素。</summary>
        public void Normalize(LabelSpec spec, RenderOptions options)
        {
            if (Elements == null) Elements = new List<FreeElement>();
            RectangleF box = LayoutPlan.AllowBox(spec, options);
            if (box.Width <= 0 || box.Height <= 0)
                box = new RectangleF(0, 0, (float)(spec == null ? 40 : spec.WidthMm),
                    (float)(spec == null ? 15 : spec.HeightMm));

            for (int i = Elements.Count - 1; i >= 0; i--)
            {
                FreeElement element = Elements[i];
                if (element == null)
                {
                    Elements.RemoveAt(i);
                    continue;
                }
                if (element.Name == null) element.Name = "";
                if (element.Content == null) element.Content = "";
                if (element.FontFamily == null) element.FontFamily = "";
                if (!Enum.IsDefined(typeof(FreeElementKind), element.Kind)) element.Kind = FreeElementKind.Text;
                if (!Enum.IsDefined(typeof(TextAlign), element.Align)) element.Align = TextAlign.Center;
                if (double.IsNaN(element.FontSizeMm) || element.FontSizeMm < 0) element.FontSizeMm = 0;
                if (element.FontSizeMm > 0 && element.FontSizeMm < 1.2) element.FontSizeMm = 1.2;
                if (element.FontSizeMm > 20) element.FontSizeMm = 20;
                element.Box = ClampBox(element.Box, box, MinSize(element.Kind));
            }

            if (Elements.Count > MaxElements)
                Elements.RemoveRange(MaxElements, Elements.Count - MaxElements);
        }

        private static float MinSize(FreeElementKind kind)
        {
            if (kind == FreeElementKind.Barcode) return 4f;
            if (kind == FreeElementKind.Qr) return 3f;
            return 2f;
        }

        private static RectangleF ClampBox(RectangleF r, RectangleF box, float minSize)
        {
            float width = r.Width;
            float height = r.Height;
            if (float.IsNaN(width) || width < minSize) width = minSize;
            if (float.IsNaN(height) || height < minSize) height = minSize;
            if (width > box.Width) width = box.Width;
            if (height > box.Height) height = box.Height;

            float x = r.X;
            float y = r.Y;
            if (float.IsNaN(x)) x = box.X;
            if (float.IsNaN(y)) y = box.Y;
            // 和 LayoutPlan 一样：只保证左上角在可放置范围内，元素大于安全区属于“故意越界”。
            x = Math.Max(box.X, Math.Min(box.Right, x));
            y = Math.Max(box.Y, Math.Min(box.Bottom, y));
            return new RectangleF(x, y, width, height);
        }

        /// <summary>
        /// 从当前自动布局（三个区块）生成一份自由排版：位置尺寸照搬，内容用占位符，
        /// 这样在主界面改文字/条码/二维码，自由排版里的内容会跟着变。
        /// </summary>
        public static FreeLayout FromAuto(LabelLayout layout)
        {
            var free = new FreeLayout();
            if (layout == null || layout.Options == null) return free;
            RenderOptions options = layout.Options;
            float mL = (float)options.MarginLeftMm;
            float mT = (float)options.MarginTopMm;

            if (layout.ShowText && layout.TextRect.Width > 0 && layout.TextRect.Height > 0)
            {
                free.Elements.Add(new FreeElement
                {
                    Kind = FreeElementKind.Text,
                    Name = "文字",
                    Box = ToMm(layout.TextRect, options, mL, mT),
                    Content = "{text}",
                    Align = options.TextAlign,
                    Bold = options.Bold,
                    FontFamily = ""
                });
            }

            if (layout.ShowBarcode && layout.BarcodeRect.Width > 0 && layout.BarcodeRect.Height > 0)
            {
                RectangleF box = layout.BarcodeRect;
                if (layout.ShowHri && layout.HriRect.Width > 0 && layout.HriRect.Height > 0)
                    box = RectangleF.FromLTRB(box.Left, box.Top,
                        Math.Max(box.Right, layout.HriRect.Right),
                        Math.Max(box.Bottom, layout.HriRect.Bottom));
                free.Elements.Add(new FreeElement
                {
                    Kind = FreeElementKind.Barcode,
                    Name = "一维码",
                    Box = ToMm(box, options, mL, mT),
                    Content = "{barcode}",
                    Align = TextAlign.Center
                });
            }

            if (layout.ShowQr && layout.QrRect.Width > 0 && layout.QrRect.Height > 0)
            {
                free.Elements.Add(new FreeElement
                {
                    Kind = FreeElementKind.Qr,
                    Name = "二维码",
                    Box = ToMm(layout.QrRect, options, mL, mT),
                    Content = "{qr}",
                    Align = TextAlign.Center
                });
            }

            return free;
        }

        private static RectangleF ToMm(RectangleF px, RenderOptions options, float mL, float mT)
        {
            double dots = options.DotsPerMm;
            return new RectangleF(
                (float)(px.X / dots - mL),
                (float)(px.Y / dots - mT),
                (float)(px.Width / dots),
                (float)(px.Height / dots));
        }

        /// <summary>把元素拖到某个位置时用它算一个不越界的默认框（mm）。</summary>
        public static RectangleF DefaultBox(LabelSpec spec, FreeElementKind kind, int index)
        {
            double w = spec == null ? 40 : spec.WidthMm;
            double h = spec == null ? 15 : spec.HeightMm;
            float offset = Math.Min(2f, (float)Math.Min(w, h) * 0.08f) * (index % 4);
            switch (kind)
            {
                case FreeElementKind.Barcode:
                    return new RectangleF((float)(w * 0.08) + offset, (float)(h * 0.45),
                        (float)(w * 0.84), Math.Max(5f, (float)(h * 0.32)));
                case FreeElementKind.Qr:
                {
                    float size = (float)Math.Max(6f, Math.Min(w, h) * 0.45);
                    return new RectangleF((float)(w - size) / 2f + offset, (float)(h - size) / 2f, size, size);
                }
                default:
                    return new RectangleF((float)(w * 0.06) + offset, (float)(h * 0.08) + offset,
                        (float)(w * 0.88), Math.Max(3f, (float)(h * 0.22)));
            }
        }
    }

    /// <summary>
    /// 自由排版内容里的占位符替换。
    /// <para>支持：{text} {barcode} {qr} {date} {time} {datetime} {year} {month} {day}
    /// {hour} {minute} {row}，中文写法（{内容} {条码} {二维码} {日期} {时间} …）等价。</para>
    /// </summary>
    public static class LabelTokens
    {
        private static readonly Regex Pattern =
            new Regex(@"\{([A-Za-z0-9_\u4e00-\u9fa5]+)\}", RegexOptions.Compiled);

        /// <summary>占位符说明，界面提示直接用这一句。</summary>
        public const string HelpText =
            "可插入 {text} {barcode} {qr} {date} {time} {row}；不认识的占位符原样显示。";

        /// <summary>界面“插入占位符”菜单用：占位符 → 说明（顺序即菜单顺序）。</summary>
        public static KeyValuePair<string, string>[] Palette()
        {
            return new[]
            {
                new KeyValuePair<string, string>("{text}", "标签文字（批量时是该行文本）"),
                new KeyValuePair<string, string>("{barcode}", "一维码内容"),
                new KeyValuePair<string, string>("{qr}", "二维码内容"),
                new KeyValuePair<string, string>("{row}", "批量行号（第几行）"),
                new KeyValuePair<string, string>("{date}", "打印日期 2026-11-17"),
                new KeyValuePair<string, string>("{time}", "打印时间 09:05"),
                new KeyValuePair<string, string>("{datetime}", "日期 + 时间"),
                new KeyValuePair<string, string>("{year}", "年（2026）"),
                new KeyValuePair<string, string>("{month}", "月（11）"),
                new KeyValuePair<string, string>("{day}", "日（17）"),
                new KeyValuePair<string, string>("{hour}", "时（09）"),
                new KeyValuePair<string, string>("{minute}", "分（05）")
            };
        }

        public static string Expand(string pattern, LabelContent content, DateTime now)
        {
            if (string.IsNullOrEmpty(pattern)) return "";
            if (pattern.IndexOf('{') < 0) return pattern;

            LabelContent source = content;
            string text = source == null ? "" : source.EffectiveText;
            string barcode = source == null ? "" : source.EffectiveBarcode;
            string qr = source == null ? "" : source.EffectiveQr;
            var extras = source == null ? null : source.Tokens;

            return Pattern.Replace(pattern, delegate(Match match)
            {
                string key = match.Groups[1].Value;
                string value;
                if (TryResolve(key, text, barcode, qr, extras, now, out value)) return value;
                return match.Value;
            });
        }

        private static bool TryResolve(string key, string text, string barcode, string qr,
            IDictionary<string, string> extras, DateTime now, out string value)
        {
            value = null;
            string k = key.ToLowerInvariant();
            switch (k)
            {
                case "text":
                case "body":
                case "content":
                case "内容":
                case "文字":
                case "文本":
                    value = text;
                    return true;
                case "barcode":
                case "bar":
                case "条码":
                case "一维码":
                    value = barcode;
                    return true;
                case "qr":
                case "qrcode":
                case "二维码":
                    value = qr;
                    return true;
                case "date":
                case "日期":
                    value = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    return true;
                case "time":
                case "时间":
                    value = now.ToString("HH:mm", CultureInfo.InvariantCulture);
                    return true;
                case "datetime":
                case "now":
                case "日期时间":
                case "当前时间":
                    value = now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                    return true;
                case "year":
                case "年":
                    value = now.Year.ToString(CultureInfo.InvariantCulture);
                    return true;
                case "month":
                case "月":
                    value = now.Month.ToString("00", CultureInfo.InvariantCulture);
                    return true;
                case "day":
                case "日":
                    value = now.Day.ToString("00", CultureInfo.InvariantCulture);
                    return true;
                case "hour":
                case "时":
                    value = now.Hour.ToString("00", CultureInfo.InvariantCulture);
                    return true;
                case "minute":
                case "分":
                    value = now.Minute.ToString("00", CultureInfo.InvariantCulture);
                    return true;
                case "second":
                case "秒":
                    value = now.Second.ToString("00", CultureInfo.InvariantCulture);
                    return true;
            }

            if (extras != null && extras.Count > 0)
            {
                string extra;
                if (extras.TryGetValue(k, out extra))
                {
                    value = extra ?? "";
                    return true;
                }
            }
            return false;
        }

        /// <summary>检查内容里是否有当前填充不了的占位符（编辑器用于提示）。</summary>
        public static List<string> UnknownTokens(string pattern, LabelContent content)
        {
            var unknown = new List<string>();
            if (string.IsNullOrEmpty(pattern)) return unknown;
            foreach (Match match in Pattern.Matches(pattern))
            {
                string probe = Expand(match.Value, content, DateTime.Now);
                if (probe == match.Value && unknown.IndexOf(match.Value) < 0) unknown.Add(match.Value);
            }
            return unknown;
        }
    }
}
