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
        private const int InfoRowCount = 5;

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
            AddInfoRow(table, row++, "版本", "v" + AppInfo.Version + " · " + EditionText());
            AddInfoRow(table, row++, "发布日期", AppInfo.ReleaseDate);
            AddInfoRow(table, row++, "作者", AppInfo.Author);
            AddLinkRow(table, row++, "项目地址", AppInfo.ProjectUrl, AppInfo.ProjectUrl);
            AddInfoRow(table, row++, "运行环境", "Windows 10 / 11（64 位）· .NET Framework 4.8");

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 56,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = Theme.Card
            };
            var update = UiKit.PrimaryButton(string.IsNullOrEmpty(_availableVersion) ? "检查更新" : "更新到 " + _availableVersion);
            update.Width = 104;
            update.Click += (s, e) => OpenUpdateDialog();
            var notices = UiKit.SecondaryButton("第三方声明");
            notices.Width = 96;
            notices.Click += (s, e) => OpenLocal(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "THIRD-PARTY-NOTICES.txt"));
            var folder = UiKit.SecondaryButton("程序目录");
            folder.Width = 78;
            folder.Click += (s, e) => OpenFolder(AppDomain.CurrentDomain.BaseDirectory);
            var copy = UiKit.SecondaryButton("复制版本");
            copy.Width = 78;
            copy.Click += (s, e) => CopyVersionInfo();
            buttons.Controls.Add(update);
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
                DetectUrls = true,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                Text = CopyrightText()
            };
            text.LinkClicked += (s, e) => OpenUrl(e.LinkText);
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

        private void AddLinkRow(TableLayoutPanel table, int row, string key, string text, string url)
        {
            var k = Theme.Label(key, 9f, Theme.Muted);
            k.Anchor = AnchorStyles.Left;
            k.Margin = new Padding(0, 5, 8, 5);

            var link = new LinkLabel
            {
                Text = text,
                AutoSize = false,
                Dock = DockStyle.Fill,
                Height = 22,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                LinkBehavior = LinkBehavior.HoverUnderline,
                Font = Theme.Ui(9f),
                LinkColor = Theme.Accent,
                ActiveLinkColor = Theme.AccentDark,
                VisitedLinkColor = Theme.Accent,
                BackColor = Theme.Card,
                Margin = new Padding(0, 3, 0, 3)
            };
            link.LinkClicked += (s, e) => OpenUrl(url);

            table.Controls.Add(k, 0, row);
            table.Controls.Add(link, 1, row);
        }

        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法打开链接：\r\n" + url + "\r\n\r\n" + ex.Message, "打开链接",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static string CopyrightText()
        {
            return
"EasyLabel 标签轻松打  ·  v" + AppInfo.Version + "\r\n" +
AppInfo.Copyright + "\r\n" +
"作者：" + AppInfo.Author + "　·　项目地址：" + AppInfo.ProjectUrl + "\r\n\r\n" +
"一、权利归属\r\n" +
"本软件（含源代码、目标程序、界面设计、图标与随附文档）由作者独立开发，著作权归作者所有。著作权自软件创作完成之日起自动产生，无需登记，受《中华人民共和国著作权法》《计算机软件保护条例》与《伯尔尼保护文学和艺术作品公约》保护。\r\n\r\n" +
"二、授权范围\r\n" +
"在遵守第三部分限制的前提下，作者授予您免费的、非独占的、不可转让的许可：\r\n" +
"· 在自有或本单位任意数量的电脑上安装、运行本软件；\r\n" +
"· 为备份目的复制本软件；\r\n" +
"· 以原始、未经修改的形式（含本声明与第三方许可证）免费分发给第三方。\r\n" +
"源代码在 GitHub 上公开，目的是便于查阅、审计与交流；公开可见不等于开源授权，除本条明确授予的权利外，作者未授予其他权利。\r\n\r\n" +
"三、使用限制\r\n" +
"· 不得销售、出租本软件，或将其捆绑进收费产品、按次计费的服务；\r\n" +
"· 不得发布基于本软件源代码修改、翻译或反编译所得的衍生版本（在 GitHub 平台内 Fork 仓库并提交改进建议的除外）；\r\n" +
"· 不得删除、隐藏或篡改本软件中的版权声明、许可文本与第三方组件声明；\r\n" +
"· 不得暗示修改版已获得作者授权、认证或背书。\r\n" +
"法律强制允许的情形（如为学习研究软件内含的设计思想和原理而作必要复制，或为在实际应用环境中使用而作必要修改）不受本条限制。\r\n\r\n" +
"四、免责声明\r\n" +
"本软件按“现状”提供。在法律允许的最大范围内，作者不提供任何明示或默示的担保，包括适销性、特定用途适用性与不侵权的担保。因使用或无法使用本软件造成的标签内容错误、打印废品、耗材损失、设备故障或业务中断，作者不承担赔偿责任。\r\n" +
"打印结果受打印机分辨率、驱动、介质与校准状态影响，请在批量打印前用测试页核对尺寸与内容。\r\n" +
"本条不排除依法不得排除的责任，包括因故意或重大过失造成财产损失的责任，以及造成人身损害的责任。\r\n\r\n" +
"五、第三方组件\r\n" +
"· ZXing.Net 0.16.11（Apache License 2.0）— 一维码 / QR 码的生成与识读，版权归其作者所有\r\n" +
"· ExcelDataReader 3.9.0（MIT License）— 读取 .xls / .xlsx，版权归其贡献者所有\r\n" +
"两者以未经修改的二进制形式随本软件分发。许可证副本随程序提供于 LICENSES 目录，归属声明见 THIRD-PARTY-NOTICES.txt，满足 Apache License 2.0 第 4 条与 MIT 许可证的随附要求。\r\n\r\n" +
"六、商标声明\r\n" +
"本软件为独立开发，与任何打印机厂商均无隶属、代理、赞助或背书关系。文中提及的产品名称、型号、指令集名称（如 ZPL）与商标，均为其各自权利人的商标或注册商标，仅以描述性方式用于说明兼容性。\r\n\r\n" +
"七、数据与隐私\r\n" +
"本软件不含遥测、埋点或使用统计，标签内容与导入的表格数据只在本机处理。仅当您点击“检查更新”或启用启动自动检查时，程序才会访问更新地址获取版本信息与更新包；该地址默认为项目发布页，也可改为贵单位自建的更新源。使用网络 9100 直连打印时，ZPL 数据仅发送至您指定的打印机地址。\r\n" +
"运行日志只记录时间、打印机、份数与错误摘要，不记录标签正文。\r\n" +
"当前数据目录：" + AppSettings.DataDirectory + "\r\n\r\n" +
"八、许可变更\r\n" +
"作者可能随新版本调整本许可条款，调整仅对该版本之后发布的版本生效；您已取得的版本仍适用其随附的许可。";
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
