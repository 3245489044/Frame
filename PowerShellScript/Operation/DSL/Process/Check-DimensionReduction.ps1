#requires -Version 5.1
<#
.SYNOPSIS
    降维门禁（DimensionReduction）—— 把「声明式/工作流即降维」从散文变成机器判据。

.DESCRIPTION
    判据载体 = 源码静态计数（git 索引枚举；非执行者叙述），权威 = clinerules/axioms.md AX.DimensionalityReduction：
      C0 登记在位：GateManifests.psd1 含本门登记块，且 Carrier/Note 无叙述性判据词（ADR-4）
      C1 形态转换点数 |F| ≤ 基线（棘轮只降不升）
      C2 形态走私点数 |S| ≤ 基线（白名单外的 Parse/ToHtml 回流，ADR-2）
      C3 写原语数     |W| ≤ 基线（ADR-3 双路径并存）
      C4 账本写者数   |L| ≤ 基线（ADR-5 多账本）
      C5 取证下限：黄金/取证文件数 ≥ 基线（降维不得删信息，ADR-6）
      C6 声明面完备：含 rules 的声明面须声明集合级 default 且条目无步骤/顺序键（ADR-1/ADR-7）；无 rules ⇒ 显式 N/A
      C7 正对照：|F|/|W|/|L| 各 ≥1 命中（符号清单被掏空 ⇒ 判「判据无标的」，禁假绿）
      C8 负对照(-NegativeControl)：ADR-1..ADR-6 合成样本逐项须判红
      C9 目标差距(-Target)：与 Target* 比对并列出真实坐标（默认不启用，仅作验收取证；-Check -Target 时判红）

    模式：默认报告模式；-Check 违规 → 退出码 1；-WriteBaseline 写操作（聚合器禁用）。
    依赖方向（硬约束）：本件只读仓库；-WriteBaseline 例外（仅写 Baselines\Process\DimensionReduction.psd1）。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl,
    [switch]$Target,
    [switch]$WriteBaseline,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateBaseline.psm1') -Force
Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateFileIndex.psm1') -Force

$baselineRel = 'Frame\PowerShellScript\Baselines\Process\DimensionReduction.psd1'
$baselinePath = Join-Path $Root $baselineRel

# ---- 坐标与符号清单（结构事实：扫哪些目录、数哪些符号）----
$scanRoots = @(
    'Frame\CsharpPrj\Plate\HtmlViewer',
    'Frame\CsharpPrj\Operation\FrontEndDriven\HtmlViewerBridge'
)
$scanExtensions = @('cs', 'js')
$evidenceRel = 'Environment\CentralTest\CSharpTest\Plate.Tests\HtmlViewer'
$declarationSurfaces = @('Projects\Information\DrivenConfig\JsonConfig\AgentRuntimeUI.json')
$registrationRel = 'Frame\PowerShellScript\Operation\DSL\Registry\GateManifests.psd1'

# 形态转换点：每一处「一种表示变成另一种表示」的实现符号
$conversionPatterns = @(
    'HtmlTemplateEngine\.Render\s*\(',
    'HtmlNodeParser\.Parse\s*\(',
    'HtmlTreeWriter\.SerializeTree\s*\(',
    'UiNodeHtml\.ToHtml\s*\(',
    'applyTree\s*\('
)
# 写原语：同一写面的互斥写法（ADR-3 双路径）
$writePatterns = @('SectionWriteMode\.BulkReplace', 'SectionWriteMode\.Patch')
# 账本：同一事实的去重/版本写者（ADR-5 多账本）
$ledgerPatterns = @('WriteLedger', 'IWriteArbiter')
# 形态走私白名单：只有这些件允许持有 string↔node 转换（唯一转换点）
$smugglingWhitelist = @('NodeRenderer.cs', 'HtmlNodeParser.cs')
function Read-TextFile {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return '' }
    return (Get-Content -LiteralPath $Path -Raw)
}

function Get-IndexedFiles {
    param([string]$RelRoot, [string[]]$Extension)
    $dir = Join-Path $Root $RelRoot
    if (-not (Test-Path -LiteralPath $dir)) { return @() }
    try {
        return @(Get-GateFiles -Root $dir -Extension $Extension -Exclude '\\(obj|bin|BuildOutput)\\')
    } catch {
        return @()
    }
}

function Get-ScopeText {
    param([string[]]$RelRoots, [string[]]$Extension)
    $sb = [System.Text.StringBuilder]::new()
    $files = [System.Collections.Generic.List[string]]::new()
    foreach ($rel in @($RelRoots)) {
        foreach ($f in (Get-IndexedFiles -RelRoot $rel -Extension $Extension)) {
            $files.Add($f)
            [void]$sb.AppendLine((Read-TextFile -Path $f))
        }
    }
    return [pscustomobject]@{ Text = $sb.ToString(); Files = $files.ToArray() }
}

