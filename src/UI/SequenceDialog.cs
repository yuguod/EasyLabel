using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using EasyLabel.Core;

namespace EasyLabel.UI
{
    /// <summary>
    /// 批量序列：固定前缀 + 数字序列 + 可选后缀。
    /// 数量与“最后一个编号”互相推算——改数量会算出末个条码，改末个条码会反推数量。
    /// 左侧设置、右侧预览列表，可直接导出成 Excel。
    /// </summary>
    public sealed class SequenceDialog : Form
    {
        /// <summary>预览列表最多渲染多少行，再多就只显示前若干行（生成/导出仍是全部）。</summary>
        private const int MaxPreviewRows = 500;

        private TextBox _prefix;
        private TextBox _start;
        private NumericUpDown _count;
        private TextBox _end;
        private NumericUpDown _step;
        private TextBox _suffix;
        private NumericUpDown _digits;
        private readonly CheckBox _clearExisting;
        private Label _summary;
        private readonly Label _error;
        private DataGridView _grid;
        private readonly Button _ok;
        private readonly Button _export;

        private bool _syncing;

        /// <summary>生成出来的整段内容（点击“生成”后有效）。</summary>
        public List<string> Values { get; private set; }

        /// <summary>是否先清空批量表格里已有的数据。</summary>
        public bool ClearExisting { get { return _clearExisting.Checked; } }

