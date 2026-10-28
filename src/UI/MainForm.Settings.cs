using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using EasyLabel.Core;

namespace EasyLabel.UI
{
    public sealed partial class MainForm
    {
        private ComboBox _printerCombo;
        private Label _printerHint;
        private CheckBox _networkCheck;
        private TextBox _networkHost;
        private NumericUpDown _networkPort;
        private ComboBox _dpiCombo;
        private NumericUpDown _darkness;
        private NumericUpDown _speed;
        private ComboBox _mediaCombo;
        private ComboBox _defaultLabelCombo;
        private ComboBox _defaultBarcodeCombo;
        private ComboBox _defaultQrCombo;
        private ComboBox _alignCombo;
        private CheckBox _settingsShowHri;
        private ComboBox _settingsFontFamily;
        private ComboBox _settingsFontSize;
        private CheckBox _settingsBold;
        private NumericUpDown _settingsMarginTop;
        private NumericUpDown _settingsMarginBottom;
        private NumericUpDown _settingsMarginLeft;
        private NumericUpDown _settingsMarginRight;
        private NumericUpDown _settingsBarcodeScale;
        private NumericUpDown _settingsQrScale;
        private Button _testPrint;
        private bool _settingsUiLoaded;

        private Panel BuildSettingsPage()
        {
            var page = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
            var flow = UiKit.Flow();
            flow.SizeChanged += (s, e) => UiKit.FitCardWidths(flow);
            page.Controls.Add(flow);

            BuildPrinterCard(flow);
            BuildPrintCard(flow);
            BuildDefaultsCard(flow);
            BuildMaintenanceCard(flow);
            _pageLayouts.Add(() => UiKit.FitCardWidths(flow));
            return page;
        }

