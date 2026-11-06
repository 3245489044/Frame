<#
.SYNOPSIS
    项目引用形态门禁（Gate=ProjectReferences）：跨子树 → CsharpPrj 必须 $(Proj_<项目名>)。

.DESCRIPTION
    判据载体 = csproj 的 ProjectReference Include 结构事实 + 解析后所属子树（禁执行者叙述；
    权威: .clinerules/axioms.md SEP.JudgmentCarrierStructural）。

    三段判据：
      P1 扫描面 = Frame + Projects 全部 csproj（与映射源 modules.json 解耦：清单外子树同样受检）
      P2 跨子树指向 CsharpPrj 的引用必须是 $(Proj_<项目名>)；悬空引用 = 0
      P3 正对照：扫描数 ≥ 基线（防解析面失效 ⇒ 棘轮恒真）

    唯一实现 = Share\Solution\Test-ProjectReferences.ps1（本脚本只做门禁契约转发 + 正对照，不重复判据；
    权威: .clinerules/axioms.md Strategy.SingleImplementation）。

    退出码：0 无违规 / 1 违规 / 2 前置缺失。
#>
[CmdletBinding()]
param([switch]$Check)

$ErrorActionPreference = 'Stop'

# 根定位（深度无关）：GateRuntime → DirectoryMap 以「Frame\ + Projects\」marker 上溯（迁 Environment 后不依赖脚本深度）
$__repoRoot = $PSScriptRoot
while ($__repoRoot -and -not ((Test-Path -LiteralPath (Join-Path $__repoRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $__repoRoot 'Projects'))) { $__repoRoot = Split-Path -Parent $__repoRoot }
if (-not $__repoRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $__repoRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
$root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot

$impl = Join-Path (Get-MappedPath -Key 'PowerShellScript') 'Share\Solution\Test-ProjectReferences.ps1'
$baselinePath = Join-Path (Get-MappedPath -Key 'Baselines') 'Meta\ProjectReferences.psd1'

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  项目引用形态守卫（ProjectReferences）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

if (-not (Test-Path -LiteralPath $impl)) {
    Write-Host "  [FAIL] 引用校验器缺失: $impl" -ForegroundColor Red
    exit 2
}
if (-not (Test-Path -LiteralPath $baselinePath)) {
    Write-Host "  [FAIL] 基线缺失: $baselinePath" -ForegroundColor Red
    exit 2
}

# P3 正对照：解析面必须真的枚举到工程（否则「0 违规」不代表健康）
Import-Module (Join-Path (Get-MappedPath -Key 'PowerShellScript') 'Share\Solution\ModulePaths.Common.psm1') -Force
$scan = @(Get-ReferenceScanProjects -FrameRoot (Get-WorkflowFrameRoot) -Roots @('Frame', 'Projects'))
$baseline = Import-PowerShellDataFile -LiteralPath $baselinePath
$minScan = [int]$baseline.ScanProjects.Value
Write-Host "  扫描面: $($scan.Count) 个 csproj（正对照下限 = $minScan）" -ForegroundColor Gray
if ($scan.Count -lt $minScan) {
    Write-Host "  [FAIL] 正对照失效：扫描数 $($scan.Count) < 基线 $minScan（解析面坍缩 ⇒ 禁判绿）" -ForegroundColor Red
    exit 1
}

& $impl -Check
exit $LASTEXITCODE
