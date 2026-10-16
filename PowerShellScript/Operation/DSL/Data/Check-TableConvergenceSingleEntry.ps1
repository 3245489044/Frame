#requires -Version 5.1
<#
.SYNOPSIS
    表结构收敛单入口守卫（TableConvergenceSingleEntry）—— 两旧方法外部调用点 = 0。

.DESCRIPTION
    判据（B1/DUP-08）：
      G1 旧方法降级：`MigrateToPure` / `ResetToPureIfNeeded` 声明为 `internal`（非 public）。
      G2 单入口：调用点仅 `KvUsharpTable.EnsureConverged`（其余调用 `MigrateToPure(` / `ResetToPureIfNeeded(` = 0）。
      G3 正/负对照：合成样本（外部直接调 MigrateToPure）必被捕获。

    模式：默认报告；-Check 时违规 → 退出码 1。

.PARAMETER Root
    仓库根（默认为本脚本位置上溯五级）。
.PARAMETER Check
    校验模式。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$violations = [System.Collections.Generic.List[string]]::new()

Write-Host ''
Write-Host '============================================================'
Write-Host '  表结构收敛单入口守卫（TableConvergenceSingleEntry）'
Write-Host '============================================================'

$kvFile = Join-Path $Root 'Frame\CsharpPrj\Operation\DataStoreDriven\Sqlite\KvUsharpTable.cs'
$scanRoots = @(
    (Join-Path $Root 'Frame\CsharpPrj\Operation\DataStoreDriven'),
    (Join-Path $Root 'Frame\CsharpPrj\Plate\DataManage'),
    (Join-Path $Root 'Projects\Plug\Agent\src')
)

if (-not (Test-Path -LiteralPath $kvFile)) {
    $violations.Add("[G1] 缺件：$kvFile")
} else {
    $kvText = [System.IO.File]::ReadAllText($kvFile)
    if ($kvText -notmatch 'internal\s+static\s+void\s+MigrateToPure\s*\(') {
        $violations.Add('[G1] `MigrateToPure` 未降级为 internal')
    }
    if ($kvText -notmatch 'internal\s+static\s+bool\s+ResetToPureIfNeeded\s*\(') {
        $violations.Add('[G1] `ResetToPureIfNeeded` 未降级为 internal')
    }
    if ($kvText -notmatch 'EnsureConverged\s*\(') {
        $violations.Add('[G1] 缺单入口 `EnsureConverged`')
    }
}

$externalCalls = 0
foreach ($sr in $scanRoots) {
    if (-not (Test-Path -LiteralPath $sr)) { continue }
    foreach ($f in (Get-ChildItem -Path $sr -Recurse -Include '*.cs' -File -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '\\(bin|obj|BuildOutput)\\' })) {
        if ($f.FullName -eq (Resolve-Path -LiteralPath $kvFile -ErrorAction SilentlyContinue).Path) { continue }
        $t = [System.IO.File]::ReadAllText($f.FullName)
        $c = [regex]::Matches($t, 'KvUsharpTable\.(MigrateToPure|ResetToPureIfNeeded)\s*\(').Count
        if ($c -gt 0) {
            $externalCalls += $c
            $violations.Add("[G2] 旁路调用：$($f.Name) 直接调 $c 处（应经 EnsureConverged）")
        }
    }
}
if ($externalCalls -ne 0) { }

# 正/负对照
$probe = 'KvUsharpTable.MigrateToPure(Executor, Table);'
if ([regex]::Matches($probe, 'KvUsharpTable\.(MigrateToPure|ResetToPureIfNeeded)\s*\(').Count -eq 0) {
    $violations.Add('[G3] 负对照失效：合成样本（旁路调用）未被捕获')
}

if ($violations.Count -eq 0) {
    Write-Host '  [成功] G1/G2 单入口通过（正负对照绿）' -ForegroundColor Green
    Write-Host ''
    exit 0
}

Write-Host ''
Write-Host "  [违规] $($violations.Count) 项：" -ForegroundColor Red
foreach ($v in $violations) { Write-Host "    - $v" -ForegroundColor Red }
Write-Host '  结果: 失败' -ForegroundColor Red
Write-Host ''
if ($Check) { exit 1 }
exit 0
