using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using EasyLabel.Core;

namespace EasyLabel.UI
{
    /// <summary>字体管理：导入 ttf/otf 到程序私有字体库，或删除已导入的字体。</summary>
    public sealed class FontManagerDialog : Form
    {
        private readonly ListView _list;
        private readonly Label _hint;
        private readonly Button _delete;

        /// <summary>本次打开期间是否有增删，关闭后主界面据此刷新字体下拉。</summary>
        public bool Changed { get; private set; }

        public FontManagerDialog()
        {
            Text = "字体管理";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(720, 470);
            MinimumSize = new Size(600, 380);
            BackColor = Theme.Background;
            Font = Theme.Ui(9f);
            AutoScaleMode = AutoScaleMode.None;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Theme.Background,
                Padding = new Padding(14, 12, 14, 10)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(46)));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(52)));

            var intro = Theme.Label(
                "导入的字体只对 EasyLabel 生效，不会安装到系统。字体文件保存在：" + FontLibrary.FontDirectory,
                8.8f, Theme.Muted);
            intro.AutoSize = false;
            intro.Dock = DockStyle.Fill;
            intro.TextAlign = ContentAlignment.MiddleLeft;
            intro.AutoEllipsis = true;
            root.Controls.Add(intro, 0, 0);

            var card = new CardPanel("已导入的字体") { Dock = DockStyle.Fill, AutoFitHeight = false };
            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = true,
                HideSelection = false,
                ShowItemToolTips = true,
                BorderStyle = BorderStyle.None,
                Font = Theme.Ui(9.2f),
                BackColor = Color.White,
                ForeColor = Theme.Text
            };
            _list.Columns.Add("字体名称", Theme.S(240));
            _list.Columns.Add("文件名", Theme.S(230));
            _list.Columns.Add("大小", Theme.S(90));
            _list.Resize += (s, e) => FitColumns();
            _list.SelectedIndexChanged += (s, e) => UpdateButtons();

            _hint = Theme.Label("", 8.3f, Theme.Muted);
            _hint.AutoSize = false;
            _hint.Dock = DockStyle.Bottom;
            _hint.Height = Theme.S(30);
            _hint.TextAlign = ContentAlignment.MiddleLeft;
            _hint.Padding = new Padding(10, 0, 0, 0);

            var host = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            host.Controls.Add(_list);
            host.Controls.Add(_hint);
            card.Content.Controls.Add(host);
            root.Controls.Add(card, 0, 1);

            var bar = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
            var add = UiKit.PrimaryButton("添加字体…");
            add.Size = new Size(Theme.S(112), Theme.S(32));
            add.Click += (s, e) => ImportFonts();

            _delete = UiKit.SecondaryButton("删除所选");
            _delete.Size = new Size(Theme.S(96), Theme.S(32));
            _delete.Click += (s, e) => DeleteSelected();

            var openDir = UiKit.SecondaryButton("打开字体目录");
            openDir.Size = new Size(Theme.S(120), Theme.S(32));
            openDir.Click += (s, e) => OpenFontDirectory();

            var close = UiKit.SecondaryButton("关闭");
            close.Size = new Size(Theme.S(80), Theme.S(32));
            close.DialogResult = DialogResult.OK;

            bar.Controls.Add(add);
            bar.Controls.Add(_delete);
            bar.Controls.Add(openDir);
            bar.Controls.Add(close);
            Action place = () =>
            {
                close.Location = new Point(bar.Width - close.Width - Theme.S(2), Theme.S(10));
                openDir.Location = new Point(close.Left - openDir.Width - Theme.S(8), Theme.S(10));
                _delete.Location = new Point(Theme.S(2), Theme.S(10));
                add.Location = new Point(_delete.Right + Theme.S(8), Theme.S(10));
            };
            bar.Resize += (s, e) => place();
            root.Controls.Add(bar, 0, 2);

            Controls.Add(root);
            AcceptButton = close;
            CancelButton = close;

            Shown += (s, e) => place();
            Load += (s, e) => Reload();
            Reload();
        }

        private void FitColumns()
        {
            int width = _list.ClientSize.Width;
            if (width < 120) return;
            int size = Theme.S(90);
            int rest = Math.Max(Theme.S(140), width - size - Theme.S(4));
            _list.Columns[0].Width = (int)(rest * 0.52);
            _list.Columns[1].Width = rest - _list.Columns[0].Width;
            _list.Columns[2].Width = size;
        }

        private void Reload()
        {
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                IList<CustomFontInfo> fonts = FontLibrary.GetCustomFonts();
                foreach (CustomFontInfo font in fonts)
                {
                    var item = new ListViewItem(font.DisplayName);
                    item.SubItems.Add(font.FileName ?? "");
                    item.SubItems.Add(font.SizeText);
                    item.Tag = font;
                    if (string.IsNullOrEmpty(font.FamilyName))
                        item.ForeColor = Theme.Error;
                    _list.Items.Add(item);
                }
            }
            finally
            {
                _list.EndUpdate();
            }

            int count = _list.Items.Count;
            _hint.Text = count == 0
                ? "还没有导入字体。点“添加字体…”选择 ttf / otf 文件。"
                : "共 " + count + " 个字体；红色表示文件无法解析。";
            FitColumns();
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            if (_delete != null) _delete.Enabled = _list.SelectedItems.Count > 0;
        }

        private void ImportFonts()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "添加字体";
                dialog.Filter = FontLibrary.BuildFilter();
                dialog.Multiselect = true;
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                var failed = new List<string>();
                var imported = new List<string>();
                foreach (string path in dialog.FileNames)
                {
                    try
                    {
                        CustomFontInfo info = FontLibrary.Import(path);
                        imported.Add(info.DisplayName);
                    }
                    catch (Exception ex)
                    {
                        failed.Add(System.IO.Path.GetFileName(path) + "：" + ex.Message);
                    }
                }

                Changed = Changed || imported.Count > 0;
                Reload();

                if (failed.Count > 0)
                {
                    MessageBox.Show(this,
                        (imported.Count > 0 ? "已导入 " + imported.Count + " 个字体。\r\n\r\n" : "") +
                        "以下文件没能导入：\r\n" + string.Join("\r\n", failed.ToArray()),
                        "添加字体", MessageBoxButtons.OK,
                        imported.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Error);
                }
                else if (imported.Count > 0)
                {
                    _hint.Text = "已导入 " + imported.Count + " 个字体：" + string.Join("、", imported.ToArray());
                }
            }
        }

        private void DeleteSelected()
        {
            if (_list.SelectedItems.Count == 0) return;
            var targets = new List<CustomFontInfo>();
            foreach (ListViewItem item in _list.SelectedItems)
            {
                var info = item.Tag as CustomFontInfo;
                if (info != null) targets.Add(info);
            }
            if (targets.Count == 0) return;

            string names = string.Join("、", targets.ConvertAll(t => t.DisplayName).ToArray());
            DialogResult confirm = MessageBox.Show(this,
                "确定删除以下字体？\r\n\r\n" + names + "\r\n\r\n删除后，已使用该字体的模版会显示为默认字体。",
                "删除字体", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes) return;

            var failed = new List<string>();
            int removed = 0;
            foreach (CustomFontInfo font in targets)
            {
                if (FontLibrary.Remove(font)) removed++;
                else failed.Add(font.DisplayName);
            }

            Changed = Changed || removed > 0;
            Reload();

            if (failed.Count > 0)
            {
                MessageBox.Show(this,
                    "以下字体文件删除失败（可能正被其他程序占用）：\r\n" + string.Join("\r\n", failed.ToArray()),
                    "删除字体", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else if (removed > 0)
            {
                _hint.Text = "已删除 " + removed + " 个字体；重启程序后彻底释放。";
            }
        }

        private void OpenFontDirectory()
        {
            try
            {
                string dir = FontLibrary.FontDirectory;
                System.IO.Directory.CreateDirectory(dir);
                Process.Start("explorer.exe", "\"" + dir + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法打开字体目录：\r\n" + ex.Message, "字体管理",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