        private void BuildPrinterCard(FlowLayoutPanel flow)
        {
            var card = AddCard(flow, "打印机连接", 326);
            var table = UiKit.FormTable(8);

            _networkCheck = UiKit.Check("使用网络直连（IP:9100），不经过 Windows 队列", false);
            _networkCheck.CheckedChanged += (s, e) =>
            {
                UpdateNetworkState();
                UpdatePrinterHint();
                if (!_loadingUi)
                {
                    _settings.UseNetwork = _networkCheck.Checked;
                    UpdatePrinterStatus();
                }
            };
            table.Controls.Add(_networkCheck, 0, 0);
            table.SetColumnSpan(_networkCheck, 2);

            var printerRow = new Panel { Height = 32, Dock = DockStyle.Fill };
            _printerCombo = UiKit.Combo();
            _printerCombo.Dock = DockStyle.Fill;
            _printerCombo.SelectedIndexChanged += (s, e) =>
            {
                if (_loadingUi || _refreshingPrinters) return;
                TryGuessDpi();
                UpdatePrinterHint();
                if (_printerCombo.SelectedItem != null)
                {
                    string name = Convert.ToString(_printerCombo.SelectedItem);
                    if (!string.Equals(name, "（未检测到打印机）"))
                    {
                        _settings.PrinterName = name;
                        // 选了队列就关掉网络直连，并同步复选框，避免界面与 _settings 互相覆盖。
                        if (_networkCheck != null && _networkCheck.Checked) _networkCheck.Checked = false;
                        _settings.UseNetwork = false;
                        UpdateNetworkState();
                        UpdatePrinterStatus();
                    }
                }
            };
            var refresh = UiKit.SecondaryButton("刷新");
            refresh.Dock = DockStyle.Right;
            refresh.Width = 62;
            refresh.Click += (s, e) => RefreshPrinterList();
            printerRow.Controls.Add(_printerCombo);
            printerRow.Controls.Add(refresh);
            UiKit.AddRow(table, 1, "打印队列", printerRow);

            var netRow = new Panel { Height = 32, Dock = DockStyle.Fill };
            _networkHost = UiKit.TextBox(false, 28);
            _networkHost.Dock = DockStyle.Fill;
            _networkHost.TextChanged += (s, e) =>
            {
                UpdatePrinterStatus();
                if (_loadingUi) return;
                _settings.NetworkHost = (_networkHost.Text ?? "").Trim();
            };
            _networkPort = UiKit.Numeric(1, 65535, 9100);
            _networkPort.Dock = DockStyle.Right;
            _networkPort.Width = 78;
            _networkPort.ValueChanged += (s, e) =>
            {
                UpdatePrinterStatus();
                if (_loadingUi) return;
                _settings.NetworkPort = (int)_networkPort.Value;
            };
            var portLabel = Theme.Label("端口", 8.6f, Theme.Muted);
            portLabel.Dock = DockStyle.Right;
            portLabel.Width = 44;
            portLabel.TextAlign = ContentAlignment.MiddleCenter;
            netRow.Controls.Add(_networkHost);
            netRow.Controls.Add(portLabel);
            netRow.Controls.Add(_networkPort);
            UiKit.AddRow(table, 2, "网络地址", netRow);

            _printerHint = Theme.Label("", 8.4f, Theme.Muted);
            _printerHint.AutoSize = false;
            _printerHint.Height = 42;
            _printerHint.Dock = DockStyle.Fill;
            table.Controls.Add(_printerHint, 0, 3);
            table.SetColumnSpan(_printerHint, 2);

            var help = Theme.Label("推荐使用打印机制造商提供的 ZPL 驱动创建队列；本程序以 RAW 方式直接发送 ZPL。", 8.4f, Theme.Muted);
            help.AutoSize = false;
            help.Dock = DockStyle.Fill;
            help.MinimumSize = new Size(0, 38);
            table.Controls.Add(help, 0, 4);
            table.SetColumnSpan(help, 2);

            var testRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                Height = 40,
                BackColor = Theme.Card
            };
            _testPrint = UiKit.SecondaryButton("打印测试页");
            _testPrint.Size = new Size(116, 36);
            _testPrint.Click += async (s, e) => await TestPrintAsync();
            testRow.Controls.Add(_testPrint);
            table.Controls.Add(testRow, 0, 5);
            table.SetColumnSpan(testRow, 2);

            card.Content.Controls.Add(table);
        }

        private void BuildPrintCard(FlowLayoutPanel flow)
        {
            var card = AddCard(flow, "打印参数", 300);
            var table = UiKit.FormTable(7);

            _dpiCombo = UiKit.Combo();
            _dpiCombo.Width = 168;
            _dpiCombo.Items.AddRange(new object[] { "203 dpi（8 点/mm）", "300 dpi（12 点/mm）", "600 dpi（24 点/mm）" });
            _dpiCombo.SelectedIndexChanged += (s, e) =>
            {
                UpdatePrinterStatus();
                if (_loadingUi) return;
                int dpi = DpiFromIndex(_dpiCombo.SelectedIndex);
                if (_settings.Dpi == dpi) return;
                // 预览走的是 _settings.Dpi，改完必须立即写回并重绘，否则要等到点一次打印才生效。
                _settings.Dpi = dpi;
                Logger.Info("打印分辨率改为 " + dpi + " dpi");
                UpdateSinglePreview();
                UpdateBatchPreview();
            };
            UiKit.AddRow(table, 0, "打印分辨率", _dpiCombo);

            _darkness = UiKit.Numeric(-30, 30, 0);
            _darkness.ValueChanged += (s, e) => UpdatePrinterStatus();
            UiKit.AddRow(table, 1, "浓度", _darkness);

            _speed = UiKit.Numeric(2, 14, 4);
            UiKit.AddRow(table, 2, "速度 ips", _speed);

            _mediaCombo = UiKit.Combo();
            _mediaCombo.Width = 178;
            _mediaCombo.Items.AddRange(new object[] { "间隙纸 / 标签纸（推荐）", "连续纸" });
            _mediaCombo.SelectedIndex = 0;
            UiKit.AddRow(table, 3, "介质类型", _mediaCombo);

            var defaultLabel = UiKit.Combo();
            defaultLabel.Width = 178;
            FillLabelSizeCombo(defaultLabel, _settings.GetLabelSpec());
            defaultLabel.SelectedIndexChanged += (s, e) => OnLabelSizeComboChanged(defaultLabel, delegate { });
            _defaultLabelCombo = defaultLabel;
            UiKit.AddRow(table, 4, "默认规格", _defaultLabelCombo);

            var hint = Theme.Label("浓度建议先保持 0；偏浅/偏深时再微调。测试页外框可用于核对 DPI 是否选对。", 8.4f, Theme.Muted);
            hint.AutoSize = false;
            hint.Dock = DockStyle.Fill;
            hint.MinimumSize = new Size(0, 38);
            table.Controls.Add(hint, 0, 5);
            table.SetColumnSpan(hint, 2);

            card.Content.Controls.Add(table);
        }

