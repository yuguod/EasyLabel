using System;
using System.Drawing;
using System.Windows.Forms;
using EasyLabel.Core;

namespace EasyLabel.UI
{
    public sealed partial class MainForm : Form
    {
        private readonly AppSettings _settings;
        private readonly System.Windows.Forms.Timer _previewTimer;
        private Panel _pageHost;
        private Panel _headerPanel;
        private Panel _statusPanel;
        private Button _headerSettingsButton;
        private Panel[] _pages;
        private Button[] _navButtons;
        private Label _statusLabel;
        private Label _statusRight;
        private Label _headerPrinter;
        private int _currentPage = -1;
        private bool _loadingUi;
        private readonly int _startPage;
        private readonly bool _demo;
        private readonly bool _importDemo;
        private readonly System.Collections.Generic.List<Action> _pageLayouts =
            new System.Collections.Generic.List<Action>();

        public MainForm(int startPage = 0, bool demo = false, bool importDemo = false)
        {
            _startPage = startPage;
            _demo = demo;
            _importDemo = importDemo;
            _settings = AppSettings.Load();
            _loadingUi = true;

            Text = AppInfo.ProductName;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1280, 720);
            MinimumSize = new Size(1100, 680);
            BackColor = Theme.Background;
            Font = Theme.Ui(9f);
            AutoScaleMode = AutoScaleMode.None;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Theme.SetDoubleBuffered(this);

            BuildShell();
            // 界面按 96 dpi 设计，这里按实际 DPI 统一放大，否则字号放大而控件尺寸不放大，文字会被裁掉。
            Theme.ApplyScale(this, new Size(1280, 720));
            // 缩放过程中 Resize 用的是“已放大”的宽度，随后控件自身又被乘了一次系数，
            // 手工定位的页头/状态栏会被推到窗口外，这里按最终尺寸重排一遍。
            LayoutHeader();
            LayoutStatus();
            MinimumSize = new Size(Theme.S(1100), Theme.S(680));
            _previewTimer = new System.Windows.Forms.Timer { Interval = 180 };
            _previewTimer.Tick += (s, e) =>
            {
                _previewTimer.Stop();
                UpdateSinglePreview();
            };

            Load += (s, e) =>
            {
                try
                {
                    Rectangle wa = Screen.FromControl(this).WorkingArea;
                    int maxW = Math.Max(1000, wa.Width - 24);
                    int maxH = Math.Max(640, wa.Height - 48);
                    if (Width > maxW || Height > maxH)
                    {
                        ClientSize = new Size(Math.Min(ClientSize.Width, maxW), Math.Min(ClientSize.Height, maxH));
                    }
                }
                catch { }
                _loadingUi = true;
                RefreshPrinterList();
                UpdatePrinterStatus();
                _loadingUi = false;
                LayoutHeader();
                LayoutStatus();
                ShowPage(Math.Max(0, Math.Min(_pages.Length - 1, _startPage)));
                RelayoutPages();
                UpdateSinglePreview();
                if (_demo) LoadDemo();
                if (_importDemo) ShowImportDemo();
                Logger.Info("程序启动，DPI=" + DeviceDpi + "，界面缩放=" + Theme.Scale.ToString("0.##") +
                            "，窗口=" + Width + "x" + Height);
            };
            FormClosing += (s, e) =>
            {
                try
                {
                    if (!_settingsUiLoaded) LoadSettingsToUi();
                    ReadSettingsFromUi();
                    _settings.Save();
                }
                catch { }
                _previewTimer.Dispose();
            };
        }

