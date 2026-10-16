#requires -Version 5.1
<#
.SYNOPSIS
    Sqlite 表白名单/封顶单源守卫（SqliteTablePolicySingleSource）—— 白名单 + 行数封顶字面量各恰一处。

.DESCRIPTION
    判据（B1/DUP-01）：
      G1 封顶单源：StorageFramework 内 `DefaultLimit = 200` 命中恰 1（SqliteTablePolicy）。
      G2 硬上限单源：StorageFramework 内 `MaxLimit = 1000` 命中恰 1。
      G3 白名单单源：StorageFramework 内 `IsUserTable` 定义（static bool IsUserTable）恰 1。
      G4 正/负对照：合成样本（第二处封顶/白名单定义）必被捕获。

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
Write-Host '  Sqlite 表白名单/封顶单源守卫（SqliteTablePolicySingleSource）'
Write-Host '============================================================'

$sfRoot = Join-Path $Root 'Frame\CsharpPrj\Plate\DataManage\StorageFramework'

function Get-Hits([string]$Text, [string]$Pattern) {
    return [regex]::Matches($Text, $Pattern).Count
}

if (-not (Test-Path -LiteralPath $sfRoot)) {
    $violations.Add("[G1] 缺失 StorageFramework 目录：$sfRoot")
} else {
    $files = @(Get-ChildItem -Path $sfRoot -Recurse -Filter '*.cs' -File | Where-Object { $_.FullName -notmatch '\\(bin|obj|BuildOutput)\\' })
    $text = [string]::Join("`n", @($files | ForEach-Object { [System.IO.File]::ReadAllText($_.FullName) }))

    $defaultHits = Get-Hits $text 'DefaultLimit\s*=\s*200'
    if ($defaultHits -ne 1) { $violations.Add("[G1] `DefaultLimit = 200` 命中 $defaultHits 处（应恰 1，单源 = SqliteTablePolicy）") }

    $maxHits = Get-Hits $text 'MaxLimit\s*=\s*1000'
    if ($maxHits -ne 1) { $violations.Add("[G2] `MaxLimit = 1000` 命中 $maxHits 处（应恰 1）") }

    $isUserHits = Get-Hits $text 'static\s+bool\s+IsUserTable\s*\('
    if ($isUserHits -ne 1) { $violations.Add("[G3] `IsUserTable` 定义命中 $isUserHits 处（应恰 1）") }

    # 正/负对照：合成样本（封顶字面量 + 白名单定义）必被捕获。
    $probe = 'private const int MaxLimit = 1000; static bool IsUserTable(string t) => false;'
    if ((Get-Hits $probe 'MaxLimit\s*=\s*1000') -eq 0) {
        $violations.Add('[G4] 负对照失效：合成样本（封顶字面量）未被捕获')
    }
    if ((Get-Hits $probe 'static\s+bool\s+IsUserTable\s*\(') -eq 0) {
        $violations.Add('[G4] 负对照失效：合成样本（白名单定义）未被捕获')
    }
}

if ($violations.Count -eq 0) {
    Write-Host '  [成功] G1/G2/G3 单源通过（正负对照绿）' -ForegroundColor Green
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
