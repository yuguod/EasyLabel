using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using EasyLabel.Core;

namespace EasyLabel.UI
{
    public sealed partial class MainForm
    {
        private Button _templateButton;
        private ContextMenuStrip _templateMenu;

        /// <summary>顶部“模版”下拉按钮。放在页头，四个页面都能用。</summary>
        private void BuildTemplateButton(Control header)
        {
            _templateButton = UiKit.SecondaryButton("模版 ▾");
            _templateButton.Size = new Size(96, 32);
            _templateMenu = CreateTemplateMenu();
            _templateButton.ContextMenuStrip = _templateMenu;
            _templateButton.Click += (s, e) => ShowTemplateMenu();
            header.Controls.Add(_templateButton);
        }

        private ContextMenuStrip CreateTemplateMenu()
        {
            var menu = new ContextMenuStrip
            {
                Font = Theme.Ui(9.2f),
                ShowImageMargin = false
            };
            // 每次展开时重建，保证刚保存/删除的模版立刻出现在“最近模版”里。
            menu.Opening += (s, e) => RebuildTemplateMenu();
            return menu;
        }

        private void ShowTemplateMenu()
        {
            if (_templateButton == null) return;
            if (_templateMenu == null) _templateMenu = CreateTemplateMenu();
            RebuildTemplateMenu();
            _templateMenu.Show(_templateButton, new Point(0, _templateButton.Height));
        }

        private void RebuildTemplateMenu()
        {
            if (_templateMenu == null) return;
            _templateMenu.Items.Clear();

            bool onWorkPage = _currentPage == 0 || _currentPage == 1;
            var save = new ToolStripMenuItem(onWorkPage ? "将当前页面保存为模版…" : "将单张页保存为模版…");
            save.Click += (s, e) => SaveCurrentAsTemplate();
            _templateMenu.Items.Add(save);

            var saveZpl = new ToolStripMenuItem(onWorkPage ? "将当前页面保存为 ZPL 模版…" : "将单张页保存为 ZPL 模版…");
            saveZpl.ToolTipText = "生成可直接发给打印机的 .zpl 文件，同时保留模版信息，之后还能打开继续编辑";
            saveZpl.Click += (s, e) => SaveCurrentAsZplTemplate();
            _templateMenu.Items.Add(saveZpl);

            var open = new ToolStripMenuItem("打开模版…");
            open.Click += (s, e) => OpenTemplateDialog();
            _templateMenu.Items.Add(open);

            _templateMenu.Items.Add(new ToolStripSeparator());

            List<LabelTemplate> templates;
            try
            {
                templates = TemplateStore.Default.List();
            }
            catch (Exception ex)
            {
                Logger.Error("列出模版失败", ex);
                templates = new List<LabelTemplate>();
            }

            if (templates.Count == 0)
            {
                var empty = new ToolStripMenuItem("（还没有模版，先保存一个）") { Enabled = false };
                _templateMenu.Items.Add(empty);
            }
            else
            {
                var header = new ToolStripMenuItem("最近模版") { Enabled = false };
                _templateMenu.Items.Add(header);
                int shown = Math.Min(8, templates.Count);
                for (int i = 0; i < shown; i++)
                {
                    LabelTemplate template = templates[i];
                    string label = template.Name + "    " + template.SizeText +
                                   (template.IsZplFile ? "    [ZPL]" : "");
                    var item = new ToolStripMenuItem(label);
                    LabelTemplate captured = template;
                    item.Click += (s, e) =>
                    {
                        // 外部 ZPL 没法还原界面参数，直接送打印机（先让用户在模版库里确认一下）。
                        if (captured.IsPrintableOnly) OpenTemplateDialog(captured.FilePath);
                        else ApplyTemplate(captured);
                    };
                    _templateMenu.Items.Add(item);
                }
                if (templates.Count > shown)
                {
                    var more = new ToolStripMenuItem("更多（共 " + templates.Count + " 个）…");
                    more.Click += (s, e) => OpenTemplateDialog();
                    _templateMenu.Items.Add(more);
                }
            }

            _templateMenu.Items.Add(new ToolStripSeparator());
            var folder = new ToolStripMenuItem("打开模版文件夹");
            folder.Click += (s, e) => OpenFolder(TemplateStore.Default.DirectoryPath);
            _templateMenu.Items.Add(folder);
        }

