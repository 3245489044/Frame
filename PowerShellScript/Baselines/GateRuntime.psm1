#requires -Version 5.1
<#
.SYNOPSIS
    GateRuntime —— 门禁运行时（根定位 / 脚本发现 / 裁决 / 结果结构），纯原语，无判据语义。

.DESCRIPTION
    本件把门禁脚本内重复散落的六类样板收敛为单源（分析文档 D1/D2/D4/D6/E2/E4 的收拢面）：
      · 根定位   —— 委托**目录映射基础框架** PScriptManager\Framework\DirectoryMap.psm1
                    （超根 = 含 Frame\+Projects\ 的目录；与深度无关；禁门内再写 ..\..\..\..\ 上溯）。
      · 脚本发现 —— 委托 DirectoryMap 的 Resolve-GateScript / Get-GateScriptCell
                    （Domain→候选目录 + 名称例外；歧义即抛）。
      · 裁决     —— Write-GateVerdict 单义退出码（0 无违规 / -Check 且违规 1 / 异常由调用方 2）。
      · 结果结构 —— New-GateResult（供组合门读结果产物，不解析文本）。
      · 参数协议 —— New-GateContext（-Root/-Check/-Detail/-Json/-NegativeControl → 单一上下文对象，
                    含 Root 与 Violations 清单）；Assert-GatePrerequisite（前置缺失统一 exit 2）。
      · 判据自证 —— Assert-GateControls（正样本必过 / 负样本必红 / 缺对照即违规）。

    形态：本 .psm1 以 Import-Module 装载 DirectoryMap（函数不重复导出 ⇒ 无同名多属主）。
    本件无 Export-ModuleMember：默认仅导出本文件自定义函数（导入件不外溢）。

.NOTES
    用法（门禁脚本头部）：
        Import-Module (Join-Path $PSRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force
        $ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Key 'GateTaxonomy'
        # ... 判据（$ctx.Root / $ctx.Violations）...
        foreach ($issue in (Assert-GateControls -Cases $controls -Label 'GateTaxonomy')) { $ctx.Violations.Add($issue) }
        Write-GateVerdict -Violations $ctx.Violations -Check:$Check -Key 'GateTaxonomy'
#>

Import-Module (Join-Path $PSScriptRoot '..\PScriptManager\Framework\DirectoryMap.psm1') -Force -DisableNameChecking

<#
.SYNOPSIS
    解析仓库超根（委托目录映射框架；与目录深度无关）。
.PARAMETER ScriptRoot
    门禁脚本所在目录（缺省 = 本文件目录）。
#>
function Get-GateRepoRoot {
    param([string]$ScriptRoot)
    $start = if ($ScriptRoot) { $ScriptRoot } else { $PSScriptRoot }
    return Get-SuperRoot -StartPath $start
}

<#
.SYNOPSIS
    按名（+ Domain）发现门脚本绝对路径（委托目录映射框架；歧义即抛）。
#>
function Find-GateScript {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [string]$Domain = '',
        [switch]$AllowFallback
    )
    return Resolve-GateScript -Gate $Name -Domain $Domain -AllowFallback:$AllowFallback
}

<#
.SYNOPSIS
    构造结构化门结果（供组合门读结果产物，脱文本耦合）。Exit: 0 无违规 / 1 违规 / 2 异常前置缺失。
#>
function New-GateResult {
    param(
        [Parameter(Mandatory = $true)][string]$Gate,
        [System.Collections.Generic.List[string]]$Violations,
        [int]$Exit = 0
    )
    $v = if ($null -eq $Violations) { @() } else { @($Violations) }
    return [pscustomobject]@{
        Gate           = $Gate
        Violations     = $v
        ViolationCount = $v.Count
        Exit           = $Exit
    }
}

<#
.SYNOPSIS
    统一门禁标题头（收敛各门自写 header）。
#>
function Write-GateHeader {
    param([string]$Title)
    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host "  $Title" -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor Cyan
}

# 退出码契约单源（D1）：0 通过 / 1 违规（-Check）/ 2 前置缺失或不可判定。
# 域侧禁止散落魔数；前置缺失唯一出口 = Assert-GatePrerequisite。
$script:GateExitPass = 0
$script:GateExitViolation = 1
$script:GateExitUndetermined = 2

