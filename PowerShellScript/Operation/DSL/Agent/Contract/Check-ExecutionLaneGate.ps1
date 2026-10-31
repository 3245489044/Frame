#requires -Version 5.1
<#
.SYNOPSIS
    执行车道门（ExecutionLaneGate）—— INV-2（声明 workflow 的领域不得同时声明单步直调）+ 默认车道能力门。
.DESCRIPTION
    N1 含 workflow= 的 [SubAgent] 不得含 step_kind = llm（同语义两条路径）。
    N2 default 车道必须 deny_tools 含 run_commands（最小权限默认面）。
    N3 包含 workflow 的 [SubAgent] 必须 accept_runtime_plan=false（不能被输入 plan 覆盖声明流）。
    N4 负对照：合成的「workflow + llm」样本必须被同一判据命中。
    默认报告；-Check 违规退出 1。只读仓库，零侧写。
#>
[CmdletBinding()]
param([switch]$Check)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking


$repoRoot = Get-GateRepoRoot -ScriptRoot $PSScriptRoot

Import-Module (Join-Path $repoRoot 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name ExecutionLaneGate
$minChecks = [int]$baseline.MinChecks.Value

$violations = [System.Collections.Generic.List[string]]::new()
$checks = 0

$subagentsPath = Join-Path $repoRoot 'Projects\Plug\Agent\Config\subagents.ushar'
$lanesPath = Join-Path $repoRoot 'Projects\Plug\Agent\Config\lanes.ushar'

function Get-SubAgentBlocks {
    param([AllowEmptyString()][string]$Text)
    $blocks = @{}
    $matches = [regex]::Matches($Text, '(?ms)^\[SubAgent\]\s*(?<tag>[A-Za-z0-9_-]+)\s*:\s*(?<body>.*?)(?=^\[|\z)')
    foreach ($m in $matches) { $blocks[$m.Groups['tag'].Value] = $m.Groups['body'].Value }
    return $blocks
}

function Test-Block {
    param([string]$Tag, [string]$Body)
    $errors = [System.Collections.Generic.List[string]]::new()
    $hasWorkflow = $Body -match '(?m)^\s*workflow\s*=\s*\S+'
    if ($hasWorkflow -and $Body -match '(?m)^\s*step_kind\s*=\s*llm\s*$') {
        $errors.Add("N1 子智能体 '$Tag' 同时声明 workflow 与 step_kind=llm（同一领域两条路径）")
    }
    if ($hasWorkflow -and $Tag -match '^powershell' -and $Body -match '(?m)^\s*accept_runtime_plan\s*=\s*true\s*$') {
        $errors.Add("N3 子智能体 '$Tag' 声明 workflow 却允许 accept_runtime_plan=true（PS 域禁运行期 plan 覆盖）")
    }
    return ,$errors
}

if (-not (Test-Path -LiteralPath $subagentsPath)) {
    $violations.Add("N1 声明面缺失：$subagentsPath")
} else {
    $text = Get-Content -LiteralPath $subagentsPath -Raw
    $blocks = Get-SubAgentBlocks -Text $text
    foreach ($tag in $blocks.Keys) {
        foreach ($e in (Test-Block -Tag $tag -Body $blocks[$tag])) { $violations.Add($e) }
    }
    $checks++

    # N4 负对照：合成样本必须被同一判据命中（workflow + 单步 llm 互斥）
    $probe = Test-Block -Tag 'powershell-probe' -Body "workflow = x`nstep_kind = llm`n"
    if (@($probe).Count -eq 0) { $violations.Add('N4 负对照失效：workflow+llm 合成样本未被判据命中') }
    $checks++
}

if (-not (Test-Path -LiteralPath $lanesPath)) {
    $violations.Add("N2 车道声明缺失：$lanesPath")
} else {
    $lanes = Get-Content -LiteralPath $lanesPath -Raw
    $m = [regex]::Match($lanes, '(?ms)^\[Lane\]\s*default\s*:\s*(?<body>.*?)(?=^\[Lane\]|\z)')
    if (-not $m.Success) {
        $violations.Add('N2 lanes.ushar 缺 default 车道')
    } else {
        $body = $m.Groups['body'].Value
        $deny = [regex]::Match($body, '(?m)^\s*deny_tools\s*=\s*(?<v>.+)$')
        if (-not $deny.Success -or $deny.Groups['v'].Value -notmatch 'run_commands') {
            $violations.Add('N2 default 车道未 deny run_commands（最小权限默认面缺失）')
        }
    }
    $checks++
}

if ($checks -lt $minChecks) {
    $violations.Add("正对照失效：实际判据数 $checks < 基线 $minChecks（判据面漂移）")
}

if ($violations.Count -eq 0) {
    Write-Host "[OK] ExecutionLaneGate：0 违规（判据数 $checks）" -ForegroundColor Green
    exit 0
}

Write-Host "[FAIL] ExecutionLaneGate 命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0
