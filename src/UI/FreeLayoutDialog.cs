using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using EasyLabel.Core;

namespace EasyLabel.UI
{
    /// <summary>
    /// 自由排版编辑：在一个标签上任意摆放多个文字块、一维码和二维码。
    /// 支持拖动/拖角缩放、网格吸附、方向键微调、六向对齐、撤销重做，元素内容支持占位符。
    /// </summary>
    public sealed class FreeLayoutDialog : Form
    {
        private readonly LabelSpec _spec;
        private readonly LabelContent _content;
        private readonly RenderOptions _options;
        private readonly FreeLayoutCanvas _canvas;

        private ListBox _list;
        private TextBox _name;
        private TextBox _text;
        private NumericUpDown _fontMm;
        private CheckBox _autoFont;
        private CheckBox _bold;
        private ComboBox _align;
        private ComboBox _font;
        private NumericUpDown _x;
        private NumericUpDown _y;
        private NumericUpDown _w;
        private NumericUpDown _h;
        private NumericUpDown _mt;
        private NumericUpDown _mb;
        private NumericUpDown _ml;
        private NumericUpDown _mr;
        private CheckBox _snap;
        private ComboBox _gridStep;
        private Label _status;
        private Label _hint;
        private Button _delete;
        private Button _duplicate;

        private bool _syncing;

        private readonly FreeLayout _initialLayout;

        public FreeLayout LayoutResult
        {
            get { return _canvas != null ? _canvas.CurrentLayout : _initialLayout; }
        }

        public bool UseAutoLayout { get; private set; }

        public double MarginTopMm { get; private set; }
        public double MarginBottomMm { get; private set; }
        public double MarginLeftMm { get; private set; }
        public double MarginRightMm { get; private set; }

        public FreeLayoutDialog(LabelSpec spec, LabelContent content, RenderOptions options, FreeLayout layout)
        {
            _spec = spec ?? LabelSpec.Label40x15;
            _content = content ?? new LabelContent();
            _options = (options ?? new RenderOptions()).Clone();
            _options.Normalize();
            _options.OffsetXMm = 0;
            _options.OffsetYMm = 0;

            _initialLayout = layout != null && layout.HasElements ? layout.Clone() : BuildInitial();
            _initialLayout.Normalize(_spec, _options);

            MarginTopMm = _options.MarginTopMm;
            MarginBottomMm = _options.MarginBottomMm;
            MarginLeftMm = _options.MarginLeftMm;
            MarginRightMm = _options.MarginRightMm;

            Text = "自由排版 — 多文字块 / 一维码 / 二维码";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(1180, 800);
            MinimumSize = new Size(980, 660);
            BackColor = Theme.Background;
            Font = Theme.Ui(9f);
            KeyPreview = true;

            _canvas = new FreeLayoutCanvas(_spec, _content, _options, _initialLayout)
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty
            };
            _canvas.SetMargins(MarginLeftMm, MarginTopMm, MarginRightMm, MarginBottomMm);
            _canvas.SelectionChanged += (s, e) => { SyncFromCanvas(); };
            _canvas.LayoutChanged += (s, e) => { UpdateListItem(); SyncNumbers(); };
            _canvas.StatusChanged += (s, e) => { if (_status != null) _status.Text = _canvas.GetStatusText(); };
            _canvas.ElementActivated += (s, e) => { if (_text != null) _text.Focus(); };

            var root = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
            root.Controls.Add(_canvas);
            root.Controls.Add(BuildSidebar());
            root.Controls.Add(BuildBottomBar());
            Controls.Add(root);

            Theme.ApplyScale(this, new Size(1180, 800));

            RefreshList();
            _canvas.SelectedIndex = LayoutResult.Elements.Count > 0 ? 0 : -1;
            SyncFromCanvas();
            Shown += (s, e) =>
            {
                FitToWorkingArea();
                _canvas.Focus();
            };
        }

        /// <summary>把窗口收进当前屏幕工作区，避免高缩放/小屏下底栏按钮跑到屏幕外。</summary>
        private void FitToWorkingArea()
        {
            Rectangle area = Screen.FromControl(this).WorkingArea;
            int w = Math.Min(Width, area.Width - Theme.S(24));
            int h = Math.Min(Height, area.Height - Theme.S(24));
            MinimumSize = new Size(Math.Min(MinimumSize.Width, w), Math.Min(MinimumSize.Height, h));
            Size = new Size(Math.Max(MinimumSize.Width, w), Math.Max(MinimumSize.Height, h));
            Location = new Point(
                Math.Max(area.Left, Math.Min(Left, area.Right - Width)),
                Math.Max(area.Top, Math.Min(Top, area.Bottom - Height)));
        }

        private FreeLayout BuildInitial()
        {
            try
            {
                using (var bmp = new Bitmap(1, 1))
                using (var g = Graphics.FromImage(bmp))
                {
                    LabelLayout auto = LabelLayout.Build(_spec, _content, _options, g);
                    if (auto.IsValid)
                    {
                        FreeLayout free = FreeLayout.FromAuto(auto);
                        if (free.HasElements) return free;
                    }
                }
            }
            catch { }

            var fallback = new FreeLayout();
            int index = 0;
            if (_content.HasText)
            {
                fallback.Elements.Add(new FreeElement
                {
                    Kind = FreeElementKind.Text,
                    Name = "文字",
                    Content = "{text}",
                    Box = FreeLayout.DefaultBox(_spec, FreeElementKind.Text, index++)
                });
            }
            if (_content.HasBarcode)
            {
                fallback.Elements.Add(new FreeElement
                {
                    Kind = FreeElementKind.Barcode,
                    Name = "一维码",
                    Content = "{barcode}",
                    Box = FreeLayout.DefaultBox(_spec, FreeElementKind.Barcode, index++)
                });
            }
            if (_content.HasQr)
            {
                fallback.Elements.Add(new FreeElement
                {
                    Kind = FreeElementKind.Qr,
                    Name = "二维码",
                    Content = "{qr}",
                    Box = FreeLayout.DefaultBox(_spec, FreeElementKind.Qr, index++)
                });
            }
            if (!fallback.HasElements)
            {
                fallback.Elements.Add(new FreeElement
                {
                    Kind = FreeElementKind.Text,
                    Name = "文字",
                    Content = "{text}",
                    Box = FreeLayout.DefaultBox(_spec, FreeElementKind.Text, 0)
                });
            }
            return fallback;
        }

        // ------------------------------------------------------------------ 侧栏