        private void BuildDefaultsCard(FlowLayoutPanel flow)
        {
            var card = AddCard(flow, "默认版式", 680);
            var table = UiKit.FormTable(16);

            _defaultBarcodeCombo = UiKit.Combo();
            _defaultBarcodeCombo.Width = 178;
            _defaultBarcodeCombo.Items.AddRange(new object[] { "Code 128（推荐）", "Code 39", "EAN-13" });
            UiKit.AddRow(table, 0, "默认一维码", _defaultBarcodeCombo);

            _defaultQrCombo = UiKit.Combo();
            _defaultQrCombo.Width = 158;
            _defaultQrCombo.Items.AddRange(new object[] { "L（约 7%）", "M（约 15%）", "Q（约 25%）", "H（约 30%）" });
            UiKit.AddRow(table, 1, "默认 QR 纠错", _defaultQrCombo);

            _settingsShowHri = UiKit.Check("默认显示一维码明文", true);
            table.Controls.Add(_settingsShowHri, 0, 2);
            table.SetColumnSpan(_settingsShowHri, 2);

            _alignCombo = UiKit.Combo();
            _alignCombo.Width = 118;
            _alignCombo.Items.AddRange(new object[] { "左对齐", "居中", "右对齐" });
            UiKit.AddRow(table, 3, "文字对齐", _alignCombo);

            _settingsFontFamily = UiKit.FontFamilyCombo();
            UiKit.SelectFontFamily(_settingsFontFamily, _settings.DefaultFontFamily);
            UiKit.AddRow(table, 4, "默认字体", _settingsFontFamily);

            _settingsFontSize = UiKit.FontSizeCombo();
            UiKit.SelectFontSize(_settingsFontSize, _settings.DefaultFontSizeMm);
            UiKit.AddRow(table, 5, "默认字号", _settingsFontSize);

            _settingsBold = UiKit.Check("默认字体加粗", _settings.DefaultBold);
            table.Controls.Add(_settingsBold, 0, 6);
            table.SetColumnSpan(_settingsBold, 2);

            _settingsMarginTop = UiKit.Numeric(0, 20, (decimal)_settings.MarginTopMm, 1, 0.1m);
            UiKit.AddRow(table, 7, "上边距 mm", _settingsMarginTop);

            _settingsMarginBottom = UiKit.Numeric(0, 20, (decimal)_settings.MarginBottomMm, 1, 0.1m);
            UiKit.AddRow(table, 8, "下边距 mm", _settingsMarginBottom);

            _settingsMarginLeft = UiKit.Numeric(0, 20, (decimal)_settings.MarginLeftMm, 1, 0.1m);
            UiKit.AddRow(table, 9, "左边距 mm", _settingsMarginLeft);

            _settingsMarginRight = UiKit.Numeric(0, 20, (decimal)_settings.MarginRightMm, 1, 0.1m);
            UiKit.AddRow(table, 10, "右边距 mm", _settingsMarginRight);

            _settingsBarcodeScale = UiKit.Numeric(70, 130, 100, 0, 5);
            UiKit.AddRow(table, 11, "条码高度 %", _settingsBarcodeScale);

            _settingsQrScale = UiKit.Numeric(70, 130, 100, 0, 5);
            UiKit.AddRow(table, 12, "二维码 %", _settingsQrScale);

            var save = UiKit.PrimaryButton("保存设置");
            // 之前用 Dock=Left 放在 FlowLayoutPanel 里，Dock 被忽略且高度被算成 0，按钮显示不出来。
            save.Size = new Size(124, 36);
            save.Margin = new Padding(0, 3, 0, 0);
            save.Click += (s, e) =>
            {
                ReadSettingsFromUi();
                _settings.Save();
                SyncDesignControlsFromSettings();
                UpdatePrinterStatus();
                UpdateSinglePreview();
                UpdateBatchPreview();
                SetStatus("设置已保存到 " + AppSettings.SettingsPath, false, false);
            };
            var saveRow = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 42, FlowDirection = FlowDirection.LeftToRight, BackColor = Theme.Card };
            saveRow.Controls.Add(save);
            table.Controls.Add(saveRow, 0, 13);
            table.SetColumnSpan(saveRow, 2);

