using System;
using System.Drawing;

namespace EasyLabel.Core
{
    /// <summary>自定义元素布局，单位毫米，坐标以标签左上角为原点。</summary>
    public sealed class LayoutPlan
    {
        /// <summary>条码框默认的条形区占比（含明文时），仅用于旧版布局没有记录比例时兜底。</summary>
        public const float DefaultBarHeightRatio = 0.74f;

        /// <summary>条形区与明文区之间默认的间隙（mm）。</summary>
        public const float DefaultHriGapMm = 0.25f;

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

        public static LayoutPlan FromLayout(LabelLayout layout)
        {
            if (layout == null) return new LayoutPlan();
            var plan = new LayoutPlan
            {
                Text = ToMm(layout.TextRect, layout.Options),
                Barcode = ToMm(layout.BarcodeRect, layout.Options),
                Qr = ToMm(layout.QrRect, layout.Options),
                HriGapMm = DefaultHriGapMm
            };

            float barHeight = plan.Barcode.Height;
            if (layout.ShowHri && layout.HriRect.Width > 0 && layout.HriRect.Height > 0)
            {
                RectangleF hri = ToMm(layout.HriRect, layout.Options);
                float gap = hri.Top - (plan.Barcode.Top + plan.Barcode.Height);
                if (gap > 0 && gap <= 2f) plan.HriGapMm = gap;
                plan.Barcode = Union(plan.Barcode, hri);
            }

            if (plan.Barcode.Height > 0 && barHeight > 0)
            {
                // 反算掉“条码高度 %”，使 FromLayout → ApplyLayoutPlan 成为幂等操作。
                float percent = Math.Max(1, layout.Options.BarcodeHeightPercent) / 100f;
                plan.BarHeightRatio = ClampRatio(barHeight / percent / plan.Barcode.Height);
            }

            if (layout.ShowQr && plan.Qr.Width > 0 && plan.Qr.Height > 0)
            {
                // 二维码同理：存回去的应该是“可用区域”，而不是已经按百分比缩放过的实际大小。
                float percent = Math.Max(1, layout.Options.QrSizePercent) / 100f;
                if (Math.Abs(percent - 1f) > 0.001f) plan.Qr = ScaleAboutCenter(plan.Qr, 1f / percent);
            }
            return plan;
        }

        private static float ClampRatio(float value)
        {
            // 无明文时 Barcode 框就是条形区本身，比例会等于 1/百分比（最大约 1.43），
            // 所以上限不能卡在 1，否则“条码高度 %”会被重复应用一次。
            if (float.IsNaN(value) || value <= 0) return 0f;
            if (value < 0.05f) return 0.05f;
            if (value > 2f) return 2f;
            return value;
        }

        private static RectangleF ToMm(RectangleF px, RenderOptions options)
        {
            if (px.Width <= 0 || px.Height <= 0) return RectangleF.Empty;
            return new RectangleF(
                (float)(px.X / options.DotsPerMm),
                (float)(px.Y / options.DotsPerMm),
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

        public void Normalize(LabelSpec spec)
        {
            if (spec == null) return;
            Text = Clamp(Text, spec, 2.0f);
            Barcode = Clamp(Barcode, spec, 4.0f);
            Qr = Clamp(Qr, spec, 3.0f);
            BarHeightRatio = ClampRatio(BarHeightRatio);
            if (HriGapMm <= 0f || HriGapMm > 2f || float.IsNaN(HriGapMm))
                HriGapMm = DefaultHriGapMm;
        }

        private static RectangleF Clamp(RectangleF r, LabelSpec spec, float minSize)
        {
            float w = (float)spec.WidthMm;
            float h = (float)spec.HeightMm;
            float width = (float)Math.Max((double)minSize, Math.Min(w, (double)r.Width));
            float height = (float)Math.Max((double)minSize, Math.Min(h, (double)r.Height));
            float x = Math.Max(0, Math.Min(w - width, r.X));
            float y = Math.Max(0, Math.Min(h - height, r.Y));
            return new RectangleF(x, y, width, height);
        }
    }
}
