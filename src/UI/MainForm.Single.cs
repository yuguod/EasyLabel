using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using EasyLabel.Core;

namespace EasyLabel.UI
{
    public sealed partial class MainForm
    {
        private LabelPreviewControl _singlePreview;
        private TextBox _singleText;
        private TextBox _singleBarcode;
        private TextBox _singleQr;
        private CheckBox _singleBarcodeEnabled;
        private CheckBox _singleBarcodeFromText;
        private CheckBox _singleShowHri;
        private CheckBox _singleQrEnabled;
        private CheckBox _singleQrFromText;
        private ComboBox _singleLabelSize;
        private ComboBox _singleLayout;
        private ComboBox _singleBarcodeType;
        private ComboBox _singleQrEcc;
        private ComboBox _singleFontSize;
        private ComboBox _singleFontFamily;
        private CheckBox _singleBold;
        private NumericUpDown _singleCopies;
        private NumericUpDown _singleMarginTop;
        private NumericUpDown _singleMarginBottom;
        private NumericUpDown _singleMarginLeft;
        private NumericUpDown _singleMarginRight;
        private NumericUpDown _singleBarcodeScale;
        private NumericUpDown _singleQrScale;
        private Label _singleInfo;
        private Label _singleWarnings;
        private Button _singlePrint;
        private Button _singleSave;
        private Button _singleLayoutEdit;
        private LayoutPlan _singleLayoutPlan;

        private Panel BuildSinglePage()
        {
            var page = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };

