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
        private Panel BuildHelpPage()
        {
            var page = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
            var card = new CardPanel("使用说明")
            {
                Dock = DockStyle.Fill
            };
            var host = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            var tools = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 46,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.White
            };
            var samples = UiKit.SecondaryButton("打开示例目录");
            samples.Size = new Size(124, 34);
            samples.Click += (s, e) =>
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "samples");
                OpenFolder(Directory.Exists(dir) ? dir : AppDomain.CurrentDomain.BaseDirectory);
            };
            var logs = UiKit.SecondaryButton("打开日志目录");
            logs.Size = new Size(124, 34);
            logs.Click += (s, e) => OpenFolder(Logger.LogDirectory);
            tools.Controls.Add(samples);
            tools.Controls.Add(logs);

            var text = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = Color.White,
                ForeColor = Theme.Text,
                Font = Theme.Ui(9.5f),
                DetectUrls = false,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };
            text.Text = HelpText();
            host.Controls.Add(text);
            host.Controls.Add(tools);
            card.Content.Controls.Add(host);
            page.Controls.Add(card);
            _pageLayouts.Add(delegate { });
            return page;
        }

        private static string HelpText()
        {
            return
@"EasyLabel 标签轻松打 · 使用说明

一、快速开始
1. 到“打印机设置”选择 Windows 打印队列，或勾选“网络直连”填写打印机 IP。
2. 确认打印分辨率与打印机实际型号一致；本工具默认 600 dpi，也支持 203 / 300 dpi。
3. 在“单张打印”输入文字，按需勾选一维码/二维码，点击“打印标签”。
4. 首次使用建议先打印“测试页”，用尺子量一下标签尺寸是否正确。

二、标签规格
· 40×15 mm：600 dpi 为 945×354 点；适合 2–3 行短文字或“文字 + 条码 + 小二维码”。
· 30×25 mm：600 dpi 为 709×591 点；适合 3–5 行文字或更宽松的条码/二维码组合。
· 30×20 mm：600 dpi 为 709×472 点；比 40×15 宽松，适合短文字 + 条码 + 二维码。
· 自定义：可选 5–300 mm 高、最大宽度约 104 mm；宽度超过打印机最大打印宽度时无法完整输出。
· 程序按 ZPL 的 ^PW / ^LL 精确设置标签宽高，不经过 Windows 驱动的页面缩放。
· 如果实际出纸尺寸整体偏大/偏小，优先检查 DPI 是否选错。

三、字体、字号、加粗与边距
· “打印机设置 → 默认版式”可设置默认字体、默认字号、默认加粗和上/下/左/右四边距。
· 单张页“高级版式”、批量页“批量版式”中也可以分别调整，修改后实时预览。
· 字号以 mm 为单位；指定字号放不下时程序会自动缩小，并在预览下方提示实际字号。
· 中文标签推荐 Microsoft YaHei UI / Microsoft YaHei / SimHei / SimSun。
· 加粗只作用于标签正文；一维码下方明文保持常规字重，避免影响扫描。
· 边距范围 0–20 mm；四边距过大时预览会提示无可用空间。

四、布局编辑（拖动位置/缩放）
· 单张页右下角、批量页工具栏都有“布局编辑…”。
· 拖动文字/一维码/二维码的边框可移动；拖动四角的白色方块可缩放。
· 二维码始终保持正方形；文字框缩小会自动换行/缩小字号。
· “应用布局”后，预览、打印和保存 ZPL 都使用新位置/尺寸；“重置自动布局”恢复。
· 切换标签规格、版式，或勾选/取消一维码/二维码时，会回到自动布局。
· 布局编辑结果只对本次运行有效；想长期保留，请点页头“模版 → 将当前页面保存为模版…”。

五、模版（保存 / 打开）
· 点页头右上角“模版 ▾”，选择“将当前页面保存为模版…”，给模版起个名字即可。
· 模版会记住：标签规格、版式、字体/字号/加粗、四边距、条码高度%、二维码%、布局编辑的位置尺寸，以及当前文字/条码内容。
· 再次点“模版 ▾”，最近 8 个模版会直接出现在菜单里，点一下就能恢复；也可选“打开模版…”进入模版库（左侧列表、右侧预览）。
· 模版库支持重命名、删除、导入文件、导出文件；导出的 .elbl 可发给同事，对方用“导入文件…”加入。
· 需要直接发给打印机或交给其它 ZPL 工具时，选“将当前页面保存为 ZPL 模版…”，生成 .zpl 文件；文件里带着模版信息，之后还能打开继续改。
· 外部拿到的 .zpl 没有内嵌信息，模版库会标成 ZPL：右侧显示指令内容，按钮变成“打印此 ZPL”，只能直发，不能改版式。
· 在批量打印页打开模版只套用版式，不会动表格里已导入的数据；示例内容仅用于预览。
· 模版保存在：" + System.IO.Path.Combine(AppSettings.DataDirectory, "templates") + @"

六、Excel / CSV 批量
· 支持 .xlsx、.xls、.xlsm、.csv。
· 导入时可选择工作表和“首行为标题行”，并映射文本、一维码、二维码三列。
· 条码列建议在 Excel 中设置为“文本”，否则 000123 可能变成 123。
· 数字格式为 000000 的单元格，程序会尝试保留前导零。
· 每行默认打印一张；勾选“仅勾选行”后只打印表格中勾选的行。
· 条码/二维码为空时，勾选“空值时用文本补齐”即可自动用文本生成。
· 导入后可直接在表格中修改文本/条码/二维码，再打印。

七、条码规则
· Code 128：支持英文、数字、常用 ASCII 符号；不支持中文/全角字符。
· Code 39：支持 A-Z、0-9 和 - . 空格 $ / + %，程序会自动转大写。
· EAN-13：输入 12 位数字自动补校验位，或输入 13 位并校验。
· QR：支持中文、网址、换行；内容越长，模块越小，40×15 上建议尽量短。
· 一维码/二维码如果放不下，程序会明确提示，不会悄悄截断。

八、常见问题
1) 点击打印没反应/报“无法打开打印机”
   · 检查 Windows 队列名称是否存在、是否离线。
   · 可先在“打印机设置”打印测试页，或保存 ZPL 文件用其他 ZPL 工具发送。
