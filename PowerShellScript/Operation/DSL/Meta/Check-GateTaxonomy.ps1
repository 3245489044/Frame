#requires -Version 5.1
<#
.SYNOPSIS
    门禁分类守卫（GateTaxonomy）—— 把「门禁怎么分类」由文档纪律变为机器判据（清单 ⇄ 索引 ⇄ 脚本文件 三方对账）。

.DESCRIPTION
    分类轴（Domain）唯一真源 = Operation\DSL\Registry\GateManifests.psd1 每件的 Domain 字段。
    编译产物 = Operation\DSL\Registry\index.generated.md（Build-GateIndex.ps1 生成，禁手改）。

    判据：
      T1 Domain 合法：每件登记项 Domain 非空且 ∈ 枚举（Meta/Agent/Node/Parsing/Data/Spec/Process/Usharp/Declarations/Build）。
      T2 落点在位：每件登记项的脚本落点（Path ∨ 默认 Operation\DSL\Check-<Name>.ps1）在盘存在（防悬空登记）。
      T3 脚本归属可达：每个 Operation\DSL\Check-*.ps1 要么被某登记项默认名命中、要么被某登记项 Path 指向、
         要么在伴随件白名单（防「新增门却未登记」脱钩）。
      T4 索引等价：index.generated.md 存在且其条目集与清单复算等价（防手改产物 / 漂移）。
      T5 正/负对照：复算器对合成样本行为正确（判据不恒真/恒假）。

    模式：默认报告模式（列违规，不设退出码）；-Check 校验模式（违规 → 退出码 1）。只读仓库，零侧写。

.PARAMETER Root
    仓库超根（缺省 = 本脚本上溯四级；含 Frame\ 与 Projects\ 两个子模块）。

.PARAMETER Check
    校验模式：存在违规 → 退出码 1。

.EXAMPLE
    .\Check-GateTaxonomy.ps1
.EXAMPLE
    .\Check-GateTaxonomy.ps1 -Check
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

# 门禁运行时（委托 PScriptManager\Framework\DirectoryMap）：根定位 + 落点派生**单源**。
Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root)) {
    # 仓库超根（与目录深度无关；由目录映射框架 marker 上溯解析）
    $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
}

# DSL 根（Check-*.ps1 的分类根；按名递归发现用，与具体 <Domain> 子目录布局解耦）
$DslRoot = Get-MappedPath -Key 'DSL'

$manifestPath = Join-Path $PSScriptRoot '..\Registry\GateManifests.psd1'
$indexPath    = Join-Path $PSScriptRoot '..\Registry\index.generated.md'

# 分类轴枚举（唯一定义点；与 Build-GateIndex.ps1 的 Domain 值域一致）
$domainEnum = @('Meta', 'Agent', 'Node', 'Parsing', 'Data', 'Spec', 'Process', 'Usharp', 'Declarations', 'Build', 'KeySpace')

# 伴随件白名单：DSL 目录内存在但未登记为独立门的 Check-*.ps1（fail-loud：新增未登记件须显式登记或入白名单）
$companionAllowlist = @{
    'KeySpaceInvariants'          = '伴随件：三视图（axis/surface/owner）共用实现，经 Check-Key{AxisCompleteness,SurfaceDistinguishable,OwnerUnique}.ps1 薄转发登记'
    'AgentCarrierSurface'         = '伴随件：载体面分项判据（未登记为独立门）'
    'AgentLayerBoundary'          = '伴随件：层边界分项判据（未登记为独立门）'
    'DeclarationEditSingleSource' = '未登记独立门（含基线 Baselines\Declarations\DeclarationEditSingleSource.psd1）——待登记或销账'
    'DeclarationReloadRegistry'   = '未登记独立门——待登记或销账'
    'ExceptionGateway'            = '未登记独立门——待登记或销账'
    'FuncCallSingleSource'        = '未登记独立门（含基线 Baselines\Declarations\FuncCallSingleSource.psd1）——待登记或销账'
    'ZeroRetry'                   = '未登记独立门（基线 Registry\zero-retry-baseline.json）：现状超基线（retrySymbolsInFrame +7 / thirdStateReturns +63 等），登记即红；待零重试轴收口后登记'
}

