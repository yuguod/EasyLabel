using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using EasyLabel.Core;

namespace EasyLabel.UI
{
    public sealed partial class MainForm
    {
        private DataGridView _batchGrid;
        private BindingList<BatchRow> _batchBinding;
        private List<BatchRow> _batchRows = new List<BatchRow>();
        private LabelPreviewControl _batchPreview;
        private Label _batchInfo;
        private Label _batchWarnings;
        private Label _batchSummary;
        private ComboBox _batchLabelSize;
        private ComboBox _batchLayout;
        private ComboBox _batchBarcodeType;
        private ComboBox _batchQrEcc;
        private CheckBox _batchBarcodeEnabled;
        private CheckBox _batchQrEnabled;
        private CheckBox _batchFallback;
        private CheckBox _batchShowHri;
        private CheckBox _batchOnlySelected;
        private NumericUpDown _batchCopies;
        private NumericUpDown _batchMarginTop;
        private NumericUpDown _batchMarginBottom;
        private NumericUpDown _batchMarginLeft;
        private NumericUpDown _batchMarginRight;
        private ComboBox _batchFontSize;
        private ComboBox _batchFontFamily;
        private CheckBox _batchBold;
        private NumericUpDown _batchBarcodeScale;
        private NumericUpDown _batchQrScale;
        private Button _batchPrint;
        private Button _batchSaveZpl;
        private Button _batchImport;
        private Button _batchLayoutEdit;
        private LayoutPlan _batchLayoutPlan;
        private bool _batchLoading;
        private bool _batchPrinting;
        private FlowLayoutPanel _batchRightFlow;

        private Panel BuildBatchPage()
        {
            var page = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };

