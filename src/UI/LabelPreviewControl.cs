using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace EasyLabel.UI
{
    public sealed class LabelPreviewControl : Control
    {
        private Bitmap _image;
        private string _caption = "";
        private string _placeholder = "标签预览";

        public LabelPreviewControl()
        {
            DoubleBuffered = true;
            BackColor = Theme.Background;
            ResizeRedraw = true;
        }

        public void SetImage(Bitmap image, string caption)
        {
            if (ReferenceEquals(_image, image)) return;
            if (_image != null) _image.Dispose();
            _image = image;
            _caption = caption ?? "";
            Invalidate();
        }

        public void SetPlaceholder(string text)
        {
            _placeholder = text ?? "";
            if (_image != null)
            {
                _image.Dispose();
                _image = null;
            }
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _image != null)
            {
                _image.Dispose();
                _image = null;
            }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            if (_image == null)
            {
                using (var pen = new Pen(Theme.Border, 1) { DashStyle = DashStyle.Dash })
                {
                    var r = new Rectangle(Theme.S(24), Theme.S(24), Width - Theme.S(48), Height - Theme.S(48));
                    g.DrawRectangle(pen, r);
                }
                TextRenderer.DrawText(g, _placeholder, Theme.Ui(10f), ClientRectangle, Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }

            int availW = Math.Max(40, Width - Theme.S(92));
            int availH = Math.Max(40, Height - Theme.S(100));
            float scale = Math.Min((float)availW / _image.Width, (float)availH / _image.Height);
            if (scale > 6f) scale = 6f;
            if (scale < 0.1f) scale = 0.1f;

            float w = _image.Width * scale;
            float h = _image.Height * scale;
            float x = (Width - w) / 2f;
            float y = (Height - h) / 2f - Theme.SF(4);

            using (var shadow = new SolidBrush(Color.FromArgb(28, 0, 0, 0)))
            {
                g.FillRectangle(shadow, x + Theme.S(6), y + Theme.S(6), w, h);
            }
            using (var white = new SolidBrush(Color.White))
            {
                g.FillRectangle(white, x, y, w, h);
            }

            var oldInterpolation = g.InterpolationMode;
            var oldPixelOffset = g.PixelOffsetMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(_image, new RectangleF(x, y, w, h));
            g.InterpolationMode = oldInterpolation;
            g.PixelOffsetMode = oldPixelOffset;

            using (var border = new Pen(Color.FromArgb(160, 168, 180), 1))
            {
                g.DrawRectangle(border, x, y, w, h);
            }

            DrawDimensions(g, x, y, w, h);

            if (!string.IsNullOrEmpty(_caption))
            {
                using (var font = Theme.Ui(8.5f))
                {
                    TextRenderer.DrawText(g, _caption, font,
                        new Rectangle(Theme.S(16), Height - Theme.S(34), Width - Theme.S(32), Theme.S(24)), Theme.Muted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }
            }
        }

        private void DrawDimensions(Graphics g, float x, float y, float w, float h)
        {
            if (_image == null) return;
            using (var pen = new Pen(Color.FromArgb(150, 158, 170), 1))
            using (var font = Theme.Ui(8.2f))
            using (var brush = new SolidBrush(Theme.Muted))
            {
                // 顶部宽度
                float topY = Math.Max(Theme.S(8), y - Theme.S(16));
                g.DrawLine(pen, x, topY, x + w, topY);
                g.DrawLine(pen, x, topY - Theme.S(4), x, topY + Theme.S(4));
                g.DrawLine(pen, x + w, topY - Theme.S(4), x + w, topY + Theme.S(4));
                string widthText = Math.Round(_image.Width * 25.4 / GetDpi(), 1).ToString("0.#") + " mm";
                var sz = g.MeasureString(widthText, font);
                using (var bg = new SolidBrush(BackColor))
                {
                    g.FillRectangle(bg, x + w / 2 - sz.Width / 2 - Theme.S(3), topY - sz.Height - 1, sz.Width + Theme.S(6), sz.Height + 2);
                }
                g.DrawString(widthText, font, brush, x + w / 2 - sz.Width / 2, topY - sz.Height - 1);

                // 左侧高度
                float leftX = Math.Max(Theme.S(8), x - Theme.S(16));
                g.DrawLine(pen, leftX, y, leftX, y + h);
                g.DrawLine(pen, leftX - Theme.S(4), y, leftX + Theme.S(4), y);
                g.DrawLine(pen, leftX - Theme.S(4), y + h, leftX + Theme.S(4), y + h);
                string heightText = Math.Round(_image.Height * 25.4 / GetDpi(), 1).ToString("0.#") + " mm";
                var state = g.Save();
                g.TranslateTransform(leftX - Theme.S(5), y + h / 2);
                g.RotateTransform(-90);
                var hsz = g.MeasureString(heightText, font);
                g.DrawString(heightText, font, brush, -hsz.Width / 2, -hsz.Height);
                g.Restore(state);
            }
        }

        private int GetDpi()
        {
            try
            {
                if (_image != null && _image.HorizontalResolution > 0)
                    return (int)Math.Round(_image.HorizontalResolution);
            }
            catch { }
            return 203;
        }
    }
}