            card.Content.Controls.Add(table);
        }

        private void BuildMaintenanceCard(FlowLayoutPanel flow)
        {
            var card = AddCard(flow, "维护与关于", 210);
            var table = UiKit.FormTable(4);

            var openData = UiKit.SecondaryButton("打开数据目录");
            openData.Width = 128;
            openData.Click += (s, e) => OpenFolder(AppSettings.DataDirectory);
            var openLogs = UiKit.SecondaryButton("打开日志目录");
            openLogs.Width = 128;
            openLogs.Click += (s, e) => OpenFolder(Logger.LogDirectory);
            var openSamples = UiKit.SecondaryButton("打开示例目录");
            openSamples.Width = 128;
            openSamples.Click += (s, e) =>
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "samples");
                OpenFolder(Directory.Exists(dir) ? dir : AppDomain.CurrentDomain.BaseDirectory);
            };
            var about = UiKit.SecondaryButton("关于");
            about.Width = 128;
            about.Click += (s, e) => ShowPage(4);

            var row = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 80, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, BackColor = Theme.Card };
            row.Controls.Add(openData);
            row.Controls.Add(openLogs);
            row.Controls.Add(openSamples);
            row.Controls.Add(about);
            table.Controls.Add(row, 0, 0);
            table.SetColumnSpan(row, 2);

            var hint = Theme.Label("设置保存在 " + AppSettings.SettingsPath + "\r\n" +
                                   "日志按天写入 logs 目录；打印内容不会写入日志。", 8.3f, Theme.Muted);
            hint.AutoSize = false;
            hint.Dock = DockStyle.Fill;
            hint.MinimumSize = new Size(0, 48);
            table.Controls.Add(hint, 0, 1);
            table.SetColumnSpan(hint, 2);

            card.Content.Controls.Add(table);
        }

        private void LoadSettingsToUi()
        {
            if (_printerCombo == null) return;
            _loadingUi = true;
            try
            {
                RefreshPrinterList();
                _networkCheck.Checked = _settings.UseNetwork;
                _networkHost.Text = _settings.NetworkHost;
                _networkPort.Value = Math.Max(1, Math.Min(65535, _settings.NetworkPort));

                _dpiCombo.SelectedIndex = IndexFromDpi(_settings.Dpi);
                _darkness.Value = Math.Max(-30, Math.Min(30, _settings.Darkness));
                _speed.Value = Math.Max(2, Math.Min(14, _settings.Speed));
                _mediaCombo.SelectedIndex = _settings.MediaType == MediaType.Continuous ? 1 : 0;
                FillLabelSizeCombo(_defaultLabelCombo, _settings.GetLabelSpec());
                _defaultBarcodeCombo.SelectedIndex = (int)_settings.DefaultBarcodeType;
                _defaultQrCombo.SelectedIndex = (int)_settings.DefaultQrEcc;
                _settingsShowHri.Checked = _settings.ShowBarcodeText;
                _alignCombo.SelectedIndex = (int)_settings.TextAlign;
                _settingsMarginTop.Value = (decimal)Math.Max(0, Math.Min(20, _settings.MarginTopMm));
                _settingsMarginBottom.Value = (decimal)Math.Max(0, Math.Min(20, _settings.MarginBottomMm));
                _settingsMarginLeft.Value = (decimal)Math.Max(0, Math.Min(20, _settings.MarginLeftMm));
                _settingsMarginRight.Value = (decimal)Math.Max(0, Math.Min(20, _settings.MarginRightMm));
                UiKit.SelectFontFamily(_settingsFontFamily, _settings.DefaultFontFamily);
                UiKit.SelectFontSize(_settingsFontSize, _settings.DefaultFontSizeMm);
                _settingsBold.Checked = _settings.DefaultBold;
                _settingsBarcodeScale.Value = Math.Max(70, Math.Min(130, _settings.BarcodeHeightPercent));
                _settingsQrScale.Value = Math.Max(70, Math.Min(130, _settings.QrSizePercent));
                UpdateNetworkState();
                UpdatePrinterHint();
                _settingsUiLoaded = true;
            }
            finally
            {
                _loadingUi = false;
            }
        }

        private void ReadSettingsFromUi()
        {
            if (_printerCombo == null || _loadingUi) return;
            if (!_settingsUiLoaded) return;
            _settings.UseNetwork = _networkCheck.Checked;
            _settings.NetworkHost = _networkHost.Text.Trim();
            _settings.NetworkPort = (int)_networkPort.Value;
            if (!_settings.UseNetwork && _printerCombo.SelectedItem != null)
            {
                string name = Convert.ToString(_printerCombo.SelectedItem);
                _settings.PrinterName = string.Equals(name, "（未检测到打印机）") ? "" : name;
            }
            else if (_settings.UseNetwork)
            {
                // 保留队列名，取消网络直连时可直接恢复。
            }
            _settings.Dpi = DpiFromIndex(_dpiCombo.SelectedIndex);
            _settings.Darkness = (int)_darkness.Value;
            _settings.Speed = (int)_speed.Value;
            _settings.MediaType = _mediaCombo.SelectedIndex == 1 ? MediaType.Continuous : MediaType.Gap;
            LabelSpec defaultSpec = _defaultLabelCombo.SelectedItem as LabelSpec;
            if (defaultSpec != null) _settings.DefaultLabel = defaultSpec.Name;
            _settings.DefaultBarcodeType = (BarcodeType)Math.Max(0, _defaultBarcodeCombo.SelectedIndex);
            _settings.DefaultQrEcc = (QrEcc)Math.Max(0, _defaultQrCombo.SelectedIndex);
            _settings.ShowBarcodeText = _settingsShowHri.Checked;
            _settings.TextAlign = (TextAlign)Math.Max(0, _alignCombo.SelectedIndex);
            _settings.MarginTopMm = (double)_settingsMarginTop.Value;
            _settings.MarginBottomMm = (double)_settingsMarginBottom.Value;
            _settings.MarginLeftMm = (double)_settingsMarginLeft.Value;
            _settings.MarginRightMm = (double)_settingsMarginRight.Value;
            _settings.DefaultFontFamily = UiKit.GetFontFamily(_settingsFontFamily);
            _settings.DefaultFontSizeMm = UiKit.GetFontSizeMm(_settingsFontSize);
            _settings.DefaultBold = _settingsBold.Checked;
            _settings.BarcodeHeightPercent = (int)_settingsBarcodeScale.Value;
            _settings.QrSizePercent = (int)_settingsQrScale.Value;
            _settings.Normalize();
        }

        private void UpdateNetworkState()
        {
            bool network = _networkCheck != null && _networkCheck.Checked;
            if (_printerCombo != null)
            {
                bool hasItems = _printerCombo.Items.Count > 0 &&
                                !string.Equals(Convert.ToString(_printerCombo.Items[0]), "（未检测到打印机）");
                _printerCombo.Enabled = !network && hasItems;
            }
            if (_networkHost != null)
            {
                _networkHost.Enabled = network;
                _networkHost.BackColor = network ? Color.White : Color.FromArgb(246, 247, 249);
            }
            if (_networkPort != null)
            {
                _networkPort.Enabled = network;
                _networkPort.BackColor = network ? Color.White : Color.FromArgb(246, 247, 249);
            }
        }

        private static int DpiFromIndex(int index)
        {
            return index == 1 ? 300 : (index == 2 ? 600 : 203);
        }

        private static int IndexFromDpi(int dpi)
        {
            return dpi == 300 ? 1 : (dpi == 600 ? 2 : 0);
        }

        private void TryGuessDpi()
        {
            if (_printerCombo == null || _dpiCombo == null) return;
            string name = Convert.ToString(_printerCombo.SelectedItem);
            string dpi = PrinterService.GuessDpi(name);
            if (string.IsNullOrEmpty(dpi)) return;
            int value = dpi == "300" ? 300 : (dpi == "600" ? 600 : 203);
            bool changed = false;
            if (_settings.Dpi != value)
            {
                _settings.Dpi = value;
                changed = true;
                Logger.Info("从队列名称识别 DPI=" + value + "：" + name);
            }
            _dpiCombo.SelectedIndex = IndexFromDpi(value);
            UpdatePrinterStatus();
            // 上面已把 _settings.Dpi 改好，下拉框的事件会因“值未变”直接返回，这里补一次重绘。
            if (changed)
            {
                UpdateSinglePreview();
                UpdateBatchPreview();
            }
        }

        private async Task TestPrintAsync()
        {
            ReadSettingsFromUi();
            if (!HasPrinterConfigured())
            {
                MessageBox.Show(this, "请先选择打印机队列或启用网络直连。", "打印测试页",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                LabelSpec spec = _settings.GetLabelSpec();
                RenderOptions render = _settings.CreateRenderOptions();
                render.Dpi = _settings.Dpi;
                render.LayoutMode = LayoutMode.TextQr;
                using (RenderedLabel rendered = LabelRenderer.RenderTestPage(spec, render,
                    _settings.UseNetwork ? _settings.NetworkHost : _settings.PrinterName))
                {
                    string zpl = ZplBuilder.BuildLabel(spec, rendered, _settings.CreatePrintOptions(), "EasyLabel test page");
                    _testPrint.Enabled = false;
                    SetStatus("正在发送测试页…", false, false);
                    await SendZplAsync(zpl, "EasyLabel test page", 1);
                    MessageBox.Show(this,
                        "测试页已发送。\r\n\r\n" +
                        "请用尺子测量测试页外框宽度：\r\n" +
                        "· " + spec.Name + " 应为 " + spec.WidthMm.ToString("0.#") + " mm 左右。\r\n\r\n" +
                        "如果只有约 " + (spec.WidthMm * 2 / 3).ToString("0.#") + " mm，说明 DPI 选成了 203，但机器可能是 300，请改为 300；\r\n" +
                        "如果只有约 " + (spec.WidthMm / 3).ToString("0.#") + " mm，请改为 600。",
                        "测试页 / DPI 自检", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("打印测试页失败", ex);
                MessageBox.Show(this, "测试页打印失败：\r\n" + ex.Message, "打印测试页",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("测试页失败：" + ex.Message, true);
            }
            finally
            {
                _testPrint.Enabled = true;
            }
        }

        private void OpenFolder(string path)
        {
            try
            {
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法打开目录：\r\n" + ex.Message, "打开目录",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ApplyUiSettingsIfLoaded()
        {
            if (!_settingsUiLoaded) return;
            ReadSettingsFromUi();
            UpdatePrinterStatus();
        }

        private void SyncDesignControlsFromSettings()
        {
            _batchLoading = true;
            try
            {
                if (_singleFontSize != null) UiKit.SelectFontSize(_singleFontSize, _settings.DefaultFontSizeMm);
                if (_batchFontSize != null) UiKit.SelectFontSize(_batchFontSize, _settings.DefaultFontSizeMm);
                if (_singleFontFamily != null) UiKit.SelectFontFamily(_singleFontFamily, _settings.DefaultFontFamily);
                if (_batchFontFamily != null) UiKit.SelectFontFamily(_batchFontFamily, _settings.DefaultFontFamily);
                if (_singleBold != null) _singleBold.Checked = _settings.DefaultBold;
                if (_batchBold != null) _batchBold.Checked = _settings.DefaultBold;

                if (_singleMarginTop != null) _singleMarginTop.Value = (decimal)Math.Max(0, Math.Min(20, _settings.MarginTopMm));
                if (_singleMarginBottom != null) _singleMarginBottom.Value = (decimal)Math.Max(0, Math.Min(20, _settings.MarginBottomMm));
                if (_singleMarginLeft != null) _singleMarginLeft.Value = (decimal)Math.Max(0, Math.Min(20, _settings.MarginLeftMm));
                if (_singleMarginRight != null) _singleMarginRight.Value = (decimal)Math.Max(0, Math.Min(20, _settings.MarginRightMm));
                if (_batchMarginTop != null) _batchMarginTop.Value = (decimal)Math.Max(0, Math.Min(20, _settings.MarginTopMm));
                if (_batchMarginBottom != null) _batchMarginBottom.Value = (decimal)Math.Max(0, Math.Min(20, _settings.MarginBottomMm));
                if (_batchMarginLeft != null) _batchMarginLeft.Value = (decimal)Math.Max(0, Math.Min(20, _settings.MarginLeftMm));
                if (_batchMarginRight != null) _batchMarginRight.Value = (decimal)Math.Max(0, Math.Min(20, _settings.MarginRightMm));
                if (_singleBarcodeScale != null) _singleBarcodeScale.Value = Math.Max(70, Math.Min(130, _settings.BarcodeHeightPercent));
                if (_batchBarcodeScale != null) _batchBarcodeScale.Value = Math.Max(70, Math.Min(130, _settings.BarcodeHeightPercent));
                if (_singleQrScale != null) _singleQrScale.Value = Math.Max(70, Math.Min(130, _settings.QrSizePercent));
                if (_batchQrScale != null) _batchQrScale.Value = Math.Max(70, Math.Min(130, _settings.QrSizePercent));
                if (_singleQrEcc != null) _singleQrEcc.SelectedIndex = (int)_settings.DefaultQrEcc;
                if (_batchQrEcc != null) _batchQrEcc.SelectedIndex = (int)_settings.DefaultQrEcc;
                if (_singleBarcodeType != null) _singleBarcodeType.SelectedIndex = (int)_settings.DefaultBarcodeType;
                if (_batchBarcodeType != null) _batchBarcodeType.SelectedIndex = (int)_settings.DefaultBarcodeType;
                if (_singleShowHri != null) _singleShowHri.Checked = _settings.ShowBarcodeText;
                if (_batchShowHri != null) _batchShowHri.Checked = _settings.ShowBarcodeText;
            }
            finally
            {
                _batchLoading = false;
            }
            ValidateAllBasic();
            UpdateBatchSummary();
        }
    }
}
