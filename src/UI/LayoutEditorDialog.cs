using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using EasyLabel.Core;

namespace EasyLabel.UI
{
    /// <summary>拖动标签元素位置、拖动角点缩放尺寸。</summary>
    public sealed class LayoutEditorDialog : Form
    {
        private readonly LabelSpec _spec;
        private readonly LabelContent _content;
        private readonly RenderOptions _options;
        private readonly LayoutEditorCanvas _canvas;
        private Label _status;

        public LayoutPlan LayoutResult { get; private set; }

        public LayoutEditorDialog(LabelSpec spec, LabelContent content, RenderOptions options)
        {
            _spec = spec;
            _content = content;
            _options = (options ?? new RenderOptions()).Clone();
            LayoutResult = _options.LayoutPlan != null ? _options.LayoutPlan.Clone() : BuildAutoPlan();
            _options.LayoutPlan = null;

            Text = "布局编辑 — 拖动位置 / 拖动角点缩放";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(940, 650);
            MinimumSize = new Size(760, 540);
            BackColor = Theme.Background;
            Font = Theme.Ui(9f);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Theme.Background
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

            _canvas = new LayoutEditorCanvas(_spec, _content, _options, LayoutResult)
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(10, 10, 10, 2)
            };
            _canvas.StatusChanged += (s, e) => UpdateStatus();
            root.Controls.Add(_canvas, 0, 0);

            _status = Theme.Label("", 8.4f, Theme.Muted);
            _status.AutoSize = false;
            _status.Dock = DockStyle.Fill;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.Padding = new Padding(14, 0, 0, 0);
            root.Controls.Add(_status, 0, 1);

            var bottom = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
            var hint = Theme.Label("拖动元素移动；拖动四角方块缩放。二维码保持正方形。", 8.6f, Theme.Muted);
            hint.AutoSize = false;
            hint.Location = new Point(14, 10);
            hint.Size = new Size(470, 24);

            var reset = UiKit.SecondaryButton("重置自动布局");
            reset.Size = new Size(128, 32);
            reset.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            reset.Click += (s, e) =>
            {
                LayoutResult = BuildAutoPlan();
                _canvas.SetPlan(LayoutResult);
                UpdateStatus();
            };

            var cancel = UiKit.SecondaryButton("取消");
            cancel.Size = new Size(80, 32);
            cancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            cancel.DialogResult = DialogResult.Cancel;

            var ok = UiKit.PrimaryButton("应用布局");
            ok.Size = new Size(104, 32);
            ok.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            ok.Click += (s, e) =>
            {
                LayoutResult = _canvas.Plan.Clone();
                DialogResult = DialogResult.OK;
                Close();
            };

            bottom.Controls.Add(hint);
            bottom.Controls.Add(reset);
            bottom.Controls.Add(cancel);
            bottom.Controls.Add(ok);
            Action placeButtons = () =>
            {
                ok.Location = new Point(bottom.Width - ok.Width - Theme.S(14), Theme.S(8));
                cancel.Location = new Point(ok.Left - cancel.Width - Theme.S(8), Theme.S(8));
                reset.Location = new Point(cancel.Left - reset.Width - Theme.S(16), Theme.S(8));
            };
            bottom.Resize += (s, e) => placeButtons();
            root.Controls.Add(bottom, 0, 2);
            Controls.Add(root);
            AcceptButton = ok;
            CancelButton = cancel;

            Theme.ApplyScale(this, new Size(940, 650));
            placeButtons();

            Shown += (s, e) => UpdateStatus();
        }

