#requires -Version 5.1
<#
.SYNOPSIS
    存储读端口单表守卫（StoreReadPortSingleTable）—— 读端口收窄为单表语义，`ListTables` 调用点 = 0。

.DESCRIPTION
    判据（B1/RED-10）：
      G1 契约收窄：`IStoreTableReadPort` 以 `ReadRows(int limit)` 为唯一读方法，无 `ListTables`。
      G2 零外层传表：Agent src 内 `ListTables` 调用点 = 0（多集合由端口内部展开）。
      G3 行来源：`StoreTableRow` 含 `Collection`（多集合逐行标注来源）。
      G4 正/负对照：合成样本（外部调 ListTables）必被捕获。

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
$__gateRoot = $PSScriptRoot
while ($__gateRoot -and -not (Test-Path -LiteralPath (Join-Path $__gateRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1'))) { $__gateRoot = Split-Path -Parent $__gateRoot }
if (-not $__gateRoot) { throw '未定位 GateRuntime（Frame\PowerShellScript\Baselines\GateRuntime.psm1）' }
Import-Module (Join-Path $__gateRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root)) { $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot }

$violations = [System.Collections.Generic.List[string]]::new()

Write-Host ''
Write-Host '============================================================'
Write-Host '  存储读端口单表守卫（StoreReadPortSingleTable）'
Write-Host '============================================================'

function Get-Count([string]$Text, [string]$Pattern) { return [regex]::Matches($Text, $Pattern).Count }

$contractsPath = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Contract\NodeDomain\StoreTableReadContracts.cs'

if (-not (Test-Path -LiteralPath $contractsPath)) {
    $violations.Add("[G1] 缺件：$contractsPath")
} else {
    $ct = [System.IO.File]::ReadAllText($contractsPath)
    if ($ct -match 'ListTables\s*\(') { $violations.Add('[G1] IStoreTableReadPort 仍含 ListTables（应单表语义）') }
    if ($ct -notmatch 'ReadRows\s*\(\s*int\s+limit\s*\)') { $violations.Add('[G1] IStoreTableReadPort 缺 `ReadRows(int limit)`') }
    if ($ct -notmatch 'Collection') { $violations.Add('[G3] StoreTableRow 缺 Collection（多集合行来源）') }
}

$agentSrc = Join-Path $Root 'Projects\Plug\Agent\src'
$listCalls = 0
if (Test-Path -LiteralPath $agentSrc) {
    foreach ($f in (Get-ChildItem -Path $agentSrc -Recurse -Include '*.cs' -File -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '\\(bin|obj|BuildOutput)\\' })) {
        $c = Get-Count ([System.IO.File]::ReadAllText($f.FullName)) '\.ListTables\s*\('
        if ($c -gt 0) { $listCalls += $c; $violations.Add("[G2] ListTables 调用点：$($f.Name) × $c") }
    }
}

# 正/负对照
if ((Get-Count 'foreach (var t in port.ListTables())' '\.ListTables\s*\(') -eq 0) {
    $violations.Add('[G4] 负对照失效：合成样本（外部调 ListTables）未被捕获')
}

if ($violations.Count -eq 0) {
    Write-Host '  [成功] G1/G2/G3 单表读端口通过（正负对照绿）' -ForegroundColor Green
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
