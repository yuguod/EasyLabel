using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using EasyLabel.Core;

namespace EasyLabel.UI
{
    /// <summary>布局编辑里可操作的三种元素。</summary>
    public enum LayoutElement
    {
        None,
        Text,
        Barcode,
        Qr
    }

    /// <summary>
    /// 版式布局编辑：在安全区（标签去掉边距的区域）内拖动位置、拖角点缩放，
    /// 支持网格与边缘吸附、方向键微调、对齐到安全区、撤销重做，并且可以就地改边距。
    /// </summary>
    public sealed class LayoutEditorDialog : Form
    {
        private readonly LabelSpec _spec;
        private readonly LabelContent _content;
        private readonly RenderOptions _options;
        private readonly LayoutEditorCanvas _canvas;

        private Button _btnText;
        private Button _btnBarcode;
        private Button _btnQr;
        private NumericUpDown _numX;
        private NumericUpDown _numY;
        private NumericUpDown _numW;
        private NumericUpDown _numH;
        private NumericUpDown _numMT;
        private NumericUpDown _numMB;
        private NumericUpDown _numML;
        private NumericUpDown _numMR;
        private CheckBox _snap;
        private CheckBox _grid;
        private ComboBox _gridStep;
        private Button _undo;
        private Button _redo;
        private Label _status;

        private bool _syncing;

        /// <summary>应用后的布局（坐标以安全区左上角为原点）。</summary>
        public LayoutPlan LayoutResult { get; private set; }

        public double MarginTopMm { get; private set; }
        public double MarginBottomMm { get; private set; }
        public double MarginLeftMm { get; private set; }
        public double MarginRightMm { get; private set; }

        /// <summary>用户没有动过元素（只调了边距或直接点应用），应回到自动布局。</summary>
        public bool UseAutoLayout { get { return !_canvas.PlanTouched; } }

        public LayoutEditorDialog(LabelSpec spec, LabelContent content, RenderOptions options)
        {
            _spec = spec;
            _content = content;
            _options = (options ?? new RenderOptions()).Clone();
            _options.Normalize();
            LayoutResult = _options.LayoutPlan != null ? _options.LayoutPlan.Clone() : BuildAutoPlan();

            MarginTopMm = _options.MarginTopMm;
            MarginBottomMm = _options.MarginBottomMm;
            MarginLeftMm = _options.MarginLeftMm;
            MarginRightMm = _options.MarginRightMm;

            Text = "布局编辑 — 拖动移动 / 拖角缩放 / 方向键微调";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(1120, 760);
            MinimumSize = new Size(900, 620);
            BackColor = Theme.Background;
            Font = Theme.Ui(9f);

            _canvas = new LayoutEditorCanvas(_spec, _content, _options, LayoutResult)
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty
            };
            _canvas.SetMargins(MarginLeftMm, MarginTopMm, MarginRightMm, MarginBottomMm);
            _canvas.StatusChanged += (s, e) => SyncFromCanvas();
            _canvas.PlanChanged += (s, e) => SyncFromCanvas();

            var root = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
            root.Controls.Add(_canvas);
            root.Controls.Add(BuildSidebar());
            root.Controls.Add(BuildBottomBar());
            Controls.Add(root);

            Theme.ApplyScale(this, new Size(1120, 760));
            KeyPreview = true;

            SelectElement(FirstActive());
            SyncFromCanvas();
            Shown += (s, e) =>
            {
                // 高 DPI + 小屏（1366×768）时窗口会比工作区高，底栏会被顶到屏幕外，
                // 看上去就像“编辑窗口没有保存按钮”。显示前先按工作区收一次。
                FitToWorkingArea();
                _canvas.Focus();
            };
        }

        /// <summary>
        /// 把窗口收进当前屏幕的工作区：缩尺寸、夹位置，并把 MinimumSize 一起压下来。
        /// 否则 125%/150% 缩放下 MinimumSize 会被放大到比工作区还大，用户既拖不回去，
        /// 底栏（“应用布局”按钮）也永远停在屏幕外。
        /// </summary>
        private void FitToWorkingArea()
        {
            Rectangle wa;
            try { wa = Screen.FromControl(this).WorkingArea; }
            catch { return; }
            if (wa.Width <= 0 || wa.Height <= 0) return;

            int margin = Theme.S(16);
            int maxW = Math.Max(Theme.S(560), wa.Width - margin * 2);
            int maxH = Math.Max(Theme.S(380), wa.Height - margin * 2);

            MinimumSize = new Size(
                Math.Min(MinimumSize.Width, maxW),
                Math.Min(MinimumSize.Height, maxH));

            int w = Math.Min(Width, maxW);
            int h = Math.Min(Height, maxH);
            var bounds = new Rectangle(Left, Top, w, h);
            if (bounds.Bottom > wa.Bottom) bounds.Y = wa.Bottom - h;
            if (bounds.Right > wa.Right) bounds.X = wa.Right - w;
            bounds.Y = Math.Max(bounds.Y, wa.Top);
            bounds.X = Math.Max(bounds.X, wa.Left);
            Bounds = bounds;
        }

        private Control BuildBottomBar()
        {
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(52), BackColor = Color.White };

            _status = Theme.Label("", 8.6f, Theme.Muted);
            _status.AutoSize = false;
            _status.Location = new Point(Theme.S(16), Theme.S(16));
            _status.Size = new Size(Theme.S(680), Theme.S(22));
            _status.Anchor = AnchorStyles.Left | AnchorStyles.Top;

