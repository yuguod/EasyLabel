param(
    [switch]$Tests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = Join-Path $root 'tools\roslyn\csc.exe'
$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path $fw)) { $fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
$netstandard = Join-Path $fw 'netstandard.dll'
$libs = Join-Path $root 'libs'
$dist = Join-Path $root 'dist'
$appDir = Join-Path $dist 'EasyLabel'
$coreDll = Join-Path $dist 'EasyLabel.Core.dll'

if (Test-Path $appDir) {
    # 原地重建：不删除旧输出目录，构建时直接覆盖同名文件。
    # 原因：受限环境（如 WorkBuddy 沙箱）会拦截几十个文件的递归删除与目录改名，
    # 而 dist 目录本就是可再生的中间产物。需要一份全新的输出时，手动删除
    # dist\EasyLabel 后重新运行本脚本即可。
    Write-Host "复用已有输出目录：$appDir（原地覆盖）"
}
New-Item -ItemType Directory -Force -Path $dist, $appDir | Out-Null

$commonRefs = @(
    "/r:$(Join-Path $fw 'System.Drawing.dll')",
    "/r:$(Join-Path $fw 'System.Xml.dll')",
    "/r:$(Join-Path $fw 'System.Xml.Linq.dll')",
    "/r:$(Join-Path $fw 'System.Core.dll')",
    "/r:$(Join-Path $fw 'System.Windows.Forms.dll')",
    "/r:$(Join-Path $fw 'Microsoft.VisualBasic.dll')",
    "/r:$netstandard",
    "/r:$(Join-Path $libs 'zxing.dll')",
    "/r:$(Join-Path $libs 'ExcelDataReader.dll')"
)

$coreSources = Get-ChildItem (Join-Path $root 'src\Core') -Filter '*.cs' | ForEach-Object { $_.FullName }
$appSources = Get-ChildItem (Join-Path $root 'src') -Recurse -Filter '*.cs' | ForEach-Object { $_.FullName }

Write-Host 'Compile core...'
& $csc /nologo /target:library /langversion:latest /optimize+ /out:$coreDll @commonRefs $coreSources
if ($LASTEXITCODE -ne 0) { throw 'Core build failed' }

Write-Host 'Compile application...'
$resources = @(
    "/resource:$(Join-Path $libs 'zxing.dll'),EasyLabel.zxing.dll",
    "/resource:$(Join-Path $libs 'ExcelDataReader.dll'),EasyLabel.ExcelDataReader.dll"
)
$appOut = Join-Path $appDir 'EasyLabel.exe'
$icon = Join-Path $root 'assets\app.ico'
$iconArgs = @()
if (Test-Path $icon) { $iconArgs = @("/win32icon:$icon") }
& $csc /nologo /target:winexe /langversion:latest /optimize+ /out:$appOut `
    /win32manifest:"$(Join-Path $root 'src\app.manifest')" `
    @iconArgs /debug:pdbonly /pdb:"$(Join-Path $dist 'EasyLabel.pdb')" @commonRefs @resources $appSources
if ($LASTEXITCODE -ne 0) { throw 'Application build failed' }

Copy-Item (Join-Path $root 'src\app.config') (Join-Path $appDir 'EasyLabel.exe.config') -Force
Set-Content -Path (Join-Path $appDir 'portable.flag') -Value 'portable' -Encoding ASCII
New-Item -ItemType Directory -Force -Path (Join-Path $appDir 'samples') | Out-Null
Copy-Item (Join-Path $root 'samples\*') (Join-Path $appDir 'samples') -Recurse -Force
Copy-Item (Join-Path $root 'README.md') $appDir -Force
Copy-Item (Join-Path $root 'LICENSE') $appDir -Force
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.txt') $appDir -Force
if (Test-Path (Join-Path $root 'LICENSES')) {
    Copy-Item (Join-Path $root 'LICENSES') $appDir -Recurse -Force
}
if (Test-Path (Join-Path $root 'docs')) {
    Copy-Item (Join-Path $root 'docs') $appDir -Recurse -Force
}
Get-ChildItem $appDir -Filter '*.pdb' -File | ForEach-Object { Remove-Item $_.FullName -Force }

if ($Tests) {
    Write-Host 'Compile tests...'
    $testDir = Join-Path $root 'tests'
    Copy-Item $coreDll $testDir -Force
    Copy-Item (Join-Path $libs 'zxing.dll') $testDir -Force
    Copy-Item (Join-Path $libs 'ExcelDataReader.dll') $testDir -Force
    New-Item -ItemType Directory -Force -Path (Join-Path $testDir 'samples') | Out-Null
    Copy-Item (Join-Path $root 'samples\sample-data.xlsx') (Join-Path $testDir 'samples\sample-data.xlsx') -Force
    $testOut = Join-Path $testDir 'TestRunner.exe'
    & $csc /nologo /target:exe /langversion:latest /out:$testOut @commonRefs `
        "/r:$coreDll" (Join-Path $testDir 'TestRunner.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed' }
}

$zip = Join-Path $dist 'EasyLabel-1.5.1-win64.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }

# 便携模式下运行程序会在输出目录生成 data（设置与日志）。打包必须排除，
# 否则会把本机的打印机名、路径和日志带进发布包。
$dataDir = Join-Path $appDir 'data'
if (Test-Path $dataDir) {
    try {
        Remove-Item $dataDir -Recurse -Force -ErrorAction Stop
        Write-Host '打包前已排除运行期数据目录 data'
    }
    catch {
        Write-Warning "无法删除 $dataDir（程序可能正在运行），请手动清理后重新打包"
    }
}

Write-Host 'Package...'
# 显式列出发布包内容，避免把构建残留（如 {guid}_EasyLabel.exe）或运行期数据带进包。
$packageItems = @(
    (Join-Path $appDir 'EasyLabel.exe'),
    (Join-Path $appDir 'EasyLabel.exe.config'),
    (Join-Path $appDir 'portable.flag'),
    (Join-Path $appDir 'LICENSE'),
    (Join-Path $appDir 'README.md'),
    (Join-Path $appDir 'THIRD-PARTY-NOTICES.txt'),
    (Join-Path $appDir 'LICENSES'),
    (Join-Path $appDir 'docs'),
    (Join-Path $appDir 'samples')
) | Where-Object { Test-Path $_ }
Compress-Archive -Path $packageItems -DestinationPath $zip -CompressionLevel Optimal
$releases = Join-Path $root 'releases'
New-Item -ItemType Directory -Force -Path $releases | Out-Null
Copy-Item $zip (Join-Path $releases (Split-Path $zip -Leaf)) -Force
Write-Host "Built: $appOut"
Write-Host "Package: $zip"
Write-Host "Archived: $(Join-Path $releases (Split-Path $zip -Leaf))"
