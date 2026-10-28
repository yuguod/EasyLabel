using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using EasyLabel.Core;

namespace EasyLabel.UI
{
    public sealed partial class MainForm
    {
        private const int InfoRowCount = 6;

        private Panel BuildCopyrightPage()
        {
            var page = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Theme.Background
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            layout.Controls.Add(BuildProductCard(), 0, 0);
            layout.Controls.Add(BuildLegalCard(), 1, 0);
            page.Controls.Add(layout);

            _pageLayouts.Add(delegate { });
            return page;
        }

        private CardPanel BuildProductCard()
        {
            var card = new CardPanel("产品信息")
            {
                Dock = DockStyle.Fill,
                AutoFitHeight = false,
                Margin = new Padding(0, 0, 12, 0)
            };

            var body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card };

            var name = Theme.Label(AppInfo.ProductName, 16f, Theme.Text, FontStyle.Bold);
            name.AutoSize = false;
            name.Dock = DockStyle.Top;
            name.Height = 38;
            name.TextAlign = ContentAlignment.MiddleLeft;

            var tagline = Theme.Label(AppInfo.Tagline, 8.6f, Theme.Muted);
            tagline.AutoSize = false;
            tagline.Dock = DockStyle.Top;
            tagline.Height = 46;
            tagline.TextAlign = ContentAlignment.TopLeft;

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = InfoRowCount,
                BackColor = Theme.Card
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < InfoRowCount; i++)
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            int row = 0;
            AddInfoRow(table, row++, "版本", "v" + AppInfo.Version + " · 绿色免安装版");
            AddInfoRow(table, row++, "发布日期", AppInfo.ReleaseDate);
            AddInfoRow(table, row++, "著作权人", AppInfo.CopyrightHolder);
            AddInfoRow(table, row++, "运行环境", "Windows 10 / 11（64 位）· .NET Framework 4.8");
            AddInfoRow(table, row++, "打印方式", "整张标签 1-bit 点阵 → ZPL ^GFA → RAW / 网络 9100");
            AddInfoRow(table, row++, "数据目录", AppSettings.DataDirectory);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 56,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = Theme.Card
            };
            var notices = UiKit.SecondaryButton("第三方声明");
            notices.Width = 104;
            notices.Click += (s, e) => OpenLocal(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "THIRD-PARTY-NOTICES.txt"));
            var folder = UiKit.SecondaryButton("程序目录");
            folder.Width = 88;
            folder.Click += (s, e) => OpenFolder(AppDomain.CurrentDomain.BaseDirectory);
            var copy = UiKit.SecondaryButton("复制版本");
            copy.Width = 90;
            copy.Click += (s, e) => CopyVersionInfo();
            buttons.Controls.Add(notices);
            buttons.Controls.Add(folder);
            buttons.Controls.Add(copy);

            // Dock=Top 的控件按“后添加的在上方”排列，所以这里倒序加入。
            body.Controls.Add(table);
            body.Controls.Add(tagline);
            body.Controls.Add(name);
            body.Controls.Add(buttons);
            card.Content.Controls.Add(body);

            return card;
        }

        private CardPanel BuildLegalCard()
        {
            var card = new CardPanel("版权与许可")
            {
                Dock = DockStyle.Fill,
                AutoFitHeight = false,
                Margin = new Padding(0, 0, 0, 0)
            };

            var text = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = Color.White,
                ForeColor = Theme.Text,
                Font = Theme.Ui(9.5f),
                DetectUrls = false,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                Text = CopyrightText()
            };
            card.Content.Controls.Add(text);
            return card;
        }

        private static void AddInfoRow(TableLayoutPanel table, int row, string key, string value)
        {
            var k = Theme.Label(key, 9f, Theme.Muted);
            k.Anchor = AnchorStyles.Left;
            k.Margin = new Padding(0, 5, 8, 5);

            var v = Theme.Label(value, 9f, Theme.Text);
            v.AutoSize = false;
            v.Dock = DockStyle.Fill;
            v.Height = 22;
            v.TextAlign = ContentAlignment.MiddleLeft;
            v.AutoEllipsis = true;
            v.Margin = new Padding(0, 3, 0, 3);

            table.Controls.Add(k, 0, row);
            table.Controls.Add(v, 1, row);
        }

        private static string CopyrightText()
        {
            return
"EasyLabel 标签轻松打  ·  v" + AppInfo.Version + "\r\n" +
AppInfo.Copyright + "\r\n\r\n" +
"一、权利归属\r\n" +
"本软件（含程序代码、界面设计、图标与随附文档）的著作权归 " + AppInfo.CopyrightHolder +
" 所有，受《中华人民共和国著作权法》及国际版权公约保护。\r\n\r\n" +
"二、您可以\r\n" +
"· 在任意数量的自有或本单位电脑上安装、使用本软件；\r\n" +
"· 为备份目的复制本软件；\r\n" +
"· 在文件完整（含本声明）的前提下免费分发给第三方。\r\n\r\n" +
"三、您不可以\r\n" +
"· 销售、出租、捆绑销售本软件，或将本软件作为收费服务的一部分；\r\n" +
"· 反编译、反向工程本软件，或删除、隐藏版权声明与许可文本；\r\n" +
"· 以本软件名义发布修改版，或暗示已获得作者授权、背书。\r\n\r\n" +
"四、免责声明\r\n" +
"本软件按“现状”提供，不附带任何明示或默示担保。因使用本软件导致的标签内容错误、耗材损失或业务中断，作者不承担责任。大批量打印前，请先用测试页核对尺寸与内容。\r\n\r\n" +
"五、第三方组件\r\n" +
"· ZXing.Net 0.16.11（Apache License 2.0）— 一维码 / QR 码生成与识读\r\n" +
"· ExcelDataReader 3.9.0（MIT License）— 读取 .xls / .xlsx\r\n" +
"以上组件版权归其各自作者所有，完整许可证见程序目录的 LICENSES 与 THIRD-PARTY-NOTICES.txt。\r\n\r\n" +
"六、商标声明\r\n" +
"本软件为独立开发的标签打印工具，与任何打印机厂商均无隶属、代理或背书关系；文中提及的打印机型号、厂商名称与商标归其各自所有者所有，仅用于说明兼容性。\r\n\r\n" +
"七、数据与隐私\r\n" +
"标签内容只在本机处理，不上传网络；日志只记录时间、打印机、数量与错误信息，不记录标签正文。\r\n" +
"设置与日志目录：" + AppSettings.DataDirectory;
        }

        private void CopyVersionInfo()
        {
            try
            {
                Clipboard.SetText(AppInfo.ProductName + "  v" + AppInfo.Version + "\r\n" +
                                  AppInfo.Copyright + "\r\n" +
                                  "运行环境：" + Environment.OSVersion.VersionString + " / .NET " +
                                  Environment.Version + "\r\nDPI：" + DeviceDpi);
                SetStatus("版本信息已复制到剪贴板", false, false);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "复制失败：\r\n" + ex.Message, "复制版本信息",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OpenLocal(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    MessageBox.Show(this, "文件不存在：\r\n" + path, "打开文件",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                Process.Start("notepad.exe", "\"" + path + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法打开文件：\r\n" + ex.Message, "打开文件",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
