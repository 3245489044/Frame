#requires -Version 5.1
<#
.SYNOPSIS
    节点域声明单一来源守卫（NodeDeclSingleSource）—— 声明面上移（JSON → .ushar）的建面/接线判据。

.DESCRIPTION
    沿《上下文与 usharp 聚拢 - 声明单一化重构方案》X1 / 《节点域全量工作流化（声明驱动）总体方案》W5：
    节点域定义面（identity / placements / selections / retentions）的**语言路径**须经唯一桥
    `Usharp.Adapters.Node`（UsharpNodeSpecSource）接线；本门禁先立「接线在册」的结构判据，
    并以报告口径显式登记「域定义面仍为 JSON」的存量缺口（U1 上移后清零）。

    判据（C1–C2 硬判据；C3 覆盖硬判据 + 存量报告；C4 正对照；C5 负对照）：
      C1 桥登记在册：BuildFramework\FrameBinaryRefs.props 的 UsharpBinaryRefs 项组须登记
                     `Usharp.Adapters.Node`（否则适配器即使被引也不可见）
      C2 桥实现在册：Usharp.Adapters.Node 的 UsharpNodeSpecSource 源文件存在且实现 ISpecSource
      C3 语言声明面覆盖（U1/E-8 硬判据）：每个域 JSON 须有**同名 .ushar 声明面**（单源上移的结构事实）；
                     存量 JSON 计数转报告（U9 退役前保留为回退面，非清零判据）
      C4 正对照（防假绿）：桥注册行数 ≥ 基线
      C5 负对照（防假绿）：缺失注册的合成样本必须被**同一判据函数**命中 C1

    判据载体 = 声明面/工程文件结构事实（引用行 / 类型实现 / 文件计数），不引入执行者叙述
    （SEP.JudgmentCarrierStructural）。

    模式：默认报告模式（列命中，不设退出码）；-Check 校验模式（C1/C2/C4/C5 违规 → 退出码 1）。

.PARAMETER Root
    仓库根目录（缺省由本脚本位置上溯四级推导：Frame\PowerShellScript\Modules\Node → 仓库根）。

.PARAMETER Check
    校验模式：存在 C1/C2/C4/C5 违规 → 退出码 1。

.PARAMETER ShowDetail
    输出逐项实测（含存量缺口清单）。

.EXAMPLE
    .\Check-NodeDeclSingleSource.ps1 -ShowDetail

.EXAMPLE
    .\Check-NodeDeclSingleSource.ps1 -Check
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

# 基线单源 = Baselines\NodeDeclSingleSource.psd1（禁内联数值基线）
Import-Module (Join-Path $PSScriptRoot '..\..\Baselines\GateBaseline.psm1') -Force
$g = Import-GateBaseline -Name NodeDeclSingleSource

$propsPath     = Join-Path $Root 'Projects\BuildFramework\FrameBinaryRefs.props'
$adapterSource = Join-Path $Root 'Frame\Usharp\src\Usharp.Adapters.Node\UsharpNodeSpecSource.cs'
$nodesDir      = Join-Path $Root 'Projects\Plug\Agent\Config\nodes'

$violations = [System.Collections.Generic.List[string]]::new()
$pending    = [System.Collections.Generic.List[string]]::new()

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  节点域声明单一来源守卫（JSON → .ushar 桥接线）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

# ---------------- 判据函数（C1；正/负对照共用同一实现） ----------------
function Test-AdapterRegistered {
    param(
        [string]$Text,
        [System.Collections.Generic.List[string]]$Sink
    )

    if ([string]::IsNullOrWhiteSpace($Text)) {
        $Sink.Add('[C1] FrameBinaryRefs.props 内容为空或不可读')
        return $false
    }
    if ([regex]::Matches($Text, '<Reference\s+Include="Usharp\.Adapters\.Node"').Count -lt 1) {
        $Sink.Add('[C1] FrameBinaryRefs.props 的 UsharpBinaryRefs 项组未登记 Usharp.Adapters.Node（语言桥不可见）')
        return $false
    }
    return $true
}

# ---------------- C1 桥登记在册 ----------------
if (-not (Test-Path -LiteralPath $propsPath)) {
    $violations.Add("[C1] 未找到 $propsPath")
}
else {
    $null = Test-AdapterRegistered -Text (Get-Content -LiteralPath $propsPath -Raw) -Sink $violations
}