        private Control BuildSidebar()
        {
            var side = new Panel
            {
                Dock = DockStyle.Right,
                Width = Theme.S(400),
                BackColor = Theme.Card,
                Padding = new Padding(Theme.S(12), Theme.S(8), Theme.S(8), Theme.S(8))
            };
            side.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Border))
                    e.Graphics.DrawLine(pen, 0, 0, 0, side.Height);
            };

            var stack = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Theme.Card,
                Padding = new Padding(0)
            };
            stack.SizeChanged += (s, e) => FitStack(stack, side);

            // ---------------------------------------------------------------- 元素列表
            stack.Controls.Add(Heading("元素"));
            var addRow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                BackColor = Theme.Card,
                Margin = new Padding(0, 0, 0, 6)
            };
            Button addText = SmallButton("+ 文字", Theme.S(84));
            addText.Click += (s, e) => AddElement(FreeElementKind.Text);
            Button addBar = SmallButton("+ 一维码", Theme.S(92));
            addBar.Click += (s, e) => AddElement(FreeElementKind.Barcode);
            Button addQr = SmallButton("+ 二维码", Theme.S(92));
            addQr.Click += (s, e) => AddElement(FreeElementKind.Qr);
            addRow.Controls.Add(addText);
            addRow.Controls.Add(addBar);
            addRow.Controls.Add(addQr);
            stack.Controls.Add(addRow);

            _list = new ListBox
            {
                Height = Theme.S(118),
                IntegralHeight = false,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Theme.Ui(9.2f),
                Margin = new Padding(0, 0, 0, 8)
            };
            _list.SelectedIndexChanged += (s, e) =>
            {
                if (_syncing) return;
                _canvas.SelectedIndex = _list.SelectedIndex;
            };
            stack.Controls.Add(_list);

            // ---------------------------------------------------------------- 属性
            stack.Controls.Add(SectionTitle("选中元素"));

            var props = PropertyTable(5);
            _name = UiKit.TextBox(false, Theme.S(26));
            _name.TextChanged += (s, e) => UpdateSelected(delegate(FreeElement el) { el.Name = _name.Text; }, true, false);
            PropRow(props, 0, "名称", _name);

            var fontHost = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Theme.Card,
                Margin = new Padding(0)
            };
            _autoFont = UiKit.Check("自动", true);
            _autoFont.Width = Theme.S(62);
            _autoFont.Margin = new Padding(0, Theme.S(4), Theme.S(4), 0);
            _fontMm = UiKit.Numeric(1.2m, 20m, 5m, 1, 0.2m);
            _fontMm.Width = Theme.S(76);
            _fontMm.Enabled = false;
            _fontMm.Margin = new Padding(0, Theme.S(2), Theme.S(4), 0);
            var fontUnit = Theme.Label("mm", 8.6f, Theme.Muted);
            fontUnit.AutoSize = true;
            fontUnit.Margin = new Padding(0, Theme.S(6), 0, 0);
            _autoFont.CheckedChanged += (s, e) =>
            {
                if (_syncing) return;
                _fontMm.Enabled = !_autoFont.Checked;
                if (_autoFont.Checked)
                    UpdateSelected(delegate(FreeElement el) { el.FontSizeMm = 0; }, false, false);
                else
                    UpdateSelected(delegate(FreeElement el) { el.FontSizeMm = (double)_fontMm.Value; }, false, false);
            };
            _fontMm.ValueChanged += (s, e) =>
            {
                if (_syncing || _autoFont.Checked) return;
                UpdateSelected(delegate(FreeElement el) { el.FontSizeMm = (double)_fontMm.Value; }, false, false);
            };
            fontHost.Controls.Add(_autoFont);
            fontHost.Controls.Add(_fontMm);
            fontHost.Controls.Add(fontUnit);
            PropRow(props, 1, "字号", fontHost);

            _bold = UiKit.Check("加粗", false);
            _bold.CheckedChanged += (s, e) =>
                UpdateSelected(delegate(FreeElement el) { el.Bold = _bold.Checked; }, false, false);
            PropRow(props, 2, "样式", _bold);

            _align = UiKit.Combo();
            _align.Items.AddRange(new object[] { "左对齐", "居中", "右对齐" });
            _align.SelectedIndex = 1;
            _align.SelectedIndexChanged += (s, e) =>
            {
                if (_syncing) return;
                UpdateSelected(delegate(FreeElement el) { el.Align = (TextAlign)Math.Max(0, _align.SelectedIndex); }, false, false);
            };
            PropRow(props, 3, "对齐", _align);

            _font = UiKit.FontFamilyCombo();
            _font.Items.Insert(0, "跟随全局字体");
            _font.SelectedIndex = 0;
            _font.SelectedIndexChanged += (s, e) =>
            {
                if (_syncing) return;
                string family = _font.SelectedIndex <= 0 ? "" : Convert.ToString(_font.SelectedItem);
                UpdateSelected(delegate(FreeElement el) { el.FontFamily = family; }, false, false);
            };
            PropRow(props, 4, "字体", _font);
            stack.Controls.Add(props);

            var contentHost = new Panel
            {
                Height = Theme.S(112),
                BackColor = Theme.Card,
                Margin = new Padding(0, 0, 0, 6)
            };
            var contentLabel = Theme.Label("内容", 8.8f, Theme.Muted);
            contentLabel.AutoSize = false;
            contentLabel.Dock = DockStyle.Top;
            contentLabel.Height = Theme.S(20);
            contentLabel.TextAlign = ContentAlignment.MiddleLeft;
            _text = UiKit.TextBox(true, Theme.S(88));
            _text.Dock = DockStyle.Fill;
            _text.AcceptsReturn = true;
            _text.ScrollBars = ScrollBars.Vertical;
            _text.Font = Theme.Ui(9.4f);
            _text.TextChanged += (s, e) => UpdateSelected(delegate(FreeElement el) { el.Content = _text.Text; }, false, false);
            contentHost.Controls.Add(_text);
            contentHost.Controls.Add(contentLabel);
            stack.Controls.Add(contentHost);

            _hint = Theme.Label("内容支持占位符：" + LabelTokens.HelpText, 8.2f, Theme.Muted);
            _hint.AutoSize = false;
            _hint.Height = Theme.S(44);
            _hint.Margin = new Padding(0, 0, 0, 6);
            stack.Controls.Add(_hint);

            var actions = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                BackColor = Theme.Card,
                Margin = new Padding(0, 0, 0, 6)
            };
            _duplicate = SmallButton("复制一份", Theme.S(94));
            _duplicate.Click += (s, e) => DuplicateSelected();
            _delete = SmallButton("删除", Theme.S(66));
            _delete.Click += (s, e) => DeleteSelected();
            actions.Controls.Add(_duplicate);
            actions.Controls.Add(_delete);
            stack.Controls.Add(actions);

            // ---------------------------------------------------------------- 位置与尺寸
            stack.Controls.Add(SectionTitle("位置与尺寸（mm）"));
            var position = QuadTable(new[] { "X", "Y", "宽", "高" },
                new Control[] { _x = SideNumeric(-60, 320), _y = SideNumeric(-60, 320),
                                _w = SideNumeric(1, 320), _h = SideNumeric(1, 320) });
            _x.ValueChanged += (s, e) => ApplyNumbers();
            _y.ValueChanged += (s, e) => ApplyNumbers();
            _w.ValueChanged += (s, e) => ApplyNumbers();
            _h.ValueChanged += (s, e) => ApplyNumbers();
            stack.Controls.Add(position);

            stack.Controls.Add(SectionTitle("对齐到安全区"));
            var alignRow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoSize = true,
                BackColor = Theme.Card,
                Margin = new Padding(0, 0, 0, 6)
            };
            alignRow.Controls.Add(AlignButton("左", LayoutAnchor.Left));
            alignRow.Controls.Add(AlignButton("水平", LayoutAnchor.CenterHorizontal));
            alignRow.Controls.Add(AlignButton("右", LayoutAnchor.Right));
            alignRow.Controls.Add(AlignButton("上", LayoutAnchor.Top));
            alignRow.Controls.Add(AlignButton("垂直", LayoutAnchor.CenterVertical));
            alignRow.Controls.Add(AlignButton("下", LayoutAnchor.Bottom));
            stack.Controls.Add(alignRow);

            // ---------------------------------------------------------------- 网格与边距
            stack.Controls.Add(SectionTitle("网格与边距"));
            var snapRow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                BackColor = Theme.Card,
                Margin = new Padding(0, 0, 0, 4)
            };
            _snap = UiKit.Check("网格吸附", true);
            _snap.Width = Theme.S(92);
            _snap.Margin = new Padding(0, Theme.S(4), Theme.S(6), 0);
            _snap.CheckedChanged += (s, e) => _canvas.SnapEnabled = _snap.Checked;
            _gridStep = UiKit.Combo();
            _gridStep.Width = Theme.S(84);
            _gridStep.Items.AddRange(new object[] { "0.5 mm", "1 mm", "2 mm", "5 mm" });
            _gridStep.SelectedIndex = 0;
            _gridStep.SelectedIndexChanged += (s, e) => _canvas.GridStep = GridStepMm();
            snapRow.Controls.Add(_snap);
            snapRow.Controls.Add(_gridStep);
            stack.Controls.Add(snapRow);

            var margins = QuadTable(new[] { "上", "下", "左", "右" },
                new Control[] { _mt = SideNumeric(-20, 20), _mb = SideNumeric(-20, 20),
                                _ml = SideNumeric(-20, 20), _mr = SideNumeric(0, 20) });
            _mt.Value = (decimal)MarginTopMm;
            _mb.Value = (decimal)MarginBottomMm;
            _ml.Value = (decimal)MarginLeftMm;
            _mr.Value = (decimal)MarginRightMm;
            _mt.ValueChanged += (s, e) => ApplyMargins();
            _mb.ValueChanged += (s, e) => ApplyMargins();
            _ml.ValueChanged += (s, e) => ApplyMargins();
            _mr.ValueChanged += (s, e) => ApplyMargins();
            stack.Controls.Add(margins);

            var note = Theme.Label(
                "坐标以安全区左上角为原点。元素可以拖到标签外，超出部分打印时会被裁掉，用来手动补偿打印机偏移。",
                8.2f, Theme.Muted);
            note.AutoSize = false;
            note.Height = Theme.S(52);
            note.Margin = new Padding(0, Theme.S(2), 0, 0);
            stack.Controls.Add(note);

            side.Controls.Add(stack);
            side.Resize += (s, e) => FitStack(stack, side);
            FitStack(stack, side);
            return side;
        }

        private static Label Heading(string text)
        {
            var label = Theme.Label(text, 10.5f, Theme.Text, FontStyle.Bold);
            label.AutoSize = false;
            label.Height = Theme.S(28);
            label.Margin = new Padding(0, 0, 0, 2);
            return label;
        }

        private static Label SectionTitle(string text)
        {
            var label = Theme.Label(text, 9.4f, Theme.Text, FontStyle.Bold);
            label.AutoSize = false;
            label.Height = Theme.S(26);
            label.Margin = new Padding(0, Theme.S(10), 0, Theme.S(2));
            return label;
        }

        /// <summary>属性表：左标签 + 右控件，固定行高，避免自适应把控件压扁。</summary>
        private static TableLayoutPanel PropertyTable(int rows)
        {
            var table = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = rows,
                BackColor = Theme.Card,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(58)));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < rows; i++)
                table.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(34)));
            table.Height = Theme.S(34) * rows;
            return table;
        }

        private static void PropRow(TableLayoutPanel table, int row, string label, Control control)
        {
            if (row >= table.RowCount) return;
            var caption = Theme.Label(label, 8.8f, Theme.Muted);
            caption.AutoSize = false;
            caption.Dock = DockStyle.Fill;
            caption.TextAlign = ContentAlignment.MiddleLeft;
            caption.Margin = new Padding(0);
            control.Dock = DockStyle.Fill;
            control.Margin = new Padding(0, Theme.S(3), 0, Theme.S(3));
            table.Controls.Add(caption, 0, row);
            table.Controls.Add(control, 1, row);
        }

        /// <summary>四列一行标签 + 一行输入框（用于 X/Y/宽/高 与四边距）。</summary>
        private static TableLayoutPanel QuadTable(string[] labels, Control[] controls)
        {
            var table = new TableLayoutPanel
            {
                ColumnCount = labels.Length,
                RowCount = 2,
                BackColor = Theme.Card,
                Margin = new Padding(0, 0, 0, 4),
                Padding = new Padding(0)
            };
            for (int i = 0; i < labels.Length; i++)
            {
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / labels.Length));
                var caption = Theme.Label(labels[i], 8.4f, Theme.Muted);
                caption.AutoSize = false;
                caption.Dock = DockStyle.Fill;
                caption.TextAlign = ContentAlignment.MiddleLeft;
                caption.Margin = new Padding(0);
                table.Controls.Add(caption, i, 0);
                controls[i].Dock = DockStyle.Fill;
                controls[i].Margin = new Padding(0, 0, Theme.S(6), 0);
                table.Controls.Add(controls[i], i, 1);
            }
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(18)));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(30)));
            table.Height = Theme.S(48);
            return table;
        }

        private Button AlignButton(string text, LayoutAnchor anchor)
        {
            Button button = SmallButton(text, text.Length > 2 ? Theme.S(62) : Theme.S(46));
            button.Click += (s, e) =>
            {
                if (_canvas.SelectedIndex < 0) return;
                _canvas.PushUndo();
                _canvas.AlignSelected(anchor);
            };
            return button;
        }

        private static Button SmallButton(string text, int width)
        {
            Button button = UiKit.SecondaryButton(text);
            button.Size = new Size(width, Theme.S(32));
            button.Margin = new Padding(0, 0, Theme.S(6), Theme.S(4));
            return button;
        }

        private static NumericUpDown SideNumeric(decimal min, decimal max)
        {
            NumericUpDown num = UiKit.Numeric(min, max, 0, 1, 0.1m);
            num.Width = Theme.S(78);
            num.Margin = new Padding(0, 0, Theme.S(6), 0);
            return num;
        }

        private static void FitStack(FlowLayoutPanel stack, Panel side)
        {
            // 纵向滚动条一出现就会吃掉宽度，所以宽度始终预留滚动条的份额，
            // 否则先按整宽排好、再出现滚动条就多出一条横向滚动条。
            int width = stack.ClientSize.Width - stack.Padding.Horizontal
                        - SystemInformation.VerticalScrollBarWidth - 4;
            if (width < 120) return;
            foreach (Control child in stack.Controls)
            {
                if (child is FlowLayoutPanel) continue; // 按钮行按自身内容排版
                if (child.Width != width) child.Width = width;
            }
        }

        private Control BuildBottomBar()
        {
            var bottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = Theme.S(58),
                BackColor = Theme.Card,
                Padding = new Padding(Theme.S(16), Theme.S(10), Theme.S(16), Theme.S(10))
            };
            bottom.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Border))
                    e.Graphics.DrawLine(pen, 0, 0, bottom.Width, 0);
            };

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = Theme.S(430),
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = Theme.Card
            };
            Button apply = UiKit.PrimaryButton("应用排版");
            apply.Size = new Size(Theme.S(110), Theme.S(36));
            apply.Click += (s, e) => Apply(false);
            Button cancel = UiKit.SecondaryButton("取消");
            cancel.Size = new Size(Theme.S(78), Theme.S(36));
            cancel.Margin = new Padding(0, 0, Theme.S(8), 0);
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Button reset = UiKit.SecondaryButton("恢复自动排版");
            reset.Size = new Size(Theme.S(122), Theme.S(36));
            reset.Margin = new Padding(0, 0, Theme.S(8), 0);
            reset.Click += (s, e) =>
            {
                if (MessageBox.Show(this, "恢复后不再使用自由排版，元素按当前边距自动排布。\r\n继续？",
                        "恢复自动排版", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                Apply(true);
            };
            buttons.Controls.Add(apply);
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(reset);
            CancelButton = cancel;

            _status = Theme.Label("", 8.6f, Theme.Muted);
            _status.AutoSize = false;
            _status.Dock = DockStyle.Fill;
            _status.TextAlign = ContentAlignment.MiddleLeft;

            bottom.Controls.Add(_status);
            bottom.Controls.Add(buttons);
            return bottom;
        }

        // ------------------------------------------------------------------ 状态同步

        private void RefreshList()
        {
            if (_list == null) return;
            _syncing = true;
            try
            {
                int keep = _canvas.SelectedIndex;
                _list.BeginUpdate();
                _list.Items.Clear();
                for (int i = 0; i < LayoutResult.Elements.Count; i++)
                    _list.Items.Add(Describe(i, LayoutResult.Elements[i]));
                if (keep >= 0 && keep < _list.Items.Count) _list.SelectedIndex = keep;
                _list.EndUpdate();
            }
            finally { _syncing = false; }
        }

        private static string Describe(int index, FreeElement element)
        {
            string name = string.IsNullOrWhiteSpace(element.Name)
                ? element.KindName + (index + 1)
                : element.Name.Trim();
            string preview = (element.Content ?? "").Replace("\r", " ").Replace("\n", " ");
            if (preview.Length > 14) preview = preview.Substring(0, 14) + "…";
            return (index + 1) + ". " + name + "　" + preview;
        }

        /// <summary>只刷新当前行的文字，拖动时不要整表重建。</summary>
        private void UpdateListItem()
        {
            if (_list == null) return;
            int index = _canvas.SelectedIndex;
            if (index < 0 || index >= _list.Items.Count || index >= LayoutResult.Elements.Count) return;
            string text = Describe(index, LayoutResult.Elements[index]);
            if (Convert.ToString(_list.Items[index]) == text) return;
            _syncing = true;
            try { _list.Items[index] = text; }
            finally { _syncing = false; }
        }

        private void SyncFromCanvas()
        {
            if (_list != null)
            {
                _syncing = true;
                try
                {
                    int index = _canvas.SelectedIndex;
                    _list.SelectedIndex = index >= 0 && index < _list.Items.Count ? index : -1;
                }
                finally { _syncing = false; }
            }

            FreeElement element = Selected;
            bool has = element != null;
            _name.Enabled = _text.Enabled = _bold.Enabled = has;
            _align.Enabled = _font.Enabled = _fontMm.Enabled = has;
            _autoFont.Enabled = has;
            _x.Enabled = _y.Enabled = _w.Enabled = _h.Enabled = has;
            _delete.Enabled = _duplicate.Enabled = has;
            if (!has)
            {
                _syncing = true;
                try
                {
                    _name.Text = "";
                    _text.Text = "";
                }
                finally { _syncing = false; }
                if (_status != null) _status.Text = _canvas.GetStatusText();
                return;
            }

            _syncing = true;
            try
            {
                _name.Text = element.Name ?? "";
                _text.Text = element.Content ?? "";
                bool auto = element.FontSizeMm <= 0;
                _autoFont.Checked = auto;
                _fontMm.Enabled = !auto;
                if (!auto) _fontMm.Value = ClampValue(_fontMm, (decimal)element.FontSizeMm);
                bool isText = element.Kind == FreeElementKind.Text;
                // 字号/对齐只对文字块生效；字体对一维码明文也生效，所以保留可用。
                _autoFont.Enabled = isText;
                _fontMm.Enabled = isText && !auto;
                _bold.Enabled = isText;
                _align.Enabled = isText;
                _font.Enabled = true;
                _bold.Checked = element.Bold;
                _align.SelectedIndex = (int)element.Align;
                string family = string.IsNullOrWhiteSpace(element.FontFamily) ? "" : element.FontFamily;
                if (string.IsNullOrEmpty(family))
                {
                    _font.SelectedIndex = 0;
                }
                else
                {
                    int index = _font.Items.IndexOf(family);
                    if (index < 0)
                    {
                        _font.Items.Add(family);
                        index = _font.Items.Count - 1;
                    }
                    _font.SelectedIndex = index;
                }
                SyncNumbers();
                _hint.Text = isText
                    ? "内容支持占位符：" + LabelTokens.HelpText
                    : "同一条码/二维码可以放多个，内容各自独立；也可以引用 {text}。";
            }
            finally { _syncing = false; }

            if (_status != null) _status.Text = _canvas.GetStatusText();
        }

        private void SyncNumbers()
        {
            FreeElement element = Selected;
            if (element == null) return;
            _syncing = true;
            try
            {
                _x.Value = ClampValue(_x, (decimal)element.Box.X);
                _y.Value = ClampValue(_y, (decimal)element.Box.Y);
                _w.Value = ClampValue(_w, (decimal)element.Box.Width);
                _h.Value = ClampValue(_h, (decimal)element.Box.Height);
            }
            finally { _syncing = false; }
        }

        private static decimal ClampValue(NumericUpDown num, decimal value)
        {
            if (value < num.Minimum) value = num.Minimum;
            if (value > num.Maximum) value = num.Maximum;
            return value;
        }

        private FreeElement Selected
        {
            get
            {
                int index = _canvas == null ? -1 : _canvas.SelectedIndex;
                return index >= 0 && index < LayoutResult.Elements.Count ? LayoutResult.Elements[index] : null;
            }
        }

        private void ApplyNumbers()
        {
            if (_syncing) return;
            FreeElement element = Selected;
            if (element == null) return;
            _canvas.PushUndo();
            element.Box = new RectangleF((float)_x.Value, (float)_y.Value, (float)_w.Value, (float)_h.Value);
            _canvas.NotifyLayoutChanged(false);
            RefreshList();
        }

        private void ApplyMargins()
        {
            if (_syncing) return;
            MarginTopMm = (double)_mt.Value;
            MarginBottomMm = (double)_mb.Value;
            MarginLeftMm = (double)_ml.Value;
            MarginRightMm = (double)_mr.Value;
            _canvas.SetMargins(MarginLeftMm, MarginTopMm, MarginRightMm, MarginBottomMm);
            SyncNumbers();
        }

        private void UpdateSelected(Action<FreeElement> change, bool refreshList, bool pushUndo)
        {
            if (_syncing) return;
            FreeElement element = Selected;
            if (element == null) return;
            if (pushUndo) _canvas.PushUndo();
            change(element);
            if (refreshList) RefreshList();
            _canvas.NotifyLayoutChanged(false);
        }

        private void AddElement(FreeElementKind kind)
        {
            if (LayoutResult.Elements.Count >= FreeLayout.MaxElements)
            {
                MessageBox.Show(this, "最多放 " + FreeLayout.MaxElements + " 个元素，已经满了。", "自由排版",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _canvas.PushUndo();
            int same = LayoutResult.CountOf(kind);
            var element = new FreeElement
            {
                Kind = kind,
                Name = FreeLayout.KindName(kind) + (same + 1),
                Content = kind == FreeElementKind.Text ? "{text}"
                    : (kind == FreeElementKind.Barcode ? "{barcode}" : "{qr}"),
                Box = FreeLayout.DefaultBox(_spec, kind, LayoutResult.Elements.Count)
            };
            LayoutResult.Elements.Add(element);
            _canvas.SelectedIndex = LayoutResult.Elements.Count - 1;
            RefreshList();
            _canvas.NotifyLayoutChanged(true);
            SyncFromCanvas();
            if (kind == FreeElementKind.Text) _text.Focus();
        }

        private void DuplicateSelected()
        {
            FreeElement element = Selected;
            if (element == null) return;
            if (LayoutResult.Elements.Count >= FreeLayout.MaxElements)
            {
                MessageBox.Show(this, "最多放 " + FreeLayout.MaxElements + " 个元素，已经满了。", "自由排版",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _canvas.PushUndo();
            FreeElement copy = element.Clone();
            copy.Box = new RectangleF(element.Box.X + 1.5f, element.Box.Y + 1.5f, element.Box.Width, element.Box.Height);
            copy.Name = (string.IsNullOrWhiteSpace(element.Name) ? element.KindName : element.Name) + " 副本";
            LayoutResult.Elements.Add(copy);
            _canvas.SelectedIndex = LayoutResult.Elements.Count - 1;
            RefreshList();
            _canvas.NotifyLayoutChanged(true);
            SyncFromCanvas();
        }

        private void DeleteSelected()
        {
            FreeElement element = Selected;
            if (element == null) return;
            _canvas.PushUndo();
            int index = _canvas.SelectedIndex;
            LayoutResult.Elements.RemoveAt(index);
            _canvas.SelectedIndex = Math.Min(index, LayoutResult.Elements.Count - 1);
            RefreshList();
            _canvas.NotifyLayoutChanged(true);
            SyncFromCanvas();
        }

        private float GridStepMm()
        {
            switch (_gridStep.SelectedIndex)
            {
                case 1: return 1f;
                case 2: return 2f;
                case 3: return 5f;
                default: return 0.5f;
            }
        }

        private void Apply(bool auto)
        {
            if (auto)
            {
                UseAutoLayout = true;
                _canvas.ReplaceLayout(new FreeLayout());
            }
            else
            {
                UseAutoLayout = false;
                LayoutResult.Normalize(_spec, _options);
                if (!LayoutResult.HasElements)
                {
                    MessageBox.Show(this, "至少需要放一个元素。\r\n可以点“恢复自动排版”回到按边距自动分区。",
                        "自由排版", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }
            MarginTopMm = (double)_mt.Value;
            MarginBottomMm = (double)_mb.Value;
            MarginLeftMm = (double)_ml.Value;
            MarginRightMm = (double)_mr.Value;
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // 只在画布有焦点时接管撤销/删除：在内容框里按 Ctrl+Z 应该是撤销打字。
            if (!_canvas.Focused) return base.ProcessCmdKey(ref msg, keyData);
            if (keyData == (Keys.Control | Keys.Z))
            {
                _canvas.Undo();
                RefreshList();
                SyncFromCanvas();
                return true;
            }
            if (keyData == (Keys.Control | Keys.Y))
            {
                _canvas.Redo();
                RefreshList();
                SyncFromCanvas();
                return true;
            }
            if (keyData == Keys.Delete)
            {
                DeleteSelected();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    /// <summary>自由排版的画布：预览渲染 + 元素框拖动/缩放/吸附/键盘微调。</summary>
    internal sealed class FreeLayoutCanvas : Control
    {
        private readonly LabelSpec _spec;
        private readonly LabelContent _content;
        private readonly RenderOptions _options;

        private readonly List<FreeLayout> _undo = new List<FreeLayout>();
        private readonly List<FreeLayout> _redo = new List<FreeLayout>();

        private int _selected = -1;
        private Grip _grip = Grip.None;
        private PointF _dragStart;
        private RectangleF _dragRect;
        private bool _dragging;
        private bool _dragUndoPushed;
        private RectangleF _labelRect;
        private float _scale = 1f;
        private string _lastError = "";

        private Bitmap _cacheBitmap;
        private string _cacheKey = "";

        private double _mL = 1.2, _mT = 1.2, _mR = 1.2, _mB = 1.2;

        private enum Grip { None, Move, NW, NE, SW, SE }

        public event EventHandler SelectionChanged;
        public event EventHandler LayoutChanged;
        public event EventHandler StatusChanged;
        public event EventHandler ElementActivated;

        public float GridStep { get; set; }
        public bool SnapEnabled { get; set; }

        public FreeLayoutCanvas(LabelSpec spec, LabelContent content, RenderOptions options, FreeLayout layout)
        {
            _spec = spec;
            _content = content;
            _options = options;
            CurrentLayout = layout ?? new FreeLayout();
            GridStep = 0.5f;
            SnapEnabled = true;
            DoubleBuffered = true;
            BackColor = Theme.Background;
            ResizeRedraw = true;
            TabStop = true;
            SetStyle(ControlStyles.Selectable, true);
        }

        public FreeLayout CurrentLayout { get; private set; }

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                int next = value;
                if (next < -1) next = -1;
                if (next >= CurrentLayout.Elements.Count) next = CurrentLayout.Elements.Count - 1;
                if (_selected == next) return;
                _selected = next;
                Invalidate();
                RaiseSelection();
            }
        }

        public RectangleF SafeArea
        {
            get { return LayoutPlan.ContentBox(_spec, _mL, _mT, _mR, _mB); }
        }

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
            CurrentLayout.Normalize(_spec, _options);
            Invalidate();
            RaiseStatus();
        }

        /// <summary>整份替换（撤销/重做、恢复自动排版后调用）。</summary>
        public void ReplaceLayout(FreeLayout layout)
        {
            CurrentLayout = layout ?? new FreeLayout();
            if (_selected >= CurrentLayout.Elements.Count) _selected = CurrentLayout.Elements.Count - 1;
            Invalidate();
            RaiseStatus();
            RaiseSelection();
        }

        public void NotifyLayoutChanged(bool selectionChanged)
        {
            Invalidate();
            RaiseStatus();
            var handler = LayoutChanged;
            if (handler != null) handler(this, EventArgs.Empty);
            if (selectionChanged) RaiseSelection();
        }

        public RectangleF SelectedRect
        {
            get
            {
                FreeElement element = Selected;
                return element == null ? RectangleF.Empty : element.Box;
            }
        }

        private FreeElement Selected
        {
            get { return _selected >= 0 && _selected < CurrentLayout.Elements.Count ? CurrentLayout.Elements[_selected] : null; }
        }

        public void AlignSelected(LayoutAnchor anchor)
        {
            FreeElement element = Selected;
            if (element == null) return;
            element.Box = LayoutPlan.AlignRect(element.Box, SafeArea, anchor);
            NotifyLayoutChanged(false);
        }

        public void PushUndo()
        {
            _undo.Add(CurrentLayout.Clone());
            if (_undo.Count > 80) _undo.RemoveAt(0);
            _redo.Clear();
        }

        public void Undo()
        {
            if (_undo.Count == 0) return;
            _redo.Add(CurrentLayout.Clone());
            CurrentLayout = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            if (_selected >= CurrentLayout.Elements.Count) _selected = CurrentLayout.Elements.Count - 1;
            NotifyLayoutChanged(true);
        }

        public void Redo()
        {
            if (_redo.Count == 0) return;
            _undo.Add(CurrentLayout.Clone());
            CurrentLayout = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            if (_selected >= CurrentLayout.Elements.Count) _selected = CurrentLayout.Elements.Count - 1;
            NotifyLayoutChanged(true);
        }

        public string GetStatusText()
        {
            RectangleF area = SafeArea;
            string safe = "安全区 " + area.Width.ToString("0.0") + " × " + area.Height.ToString("0.0") + " mm";
            FreeElement element = Selected;
            if (element == null)
                return safe + " · 点元素选中，拖动移动、拖角缩放，方向键微调（Shift 1 mm）";
            string name = string.IsNullOrWhiteSpace(element.Name) ? element.KindName : element.Name.Trim();
            string outside = IsOutsideLabel(element.Box) ? "　·　超出标签，打印会被裁掉" : "";
            return name + "  X " + element.Box.X.ToString("0.0") + "  Y " + element.Box.Y.ToString("0.0") +
                   "  宽 " + element.Box.Width.ToString("0.0") + "  高 " + element.Box.Height.ToString("0.0") +
                   " mm　·　" + safe + outside;
        }

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

        private void RaiseSelection()
        {
            var handler = SelectionChanged;
            if (handler != null) handler(this, EventArgs.Empty);
            RaiseStatus();
        }

        private void RaiseStatus()
        {
            var handler = StatusChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        // ------------------------------------------------------------------ 绘制

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            float overflow = LayoutPlan.OverflowMm;
            float availableW = Math.Max(80, Width - Theme.S(40));
            float availableH = Math.Max(60, Height - Theme.S(40));
            _scale = Math.Min(availableW / ((float)_spec.WidthMm + 2 * overflow),
                              availableH / ((float)_spec.HeightMm + 2 * overflow));
            if (_scale > 48f) _scale = 48f;
            if (_scale < 1f) _scale = 1f;
            float w = (float)_spec.WidthMm * _scale;
            float h = (float)_spec.HeightMm * _scale;
            float x = (Width - w) / 2f;
            float y = (Height - h) / 2f;
            _labelRect = new RectangleF(x, y, w, h);

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
            Bitmap preview = GetPreview();
            if (preview != null)
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(preview, _labelRect);
            }

            DrawSafeArea(g);
            DrawGrid(g);
            for (int i = 0; i < CurrentLayout.Elements.Count; i++)
                DrawElement(g, i, CurrentLayout.Elements[i]);

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

        /// <summary>缓存渲染结果：拖动时内容没变就不重复编码二维码。</summary>
        private Bitmap GetPreview()
        {
            string key = CacheKey();
            if (_cacheBitmap != null && key == _cacheKey) return _cacheBitmap;
            if (_cacheBitmap != null)
            {
                _cacheBitmap.Dispose();
                _cacheBitmap = null;
            }
            _cacheKey = key;
            try
            {
                RenderOptions preview = _options.Clone();
                preview.MarginLeftMm = _mL;
                preview.MarginTopMm = _mT;
                preview.MarginRightMm = _mR;
                preview.MarginBottomMm = _mB;
                preview.OffsetXMm = 0;
                preview.OffsetYMm = 0;
                preview.LayoutPlan = null;
                preview.FreeLayout = CurrentLayout.Clone();
                RenderedLabel rendered = LabelRenderer.Render(_spec, _content, preview);
                _cacheBitmap = (Bitmap)rendered.Bitmap.Clone();
                rendered.Dispose();
            }
            catch (LabelRenderException ex)
            {
                _lastError = ex.Message;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
            }
            return _cacheBitmap;
        }

        private string CacheKey()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(_spec.WidthMm).Append('x').Append(_spec.HeightMm)
              .Append('|').Append(_mL).Append(',').Append(_mT).Append(',').Append(_mR).Append(',').Append(_mB)
              .Append('|').Append(_options.Dpi).Append(_options.BarcodeType).Append(_options.QrEcc)
              .Append(_options.ShowBarcodeText).Append(_options.FontFamily).Append(_options.Bold)
              .Append(_content.Text).Append(_content.EffectiveBarcode).Append(_content.EffectiveQr);
            foreach (FreeElement element in CurrentLayout.Elements)
            {
                sb.Append('#').Append((int)element.Kind).Append(',')
                  .Append(element.Box.X).Append(',').Append(element.Box.Y).Append(',')
                  .Append(element.Box.Width).Append(',').Append(element.Box.Height).Append(',')
                  .Append(element.Content).Append(',').Append(element.FontSizeMm).Append(',')
                  .Append(element.Bold).Append((int)element.Align).Append(element.FontFamily);
            }
            return sb.ToString();
        }

        private void DrawSafeArea(Graphics g)
        {
            RectangleF area = ToScreen(new RectangleF(0, 0, SafeArea.Width, SafeArea.Height));
            using (var shade = new SolidBrush(Color.FromArgb(20, 70, 90, 140)))
            {
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

        private static void ShadeBand(Graphics g, Brush brush, float x, float y, float width, float height)
        {
            if (width <= 0.5f || height <= 0.5f) return;
            g.FillRectangle(brush, x, y, width, height);
        }

        private void DrawGrid(Graphics g)
        {
            if (GridStep <= 0.01f) return;
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

        private void DrawElement(Graphics g, int index, FreeElement element)
        {
            RectangleF r = ToScreen(element.Box);
            bool selected = index == _selected;
            Color color = element.Kind == FreeElementKind.Text ? Theme.Accent
                : (element.Kind == FreeElementKind.Barcode ? Color.FromArgb(30, 130, 90) : Color.FromArgb(205, 130, 30));

            using (var fill = new SolidBrush(Color.FromArgb(selected ? 26 : 10, color)))
                g.FillRectangle(fill, r);
            using (var pen = new Pen(color, selected ? 2f : 1f))
            {
                pen.DashStyle = selected ? DashStyle.Solid : DashStyle.Dash;
                g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            }
            DrawOutsideTint(g, r);

            // 画布上只留序号徽标（名称在右侧列表里），避免文字标签盖住标签内容。
            using (var font = Theme.Ui(7.6f))
            {
                string badge = (index + 1).ToString();
                Size badgeSize = TextRenderer.MeasureText(badge, font);
                var badgeRect = new Rectangle((int)r.X + 1, (int)r.Y + 1, badgeSize.Width + 3, badgeSize.Height);
                using (var backdrop = new SolidBrush(Color.FromArgb(selected ? 230 : 185, Color.White)))
                    g.FillRectangle(backdrop, badgeRect);
                TextRenderer.DrawText(g, badge, font, new Point(badgeRect.X + 2, badgeRect.Y), color,
                    TextFormatFlags.NoPadding);
            }

            if (!selected) return;
            foreach (RectangleF handle in HandleRects(r))
            {
                using (var fill = new SolidBrush(Color.White))
                using (var pen = new Pen(color, 1.5f))
                {
                    g.FillRectangle(fill, handle);
                    g.DrawRectangle(pen, handle.X, handle.Y, handle.Width, handle.Height);
                }
            }
            // 尺寸不画在画布上（底部状态行已经实时显示），免得盖住标签里的内容。
        }

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

        // ------------------------------------------------------------------ 交互

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            _grip = Grip.None;
            _dragging = false;
            _dragUndoPushed = false;
            _dragStart = e.Location;

            for (int i = CurrentLayout.Elements.Count - 1; i >= 0; i--)
            {
                RectangleF screen = ToScreen(CurrentLayout.Elements[i].Box);
                Grip handle = HitHandle(screen, e.Location);
                if (handle != Grip.None)
                {
                    SelectedIndex = i;
                    _grip = handle;
                    _dragRect = CurrentLayout.Elements[i].Box;
                    _dragging = true;
                    return;
                }
                if (screen.Contains(e.Location))
                {
                    SelectedIndex = i;
                    _grip = Grip.Move;
                    _dragRect = CurrentLayout.Elements[i].Box;
                    _dragging = true;
                    return;
                }
            }

            SelectedIndex = -1;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            FreeElement element = Selected;
            if (element != null && element.Kind == FreeElementKind.Text)
            {
                var handler = ElementActivated;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging)
            {
                UpdateCursor(e.Location);
                return;
            }

            FreeElement element = Selected;
            if (element == null) return;

            float dx = (e.X - _dragStart.X) / _scale;
            float dy = (e.Y - _dragStart.Y) / _scale;
            RectangleF allow = AllowArea;
            RectangleF rect;

            if (_grip == Grip.Move)
            {
                rect = _dragRect;
                rect.X = Clamp(Snap(_dragRect.X + dx), allow.Left, Math.Max(allow.Left, allow.Right - _dragRect.Width));
                rect.Y = Clamp(Snap(_dragRect.Y + dy), allow.Top, Math.Max(allow.Top, allow.Bottom - _dragRect.Height));
            }
            else if (element.Kind == FreeElementKind.Qr)
            {
                rect = ResizeSquare(_dragRect, dx, dy, allow);
            }
            else
            {
                rect = ResizeFree(_dragRect, dx, dy, allow, element.Kind == FreeElementKind.Text ? 2f : 4f);
            }

            if (rect != _dragRect && !_dragUndoPushed)
            {
                PushUndo();
                _dragUndoPushed = true;
            }

            element.Box = rect;
            Invalidate();
            RaiseStatus();
            var handler = LayoutChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            _grip = Grip.None;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Left || keyData == Keys.Right ||
                keyData == Keys.Up || keyData == Keys.Down || keyData == Keys.Delete) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            FreeElement element = Selected;
            if (element == null) return;

            float step = e.Shift ? 1f : 0.1f;
            float dx = 0, dy = 0;
            if (e.KeyCode == Keys.Left) dx = -step;
            else if (e.KeyCode == Keys.Right) dx = step;
            else if (e.KeyCode == Keys.Up) dy = -step;
            else if (e.KeyCode == Keys.Down) dy = step;
            else return;

            PushUndo();
            RectangleF allow = AllowArea;
            RectangleF rect = element.Box;
            rect.X += dx;
            rect.Y += dy;
            rect.X = Clamp(rect.X, allow.Left, Math.Max(allow.Left, allow.Right - rect.Width));
            rect.Y = Clamp(rect.Y, allow.Top, Math.Max(allow.Top, allow.Bottom - rect.Height));
            element.Box = rect;
            Invalidate();
            RaiseStatus();
            var changed = LayoutChanged;
            if (changed != null) changed(this, EventArgs.Empty);
            e.Handled = true;
        }

        private float Snap(float value)
        {
            return SnapEnabled ? LayoutPlan.Snap(value, GridStep) : value;
        }

        private RectangleF ResizeFree(RectangleF start, float dx, float dy, RectangleF allow, float minSize)
        {
            float left = start.Left;
            float top = start.Top;
            float right = start.Right;
            float bottom = start.Bottom;

            if (_grip == Grip.NW || _grip == Grip.SW) left = Snap(start.Left + dx);
            if (_grip == Grip.NE || _grip == Grip.SE) right = Snap(start.Right + dx);
            if (_grip == Grip.NW || _grip == Grip.NE) top = Snap(start.Top + dy);
            if (_grip == Grip.SW || _grip == Grip.SE) bottom = Snap(start.Bottom + dy);

            if (right - left < minSize)
            {
                if (_grip == Grip.NW || _grip == Grip.SW) left = right - minSize;
                else right = left + minSize;
            }
            if (bottom - top < minSize)
            {
                if (_grip == Grip.NW || _grip == Grip.NE) top = bottom - minSize;
                else bottom = top + minSize;
            }

            left = Clamp(left, allow.Left, allow.Right - minSize);
            top = Clamp(top, allow.Top, allow.Bottom - minSize);
            right = Clamp(right, left + minSize, allow.Right);
            bottom = Clamp(bottom, top + minSize, allow.Bottom);
            return new RectangleF(left, top, right - left, bottom - top);
        }

        private RectangleF ResizeSquare(RectangleF start, float dx, float dy, RectangleF allow)
        {
            float size = Math.Max(start.Width, start.Height);
            float delta = Math.Abs(dx) > Math.Abs(dy) ? dx : dy;
            bool leftSide = _grip == Grip.NW || _grip == Grip.SW;
            bool topSide = _grip == Grip.NW || _grip == Grip.NE;
            size = Snap(Math.Max(3f, size + (leftSide ? -delta : delta)));

            float left = leftSide ? start.Right - size : start.Left;
            float top = topSide ? start.Bottom - size : start.Top;
            left = Clamp(left, allow.Left, Math.Max(allow.Left, allow.Right - size));
            top = Clamp(top, allow.Top, Math.Max(allow.Top, allow.Bottom - size));
            if (size > allow.Width) size = allow.Width;
            if (size > allow.Height) size = allow.Height;
            return new RectangleF(left, top, size, size);
        }

        private static float Clamp(float value, float min, float max)
        {
            if (max < min) return min;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static Grip HitHandle(RectangleF screen, PointF point)
        {
            RectangleF[] handles =
            {
                new RectangleF(screen.Left - 5, screen.Top - 5, 10, 10),
                new RectangleF(screen.Right - 5, screen.Top - 5, 10, 10),
                new RectangleF(screen.Left - 5, screen.Bottom - 5, 10, 10),
                new RectangleF(screen.Right - 5, screen.Bottom - 5, 10, 10)
            };
            for (int i = 0; i < handles.Length; i++)
            {
                if (!handles[i].Contains(point)) continue;
                return i == 0 ? Grip.NW : (i == 1 ? Grip.NE : (i == 2 ? Grip.SW : Grip.SE));
            }
            return Grip.None;
        }

        private void UpdateCursor(PointF point)
        {
            Cursor cursor = Cursors.Default;
            for (int i = CurrentLayout.Elements.Count - 1; i >= 0; i--)
            {
                RectangleF screen = ToScreen(CurrentLayout.Elements[i].Box);
                Grip handle = HitHandle(screen, point);
                if (handle == Grip.NW || handle == Grip.SE) cursor = Cursors.SizeNWSE;
                else if (handle == Grip.NE || handle == Grip.SW) cursor = Cursors.SizeNESW;
                else if (screen.Contains(point)) cursor = Cursors.SizeAll;
                if (cursor != Cursors.Default) break;
            }
            if (Cursor != cursor) Cursor = cursor;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _cacheBitmap != null)
            {
                _cacheBitmap.Dispose();
                _cacheBitmap = null;
            }
            base.Dispose(disposing);
        }
    }
}