        private LayoutPlan BuildAutoPlan()
        {
            try
            {
                RenderOptions options = _options.Clone();
                options.LayoutPlan = null;
                using (var bmp = new Bitmap(1, 1))
                using (var g = Graphics.FromImage(bmp))
                {
                    LabelLayout layout = LabelLayout.Build(_spec, _content, options, g);
                    if (!layout.IsValid)
                    {
                        MessageBox.Show(this, string.Join("\r\n", layout.Errors.ToArray()),
                            "无法进入布局编辑", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return new LayoutPlan();
                    }
                    return LayoutPlan.FromLayout(layout);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "无法进入布局编辑", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return new LayoutPlan();
            }
        }

        private void UpdateStatus()
        {
            if (_canvas == null) return;
            _status.Text = _canvas.GetStatusText();
            _canvas.Invalidate();
        }
    }

    internal sealed class LayoutEditorCanvas : Control
    {
        private enum Kind { None, Text, Barcode, Qr }
        private enum Grip { None, Move, NW, NE, SW, SE }

        private readonly LabelSpec _spec;
        private readonly LabelContent _content;
        private readonly RenderOptions _options;
        private LayoutPlan _plan;
        private Kind _selected = Kind.None;
        private Grip _dragHandle = Grip.None;
        private PointF _dragStart;
        private RectangleF _dragRect;
        private RectangleF _labelRect;
        private float _scale = 1f;
        private bool _dragging;
        private string _lastError = "";

        public event EventHandler StatusChanged;
        public LayoutPlan Plan { get { return _plan; } }

        public LayoutEditorCanvas(LabelSpec spec, LabelContent content, RenderOptions options, LayoutPlan plan)
        {
            _spec = spec;
            _content = content;
            _options = options;
            _plan = plan;
            DoubleBuffered = true;
            BackColor = Theme.Background;
            ResizeRedraw = true;
            TabStop = true;
        }

        public void SetPlan(LayoutPlan plan)
        {
            _plan = plan;
            _selected = Kind.None;
            Invalidate();
        }

        public string GetStatusText()
        {
            if (_selected == Kind.None) return "请选择元素；拖动移动，拖动四角缩放。";
            RectangleF r = GetRect(_selected);
            string name = _selected == Kind.Text ? "文字" : (_selected == Kind.Barcode ? "一维码" : "二维码");
            return name + "  X " + r.X.ToString("0.0") + " mm  Y " + r.Y.ToString("0.0") +
                   " mm  宽 " + r.Width.ToString("0.0") + "  高 " + r.Height.ToString("0.0") + " mm";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            float availableW = Math.Max(80, Width - Theme.S(80));
            float availableH = Math.Max(60, Height - Theme.S(70));
            _scale = Math.Min(availableW / (float)_spec.WidthMm, availableH / (float)_spec.HeightMm);
            if (_scale > 48f) _scale = 48f;
            if (_scale < 1f) _scale = 1f;
            float w = (float)_spec.WidthMm * _scale;
            float h = (float)_spec.HeightMm * _scale;
            float x = (Width - w) / 2f;
            float y = (Height - h) / 2f - Theme.SF(4);
            _labelRect = new RectangleF(x, y, w, h);

            using (var shadow = new SolidBrush(Color.FromArgb(30, 0, 0, 0)))
                g.FillRectangle(shadow, x + Theme.S(7), y + Theme.S(7), w, h);
            using (var white = new SolidBrush(Color.White))
                g.FillRectangle(white, _labelRect);

            _lastError = "";
            try
            {
                RenderOptions preview = _options.Clone();
                preview.LayoutPlan = _plan;
                using (RenderedLabel rendered = LabelRenderer.Render(_spec, _content, preview))
                {
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(rendered.Bitmap, _labelRect);
                }
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
            }

            using (var border = new Pen(Color.FromArgb(150, 158, 170), 1))
                g.DrawRectangle(border, x, y, w, h);

            DrawElement(g, Kind.Text, "文字", Theme.Accent);
            DrawElement(g, Kind.Barcode, "一维码", Color.FromArgb(30, 130, 90));
            DrawElement(g, Kind.Qr, "二维码", Color.FromArgb(205, 130, 30));

            if (!string.IsNullOrEmpty(_lastError))
            {
                using (var font = Theme.Ui(8.5f))
                {
                    TextRenderer.DrawText(g, _lastError, font, new Rectangle(Theme.S(12), Height - Theme.S(30), Width - Theme.S(24), Theme.S(24)),
                        Theme.Error, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
                }
            }
        }

        private void DrawElement(Graphics g, Kind kind, string title, Color color)
        {
            if (!IsActive(kind)) return;
            RectangleF r = ToScreen(GetRect(kind));
            bool selected = _selected == kind;
            using (var fill = new SolidBrush(Color.FromArgb(selected ? 34 : 14, color)))
                g.FillRectangle(fill, r);
            using (var pen = new Pen(color, selected ? 2f : 1f))
            {
                pen.DashStyle = selected ? DashStyle.Solid : DashStyle.Dash;
                g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            }
            using (var font = Theme.Ui(8.2f))
            {
                TextRenderer.DrawText(g, title, font, new Point((int)r.X + 4, (int)r.Y + 3), color,
                    TextFormatFlags.NoPadding);
            }
            if (selected)
            {
                foreach (RectangleF handle in HandleRects(r))
                {
                    using (var fill = new SolidBrush(Color.White))
                    using (var pen = new Pen(color, 1.5f))
                    {
                        g.FillRectangle(fill, handle);
                        g.DrawRectangle(pen, handle.X, handle.Y, handle.Width, handle.Height);
                    }
                }
            }
        }

        private bool IsActive(Kind kind)
        {
            if (kind == Kind.Text) return _content.HasText;
            if (kind == Kind.Barcode) return _content.HasBarcode;
            if (kind == Kind.Qr) return _content.HasQr;
            return false;
        }

        private RectangleF GetRect(Kind kind)
        {
            if (kind == Kind.Text) return _plan.Text;
            if (kind == Kind.Barcode) return _plan.Barcode;
            if (kind == Kind.Qr) return _plan.Qr;
            return RectangleF.Empty;
        }

        private void SetRect(Kind kind, RectangleF rect)
        {
            if (kind == Kind.Text) _plan.Text = rect;
            else if (kind == Kind.Barcode) _plan.Barcode = rect;
            else if (kind == Kind.Qr) _plan.Qr = rect;
        }

        private RectangleF ToScreen(RectangleF mm)
        {
            return new RectangleF(
                _labelRect.X + mm.X * _scale,
                _labelRect.Y + mm.Y * _scale,
                mm.Width * _scale,
                mm.Height * _scale);
        }

        private IEnumerable<RectangleF> HandleRects(RectangleF r)
        {
            const float s = 9f;
            yield return new RectangleF(r.Left - s / 2, r.Top - s / 2, s, s);
            yield return new RectangleF(r.Right - s / 2, r.Top - s / 2, s, s);
            yield return new RectangleF(r.Left - s / 2, r.Bottom - s / 2, s, s);
            yield return new RectangleF(r.Right - s / 2, r.Bottom - s / 2, s, s);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            _dragHandle = Grip.None;
            _dragging = false;
            _dragStart = e.Location;

            foreach (Kind kind in new[] { Kind.Qr, Kind.Barcode, Kind.Text })
            {
                if (!IsActive(kind)) continue;
                RectangleF screen = ToScreen(GetRect(kind));
                Grip handle = HitHandle(screen, e.Location);
                if (handle != Grip.None)
                {
                    _selected = kind;
                    _dragHandle = handle;
                    _dragRect = GetRect(kind);
                    _dragging = true;
                    Invalidate();
                    RaiseStatus();
                    return;
                }
                if (screen.Contains(e.Location))
                {
                    _selected = kind;
                    _dragHandle = Grip.Move;
                    _dragRect = GetRect(kind);
                    _dragging = true;
                    Invalidate();
                    RaiseStatus();
                    return;
                }
            }

            _selected = Kind.None;
            Invalidate();
            RaiseStatus();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (!_dragging)
            {
                UpdateCursor(e.Location);
                return;
            }

            float dx = (e.X - _dragStart.X) / _scale;
            float dy = (e.Y - _dragStart.Y) / _scale;
            RectangleF rect = _dragRect;

            if (_dragHandle == Grip.Move)
            {
                rect.X = Clamp(Snap(rect.X + dx), 0, (float)_spec.WidthMm - rect.Width);
                rect.Y = Clamp(Snap(rect.Y + dy), 0, (float)_spec.HeightMm - rect.Height);
            }
            else if (_selected == Kind.Qr)
            {
                rect = ResizeQr(rect, dx, dy);
            }
            else
            {
                rect = ResizeFree(rect, dx, dy, _selected == Kind.Text ? 2f : 4f);
            }

            SetRect(_selected, rect);
            Invalidate();
            RaiseStatus();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            _dragHandle = Grip.None;
        }

        private RectangleF ResizeFree(RectangleF start, float dx, float dy, float minSize)
        {
            float left = start.Left;
            float top = start.Top;
            float right = start.Right;
            float bottom = start.Bottom;

            if (_dragHandle == Grip.NW || _dragHandle == Grip.SW) left += dx;
            if (_dragHandle == Grip.NE || _dragHandle == Grip.SE) right += dx;
            if (_dragHandle == Grip.NW || _dragHandle == Grip.NE) top += dy;
            if (_dragHandle == Grip.SW || _dragHandle == Grip.SE) bottom += dy;

            left = Math.Max(0, Math.Min(left, right - minSize));
            top = Math.Max(0, Math.Min(top, bottom - minSize));
            right = Math.Min((float)_spec.WidthMm, Math.Max(right, left + minSize));
            bottom = Math.Min((float)_spec.HeightMm, Math.Max(bottom, top + minSize));

            return RectangleF.FromLTRB(Snap(left), Snap(top), Snap(right), Snap(bottom));
        }

        private RectangleF ResizeQr(RectangleF start, float dx, float dy)
        {
            float sx = (_dragHandle == Grip.NE || _dragHandle == Grip.SE) ? dx : -dx;
            float sy = (_dragHandle == Grip.SW || _dragHandle == Grip.SE) ? dy : -dy;
            float size = Math.Max(3f, start.Width + (Math.Abs(sx) >= Math.Abs(sy) ? sx : sy));
            size = Math.Min(size, Math.Min((float)_spec.WidthMm, (float)_spec.HeightMm));
            size = Snap(size);
            float x = (_dragHandle == Grip.NW || _dragHandle == Grip.SW) ? start.Right - size : start.Left;
            float y = (_dragHandle == Grip.NW || _dragHandle == Grip.NE) ? start.Bottom - size : start.Top;
            x = Clamp(x, 0, (float)_spec.WidthMm - size);
            y = Clamp(y, 0, (float)_spec.HeightMm - size);
            return new RectangleF(Snap(x), Snap(y), size, size);
        }

        private static float Snap(float value)
        {
            return (float)(Math.Round(value * 2.0) / 2.0);
        }

        private static float Clamp(float value, float min, float max)
        {
            if (max < min) return min;
            return Math.Max(min, Math.Min(max, value));
        }

        private Grip HitHandle(RectangleF rect, PointF p)
        {
            const float hit = 7f;
            RectangleF[] handles = new RectangleF[4];
            int i = 0;
            foreach (RectangleF r in HandleRects(rect)) handles[i++] = r;
            if (Near(handles[0], p, hit)) return Grip.NW;
            if (Near(handles[1], p, hit)) return Grip.NE;
            if (Near(handles[2], p, hit)) return Grip.SW;
            if (Near(handles[3], p, hit)) return Grip.SE;
            return Grip.None;
        }

        private static bool Near(RectangleF r, PointF p, float pad)
        {
            return p.X >= r.Left - pad && p.X <= r.Right + pad &&
                   p.Y >= r.Top - pad && p.Y <= r.Bottom + pad;
        }

        private void UpdateCursor(PointF p)
        {
            Grip handle = Grip.None;
            foreach (Kind kind in new[] { Kind.Qr, Kind.Barcode, Kind.Text })
            {
                if (!IsActive(kind)) continue;
                RectangleF rect = ToScreen(GetRect(kind));
                handle = HitHandle(rect, p);
                if (handle != Grip.None) break;
                if (rect.Contains(p)) { handle = Grip.Move; break; }
            }
            if (handle == Grip.NW || handle == Grip.SE) Cursor = Cursors.SizeNWSE;
            else if (handle == Grip.NE || handle == Grip.SW) Cursor = Cursors.SizeNESW;
            else if (handle == Grip.Move) Cursor = Cursors.SizeAll;
            else Cursor = Cursors.Default;
        }

        private void RaiseStatus()
        {
            EventHandler handler = StatusChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }
    }
}