<#
.SYNOPSIS
    退出码契约（Pass=0 / Violation=1 / Undetermined=2）。
#>
function Get-GateExitCodePolicy {
    return [pscustomobject]@{
        Pass         = [int]$script:GateExitPass
        Violation    = [int]$script:GateExitViolation
        Undetermined = [int]$script:GateExitUndetermined
    }
}

<#
.SYNOPSIS
    幂等导入 GateRuntime（重复调用为 no-op；仅 -Force 才重建）。
.DESCRIPTION
    动机：Meta/Declarations 44 件里出现 50 次 `Import-Module GateRuntime -Force`，
    同一进程内反复重建模块（重设别名、重读 DirectoryMap）。
    已加载且路径一致 ⇒ 直接返回现有模块；否则按本文件所在目录导入。
    注意：**首次**导入仍需调用方定位本文件（`Get-GateRepoRoot`）；本函数用于消除后续重复导入。
.PARAMETER Force
    强制重建（会重置别名与退出码常量；一般不需要）。
.OUTPUTS
    [PSModuleInfo] 已加载的 GateRuntime 模块。
#>
function Import-GateRuntime {
    param([switch]$Force)

    $path = Join-Path $PSScriptRoot 'GateRuntime.psm1'
    $existing = Get-Module -Name 'GateRuntime' -ErrorAction SilentlyContinue
    if ($null -ne $existing -and -not $Force -and $existing.Path -eq $path) { return $existing }
    return Import-Module $path -Force -DisableNameChecking -PassThru
}

<#
.SYNOPSIS
    显性「未验证」裁决出口（SKIP 不许算绿）。
.DESCRIPTION
    目标缺失 / 无对照对象 → 输出 `[SKIPPED_NO_TARGET]`，与 PASS 的 `[PASS]`/`OK` 字样严格区分。
    跳过不是通过：台账统计与人工阅读都必须能一眼区分。退出码由调用方决定（通常仍为 0）。
#>
function Write-GateSkip {
    param(
        [Parameter(Mandatory = $true)][string]$Reason,
        [string]$Key = ''
    )
    $label = if ($Key) { "$Key " } else { '' }
    Write-Host "[SKIPPED_NO_TARGET] ${label}未验证：$Reason" -ForegroundColor Yellow
}

<#
.SYNOPSIS
    统一门禁裁决尾巴（单义退出码：0 无违规 / -Check 且违规 → 1）。
.DESCRIPTION
    · 无违规 → 打印 [PASS]，exit 0；
    · 有违规 → 打印明细；-Check 时 exit 1（判定模式），否则 exit 0（报告模式）；
    · 前置缺失等异常场景由调用方 `throw` 或在调用前 `exit 2`（本函数不产生 2）。
.PARAMETER Violations
    违规串列表。
.PARAMETER Check
    判定模式：有违规即退出码 1。
.PARAMETER Key
    门名（用于 PASS 行）。
.PARAMETER ReportOnly
    报告模式提示语（有违规且非 -Check 时附加）。
#>
function Write-GateVerdict {
    param(
        [System.Collections.Generic.List[string]]$Violations,
        [switch]$Check,
        [string]$Key = '',
        [string]$ReportOnly = '（报告模式：不设退出码；加 -Check 进入门禁判定）',
        # 机器可读出口（治 R2）：-Json 输出整块结果；-EmitActual 只输出 actual 表。
        # JSON 模式禁止任何彩色 Write-Host（否则污染 stdout 解析）；退出码语义不变。
        [switch]$Json,
        [switch]$EmitActual,
        [System.Collections.IDictionary]$Actual,
        # 门内保留的修复口径 / 判据摘要（统一文案出口，避免各门各写一套头尾）
        [string]$Hint = ''
    )
    $v = if ($null -eq $Violations) { @() } else { @($Violations) }
    $count = $v.Count
    $exit = if ($count -eq 0) { 0 } elseif ($Check) { 1 } else { 0 }

    $actualTable = [ordered]@{}
    if ($null -ne $Actual) {
        foreach ($k in @($Actual.Keys)) { $actualTable[[string]$k] = $Actual[$k] }
    }
    if ($EmitActual) {
        [ordered]@{ gate = $Key; actual = $actualTable } | ConvertTo-Json -Depth 8 -Compress
        exit $exit
    }
    if ($Json) {
        [ordered]@{
            gate       = $Key
            passed     = ($count -eq 0)
            violations = @($v)
            actual     = $actualTable
        } | ConvertTo-Json -Depth 8 -Compress
        exit $exit
    }

    if ($count -eq 0) {
        Write-Host ""
        Write-Host "  [PASS] $Key：0 违规" -ForegroundColor Green
        Write-Host ""
        exit 0
    }
    Write-Host ""
    Write-Host "  违规明细（$count 项）：" -ForegroundColor Yellow
    foreach ($x in $v) { Write-Host "    - $x" -ForegroundColor Yellow }
    Write-Host ""
    if ($Hint) { Write-Host "  修复口径：$Hint" -ForegroundColor DarkGray; Write-Host "" }
    if ($Check) { exit 1 }
    if ($ReportOnly) { Write-Host $ReportOnly -ForegroundColor DarkGray }
    exit 0
}

