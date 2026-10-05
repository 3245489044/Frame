#requires -Version 5.1
<#
.SYNOPSIS
    验证成比例守卫（VerifyProportionality）—— 验证面须 = 受影响工程面（过程层缺陷 P4）。

.DESCRIPTION
    根因（修复文档 §1-A/§2）：验证面粗粒度 —— `Get-VerifyPlan` 早前按**路径前缀**映射，
    `Projects\*` 一律“全矩阵”，即成「改 1 工程跑全矩阵」（验证成本与改动无关）。
    本件把「验证与改动成比例」变成机器判据：以 `Get-VerifyPlan` 的工程影响面映射为标的面。

    判据（H0–H5）：
      H0 正对照：样本数 ≥ 基线（判据无标的 ⇒ 假绿）。
      H1 成比例：产品单工程改动的「验证触及工程数 ÷ 改动涉及工程数」≤ RatioMax（对齐 §3.2 ≤ 1.5）。
      H2 映射落点：产品单工程改动须映射到具体测试工程（不得回落全矩阵）。
      H3 不静默丢面：任一改动须被分类（映射 ∧ 或 ∧ 全矩阵 ∧ 或 ∧ Unmapped），禁「既不映射也不报」。
      H4 负对照：不可分类改动须判 Unmapped（fail-loud，≠「全部」，禁静默全量）。
      H5 正对照：框架改动仍产出计划（豁免面不空转）。

    模式：默认报告模式（不设退出码）；-Check 校验模式（违规 → 退出码 1）。
    依赖方向（硬约束）：本件只读仓库；零侧写（调用 Get-VerifyPlan 属诊断相位，零运行）。

.PARAMETER Changed
    本批改动文件（仓库相对路径或绝对路径；多值）。给定时额外复算本批比值（产品面超界即红）。
.PARAMETER Check
    校验模式：违规 → 退出码 1。
.PARAMETER ShowDetail
    输出逐样本实测（改动域 / 验证触及 / 比值 / 备注）。
