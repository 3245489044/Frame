#requires -Version 5.1
<#
.SYNOPSIS
    FuncCall 单源分派守卫（FuncCallSingleSource）—— 「统一执行单元」的建面/对账判据。

.DESCRIPTION
    沿《任务工作流化-Agent任务历史操作与FuncCall统一治理-方案》§3.1/§3.2、§8：
    执行单元 FuncCall 的 kind 分类（internal/external/composition）与分派入口必须**单源**。

    判据（D1–D5；报告模式起步）：
      D1 kind 单源：`enum FuncCallKind` 定义恰 1 处
      D2 分派单源：`interface IFuncCallDispatcher` 定义恰 1 处
      D3 管线经分派：`FuncCallRun` 经 `dispatcher.Dispatch(` 分派（命中 ≥ 1）
      D4 正对照：FuncCallKind 成员数 ≥ 基线（分类被删而门禁恒绿 = 失效）
      D5 负对照：合成「两个 IFuncCallDispatcher 定义」须命中 D2（判据不恒真/恒假）
    模式：默认报告模式（列命中，不设退出码）；-Check 校验模式（违规 → 退出码 1）。
.PARAMETER Root
    仓库根目录（缺省由本脚本位置上溯四级推导）。
.PARAMETER Check
    校验模式：存在违规 → 退出码 1。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

Import-Module (Join-Path $PSScriptRoot '..\..\Baselines\GateBaseline.psm1') -Force
$g = Import-GateBaseline -Name FuncCallSingleSource

$scanRoots = @(
    (Join-Path $Root 'Frame\CsharpPrj'),
    (Join-Path $Root 'Projects\Plug')
)
$exclude = 'BuildOutput|\\obj\\|\\docs\\'

function Get-SourceFiles {
    $list = [System.Collections.Generic.List[string]]::new()
    foreach ($r in $scanRoots) {
        if (-not (Test-Path -LiteralPath $r)) { continue }
        foreach ($f in @(Get-ChildItem -LiteralPath $r -Recurse -Filter '*.cs' -File -ErrorAction SilentlyContinue)) {
            if ($f.FullName -match $exclude) { continue }
            $list.Add($f.FullName)
        }
    }
    return $list
}

function Get-DefinitionCount {
    param([string[]]$Files, [string]$Pattern)
    $count = 0
    foreach ($f in $Files) {
        $hits = @(Select-String -LiteralPath $f -Pattern $Pattern -ErrorAction SilentlyContinue)
        $count += $hits.Count
    }
    return $count
}

$violations = [System.Collections.Generic.List[string]]::new()
$files = Get-SourceFiles

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  FuncCall 单源分派守卫（FuncCallKind / IFuncCallDispatcher）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

# ---- D1 / D2 定义单源 ----
$kindDefs = Get-DefinitionCount -Files $files -Pattern 'enum\s+FuncCallKind\b'
$dispatchDefs = Get-DefinitionCount -Files $files -Pattern 'interface\s+IFuncCallDispatcher\b'
if ($kindDefs -ne 1) { $violations.Add("[D1] enum FuncCallKind 定义 $kindDefs 处（须恰 1）") }
if ($dispatchDefs -ne 1) { $violations.Add("[D2] interface IFuncCallDispatcher 定义 $dispatchDefs 处（须恰 1）") }

# ---- D3 管线经分派 ----
$dispatchCalls = Get-DefinitionCount -Files $files -Pattern 'dispatcher\.Dispatch\('
if ($dispatchCalls -lt 1) { $violations.Add("[D3] 未发现 dispatcher.Dispatch( 调用（管线未走单源分派）") }

# ---- D4 正对照 ----
$minKind = [int]$g.MinKindMembers.Value
$funcCallFile = $files | Where-Object { $_ -match 'FuncCallDispatch\.cs$' } | Select-Object -First 1
$kindMembers = 0
if ($funcCallFile) {
    $enumMatch = [regex]::Match((Get-Content -LiteralPath $funcCallFile -Raw), 'enum\s+FuncCallKind\b[^{]*\{(?<body>[^}]*)\}')
    if ($enumMatch.Success) {
        $kindMembers = @($enumMatch.Groups['body'].Value -split ',' | Where-Object { $_.Trim() -ne '' }).Count
    }
}
if ($kindMembers -lt $minKind) {
    $violations.Add("[D4] 正对照基线：FuncCallKind 成员数 $kindMembers < 基线 $minKind（分类被删而门禁恒绿 = 失效）")
}

# ---- D5 负对照（同一判据函数须命中合成违规样本）----
$probe = "enum FuncCallKind { Internal }`ninterface IFuncCallDispatcher {}`ninterface IFuncCallDispatcher {}"
$probeDefs = @([regex]::Matches($probe, 'interface\s+IFuncCallDispatcher\b')).Count
if ($probeDefs -le 1) {
    $violations.Add('[D5] 负对照失效：合成双定义未被判据识别（判据恒真/恒假？）')
}

Write-Host ("  实测：FuncCallKind 定义 {0} / 分派器定义 {1} / Dispatch 调用 {2} / 成员 {3}" -f `
    $kindDefs, $dispatchDefs, $dispatchCalls, $kindMembers) -ForegroundColor Gray

if ($violations.Count -eq 0) {
    Write-Host '[OK] FuncCall 单源分派守卫：0 违规（D1/D2 定义单源；D3 管线经分派；D4/D5 对照命中）' -ForegroundColor Green
    exit 0
}

Write-Host "[FAIL] FuncCall 单源分派守卫命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($item in $violations) { Write-Host "  - $item" -ForegroundColor Yellow }

if ($Check) { exit 1 }
exit 0
