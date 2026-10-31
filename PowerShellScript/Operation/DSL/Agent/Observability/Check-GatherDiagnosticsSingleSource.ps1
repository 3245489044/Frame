#requires -Version 5.1
<#
.SYNOPSIS
    聚拢诊断单源守卫（GatherDiagnosticsSingleSource）—— 诊断文本由模型字段投影，无自拼副本。

.DESCRIPTION
    判据（B2/RED-09）：
      G1 零自拼：NodeGatherService 内 `Diagnostics =` 赋值与 `BuildDiagnostics` 定义 = 0。
      G2 渲染投影：渲染侧由模型字段投影（ConversationViewService 的 BuildGatherNote 在位）。
      G3 正/负对照：合成样本（Diagnostics 自拼赋值）必被捕获。

    模式：默认报告；-Check 时违规 → 退出码 1。

.PARAMETER Root
    仓库根（默认为本脚本位置上溯五级）。
.PARAMETER Check
    校验模式。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
}

$violations = [System.Collections.Generic.List[string]]::new()

Write-Host ''
Write-Host '============================================================'
Write-Host '  聚拢诊断单源守卫（GatherDiagnosticsSingleSource）'
Write-Host '============================================================'

function Get-Count([string]$Text, [string]$Pattern) { return [regex]::Matches($Text, $Pattern).Count }

$service = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Modules\Framework\Gather\NodeGatherService.cs'
$view    = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\Orchestration\ConversationViewService.Nodes.cs'

if (-not (Test-Path -LiteralPath $service)) {
    $violations.Add("[G1] 缺件：$service")
} else {
    $t = [System.IO.File]::ReadAllText($service)
    $assign = Get-Count $t '\bDiagnostics\s*='
    $build = Get-Count $t 'BuildDiagnostics\s*\('
    if ($assign -ne 0) { $violations.Add("[G1] 诊断自拼赋值命中 $assign 处（应 0）") }
    if ($build -ne 0) { $violations.Add("[G1] BuildDiagnostics 定义/调用命中 $build 处（应 0）") }
}

if (Test-Path -LiteralPath $view) {
    if ([System.IO.File]::ReadAllText($view) -notmatch 'BuildGatherNote') {
        $violations.Add('[G2] 渲染侧缺模型字段投影（BuildGatherNote）')
    }
} else {
    $violations.Add("[G2] 缺件：$view")
}

# 正/负对照
if ((Get-Count '        { Diagnostics = includeDiagnostics ? BuildDiagnostics(result) : null };' '\bDiagnostics\s*=') -eq 0) {
    $violations.Add('[G3] 负对照失效：合成样本（诊断自拼赋值）未被捕获')
}

if ($violations.Count -eq 0) {
    Write-Host '  [成功] G1/G2 聚拢诊断单源通过（正负对照绿）' -ForegroundColor Green
    Write-Host ''
    exit 0
}

Write-Host ''
Write-Host "  [违规] $($violations.Count) 项：" -ForegroundColor Red
foreach ($v in $violations) { Write-Host "    - $v" -ForegroundColor Red }
Write-Host '  结果: 失败' -ForegroundColor Red
Write-Host ''
if ($Check) { exit 1 }
exit 0