            var reset = UiKit.SecondaryButton("重置为自动布局");
            reset.Size = new Size(Theme.S(144), Theme.S(32));
            reset.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            reset.Click += (s, e) =>
            {
                _canvas.PushUndo();
                _canvas.SetPlan(BuildAutoPlan());
                SelectElement(FirstActive());
                SyncFromCanvas();
            };

            var cancel = UiKit.SecondaryButton("取消");
            cancel.Size = new Size(Theme.S(80), Theme.S(32));
            cancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            var ok = UiKit.PrimaryButton("应用布局");
            ok.Size = new Size(Theme.S(104), Theme.S(32));
            ok.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            ok.Click += (s, e) => Apply();

            Action place = () =>
            {
                int top = (bottom.Height - ok.Height) / 2;
                ok.Location = new Point(bottom.Width - ok.Width - Theme.S(16), top);
                cancel.Location = new Point(ok.Left - cancel.Width - Theme.S(8), top);
                reset.Location = new Point(cancel.Left - reset.Width - Theme.S(16), top);
                // 状态文字按剩余空间收窄，窄窗口下不会压到“重置为自动布局”按钮上。
                int statusWidth = Math.Max(Theme.S(120), reset.Left - Theme.S(28));
                _status.Width = statusWidth;
            };
            bottom.Resize += (s, e) => place();