function Measure-SymbolPresence {
    param([string]$Text, [string[]]$Patterns)
    $present = [System.Collections.Generic.List[string]]::new()
    if ([string]::IsNullOrEmpty($Text)) { return $present.ToArray() }
    foreach ($p in @($Patterns)) {
        if ([string]::IsNullOrWhiteSpace($p)) { continue }
        if ($Text -match $p) { $present.Add($p) }
    }
    return $present.ToArray()
}

function Measure-SmugglingTextSites {
    param([string]$Text)
    $n = 0
    if ([string]::IsNullOrEmpty($Text)) { return 0 }
    if ($Text -match 'HtmlNodeParser\.Parse\s*\(') { $n++ }
    if ($Text -match '\.ToHtml\s*\(') { $n++ }
    return $n
}

function Measure-SmugglingSites {
    param([string[]]$RelRoots, [string[]]$Extension, [string[]]$Whitelist)
    $sites = [System.Collections.Generic.List[string]]::new()
    foreach ($rel in @($RelRoots)) {
        foreach ($f in (Get-IndexedFiles -RelRoot $rel -Extension $Extension)) {
            $name = [IO.Path]::GetFileName($f)
            if (@($Whitelist) -contains $name) { continue }
            if ((Measure-SmugglingTextSites -Text (Read-TextFile -Path $f)) -gt 0) { $sites.Add($f) }
        }
    }
    return $sites.ToArray()
}

function Test-EvidenceFloor {
    param([int]$Actual, [int]$Floor)
    return ($Actual -ge $Floor)
}

function Test-DeclarativeSurface {
    param([string]$Text, [string]$Label = '')
    $v = [System.Collections.Generic.List[string]]::new()
    if ([string]::IsNullOrWhiteSpace($Text)) {
        $v.Add("[C6] 声明面缺失或为空：$Label")
        return $v.ToArray()
    }
    if ($Text -notmatch '"rules"\s*:\s*\[') { return $v.ToArray() }   # 无 rules ⇒ 显式 N/A（P4 前）
    if ($Text -match '"(order|steps?|sequence|then|next)"\s*:') {
        $v.Add("[C6][ADR-1] $Label 的 rules 含步骤/顺序语义键")
    }
    if ($Text -notmatch '"default"\s*:') {
        $v.Add("[C6][ADR-7] $Label 含 rules 但未声明集合级 default")
    }
    return $v.ToArray()
}

function Test-NarrativeCarrier {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $false }
    return ($Text -match '影响面太大|很安全|顺便|顺手|收益大于|风险可控|先不改')
}

function Test-RegistrationCarrier {
    param([string]$Text)
    $v = [System.Collections.Generic.List[string]]::new()
    $m = [regex]::Match($Text, '(?ms)^\s*DimensionReduction\s*=\s*@\{(?<body>.*?)^\s*\}')
    if (-not $m.Success) {
        $v.Add('[C0] DimensionReduction 未登记于 GateManifests.psd1')
        return $v.ToArray()
    }
    $body = $m.Groups['body'].Value
    $carrier = ([regex]::Match($body, "Carrier\s*=\s*'(?<c>[^']*)'")).Groups['c'].Value
    $note = ([regex]::Match($body, "Note\s*=\s*'(?<c>[^']*)'")).Groups['c'].Value
    if (Test-NarrativeCarrier -Text ("$carrier|$note")) {
        $v.Add('[C0][ADR-4] 登记项 Carrier/Note 含叙述性判据词（动机/收益/风险/影响面）')
    }
    return $v.ToArray()
}
# ---- C8 负对照（判据自证；合成样本必须逐项判红）----
if ($NegativeControl) {
    $fail = [System.Collections.Generic.List[string]]::new()
    if (@(Test-DeclarativeSurface -Text '{"rules":[{"id":"a","steps":["x"]}]}' -Label 'probe').Count -eq 0) { $fail.Add('ADR-1 伪声明式未命中') }
    if ((Measure-SmugglingTextSites -Text 'var n = HtmlNodeParser.Parse(html); var h = node.ToHtml();') -lt 1) { $fail.Add('ADR-2 形态走私未命中') }
    if ((Measure-SymbolPresence -Text 'SectionWriteMode.Patch; SectionWriteMode.BulkReplace' -Patterns $writePatterns).Count -lt 2) { $fail.Add('ADR-3 双路径未命中') }
    if (-not (Test-NarrativeCarrier -Text '影响面太大，先不改')) { $fail.Add('ADR-4 叙述代判据未命中') }
    if ((Measure-SymbolPresence -Text 'WriteLedger a; IWriteArbiter b' -Patterns $ledgerPatterns).Count -lt 2) { $fail.Add('ADR-5 多账本未命中') }
    if (Test-EvidenceFloor -Actual 1 -Floor 5) { $fail.Add('ADR-6 降维成删信息未命中') }
    if ($fail.Count -gt 0) {
        Write-Output ('FAIL DimensionReduction: ' + ($fail -join ' / '))
        exit 1
    }
    Write-Output 'PASS DimensionReduction (negative control: ADR-1..ADR-6 全部判红)'
    exit 0
}

