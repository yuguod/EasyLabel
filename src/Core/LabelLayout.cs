using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using ZXing.Common;

namespace EasyLabel.Core
{
    /// <summary>
    /// 版式计算：把标签内容分配到文字、一维码、二维码区域，并计算实际点阵尺寸。
    /// 预览和打印共用这一份结果。
    /// </summary>
    public sealed class LabelLayout
    {
        public const string UiFontName = "Microsoft YaHei UI";

        public LabelSpec Spec { get; private set; }
        public LabelContent Content { get; private set; }
        public RenderOptions Options { get; private set; }
        public LayoutMode Mode { get; private set; }

        public RectangleF LabelRect { get; private set; }
        public RectangleF TextRect { get; private set; }
        public RectangleF BarcodeRect { get; private set; }
        public RectangleF HriRect { get; private set; }
        public RectangleF QrRect { get; private set; }

        public bool ShowText { get; private set; }
        public bool ShowBarcode { get; private set; }
        public bool ShowQr { get; private set; }
        public bool ShowHri { get; private set; }

        public string Text { get; private set; }
        public EncodedBarcode Barcode { get; private set; }
        public EncodedQr Qr { get; private set; }

        public float TextFontPx { get; private set; }
        public float HriFontPx { get; private set; }
        public int BarcodeScale { get; private set; }
        public int QrScale { get; private set; }

        public List<string> Errors { get; private set; }
        public List<string> Warnings { get; private set; }

        private LabelLayout()
        {
            Errors = new List<string>();
            Warnings = new List<string>();
        }

        public bool IsValid
        {
            get { return Errors.Count == 0; }
        }

        public string InfoText
        {
            get
            {
                var parts = new List<string>();
                parts.Add("版式 " + ModeDisplayName(Mode));
                if (ShowText) parts.Add("文字 " + Math.Round(TextFontPx / Options.Dpi * 25.4, 1).ToString("0.0") + " mm");
                if (ShowBarcode && Barcode != null)
                    parts.Add("一维码 " + BarcodeScale + " 点/模块");
                if (ShowQr && Qr != null)
                    parts.Add("QR v" + Qr.Version + " / " + QrScale + " 点/模块");
                return string.Join(" · ", parts.ToArray());
            }
        }

        public static string ModeDisplayName(LayoutMode mode)
        {
            switch (mode)
            {
                case LayoutMode.TextOnly: return "纯文本";
                case LayoutMode.BarcodeOnly: return "一维码";
                case LayoutMode.QrOnly: return "二维码";
                case LayoutMode.TextBarcode: return "文本 + 一维码";
                case LayoutMode.TextQr: return "文本 + 二维码";
                case LayoutMode.BarcodeQr: return "一维码 + 二维码";
                case LayoutMode.All: return "文本 + 一维码 + 二维码";
                default: return "自动";
            }
        }

        public static LabelLayout Build(LabelSpec spec, LabelContent content, RenderOptions options, Graphics measureGraphics)
        {
            if (spec == null) throw new ArgumentNullException("spec");
            if (content == null) throw new ArgumentNullException("content");
            if (options == null) throw new ArgumentNullException("options");
            options.Normalize();

            var layout = new LabelLayout();
            layout.Spec = spec;
            layout.Content = content;
            layout.Options = options;
            layout.LabelRect = new RectangleF(0, 0, spec.WidthDots(options.Dpi), spec.HeightDots(options.Dpi));
            layout.Text = content.EffectiveText;

            layout.ResolveMode();
            if (layout.Errors.Count > 0) return layout;

            layout.BuildZones();
            layout.PrepareBarcode();
            layout.PrepareQr();
            layout.PrepareText(measureGraphics);
            layout.ValidateBarcodeHri(measureGraphics);
            return layout;
        }

