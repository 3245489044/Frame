# Check-ConcurrencyOptionsSingleSource.ps1
# 并发策略声明单源守卫（C1–C5）：spec（唯一结构源）⇄ ushar 值面 ⇄ 生成 C# ⇄ Agent schema ⇄ 消费方。
# 用法：pwsh -File Check-ConcurrencyOptionsSingleSource.ps1 [-Check] [-NegativeControl]
#
# 判据载体 = 结构事实（键集/属性集/字面量计数），禁执行者叙述。
# 说明：不递归枚举仓库（模块目录有界，直接取该工程 .cs），故无需 GateFileIndex 债务登记。

[CmdletBinding()]
param(
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
while ($repoRoot -and -not ((Test-Path (Join-Path $repoRoot 'Frame')) -and (Test-Path (Join-Path $repoRoot 'Projects')))) { $repoRoot = Split-Path $repoRoot -Parent }
if (-not $repoRoot) { Write-Host '[FAIL] 无法定位仓库根'; exit 2 }
Import-Module (Join-Path $repoRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
$ledgerForge = Get-GateLedgerForge -RepoRoot $repoRoot
if (-not $ledgerForge) { Write-Host '[FAIL] 无法执行：LedgerForge 未构建（键集比较不可用）'; exit 2 }

$violations = [System.Collections.Generic.List[string]]::new()
function Add-Violation([string]$code, [string]$message) { $violations.Add("[$code] $message") | Out-Null }

$moduleRoot = Join-Path $repoRoot 'Frame\CsharpPrj\Modules\Concurrency'
$specPath = Join-Path $moduleRoot 'Options\concurrency-options.spec.json'
$generatedPath = Join-Path $moduleRoot 'Generated\ConcurrencyOptions.g.cs'
$usharPath = Join-Path $repoRoot 'Projects\Plug\Agent\Config\agent-config.ushar'
$schemaPath = Join-Path $repoRoot 'Projects\Plug\Agent\Config\agent-config.schema.json'
$policyPath = Join-Path $repoRoot 'Projects\Plug\Agent\src\Agent.Core\Configuration\ConcurrencyPolicy.cs'

function Read-Text([string]$Path) { if (Test-Path -LiteralPath $Path) { return (Get-Content -LiteralPath $Path -Raw -Encoding UTF8) } return '' }

function Get-SpecKeySet([string]$SpecText, [string]$Section) {
    $obj = $SpecText | ConvertFrom-Json
    $set = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($p in @($obj.sections.$Section.params)) { [void]$set.Add([string]$p.key) }
    return $set
}

function Get-UsharKeySet([string]$Text, [string]$Section) {
    $set = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $dupes = [System.Collections.Generic.List[string]]::new()
    $rx = [regex]('(?m)^\s*' + [regex]::Escape($Section) + '\.([A-Za-z_][A-Za-z0-9_]*)\s*=')
    foreach ($m in $rx.Matches($Text)) {
        $key = $m.Groups[1].Value
        if (-not $set.Add($key)) { $dupes.Add($key) }
    }
    return [pscustomobject]@{ Keys = $set; Duplicates = $dupes }
}

function Get-GeneratedKeySet([string]$Text, [string]$Section) {
    $set = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $block = [regex]::Match($Text, '(?s)// >>> section ' + [regex]::Escape($Section) + '(?<body>.*?)// <<< section ' + [regex]::Escape($Section))
    if (-not $block.Success) { return $set }
    $rx = [regex]'public\s+[A-Za-z0-9_?<>\[\]]+\s+(?<p>[A-Za-z_][A-Za-z0-9_]*)\s*\{\s*get;\s*init;\s*\}'
    foreach ($m in $rx.Matches($block.Groups['body'].Value)) { [void]$set.Add($m.Groups['p'].Value) }
    return $set
}

function Get-SchemaKeySet([string]$Text, [string]$Section) {
    $set = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $obj = $Text | ConvertFrom-Json
    $node = $obj.properties.AgentConfig.properties.$Section
    if ($null -eq $node) { return $set }
    foreach ($p in $node.properties.PSObject.Properties) { [void]$set.Add([string]$p.Name) }
    return $set
}

# 键集比较统一到 C# LedgerKeyClosure(Ordinal)（A8）；C1–C5 业务判据仍在本门。
function Compare-KeySets([string]$Label, $Expected, $Actual, $ExtraLabel) {
    $diff = Compare-GateKeySet -Reference @($Expected) -Other @($Actual) -ReferenceLabel 'expected' -OtherLabel 'actual' -RepoRoot $repoRoot
    foreach ($k in @($diff.OnlyLeft)) { Add-Violation $Label "缺失键：$k（$ExtraLabel）" }
    foreach ($k in @($diff.OnlyRight)) { Add-Violation $Label "多出键：$k（$ExtraLabel）" }
}

function Measure-Forbidden([string]$Text, [string[]]$Patterns) {
    $hits = [System.Collections.Generic.List[string]]::new()
    foreach ($p in $Patterns) { if ($Text -match $p) { $hits.Add($p) } }
    return $hits
}

$specText = Read-Text $specPath
$generatedText = Read-Text $generatedPath
$usharText = Read-Text $usharPath
$schemaText = Read-Text $schemaPath
$policyText = Read-Text $policyPath

if ([string]::IsNullOrWhiteSpace($specText)) { Add-Violation 'C0' "spec 缺失：$specPath" }
if ([string]::IsNullOrWhiteSpace($generatedText)) { Add-Violation 'C0' "生成物缺失：$generatedPath" }
if ([string]::IsNullOrWhiteSpace($usharText)) { Add-Violation 'C0' "配置根缺失：$usharPath" }
if ([string]::IsNullOrWhiteSpace($schemaText)) { Add-Violation 'C0' "schema 缺失：$schemaPath" }

if ($violations.Count -eq 0) {
    foreach ($section in @('TaskComplexity', 'ConcurrencyBudget')) {
        $specKeys = Get-SpecKeySet $specText $section
        if ($specKeys.Count -eq 0) { Add-Violation 'C1' "$section：spec 无参数行（判据无标的）"; continue }

        # C1：spec 键集 ⇄ ushar 值面（唯一配置根必须承载全部参数，且无重复键）
        $ushar = Get-UsharKeySet $usharText $section
        Compare-KeySets 'C1' $specKeys $ushar.Keys "agent-config.ushar:$section"
        foreach ($d in $ushar.Duplicates) { Add-Violation 'C1' "ushar 重复键：$section.$d" }

        # C2：spec 键集 ⇄ 生成 records ⇄ Agent schema（三面对拍；漂移即红）
        $generated = Get-GeneratedKeySet $generatedText $section
        Compare-KeySets 'C2' $specKeys $generated "Generated\ConcurrencyOptions.g.cs:$section"
        $schema = Get-SchemaKeySet $schemaText $section
        Compare-KeySets 'C2' $specKeys $schema "agent-config.schema.json:$section"
    }
}
# ---- 判据实现（供正/负对照复用） ----
$csFiles = @([System.IO.Directory]::GetFiles($moduleRoot, '*.cs', [System.IO.SearchOption]::AllDirectories) |
    Where-Object { $_ -notmatch (Get-GateSourceExcludePattern) })
$moduleText = (@($csFiles | ForEach-Object { Read-Text $_ }) -join "`n")

$forbiddenPatterns = @(
    '\bWeight(InputTokens|Deliverables|TargetKinds|NodeCount|CrossModule|SubAgents)\b',
    '\.Take\(\s*\d+',
    '\bNormalize\s*\(',
    'Math\.(Clamp|Max|Min)\s*\([^)]*\boptions\.'
)

function Get-ForbiddenHits([string]$Text) { return (Measure-Forbidden $Text $forbiddenPatterns) }
function Get-UnitDeclCount([string]$Text) { return [regex]::Matches($Text, 'public\s+[A-Za-z0-9_?]+\s+UnitTokensPerNode\s*\{').Count }
function Get-CopyCount([string]$Text) { return [regex]::Matches($Text, '\bCopy\(\)').Count }

function Get-MissingConsumers([string]$SpecText, [string]$ConsumerText) {
    $missing = [System.Collections.Generic.List[string]]::new()
    $obj = $SpecText | ConvertFrom-Json
    foreach ($section in @('TaskComplexity', 'ConcurrencyBudget')) {
        foreach ($p in @($obj.sections.$section.params)) {
            foreach ($token in ([string]$p.consumer -split '\|')) {
                $t = $token.Trim()
                if ($t.Length -eq 0) { continue }
                if ($ConsumerText -notmatch ('\b' + [regex]::Escape($t) + '\b')) { $missing.Add("$section.$($p.key) ⇒ $t") }
            }
        }
    }
    return $missing
}

# ---- 负对照（判据自证：合成样本必须逐项判红） ----
if ($NegativeControl) {
    $fail = [System.Collections.Generic.List[string]]::new()
    $probeSpec = @{ sections = @{ TaskComplexity = @{ params = @(@{ key = 'A'; consumer = 'NoSuchConsumer' }) } } } | ConvertTo-Json -Depth 6
    $probeUshar = 'TaskComplexity.A = 1'
    $probeUsharKeys = (Get-UsharKeySet $probeUshar 'TaskComplexity').Keys
    $probeMissing = @(@('A', 'B') | Where-Object { -not $probeUsharKeys.Contains($_) })
    if ($probeMissing.Count -lt 1) { $fail.Add('C1 缺键未被判出') }
    if (@(Get-ForbiddenHits 'public double WeightInputTokens { get; init; }').Count -lt 1) { $fail.Add('C3 具名权重属性未被判出') }
    if (@(Get-ForbiddenHits 'var s = text.Split(seps).Take(8).ToArray();').Count -lt 1) { $fail.Add('C3 字面量成员上限未被判出') }
    if (@(Get-ForbiddenHits 'var o = Math.Max(1, options.UnitTokensPerNode);').Count -lt 1) { $fail.Add('C3 静默钳制未被判出') }
    $twoDecls = "public long UnitTokensPerNode { get; init; }`n        public long UnitTokensPerNode { get; init; }"
    if ((Get-UnitDeclCount $twoDecls) -ne 2) { $fail.Add('C4 双声明未被判出') }
    if ((Get-CopyCount 'public TaskComplexityOptions Copy() => this;') -lt 1) { $fail.Add('C4 冗余 Copy 未被判出') }
    $probeConsumer = Get-MissingConsumers $probeSpec 'class NothingHere { }'
    if (@($probeConsumer).Count -lt 1) { $fail.Add('C5 无消费方键未被判出') }

    if ($fail.Count -gt 0) {
        Write-Host '[FAIL] ConcurrencyOptionsSingleSource 负对照未触发：' -ForegroundColor Yellow
        $fail | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
        exit 1
    }
    Write-Host '[PASS] ConcurrencyOptionsSingleSource 负对照（C1/C3/C4/C5 全部判红）' -ForegroundColor Green
    exit 0
}

# ---- C3 代码回填声明面 ----
foreach ($hit in (Get-ForbiddenHits $moduleText)) {
    Add-Violation 'C3' "代码回填声明面（命中 $hit）：权重/成员上限/饱和点/静默钳制必须来自 spec"
}

# ---- C4 单一声明点 + 无冗余 Copy ----
$unitDecls = Get-UnitDeclCount $moduleText
if ($unitDecls -ne 1) { Add-Violation 'C4' "UnitTokensPerNode 声明点应唯一（实际 $unitDecls 处）" }
$copyDecls = Get-CopyCount $moduleText
if ($copyDecls -ne 0) { Add-Violation 'C4' "不可变 record 不应再有 Copy()（实际 $copyDecls 处）" }

# ---- C5 声明键必须有真实消费方（零消费方键 ⇒ 假参数） ----
if (-not [string]::IsNullOrWhiteSpace($specText)) {
    foreach ($missing in (Get-MissingConsumers $specText ($moduleText + "`n" + $policyText))) {
        Add-Violation 'C5' "声明键无消费方：$missing（请接线或从 spec 删除）"
    }
}

if ($violations.Count -eq 0) {
    Write-Host '[OK] ConcurrencyOptionsSingleSource：C1–C5 通过（spec ⇄ ushar ⇄ 生成物 ⇄ schema ⇄ 消费方）。' -ForegroundColor Green
    exit 0
}

Write-Host '[FAIL] ConcurrencyOptionsSingleSource 违规：' -ForegroundColor Red
$violations | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0

