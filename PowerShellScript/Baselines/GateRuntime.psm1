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
    按名发现 spec（Gate\Spec\<Name>.gate.psd1；缺省返回空串）。
#>
function Find-GateSpec {
    param([Parameter(Mandatory = $true)][string]$Name)
    if ($Name -match '[\\/:*?"<>|]') { return '' }
    $specRoot = Join-Path (Get-MappedPath -Key 'CentralTestGate') 'Spec'
    $path = Join-Path $specRoot ($Name + '.gate.psd1')
    if (-not (Test-Path -LiteralPath $path)) { return '' }
    return (Resolve-Path -LiteralPath $path).Path
}

<#
.SYNOPSIS
    门禁执行目标：优先 Check-<Name>.ps1，其次 spec（统一走 Invoke-GateRunner.ps1）。
.OUTPUTS
    { Kind = script|spec|none; Script = 绝对路径; Args = 传给脚本的参数表 }
#>
function Find-GateTarget {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [string]$Domain = '',
        [switch]$AllowFallback,
        [hashtable]$Args = @{}
    )

    $script = ''
    try { $script = Resolve-GateScript -Gate $Name -Domain $Domain -AllowFallback:$AllowFallback } catch { $script = '' }
    if ($script -and (Test-Path -LiteralPath $script)) {
        return [pscustomobject]@{ Kind = 'script'; Script = $script; Args = $Args }
    }

    $spec = Find-GateSpec -Name $Name
    if (-not [string]::IsNullOrWhiteSpace($spec)) {
        $runner = Join-Path (Get-MappedPath -Key 'CentralTestGate') 'Invoke-GateRunner.ps1'
        if (Test-Path -LiteralPath $runner) {
            # spec 目标只接受 runner 声明的参数：清单里的脚本专属开关（如 -NegativeControl）不得转发，
            # 否则参数绑定失败会把可判定的门变成 exit 2。负对照在 spec 侧由 Negative 声明承载。
            $runnerParams = (Get-Command -Name $runner).Parameters
            $merged = @{}
            foreach ($k in $Args.Keys) {
                if (-not $runnerParams.ContainsKey([string]$k)) { continue }
                $merged[$k] = $Args[$k]
            }
            $merged['Spec'] = $spec
            $merged['Check'] = $true
            return [pscustomobject]@{ Kind = 'spec'; Script = $runner; Args = $merged }
        }
    }

    return [pscustomobject]@{ Kind = 'none'; Script = $script; Args = $Args }
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
    进程内引导：GateRuntime 已导入时规范化仓库根（可顺带导入伴生模块）。
.DESCRIPTION
    与 Baselines\Initialize-GateModule.ps1 同名同义：
      · .ps1 负责**跨进程自举**（定位 GateRuntime + 导入 + 返回根）；
      · 本函数负责**已导入后的进程内**分支（不再重复定位/导入）。
    域侧独立进程用 .ps1；已 import 的编排器/组合门可直接调用本函数。
.PARAMETER ScriptRoot
    调用方脚本目录（缺省 = 本文件目录）。
.PARAMETER CompanionModule
    随同导入的 Baselines 模块文件名（如 'GateFileIndex.psm1'），导入到 Global 作用域。
.PARAMETER Force
    伴生模块强制重建。
.OUTPUTS
    [string] 仓库根（已 TrimEnd 分隔符）
