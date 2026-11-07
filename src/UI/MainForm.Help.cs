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
· 文字对齐：单张页用三段按钮切换居左/居中/居右，批量页用下拉；只决定文字在文字框内的水平位置，一维码下方明文始终居中以保证扫描。
· 系统里没装的字体不用装进 Windows：到“打印机设置 → 维护与关于 → 字体管理…”点“添加字体…”，可一次导入多个 .ttf / .otf / .ttc，导入后立刻出现在字体下拉里。删除只影响本程序；字体文件存放在数据目录的 Fonts 子目录，随数据目录备份即可迁移。
· 中文标签推荐 Microsoft YaHei UI / Microsoft YaHei / SimHei / SimSun。
· 加粗只作用于标签正文；一维码下方明文保持常规字重，避免影响扫描。
· 上边距、左边距范围 −20–20 mm，下边距、右边距 0–20 mm；边距过大时预览会提示无可用空间。
· 上/左边距填负数表示“纯平移”：内容整体上移/左移，可用尺寸不变，超出标签的部分会被裁掉。
· 边距同时决定“安全区”：自定义布局下改边距，整块版式会跟着内缩/外扩。

三之二、打印偏移补偿（打印机固定偏位）
· 位置在“打印机设置 → 打印偏移补偿”：X 偏移、Y 偏移，范围 −20–20 mm，精度 0.1 mm。
· 正数往右/往下，负数往左/往上；整幅内容一起平移，预览里能直接看到结果。
· 用于抵消机械起始位置、非打印区、标签贴偏等“每台机器都固定”的误差，换机器要重新调。
· 偏移不写进模版，也不会影响别人打开你的 .elbl。

四、布局编辑（拖动位置/缩放）
· 单张页右下角、批量页工具栏都有“布局编辑…”，预览区右键也能进。
· 画布上用虚线框出“安全区”（标签去掉四边距的区域），外面浅色那圈就是边距；网格间距可选 0.5/1/2/5 mm。
· 拖动文字/一维码/二维码的边框可移动；拖动四角的白色方块可缩放；靠近网格、安全区边缘和中线会自动吸附。
· 方向键微调 0.1 mm，Shift + 方向键 1 mm；Ctrl+Z 撤销，Ctrl+Y 重做；回车保存并关闭，Esc 取消。
· 窗口显示时按屏幕可用区域自适应，小屏或高缩放下“应用布局”按钮也不会被顶到屏幕外面。
· 右侧可精确输入 X/Y/宽/高，或用“贴左/水平居中/贴右、贴上/垂直居中/贴下、铺满安全区”一键摆位。
· 右侧还能就地改四个边距，安全区会实时变化；点“应用布局”后边距回写到页面。
· 自定义布局的坐标以安全区左上角为原点，所以改边距时整块版式会跟着内缩/外扩，边距在自定义布局下同样有效。
· 元素可以拖到标签外面（最多 8 mm）：越界部分显示红色斜纹，打印时会被裁掉；这是手动补偿打印机偏移的办法。
· 边距、布局拖动、打印偏移补偿三者都是在挪内容，不要重复叠加，否则会补偿过量。
· “应用布局”后，预览、打印和保存 ZPL 都使用新位置/尺寸；“重置为自动布局”或预览右键“恢复自动布局”可恢复。
· 切换标签规格、版式，或勾选/取消一维码/二维码时，会回到自动布局。
· 布局编辑结果只对本次运行有效；想长期保留，请点页头“模版 → 将当前页面保存为模版…”。

五、模版（保存 / 打开）
· 点页头右上角“模版 ▾”，选择“将当前页面保存为模版…”，给模版起个名字即可。
· 模版会记住：标签规格、版式、字体/字号/加粗、四边距、条码高度%、二维码%、布局编辑的位置尺寸，以及当前文字/条码内容。
· 再次点“模版 ▾”，最近 8 个模版会直接出现在菜单里，点一下就能恢复；也可选“打开模版…”进入模版库（左侧列表、右侧预览）。
· 模版库支持重命名、删除、导入文件、导出文件；导出的 .elbl 可发给同事，对方用“导入文件…”加入。
· 需要直接发给打印机或交给其它 ZPL 工具时，选“将当前页面保存为 ZPL 模版…”，生成 .zpl 文件；文件里带着模版信息，之后还能打开继续改。
· 外部拿到的 .zpl 没有内嵌信息，模版库会标成 ZPL：右侧显示指令内容，按钮变成“打印此 ZPL”，只能直发，不能改版式。
· 只想拿文件直接出纸：点页头“导入打印”，选 .elbl 或 .zpl 即可；先看预览/指令，再决定打几份，不用先进模版库。
· 导入窗口里可以勾选“打印后加入模版库”；.elbl 还能点“打开编辑”载入单张页继续改内容。
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
· 更新地址：默认直接读项目在 GitHub 的 Release 发布页（可在“打印机设置 → 软件更新”查看），
  也可以填仓库主页、某个 Release 页面，或内网自建的 update.json 地址；留空即恢复默认。
  检查更新只读取版本信息，不上传任何数据。
· 手动更新：也可以直接下载新的安装包覆盖安装，或解压绿色包替换 exe。

十、数据与隐私
· 不含遥测、埋点或使用统计；标签内容与导入的表格数据只在本机处理。
· 检查更新时会向更新地址请求发布信息（版本号、包大小、校验值），下载更新包时同样只访问该地址。
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
