#requires -Version 5.1
<#
.SYNOPSIS
    契约注册表对账（P4 推广：声明契约 ⇔ 实现/证据）——G0 元契约 / G1 悬空 / G2 漂移 / G3 未声明 / G4 可见化 + 正负对照。

.DESCRIPTION
    声明面权威 = Frame\CsharpPrj\contracts.index.json（列出各子系统自带 contracts.json）+ 各 contracts.json 条目。
    判据（载体 = 声明结构事实；禁执行者叙述 —— 权威: axioms SEP.JudgmentCarrierStructural）：
      G0 元契约：wellformed(C) 不成立（缺 carrier/evidence/tier/effect、载体含语义词、受控词越界）→ 退出码 1。
      G1 悬空  ：host.impl 声明的类型/文件/符号不在位 → 退出码 1。
      G2 漂移  ：实现处不含 id 末段符号（\b 匹配，同 Check-GateRegistry G2）→ 退出码 1。
      G3 未声明：扫描面（scanGlobs）内存在 IContractHost 实现但无对应 contracts.json 条目（"黑户校验"）→ 退出码 1。
      G4 可见化：tier=Runtime ∧ host.kind=Probe 但 impl 落点不在位 → 仅报告（不失败）。
      对照     ：合成缺-carrier 负样本必命中 ∧ 合法正样本必放行（防判据恒真/恒假）。
    与 C# ContractCatalog 同读一份 contracts.json（无第二份声明）。

    模式：默认报告（不设退出码）；-Check 时 G0/G1/G2/G3 违规或对照失效 → 退出码 1。只读仓库，零侧写。

.PARAMETER Root
    仓库根目录（缺省由本脚本位置上溯四级推导：Frame\PowerShellScript\Operation\DSL → 仓库根）。

.PARAMETER Check
    校验模式：存在违规（G0/G1/G2/G3）或对照失效 → 退出码 1。

.EXAMPLE
    .\Check-ContractRegistry.ps1

.EXAMPLE
    .\Check-ContractRegistry.ps1 -Check
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

