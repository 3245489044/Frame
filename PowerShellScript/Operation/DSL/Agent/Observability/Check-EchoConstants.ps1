#requires -Version 5.1
<#
.SYNOPSIS
    回显投影单源门禁（禁叙事式常量回显）。

.DESCRIPTION
      [E1] 总线转发面不得出现「Task.FromResult("已…")」叙事常量；
      [E2] 总线转发面必须经 OutcomeEchoProjector 投影；
      [E3] 投影器必须输出结构事实（decision= / wrote=）；
      [E4] 正对照：总线转发面仍保留会话渲染映射（conversation history/state），防误删读面回显。
    只读源码；-Check 时违规 > 0 退出码 1。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$bus = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui\Core\Assembly\Dispatch\AgentUiLibraryBusForward.cs'
$projector = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Modules\Response\Intent\Echo\OutcomeEchoProjector.cs'

$violations = [System.Collections.Generic.List[string]]::new()

if (-not (Test-Path -LiteralPath $bus)) { $violations.Add("缺少总线转发面: $bus") }
if (-not (Test-Path -LiteralPath $projector)) { $violations.Add("缺少唯一回显投影: $projector") }

if ($violations.Count -eq 0) {
    $busText = Get-Content -LiteralPath $bus -Raw
    $hits = Select-String -LiteralPath $bus -Pattern 'Task\.FromResult\(\s*\$?"已' -AllMatches
    foreach ($hit in $hits) {
        $violations.Add("[E1] 叙事式回显常量命中 AgentUiLibraryBusForward.cs:$($hit.LineNumber)")
    }
    if ($busText -notmatch 'OutcomeEchoProjector\.Project') {
        $violations.Add('[E2] 总线转发面未经 OutcomeEchoProjector 投影')
    }
    if ($busText -notmatch '\("conversation",\s*"history"\)') {
        $violations.Add('[E4] 正对照失败：会话渲染映射缺失（防把读面回显一并删除）')
    }

    $projectorText = Get-Content -LiteralPath $projector -Raw
    if ($projectorText -notmatch 'decision=') { $violations.Add('[E3] 回显投影未输出 decision（结构事实）') }
    if ($projectorText -notmatch 'wrote=') { $violations.Add('[E3] 回显投影未输出 wrote（实际写面）') }
}

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Host "[违规] $_" }
    if ($Check) { exit 1 }
    exit 0
}

Write-Host '[通过] 回显仅由结构结果投影（无叙事常量，写面可见）'
exit 0
