using System;
using System.Drawing;

namespace EasyLabel.Core
{
    /// <summary>
    /// 自定义元素布局，单位毫米。
    /// <para>
    /// 坐标原点在 **安全区左上角**（安全区 = 标签去掉四边距后的区域），
    /// 所以改边距时整个版式会跟着内缩/外扩，边距在自定义布局下同样有效。
    /// v1.8 及更早的模版里存的是以标签左上角为原点的绝对坐标，
    /// 读取时由 <see cref="TemplateStore"/> 按当时的边距平移过来。
    /// </para>
    /// </summary>
    public sealed class LayoutPlan
    {
        /// <summary>条码框默认的条形区占比（含明文时），仅用于旧版布局没有记录比例时兜底。</summary>
        public const float DefaultBarHeightRatio = 0.74f;

        /// <summary>条形区与明文区之间默认的间隙（mm）。</summary>
        public const float DefaultHriGapMm = 0.25f;

        /// <summary>写入模版文件时标记坐标系，用于区分旧版绝对坐标。</summary>
        public const string SpaceMargins = "margins";

        /// <summary>
        /// 允许元素越出标签的最大距离（mm）。布局编辑里可以自由把元素拖到标签外——
        /// 这是手动补偿打印机偏移的一种办法；打印时超出标签的部分自然被裁掉。
        /// </summary>
        public const float OverflowMm = 8f;

        public RectangleF Text { get; set; }

        /// <summary>一维码框：含明文时为“条形区 + 明文区”的外接矩形。</summary>
        public RectangleF Barcode { get; set; }
        public RectangleF Qr { get; set; }

        /// <summary>
        /// 条形区占 <see cref="Barcode"/> 高度的比例，已剔除“条码高度 %”的影响，
        /// 因此再次应用布局时不会重复缩放。0 表示未记录，按默认 0.74 / 1.0 兜底。
        /// </summary>
        public float BarHeightRatio { get; set; }

        /// <summary>条形区与明文区之间的间隙（mm），随布局一起保存以做到完全复现。</summary>
        public float HriGapMm { get; set; }

        public LayoutPlan()
        {
            Text = RectangleF.Empty;
            Barcode = RectangleF.Empty;
            Qr = RectangleF.Empty;
            BarHeightRatio = 0f;
            HriGapMm = DefaultHriGapMm;
        }

        public LayoutPlan Clone()
        {
            return new LayoutPlan
            {
                Text = Text,
                Barcode = Barcode,
                Qr = Qr,
                BarHeightRatio = BarHeightRatio,
                HriGapMm = HriGapMm
            };
        }

        /// <summary>安全区：标签去掉四边距后的可用区域，宽高至少保留 2 mm。</summary>
        public static RectangleF ContentBox(LabelSpec spec, double marginLeft, double marginTop,
            double marginRight, double marginBottom)
        {
            if (spec == null) return RectangleF.Empty;
            double w = Math.Max(2.0, spec.WidthMm - Math.Max(0, marginLeft) - Math.Max(0, marginRight));
            double h = Math.Max(2.0, spec.HeightMm - Math.Max(0, marginTop) - Math.Max(0, marginBottom));
            return new RectangleF(0, 0, (float)w, (float)h);
        }

        public static RectangleF ContentBox(LabelSpec spec, RenderOptions options)
        {
            if (options == null) return ContentBox(spec, 0, 0, 0, 0);
            return ContentBox(spec, options.MarginLeftMm, options.MarginTopMm,
                options.MarginRightMm, options.MarginBottomMm);
        }

        /// <summary>
        /// 可放置区域（本坐标系的毫米矩形）：标签四边各放出 <see cref="OverflowMm"/>。
        /// 负边距会把安全区原点推到标签外，这里换算时一并扣掉，保证“标签外 8 mm”这个边界一致。
        /// </summary>
        public static RectangleF AllowBox(LabelSpec spec, RenderOptions options)
        {
            if (spec == null) return RectangleF.Empty;
            double mL = options == null ? 0 : options.MarginLeftMm;
            double mT = options == null ? 0 : options.MarginTopMm;
            return new RectangleF(
                (float)(-OverflowMm - mL),
                (float)(-OverflowMm - mT),
                (float)(spec.WidthMm + 2 * OverflowMm),
                (float)(spec.HeightMm + 2 * OverflowMm));
        }

        public static RectangleF AllowBox(LabelSpec spec, double marginLeftMm, double marginTopMm)
        {
            return AllowBox(spec, new RenderOptions
            {
                MarginLeftMm = marginLeftMm,
                MarginTopMm = marginTopMm
            });
        }

