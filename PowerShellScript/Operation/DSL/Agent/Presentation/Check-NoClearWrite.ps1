#requires -Version 7.0
<#
.SYNOPSIS
    无清空写守卫（NoClearWrite）——A 生效后写通道不得承载删除/清空生命周期。

.DESCRIPTION
    C1 `SetView(` 零残留（过渡方案 B 的视图生命周期入口已下线）
    C2 `_pendingStreamClear` 零残留
    C3 `UiStreamTrim(0)` / `Trim(...,0)` 零命中（DOM 侧 keep<=0 是 no-op，不能表达清空）
    C4 空串清空目标零命中（`new UiWriteTarget(..., string.Empty)`）
    C5 负对照：合成清空写点必须被 C3/C4 判据捕获
    扫描面 = Frame 写通道/流水线 + Agent.Ui。纯扫描（只读、0 写入）。
#>
[CmdletBinding()]
param([switch]$Check, [switch]$NegativeControl)

$ErrorActionPreference = 'Stop'

$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

$repo = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
$scanRoots = @(
    (Join-Path $repo 'Frame\CsharpPrj\Plate\HtmlViewer'),
    (Join-Path $repo 'Frame\CsharpPrj\Operation\FrontEndDriven\HtmlViewerBridge'),
    (Join-Path $repo 'Projects\Plug\Agent\src\Agent.Ui')
)

function Get-SourceFiles {
    foreach ($root in $scanRoots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        Get-ChildItem -LiteralPath $root -Recurse -File -Include *.cs, *.js |
            Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\|\\Assets\\scripts\\' }
    }
}

function Test-ClearWriteText([string]$text) {
    if ($text -match 'new\s+UiWriteTarget\s*\([^)]*,\s*string\.Empty\s*\)') { return $true }
    if ($text -match 'UiStreamTrim\s*\(\s*0\s*\)') { return $true }
    if ($text -match 'UiWritePlan\.Trim\s*\([^)]*,\s*0\s*\)') { return $true }
    return $false
}

$rules = @(
    [pscustomobject]@{ Id = 'C1'; Title = 'SetView 生命周期入口已下线'
        Ok = {
            $hits = @(Get-SourceFiles | Where-Object {
                ([System.IO.File]::ReadAllText($_.FullName)) -match '\.SetView\s*\(|void\s+SetView\s*\('
            })
            return ($hits.Count -eq 0)
        } }
    [pscustomobject]@{ Id = 'C2'; Title = '_pendingStreamClear 清零标记已下线'
        Ok = {
            $hits = @(Get-SourceFiles | Where-Object {
                ([System.IO.File]::ReadAllText($_.FullName)) -match '_pendingStreamClear'
            })
            return ($hits.Count -eq 0)
        } }
    [pscustomobject]@{ Id = 'C3-C4'; Title = '无 Trim(0)/空串清空目标'
        Ok = {
            $hits = @(Get-SourceFiles | Where-Object { Test-ClearWriteText ([System.IO.File]::ReadAllText($_.FullName)) })
            return ($hits.Count -eq 0)
        } }
)

if ($NegativeControl) {
    if (-not (Test-ClearWriteText 'clears.Add(new UiWriteTarget(_streamName, string.Empty));')) { throw '负对照失败：C4 未捕获空串清空目标' }
    if (-not (Test-ClearWriteText 'UiWritePlan.Trim(stream, 0)')) { throw '负对照失败：C3 未捕获 Trim(0)' }
    if (Test-ClearWriteText 'trees.Add(new UiWriteTarget(regionName, bound));') { throw '负对照失败：C3/C4 对合法写目标误报' }
    Write-Host '[OK] 负对照通过：清空写点被捕获，合法写目标不误报。' -ForegroundColor Green
}

if (-not $Check -and -not $NegativeControl) {
    Write-Host '用法: -Check（检查） / -NegativeControl（判据自检）' -ForegroundColor Yellow
    return
}

if ($Check) {
    $failed = 0
    foreach ($r in $rules) {
        $pass = $false
        try { $pass = [bool](& $r.Ok) } catch { $pass = $false }
        $mark = if ($pass) { 'PASS' } else { 'FAIL' }
        if (-not $pass) { $failed++ }
        Write-Host ("[{0}] {1} {2}" -f $mark, $r.Id, $r.Title) -ForegroundColor $(if ($pass) { 'Green' } else { 'Red' })
    }
    if ($failed -gt 0) {
        Write-Host "NoClearWrite: $failed 项违规（写通道仍在承担生命周期）。" -ForegroundColor Red
        exit 1
    }
    Write-Host 'NoClearWrite: 全部通过。' -ForegroundColor Green
}

exit 0
