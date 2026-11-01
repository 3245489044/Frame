<#
.SYNOPSIS
    启动前构建门（提交态）：判定 UI 启动是否需要编译。

.DESCRIPTION
    判据（结构事实，非叙述）：
      1) 工作区脏（含子模块、未跟踪文件）⇒ 不自动编译（代码修改在途）。
      2) 工作区干净：
           - 关键产物缺失 ⇒ 需要编译；
           - 无上次成功构建记录 ⇒ 需要编译；
           - 主仓 HEAD / 子模块提交与上次成功构建记录一致 ⇒ 跳过编译；
           - 不一致 ⇒ 需要编译。

    stamp 落点：<RepoRoot>\BuildOutput\.build-gate\last-built.json
    由 UI启动.bat 在构建成功后以 -UpdateStamp 写入（工作区脏时拒绝写入）。

    退出码：
      0  = 需要编译
      10 = 跳过（提交未变化）
      11 = 跳过（工作区脏）
      12 = 阻断（工作区脏且关键产物缺失）
      2  = 前置失败（路径/模块缺失）

.PARAMETER RepoRoot
    仓库根；缺省由框架 ReadProjectConfig.Get-ProjectRoot 解析。

.PARAMETER Configuration
    构建配置（Debug/Release）。

.PARAMETER UpdateStamp
    构建成功后记录当前提交态；工作区脏或 git 不可用时拒绝写入并清除旧 stamp。

.PARAMETER Invalidate
    清除旧构建 stamp（强制编译前调用，避免失败/脏构建后误命中旧记录）。
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = '',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$UpdateStamp,
    [switch]$Invalidate
)

$ErrorActionPreference = 'Stop'

function Write-Gate {
    param([string]$Text, [string]$Color = 'Gray')
    Write-Host "[BUILD-GATE] $Text" -ForegroundColor $Color
}

$coreModule = Join-Path $PSScriptRoot '..\..\Modules\Core\ReadProjectConfig.ps1'
if (-not (Test-Path -LiteralPath $coreModule)) {
    Write-Gate ("缺少框架路径模块：{0}" -f $coreModule) 'Red'
    exit 2
}
. $coreModule

if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $RepoRoot = Get-ProjectRoot }
if (-not (Test-Path -LiteralPath $RepoRoot)) {
    Write-Gate ("仓库根不存在：{0}" -f $RepoRoot) 'Red'
    exit 2
}
$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path

$gitCommand = Get-Command -Name 'git' -ErrorAction SilentlyContinue
if ($null -eq $gitCommand -or [string]::IsNullOrWhiteSpace($gitCommand.Source)) {
    Write-Gate '未找到 git，无法判定提交态；按需要编译' 'Yellow'
    if ($UpdateStamp) { exit 1 }
    exit 0
}

function Invoke-Git {
    param([string[]]$GitArgs)

    $output = & $gitCommand.Source -C $RepoRoot @GitArgs 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw ("git {0} 失败（exit {1}）" -f ($GitArgs -join ' '), $LASTEXITCODE)
    }
    return @($output)
}

function Get-StatusPath {
    param([string]$Line)

    if ([string]::IsNullOrWhiteSpace($Line) -or $Line.Length -le 3) { return '' }
    $path = $Line.Substring(3).Trim()
    if ($path.StartsWith('"') -and $path.EndsWith('"') -and $path.Length -ge 2) {
        $path = $path.Substring(1, $path.Length - 2)
    }
    if ($path -match ' -> ') { $path = ($path -split ' -> ')[-1] }
    return $path
}

function Test-IsGitlink {
    param([string]$Path)

    try {
        $out = & $gitCommand.Source -C $RepoRoot ls-files -s -- $Path 2>$null
        return [bool]($out -match '^160000\s')
    }
    catch {
        return $false
    }
}

function Test-BuildRelevantPath {
    param(
        [string]$Path,
        [switch]$MainRepo
    )

    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    $normalized = $Path.Replace('/', '\')

    # 构建产物/临时目录不属于“代码修改在途”
    if ($normalized -match '(^|\\)(BuildOutput|obj|bin|Temp|\.vs|\.vscode)(\\|$)') { return $false }
    if ($normalized -match '^(Docs|doc)(\\|$)') { return $false }

    if ($MainRepo) {
        if (Test-IsGitlink -Path $normalized) { return $false }   # 子模块条目由逐仓检查覆盖
        if ($normalized -match '^(Frame|Projects)(\\|$)') { return $true }
        if ($normalized -match '^(Directory\.Build\.(props|targets)|Directory\.Packages\.props|global\.json|NuGet\.config|\.gitmodules)$') { return $true }
        return $false
    }

    # 子模块内部：除 Docs/产物外的改动都视为构建相关
    return $true
}

function Get-RepoSnapshot {
    $dirtyEntries = New-Object System.Collections.Generic.List[string]

    # 主仓：忽略子模块 gitlink 条目，仅保留构建相关文件（随后再逐子模块检查）
    $mainStatus = @(Invoke-Git -GitArgs @('status', '--porcelain', '--ignore-submodules=all'))
    foreach ($line in $mainStatus) {
        $path = Get-StatusPath -Line $line
        if (Test-BuildRelevantPath -Path $path -MainRepo) { $dirtyEntries.Add($line) }
    }

    $head = [string](Invoke-Git -GitArgs @('rev-parse', 'HEAD') | Select-Object -First 1)

    $subLines = @()
    try { $subLines = @(Invoke-Git -GitArgs @('submodule', 'status', '--recursive')) }
    catch { $subLines = @() }

    $subEntries = foreach ($line in $subLines) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        if ($line -notmatch '^(?<flag>[ +\-U])(?<sha>[0-9a-fA-F]{7,40})\s+(?<path>\S+)') { continue }

        $subPath = $Matches['path']
        $subSha = $Matches['sha'].ToLowerInvariant()

        $subRoot = Join-Path $RepoRoot $subPath
        if (Test-Path -LiteralPath $subRoot) {
            try {
                $subStatus = @(& $gitCommand.Source -C $subRoot status --porcelain --ignore-submodules=all 2>$null)
                foreach ($subLine in $subStatus) {
                    $subFile = Get-StatusPath -Line $subLine
                    if (Test-BuildRelevantPath -Path $subFile) {
                        $dirtyEntries.Add(('[{0}] {1}' -f $subPath, $subLine))
                    }
                }
            }
            catch {
                # 子模块不可读：不阻断，交由产物检查/构建处理
            }
        }

        '{0}={1}' -f $subPath, $subSha
    }

    $revision = (@('main=' + $head.ToLowerInvariant()) + @($subEntries | Sort-Object)) -join ';'

    return [pscustomobject]@{
        Dirty    = ($dirtyEntries.Count -gt 0)
        Status   = @($dirtyEntries)
        Head     = $head
        Revision = $revision
    }
}