2) 打印出来是空白/乱码
   · 确认队列使用打印机制造商提供的 ZPL 驱动，且打印机支持 ZPL II。
   · 本程序发送的是 RAW ZPL，不适用仅支持 EPL 或纯图形驱动的队列。
3) 标签走纸不准/跳过标签
   · 在打印机面板执行介质校准（按键组合见打印机手册），确认间隙传感器类型。
   · 本程序默认 ^MNY（间隙纸）；连续纸请在设置中切换。
4) 条码扫描不出来
   · 提高打印分辨率、减小内容长度，或降低打印速度/调整浓度。
   · 40×15 的“全部”版式空间很紧，建议改用 30×25 / 30×20 或只保留一种码。
5) 中文变成方框
   · 本程序把中文栅格化后发送，正常不会依赖打印机字体；如仍异常，请确认使用的是本程序生成的 ZPL，而不是驱动重新排版。

九、安装版与在线更新
· 安装版：运行 EasyLabel-*-Setup.exe，按提示选择安装目录即可；会创建开始菜单快捷方式和卸载项，
  程序与数据分开存放，设置/模版在 %APPDATA%\EasyLabel，卸载时默认保留。
· 绿色版：解压即用，程序目录有 portable.flag，设置与日志写在 data 子目录。
· 在线更新：点左下角版本号（或“关于 → 检查更新”）可手动检查；启动后也会每天自动查一次。
  更新包下载后会校验 SHA-256，再替换程序文件并自动重启，打印机设置与模版不受影响。
· 更新地址：默认指向项目发布页；企业内网可在“打印机设置 → 软件更新”改成自建的 update.json 地址，
  留空即恢复默认。检查更新只读取版本信息，不上传任何数据。
· 手动更新：也可以直接下载新的安装包覆盖安装，或解压绿色包替换 exe。

十、数据与隐私
· 不含遥测、埋点或使用统计；标签内容与导入的表格数据只在本机处理。
· 检查更新时会向更新地址请求 update.json（仅版本信息），下载更新包时同样只访问该地址。
· 使用“网络 9100”直连打印时，ZPL 数据仅发送到您指定的打印机地址。
· 日志只记录时间、打印机、标签数量和错误信息，不记录标签正文。
· 设置保存在：" + AppSettings.SettingsPath + @"

十一、开源组件
· ZXing.Net 0.16.11（Apache-2.0）：一维码/QR 生成与识读。
· ExcelDataReader 3.9.0（MIT）：读取 .xls/.xlsx。
详细许可见程序目录下的 THIRD-PARTY-NOTICES.txt，或程序内“关于”页。

十二、版权
" + AppInfo.Copyright + @"
作者：yuguod    项目地址：https://github.com/yuguod/EasyLabel
未经授权不得以本程序名义发布修改版本；第三方组件版权归其各自作者所有。";
        }
    }
}