        private void ResolveMode()
        {
            bool hasText = Content.HasText;
            bool hasBarcode = Content.HasBarcode;
            bool hasQr = Content.HasQr;

            LayoutMode requested = Options.LayoutMode;
            if (requested == LayoutMode.Auto)
            {
                if (hasBarcode && hasQr) requested = hasText ? LayoutMode.All : LayoutMode.BarcodeQr;
                else if (hasBarcode) requested = hasText ? LayoutMode.TextBarcode : LayoutMode.BarcodeOnly;
                else if (hasQr) requested = hasText ? LayoutMode.TextQr : LayoutMode.QrOnly;
                else if (hasText) requested = LayoutMode.TextOnly;
                else
                {
                    Errors.Add("没有可打印内容：请填写文字、一维码或二维码");
                    return;
                }
            }

            Mode = requested;
            bool wantText = false, wantBarcode = false, wantQr = false;
            switch (requested)
            {
                case LayoutMode.TextOnly:
                    wantText = true; break;
                case LayoutMode.BarcodeOnly:
                    wantBarcode = true; break;
                case LayoutMode.QrOnly:
                    wantQr = true; break;
                case LayoutMode.TextBarcode:
                    wantText = true; wantBarcode = true; break;
                case LayoutMode.TextQr:
                    wantText = true; wantQr = true; break;
                case LayoutMode.BarcodeQr:
                    wantBarcode = true; wantQr = true; break;
                case LayoutMode.All:
                    wantText = true; wantBarcode = true; wantQr = true; break;
                default:
                    Errors.Add("未知的版式");
                    return;
            }

            if (wantText && !hasText) Errors.Add("当前版式需要文字内容，但文字为空");
            if (wantBarcode && !hasBarcode) Errors.Add("当前版式需要一维码内容，但一维码为空");
            if (wantQr && !hasQr) Errors.Add("当前版式需要二维码内容，但二维码为空");

            ShowText = wantText && hasText;
            ShowBarcode = wantBarcode && hasBarcode;
            ShowQr = wantQr && hasQr;
            ShowHri = ShowBarcode && Options.ShowBarcodeText;
        }

        private RectangleF MRect(double x, double y, double w, double h)
        {
            var r = new RectangleF(
                Options.MmToPx(x), Options.MmToPx(y),
                Options.MmToPx(w), Options.MmToPx(h));
            return ClampRect(r);
        }

        private RectangleF ClampRect(RectangleF r)
        {
            float left = Math.Max(0, r.Left);
            float top = Math.Max(0, r.Top);
            float right = Math.Min(LabelRect.Right, r.Right);
            float bottom = Math.Min(LabelRect.Bottom, r.Bottom);
            if (right < left) right = left;
            if (bottom < top) bottom = top;
            return RectangleF.FromLTRB(left, top, right, bottom);
        }

        private static RectangleF EmptyRect
        {
            get { return RectangleF.Empty; }
        }

        private void ApplyLayoutPlan(LayoutPlan source)
        {
            LayoutPlan plan = source.Clone();
            plan.Normalize(Spec);

            if (ShowText)
                TextRect = MRect(plan.Text.X, plan.Text.Y, plan.Text.Width, plan.Text.Height);

            if (ShowBarcode)
            {
                RectangleF box = plan.Barcode;
                float gap = plan.HriGapMm;

                // 高度比例由 LayoutPlan 记录（已剔除百分比影响），再乘当前“条码高度 %”，
                // 这样进出布局编辑器不会累积变形，百分比控件在自定义布局下也依然有效。
                float ratio = plan.BarHeightRatio > 0
                    ? plan.BarHeightRatio
                    : (ShowHri ? LayoutPlan.DefaultBarHeightRatio : 1.0f);
                float barH = box.Height * ratio * (Options.BarcodeHeightPercent / 100f);

                if (ShowHri)
                {
                    float maxBarH = Math.Max(1.0f, box.Height - gap - 0.8f);
                    if (barH > maxBarH) barH = maxBarH;
                }
                else if (barH > box.Height)
                {
                    barH = box.Height;
                }
                if (barH < 1.0f) barH = Math.Min(1.0f, box.Height);

                BarcodeRect = MRect(box.X, box.Y, box.Width, barH);
                HriRect = ShowHri
                    ? MRect(box.X, box.Y + barH + gap, box.Width, Math.Max(0.0f, box.Height - barH - gap))
                    : EmptyRect;
            }

            if (ShowQr)
            {
                // 与一维码一致：plan.Qr 是“二维码可用区域”，二维码% 在此区域内缩放，
                // 所以两条入口（布局编辑、百分比）不会互相覆盖。
                RectangleF zone = plan.Qr;
                float size = Math.Min(zone.Width, zone.Height) * (Options.QrSizePercent / 100f);
                float cx = zone.Left + zone.Width / 2f;
                float cy = zone.Top + zone.Height / 2f;
                QrRect = MRect(cx - size / 2f, cy - size / 2f, size, size);
            }
        }