<#
.SYNOPSIS
    基线核减判据（只减不增）：新增键（不在基线表中）或计数上涨即违规。
.DESCRIPTION
    上提自 Modules\Node\NodeGateCommon.ps1（全局化后 Node 侧可复用）。
.PARAMETER Counts
    实测计数（键 → 计数）。
.PARAMETER Baselines
    基线表（键 → 冻结值）。
.PARAMETER Label
    违规文案前缀（如「AgentFrameworkSingleSource」）。
.PARAMETER BaselineError
    基线加载/解析异常文案（非空 ⇒ 基线不可解析，判不可判定；禁止当作「未增长」放行）。
.PARAMETER AsResult
    返回结构化结果 `{Status; BaselineError; Violations; Counts; Baselines}`；
    Status ∈ BaselineInvalid / NoIncrease / Increase。未指定时返回旧的 `List[string]`（兼容既有调用）。
.OUTPUTS
    默认 `List[string]` 违规清单；`-AsResult` 时返回结构化对象。
#>
function Test-BaselineNoIncrease {
    param(
        [AllowNull()][hashtable]$Counts,
        [AllowNull()][hashtable]$Baselines,
        [string]$Label = '',
        [string]$BaselineError = '',
        [switch]$AsResult
    )
    $violations = [System.Collections.Generic.List[string]]::new()

    # 基线不可解析 ≠ 未增长：此前 Check-ZeroRetry 把解析异常吞成 fail-open，这里改为 fail-closed。
    $baselineInvalid = ($null -eq $Baselines) -or (-not [string]::IsNullOrWhiteSpace($BaselineError))
    if ($baselineInvalid) {
        $reason = if (-not [string]::IsNullOrWhiteSpace($BaselineError)) { $BaselineError } else { '基线表为空（未加载）' }
        $violations.Add("$Label 基线不可解析：$reason（不可判定，非通过）")
        if ($AsResult) {
            return [pscustomobject]@{
                Status        = 'BaselineInvalid'
                BaselineError = $reason
                Violations    = $violations
                Counts        = $Counts
                Baselines     = $Baselines
            }
        }
        return $violations
    }

    $increased = $false
    if ($null -ne $Counts) {
        foreach ($k in $Counts.Keys) {
            if (-not $Baselines.ContainsKey($k)) {
                $violations.Add("$Label 新增散落点：$k = $($Counts[$k])（基线表无此项 ⇒ 只减不增禁新增）")
                $increased = $true
                continue
            }
            if ($Counts[$k] -gt $Baselines[$k]) {
                $violations.Add("$Label 取值点新增：$k = $($Counts[$k]) > 基线 $($Baselines[$k])（只减不增）")
                $increased = $true
            }
        }
    }

    if ($AsResult) {
        return [pscustomobject]@{
            Status        = if ($increased) { 'Increase' } else { 'NoIncrease' }
            BaselineError = ''
            Violations    = $violations
            Counts        = $Counts
            Baselines     = $Baselines
        }
    }
    return $violations
}

# 区域/容器键字形单源（D7）：声明产物（ui-regions.json / ui-declaration.generated.json）
# 的容器键一律小写；提取正则只允许小写，禁各门自写大小写不同的变体（大写键不再被静默接受）。
$script:GateRegionKeyPattern = 'region-[a-z0-9-]+'