            var toolbar = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(14, 10, 14, 8)
            };
            toolbar.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Border))
                    e.Graphics.DrawLine(pen, 0, toolbar.Height - 1, toolbar.Width, toolbar.Height - 1);
            };

            var left = new FlowLayoutPanel
            {
                Dock = DockStyle.Left,
                Width = 600,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.White
            };
            _batchImport = UiKit.PrimaryButton("导入 Excel/CSV");
            _batchImport.Size = new Size(154, 36);
            _batchImport.Click += (s, e) => ImportBatch();
            var template = UiKit.SecondaryButton("导入模板");
            template.Size = new Size(84, 36);
            template.Click += (s, e) => SaveTemplate();
            _batchLayoutEdit = UiKit.SecondaryButton("布局编辑…");
            _batchLayoutEdit.Size = new Size(100, 36);
            _batchLayoutEdit.Click += (s, e) => EditBatchLayout();
            var clear = UiKit.SecondaryButton("清空");
            clear.Size = new Size(58, 36);
            clear.Click += (s, e) => ClearBatch();
            var all = UiKit.LinkButton("全选");
            all.Size = new Size(52, 36);
            all.Click += (s, e) => SelectAll(true);
            var none = UiKit.LinkButton("全不选");
            none.Size = new Size(62, 36);
            none.Click += (s, e) => SelectAll(false);
            left.Controls.Add(_batchImport);
            left.Controls.Add(template);
            left.Controls.Add(_batchLayoutEdit);
            left.Controls.Add(clear);
            left.Controls.Add(all);
            left.Controls.Add(none);

            var right = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = 410,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.White
            };
            var copiesLabel = Theme.Label("份数", 8.8f, Theme.Muted);
            copiesLabel.Margin = new Padding(0, 10, 4, 0);
            _batchCopies = UiKit.Numeric(1, 999, 1);
            _batchCopies.Width = 62;
            _batchCopies.Margin = new Padding(0, 3, 8, 0);
            _batchOnlySelected = UiKit.Check("仅勾选行", true);
            _batchOnlySelected.Margin = new Padding(0, 10, 8, 0);
            _batchSaveZpl = UiKit.SecondaryButton("保存 ZPL");
            _batchSaveZpl.Size = new Size(92, 36);
            _batchSaveZpl.Click += (s, e) => SaveBatchZpl();
            _batchPrint = UiKit.PrimaryButton("打印");
            _batchPrint.Size = new Size(78, 36);
            _batchPrint.Click += async (s, e) => await PrintBatchAsync();
            right.Controls.Add(copiesLabel);
            right.Controls.Add(_batchCopies);
            right.Controls.Add(_batchOnlySelected);
            right.Controls.Add(_batchSaveZpl);
            right.Controls.Add(_batchPrint);

            toolbar.Controls.Add(right);
            toolbar.Controls.Add(left);

            var gridHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card };
            var gridCard = new CardPanel("标签数据") { AutoFitHeight = false };
            _batchGrid = new DataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = false };
            Theme.StyleGrid(_batchGrid);
            _batchGrid.CellValueChanged += (s, e) => OnBatchCellChanged();
            _batchGrid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_batchGrid.IsCurrentCellDirty) _batchGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _batchGrid.SelectionChanged += (s, e) => UpdateBatchPreview();
            _batchGrid.CellFormatting += (s, e) => FormatBatchCell(e);
            _batchGrid.CellToolTipTextNeeded += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
                string prop = _batchGrid.Columns[e.ColumnIndex].DataPropertyName;
                if (prop == "Text" || prop == "Barcode" || prop == "Qr")
                    e.ToolTipText = Convert.ToString(_batchGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value);
            };
            _batchGrid.DataError += (s, e) =>
            {
                Logger.Error("批量表格数据错误：" + e.Exception.Message);
                e.ThrowException = false;
            };

            var cSelected = new DataGridViewCheckBoxColumn
            {
                HeaderText = "选",
                DataPropertyName = "Selected",
                Width = Theme.S(42),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            var cRow = new DataGridViewTextBoxColumn
            {
                HeaderText = "行",
                DataPropertyName = "RowNumber",
                Width = Theme.S(52),
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            var cText = new DataGridViewTextBoxColumn
            {
                HeaderText = "文本",
                DataPropertyName = "Text",
                FillWeight = 110,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            var cBarcode = new DataGridViewTextBoxColumn
            {
                HeaderText = "一维码",
                DataPropertyName = "Barcode",
                FillWeight = 60,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            var cQr = new DataGridViewTextBoxColumn
            {
                HeaderText = "二维码",
                DataPropertyName = "Qr",
                FillWeight = 80,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            var cStatus = new DataGridViewTextBoxColumn
            {
                HeaderText = "状态",
                DataPropertyName = "Status",
                Width = Theme.S(72),
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            _batchGrid.Columns.AddRange(cSelected, cRow, cText, cBarcode, cQr, cStatus);

            _batchSummary = Theme.Label("尚未导入数据", 8.6f, Theme.Muted);
            _batchSummary.Dock = DockStyle.Bottom;
            _batchSummary.Height = 26;
            _batchSummary.TextAlign = ContentAlignment.MiddleLeft;
            _batchSummary.Padding = new Padding(4, 0, 0, 0);

            gridHost.Controls.Add(_batchGrid);
            gridHost.Controls.Add(_batchSummary);
            gridCard.Content.Controls.Add(gridHost);

            var rightFlow = UiKit.Flow();
            rightFlow.Padding = new Padding(0);
            rightFlow.SizeChanged += (s, e) => UiKit.FitCardWidths(rightFlow);
            BuildBatchPreviewCard(rightFlow);
            BuildBatchOptionsCard(rightFlow);
            _batchRightFlow = rightFlow;

            var content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
            gridCard.Dock = DockStyle.None;
            rightFlow.Dock = DockStyle.None;
            content.Controls.Add(gridCard);
            content.Controls.Add(rightFlow);

            Action layout = () =>
            {
                int w = content.ClientSize.Width;
                int h = content.ClientSize.Height;
                if (w < 100 || h < 100) return;
                int left = (int)(w * 0.64);
                int minRight = Math.Min(Theme.S(380), Math.Max(Theme.S(330), w - Theme.S(520)));
                left = Math.Max(Theme.S(480), Math.Min(left, w - minRight - Theme.S(14)));
                gridCard.SetBounds(Theme.S(14), Theme.S(10), left - Theme.S(21), h - Theme.S(22));
                rightFlow.SetBounds(left, Theme.S(10), w - left - Theme.S(14), h - Theme.S(22));
                UiKit.FitCardWidths(rightFlow);
                ResetBatchScroll();
            };
            content.Resize += (s, e) => layout();
            content.VisibleChanged += (s, e) => { if (content.Visible) layout(); };

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Theme.Background
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(toolbar, 0, 0);
            root.Controls.Add(content, 0, 1);
            page.Controls.Add(root);

            _batchBinding = new BindingList<BatchRow>();
            _batchGrid.DataSource = _batchBinding;
            _pageLayouts.Add(layout);
            layout();
            return page;
        }

        private void BuildBatchPreviewCard(FlowLayoutPanel flow)
        {
            var card = AddCard(flow, "选中行预览", 320);
            card.AutoFitHeight = false;
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Theme.Card
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));

            _batchPreview = new LabelPreviewControl { Dock = DockStyle.Fill };
            var infoPanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card };
            _batchInfo = Theme.Label("", 8.4f, Theme.Muted);
            _batchInfo.AutoSize = false;
            _batchInfo.Dock = DockStyle.Top;
            _batchInfo.Height = 36;
            _batchWarnings = Theme.Label("", 8.2f, Theme.Warning);
            _batchWarnings.AutoSize = false;
            _batchWarnings.Dock = DockStyle.Top;
            _batchWarnings.Height = 44;
            infoPanel.Controls.Add(_batchWarnings);
            infoPanel.Controls.Add(_batchInfo);
            layout.Controls.Add(_batchPreview, 0, 0);
            layout.Controls.Add(infoPanel, 0, 1);
            card.Content.Controls.Add(layout);
        }

        private void BuildBatchOptionsCard(FlowLayoutPanel flow)
        {
            var card = AddCard(flow, "批量版式", 626);
            var table = UiKit.FormTable(16);

            _batchLabelSize = UiKit.Combo();
            FillLabelSizeCombo(_batchLabelSize, _settings.GetLabelSpec());
            _batchLabelSize.SelectedIndexChanged += (s, e) =>
            {
                _batchLayoutPlan = null;
                OnLabelSizeComboChanged(_batchLabelSize, OnBatchOptionsChanged);
            };
            UiKit.AddRow(table, 0, "标签规格", _batchLabelSize);

            _batchLayout = UiKit.Combo();
            _batchLayout.Items.AddRange(new object[] { "自动", "纯文本", "仅一维码", "仅二维码", "文本+一维码", "文本+二维码", "一维码+二维码", "全部" });
            _batchLayout.SelectedIndex = 0;
            _batchLayout.SelectedIndexChanged += (s, e) =>
            {
                _batchLayoutPlan = null;
                OnBatchOptionsChanged();
            };
            UiKit.AddRow(table, 1, "版式", _batchLayout);

            _batchBarcodeType = UiKit.Combo();
            _batchBarcodeType.Items.AddRange(new object[] { "Code 128", "Code 39", "EAN-13" });
            _batchBarcodeType.SelectedIndex = (int)_settings.DefaultBarcodeType;
            _batchBarcodeType.SelectedIndexChanged += (s, e) => OnBatchOptionsChanged();
            UiKit.AddRow(table, 2, "一维码", _batchBarcodeType);

            _batchQrEcc = UiKit.Combo();
            _batchQrEcc.Items.AddRange(new object[] { "L（约 7%）", "M（约 15%）", "Q（约 25%）", "H（约 30%）" });
            _batchQrEcc.SelectedIndex = (int)_settings.DefaultQrEcc;
            _batchQrEcc.SelectedIndexChanged += (s, e) => OnBatchOptionsChanged();
            UiKit.AddRow(table, 3, "QR 纠错", _batchQrEcc);

            _batchFontFamily = UiKit.FontFamilyCombo();
            UiKit.SelectFontFamily(_batchFontFamily, _settings.DefaultFontFamily);
            _batchFontFamily.SelectedIndexChanged += (s, e) => OnBatchOptionsChanged();
            UiKit.AddRow(table, 4, "字体", _batchFontFamily);

            _batchFontSize = UiKit.FontSizeCombo();
            UiKit.SelectFontSize(_batchFontSize, _settings.DefaultFontSizeMm);
            _batchFontSize.SelectedIndexChanged += (s, e) => OnBatchOptionsChanged();
            UiKit.AddRow(table, 5, "文字大小", _batchFontSize);

            _batchBold = UiKit.Check("字体加粗", _settings.DefaultBold);
            _batchBold.CheckedChanged += (s, e) => OnBatchOptionsChanged();
            UiKit.AddRow(table, 6, "文字样式", _batchBold);
            _batchBold.Anchor = AnchorStyles.Left;

            _batchMarginTop = UiKit.Numeric(0, 20, (decimal)_settings.MarginTopMm, 1, 0.1m);
            _batchMarginTop.ValueChanged += (s, e) => OnBatchOptionsChanged();
            UiKit.AddRow(table, 7, "上边距 mm", _batchMarginTop);

            _batchMarginBottom = UiKit.Numeric(0, 20, (decimal)_settings.MarginBottomMm, 1, 0.1m);
            _batchMarginBottom.ValueChanged += (s, e) => OnBatchOptionsChanged();
            UiKit.AddRow(table, 8, "下边距 mm", _batchMarginBottom);

            _batchMarginLeft = UiKit.Numeric(0, 20, (decimal)_settings.MarginLeftMm, 1, 0.1m);
            _batchMarginLeft.ValueChanged += (s, e) => OnBatchOptionsChanged();
            UiKit.AddRow(table, 9, "左边距 mm", _batchMarginLeft);

            _batchMarginRight = UiKit.Numeric(0, 20, (decimal)_settings.MarginRightMm, 1, 0.1m);
            _batchMarginRight.ValueChanged += (s, e) => OnBatchOptionsChanged();
            UiKit.AddRow(table, 10, "右边距 mm", _batchMarginRight);

            _batchBarcodeScale = UiKit.Numeric(70, 130, _settings.BarcodeHeightPercent, 0, 5);
            _batchBarcodeScale.ValueChanged += (s, e) => OnBatchOptionsChanged();
            UiKit.AddRow(table, 11, "条码高度 %", _batchBarcodeScale);

            _batchQrScale = UiKit.Numeric(70, 130, _settings.QrSizePercent, 0, 5);
            _batchQrScale.ValueChanged += (s, e) => OnBatchOptionsChanged();
            UiKit.AddRow(table, 12, "二维码 %", _batchQrScale);

            var checks = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                Height = 58,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Theme.Card
            };
            _batchBarcodeEnabled = UiKit.Check("打印一维码", true);
            _batchQrEnabled = UiKit.Check("打印二维码", false);
            _batchBarcodeEnabled.CheckedChanged += (s, e) =>
            {
                _batchLayoutPlan = null;
                UpdateBatchControlStates();
                OnBatchOptionsChanged();
            };
            _batchQrEnabled.CheckedChanged += (s, e) =>
            {
                _batchLayoutPlan = null;
                UpdateBatchControlStates();
                OnBatchOptionsChanged();
            };
            checks.Controls.Add(_batchBarcodeEnabled);
            checks.Controls.Add(_batchQrEnabled);
            table.Controls.Add(checks, 0, 13);
            table.SetColumnSpan(checks, 2);

            _batchFallback = UiKit.Check("空值时用文本补齐", true);
            _batchFallback.CheckedChanged += (s, e) => OnBatchOptionsChanged();
            table.Controls.Add(_batchFallback, 0, 14);
            table.SetColumnSpan(_batchFallback, 2);

            _batchShowHri = UiKit.Check("显示一维码明文", true);
            _batchShowHri.CheckedChanged += (s, e) => OnBatchOptionsChanged();
            table.Controls.Add(_batchShowHri, 0, 15);
            table.SetColumnSpan(_batchShowHri, 2);

            card.Content.Controls.Add(table);
        }

        private void OnBatchCellChanged()
        {
            if (_batchLoading) return;
            var row = _batchGrid.CurrentRow != null ? _batchGrid.CurrentRow.DataBoundItem as BatchRow : null;
            if (row != null) row.ValidateBasic(GetBatchOptions());
            UpdateBatchSummary();
            UpdateBatchPreview();
        }

        private void FormatBatchCell(DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || _batchGrid.Columns[e.ColumnIndex].DataPropertyName != "Status") return;
            string status = Convert.ToString(e.Value);
            if (status == "有误") e.CellStyle.ForeColor = Theme.Error;
            else if (status == "已发送") e.CellStyle.ForeColor = Theme.Success;
            else e.CellStyle.ForeColor = Theme.Muted;
        }

        private void EditBatchLayout()
        {
            if (_batchRows == null || _batchRows.Count == 0)
            {
                MessageBox.Show(this, "请先导入 Excel/CSV 数据，或点击“模板”添加示例行。", "布局编辑",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            BatchOptions options = GetBatchOptions();
            BatchRow row = _batchRows.FirstOrDefault(r => r.Selected) ?? _batchRows[0];
            LabelContent content = row.ToContent(options);
            if (!content.HasText && !content.HasBarcode && !content.HasQr)
            {
                MessageBox.Show(this, "当前行没有可编辑的内容。", "布局编辑",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dialog = new LayoutEditorDialog(GetBatchSpec(), content, options.CreateRenderOptions()))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _batchLayoutPlan = dialog.LayoutResult;
                    OnBatchOptionsChanged();
                    SetStatus("已应用批量布局；所有行按该位置/尺寸打印。", false, false);
                }
            }
        }

        private BatchOptions GetBatchOptions()
        {
            return new BatchOptions
            {
                BarcodeEnabled = _batchBarcodeEnabled != null && _batchBarcodeEnabled.Checked,
                QrEnabled = _batchQrEnabled != null && _batchQrEnabled.Checked,
                UseTextFallback = _batchFallback == null || _batchFallback.Checked,
                BarcodeType = (BarcodeType)Math.Max(0, _batchBarcodeType.SelectedIndex),
                QrEcc = (QrEcc)Math.Max(0, _batchQrEcc.SelectedIndex),
                ShowBarcodeText = _batchShowHri == null || _batchShowHri.Checked,
                LayoutMode = (LayoutMode)Math.Max(0, _batchLayout.SelectedIndex),
                Dpi = _settings.Dpi,
                MarginTopMm = (double)_batchMarginTop.Value,
                MarginBottomMm = (double)_batchMarginBottom.Value,
                MarginLeftMm = (double)_batchMarginLeft.Value,
                MarginRightMm = (double)_batchMarginRight.Value,
                FontSizeMm = UiKit.GetFontSizeMm(_batchFontSize),
                FontFamily = UiKit.GetFontFamily(_batchFontFamily),
                Bold = _batchBold != null && _batchBold.Checked,
                LayoutPlan = _batchLayoutPlan == null ? null : _batchLayoutPlan.Clone(),
                BarcodeHeightPercent = (int)_batchBarcodeScale.Value,
                QrSizePercent = (int)_batchQrScale.Value,
                TextAlign = _settings.TextAlign
            };
        }

        private LabelSpec GetBatchSpec()
        {
            LabelSpec selected = _batchLabelSize.SelectedItem as LabelSpec;
            return selected ?? _settings.GetLabelSpec();
        }

        private void UpdateBatchControlStates()
        {
            bool barcode = _batchBarcodeEnabled != null && _batchBarcodeEnabled.Checked;
            bool qr = _batchQrEnabled != null && _batchQrEnabled.Checked;
            if (_batchBarcodeType != null) _batchBarcodeType.Enabled = barcode;
            if (_batchShowHri != null) _batchShowHri.Enabled = barcode;
            if (_batchQrEcc != null) _batchQrEcc.Enabled = qr;
            if (_batchFallback != null) _batchFallback.Enabled = barcode || qr;
        }

        private void OnBatchOptionsChanged()
        {
            if (_batchLoading) return;
            UpdateBatchControlStates();
            ValidateAllBasic();
            UpdateBatchSummary();
            UpdateBatchPreview();
        }

        private void ValidateAllBasic()
        {
            if (_batchRows == null) return;
            BatchOptions options = GetBatchOptions();
            _batchLoading = true;
            try
            {
                foreach (BatchRow row in _batchRows) row.ValidateBasic(options);
            }
            finally
            {
                _batchLoading = false;
            }
        }

        private void UpdateBatchSummary()
        {
            if (_batchSummary == null) return;
            if (_batchRows == null || _batchRows.Count == 0)
            {
                _batchSummary.Text = "尚未导入数据";
                _batchSummary.ForeColor = Theme.Muted;
                return;
            }
            int selected = 0, valid = 0;
            foreach (BatchRow row in _batchRows)
            {
                if (row.Selected) selected++;
                if (row.IsValid) valid++;
            }
            _batchSummary.Text = "共 " + _batchRows.Count + " 行 · 勾选 " + selected + " · 有效 " + valid;
            _batchSummary.ForeColor = valid == _batchRows.Count ? Theme.Success : Theme.Warning;
        }

        private void UpdateBatchPreview()
        {
            if (_batchPreview == null) return;
            BatchRow row = null;
            if (_batchGrid != null && _batchGrid.CurrentRow != null)
                row = _batchGrid.CurrentRow.DataBoundItem as BatchRow;
            if (row == null && _batchRows.Count > 0) row = _batchRows[0];
            if (row == null)
            {
                _batchPreview.SetPlaceholder("导入数据后，选择一行查看预览");
                _batchInfo.Text = "";
                _batchWarnings.Text = "";
                return;
            }
            if (string.IsNullOrWhiteSpace(row.Text) && string.IsNullOrWhiteSpace(row.Barcode) && string.IsNullOrWhiteSpace(row.Qr))
            {
                _batchPreview.SetPlaceholder("该行没有可打印内容");
                _batchInfo.Text = "第 " + row.RowNumber + " 行";
                _batchWarnings.Text = "";
                return;
            }

            try
            {
                BatchOptions options = GetBatchOptions();
                LabelSpec spec = GetBatchSpec();
                LabelContent content = row.ToContent(options);
                RenderedLabel rendered = LabelRenderer.Render(spec, content, options.CreateRenderOptions());
                _batchPreview.SetImage(rendered.Bitmap,
                    rendered.WidthDots + " × " + rendered.HeightDots + " 点 · " + _settings.Dpi + " dpi");
                _batchInfo.Text = "第 " + row.RowNumber + " 行 · " + spec.Name + " · " + rendered.Info;
                _batchWarnings.Text = rendered.Warnings.Count > 0 ? string.Join("\r\n", rendered.Warnings.ToArray()) : "";
                _batchWarnings.ForeColor = Theme.Warning;
            }
            catch (LabelRenderException ex)
            {
                _batchPreview.SetPlaceholder("该行无法生成标签\r\n" + ex.Message);
                _batchInfo.Text = "第 " + row.RowNumber + " 行";
                _batchWarnings.Text = ex.Message;
                _batchWarnings.ForeColor = Theme.Error;
            }
            catch (Exception ex)
            {
                Logger.Error("批量预览失败", ex);
                _batchPreview.SetPlaceholder("预览失败\r\n" + ex.Message);
                _batchWarnings.Text = ex.Message;
                _batchWarnings.ForeColor = Theme.Error;
            }
        }

        private void ImportBatch()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "选择 Excel 或 CSV 文件";
                dialog.Filter = "Excel / CSV (*.xlsx;*.xls;*.xlsm;*.csv)|*.xlsx;*.xls;*.xlsm;*.csv|Excel (*.xlsx;*.xls;*.xlsm)|*.xlsx;*.xls;*.xlsm|CSV (*.csv)|*.csv|所有文件 (*.*)|*.*";
                dialog.InitialDirectory = Directory.Exists(_settings.LastImportFolder)
                    ? _settings.LastImportFolder
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    Cursor = Cursors.WaitCursor;
                    WorkbookData workbook = ExcelImporter.LoadWorkbook(dialog.FileName);
                    Cursor = Cursors.Default;
                    using (var import = new ImportDialog(workbook, _settings))
                    {
                        DialogResult importResult = import.ShowDialog(this);
                        Logger.Info("ImportBatch: result=" + importResult + ", rows=" + import.ResultRows.Count);
                        if (importResult != DialogResult.OK) return;
                        _settings.LastImportFolder = Path.GetDirectoryName(dialog.FileName);
                        ApplyImportedRows(import.ResultRows, dialog.FileName);
                    }
                }
                catch (Exception ex)
                {
                    Cursor = Cursors.Default;
                    Logger.Error("导入 Excel/CSV 失败", ex);
                    MessageBox.Show(this, "导入失败：\r\n" + ex.Message, "导入", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void BindBatchRows()
        {
            _batchLoading = true;
            try
            {
                _batchGrid.DataSource = null;
                _batchBinding = new BindingList<BatchRow>(_batchRows);
                _batchGrid.DataSource = _batchBinding;
            }
            finally
            {
                _batchLoading = false;
            }
        }

        private void ApplyImportedRows(List<BatchRow> rows, string fileName)
        {
            _batchRows = rows ?? new List<BatchRow>();
            BindBatchRows();

            bool hasBarcode = false, hasQr = false;
            foreach (BatchRow row in _batchRows)
            {
                if (!string.IsNullOrWhiteSpace(row.Barcode)) hasBarcode = true;
                if (!string.IsNullOrWhiteSpace(row.Qr)) hasQr = true;
            }
            _batchLoading = true;
            _batchBarcodeEnabled.Checked = hasBarcode;
            _batchQrEnabled.Checked = hasQr;
            _batchFallback.Checked = true;
            _batchLoading = false;
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
            SetStatus("已导入 " + _batchRows.Count + " 行数据：" + Path.GetFileName(fileName ?? ""), false, false);
            ResetBatchScroll();
        }

        private void ResetBatchScroll()
        {
            if (_batchRightFlow == null) return;
            try
            {
                _batchRightFlow.AutoScrollPosition = new Point(0, 0);
                _batchRightFlow.VerticalScroll.Value = 0;
                _batchRightFlow.PerformLayout();
            }
            catch { }
        }

        private void SelectAll(bool selected)
        {
            if (_batchRows.Count == 0) return;
            _batchLoading = true;
            try
            {
                foreach (BatchRow row in _batchRows) row.Selected = selected;
            }
            finally
            {
                _batchLoading = false;
            }
            _batchGrid.Refresh();
            UpdateBatchSummary();
        }

        private void ClearBatch()
        {
            if (_batchRows.Count == 0) return;
            if (MessageBox.Show(this, "确定清空当前批量数据？", "清空", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes) return;
            _batchRows.Clear();
            BindBatchRows();
            UpdateBatchSummary();
            UpdateBatchPreview();
            SetStatus("批量数据已清空", false, false);
        }

        private void SaveTemplate()
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "保存导入模板";
                dialog.Filter = "Excel 模板 (*.xlsx)|*.xlsx|CSV 模板 (*.csv)|*.csv";
                dialog.FileName = "标签导入模板.xlsx";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    if (dialog.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                    {
                        string sample = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "samples", "sample-data.xlsx");
                        if (File.Exists(sample))
                        {
                            File.Copy(sample, dialog.FileName, true);
                        }
                        else
                        {
                            string csv = Path.ChangeExtension(dialog.FileName, ".csv");
                            WriteCsvTemplate(csv);
                            MessageBox.Show(this, "未找到示例 xlsx，已改为保存 CSV 模板：\r\n" + csv,
                                "模板", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    }
                    else
                    {
                        WriteCsvTemplate(dialog.FileName);
                    }
                    SetStatus("模板已保存：" + dialog.FileName, false, false);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "保存模板失败：\r\n" + ex.Message, "模板", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private static void WriteCsvTemplate(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("文本,条码,二维码");
            sb.AppendLine("产品A,A-2026-001,https://example.com/A-001");
            sb.AppendLine("产品B,B-2026-002,https://example.com/B-002");
            sb.AppendLine("产品C,C-2026-003,https://example.com/C-003");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private void SaveBatchZpl()
        {
            ApplyUiSettingsIfLoaded();
            if (_batchRows.Count == 0) return;
            var rows = GetRowsToPrint();
            if (rows.Count == 0) return;
            try
            {
                Cursor = Cursors.WaitCursor;
                LabelSpec spec = GetBatchSpec();
                BatchOptions options = GetBatchOptions();
                PrintOptions print = _settings.CreatePrintOptions();
                var sb = new StringBuilder();
                int copies = (int)_batchCopies.Value;
                foreach (BatchRow row in rows)
                {
                    using (RenderedLabel rendered = LabelRenderer.Render(spec, row.ToContent(options), options.CreateRenderOptions()))
                    {
                        sb.Append(ZplBuilder.BuildLabel(spec, rendered, print, "EasyLabel row " + row.RowNumber, copies));
                    }
                }
                using (var dialog = new SaveFileDialog())
                {
                    dialog.Title = "保存批量 ZPL";
                    dialog.Filter = "ZPL 文件 (*.zpl)|*.zpl|所有文件 (*.*)|*.*";
                    dialog.FileName = "batch-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".zpl";
                    dialog.InitialDirectory = Directory.Exists(_settings.LastFolder)
                        ? _settings.LastFolder
                        : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.ASCII);
                    _settings.LastFolder = Path.GetDirectoryName(dialog.FileName);
                    _settings.Save();
                    SetStatus("已保存批量 ZPL：" + dialog.FileName, false, false);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("保存批量 ZPL 失败", ex);
                MessageBox.Show(this, "保存批量 ZPL 失败：\r\n" + ex.Message, "保存 ZPL",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private List<BatchRow> GetRowsToPrint()
        {
            var rows = new List<BatchRow>();
            bool only = _batchOnlySelected.Checked;
            foreach (BatchRow row in _batchRows)
            {
                if (only && !row.Selected) continue;
                rows.Add(row);
            }
            return rows;
        }

        private async Task PrintBatchAsync()
        {
            if (_batchPrinting) return;
            ApplyUiSettingsIfLoaded();
            if (!HasPrinterConfigured())
            {
                var result = MessageBox.Show(this, "尚未配置打印机。\r\n是否先保存 ZPL 文件？", "打印",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (result == DialogResult.Yes) SaveBatchZpl();
                else ShowPage(2);
                return;
            }

            var rows = GetRowsToPrint();
            if (rows.Count == 0)
            {
                MessageBox.Show(this, "没有可打印的行。请导入数据或勾选行。", "打印",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            LabelSpec spec = GetBatchSpec();
            BatchOptions options = GetBatchOptions();
            int copies = Math.Max(1, (int)_batchCopies.Value);
            PrintOptions print = _settings.CreatePrintOptions();
            int total = rows.Count * copies;
            int max = Math.Max(1, Math.Min(1000000, rows.Count + total));
            var progress = new ProgressForm("批量打印", max);
            progress.Show(this);
            _batchPrinting = true;
            SetBatchBusy(true);
            try
            {
                var result = await Task.Run(() => RunBatch(rows, options, spec, print, copies, progress, progress.Token));
                try { progress.Finish(); } catch { }

                if (result.Canceled)
                {
                    int sent = result.SentLabels;
                    SetStatus("批量打印已取消：已发送 " + sent + " / " + total + " 张", false, true);
                    MessageBox.Show(this,
                        "已取消。\r\n\r\n" +
                        "已发送 " + sent + " 张（已进入打印队列，可能仍会出纸），" +
                        "剩余 " + Math.Max(0, total - sent) + " 张未发送。",
                        "批量打印", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (result.ValidationErrors.Count > 0)
                {
                    foreach (var kv in result.RowErrors)
                    {
                        kv.Key.IsValid = false;
                        kv.Key.Error = kv.Value;
                        kv.Key.Status = "有误";
                    }
                    UpdateBatchSummary();
                    var preview = new StringBuilder();
                    for (int i = 0; i < result.ValidationErrors.Count && i < 10; i++)
                        preview.AppendLine(result.ValidationErrors[i]);
                    if (result.ValidationErrors.Count > 10)
                        preview.AppendLine("… 另有 " + (result.ValidationErrors.Count - 10) + " 行错误");
                    MessageBox.Show(this, "有 " + result.ValidationErrors.Count + " 行无法打印，未发送任何标签：\r\n\r\n" +
                        preview, "批量打印", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    SetStatus("批量校验未通过，未发送", true);
                    return;
                }

                foreach (BatchRow row in rows)
                {
                    row.Status = "已发送";
                    row.Error = "";
                }
                SetStatus("已发送 " + result.SentLabels + " 张标签", false, false);
                MessageBox.Show(this, "已发送 " + result.SentLabels + " 张标签。\r\n\r\n" +
                    "注意：这是发送到打印队列的成功计数，实际出纸请查看打印机。",
                    "批量打印", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                SetStatus("批量打印已取消（校验阶段取消，未发送任何标签）", false, true);
            }
            catch (Exception ex)
            {
                Logger.Error("批量打印失败", ex);
                MessageBox.Show(this, "批量打印失败：\r\n" + ex.Message, "打印", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("批量打印失败：" + ex.Message, true);
            }
            finally
            {
                _batchPrinting = false;
                SetBatchBusy(false);
                try { progress.Finish(); } catch { }
            }
        }

        /// <summary>
        /// 打印期间锁住批量页：否则后台线程遍历的 BatchRow 会被用户同时编辑/清空，
        /// 出现前 100 张新内容、后 100 张旧内容的错乱。
        /// </summary>
        private void SetBatchBusy(bool busy)
        {
            if (_pages != null && _pages.Length > 1 && _pages[1] != null) _pages[1].Enabled = !busy;
            if (_batchPrint != null) _batchPrint.Enabled = !busy;
            if (_batchSaveZpl != null) _batchSaveZpl.Enabled = !busy;
            if (_batchImport != null) _batchImport.Enabled = !busy;
            if (_batchLayoutEdit != null) _batchLayoutEdit.Enabled = !busy;
        }

        private BatchRunResult RunBatch(List<BatchRow> rows, BatchOptions options, LabelSpec spec,
            PrintOptions print, int copies, ProgressForm progress, CancellationToken token)
        {
            var result = new BatchRunResult();
            RenderOptions renderOptions = options.CreateRenderOptions();
            renderOptions.Dpi = _settings.Dpi;
            renderOptions.LayoutMode = options.LayoutMode;
            renderOptions.BarcodeType = options.BarcodeType;
            renderOptions.QrEcc = options.QrEcc;
            renderOptions.ShowBarcodeText = options.ShowBarcodeText;
            renderOptions.MarginTopMm = options.MarginTopMm;
            renderOptions.MarginBottomMm = options.MarginBottomMm;
            renderOptions.MarginLeftMm = options.MarginLeftMm;
            renderOptions.MarginRightMm = options.MarginRightMm;
            renderOptions.FontSizeMm = options.FontSizeMm;
            renderOptions.FontFamily = options.FontFamily;
            renderOptions.Bold = options.Bold;
            renderOptions.LayoutPlan = options.LayoutPlan;
            renderOptions.BarcodeHeightPercent = options.BarcodeHeightPercent;
            renderOptions.QrSizePercent = options.QrSizePercent;
            renderOptions.TextAlign = options.TextAlign;

            for (int i = 0; i < rows.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                BatchRow row = rows[i];
                progress.Report(i, "正在校验第 " + (i + 1) + " / " + rows.Count + " 行");
                ValidationResult check = LabelRenderer.Validate(spec, row.ToContent(options), renderOptions);
                if (!check.IsValid)
                {
                    string err = check.ErrorText;
                    result.RowErrors[row] = err;
                    result.ValidationErrors.Add("第 " + row.RowNumber + " 行：" + err);
                }
            }
            if (result.ValidationErrors.Count > 0) return result;

            const int ChunkLimit = 50;
            var chunk = new StringBuilder();
            int labelsInChunk = 0;
            int sent = 0;
            int chunkIndex = 0;
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

            try
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    BatchRow row = rows[i];
                    using (RenderedLabel rendered = LabelRenderer.Render(spec, row.ToContent(options), renderOptions))
                    {
                        // 份数交给 ^PQ，不再把整张位图复制 N 份发送。
                        string one = ZplBuilder.BuildLabel(spec, rendered, print, "EasyLabel row " + row.RowNumber, copies);
                        chunk.Append(one);
                        labelsInChunk += copies;
                        progress.Report(rows.Count + Math.Min(sent + labelsInChunk, rows.Count * copies),
                            "正在发送 " + (sent + labelsInChunk) + " / " + (rows.Count * copies) + " 张…");
                        if (labelsInChunk >= ChunkLimit)
                        {
                            chunkIndex++;
                            SendBatchChunk(chunk, stamp, chunkIndex);
                            sent += labelsInChunk;
                            labelsInChunk = 0;
                            chunk.Clear();
                        }
                    }
                }
                if (labelsInChunk > 0)
                {
                    chunkIndex++;
                    SendBatchChunk(chunk, stamp, chunkIndex);
                    sent += labelsInChunk;
                }
                result.SentLabels = sent;
            }
            catch (OperationCanceledException)
            {
                // 未凑满一块的数据直接丢弃；SentLabels 只统计已经真正写进打印机的张数。
                result.Canceled = true;
                result.SentLabels = sent;
            }
            return result;
        }

        private void SendBatchChunk(StringBuilder chunk, string stamp, int index)
        {
            if (chunk == null || chunk.Length == 0) return;
            PrinterService.Send(Encoding.ASCII.GetBytes(chunk.ToString()),
                "EasyLabel batch " + stamp + " #" + index, _settings);
        }

        private sealed class BatchRunResult
        {
            public int SentLabels { get; set; }
            public bool Canceled { get; set; }
            public List<string> ValidationErrors { get; private set; }
            public Dictionary<BatchRow, string> RowErrors { get; private set; }

            public BatchRunResult()
            {
                ValidationErrors = new List<string>();
                RowErrors = new Dictionary<BatchRow, string>();
            }
        }

        private void ShowImportDemo()
        {
            try
            {
                string sample = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "samples", "sample-data.xlsx");
                if (!File.Exists(sample)) return;
                WorkbookData wb = ExcelImporter.LoadWorkbook(sample);
                using (var dialog = new ImportDialog(wb, _settings))
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        ApplyImportedRows(dialog.ResultRows, sample);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("导入示例失败", ex);
            }
        }
    }
}
