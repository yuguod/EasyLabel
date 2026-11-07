using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using EasyLabel.Core;

namespace EasyLabel.UI
{
    /// <summary>
    /// 导入 .elbl / .zpl 后先确认再打印：模版渲染成图看效果，纯 ZPL 显示指令原文和概要。
    /// 对话框只负责收集“打不打、打几份、要不要顺带入库”，真正的发送交给主窗体统一处理。
    /// </summary>
    public sealed class ImportPrintDialog : Form
    {
        private const int DesignWidth = 760;
        private const int DesignHeight = 620;
        private const int MaxPreviewChars = 40000;

        private readonly LabelTemplate _template;
        private readonly AppSettings _settings;
        private NumericUpDown _copies;
        private CheckBox _addToLibrary;
        private Button _print;

        /// <summary>用户点了“打印”。</summary>
        public bool PrintRequested { get; private set; }

        /// <summary>用户勾了“同时加入模版库”。</summary>
        public bool AddToLibrary { get; private set; }

        /// <summary>用户点了“打开编辑”（只对带版式的 .elbl 有效）。</summary>
        public bool OpenEditor { get; private set; }

        /// <summary>没配打印机时点了“去设置打印机”，调用方应跳到打印机设置页。</summary>
        public bool OpenSettingsRequested { get; private set; }

        public int Copies
        {
            get { return _copies == null ? 1 : (int)_copies.Value; }
        }

