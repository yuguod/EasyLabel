param(
    [string]$Version = '',
    [string]$NotesFile = '',
    [switch]$Draft,
    [switch]$DryRun,
    [switch]$SkipPush
)

# 发布脚本：打标签 → 推送到 GitHub → 建 Release 并上传 4 个附件。
# 依赖 GitHub CLI（gh），认证一次即可：gh auth login
# 用法：
#   .\publish.ps1                 发布当前 AssemblyInfo 里的版本号
#   .\publish.ps1 -DryRun         只做检查并打印将要执行的命令
#   .\publish.ps1 -Draft          发成草稿
#   .\publish.ps1 -NotesFile docs\v1.7.0.md   用独立文件作为更新说明

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

# git：优先 PATH，其次 WorkBuddy 自带的 PortableGit。gh 也要求 git 在 PATH 里，
# 所以找到后把所在目录前插到本进程的 PATH（只影响本次运行）。
$Git = (Get-Command git -ErrorAction SilentlyContinue).Source
if (-not $Git) {
    $candidates = @()
    $portableRoot = Join-Path $env:USERPROFILE '.workbuddy\binaries\PortableGit'
    if (Test-Path $portableRoot) {
        $candidates += @(Get-ChildItem (Join-Path $portableRoot 'versions') -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName 'cmd\git.exe' })
    }
    $candidates += (Join-Path $env:LOCALAPPDATA 'Programs\Git\cmd\git.exe')
    $candidates += 'C:\Program Files\Git\cmd\git.exe'
    foreach ($c in $candidates) {
        if ($c -and (Test-Path $c)) { $Git = $c; break }
    }
}
if (-not $Git) {
    Write-Host '[失败] 找不到 git，请先安装 Git for Windows' -ForegroundColor Red
    exit 1
}
$env:Path = (Split-Path $Git -Parent) + ';' + $env:Path

