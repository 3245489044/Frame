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
      T6 Family 合法：适用面（Domain=Agent ∪ 落点在 DSL\Agent\）的门须声明 Family（判据面）且 ∈ 枚举。
      T7 Form 合法：同上适用面的门须声明 Form（判据形态）且 ∈ 枚举。

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
    'ReadModelOnly'               = '未登记独立门：基线 Agent\ReadModelOnly.psd1 尚未事实行化（Check-GateBaselines U9/U11 债务）；基线外置合规后登记'
    'DeclarationEditSingleSource' = '未登记独立门（含基线 Baselines\Declarations\DeclarationEditSingleSource.psd1）——待登记或销账'
    'DeclarationReloadRegistry'   = '未登记独立门——待登记或销账'
    'ExceptionGateway'            = '未登记独立门——待登记或销账'
    'FuncCallSingleSource'        = '未登记独立门（含基线 Baselines\Declarations\FuncCallSingleSource.psd1）——待登记或销账'
    'ZeroRetry'                   = '未登记独立门（基线 Registry\zero-retry-baseline.json）：现状超基线（retrySymbolsInFrame +7 / thirdStateReturns +63 等），登记即红；待零重试轴收口后登记'
    # 既有伴随件（UPC 施工核查发现：DSL 内已存在但未登记为独立门；登记为债务，待后续销账）
    'UiDeclarationSingleSource'   = '伴随件：UI 声明单源分项判据（未登记为独立门；既有债务，待登记或并入宿主门）'
    'DeclarationSingleSource'     = '伴随件：声明单源分项判据（未登记为独立门；既有债务，待登记或并入宿主门）'
    'LogMessageCatalog'           = '伴随件：日志文案目录分项判据（未登记为独立门；既有债务，待登记或并入宿主门）'

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

# ── T6/T7 轴枚举与谓词（唯一定义点；判定与 T5 对照共用 ⇒ 防两套口径）──
# 动机：Domain 只回答「脚本放哪」，不回答「判据看哪类结构 / 用什么运算判」；
#   Agent 域曾平铺 88 件，检索与复用只能靠文件名猜。
$familyEnum = @('Declaration', 'Contract', 'Runtime', 'Presentation', 'Observability', 'Boundary', 'Meta')
$formEnum   = @('SingleSource', 'Closure', 'Boundary', 'Ratchet', 'Drift', 'Consistency')
$agentCellPrefix = 'Frame\PowerShellScript\Operation\DSL\Agent\'

function Test-FamilyValid { param([string]$Family) return (-not [string]::IsNullOrWhiteSpace($Family)) -and ($familyEnum -contains $Family) }
function Test-FormValid   { param([string]$Form)   return (-not [string]::IsNullOrWhiteSpace($Form))   -and ($formEnum -contains $Form) }

# T6/T7 适用面谓词（唯一定义点；判定与 T5 对照共用 ⇒ 防两套口径）
# 适用面 = 语义域（Domain=Agent）∪ 物理落点（DSL\Agent\）——消除双向盲区：
#   ① 语义在 Agent 但落在他域目录（如 PromptCacheStability）
#   ② 落点在 Agent 目录但登记域为他域（AgentStorageIsolation / DispatchTraceSchema / FallbackInventoryRate）
function Test-AgentAxisApplicable {
    param([string]$Domain, [string]$Cell)
    if ([string]::IsNullOrWhiteSpace($Domain) -or [string]::IsNullOrWhiteSpace($Cell)) { return $false }
    if ($Domain -eq 'Agent') { return $true }
    return $Cell.StartsWith($agentCellPrefix, [System.StringComparison]::OrdinalIgnoreCase)
}

