#requires -Version 5.1
<#
.SYNOPSIS
    行身份裸键回退单处守卫（RowIdentityBareKey）—— 裸键回退路径 = 1。

.DESCRIPTION
    判据（B3/SCT-10）：
      G1 单处分派：Agent src 内 `RowSelection.Decode(` 只允许出现在 `DomainRoute.cs`（唯一分派点）。
      G2 兼容面单源：`DomainRoute.DecodeEntry` 在位。
      G3 正/负对照：合成样本（他处裸键解码）必被捕获。

    模式：默认报告；-Check 时违规 → 退出码 1。
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
Write-Host '  行身份裸键回退单处守卫（RowIdentityBareKey）'
Write-Host '============================================================'

function Get-Count([string]$Text, [string]$Pattern) { return [regex]::Matches($Text, $Pattern).Count }

$src = Join-Path $Root 'Projects\Plug\Agent\src'
$hits = 0
foreach ($f in (Get-ChildItem -Path $src -Recurse -Include '*.cs' -File -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '\\(bin|obj|BuildOutput)\\' })) {
    $t = [System.IO.File]::ReadAllText($f.FullName)
    $c = Get-Count $t 'RowSelection\.Decode\s*\('
    if ($c -eq 0) { continue }
    $hits += $c
    if ($f.Name -ne 'DomainRoute.cs') {
        $violations.Add("[G1] 越界裸键解码：$($f.Name) × $c（应经 DomainRoute.DecodeEntry）")
    }
}
if ($hits -ne 1) { $violations.Add("[G1] 裸键解码总处数 ≠ 1（实际 $hits）") }

$route = Join-Path $src 'Agent.Ui\Core\Functions\Db\DomainRoute.cs'
if (-not (Test-Path -LiteralPath $route)) {
    $violations.Add('[G2] 未见 DomainRoute.cs（唯一分派点缺失）')
} elseif ((Get-Count ([System.IO.File]::ReadAllText($route)) 'DecodeEntry') -eq 0) {
    $violations.Add('[G2] 唯一兼容入口 DomainRoute.DecodeEntry 缺失')
}

# 正/负对照
if ((Get-Count 'RowSelection.Decode(entry, fallback)' 'RowSelection\.Decode\s*\(') -eq 0) {
    $violations.Add('[G3] 负对照失效：合成样本（裸键解码）未被捕获')
}

if ($violations.Count -eq 0) {
    Write-Host '  [成功] G1/G2 行身份裸键回退单处通过（正负对照绿）' -ForegroundColor Green
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
