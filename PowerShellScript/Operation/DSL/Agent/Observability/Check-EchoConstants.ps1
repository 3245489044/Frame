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

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

# 上下文/裁决（根定位 + 违规清单 + 统一出口）
$ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Key 'EchoConstants'
$Root = $ctx.Root
$bus = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui\Core\Assembly\Dispatch\AgentUiLibraryBusForward.cs'
$projector = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Modules\Response\Intent\Echo\OutcomeEchoProjector.cs'

$violations = $ctx.Violations

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

Write-GateVerdict -Violations $violations -Check:$Check -Key 'EchoConstants'