$stampDir = Join-Path $RepoRoot 'BuildOutput\.build-gate'
$stampFile = Join-Path $stampDir 'last-built.json'

function Test-KeyArtifacts {
    $required = @(
        (Join-Path $RepoRoot "Projects\FileDriven\BuildOutput\$Configuration\FileDriven.Launcher.exe")
        (Join-Path $RepoRoot "Frame\CsharpPrj\BuildOutput\$Configuration\BasicMethod.dll")
        (Join-Path $RepoRoot "Projects\Plug\Agent\BuildOutput\$Configuration\Agent.Launcher.dll")
    )
    return @($required | Where-Object { -not (Test-Path -LiteralPath $_) })
}

if ($Invalidate) {
    if (Test-Path -LiteralPath $stampFile) {
        Remove-Item -LiteralPath $stampFile -Force
        Write-Gate '已清除构建 stamp（下次启动按提交态重判）' 'DarkGray'
    }
    exit 0
}

if ($UpdateStamp) {
    $snapshot = Get-RepoSnapshot
    if ($snapshot.Dirty) {
        if (Test-Path -LiteralPath $stampFile) { Remove-Item -LiteralPath $stampFile -Force }
        Write-Gate '工作区有未提交修改：不记录「已构建」stamp（已清除旧 stamp）' 'Yellow'
        exit 1
    }

    New-Item -ItemType Directory -Path $stampDir -Force | Out-Null
    $payload = [ordered]@{
        schema        = 'build-gate-v1'
        revision      = $snapshot.Revision
        configuration = $Configuration
        builtAt       = (Get-Date).ToString('o')
        repoRoot      = $RepoRoot
    }
    $payload | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $stampFile -Encoding UTF8
    $shortHead = $snapshot.Head.Substring(0, [Math]::Min(12, $snapshot.Head.Length))
    Write-Gate ("已记录构建 stamp（{0}）" -f $shortHead) 'DarkGray'
    exit 0
}

try {
    $snapshot = Get-RepoSnapshot
}
catch {
    Write-Gate ("无法读取 git 状态：{0}；按需要编译" -f $_.Exception.Message) 'Yellow'
    exit 0
}

$missing = @(Test-KeyArtifacts)

if ($snapshot.Dirty) {
    Write-Gate ("工作区有未提交修改（{0} 项）：跳过自动编译" -f $snapshot.Status.Count) 'Yellow'
    foreach ($line in ($snapshot.Status | Select-Object -First 8)) {
        Write-Host ("             {0}" -f $line) -ForegroundColor DarkGray
    }
    if ($snapshot.Status.Count -gt 8) {
        Write-Host '             ...' -ForegroundColor DarkGray
    }
    if ($missing.Count -gt 0) {
        Write-Gate ("关键产物缺失 {0} 项，且工作区脏：不自动编译，也不启动" -f $missing.Count) 'Red'
        foreach ($path in $missing) { Write-Host ("             {0}" -f $path) -ForegroundColor DarkRed }
        Write-Gate '处置：提交/暂存修改后重试，或显式编译 UI启动.bat --fast / --full' 'Yellow'
        exit 12
    }
    Write-Gate '显式编译：UI启动.bat --fast（快速）或 --full（完整）' 'DarkGray'
    exit 11
}

if ($missing.Count -gt 0) {
    Write-Gate ("关键产物缺失 {0} 项：需要编译" -f $missing.Count) 'Yellow'
    foreach ($path in $missing) { Write-Host ("             {0}" -f $path) -ForegroundColor DarkGray }
    exit 0
}

if (-not (Test-Path -LiteralPath $stampFile)) {
    Write-Gate '无上次成功构建记录：需要编译' 'Yellow'
    exit 0
}

try {
    $stamp = Get-Content -LiteralPath $stampFile -Raw | ConvertFrom-Json
}
catch {
    Write-Gate '构建 stamp 解析失败：需要编译' 'Yellow'
    exit 0
}

if ([string]$stamp.schema -ne 'build-gate-v1' -or
    [string]$stamp.configuration -ne $Configuration -or
    [string]$stamp.revision -ne $snapshot.Revision) {
    $shortHead = $snapshot.Head.Substring(0, [Math]::Min(12, $snapshot.Head.Length))
    Write-Gate ("提交态已变化（{0}）：需要编译" -f $shortHead) 'Yellow'
    exit 0
}

$shortHead = $snapshot.Head.Substring(0, [Math]::Min(12, $snapshot.Head.Length))
Write-Gate ("提交未变化（{0}）：跳过编译" -f $shortHead) 'Green'
exit 10
