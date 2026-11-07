using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using EasyLabel.Core;

namespace EasyLabel.UI
{
    public sealed partial class MainForm
    {
        private Button _importPrintButton;

        /// <summary>页头的“导入打印”按钮：.elbl 模版和 .zpl 指令文件都能直接拿来出纸。</summary>
        private void BuildImportPrintButton(Control header)
        {
            _importPrintButton = UiKit.SecondaryButton("导入打印");
            _importPrintButton.Size = new Size(96, 32);
            _importPrintButton.Click += (s, e) => ImportPrintFile();
            header.Controls.Add(_importPrintButton);
        }

        /// <summary>选文件 → 看预览/概要 → 打印或载入编辑。.elbl 与 .zpl 共用同一条链路。</summary>
        private void ImportPrintFile()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "导入并打印";
                dialog.Filter =
                    "EasyLabel 与 ZPL (*" + TemplateStore.Extension + ";*" + TemplateStore.ZplExtension + ")|*" +
                    TemplateStore.Extension + ";*" + TemplateStore.ZplExtension +
                    "|EasyLabel 模版 (*" + TemplateStore.Extension + ")|*" + TemplateStore.Extension +
                    "|ZPL 文件 (*" + TemplateStore.ZplExtension + ")|*" + TemplateStore.ZplExtension +
                    "|所有文件 (*.*)|*.*";
                dialog.InitialDirectory = Directory.Exists(_settings.LastFolder)
                    ? _settings.LastFolder
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                _settings.LastFolder = Path.GetDirectoryName(dialog.FileName);
                _settings.Save();
                OpenImportFile(dialog.FileName);
            }
        }

        private async void OpenImportFile(string path)
        {
            LabelTemplate template;
            try
            {
                template = TemplateStore.Default.Load(path);
            }
            catch (Exception ex)
            {
                Logger.Error("导入文件失败：" + path, ex);
                MessageBox.Show(this, "无法读取这个文件：\r\n" + ex.Message + "\r\n\r\n" +
                                      "支持 EasyLabel 模版（.elbl）和 ZPL 指令文件（.zpl）。",
                    "导入并打印", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var dialog = new ImportPrintDialog(path, template, _settings))
            {
                DialogResult result = dialog.ShowDialog(this);
                if (dialog.OpenSettingsRequested)
                {
                    SetStatus("先选择打印机，再回来点“导入打印”", false, true);
                    ShowPage(2);
                    return;
                }
                if (result != DialogResult.OK) return;

                if (dialog.AddToLibrary) AddToTemplateLibrary(path);

                if (dialog.OpenEditor)
                {
                    // 打开编辑只对带版式的 .elbl 有意义；纯 ZPL 没有可编辑的内容。
                    if (template.IsPrintableOnly) PrintRawZpl(template);
                    else ApplyTemplate(template);
                    return;
                }

                if (dialog.PrintRequested) await PrintImportedAsync(template, dialog.Copies);
            }
        }

        private void AddToTemplateLibrary(string path)
        {
            try
            {
                LabelTemplate imported = TemplateStore.Default.Import(path);
                SetStatus("已加入模版库：" + imported.Name, false, false);
            }
            catch (Exception ex)
            {
                Logger.Error("加入模版库失败：" + path, ex);
                MessageBox.Show(this, "加入模版库失败：\r\n" + ex.Message, "导入并打印",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 打印导入的文件：.elbl 按模版重新渲染成 ZPL（保证和预览一致），
        /// 纯 ZPL 直接用原文，只把份数写进 ^PQ。
        /// </summary>
        private async Task PrintImportedAsync(LabelTemplate template, int copies)
        {
            if (!HasPrinterConfigured())
            {
                MessageBox.Show(this, "还没有选择打印机。\r\n请先在“打印机设置”里选择打印机或填写网络地址。",
                    "导入并打印", MessageBoxButtons.OK, MessageBoxIcon.Information);
                ShowPage(2);
                return;
            }

            if (copies < 1) copies = 1;
            string jobName = "EasyLabel " + template.Name;
            try
            {
                string zpl;
                if (template.IsPrintableOnly)
                {
                    // 内嵌的 ^FX 注释对打印机无害，但没必要发出去，先剥掉再改份数。
                    zpl = ZplBuilder.OverrideCopies(ZplTemplateFile.StripMetadata(template.RawZpl), copies);
                }
                else
                {
                    LabelSpec spec = template.GetSpec();
                    RenderOptions options = template.CreateRenderOptions(_settings.Dpi);
                    options.OffsetXMm = _settings.PrintOffsetXMm;
                    options.OffsetYMm = _settings.PrintOffsetYMm;
                    using (RenderedLabel rendered = LabelRenderer.Render(spec, template.CreateContent(), options))
                    {
                        zpl = ZplBuilder.BuildLabel(spec, rendered, _settings.CreatePrintOptions(), jobName, copies);
                    }
                }

                SetStatus("正在发送 " + template.Name + " " + copies + " 张…", false, false);
                await SendZplAsync(zpl, jobName, copies);
            }
            catch (LabelRenderException ex)
            {
                MessageBox.Show(this, ex.Message, "无法打印", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus("打印取消：" + ex.Message, true);
            }
            catch (Exception ex)
            {
                Logger.Error("导入打印失败：" + template.Name, ex);
                MessageBox.Show(this, "打印失败：\r\n" + ex.Message, "导入并打印",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("打印失败：" + ex.Message, true);
            }
        }
    }
}