        /// <summary>把自动布局（已含边距）转换成本坐标系下的相对坐标。</summary>
        public static LayoutPlan FromLayout(LabelLayout layout)
        {
            if (layout == null) return new LayoutPlan();
            RenderOptions options = layout.Options;
            double mL = options == null ? 0 : options.MarginLeftMm;
            double mT = options == null ? 0 : options.MarginTopMm;

            var plan = new LayoutPlan
            {
                Text = ToMm(layout.TextRect, options, mL, mT),
                Barcode = ToMm(layout.BarcodeRect, options, mL, mT),
                Qr = ToMm(layout.QrRect, options, mL, mT),
                HriGapMm = DefaultHriGapMm
            };

            float barHeight = plan.Barcode.Height;
            if (layout.ShowHri && layout.HriRect.Width > 0 && layout.HriRect.Height > 0)
            {
                RectangleF hri = ToMm(layout.HriRect, options, mL, mT);
                float gap = hri.Top - (plan.Barcode.Top + plan.Barcode.Height);
                if (gap > 0 && gap <= 2f) plan.HriGapMm = gap;
                plan.Barcode = Union(plan.Barcode, hri);
            }

            if (plan.Barcode.Height > 0 && barHeight > 0)
            {
                // 反算掉“条码高度 %”，使 FromLayout → ApplyLayoutPlan 成为幂等操作。
                float percent = Math.Max(1, options.BarcodeHeightPercent) / 100f;
                plan.BarHeightRatio = ClampRatio(barHeight / percent / plan.Barcode.Height);
            }

            if (layout.ShowQr && plan.Qr.Width > 0 && plan.Qr.Height > 0)
            {
                // 二维码同理：存回去的应该是“可用区域”，而不是已经按百分比缩放过的实际大小。
                float percent = Math.Max(1, options.QrSizePercent) / 100f;
                if (Math.Abs(percent - 1f) > 0.001f) plan.Qr = ScaleAboutCenter(plan.Qr, 1f / percent);
            }
            return plan;
        }

        /// <summary>把旧版（标签左上角为原点）的布局平移到安全区坐标系。</summary>
        public LayoutPlan ToMarginSpace(float marginLeftMm, float marginTopMm)
        {
            var plan = Clone();
            plan.Text = Offset(plan.Text, -marginLeftMm, -marginTopMm);
            plan.Barcode = Offset(plan.Barcode, -marginLeftMm, -marginTopMm);
            plan.Qr = Offset(plan.Qr, -marginLeftMm, -marginTopMm);

            // 平移后可能落成负数（旧布局本来就压着边距），夹回安全区左下角。
            float w = Math.Max(2f, plan.MaxRight());
            float h = Math.Max(2f, plan.MaxBottom());
            plan.ClampToBox(new RectangleF(0, 0, w, h));
            return plan;
        }

        private float MaxRight()
        {
            float v = 0f;
            if (Text.Width > 0) v = Math.Max(v, Text.Right);
            if (Barcode.Width > 0) v = Math.Max(v, Barcode.Right);
            if (Qr.Width > 0) v = Math.Max(v, Qr.Right);
            return v;
        }

        private float MaxBottom()
        {
            float v = 0f;
            if (Text.Height > 0) v = Math.Max(v, Text.Bottom);
            if (Barcode.Height > 0) v = Math.Max(v, Barcode.Bottom);
            if (Qr.Height > 0) v = Math.Max(v, Qr.Bottom);
            return v;
        }

        private static RectangleF Offset(RectangleF r, float dx, float dy)
        {
            if (r.Width <= 0 || r.Height <= 0) return r;
            return new RectangleF(r.X + dx, r.Y + dy, r.Width, r.Height);
        }

        private static float ClampRatio(float value)
        {
            // 无明文时 Barcode 框就是条形区本身，比例会等于 1/百分比（最大约 1.43），
            // 所以上限不能卡在 1，否则“条码高度 %”会被重复应用一次。
            if (float.IsNaN(value) || value <= 0) return 0f;
            if (value < 0.05f) value = 0.05f;
            if (value > 2f) value = 2f;
            return value;
        }

        private static RectangleF ToMm(RectangleF px, RenderOptions options, double mL, double mT)
        {
            if (options == null || px.Width <= 0 || px.Height <= 0) return RectangleF.Empty;
            return new RectangleF(
                (float)(px.X / options.DotsPerMm - mL),
                (float)(px.Y / options.DotsPerMm - mT),
                (float)(px.Width / options.DotsPerMm),
                (float)(px.Height / options.DotsPerMm));
        }

        private static RectangleF Union(RectangleF a, RectangleF b)
        {
            if (a.Width <= 0 || a.Height <= 0) return b;
            if (b.Width <= 0 || b.Height <= 0) return a;
            float left = Math.Min(a.Left, b.Left);
            float top = Math.Min(a.Top, b.Top);
            float right = Math.Max(a.Right, b.Right);
            float bottom = Math.Max(a.Bottom, b.Bottom);
            return RectangleF.FromLTRB(left, top, right, bottom);
        }

