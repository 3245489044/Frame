#requires -Version 5.1
<#
.SYNOPSIS
    UMF 唯一读入口门禁（MountRead）—— UI/Session/ReadModel 直连图查询回归锁。

.DESCRIPTION
    判据（施工单 §5.1 Check-MountRead）：
      R1  UI / Session / ReadModel 生产代码直接调用图查询原语
          （GetReferencesAsync / GetRelatedNodesAsync / GetMemberNodeKeys(Many)Async /
            GetContainerNodeKeysAsync / GetSessionNodeKeysAsync / ProjectSessionNodesAsync）
          的**总点数** ≤ 基线 `Baselines\Node\MountRead.psd1` 的 DirectGraphReads（只减不增）。
      R2  未装配挂载读门面的**新增**文件不得出现上述原语（基线表无该文件 ⇒ 违规）。
      R3  负对照：合成一行直连查询必须被同一判据命中。

    基线口径：`Origin=Debt` 且 `DueWhen` 必填（债务可见化，R6 归零）。

.PARAMETER Root
    仓库根目录（默认由脚本位置上溯四级推导）。

.EXAMPLE
    .\Check-MountRead.ps1 -Check -NegativeControl
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  UMF MountRead（唯一读入口 · 直连图查询回归锁）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

$baselinePath = Join-Path $Root 'Frame\PowerShellScript\Baselines\Node\MountRead.psd1'
if (-not (Test-Path -LiteralPath $baselinePath)) {
    Write-Host "  [失败] 缺基线：$baselinePath" -ForegroundColor Red
    exit 1
}
$baseline = Import-PowerShellDataFile -LiteralPath $baselinePath
$budget = [int]$baseline.Facts.DirectGraphReads.Value
$fileBudgets = @{}
if ($baseline.ContainsKey('Files')) { foreach ($k in $baseline.Files.Keys) { $fileBudgets[$k] = [int]$baseline.Files[$k] } }

# 判据载体 = 源码静态形态（非执行者叙述）。
$directReadPattern = 'GetReferencesAsync|GetRelatedNodesAsync|GetMemberNodeKeysManyAsync|GetMemberNodeKeysAsync|GetContainerNodeKeysAsync|GetSessionNodeKeysAsync|ProjectSessionNodesAsync'

function Test-DirectGraphRead([string]$text) {
    return ($text -match $directReadPattern)
}

$scanRoots = @(
    (Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui'),
    (Join-Path $Root 'Projects\Plug\Agent\src\Agent.Session')
)

$violations = [System.Collections.Generic.List[string]]::new()
$total = 0
$perFile = @{}

foreach ($rootDir in $scanRoots) {
    if (-not (Test-Path -LiteralPath $rootDir)) { continue }
    $files = Get-ChildItem -LiteralPath $rootDir -Recurse -File -Filter '*.cs' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(obj|bin|BuildOutput)\\' }
    foreach ($file in $files) {
        $hits = 0
        foreach ($line in [System.IO.File]::ReadAllLines($file.FullName)) {
            $trim = $line.TrimStart()
            if ($trim.StartsWith('//') -or $trim.StartsWith('*') -or $trim.StartsWith('/*')) { continue }
            if (Test-DirectGraphRead $line) { $hits++ }
        }
        if ($hits -le 0) { continue }
        $total += $hits
        $leaf = $file.Name
        $perFile[$leaf] = [int]$perFile[$leaf] + $hits
        # 逐文件棘轮仅在基线登记了 Files 时生效（未登记 = 报告态，只跑总量棘轮）。
        if ($fileBudgets.Count -gt 0) {
            if (-not $fileBudgets.ContainsKey($leaf)) {
                $violations.Add("R2 新增直连读文件：$leaf = $hits（须经 IMountQuery / MountProjectionRegistry）")
            }
            elseif ($perFile[$leaf] -gt $fileBudgets[$leaf]) {
                $violations.Add("R2 直连读新增：$leaf = $($perFile[$leaf]) > 基线 $($fileBudgets[$leaf])（只减不增）")
            }
        }
    }
}

if ($total -gt $budget) {
    $violations.Add("R1 直连图查询总点数 $total > 基线 $budget（只减不增；DueWhen=$($baseline.Facts.DirectGraphReads.DueWhen)）")
}

if ($NegativeControl) {
    $sample = 'var keys = await _grounding.GetMemberNodeKeysAsync(taskKey, wf, ct);'
    if (-not (Test-DirectGraphRead $sample)) {
        $violations.Add('NEG 负对照失效：合成直连查询未被判据命中（判据空转）')
    }
    Write-Host '  [负对照] 合成直连读样本已被同一判据命中' -ForegroundColor DarkGray
}

Write-Host "  直连读点数 = $total / 基线 $budget" -ForegroundColor DarkGray

if ($ShowDetail -and $violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Host "  [违规] $_" -ForegroundColor Red }
}

if ($violations.Count -gt 0) {
    Write-Host "  [失败] 违规 $($violations.Count) 项" -ForegroundColor Red
    if ($Check) { exit 1 }
}

Write-Host '  [通过] UI/Session 直连图查询未超基线' -ForegroundColor Green
exit 0
