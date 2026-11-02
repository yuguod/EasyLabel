using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using EasyLabel.Core;

namespace EasyLabel.UI
{
    /// <summary>
    /// 三段式对齐选择器（居左 / 居中 / 居右）。自绘，避免三个按钮边框重复叠加。
    /// 尺寸用设计像素，DPI 交给 Theme.ApplyScale 统一放大。
    /// </summary>
    public sealed class AlignSelector : Control
    {
        private static readonly string[] Labels = { "居左", "居中", "居右" };
        private TextAlign _value = TextAlign.Center;
        private int _hover = -1;

        public event EventHandler ValueChanged;

        public AlignSelector()
            : this(TextAlign.Center)
        {
        }

        public AlignSelector(TextAlign value)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.White;
            Size = new Size(156, 32);
            Cursor = Cursors.Hand;
            TabStop = true;
            _value = value;
        }

        public TextAlign Value
        {
            get { return _value; }
            set
            {
                if (!Enum.IsDefined(typeof(TextAlign), value)) value = TextAlign.Center;
                if (_value == value) return;
                _value = value;
                Invalidate();
                RaiseValueChanged();
            }
        }

        /// <summary>只改显示、不触发事件，用于加载模板时避免连锁重绘。</summary>
        public void SetValueSilent(TextAlign value)
        {
            _value = Enum.IsDefined(typeof(TextAlign), value) ? value : TextAlign.Center;
            Invalidate();
        }

        private int IndexOf(TextAlign value)
        {
            if (value == TextAlign.Left) return 0;
            if (value == TextAlign.Right) return 2;
            return 1;
        }

        private TextAlign ValueOf(int index)
        {
            if (index == 0) return TextAlign.Left;
            if (index == 2) return TextAlign.Right;
            return TextAlign.Center;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            int w = Width - 1;
            int h = Height - 1;
            if (w < 12 || h < 8) return;

            int selected = IndexOf(_value);
            int cellW = w / 3;
            using (GraphicsPath path = Rounded(new Rectangle(0, 0, w, h), 7))
            {
                using (var fill = new SolidBrush(Color.White)) g.FillPath(fill, path);
                if (_hover >= 0 && _hover != selected)
                {
                    Region old = g.Clip;
                    g.SetClip(path);
                    using (var hover = new SolidBrush(Color.FromArgb(246, 249, 255)))
                        g.FillRectangle(hover, _hover * cellW, 0, cellW, h);
                    g.Clip = old;
                }
                if (selected >= 0)
                {
                    Region old = g.Clip;
                    g.SetClip(path);
                    using (var accent = new SolidBrush(Theme.Accent))
                    {
                        Rectangle cell = new Rectangle(selected * cellW, 0, cellW, h);
                        // 首尾格的圆角由整体裁剪保证，这里直接填矩形。
                        g.FillRectangle(accent, cell);
                    }
                    g.Clip = old;
                }
                using (var pen = new Pen(Theme.Border, 1f)) g.DrawPath(pen, path);
            }

            using (var font = Theme.Ui(9.2f))
            {
                for (int i = 0; i < 3; i++)
                {
                    Rectangle cell = new Rectangle(i * cellW, 0, i == 2 ? w - i * cellW : cellW, h);
                    Color color = i == selected ? Color.White : Theme.Text;
                    TextRenderer.DrawText(g, Labels[i], font, cell, color,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                    if (i > 0 && i != selected && i - 1 != selected)
                    {
                        using (var pen = new Pen(Color.FromArgb(226, 230, 236), 1f))
                            g.DrawLine(pen, i * cellW, 4, i * cellW, h - 4);
                    }
                }
            }
        }

        private static GraphicsPath Rounded(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Max(1, radius * 2);
            if (rect.Width < d || rect.Height < d)
            {
                path.AddRectangle(rect);
                return path;
            }
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button != MouseButtons.Left) return;
            int cellW = Math.Max(1, (Width - 1) / 3);
            int index = Math.Max(0, Math.Min(2, e.X / cellW));
            Value = ValueOf(index);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int cellW = Math.Max(1, (Width - 1) / 3);
            int index = Math.Max(0, Math.Min(2, e.X / cellW));
            if (index != _hover)
            {
                _hover = index;
                Cursor = Cursors.Hand;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1)
            {
                _hover = -1;
                Invalidate();
            }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Left || keyData == Keys.Right) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int index = IndexOf(_value);
            if (e.KeyCode == Keys.Left && index > 0)
            {
                Value = ValueOf(index - 1);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Right && index < 2)
            {
                Value = ValueOf(index + 1);
                e.Handled = true;
            }
        }

        private void RaiseValueChanged()
        {
            EventHandler handler = ValueChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }
    }
}