$violations = [System.Collections.Generic.List[string]]::new()

if (-not (Test-Path -LiteralPath $manifestPath)) { Write-Error "门禁清单不存在：$manifestPath"; exit 2 }
$gates = Import-PowerShellDataFile -LiteralPath $manifestPath

# ── 落点口径单源 = 门禁运行时（委托目录映射框架；Domain → 候选目录 + 名称例外）──
#   本件禁再写落点解析（防与 Build-GateIndex.ps1 分叉）。
function Resolve-GateCell {
    param([string]$Name, $Spec)
    $domain = if ($Spec.ContainsKey('Domain')) { [string]$Spec.Domain } else { '' }
    try {
        return [pscustomobject]@{ Cell = (Get-GateScriptCell -Gate $Name -Domain $domain -AllowFallback); Error = '' }
    }
    catch {
        return [pscustomobject]@{ Cell = ''; Error = $_.Exception.Message }
    }
}

# Domain 合法性谓词（唯一定义点；T1 判定与 T5 正/负对照**共用** ⇒ 防两套口径）
function Test-DomainValid {
    param([string]$Domain)
    return (-not [string]::IsNullOrWhiteSpace($Domain)) -and ($domainEnum -contains $Domain)
}

# ── T1：Domain 合法 ──
foreach ($name in ($gates.Keys | Sort-Object)) {
    $spec = $gates[$name]
    $domain = if ($spec.ContainsKey('Domain')) { [string]$spec.Domain } else { '' }
    if (-not (Test-DomainValid -Domain $domain)) {
        if ([string]::IsNullOrWhiteSpace($domain)) {
            $violations.Add("[T1] $name — 缺 Domain（分类未定，禁挂空域）")
        } else {
            $violations.Add("[T1] $name — Domain '$domain' 不在枚举 [$($domainEnum -join '/')]")
        }
    }
}

# ── T2：登记脚本落点在位 ──
foreach ($name in ($gates.Keys | Sort-Object)) {
    $spec = $gates[$name]
    $r = Resolve-GateCell -Name $name -Spec $spec
    if ($r.Error) { $violations.Add("[T2] $name — 落点解析失败：$($r.Error)"); continue }
    $resolved = Join-Path $Root $r.Cell
    if (-not (Test-Path -LiteralPath $resolved)) {
        $violations.Add("[T2] $name — 登记脚本不存在：$($r.Cell)")
    }
}

# ── T3：DSL Check-*.ps1 归属可达（+ 门落点重名/歧义即红）──
# 可达集 = { 按 Domain 派生命中的落点 } ∪ 白名单
$reached = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($name in $gates.Keys) {
    $spec = $gates[$name]
    $r = Resolve-GateCell -Name $name -Spec $spec
    if ($r.Error) { continue }
    $abs = [System.IO.Path]::GetFullPath((Join-Path $Root $r.Cell))
    if (-not $reached.Add($abs)) {
        $violations.Add("[T3] $name — 落点与其他门重复（同名/歧义）：$($r.Cell)")
    }
}

# 递归枚举 DSL\ 全域 Check-*.ps1（分类到 <Domain>\ 子目录后仍全覆盖）
$dslChecks = @(Get-ChildItem -LiteralPath $DslRoot -Recurse -Filter 'Check-*.ps1' -File -ErrorAction SilentlyContinue)
foreach ($f in $dslChecks) {
    $base = $f.BaseName -replace '^Check-', ''
    $isReached = $reached.Contains([System.IO.Path]::GetFullPath($f.FullName))
    $isCompanion = $companionAllowlist.ContainsKey($base)
    if (-not $isReached -and -not $isCompanion) {
        $violations.Add("[T3] Check-$base.ps1 — 未登记为独立门且不在伴随白名单（新增门未登记 / 归类脱钩）")
    }
}