        private static RectangleF ScaleAboutCenter(RectangleF r, float factor)
        {
            if (factor <= 0 || float.IsNaN(factor) || float.IsInfinity(factor)) return r;
            float w = r.Width * factor;
            float h = r.Height * factor;
            float cx = r.Left + r.Width / 2f;
            float cy = r.Top + r.Height / 2f;
            return new RectangleF(cx - w / 2f, cy - h / 2f, w, h);
        }

        /// <summary>按标签整幅裁剪（旧行为，保留给只拿到规格的调用方）。</summary>
        public void Normalize(LabelSpec spec)
        {
            if (spec == null) return;
            Normalize(spec, new RectangleF(0, 0, (float)spec.WidthMm, (float)spec.HeightMm));
        }

        /// <summary>按安全区裁剪：超出安全区的元素会被拉回，所以边距变大时版式会跟着收窄。</summary>
        public void Normalize(LabelSpec spec, RectangleF box)
        {
            if (spec == null) return;
            if (box.Width <= 0 || box.Height <= 0)
                box = new RectangleF(0, 0, (float)spec.WidthMm, (float)spec.HeightMm);
            ClampToBox(box);
            BarHeightRatio = ClampRatio(BarHeightRatio);
            if (HriGapMm <= 0f || HriGapMm > 2f || float.IsNaN(HriGapMm))
                HriGapMm = DefaultHriGapMm;
        }

        public void Normalize(LabelSpec spec, RenderOptions options)
        {
            // 允许越界：安全区只用来定位原点，元素可以故意放到标签外做手动补偿。
            Normalize(spec, AllowBox(spec, options));
        }

        private void ClampToBox(RectangleF box)
        {
            Text = Clamp(Text, box, 2.0f);
            Barcode = Clamp(Barcode, box, 4.0f);
            Qr = Clamp(Qr, box, 3.0f);
        }

        private static RectangleF Clamp(RectangleF r, RectangleF box, float minSize)
        {
            if (r.Width <= 0 || r.Height <= 0) return RectangleF.Empty;
            float width = (float)Math.Max((double)minSize, Math.Min((double)box.Width, (double)r.Width));
            float height = (float)Math.Max((double)minSize, Math.Min((double)box.Height, (double)r.Height));
            // 只保证“左上角还在可放置范围内”，不再为了把整块塞进去而平移或缩小：
            // 元素比安全区大属于正常用法（越界打印），硬塞会让它跑到负坐标，看着像没生效。
            float x = Math.Max(box.X, Math.Min((float)box.Right, r.X));
            float y = Math.Max(box.Y, Math.Min((float)box.Bottom, r.Y));
            return new RectangleF(x, y, width, height);
        }

        // ---- 编辑辅助：吸附与对齐 ----

        /// <summary>按步长吸附；步长为 0 时原样返回。</summary>
        public static float Snap(float value, float step)
        {
            if (step <= 0.01f) return value;
            return (float)(Math.Round(value / step) * step);
        }

        public static RectangleF Snap(RectangleF rect, float step)
        {
            if (step <= 0.01f) return rect;
            return new RectangleF(Snap(rect.X, step), Snap(rect.Y, step),
                Math.Max(0.1f, Snap(rect.Width, step)), Math.Max(0.1f, Snap(rect.Height, step)));
        }

        /// <summary>把矩形对齐到安全区的某条边或中线，尺寸不变。</summary>
        public static RectangleF AlignRect(RectangleF rect, RectangleF area, LayoutAnchor anchor)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return rect;
            float x = rect.X;
            float y = rect.Y;
            switch (anchor)
            {
                case LayoutAnchor.Left:
                    x = area.Left;
                    break;
                case LayoutAnchor.CenterHorizontal:
                    x = area.Left + (area.Width - rect.Width) / 2f;
                    break;
                case LayoutAnchor.Right:
                    x = area.Right - rect.Width;
                    break;
                case LayoutAnchor.Top:
                    y = area.Top;
                    break;
                case LayoutAnchor.CenterVertical:
                    y = area.Top + (area.Height - rect.Height) / 2f;
                    break;
                case LayoutAnchor.Bottom:
                    y = area.Bottom - rect.Height;
                    break;
            }
            return new RectangleF(x, y, rect.Width, rect.Height);
        }
    }

    /// <summary>元素相对安全区的对齐锚点。</summary>
    public enum LayoutAnchor
    {
        Left,
        CenterHorizontal,
        Right,
        Top,
        CenterVertical,
        Bottom
    }
}
