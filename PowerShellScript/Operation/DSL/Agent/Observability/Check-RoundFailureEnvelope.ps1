#requires -Version 5.1
<#
.SYNOPSIS
    失败信封与单轮 trace 守卫（RoundFailureEnvelope）—— R1/R3/R7。

.DESCRIPTION
    判据（每项均带正/负对照，防恒真假绿）：
      R1 契约在位：Agent.Contract 含 `TaskFailureEnvelope`（Stage/Reason/NodeRef 三项 + unknown 兜底）。
      R2 载荷在位：AgentTaskItem 含 `TaskFailureEnvelope? Failure`；序列化器 Read/Write 均处理 `Failure`（旧载荷零迁移）。
      R3 单写点：`Failure =` 写入点仅 ConversationStageBodies（成功轮置 null、失败轮写信封）。
      R4 trace 单发点：`Round_trace` 日志登记于 agent_log_messages.json，且仅 ConversationStageBodies 发射 `Round_trace`。
      R5 Faulted 写入面：`TaskExecutionStatus.Faulted` 的赋值点 ⊆ 白名单（ConversationStageBodies / AgentTaskPool.SweepDormantAsync）。
      N1 负对照：合成「Faulted 但无信封」文本必须被判红（谓词不得恒真）。
      P1 正对照：真实 ConversationStageBodies 文本必须被判绿。

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
$ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Key 'RoundFailureEnvelope'
$Root = $ctx.Root
$violations = $ctx.Violations
$agent = Join-Path $Root 'Projects\Plug\Agent\src'

Write-Host ''
Write-Host '============================================================'
Write-Host '  失败信封与单轮 trace 守卫（RoundFailureEnvelope）'
Write-Host '============================================================'

function Get-Text([string]$Relative) {
    $path = Join-Path $agent $Relative
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    return [System.IO.File]::ReadAllText($path)
}

function Test-Has([string]$Relative, [string]$Pattern, [string]$Code, [string]$Message) {
    $text = Get-Text $Relative
    if ($null -eq $text) { $script:violations.Add("[$Code] 缺件：$Relative"); return }
    if ($text -notmatch $Pattern) { $script:violations.Add("[$Code] $Message") }
}

# R1 契约
Test-Has 'Agent.Contract\State\TaskFailureEnvelope.cs' 'record TaskFailureEnvelope' 'R1' '缺少 TaskFailureEnvelope 契约'
Test-Has 'Agent.Contract\State\TaskFailureEnvelope.cs' 'Unknown' 'R1' 'TaskFailureEnvelope 缺 unknown 兜底'

# R2 载荷 + 序列化
Test-Has 'Agent.Core\Runtime\State\TaskPool\AgentTaskItem.cs' 'TaskFailureEnvelope\?\s+Failure' 'R2' 'AgentTaskItem 缺 Failure 字段'
Test-Has 'Agent.Core\Runtime\State\TaskPool\AgentTaskItemJsonConverter.cs' 'TryFailure' 'R2' '序列化器缺 Failure 读取'
Test-Has 'Agent.Core\Runtime\State\TaskPool\AgentTaskItemJsonConverter.cs' 'WritePropertyName\("Failure"\)' 'R2' '序列化器缺 Failure 写入'

# R3/R4 单写点 / trace 单发点
$bodiesRel = 'Agent.Core\Orchestration\SessionWorkflow\Circuit\ConversationStageBodies.cs'
Test-Has $bodiesRel 'TaskFailureEnvelope\.Create' 'R3' '完成阶段未构造失败信封'
Test-Has $bodiesRel 'finalizedItem\.Failure\s*=\s*failure' 'R3' '完成阶段未把信封写进任务行'
Test-Has $bodiesRel '"Round_trace"' 'R4' '完成阶段未发射 Round_trace'

Test-Has 'Agent.Core\Diagnostics\agent_log_messages.json' '"Round_trace"' 'R4' 'Round_trace 未在日志名册登记'

$failureWriters = @()
$traceEmitters = @()
$faultedAssign = @()
foreach ($file in (Get-ChildItem -Path $agent -Recurse -Filter *.cs -File | Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' })) {
    $text = [System.IO.File]::ReadAllText($file.FullName)
    if ($text -match 'TaskFailureEnvelope\.Create') { $failureWriters += $file.FullName }
    if ($text -match '"Round_trace"') { $traceEmitters += $file.FullName }
    if ($text -match 'Status\s*=\s*TaskExecutionStatus\.Faulted') { $faultedAssign += $file.FullName }
}
if ($failureWriters.Count -ne 1) {
    $violations.Add("[R3] 失败信封决策点（TaskFailureEnvelope.Create）应唯一，实际 $($failureWriters.Count)：$($failureWriters -join '; ')")
}
if ($traceEmitters.Count -ne 1) {
    $violations.Add("[R4] Round_trace 发射点应唯一，实际 $($traceEmitters.Count)：$($traceEmitters -join '; ')")
}
$faultedAllow = @('ConversationStageBodies.cs', 'AgentTaskPool.cs')
foreach ($path in $faultedAssign) {
    if ($faultedAllow -notcontains (Split-Path -Leaf $path)) {
        $violations.Add("[R5] Faulted 赋值点不在白名单：$path")
    }
}

# N1 负对照 + P1 正对照（谓词必须区分有无信封）
function Test-EnvelopePresent([string]$Text) {
    return ($Text -match 'TaskFailureEnvelope\.Create') -and ($Text -match 'finalizedItem\.Failure\s*=\s*failure')
}
$negative = 'ctx.Item.Status = TaskExecutionStatus.Faulted;'
$positive = Get-Text $bodiesRel
if ($null -eq $positive) {
    $violations.Add('[P1] 正对照缺件：ConversationStageBodies.cs')
} elseif (-not (Test-EnvelopePresent $positive)) {
    $violations.Add('[P1] 正对照失败：真实完成阶段未被判绿')
}
if (Test-EnvelopePresent $negative) {
    $violations.Add('[N1] 负对照失败：无信封文本被判绿（谓词恒真）')
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'RoundFailureEnvelope'