        private void BuildZones()
        {
            if (Options.LayoutPlan != null)
            {
                ApplyLayoutPlan(Options.LayoutPlan);
                return;
            }

            double mL = Options.MarginLeftMm;
            double mR = Options.MarginRightMm;
            double mT = Options.MarginTopMm;
            double mB = Options.MarginBottomMm;
            double W = Spec.WidthMm;
            double H = Spec.HeightMm;
            double x0 = mL;
            double y0 = mT;
            double cw = W - mL - mR;
            double ch = H - mT - mB;
            if (cw < 3 || ch < 3)
            {
                Errors.Add("上下左右边距过大，标签内没有可用空间");
                return;
            }
            bool small = W <= 41 && H <= 16;
            const double gap = 0.4;

            TextRect = EmptyRect;
            BarcodeRect = EmptyRect;
            HriRect = EmptyRect;
            QrRect = EmptyRect;

            if (small)
            {
                switch (Mode)
                {
                    case LayoutMode.TextOnly:
                        TextRect = MRect(x0, y0, cw, ch);
                        break;
                    case LayoutMode.BarcodeOnly:
                    {
                        double bh = ch * 0.64;
                        BarcodeRect = MRect(x0, y0, cw, bh);
                        HriRect = MRect(x0, y0 + bh + gap, cw, Math.Max(0, ch - bh - gap));
                        break;
                    }
                    case LayoutMode.TextBarcode:
                    {
                        double th = ch * 0.32;
                        double bh = ch * 0.48;
                        TextRect = MRect(x0, y0, cw, th);
                        BarcodeRect = MRect(x0, y0 + th + gap, cw, bh);
                        HriRect = MRect(x0, y0 + th + bh + 2 * gap, cw,
                            Math.Max(0, ch - th - bh - 2 * gap));
                        break;
                    }
                    case LayoutMode.QrOnly:
                    {
                        double qr = Math.Min(11.0, Math.Min(cw, ch));
                        QrRect = MRect(x0 + (cw - qr) / 2, y0 + (ch - qr) / 2, qr, qr);
                        break;
                    }
                    case LayoutMode.TextQr:
                    {
                        double qr = Math.Min(10.5, Math.Min(ch, cw * 0.45));
                        TextRect = MRect(x0, y0, cw - qr - 1.0, ch);
                        QrRect = MRect(x0 + cw - qr, y0 + (ch - qr) / 2, qr, qr);
                        break;
                    }
                    case LayoutMode.BarcodeQr:
                    {
                        double qr = Math.Min(9.5, Math.Min(ch, cw * 0.35));
                        double bw = cw - qr - 1.8;
                        double bh = ch * 0.64;
                        BarcodeRect = MRect(x0, y0, bw, bh);
                        HriRect = MRect(x0, y0 + bh + gap, bw, Math.Max(0, ch - bh - gap));
                        QrRect = MRect(x0 + cw - qr, y0 + (ch - qr) / 2, qr, qr);
                        break;
                    }
                    case LayoutMode.All:
                    {
                        double qr = Math.Min(10.0, Math.Min(ch, cw * 0.32));
                        double th = ch * 0.32;
                        double bh = ch * 0.42;
                        double bw = cw - qr - 1.8;
                        TextRect = MRect(x0, y0, cw, th);
                        BarcodeRect = MRect(x0, y0 + th + gap, bw, bh);
                        HriRect = MRect(x0, y0 + th + bh + 2 * gap, bw,
                            Math.Max(0, ch - th - bh - 2 * gap));
                        QrRect = MRect(x0 + cw - qr, y0 + (ch - qr) / 2, qr, qr);
                        break;
                    }
                }
            }
            else
            {
                switch (Mode)
                {
                    case LayoutMode.TextOnly:
                        TextRect = MRect(x0, y0, cw, ch);
                        break;
                    case LayoutMode.BarcodeOnly:
                    {
                        double bh = ch * 0.66;
                        BarcodeRect = MRect(x0, y0, cw, bh);
                        HriRect = MRect(x0, y0 + bh + gap, cw, Math.Max(0, ch - bh - gap));
                        break;
                    }
                    case LayoutMode.TextBarcode:
                    {
                        double th = ch * 0.24;
                        double bh = ch * 0.55;
                        TextRect = MRect(x0, y0, cw, th);
                        BarcodeRect = MRect(x0, y0 + th + gap, cw, bh);
                        HriRect = MRect(x0, y0 + th + bh + 2 * gap, cw,
                            Math.Max(0, ch - th - bh - 2 * gap));
                        break;
                    }
                    case LayoutMode.QrOnly:
                    {
                        double qr = Math.Min(18.0, Math.Min(cw, ch));
                        QrRect = MRect(x0 + (cw - qr) / 2, y0 + (ch - qr) / 2, qr, qr);
                        break;
                    }
                    case LayoutMode.TextQr:
                    {
                        double qr = Math.Min(13.5, Math.Min(cw, ch * 0.60));
                        TextRect = MRect(x0, y0, cw, Math.Max(0, ch - qr - gap));
                        QrRect = MRect(x0 + (cw - qr) / 2, y0 + ch - qr, qr, qr);
                        break;
                    }
                    case LayoutMode.BarcodeQr:
                    {
                        double qr = Math.Min(10.5, Math.Min(cw, ch * 0.45));
                        double bh = ch * 0.36;
                        BarcodeRect = MRect(x0, y0, cw, bh);
                        HriRect = MRect(x0, y0 + bh + gap, cw, Math.Max(0, ch * 0.11));
                        QrRect = MRect(x0 + (cw - qr) / 2, y0 + ch - qr, qr, qr);
                        break;
                    }
                    case LayoutMode.All:
                    {
                        double qr = Math.Min(10.0, Math.Min(ch, cw * 0.40));
                        double th = ch * 0.22;
                        double bh = ch * 0.36;
                        double bw = cw - qr - 1.5;
                        TextRect = MRect(x0, y0, cw, th);
                        BarcodeRect = MRect(x0, y0 + th + gap, bw, bh);
                        HriRect = MRect(x0, y0 + th + bh + 2 * gap, bw, Math.Max(0, ch * 0.11));
                        QrRect = MRect(x0 + cw - qr, y0 + ch - qr, qr, qr);
                        break;
                    }
                }
            }

            // 条码高度/二维码大小微调；上限以版式区域为准，避免压到文字。
            float contentBottom = Options.MmToPx(H - mB);
            if (ShowBarcode)
            {
                float gapPx = Options.MmToPx(gap);
                float baseHriH = HriRect.Height;
                if (!ShowHri)
                {
                    float merged = BarcodeRect.Height + gapPx + baseHriH;
                    BarcodeRect = new RectangleF(BarcodeRect.X, BarcodeRect.Y, BarcodeRect.Width,
                        Math.Min(merged * Options.BarcodeHeightPercent / 100f, contentBottom - BarcodeRect.Top));
                    HriRect = EmptyRect;
                }
                else
                {
                    BarcodeRect = new RectangleF(BarcodeRect.X, BarcodeRect.Y, BarcodeRect.Width,
                        Math.Min(BarcodeRect.Height * Options.BarcodeHeightPercent / 100f, contentBottom - BarcodeRect.Top));
                    float hriTop = BarcodeRect.Bottom + gapPx;
                    float hriH = Math.Min(baseHriH, contentBottom - hriTop);
                    HriRect = new RectangleF(BarcodeRect.X, hriTop, BarcodeRect.Width, Math.Max(0, hriH));
                }
            }

            if (ShowQr)
            {
                float baseSize = Math.Min(QrRect.Width, QrRect.Height);
                float maxSize = Options.MmToPx(Math.Min(cw, ch));
                float size = Math.Min(baseSize * Options.QrSizePercent / 100f, maxSize);
                if (size < 4) size = baseSize;
                float cx = QrRect.Left + QrRect.Width / 2f;
                float cy = QrRect.Top + QrRect.Height / 2f;
                QrRect = ClampRect(new RectangleF(cx - size / 2f, cy - size / 2f, size, size));
            }

            if (ShowText && (TextRect.Width < 4 || TextRect.Height < 4))
                Errors.Add("文字区域太小，请调整边距或版式");
            if (ShowBarcode && (BarcodeRect.Width < 8 || BarcodeRect.Height < 8))
                Errors.Add("一维码区域太小，请调整边距或版式");
            if (ShowQr && (QrRect.Width < 8 || QrRect.Height < 8))
                Errors.Add("二维码区域太小，请调整边距或版式");
        }

