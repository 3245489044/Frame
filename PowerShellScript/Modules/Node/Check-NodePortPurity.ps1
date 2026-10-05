#requires -Version 5.1
<#
.SYNOPSIS
    节点端口纯净性守卫（NodePortPurity）—— 「位置层不绑语言 + 桥接唯一」的机器判据（CT-U3 / P20 / §2.2）。

.DESCRIPTION
    扫描面：
      · Frame\CsharpPrj\Modules\Node\Modules.Node.csproj 与其 **\*.cs
      · Frame\Usharp\src\*\*.csproj（各 Usharp 工程的 ProjectReference）

    判据：
      C1 位置层零语言污染：Modules.Node 的 csproj 与源码零 `Usharp` 命中（类型/命名空间/程序集名）。
      C2 位置层不引上层重量闭包：Modules.Node 不得引用 HierarchyMethod / UniversalReference（K6）。
      C3 桥接唯一：引 Modules.Node 的 Usharp 工程恰为白名单 { Usharp.Adapters.Node }（唯一引 Node 处）。
      C4 正对照（防假绿）：桥接工程存在 ∧ 确实引用 Modules.Node ∧ 含 ISpecSource 实现。
      C5 读面三态收口（F-4 / F7 防回归）：三态词面（Ok/OkButEmpty/Unavailable）定义在位 ∧
         读面适配器消费 IsUnavailable（非 bool+集合判定）；附负对照（未消费形态必须被捕获）。

    模式：默认报告模式（列命中，不设退出码）；-Check 校验模式（违规或正对照失效 → 退出码 1）。

.PARAMETER Root
    仓库根目录（缺省由本脚本位置上溯四级推导）。

.PARAMETER Check
    校验模式：存在违规（C1–C5）或正/负对照失效 → 退出码 1。

.PARAMETER ShowDetail
    输出逐项实测计数（含达标项）。

.EXAMPLE
    .\Check-NodePortPurity.ps1 -ShowDetail

.EXAMPLE
    .\Check-NodePortPurity.ps1 -Check
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

# 桥接白名单：唯一允许引用 Modules.Node 的 Usharp 工程（只减不增；扩展须给出耦合方向论证）。
$BridgeProjects = @('Usharp.Adapters.Node')
$nodeCsproj = 'Frame\CsharpPrj\Modules\Node\Modules.Node.csproj'
$nodeSourceDir = 'Frame\CsharpPrj\Modules\Node'
$forbiddenInCsproj = @('HierarchyMethod', 'UniversalReference')

$violations = [System.Collections.Generic.List[string]]::new()

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  节点端口纯净性守卫（位置层零语言污染 / 桥接唯一）" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

# ---------------- C1 位置层零语言污染 ----------------
$csprojPath = Join-Path $Root $nodeCsproj
if (-not (Test-Path -LiteralPath $csprojPath)) {
    Write-Host "[FAIL] 未找到位置层工程：$nodeCsproj ⇒ 判据不可判，禁静默通过" -ForegroundColor Yellow
    exit 1
}

$csprojText = Get-Content -LiteralPath $csprojPath -Raw
# 只判**可执行引用行**（ProjectReference / Reference / PackageReference）；
# csproj 注释与 ModuleRule_* 元数据里的「禁 Usharp」是规范声明，非引用。
foreach ($line in ($csprojText -split "`n")) {
    if ($line -match '<(ProjectReference|Reference|PackageReference)\b' -and $line -match 'Usharp') {
        $violations.Add("[C1] $nodeCsproj 的可执行引用含 'Usharp'（位置层被语言绑定，P20）：$($line.Trim())")
    }
}

$nodeFiles = Get-ChildItem -LiteralPath (Join-Path $Root $nodeSourceDir) -Recurse -Force -Filter *.cs -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(obj|bin|BuildOutput|_deprecated)\\' }
$sourceHits = 0
foreach ($file in $nodeFiles) {
    $relative = $file.FullName.Substring($Root.Length).TrimStart('\')
    $lines = @(Get-Content -LiteralPath $file.FullName -ErrorAction SilentlyContinue)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ([string]::IsNullOrEmpty($lines[$i])) { continue }
        # 去行内注释后判定（注释里的「禁 Usharp」是规范声明）
        $code = ($lines[$i] -split '//')[0]
        if ($code -notmatch 'Usharp') { continue }
        $sourceHits++
        $violations.Add("[C1] $relative`:$($i + 1) 代码面出现 'Usharp'（位置层零语言类型，P20）")
    }
}

# ---------------- C2 位置层不引上层重量闭包 ----------------
foreach ($forbidden in $forbiddenInCsproj) {
    if ($csprojText -match $forbidden) {
        $violations.Add("[C2] $nodeCsproj 引用了 '$forbidden'（位置层被上层闭包污染，K6）")
    }
}

# ---------------- C3 桥接唯一（引 Modules.Node 的 Usharp 工程） ----------------
$usharpSrc = Join-Path $Root 'Frame\Usharp\src'
$bridges = [System.Collections.Generic.List[string]]::new()

if (Test-Path -LiteralPath $usharpSrc) {
    foreach ($project in Get-ChildItem -LiteralPath $usharpSrc -Recurse -Force -Filter *.csproj -ErrorAction SilentlyContinue) {
        $text = Get-Content -LiteralPath $project.FullName -Raw
        if ($text -match 'Modules\\+Node\\+Modules\.Node\.csproj') {
            $bridges.Add($project.BaseName)
        }
    }
}

