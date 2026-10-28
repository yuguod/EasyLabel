using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace EasyLabel.Core
{
    public sealed class RenderedLabel : IDisposable
    {
        public Bitmap Bitmap { get; private set; }
        public byte[] PackedBits { get; private set; }
        public int Stride { get; private set; }
        public int WidthDots { get; private set; }
        public int HeightDots { get; private set; }
        public LabelSpec Spec { get; private set; }
        public int Dpi { get; private set; }
        public LayoutMode Mode { get; private set; }
        public List<string> Warnings { get; private set; }
        public string Info { get; private set; }

        public RenderedLabel(Bitmap bitmap, byte[] packedBits, int stride, int width, int height,
            LabelSpec spec, int dpi, LayoutMode mode, List<string> warnings, string info)
        {
            Bitmap = bitmap;
            PackedBits = packedBits;
            Stride = stride;
            WidthDots = width;
            HeightDots = height;
            Spec = spec;
            Dpi = dpi;
            Mode = mode;
            Warnings = warnings ?? new List<string>();
            Info = info ?? "";
        }

        public void Dispose()
        {
            if (Bitmap != null)
            {
                Bitmap.Dispose();
                Bitmap = null;
            }
        }
    }

    public static class LabelRenderer
    {
        public static RenderedLabel Render(LabelSpec spec, LabelContent content, RenderOptions options)
        {
            if (spec == null) throw new ArgumentNullException("spec");
            if (content == null) throw new ArgumentNullException("content");
            if (options == null) throw new ArgumentNullException("options");
            options.Normalize();

            int w = spec.WidthDots(options.Dpi);
            int h = spec.HeightDots(options.Dpi);
            if (w < 8 || h < 8) throw new LabelRenderException(new[] { "标签尺寸太小" });

            using (var source = new Bitmap(w, h, PixelFormat.Format24bppRgb))
            {
                try { source.SetResolution(options.Dpi, options.Dpi); } catch { }
                LabelLayout layout;
                using (var g = Graphics.FromImage(source))
                {
                    Configure(g);
                    g.Clear(Color.White);
                    layout = LabelLayout.Build(spec, content, options, g);
                    if (!layout.IsValid)
                        throw new LabelRenderException(layout.Errors);
                    layout.Draw(g);
                }

                byte[] packed;
                int stride;
                Pack(source, out packed, out stride);
                Bitmap preview = Create1bppBitmap(packed, w, h, stride, options.Dpi);
                return new RenderedLabel(preview, packed, stride, w, h, spec, options.Dpi,
                    layout.Mode, layout.Warnings, layout.InfoText);
            }
        }

        public static ValidationResult Validate(LabelSpec spec, LabelContent content, RenderOptions options)
        {
            var result = new ValidationResult();
            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
            {
                Configure(g);
                var layout = LabelLayout.Build(spec, content, options, g);
                result.Errors.AddRange(layout.Errors);
                result.Warnings.AddRange(layout.Warnings);
            }
            return result;
        }

        public static RenderedLabel RenderTestPage(LabelSpec spec, RenderOptions options, string printerName)
        {
            if (options == null) options = new RenderOptions();
            options.Normalize();

            var content = new LabelContent
            {
                Text = AppInfo.ProductName + " 打印测试\n" + spec.Name + " / " + options.Dpi + " dpi\n" +
                       DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                ShowQr = true,
                QrValue = "EASYLABEL TEST " + spec.Name + " " + options.Dpi + "dpi",
                UseTextForQr = false,
                ShowBarcode = false
            };

            int w = spec.WidthDots(options.Dpi);
            int h = spec.HeightDots(options.Dpi);
            using (var source = new Bitmap(w, h, PixelFormat.Format24bppRgb))
            {
                try { source.SetResolution(options.Dpi, options.Dpi); } catch { }
                using (var g = Graphics.FromImage(source))
                {
                    Configure(g);
                    g.Clear(Color.White);

                    // 外框和角标，用于核对实际打印尺寸。
                    using (var pen = new Pen(Color.Black, 1))
                    {
                        g.DrawRectangle(pen, 0, 0, w - 1, h - 1);
                        int tick = Math.Max(2, (int)Math.Round(h * 0.06));
                        g.DrawLine(pen, 0, 0, tick, 0);
                        g.DrawLine(pen, 0, 0, 0, tick);
                        g.DrawLine(pen, w - 1, 0, w - 1 - tick, 0);
                        g.DrawLine(pen, w - 1, 0, w - 1, tick);
                        g.DrawLine(pen, 0, h - 1, tick, h - 1);
                        g.DrawLine(pen, 0, h - 1, 0, h - 1 - tick);
                        g.DrawLine(pen, w - 1, h - 1, w - 1 - tick, h - 1);
                        g.DrawLine(pen, w - 1, h - 1, w - 1, h - 1 - tick);
                    }

                    var testOptions = options.Clone();
                    testOptions.LayoutMode = LayoutMode.TextQr;
                    testOptions.ShowBarcodeText = false;
                    var layout = LabelLayout.Build(spec, content, testOptions, g);
                    if (layout.IsValid) layout.Draw(g);
                }

                byte[] packed;
                int stride;
                Pack(source, out packed, out stride);
                Bitmap preview = Create1bppBitmap(packed, w, h, stride, options.Dpi);
                var warnings = new List<string>();
                warnings.Add("测试页：请用尺子核对 " + spec.Name + " 尺寸");
                if (!string.IsNullOrEmpty(printerName)) warnings.Add("打印机：" + printerName);
                return new RenderedLabel(preview, packed, stride, w, h, spec, options.Dpi,
                    LayoutMode.TextQr, warnings, "测试页 · " + spec.Name + " · " + options.Dpi + " dpi");
            }
        }

        private static void Configure(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.None;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.CompositingQuality = CompositingQuality.HighSpeed;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
        }

        private static void Pack(Bitmap source, out byte[] packed, out int stride)
        {
            // 逐行按 3 字节/像素取亮度，非 24bpp 会直接读错位，宁可早抛也别打出花屏。
            if (source.PixelFormat != PixelFormat.Format24bppRgb)
                throw new ArgumentException("点阵打包只支持 24bppRgb 位图，当前为 " + source.PixelFormat, "source");

            int w = source.Width;
            int h = source.Height;
            stride = (w + 7) / 8;
            packed = new byte[stride * h];

            var rect = new Rectangle(0, 0, w, h);
            BitmapData data = source.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                // 自下而上的位图 Stride 为负，行首指针要按带符号的 Stride 累加。
                int sourceStride = Math.Abs(data.Stride);
                byte[] row = new byte[sourceStride];
                for (int y = 0; y < h; y++)
                {
                    IntPtr rowPtr = IntPtr.Add(data.Scan0, y * data.Stride);
                    Marshal.Copy(rowPtr, row, 0, sourceStride);
                    int dstRow = y * stride;
                    for (int x = 0; x < w; x++)
                    {
                        int i = x * 3;
                        int b = row[i];
                        int g = row[i + 1];
                        int r = row[i + 2];
                        int lum = (r * 299 + g * 587 + b * 114) / 1000;
                        if (lum < 128)
                        {
                            packed[dstRow + (x >> 3)] |= (byte)(1 << (7 - (x & 7)));
                        }
                    }
                }
            }
            finally
            {
                source.UnlockBits(data);
            }
        }

        private static Bitmap Create1bppBitmap(byte[] packed, int w, int h, int stride, int dpi)
        {
            var bmp = new Bitmap(w, h, PixelFormat.Format1bppIndexed);
            try { bmp.SetResolution(dpi, dpi); } catch { }
            try
            {
                ColorPalette palette = bmp.Palette;
                palette.Entries[0] = Color.White;
                palette.Entries[1] = Color.Black;
                bmp.Palette = palette;
            }
            catch { }

            var rect = new Rectangle(0, 0, w, h);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format1bppIndexed);
            try
            {
                int destStride = Math.Abs(data.Stride);
                for (int y = 0; y < h; y++)
                {
                    IntPtr rowPtr = IntPtr.Add(data.Scan0, y * data.Stride);
                    int copy = Math.Min(stride, destStride);
                    Marshal.Copy(packed, y * stride, rowPtr, copy);
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }
            return bmp;
        }
    }
}