        private void SaveCurrentAsTemplate()
        {
            try
            {
                LabelTemplate template = CaptureCurrentTemplate();
                using (var prompt = new TextPromptDialog("保存为模版", "模版名称", template.Name, "保存"))
                {
                    if (prompt.ShowDialog(this) != DialogResult.OK) return;
                    template.Name = prompt.Value;
                }

                TemplateStore store = TemplateStore.Default;
                if (store.Exists(template.Name))
                {
                    if (MessageBox.Show(this,
                        "已存在同名模版“" + template.Name + "”，要覆盖它吗？", "保存为模版",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                }
                string path = store.Save(template);
                SetStatus("模版已保存：" + template.Name + "  →  " + path, false, false);
            }
            catch (Exception ex)
            {
                Logger.Error("保存模版失败", ex);
                MessageBox.Show(this, "保存模版失败：\r\n" + ex.Message, "保存为模版",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 保存为 .zpl 模版：文件本身就是一份可以直接发给打印机的 ZPL，
        /// 同时在注释里带上模版信息，之后还能被 EasyLabel 打开继续改。
        /// </summary>
        private void SaveCurrentAsZplTemplate()
        {
            try
            {
                LabelTemplate template = CaptureCurrentTemplate();
                using (var prompt = new TextPromptDialog("保存为 ZPL 模版", "模版名称", template.Name, "保存"))
                {
                    if (prompt.ShowDialog(this) != DialogResult.OK) return;
                    template.Name = prompt.Value;
                }

                TemplateStore store = TemplateStore.Default;
                if (store.Exists(template.Name, true))
                {
                    if (MessageBox.Show(this,
                        "已存在同名 ZPL 文件“" + template.Name + "”，要覆盖它吗？", "保存为 ZPL 模版",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                }

                string zpl = BuildZplForTemplate(template);
                string path = store.SaveZpl(template, zpl);
                SetStatus("ZPL 模版已保存：" + template.Name + "  →  " + path, false, false);
            }
            catch (Exception ex)
            {
                Logger.Error("保存 ZPL 模版失败", ex);
                MessageBox.Show(this, "保存 ZPL 模版失败：\r\n" + ex.Message, "保存为 ZPL 模版",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>按当前 DPI 把模版渲染成 ZPL 指令。</summary>
        private string BuildZplForTemplate(LabelTemplate template)
        {
            LabelSpec spec = template.GetSpec();
            LabelContent content = template.CreateContent();
            if (!content.HasText && !content.HasBarcode && !content.HasQr)
                throw new InvalidOperationException("当前页面没有可打印的内容（文字、一维码、二维码都是空的）。");

            RenderOptions options = template.CreateRenderOptions(_settings.Dpi);
            using (RenderedLabel rendered = LabelRenderer.Render(spec, content, options))
            {
                string comment = "EasyLabel " +
                                 spec.WidthMm.ToString("0.#", CultureInfo.InvariantCulture) + "x" +
                                 spec.HeightMm.ToString("0.#", CultureInfo.InvariantCulture);
                return ZplBuilder.BuildLabel(spec, rendered, _settings.CreatePrintOptions(), comment, template.Copies);
            }
        }

        /// <summary>把当前页面的规格、版式、自定义布局和内容打包成模版。</summary>
        private LabelTemplate CaptureCurrentTemplate()
        {
            if (_currentPage == 1)
            {
                BatchOptions batch = GetBatchOptions();
                LabelSpec spec = GetBatchSpec();
                RenderOptions options = batch.CreateRenderOptions();
                options.Dpi = _settings.Dpi;
                BatchRow row = _batchRows.FirstOrDefault(r => r.Selected) ?? _batchRows.FirstOrDefault();
                LabelContent content = row != null ? row.ToContent(batch) : new LabelContent();
                LabelTemplate template = LabelTemplate.Capture(SuggestTemplateName(spec, content), spec, options,
                    content, (int)_batchCopies.Value);
                // 批量页的“是否打印码”由勾选框决定，行里可能没有对应数据，这里以勾选框为准。
                template.ShowBarcode = batch.BarcodeEnabled;
                template.ShowQr = batch.QrEnabled;
                template.BarcodeFromText = batch.UseTextFallback;
                template.QrFromText = batch.UseTextFallback;
                return template;
            }

            LabelSpec singleSpec = GetSingleSpec();
            LabelContent singleContent = GetSingleContent();
            return LabelTemplate.Capture(SuggestTemplateName(singleSpec, singleContent), singleSpec,
                GetSingleOptions(), singleContent, (int)_singleCopies.Value);
        }

        private void OpenTemplateDialog(string preselectPath = null)
        {
            using (var dialog = new TemplateDialog(TemplateStore.Default, _settings.Dpi, preselectPath))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                LabelTemplate selected = dialog.SelectedTemplate;
                if (selected == null) return;
                if (selected.IsPrintableOnly) PrintRawZpl(selected);
                else ApplyTemplate(selected);
            }
        }

        /// <summary>打开模版：批量页只套版式，单张页连同示例内容一起恢复。</summary>
        private void ApplyTemplate(LabelTemplate template)
        {
            if (template == null) return;
            if (template.IsPrintableOnly)
            {
                // 外部 ZPL 没有版式可套用，只能整份发给打印机。
                PrintRawZpl(template);
                return;
            }
            try
            {
                int target = _currentPage == 1 ? 1 : 0;
                if (target != _currentPage) ShowPage(target);

                // 文字对齐没有单独的界面开关，统一由设置里的默认值决定，这里同步过去。
                _settings.TextAlign = template.TextAlign;
                if (_alignCombo != null) _alignCombo.SelectedIndex = ClampIndex(_alignCombo, (int)template.TextAlign);

                if (target == 0) ApplyTemplateToSingle(template);
                else ApplyTemplateToBatch(template);
                SetStatus("已打开模版：" + template.Name + "（" + template.GetSpec().Name + "）", false, false);
            }
            catch (Exception ex)
            {
                Logger.Error("应用模版失败：" + template.Name, ex);
                MessageBox.Show(this, "打开模版失败：\r\n" + ex.Message, "打开模版",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ApplyTemplateToSingle(LabelTemplate template)
        {
            LabelSpec spec = template.GetSpec();

            // 规格下拉框会清空自定义布局，所以布局必须最后再赋值。
            FillLabelSizeCombo(_singleLabelSize, spec);
            _singleLayout.SelectedIndex = ClampIndex(_singleLayout, (int)template.LayoutMode);
            _singleBarcodeType.SelectedIndex = ClampIndex(_singleBarcodeType, (int)template.BarcodeType);
            _singleQrEcc.SelectedIndex = ClampIndex(_singleQrEcc, (int)template.QrEcc);

            UiKit.SelectFontSize(_singleFontSize, template.FontSizeMm);
            UiKit.SelectFontFamily(_singleFontFamily, template.FontFamily);
            _singleBold.Checked = template.Bold;
            _singleMarginTop.Value = ClampValue(_singleMarginTop, (decimal)template.MarginTopMm);
            _singleMarginBottom.Value = ClampValue(_singleMarginBottom, (decimal)template.MarginBottomMm);
            _singleMarginLeft.Value = ClampValue(_singleMarginLeft, (decimal)template.MarginLeftMm);
            _singleMarginRight.Value = ClampValue(_singleMarginRight, (decimal)template.MarginRightMm);
            _singleBarcodeScale.Value = ClampValue(_singleBarcodeScale, template.BarcodeHeightPercent);
            _singleQrScale.Value = ClampValue(_singleQrScale, template.QrSizePercent);

            _singleShowHri.Checked = template.ShowBarcodeText;
            _singleBarcodeEnabled.Checked = template.ShowBarcode;
            _singleQrEnabled.Checked = template.ShowQr;
            _singleBarcodeFromText.Checked = template.BarcodeFromText;
            _singleQrFromText.Checked = template.QrFromText;
            _singleText.Text = template.Text ?? "";
            _singleBarcode.Text = template.BarcodeValue ?? "";
            _singleQr.Text = template.QrValue ?? "";
            _singleCopies.Value = ClampValue(_singleCopies, template.Copies);

            _singleLayoutPlan = template.LayoutPlan == null ? null : template.LayoutPlan.Clone();
            if (_singleLayoutPlan != null) _singleLayoutPlan.Normalize(spec);

            UpdateSingleControlStates();
            UpdateSinglePreview();
        }

        private void ApplyTemplateToBatch(LabelTemplate template)
        {
            LabelSpec spec = template.GetSpec();
            _batchLoading = true;
            try
            {
                FillLabelSizeCombo(_batchLabelSize, spec);
                _batchLayout.SelectedIndex = ClampIndex(_batchLayout, (int)template.LayoutMode);
                _batchBarcodeType.SelectedIndex = ClampIndex(_batchBarcodeType, (int)template.BarcodeType);
                _batchQrEcc.SelectedIndex = ClampIndex(_batchQrEcc, (int)template.QrEcc);

                UiKit.SelectFontSize(_batchFontSize, template.FontSizeMm);
                UiKit.SelectFontFamily(_batchFontFamily, template.FontFamily);
                _batchBold.Checked = template.Bold;
                _batchMarginTop.Value = ClampValue(_batchMarginTop, (decimal)template.MarginTopMm);
                _batchMarginBottom.Value = ClampValue(_batchMarginBottom, (decimal)template.MarginBottomMm);
                _batchMarginLeft.Value = ClampValue(_batchMarginLeft, (decimal)template.MarginLeftMm);
                _batchMarginRight.Value = ClampValue(_batchMarginRight, (decimal)template.MarginRightMm);
                _batchBarcodeScale.Value = ClampValue(_batchBarcodeScale, template.BarcodeHeightPercent);
                _batchQrScale.Value = ClampValue(_batchQrScale, template.QrSizePercent);

                _batchShowHri.Checked = template.ShowBarcodeText;
                _batchBarcodeEnabled.Checked = template.ShowBarcode;
                _batchQrEnabled.Checked = template.ShowQr;
                _batchFallback.Checked = template.BarcodeFromText || template.QrFromText;
                _batchCopies.Value = ClampValue(_batchCopies, template.Copies);

                _batchLayoutPlan = template.LayoutPlan == null ? null : template.LayoutPlan.Clone();
                if (_batchLayoutPlan != null) _batchLayoutPlan.Normalize(spec);
            }
            finally
            {
                _batchLoading = false;
            }

            UpdateBatchControlStates();
            ValidateAllBasic();
            UpdateBatchSummary();
            UpdateBatchPreview();
        }

        /// <summary>
        /// 把纯 ZPL 文件整份发给打印机。这类文件里的内容已经栅格化成 ^GFA，
        /// 界面上无法改动，所以只提供“发送”和“另存”。
        /// </summary>
        private async void PrintRawZpl(LabelTemplate template)
        {
            if (template == null || string.IsNullOrWhiteSpace(template.RawZpl)) return;

            if (!HasPrinterConfigured())
            {
                DialogResult result = MessageBox.Show(this,
                    "尚未配置打印机。\r\n是否把这个 .zpl 文件另存到别处？", "ZPL 文件",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (result == DialogResult.Yes) SaveRawZplAs(template);
                return;
            }

            try
            {
                string target = _settings.UseNetwork
                    ? _settings.NetworkHost + ":" + _settings.NetworkPort
                    : _settings.PrinterName;
                int labels = template.ZplCopies > 0 ? template.ZplCopies : 1;
                // 这一步会真的出纸，先确认一下，避免双击误触。
                DialogResult confirm = MessageBox.Show(this,
                    "把“" + template.Name + "”的 ZPL 内容发送到：\r\n" + target +
                    "\r\n\r\n标签数：" + labels + " 张。继续吗？", "打印 ZPL 文件",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (confirm != DialogResult.OK) return;

                await SendZplAsync(template.RawZpl, "EasyLabel " + template.Name, labels);
            }
            catch (Exception ex)
            {
                Logger.Error("发送 ZPL 文件失败：" + template.Name, ex);
                MessageBox.Show(this, "发送失败：\r\n" + ex.Message, "ZPL 文件",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SaveRawZplAs(LabelTemplate template)
        {
            try
            {
                using (var dialog = new SaveFileDialog())
                {
                    dialog.Title = "保存 ZPL 文件";
                    dialog.Filter = "ZPL 文件 (*" + TemplateStore.ZplExtension + ")|*" + TemplateStore.ZplExtension +
                                    "|所有文件 (*.*)|*.*";
                    dialog.FileName = TemplateStore.SanitizeFileName(template.Name) + TemplateStore.ZplExtension;
                    dialog.InitialDirectory = Directory.Exists(_settings.LastFolder)
                        ? _settings.LastFolder
                        : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    ZplTemplateFile.WriteText(dialog.FileName, template.RawZpl);
                    _settings.LastFolder = Path.GetDirectoryName(dialog.FileName);
                    _settings.Save();
                    SetStatus("已保存 ZPL：" + dialog.FileName, false, false);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("另存 ZPL 失败", ex);
                MessageBox.Show(this, "保存失败：\r\n" + ex.Message, "ZPL 文件",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static int ClampIndex(ComboBox combo, int index)
        {
            if (combo == null || combo.Items.Count == 0) return 0;
            return Math.Max(0, Math.Min(combo.Items.Count - 1, index));
        }

        private static decimal ClampValue(NumericUpDown numeric, decimal value)
        {
            if (numeric == null) return value;
            return Math.Max(numeric.Minimum, Math.Min(numeric.Maximum, value));
        }

        private static string SuggestTemplateName(LabelSpec spec, LabelContent content)
        {
            string first = "";
            if (content != null)
            {
                string text = content.EffectiveText ?? "";
                int br = text.IndexOf('\n');
                if (br >= 0) text = text.Substring(0, br);
                first = text.Trim();
                if (first.Length > 18) first = first.Substring(0, 18).Trim();
            }
            if (first.Length == 0) return (spec == null ? "标签" : spec.Name) + " 模版";
            return first + " · " + spec.WidthMm.ToString("0.#") + "×" + spec.HeightMm.ToString("0.#");
        }
    }
}