        private void PrepareBarcode()
        {
            if (!ShowBarcode) return;
            try
            {
                Barcode = BarcodeFactory.EncodeBarcode(Content.EffectiveBarcode, Options.BarcodeType);
            }
            catch (Exception ex)
            {
                Errors.Add(ex.Message);
                return;
            }

            int natural = Barcode.Matrix.Width;
            int available = (int)Math.Floor(BarcodeRect.Width);
            int scale = natural > 0 ? available / natural : 0;
            if (scale < 1)
            {
                Errors.Add("一维码内容过长：需要 " + natural + " 点宽，当前可用 " + available +
                           " 点。请缩短内容、减小边距，或改用 30×25 标签");
                return;
            }
            BarcodeScale = scale;
            if (scale == 1) Warnings.Add("一维码模块宽度仅 1 点，建议缩短内容或使用 300 dpi");
        }

        private void PrepareQr()
        {
            if (!ShowQr) return;
            try
            {
                Qr = BarcodeFactory.EncodeQr(Content.EffectiveQr, Options.QrEcc);
            }
            catch (Exception ex)
            {
                Errors.Add("二维码生成失败：" + ex.Message);
                return;
            }

            int modules = Qr.Matrix.Width;
            int total = modules + 8; // 四模块静区
            int available = (int)Math.Floor(Math.Min(QrRect.Width, QrRect.Height));
            int scale = total > 0 ? available / total : 0;
            if (scale < 1)
            {
                Errors.Add("二维码内容过长：当前版本 v" + Qr.Version + " 需要 " + total + "×" + total +
                           " 点，二维码区域仅 " + available + " 点。请缩短内容、降低纠错等级，或改用 30×25 标签");
                return;
            }
            QrScale = scale;
            if (scale == 1) Warnings.Add("二维码模块宽度仅 1 点，扫描成功率可能偏低，建议缩短内容或使用 300 dpi");
        }