        public SequenceDialog()
        {
            Values = new List<string>();

            Text = "批量序列 — 固定字符 + 连续编号";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(Theme.S(1000), Theme.S(690));
            MinimumSize = new Size(Theme.S(840), Theme.S(580));
            BackColor = Theme.Background;
            Font = Theme.Ui(9f);
            AutoScaleMode = AutoScaleMode.None;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Theme.Background,
                Padding = new Padding(Theme.S(14), Theme.S(10), Theme.S(14), Theme.S(8))
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(38)));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(54)));

            var intro = Theme.Label(
                "固定字符不变、编号依次递增，每一行生成一张标签。改“数量”会自动填好最后一个编号，直接改最后一个编号会反推数量。",
                8.8f, Theme.Muted);
            intro.AutoSize = false;
            intro.Dock = DockStyle.Fill;
            intro.TextAlign = ContentAlignment.MiddleLeft;
            intro.AutoEllipsis = true;
            root.Controls.Add(intro, 0, 0);

            // ================================================================ 主体：左设置 + 右预览
            var main = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Theme.Background
            };
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(432)));
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            main.Controls.Add(BuildSettingsCard(), 0, 0);
            main.Controls.Add(BuildPreviewCard(), 1, 0);
            root.Controls.Add(main, 0, 1);

            // ================================================================ 底栏
            var bottom = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(392)));

            var bottomLeft = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            bottomLeft.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(176)));
            bottomLeft.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            _clearExisting = UiKit.Check("生成后清空现有数据", false);
            _clearExisting.AutoSize = false;
            _clearExisting.Dock = DockStyle.Fill;
            _clearExisting.TextAlign = ContentAlignment.MiddleLeft;
            _clearExisting.Margin = new Padding(0, 0, 0, 0);

            _error = Theme.Label("", 8.6f, Theme.Error);
            _error.AutoSize = false;
            _error.Dock = DockStyle.Fill;
            _error.TextAlign = ContentAlignment.MiddleLeft;
            _error.AutoEllipsis = true;
            _error.Padding = new Padding(Theme.S(10), 0, Theme.S(8), 0);

            bottomLeft.Controls.Add(_clearExisting, 0, 0);
            bottomLeft.Controls.Add(_error, 1, 0);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };

            _ok = UiKit.PrimaryButton("生成并加入表格");
            _ok.Size = new Size(Theme.S(158), Theme.S(34));
            _ok.Margin = new Padding(Theme.S(8), Theme.S(8), 0, 0);
            _ok.Click += (s, e) => Generate();

            var cancel = UiKit.SecondaryButton("取消");
            cancel.Size = new Size(Theme.S(78), Theme.S(34));
            cancel.Margin = new Padding(Theme.S(8), Theme.S(8), 0, 0);
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            _export = UiKit.SecondaryButton("导出 Excel…");
            _export.Size = new Size(Theme.S(114), Theme.S(34));
            _export.Margin = new Padding(0, Theme.S(8), 0, 0);
            _export.Click += (s, e) => ExportExcel();

            // FlowDirection.RightToLeft：先加的在最右边，视觉顺序是 导出 / 取消 / 生成。
            buttons.Controls.Add(_ok);
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(_export);

            bottom.Controls.Add(bottomLeft, 0, 0);
            bottom.Controls.Add(buttons, 1, 0);
            root.Controls.Add(bottom, 0, 2);

            Controls.Add(root);
            AcceptButton = _ok;
            CancelButton = cancel;

            Shown += (s, e) =>
            {
                FitToWorkingArea();
                SyncFromCount();
                _start.Focus();
                _start.SelectAll();
            };
        }

        // ---------------------------------------------------------------- 设置卡片

        private CardPanel BuildSettingsCard()
        {
            var card = new CardPanel("序列设置")
            {
                Dock = DockStyle.Fill,
                AutoFitHeight = false,
                Margin = new Padding(0)
            };
            // 屏幕特别矮时给设置区一个纵向滚动条，而不是把最后几行裁掉。
            card.Content.AutoScroll = true;

            var form = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 10,
                BackColor = Theme.Card
            };
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(84)));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            // 0 小标题 / 1 前缀 / 2 后缀 / 3 小标题 / 4 起始+位数 / 5 步长 / 6 小标题 / 7 数量 / 8 末个 / 9 提示
            for (int i = 0; i < 10; i++)
                form.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(38)));

            form.RowStyles[0] = new RowStyle(SizeType.Absolute, Theme.S(28));
            form.RowStyles[3] = new RowStyle(SizeType.Absolute, Theme.S(30));
            form.RowStyles[6] = new RowStyle(SizeType.Absolute, Theme.S(30));
            form.RowStyles[9] = new RowStyle(SizeType.Absolute, Theme.S(68));

            _prefix = UiKit.TextBox(false, Theme.S(26));
            _prefix.Dock = DockStyle.Fill;
            _prefix.TextChanged += (s, e) => { if (!_syncing) SyncFromCount(false); };

            _suffix = UiKit.TextBox(false, Theme.S(26));
            _suffix.Dock = DockStyle.Fill;
            _suffix.TextChanged += (s, e) => { if (!_syncing) SyncFromCount(false); };

            _start = UiKit.TextBox(false, Theme.S(26));
            _start.Dock = DockStyle.Fill;
            _start.Text = "0001";
            _start.TextChanged += (s, e) => OnStartChanged();

            _digits = UiKit.Numeric(1, SequenceGenerator.MaxDigits, 4);
            _digits.Dock = DockStyle.Fill;
            _digits.ValueChanged += (s, e) => { if (!_syncing) SyncFromCount(false); };

            _step = UiKit.Numeric(1, 10000, 1);
            _step.ValueChanged += (s, e) => { if (!_syncing) SyncFromCount(false); };

            _count = UiKit.Numeric(1, SequenceGenerator.MaxCount, 50);
            _count.ValueChanged += (s, e) => { if (!_syncing) SyncFromCount(false); };

            _end = UiKit.TextBox(false, Theme.S(26));
            _end.Dock = DockStyle.Fill;
            _end.TextChanged += (s, e) => { if (!_syncing) SyncFromEnd(); };

            // ---- 条码内容
            var contentTitle = SectionLabel("条码内容");
            form.Controls.Add(contentTitle, 0, 0);
            form.SetColumnSpan(contentTitle, 2);

            AddField(form, 1, "固定前缀", _prefix);
            AddField(form, 2, "固定后缀", _suffix);

            // ---- 编号规则
            var numberTitle = SectionLabel("编号规则");
            form.Controls.Add(numberTitle, 0, 3);
            form.SetColumnSpan(numberTitle, 2);

            form.Controls.Add(FieldText("起始编号"), 0, 4);
            form.Controls.Add(BuildStartRow(), 1, 4);

            AddField(form, 5, "步长", WrapHint(_step, "1 = 连号，2 = 隔一个"));

            // ---- 数量
            var countTitle = SectionLabel("数量");
            form.Controls.Add(countTitle, 0, 6);
            form.SetColumnSpan(countTitle, 2);

            AddField(form, 7, "数量", WrapHint(_count, "个"));
            AddField(form, 8, "最后一个编号", _end);

            var tip = Theme.Label(
                "· 整串条码可直接粘到“起始编号”，程序会自动拆出前缀（如 20261117DLX0001）。\r\n" +
                "· 后缀可留空，例如填 -A 得到 20261117DLX0001-A。\r\n" +
                "· 生成后仍可在批量表格里逐行修改；也可以先导出成 Excel 存档。",
                8.4f, Theme.Muted);
            tip.AutoSize = false;
            tip.Dock = DockStyle.Fill;
            tip.Margin = new Padding(0, Theme.S(12), 0, 0);
            tip.TextAlign = ContentAlignment.TopLeft;
            form.Controls.Add(tip, 0, 9);
            form.SetColumnSpan(tip, 2);

            card.Content.Controls.Add(form);
            return card;
        }

        private Control BuildStartRow()
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Theme.Card,
                Margin = new Padding(0)
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(66)));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _start.Margin = new Padding(0, Theme.S(6), 0, Theme.S(6));
            row.Controls.Add(_start, 0, 0);

            var digitsLabel = Theme.Label("位数", 8.6f, Theme.Muted);
            digitsLabel.Anchor = AnchorStyles.Left;
            digitsLabel.Margin = new Padding(Theme.S(10), 0, Theme.S(6), 0);
            row.Controls.Add(digitsLabel, 1, 0);

            _digits.Margin = new Padding(0, Theme.S(6), 0, Theme.S(6));
            row.Controls.Add(_digits, 2, 0);
            return row;
        }

        // ---------------------------------------------------------------- 预览卡片

        private CardPanel BuildPreviewCard()
        {
            var card = new CardPanel("预览")
            {
                Dock = DockStyle.Fill,
                AutoFitHeight = false,
                Margin = new Padding(Theme.S(10), 0, 0, 0)
            };

            var host = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Theme.Card
            };
            host.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(28)));
            host.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _summary = Theme.Label("", 9f, Theme.Text);
            _summary.AutoSize = false;
            _summary.Dock = DockStyle.Fill;
            _summary.TextAlign = ContentAlignment.MiddleLeft;
            _summary.AutoEllipsis = true;
            _summary.Padding = new Padding(0, 0, 0, Theme.S(6));

            _grid = new DataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = false };
            Theme.StyleGrid(_grid);
            _grid.ReadOnly = true;
            _grid.TabStop = false;
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "序号",
                Width = Theme.S(58),
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle =
                {
                    Alignment = DataGridViewContentAlignment.MiddleRight,
                    ForeColor = Theme.Muted
                }
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "条码内容",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });

            host.Controls.Add(_summary, 0, 0);
            host.Controls.Add(_grid, 0, 1);
            card.Content.Controls.Add(host);
            return card;
        }

        /// <summary>窗口在小屏 / 高缩放下收进当前屏幕工作区，避免底部按钮跑到屏幕外。</summary>
        private void FitToWorkingArea()
        {
            try
            {
                Rectangle wa = Screen.FromControl(this).WorkingArea;
                int margin = Theme.S(12);
                int maxW = Math.Max(Theme.S(700), wa.Width - margin * 2);
                int maxH = Math.Max(Theme.S(460), wa.Height - margin * 2);

                MinimumSize = new Size(Math.Min(MinimumSize.Width, maxW), Math.Min(MinimumSize.Height, maxH));
                if (Width > maxW) Width = maxW;
                if (Height > maxH) Height = maxH;
                if (Right > wa.Right) Left = Math.Max(wa.Left, wa.Right - Width);
                if (Bottom > wa.Bottom) Top = Math.Max(wa.Top, wa.Bottom - Height);
                if (Left < wa.Left) Left = wa.Left;
                if (Top < wa.Top) Top = wa.Top;
            }
            catch { }
        }

        // ---------------------------------------------------------------- 小工具

        private static Label SectionLabel(string text)
        {
            var label = Theme.Label(text, 8.8f, Theme.Accent, FontStyle.Bold);
            label.AutoSize = false;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.BottomLeft;
            label.Margin = new Padding(0, 0, 0, Theme.S(2));
            return label;
        }

        private static Label FieldText(string text)
        {
            var label = Theme.Label(text, 8.9f, Theme.Muted);
            label.AutoSize = false;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        private static void AddField(TableLayoutPanel form, int row, string title, Control field)
        {
            form.Controls.Add(FieldText(title), 0, row);
            field.Dock = DockStyle.Fill;
            // 容器（WrapHint 的表格）自己内部已经有上下留白，不要再加外边距，否则会把数字框压扁。
            bool container = field is TableLayoutPanel || field is Panel;
            field.Margin = container ? new Padding(0) : new Padding(0, Theme.S(6), 0, Theme.S(6));
            form.Controls.Add(field, 1, row);
        }

        /// <summary>把输入框和后面的灰色提示放进同一行（输入框固定宽度，提示占满剩余）。</summary>
        private static Control WrapHint(Control field, string hint)
        {
            var host = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Theme.Card,
                Margin = new Padding(0)
            };
            host.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(96)));
            host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            host.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            field.Dock = DockStyle.Fill;
            field.Margin = new Padding(0, Theme.S(6), 0, Theme.S(6));
            host.Controls.Add(field, 0, 0);

            var label = Theme.Label(hint, 8.4f, Theme.Muted);
            label.AutoSize = false;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Padding = new Padding(Theme.S(8), 0, 0, 0);
            label.AutoEllipsis = true;
            host.Controls.Add(label, 1, 0);
            return host;
        }

        // ---------------------------------------------------------------- 联动

        /// <summary>起始框变了：同步位数，并按当前数量重算末个编号。</summary>
        private void OnStartChanged()
        {
            if (_syncing) return;

            long value;
            int digits;
            string head, number;
            // 直接粘 “20261117DLX0001” 这类整串时，自动拆成前缀 + 编号。
            if (!SequenceGenerator.TryParseNumber(_start.Text, out value, out digits) &&
                SequenceGenerator.TrySplitTrailingNumber(_start.Text, out head, out number) &&
                SequenceGenerator.TryParseNumber(number, out value, out digits))
            {
                _syncing = true;
                try
                {
                    if (_prefix.Text.Trim().Length == 0) _prefix.Text = head;
                    _start.Text = number;
                }
                finally { _syncing = false; }
            }

            SyncFromCount();
        }

        /// <summary>以“起始 + 数量”为准，刷新末个编号；syncDigits 时把位数对齐到起始值的长度。</summary>
        private void SyncFromCount(bool syncDigits = true)
        {
            long start;
            int digits;
            if (!SequenceGenerator.TryParseNumber(_start.Text, out start, out digits))
            {
                SetError("起始编号请填数字，例如 0001");
                _ok.Enabled = false;
                _export.Enabled = false;
                return;
            }

            if (syncDigits)
            {
                _syncing = true;
                try { _digits.Value = Math.Min(digits, SequenceGenerator.MaxDigits); }
                finally { _syncing = false; }
            }

            long step = (long)_step.Value;
            int count = (int)_count.Value;
            long last = start + step * (count - 1);
            int useDigits = Math.Max(1, Math.Min(SequenceGenerator.MaxDigits, (int)_digits.Value));

            var spec = new SequenceSpec
            {
                Prefix = _prefix.Text.Trim(),
                Suffix = _suffix.Text.Trim(),
                Start = start,
                Step = step,
                Count = count,
                Digits = useDigits
            };

            _syncing = true;
            try
            {
                // 末个框里显示完整条码（含前缀/后缀），改数量时一眼能看到最后一个。
                _end.Text = SequenceGenerator.Compose(spec, last);
            }
            finally { _syncing = false; }

            UpdatePreview();
        }

        /// <summary>
        /// 解析“最后一个编号”：允许只填数字，也允许填完整条码（自动剥掉前缀与后缀）。
        /// </summary>
        private bool TryParseEndNumber(string text, out long value, out int digits)
        {
            value = 0;
            digits = 1;
            string t = (text ?? "").Trim();
            string prefix = _prefix.Text.Trim();
            string suffix = _suffix.Text.Trim();

            if (prefix.Length > 0 && t.StartsWith(prefix, StringComparison.Ordinal)) t = t.Substring(prefix.Length);
            if (suffix.Length > 0 && t.EndsWith(suffix, StringComparison.Ordinal)) t = t.Substring(0, t.Length - suffix.Length);

            if (SequenceGenerator.TryParseNumber(t, out value, out digits)) return true;

            string head, number;
            return SequenceGenerator.TrySplitTrailingNumber(t, out head, out number) &&
                   SequenceGenerator.TryParseNumber(number, out value, out digits);
        }

        /// <summary>以“起始 + 最后一个”为准，反推数量。</summary>
        private void SyncFromEnd()
        {
            long start;
            int startDigits;
            if (!SequenceGenerator.TryParseNumber(_start.Text, out start, out startDigits))
            {
                SetError("起始编号请填数字，例如 0001");
                _ok.Enabled = false;
                _export.Enabled = false;
                return;
            }

            long last;
            int endDigits;
            if (!TryParseEndNumber(_end.Text, out last, out endDigits))
            {
                SetError("最后一个编号请填数字，例如 0050");
                _ok.Enabled = false;
                _export.Enabled = false;
                return;
            }

            long step = (long)_step.Value;
            long count = SequenceGenerator.CountFor(start, last, step);
            if (count < 0)
            {
                SetError("从 " + start + " 按步长 " + step + " 走不到 " + last + "，请调整步长或末位编号");
                _ok.Enabled = false;
                _export.Enabled = false;
                return;
            }
            if (count < 1)
            {
                SetError("最后一个编号不能小于起始编号");
                _ok.Enabled = false;
                _export.Enabled = false;
                return;
            }
            if (count > SequenceGenerator.MaxCount)
            {
                SetError("数量超过 " + SequenceGenerator.MaxCount + " 行，请缩小范围");
                _ok.Enabled = false;
                _export.Enabled = false;
                return;
            }

            _syncing = true;
            try
            {
                _count.Value = count;
            }
            finally { _syncing = false; }

            UpdatePreview();
        }

        // ---------------------------------------------------------------- 预览

        private void UpdatePreview()
        {
            SequenceSpec spec = BuildSpec();
            string error = SequenceGenerator.Validate(spec);
            if (error.Length > 0)
            {
                SetError(error);
                _ok.Enabled = false;
                _export.Enabled = false;
                _summary.Text = "";
                _grid.Rows.Clear();
                return;
            }

            SetError("");
            _ok.Enabled = true;
            _export.Enabled = true;

            long last = SequenceGenerator.LastValue(spec.Start, spec.Step, spec.Count);
            _summary.Text = "共 " + spec.Count.ToString("N0", CultureInfo.CurrentCulture) + " 个" +
                            "　·　编号 " + SequenceGenerator.Format(spec.Start, spec.Digits) + " → " +
                            SequenceGenerator.Format(last, spec.Digits) +
                            (spec.Step > 1 ? "　·　步长 " + spec.Step : "") +
                            (spec.Count > MaxPreviewRows ? "　·　下方仅预览前 " + MaxPreviewRows + " 行" : "");

            _grid.SuspendLayout();
            try
            {
                _grid.Rows.Clear();
                int show = Math.Min(spec.Count, MaxPreviewRows);
                for (int i = 0; i < show; i++)
                {
                    _grid.Rows.Add((i + 1).ToString(CultureInfo.CurrentCulture),
                        SequenceGenerator.Compose(spec, spec.Start + spec.Step * i));
                }
                if (spec.Count > show)
                {
                    _grid.Rows.Add("…", "（共 " + spec.Count + " 行，生成和导出包含全部内容）");
                }
            }
            finally
            {
                _grid.ResumeLayout();
            }
            try { if (_grid.Rows.Count > 0) _grid.FirstDisplayedScrollingRowIndex = 0; } catch { }
        }

        private void SetError(string message)
        {
            _error.Text = message.Length == 0 ? "" : "⚠ " + message;
        }

        private SequenceSpec BuildSpec()
        {
            var spec = new SequenceSpec
            {
                Prefix = _prefix.Text.Trim(),
                Suffix = _suffix.Text.Trim(),
                Step = (long)_step.Value,
                Count = (int)_count.Value,
                Digits = (int)_digits.Value
            };

            long start;
            int digits;
            spec.Start = SequenceGenerator.TryParseNumber(_start.Text, out start, out digits) ? start : -1;
            return spec;
        }

        // ---------------------------------------------------------------- 导出

        private void ExportExcel()
        {
            SequenceSpec spec = BuildSpec();
            string error = SequenceGenerator.Validate(spec);
            if (error.Length > 0)
            {
                SetError(error);
                MessageBox.Show(this, error, "导出序列", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var values = SequenceGenerator.Build(spec);
            long last = SequenceGenerator.LastValue(spec.Start, spec.Step, spec.Count);

            using (var save = new SaveFileDialog())
            {
                save.Title = "导出序列到 Excel";
                save.Filter = "Excel 工作簿 (*.xlsx)|*.xlsx";
                save.DefaultExt = "xlsx";
                save.AddExtension = true;
                save.OverwritePrompt = true;
                save.FileName = SuggestFileName(spec, last);
                try
                {
                    string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    if (!string.IsNullOrEmpty(desktop) && Directory.Exists(desktop)) save.InitialDirectory = desktop;
                }
                catch { }

                if (save.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    XlsxWriter.WriteList(save.FileName, "序列", "条码内容", values);
                    Logger.Info("序列导出 Excel：" + save.FileName + "（" + values.Count + " 行）");
                }
                catch (Exception ex)
                {
                    Logger.Error("导出序列 Excel 失败", ex);
                    MessageBox.Show(this, "导出失败：\r\n\r\n" + ex.Message, "导出序列",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (MessageBox.Show(this,
                        "已导出 " + values.Count + " 行到：\r\n" + save.FileName + "\r\n\r\n是否打开所在文件夹？",
                        "导出完成", MessageBoxButtons.YesNo, MessageBoxIcon.Information,
                        MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                {
                    try { Process.Start("explorer.exe", "/select,\"" + save.FileName + "\""); } catch { }
                }
            }
        }

        private static string SuggestFileName(SequenceSpec spec, long last)
        {
            string name = "序列_" + (spec.Prefix ?? "") + SequenceGenerator.Format(spec.Start, spec.Digits) +
                          "-" + SequenceGenerator.Format(last, spec.Digits) +
                          (spec.Suffix ?? "") + ".xlsx";
            foreach (char bad in Path.GetInvalidFileNameChars())
                name = name.Replace(bad, '_');
            if (name.Length > 90) name = name.Substring(0, 86) + ".xlsx";
            return name;
        }

        private void Generate()
        {
            SequenceSpec spec = BuildSpec();
            string error = SequenceGenerator.Validate(spec);
            if (error.Length > 0)
            {
                SetError(error);
                MessageBox.Show(this, error, "批量序列", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Values = SequenceGenerator.Build(spec);
            Logger.Info("SequenceDialog: 生成 " + Values.Count + " 个，首个 " + Values[0]);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