<#
.SYNOPSIS
    区域/容器键提取正则（单源常量；调用方自行拼 JSON 引号与捕获组）。
#>
function Get-GateRegionKeyPattern {
    return $script:GateRegionKeyPattern
}

<#
.SYNOPSIS
    定位 LedgerForge 离线工具（未构建 → $null；调用方应 Assert-GatePrerequisite）。
#>
function Get-GateLedgerForge {
    param([string]$RepoRoot = '')
    $root = if ($RepoRoot) { $RepoRoot } else { Get-GateRepoRoot }
    foreach ($configuration in @('Debug', 'Release')) {
        $candidate = Join-Path $root "Frame\CsharpPrj\Plate\DeclarativeLedger.Tools\LedgerForge\BuildOutput\$configuration\LedgerForge.exe"
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    return $null
}

<#
.SYNOPSIS
    键集对拍（唯一比较实现 = C# LedgerKeyClosure(Ordinal)，经 LedgerForge compare-faces）。
.DESCRIPTION
    只统一「键集比较」，不承载任何业务判据；业务规则仍留调用门。
    返回 OnlyLeft（仅在 Reference）/ OnlyRight（仅在 Other）。
#>
function Compare-GateKeySet {
    param(
        [string[]]$Reference,
        [string[]]$Other,
        [string]$ReferenceLabel = 'reference',
        [string]$OtherLabel = 'other',
        [string]$RepoRoot = ''
    )

    $ledgerForge = Get-GateLedgerForge -RepoRoot $RepoRoot
    if (-not $ledgerForge) { throw 'LedgerForge 未构建（缺 BuildOutput\Debug|Release\LedgerForge.exe），键集对拍不可执行' }

    function ConvertTo-GateJsonArray([string[]]$Items) {
        $quoted = @()
        foreach ($item in @($Items)) {
            if ([string]::IsNullOrEmpty($item)) { continue }
            $quoted += '"' + ($item.Replace('\', '\\').Replace('"', '\"')) + '"'
        }
        return '[' + ($quoted -join ',') + ']'
    }

    $probeDir = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), ('keyset-' + [guid]::NewGuid().ToString('N')))
    try {
        New-Item -ItemType Directory -Force -Path $probeDir | Out-Null
        $refPath = Join-Path $probeDir 'reference.json'
        $otherPath = Join-Path $probeDir 'other.json'
        $outPath = Join-Path $probeDir 'result.json'
        [System.IO.File]::WriteAllText($refPath, (ConvertTo-GateJsonArray @($Reference)))
        [System.IO.File]::WriteAllText($otherPath, (ConvertTo-GateJsonArray @($Other)))

        & $ledgerForge compare-faces --face "${ReferenceLabel}=$refPath" --face "${OtherLabel}=$otherPath" --ref $ReferenceLabel --out $outPath | Out-Null
        $code = $LASTEXITCODE
        if ($code -eq 2 -or -not (Test-Path -LiteralPath $outPath)) {
            throw "compare-faces 不可执行（退出码 $code）"
        }

        $payload = Get-Content -LiteralPath $outPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $onlyLeft = [System.Collections.Generic.List[string]]::new()
        $onlyRight = [System.Collections.Generic.List[string]]::new()
        foreach ($violation in @($payload.violations)) {
            if ($violation.kind -ceq 'missing') { $onlyLeft.Add([string]$violation.key) }
            elseif ($violation.kind -ceq 'extra') { $onlyRight.Add([string]$violation.key) }
        }
        return [pscustomobject]@{ OnlyLeft = @($onlyLeft); OnlyRight = @($onlyRight) }
    }
    finally {
        if (Test-Path -LiteralPath $probeDir) { Remove-Item -LiteralPath $probeDir -Recurse -Force }
    }
}

<#
.SYNOPSIS
    构造门禁上下文（参数协议 + 根定位 + 违规清单的单源）。
.DESCRIPTION
    把各门自写的「根定位 + 模式开关 + `$violations = List[string]::new()`」收敛为一次调用：
      · 根 = -Root（显式）> Get-GateRepoRoot（$ScriptRoot 上溯；与目录深度无关）。
      · 模式 = Check（判定）/ Detail（清单）/ Json（结构化输出）/ NegativeControl（判据自检）。
.PARAMETER ScriptRoot
    门禁脚本目录（缺省 = 调用方 $PSScriptRoot；不传则由 Get-GateRepoRoot 取本文件位置）。
.PARAMETER Root
    显式仓库超根（外部编排传入时优先）。
#>
function New-GateContext {
    param(
        [string]$ScriptRoot = '',
        [string]$Root = '',
        [switch]$Check,
        [switch]$Detail,
        [switch]$Json,
        [switch]$NegativeControl,
        [string]$Key = ''
    )
    $repo = if (-not [string]::IsNullOrWhiteSpace($Root)) { $Root }
            elseif (-not [string]::IsNullOrWhiteSpace($ScriptRoot)) { Get-GateRepoRoot -ScriptRoot $ScriptRoot }
            else { Get-GateRepoRoot }
    if (-not [string]::IsNullOrWhiteSpace([string]$repo)) {
        $repo = ([string]$repo).TrimEnd([char[]]@('\', '/'))
    }
    return [pscustomobject]@{
        Root            = $repo
        Check           = [bool]$Check
        Detail          = [bool]$Detail
        Json            = [bool]$Json
        NegativeControl = [bool]$NegativeControl
        Key             = $Key
        Violations      = [System.Collections.Generic.List[string]]::new()
    }
}

<#
.SYNOPSIS
    追加违规（文案单源：`[Id] Message`）。
.DESCRIPTION
    动机：18 份域内 Add-Violation 各自捕获 `$script:violations`，无法跨模块写调用方作用域，
    且存在两种拼写与三种消息前缀。本函数改为**显式传入清单**（`$ctx.Violations`），
    前缀统一为 `[Id]`（Id 已自带方括号时不再重复包裹）。
.PARAMETER Id
    判据编号（如 'C1'、'F2'；可自带 `[C1]`）
.PARAMETER Message
    违规文案
.PARAMETER Violations
    违规清单（`New-GateContext` 提供的 `List[string]`）
.EXAMPLE
    Add-GateViolation 'C1' '缺少声明' -Violations $ctx.Violations
#>
function Add-GateViolation {
    param(
        [Parameter(Mandatory = $true, Position = 0)][string]$Id,
        [Parameter(Mandatory = $true, Position = 1)][AllowEmptyString()][string]$Message,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Violations
    )
    $prefix = if ($Id -match '^\s*\[[^\]]+\]\s*$') { $Id.Trim() } else { '[' + $Id.Trim() + ']' }
    $Violations.Add("$prefix $Message")
}

<#
.SYNOPSIS
    对象属性安全取数（StrictMode 安全；对象空/属性缺失 ⇒ $null）。
.DESCRIPTION
    收敛 4 份域内 Get-Prop；同时支持 PSCustomObject（JSON 产物）与 IDictionary（声明表）。
.PARAMETER Object
    目标对象（可为 $null）
.PARAMETER Name
    属性/键名
#>
function Get-GateJsonProperty {
    param(
        [Parameter(Position = 0)][AllowNull()]$Object,
        [Parameter(Mandatory = $true, Position = 1)][string]$Name
    )
    if ($null -eq $Object) { return $null }
    if ($Object -is [System.Collections.IDictionary]) {
        if ($Object.Contains($Name)) { return $Object[$Name] }
        return $null
    }
    $prop = $Object.PSObject.Properties[$Name]
    if ($null -eq $prop) { return $null }
    return $prop.Value
}

<#
.SYNOPSIS
    前置面探测（不 exit）：文件/目录缺失清单。
.DESCRIPTION
    与 Assert-GatePrerequisite 同口径但不终止进程，供报告模式/编排分流使用，
    使「前置缺失（2）」与「违规（1）」在调用方仍可区分。
.OUTPUTS
    { Ok; Label; Key; Missing }
#>
function Test-GatePrerequisite {
    param(
        [Parameter(Mandatory = $true)][string[]]$Path,
        [string]$Label = '前置面',
        [string]$Key = ''
    )
    $missing = [System.Collections.Generic.List[string]]::new()
    foreach ($p in @($Path)) {
        if ([string]::IsNullOrWhiteSpace($p) -or -not (Test-Path -LiteralPath $p)) { $missing.Add([string]$p) }
    }
    return [pscustomobject]@{
        Ok      = ($missing.Count -eq 0)
        Label   = $Label
        Key     = $Key
        Missing = $missing.ToArray()
    }
}

<#
.SYNOPSIS
    前置面断言：文件/目录缺失 ⇒ 统一 exit 2（不可判定 ≠ 通过）。
.DESCRIPTION
    各门此前用 Write-Error/throw/裸 exit 2 三种写法，文案各异；本函数是唯一前置缺失出口。
    `Write-Error …; exit 2` 在 `$ErrorActionPreference='Stop'` 下会先终止，exit 2 不可达；
    本函数只用 Write-Host + exit，保证退出码可达。
.PARAMETER Path
    前置文件/目录（可多值）
.PARAMETER Throw
    不终止进程，改为抛错（单元测试/外部编排捕获后自行归类）。
#>
function Assert-GatePrerequisite {
    param(
        [Parameter(Mandatory = $true)][string[]]$Path,
        [string]$Label = '前置面',
        [string]$Key = '',
        [switch]$Throw
    )
    $probe = Test-GatePrerequisite -Path $Path -Label $Label -Key $Key
    if ($probe.Ok) { return }

    $detail = ($probe.Missing -join '；')
    $message = "$Key 前置缺失：$Label = $detail（不可判定，非通过）"
    if ($Throw) { throw $message }

    Write-Host ""
    Write-Host "  [FAIL] $message" -ForegroundColor Yellow
    Write-Host ""
    exit $script:GateExitUndetermined
}

<#
.SYNOPSIS
    判据自证：正样本必过、负样本必红；未提供对照 ⇒ 判红。
.DESCRIPTION
    动机：各门内联手写「负对照」65 件 286 处，写法各异，且存在「恒假/恒真」假绿。
    判据载体 = 同一 Predicate 对 Good/Bad 样本的返回值（判据自证，禁执行者叙述）。

    每个 Case 必带：
      Id        判据编号（违规文案前缀）
      Predicate 单参函数块（输入文本 ⇒ $true 命中违规）
      Good      合法样本（Predicate 必须为 $false）
      Bad       违规样本（Predicate 必须为 $true）
    缺 Predicate 或缺任一对照样本 ⇒ 违规（"无自证不可采信"）。

    文件 fixture（可选，`-FixtureRoot`）：
      Good/Bad 若为含 `Path` + `Content` 的字典/对象，框架把 Content 写到 `<FixtureRoot>\<Path>`
      （UTF-8，必要时建目录），并把目标**绝对路径**传给 Predicate；其余样本原样透传。
      框架不删除已写文件（目录由调用方持有/清理）。
#>
function Assert-GateControls {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Cases,
        [string]$Label = '判据自证',
        [string]$FixtureRoot = ''
    )
    $issues = [System.Collections.Generic.List[string]]::new()
    if (@($Cases).Count -eq 0) {
        $issues.Add("$Label 无对照样本（判据无自证 ⇒ 不可采信）")
        return $issues
    }
    foreach ($c in @($Cases)) {
        # 样本既可为 hashtable（门脚本惯用）也可为 pscustomobject；两种成员的读法必须一致。
        $isDict = $c -is [System.Collections.IDictionary]
        $hasMember = {
            param($n)
            if ($isDict) { return $c.Contains($n) }
            return (@($c.PSObject.Properties.Name) -contains $n)
        }
        $getMember = {
            param($n)
            if ($isDict) { if ($c.Contains($n)) { return $c[$n] } else { return $null } }
            if (@($c.PSObject.Properties.Name) -contains $n) { return $c.$n }
            return $null
        }
        $resolveSample = {
            param($Sample)
            if ($null -eq $Sample -or [string]::IsNullOrWhiteSpace($FixtureRoot)) { return $Sample }
            $sampleIsDict = $Sample -is [System.Collections.IDictionary]
            $sampleHas = {
                param($n)
                if ($sampleIsDict) { return $Sample.Contains($n) }
                return (@($Sample.PSObject.Properties.Name) -contains $n)
            }
            if (-not ((& $sampleHas 'Path') -and (& $sampleHas 'Content'))) { return $Sample }
            $relative = if ($sampleIsDict) { [string]$Sample['Path'] } else { [string]$Sample.Path }
            $content = if ($sampleIsDict) { [string]$Sample['Content'] } else { [string]$Sample.Content }
            if ([string]::IsNullOrWhiteSpace($relative)) { return $Sample }
            $target = if ([System.IO.Path]::IsPathRooted($relative)) { $relative } else { Join-Path $FixtureRoot $relative }
            $dir = Split-Path -Parent $target
            if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
            [System.IO.File]::WriteAllText($target, $content, [System.Text.Encoding]::UTF8)
            return $target
        }

        $id = if (& $hasMember 'Id') { [string](& $getMember 'Id') } else { '<未编号>' }
        $predicate = if (& $hasMember 'Predicate') { & $getMember 'Predicate' } else { $null }
        if ($null -eq $predicate) {
            $issues.Add("$Label [$id] 缺 Predicate（判据不可执行）")
            continue
        }

        $hasGood = & $hasMember 'Good'
        $hasBad = & $hasMember 'Bad'
        if (-not $hasGood -and -not $hasBad) {
            $issues.Add("$Label [$id] 缺正/负对照样本（无自证 ⇒ 不可采信）")
            continue
        }
        if ($hasGood) {
            $good = $false
            try { $good = [bool](& $predicate (& $resolveSample (& $getMember 'Good'))) } catch { $issues.Add("$Label [$id] 正对照执行异常：$($_.Exception.Message)"); continue }
            if ($good) { $issues.Add("$Label [$id] 正对照失败：合法样本被判定为违规") }
        }
        if ($hasBad) {
            $bad = $true
            try { $bad = [bool](& $predicate (& $resolveSample (& $getMember 'Bad'))) } catch { $issues.Add("$Label [$id] 负对照执行异常：$($_.Exception.Message)"); continue }
            if (-not $bad) { $issues.Add("$Label [$id] 负对照失败：违规样本未被捕获（判据空转）") }
        }
    }
    return $issues
}


<#
.SYNOPSIS
    仓库检索命中数（命中文件数）——4 份域内 Get-SearchFileCount 的统一上提。
.DESCRIPTION
    委托 `Frame\Invoke\Invoke-Frame.ps1 -Action search-repo -List`，解析「合计命中：N」，
    兼容旧输出的「（N 命中）」尾注。
    不可用（入口/仓根/宿主缺失、子进程非 0、无解析结果）⇒ **返回 -1**；
    调用方必须把 -1 视为违规或不可判定，禁止当作 0。
.PARAMETER Pattern
    检索词/正则（配合 -Regex）
.PARAMETER PathSpec
    路径规格（透传 search-repo）
.PARAMETER Repo
    仓库根
.PARAMETER Regex
    以正则检索
.PARAMETER Untracked
    含未跟踪文件
.PARAMETER SearchEntry
    检索入口（默认 `<Repo>\Frame\Invoke\Invoke-Frame.ps1`）
.PARAMETER ShellExe
    宿主可执行文件（默认当前 $PSHOME 下的 pwsh，其次 powershell）
.OUTPUTS
    [int] 命中文件数；不可用 ⇒ -1
#>
function Get-GateSearchFileCount {
    param(
        [Parameter(Mandatory = $true, Position = 0)][string]$Pattern,
        [Parameter(Mandatory = $true, Position = 1)][string]$PathSpec,
        [Parameter(Mandatory = $true, Position = 2)][string]$Repo,
        [switch]$Regex,
        [switch]$Untracked,
        [string]$SearchEntry = '',
        [string]$ShellExe = ''
    )

    if ([string]::IsNullOrWhiteSpace($Repo) -or -not (Test-Path -LiteralPath $Repo)) { return -1 }

    $entry = if ([string]::IsNullOrWhiteSpace($SearchEntry)) { Join-Path $Repo 'Frame\Invoke\Invoke-Frame.ps1' } else { $SearchEntry }
    if (-not (Test-Path -LiteralPath $entry)) { return -1 }

    $shell = $ShellExe
    if ([string]::IsNullOrWhiteSpace($shell)) {
        foreach ($candidate in @((Join-Path $PSHOME 'pwsh.exe'), (Join-Path $PSHOME 'powershell.exe'))) {
            if (Test-Path -LiteralPath $candidate) { $shell = $candidate; break }
        }
    }
    if ([string]::IsNullOrWhiteSpace($shell) -or -not (Test-Path -LiteralPath $shell)) { return -1 }

    $cliArgs = @('-NoProfile', '-File', $entry, '-Action', 'search-repo',
                 '-Pattern', $Pattern, '-PathSpec', $PathSpec, '-Repo', $Repo, '-List')
    if ($Regex) { $cliArgs += '-Regex' }
    if ($Untracked) { $cliArgs += '-Untracked' }

    $raw = @(& $shell @cliArgs 2>&1 | ForEach-Object { "$_" })
    $lastExit = Get-Variable -Name LASTEXITCODE -ErrorAction SilentlyContinue
    if ($null -ne $lastExit -and [int]$lastExit.Value -ne 0) { return -1 }

    foreach ($line in $raw) {
        if ($line -match '合计命中：\s*(\d+)') { return [int]$Matches[1] }
    }
    foreach ($line in $raw) {
        if ($line -match '（\s*(\d+)\s*命中）') { return [int]$Matches[1] }
    }
    return -1
}

# 源码扫描排除集（D5 单源）：obj / bin / BuildOutput / _deprecated 统一口径。
# 此前 6 套写法（缺 BuildOutput、缺 _deprecated、加非捕获组、二次 Where 过滤）导致漂移。
$script:GateSourceExcludePattern = '\\(obj|bin|BuildOutput|_deprecated)\\'

# 未识别 kind（A7 单源）：与 C# Agent.Contract.Api.RecognizedKinds.Unrecognized 同值；
# 生成物事实可由 LedgerForge contract-facts 复核（constantsStale=false 时等价）。
$script:GateUnrecognizedKind = 'unrecognized'

<#
.SYNOPSIS
    源码扫描排除集（D5 单源），供 Get-GateFiles -Exclude / Where-Object -notmatch 使用。
#>
function Get-GateSourceExcludePattern {
    return $script:GateSourceExcludePattern
}

<#
.SYNOPSIS
    未识别 kind 常量（A7 单源），替代各门散落的 'unrecognized' 字面量。
#>
function Get-GateUnrecognizedKind {
    return $script:GateUnrecognizedKind
}

<# 契约事实（A7）已归位到 GateFacts.psm1：Get-GateContractFacts（取数 + 过期断言单源）。 #>


# 兼容别名：域侧既有调用名（18 份 Add-Violation / 4 份 Get-Prop）无需改名即可接管。
New-Alias -Name Add-Violation -Value Add-GateViolation
New-Alias -Name Get-Prop -Value Get-GateJsonProperty

# 显式导出面（自有 API + 门禁面向的目录映射 API）：
#   固定契约，禁依赖「默认全导出」的隐式面（防随导入件漂移）。
Export-ModuleMember -Function @(
    # 自有（门禁运行时）
    'Get-GateRepoRoot',
    'Find-GateScript',
    'New-GateResult',
    'Write-GateHeader',
    'Write-GateVerdict',
    'Test-BaselineNoIncrease',
    'New-GateContext',
    'Get-GateExitCodePolicy',
    'Import-GateRuntime',
    'Test-GatePrerequisite',
    'Assert-GatePrerequisite',
    'Add-GateViolation',
    'Get-GateJsonProperty',
    'Get-GateSearchFileCount',
    'Assert-GateControls',
    'Get-GateRegionKeyPattern',
    'Get-GateSourceExcludePattern',
    'Get-GateUnrecognizedKind',
    'Get-GateLedgerForge',
    'Compare-GateKeySet',
    'Write-GateSkip',
    # 目录映射（委托 DirectoryMap；一键装载供门禁消费）
    'Get-SuperRoot',
    'Get-RepositoryRoot',
    'Get-FrameRoot',
    'Get-MappedPath',
    'Get-DomainPaths',
    'Get-DomainPath',
    'Get-GateDomains',
    'Get-GateScriptOverride',
    'Get-GateScriptLeafName',
    'Resolve-GateScript',
    'Get-GateScriptCell'
) -Alias @(
    'Add-Violation',
    'Get-Prop'
)