        private void PrepareText(Graphics g)
        {
            if (!ShowText) return;
            if (g == null) return;

            RectangleF inner = Deflate(TextRect, Options.MmToPx(0.25));
            if (inner.Width < 4 || inner.Height < 4)
            {
                Errors.Add("文字区域太小");
                return;
            }

            float maxByZone = inner.Height * 0.95f;
            float requestedMm = (float)Options.FontSizeMm;
            float baseMm = Mode == LayoutMode.TextOnly ? 6.5f : 10.0f;
            float maxBySpec = Options.MmToPx(requestedMm > 0 ? requestedMm : baseMm);
            float maxPx = Math.Min(maxByZone, maxBySpec);
            float minMm = (Mode == LayoutMode.All || Mode == LayoutMode.BarcodeQr) ? 1.3f : 1.55f;
            float minPx = Options.MmToPx(minMm);
            if (maxPx < minPx) maxPx = minPx;

            float fitted;
            if (!TryFitText(g, Text, inner, maxPx, minPx, out fitted))
            {
                TextFontPx = minPx;
                Errors.Add("文本过长，无法在标签内完整显示。请缩短文字、减小字号，或改用 30×25 标签");
                return;
            }
            TextFontPx = fitted;

            if (requestedMm > 0 && fitted < Options.MmToPx(requestedMm) * 0.90f)
            {
                Warnings.Add("文字已自动缩小到 " + (fitted / Options.Dpi * 25.4).ToString("0.0") +
                             " mm：当前版式的文字区域不足");
            }
            if (fitted < Options.MmToPx(1.8))
                Warnings.Add("文字较小（约 " + (fitted / Options.Dpi * 25.4).ToString("0.0") + " mm），建议缩短内容");
        }

