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
    $resolved = [IO.Path]::GetFullPath($appDir)
    if (!$resolved.StartsWith([IO.Path]::GetFullPath($root), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove outside project: $resolved"
    }
    Remove-Item $appDir -Recurse -Force
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

$zip = Join-Path $dist 'EasyLabel-1.5.0-win64.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Write-Host 'Package...'
Compress-Archive -Path (Join-Path $appDir '*') -DestinationPath $zip -CompressionLevel Optimal
$releases = Join-Path $root 'releases'
New-Item -ItemType Directory -Force -Path $releases | Out-Null
Copy-Item $zip (Join-Path $releases (Split-Path $zip -Leaf)) -Force
Write-Host "Built: $appOut"
Write-Host "Package: $zip"
Write-Host "Archived: $(Join-Path $releases (Split-Path $zip -Leaf))"