# 根定位（深度无关）：GateRuntime → DirectoryMap 以「Frame\ + Projects\」marker 上溯（迁 Environment 后不依赖脚本深度）
$__repoRoot = $PSScriptRoot
while ($__repoRoot -and -not ((Test-Path -LiteralPath (Join-Path $__repoRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $__repoRoot 'Projects'))) { $__repoRoot = Split-Path -Parent $__repoRoot }
if (-not $__repoRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $__repoRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
$ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Key 'ContractRegistry'
$Root = $ctx.Root
$violations = $ctx.Violations

$indexRel = 'CsharpPrj\contracts.index.json'
$indexPath = Join-Path (Get-MappedPath -Key 'Frame') $indexRel

# ---- 受控词（与 C# 枚举一致；禁散落字面量）----
$tierSet = @('Build', 'Assembly', 'Gate', 'Runtime')
$effectSet = @('FailFast', 'Block', 'Heal', 'Log')
$carrierKindSet = @('StructuralFact')
$hostKindSet = @('DotNet', 'Roslyn', 'PowerGate', 'Probe')
$narrativeMarkers = @(
    '动机', '必要性', '收益', '副作用', '风险', '顺手', '已修复', '应该没问题',
    'motive', 'rationale', 'benefit', 'side effect', 'side-effect', 'risk',
    'should be fine', 'should be ok', 'already fixed', 'i fixed', 'it works'
)

# ---- 载体铁律：陈述性语义词检出 ----
function Test-Narrative {
    param([string]$Text)
    if ([string]::IsNullOrEmpty($Text)) { return $false }
    foreach ($marker in $narrativeMarkers) {
        if ($Text.IndexOf($marker, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) { return $true }
    }
    return $false
}

# ---- G0 元契约：返回不合格原因清单（空 = 合格）----
function Get-ContractWellFormednessViolations {
    param([object]$Contract)

    $violations = New-Object System.Collections.Generic.List[string]
    if ($null -eq $Contract) { [void]$violations.Add('契约为空'); return , $violations }

    if ([string]::IsNullOrWhiteSpace([string]$Contract.id)) { [void]$violations.Add('缺 id') }
    if ([string]::IsNullOrWhiteSpace([string]$Contract.semantics)) { [void]$violations.Add('缺 semantics') }

    $carrier = $Contract.carrier
    if ($null -eq $carrier) {
        [void]$violations.Add('缺 carrier')
    }
    else {
        $kind = [string]$carrier.kind
        if ($carrierKindSet -notcontains $kind) { [void]$violations.Add("carrier.kind 非受控词: '$kind'") }
        $facts = @($carrier.facts)
        if ($facts.Count -eq 0) {
            [void]$violations.Add('carrier.facts 为空')
        }
        else {
            foreach ($fact in $facts) {
                if ([string]::IsNullOrWhiteSpace([string]$fact)) { [void]$violations.Add('carrier.facts 含空白项'); break }
            }
            foreach ($fact in $facts) {
                if (Test-Narrative -Text ([string]$fact)) {
                    [void]$violations.Add("carrier.facts 含语义词（载体铁律）: '$fact'"); break
                }
            }
        }
    }

    $tier = [string]$Contract.tier
    if ($tierSet -notcontains $tier) { [void]$violations.Add("tier 非受控词: '$tier'") }
    $effect = [string]$Contract.effect
    if ($effectSet -notcontains $effect) { [void]$violations.Add("effect 非受控词: '$effect'") }

    $evidence = $Contract.evidence
    if ($null -eq $evidence) {
        [void]$violations.Add('缺 evidence')
    }
    else {
        if ([string]::IsNullOrWhiteSpace([string]$evidence.recomputeCmd)) { [void]$violations.Add('缺 evidence.recomputeCmd（可复算命令）') }
        if ([string]::IsNullOrWhiteSpace([string]$evidence.positive)) { [void]$violations.Add('缺 evidence.positive（正对照样本）') }
        if ([string]::IsNullOrWhiteSpace([string]$evidence.negative)) { [void]$violations.Add('缺 evidence.negative（负对照样本）') }
    }

    return , $violations
}

# ---- G1/G2 载体：host.impl 定位（仓库相对文件路径 或 符号检索）----
function Resolve-HostImpl {
    param([string]$Impl)

    if ([string]::IsNullOrWhiteSpace($Impl)) { return $null }
    $normalized = ($Impl -replace '/', '\').Trim()
    $candidate = Join-Path $Root $normalized
    if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $normalized }

    if ($normalized -match '\.(cs|ps1|psd1|json)$') { return $null }  # 形如路径但不在位

    # 视为符号：取末段，在受控范围内检索（仅对已声明契约生效）
    $name = $Impl
    if ($Impl.Contains('.')) { $name = $Impl.Substring($Impl.LastIndexOf('.') + 1) }
    foreach ($scope in @((Join-Path $Root 'Frame'), (Join-Path $Root 'Projects'))) {
        if (-not (Test-Path -LiteralPath $scope)) { continue }
        $hit = Get-ChildItem -LiteralPath $scope -Recurse -File -Include *.cs, *.ps1 -ErrorAction SilentlyContinue |
            Select-String -Pattern ("\b" + [regex]::Escape($name) + "\b") -List -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($hit) {
            $rel = [string]$hit.Path
            if ($rel.StartsWith($Root, [System.StringComparison]::OrdinalIgnoreCase)) { $rel = $rel.Substring($Root.Length) }
            return $rel.TrimStart('\')
        }
    }
    return $null
}

# ---- 加载声明面 ----
$failures = New-Object System.Collections.Generic.List[string]
$notices = New-Object System.Collections.Generic.List[string]
$contracts = New-Object System.Collections.Generic.List[object]
$declaredHosts = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
$scanGlobs = @()

if (-not (Test-Path -LiteralPath $indexPath)) {
    [void]$failures.Add("G0 索引缺件：$indexRel（契约声明索引不存在）")
}
else {
    $index = $null
    try { $index = Get-Content -LiteralPath $indexPath -Raw -Encoding UTF8 | ConvertFrom-Json }
    catch { [void]$failures.Add("G0 索引解析失败：$indexRel → $($_.Exception.Message)") }

    if ($null -ne $index) {
        $scanGlobs = @($index.scanGlobs)
        foreach ($rel in @($index.files)) {
            if ([string]::IsNullOrWhiteSpace([string]$rel)) { continue }
            $fileRel = ([string]$rel) -replace '/', '\'
            $filePath = Join-Path $Root $fileRel
            if (-not (Test-Path -LiteralPath $filePath)) { [void]$failures.Add("G0 声明缺件：$fileRel"); continue }

            $doc = $null
            try { $doc = Get-Content -LiteralPath $filePath -Raw -Encoding UTF8 | ConvertFrom-Json }
            catch { [void]$failures.Add("G0 声明解析失败：$fileRel → $($_.Exception.Message)"); continue }

            foreach ($entry in @($doc.contracts)) {
                if ($null -eq $entry) { continue }
                $contracts.Add([PSCustomObject]@{ Rel = $fileRel; Contract = $entry })
            }
        }
    }
}

# ---- G0 元契约（逐声明）----
foreach ($wrap in $contracts) {
    $id = [string]$wrap.Contract.id
    foreach ($v in (Get-ContractWellFormednessViolations -Contract $wrap.Contract)) {
        [void]$failures.Add("G0 $id（$($wrap.Rel)）：$v")
    }

    $impl = [string]$wrap.Contract.host.impl
    if (-not [string]::IsNullOrWhiteSpace($impl)) {
        [void]$declaredHosts.Add($impl)
        [void]$declaredHosts.Add($(if ($impl.Contains('.')) { $impl.Substring($impl.LastIndexOf('.') + 1) } else { $impl }))
    }
}

# ---- G1 悬空 / G2 漂移 ----
foreach ($wrap in $contracts) {
    $id = [string]$wrap.Contract.id
    $impl = [string]$wrap.Contract.host.impl
    if ([string]::IsNullOrWhiteSpace($impl)) {
        [void]$failures.Add("G1 $id：host.impl 缺省（声明项须给实现落点）")
        continue
    }

    $resolved = Resolve-HostImpl -Impl $impl
    if ($null -eq $resolved) {
        [void]$failures.Add("G1 $id：悬空实现（host.impl='$impl' 文件/符号不在位）")
        continue
    }

    $resolvedFull = Join-Path $Root $resolved
    if (Test-Path -LiteralPath $resolvedFull -PathType Leaf) {
        $segment = $id
        $slash = $id.LastIndexOf('/')
        if ($slash -ge 0) { $segment = $id.Substring($slash + 1) }
        $at = $segment.IndexOf('@')
        if ($at -gt 0) { $segment = $segment.Substring(0, $at) }
        if (-not [string]::IsNullOrWhiteSpace($segment)) {
            $text = Get-Content -LiteralPath $resolvedFull -Raw -Encoding UTF8
            if ($text -notmatch ("\b" + [regex]::Escape($segment) + "\b")) {
                [void]$failures.Add("G2 $id：实现漂移（$resolved 内不含 id 末段符号 '$segment'）")
            }
        }
    }
}

# ---- G3 未声明（"黑户校验"；扫描面窄化，未配置则跳过并可见化）----
$activeGlobs = @($scanGlobs | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
if ($activeGlobs.Count -eq 0) {
    [void]$notices.Add('G3 跳过：扫描面（scanGlobs）未配置（纯框架态，无 Consumer 声明面）')
}
else {
    foreach ($glob in $activeGlobs) {
        $abs = Join-Path $Root ([string]$glob)
        $found = @()
        try { $found = @(Get-ChildItem -Path $abs -Recurse -File -ErrorAction SilentlyContinue) } catch { $found = @() }
        foreach ($file in $found) {
            $text = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
            foreach ($m in [regex]::Matches($text, '([A-Za-z_][A-Za-z0-9_]*)\s*:\s*(?:[A-Za-z0-9_.]*)\bIContractHost\b')) {
                $symbol = $m.Groups[1].Value
                if (-not $declaredHosts.Contains($symbol)) {
                    $rel = $file.FullName
                    if ($rel.StartsWith($Root, [System.StringComparison]::OrdinalIgnoreCase)) { $rel = $rel.Substring($Root.Length) }
                    [void]$failures.Add("G3 $(($rel).TrimStart('\'))：IContractHost 实现 '$symbol' 未在 contracts.json 声明（黑户校验）")
                }
            }
        }
    }
}

# ---- G4 可见化（tier=Runtime ∧ host.kind=Probe 但落点不在位）----
foreach ($wrap in $contracts) {
    $c = $wrap.Contract
    if (([string]$c.tier) -eq 'Runtime' -and ([string]$c.host.kind) -eq 'Probe') {
        $impl = [string]$c.host.impl
        if ([string]::IsNullOrWhiteSpace($impl) -or $null -eq (Resolve-HostImpl -Impl $impl)) {
            [void]$notices.Add("G4 $([string]$c.id)：tier=Runtime∧host.kind=Probe 但探针落点不在位（可见化，不失败）")
        }
    }
}

# ---- 对照（正/负样本防假绿：判据不得恒真/恒假）----
$negativeMissingCarrier = [PSCustomObject]@{
    id = 'CTL-0/MissingCarrier'; semantics = 's'; carrier = $null
    tier = 'Assembly'; effect = 'FailFast'
    evidence = [PSCustomObject]@{ recomputeCmd = 'c'; positive = 'p'; negative = 'n' }
}
if ((Get-ContractWellFormednessViolations -Contract $negativeMissingCarrier).Count -eq 0) {
    [void]$failures.Add('对照：负对照失效 —— 缺 carrier 未被判不合格（判据恒真？）')
}

$negativeNarrativeCarrier = [PSCustomObject]@{
    id = 'CTL-0/NarrativeCarrier'; semantics = 's'
    carrier = [PSCustomObject]@{ kind = 'StructuralFact'; facts = @('动机：因为顺手') }
    tier = 'Assembly'; effect = 'FailFast'
    evidence = [PSCustomObject]@{ recomputeCmd = 'c'; positive = 'p'; negative = 'n' }
}
if ((Get-ContractWellFormednessViolations -Contract $negativeNarrativeCarrier).Count -eq 0) {
    [void]$failures.Add('对照：负对照失效 —— 语义载体（含语义词）未被判不合格')
}

$positiveValid = [PSCustomObject]@{
    id = 'CTL-0/Valid'; semantics = '不变量：已注册符号计数为 1'
    carrier = [PSCustomObject]@{ kind = 'StructuralFact'; facts = @('已注册符号集') }
    tier = 'Gate'; effect = 'Block'
    host = [PSCustomObject]@{ kind = 'PowerGate'; impl = 'Check-ContractRegistry.ps1' }
    evidence = [PSCustomObject]@{ recomputeCmd = 'c'; positive = 'p'; negative = 'n' }
}
if ((Get-ContractWellFormednessViolations -Contract $positiveValid).Count -ne 0) {
    [void]$failures.Add('对照：正对照失效 —— 合法契约被判不合格（判据恒假？）')
}

# ---- 汇总输出 ----
Write-Host '=== [ContractRegistry] 契约注册表对账（声明契约 ⇔ 实现/证据） ==='
Write-Host ("声明索引={0} | 契约条目={1} | G3 扫描面={2}" -f $indexRel, $contracts.Count, $activeGlobs.Count)

if ($notices.Count -gt 0) {
    Write-Host '--- 可见化（G3/G4，非失败） ---'
    foreach ($n in $notices) { Write-Host ("  " + $n) }
}

if ($failures.Count -gt 0) {
    Write-Host '--- 违规清单（G0/G1/G2/G3、对照） ---'
    foreach ($f in $failures) { Write-Host ("  [违规] " + $f) }
    Write-Host '修复口径：补齐 contracts.json 五元组 + 可复算证据；载体须为结构事实（禁语义词）；host.impl 须在位且含 id 末段符号。'
}

if ($Check -and $failures.Count -gt 0) {
    Write-Host '校验失败：契约注册表对账未通过（元契约/悬空/漂移/未声明/对照）。'
    exit 1
}

if ($failures.Count -eq 0) {
    Write-Host 'ContractRegistry: OK —— 元契约合格、实现无悬空/漂移、无黑户校验，且正/负对照命中。'
}
exit 0

