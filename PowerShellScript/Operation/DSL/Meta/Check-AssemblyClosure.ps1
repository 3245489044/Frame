<#
.SYNOPSIS
    装配闭包门禁（Gate=AssemblyClosure）—— 装载组「声明 ⇔ 清单 ⇔ 磁盘」三段闭包。
.DESCRIPTION
    判据载体 = assembly-closure.json（声明）∪ deps.json（清单）∪ 文件系统（资产）。
    权威: axioms AX.NoSilentOutlet / SEP.JudgmentCarrierStructural；实现单源 = Share\AssemblyClosure\AssemblyClosure.psm1。

    A1 声明 ↔ 根/清单：required 组须有根与清单（缺一即红）
    A2 清单 ↔ 磁盘闭包：入口件 + deps 前缀命中库的 runtime 资产须全落盘
    A3 函数库归属唯一：function-libraries.json 的 enabled 库，其程序集须恰在一个装配组根内
    A4 正对照：装配组数 ≥ 基线（防声明面坍缩 ⇒ 假绿）

    退出码：0 无违规 / 1 违规 / 2 前置缺失。
#>
[CmdletBinding()]
param([switch]$Check)

$ErrorActionPreference = 'Stop'

$modulePath = Join-Path $PSScriptRoot '..\..\..\Share\AssemblyClosure\AssemblyClosure.psm1'
$baselinePath = Join-Path $PSScriptRoot '..\..\..\Baselines\Meta\AssemblyClosure.psd1'

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  装配闭包守卫（AssemblyClosure）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

if (-not (Test-Path -LiteralPath $modulePath)) {
    Write-Host "  [FAIL] 闭包实现缺失: $modulePath" -ForegroundColor Red
    exit 2
}
Import-Module $modulePath -Force -DisableNameChecking

if (-not (Test-Path -LiteralPath $baselinePath)) {
    Write-Host "  [FAIL] 基线缺失: $baselinePath" -ForegroundColor Red
    exit 2
}
$baseline = Import-PowerShellDataFile -LiteralPath $baselinePath

$repoRoot = Get-ClosureRepoRoot -StartPath $PSScriptRoot
$configuration = [string]$env:WF_CONFIGURATION
if ([string]::IsNullOrWhiteSpace($configuration)) { $configuration = 'Debug' }

$violations = New-Object System.Collections.Generic.List[string]

try {
    $states = @(Get-ClosureStates -RepoRoot $repoRoot -Configuration $configuration)
}
catch {
    Write-Host ("  [FAIL] 声明解析失败: {0}" -f $_.Exception.Message) -ForegroundColor Red
    exit 2
}

$missingCount = 0
foreach ($s in $states) {
    $tag = if ($s.Required) { 'required' } else { 'optional' }
    Write-Host ("  组 {0,-11} [{1}] root={2}" -f $s.Id, $tag, $s.Root) -ForegroundColor Gray
    if ($s.Required) {
        if (-not $s.RootExists) { $violations.Add(("[A1] {0}: 根不存在 {1}" -f $s.Id, $s.Root)) }
        foreach ($m in $s.Missing) {
            $violations.Add(("[A2] {0}: {1}" -f $s.Id, $m))
            $missingCount++
        }
    }
    elseif ($s.Missing.Count -gt 0) {
        Write-Host ("        （optional 缺件已可见化，不阻断）: {0}" -f ($s.Missing -join '; ')) -ForegroundColor DarkYellow
    }
}

# A3 函数库归属唯一
try {
    $ownership = @(Get-ClosureFunctionLibraryOwnership -RepoRoot $repoRoot -Configuration $configuration)
    foreach ($o in $ownership) {
        if ([string]::IsNullOrWhiteSpace($o.Assembly)) { continue }
        if ($o.FoundGroups.Count -eq 1) { continue }
        if ($o.FoundGroups.Count -eq 0) {
            $violations.Add(("[A3] 函数库 {0} 的程序集 {1}.dll 不在任何装配组根内（启动将无法解析）" -f $o.Name, $o.Assembly))
        }
        else {
            # 共享库被多个产品目录复制属正常（非第二装载源）：可见化，不阻断。
            Write-Host ("        [INFO] {0} 的程序集 {1}.dll 存在于多个组（{2}）—— 共享库复制件，按存在性判" -f $o.Name, $o.Assembly, ($o.FoundGroups -join ',')) -ForegroundColor DarkGray
        }
    }
    Write-Host ("  函数库归属: {0} 项（唯一命中 {1}）" -f $ownership.Count,
        (@($ownership | Where-Object { $_.FoundGroups.Count -eq 1 }).Count)) -ForegroundColor Gray
}
catch {
    $violations.Add(("[A3] 函数库归属判定不可得: {0}" -f $_.Exception.Message))
}

# A4 正对照
$minGroups = [int]$baseline.MinGroups.Value
$minRoots = [int]$baseline.MinRoots.Value
$rootCount = @($states | Where-Object { $_.RootExists }).Count
Write-Host ("  正对照: 组数={0}（下限 {1}） 已有根={2}（下限 {3}）" -f $states.Count, $minGroups, $rootCount, $minRoots) -ForegroundColor Gray
if ($states.Count -lt $minGroups) {
    $violations.Add(("[A4] 声明面坍缩：组数 {0} < 基线 {1}（禁判绿）" -f $states.Count, $minGroups))
}
if ($rootCount -lt $minRoots) {
    $violations.Add(("[A4] 产物面坍缩：已有根 {0} < 基线 {1}（禁判绿）" -f $rootCount, $minRoots))
}

Write-Host ''
if ($violations.Count -gt 0) {
    Write-Host ("  [FAIL] 违规 {0} 项（缺件 {1}）" -f $violations.Count, $missingCount) -ForegroundColor Red
    foreach ($v in $violations) { Write-Host ("         {0}" -f $v) -ForegroundColor DarkRed }
    Write-Host '  处置: Frame\PowerShellScript\Share\Build\Invoke-QuickBuild.ps1 -Configuration Debug' -ForegroundColor Yellow
    if ($Check) { exit 1 }
    exit 0
}

Write-Host '  [OK] 装配闭包完整（声明 ⇔ 清单 ⇔ 磁盘）' -ForegroundColor Green
exit 0
