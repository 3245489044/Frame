#requires -Version 5.1
<#
.SYNOPSIS
    GateFileIndex 守卫 —— 禁止门禁源文件枚举回退为递归 Get-ChildItem（R1 棘轮）。

.DESCRIPTION
    背景：门禁源文件枚举应统一走 Baselines\GateFileIndex.psm1（git ls-files 索引），
    否则会在 node_modules/BuildOutput 上重复遍历。本件把「递归枚举」变成可见的清册：

      F1 清点：扫描面内 Check-*.ps1 的 `Get-ChildItem ... -Recurse` 落点。
      F2 在册：命中文件必须在 Baselines\Meta\GateFileIndex.psd1 的 Allowlist 中；
              每条带 Origin ∈ {Derived,Debt,Verdict} + DueWhen（Debt 必带）。
      F3 上限：单件命中数不得超过 DueWhen 中 `MaxHits=<n>`（缺省 1）。
      F4 正/负对照：合成递归样本必命中；非递归样本不得命中（防判据空转）。
      F5 可见化：Allowlist 中已无命中的条目列 [可销账]（不拦）。

    模式：默认报告；-Check 违规 → 退出码 1。只读仓库，零侧写。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $__repoRoot = $PSScriptRoot
    while ($__repoRoot -and -not ((Test-Path -LiteralPath (Join-Path $__repoRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $__repoRoot 'Projects'))) { $__repoRoot = Split-Path -Parent $__repoRoot }
    if (-not $__repoRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
    Import-Module (Join-Path $__repoRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
    $ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Detail:$ShowDetail -Key 'GateFileIndex'
    $Root = $ctx.Root
}

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateFileIndex.psm1') -Force
Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force

$baseline = Import-GateBaseline -Name GateFileIndex
$violations = [System.Collections.Generic.List[string]]::new()
$pending = [System.Collections.Generic.List[string]]::new()

# ---- 清册：path -> { MaxHits; Origin; DueWhen } ----
$allow = @{}
foreach ($row in @($baseline.Allowlist)) {
    if ($null -eq $row) { continue }
    $key = ([string]$row.Value).Replace('/', '\').TrimStart('\')
    if ([string]::IsNullOrWhiteSpace($key)) { continue }
    $origin = [string]$row.Origin
    $due = [string]$row.DueWhen
    if ($origin -notin @('Derived', 'Debt', 'Verdict')) {
        $violations.Add("[F2] Allowlist '$key' Origin='$origin' 非法（须 ∈ Derived/Debt/Verdict）")
    }
    if ($origin -in @('Debt', 'Verdict') -and [string]::IsNullOrWhiteSpace($due)) {
        $violations.Add("[F2] Allowlist '$key' Origin=$origin 缺 DueWhen（禁无期债务）")
    }
    $max = 1
    $m = [regex]::Match($due, 'MaxHits=(?<n>\d+)')
    if ($m.Success) { $max = [int]$m.Groups['n'].Value }
    $allow[$key] = @{ MaxHits = $max; Origin = $origin; DueWhen = $due }
}

# ---- 扫描面：Frame\PowerShellScript 下全部 Check-*.ps1（git 索引）----
$scanRoot = Join-Path $Root 'Frame\PowerShellScript'
$scripts = @(Get-GateFiles -Root $scanRoot -Extension ps1 |
    Where-Object { (Split-Path -Leaf $_) -like 'Check-*.ps1' } |
    Sort-Object -Unique)

$rxRecurse = 'Get-ChildItem[^\r\n]*-Recurse'
$hits = @{}
foreach ($script in $scripts) {
    if ((Split-Path -Leaf $script) -eq 'Check-GateFileIndex.ps1') { continue }  # 自排除（判据文本含样本）
    $count = 0
    foreach ($line in [IO.File]::ReadAllLines($script)) {
        $trim = $line.TrimStart()
        if ($trim.StartsWith('#')) { continue }                       # 注释提及不算使用
        if ($trim -match '^\$rxRecurse\s*=') { continue }             # 判据自身正则定义
        if ($trim -match '^\$positive\s*=' -or $trim -match '^\$negative\s*=') { continue }
        if ($line -match $rxRecurse) { $count++ }
    }
    if ($count -gt 0) {
        $rel = [IO.Path]::GetRelativePath($Root, $script).Replace('/', '\')
        $hits[$rel] = $count
    }
}

# ---- F2/F3 对账 ----
foreach ($rel in @($hits.Keys | Sort-Object)) {
    $count = [int]$hits[$rel]
    if (-not $allow.ContainsKey($rel)) {
        $violations.Add("[F2] 未在册的递归枚举：$rel（$count 处）—— 请改用 Get-GateFiles，或在 GateFileIndex.psd1 登记债务")
        continue
    }
    if ($count -gt [int]$allow[$rel].MaxHits) {
        $violations.Add("[F3] $rel 递归枚举 $count 处 > 在册上限 $($allow[$rel].MaxHits)（只减不增）")
    }
}
foreach ($rel in @($allow.Keys | Sort-Object)) {
    if (-not $hits.ContainsKey($rel)) { $pending.Add($rel) }
}
if ($ShowDetail) {
    Write-Host ("  扫描面: {0} 件 Check-*.ps1 | 递归命中: {1} 件" -f $scripts.Count, $hits.Count) -ForegroundColor Gray
    foreach ($rel in @($hits.Keys | Sort-Object)) { Write-Host ("    {0} = {1}" -f $rel, $hits[$rel]) -ForegroundColor DarkGray }
    foreach ($p in $pending) { Write-Host ("    [可销账] {0}" -f $p) -ForegroundColor DarkGray }
}

# ---- F4 正/负对照 ----
$positive = 'Get-ChildItem -LiteralPath $dir -Recurse -File -Filter *.cs'
$negative = 'Get-ChildItem -LiteralPath $dir -File -Filter *.cs'
if ([regex]::Matches($positive, $rxRecurse).Count -lt 1) {
    $violations.Add('[F4] 正对照失败：递归样本未被判据命中（判据空转）')
}
if ([regex]::Matches($negative, $rxRecurse).Count -ne 0) {
    $violations.Add('[F4] 负对照失败：非递归样本被误判为递归枚举')
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'GateFileIndex'