# ---- 测量（单次 git 索引；禁递归枚举）----
$scope = Get-ScopeText -RelRoots $scanRoots -Extension $scanExtensions
$conversions = @(Measure-SymbolPresence -Text $scope.Text -Patterns $conversionPatterns)
$writes = @(Measure-SymbolPresence -Text $scope.Text -Patterns $writePatterns)
$ledgers = @(Measure-SymbolPresence -Text $scope.Text -Patterns $ledgerPatterns)
$smuggling = @(Measure-SmugglingSites -RelRoots $scanRoots -Extension $scanExtensions -Whitelist $smugglingWhitelist)
$evidence = @(Get-IndexedFiles -RelRoot $evidenceRel -Extension @('cs'))

$measured = [pscustomobject]@{
    FormConversions    = $conversions.Count
    FormSmugglingSites = $smuggling.Count
    WritePrimitives    = $writes.Count
    LedgerWriters      = $ledgers.Count
    Evidence           = $evidence.Count
}

# ---- -WriteBaseline（写操作，聚合器禁用）----
if ($WriteBaseline) {
    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine('@{')
    [void]$sb.AppendLine("    Schema = 'gate-baseline-v1'")
    [void]$sb.AppendLine("    Gate   = 'DimensionReduction'")
    [void]$sb.AppendLine("    # 计数事实 = 源码静态计数（git 索引）；Debt = 目标未达，DueWhen 给出销账条件。")
    [void]$sb.AppendLine("    FormConversions    = @{ Value = $($measured.FormConversions);    Origin = 'Debt'; DueWhen = '平面化 P3 完成时收敛到 TargetFormConversions' }")
    [void]$sb.AppendLine("    FormSmugglingSites = @{ Value = $($measured.FormSmugglingSites); Origin = 'Debt'; DueWhen = '平面化 P2 完成时收敛到 TargetFormSmugglingSites' }")
    [void]$sb.AppendLine("    WritePrimitives    = @{ Value = $($measured.WritePrimitives);    Origin = 'Debt'; DueWhen = '平面化 P3 完成时收敛到 TargetWritePrimitives' }")
    [void]$sb.AppendLine("    LedgerWriters      = @{ Value = $($measured.LedgerWriters);      Origin = 'Debt'; DueWhen = '平面化 P3 完成时收敛到 TargetLedgerWriters' }")
    [void]$sb.AppendLine("    EvidenceFloor      = @{ Value = $($measured.Evidence);           Origin = 'Derived'; DueWhen = '' }")
    [void]$sb.AppendLine("    TargetFormConversions    = @{ Value = 2; Origin = 'Derived'; DueWhen = '' }")
    [void]$sb.AppendLine("    TargetFormSmugglingSites = @{ Value = 0; Origin = 'Derived'; DueWhen = '' }")
    [void]$sb.AppendLine("    TargetWritePrimitives    = @{ Value = 1; Origin = 'Derived'; DueWhen = '' }")
    [void]$sb.AppendLine("    TargetLedgerWriters      = @{ Value = 1; Origin = 'Derived'; DueWhen = '' }")
    [void]$sb.AppendLine('}')
    $dir = Split-Path $baselinePath -Parent
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    [System.IO.File]::WriteAllText($baselinePath, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "基线已写入: $baselinePath" -ForegroundColor Cyan
    Write-Host ("实测: |F|={0} |S|={1} |W|={2} |L|={3} |E|={4}" -f $measured.FormConversions, $measured.FormSmugglingSites, $measured.WritePrimitives, $measured.LedgerWriters, $measured.Evidence)
    exit 0
}
$g = Import-GateBaseline -Name DimensionReduction
$violations = [System.Collections.Generic.List[string]]::new()

# ---- C1/C2/C3/C4 棘轮（只降不升）----
$ratchet = @(
    @{ Name = 'FormConversions';    Actual = $measured.FormConversions;    Limit = [int]$g.FormConversions.Value },
    @{ Name = 'FormSmugglingSites'; Actual = $measured.FormSmugglingSites; Limit = [int]$g.FormSmugglingSites.Value },
    @{ Name = 'WritePrimitives';    Actual = $measured.WritePrimitives;    Limit = [int]$g.WritePrimitives.Value },
    @{ Name = 'LedgerWriters';      Actual = $measured.LedgerWriters;      Limit = [int]$g.LedgerWriters.Value }
)
foreach ($r in $ratchet) {
    if ($r.Actual -gt $r.Limit) {
        $violations.Add("[C1] $($r.Name) 实测 $($r.Actual) > 基线 $($r.Limit)（棘轮只降不升）")
    }
}

# ---- C5 取证下限（ADR-6）----
if (-not (Test-EvidenceFloor -Actual $measured.Evidence -Floor ([int]$g.EvidenceFloor.Value))) {
    $violations.Add("[C5][ADR-6] 取证文件数 $($measured.Evidence) < 下限 $([int]$g.EvidenceFloor.Value)（降维不得删信息）")
}

# ---- C7 正对照（符号清单不得被掏空）----
if ($measured.FormConversions -lt 1 -or $measured.WritePrimitives -lt 1 -or $measured.LedgerWriters -lt 1) {
    $violations.Add("[C7] 判据无标的：|F|=$($measured.FormConversions) |W|=$($measured.WritePrimitives) |L|=$($measured.LedgerWriters)")
}

# ---- C6 声明面完备（ADR-1/ADR-7）----
foreach ($rel in $declarationSurfaces) {
    foreach ($v in @(Test-DeclarativeSurface -Text (Read-TextFile -Path (Join-Path $Root $rel)) -Label $rel)) {
        $violations.Add($v)
    }
}

# ---- C0 登记载体（ADR-4 自审：判据载体禁叙述词）----
foreach ($v in @(Test-RegistrationCarrier -Text (Read-TextFile -Path (Join-Path $Root $registrationRel)))) {
    $violations.Add($v)
}

# ---- C9 目标差距（-Target：真实违例清单，供验收取证）----
$gaps = @(
    @{ Name = 'FormConversions';    Actual = $measured.FormConversions;    Target = [int]$g.TargetFormConversions.Value },
    @{ Name = 'FormSmugglingSites'; Actual = $measured.FormSmugglingSites; Target = [int]$g.TargetFormSmugglingSites.Value },
    @{ Name = 'WritePrimitives';    Actual = $measured.WritePrimitives;    Target = [int]$g.TargetWritePrimitives.Value },
    @{ Name = 'LedgerWriters';      Actual = $measured.LedgerWriters;      Target = [int]$g.TargetLedgerWriters.Value }
)
$gapLines = [System.Collections.Generic.List[string]]::new()
foreach ($x in $gaps) {
    if ($x.Actual -gt $x.Target) {
        $gapLines.Add("[目标差距] $($x.Name) 实测 $($x.Actual) > 目标 $($x.Target)")
    }
}
if ($Target) { foreach ($line in $gapLines) { $violations.Add("[C9] $line") } }

# ---- 报告 ----
Write-Host '=== 降维门禁（DimensionReduction） ==='
Write-Host '检查项: C0 登记载体  C1-C4 计数棘轮  C5 取证下限  C6 声明面完备  C7 正对照  C9 目标差距(-Target)'
Write-Host ("实测: |F|={0} |S|={1} |W|={2} |L|={3} |E|={4}" -f $measured.FormConversions, $measured.FormSmugglingSites, $measured.WritePrimitives, $measured.LedgerWriters, $measured.Evidence)
if ($gapLines.Count -gt 0) {
    Write-Host '--- 目标差距（Debt，未达 Target*；非 -Target 不阻断）---' -ForegroundColor Yellow
    foreach ($line in $gapLines) { Write-Host "  $line" }
}
if ($ShowDetail) {
    Write-Host ("扫描: {0} 文件 | 索引来源 = {1}" -f @($scope.Files).Count, (Get-GateFileStats).Source)
    if ($smuggling.Count -gt 0) {
        Write-Host '形态走私坐标:'
        foreach ($s in $smuggling) { Write-Host "  $s" }
    }
}

if ($violations.Count -gt 0) {
    Write-Host '--- 违规清单 ---' -ForegroundColor Yellow
    foreach ($v in $violations) { Write-Host "  [FAIL] $v" -ForegroundColor Yellow }
    if ($Check) { Write-Host "校验失败：降维门禁发现 $($violations.Count) 项违规"; exit 1 }
    exit 0
}

Write-Host '  [OK] 降维门禁通过（计数未超基线；目标差距见上）'
exit 0



