#requires -Version 5.1
<#
.SYNOPSIS
    规格工具链契约守卫（SpecToolingContract）—— Tools\**\*.ps1 的结构事实下限。

.DESCRIPTION
    背景（方案 §4.2 R2）：Tools\ 下 25 件工具的「可发现性 / 确定性退出 / 无深度计数」三项此前靠约定，
    无机器事实。本件把契约落成可判定判据（载体 = 结构事实：文本形态 / 文件计数）。

    判据：
      C1 可发现性：每件工具含 .SYNOPSIS 块（`<#` + `.SYNOPSIS`）；
      C2 确定性退出：每件工具含 `exit` 语句（`^\s*exit\b`），存量未达标件须在基线 ExitExempt 在册（Debt 棘轮，只减不增）；
      C3 无深度计数（红线）：不得出现 `$PSScriptRoot\..\..` 一类目录深度计数（应经 SpecPaths 基座）；
      U 正/负对照：扫描面 ≥ 基线 MinTools；合成样本判据行为正确。

    模式：默认报告模式；-Check 时违规 → 退出码 1。

.PARAMETER Root
    仓库根目录（缺省由本脚本位置上溯四级推导）。

.PARAMETER Check
    校验模式：违规 → 退出码 1。

.PARAMETER ShowDetail
    输出逐条实测落点。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$b = Import-GateBaseline -Name SpecToolingContract

$violations = [System.Collections.Generic.List[string]]::new()
$pending = [System.Collections.Generic.List[string]]::new()

$toolsRoot = Join-Path $Root 'Environment\CentralTest\Tools'
$minTools = [int]$b.MinTools.Value
$exitExempt = @($b.ExitExempt.Value)

if (-not (Test-Path -LiteralPath $toolsRoot)) {
    $violations.Add("[C0] 工具根不存在：$toolsRoot")
}

$tools = @(Get-ChildItem -LiteralPath $toolsRoot -Recurse -File -Filter '*.ps1' -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\lib\\' -and $_.FullName -notmatch '\\_deprecated\\' })

$rxSynopsis = '\.SYNOPSIS'
$rxExit = '(?m)^\s*exit\b'
$rxDepth = '\$PSScriptRoot[\s\\/''"]*\.\.[\\/]+\.\.[\\/]'

foreach ($tool in $tools) {
    $content = Get-Content -LiteralPath $tool.FullName -Raw

    if ($content -notmatch $rxSynopsis) {
        $violations.Add("[C1] 缺 .SYNOPSIS：$($tool.Name)")
    }

    if ($content -notmatch $rxExit) {
        if ($tool.Name -in $exitExempt) {
            $pending.Add("[C2][Debt] 缺确定性 exit（在册豁免）：$($tool.Name)")
        } else {
            $violations.Add("[C2] 缺确定性 exit：$($tool.Name)")
        }
    }

    if ($content -match $rxDepth) {
        $violations.Add("[C3] 出现目录深度计数（应经 SpecPaths 基座）：$($tool.Name)")
    }
}

# ---- 正/负对照（判据自证） ----
if ($tools.Count -lt $minTools) {
    $violations.Add("[U] 扫描面 $($tools.Count) < 下限 $minTools（判据无标的假绿）")
}
$posDepth = "$" + "PSScriptRoot\..\..\Environment\X.ps1"
if ($posDepth -notmatch $rxDepth) { $violations.Add('[U] 正对照失败：深度计数样本未被捕获') }
$negDepth = "Join-Path $" + "PSScriptRoot '..\lib\X.psm1'"
if ($negDepth -match $rxDepth) { $violations.Add('[U] 负对照失败：单层 sibling 路径被误捕') }

# ---- 报告 ----
Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  规格工具链契约守卫（SpecToolingContract）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host "  扫描面：$toolsRoot · $($tools.Count) 件（下限 $minTools）" -ForegroundColor Gray
Write-Host "  豁免在册（缺 exit，Debt）：$($pending.Count) 件" -ForegroundColor Gray

if ($ShowDetail -and $pending.Count -gt 0) {
    Write-Host '  --- 债务台账（不拦；只减不增） ---' -ForegroundColor DarkGray
    foreach ($p in $pending) { Write-Host "    - $p" -ForegroundColor DarkGray }
}

if ($violations.Count -eq 0) {
    Write-Host ''
    Write-Host '[OK] SpecToolingContract：0 违规（SYNOPSIS / 确定性退出 / 无深度计数 / 正负对照绿）' -ForegroundColor Green
    exit 0
}

Write-Host ''
Write-Host "[FAIL] SpecToolingContract 命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0
