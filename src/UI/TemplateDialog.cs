using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using EasyLabel.Core;

namespace EasyLabel.UI
{
    /// <summary>模版库浏览：左侧列表、右侧实时预览，可打开/重命名/删除/导入/导出。</summary>
    public sealed class TemplateDialog : Form
    {
        private readonly TemplateStore _store;
        private readonly int _dpi;
        private readonly ListView _list;
        private readonly LabelPreviewControl _preview;
        private readonly TextBox _zplView;
        private readonly Label _info;
        private readonly Label _hint;
        private readonly Button _open;
        private readonly Button _rename;
        private readonly Button _delete;
        private readonly Button _export;
        private readonly string _preselectPath;

        public LabelTemplate SelectedTemplate { get; private set; }

        public TemplateDialog(TemplateStore store, int dpi, string preselectPath = null)
        {
            _store = store ?? TemplateStore.Default;
            _dpi = dpi <= 0 ? 203 : dpi;
            _preselectPath = preselectPath;

            Text = "打开模版";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(920, 620);
            MinimumSize = new Size(760, 520);
            BackColor = Theme.Background;
            Font = Theme.Ui(9f);
            AutoScaleMode = AutoScaleMode.None;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Theme.Background,
                Padding = new Padding(14, 12, 14, 10)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));

