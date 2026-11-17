using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using EasyLabel.Core;

namespace EasyLabel.UI
{
    /// <summary>
    /// 批量序列：固定前缀 + 数字序列 + 可选后缀。
    /// 数量与“最后一个编号”互相推算——改数量会算出末个条码，改末个条码会反推数量。
    /// </summary>
    public sealed class SequenceDialog : Form
    {
        private readonly TextBox _prefix;
        private readonly TextBox _start;
        private readonly NumericUpDown _count;
        private readonly TextBox _end;
        private readonly NumericUpDown _step;
        private readonly TextBox _suffix;
        private readonly NumericUpDown _digits;
        private readonly CheckBox _clearExisting;
        private readonly Label _summary;
        private readonly TextBox _sample;
        private readonly Label _error;
        private readonly Button _ok;

        private bool _syncing;

        /// <summary>生成出来的整段内容（点击“生成”后有效）。</summary>
        public List<string> Values { get; private set; }

        /// <summary>是否先清空批量表格里已有的数据。</summary>
        public bool ClearExisting { get { return _clearExisting.Checked; } }

        public SequenceDialog()
        {
            Values = new List<string>();

            Text = "批量序列 — 固定前缀 + 连续编号";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(780, 680);
            MinimumSize = new Size(660, 580);
            BackColor = Theme.Background;
            Font = Theme.Ui(9f);
            AutoScaleMode = AutoScaleMode.None;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Theme.Background,
                Padding = new Padding(14, 12, 14, 10)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(44)));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(330)));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(52)));

            var intro = Theme.Label(
                "固定字符不变，编号依次递增，每一行生成一张标签。改“数量”会自动填好最后一个编号；直接改最后一个编号会反推数量。",
                8.8f, Theme.Muted);
            intro.AutoSize = false;
            intro.Dock = DockStyle.Fill;
            intro.TextAlign = ContentAlignment.MiddleLeft;
            intro.AutoEllipsis = true;
            root.Controls.Add(intro, 0, 0);

            // ---------------------------------------------------------------- 设置
            var card = new CardPanel("序列设置") { Dock = DockStyle.Fill, AutoFitHeight = false };
            var form = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 7,
                BackColor = Theme.Card
            };
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(112)));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(300)));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 7; i++)
                form.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(38)));

            _prefix = UiKit.TextBox(false, Theme.S(28));
            _prefix.Dock = DockStyle.Fill;
            _prefix.TextChanged += (s, e) => { if (!_syncing) SyncFromCount(false); };

            _start = UiKit.TextBox(false, Theme.S(28));
            _start.Dock = DockStyle.Fill;
            _start.Text = "0001";
            _start.TextChanged += (s, e) => OnStartChanged();

            _count = UiKit.Numeric(1, SequenceGenerator.MaxCount, 50);
            _count.Dock = DockStyle.Left;
            _count.Width = Theme.S(110);
            _count.ValueChanged += (s, e) => { if (!_syncing) SyncFromCount(false); };

            _end = UiKit.TextBox(false, Theme.S(28));
            _end.Dock = DockStyle.Fill;
            _end.TextChanged += (s, e) => { if (!_syncing) SyncFromEnd(); };

            _step = UiKit.Numeric(1, 10000, 1);
            _step.Dock = DockStyle.Left;
            _step.Width = Theme.S(110);
            _step.ValueChanged += (s, e) => { if (!_syncing) SyncFromCount(false); };

            _suffix = UiKit.TextBox(false, Theme.S(28));
            _suffix.Dock = DockStyle.Fill;
            _suffix.TextChanged += (s, e) => { if (!_syncing) SyncFromCount(false); };

            _digits = UiKit.Numeric(1, SequenceGenerator.MaxDigits, 4);
            _digits.Dock = DockStyle.Left;
            _digits.Width = Theme.S(110);
            _digits.ValueChanged += (s, e) => { if (!_syncing) SyncFromCount(false); };

            form.Controls.Add(FieldLabel("固定前缀"), 0, 0);
            form.Controls.Add(_prefix, 1, 0);
            form.Controls.Add(FieldHint("不变的部分，例如 20261117DLX"), 2, 0);

            form.Controls.Add(FieldLabel("起始编号"), 0, 1);
            form.Controls.Add(_start, 1, 1);
            form.Controls.Add(FieldHint("0001 会按 4 位补零；也可以直接粘整串条码"), 2, 1);

            form.Controls.Add(FieldLabel("数量"), 0, 2);
            form.Controls.Add(_count, 1, 2);
            form.Controls.Add(FieldHint("改这里会自动填好最后一个编号"), 2, 2);

            form.Controls.Add(FieldLabel("最后一个编号"), 0, 3);
            form.Controls.Add(_end, 1, 3);
            form.Controls.Add(FieldHint("改这里会反推数量"), 2, 3);

            form.Controls.Add(FieldLabel("步长"), 0, 4);
            form.Controls.Add(_step, 1, 4);
            form.Controls.Add(FieldHint("连号填 1；隔号填 2、5 等"), 2, 4);

            form.Controls.Add(FieldLabel("编号位数"), 0, 5);
            form.Controls.Add(_digits, 1, 5);
            form.Controls.Add(FieldHint("不足补零，超出位数不截断"), 2, 5);

            form.Controls.Add(FieldLabel("固定后缀"), 0, 6);
            form.Controls.Add(_suffix, 1, 6);
            form.Controls.Add(FieldHint("可以留空，例如 -A"), 2, 6);

            card.Content.Controls.Add(form);
            root.Controls.Add(card, 0, 1);

            // ---------------------------------------------------------------- 预览
            var preview = new CardPanel("预览") { Dock = DockStyle.Fill, AutoFitHeight = false };
            var previewHost = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Theme.Card
            };
            previewHost.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(26)));
            previewHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _summary = Theme.Label("", 9.2f, Theme.Text);
            _summary.AutoSize = false;
            _summary.Dock = DockStyle.Fill;
            _summary.TextAlign = ContentAlignment.MiddleLeft;

            _sample = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Theme.Card,
                ForeColor = Theme.Text,
                Font = new Font(FontFamily.GenericMonospace, 9.2f)
            };
            _sample.TabStop = false;

            previewHost.Controls.Add(_summary, 0, 0);
            previewHost.Controls.Add(_sample, 0, 1);
            preview.Content.Controls.Add(previewHost);
            root.Controls.Add(preview, 0, 2);

            // ---------------------------------------------------------------- 按钮栏
            var bottom = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };

            _error = Theme.Label("", 8.6f, Theme.Error);
            _error.AutoSize = false;
            _error.Location = new Point(0, Theme.S(17));
            _error.Size = new Size(Theme.S(320), Theme.S(22));
            _error.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            _error.TextAlign = ContentAlignment.MiddleLeft;
            _error.AutoEllipsis = true;

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = Theme.S(410),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent
            };
            _clearExisting = UiKit.Check("生成后清空现有数据", false);
            _clearExisting.Margin = new Padding(0, Theme.S(14), Theme.S(14), 0);

            var cancel = UiKit.SecondaryButton("取消");
            cancel.Size = new Size(Theme.S(78), Theme.S(34));
            cancel.Margin = new Padding(Theme.S(6), Theme.S(6), 0, 0);
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            _ok = UiKit.PrimaryButton("生成并加入表格");
            _ok.Size = new Size(Theme.S(140), Theme.S(34));
            _ok.Margin = new Padding(Theme.S(8), Theme.S(6), 0, 0);
            _ok.Click += (s, e) => Generate();

            buttons.Controls.Add(_clearExisting);
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(_ok);
            bottom.Controls.Add(buttons);
            bottom.Controls.Add(_error);
            root.Controls.Add(bottom, 0, 3);

            Controls.Add(root);
            AcceptButton = _ok;
            CancelButton = cancel;

            Shown += (s, e) =>
            {
                SyncFromCount();
                _start.Focus();
                _start.SelectAll();
            };
        }

        private static Label FieldLabel(string text)
        {
            var label = Theme.Label(text, 8.9f, Theme.Muted);
            label.AutoSize = false;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        private static Label FieldHint(string text)
        {
            var label = Theme.Label(text, 8.4f, Theme.Muted);
            label.AutoSize = false;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Padding = new Padding(Theme.S(10), 0, 0, 0);
            label.AutoEllipsis = true;
            return label;
        }

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
                return;
            }

            string text = _end.Text;
            long last;
            int endDigits;
            if (!TryParseEndNumber(text, out last, out endDigits))
            {
                SetError("最后一个编号请填数字，例如 0050");
                _ok.Enabled = false;
                return;
            }

            long step = (long)_step.Value;
            long count = SequenceGenerator.CountFor(start, last, step);
            if (count < 0)
            {
                SetError("从 " + start + " 按步长 " + step + " 走不到 " + last + "，请调整步长或末位编号");
                _ok.Enabled = false;
                UpdateSample();
                return;
            }
            if (count < 1)
            {
                SetError("最后一个编号不能小于起始编号");
                _ok.Enabled = false;
                UpdateSample();
                return;
            }
            if (count > SequenceGenerator.MaxCount)
            {
                SetError("数量超过 " + SequenceGenerator.MaxCount + " 行，请缩小范围");
                _ok.Enabled = false;
                UpdateSample();
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

        private void UpdatePreview()
        {
            SequenceSpec spec = BuildSpec();
            string error = SequenceGenerator.Validate(spec);
            if (error.Length > 0)
            {
                SetError(error);
                _ok.Enabled = false;
                _sample.Text = "";
                _summary.Text = "";
                return;
            }

            SetError("");
            _ok.Enabled = true;
            _summary.Text = "共 " + spec.Count + " 个 · 首个 " + SequenceGenerator.Compose(spec, spec.Start) +
                            " · 末个 " + SequenceGenerator.Compose(spec, SequenceGenerator.LastValue(spec.Start, spec.Step, spec.Count));

            var lines = new List<string>();
            foreach (string item in SequenceGenerator.Preview(spec, 3, 3)) lines.Add(item);
            _sample.Text = string.Join("\r\n", lines.ToArray());
        }

        private void UpdateSample()
        {
            _sample.Text = "";
            _summary.Text = "";
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
