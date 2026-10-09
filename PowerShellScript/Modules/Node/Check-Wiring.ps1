#requires -Version 5.1
<#
.SYNOPSIS
    接线生命周期守卫（Wiring）—— 声明状态 ⇔ 实现事实 一致性核对（W = ⟨D,C,F,φ,r⟩）。

.DESCRIPTION
    《统一节点框架-接线本质与彻底重构-方案》§4：接线单元 W = ⟨D,C,F,φ,r⟩（声明面/消费点/回退面/φ/r）；
    生命周期 = declared → primary → retired（本项目口径：**不做 dual-read 运行期双读**，φ 由既有等价门离线承担）。
    判据载体 = 结构事实（D/F 存在性 + C 读面探针），不引执行者叙述（SEP.JudgmentCarrierStructural）。
    注册表单源 = Baselines\Wiring.psd1（禁散落；本脚本**不重写** φ/E 判据，只做状态一致性核对）。

    判据：
      declared：D 存在 ∧（若给 CProbe）C 未读 D（Expect=notRead）
      primary ：D 存在 ∧（F 在册 ⇒ F 存在）∧ E 判据在册（非空）∧（若给 CProbe）C 主读 D（Expect=read）
      retired ：D 存在（唯一源）∧ F 不存在（若给 F）∧（若给 CProbe）C 主读 D
      正对照 ：注册单元数 ≥ UnitFloor（注册表被掏空 ⇒ 假绿）
      负对照 ：合成 primary∧E缺 / retired∧F仍在 必须被同一判据函数命中

    模式：默认报告模式；-Check 违规 → 退出码 1。

.PARAMETER Root
    仓库根目录（缺省由脚本位置上溯四级推导：Frame\PowerShellScript\Modules\Node → 仓库根）。
.PARAMETER Check
    校验模式：存在违规 → 退出码 1。
.PARAMETER ShowDetail
    输出逐单元四态分布。

.EXAMPLE
    .\Check-Wiring.ps1 -ShowDetail

.EXAMPLE
    .\Check-Wiring.ps1 -Check
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'NodeGateCommon.ps1')
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

# 注册表单源 = Baselines\Wiring.psd1（禁内联；U7 接线一致性由 Check-GateBaselines 强制）
Import-Module (Join-Path $PSScriptRoot '..\..\Baselines\GateBaseline.psm1') -Force
$w = Import-GateBaseline -Name Wiring
$unitFloor = [int]$w.UnitFloor.Value

$violations = [System.Collections.Generic.List[string]]::new()

Write-GateHeader '接线生命周期守卫（Wiring：declared → primary → retired）'

<# 单单元状态一致性核对（正/负对照共用同一实现）。 #>
function Test-WiringUnit {
    param([string]$RepoRoot, $Unit)
    $issues = [System.Collections.Generic.List[string]]::new()
    $id = [string]$Unit.Value
    if ([string]::IsNullOrWhiteSpace($id)) { $id = '<no-id>' }

    $state = [string]$Unit.State
    if ($state -notin @('declared', 'primary', 'retired')) {
        $issues.Add("[状态非法] $id：State='$state'（须 ∈ {declared,primary,retired}；dual-read 已按项目口径废除）")
        return , $issues
    }

    # D 存在性（所有态：声明面须在）
    $d = [string]$Unit.D
    if (-not [string]::IsNullOrWhiteSpace($d) -and -not (Test-Path -LiteralPath (Join-Path $RepoRoot $d))) {
        $issues.Add("[D 缺失] $id：$d")
    }

    # F 回退面存在性（primary 须在册；retired 须不存在）
    $f = [string]$Unit.F
    $fExists = (-not [string]::IsNullOrWhiteSpace($f)) -and (Test-Path -LiteralPath (Join-Path $RepoRoot $f))
    if ($state -eq 'primary' -and -not [string]::IsNullOrWhiteSpace($f) -and -not $fExists) {
        $issues.Add("[F 缺失] $id：primary 须保留回退面 $f")
    }
    if ($state -eq 'retired' -and $fExists) {
        $issues.Add("[越级退役] $id：retired 但回退面仍在 $f")
    }

    # E 存在性判据在册（primary 须在册；缺 ⇒ 假转正）
    if ($state -eq 'primary' -and [string]::IsNullOrWhiteSpace([string]$Unit.E)) {
        $issues.Add("[假转正] $id：primary 但 E 判据缺（未声明存在性判据）")
    }

    # C 读面探针（结构事实：源码命中/不命中）
    $probeFile = [string]$Unit.CProbeFile
    if (-not [string]::IsNullOrWhiteSpace($probeFile)) {
        $probePath = Join-Path $RepoRoot $probeFile
        if (-not (Test-Path -LiteralPath $probePath)) {
            $issues.Add("[CProbe 缺失] $id：探针文件 $probeFile")
        } else {
            $hit = (Get-Content -LiteralPath $probePath -Raw) -match [string]$Unit.CProbePattern
            $expect = [string]$Unit.CProbeExpect
            if ($expect -eq 'read' -and -not $hit) {
                $issues.Add("[C 未接线] $id：primary 但 C 未命中 D 读面探针（$probeFile !~ $($Unit.CProbePattern)）")
            }
            if ($expect -eq 'notRead' -and $hit) {
                $issues.Add("[状态漂移] $id：declared 但 C 已命中 D 读面探针（$probeFile ~ $($Unit.CProbePattern)）")
            }
        }
    }

    return , $issues
}

$distribution = [ordered]@{ declared = 0; primary = 0; retired = 0 }
$units = @($w.Units)
foreach ($unit in $units) {
    $state = [string]$unit.State
    if ($distribution.Contains($state)) { $distribution[$state]++ }
    foreach ($issue in (Test-WiringUnit -RepoRoot $Root -Unit $unit)) { $violations.Add($issue) }
}

# 正对照：注册单元数 ≥ 基线（注册表被掏空 / 扫描面失效 ⇒ 恒绿）
if ($units.Count -lt $unitFloor) {
    $violations.Add("正对照失效：注册单元 $($units.Count) < 下限 $unitFloor（注册表被掏空或未装载？）")
}

# 负对照：合成 primary∧E缺 / retired∧F仍在 必须被同一判据函数命中（存在路径用注册表自身）
$selfPath = 'Frame\PowerShellScript\Baselines\Wiring.psd1'
$neg1 = Test-WiringUnit -RepoRoot $Root -Unit @{ Value = '__neg_primary_no_e__'; State = 'primary'; D = $selfPath; F = $selfPath; E = '' }
if (@($neg1 | Where-Object { $_ -match '假转正' }).Count -eq 0) {
    $violations.Add('负对照失效：合成 primary∧E缺 未被命中（判据恒真？）')
}
$neg2 = Test-WiringUnit -RepoRoot $Root -Unit @{ Value = '__neg_retired_with_f__'; State = 'retired'; D = $selfPath; F = $selfPath; E = '' }
if (@($neg2 | Where-Object { $_ -match '越级退役' }).Count -eq 0) {
    $violations.Add('负对照失效：合成 retired∧F仍在 未被命中（判据恒真？）')
}

if ($ShowDetail) {
    Write-Host ("  实测：注册单元 {0}（declared {1} / primary {2} / retired {3}），下限 {4}" -f `
        $units.Count, $distribution['declared'], $distribution['primary'], $distribution['retired'], $unitFloor)
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'Wiring'