# ---------------- C2 桥实现在册 ----------------
if (-not (Test-Path -LiteralPath $adapterSource)) {
    $violations.Add("[C2] 未找到桥实现 $adapterSource")
}
else {
    $src = Get-Content -LiteralPath $adapterSource -Raw
    if ($src -notmatch 'class\s+UsharpNodeSpecSource' -or $src -notmatch ':\s*ISpecSource') {
        $violations.Add("[C2] $adapterSource 未实现 ISpecSource（桥端口未就位）")
    }
}

# ---------------- C3 语言声明面覆盖（硬判据：每个域 JSON 须有同名 .ushar）+ 存量报告 ----------------
$domainJson = @()
if (Test-Path -LiteralPath $nodesDir) {
    $domainJson = @(Get-ChildItem -LiteralPath $nodesDir -Filter '*.json' -File |
        Where-Object { $_.Name -notlike 'node-gather-*' } | Sort-Object Name)
}
if ($domainJson.Count -gt 0) {
    $pending.Add("[C3] 域定义面仍为 JSON：$($domainJson.Count) 件（U9 退役前保留为回退面）")
}
# 覆盖硬判据（U1/E-8）：每个域 JSON 须有同名 .ushar 声明面（单源上移的结构事实；缺即违规）。
foreach ($json in $domainJson) {
    $usharPath = Join-Path $nodesDir ($json.BaseName + '.ushar')
    if (-not (Test-Path -LiteralPath $usharPath)) {
        $violations.Add("[C3] 域定义面未上移：$($json.Name) 缺同名 .ushar 声明面（$($json.BaseName).ushar）")
    }
}

# ---------------- C4 正对照（防假绿） ----------------
$minReg = [int]$g.AdapterRegistration.Value
$regCount = 0
if (Test-Path -LiteralPath $propsPath) {
    $regCount = [regex]::Matches((Get-Content -LiteralPath $propsPath -Raw), '<Reference\s+Include="Usharp\.Adapters\.Node"').Count
}
if ($regCount -lt $minReg) {
    $violations.Add("[C4] 正对照基线：桥注册行数 $regCount < 基线 $minReg（注册被删而门禁恒绿 = 失效）")
}

# ---------------- C5 负对照（同一判据函数对合成样本必须命中） ----------------
$probeSink = [System.Collections.Generic.List[string]]::new()
$probeText = '<Project><ItemGroup Label="UsharpBinaryRefs"><Reference Include="Usharp.Syntax" /></ItemGroup></Project>'
$null = Test-AdapterRegistered -Text $probeText -Sink $probeSink
if (@($probeSink | Where-Object { $_ -like '*[C1]*' }).Count -eq 0) {
    $violations.Add('[C5] 负对照失效：合成缺失注册未被 C1 判据命中（判据恒真/恒假？）')
}

# ---------------- 汇总 ----------------
Write-Host ("  实测：桥注册 {0} 行 / 域定义面 JSON {1} 件" -f $regCount, $domainJson.Count) -ForegroundColor Gray

if ($ShowDetail) {
    foreach ($f in $domainJson) { Write-Host ("  域定义面JSON : {0}" -f $f.Name) -ForegroundColor DarkGray }
}
if ($ShowDetail -and $pending.Count -gt 0) {
    Write-Host '[NLP] --- 存量缺口（U1 目标，无新增即通过） ---' -ForegroundColor DarkGray
    foreach ($item in $pending) { Write-Host "  - $item" -ForegroundColor DarkGray }
}

if ($violations.Count -eq 0) {
    Write-Host '[OK] 节点域声明单一来源守卫：0 违规（C1/C2 接线在册；C4/C5 对照命中）' -ForegroundColor Green
    exit 0
}

Write-Host "[FAIL] 节点域声明单一来源守卫命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($item in $violations) { Write-Host "  - $item" -ForegroundColor Yellow }
Write-Host '  修复口径：FrameBinaryRefs.props 的 UsharpBinaryRefs 增 Usharp.Adapters.Node 登记；' -ForegroundColor Gray
Write-Host '            桥实现须为 Usharp.Adapters.Node\UsharpNodeSpecSource（实现 ISpecSource）。' -ForegroundColor Gray

if ($Check) { exit 1 }
exit 0