# 落点缓存：T2/T3/T4/T6/T7 共用一次解析（防多套口径与重复扫描）
$cellCache = @{}
foreach ($name in $gates.Keys) {
    $cellCache[$name] = (Resolve-GateCell -Name $name -Spec $gates[$name]).Cell
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

# ── T4：index.generated.md 与清单复算等价（行 + 所属域小节，防「行恒等但挂错域」）──
# 复算有效行集 = 「Domain<TAB>| `Gate` | Tier | Expect | Family | Form | `Script` |」
$expectedRows = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$expectedDomains = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($name in $gates.Keys) {
    $spec = $gates[$name]
    $domain = [string]$spec.Domain
    [void]$expectedDomains.Add($domain)
    $tier = if ($spec.ContainsKey('Tier')) { [string]$spec.Tier } else { 'full' }
    $family = if ($spec.ContainsKey('Family')) { [string]$spec.Family } else { '—' }
    $form   = if ($spec.ContainsKey('Form')) { [string]$spec.Form } else { '—' }
    $cell = [string]$cellCache[$name]
    [void]$expectedRows.Add("$domain`t| ``$name`` | $tier | $([int]$spec.Expect) | $family | $form | ``$cell`` |")
}

if (-not (Test-Path -LiteralPath $indexPath)) {
    $violations.Add('[T4] 索引产物缺失：index.generated.md（先运行 Build-GateIndex.ps1）')
} else {
    $indexLines = Get-Content -LiteralPath $indexPath -Encoding UTF8
    $actualRows = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $actualDomains = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $currentSection = ''
    foreach ($ln in $indexLines) {
        if ($ln -match '^##\s+(\S+?)（\d+ 件）') { $currentSection = $Matches[1]; [void]$actualDomains.Add($currentSection); continue }
        if ($ln -match '^\|\s+`([^`]+)`\s+\|') { [void]$actualRows.Add("$currentSection`t$($ln.TrimEnd())") }
    }
    foreach ($r in $expectedRows) { if (-not $actualRows.Contains($r)) { $violations.Add("[T4] 索引漂移：清单有但产物缺行或挂错域小节 → $r") } }
    foreach ($r in $actualRows)   { if (-not $expectedRows.Contains($r)) { $violations.Add("[T4] 索引漂移：产物有但清单无行（手改?）→ $r") } }
    foreach ($d in ($expectedDomains | Sort-Object)) {
        if (-not $actualDomains.Contains($d)) { $violations.Add("[T4] 索引缺域小节：$d") }
    }
}

# ── T6/T7：判据面（Family）与判据形态（Form）分类轴 ──
# 适用面 = 语义域（Domain=Agent）∪ 物理落点（DSL\Agent\）；其余域尚未补轴（Domain 仍是唯一组织轴）
foreach ($name in ($gates.Keys | Sort-Object)) {
    $spec = $gates[$name]
    $cell = [string]$cellCache[$name]
    if (-not (Test-AgentAxisApplicable -Domain ([string]$spec.Domain) -Cell $cell)) { continue }

    $family = if ($spec.ContainsKey('Family')) { [string]$spec.Family } else { '' }
    if (-not (Test-FamilyValid -Family $family)) {
        if ([string]::IsNullOrWhiteSpace($family)) {
            $violations.Add("[T6] $name — Agent 域脚本缺 Family（判据面未定；枚举 $($familyEnum -join '/')）")
        } else {
            $violations.Add("[T6] $name — Family '$family' 不在枚举 [$($familyEnum -join '/')]")
        }
    }

    $form = if ($spec.ContainsKey('Form')) { [string]$spec.Form } else { '' }
    if (-not (Test-FormValid -Form $form)) {
        if ([string]::IsNullOrWhiteSpace($form)) {
            $violations.Add("[T7] $name — Agent 域脚本缺 Form（判据形态未定；枚举 $($formEnum -join '/')）")
        } else {
            $violations.Add("[T7] $name — Form '$form' 不在枚举 [$($formEnum -join '/')]")
        }
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

# T6/T7 谓词自证：合成非法值必被判非法，枚举内合法值必被判合法
if (Test-FamilyValid -Family '__NONEXISTENT__') { $violations.Add('[T5] 负对照失败：合成非法 Family 被 T6 谓词误判为合法') }
if (Test-FormValid -Form '__NONEXISTENT__')     { $violations.Add('[T5] 负对照失败：合成非法 Form 被 T7 谓词误判为合法') }
if (-not (Test-FamilyValid -Family @($familyEnum)[0])) { $violations.Add('[T5] 正对照失败：枚举内合法 Family 被判非法') }
if (-not (Test-FormValid -Form @($formEnum)[0]))       { $violations.Add('[T5] 正对照失败：枚举内合法 Form 被判非法') }

# T6/T7 适用面谓词自证（三类样本：语义域命中 / 落点命中 / 两者皆非）
$agentAxisCases = @(
    @{ Id = '语义域命中（Domain=Agent、落点在 Node）'; Domain = 'Agent'; Cell = 'Frame\PowerShellScript\Operation\DSL\Node\Check-X.ps1'; Expect = $true },
    @{ Id = '落点命中（Domain=Node、落在 Agent 目录）'; Domain = 'Node';  Cell = 'Frame\PowerShellScript\Operation\DSL\Agent\Check-X.ps1'; Expect = $true },
    @{ Id = '两者皆非（Domain=Node、落点在 Node）';       Domain = 'Node';  Cell = 'Frame\PowerShellScript\Operation\DSL\Node\Check-X.ps1'; Expect = $false }
)
foreach ($c in $agentAxisCases) {
    $actual = Test-AgentAxisApplicable -Domain $c.Domain -Cell $c.Cell
    if ($actual -ne $c.Expect) { $violations.Add("[T5] 适用面谓词对照失败：$($c.Id)（实际=$actual，期望=$($c.Expect)）") }
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