            var topBar = new Panel { BackColor = Color.White };
            topBar.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Border))
                {
                    e.Graphics.DrawLine(pen, 0, topBar.Height - 1, topBar.Width, topBar.Height - 1);
                }
            };
            var topFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.White,
                Padding = new Padding(14, 8, 10, 6)
            };
            _singleLabelSize = UiKit.Combo();
            FillLabelSizeCombo(_singleLabelSize, _settings.GetLabelSpec());
            _singleLabelSize.SelectedIndexChanged += (s, e) =>
            {
                _singleLayoutPlan = null;
                OnLabelSizeComboChanged(_singleLabelSize, SchedulePreview);
            };
            _singleLabelSize.Width = 132;

            _singleLayout = UiKit.Combo();
            _singleLayout.Items.AddRange(new object[] { "自动", "纯文本", "仅一维码", "仅二维码", "文本+一维码", "文本+二维码", "一维码+二维码", "全部" });
            _singleLayout.SelectedIndex = 0;
            _singleLayout.SelectedIndexChanged += (s, e) =>
            {
                _singleLayoutPlan = null;
                SchedulePreview();
            };
            _singleLayout.Width = 150;

            _singleQrEcc = UiKit.Combo();
            _singleQrEcc.Items.AddRange(new object[] { "L（约 7%）", "M（约 15%）", "Q（约 25%）", "H（约 30%）" });
            _singleQrEcc.SelectedIndex = (int)_settings.DefaultQrEcc;
            _singleQrEcc.SelectedIndexChanged += (s, e) => SchedulePreview();
            _singleQrEcc.Width = 148;

            _singleCopies = UiKit.Numeric(1, 9999, 1, 0, 1);
            _singleCopies.Width = 66;

            _singleFontSize = UiKit.FontSizeCombo();
            UiKit.SelectFontSize(_singleFontSize, _settings.DefaultFontSizeMm);
            _singleFontSize.SelectedIndexChanged += (s, e) => SchedulePreview();

            var sizeLabel = Theme.Label("标签规格", 8.8f, Theme.Muted);
            sizeLabel.Margin = new Padding(0, 7, 6, 0);
            var layoutLabel = Theme.Label("版式", 8.8f, Theme.Muted);
            layoutLabel.Margin = new Padding(18, 7, 6, 0);
            var eccLabel = Theme.Label("QR 纠错", 8.8f, Theme.Muted);
            eccLabel.Margin = new Padding(18, 7, 6, 0);
            var fontLabel = Theme.Label("文字大小", 8.8f, Theme.Muted);
            fontLabel.Margin = new Padding(18, 7, 6, 0);
            var copiesLabel = Theme.Label("份数", 8.8f, Theme.Muted);
            copiesLabel.Margin = new Padding(18, 7, 6, 0);

            topFlow.Controls.Add(sizeLabel);
            topFlow.Controls.Add(_singleLabelSize);
            topFlow.Controls.Add(layoutLabel);
            topFlow.Controls.Add(_singleLayout);
            topFlow.Controls.Add(eccLabel);
            topFlow.Controls.Add(_singleQrEcc);
            topFlow.Controls.Add(fontLabel);
            topFlow.Controls.Add(_singleFontSize);
            topFlow.Controls.Add(copiesLabel);
            topFlow.Controls.Add(_singleCopies);
            topBar.Controls.Add(topFlow);
            page.Controls.Add(topBar);

            var flow = UiKit.Flow();
            flow.Dock = DockStyle.None;
            flow.Padding = new Padding(0);
            flow.SizeChanged += (s, e) => UiKit.FitCardWidths(flow);
            BuildSingleCards(flow);

            var previewCard = new CardPanel("打印预览");
            previewCard.Dock = DockStyle.None;
            var previewLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Theme.Card
            };
            previewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            previewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 166));

            _singlePreview = new LabelPreviewControl { Dock = DockStyle.Fill };
            var bottom = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card };
            _singleInfo = Theme.Label("", 8.6f, Theme.Muted);
            _singleInfo.AutoSize = false;
            _singleInfo.Dock = DockStyle.Top;
            _singleInfo.Height = 36;
            _singleWarnings = Theme.Label("", 8.4f, Theme.Warning);
            _singleWarnings.AutoSize = false;
            _singleWarnings.Dock = DockStyle.Top;
            _singleWarnings.Height = 44;
            var actionRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                FlowDirection = FlowDirection.RightToLeft,
                BackColor = Theme.Card,
                Padding = new Padding(0, 4, 0, 0)
            };
            _singlePrint = UiKit.PrimaryButton("打印标签");
            _singlePrint.Size = new Size(122, 36);
            _singlePrint.Click += async (s, e) => await PrintSingleAsync();
            _singleSave = UiKit.SecondaryButton("保存 ZPL");
            _singleSave.Size = new Size(100, 36);
            _singleSave.Click += (s, e) => SaveSingleZpl();
            _singleLayoutEdit = UiKit.SecondaryButton("布局编辑…");
            _singleLayoutEdit.Size = new Size(100, 36);
            _singleLayoutEdit.Click += (s, e) => EditSingleLayout();
            actionRow.Controls.Add(_singlePrint);
            actionRow.Controls.Add(_singleSave);
            actionRow.Controls.Add(_singleLayoutEdit);
            bottom.Controls.Add(actionRow);
            bottom.Controls.Add(_singleWarnings);
            bottom.Controls.Add(_singleInfo);
            previewLayout.Controls.Add(_singlePreview, 0, 0);
            previewLayout.Controls.Add(bottom, 0, 1);
            previewCard.Content.Controls.Add(previewLayout);

            page.Controls.Add(flow);
            page.Controls.Add(previewCard);

            Action layout = () =>
            {
                int w = page.ClientSize.Width;
                int h = page.ClientSize.Height;
                if (w < 100 || h < 100) return;
                int left = (int)(w * 0.46);
                int minRight = Math.Min(Theme.S(430), Math.Max(Theme.S(300), w - Theme.S(320)));
                left = Math.Max(Theme.S(300), Math.Min(left, w - minRight - Theme.S(12)));
                topBar.SetBounds(0, 0, w, Theme.S(50));
                flow.SetBounds(0, Theme.S(58), left - Theme.S(12), h - Theme.S(58));
                previewCard.SetBounds(left, Theme.S(58), w - left, h - Theme.S(58));
                UiKit.FitCardWidths(flow);
            };
            page.Resize += (s, e) => layout();
            page.VisibleChanged += (s, e) => { if (page.Visible) layout(); };
            _pageLayouts.Add(layout);
            layout();

            UpdateSingleControlStates();
            return page;
        }

        private void BuildSingleCards(FlowLayoutPanel flow)
        {
            var textCard = AddCard(flow, "标签文字", 150);
            var textHint = Theme.Label("支持多行；放不下时会自动缩小。", 8.4f, Theme.Muted);
            textHint.AutoSize = false;
            textHint.Height = 22;
            textHint.Dock = DockStyle.Top;
            _singleText = UiKit.TextBox(true, 90);
            _singleText.Dock = DockStyle.Top;
            _singleText.AcceptsReturn = true;
            _singleText.ScrollBars = ScrollBars.Vertical;
            _singleText.TextChanged += (s, e) => SchedulePreview();
            textCard.Content.Controls.Add(_singleText);
            textCard.Content.Controls.Add(textHint);

            var barcodeCard = AddCard(flow, "一维码", 204);
            var bt = UiKit.FormTable(5);
            _singleBarcodeEnabled = UiKit.Check("打印一维码", false);
            _singleBarcodeEnabled.CheckedChanged += (s, e) =>
            {
                _singleLayoutPlan = null;
                UpdateSingleControlStates();
                SchedulePreview();
            };
            bt.Controls.Add(_singleBarcodeEnabled, 0, 0);
            bt.SetColumnSpan(_singleBarcodeEnabled, 2);

            _singleBarcodeType = UiKit.Combo();
            _singleBarcodeType.Items.AddRange(new object[] { "Code 128", "Code 39", "EAN-13" });
            _singleBarcodeType.SelectedIndex = 0;
            _singleBarcodeType.SelectedIndexChanged += (s, e) => SchedulePreview();
            UiKit.AddRow(bt, 1, "类型", _singleBarcodeType, false, true);

            _singleBarcode = UiKit.TextBox(false, 28);
            _singleBarcode.TextChanged += (s, e) => SchedulePreview();
            UiKit.AddRow(bt, 2, "内容", _singleBarcode, false, true);

            var barcodeChecks = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                Height = 30,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Theme.Card
            };
            _singleBarcodeFromText = UiKit.Check("空值用文字", true);
            _singleBarcodeFromText.CheckedChanged += (s, e) => SchedulePreview();
            _singleShowHri = UiKit.Check("显示明文", true);
            _singleShowHri.CheckedChanged += (s, e) => SchedulePreview();
            barcodeChecks.Controls.Add(_singleBarcodeFromText);
            barcodeChecks.Controls.Add(_singleShowHri);
            bt.Controls.Add(barcodeChecks, 0, 3);
            bt.SetColumnSpan(barcodeChecks, 2);

            var hriHint = Theme.Label("Code 128 支持英文/数字；中文请用二维码。", 8.2f, Theme.Muted);
            hriHint.AutoSize = false;
            hriHint.Dock = DockStyle.Fill;
            hriHint.MinimumSize = new Size(0, 22);
            bt.Controls.Add(hriHint, 0, 4);
            bt.SetColumnSpan(hriHint, 2);
            barcodeCard.Content.Controls.Add(bt);

            var qrCard = AddCard(flow, "二维码", 176);
            var qt = UiKit.FormTable(4);
            _singleQrEnabled = UiKit.Check("打印二维码", false);
            _singleQrEnabled.CheckedChanged += (s, e) =>
            {
                _singleLayoutPlan = null;
                UpdateSingleControlStates();
                SchedulePreview();
            };
            qt.Controls.Add(_singleQrEnabled, 0, 0);
            qt.SetColumnSpan(_singleQrEnabled, 2);

            _singleQr = UiKit.TextBox(false, 28);
            _singleQr.TextChanged += (s, e) => SchedulePreview();
            UiKit.AddRow(qt, 1, "内容", _singleQr, false, true);

            _singleQrFromText = UiKit.Check("内容为空时使用标签文字", true);
            _singleQrFromText.CheckedChanged += (s, e) => SchedulePreview();
            qt.Controls.Add(_singleQrFromText, 0, 2);
            qt.SetColumnSpan(_singleQrFromText, 2);

            var qrHint = Theme.Label("QR 支持中文/网址；内容越长，模块越小。", 8.2f, Theme.Muted);
            qrHint.AutoSize = false;
            qrHint.Dock = DockStyle.Fill;
            qrHint.MinimumSize = new Size(0, 22);
            qt.Controls.Add(qrHint, 0, 3);
            qt.SetColumnSpan(qrHint, 2);
            qrCard.Content.Controls.Add(qt);

            var layoutCard = AddCard(flow, "高级版式", 380);
            var lt = UiKit.FormTable(9);

            _singleFontFamily = UiKit.FontFamilyCombo();
            UiKit.SelectFontFamily(_singleFontFamily, _settings.DefaultFontFamily);
            _singleFontFamily.SelectedIndexChanged += (s, e) => SchedulePreview();
            UiKit.AddRow(lt, 0, "字体", _singleFontFamily);
            _singleFontFamily.Anchor = AnchorStyles.Left;

            _singleBold = UiKit.Check("字体加粗", _settings.DefaultBold);
            _singleBold.CheckedChanged += (s, e) => SchedulePreview();
            UiKit.AddRow(lt, 1, "文字样式", _singleBold);
            _singleBold.Anchor = AnchorStyles.Left;

            _singleMarginTop = UiKit.Numeric(0, 20, (decimal)_settings.MarginTopMm, 1, 0.1m);
            _singleMarginTop.ValueChanged += (s, e) => SchedulePreview();
            UiKit.AddRow(lt, 2, "上边距 mm", _singleMarginTop);
            _singleMarginTop.Anchor = AnchorStyles.Left;

            _singleMarginBottom = UiKit.Numeric(0, 20, (decimal)_settings.MarginBottomMm, 1, 0.1m);
            _singleMarginBottom.ValueChanged += (s, e) => SchedulePreview();
            UiKit.AddRow(lt, 3, "下边距 mm", _singleMarginBottom);
            _singleMarginBottom.Anchor = AnchorStyles.Left;

            _singleMarginLeft = UiKit.Numeric(0, 20, (decimal)_settings.MarginLeftMm, 1, 0.1m);
            _singleMarginLeft.ValueChanged += (s, e) => SchedulePreview();
            UiKit.AddRow(lt, 4, "左边距 mm", _singleMarginLeft);
            _singleMarginLeft.Anchor = AnchorStyles.Left;

            _singleMarginRight = UiKit.Numeric(0, 20, (decimal)_settings.MarginRightMm, 1, 0.1m);
            _singleMarginRight.ValueChanged += (s, e) => SchedulePreview();
            UiKit.AddRow(lt, 5, "右边距 mm", _singleMarginRight);
            _singleMarginRight.Anchor = AnchorStyles.Left;

            _singleBarcodeScale = UiKit.Numeric(70, 130, _settings.BarcodeHeightPercent, 0, 5);
            _singleBarcodeScale.ValueChanged += (s, e) => SchedulePreview();
            UiKit.AddRow(lt, 6, "条码高度 %", _singleBarcodeScale);
            _singleBarcodeScale.Anchor = AnchorStyles.Left;

            _singleQrScale = UiKit.Numeric(70, 130, _settings.QrSizePercent, 0, 5);
            _singleQrScale.ValueChanged += (s, e) => SchedulePreview();
            UiKit.AddRow(lt, 7, "二维码 %", _singleQrScale);
            _singleQrScale.Anchor = AnchorStyles.Left;

            var note = Theme.Label("四边距独立；文字大小以 mm 指定，空间不足会自动缩小并提示。", 8.2f, Theme.Muted);
            note.AutoSize = false;
            note.Dock = DockStyle.Fill;
            note.MinimumSize = new Size(0, 28);
            lt.Controls.Add(note, 0, 8);
            lt.SetColumnSpan(note, 2);

            layoutCard.Content.Controls.Add(lt);
        }

        private CardPanel AddCard(FlowLayoutPanel flow, string title, int height)
        {
            var card = new CardPanel(title)
            {
                Height = height,
                Width = Math.Max(200, flow.ClientSize.Width - flow.Padding.Horizontal - 4),
                Margin = new Padding(0, 0, 0, 12)
            };
            flow.Controls.Add(card);
            return card;
        }
        private void UpdateSingleControlStates()
        {
            bool barcode = _singleBarcodeEnabled != null && _singleBarcodeEnabled.Checked;
            bool qr = _singleQrEnabled != null && _singleQrEnabled.Checked;
            if (_singleBarcodeType != null) _singleBarcodeType.Enabled = barcode;
            if (_singleBarcode != null) _singleBarcode.Enabled = barcode;
            if (_singleBarcodeFromText != null) _singleBarcodeFromText.Enabled = barcode;
            if (_singleShowHri != null) _singleShowHri.Enabled = barcode;
            if (_singleQr != null) _singleQr.Enabled = qr;
            if (_singleQrFromText != null) _singleQrFromText.Enabled = qr;
        }

        private LabelSpec GetSingleSpec()
        {
            LabelSpec selected = _singleLabelSize.SelectedItem as LabelSpec;
            return selected ?? _settings.GetLabelSpec();
        }

        private LabelContent GetSingleContent()
        {
            return new LabelContent
            {
                Text = _singleText.Text,
                ShowBarcode = _singleBarcodeEnabled.Checked,
                BarcodeType = (BarcodeType)Math.Max(0, _singleBarcodeType.SelectedIndex),
                BarcodeValue = _singleBarcode.Text,
                UseTextForBarcode = _singleBarcodeFromText.Checked,
                ShowQr = _singleQrEnabled.Checked,
                QrValue = _singleQr.Text,
                UseTextForQr = _singleQrFromText.Checked
            };
        }

        private void EditSingleLayout()
        {
            ApplyUiSettingsIfLoaded();
            LabelContent content = GetSingleContent();
            if (!content.HasText && !content.HasBarcode && !content.HasQr)
            {
                MessageBox.Show(this, "请先填写文字、一维码或二维码内容。", "布局编辑",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dialog = new LayoutEditorDialog(GetSingleSpec(), content, GetSingleOptions()))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _singleLayoutPlan = dialog.LayoutResult;
                    SchedulePreview();
                    SetStatus("已应用自定义布局；打印和保存 ZPL 都会使用该位置/尺寸。", false, false);
                }
            }
        }

        private RenderOptions GetSingleOptions()
        {
            RenderOptions o = _settings.CreateRenderOptions();
            o.Dpi = _settings.Dpi;
            o.LayoutMode = (LayoutMode)Math.Max(0, _singleLayout.SelectedIndex);
            o.BarcodeType = (BarcodeType)Math.Max(0, _singleBarcodeType.SelectedIndex);
            o.QrEcc = (QrEcc)Math.Max(0, _singleQrEcc.SelectedIndex);
            o.ShowBarcodeText = _singleShowHri.Checked;
            o.MarginTopMm = (double)_singleMarginTop.Value;
            o.MarginBottomMm = (double)_singleMarginBottom.Value;
            o.MarginLeftMm = (double)_singleMarginLeft.Value;
            o.MarginRightMm = (double)_singleMarginRight.Value;
            o.FontSizeMm = UiKit.GetFontSizeMm(_singleFontSize);
            o.FontFamily = UiKit.GetFontFamily(_singleFontFamily);
            o.Bold = _singleBold.Checked;
            o.LayoutPlan = _singleLayoutPlan == null ? null : _singleLayoutPlan.Clone();
            o.BarcodeHeightPercent = (int)_singleBarcodeScale.Value;
            o.QrSizePercent = (int)_singleQrScale.Value;
            return o;
        }

        private void UpdateSinglePreview()
        {
            if (_singlePreview == null || _singleLabelSize == null) return;
            LabelContent content = GetSingleContent();
            if (!content.HasText && !content.HasBarcode && !content.HasQr)
            {
                _singlePreview.SetPlaceholder("输入文字、一维码或二维码后显示预览");
                _singleInfo.Text = "";
                _singleWarnings.Text = "";
                SetStatus("就绪", false, false);
                return;
            }
            try
            {
                LabelSpec spec = GetSingleSpec();
                RenderOptions options = GetSingleOptions();
                RenderedLabel rendered = LabelRenderer.Render(spec, content, options);
                _singlePreview.SetImage(rendered.Bitmap,
                    rendered.WidthDots + " × " + rendered.HeightDots + " 点 · " + options.Dpi + " dpi");
                _singleInfo.Text = spec.Name + " · " + rendered.WidthDots + "×" + rendered.HeightDots +
                                   " 点 · " + options.Dpi + " dpi · " + rendered.Info;
                if (rendered.Warnings.Count > 0)
                {
                    _singleWarnings.Text = string.Join("\r\n", rendered.Warnings.ToArray());
                    _singleWarnings.ForeColor = Theme.Warning;
                }
                else
                {
                    _singleWarnings.Text = "";
                }
                SetStatus("预览已更新", false, false);
            }
            catch (LabelRenderException ex)
            {
                _singlePreview.SetPlaceholder("暂无法预览\r\n" + ex.Message);
                _singleInfo.Text = "";
                _singleWarnings.Text = ex.Message;
                _singleWarnings.ForeColor = Theme.Error;
                SetStatus("标签内容有误：" + ex.Message, true);
            }
            catch (Exception ex)
            {
                Logger.Error("单张预览失败", ex);
                _singlePreview.SetPlaceholder("预览失败\r\n" + ex.Message);
                _singleInfo.Text = "";
                _singleWarnings.Text = ex.Message;
                _singleWarnings.ForeColor = Theme.Error;
                SetStatus("预览失败：" + ex.Message, true);
            }
        }

        private async Task PrintSingleAsync()
        {
            ApplyUiSettingsIfLoaded();
            if (!HasPrinterConfigured())
            {
                var result = MessageBox.Show(this, "尚未配置打印机。\r\n是否先保存 ZPL 文件？", "打印",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (result == DialogResult.Yes) SaveSingleZpl();
                else ShowPage(2);
                return;
            }

            try
            {
                LabelSpec spec = GetSingleSpec();
                RenderOptions options = GetSingleOptions();
                int copies = (int)_singleCopies.Value;
                string zpl;
                using (RenderedLabel rendered = LabelRenderer.Render(spec, GetSingleContent(), options))
                {
                    zpl = ZplBuilder.BuildLabel(spec, rendered, _settings.CreatePrintOptions(),
                        "EasyLabel " + spec.Name, copies);
                }
                _singlePrint.Enabled = false;
                SetStatus("正在发送 " + copies + " 张标签…", false, false);
                await SendZplAsync(zpl, "EasyLabel " + spec.Name, copies);
            }
            catch (LabelRenderException ex)
            {
                MessageBox.Show(this, ex.Message, "无法打印", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus("打印取消：" + ex.Message, true);
            }
            catch (Exception ex)
            {
                Logger.Error("单张打印失败", ex);
                MessageBox.Show(this, "打印失败：\r\n" + ex.Message, "打印", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("打印失败：" + ex.Message, true);
            }
            finally
            {
                _singlePrint.Enabled = true;
            }
        }

        private void SaveSingleZpl()
        {
            ApplyUiSettingsIfLoaded();
            try
            {
                LabelSpec spec = GetSingleSpec();
                string zpl;
                using (RenderedLabel rendered = LabelRenderer.Render(spec, GetSingleContent(), GetSingleOptions()))
                {
                    zpl = ZplBuilder.BuildLabel(spec, rendered, _settings.CreatePrintOptions(), "EasyLabel " + spec.Name);
                }
                using (var dialog = new SaveFileDialog())
                {
                    dialog.Title = "保存 ZPL 文件";
                    dialog.Filter = "ZPL 文件 (*.zpl)|*.zpl|所有文件 (*.*)|*.*";
                    dialog.FileName = "label-" + spec.WidthMm.ToString("0.#") + "x" + spec.HeightMm.ToString("0.#") + "-" +
                                      DateTime.Now.ToString("yyyyMMdd-HHmm") + ".zpl";
                    dialog.InitialDirectory = Directory.Exists(_settings.LastFolder)
                        ? _settings.LastFolder
                        : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    File.WriteAllText(dialog.FileName, zpl, Encoding.ASCII);
                    _settings.LastFolder = Path.GetDirectoryName(dialog.FileName);
                    _settings.Save();
                    SetStatus("已保存 ZPL：" + dialog.FileName, false, false);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("保存 ZPL 失败", ex);
                MessageBox.Show(this, "保存失败：\r\n" + ex.Message, "保存 ZPL", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task SendZplAsync(string zpl, string jobName, int labels)
        {
            byte[] data = ZplBuilder.ToAscii(zpl);
            await Task.Run(() => PrinterService.Send(data, jobName, _settings));
            string target = _settings.UseNetwork
                ? _settings.NetworkHost + ":" + _settings.NetworkPort
                : _settings.PrinterName;
            SetStatus("已发送 " + labels + " 张到 " + target, false, false);
            Logger.Info("发送打印作业 " + jobName + "，标签数 " + labels + "，字节 " + data.Length);
        }

        private bool HasPrinterConfigured()
        {
            if (_settings.UseNetwork) return !string.IsNullOrWhiteSpace(_settings.NetworkHost);
            return !string.IsNullOrWhiteSpace(_settings.PrinterName);
        }

        private void LoadDemo()
        {
            if (_startPage == 1)
            {
                _batchRows = new List<BatchRow>
                {
                    new BatchRow { RowNumber = 2, Text = "产品A", Barcode = "A-2026-001", Qr = "https://example.com/A-001", Selected = true },
                    new BatchRow { RowNumber = 3, Text = "产品B", Barcode = "B-2026-002", Qr = "https://example.com/B-002", Selected = true },
                    new BatchRow { RowNumber = 4, Text = "产品C", Barcode = "C-2026-003", Qr = "https://example.com/C-003", Selected = true }
                };
                BindBatchRows();
                _batchBarcodeEnabled.Checked = true;
                _batchQrEnabled.Checked = true;
                _batchFallback.Checked = true;
                UpdateBatchControlStates();
                ValidateAllBasic();
                UpdateBatchSummary();
                if (_batchGrid.Rows.Count > 0)
                {
                    _batchGrid.ClearSelection();
                    _batchGrid.Rows[0].Selected = true;
                    _batchGrid.CurrentCell = _batchGrid.Rows[0].Cells[2];
                }
                UpdateBatchPreview();
                ResetBatchScroll();
                return;
            }

            _singleLabelSize.SelectedIndex = 0;
            _singleLayout.SelectedIndex = 0;
            _singleText.Text = "产品A";
            _singleBarcodeEnabled.Checked = true;
            _singleBarcode.Text = "A-2026-001";
            _singleQrEnabled.Checked = true;
            _singleQr.Text = "https://example.com/A-001";
            _singleCopies.Value = 1;
            UpdateSingleControlStates();
            UpdateSinglePreview();
        }
    }
}
