#requires -Version 5.1
<#
.SYNOPSIS
    框架激活守卫（FrameworkActivation）—— 防止 ReferenceGraphEngine 退回死代码（零装配点 ⇒ 图引擎空转）。

.DESCRIPTION
    基线 = Baselines\FrameworkActivation.psd1（GraphEngineAssemblies.Value = 装配点下限）。
    FA1 扫描 `new ReferenceGraphEngine` 命中数（全仓 .cs，排除 obj/bin/BuildOutput）；
    命中 < 基线 ⇒ 图引擎空转（违归：退回死代码）。

    模式：默认报告模式；-Check 校验模式（违归 → 退出码 1）。

.PARAMETER Root
    仓库根目录（默认由本脚本位置上溯四级推导）。

.PARAMETER Check
    校验模式：违归 → 退出码 1。

.PARAMETER ShowDetail
    输出逐命中点明细。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root))
{
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$violations = [System.Collections.Generic.List[string]]::new()

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  框架激活守卫（图引擎装配点 ≥ 基线）" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

# 基线单源 = Baselines\FrameworkActivation.psd1（禁内联数值基线；守卫 = Operation\DSL\Check-GateBaselines.ps1）
Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name FrameworkActivation
$expected = [int]$baseline.GraphEngineAssemblies.Value

$csRoot = Join-Path $Root 'Frame\CsharpPrj'
$hits = @(Get-ChildItem -Path $csRoot -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\|\\BuildOutput\\' } |
    Select-String -Pattern 'new ReferenceGraphEngine' -SimpleMatch)

$actual = $hits.Count
Write-Host ("  FA1 图引擎装配点：实测 {0} / 基线 {1}" -f $actual, $expected) -ForegroundColor Gray

if ($ShowDetail)
{
    foreach ($h in $hits)
    {
        Write-Host ("    {0}:{1}  {2}" -f ($h.Path -replace [regex]::Escape($Root + '\'), ''), $h.LineNumber, $h.Line.Trim()) -ForegroundColor DarkGray
    }
}

if ($actual -lt $expected)
{
    $violations.Add("[FA1] 图引擎装配点 $actual < 基线 $expected ⇒ ReferenceGraphEngine 空转（须经 UniversalReferenceManagerFactory.CreateGraphEngine 或等价入口装配）")
}

if ($violations.Count -eq 0)
{
    Write-Host ""
    Write-Host "[OK] 框架激活守卫：0 违归（图引擎已装配，未退回死代码）" -ForegroundColor Green
    exit 0
}

Write-Host ""
Write-Host "[FAIL] 框架激活守卫命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0