        public ImportPrintDialog(string path, LabelTemplate template, AppSettings settings)
        {
            if (template == null) throw new ArgumentNullException("template");
            _template = template;
            _settings = settings ?? new AppSettings();

            Text = "导入并打印";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Theme.Background;
            ClientSize = new Size(DesignWidth, DesignHeight);
            Font = Theme.Ui(9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildUi(path);
            Theme.ApplyScale(this, new Size(DesignWidth, DesignHeight));
        }

        private void BuildUi(string path)
        {
            string fileName = string.IsNullOrEmpty(path) ? _template.Name : Path.GetFileName(path);

            var header = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Color.White };
            header.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Border))
                    e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
                using (var brush = new SolidBrush(Theme.Accent))
                    e.Graphics.FillRectangle(brush, 0, 0, 4, header.Height);
            };
            var title = Theme.Label("导入并打印", 16f, Theme.Text, FontStyle.Bold);
            title.Location = new Point(24, 12);
            var subtitle = Theme.Label(fileName + " · " + (_template.IsPrintableOnly ? "ZPL 指令文件" : "EasyLabel 模版"),
                8.8f, Theme.Muted);
            subtitle.AutoSize = false;
            subtitle.AutoEllipsis = true;
            subtitle.Location = new Point(26, 44);
            subtitle.Size = new Size(DesignWidth - 60, 20);
            header.Controls.Add(title);
            header.Controls.Add(subtitle);

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Color.White };
            footer.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Border))
                    e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
            };

            var body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };

            // ---- 概要卡片 ----
            var info = new Panel { Location = new Point(24, 14), Size = new Size(DesignWidth - 48, 84), BackColor = Color.White };
            info.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Border))
                    e.Graphics.DrawRectangle(pen, 0, 0, info.Width - 1, info.Height - 1);
            };

            var head = Theme.Label(Headline() , 10.5f, Theme.Text, FontStyle.Bold);
            head.Location = new Point(16, 10);
            var detail = Theme.Label(DetailLine(path), 8.8f, Theme.Muted);
            detail.AutoSize = false;
            detail.AutoEllipsis = true;
            detail.Location = new Point(18, 36);
            detail.Size = new Size(DesignWidth - 84, 20);
            var target = Theme.Label(TargetLine(), 8.8f, HasPrinter() ? Theme.Muted : Theme.Warning);
            target.AutoSize = false;
            target.AutoEllipsis = true;
            target.Location = new Point(18, 56);
            target.Size = new Size(DesignWidth - 84, 20);
            info.Controls.Add(head);
            info.Controls.Add(detail);
            info.Controls.Add(target);

            // ---- 预览 ----
            var preview = new Panel
            {
                Location = new Point(24, 106),
                Size = new Size(DesignWidth - 48, 300),
                BackColor = Color.White
            };
            preview.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Border))
                    e.Graphics.DrawRectangle(pen, 0, 0, preview.Width - 1, preview.Height - 1);
            };
            if (_template.IsPrintableOnly)
            {
                // 指令文本贴边走很难看；图片预览由控件自己留白，不需要额外缩进。
                preview.Padding = new Padding(8, 6, 8, 6);
                BuildZplPreview(preview);
            }
            else BuildImagePreview(preview);

            // ---- 选项 ----
            var copiesLabel = Theme.Label("打印份数", 9f, Theme.Text);
            copiesLabel.Location = new Point(26, 420);
            _copies = UiKit.Numeric(1, 9999, Math.Max(1, DefaultCopies()), 0, 1);
            _copies.Location = new Point(104, 416);
            _copies.Size = new Size(84, 26);

            _addToLibrary = UiKit.Check("打印后加入模版库", false);
            _addToLibrary.Location = new Point(212, 418);
            _addToLibrary.Width = 200;

            var hint = Theme.Label(HintText(), 8.4f, Theme.Muted);
            hint.AutoSize = false;
            hint.Location = new Point(26, 448);
            hint.Size = new Size(DesignWidth - 52, 32);

            body.Controls.Add(info);
            body.Controls.Add(preview);
            body.Controls.Add(copiesLabel);
            body.Controls.Add(_copies);
            body.Controls.Add(_addToLibrary);
            body.Controls.Add(hint);

            // ---- 底部按钮 ----
            var cancel = UiKit.SecondaryButton("取消");
            cancel.Size = new Size(88, 36);
            cancel.Location = new Point(DesignWidth - 24 - 122 - 8 - 88, 12);
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            _print = UiKit.PrimaryButton("打印");
            _print.Size = new Size(122, 36);
            _print.Location = new Point(DesignWidth - 24 - 122, 12);
            if (HasPrinter())
            {
                _print.Click += (s, e) =>
                {
                    PrintRequested = true;
                    AddToLibrary = _addToLibrary.Checked;
                    DialogResult = DialogResult.OK;
                    Close();
                };
            }
            else
            {
                // 没配打印机时别给一个点了没反应的按钮，直接把人带到设置页。
                _print.Text = "去设置打印机";
                _print.Click += (s, e) =>
                {
                    OpenSettingsRequested = true;
                    DialogResult = DialogResult.Cancel;
                    Close();
                };
            }

            if (!_template.IsPrintableOnly)
            {
                var edit = UiKit.SecondaryButton("打开编辑");
                edit.Size = new Size(100, 36);
                edit.Location = new Point(cancel.Left - 8 - 100, 12);
                edit.Click += (s, e) =>
                {
                    OpenEditor = true;
                    AddToLibrary = _addToLibrary.Checked;
                    DialogResult = DialogResult.OK;
                    Close();
                };
                footer.Controls.Add(edit);
            }

            footer.Controls.Add(_print);
            footer.Controls.Add(cancel);

            Controls.Add(body);
            Controls.Add(footer);
            Controls.Add(header);
            CancelButton = cancel;
            AcceptButton = _print;
        }

        private void BuildImagePreview(Panel host)
        {
            var view = new LabelPreviewControl { Dock = DockStyle.Fill, Margin = new Padding(2) };
            host.Controls.Add(view);
            try
            {
                LabelContent content = _template.CreateContent();
                RenderOptions options = _template.CreateRenderOptions(_settings.Dpi);
                options.OffsetXMm = _settings.PrintOffsetXMm;
                options.OffsetYMm = _settings.PrintOffsetYMm;
                if (!content.HasText && !content.HasBarcode && !content.HasQr)
                {
                    view.SetPlaceholder("该模版只保存了版式，没有示例内容\r\n打开后输入文字即可");
                    return;
                }
                RenderedLabel rendered = LabelRenderer.Render(_template.GetSpec(), content, options);
                view.SetImage(rendered.Bitmap, rendered.WidthDots + " × " + rendered.HeightDots + " 点 · " + _settings.Dpi + " dpi");
            }
            catch (LabelRenderException ex)
            {
                view.SetPlaceholder("内容无法预览\r\n" + ex.Message);
            }
            catch (Exception ex)
            {
                Logger.Error("导入打印预览失败：" + _template.Name, ex);
                view.SetPlaceholder("预览失败\r\n" + ex.Message);
            }
        }

        private void BuildZplPreview(Panel host)
        {
            string text = _template.RawZpl ?? "";
            string shown = text.Length > MaxPreviewChars
                ? text.Substring(0, MaxPreviewChars) + "\r\n\r\n…（文件较长，只显示前 " + MaxPreviewChars + " 个字符，打印不受影响）"
                : text;

            var box = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                WordWrap = false,
                ScrollBars = ScrollBars.Both,
                BorderStyle = BorderStyle.None,
                BackColor = Color.White,
                ForeColor = Theme.Text,
                Text = shown,
                // 不参与 Tab：否则一打开就把整段指令全选成蓝底，看着像是坏了。
                TabStop = false,
                Margin = new Padding(8, 6, 4, 4),
                Font = MonospaceFont()
            };
            host.Controls.Add(box);
            box.SelectionLength = 0;
        }

        private string Headline()
        {
            if (_template.IsPrintableOnly)
            {
                ZplSummary summary = ZplTemplateFile.Inspect(_template.RawZpl);
                string size = summary.WidthDots > 0 && summary.HeightDots > 0
                    ? summary.WidthDots + " × " + summary.HeightDots + " 点"
                    : "尺寸未声明";
                return "外部 ZPL 文件 · " + size;
            }
            LabelSpec spec = _template.GetSpec();
            return "EasyLabel 模版 · " + spec.WidthMm.ToString("0.#", CultureInfo.InvariantCulture) + " × " +
                   spec.HeightMm.ToString("0.#", CultureInfo.InvariantCulture) + " mm";
        }

        private string DetailLine(string path)
        {
            if (_template.IsPrintableOnly)
            {
                ZplSummary summary = ZplTemplateFile.Inspect(_template.RawZpl);
                string parts = summary.ByteCount > 0 ? summary.ByteText : SizeText(path);
                parts += " · " + summary.LineCount + " 行";
                if (summary.HasGraphic) parts += " · 含点阵图";
                if (summary.HasEmbeddedTemplate) parts += " · 带 EasyLabel 版式信息";
                return parts;
            }
            string detail = _template.LayoutModeName + " · " + _template.BarcodeTypeName + " · QR " + _template.QrEcc;
            string font = string.IsNullOrWhiteSpace(_template.FontFamily) ? "默认字体" : _template.FontFamily;
            detail += " · " + font + (_template.FontSizeMm > 0
                ? " " + _template.FontSizeMm.ToString("0.#", CultureInfo.InvariantCulture) + " mm"
                : " 自动字号");
            if (_template.Bold) detail += " 加粗";
            return detail;
        }

        private string TargetLine()
        {
            if (!HasPrinter()) return "尚未配置打印机 — 请先到“打印机设置”里选择，再回来打印";
            string target = _settings.UseNetwork
                ? _settings.NetworkHost + ":" + _settings.NetworkPort
                : _settings.PrinterName;
            return "打印到：" + target;
        }

        private string HintText()
        {
            if (_template.IsPrintableOnly)
            {
                return "ZPL 内容已经栅格化，这里只原样发送，不能改版式；上面的份数会写进 ^PQ。\r\n" +
                       "勾选“同时加入模版库”后，下次可以在“模版 ▾ → 打开模版…”里直接找到它。";
            }
            return "打印时按当前模版的版式和 " + _settings.Dpi + " dpi 重新生成 ZPL 再发送，不会改动原文件。\r\n" +
                   "想调整文字或条码，点“打开编辑”载入到单张页。";
        }

        private int DefaultCopies()
        {
            int copies = _template.IsPrintableOnly ? _template.ZplCopies : _template.Copies;
            return copies < 1 ? 1 : Math.Min(9999, copies);
        }

        private bool HasPrinter()
        {
            if (_settings.UseNetwork) return !string.IsNullOrWhiteSpace(_settings.NetworkHost);
            return !string.IsNullOrWhiteSpace(_settings.PrinterName);
        }

        private static string SizeText(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    long length = new FileInfo(path).Length;
                    if (length >= 1024) return (length / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
                    return length.ToString(CultureInfo.InvariantCulture) + " B";
                }
            }
            catch { }
            return "大小未知";
        }

        private static Font MonospaceFont()
        {
            try { return new Font("Consolas", 9.2f, FontStyle.Regular, GraphicsUnit.Point); }
            catch { return Theme.Ui(9.2f); }
        }
    }
}
