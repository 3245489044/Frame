#requires -Version 5.1
<#
.SYNOPSIS
    信号声明面守卫（SignalDeclaration）—— 词表/迁移/判定表/六段流/观测契约/端口登记对账。
.DESCRIPTION
    判据（S1–S6，载体=声明面与契约源码文本，只读）：
      S1 SignalState 词表 ↔ SignalState 枚举名（snake_case）双向一致。
      S2 SignalLifecycle：每个非终态至少一条出边；终态无出边。
      S3 signal-ack / signal-progress / signal-source-map 均为非空总函数（每条 rule 有 label/priority/when）。
      S4 [Flow] SignalIntake 每个 step 有 [Node]；[Branch]/[When] 的 from/to/then 目标存在。
      S5 [Obs] required 覆盖 signal_id/signal_key/state/attempt；enum=state 子集于 SignalState。
      S6 [Node] fn= 值均登记为 WorkflowFunctionNames 常量。
    模式：默认报告（不设退出码）；-Check 校验（违规 → 退出码 1）。
.PARAMETER Check
    校验模式：违规 → 退出码 1。
#>
[CmdletBinding()]
param([switch]$Check)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking


function Find-RepoRoot {
    $d = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
    if ($d) { return $d }
    throw '[SignalDeclaration] 无法定位仓库根'
}

$repoRoot = Find-RepoRoot
$agentRoot = Join-Path $repoRoot 'Projects\Plug\Agent'
$violations = New-Object System.Collections.Generic.List[string]

function Add-Violation([string]$Id, [string]$Msg) { $script:violations.Add("$Id $Msg") }
function Read-Text([string]$Path) { if (Test-Path -LiteralPath $Path) { return (Get-Content -LiteralPath $Path -Raw) } return '' }

$nodeFile = Join-Path $agentRoot 'Config\nodes\observability\signal.ushar'
$ackFile = Join-Path $agentRoot 'Config\decisions\signal-ack.ushar'
$progressFile = Join-Path $agentRoot 'Config\decisions\signal-progress.ushar'
$sourceFile = Join-Path $agentRoot 'Config\decisions\signal-source.ushar'
$flowFile = Join-Path $agentRoot 'Config\workflows\context\signal-intake.ushar'
$stateFile = Join-Path $agentRoot 'src\Agent.Contract\Signal\SignalState.cs'
$namesFile = Join-Path $agentRoot 'src\Agent.Contract\WorkflowFunction\Names\WorkflowFunctionNames.Signal.cs'

foreach ($f in @($nodeFile, $ackFile, $progressFile, $sourceFile, $flowFile, $stateFile, $namesFile)) {
    if (-not (Test-Path -LiteralPath $f)) { Add-Violation 'S0' "缺文件: $f" }
}

$nodeText = Read-Text $nodeFile
$flowText = Read-Text $flowFile

function Get-SpaceKeys([string]$Text, [string]$Space) {
    $keys = @()
    $in = $false
    foreach ($line in ($Text -split "`r?`n")) {
        $t = $line.Trim()
        if ($t -match "^\[Space\]\s+$Space\s*:") { $in = $true; continue }
        if ($in -and $t -match '^\[') { break }
        if ($in -and $t -match '^([A-Za-z0-9_]+)') { $keys += $Matches[1] }
    }
    return $keys
}

# S1 状态词表 ↔ 枚举
$stateKeys = @(Get-SpaceKeys $nodeText 'SignalState')
$enumText = Read-Text $stateFile
$enumKeys = @([regex]::Matches($enumText, '(?m)^\s*([A-Za-z][A-Za-z0-9_]*)\s*=\s*\d+') | ForEach-Object { $_.Groups[1].Value.ToLowerInvariant() })
if ($stateKeys.Count -eq 0) { Add-Violation 'S1' '[Space] SignalState 词表为空' }
foreach ($k in $stateKeys) { if ($enumKeys -notcontains $k) { Add-Violation 'S1' "词表状态 '$k' 未在 SignalState 枚举" } }
foreach ($k in $enumKeys) { if ($stateKeys -notcontains $k) { Add-Violation 'S1' "枚举状态 '$k' 未在 [Space] SignalState" } }

# S2 生命周期边
$edgeLines = @()
$inLifecycle = $false
foreach ($line in ($nodeText -split "`r?`n")) {
    $t = $line.Trim()
    if ($t -match '^\[Matrix\]\s+SignalLifecycle') { $inLifecycle = $true; continue }
    if ($inLifecycle -and $t -match '^\[') { break }
    if ($inLifecycle -and $t -match '^([A-Za-z0-9_]+)\s*=\s*([A-Za-z0-9_]+)') {
        $edgeLines += [pscustomobject]@{ To = $Matches[1].ToLowerInvariant(); From = $Matches[2].ToLowerInvariant() }
    }
}
if ($edgeLines.Count -eq 0) { Add-Violation 'S2' 'SignalLifecycle 无边' }
$terminal = @('completed', 'cancelled', 'expired', 'deadletter')
foreach ($s in $stateKeys) {
    $out = @($edgeLines | Where-Object { $_.From -eq $s })
    if ($terminal -contains $s) {
        if ($out.Count -gt 0) { Add-Violation 'S2' "终态 '$s' 不得有出边（实际 $($out.Count)）" }
    } elseif ($out.Count -eq 0) {
        Add-Violation 'S2' "非终态 '$s' 无出边（不可达终态）"
    }
}

