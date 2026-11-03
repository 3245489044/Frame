# Check-TaskHistorySingleEntry.ps1
# 归拢写边单入口守卫（H1–H3）：history_owns_task / 解除边的命令构造点必须唯一（TaskHistoryCommandPort.cs）。
# 判据载体 = 源码静态构造点计数（结构事实）；不论调用方是谁，另造命令即绕过单父预检/自环/已归拢判据。
# 用法：pwsh -File Check-TaskHistorySingleEntry.ps1 [-Check] [-NegativeControl]

[CmdletBinding()]
param(
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
while ($repoRoot -and -not (Test-Path (Join-Path $repoRoot 'Projects\Plug\Agent'))) {
    $parent = Split-Path $repoRoot -Parent
    if ($parent -eq $repoRoot) { $repoRoot = $null; break }
    $repoRoot = $parent
}
if (-not $repoRoot) { Write-Host '[FAIL] 无法定位仓库根'; exit 2 }

$violations = [System.Collections.Generic.List[string]]::new()
function Add-Violation([string]$code, [string]$message) { $violations.Add("[$code] $message") | Out-Null }

$roots = @(
    (Join-Path $repoRoot 'Frame\CsharpPrj'),
    (Join-Path $repoRoot 'Projects\Plug\Agent\src')
)
$files = @()
foreach ($root in $roots) {
    if (-not (Test-Path -LiteralPath $root)) { continue }
    $files += @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter *.cs -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(obj|bin|BuildOutput)\\' })
}

function Get-CommandSites([string]$text, [string]$pattern) {
    return ([regex]::Matches($text, $pattern)).Count
}

# H1/H2：归拢/解除命令构造点唯一（单父预检/自环/回滚判据只在端口内）。
$attachSites = @()
$detachSites = @()
foreach ($f in $files) {
    $text = [System.IO.File]::ReadAllText($f.FullName)
    if ((Get-CommandSites $text 'new\s+AttachTaskCommand\s*\(') -gt 0) { $attachSites += $f.FullName }
    if ((Get-CommandSites $text 'new\s+DetachTaskCommand\s*\(') -gt 0) { $detachSites += $f.FullName }
}
$expected = 'TaskHistoryCommandPort.cs'
if ($attachSites.Count -ne 1 -or (Split-Path $attachSites[0] -Leaf) -ne $expected) {
    Add-Violation 'H1' "归拢写边命令构造点应唯一于 $expected，实际 $(@($attachSites | ForEach-Object { Split-Path $_ -Leaf }) -join ',')"
}
if ($detachSites.Count -ne 1 -or (Split-Path $detachSites[0] -Leaf) -ne $expected) {
    Add-Violation 'H2' "解除归拢命令构造点应唯一于 $expected，实际 $(@($detachSites | ForEach-Object { Split-Path $_ -Leaf }) -join ',')"
}

# H3：归拢写面不得自造 intent（UserRequested 只属于「显式用户创建历史」，不属于挂/卸边）。
$writeSurface = @(
    'TaskHistoryCommandPort.cs',
    'TaskProvisionOpsFunctions.cs',
    'HistoryRelationWritePort.cs'
)
foreach ($f in $files) {
    if ($writeSurface -notcontains (Split-Path $f.FullName -Leaf)) { continue }
    $text = [System.IO.File]::ReadAllText($f.FullName)
    if ($text -match 'CreationIntent\.UserRequested') {
        Add-Violation 'H3' "$($f.FullName) 归拢写面使用 CreationIntent.UserRequested（应为 SystemExplicit，审计口径单源）"
    }
}

if ($NegativeControl) {
    # 负对照：合成第二个 AttachTaskCommand 构造点必须被同一判据捕获。
    $probe = 'await commands.AttachTaskAsync(new AttachTaskCommand(ContainerKey.Create(h), TaskKey.Create(t), CreationIntent.SystemExplicit, "x"), ct);'
    if ((Get-CommandSites $probe 'new\s+AttachTaskCommand\s*\(') -lt 1) {
        Add-Violation 'H4' '负对照失效：合成归拢命令构造点未被判据捕获'
    }
    $probeIntent = 'new AttachTaskCommand(k1, k2, CreationIntent.UserRequested, "x")'
    if ($probeIntent -notmatch 'CreationIntent\.UserRequested') {
        Add-Violation 'H4' '负对照失效：合成 UserRequested 写面未被判据捕获'
    }
}

if ($violations.Count -eq 0) {
    Write-Host '[OK] TaskHistorySingleEntry：H1–H3 通过。' -ForegroundColor Green
    exit 0
}

Write-Host '[FAIL] TaskHistorySingleEntry 违规：' -ForegroundColor Red
$violations | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0