# ── T4：index.generated.md 与清单复算等价 ──
# 复算有效行集 = 每个登记项一行「| `Gate` | Tier | Expect | `Script` |」
$expectedRows = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$expectedDomains = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($name in $gates.Keys) {
    $spec = $gates[$name]
    [void]$expectedDomains.Add([string]$spec.Domain)
    $tier = if ($spec.ContainsKey('Tier')) { [string]$spec.Tier } else { 'full' }
    $cell = (Resolve-GateCell -Name $name -Spec $spec).Cell
    [void]$expectedRows.Add("| ``$name`` | $tier | $([int]$spec.Expect) | ``$cell`` |")
}

if (-not (Test-Path -LiteralPath $indexPath)) {
    $violations.Add('[T4] 索引产物缺失：index.generated.md（先运行 Build-GateIndex.ps1）')
} else {
    $indexLines = Get-Content -LiteralPath $indexPath -Encoding UTF8
    $actualRows = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $actualDomains = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($ln in $indexLines) {
        if ($ln -match '^##\s+(\S+?)（\d+ 件）') { [void]$actualDomains.Add($Matches[1]); continue }
        if ($ln -match '^\|\s+`([^`]+)`\s+\|') { [void]$actualRows.Add($ln.TrimEnd()) }
    }
    foreach ($r in $expectedRows) { if (-not $actualRows.Contains($r)) { $violations.Add("[T4] 索引漂移：清单有但产物缺行 → $r") } }
    foreach ($r in $actualRows)   { if (-not $expectedRows.Contains($r)) { $violations.Add("[T4] 索引漂移：产物有但清单无行（手改?）→ $r") } }
    foreach ($d in ($expectedDomains | Sort-Object)) {
        if (-not $actualDomains.Contains($d)) { $violations.Add("[T4] 索引缺域小节：$d") }
    }
}

# ── T5：正/负对照（判据自证：T1 的 Domain 合法性谓词 Test-DomainValid）──
if ($expectedRows.Count -eq 0) { $violations.Add('[T5] 正对照失败：复算行集为空（判据无标的假绿）') }
# 负对照：合成非法域**必须**被判非法（谓词恒真/恒假都将被此处捕获 —— 原实现 `-contains '__NONEXISTENT__'` 恒假 ⇒ 该对照永不触发）
if (Test-DomainValid -Domain '__NONEXISTENT__') {
    $violations.Add('[T5] 负对照失败：合成非法域被 T1 谓词误判为合法（判据恒真）')
}
# 正对照：枚举内实存域**必须**被判合法（谓词被改坏为恒假即红）
$posDomain = @($domainEnum)[0]
if (-not (Test-DomainValid -Domain $posDomain)) {
    $violations.Add("[T5] 正对照失败：枚举内合法域 '$posDomain' 被判非法（谓词被改坏）")
}

# ── 报告 ──
Write-Host '=== 门禁分类守卫（GateTaxonomy：清单 ⇄ 索引 ⇄ 脚本 三方对账）===' -ForegroundColor White
Write-Host "登记门数: $($gates.Keys.Count) / Domain 枚举: $($domainEnum -join '/')"
Write-Host "DSL 脚本件: $($dslChecks.Count) / 伴随白名单: $($companionAllowlist.Count)"
Write-Host "违规数: $($violations.Count)"

if ($violations.Count -gt 0) {
    Write-Host '--- 违规清单 ---' -ForegroundColor Yellow
    foreach ($v in $violations) { Write-Host "  [FAIL] $v" -ForegroundColor Yellow }
}

if (-not $Check) {
    if ($violations.Count -eq 0) { Write-Host 'GateTaxonomy(报告): 分类单源一致。' -ForegroundColor Green }
    exit 0
}

if ($violations.Count -eq 0) {
    Write-Host 'Check-GateTaxonomy: OK —— 门禁分类「清单 ⇄ 索引 ⇄ 脚本」一致。' -ForegroundColor Green
    exit 0
}
Write-Host 'Check-GateTaxonomy: FAIL —— 分类漂移。' -ForegroundColor Red
exit 1
