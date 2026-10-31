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
#>
function Test-BaselineNoIncrease {
    param(
        [hashtable]$Counts,
        [hashtable]$Baselines,
        [string]$Label
    )
    $violations = [System.Collections.Generic.List[string]]::new()
    foreach ($k in $Counts.Keys) {
        if (-not $Baselines.ContainsKey($k)) {
            $violations.Add("$Label 新增散落点：$k = $($Counts[$k])（基线表无此项 ⇒ 只减不增禁新增）")
            continue
        }
        if ($Counts[$k] -gt $Baselines[$k]) {
            $violations.Add("$Label 取值点新增：$k = $($Counts[$k]) > 基线 $($Baselines[$k])（只减不增）")
        }
    }
    return $violations
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
    前置面断言：文件/目录缺失 ⇒ 统一 exit 2（不可判定 ≠ 通过）。
.DESCRIPTION
    各门此前用 Write-Error/throw/裸 exit 2 三种写法，文案各异；本函数是唯一前置缺失出口。
#>
function Assert-GatePrerequisite {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [string]$Label = '前置面',
        [string]$Key = ''
    )
    if (Test-Path -LiteralPath $Path) { return }
    Write-Host ""
    Write-Host "  [FAIL] $Key 前置缺失：$Label = $Path（不可判定，非通过）" -ForegroundColor Yellow
    Write-Host ""
    exit 2
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
#>
function Assert-GateControls {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Cases,
        [string]$Label = '判据自证'
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
            try { $good = [bool](& $predicate (& $getMember 'Good')) } catch { $issues.Add("$Label [$id] 正对照执行异常：$($_.Exception.Message)"); continue }
            if ($good) { $issues.Add("$Label [$id] 正对照失败：合法样本被判定为违规") }
        }
        if ($hasBad) {
            $bad = $true
            try { $bad = [bool](& $predicate (& $getMember 'Bad')) } catch { $issues.Add("$Label [$id] 负对照执行异常：$($_.Exception.Message)"); continue }
            if (-not $bad) { $issues.Add("$Label [$id] 负对照失败：违规样本未被捕获（判据空转）") }
        }
    }
    return $issues
}


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
    'Assert-GatePrerequisite',
    'Assert-GateControls',
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
)