function Step([string]$text) {
    Write-Host ("`n==> " + $text) -ForegroundColor Cyan
}
function Fail([string]$text) {
    Write-Host "[失败] $text" -ForegroundColor Red
    exit 1
}
function Warn([string]$text) {
    Write-Host "[注意] $text" -ForegroundColor Yellow
}
function Run([string]$exe, [string[]]$exeArgs) {
    $flat = ($exeArgs | ForEach-Object { if ($_ -match '\s') { "`"$_`"" } else { $_ } }) -join ' '
    if ($DryRun) {
        Write-Host "  [dry-run] $exe $flat" -ForegroundColor DarkGray
        return 0
    }
    Write-Host "  $exe $flat" -ForegroundColor DarkGray
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $exe @exeArgs
        return $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $old }
}

# 静默执行并返回退出码，避免原生命令的 stderr 在 Stop 模式下被当成异常
function Quiet([string]$exe, [string[]]$exeArgs) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $exe @exeArgs 2>&1 | Out-Null
        return $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $old }
}

# ---------------------------------------------------------------- 版本号
if (-not $Version) {
    $infoPath = Join-Path $root 'src\AssemblyInfo.cs'
    if (-not (Test-Path $infoPath)) { Fail "找不到 $infoPath" }
    $info = Get-Content $infoPath -Raw -Encoding UTF8
    if ($info -match 'AssemblyVersion\("(\d+\.\d+\.\d+)') { $Version = $Matches[1] }
}
if (-not $Version) { Fail '无法确定版本号，请用 -Version 指定' }
$tag = "v$Version"

Step "版本 $Version，标签 $tag"
if ($DryRun) { Write-Host '  （dry-run：只检查，不改动远端）' -ForegroundColor DarkGray }

# ---------------------------------------------------------------- 产物检查
Step '检查构建产物'
$releases = Join-Path $root 'releases'
$manifestPath = Join-Path $root 'update.json'
$zipUpdate = Join-Path $releases "EasyLabel-$Version-update.zip"
$assets = @(
    (Join-Path $releases "EasyLabel-$Version-Setup.exe"),
    (Join-Path $releases "EasyLabel-$Version-win64.zip"),
    $zipUpdate,
    $manifestPath
)
foreach ($a in $assets) {
    if (-not (Test-Path $a)) { Fail "缺少产物：$a`n先执行 .\build.ps1 生成该版本" }
    Write-Host ("  {0,-34} {1,10:N0} 字节" -f (Split-Path $a -Leaf), (Get-Item $a).Length)
}

# ---------------------------------------------------------------- 清单一致性
Step '校验 update.json 与更新包'
$manifest = Get-Content $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.version -ne $Version) {
    Fail "update.json 里的版本是 $($manifest.version)，与 $Version 不一致；先跑 build.ps1"
}
$hash = (Get-FileHash $zipUpdate -Algorithm SHA256).Hash.ToLower()
if (-not $manifest.sha256) {
    Fail 'update.json 缺少 sha256，客户端将跳过校验；先跑 build.ps1 重新生成清单'
}
if ($manifest.sha256.ToLower() -ne $hash) {
    Fail "update.json 的 sha256 与更新包不符`n  清单：$($manifest.sha256)`n  实际：$hash"
}
if ($manifest.url -notmatch [regex]::Escape("/download/$tag/")) {
    Fail "update.json 的 url 没指向 $tag：$($manifest.url)"
}
Write-Host "  sha256 = $hash" -ForegroundColor DarkGray

# ---------------------------------------------------------------- gh 可用性
Step '检查 GitHub CLI'
$ghCmd = (Get-Command gh -ErrorAction SilentlyContinue)
$Gh = if ($ghCmd) { $ghCmd.Source } else { '' }
if (-not $Gh) {
    $ghCandidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\gh\bin\gh.exe'),
        (Join-Path $env:ProgramFiles 'GitHub CLI\gh.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'GitHub CLI\gh.exe')
    )
    foreach ($c in $ghCandidates) {
        if ($c -and (Test-Path $c)) { $Gh = $c; break }
    }
}
if (-not $Gh) {
    Fail "找不到 gh。安装后重试：`n  winget install --id GitHub.cli`n  或下载便携版（gh_*_windows_amd64.zip）解压到 %LOCALAPPDATA%\Programs\gh"
}
$env:Path = (Split-Path $Gh -Parent) + ';' + $env:Path
Write-Host ("  {0}  ({1})" -f (& $Gh --version | Select-Object -First 1), $Gh)
if ((Quiet $Gh @('auth', 'status', '--hostname', 'github.com')) -ne 0) {
    Fail "gh 未登录，先执行：gh auth login --hostname github.com"
}

# ---------------------------------------------------------------- Git 状态
Step '检查 Git 状态'
$branch = (& $Git rev-parse --abbrev-ref HEAD).Trim()
$head = (& $Git rev-parse HEAD).Trim()
$dirty = @(& $Git status --porcelain)
if ($dirty.Count -gt 0) {
    Warn '工作区有未提交改动，这些改动不会进入本次发布'
}
Write-Host "  分支 $branch  HEAD $($head.Substring(0,7))"

$tagCommit = ''
$old = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
$tagCommit = ((& $Git rev-parse --verify --quiet "refs/tags/$tag^{commit}" 2>&1) -join '').Trim()
$tagCode = $LASTEXITCODE
$ErrorActionPreference = $old
$tagExists = ($tagCode -eq 0 -and $tagCommit -match '^[0-9a-f]{40}$')
if ($tagExists) {
    $tagCommit = $tagCommit.Trim()
    if ($tagCommit -ne $head) {
        Fail "标签 $tag 已存在，指向 $($tagCommit.Substring(0,7))，与当前 HEAD 不一致。`n  确认后执行：git tag -d $tag ; git push origin :refs/tags/$tag"
    }
    Write-Host "  标签 $tag 已存在且指向 HEAD"
}
else {
    Write-Host "  标签 $tag 需要新建"
    $code = Run $Git @('tag', '-a', $tag, '-m', "EasyLabel $tag")
    if ($code -ne 0) { Fail "创建标签失败（exit=$code）" }
}

# ---------------------------------------------------------------- 推送
if (-not $SkipPush) {
    Step '推送提交与标签'
    $code = Run $Git @('push', 'origin', "HEAD:refs/heads/$branch")
    if ($code -ne 0) { Fail "推送分支失败（exit=$code）" }
    $code = Run $Git @('push', 'origin', "refs/tags/$tag")
    if ($code -ne 0) { Fail "推送标签失败（exit=$code）" }
}
else {
    Warn '已指定 -SkipPush，跳过推送'
}

# ---------------------------------------------------------------- 发布说明
Step '生成发布说明'
$notes = $manifest.notes
if ($NotesFile) {
    if (-not (Test-Path $NotesFile)) { Fail "找不到说明文件：$NotesFile" }
    $notes = Get-Content $NotesFile -Raw -Encoding UTF8
}
if (-not $notes) { $notes = "EasyLabel $tag" }

$lines = @()
$lines += '## 本次更新'
$lines += ''
foreach ($line in ($notes -split "`r?`n")) {
    if ($line -match "^\s*$tag\s*$") { continue }
    if ($line -match '^\s*·\s*') { $lines += ('- ' + $line.TrimStart(" $([char]0xB7)").Trim()) }
    else { $lines += $line }
}
$lines += ''
$lines += '## 下载'
$lines += ''
$lines += '| 文件 | 说明 |'
$lines += '| --- | --- |'
$lines += "| ``EasyLabel-$Version-Setup.exe`` | 安装版（推荐），免管理员 |"
$lines += "| ``EasyLabel-$Version-win64.zip`` | 绿色免安装版，解压后双击 ``EasyLabel.exe`` |"
$lines += ('| `EasyLabel-' + $Version + '-update.zip` | 供程序内“在线更新”使用，一般无需手动下载 |')
$lines += '| `update.json` | 更新清单，客户端据此判断新版本 |'
$lines += ''
$lines += "``EasyLabel-$Version-update.zip`` 的 SHA-256：``$hash``"
$bodyText = ($lines -join "`n") + "`n"

$bodyFile = Join-Path $env:TEMP "easylabel-release-$Version.md"
[System.IO.File]::WriteAllText($bodyFile, $bodyText, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "  说明正文 $($bodyText.Length) 字符 → $bodyFile" -ForegroundColor DarkGray

# ---------------------------------------------------------------- 建/更新 Release
$releaseExists = $false
# 只读查询，dry-run 也可以照做，这样预览结果与真实状态一致
if ((Quiet $Gh @('release', 'view', $tag)) -eq 0) { $releaseExists = $true }

$title = "EasyLabel 标签轻松打 $tag"
if (-not $releaseExists) {
    Step "创建 Release $tag"
    $ghArgs = @('release', 'create', $tag, '--title', $title, '--notes-file', $bodyFile)
    if ($Draft) { $ghArgs += '--draft' }
    $ghArgs += $assets
    $code = Run $Gh $ghArgs
    if ($code -ne 0) { Fail "创建 Release 失败（exit=$code）" }
}
else {
    Step "Release $tag 已存在，更新说明并覆盖附件"
    $code = Run $Gh @('release', 'edit', $tag, '--notes-file', $bodyFile)
    if ($code -ne 0) { Fail "更新 Release 说明失败（exit=$code）" }
    $code = Run $Gh (@('release', 'upload', $tag) + $assets + @('--clobber'))
    if ($code -ne 0) { Fail "上传附件失败（exit=$code）" }
}

if ($DryRun) {
    Step 'dry-run 结束，未做任何改动'
    Remove-Item $bodyFile -Force -ErrorAction SilentlyContinue
    exit 0
}

# ---------------------------------------------------------------- 收尾核对
Step '核对线上结果'
$old = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
$json = & $Gh release view $tag --json tagName,isDraft,url,assets 2>&1
$code = $LASTEXITCODE
$ErrorActionPreference = $old
if ($code -ne 0) { Fail '无法读取 Release 信息' }
$view = ($json -join "`n") | ConvertFrom-Json
Write-Host ("  状态 draft={0}" -f $view.isDraft)
foreach ($a in $view.assets) {
    Write-Host ("  {0,-34} {1,10:N0} 字节" -f $a.name, $a.size)
}
$expected = $assets.Count
if ($view.assets.Count -ne $expected) {
    Warn "附件数量为 $($view.assets.Count)，预期 $expected"
}
Write-Host ''
Write-Host "发布完成：$($view.url)" -ForegroundColor Green
Write-Host "客户端清单：https://raw.githubusercontent.com/yuguod/EasyLabel/$branch/update.json" -ForegroundColor DarkGray
Remove-Item $bodyFile -Force -ErrorAction SilentlyContinue