            bottom.Controls.Add(_status);
            bottom.Controls.Add(reset);
            bottom.Controls.Add(cancel);
            bottom.Controls.Add(ok);
            place();
            // 回车 = 保存并关闭，Esc = 取消；底栏按钮不在鼠标焦点时也能键盘完成。
            AcceptButton = ok;
            CancelButton = cancel;
            return bottom;
        }

        private Control BuildSidebar()
        {
            var sidebar = new Panel
            {
                Dock = DockStyle.Right,
                Width = Theme.S(316),
                BackColor = Color.White,
                AutoScroll = true,
                Padding = new Padding(Theme.S(16), Theme.S(10), Theme.S(10), Theme.S(10))
            };

            var stack = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                RowCount = 0,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.White,
                Margin = Padding.Empty
            };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            Action<Control> add = c =>
            {
                stack.RowCount++;
                stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                c.Dock = DockStyle.Top;
                stack.Controls.Add(c, 0, stack.RowCount - 1);
            };

            // ---- 元素 ----
            add(SectionTitle("元素"));
            var elementRow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                Dock = DockStyle.Top,
                Padding = new Padding(0, Theme.S(2), 0, Theme.S(6))
            };
            _btnText = ElementButton("文字", LayoutElement.Text);
            _btnBarcode = ElementButton("一维码", LayoutElement.Barcode);
            _btnQr = ElementButton("二维码", LayoutElement.Qr);
            elementRow.Controls.Add(_btnText);
            elementRow.Controls.Add(_btnBarcode);
            elementRow.Controls.Add(_btnQr);
            add(elementRow);

            // ---- 位置与尺寸 ----
            add(SectionTitle("位置与尺寸（相对安全区，mm）"));
            _numX = SideNumeric(-20, 400);
            _numY = SideNumeric(-20, 400);
            _numW = SideNumeric(0.5m, 400);
            _numH = SideNumeric(0.5m, 400);
            add(PairTable("X", _numX, "Y", _numY));
            add(PairTable("宽", _numW, "高", _numH));
            _numX.ValueChanged += (s, e) => ApplyNumbers();
            _numY.ValueChanged += (s, e) => ApplyNumbers();
            _numW.ValueChanged += (s, e) => ApplyNumbers();
            _numH.ValueChanged += (s, e) => ApplyNumbers();

            // ---- 对齐 ----
            add(SectionTitle("对齐到安全区"));
            var alignH = Row();
            alignH.Controls.Add(AlignButton("贴左", LayoutAnchor.Left));
            alignH.Controls.Add(AlignButton("水平居中", LayoutAnchor.CenterHorizontal));
            alignH.Controls.Add(AlignButton("贴右", LayoutAnchor.Right));
            add(alignH);

            var alignV = Row();
            alignV.Controls.Add(AlignButton("贴上", LayoutAnchor.Top));
            alignV.Controls.Add(AlignButton("垂直居中", LayoutAnchor.CenterVertical));
            alignV.Controls.Add(AlignButton("贴下", LayoutAnchor.Bottom));
            add(alignV);

            var fit = UiKit.SecondaryButton("铺满安全区");
            fit.Size = new Size(Theme.S(104), Theme.S(30));
            fit.Margin = new Padding(0, Theme.S(2), 0, Theme.S(10));
            fit.Click += (s, e) =>
            {
                if (_canvas.Selected == LayoutElement.None) return;
                _canvas.PushUndo();
                RectangleF area = _canvas.SafeArea;
                _canvas.SetSelectedRect(new RectangleF(0, 0, area.Width, area.Height));
                SyncFromCanvas();
            };
            add(fit);

            // ---- 安全区（边距）----
            add(SectionTitle("安全区（边距 mm）"));
            _numMT = SideNumeric(-20, 20);
            _numMB = SideNumeric(0, 20);
            _numML = SideNumeric(-20, 20);
            _numMR = SideNumeric(0, 20);
            add(PairTable("上", _numMT, "下", _numMB));
            add(PairTable("左", _numML, "右", _numMR));
            _numMT.ValueChanged += (s, e) => ApplyMargins();
            _numMB.ValueChanged += (s, e) => ApplyMargins();
            _numML.ValueChanged += (s, e) => ApplyMargins();
            _numMR.ValueChanged += (s, e) => ApplyMargins();

            var marginHint = Theme.Label("安全区 = 标签去掉边距后的区域，浅色部分就是边距。\n上/左边距可以填负数，等于把内容整体往左/上挪。", 8.1f, Theme.Muted);
            marginHint.AutoSize = false;
            marginHint.Dock = DockStyle.Top;
            marginHint.Height = Theme.S(40);
            add(marginHint);

            // ---- 网格 ----
            add(SectionTitle("网格与吸附"));
            var toggleRow = Row();
            _grid = UiKit.Check("显示网格", true);
            _grid.CheckedChanged += (s, e) => { _canvas.ShowGrid = _grid.Checked; _canvas.Invalidate(); };
            _snap = UiKit.Check("吸附", true);
            _snap.CheckedChanged += (s, e) => { _canvas.SnapEnabled = _snap.Checked; };
            toggleRow.Controls.Add(_grid);
            toggleRow.Controls.Add(_snap);
            add(toggleRow);

            var stepRow = Row();
            var stepLabel = Theme.Label("间距", 8.6f, Theme.Muted);
            stepLabel.AutoSize = false;
            stepLabel.Width = Theme.S(40);
            stepLabel.Height = Theme.S(28);
            stepLabel.TextAlign = ContentAlignment.MiddleLeft;
            _gridStep = UiKit.Combo();
            _gridStep.Items.AddRange(new object[] { "0.5 mm", "1 mm", "2 mm", "5 mm" });
            _gridStep.SelectedIndex = 0;
            _gridStep.Width = Theme.S(84);
            _gridStep.SelectedIndexChanged += (s, e) =>
            {
                float[] steps = { 0.5f, 1f, 2f, 5f };
                _canvas.GridStep = steps[Math.Max(0, Math.Min(steps.Length - 1, _gridStep.SelectedIndex))];
                _canvas.Invalidate();
            };
            stepRow.Controls.Add(stepLabel);
            stepRow.Controls.Add(_gridStep);
            add(stepRow);

            // ---- 撤销 ----
            add(SectionTitle("操作"));
            var history = Row();
            _undo = UiKit.SecondaryButton("撤销");
            _undo.Size = new Size(Theme.S(84), Theme.S(30));
            _undo.Click += (s, e) => { _canvas.Undo(); SyncFromCanvas(); };
            _redo = UiKit.SecondaryButton("重做");
            _redo.Size = new Size(Theme.S(84), Theme.S(30));
            _redo.Click += (s, e) => { _canvas.Redo(); SyncFromCanvas(); };
            history.Controls.Add(_undo);
            history.Controls.Add(_redo);
            add(history);

            var keys = Theme.Label("元素可以拖到标签外（最多 8 mm），红色斜纹部分\n打印时会被裁掉；方向键 0.1 mm，Shift 1 mm。", 8.1f, Theme.Muted);
            keys.AutoSize = false;
            keys.Dock = DockStyle.Top;
            keys.Height = Theme.S(34);
            add(keys);

            sidebar.Controls.Add(stack);
            return sidebar;
        }

        private static Label SectionTitle(string text)
        {
            var label = Theme.Label(text, 8.8f, Theme.Text, FontStyle.Bold);
            label.AutoSize = false;
            label.Dock = DockStyle.Top;
            label.Height = Theme.S(26);
            label.TextAlign = ContentAlignment.BottomLeft;
            label.Margin = new Padding(0, Theme.S(8), 0, Theme.S(2));
            return label;
        }

        private static FlowLayoutPanel Row()
        {
            return new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoSize = true,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, Theme.S(4))
            };
        }

        private static NumericUpDown SideNumeric(decimal min, decimal max)
        {
            var num = UiKit.Numeric(min, max, 0, 1, 0.1m);
            num.Width = Theme.S(74);
            num.Margin = new Padding(0, Theme.S(2), Theme.S(10), Theme.S(2));
            return num;
        }

        private static TableLayoutPanel PairTable(string l1, Control c1, string l2, Control c2)
        {
            var table = new TableLayoutPanel
            {
                ColumnCount = 4,
                RowCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, Theme.S(4))
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(22)));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(88)));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(22)));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(88)));

            table.Controls.Add(Caption(l1), 0, 0);
            table.Controls.Add(c1, 1, 0);
            table.Controls.Add(Caption(l2), 2, 0);
            table.Controls.Add(c2, 3, 0);
            return table;
        }

        private static Label Caption(string text)
        {
            var label = Theme.Label(text, 8.6f, Theme.Muted);
            label.AutoSize = false;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        private Button ElementButton(string text, LayoutElement element)
        {
            var button = UiKit.SecondaryButton(text);
            button.Size = new Size(Theme.S(80), Theme.S(30));
            button.Margin = new Padding(0, 0, Theme.S(6), 0);
            button.Click += (s, e) => SelectElement(element);
            return button;
        }

        private Button AlignButton(string text, LayoutAnchor anchor)
        {
            var button = UiKit.SecondaryButton(text);
            button.Size = new Size(text.Length > 3 ? Theme.S(84) : Theme.S(58), Theme.S(30));
            button.Margin = new Padding(0, 0, Theme.S(6), Theme.S(6));
            button.Click += (s, e) =>
            {
                if (_canvas.Selected == LayoutElement.None) return;
                _canvas.PushUndo();
                _canvas.AlignSelected(anchor);
                SyncFromCanvas();
            };
            return button;
        }

        private LayoutElement FirstActive()
        {
            if (_content.HasText) return LayoutElement.Text;
            if (_content.HasBarcode) return LayoutElement.Barcode;
            if (_content.HasQr) return LayoutElement.Qr;
            return LayoutElement.None;
        }

        private void SelectElement(LayoutElement element)
        {
            _canvas.Selected = element;

            _btnText.Enabled = _content.HasText;
            _btnBarcode.Enabled = _content.HasBarcode;
            _btnQr.Enabled = _content.HasQr;

            Theme.StyleSecondary(_btnText);
            Theme.StyleSecondary(_btnBarcode);
            Theme.StyleSecondary(_btnQr);
            if (element == LayoutElement.Text) Theme.StylePrimary(_btnText);
            else if (element == LayoutElement.Barcode) Theme.StylePrimary(_btnBarcode);
            else if (element == LayoutElement.Qr) Theme.StylePrimary(_btnQr);

            SyncFromCanvas();
        }

        /// <summary>把画布状态刷到数字框、按钮和状态行；期间不回写画布，避免互相触发。</summary>
        private void SyncFromCanvas()
        {
            if (_syncing) return;
            _syncing = true;
            try
            {
                RectangleF rect = _canvas.SelectedRect;
                bool has = _canvas.Selected != LayoutElement.None && rect.Width > 0;
                _numX.Enabled = _numY.Enabled = _numW.Enabled = _numH.Enabled = has;
                if (has)
                {
                    _numX.Value = ClampTo(_numX, (decimal)rect.X);
                    _numY.Value = ClampTo(_numY, (decimal)rect.Y);
                    _numW.Value = ClampTo(_numW, (decimal)rect.Width);
                    _numH.Value = ClampTo(_numH, (decimal)rect.Height);
                }

                _numMT.Value = ClampTo(_numMT, (decimal)_canvas.MarginTop);
                _numMB.Value = ClampTo(_numMB, (decimal)_canvas.MarginBottom);
                _numML.Value = ClampTo(_numML, (decimal)_canvas.MarginLeft);
                _numMR.Value = ClampTo(_numMR, (decimal)_canvas.MarginRight);

                _undo.Enabled = _canvas.CanUndo;
                _redo.Enabled = _canvas.CanRedo;
                _status.Text = _canvas.GetStatusText();
            }
            finally
            {
                _syncing = false;
            }
        }

        private static decimal ClampTo(NumericUpDown num, decimal value)
        {
            if (value < num.Minimum) value = num.Minimum;
            if (value > num.Maximum) value = num.Maximum;
            return Math.Round(value, 2);
        }

        private void ApplyNumbers()
        {
            if (_syncing) return;
            if (_canvas.Selected == LayoutElement.None) return;
            var rect = new RectangleF((float)_numX.Value, (float)_numY.Value,
                (float)_numW.Value, (float)_numH.Value);
            _canvas.SetSelectedRect(rect);
            SyncFromCanvas();
        }

        private void ApplyMargins()
        {
            if (_syncing) return;
            _canvas.SetMargins((double)_numML.Value, (double)_numMT.Value, (double)_numMR.Value, (double)_numMB.Value);
            SyncFromCanvas();
        }

        private void Apply()
        {
            LayoutResult = _canvas.Plan.Clone();
            MarginLeftMm = _canvas.MarginLeft;
            MarginTopMm = _canvas.MarginTop;
            MarginRightMm = _canvas.MarginRight;
            MarginBottomMm = _canvas.MarginBottom;
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Z)) { _canvas.Undo(); SyncFromCanvas(); return true; }
            if (keyData == (Keys.Control | Keys.Y)) { _canvas.Redo(); SyncFromCanvas(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
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
    }

    /// <summary>布局画布：画安全区与网格，处理拖动、缩放、吸附、撤销和键盘微调。</summary>
    internal sealed class LayoutEditorCanvas : Control
    {
        private readonly LabelSpec _spec;
        private readonly LabelContent _content;
        private readonly RenderOptions _options;
        private LayoutPlan _plan;

        private readonly List<LayoutPlan> _undo = new List<LayoutPlan>();
        private readonly List<LayoutPlan> _redo = new List<LayoutPlan>();

        private LayoutElement _selected = LayoutElement.None;
        private Grip _grip = Grip.None;
        private PointF _dragStart;
        private RectangleF _dragRect;
        private bool _dragging;
        private bool _dragUndoPushed;
        private RectangleF _labelRect;
        private float _scale = 1f;
        private string _lastError = "";

        private double _mL = 1.2, _mT = 1.2, _mR = 1.2, _mB = 1.2;

        private enum Grip { None, Move, NW, NE, SW, SE }

        public event EventHandler StatusChanged;
        public event EventHandler PlanChanged;

        public float GridStep { get; set; }
        public bool SnapEnabled { get; set; }
        public bool ShowGrid { get; set; }

        public double MarginLeft { get { return _mL; } }
        public double MarginTop { get { return _mT; } }
        public double MarginRight { get { return _mR; } }
        public double MarginBottom { get { return _mB; } }

        public LayoutPlan Plan { get { return _plan; } }

        /// <summary>用户是否在画布上动过元素；没动过就表示“仍用自动布局”。</summary>
        public bool PlanTouched { get; private set; }

        public LayoutEditorCanvas(LabelSpec spec, LabelContent content, RenderOptions options, LayoutPlan plan)
        {
            _spec = spec;
            _content = content;
            _options = options;
            _plan = plan ?? new LayoutPlan();
            GridStep = 0.5f;
            SnapEnabled = true;
            ShowGrid = true;
            DoubleBuffered = true;
            BackColor = Theme.Background;
            ResizeRedraw = true;
            TabStop = true;
            SetStyle(ControlStyles.Selectable, true);
        }

        public LayoutElement Selected
        {
            get { return _selected; }
            set
            {
                _selected = value;
                Invalidate();
                RaiseStatus();
            }
        }

        public RectangleF SafeArea
        {
            get { return LayoutPlan.ContentBox(_spec, _mL, _mT, _mR, _mB); }
        }

        /// <summary>
        /// 可放置区域：安全区各边再放出 <see cref="LayoutPlan.OverflowMm"/>，
        /// 所以元素能拖到标签外面——这是手动补偿打印机偏移的一种办法。
        /// </summary>
        public RectangleF AllowArea
        {
            get { return LayoutPlan.AllowBox(_spec, _mL, _mT); }
        }

        public void SetMargins(double left, double top, double right, double bottom)
        {
            _mL = Math.Max(RenderOptions.MarginMinMm, Math.Min(RenderOptions.MarginMaxMm, left));
            _mT = Math.Max(RenderOptions.MarginMinMm, Math.Min(RenderOptions.MarginMaxMm, top));
            _mR = Math.Max(0, Math.Min(RenderOptions.MarginMaxMm, right));
            _mB = Math.Max(0, Math.Min(RenderOptions.MarginMaxMm, bottom));
            _plan.Normalize(_spec, AllowArea);
            Invalidate();
            RaiseStatus();
        }

        public void SetPlan(LayoutPlan plan)
        {
            _plan = plan ?? new LayoutPlan();
            _plan.Normalize(_spec, AllowArea);
            PlanTouched = false;
            Invalidate();
            RaiseStatus();
        }

        public RectangleF SelectedRect
        {
            get { return GetRect(_selected); }
        }

        public void SetSelectedRect(RectangleF rect)
        {
            if (_selected == LayoutElement.None) return;
            RectangleF area = AllowArea;
            float minSize = _selected == LayoutElement.Text ? 2f : (_selected == LayoutElement.Barcode ? 4f : 3f);
            float w = Clamp(rect.Width, minSize, area.Width);
            float h = Clamp(rect.Height, minSize, area.Height);
            float x = Clamp(rect.X, area.Left, area.Right - w);
            float y = Clamp(rect.Y, area.Top, area.Bottom - h);
            SetRect(_selected, new RectangleF(x, y, w, h));
            PlanTouched = true;
            Invalidate();
            RaiseStatus();
            RaisePlanChanged();
        }

        public void AlignSelected(LayoutAnchor anchor)
        {
            if (_selected == LayoutElement.None) return;
            SetRect(_selected, LayoutPlan.AlignRect(GetRect(_selected), SafeArea, anchor));
            PlanTouched = true;
            Invalidate();
            RaiseStatus();
            RaisePlanChanged();
        }

        public void PushUndo()
        {
            _undo.Add(_plan.Clone());
            if (_undo.Count > 80) _undo.RemoveAt(0);
            _redo.Clear();
            RaiseStatus();
        }

        public bool CanUndo { get { return _undo.Count > 0; } }
        public bool CanRedo { get { return _redo.Count > 0; } }

        public void Undo()
        {
            if (_undo.Count == 0) return;
            _redo.Add(_plan.Clone());
            _plan = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            PlanTouched = true;
            Invalidate();
            RaiseStatus();
            RaisePlanChanged();
        }

        public void Redo()
        {
            if (_redo.Count == 0) return;
            _undo.Add(_plan.Clone());
            _plan = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            PlanTouched = true;
            Invalidate();
            RaiseStatus();
            RaisePlanChanged();
        }

        public string GetStatusText()
        {
            RectangleF area = SafeArea;
            string safe = "安全区 " + area.Width.ToString("0.0") + " × " + area.Height.ToString("0.0") + " mm";
            if (_selected == LayoutElement.None) return safe + " · 请选择元素，拖动移动，拖角点缩放；可拖出标签边界。";
            RectangleF r = GetRect(_selected);
            string name = _selected == LayoutElement.Text ? "文字" : (_selected == LayoutElement.Barcode ? "一维码" : "二维码");
            string outside = IsOutsideLabel(r) ? "    ·    超出标签，打印会被裁掉" : "";
            return name + "  X " + r.X.ToString("0.0") + "  Y " + r.Y.ToString("0.0") +
                   "  宽 " + r.Width.ToString("0.0") + "  高 " + r.Height.ToString("0.0") + " mm    ·    " + safe + outside;
        }

        /// <summary>元素是否越出了标签可见区域（坐标是相对安全区的，这里换算回标签再看）。</summary>
        private bool IsOutsideLabel(RectangleF r)
        {
            if (r.Width <= 0 || r.Height <= 0) return false;
            float left = (float)-_mL;
            float top = (float)-_mT;
            float right = left + (float)_spec.WidthMm;
            float bottom = top + (float)_spec.HeightMm;
            return r.Left < left - 0.05f || r.Top < top - 0.05f ||
                   r.Right > right + 0.05f || r.Bottom > bottom + 0.05f;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            // 画布给标签四周留出 8 mm 的越界区，元素拖出去也能看见；
            // 所以缩放要按“标签 + 两侧留白”来算，否则拖出去的部分会被画布边缘挡掉。
            float overflow = LayoutPlan.OverflowMm;
            float availableW = Math.Max(80, Width - Theme.S(40));
            float availableH = Math.Max(60, Height - Theme.S(36));
            _scale = Math.Min(availableW / ((float)_spec.WidthMm + 2 * overflow),
                              availableH / ((float)_spec.HeightMm + 2 * overflow));
            if (_scale > 48f) _scale = 48f;
            if (_scale < 1f) _scale = 1f;
            float w = (float)_spec.WidthMm * _scale;
            float h = (float)_spec.HeightMm * _scale;
            float x = (Width - w) / 2f;
            float y = (Height - h) / 2f;
            _labelRect = new RectangleF(x, y, w, h);

            // 越界区底纹：一眼能看出“拖到这里就会超出标签”。
            var overflowRect = new RectangleF(x - overflow * _scale, y - overflow * _scale,
                w + 2 * overflow * _scale, h + 2 * overflow * _scale);
            using (var band = new SolidBrush(Color.FromArgb(246, 243, 238)))
                g.FillRectangle(band, overflowRect);
            using (var bandPen = new Pen(Color.FromArgb(90, 176, 160, 140), 1f))
            {
                bandPen.DashStyle = DashStyle.Dot;
                g.DrawRectangle(bandPen, overflowRect.X, overflowRect.Y, overflowRect.Width, overflowRect.Height);
            }

            using (var shadow = new SolidBrush(Color.FromArgb(26, 0, 0, 0)))
                g.FillRectangle(shadow, x + Theme.S(4), y + Theme.S(4), w, h);
            using (var white = new SolidBrush(Color.White))
                g.FillRectangle(white, _labelRect);

            _lastError = "";
            try
            {
                RenderOptions preview = _options.Clone();
                preview.MarginLeftMm = _mL;
                preview.MarginTopMm = _mT;
                preview.MarginRightMm = _mR;
                preview.MarginBottomMm = _mB;
                // 打印偏移是“补偿打印机”的，编辑时清零，否则整幅内容跟着偏，坐标就对不上了。
                preview.OffsetXMm = 0;
                preview.OffsetYMm = 0;
                preview.LayoutPlan = _plan.Clone();
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

            DrawSafeArea(g);
            DrawGrid(g);
            DrawElement(g, LayoutElement.Text, "文字", Theme.Accent);
            DrawElement(g, LayoutElement.Barcode, "一维码", Color.FromArgb(30, 130, 90));
            DrawElement(g, LayoutElement.Qr, "二维码", Color.FromArgb(205, 130, 30));

            using (var border = new Pen(Color.FromArgb(150, 158, 170), 1))
                g.DrawRectangle(border, x, y, w, h);

            if (!string.IsNullOrEmpty(_lastError))
            {
                using (var font = Theme.Ui(8.5f))
                {
                    TextRenderer.DrawText(g, _lastError, font,
                        new Rectangle(Theme.S(12), Height - Theme.S(30), Width - Theme.S(24), Theme.S(24)),
                        Theme.Error, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
                }
            }
        }

        private void DrawSafeArea(Graphics g)
        {
            RectangleF area = ToScreen(new RectangleF(0, 0, SafeArea.Width, SafeArea.Height));
            using (var shade = new SolidBrush(Color.FromArgb(20, 70, 90, 140)))
            {
                // 安全区之外（= 边距）铺一层浅色，边距吃了多少空间一眼可见。
                // 负边距会让安全区跑到标签外面，这里只画落在标签内的部分，避免出现反向外框。
                ShadeBand(g, shade, _labelRect.Left, _labelRect.Top, _labelRect.Width, area.Top - _labelRect.Top);
                ShadeBand(g, shade, _labelRect.Left, area.Bottom, _labelRect.Width, _labelRect.Bottom - area.Bottom);
                ShadeBand(g, shade, _labelRect.Left, area.Top, area.Left - _labelRect.Left, area.Height);
                ShadeBand(g, shade, area.Right, area.Top, _labelRect.Right - area.Right, area.Height);
            }
            using (var pen = new Pen(Color.FromArgb(90, Theme.Accent), 1.2f))
            {
                pen.DashStyle = DashStyle.Dash;
                g.DrawRectangle(pen, area.X, area.Y, area.Width, area.Height);
            }
        }

        /// <summary>只在标签范围内填色，宽高非正时跳过（负边距/大边距都会遇到）。</summary>
        private static void ShadeBand(Graphics g, Brush brush, float x, float y, float width, float height)
        {
            if (width <= 0.5f || height <= 0.5f) return;
            g.FillRectangle(brush, x, y, width, height);
        }

        private void DrawGrid(Graphics g)
        {
            if (!ShowGrid || GridStep <= 0.01f) return;
            RectangleF area = ToScreen(new RectangleF(0, 0, SafeArea.Width, SafeArea.Height));
            using (var minor = new Pen(Color.FromArgb(26, 60, 80, 120), 1f))
            using (var major = new Pen(Color.FromArgb(50, 60, 80, 120), 1f))
            {
                GraphicsState state = g.Save();
                g.SetClip(_labelRect, CombineMode.Intersect);
                for (float mm = GridStep; mm < SafeArea.Width - 0.001f; mm += GridStep)
                {
                    float px = area.Left + mm * _scale;
                    g.DrawLine(IsMajor(mm) ? major : minor, px, area.Top, px, area.Bottom);
                }
                for (float mm = GridStep; mm < SafeArea.Height - 0.001f; mm += GridStep)
                {
                    float py = area.Top + mm * _scale;
                    g.DrawLine(IsMajor(mm) ? major : minor, area.Left, py, area.Right, py);
                }
                g.Restore(state);
            }
        }

        private static bool IsMajor(float mm)
        {
            float mod = mm % 5f;
            return mod < 0.01f || mod > 4.99f;
        }

        private void DrawElement(Graphics g, LayoutElement element, string title, Color color)
        {
            if (!IsActive(element)) return;
            RectangleF r = ToScreen(GetRect(element));
            bool selected = _selected == element;
            using (var fill = new SolidBrush(Color.FromArgb(selected ? 30 : 12, color)))
                g.FillRectangle(fill, r);
            using (var pen = new Pen(color, selected ? 2f : 1f))
            {
                pen.DashStyle = selected ? DashStyle.Solid : DashStyle.Dash;
                g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            }
            // 越出标签的部分打印时会被裁掉，用红色斜纹标出来，避免“看着有、打不出来”。
            DrawOutsideTint(g, r);
            using (var font = Theme.Ui(8.2f))
            {
                // 元素框里的内容经常是深色条码/文字，标题加一层白底才不会糊在一起。
                Size titleSize = TextRenderer.MeasureText(title, font);
                var titleRect = new Rectangle((int)r.X + 2, (int)r.Y + 2, titleSize.Width + 4, titleSize.Height);
                using (var backdrop = new SolidBrush(Color.FromArgb(210, Color.White)))
                    g.FillRectangle(backdrop, titleRect);
                TextRenderer.DrawText(g, title, font, new Point(titleRect.X + 2, titleRect.Y), color,
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
                using (var font = Theme.Ui(8f))
                {
                    RectangleF mm = GetRect(element);
                    string size = mm.Width.ToString("0.0") + " × " + mm.Height.ToString("0.0") + " mm";
                    Size sizeText = TextRenderer.MeasureText(size, font);
                    // 优先标在框上方，上面没空间再放下方，避免压住条码/文字。
                    int labelY = (int)r.Top - sizeText.Height - 2;
                    if (labelY < (int)_labelRect.Top) labelY = (int)r.Bottom + 2;
                    var box = new Rectangle((int)r.X, labelY, sizeText.Width + 4, sizeText.Height);
                    using (var backdrop = new SolidBrush(Color.FromArgb(215, Color.White)))
                        g.FillRectangle(backdrop, box);
                    TextRenderer.DrawText(g, size, font, new Point(box.X + 2, box.Y), color,
                        TextFormatFlags.NoPadding);
                }
            }
        }

        /// <summary>把元素落在标签外的部分用红色斜纹盖住：这些内容打印时会被裁掉。</summary>
        private void DrawOutsideTint(Graphics g, RectangleF r)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            if (r.Left >= _labelRect.Left && r.Right <= _labelRect.Right &&
                r.Top >= _labelRect.Top && r.Bottom <= _labelRect.Bottom) return;

            using (var region = new Region(r))
            {
                region.Exclude(_labelRect);
                GraphicsState state = g.Save();
                g.SetClip(region, CombineMode.Replace);
                using (var hatch = new HatchBrush(HatchStyle.WideUpwardDiagonal,
                    Color.FromArgb(190, 206, 66, 66), Color.FromArgb(70, 214, 120, 120)))
                {
                    g.FillRectangle(hatch, r);
                }
                g.Restore(state);
            }
        }

        private bool IsActive(LayoutElement element)
        {
            if (element == LayoutElement.Text) return _content.HasText;
            if (element == LayoutElement.Barcode) return _content.HasBarcode;
            if (element == LayoutElement.Qr) return _content.HasQr;
            return false;
        }

        private RectangleF GetRect(LayoutElement element)
        {
            if (element == LayoutElement.Text) return _plan.Text;
            if (element == LayoutElement.Barcode) return _plan.Barcode;
            if (element == LayoutElement.Qr) return _plan.Qr;
            return RectangleF.Empty;
        }

        private void SetRect(LayoutElement element, RectangleF rect)
        {
            if (element == LayoutElement.Text) _plan.Text = rect;
            else if (element == LayoutElement.Barcode) _plan.Barcode = rect;
            else if (element == LayoutElement.Qr) _plan.Qr = rect;
        }

        private RectangleF ToScreen(RectangleF mm)
        {
            return new RectangleF(
                _labelRect.X + (float)(_mL + mm.X) * _scale,
                _labelRect.Y + (float)(_mT + mm.Y) * _scale,
                mm.Width * _scale,
                mm.Height * _scale);
        }

        private static IEnumerable<RectangleF> HandleRects(RectangleF r)
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
            _grip = Grip.None;
            _dragging = false;
            _dragUndoPushed = false;
            _dragStart = e.Location;

            foreach (LayoutElement element in new[] { LayoutElement.Qr, LayoutElement.Barcode, LayoutElement.Text })
            {
                if (!IsActive(element)) continue;
                RectangleF screen = ToScreen(GetRect(element));
                Grip handle = HitHandle(screen, e.Location);
                if (handle != Grip.None)
                {
                    Selected = element;
                    _grip = handle;
                    _dragRect = GetRect(element);
                    _dragging = true;
                    return;
                }
                if (screen.Contains(e.Location))
                {
                    Selected = element;
                    _grip = Grip.Move;
                    _dragRect = GetRect(element);
                    _dragging = true;
                    return;
                }
            }

            Selected = LayoutElement.None;
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
            RectangleF area = SafeArea;
            RectangleF allow = AllowArea;
            RectangleF rect;

            if (_grip == Grip.Move)
            {
                rect = _dragRect;
                rect.X = Clamp(SnapEdge(_dragRect.X + dx, (float)area.Width - _dragRect.Width),
                    allow.Left, Math.Max(allow.Left, allow.Right - _dragRect.Width));
                rect.Y = Clamp(SnapEdge(_dragRect.Y + dy, (float)area.Height - _dragRect.Height),
                    allow.Top, Math.Max(allow.Top, allow.Bottom - _dragRect.Height));
            }
            else if (_selected == LayoutElement.Qr)
            {
                rect = ResizeSquare(_dragRect, dx, dy, allow, area);
            }
            else
            {
                rect = ResizeFree(_dragRect, dx, dy, allow, area, _selected == LayoutElement.Text ? 2f : 4f);
            }

            if (rect != _dragRect && !_dragUndoPushed)
            {
                PushUndo();
                _dragUndoPushed = true;
            }

            SetRect(_selected, rect);
            PlanTouched = true;
            Invalidate();
            RaiseStatus();
            RaisePlanChanged();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            _grip = Grip.None;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Up || keyData == Keys.Down)
                return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_selected == LayoutElement.None) return;

            float step = e.Shift ? 1f : 0.1f;
            float dx = 0, dy = 0;
            if (e.KeyCode == Keys.Left) dx = -step;
            else if (e.KeyCode == Keys.Right) dx = step;
            else if (e.KeyCode == Keys.Up) dy = -step;
            else if (e.KeyCode == Keys.Down) dy = step;
            else return;

            PushUndo();
            RectangleF allow = AllowArea;
            RectangleF r = GetRect(_selected);
            r.X = Clamp(r.X + dx, allow.Left, Math.Max(allow.Left, allow.Right - r.Width));
            r.Y = Clamp(r.Y + dy, allow.Top, Math.Max(allow.Top, allow.Bottom - r.Height));
            SetRect(_selected, r);
            PlanTouched = true;
            Invalidate();
            RaiseStatus();
            RaisePlanChanged();
            e.Handled = true;
        }

        /// <summary>移动时吸附：先按网格，再吸安全区的左/中/右（上/中/下）。</summary>
        private float SnapEdge(float value, float maxValue)
        {
            if (!SnapEnabled) return value;
            float snapped = LayoutPlan.Snap(value, GridStep);
            float[] targets = { 0f, maxValue / 2f, maxValue };
            float best = float.MaxValue;
            foreach (float target in targets)
            {
                float delta = Math.Abs(value - target);
                if (delta <= 1.0f && delta < best)
                {
                    best = delta;
                    snapped = target;
                }
            }
            return snapped;
        }

        /// <summary>缩放时吸附：网格 + 安全区边线，居中参考没用，只吸两条边。</summary>
        private float SnapLine(float value, float limit)
        {
            if (!SnapEnabled) return value;
            float snapped = LayoutPlan.Snap(value, GridStep);
            if (Math.Abs(value - limit) <= 1.0f) snapped = limit;
            else if (Math.Abs(value) <= 1.0f) snapped = 0f;
            return snapped;
        }

        /// <summary>
        /// 自由缩放。夹取范围是 <paramref name="allow"/>（安全区外放 8 mm），
        /// 吸附目标仍是 <paramref name="safe"/> 的边线，这样“拖出边界”和“对齐安全区”互不干扰。
        /// </summary>
        private RectangleF ResizeFree(RectangleF start, float dx, float dy, RectangleF allow, RectangleF safe, float minSize)
        {
            float left = start.Left;
            float top = start.Top;
            float right = start.Right;
            float bottom = start.Bottom;

            if (_grip == Grip.NW || _grip == Grip.SW) left += dx;
            if (_grip == Grip.NE || _grip == Grip.SE) right += dx;
            if (_grip == Grip.NW || _grip == Grip.NE) top += dy;
            if (_grip == Grip.SW || _grip == Grip.SE) bottom += dy;

            left = Clamp(left, allow.Left, right - minSize);
            top = Clamp(top, allow.Top, bottom - minSize);
            right = Clamp(right, left + minSize, allow.Right);
            bottom = Clamp(bottom, top + minSize, allow.Bottom);

            return RectangleF.FromLTRB(SnapLine(left, safe.Width), SnapLine(top, safe.Height),
                SnapLine(right, safe.Width), SnapLine(bottom, safe.Height));
        }

        private RectangleF ResizeSquare(RectangleF start, float dx, float dy, RectangleF allow, RectangleF safe)
        {
            float sx = (_grip == Grip.NE || _grip == Grip.SE) ? dx : -dx;
            float sy = (_grip == Grip.SW || _grip == Grip.SE) ? dy : -dy;
            float size = Math.Max(3f, start.Width + (Math.Abs(sx) >= Math.Abs(sy) ? sx : sy));
            size = Math.Min(size, allow.Height);
            size = LayoutPlan.Snap(size, GridStep);
            if (size < 3f) size = 3f;
            float x = (_grip == Grip.NW || _grip == Grip.SW) ? start.Right - size : start.Left;
            float y = (_grip == Grip.NW || _grip == Grip.NE) ? start.Bottom - size : start.Top;
            x = Clamp(x, allow.Left, Math.Max(allow.Left, allow.Right - size));
            y = Clamp(y, allow.Top, Math.Max(allow.Top, allow.Bottom - size));
            return new RectangleF(x, y, size, size);
        }

        private static float Clamp(float value, float min, float max)
        {
            if (max < min) return min;
            return Math.Max(min, Math.Min(max, value));
        }

        private Grip HitHandle(RectangleF rect, PointF p)
        {
            const float hit = 7f;
            var handles = new RectangleF[4];
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
            foreach (LayoutElement element in new[] { LayoutElement.Qr, LayoutElement.Barcode, LayoutElement.Text })
            {
                if (!IsActive(element)) continue;
                RectangleF rect = ToScreen(GetRect(element));
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

        private void RaisePlanChanged()
        {
            EventHandler handler = PlanChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }
    }
}
