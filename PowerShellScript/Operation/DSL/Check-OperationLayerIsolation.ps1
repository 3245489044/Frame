#requires -Version 5.1
<#
.SYNOPSIS
    Operation 层同层引用隔离守卫（N-1）——「Operation 内部零同层互引」的机器判据。

.DESCRIPTION
    沿《DataStoreDriven-节点图引用逻辑通用化与分层提取方案》§2.3 (G-1) / §5.1 N-1（台账 = Docs\design\DataStoreDriven-分层提取-实施台账与批次计划.md）：
    `Frame\CsharpPrj\Operation\*` 属**同一层**（真源 = `CsharpPrj\modules.json` roots.layer，Operation = 4），
    同层项目之间**禁新增 ProjectReference**（同层不互引；跨层协作只经 端口注入 / loc 位置反转 / 组合根装配）。

    实测基线（2026-09-29）：同层引用**已存在 18 条**（历史存量，只减不增；其中 9 条来自聚合工程 `Operation.csproj`，
    它本就应引其子工程）；**目标对 `WorkFlowMethod ↔ DataStoreDriven` = 0**（本方案的硬约束，必须恒 0）。

    判据：
      D1 棘轮基线：实测同层引用集合 ⊆ 基线集合（新增即违规）；基线中已消失的条目给**告警**（提示收紧基线）
      D2 目标对恒 0：`WorkFlowMethod ↔ DataStoreDriven` 任一方向出现引用 ⇒ 违规
      D3 正对照（防假绿）：解析到 Operation 工程数 ≥ 基线 ∧ `Proj_*` 映射项数 ≥ 基线 ∧ 基线各条目的"源工程"存在
      D4 负对照（防假绿）：合成 `$(Proj_DataStoreDriven)` 引用的临时 csproj，必须被**同一解析函数**解析为同层目标

    判据载体 = csproj 结构事实（ProjectReference 目标路径）+ 层号真源文件，不引入执行者叙述（SEP.JudgmentCarrierStructural）。
    层序（向上引用 / Share 出口 / Frame 内外）由 `Frame\PowerShellScript\Share\Solution\Test-ProjectReferences.ps1` 统一校验，本脚本不重复实现。

    模式：默认报告模式（列实测，不设退出码）；-Check 校验模式（违规或正对照失效 → 退出码 1）。

.PARAMETER Root
    仓库根目录（缺省由本脚本位置上溯四级推导：Frame\PowerShellScript\Operation\DSL → 仓库根）。

.PARAMETER Check
    校验模式：存在违规（D1/D2/D3/D4）→ 退出码 1。

.PARAMETER ShowDetail
    输出逐项实测（含达标项）。

.EXAMPLE
    .\Check-OperationLayerIsolation.ps1 -ShowDetail

.EXAMPLE
    .\Check-OperationLayerIsolation.ps1 -Check
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$csharpPrjDir = Join-Path $Root 'Frame\CsharpPrj'
$propsPath    = Join-Path $csharpPrjDir 'generated\Directory.Build.props.modules.generated.props'
$modulesJson  = Join-Path $csharpPrjDir 'modules.json'
$operationDir = Join-Path $csharpPrjDir 'Operation'

