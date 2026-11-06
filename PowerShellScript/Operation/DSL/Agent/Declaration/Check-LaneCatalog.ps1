#requires -Version 5.1
<#
.SYNOPSIS
    车道目录守卫（LaneCatalog）—— lanes.ushar 的结构事实校验 + PS 域声明一致性。
.DESCRIPTION
    L1 声明面存在且非空 / L2 executor 必填且 fallback 可达 / L3 workflow 存在且拒绝运行期 plan /
    L4 降级车道须 allow_tools 且不得被意图命中 / L5 PS 子智能体走 workflow 且 accept_runtime_plan=false /
    L6 正负对照。默认报告；-Check 违规退出 1。只读仓库，零侧写。
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
$baseline = Import-GateBaseline -Name LaneCatalog
$minChecks = [int]$baseline.MinChecks.Value

$violations = [System.Collections.Generic.List[string]]::new()
$checks = 0

$lanesPath = Join-Path $repoRoot 'Projects\Plug\Agent\Config\policy\lanes.ushar'
$subagentsPath = Join-Path $repoRoot 'Projects\Plug\Agent\Config\agents\subagents.ushar'

function Read-Lanes {
    param([AllowEmptyString()][string]$Text)
    $result = @{}
    $cur = $null
    $fields = @{}
    foreach ($raw in ($Text -split "`r?`n")) {
        $t = $raw.Trim()
        if ($t.Length -eq 0 -or $t.StartsWith('#')) { continue }
        if ($t -match '^\[Lane\]\s*(?<id>[A-Za-z0-9_-]+)\s*:?\s*$') {
            if ($null -ne $cur) { $result[$cur] = $fields }
            $cur = $Matches['id']
            $fields = @{}
            continue
        }
        if ($null -eq $cur) { continue }
        if ($t.StartsWith('[')) { continue }
        $eq = $t.IndexOf('=')
        if ($eq -le 0) { continue }
        $fields[$t.Substring(0, $eq).Trim()] = $t.Substring($eq + 1).Trim()
    }
    if ($null -ne $cur) { $result[$cur] = $fields }
    return $result
}

function Test-Declarations {
    param([hashtable]$Lanes)
    $errors = [System.Collections.Generic.List[string]]::new()
    foreach ($id in $Lanes.Keys) {
        $lane = $Lanes[$id]
        if (-not $lane.ContainsKey('executor') -or [string]::IsNullOrWhiteSpace($lane['executor'])) {
            $errors.Add("L2 车道 '$id' 缺 executor")
        }
        if ($lane.ContainsKey('fallback')) {
            foreach ($next in ($lane['fallback'] -split '[,;\s]+' | Where-Object { $_ })) {
                if (-not $Lanes.ContainsKey($next)) { $errors.Add("L2 车道 '$id' fallback 悬空：'$next'") }
            }
        }
        if ($lane.ContainsKey('workflow') -and -not [string]::IsNullOrWhiteSpace($lane['workflow'])) {
            $wf = $lane['workflow']
            if (-not $wf.EndsWith('.ushar')) { $wf += '.ushar' }
            if (-not (Test-Path (Join-Path $repoRoot "Projects\Plug\Agent\Config\workflows\$wf"))) {
                $errors.Add("L3 车道 '$id' workflow 文件缺失：$wf")
            }
            if ($lane.ContainsKey('accept_runtime_plan') -and $lane['accept_runtime_plan'] -eq 'true') {
                $errors.Add("L3 车道 '$id' 声明 workflow 却允许 accept_runtime_plan=true")
            }
        }
        if ($lane.ContainsKey('degraded') -and $lane['degraded'] -eq 'true') {
            if (-not $lane.ContainsKey('allow_tools') -or [string]::IsNullOrWhiteSpace($lane['allow_tools'])) {
                $errors.Add("L4 降级车道 '$id' 未声明 allow_tools")
            }
        }
    }
    return ,$errors
}



if (-not (Test-Path -LiteralPath $lanesPath)) {
    $violations.Add("L1 车道声明缺失：$lanesPath")
} else {
    $text = Get-Content -LiteralPath $lanesPath -Raw
    $lanes = Read-Lanes -Text $text
    if ($lanes.Count -eq 0) { $violations.Add('L1 lanes.ushar 未声明任何 [Lane]（禁空载）') }
    $checks++

    $laneErrors = Test-Declarations -Lanes $lanes
    foreach ($e in $laneErrors) { $violations.Add($e) }
    $checks++

    if (Test-Path -LiteralPath $subagentsPath) {
        $subagents = Get-Content -LiteralPath $subagentsPath -Raw
        $intentLanes = [regex]::Matches($subagents, '(?m)^\s*lane\s*=\s*(?<lane>[A-Za-z0-9_-]+)\s*$') |
            ForEach-Object { $_.Groups['lane'].Value } | Select-Object -Unique
        foreach ($id in $lanes.Keys) {
            $lane = $lanes[$id]
            if ($lane.ContainsKey('degraded') -and $lane['degraded'] -eq 'true' -and $intentLanes -contains $id) {
                $violations.Add("L4 降级车道 '$id' 被意图声明命中（默认就降级）")
            }
        }
        $checks++

        foreach ($tag in @('powershell', 'powershell-forge')) {
            $m = [regex]::Match($subagents, "(?ms)^\[SubAgent\]\s*$([regex]::Escape($tag))\s*:\s*(?<body>.*?)(?=^\[|\z)")
            if (-not $m.Success) { $violations.Add("L5 子智能体 '$tag' 声明缺失"); continue }
            $body = $m.Groups['body'].Value
            if ($body -notmatch '(?m)^\s*step_kind\s*=\s*workflow\s*$') {
                $violations.Add("L5 子智能体 '$tag' step_kind 必须为 workflow（禁单步直调）")
            }
            if ($body -match '(?m)^\s*accept_runtime_plan\s*=\s*true\s*$') {
                $violations.Add("L5 子智能体 '$tag' accept_runtime_plan 必须为 false")
            }
        }
        $checks++
    }

    # L6 负对照：合成悬空 fallback 必须被同一判据命中（防判据恒真）
    $probe = @{}
    $probe['a'] = @{ executor = 'cline'; fallback = 'missing' }
    $probeErrors = Test-Declarations -Lanes $probe
    if (@($probeErrors | Where-Object { $_ -like 'L2 *fallback*' }).Count -eq 0) {
        $violations.Add('L6 负对照失效：悬空 fallback 未被判据命中')
    }
    $checks++
}

if ($checks -lt $minChecks) {
    $violations.Add("正对照失效：实际判据数 $checks < 基线 $minChecks（判据面漂移）")
}

if ($violations.Count -eq 0) {
    Write-Host "[OK] LaneCatalog：0 违规（判据数 $checks）" -ForegroundColor Green
    exit 0
}

Write-Host "[FAIL] LaneCatalog 命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0