# S3 判定表总函数形态
foreach ($item in @(
    @{ Id = 'signal-ack'; Path = $ackFile },
    @{ Id = 'signal-progress'; Path = $progressFile },
    @{ Id = 'signal-source-map'; Path = $sourceFile })) {
    $text = Read-Text $item.Path
    if ($text -notmatch [regex]::Escape("[Matrix] $($item.Id)")) { Add-Violation 'S3' "缺 [Matrix] $($item.Id)"; continue }
    $ruleCount = 0
    foreach ($line in ($text -split "`r?`n")) {
        $t = $line.Trim()
        if ($t -notmatch '^rule\s+\w+\s*\|') { continue }
        $ruleCount++
        foreach ($need in @('label=', 'priority=', 'when=')) {
            if ($t -notmatch [regex]::Escape($need)) { Add-Violation 'S3' "$($item.Id) rule 缺 '$need': $t" }
        }
    }
    if ($ruleCount -eq 0) { Add-Violation 'S3' "$($item.Id) 无 rule" }
}

# S4 六段流闭合
$steps = @()
$inFlow = $false
foreach ($line in ($flowText -split "`r?`n")) {
    $t = $line.Trim()
    if ($t -match '^\[Flow\]\s+SignalIntake') { $inFlow = $true; continue }
    if ($inFlow -and $t -match '^\[') { break }
    if ($inFlow -and $t -match '^step\s+([A-Za-z0-9_]+)') { $steps += $Matches[1] }
}
if ($steps.Count -eq 0) { Add-Violation 'S4' '[Flow] SignalIntake 无 step' }
foreach ($s in $steps) {
    if ($flowText -notmatch "(?m)^\s*\[Node\]\s+$([regex]::Escape($s))\s*:") { Add-Violation 'S4' "step '$s' 无 [Node]" }
}
foreach ($line in ($flowText -split "`r?`n")) {
    $t = $line.Trim()
    if ($t -match '^\[Branch\]') {
        foreach ($m in [regex]::Matches($t, '(?:then|from|to)=([A-Za-z0-9_]+)')) {
            $target = $m.Groups[1].Value
            if ($steps -notcontains $target) { Add-Violation 'S4' "[Branch] 目标 '$target' 不在步骤集" }
        }
    }
    if ($t -match '^\[When\]') {
        foreach ($m in [regex]::Matches($t, '(?:from|to)=([A-Za-z0-9_]+)')) {
            $target = $m.Groups[1].Value
            if ($steps -notcontains $target) { Add-Violation 'S4' "[When] 目标 '$target' 不在步骤集" }
        }
    }
}

# S5 观测契约
$required = @([regex]::Matches($nodeText, 'required=([^\r\n]+)') | ForEach-Object { $_.Groups[1].Value.Split(',') } | ForEach-Object { $_.Trim() })
foreach ($need in @('signal_id', 'signal_key', 'state', 'attempt')) {
    if ($required -notcontains $need) { Add-Violation 'S5' "[Obs] required 缺 '$need'" }
}
$enumStates = @([regex]::Matches($nodeText, 'enum=state:([^\r\n]+)') | ForEach-Object { $_.Groups[1].Value.Split(',') } | ForEach-Object { $_.Trim() })
foreach ($s in $enumStates) { if ($stateKeys -notcontains $s) { Add-Violation 'S5' "[Obs] enum=state '$s' 不在 SignalState 词表" } }

# S6 fn 常量登记
$fnValues = @([regex]::Matches($flowText, 'fn=([A-Za-z0-9_]+)') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
$constText = Read-Text $namesFile
$constValues = @([regex]::Matches($constText, '=\s*"([a-z0-9_]+)"') | ForEach-Object { $_.Groups[1].Value })
foreach ($fn in $fnValues) { if ($constValues -notcontains $fn) { Add-Violation 'S6' "fn '$fn' 未登记 WorkflowFunctionNames 常量" } }
if ($fnValues.Count -eq 0) { Add-Violation 'S6' '流内无 fn= 声明（正对照失效）' }

if ($violations.Count -eq 0) {
    Write-Host '[SignalDeclaration] OK（S1–S6 全过）'
    exit 0
}
Write-Host "[SignalDeclaration] 违规 $($violations.Count) 项："
foreach ($v in $violations) { Write-Host "  - $v" }
if ($Check) { exit 1 }
exit 0
