param(
    [string]$AppDir = "",
    [string]$OutDir = ""
)

# 界面冒烟：不开真实窗口，把窗体直接画进 Bitmap 存成 PNG，用来检查布局有没有重叠/裁切。
# 用法：powershell -STA -File tools\ui-smoke.ps1 [-AppDir dist\EasyLabel] [-OutDir dist\ui-smoke]
# 说明：程序把 zxing 等依赖嵌在 exe 里、在 Main 中注册解析器；这里只反射加载 exe，
#       所以要自己从 libs\ 兜住依赖，否则模版预览会显示“预览失败”。

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if (-not $AppDir) { $AppDir = Join-Path $root 'dist\EasyLabel' }
if (-not $OutDir) { $OutDir = Join-Path $root 'dist\ui-smoke' }
if (-not (Test-Path (Join-Path $AppDir 'EasyLabel.exe'))) {
    Write-Host "[失败] 找不到 $AppDir\EasyLabel.exe，先跑 build.ps1" -ForegroundColor Red
    exit 1
}
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

$libs = Join-Path $root 'libs'
[AppDomain]::CurrentDomain.add_AssemblyResolve([ResolveEventHandler]{
    param($sender, $e)
    $name = (New-Object System.Reflection.AssemblyName($e.Name)).Name
    $candidate = Join-Path $libs ($name + '.dll')
    if (Test-Path $candidate) { return [System.Reflection.Assembly]::LoadFrom($candidate) }
    return $null
})

[Reflection.Assembly]::LoadFrom((Join-Path $AppDir 'EasyLabel.exe')) | Out-Null

function Shot($form, $file) {
    $form.Show()
    [System.Windows.Forms.Application]::DoEvents()
    Start-Sleep -Milliseconds 300
    [System.Windows.Forms.Application]::DoEvents()
    $bmp = New-Object System.Drawing.Bitmap($form.Width, $form.Height)
    $form.DrawToBitmap($bmp, (New-Object System.Drawing.Rectangle(0, 0, $form.Width, $form.Height)))
    $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

$tmp = Join-Path $env:TEMP ("elbl-smoke-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tmp -Force | Out-Null
$store = New-Object EasyLabel.Core.TemplateStore($tmp)

# 造一个临时模版：有文字、有 Code 128，份数 2，用来验证预览与份数回填。
$t = New-Object EasyLabel.Core.LabelTemplate
$t.Name = "冒烟测试 40x25"
$t.WidthMm = 40
$t.HeightMm = 25
$t.Text = "冒烟测试 SMOKE-01"
$t.ShowBarcode = $true
$t.BarcodeValue = "S-2026-001"
$t.ShowBarcodeText = $true
$t.Copies = 2
$t.FontSizeMm = 2.5
$templatePath = $store.Save($t)

$settings = [EasyLabel.Core.AppSettings]::Load()

$dlg = New-Object EasyLabel.UI.ImportPrintDialog($templatePath, $t, $settings)
Shot $dlg (Join-Path $OutDir 'import-elbl.png')
Write-Host ("导入 .elbl：" + $dlg.Width + "x" + $dlg.Height + "  份数=" + $dlg.Copies)
$dlg.Close(); $dlg.Dispose()

$zplPath = Join-Path $AppDir 'samples\demo-40x15.zpl'
$zplTemplate = $store.LoadZpl($zplPath)
$dlg2 = New-Object EasyLabel.UI.ImportPrintDialog($zplPath, $zplTemplate, $settings)
Shot $dlg2 (Join-Path $OutDir 'import-zpl.png')
Write-Host ("导入 .zpl ：" + $dlg2.Width + "x" + $dlg2.Height + "  份数=" + $dlg2.Copies)
$dlg2.Close(); $dlg2.Dispose()

# 版式布局编辑：安全区、网格、右侧数值/对齐面板。
$spec = [EasyLabel.Core.LabelSpec]::Label40x15
$content = New-Object EasyLabel.Core.LabelContent
$content.Text = "版式测试 LAYOUT-01"
$content.ShowBarcode = $true
$content.BarcodeValue = "A-2026-001"
$content.ShowQr = $true
$content.QrValue = "https://example.com/layout"
$opts = New-Object EasyLabel.Core.RenderOptions
$opts.Dpi = 203
$opts.LayoutMode = [EasyLabel.Core.LayoutMode]::All
$editor = New-Object EasyLabel.UI.LayoutEditorDialog($spec, $content, $opts)
Shot $editor (Join-Path $OutDir 'layout-editor.png')
Write-Host ("布局编辑：" + $editor.Width + "x" + $editor.Height)
$editor.Close(); $editor.Dispose()

# 越界布局：元素拖到标签外时应该有红色斜纹 + 画布四周留出 8 mm 可拖区。
$planOut = New-Object EasyLabel.Core.LayoutPlan
$planOut.Text = New-Object System.Drawing.RectangleF(-4, 1, 40, 6)
$opts2 = New-Object EasyLabel.Core.RenderOptions
$opts2.Dpi = 203
$opts2.LayoutMode = [EasyLabel.Core.LayoutMode]::TextBarcode
$opts2.LayoutPlan = $planOut
$content2 = New-Object EasyLabel.Core.LabelContent
$content2.Text = "越界测试 OFFSET"
$content2.ShowBarcode = $true
$content2.BarcodeValue = "B-2026-002"
$editor2 = New-Object EasyLabel.UI.LayoutEditorDialog($spec, $content2, $opts2)
Shot $editor2 (Join-Path $OutDir 'layout-editor-overflow.png')
Write-Host ("越界布局：" + $editor2.Width + "x" + $editor2.Height)
$editor2.Close(); $editor2.Dispose()

# 打印机设置页：检查“打印偏移补偿”卡片的排版有没有被裁切。
$main = [Activator]::CreateInstance([EasyLabel.UI.MainForm], @(0, $false, $false))
$pagesField = [EasyLabel.UI.MainForm].GetField('_pages', [System.Reflection.BindingFlags]'Instance,NonPublic')
$pages = $pagesField.GetValue($main)
$hostForm = New-Object System.Windows.Forms.Form
$hostForm.ClientSize = New-Object System.Drawing.Size(1000, 1180)
$settingsPage = $pages[2]
$settingsPage.Visible = $true
$settingsPage.Dock = [System.Windows.Forms.DockStyle]::Fill
$hostForm.Controls.Add($settingsPage)
$settingsPage.PerformLayout()
Shot $hostForm (Join-Path $OutDir 'settings-page.png')
Write-Host ("设置页：" + $hostForm.Width + "x" + $hostForm.Height)
$hostForm.Controls.Remove($settingsPage)
$hostForm.Dispose()
$main.Dispose()

Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
Write-Host ("截图已保存：" + $OutDir) -ForegroundColor Green