#>
[CmdletBinding()]
param(
    [string[]]$Changed = @(),
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'

# 定位仓库根（含 Projects\ 与 Frame\）—— 相对 $PSScriptRoot 上溯，禁硬编码绝对路径
$repoRoot = $PSScriptRoot
while ($repoRoot -and $repoRoot -ne (Split-Path $repoRoot -Parent)) {
    if ((Test-Path (Join-Path $repoRoot 'Projects')) -and (Test-Path (Join-Path $repoRoot 'Frame'))) { break }
    $repoRoot = Split-Path $repoRoot -Parent
}

Import-Module (Join-Path $repoRoot 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline   = Import-GateBaseline -Name VerifyProportionality
$ratioMax   = [double]$baseline.RatioMax.Value
$minSamples = [int]$baseline.MinSamples.Value

$violations = [System.Collections.Generic.List[string]]::new()
$planScript = Join-Path $repoRoot 'Environment\CentralTest\Get-VerifyPlan.ps1'

# ---------------- 工具：验证计划（诊断相位；调用 Get-VerifyPlan，零运行） ----------------
function Get-VerifyPlanData {
    param([Parameter(Mandatory = $true)][string[]]$Files)
    $raw = & $planScript -Changed $Files -Json 2>$null
    return (($raw | Out-String) | ConvertFrom-Json)
}

# 工程域（结构事实）：产品工程 = Agent.<X>；框架/环境按顶层域归并
function Get-EngineeringDomain {
    param([Parameter(Mandatory = $true)][string]$Rel)
    if ($Rel -match '^Projects\\Plug\\Agent\\src\\(?<d>Agent\.[A-Za-z0-9\.]+)\\') { return $Matches['d'] }
    if ($Rel -match '^Frame\\CsharpPrj\\')       { return 'CsharpPrj' }
    if ($Rel -match '^Frame\\Usharp\\')          { return 'Usharp' }
    if ($Rel -match '^Frame\\TypeScriptPrj\\')   { return 'TypeScriptPrj' }
    if ($Rel -match '^Frame\\PowerShellScript\\'){ return 'PowerShellScript' }
    if ($Rel -match '^Frame\\Invoke\\')          { return 'Invoke' }
    if ($Rel -match '^Environment\\CentralTest\\'){ return 'CentralTest' }
    if ($Rel -match '^\.clinerules\\')           { return '.clinerules' }
    if ($Rel -match '^Docs\\')                   { return 'Docs' }
    return (($Rel -split '\\')[0])
}

# 比例度量：验证触及工程数 ÷ 改动涉及工程数；全矩阵 ≈ +∞
function Get-PlanMetrics {
    param([Parameter(Mandatory = $true)]$Plan, [Parameter(Mandatory = $true)][string[]]$Files)
    $domains = @($Files | ForEach-Object { Get-EngineeringDomain -Rel ([string]$_ -replace '/', '\') } | Sort-Object -Unique)
    $verifyCount = if ($Plan.TestSet) { [double]::PositiveInfinity } else { [double]@($Plan.TestProjects).Count }
    $ratio = if ($domains.Count -eq 0) { [double]::PositiveInfinity } else { $verifyCount / $domains.Count }
    return @{ Domains = $domains; VerifyCount = $verifyCount; Ratio = $ratio }
}

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  验证成比例守卫（VerifyProportionality：验证面 ∝ 改动面）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

if (-not (Test-Path -LiteralPath $planScript)) {
    Write-Host "[FAIL] Get-VerifyPlan 缺件（映射标的不存在）：$planScript" -ForegroundColor Yellow
    if ($Check) { exit 1 }
    exit 0
}

# ---------------- 样本（判据标的面；structural facts，非执行者叙述） ----------------
$samples = @(
    @{ Id = 'A'; Kind = 'proportional'; Files = @('Projects\Plug\Agent\src\Agent.Modules\Signal\__probe__.cs') }
    @{ Id = 'B'; Kind = 'not-dropped';  Files = @('Projects\Plug\Agent\src\Agent.Contract\__probe__.cs') }
    @{ Id = 'C'; Kind = 'unmapped';     Files = @('Frame\__scope_probe__\y.txt') }
    @{ Id = 'D'; Kind = 'framework';    Files = @('Frame\PowerShellScript\Operation\DSL\Check-BatchScope.ps1') }
)

if ($samples.Count -lt $minSamples) {
    $violations.Add("[H0] 正对照失效：样本数 $($samples.Count) < 基线 $minSamples（判据无标的）")
}

foreach ($s in $samples) {
    $plan = Get-VerifyPlanData -Files $s.Files
    $m = Get-PlanMetrics -Plan $plan -Files $s.Files
    $tp = @($plan.TestProjects).Count
    if ($ShowDetail) {
        Write-Host ("  样本 {0} [{1}] 改动域={2} 验证触及={3} 比值={4}" -f `
                $s.Id, $s.Kind, $m.Domains.Count, $(if ($plan.TestSet) { '全矩阵' } else { $tp }),
                $(if ([double]::IsPositiveInfinity($m.Ratio)) { '∞' } else { [math]::Round($m.Ratio, 2) })) -ForegroundColor DarkGray
    }
    switch ($s.Kind) {
        'proportional' {
            if ($tp -lt 1) { $violations.Add("[H2] 样本 $($s.Id) 未映射到具体测试工程（回落全矩阵）: $($s.Files -join ', ')") }
            if ($plan.TestSet) { $violations.Add("[H2] 样本 $($s.Id) 触发全矩阵（产品单工程改动应成比例）") }
            if ($m.Ratio -gt $ratioMax) { $violations.Add("[H1] 样本 $($s.Id) 验证/改动比超界（> $ratioMax）") }
        }
        'not-dropped' {
            if (-not $plan.TestSet -and $tp -lt 1 -and @($plan.Unmapped).Count -lt 1) {
                $violations.Add("[H3] 样本 $($s.Id) 被静默丢弃（既未映射、也未全矩阵、也非 Unmapped）: $($s.Files -join ', ')")
            }
        }
        'unmapped' {
            if (@($plan.Unmapped).Count -lt 1) { $violations.Add("[H4] 负对照失效：未分类改动未判 Unmapped（fail-loud 失效）") }
        }
        'framework' {
            if ($null -eq $plan) { $violations.Add("[H5] 正对照失效：框架改动未产出计划") }
        }
    }
}

# ---------------- 本批复算（-Changed 给定时） ----------------
if (@($Changed).Count -gt 0) {
    $relChanged = @($Changed | ForEach-Object { ([string]$_ -replace '/', '\').TrimStart('\') })
    $plan = Get-VerifyPlanData -Files $relChanged
    $m = Get-PlanMetrics -Plan $plan -Files $relChanged
    $productOnly = (@($relChanged | Where-Object { $_ -match '^Projects\\' }).Count -eq $relChanged.Count)
    Write-Host ("[本批] 改动域 {0} | 验证触及 {1} | 比值 {2}" -f $m.Domains.Count, $(if ($plan.TestSet) { '全矩阵' } else { @($plan.TestProjects).Count }),
            $(if ([double]::IsPositiveInfinity($m.Ratio)) { '∞' } else { [math]::Round($m.Ratio, 2) })) -ForegroundColor Gray
    if ($productOnly -and $m.Ratio -gt $ratioMax) {
        $violations.Add("[H1] 本批（产品面）验证/改动比超界（> $ratioMax）：改动 $($m.Domains -join ', ')")
    }
}

# ---------------- 报告 ----------------
Write-Host ("样本数: {0} | 比例上限: {1}" -f $samples.Count, $ratioMax) -ForegroundColor Gray

if ($violations.Count -eq 0) {
    Write-Host ''
    Write-Host '[OK] VerifyProportionality：0 违规（产品单工程改动成比例 ∧ 未分类改动 fail-loud）' -ForegroundColor Green
    exit 0
}

Write-Host ''
Write-Host "[FAIL] VerifyProportionality 命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
Write-Host '  修复口径：细化 Get-VerifyPlan 的工程影响面映射（复用 TestTargets 别名表）；不确定即并入（保守全矩阵）' -ForegroundColor Gray
if ($Check) { exit 1 }
exit 0