        private void BuildShell()
        {
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 74,
                BackColor = Color.White
            };
            header.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Border))
                    e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
                using (var brush = new SolidBrush(Theme.Accent))
                    e.Graphics.FillRectangle(brush, 0, 0, 4, header.Height);
            };

            var title = Theme.Label(AppInfo.ProductName, 18f, Theme.Text, FontStyle.Bold);
            title.Location = new Point(24, 12);
            var subtitle = Theme.Label(AppInfo.Tagline, 8.8f, Theme.Muted);
            subtitle.Location = new Point(26, 45);
            header.Controls.Add(title);
            header.Controls.Add(subtitle);

            _headerPrinter = Theme.Label("打印机：未选择", 9f, Theme.Warning);
            _headerPrinter.AutoSize = false;
            _headerPrinter.TextAlign = ContentAlignment.MiddleRight;
            _headerPrinter.Height = 24;
            header.Controls.Add(_headerPrinter);

            var settingsButton = UiKit.SecondaryButton("打印机设置");
            settingsButton.Size = new Size(108, 32);
            settingsButton.Click += (s, e) => ShowPage(2);
            header.Controls.Add(settingsButton);
            _headerSettingsButton = settingsButton;

            BuildTemplateButton(header);

            _headerPanel = header;
            header.Resize += (s, e) => LayoutHeader();

            var sidebar = new Panel
            {
                Dock = DockStyle.Left,
                Width = 184,
                BackColor = Color.White,
                Padding = new Padding(12, 16, 12, 12)
            };
            sidebar.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Border))
                    e.Graphics.DrawLine(pen, sidebar.Width - 1, 0, sidebar.Width - 1, sidebar.Height);
            };

            var navHost = new Panel { Dock = DockStyle.Top, Height = 296, BackColor = Color.White };
            _navButtons = new Button[5];
            string[] names = { "单张打印", "批量打印", "打印机设置", "使用说明", "关于" };
            for (int i = names.Length - 1; i >= 0; i--)
            {
                int page = i;
                var b = new Button
                {
                    Text = names[i],
                    Dock = DockStyle.Top,
                    Height = 46,
                    FlatStyle = FlatStyle.Flat,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(20, 0, 0, 0),
                    Font = Theme.Ui(10f),
                    ForeColor = Theme.Muted,
                    BackColor = Color.White,
                    Cursor = Cursors.Hand,
                    UseVisualStyleBackColor = false
                };
                b.FlatAppearance.BorderSize = 0;
                b.FlatAppearance.MouseOverBackColor = Theme.AccentSoft;
                b.Click += (s, e) => ShowPage(page);
                _navButtons[i] = b;
                navHost.Controls.Add(b);
            }

            var version = Theme.Label("v" + AppInfo.Version + " · 绿色版", 8.2f, Theme.Muted);
            version.Dock = DockStyle.Bottom;
            version.TextAlign = ContentAlignment.MiddleCenter;
            version.Height = 30;
            sidebar.Controls.Add(version);
            sidebar.Controls.Add(navHost);

            _pageHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Background,
                Padding = new Padding(22, 18, 22, 14)
            };

            var status = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 30,
                BackColor = Color.White
            };
            status.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Border))
                    e.Graphics.DrawLine(pen, 0, 0, status.Width, 0);
            };
            _statusLabel = Theme.Label("就绪", 8.6f, Theme.Muted);
            _statusLabel.AutoSize = false;
            _statusLabel.Location = new Point(16, 6);
            _statusLabel.Width = 800;
            _statusLabel.Height = 20;
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusRight = Theme.Label("ZPL · " + _settings.Dpi + " dpi", 8.6f, Theme.Muted);
            _statusRight.AutoSize = false;
            _statusRight.TextAlign = ContentAlignment.MiddleRight;
            _statusRight.Height = 20;
            _statusRight.Width = 204;
            status.Controls.Add(_statusLabel);
            status.Controls.Add(_statusRight);
            _statusPanel = status;
            status.Resize += (s, e) => LayoutStatus();

            _pages = new Panel[5];
            _pages[0] = BuildSinglePage();
            _pages[1] = BuildBatchPage();
            _pages[2] = BuildSettingsPage();
            _pages[3] = BuildHelpPage();
            _pages[4] = BuildCopyrightPage();
            foreach (var page in _pages)
            {
                page.Visible = false;
                page.Dock = DockStyle.Fill;
                _pageHost.Controls.Add(page);
            }

            Controls.Add(_pageHost);
            Controls.Add(sidebar);
            Controls.Add(header);
            Controls.Add(status);
        }

        /// <summary>
        /// 页头右侧按实际宽度右对齐排布：打印机文字、模版、打印机设置。
        /// 全部用控件自身宽度推算，缩放后不会因为写死的像素值错位。
        /// </summary>
        private void LayoutHeader()
        {
            if (_headerPanel == null || _headerSettingsButton == null) return;
            int width = _headerPanel.Width;
            if (width < 200) return;

            _headerSettingsButton.Location = new Point(
                width - _headerSettingsButton.Width - Theme.S(24), Theme.S(21));
            if (_templateButton != null)
            {
                _templateButton.Location = new Point(
                    _headerSettingsButton.Left - _templateButton.Width - Theme.S(8), Theme.S(21));
            }

            int leftEdge = Math.Max(Theme.S(300), width - Theme.S(712));
            int rightEdge = (_templateButton != null ? _templateButton.Left : _headerSettingsButton.Left) - Theme.S(16);
            _headerPrinter.Location = new Point(leftEdge, Theme.S(23));
            _headerPrinter.Width = Math.Max(Theme.S(120), rightEdge - leftEdge);
        }

        private void LayoutStatus()
        {
            if (_statusPanel == null || _statusRight == null) return;
            if (_statusPanel.Width < 100) return;
            _statusRight.Location = new Point(
                _statusPanel.Width - _statusRight.Width - Theme.S(16), Theme.S(6));
        }

        private void ShowPage(int index)
        {
            if (index < 0 || index >= _pages.Length) return;
            _currentPage = index;
            for (int i = 0; i < _pages.Length; i++)
                _pages[i].Visible = i == index;
            for (int i = 0; i < _navButtons.Length; i++)
            {
                bool active = i == index;
                _navButtons[i].BackColor = active ? Theme.AccentSoft : Color.White;
                _navButtons[i].ForeColor = active ? Theme.Accent : Theme.Muted;
                _navButtons[i].Font = Theme.Ui(10f, active ? FontStyle.Bold : FontStyle.Regular);
            }
            if (index == 2) LoadSettingsToUi();
            if (index == 0)
            {
                // 设置页可能改过 DPI，切回来要按当前设置重绘，而不是沿用旧分辨率的图。
                UpdateSinglePreview();
            }
            if (index == 1)
            {
                UpdateBatchPreview();
                ResetBatchScroll();
            }
            Relayout(index);
        }

        /// <summary>按当前实际尺寸重算某一页的手工布局。</summary>
        private void Relayout(int index)
        {
            if (index < 0 || index >= _pageLayouts.Count) return;
            try { _pageLayouts[index](); } catch { }
        }

        private void RelayoutPages()
        {
            for (int i = 0; i < _pageLayouts.Count; i++) Relayout(i);
        }

        private void RefreshPrinterList()
        {
            if (_printerCombo == null) return;
            string current = _settings.PrinterName;
            _printerCombo.Items.Clear();
            var printers = PrinterService.GetInstalledPrinters();

            // 重新填充列表会让 SelectedIndex 归位并触发 SelectedIndexChanged，
            // 这里标记一下，避免“只是刷新列表”被当成用户换队列而关掉网络直连。
            _refreshingPrinters = true;
            try
            {
                foreach (string p in printers) _printerCombo.Items.Add(p);

                if (printers.Count == 0)
                {
                    _printerCombo.Items.Add("（未检测到打印机）");
                    _printerCombo.SelectedIndex = 0;
                    _printerCombo.Enabled = false;
                }
                else
                {
                    _printerCombo.Enabled = true;
                    int idx = !string.IsNullOrEmpty(current) ? _printerCombo.Items.IndexOf(current) : -1;
                    if (idx < 0)
                    {
                        for (int i = 0; i < _printerCombo.Items.Count; i++)
                        {
                            if (PrinterService.IsZebraLike(Convert.ToString(_printerCombo.Items[i])))
                            {
                                idx = i;
                                break;
                            }
                        }
                    }
                    _printerCombo.SelectedIndex = idx;
                }
                UpdatePrinterHint();
            }
            finally
            {
                _refreshingPrinters = false;
            }
        }

        private void UpdatePrinterStatus()
        {
            string printer = _settings.UseNetwork
                ? "网络 " + _settings.NetworkHost + ":" + _settings.NetworkPort
                : (_settings.PrinterName ?? "");
            if (string.IsNullOrWhiteSpace(printer))
            {
                _headerPrinter.Text = "打印机：未选择";
                _headerPrinter.ForeColor = Theme.Warning;
            }
            else
            {
                _headerPrinter.Text = "打印机：" + printer + "  ·  " + _settings.Dpi + " dpi";
                _headerPrinter.ForeColor = Theme.Success;
            }
            _statusRight.Text = "ZPL · " + _settings.Dpi + " dpi";
        }

        private void SetStatus(string text, bool error = false, bool warning = false)
        {
            _statusLabel.Text = text ?? "";
            _statusLabel.ForeColor = error ? Theme.Error : (warning ? Theme.Warning : Theme.Muted);
        }

        private void UpdatePrinterHint()
        {
            if (_printerHint == null) return;
            if (_printerCombo == null || _printerCombo.Items.Count == 0 ||
                _printerCombo.SelectedItem == null ||
                string.Equals(Convert.ToString(_printerCombo.SelectedItem), "（未检测到打印机）"))
            {
                _printerHint.Text = "未选择打印机队列。可保存 ZPL 文件，或勾选“网络直连”填写打印机 IP。";
                _printerHint.ForeColor = Theme.Warning;
                return;
            }
            string name = Convert.ToString(_printerCombo.SelectedItem);
            if (PrinterService.IsZebraLike(name))
            {
                _printerHint.Text = "已识别为标签打印机队列（ZPL 驱动）。请确认队列名称中的 DPI 与实际机器一致。";
                _printerHint.ForeColor = Theme.Success;
            }
            else
            {
                _printerHint.Text = "该队列可能不是 ZPL 驱动；直接发送 ZPL 仍可尝试，但建议安装打印机厂商的 ZPL 驱动。";
                _printerHint.ForeColor = Theme.Muted;
            }
        }

        private void SchedulePreview()
        {
            if (_loadingUi) return;
            _previewTimer.Stop();
            _previewTimer.Start();
        }

        private bool _changingLabelSize;
        private bool _refreshingPrinters;

        private void FillLabelSizeCombo(ComboBox combo, LabelSpec selected)
        {
            _changingLabelSize = true;
            try
            {
                combo.Items.Clear();
                foreach (LabelSpec spec in LabelSpec.All) combo.Items.Add(spec);
                if (selected != null && FindSpecIndex(combo, selected) < 0) combo.Items.Add(selected);
                combo.Items.Add("自定义…");
                int index = selected != null ? FindSpecIndex(combo, selected) : 0;
                combo.SelectedIndex = Math.Max(0, index);
                combo.Tag = selected ?? LabelSpec.Label40x15;
            }
            finally
            {
                _changingLabelSize = false;
            }
        }

        private void OnLabelSizeComboChanged(ComboBox combo, Action changed)
        {
            if (_changingLabelSize) return;
            LabelSpec spec = combo.SelectedItem as LabelSpec;
            if (spec != null)
            {
                combo.Tag = spec;
                changed();
                return;
            }

            string text = combo.SelectedItem as string;
            if (!string.Equals(text, "自定义…")) return;

            LabelSpec previous = combo.Tag as LabelSpec;
            if (previous == null) previous = _settings.GetLabelSpec();
            LabelSpec custom = null;
            _changingLabelSize = true;
            try
            {
                using (var dialog = new CustomSizeDialog(previous))
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK) custom = dialog.Spec;
                }

                if (custom == null)
                {
                    combo.SelectedItem = previous;
                    return;
                }

                int placeholder = combo.Items.IndexOf("自定义…");
                if (placeholder >= 0) combo.Items.RemoveAt(placeholder);
                int existing = FindSpecIndex(combo, custom);
                if (existing < 0)
                {
                    combo.Items.Add(custom);
                    existing = combo.Items.Count - 1;
                }
                combo.Items.Add("自定义…");
                combo.SelectedIndex = existing;
                combo.Tag = custom;
                _settings.DefaultLabel = custom.Name;
            }
            finally
            {
                _changingLabelSize = false;
            }
            changed();
        }

        private static int FindSpecIndex(ComboBox combo, LabelSpec spec)
        {
            if (spec == null) return -1;
            for (int i = 0; i < combo.Items.Count; i++)
            {
                LabelSpec item = combo.Items[i] as LabelSpec;
                if (item != null &&
                    Math.Abs(item.WidthMm - spec.WidthMm) < 0.001 &&
                    Math.Abs(item.HeightMm - spec.HeightMm) < 0.001)
                    return i;
            }
            return -1;
        }
    }
}
