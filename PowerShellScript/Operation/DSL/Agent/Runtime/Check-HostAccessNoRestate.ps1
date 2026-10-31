#requires -Version 5.1
<#
.SYNOPSIS
    宿主访问面零重抄守卫（HostAccessNoRestate）—— 接口/实现不再各写一遍逐槽成员（电路装配化）。

.DESCRIPTION
    判据（B5/RED-02）：
      G1 实现单成员：AgentUiHostAccess 类体内具名端口成员（`=> Read(HostPort.`）= 0；`Read(HostPort port)` 恰 1。
      G2 接线单源：UiHostAccessCircuit 声明电路配方（OrderedRecipe）与端口读器表。
      G3 端口表在位：HostPort 枚举 + IHostAccess 槽表接口。
      G4 正/负对照：合成样本（实现内具名端口成员）必被捕获。

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

# 上下文/裁决（根定位 + 违规清单 + 统一出口）
$ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Key 'HostAccessNoRestate'
$Root = $ctx.Root
$violations = $ctx.Violations

Write-Host ''
Write-Host '============================================================'
Write-Host '  宿主访问面零重抄守卫（HostAccessNoRestate）'
Write-Host '============================================================'

function Get-Count([string]$Text, [string]$Pattern) { return [regex]::Matches($Text, $Pattern).Count }

$accessFile  = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui\Core\Host\AgentUiHostAccess.cs'
$circuitFile = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui\Core\Host\UiHostAccessCircuit.cs'
$portFile    = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui\Core\Host\HostPort.cs'

if (-not (Test-Path -LiteralPath $accessFile)) {
    $violations.Add("[G1] 缺件：$accessFile")
} else {
    $text = [System.IO.File]::ReadAllText($accessFile)
    $marker = 'public sealed class AgentUiHostAccess'
    $idx = $text.IndexOf($marker, [System.StringComparison]::Ordinal)
    if ($idx -lt 0) {
        $violations.Add('[G1] 未找到 AgentUiHostAccess 类（判据不可判）')
    } else {
        $impl = $text.Substring($idx)
        $named = Get-Count $impl '=>\s*Read\(HostPort\.'
        if ($named -ne 0) { $violations.Add("[G1] 实现类内具名端口成员命中 $named 处（应 0：成员须在接口默认实现）") }
        $readImpl = Get-Count $impl 'public\s+object\?\s+Read\(HostPort\s+port\)'
        if ($readImpl -ne 1) { $violations.Add("[G1] 实现类内 Read(HostPort port) 命中 $readImpl 处（应恰 1）") }
    }
}

if (-not (Test-Path -LiteralPath $circuitFile)) {
    $violations.Add("[G2] 缺件：$circuitFile")
} else {
    $c = [System.IO.File]::ReadAllText($circuitFile)
    if ($c -notmatch 'OrderedRecipe<') { $violations.Add('[G2] 未声明电路配方（OrderedRecipe）') }
    if ($c -notmatch 'recipe\.(Then|Collect)') { $violations.Add('[G2] 未按步骤声明端口读器（Then/Collect）') }
}

if (-not (Test-Path -LiteralPath $portFile)) {
    $violations.Add("[G3] 缺件：$portFile")
} else {
    $p = [System.IO.File]::ReadAllText($portFile)
    if ($p -notmatch 'enum\s+HostPort') { $violations.Add('[G3] 缺 HostPort 端口表枚举') }
    if ($p -notmatch 'interface\s+IHostAccess') { $violations.Add('[G3] 缺 IHostAccess 槽表接口') }
}

# 正/负对照
if ((Get-Count 'public AgentTaskPoolAdapter? TaskPool => Read(HostPort.TaskPool) as AgentTaskPoolAdapter;' '=>\s*Read\(HostPort\.') -eq 0) {
    $violations.Add('[G4] 负对照失效：合成样本（具名端口成员）未被捕获')
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'HostAccessNoRestate'
