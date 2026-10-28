using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace EasyLabel.UI
{
    public static class Theme
    {
        public static readonly Color Background = Color.FromArgb(244, 246, 250);
        public static readonly Color Card = Color.White;
        public static readonly Color Border = Color.FromArgb(226, 230, 236);
        public static readonly Color Text = Color.FromArgb(31, 41, 55);
        public static readonly Color Muted = Color.FromArgb(107, 114, 128);
        public static readonly Color Accent = Color.FromArgb(37, 99, 235);
        public static readonly Color AccentDark = Color.FromArgb(29, 78, 216);
        public static readonly Color AccentSoft = Color.FromArgb(235, 242, 255);
        public static readonly Color Success = Color.FromArgb(22, 163, 74);
        public static readonly Color Warning = Color.FromArgb(217, 119, 6);
        public static readonly Color Error = Color.FromArgb(220, 38, 38);
        public static readonly Color GridAlt = Color.FromArgb(249, 250, 252);

        public static readonly string FontName = "Microsoft YaHei UI";

        /// <summary>
        /// 界面缩放系数。字号以 pt 为单位，会随系统 DPI 自动放大（192 dpi 时约 2 倍），
        /// 而控件尺寸是像素值不会自动跟随，两者不匹配就会把文字裁掉一半。
        /// 这里按屏幕 DC 的实际 DPI 统一放大控件树。
        /// </summary>
        public static float Scale { get; private set; }

        static Theme()
        {
            Scale = DetectScale();
        }

        private static float DetectScale()
        {
            try
            {
                using (var g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    float s = g.DpiX / 96f;
                    if (s < 1f) s = 1f;
                    if (s > 3f) s = 3f;
                    return s;
                }
            }
            catch
            {
                return 1f;
            }
        }

        /// <summary>
        /// 在创建任何界面之前调用：按主窗体设计尺寸限制缩放比例，保证窗口能放进屏幕工作区。
        /// </summary>
        public static void Initialize(Size designSize)
        {
            float scale = DetectScale();
            try
            {
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                float maxW = (wa.Width - 40f) / Math.Max(1, designSize.Width);
                float maxH = (wa.Height - 96f) / Math.Max(1, designSize.Height);
                float max = Math.Min(maxW, maxH);
                if (scale > max) scale = Math.Max(1f, max);
            }
            catch { }
            Scale = scale;
        }

        /// <summary>把 96 dpi 下的设计像素换算成当前 DPI 的像素。</summary>
        public static int S(int px)
        {
            return (int)Math.Round(px * Scale);
        }

        public static float SF(float px)
        {
            return px * Scale;
        }

        public static Padding S(Padding p)
        {
            return new Padding(S(p.Left), S(p.Top), S(p.Right), S(p.Bottom));
        }

        /// <summary>
        /// 按当前 DPI 放大窗体及其全部子控件。必须在界面构建完成后、显示之前调用一次。
        /// 只缩放位置/尺寸，字体已经是 pt 单位、由 DPI 自行放大，不会重复放大。
        /// </summary>
        public static void ApplyScale(Form form, Size designSize)
        {
            if (form == null) return;
            form.AutoScaleMode = AutoScaleMode.None;
            if (Scale <= 1.001f) return;

            form.Scale(new SizeF(Scale, Scale));

            try
            {
                Rectangle wa = Screen.FromControl(form).WorkingArea;
                if (form.Width > wa.Width) form.Width = wa.Width;
                if (form.Height > wa.Height) form.Height = wa.Height;
            }
            catch { }
        }

        public static Font Ui(float size, FontStyle style = FontStyle.Regular)
        {
            try { return new Font(FontName, size, style, GraphicsUnit.Point); }
            catch { return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point); }
        }

        public static void StylePrimary(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = Accent;
            button.ForeColor = Color.White;
            button.Font = Ui(9.5f, FontStyle.Bold);
            button.Cursor = Cursors.Hand;
            button.Height = 36;
            button.UseVisualStyleBackColor = false;
            button.FlatAppearance.MouseOverBackColor = AccentDark;
            button.FlatAppearance.MouseDownBackColor = AccentDark;
        }

        public static void StyleSecondary(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Border;
            button.BackColor = Color.White;
            button.ForeColor = Text;
            button.Font = Ui(9.2f);
            button.Cursor = Cursors.Hand;
            button.Height = 36;
            button.UseVisualStyleBackColor = false;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(248, 250, 253);
        }

        public static void StyleLink(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = Color.Transparent;
            button.ForeColor = Accent;
            button.Font = Ui(9.2f);
            button.Cursor = Cursors.Hand;
            button.Height = 28;
            button.UseVisualStyleBackColor = false;
        }

        public static void StyleTextBox(TextBox box)
        {
            box.BorderStyle = BorderStyle.FixedSingle;
            box.BackColor = Color.White;
            box.ForeColor = Text;
            box.Font = Ui(9.5f);
        }

        public static void StyleCombo(ComboBox combo)
        {
            combo.FlatStyle = FlatStyle.Flat;
            combo.BackColor = Color.White;
            combo.ForeColor = Text;
            combo.Font = Ui(9.5f);
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.Height = 28;
        }

        public static void StyleCheck(CheckBox check)
        {
            check.Font = Ui(9.2f);
            check.ForeColor = Text;
            check.BackColor = Color.Transparent;
            check.Cursor = Cursors.Hand;
        }

        public static void StyleNumeric(NumericUpDown num)
        {
            num.BorderStyle = BorderStyle.FixedSingle;
            num.Font = Ui(9.2f);
            num.ForeColor = Text;
            num.BackColor = Color.White;
            num.Height = 28;
        }

        public static void StyleGrid(DataGridView grid)
        {
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.GridColor = Color.FromArgb(238, 241, 245);
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
            grid.ColumnHeadersDefaultCellStyle.Font = Ui(9f, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(248, 250, 252);
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Muted;
            grid.ColumnHeadersHeight = Theme.S(34);
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.DefaultCellStyle.BackColor = Color.White;
            grid.DefaultCellStyle.ForeColor = Text;
            grid.DefaultCellStyle.Font = Ui(9f);
            grid.DefaultCellStyle.SelectionBackColor = AccentSoft;
            grid.DefaultCellStyle.SelectionForeColor = Text;
            grid.DefaultCellStyle.Padding = new Padding(4, 2, 4, 2);
            grid.AlternatingRowsDefaultCellStyle.BackColor = GridAlt;
            grid.RowHeadersVisible = false;
            // DataGridView 的列宽/行高不会被 Control.Scale 缩放，这里自己按 DPI 放大。
            grid.RowTemplate.Height = Theme.S(30);
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
        }

        public static Label Label(string text, float size, Color color, FontStyle style = FontStyle.Regular)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Font = Ui(size, style),
                ForeColor = color,
                BackColor = Color.Transparent
            };
        }

        public static void SetDoubleBuffered(Control control)
        {
            if (control == null) return;
            try
            {
                var prop = typeof(Control).GetProperty("DoubleBuffered",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (prop != null) prop.SetValue(control, true, null);
            }
            catch { }
        }
    }

    public sealed class CardPanel : Panel
    {
        public string Title { get; set; }
        public Panel Content { get; private set; }

        /// <summary>是否按内容自动调整高度。预览卡片需要撑满剩余空间，应设为 false。</summary>
        public bool AutoFitHeight { get; set; }

        private readonly Label _titleLabel;

        public CardPanel(string title)
        {
            Title = title ?? "";
            BackColor = Theme.Card;
            Padding = new Padding(1);
            AutoFitHeight = true;
            Theme.SetDoubleBuffered(this);
            Content = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Card,
                Padding = new Padding(16, 0, 16, 14)
            };
            _titleLabel = new Label
            {
                Text = Title,
                Dock = DockStyle.Top,
                Height = 44,
                Font = Theme.Ui(10.5f, FontStyle.Bold),
                ForeColor = Theme.Text,
                BackColor = Theme.Card,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(16, 0, 0, 0)
            };
            Controls.Add(Content);
            Controls.Add(_titleLabel);
        }

        /// <summary>
        /// 按内容实际需要的高度设置卡片高度，避免固定高度写小了把内容裁掉。
        /// </summary>
        public void FitHeight()
        {
            if (!AutoFitHeight) return;
            int inner = Width - Padding.Horizontal - Content.Padding.Horizontal;
            if (inner < 60) return;

            int stacked = 0;
            int filled = 0;
            int free = 0;
            foreach (Control c in Content.Controls)
            {
                if (c.Dock == DockStyle.Top || c.Dock == DockStyle.Bottom)
                {
                    // 上/下停靠的子控件高度是显式设置的；GetPreferredSize 对 TextBox 之类的返回值不可靠。
                    stacked += c.Height + c.Margin.Vertical;
                    continue;
                }
                int h = c.GetPreferredSize(new Size(Math.Max(20, inner), 0)).Height + c.Margin.Vertical;
                if (c.Dock == DockStyle.Fill) filled = Math.Max(filled, h);
                else free = Math.Max(free, c.Bottom + c.Margin.Bottom);
            }

            int needed = Math.Max(stacked + filled, free);
            int total = needed + Content.Padding.Vertical + (_titleLabel.Visible ? _titleLabel.Height : 0) + 2;
            if (total > 0 && Math.Abs(Height - total) > 2) Height = total;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Theme.Border))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
            if (string.IsNullOrEmpty(Title)) _titleLabel.Visible = false;
        }
    }

    public static class UiKit
    {
        public static Button PrimaryButton(string text)
        {
            var b = new Button { Text = text, AutoSize = false };
            Theme.StylePrimary(b);
            return b;
        }

        public static Button SecondaryButton(string text)
        {
            var b = new Button { Text = text, AutoSize = false };
            Theme.StyleSecondary(b);
            return b;
        }

        public static Button LinkButton(string text)
        {
            var b = new Button { Text = text, AutoSize = true };
            Theme.StyleLink(b);
            return b;
        }

        public static TextBox TextBox(bool multiline, int height)
        {
            var t = new TextBox { Multiline = multiline, Height = height, ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None };
            Theme.StyleTextBox(t);
            return t;
        }

        public static ComboBox Combo()
        {
            var c = new ComboBox { Width = 160 };
            Theme.StyleCombo(c);
            return c;
        }

        public static ComboBox FontFamilyCombo()
        {
            var c = Combo();
            c.Items.AddRange(new object[]
            {
                "Microsoft YaHei UI",
                "Microsoft YaHei",
                "SimHei",
                "SimSun",
                "Segoe UI",
                "Arial"
            });
            c.SelectedIndex = 0;
            c.Width = 170;
            return c;
        }

        public static string GetFontFamily(ComboBox combo)
        {
            if (combo == null || combo.SelectedItem == null) return "Microsoft YaHei UI";
            string value = Convert.ToString(combo.SelectedItem);
            return string.IsNullOrWhiteSpace(value) ? "Microsoft YaHei UI" : value;
        }

        public static void SelectFontFamily(ComboBox combo, string family)
        {
            if (combo == null) return;
            if (string.IsNullOrWhiteSpace(family)) family = "Microsoft YaHei UI";
            int index = combo.Items.IndexOf(family);
            if (index < 0)
            {
                combo.Items.Add(family);
                index = combo.Items.Count - 1;
            }
            combo.SelectedIndex = index;
        }

        public static ComboBox FontSizeCombo()
        {
            var c = Combo();
            c.Items.Add("自动");
            for (double mm = 1.5; mm <= 10.0001; mm += 0.5)
                c.Items.Add(mm.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " mm");
            c.SelectedIndex = 0;
            c.Width = 112;
            return c;
        }

        public static double GetFontSizeMm(ComboBox combo)
        {
            if (combo == null || combo.SelectedIndex <= 0) return 0;
            string text = Convert.ToString(combo.SelectedItem);
            int space = text.IndexOf(' ');
            if (space > 0) text = text.Substring(0, space);
            double mm;
            return double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out mm) ? mm : 0;
        }

        public static void SelectFontSize(ComboBox combo, double mm)
        {
            if (combo == null) return;
            if (mm <= 0)
            {
                combo.SelectedIndex = 0;
                return;
            }
            string wanted = mm.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " mm";
            int index = combo.Items.IndexOf(wanted);
            if (index < 0)
            {
                double best = double.MaxValue;
                for (int i = 1; i < combo.Items.Count; i++)
                {
                    string text = Convert.ToString(combo.Items[i]);
                    double v;
                    if (double.TryParse(text.Split(' ')[0], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out v))
                    {
                        double diff = Math.Abs(v - mm);
                        if (diff < best) { best = diff; index = i; }
                    }
                }
            }
            combo.SelectedIndex = index < 0 ? 0 : index;
        }

        public static NumericUpDown Numeric(decimal min, decimal max, decimal value, int decimals = 0, decimal step = 1)
        {
            var n = new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Value = Math.Min(max, Math.Max(min, value)),
                DecimalPlaces = decimals,
                Increment = step,
                Width = 90
            };
            Theme.StyleNumeric(n);
            return n;
        }

        public static CheckBox Check(string text, bool isChecked)
        {
            var c = new CheckBox { Text = text, Checked = isChecked, AutoSize = true };
            Theme.StyleCheck(c);
            return c;
        }

        public static FlowLayoutPanel Flow()
        {
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Theme.Background,
                Padding = new Padding(0)
            };
            Theme.SetDoubleBuffered(flow);
            return flow;
        }

        public static TableLayoutPanel FormTable(int rows)
        {
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = Math.Max(1, rows),
                BackColor = Theme.Card,
                AutoSize = false
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < table.RowCount; i++)
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            return table;
        }

        public static void AddRow(TableLayoutPanel table, int row, string label, Control control, bool span = false, bool fill = false)
        {
            var lbl = Theme.Label(label, 9.2f, Theme.Muted);
            lbl.Anchor = AnchorStyles.Left;
            lbl.Margin = new Padding(0, 6, 8, 6);
            control.Margin = fill ? new Padding(0, 4, 24, 4) : new Padding(0, 4, 0, 4);
            // 数字框、下拉框按自身宽度左对齐即可；文本框才拉满整行。
            control.Anchor = fill ? (AnchorStyles.Left | AnchorStyles.Right) : AnchorStyles.Left;
            if (span)
            {
                table.Controls.Add(lbl, 0, row);
                table.SetColumnSpan(lbl, 2);
                table.Controls.Add(control, 0, row + 1);
                table.SetColumnSpan(control, 2);
            }
            else
            {
                table.Controls.Add(lbl, 0, row);
                table.Controls.Add(control, 1, row);
            }
        }

        public static Panel Spacer(int height)
        {
            return new Panel { Height = height, Width = 1, BackColor = Color.Transparent };
        }

        private static bool _fittingCards;

        public static void FitCardWidths(FlowLayoutPanel flow)
        {
            if (flow == null || _fittingCards) return;
            int width = flow.ClientSize.Width - flow.Padding.Horizontal - 2;
            if (width < 100) return;
            _fittingCards = true;
            try
            {
                foreach (Control c in flow.Controls)
                {
                    if (c is CardPanel) c.Width = width;
                }
                foreach (Control c in flow.Controls)
                {
                    var card = c as CardPanel;
                    if (card != null) card.FitHeight();
                }
            }
            finally
            {
                _fittingCards = false;
            }
        }
    }
}
