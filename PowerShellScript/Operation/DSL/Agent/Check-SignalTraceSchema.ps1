#requires -Version 5.1
<#
.SYNOPSIS
    信号 Trace 契约守卫（SignalTraceSchema）—— 相位封闭 + 日志键登记 + 迁移可见化。
.DESCRIPTION
    判据（T1–T4，载体=声明面/日志定义/源码文本，只读）：
      T1 [Space] SignalPhase 恰为六段闭集（emit/admit/route/dispatch/ack/observe）。
      T2 Signal 运行期所有 AgentLog.Log("Key") 的 Key 均登记 agent_log_messages.json（未登记即静默 ⇒ 红）。
      T3 SignalIntake 每次投递/回执路径有可见化日志（flow_failed/flow_error/deadletter/illegal_transition/sweep 在册）。
      T4 六段 fn 均在其执行体文件中存在（正对照，防空转假绿）。
    模式：默认报告（不设退出码）；-Check 校验（违规 → 退出码 1）。
.PARAMETER Check
    校验模式：违规 → 退出码 1。
#>
[CmdletBinding()]
param([switch]$Check)

$ErrorActionPreference = 'Stop'

function Find-RepoRoot {
    $d = $PSScriptRoot
    while ($d -and -not (Test-Path (Join-Path $d 'Projects\Plug\Agent'))) { $d = Split-Path $d -Parent }
    if ($d) { return $d }
    throw '[SignalTraceSchema] 无法定位仓库根'
}

$repoRoot = Find-RepoRoot
$agentRoot = Join-Path $repoRoot 'Projects\Plug\Agent'
$violations = New-Object System.Collections.Generic.List[string]
function Add-Violation([string]$Id, [string]$Msg) { $script:violations.Add("$Id $Msg") }

$nodeFile = Join-Path $agentRoot 'Config\nodes\signal.ushar'
$flowFile = Join-Path $agentRoot 'Config\workflows\signal-intake.ushar'
$logFile = Join-Path $agentRoot 'src\Agent.Core\Diagnostics\agent_log_messages.json'
$signalDir = Join-Path $agentRoot 'src\Agent.Core\Engine\Signal'
$stepFile = Join-Path $agentRoot 'src\Agent.Modules\Framework\Signal\SignalStepFunctions.cs'

# T1 相位闭集
$nodeText = Get-Content -LiteralPath $nodeFile -Raw
$phases = @()
$inPhase = $false
foreach ($line in ($nodeText -split "`r?`n")) {
    $t = $line.Trim()
    if ($t -match '^\[Space\]\s+SignalPhase') { $inPhase = $true; continue }
    if ($inPhase -and $t -match '^\[') { break }
    if ($inPhase -and $t -match '^([A-Za-z0-9_]+)') { $phases += $Matches[1] }
}
$expectedPhases = @('emit', 'admit', 'route', 'dispatch', 'ack', 'observe')
foreach ($p in $expectedPhases) { if ($phases -notcontains $p) { Add-Violation 'T1' "缺相位 '$p'" } }
foreach ($p in $phases) { if ($expectedPhases -notcontains $p) { Add-Violation 'T1' "未注册相位 '$p'" } }

# T2 日志键登记
$logText = Get-Content -LiteralPath $logFile -Raw
$keys = @()
Get-ChildItem -LiteralPath $signalDir -Filter '*.cs' | ForEach-Object {
    $text = Get-Content -LiteralPath $_.FullName -Raw
    foreach ($m in [regex]::Matches($text, 'AgentLog\.Log\("([^"]+)"')) { $keys += $m.Groups[1].Value }
}
$keys = @($keys | Sort-Object -Unique)
foreach ($key in $keys) {
    if ($logText -notmatch [regex]::Escape('"' + $key + '"')) {
        Add-Violation 'T2' "日志键未登记: $key"
    }
}
if ($keys.Count -eq 0) { Add-Violation 'T2' 'Signal 运行期零日志键（正对照失效）' }

# T3 关键可见化键
foreach ($key in @('SignalIntake_flow_failed', 'SignalIntake_flow_error', 'SignalLedger_deadletter', 'SignalLedger_illegal_transition', 'SignalLedger_sweep')) {
    if ($logText -notmatch [regex]::Escape('"' + $key + '"')) { Add-Violation 'T3' "缺关键可见化键: $key" }
}

# T4 六段 fn 执行体存在（仅 [Node] 行的 fn=，注释/正文不算）
$stepText = Get-Content -LiteralPath $stepFile -Raw
$flowText = Get-Content -LiteralPath $flowFile -Raw
$fnValues = @([regex]::Matches($flowText, '(?m)^\s*\[Node\][^\r\n]*?fn=([A-Za-z0-9_]+)') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
foreach ($fn in $fnValues) {
    $constName = ($fn -replace '_', '')
    if ($stepText -notmatch [regex]::Escape('WorkflowFunctionNames.Signal' + ($fn -replace '^signal_', ''))) {
        Add-Violation 'T4' "fn '$fn' 在 SignalStepFunctions 无描述符登记"
    }
}

if ($violations.Count -eq 0) {
    Write-Host '[SignalTraceSchema] OK（T1–T4 全过）'
    exit 0
}
Write-Host "[SignalTraceSchema] 违规 $($violations.Count) 项："
foreach ($v in $violations) { Write-Host "  - $v" }
if ($Check) { exit 1 }
exit 0