# ---------------- 棘轮基线（只减不增；新增须解 §2.3 (G-1) 并在台账给出理由） ----------------
$SameLayerBaseline = @(
    'Operation\BackEndDriven\BackEndDriven.csproj -> Operation\WorkFlowMethod\WorkFlowMethod.csproj'
    'Operation\DataStoreDriven\Instances\DataStoreDriven.Instances.csproj -> Operation\DataStoreDriven\DataStoreDriven.csproj'
    'Operation\FrontEndDriven\FrontEndDriven.csproj -> Operation\StateHoldDriven\StateHoldDriven.csproj'
    'Operation\HierarchyMethod\HierarchyMethod.csproj -> Operation\DataStoreDriven\DataStoreDriven.csproj'
    'Operation\HierarchyMethod\HierarchyMethod.csproj -> Operation\LoopFoldMethod\LoopFoldMethod.csproj'
    'Operation\HierarchyMethod\HierarchyMethod.csproj -> Operation\WorkFlowMethod\WorkFlowMethod.csproj'
    'Operation\LoopFoldMethod\LoopFoldMethod.csproj -> Operation\DataStoreDriven\DataStoreDriven.csproj'
    'Operation\Operation.csproj -> Operation\BackEndDriven\BackEndDriven.csproj'
    'Operation\Operation.csproj -> Operation\DataStoreDriven\DataStoreDriven.csproj'
    'Operation\Operation.csproj -> Operation\DataStoreDriven\Instances\DataStoreDriven.Instances.csproj'
    'Operation\Operation.csproj -> Operation\FrontEndDriven\FrontEndDriven.csproj'
    'Operation\Operation.csproj -> Operation\HierarchyMethod\HierarchyMethod.csproj'
    'Operation\Operation.csproj -> Operation\LoopFoldMethod\LoopFoldMethod.csproj'
    'Operation\Operation.csproj -> Operation\StateHoldDriven\StateHoldDriven.csproj'
    'Operation\Operation.csproj -> Operation\StatisticalDriven\StatisticalDriven.csproj'
    'Operation\Operation.csproj -> Operation\WorkFlowMethod\WorkFlowMethod.csproj'
    'Operation\StateHoldDriven\StateHoldDriven.csproj -> Operation\BackEndDriven\BackEndDriven.csproj'
    'Operation\StateHoldDriven\StateHoldDriven.csproj -> Operation\WorkFlowMethod\WorkFlowMethod.csproj'
)

# ---------------- 目标对（本方案硬约束：恒 0；出处 = 方案 §1.1 关键既有事实 / §6-P0/P5） ----------------
$MustBeZeroPairs = @(
    'Operation\WorkFlowMethod\WorkFlowMethod.csproj -> Operation\DataStoreDriven\DataStoreDriven.csproj'
    'Operation\DataStoreDriven\DataStoreDriven.csproj -> Operation\WorkFlowMethod\WorkFlowMethod.csproj'
)

# ---------------- 正对照/债务基线（防解析面失效 ⇒ 棘轮恒真） ----------------
$OperationProjectBaseline = 11
$ProjMapBaseline          = 100
# 悬空同层引用债务基线（只减不增）：4 条 = 孤儿工程 FunctionExecutionFramework.csproj 的 4 条失效相对路径
# （其源码已被 WorkFlowMethod 默认 glob 收编；仓内 Node 文档已登记该既有噪声）
$DanglingDebtBaseline     = 4

$violations = [System.Collections.Generic.List[string]]::new()
$warnings   = [System.Collections.Generic.List[string]]::new()

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  Operation 层同层引用隔离守卫（N-1：同层不互引）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

# ---------------- 层号真源（D7 粗口径：modules.json roots.layer） ----------------
$operationLayer = $null
if (Test-Path -LiteralPath $modulesJson) {
    $roots = (Get-Content -LiteralPath $modulesJson -Raw | ConvertFrom-Json).roots
    if ($roots.PSObject.Properties.Name -contains 'Operation') { $operationLayer = [int]$roots.Operation.layer }
}
if ($null -eq $operationLayer) {
    $violations.Add("[D3] 层号真源缺失：modules.json 未声明 roots.Operation.layer ⇒ 同层口径不可判（$modulesJson）")
}
elseif ($ShowDetail) {
    Write-Host "  层号真源：modules.json roots.Operation.layer = $operationLayer（D7 粗口径：同层 = CsharpPrj\Operation\*）" -ForegroundColor Gray
}

if (-not (Test-Path -LiteralPath $propsPath)) {
    $violations.Add("[D3] 模块路径映射缺失：$propsPath（须先跑 Generate-ModulePaths.ps1）")
}

# ---------------- 判据函数（D1/D2/D4 共用同一实现） ----------------
function Get-ModulePathMap {
    param([string]$PropsFile)

    $map = @{}
    foreach ($m in [regex]::Matches([System.IO.File]::ReadAllText($PropsFile), '<(Proj_[A-Za-z0-9_]+)>\$\(CsharpPrjDir\)([^<]+)</\1>')) {
        $map[$m.Groups[1].Value] = $m.Groups[2].Value.Trim() -replace '/', '\'
    }
    return $map
}

