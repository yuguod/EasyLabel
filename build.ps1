param(
    [switch]$Tests,
    [string]$Version,
    [string]$DownloadUrl,
    # 输出目录，默认 dist。CI 或想保留一份正式产物时可用 -OutDir 指定别处，
    # 避免与原目录里正在运行的程序冲突。
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = Join-Path $root 'tools\roslyn\csc.exe'
$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path $fw)) { $fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
$netstandard = Join-Path $fw 'netstandard.dll'
$libs = Join-Path $root 'libs'
$dist = if ($OutDir) { [System.IO.Path]::GetFullPath((Join-Path $root $OutDir)) } else { Join-Path $root 'dist' }
$appDir = Join-Path $dist 'EasyLabel'
$coreDll = Join-Path $dist 'EasyLabel.Core.dll'
$objDir = Join-Path $dist 'obj'
$installerDir = Join-Path $root 'installer'

# ------------------------------------------------------------------ 版本号
# 版本号只在 src\AssemblyInfo.cs 里维护，这里读出来用于包名、安装程序与更新清单。
$assemblyInfo = Get-Content (Join-Path $root 'src\AssemblyInfo.cs') -Raw
if (-not $Version) {
    $match = [regex]::Match($assemblyInfo, 'AssemblyVersion\("([0-9]+(?:\.[0-9]+){1,3})"\)')
    if (-not $match.Success) { throw 'AssemblyInfo.cs 里找不到 AssemblyVersion' }
    $Version = $match.Groups[1].Value
}
$parts = $Version.Split('.')
if ($parts.Count -ge 3) { $Version3 = ($parts[0..2] -join '.') } else { $Version3 = $Version }
if (-not $DownloadUrl) {
    $DownloadUrl = "https://github.com/yuguod/EasyLabel/releases/download/v$Version3/EasyLabel-$Version3-update.zip"
}
Write-Host "版本：$Version3"

if (Test-Path $appDir) {
    # 原地重建：不删除旧输出目录，构建时直接覆盖同名文件。
    # 原因：受限环境（如 WorkBuddy 沙箱）会拦截几十个文件的递归删除与目录改名，
    # 而 dist 目录本就是可再生的中间产物。需要一份全新的输出时，手动删除
    # dist\EasyLabel 后重新运行本脚本即可。
    Write-Host "复用已有输出目录：$appDir（原地覆盖）"
}
New-Item -ItemType Directory -Force -Path $dist, $appDir, $objDir | Out-Null

$commonRefs = @(
    "/r:$(Join-Path $fw 'System.Drawing.dll')",
    "/r:$(Join-Path $fw 'System.Xml.dll')",
    "/r:$(Join-Path $fw 'System.Xml.Linq.dll')",
    "/r:$(Join-Path $fw 'System.Core.dll')",
    "/r:$(Join-Path $fw 'System.Windows.Forms.dll')",
    "/r:$(Join-Path $fw 'Microsoft.VisualBasic.dll')",
    "/r:$(Join-Path $fw 'System.Web.Extensions.dll')",
    "/r:$(Join-Path $fw 'System.IO.Compression.dll')",
    "/r:$(Join-Path $fw 'System.IO.Compression.FileSystem.dll')",
    "/r:$netstandard",
    "/r:$(Join-Path $libs 'zxing.dll')",
    "/r:$(Join-Path $libs 'ExcelDataReader.dll')"
)

$coreSources = Get-ChildItem (Join-Path $root 'src\Core') -Filter '*.cs' | ForEach-Object { $_.FullName }
# 主程序不包含 Updater（那是独立进程，单独编译）
$appSources = Get-ChildItem (Join-Path $root 'src') -Recurse -Filter '*.cs' |
    Where-Object { $_.FullName -notlike "*\src\Updater\*" } |
    ForEach-Object { $_.FullName }

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

Write-Host 'Compile updater...'
# 在线更新的替换程序：独立 exe，随程序一起分发。正在运行的 exe 无法自替换，必须由它接手。
$updaterOut = Join-Path $appDir 'EasyLabelUpdater.exe'
& $csc /nologo /target:winexe /langversion:latest /optimize+ /out:$updaterOut `
    /r:$(Join-Path $fw 'System.dll') `
    /r:$(Join-Path $fw 'System.Drawing.dll') `
    /r:$(Join-Path $fw 'System.Windows.Forms.dll') `
    (Join-Path $root 'src\Updater\Updater.cs')
if ($LASTEXITCODE -ne 0) { throw 'Updater build failed' }

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

# 显式列出发布包内容，避免把构建残留或运行期数据带进包。
$packageItems = @(
    (Join-Path $appDir 'EasyLabel.exe'),
    (Join-Path $appDir 'EasyLabel.exe.config'),
    (Join-Path $appDir 'EasyLabelUpdater.exe'),
    (Join-Path $appDir 'LICENSE'),
    (Join-Path $appDir 'README.md'),
    (Join-Path $appDir 'THIRD-PARTY-NOTICES.txt'),
    (Join-Path $appDir 'LICENSES'),
    (Join-Path $appDir 'docs'),
    (Join-Path $appDir 'samples')
) | Where-Object { Test-Path $_ }

# ---------------------------------------------------------------- 绿色包（含 portable.flag）
$zip = Join-Path $dist "EasyLabel-$Version3-win64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Write-Host 'Package portable zip...'
Compress-Archive -Path ($packageItems + (Join-Path $appDir 'portable.flag')) -DestinationPath $zip -CompressionLevel Optimal

# ---------------------------------------------------------------- 更新包（不含 portable.flag）
# 在线更新只拿这一份：安装版不能因为包里带 portable.flag 而“变回”绿色版，
# 便携版的运行期数据也不该被覆盖。
$updateZip = Join-Path $dist "EasyLabel-$Version3-update.zip"
if (Test-Path $updateZip) { Remove-Item $updateZip -Force }
Write-Host 'Package update zip...'
Compress-Archive -Path $packageItems -DestinationPath $updateZip -CompressionLevel Optimal

# ---------------------------------------------------------------- 安装程序
Write-Host 'Compile installer...'
# 安装程序的版本、产品名由构建脚本生成，避免两处维护。
$setupInfoPath = Join-Path $objDir 'SetupInfo.cs'
$setupInfoSource = @"
namespace EasyLabel.Setup
{
    internal static class SetupInfo
    {
        public const string Version = "$Version3";
        public const string ProductName = "EasyLabel 标签轻松打";
        public const string Publisher = "yuguod";
        public const string ProjectUrl = "https://github.com/yuguod/EasyLabel";
    }
}
"@
Set-Content -Path $setupInfoPath -Value $setupInfoSource -Encoding UTF8

$setupOut = Join-Path $dist "EasyLabel-$Version3-Setup.exe"
& $csc /nologo /target:winexe /langversion:latest /optimize+ /out:$setupOut `
    /win32manifest:"$(Join-Path $installerDir 'setup.manifest')" `
    @iconArgs `
    "/resource:$updateZip,payload.zip" `
    /r:$(Join-Path $fw 'System.dll') `
    /r:$(Join-Path $fw 'System.Drawing.dll') `
    /r:$(Join-Path $fw 'System.Windows.Forms.dll') `
    /r:$(Join-Path $fw 'System.IO.Compression.dll') `
    /r:$(Join-Path $fw 'System.IO.Compression.FileSystem.dll') `
    (Join-Path $installerDir 'Setup.cs') $setupInfoPath
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed' }

# ---------------------------------------------------------------- 更新清单
Write-Host 'Write update.json...'
$hash = (Get-FileHash $updateZip -Algorithm SHA256).Hash.ToLower()
$size = (Get-Item $updateZip).Length
$notes = ''
$releaseDate = (Get-Date).ToString('yyyy-MM-dd')
$sourceManifest = Join-Path $root 'update.json'
if (Test-Path $sourceManifest) {
    try {
        $source = Get-Content $sourceManifest -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($source.notes) { $notes = $source.notes }
        if ($source.releaseDate) { $releaseDate = $source.releaseDate }
    }
    catch {
        Write-Warning "无法解析根目录 update.json，更新说明留空：$($_.Exception.Message)"
    }
}
$manifest = [ordered]@{
    version     = $Version3
    releaseDate = $releaseDate
    url         = $DownloadUrl
    sha256      = $hash
    size        = $size
    mandatory   = $false
    notes       = $notes
}
$manifestPath = Join-Path $dist 'update.json'
$manifestJson = $manifest | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText($manifestPath, $manifestJson, (New-Object System.Text.UTF8Encoding($false)))

# ---------------------------------------------------------------- 归档
$releases = Join-Path $root 'releases'
if (-not $OutDir) {
    New-Item -ItemType Directory -Force -Path $releases | Out-Null
    Copy-Item $zip (Join-Path $releases (Split-Path $zip -Leaf)) -Force
    Copy-Item $updateZip (Join-Path $releases (Split-Path $updateZip -Leaf)) -Force
    Copy-Item $setupOut (Join-Path $releases (Split-Path $setupOut -Leaf)) -Force
    Copy-Item $manifestPath (Join-Path $releases 'update.json') -Force
}
else {
    Write-Host '指定了 -OutDir，跳过 releases 归档'
}

Write-Host ''
Write-Host "Built:      $appOut"
Write-Host "Updater:    $updaterOut"
Write-Host "Portable:   $zip"
Write-Host "Update zip: $updateZip"
Write-Host "Setup:      $setupOut"
Write-Host "Manifest:   $manifestPath"
Write-Host "Archived:   $releases"
