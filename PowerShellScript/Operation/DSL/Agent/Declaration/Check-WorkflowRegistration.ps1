#requires -Version 5.1
<#
.SYNOPSIS
    工作流登记守卫（WorkflowRegistration）—— 登记表（单源）⇄ 声明文件 ⇄ 投影常量 三方一致。

.DESCRIPTION
    判据（W1–W6，2026-11-06 改为以 WorkflowRegistry 单源取数）：
      W1 登记面可解析：WorkflowRegistry.cs 的 Row(...) 全部取出且逻辑键/文件名唯一；
                       WorkflowSpecCatalog.cs 中 WorkflowRegistry.FileName("workflows/<x>") 投影必须已登记。
      W2 流号唯一：同一 [Flow] 流号不得登记两次。
      W3 声明面存在：每个登记 basename 对应 Config/workflows/**/<basename>.ushar（递归；_deprecated 排除）。
      W4 流号存在：该文件含 `[Flow] <flowId>`（治「声明改了不生效」）。
      W5 未登记声明面：Config/workflows/**/*.ushar ∖ 登记集合，须在基线豁免清单内（显式豁免）。
      W6 正/负对照：登记数 ≥ 基线下限；集合比较函数对合成差异样本必须报出。

    模式：默认报告模式（不设退出码）；-Check 校验模式（违规 → 退出码 1）。
    依赖方向（硬约束）：本件只读仓库；零侧写（不做 B6 派生改写，仅对账）。
