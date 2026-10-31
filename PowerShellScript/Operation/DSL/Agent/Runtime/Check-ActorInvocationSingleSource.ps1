#requires -Version 5.1
<#
.SYNOPSIS
    调用登记单源守卫（ActorInvocationSingleSource）—— P5′/P8′。

.DESCRIPTION
    判据：
      I1 调用台账写点 = 1（`ActorInvocationLedger.Record` 的生产调用点仅 AgentTaskPool 活入队单点）。
      I2 行必含 InvocationId（合同 ActorObservationRow 定义 ∧ 合并器赋值）。
      I3 同 TaskKey 幂等（台账含 TryAdd 守卫，不重复登记）。
      I4 结果账写点 = 1（仅 SubAgentResultPublisher）。
      I5 正/负对照：生产扫描器必须捕获合成写点、且不把读点/定义点误判为写点。

    模式：默认报告；-Check 时违规 → 退出码 1。
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
$ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Key 'ActorInvocationSingleSource'
$Root = $ctx.Root
$violations = $ctx.Violations
$src = Join-Path $Root 'Projects\Plug\Agent\src'
Import-Module (Join-Path (Get-FrameRoot) 'PowerShellScript\Baselines\GateFileIndex.psm1') -Force

Write-Host ''
Write-Host '============================================================'
Write-Host '  调用登记单源守卫（ActorInvocationSingleSource：P5′/P8′）'
Write-Host '============================================================'

function Get-RecordCallers([string]$Pattern, [string]$Text = '') {
    $hits = [System.Collections.Generic.List[string]]::new()
    # 正/负对照入口：把合成文本喂给**同一**分类器（E-5：禁常量对常量自检）。
    if ($PSBoundParameters.ContainsKey('Text')) {
        if ($Text -match $Pattern) { $hits.Add('<probe>') }
        return $hits
    }
    if (-not (Test-Path -LiteralPath $src)) { return $hits }
    foreach ($file in (Get-GateFiles -Root $src -Extension cs -Exclude '\\(obj|bin|BuildOutput)\\')) {
        $name = [System.IO.Path]::GetFileName($file)
        if ($name -in @('ActorInvocationLedger.cs', 'ResultObservationLedger.cs')) { continue }
        $text = [System.IO.File]::ReadAllText($file)
        if ($text -match $Pattern) { $hits.Add($name) }
    }
    return $hits
}

$invocationWriters = @(Get-RecordCallers 'ActorInvocationLedger\.Record\s*\(')
if ($invocationWriters.Count -ne 1) {
    $violations.Add("[I1] 调用登记写点=$($invocationWriters.Count)（须恰 1：AgentTaskPool）")
} elseif ($invocationWriters[0] -ne 'AgentTaskPool.cs') {
    $violations.Add("[I1] 调用登记写点=$($invocationWriters[0])（须 AgentTaskPool.cs：活入队单点）")
}

$resultWriters = @(Get-RecordCallers 'ResultObservationLedger\.Record\s*\(')
if ($resultWriters.Count -ne 1) {
    $violations.Add("[I4] 结果账写点=$($resultWriters.Count)（须恰 1：SubAgentResultPublisher）")
} elseif ($resultWriters[0] -ne 'SubAgentResultPublisher.cs') {
    $violations.Add("[I4] 结果账写点=$($resultWriters[0])（须 SubAgentResultPublisher.cs）")
}

$contracts = Join-Path $src 'Agent.Session\Projection\ActorObservationContracts.cs'
$projector = Join-Path $src 'Agent.Session\Projection\ActorObservationProjector.cs'
if (Test-Path -LiteralPath $contracts) {
    $text = [System.IO.File]::ReadAllText($contracts)
    if ($text -notmatch 'string InvocationId') { $violations.Add('[I2] ActorObservationRow 缺 InvocationId（行身份不完整）') }
} else {
    $violations.Add('[I2] 缺件（判据不可判）：ActorObservationContracts.cs')
}
if (Test-Path -LiteralPath $projector) {
    $text = [System.IO.File]::ReadAllText($projector)
    if ($text -notmatch 'InvocationId: invocation\.InvocationId') { $violations.Add('[I2] 合并器未赋值 InvocationId') }
} else {
    $violations.Add('[I2] 缺件（判据不可判）：ActorObservationProjector.cs')
}

$ledger = Join-Path $src 'Agent.Session\Projection\ActorInvocationLedger.cs'
if (Test-Path -LiteralPath $ledger) {
    $text = [System.IO.File]::ReadAllText($ledger)
    if ($text -notmatch 'TryAdd') { $violations.Add('[I3] 调用台账缺幂等守卫（同 TaskKey 可重复登记）') }
} else {
    $violations.Add('[I3] 缺件（判据不可判）：ActorInvocationLedger.cs')
}

$probeWriter = 'ActorInvocationLedger.Record(taskKey, tag);'
$probeReader = 'ActorInvocationLedger.Get(taskKey);'
if (-not (Get-RecordCallers 'ActorInvocationLedger\.Record\s*\(' -Text $probeWriter)) {
    $violations.Add('[I5] 负对照失效：写点合成样本未被扫描器识别')
}
if (Get-RecordCallers 'ActorInvocationLedger\.Record\s*\(' -Text $probeReader) {
    $violations.Add('[I5] 正对照失效：读点被误判为写点')
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'ActorInvocationSingleSource'