#>
function Initialize-GateModule {
    param(
        [string]$ScriptRoot = '',
        [string[]]$CompanionModule = @(),
        [switch]$Force
    )

    $root = if (-not [string]::IsNullOrWhiteSpace($ScriptRoot)) { Get-GateRepoRoot -ScriptRoot $ScriptRoot }
            else { Get-GateRepoRoot }
    if ([string]::IsNullOrWhiteSpace([string]$root)) { throw 'Initialize-GateModule 未定位仓库根（Get-GateRepoRoot 返回空）' }
    $root = ([string]$root).TrimEnd([char[]]@('\', '/'))

    foreach ($name in @($CompanionModule)) {
        if ([string]::IsNullOrWhiteSpace($name)) { continue }
        $path = Join-Path $root ("Frame\PowerShellScript\Baselines\$name")
        if (-not (Test-Path -LiteralPath $path)) { throw "Initialize-GateModule 伴生模块缺失：$path" }
        Import-Module $path -Scope Global -DisableNameChecking -Force:$Force | Out-Null
    }
    return $root
}

<#
.SYNOPSIS
    计数断言表（表驱动 + [待消除] 渲染 + 硬/软分流）—— 6 个 Data 计数表门的统一执行器。
.DESCRIPTION
    动机（GC §5.1 T2）：Check-StorageWriteEntries / GraphPathSingleSource / ReadIdentitySingleSource /
    ConversationReadSingleSource / SelectionContract / SessionDuplication 各自复制「逐条比数 → 拼违规 →
    打印待消除」循环，硬/软分流与「检索不可用 = 缺证即违规」口径还各自漂移。

    每条断言（hashtable 或 pscustomobject）：
      Id       判据编号（必填；直接作为违规文案前缀，不加方括号）
      Title    判据标题（Name 亦可）
      Actual   实测值（int；-1 表示检索不可用）
      Measure  取数脚本块（未给 Actual 时调用，须返回 int）
      Pattern / PathSpec / Repo / Regex / Untracked / SearchEntry / ShellExe
               未给 Actual/Measure 时的自动检索取数（委托 Get-GateSearchFileCount；-RepoRoot 可把 Repo 名解析为绝对路径）
      Expected 期望值（Mode=eq/le/ge）
      Baseline 棘轮基线（Mode=target/le；缺省 soft 目标比较的硬阈）
      Target   soft 目标（非 -Final 时：Actual > Target 且 ≤ Baseline ⇒ 待消除；-Final 时升为硬阈）
      Mode     eq | le | ge | target（缺省：有 Target/Baseline ⇒ target；有 Expected ⇒ eq；否则 le）
      Kind     count | control | attest | warn
               control = 正/负对照自证（Id/Predicate/Good/Bad），委托 Assert-GateControls；
               attest 只可见化，不判失败；
               warn   求值照常（eq/le/ge/target 都判），判定不符进告警通道、不进违规；
                      但「判别不可执行」类（缺 Actual/Measure、Actual=-1 缺证、缺 Expected/Baseline、
                      缺 Baseline/Target、未知 Mode、判据面失效）仍进违规 —— fail-closed 不因降级告警而失守
      Batch    批次号（待消除/运行期文案）
      Pattern  检索词（Actual=-1 时回显）

    退出码：本函数**不 exit**（除 -Verdict）；返回结构化结果，调用方交给 Write-GateVerdict。
.PARAMETER Checks
    断言表（可空；空表判违规「无断言不可采信」）。
.PARAMETER Key
    门名（标题/摘要/违规文案前缀）。
.PARAMETER Check
    判定模式：结果 Exit = 1（有违规）；仅影响返回值与 -Verdict。
.PARAMETER Final
    终态模式：Actual 超过 Target 即硬违规（否则为待消除）。
.PARAMETER Detail
    逐条打印实测/阈值。
.PARAMETER NoHeader
    不打印 Write-GateHeader（调用方已自绘标题时）。
.PARAMETER FixtureRoot
    `Kind=control` 的文件型对照样本落盘目录（逐条可用 FixtureRoot 覆盖）。
.PARAMETER Violations
    既有违规清单（`$ctx.Violations`）；本批违规追加进去。
.PARAMETER Warnings
    既有告警清单；`Kind=warn` 的判定不符项追加进去（与违规清单分账，不影响 Exit）。
.PARAMETER Hint
    -Verdict 时传给 Write-GateVerdict 的修复口径。
.PARAMETER Verdict
    本批结束后直接调用 Write-GateVerdict 并 exit（终止式；与旧门禁尾巴等价）。
.OUTPUTS
    { Key; Total; Search; Attest; Passed; Pending; Warnings; WarningCount; Violations; ViolationCount; Exit }
#>
function Invoke-GateCountChecks {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [AllowEmptyCollection()]
        [object[]]$Checks,

        [string]$Key = '',
        [switch]$Check,
        [switch]$Final,
        [switch]$Detail,
        [switch]$NoHeader,

        [System.Collections.Generic.List[string]]$Violations,
        [System.Collections.Generic.List[string]]$Warnings,
        [string]$Hint = '',
        [switch]$Verdict,

        # 自动检索取数的调用级默认（断言可逐条覆盖）。
        [string]$RepoRoot = '',
        [string]$SearchEntry = '',
        [string]$ShellExe = '',
        [string]$FixtureRoot = ''
    )

    $found = [System.Collections.Generic.List[string]]::new()
    $warned = [System.Collections.Generic.List[string]]::new()
    $pending = [System.Collections.Generic.List[string]]::new()
    $attest = [System.Collections.Generic.List[string]]::new()
    $passed = 0
    $search = 0
    $control = 0
    $total = 0
    $label = if ([string]::IsNullOrWhiteSpace($Key)) { 'GateCountChecks' } else { $Key }

    if (-not $NoHeader) { Write-GateHeader "$label（计数断言表）" }

    foreach ($c in @($Checks)) {
        if ($null -eq $c) { continue }
        $total++

        $id = [string](Get-GateJsonProperty $c 'Id')
        $title = [string](Get-GateJsonProperty $c 'Title')
        if ([string]::IsNullOrWhiteSpace($title)) { $title = [string](Get-GateJsonProperty $c 'Name') }
        $batch = [string](Get-GateJsonProperty $c 'Batch')
        $kind = [string](Get-GateJsonProperty $c 'Kind')
        if ([string]::IsNullOrWhiteSpace($kind)) { $kind = 'count' }
        $batchText = if ([string]::IsNullOrWhiteSpace($batch)) { '' } else { "（批 $batch）" }

        # Kind=warn：判定不符进告警通道；「判别不可执行」类仍留在 $found（fail-closed）。
        # 注意用 if/else 语句赋值：空 List 走表达式赋值会被管道枚举成 $null。
        $warnMode = ($kind -eq 'warn')
        $findings = $null
        # 注意：$Warnings 是参数（变量名大小写不敏感），本地清单必须另起名 $warned。
        if ($warnMode) { $findings = $warned } else { $findings = $found }

        if ($kind -eq 'attest') {
            $attest.Add("$id $title$batchText")
            continue
        }
        if ($kind -eq 'control') {
            # 正/负对照并入同一张断言表（T7：与被测判据共享 Predicate；缺 Good/Bad 即违规）。
            $caseFixture = [string](Get-GateJsonProperty $c 'FixtureRoot')
            if ([string]::IsNullOrWhiteSpace($caseFixture)) { $caseFixture = $FixtureRoot }
            foreach ($v in @(Assert-GateControls -Cases @($c) -Label $label -FixtureRoot $caseFixture)) {
                $found.Add([string]$v)
            }
            $control++
            continue
        }
        $search++

        # 取数：Actual 优先；否则 Measure 脚本块。
        $actual = -1
        $hasActual = $false
        $actualRaw = Get-GateJsonProperty $c 'Actual'
        if ($null -ne $actualRaw) { $actual = [int]$actualRaw; $hasActual = $true }
        else {
            $measure = Get-GateJsonProperty $c 'Measure'
            if ($measure -is [scriptblock]) { $actual = [int](& $measure); $hasActual = $true }
            else {
                # 自动检索：Pattern + PathSpec + Repo ⇒ 委托 Get-GateSearchFileCount（不可用仍返回 -1）。
                $pattern = [string](Get-GateJsonProperty $c 'Pattern')
                $pathSpec = [string](Get-GateJsonProperty $c 'PathSpec')
                $repo = [string](Get-GateJsonProperty $c 'Repo')
                if (-not [string]::IsNullOrWhiteSpace($pattern) -and -not [string]::IsNullOrWhiteSpace($pathSpec) -and -not [string]::IsNullOrWhiteSpace($repo)) {
                    $entry = [string](Get-GateJsonProperty $c 'SearchEntry')
                    if ([string]::IsNullOrWhiteSpace($entry)) { $entry = $SearchEntry }
                    $shell = [string](Get-GateJsonProperty $c 'ShellExe')
                    if ([string]::IsNullOrWhiteSpace($shell)) { $shell = $ShellExe }
                    $repoPath = $repo
                    if (-not [string]::IsNullOrWhiteSpace($RepoRoot) -and -not [System.IO.Path]::IsPathRooted($repo)) {
                        $repoPath = Join-Path $RepoRoot $repo
                    }
                    $actual = Get-GateSearchFileCount -Pattern $pattern -PathSpec $pathSpec -Repo $repoPath `
                        -Regex:([bool](Get-GateJsonProperty $c 'Regex')) `
                        -Untracked:([bool](Get-GateJsonProperty $c 'Untracked')) `
                        -SearchEntry $entry -ShellExe $shell
                    $hasActual = $true
                }
            }
        }
        if (-not $hasActual) {
            $found.Add("$id $title：缺 Actual/Measure（判据不可执行）")
            continue
        }

        # 检索不可用 ≠ 0 命中：缺证即违规（对齐 EmptyResultGate）。
        if ($actual -lt 0) {
            $pattern = [string](Get-GateJsonProperty $c 'Pattern')
            $found.Add("$id 检索不可用（pattern='$pattern'）—— 判据缺证即违规")
            continue
        }

        $mode = [string](Get-GateJsonProperty $c 'Mode')
        $expectedRaw = Get-GateJsonProperty $c 'Expected'
        $baselineRaw = Get-GateJsonProperty $c 'Baseline'
        $targetRaw = Get-GateJsonProperty $c 'Target'

        if ([string]::IsNullOrWhiteSpace($mode)) {
            if ($null -ne $targetRaw -or $null -ne $baselineRaw) { $mode = 'target' }
            elseif ($null -ne $expectedRaw) { $mode = 'eq' }
            else { $mode = 'le' }
        }
        $mode = $mode.ToLowerInvariant()

        $expected = $null
        if ($null -ne $expectedRaw) { $expected = [int]$expectedRaw }
        elseif ($null -ne $baselineRaw) { $expected = [int]$baselineRaw }

        # 判据活性（防恒绿）：阈值为正却实测 0 ⇒ 载体不可能命中（pattern 写坏/被转义）。
        # 仅对 eq/ge 生效：le/target 下实测 0 是「棘轮降到 0」的合法终态，
        # 若一并判红，收敛成功反被判失败（D 组 G5 接线事故）。le/target 的缺证保护见下方 -1 / 缺基线分支。
        if (($mode -eq 'eq' -or $mode -eq 'ge') -and $null -ne $expected -and $expected -gt 0 -and $actual -eq 0) {
            $found.Add("$id $title：判据面失效（阈值 $expected 但实测 0）—— 恒绿断言")
            if ($Detail) { Write-Host ("  [失效] {0,-4} {1,-46} {2,3}" -f $id, $title, $actual) -ForegroundColor Yellow }
            continue
        }

        if ($mode -eq 'eq') {
            if ($null -eq $expected) {
                $found.Add("$id $title：缺 Expected/Baseline（判别不可执行）")
                continue
            }
            if ($actual -ne $expected) {
                $findings.Add("$id $title：实测 $actual，期望 = $expected")
                if ($Detail) { Write-Host ("  [{0}] {1,-4} {2,-46} {3,3} (期望 {4})" -f $(if ($warnMode) { '告警' } else { '超阈' }), $id, $title, $actual, $expected) -ForegroundColor $(if ($warnMode) { 'DarkYellow' } else { 'Yellow' }) }
                continue
            }
            $passed++
        }
        elseif ($mode -eq 'le') {
            if ($null -eq $expected) {
                $found.Add("$id $title：缺 Expected/Baseline（判别不可执行）")
                continue
            }
            $ratchet = Test-BaselineNoIncrease -Counts @{ $id = $actual } -Baselines @{ $id = $expected } -Label "$label $id $title" -AsResult
            if ($ratchet.Status -eq 'Increase') {
                foreach ($v in @($ratchet.Violations)) { $findings.Add([string]$v) }
                if ($Detail) { Write-Host ("  [{0}] {1,-4} {2,-46} {3,3} (基线 {4})" -f $(if ($warnMode) { '告警' } else { '超阈' }), $id, $title, $actual, $expected) -ForegroundColor $(if ($warnMode) { 'DarkYellow' } else { 'Yellow' }) }
                continue
            }
            $passed++
        }
        elseif ($mode -eq 'ge') {
            if ($null -eq $expected) {
                $found.Add("$id $title：缺 Expected/Baseline（判别不可执行）")
                continue
            }
            if ($actual -lt $expected) {
                $findings.Add("$id $title：实测 $actual < 期望 $expected（下限不满足）")
                if ($Detail) { Write-Host ("  [{0}] {1,-4} {2,-46} {3,3} (期望 {4})" -f $(if ($warnMode) { '告警' } else { '欠阈' }), $id, $title, $actual, $expected) -ForegroundColor $(if ($warnMode) { 'DarkYellow' } else { 'Yellow' }) }
                continue
            }
            $passed++
        }
        elseif ($mode -eq 'target') {
            # target：非 Final = 基线内待消除；Final = Target 硬阈。
            $baseline = if ($null -ne $baselineRaw) { [int]$baselineRaw } else { $expected }
            $target = if ($null -ne $targetRaw) { [int]$targetRaw } else { $null }

            $limit = $null
            if ($Final -and $null -ne $target) { $limit = $target }
            elseif ($null -ne $baseline) { $limit = $baseline }
            elseif ($null -ne $target) { $limit = $target }
            if ($null -eq $limit) {
                $found.Add("$id $title：缺 Baseline/Target（判别不可执行）")
                continue
            }

            if ($actual -gt $limit) {
                $limitLabel = if ($Final -and $null -ne $target) { "目标 $target" } else { "基线 $baseline" }
                $findings.Add("$id $title：实测 $actual > $limitLabel$batchText —— 重复点新增/未收敛")
                if ($Detail) { Write-Host ("  [{0}] {1,-4} {2,-46} {3,3}  ({4})" -f $(if ($warnMode) { '告警' } else { '超阈' }), $id, $title, $actual, $limitLabel) -ForegroundColor $(if ($warnMode) { 'DarkYellow' } else { 'Yellow' }) }
                continue
            }
            if (-not $Final -and $null -ne $target -and $actual -gt $target) {
                $pending.Add("$id $title：实测 $actual → 目标 $target$batchText")
                if ($Detail) { Write-Host ("  [待消除] {0,-4} {1,-46} {2,3}  (基线 {3} / 目标 {4})" -f $id, $title, $actual, $baseline, $target) -ForegroundColor DarkGray }
                continue
            }
            $passed++
            if ($Detail) { Write-Host ("  [实测] {0,-4} {1,-46} {2,3}  (基线 {3} / 目标 {4})" -f $id, $title, $actual, $baseline, $target) -ForegroundColor DarkGray }
        }
        else {
            # 未知 Mode（如拼写 gte/target）不得静默降级为 target（那会改变硬/软分流口径）。
            $found.Add("$id $title：未知 Mode '$mode'（允许 eq/le/ge/target）—— 判别不可执行")
        }
    }

    if ($total -eq 0) { $found.Add("$label 无断言（判据无标的 ⇒ 不可采信）") }

    $modeText = if ($Final) { '终态模式 (-Final)' } else { '基线模式（拦新增重复点）' }
    Write-Host ("[$label] 计数断言表 · $modeText")
    Write-Host ("[$label] 断言 {0} 条（搜索 {1} / 对照 {2} / 运行期 {3}） | 通过 {4} | 待消除 {5} | 告警 {6} | 违规 {7}" -f `
        $total, $search, $control, $attest.Count, $passed, $pending.Count, $warned.Count, $found.Count)

    if ($pending.Count -gt 0) {
        Write-Host "[$label] --- 待消除（基线已锁定，批完成后转 Target 判据） ---"
        foreach ($p in $pending) { Write-Host "  - $p" }
    }
    if ($attest.Count -gt 0) {
        Write-Host "[$label] --- 运行期判据（不判失败） ---"
        foreach ($a in $attest) { Write-Host "  - $a" }
    }
    if ($warned.Count -gt 0) {
        Write-Host "[$label] 告警 $($warned.Count) 项（不进裁决，不阻断）："
        foreach ($w in $warned) { Write-Host "  - $w" }
    }
    if ($found.Count -gt 0) {
        Write-Host "[$label] 违规 $($found.Count) 项："
        foreach ($v in $found) { Write-Host "  - $v" }
    }

    if ($Verdict) {
        Write-GateVerdict -Violations $found -Check:$Check -Key $Key -Hint $Hint
        return
    }

    if ($null -ne $Violations) {
        foreach ($v in $found) { $Violations.Add($v) }
    }
    if ($null -ne $Warnings) {
        foreach ($w in $warned) { $Warnings.Add($w) }
    }

    $exit = 0
    if ($found.Count -gt 0 -and $Check) { $exit = 1 }
    return [pscustomobject]@{
        Key            = $Key
        Total          = $total
        Search         = $search
        Control        = $control
        Attest         = $attest.Count
        Passed         = $passed
        Pending        = $pending
        Warnings       = $warned
        WarningCount   = $warned.Count
        Violations     = $found
        ViolationCount = $found.Count
        Exit           = $exit
    }
}

<#
.SYNOPSIS
    规则表执行器（Ok 谓词逐条判定 + 统一违规文案）—— Presentation 规则表门的统一骨架。
.DESCRIPTION
    动机：Agent\Presentation 12 门各自复制「foreach 规则 → try/catch → 拼违规 → 打印 PASS/FAIL」循环，
    异常口径（是否吞掉消息）、缺谓词处理、文案前缀各不相同。

    规则对象（hashtable 或 pscustomobject）：
      Id     规则编号（缺省 R1/R2…；违规文案前缀 `[Id]`）
      Title  规则标题（Name 亦可；可为空 ⇒ 文案只留 `[Id]`）
      Ok     判定脚本块（必须返回布尔；返回值按 [bool] 强制；抛异常 ⇒ 违规，不终止门禁）

    判定顺序：逐条求值 `Ok`（异常即该条违规）→ 汇总 `Passed/Failed` → 返回结构化结果。
    本函数**不 exit**：调用方把 `.Violations` 并入 `$ctx.Violations` 后交给 Write-GateVerdict。
    正/负对照**不并入本表**：规则表断言对象是仓内事实，Good/Bad 是谓词自证；
    谓词自证请用 `Assert-GateControls`（T7），计数断言请用 `Invoke-GateCountChecks`。
.PARAMETER Rules
    规则表（空表 ⇒ 违规「无规则不可采信」）。
.PARAMETER Key
    门名（打印前缀；缺省 GateRuleSet）。
.PARAMETER Violations
    既有违规清单（`$ctx.Violations`）；失败项追加进去。
.PARAMETER Detail
    逐条打印判定结果（默认打印）。
.PARAMETER ShowException
    异常信息随违规文案一并输出（缺省吞掉，与多数现有门一致）。
.PARAMETER NoPrint
    不打印逐条 PASS/FAIL（仅收集结果）。
.OUTPUTS
    { Key; Total; Passed; Failed; Violations; ViolationCount; Exit }
#>
function Invoke-GateRuleSet {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [AllowEmptyCollection()]
        [object[]]$Rules,

        [string]$Key = '',

        [System.Collections.Generic.List[string]]$Violations,

        [switch]$Detail,
        [switch]$ShowException,
        [switch]$NoPrint
    )

    $found = [System.Collections.Generic.List[string]]::new()
    $label = if ([string]::IsNullOrWhiteSpace($Key)) { 'GateRuleSet' } else { $Key }
    $total = 0
    $passed = 0
    $failed = 0

    if (@($Rules).Count -eq 0) {
        $found.Add("$label：无规则不可采信（规则表为空）")
    }

    foreach ($r in @($Rules)) {
        if ($null -eq $r) { continue }
        $total++
        $id = [string](Get-GateJsonProperty $r 'Id')
        if ([string]::IsNullOrWhiteSpace($id)) { $id = "R$total" }
        $title = [string](Get-GateJsonProperty $r 'Title')
        if ([string]::IsNullOrWhiteSpace($title)) { $title = [string](Get-GateJsonProperty $r 'Name') }
        $text = if ([string]::IsNullOrWhiteSpace($title)) { "[$id]" } else { "[$id] $title" }

        $ok = $false
        $detailText = ''
        $predicate = Get-GateJsonProperty $r 'Ok'
        if ($predicate -isnot [scriptblock]) {
            $detailText = '缺 Ok 谓词（规则不可执行）'
        }
        else {
            try { $ok = [bool](& $predicate) }
            catch {
                $ok = $false
                if ($ShowException) { $detailText = [string]$_.Exception.Message }
            }
        }

        if ($ok) {
            $passed++
            if (-not $NoPrint) { Write-Host "  [PASS] $text" -ForegroundColor Green }
            if ($Detail) { Write-Host ("         {0}" -f $text) -ForegroundColor DarkGray }
        }
        else {
            $failed++
            $msg = if ([string]::IsNullOrWhiteSpace($detailText)) { $text } else { "$text（$detailText）" }
            $found.Add($msg)
            if (-not $NoPrint) { Write-Host "  [FAIL] $msg" -ForegroundColor Red }
        }
    }

    if ($null -ne $Violations) {
        foreach ($v in $found) { $Violations.Add($v) }
    }

    return [pscustomobject]@{
        Key            = $label
        Total          = $total
        Passed         = $passed
        Failed         = $failed
        Violations     = $found
        ViolationCount = $found.Count
        Exit           = if ($found.Count -gt 0) { [int]$script:GateExitViolation } else { [int]$script:GateExitPass }
    }
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
    两种参数集（互斥）：
      · Table（默认）：`-Counts` + `-Baselines` 计数表；
      · Scalar：`-Actual` + `-Expected [-Key]`，内部自动构成单行计数表（14 个棘轮门零样板）。
    未给 `-Expected` ⇒ 基线不可解析（fail-closed），不得当作「未增长」放行。
.PARAMETER Counts
    实测计数（键 → 计数）。
.PARAMETER Baselines
    基线表（键 → 冻结值）。
.PARAMETER Actual
    标量实测值（Scalar 参数集；必填）。
.PARAMETER Expected
    标量基线值（Scalar 参数集；缺省 ⇒ BaselineInvalid）。
.PARAMETER Key
    标量键名（默认 'count'；用于违规文案与结果回显）。
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
    [CmdletBinding(DefaultParameterSetName = 'Table')]
    param(
        [Parameter(ParameterSetName = 'Table')][AllowNull()][hashtable]$Counts,
        [Parameter(ParameterSetName = 'Table')][AllowNull()][hashtable]$Baselines,

        # 标量路径（14 个棘轮门的零样板入口）：单值 actual/expected 自动构成单行计数表。
        [Parameter(ParameterSetName = 'Scalar', Mandatory = $true)][int]$Actual,
        [Parameter(ParameterSetName = 'Scalar')][AllowNull()][Nullable[int]]$Expected,
        [Parameter(ParameterSetName = 'Scalar')][string]$Key = 'count',

        [string]$Label = '',
        [string]$BaselineError = '',
        [switch]$AsResult
    )
    $violations = [System.Collections.Generic.List[string]]::new()

    if ($PSCmdlet.ParameterSetName -eq 'Scalar') {
        $Counts = @{ $Key = [int]$Actual }
        if ($null -eq $Expected) {
            $Baselines = $null
            if ([string]::IsNullOrWhiteSpace($BaselineError)) {
                $BaselineError = "标量基线缺失（-Expected 未提供，key=$Key）"
            }
        }
        else {
            $Baselines = @{ $Key = [int]$Expected }
        }
    }

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

    批量采用契约（T7）：
      · **必须同时给 Good 与 Bad**（缺任一即违规：无自证不可采信）；只做正对照不算负对照。
      · Predicate 必须是**门禁实际判据本身**（同一脚本块/正则），不得另写一份合成检测逻辑。
      · Predicate 抛异常 = 违规（不是放行）；统一走 `-Violations` 收集 + `Write-GateVerdict`，禁 `throw` 收口。
      · `-Checks -Kind control` 可把 Case 直接并入 `Invoke-GateCountChecks` 的同一张断言表。
.PARAMETER Cases
    判据自证表（Id/Predicate/Good/Bad）。
.PARAMETER Label
    违规文案前缀。
.PARAMETER FixtureRoot
    文件型样本落盘目录（可选）。
.PARAMETER Violations
    既有违规清单（`$ctx.Violations`）；本批问题追加进去。
#>
function Assert-GateControls {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Cases,
        [string]$Label = '判据自证',
        [string]$FixtureRoot = '',
        [System.Collections.Generic.List[string]]$Violations
    )
    $issues = [System.Collections.Generic.List[string]]::new()
    if (@($Cases).Count -eq 0) {
        $issues.Add("$Label 无对照样本（判据无自证 ⇒ 不可采信）")
        if ($null -ne $Violations) { foreach ($i in $issues) { $Violations.Add([string]$i) } }
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
        if (-not $hasGood -or -not $hasBad) {
            $missing = @()
            if (-not $hasGood) { $missing += 'Good' }
            if (-not $hasBad) { $missing += 'Bad' }
            $issues.Add("$Label [$id] 缺对照样本（$($missing -join '/')）：无自证 ⇒ 不可采信")
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
    if ($null -ne $Violations) { foreach ($i in $issues) { $Violations.Add([string]$i) } }
    return $issues
}


<#
.SYNOPSIS
    仓库检索命中数（命中文件数）——4 份域内 Get-SearchFileCount 的统一上提。
.DESCRIPTION
    委托 `Frame\Invoke\Invoke-Frame.ps1 -Action search-repo -List`，解析「合计命中：N」，
    兼容旧输出的「（N 命中）」尾注。
    默认把 `Get-GateSourceExcludeGitPathSpec`（obj/bin/BuildOutput/_deprecated/测试产物/`Runtime\Payload` 镜像）
    作为排除 pathspec 一并下发给 git grep（git grep 本身已不搜被忽略文件，这是二层防护）。
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
.PARAMETER IncludeBuildOutput
    关闭缺省排除（仅统计「含镜像/构建产物」时才用；一般不应开启）
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
        [switch]$IncludeBuildOutput,
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

    $pathSpecs = @($PathSpec)
    if (-not $IncludeBuildOutput) { $pathSpecs += @(Get-GateSourceExcludeGitPathSpec) }

    $cliArgs = @('-NoProfile', '-File', $entry, '-Action', 'search-repo',
                 '-Pattern', $Pattern, '-PathSpec') + $pathSpecs + @('-Repo', $Repo, '-List')
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

# 源码扫描排除集（D5 单源）：obj / bin / BuildOutput / _deprecated / 测试与覆盖率产物 / 部署镜像。
# 此前 6 套写法（缺 BuildOutput、缺 _deprecated、加非捕获组、二次 Where 过滤）导致漂移。
#
# 排除依据（逐项可追溯，禁 `-Exclude *` 一把梭）：
#   obj / bin / BuildOutput   → .gitignore L9-L11（编译产物；`**/bin/`、`**/obj/`）
#   TestResults / coverage    → .gitignore L12-L13（测试/覆盖率产物）
#   .VSCodeCounter            → .gitignore L20（VS Code 统计产物）
#   Runtime\Payload           → .gitignore L22-L24（`/Runtime/` = Build-Distribution.bat 生成的部署镜像，
#                                其 `Payload\PowerShellScript\` 是全仓源码的过期副本；曾混入棘轮计数）
#   Runtime.staging           → .gitignore L25（同上的分阶段镜像目录）
#   node_modules / backups    → 非 .gitignore（外部依赖 / 手工备份），属构建与运维产物，非源码
#   _deprecated               → 仓内源码退役约定目录（保留原文便于追溯，但不入判据面）
#
# 刻意**不**排除的：
#   \Runtime\（裸段）        → 会误伤 `Docs\design\Runtime` 等真实文档目录（.gitignore L23 注释明示只锚定根）
#   \Data\（裸段）           → 会误伤真实源码目录 Data\；且 /Data/ 是运行期状态（db/日志），本就不在源码扩展面
# 活代码 vs 镜像的判据：镜像路径必含 `Runtime\Payload\` 或 `Runtime.staging\`，且由 git 忽略（`git check-ignore` 命中）；
# 同名源码只存在于 Frame\ / Projects\ / Environment\ 等工作树内，不在这两个段下。
$script:GateSourceExcludeSegments = @('obj', 'bin', 'BuildOutput', '_deprecated', 'TestResults', 'coverage', '.VSCodeCounter', 'node_modules', 'backups')
$script:GateSourceExcludeMirrors = @('Runtime\Payload', 'Runtime.staging')
$script:GateSourceExcludePattern = '\\(' + (($script:GateSourceExcludeSegments | ForEach-Object { [regex]::Escape($_) }) -join '|') + ')\\' +
                                   '|\\(' + (($script:GateSourceExcludeMirrors | ForEach-Object { [regex]::Escape($_) }) -join '|') + ')\\'
# 同一排除集的 git pathspec 形态（供 git grep 面使用；与上面的正则始终保持同源）。
$script:GateSourceExcludeGitPathSpec = @(
    ':(exclude,glob)**/obj/**',
    ':(exclude,glob)**/bin/**',
    ':(exclude,glob)**/BuildOutput/**',
    ':(exclude,glob)**/_deprecated/**',
    ':(exclude,glob)**/TestResults/**',
    ':(exclude,glob)**/coverage/**',
    ':(exclude,glob)**/.VSCodeCounter/**',
    ':(exclude,glob)**/node_modules/**',
    ':(exclude,glob)**/backups/**',
    ':(exclude)Runtime/Payload',
    ':(exclude)Runtime.staging'
)

# 未识别 kind（A7 单源）：与 C# Agent.Contract.Api.RecognizedKinds.Unrecognized 同值；
# 生成物事实可由 LedgerForge contract-facts 复核（constantsStale=false 时等价）。
$script:GateUnrecognizedKind = 'unrecognized'

<#
.SYNOPSIS
    源码扫描排除集（D5 单源，绝对路径正则），供 Get-GateFiles -Exclude / Where-Object -notmatch 使用。
#>
function Get-GateSourceExcludePattern {
    return $script:GateSourceExcludePattern
}

<#
.SYNOPSIS
    源码扫描排除集（git pathspec 形态），供 git grep 面（Get-GateSearchFileCount）使用。
.DESCRIPTION
    与 Get-GateSourceExcludePattern 同一份排除清单（段列表 + 镜像列表），只是形态不同：
    正则用于文件系统枚举，pathspec 用于 `git grep -- <pathspec...>`。
#>
function Get-GateSourceExcludeGitPathSpec {
    return $script:GateSourceExcludeGitPathSpec
}

<#
.SYNOPSIS
    未识别 kind 常量（A7 单源），替代各门散落的 'unrecognized' 字面量。
#>
function Get-GateUnrecognizedKind {
    return $script:GateUnrecognizedKind
}

<# 契约事实（A7）：GateFacts 的 contract-facts 包装已随死码删除（无调用点）。 #>


# 兼容别名：域侧既有调用名（18 份 Add-Violation / 4 份 Get-Prop）无需改名即可接管。
New-Alias -Name Add-Violation -Value Add-GateViolation
New-Alias -Name Get-Prop -Value Get-GateJsonProperty

# 显式导出面（自有 API + 门禁面向的目录映射 API）：
#   固定契约，禁依赖「默认全导出」的隐式面（防随导入件漂移）。
Export-ModuleMember -Function @(
    # 自有（门禁运行时）
    'Get-GateRepoRoot',
    'Find-GateScript',
    'Find-GateSpec',
    'Find-GateTarget',
    'New-GateResult',
    'Write-GateHeader',
    'Write-GateVerdict',
    'Test-BaselineNoIncrease',
    'New-GateContext',
    'Get-GateExitCodePolicy',
    'Import-GateRuntime',
    'Initialize-GateModule',
    'Invoke-GateCountChecks',
    'Invoke-GateRuleSet',
    'Test-GatePrerequisite',
    'Assert-GatePrerequisite',
    'Add-GateViolation',
    'Get-GateJsonProperty',
    'Get-GateSearchFileCount',
    'Assert-GateControls',
    'Get-GateRegionKeyPattern',
    'Get-GateSourceExcludePattern',
    'Get-GateSourceExcludeGitPathSpec',
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