        private void ValidateBarcodeHri(Graphics g)
        {
            if (!ShowBarcode || !ShowHri || Barcode == null || g == null) return;
            float minPx = Options.MmToPx(1.1);
            float maxPx = Math.Min(HriRect.Height * 0.9f, Options.MmToPx(2.6));
            if (maxPx < minPx) maxPx = minPx;
            float fitted;
            if (!TryFitText(g, Barcode.DisplayValue, HriRect, maxPx, minPx, out fitted))
            {
                HriFontPx = minPx;
                Warnings.Add("一维码明文较小，可能不易阅读");
            }
            else
            {
                HriFontPx = fitted;
            }
        }

        /// <summary>
        /// 在 [minPx, maxPx] 内二分查找可放下的最大字号。
        /// 旧实现从 maxPx 起每次 -1px 线性扫描，600 dpi 下每行要测上百个 Font，
        /// 批量校验 1000 行就是约 5 万次字体创建与测量，这就是“点打印先卡两秒”的来源。
        /// </summary>
        private bool TryFitText(Graphics g, string text, RectangleF rect, float maxPx, float minPx, out float fitted)
        {
            fitted = minPx;
            if (string.IsNullOrEmpty(text)) return true;

            using (var measureFormat = CreateStringFormat(false))
            {
                if (Fits(g, text, rect, maxPx, measureFormat))
                {
                    fitted = maxPx;
                    return true;
                }
                if (!Fits(g, text, rect, minPx, measureFormat)) return false;

                double lo = minPx;
                double hi = maxPx;
                for (int i = 0; i < 24 && hi - lo > 0.02; i++)
                {
                    double mid = (lo + hi) / 2.0;
                    if (Fits(g, text, rect, (float)mid, measureFormat)) lo = mid;
                    else hi = mid;
                }
                fitted = (float)lo;
                return true;
            }
        }

        private bool Fits(Graphics g, string text, RectangleF rect, float px, StringFormat format)
        {
            using (var font = CreateFont(px, Options.Bold))
            {
                SizeF size = g.MeasureString(text, font, new SizeF(rect.Width, 100000f), format);
                return size.Width <= rect.Width + 1.0f && size.Height <= rect.Height + 1.0f;
            }
        }