foreach ($bridge in $bridges) {
    if ($BridgeProjects -notcontains $bridge) {
        $violations.Add("[C3] 未登记的桥接工程 '$bridge' 引用了 Modules.Node（桥接唯一：只许 $($BridgeProjects -join '/')；扩展须论证耦合方向）")
    }
}
foreach ($expected in $BridgeProjects) {
    if ($bridges -notcontains $expected) {
        $violations.Add("[C4] 正对照失效：桥接工程 '$expected' 未引用 Modules.Node（桥接被删 ⇒ 判据恒绿）")
    }
}

# ---------------- C4 正对照（防假绿） ----------------
$bridgeDir = Join-Path $usharpSrc 'Usharp.Adapters.Node'
$bridgeSourceHits = 0
if (Test-Path -LiteralPath $bridgeDir) {
    foreach ($file in Get-ChildItem -LiteralPath $bridgeDir -Recurse -Force -Filter *.cs -ErrorAction SilentlyContinue) {
        $text = Get-Content -LiteralPath $file.FullName -Raw
        if ($text -match ':\s*ISpecSource') { $bridgeSourceHits++ }
    }
}
else {
    $violations.Add("[C4] 正对照失效：桥接工程目录缺失（$bridgeDir）")
}

if ($bridgeSourceHits -eq 0) {
    $violations.Add("[C4] 正对照失效：桥接工程未实现 ISpecSource（端口装配被删 ⇒ 判据恒绿）")
}

# ---------------- C5 读面三态收口（F-4 / F7 防回归） ----------------
# 三态词面（Ok/OkButEmpty/Unavailable）定义在位 + 读面适配器消费 IsUnavailable（非 bool+集合）；
# 缺任一 ⇒ 「空」与「不可用」不可区分，回退面收口退化（F-4：仅 Unavailable 允许回退/清除）。
function Test-ConsumesTriState([string]$text) { return $text -match 'IsUnavailable' }

$contractsRel  = 'Projects\Plug\Agent\src\Agent.Contract\NodeDomain\NodeDomainPortContracts.cs'
$adapterRel    = 'Projects\Plug\Agent\src\Agent.Modules\Gather\DomainNodeReadAdapter.cs'
$contractsPath = Join-Path $Root $contractsRel
$adapterPath   = Join-Path $Root $adapterRel

if (Test-Path -LiteralPath $contractsPath) {
    $contractsText = Get-Content -LiteralPath $contractsPath -Raw
    foreach ($token in @('OkButEmptyStatus', 'UnavailableStatus', 'IsUnavailable')) {
        if ($contractsText -notmatch [regex]::Escape($token)) {
            $violations.Add("[C5] 读面三态词面缺 '$token'（NodeReadOutcome）：回退面收口退化（F-4）")
        }
    }
}
else {
    $violations.Add("[C5] 读面契约缺失：$contractsRel（三态不可判）")
}

if (Test-Path -LiteralPath $adapterPath) {
    if (-not (Test-ConsumesTriState (Get-Content -LiteralPath $adapterPath -Raw))) {
        $violations.Add("[C5] 读面适配器 DomainNodeReadAdapter 未消费 IsUnavailable（bool+集合 判定成功/失败 ⇒ 回退面收口退化）")
    }
    # 负对照（防假绿）：不含 IsUnavailable 的形态必须被判据核心判为「未消费」。
    if (Test-ConsumesTriState 'var items = read.Items; return new NodeGatherRead(NodeGatherOutcome.Available(items.Count), items);') {
        $violations.Add("[C5] 负对照失效：未消费三态的形态未被判据捕获（判据恒真）")
    }
}
else {
    $violations.Add("[C5] 读面适配器缺失：$adapterRel（三态消费面被删 ⇒ 判据恒绿）")
}

# ---------------- 输出 ----------------
Write-Host ""
Write-Host "  实测：C1 位置层源码 $($nodeFiles.Count) 文件 / 语言命中 $sourceHits 处" -ForegroundColor Gray
Write-Host "        C3 引 Modules.Node 的 Usharp 工程 $($bridges.Count) 件（$($bridges -join ', ')）" -ForegroundColor Gray
Write-Host "        C4 桥接 ISpecSource 实现 $bridgeSourceHits 处" -ForegroundColor Gray

if ($ShowDetail) {
    Write-Host "  [C1] 位置层工程：$nodeCsproj" -ForegroundColor Gray
    Write-Host "  [C2] 禁引闭包：$($forbiddenInCsproj -join ' / ')" -ForegroundColor Gray
    Write-Host "  [C3] 桥接白名单：$($BridgeProjects -join ' / ')" -ForegroundColor Gray
    foreach ($file in $nodeFiles) {
        Write-Host "       - $(($file.FullName.Substring($Root.Length).TrimStart('\')))" -ForegroundColor DarkGray
    }
}

if ($violations.Count -eq 0) {
    Write-Host ""
    Write-Host "[OK] 节点端口纯净性守卫：0 违规（位置层零语言污染且桥接唯一）" -ForegroundColor Green
    exit 0
}

Write-Host ""
foreach ($violation in $violations) {
    Write-Host "  $violation" -ForegroundColor Yellow
}
Write-Host ""
Write-Host "[FAIL] 节点端口纯净性守卫：$($violations.Count) 处违规" -ForegroundColor Red
if ($Check) { exit 1 }
exit 0
