using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using EasyLabel.Core;

namespace EasyLabel.UI
{
    public sealed class ImportDialog : Form
    {
        private readonly WorkbookData _workbook;
        private readonly ComboBox _sheetCombo;
        private readonly CheckBox _headerCheck;
        private readonly ComboBox _textCombo;
        private readonly ComboBox _barcodeCombo;
        private readonly ComboBox _qrCombo;
        private readonly DataGridView _previewGrid;
        private readonly Label _summary;
        private readonly Label _hint;
        private readonly Button _ok;

        public SheetData SelectedSheet { get; private set; }
        public bool HasHeader { get; private set; }
        public int TextColumn { get; private set; }
        public int BarcodeColumn { get; private set; }
        public int QrColumn { get; private set; }
        public List<BatchRow> ResultRows { get; private set; }

        public ImportDialog(WorkbookData workbook, AppSettings settings)
        {
            _workbook = workbook;
            ResultRows = new List<BatchRow>();
            TextColumn = -1;
            BarcodeColumn = -1;
            QrColumn = -1;

            Text = "导入 Excel / CSV";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(880, 640);
            BackColor = Theme.Background;
            Font = Theme.Ui(9f);

            var top = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(18, 12, 18, 10)
            };
            top.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Border))
                    e.Graphics.DrawLine(pen, 0, top.Height - 1, top.Width, top.Height - 1);
            };

            var fileTitle = Theme.Label("数据文件", 10.5f, Theme.Text, FontStyle.Bold);
            fileTitle.Location = new Point(18, 12);
            var filePath = Theme.Label(workbook.FilePath, 8.8f, Theme.Muted);
            filePath.Location = new Point(18, 36);
            filePath.AutoSize = false;
            filePath.Width = 820;
            filePath.Height = 20;
            filePath.AutoEllipsis = true;

            var sheetLabel = Theme.Label("工作表", 9.2f, Theme.Muted);
            sheetLabel.Location = new Point(18, 66);
            _sheetCombo = UiKit.Combo();
            _sheetCombo.Location = new Point(74, 62);
            _sheetCombo.Size = new Size(220, 28);
            _sheetCombo.SelectedIndexChanged += (s, e) => RefreshSheet();

            _headerCheck = UiKit.Check("首行为标题行", true);
            _headerCheck.Location = new Point(314, 66);
            _headerCheck.CheckedChanged += (s, e) => RefreshColumns(true);

            var hint = Theme.Label("条码列建议在 Excel 中设为“文本”，避免前导零丢失", 8.8f, Theme.Muted);
            hint.Location = new Point(500, 68);
            hint.AutoSize = false;
            hint.Width = 350;
            hint.Height = 20;
            hint.TextAlign = ContentAlignment.MiddleLeft;

            _textCombo = UiKit.Combo();
            _barcodeCombo = UiKit.Combo();
            _qrCombo = UiKit.Combo();
            int y = 106;
            AddMapping(top, "文本列", _textCombo, 18, y);
            AddMapping(top, "一维码列", _barcodeCombo, 240, y);
            AddMapping(top, "二维码列", _qrCombo, 462, y);
            int x = 684;
            var note = Theme.Label("（无）表示不导入该字段", 8.6f, Theme.Muted);
            note.Location = new Point(x, y + 5);

            _textCombo.SelectedIndexChanged += (s, e) => RefreshPreview();
            _barcodeCombo.SelectedIndexChanged += (s, e) => RefreshPreview();
            _qrCombo.SelectedIndexChanged += (s, e) => RefreshPreview();

            top.Controls.Add(fileTitle);
            top.Controls.Add(filePath);
            top.Controls.Add(sheetLabel);
            top.Controls.Add(_sheetCombo);
            top.Controls.Add(_headerCheck);
            top.Controls.Add(hint);
            top.Controls.Add(note);

            var previewCard = new CardPanel("数据预览（前 20 行）")
            {
                Dock = DockStyle.Fill
            };
            _previewGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White
            };
            Theme.StyleGrid(_previewGrid);
            _previewGrid.RowTemplate.Height = Theme.S(32);
            previewCard.Content.Controls.Add(_previewGrid);
            var previewWrapper = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 0, 18, 0), BackColor = Theme.Background };
            previewWrapper.Controls.Add(previewCard);

            var bottom = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(18, 10, 18, 10)
            };
            bottom.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Border))
                    e.Graphics.DrawLine(pen, 0, 0, bottom.Width, 0);
            };

            _summary = Theme.Label("", 8.9f, Theme.Muted);
            _summary.Location = new Point(18, 12);
            _summary.AutoSize = false;
            _summary.Width = 500;
            _summary.Height = 20;
            _hint = Theme.Label("", 8.6f, Theme.Warning);
            _hint.Location = new Point(18, 33);
            _hint.AutoSize = false;
            _hint.Width = 540;
            _hint.Height = 20;

            var cancel = UiKit.SecondaryButton("取消");
            cancel.Location = new Point(670, 14);
            cancel.Size = new Size(86, 36);
            cancel.DialogResult = DialogResult.Cancel;
            _ok = UiKit.PrimaryButton("导入");
            _ok.Location = new Point(768, 14);
            _ok.Size = new Size(94, 36);
            _ok.Click += (s, e) => DoImport();

            bottom.Controls.Add(_summary);
            bottom.Controls.Add(_hint);
            bottom.Controls.Add(cancel);
            bottom.Controls.Add(_ok);
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Theme.Background
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 158));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            root.Controls.Add(top, 0, 0);
            root.Controls.Add(previewWrapper, 0, 1);
            root.Controls.Add(bottom, 0, 2);
            Controls.Add(root);

            AcceptButton = _ok;
            CancelButton = cancel;

            Theme.ApplyScale(this, new Size(880, 640));

            Load += (s, e) =>
            {
                foreach (SheetData sheet in workbook.Sheets)
                    _sheetCombo.Items.Add(sheet.Name);
                if (_sheetCombo.Items.Count > 0) _sheetCombo.SelectedIndex = 0;
                if (workbook.IsCsv) _sheetCombo.Enabled = false;
            };
        }

        private void AddMapping(Control parent, string label, ComboBox combo, int x, int y)
        {
            var lbl = Theme.Label(label, 9.2f, Theme.Muted);
            lbl.Location = new Point(x, y + 5);
            combo.Location = new Point(x + 68, y);
            combo.Size = new Size(146, 28);
            parent.Controls.Add(lbl);
            parent.Controls.Add(combo);
        }

        private void RefreshSheet()
        {
            int index = _sheetCombo.SelectedIndex;
            if (index < 0 || index >= _workbook.Sheets.Count) return;
            SelectedSheet = _workbook.Sheets[index];
            RefreshColumns(true);
        }

        private void RefreshColumns(bool suggest)
        {
            if (SelectedSheet == null) return;
            bool hasHeader = _headerCheck.Checked;
            int cols = SelectedSheet.ColumnCount;

            int oldText = _textCombo.SelectedIndex;
            int oldBarcode = _barcodeCombo.SelectedIndex;
            int oldQr = _qrCombo.SelectedIndex;

            _textCombo.Items.Clear();
            _barcodeCombo.Items.Clear();
            _qrCombo.Items.Clear();
            _textCombo.Items.Add("（无）");
            _barcodeCombo.Items.Add("（无）");
            _qrCombo.Items.Add("（无）");
            for (int c = 0; c < cols; c++)
            {
                string name = SelectedSheet.ColumnName(c, hasHeader);
                _textCombo.Items.Add(name);
                _barcodeCombo.Items.Add(name);
                _qrCombo.Items.Add(name);
            }

            if (suggest)
            {
                int tc, bc, qc;
                ExcelImporter.SuggestMapping(SelectedSheet, hasHeader, out tc, out bc, out qc);
                _textCombo.SelectedIndex = tc >= 0 ? tc + 1 : 0;
                _barcodeCombo.SelectedIndex = bc >= 0 ? bc + 1 : 0;
                _qrCombo.SelectedIndex = qc >= 0 ? qc + 1 : 0;
            }
            else
            {
                _textCombo.SelectedIndex = Math.Min(oldText, _textCombo.Items.Count - 1);
                _barcodeCombo.SelectedIndex = Math.Min(oldBarcode, _barcodeCombo.Items.Count - 1);
                _qrCombo.SelectedIndex = Math.Min(oldQr, _qrCombo.Items.Count - 1);
            }

            RefreshPreview();
        }

        private void RefreshPreview()
        {
            if (SelectedSheet == null) return;
            bool hasHeader = _headerCheck.Checked;
            int textCol = _textCombo.SelectedIndex - 1;
            int barcodeCol = _barcodeCombo.SelectedIndex - 1;
            int qrCol = _qrCombo.SelectedIndex - 1;

            _previewGrid.Rows.Clear();
            _previewGrid.Columns.Clear();
            _previewGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "行", FillWeight = 12 });
            if (textCol >= 0)
                _previewGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "文本", FillWeight = 40 });
            if (barcodeCol >= 0)
                _previewGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "一维码", FillWeight = 28 });
            if (qrCol >= 0)
                _previewGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "二维码", FillWeight = 42 });

            int start = hasHeader ? 1 : 0;
            int dataRows = Math.Max(0, SelectedSheet.Rows.Count - start);
            int shown = 0;
            for (int i = start; i < SelectedSheet.Rows.Count && shown < 20; i++)
            {
                string[] row = SelectedSheet.Rows[i];
                if (IsEmpty(row)) continue;
                var cells = new List<string>();
                cells.Add((i + 1).ToString());
                if (textCol >= 0) cells.Add(Get(row, textCol));
                if (barcodeCol >= 0) cells.Add(Get(row, barcodeCol));
                if (qrCol >= 0) cells.Add(Get(row, qrCol));
                _previewGrid.Rows.Add(cells.ToArray());
                shown++;
            }

            TextColumn = textCol;
            BarcodeColumn = barcodeCol;
            QrColumn = qrCol;
            HasHeader = hasHeader;

            _summary.Text = "共 " + dataRows + " 行数据，已预览 " + shown + " 行";
            _hint.Text = SelectedSheet.Truncated
                ? "工作表超过 " + ExcelImporter.MaxRowsPerSheet + " 行，已截断；建议拆分后再导入。"
                : "";
            if (textCol < 0 && barcodeCol < 0 && qrCol < 0) _hint.Text = "请至少映射一列。";
            _ok.Enabled = (textCol >= 0 || barcodeCol >= 0 || qrCol >= 0) && dataRows > 0;
        }

        private static bool IsEmpty(string[] row)
        {
            if (row == null) return true;
            foreach (string v in row)
                if (!string.IsNullOrWhiteSpace(v)) return false;
            return true;
        }

        private static string Get(string[] row, int col)
        {
            if (row == null || col < 0 || col >= row.Length) return "";
            return (row[col] ?? "").Trim();
        }

        private void DoImport()
        {
            if (SelectedSheet == null)
            {
                MessageBox.Show(this, "请选择工作表", "导入", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int start = _headerCheck.Checked ? 1 : 0;
            int textCol = _textCombo.SelectedIndex - 1;
            int barcodeCol = _barcodeCombo.SelectedIndex - 1;
            int qrCol = _qrCombo.SelectedIndex - 1;
            if (textCol < 0 && barcodeCol < 0 && qrCol < 0)
            {
                MessageBox.Show(this, "请至少映射一列", "导入", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            ResultRows.Clear();
            for (int i = start; i < SelectedSheet.Rows.Count; i++)
            {
                string[] row = SelectedSheet.Rows[i];
                if (IsEmpty(row)) continue;
                string text = textCol >= 0 ? Get(row, textCol) : "";
                string barcode = barcodeCol >= 0 ? Get(row, barcodeCol) : "";
                string qr = qrCol >= 0 ? Get(row, qrCol) : "";
                if (text.Length == 0 && barcode.Length == 0 && qr.Length == 0) continue;
                ResultRows.Add(new BatchRow
                {
                    RowNumber = i + 1,
                    Text = text,
                    Barcode = barcode,
                    Qr = qr,
                    Selected = true
                });
            }

            if (ResultRows.Count == 0)
            {
                Logger.Info("ImportDialog: 没有可导入的数据行");
                MessageBox.Show(this, "没有可导入的数据行", "导入", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Logger.Info("ImportDialog: 导入 " + ResultRows.Count + " 行，工作表 " + SelectedSheet.Name);
            TextColumn = textCol;
            BarcodeColumn = barcodeCol;
            QrColumn = qrCol;
            HasHeader = _headerCheck.Checked;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