        public void Draw(Graphics g)
        {
            if (g == null) return;
            g.SmoothingMode = SmoothingMode.None;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

            using (var brush = new SolidBrush(Color.Black))
            {
                if (ShowBarcode && Barcode != null)
                {
                    DrawBarcode(g, brush);
                }

                if (ShowQr && Qr != null)
                {
                    DrawQr(g, brush);
                }

                if (ShowText)
                {
                    DrawText(g, brush);
                }

                if (ShowBarcode && ShowHri && Barcode != null)
                {
                    DrawHri(g, brush);
                }
            }
        }

        private void DrawBarcode(Graphics g, Brush brush)
        {
            int natural = Barcode.Matrix.Width;
            float width = natural * BarcodeScale;
            float left = BarcodeRect.Left + (BarcodeRect.Width - width) / 2f;
            float top = BarcodeRect.Top;
            float height = BarcodeRect.Height;
            for (int x = 0; x < natural; x++)
            {
                if (Barcode.Matrix[x, 0])
                {
                    g.FillRectangle(brush, left + x * BarcodeScale, top, BarcodeScale, height);
                }
            }
        }

        private void DrawQr(Graphics g, Brush brush)
        {
            int modules = Qr.Matrix.Width;
            int total = modules + 8;
            float size = total * QrScale;
            float left = QrRect.Left + (QrRect.Width - size) / 2f;
            float top = QrRect.Top + (QrRect.Height - size) / 2f;

            using (var white = new SolidBrush(Color.White))
            {
                g.FillRectangle(white, QrRect);
            }

            for (int y = 0; y < modules; y++)
            {
                for (int x = 0; x < modules; x++)
                {
                    if (Qr.Matrix[x, y] == 1)
                    {
                        g.FillRectangle(brush,
                            left + (x + 4) * QrScale,
                            top + (y + 4) * QrScale,
                            QrScale, QrScale);
                    }
                }
            }
        }

        private void DrawText(Graphics g, Brush brush)
        {
            RectangleF inner = Deflate(TextRect, Options.MmToPx(0.25));
            using (var font = CreateFont(TextFontPx, Options.Bold))
            using (var format = CreateStringFormat(false))
            {
                g.DrawString(Text, font, brush, inner, format);
            }
        }

        private void DrawHri(Graphics g, Brush brush)
        {
            if (HriRect.Height < 2) return;
            using (var font = CreateFont(HriFontPx, false))
            using (var format = CreateStringFormat(true))
            {
                g.DrawString(Barcode.DisplayValue, font, brush, HriRect, format);
            }
        }

        private Font CreateFont(float px, bool bold)
        {
            if (px < 1) px = 1;
            string family = string.IsNullOrWhiteSpace(Options.FontFamily) ? UiFontName : Options.FontFamily;
            FontStyle style = bold ? FontStyle.Bold : FontStyle.Regular;
            try
            {
                return new Font(family, px, style, GraphicsUnit.Pixel);
            }
            catch
            {
                return new Font(FontFamily.GenericSansSerif, px, style, GraphicsUnit.Pixel);
            }
        }

        private StringFormat CreateStringFormat(bool hri)
        {
            var format = new StringFormat();
            switch (Options.TextAlign)
            {
                case TextAlign.Left:
                    format.Alignment = StringAlignment.Near; break;
                case TextAlign.Right:
                    format.Alignment = StringAlignment.Far; break;
                default:
                    format.Alignment = StringAlignment.Center; break;
            }
            if (hri) format.Alignment = StringAlignment.Center;
            format.LineAlignment = StringAlignment.Center;
            format.Trimming = StringTrimming.None;
            format.FormatFlags = StringFormatFlags.LineLimit;
            return format;
        }

        private static RectangleF Deflate(RectangleF r, float amount)
        {
            if (r.Width <= amount * 2 || r.Height <= amount * 2) return r;
            return RectangleF.FromLTRB(r.Left + amount, r.Top + amount, r.Right - amount, r.Bottom - amount);
        }
    }
}