function Resolve-ProjectReferenceTargets {
    param(
        [string]$ProjectPath,
        [hashtable]$Map,
        [string]$CsharpPrjRoot
    )

    $targets = [System.Collections.Generic.List[string]]::new()
    if (-not (Test-Path -LiteralPath $ProjectPath)) { return $targets }

    # XML 注释内的 ProjectReference 不构成引用（实测：BackEndDriven.csproj 第 29 行注释掉了一条 FunctionStorage 引用）
    $text = [regex]::Replace([System.IO.File]::ReadAllText($ProjectPath), '(?s)<!--.*?-->', '')
    foreach ($m in [regex]::Matches($text, 'ProjectReference[^>]*Include="([^"]+)"')) {
        $inc = $m.Groups[1].Value
        $rel = $null
        if ($inc -match '^\$\((Proj_[A-Za-z0-9_]+)\)$' -and $Map.ContainsKey($Matches[1])) {
            $rel = $Map[$Matches[1]]
        }
        elseif ($inc -notmatch '\$\(' -and -not [string]::IsNullOrWhiteSpace($inc)) {
            $full = [System.IO.Path]::GetFullPath((Join-Path (Split-Path $ProjectPath -Parent) $inc))
            if ($full.StartsWith($CsharpPrjRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
                $rel = $full.Substring($CsharpPrjRoot.Length).TrimStart('\')
            }
        }
        if ($null -ne $rel) { $targets.Add($rel) }
    }
    return $targets
}

function Test-SameLayerRel {
    param([string]$Rel)

    if ([string]::IsNullOrWhiteSpace($Rel)) { return $false }
    return ($Rel -like 'Operation\*')
}

function Get-OperationSameLayerRefs {
    param(
        [string[]]$ProjectPaths,
        [hashtable]$Map,
        [string]$CsharpPrjRoot
    )

    $refs = [System.Collections.Generic.List[object]]::new()
    foreach ($p in $ProjectPaths) {
        if (-not $p.StartsWith($CsharpPrjRoot, [System.StringComparison]::OrdinalIgnoreCase)) { continue }
        $fromRel = $p.Substring($CsharpPrjRoot.Length).TrimStart('\')
        if (-not (Test-SameLayerRel -Rel $fromRel)) { continue }

        foreach ($toRel in (Resolve-ProjectReferenceTargets -ProjectPath $p -Map $Map -CsharpPrjRoot $CsharpPrjRoot)) {
            if ($fromRel -eq $toRel) { continue }
            if (-not (Test-SameLayerRel -Rel $toRel)) { continue }
            $refs.Add([pscustomobject]@{
                Pair   = "$fromRel -> $toRel"
                From   = $fromRel
                To     = $toRel
                # 悬空（目标工程不存在）不算真实同层依赖：单列 D5，主责门禁 = Test-ProjectReferences.ps1 -Check
                Exists = (Test-Path -LiteralPath (Join-Path $CsharpPrjRoot $toRel))
            })
        }
    }
    return $refs
}

# ---------------- 扫描面 ----------------
$projectFiles = @()
if (Test-Path -LiteralPath $operationDir) {
    $projectFiles = @(Get-ChildItem -LiteralPath $operationDir -Recurse -Filter '*.csproj' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\obj\\' -and $_.FullName -notmatch '\\bin\\' } |
        ForEach-Object { $_.FullName })
}
else {
    $violations.Add("[D3] 扫描面缺失：$operationDir")
}

$map = @{}
if (Test-Path -LiteralPath $propsPath) { $map = Get-ModulePathMap -PropsFile $propsPath }

# ---------------- D3 正对照 ----------------
if ($projectFiles.Count -lt $OperationProjectBaseline) {
    $violations.Add("[D3] 正对照失效：解析到 Operation 工程 $($projectFiles.Count) < 基线 $OperationProjectBaseline（扫描面被削 ⇒ 棘轮恒真）")
}
if ($map.Count -lt $ProjMapBaseline) {
    $violations.Add("[D3] 正对照失效：Proj_* 映射项 $($map.Count) < 基线 $ProjMapBaseline（映射被削 ⇒ 目标解析恒空）")
}
foreach ($entry in $SameLayerBaseline) {
    $fromRel = ($entry -split ' -> ')[0]
    if (-not (Test-Path -LiteralPath (Join-Path $csharpPrjDir $fromRel))) {
        $violations.Add("[D3] 基线条目失效：源工程不存在（工程改名/删除须同批收紧基线）: $fromRel")
    }
}

# ---------------- D1 / D2 / D5 ----------------
$allRefs  = @(Get-OperationSameLayerRefs -ProjectPaths $projectFiles -Map $map -CsharpPrjRoot $csharpPrjDir)
$measured = @($allRefs | Where-Object { $_.Exists } | ForEach-Object { $_.Pair } | Sort-Object -Unique)
$dangling = @($allRefs | Where-Object { -not $_.Exists } | ForEach-Object { $_.Pair } | Sort-Object -Unique)

$newPairs = @($measured | Where-Object { $SameLayerBaseline -notcontains $_ })
foreach ($p in $newPairs) {
    $violations.Add("[D1] 新增同层引用（禁）：$p —— 跨层协作须走 端口注入 / loc 位置反转 / 组合根装配（方案 §2.3 G-1）")
}

foreach ($p in $MustBeZeroPairs) {
    if ($measured -contains $p) {
        $violations.Add("[D2] 目标对被破坏（须恒 0）：$p")
    }
}

$stale = @($SameLayerBaseline | Where-Object { $measured -notcontains $_ })
foreach ($p in ($stale | Sort-Object -Unique)) {
    $warnings.Add("[D1] 基线条目已不再命中（可收紧基线，只减不增）：$p")
}

# D5 悬空同层引用（债务，只减不增；主责门禁 = Share\Solution\Test-ProjectReferences.ps1 -Check）
if ($dangling.Count -gt $DanglingDebtBaseline) {
    $warnings.Add("[D5] 悬空同层引用 $($dangling.Count) 条 > 债务基线 $DanglingDebtBaseline（只减不增）")
}

# ---------------- D4 负对照（同一判据函数对合成违规必须命中） ----------------
$probeDir = Join-Path ([System.IO.Path]::GetTempPath()) ('op-layer-probe-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir -Force | Out-Null
$probePath = Join-Path $probeDir 'Probe.csproj'
@'
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="$(Proj_DataStoreDriven)" />
  </ItemGroup>
</Project>
'@ | Set-Content -LiteralPath $probePath -Encoding utf8
try {
    $probeTargets = @(Resolve-ProjectReferenceTargets -ProjectPath $probePath -Map $map -CsharpPrjRoot $csharpPrjDir)
    $probeHit = @($probeTargets | Where-Object { Test-SameLayerRel -Rel $_ })
    if ($probeHit.Count -eq 0) {
        $violations.Add('[D4] 负对照失效：合成 $(Proj_DataStoreDriven) 未被同一解析函数判为同层（判据恒真/恒假？）')
    }
    elseif ($ShowDetail) {
        Write-Host "  负对照命中：$($probeHit[0])" -ForegroundColor Gray
    }
}
finally {
    if (Test-Path -LiteralPath $probePath) { Remove-Item -LiteralPath $probePath -Force }
    if (Test-Path -LiteralPath $probeDir) { Remove-Item -LiteralPath $probeDir -Force -Recurse }
}

# ---------------- 汇总 ----------------
Write-Host ''
Write-Host ("  实测：Operation 工程 {0} / Proj_* 映射 {1} / 同层引用 {2} 条 / 棘轮基线 {3} 条 / 悬空 {4} 条" -f `
    $projectFiles.Count, $map.Count, $measured.Count, $SameLayerBaseline.Count, $dangling.Count) -ForegroundColor Gray

if ($ShowDetail) {
    foreach ($p in $measured) {
        Write-Host "  同层引用 : $p" -ForegroundColor Gray
    }
    foreach ($p in $dangling) {
        Write-Host "  悬空引用 : $p" -ForegroundColor DarkYellow
    }
}

foreach ($w in $warnings) { Write-Host "  [告警] $w" -ForegroundColor Yellow }

if ($violations.Count -eq 0) {
    Write-Host '[OK] Operation 同层引用隔离：0 违规（棘轮基线未增长；目标对 WorkFlowMethod<->DataStoreDriven = 0；D3/D4 正负对照命中）' -ForegroundColor Green
    exit 0
}

Write-Host "[FAIL] Operation 同层引用隔离命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($item in $violations) { Write-Host "  - $item" -ForegroundColor Yellow }
Write-Host '  修复口径：同层不互引（Operation 全体为一层）；跨层协作只经 端口注入 / loc 位置反转 / 组合根装配。' -ForegroundColor Gray
Write-Host '            历史存量 18 条为棘轮基线（只减不增）；新增同层引用必须先在方案 §8 裁决并登记例外理由。' -ForegroundColor Gray

if ($Check) { exit 1 }
exit 0