#>
[CmdletBinding()]
param(
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

# 定位仓库根（含 Projects\ 与 Frame\）—— 相对 $PSScriptRoot 上溯，禁硬编码绝对路径
$repoRoot = Get-GateRepoRoot -ScriptRoot $PSScriptRoot

Import-Module (Join-Path $repoRoot 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name WorkflowRegistration
$minRegistrations = [int]$baseline.MinRegistrations.Value
$unregisteredExempt = @($baseline.UnregisteredExempt.Value)

$violations = [System.Collections.Generic.List[string]]::new()

$registryPath = Join-Path $repoRoot 'Projects\Plug\Agent\src\Agent.Core\Configuration\Specs\WorkflowRegistry.cs'
$specPath = Join-Path $repoRoot 'Projects\Plug\Agent\src\Agent.Core\Configuration\Specs\WorkflowSpecCatalog.cs'
$workflowsDir = Join-Path $repoRoot 'Projects\Plug\Agent\Config\workflows'

function Get-SetDiff {
    param([string[]]$Left, [string[]]$Right)
    return @($Left | Where-Object { $Right -notcontains $_ } | Sort-Object)
}

function Find-WorkflowFile {
    param([string]$BaseName, [string]$RelativeFile)
    if (-not [string]::IsNullOrWhiteSpace($RelativeFile)) {
        $registered = Join-Path (Join-Path $repoRoot 'Projects\Plug\Agent') ($RelativeFile -replace '/', '\')
        if (Test-Path -LiteralPath $registered) { return $registered }
    }
    $direct = Join-Path $workflowsDir "$BaseName.ushar"
    if (Test-Path -LiteralPath $direct) { return $direct }
    if (-not (Test-Path -LiteralPath $workflowsDir)) { return $null }
    return (Get-ChildItem -LiteralPath $workflowsDir -Recurse -Filter "$BaseName.ushar" -File |
        Where-Object { $_.FullName -notmatch '\\_deprecated\\' } |
        Sort-Object -Property FullName |
        Select-Object -First 1 -ExpandProperty FullName)
}

if (-not (Test-Path -LiteralPath $registryPath)) {
    Write-Host "[FAIL] 登记表单源缺失：$registryPath" -ForegroundColor Yellow
    if ($Check) { exit 1 }
    exit 0
}

$text = Get-Content -LiteralPath $registryPath -Raw -Encoding UTF8

# ---- 取数：Row("basename", "FlowId", Modules.X, null|GateKeys.X, true|false [, "relative/path.ushar"]) ----
$rows = [System.Collections.Generic.List[object]]::new()
$rowPattern = 'Row\(\s*"(?<b>[^"]+)"\s*,\s*"(?<f>[^"]+)"\s*,\s*Modules\.(?<m>\w+)\s*,\s*(?<g>null|GateKeys\.\w+)\s*,\s*(?<v>true|false)\s*(?:,\s*"(?<r>[^"]+)")?\s*\)'
foreach ($m in [regex]::Matches($text, $rowPattern)) {
    $rows.Add([pscustomobject]@{
        BaseName     = $m.Groups['b'].Value
        FlowId       = $m.Groups['f'].Value
        Module       = $m.Groups['m'].Value
        Validate     = $m.Groups['v'].Value -eq 'true'
        RelativeFile = $m.Groups['r'].Value
    })
}

if ($rows.Count -eq 0) {
    $violations.Add('W1 取数面失效：WorkflowRegistry.cs 的 Row(...) 未匹配（判据无标的）')
}

$baseNames = @($rows.BaseName | Sort-Object -Unique)
$flowIds = @($rows.FlowId | Sort-Object -Unique)

# ---- W1 登记面唯一 + 投影一致 ----
foreach ($dup in ($rows | Group-Object BaseName | Where-Object Count -gt 1)) {
    $violations.Add("W1 登记重复：$($dup.Name)（$($dup.Count) 行）")
}
foreach ($dup in ($rows | Group-Object FlowId | Where-Object Count -gt 1)) {
    $violations.Add("W2 流号重复：$($dup.Name)（$($dup.Count) 行）")
}

if (Test-Path -LiteralPath $specPath) {
    $specText = Get-Content -LiteralPath $specPath -Raw -Encoding UTF8
    foreach ($m in [regex]::Matches($specText, 'WorkflowRegistry\.FileName\(\s*"workflows/(?<b>[^"]+)"\s*\)')) {
        $b = $m.Groups['b'].Value
        if ($baseNames -notcontains $b) { $violations.Add("W1 投影引用未登记工作流：workflows/$b（WorkflowSpecCatalog）") }
    }
}
else {
    $violations.Add("W1 投影面缺失：$specPath")
}

# ---- W3/W4 声明面存在 + 流号在位 ----
foreach ($row in $rows) {
    $file = Find-WorkflowFile -BaseName $row.BaseName -RelativeFile $row.RelativeFile
    if (-not $file) {
        $violations.Add("W3 声明文件缺失：Config/workflows/**/$($row.BaseName).ushar（已登记）")
        continue
    }
    if ((Get-Content -LiteralPath $file -Raw -Encoding UTF8) -notmatch ('(?m)^\[Flow\]\s+' + [regex]::Escape($row.FlowId) + '\b')) {
        $violations.Add("W4 声明文件未含 [Flow] $($row.FlowId)：$($row.BaseName).ushar")
    }
}

# ---- W5 未登记声明面（显式豁免；递归，_deprecated 排除）----
if (Test-Path -LiteralPath $workflowsDir) {
    $declared = @(Get-ChildItem -LiteralPath $workflowsDir -Recurse -Filter '*.ushar' -File |
        Where-Object { $_.FullName -notmatch '\\_deprecated\\' } |
        ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_.Name) } |
        Sort-Object -Unique)
    foreach ($b in (Get-SetDiff -Left $declared -Right $baseNames)) {
        if ($unregisteredExempt -notcontains $b) {
            $violations.Add("W5 未登记声明面：$b.ushar（登记集合缺，且不在豁免基线）")
        }
    }
}

# ---- W6 正/负对照 ----
if ($baseNames.Count -lt $minRegistrations) {
    $violations.Add("W6 正对照失效：登记数 $($baseNames.Count) < 基线 $minRegistrations")
}
$syntheticDiff = Get-SetDiff -Left @('a', 'b') -Right @('a')
if ($syntheticDiff.Count -ne 1 -or $syntheticDiff[0] -ne 'b') {
    $violations.Add('W6 负对照失效：集合比较函数未报出合成差异样本')
}

if ($violations.Count -eq 0) {
    Write-Host ''
    Write-Host ("[OK] WorkflowRegistration：0 违规（登记 {0} 条；声明文件在位 ∧ 流号一致 ∧ 豁免 {1}）" -f $baseNames.Count, ($unregisteredExempt -join ',')) -ForegroundColor Green
    exit 0
}

Write-Host ''
Write-Host "[FAIL] WorkflowRegistration 命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
Write-Host '  修复口径：工作流只登记于 WorkflowRegistry.Row(...)（逻辑键/流号/模块/门控/校验位）；搬迁只改可选 relativeFile。' -ForegroundColor Gray
if ($Check) { exit 1 }
exit 0