            var content = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Theme.Background
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));

            // ---- 左侧：模版列表 ----
            var listCard = new CardPanel("模版库") { AutoFitHeight = false, Dock = DockStyle.Fill };
            listCard.Margin = new Padding(0, 0, 8, 0);

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                ShowItemToolTips = true,
                BorderStyle = BorderStyle.None,
                Font = Theme.Ui(9.2f),
                BackColor = Color.White,
                ForeColor = Theme.Text
            };
            _list.Columns.Add("名称", Theme.S(180));
            _list.Columns.Add("类型", Theme.S(58));
            _list.Columns.Add("修改时间", Theme.S(90));
            _list.Resize += (s, e) => FitColumns();
            _list.SelectedIndexChanged += (s, e) => UpdatePreview();
            _list.DoubleClick += (s, e) => Accept();

            _hint = Theme.Label("", 8.3f, Theme.Muted);
            _hint.AutoSize = false;
            _hint.Dock = DockStyle.Bottom;
            _hint.Height = 26;
            _hint.TextAlign = ContentAlignment.MiddleLeft;

            var listHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            listHost.Controls.Add(_list);
            listHost.Controls.Add(_hint);
            listCard.Content.Controls.Add(listHost);

            // ---- 右侧：预览 ----
            var previewCard = new CardPanel("模版预览") { AutoFitHeight = false, Dock = DockStyle.Fill };
            previewCard.Margin = new Padding(8, 0, 0, 0);

            var previewLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Theme.Card
            };
            previewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            previewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));

            _preview = new LabelPreviewControl { Dock = DockStyle.Fill };

            // 纯 ZPL 文件没有可还原的版式，右侧直接显示指令内容（便于确认是不是要找的那张）。
            _zplView = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                WordWrap = false,
                ScrollBars = ScrollBars.Both,
                BorderStyle = BorderStyle.None,
                BackColor = Color.White,
                ForeColor = Theme.Text,
                Visible = false,
                Margin = new Padding(8, 6, 4, 0),
                Font = MonospaceFont()
            };

            _info = Theme.Label("", 8.6f, Theme.Muted);
            _info.AutoSize = false;
            _info.Dock = DockStyle.Fill;
            _info.TextAlign = ContentAlignment.TopLeft;
            _info.Padding = new Padding(2, 4, 2, 0);
            previewLayout.Controls.Add(_preview, 0, 0);
            previewLayout.Controls.Add(_zplView, 0, 0);
            previewLayout.Controls.Add(_info, 0, 1);
            previewCard.Content.Controls.Add(previewLayout);

            content.Controls.Add(listCard, 0, 0);
            content.Controls.Add(previewCard, 1, 0);

            // ---- 底部按钮 ----
            var bottom = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
            var leftButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Left,
                Width = 300,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Theme.Background,
                Padding = new Padding(0, 10, 0, 0)
            };
            var import = UiKit.SecondaryButton("导入文件…");
            import.Size = new Size(112, 36);
            import.Margin = new Padding(0, 0, 8, 0);
            import.Click += (s, e) => ImportTemplate();
            _export = UiKit.SecondaryButton("导出…");
            _export.Size = new Size(84, 36);
            _export.Click += (s, e) => ExportTemplate();
            leftButtons.Controls.Add(import);
            leftButtons.Controls.Add(_export);

            var rightButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = 460,
                FlowDirection = FlowDirection.RightToLeft,
                BackColor = Theme.Background,
                Padding = new Padding(0, 10, 0, 0)
            };
            _open = UiKit.PrimaryButton("打开模版");
            _open.Size = new Size(112, 36);
            _open.Margin = new Padding(8, 0, 0, 0);
            _open.Click += (s, e) => Accept();
            var close = UiKit.SecondaryButton("关闭");
            close.Size = new Size(84, 36);
            close.Margin = new Padding(8, 0, 0, 0);
            close.DialogResult = DialogResult.Cancel;
            _rename = UiKit.SecondaryButton("重命名…");
            _rename.Size = new Size(96, 36);
            _rename.Margin = new Padding(8, 0, 0, 0);
            _rename.Click += (s, e) => RenameTemplate();
            _delete = UiKit.SecondaryButton("删除");
            _delete.Size = new Size(76, 36);
            _delete.Margin = new Padding(8, 0, 0, 0);
            _delete.Click += (s, e) => DeleteTemplate();
            rightButtons.Controls.Add(_open);
            rightButtons.Controls.Add(close);
            rightButtons.Controls.Add(_rename);
            rightButtons.Controls.Add(_delete);

            bottom.Controls.Add(rightButtons);
            bottom.Controls.Add(leftButtons);

            root.Controls.Add(content, 0, 0);
            root.Controls.Add(bottom, 0, 1);
            Controls.Add(root);
            AcceptButton = _open;
            CancelButton = close;

            Theme.ApplyScale(this, new Size(920, 620));
            Shown += (s, e) =>
            {
                Reload(_preselectPath);
                UpdatePreview();
            };
        }

        private void Reload(string selectPath)
        {
            if (_list == null) return;
            Cursor previous = Cursor;
            try
            {
                Cursor = Cursors.WaitCursor;
                List<LabelTemplate> templates = _store.List();
                _list.BeginUpdate();
                _list.Items.Clear();
                foreach (LabelTemplate template in templates)
                {
                    string name = string.IsNullOrWhiteSpace(template.Name) ? "未命名模版" : template.Name;
                    var item = new ListViewItem(name);
                    item.SubItems.Add(template.FileTypeName);
                    item.SubItems.Add(ToLocalTime(template.ModifiedUtc).ToString("MM-dd HH:mm"));
                    item.ToolTipText = template.IsPrintableOnly
                        ? name + "\r\n纯 ZPL 指令 · " + template.SizeText + " · 只能直接打印"
                        : name + "\r\n" + template.SizeText + " · " + template.LayoutModeName;
                    item.Tag = template;
                    _list.Items.Add(item);
                }
                _list.EndUpdate();
                FitColumns();

                int zplCount = 0;
                foreach (LabelTemplate t in templates) if (t.IsZplFile) zplCount++;

                _hint.Text = templates.Count == 0
                    ? "还没有模版。可在单张/批量页点“模版 → 保存为模版…”创建。"
                    : "共 " + templates.Count + " 个模版" +
                      (zplCount > 0 ? "（其中 " + zplCount + " 个 ZPL 文件）" : "") + " · 双击可直接打开";
                _hint.ForeColor = templates.Count == 0 ? Theme.Warning : Theme.Muted;

                int index = -1;
                if (!string.IsNullOrEmpty(selectPath))
                {
                    for (int i = 0; i < _list.Items.Count; i++)
                    {
                        var t = _list.Items[i].Tag as LabelTemplate;
                        if (t != null && string.Equals(t.FilePath, selectPath, StringComparison.OrdinalIgnoreCase))
                        {
                            index = i;
                            break;
                        }
                    }
                }
                if (index < 0 && _list.Items.Count > 0) index = 0;
                if (index >= 0)
                {
                    _list.Items[index].Selected = true;
                    _list.Items[index].Focused = true;
                    _list.EnsureVisible(index);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("加载模版列表失败", ex);
                _hint.Text = "读取模版库失败：" + ex.Message;
                _hint.ForeColor = Theme.Error;
            }
            finally
            {
                Cursor = previous;
            }
            UpdatePreview();
        }

        private LabelTemplate CurrentTemplate
        {
            get
            {
                if (_list == null || _list.SelectedItems.Count == 0) return null;
                return _list.SelectedItems[0].Tag as LabelTemplate;
            }
        }

        /// <summary>名称列吃掉剩余宽度，避免长模版名把“类型/修改时间”挤出可视区。</summary>
        private void FitColumns()
        {
            if (_list == null || _list.Columns.Count < 3) return;
            int others = _list.Columns[1].Width + _list.Columns[2].Width;
            int available = _list.ClientSize.Width - others - Theme.S(6);
            int min = Theme.S(120);
            _list.Columns[0].Width = Math.Max(min, available);
        }

        private void UpdatePreview()
        {
            LabelTemplate template = CurrentTemplate;
            bool has = template != null;
            if (_open != null)
            {
                _open.Enabled = has;
                // 纯 ZPL 打开不了界面参数，按钮直接说明它只能打印。
                _open.Text = has && template.IsPrintableOnly ? "打印此 ZPL" : "打开模版";
            }
            if (_rename != null) _rename.Enabled = has;
            if (_delete != null) _delete.Enabled = has;
            if (_export != null) _export.Enabled = has;

            if (!has)
            {
                ShowZplText(null);
                _preview.SetPlaceholder("选择左侧模版查看预览");
                _info.Text = "";
                return;
            }

            if (template.IsPrintableOnly)
            {
                ZplSummary summary = ZplTemplateFile.Inspect(template.RawZpl ?? "");
                _info.Text = "纯 ZPL 指令（外部文件）· " + summary.SizeText + " · " + summary.ByteText +
                             " · " + summary.LineCount + " 行" +
                             (summary.HasGraphic ? " · 含 ^GFA 位图" : "") +
                             (summary.Copies > 1 ? " · ^PQ " + summary.Copies + " 份" : "") + "\r\n" +
                             "文件里没有 EasyLabel 版式信息，不能在界面上编辑，只能直接发送给打印机。";
                ShowZplText(template.RawZpl);
                return;
            }

            ShowZplText(null);
            _info.Text = template.GetSpec().Name + " · " + template.LayoutModeName +
                         " · " + (string.IsNullOrWhiteSpace(template.FontFamily) ? "默认字体" : template.FontFamily) +
                         (template.FontSizeMm > 0 ? " " + template.FontSizeMm.ToString("0.#") + " mm" : " 自动字号") +
                         (template.Bold ? " 加粗" : "") + "\r\n" +
                         "一维码 " + template.BarcodeTypeName + " · QR " + template.QrEcc +
                         " · 边距 " + template.MarginTopMm.ToString("0.#") + "/" + template.MarginBottomMm.ToString("0.#") +
                         "/" + template.MarginLeftMm.ToString("0.#") + "/" + template.MarginRightMm.ToString("0.#") + " mm" +
                         (template.LayoutPlan != null ? " · 自定义布局" : " · 自动布局");

            try
            {
                LabelContent content = template.CreateContent();
                RenderOptions options = template.CreateRenderOptions(_dpi);
                if (!content.HasText && !content.HasBarcode && !content.HasQr)
                {
                    _preview.SetPlaceholder("该模版没有保存示例内容\r\n打开后直接输入文字即可");
                    return;
                }
                RenderedLabel rendered = LabelRenderer.Render(template.GetSpec(), content, options);
                _preview.SetImage(rendered.Bitmap,
                    rendered.WidthDots + " × " + rendered.HeightDots + " 点 · " + _dpi + " dpi");
            }
            catch (LabelRenderException ex)
            {
                _preview.SetPlaceholder("该模版的内容无法预览\r\n" + ex.Message);
            }
            catch (Exception ex)
            {
                Logger.Error("模版预览失败", ex);
                _preview.SetPlaceholder("预览失败\r\n" + ex.Message);
            }
        }

        /// <summary>切换到 ZPL 文本视图；传 null 回到标签预览。</summary>
        private void ShowZplText(string zpl)
        {
            if (_zplView == null || _preview == null) return;
            if (zpl == null)
            {
                _zplView.Visible = false;
                _preview.Visible = true;
                return;
            }
            _zplView.Text = CompactZpl(zpl);
            _zplView.SelectionStart = 0;
            _zplView.SelectionLength = 0;
            _zplView.Visible = true;
            _preview.Visible = false;
        }

        /// <summary>ZPL 里 ^GFA 是一行几十 KB 的十六进制，整行塞进文本框会卡，这里截断显示。</summary>
        private static string CompactZpl(string zpl)
        {
            if (string.IsNullOrEmpty(zpl)) return "";
            string[] lines = zpl.Replace("\r\n", "\n").Split('\n');
            var sb = new StringBuilder();
            int limit = Math.Min(lines.Length, 400);
            for (int i = 0; i < limit; i++)
            {
                string line = lines[i];
                if (line.Length > 160) line = line.Substring(0, 160) + "  …（本行共 " + lines[i].Length + " 字符）";
                sb.AppendLine(line);
            }
            if (lines.Length > limit)
                sb.AppendLine("… 其余 " + (lines.Length - limit) + " 行未显示，可用“导出…”查看完整内容。");
            return sb.ToString();
        }

        private static Font MonospaceFont()
        {
            try { return new Font("Consolas", 9.2f, FontStyle.Regular, GraphicsUnit.Point); }
            catch { return Theme.Ui(9.2f); }
        }

        private void Accept()
        {
            LabelTemplate template = CurrentTemplate;
            if (template == null) return;
            SelectedTemplate = template;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void DeleteTemplate()
        {
            LabelTemplate template = CurrentTemplate;
            if (template == null) return;
            if (MessageBox.Show(this, "确定删除模版“" + template.Name + "”？\r\n该操作不可撤销。", "删除模版",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            try
            {
                string path = template.FilePath;
                _store.Delete(template);
                Reload(null);
                _hint.Text = "已删除模版：" + template.Name;
                _hint.ForeColor = Theme.Muted;
                if (File.Exists(path)) _hint.Text = "文件未能删除，可能正被占用：" + path;
            }
            catch (Exception ex)
            {
                Logger.Error("删除模版失败", ex);
                MessageBox.Show(this, "删除失败：\r\n" + ex.Message, "删除模版", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RenameTemplate()
        {
            LabelTemplate template = CurrentTemplate;
            if (template == null) return;
            string oldName = template.Name;
            using (var prompt = new TextPromptDialog("重命名模版", "新的模版名称", oldName, "重命名",
                delegate(string value)
                {
                    if (string.Equals(value, oldName, StringComparison.Ordinal)) return null;
                    if (_store.Exists(value, template.IsZplFile)) return "已存在同名文件，请换一个名称。";
                    return null;
                }))
            {
                if (prompt.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string path = _store.Rename(template, prompt.Value);
                    Reload(path);
                    _hint.Text = "已重命名为：" + prompt.Value;
                    _hint.ForeColor = Theme.Muted;
                }
                catch (Exception ex)
                {
                    Logger.Error("重命名模版失败", ex);
                    MessageBox.Show(this, "重命名失败：\r\n" + ex.Message, "重命名模版",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ImportTemplate()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "导入模版文件";
                dialog.Filter = "模版与 ZPL (*" + TemplateStore.Extension + ";*" + TemplateStore.ZplExtension + ")|*" +
                                TemplateStore.Extension + ";*" + TemplateStore.ZplExtension +
                                "|EasyLabel 模版 (*" + TemplateStore.Extension + ")|*" + TemplateStore.Extension +
                                "|ZPL 文件 (*" + TemplateStore.ZplExtension + ")|*" + TemplateStore.ZplExtension +
                                "|所有文件 (*.*)|*.*";
                dialog.InitialDirectory = _store.DirectoryPath;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    LabelTemplate imported = _store.Import(dialog.FileName);
                    Reload(imported.FilePath);
                    _hint.Text = "已导入模版：" + imported.Name;
                    _hint.ForeColor = Theme.Muted;
                }
                catch (Exception ex)
                {
                    Logger.Error("导入模版失败", ex);
                    MessageBox.Show(this, "导入失败：\r\n" + ex.Message, "导入模版",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ExportTemplate()
        {
            LabelTemplate template = CurrentTemplate;
            if (template == null) return;
            using (var dialog = new SaveFileDialog())
            {
                bool zpl = template.IsZplFile;
                string ext = zpl ? TemplateStore.ZplExtension : TemplateStore.Extension;
                dialog.Title = zpl ? "导出 ZPL 文件" : "导出模版文件";
                dialog.Filter = zpl
                    ? "ZPL 文件 (*" + ext + ")|*" + ext + "|所有文件 (*.*)|*.*"
                    : "EasyLabel 模版 (*" + ext + ")|*" + ext;
                dialog.FileName = TemplateStore.SanitizeFileName(template.Name) + ext;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    _store.Export(template, dialog.FileName);
                    _hint.Text = "已导出：" + dialog.FileName;
                    _hint.ForeColor = Theme.Muted;
                }
                catch (Exception ex)
                {
                    Logger.Error("导出模版失败", ex);
                    MessageBox.Show(this, "导出失败：\r\n" + ex.Message, "导出模版",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private static DateTime ToLocalTime(DateTime utc)
        {
            if (utc.Kind == DateTimeKind.Utc) return utc.ToLocalTime();
            return utc;
        }
    }
}
