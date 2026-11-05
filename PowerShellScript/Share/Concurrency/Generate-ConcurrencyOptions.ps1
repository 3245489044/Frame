<#
.SYNOPSIS
    并发策略声明生成器 —— 从 spec（唯一结构源）生成 C# 选项类型 + Agent 配置 schema 片段。

.DESCRIPTION
    真源 = Frame\CsharpPrj\Modules\Foundations\Concurrency\Options\concurrency-options.spec.json。
    产物（纯编译产物，禁止手改；漂移由 Check-GeneratedArtifactsDrift 判红）：
      · -Target cs     → Frame\CsharpPrj\Modules\Foundations\Concurrency\Generated\ConcurrencyOptions.g.cs
      · -Target schema → Projects\Plug\Agent\Config\generated\concurrency-options.schema.generated.json
      · -Target all    → 两份都写（缺省 = 仓库内定稿路径）

    运行期值仍由 agent-config.ushar 承载（唯一配置根）；本器只发「类型 + 默认值 + 值域校验 + 声明表」，
    不发行为（特征选择器实现留手写 ComplexityFeatureSelectors，由用例断言 spec.selector 集 ≡ 实现集）。

.PARAMETER Spec
    spec 路径（仓库相对或绝对；缺省 = 模块内 concurrency-options.spec.json）。

.PARAMETER Target
    cs | schema | all（缺省 all）。

.PARAMETER OutFile
    单份产物输出路径（与 -Target cs|schema 联用；缺省 = 仓库内定稿路径）。
#>
[CmdletBinding()]
param(
    [string]$Spec = 'Frame\CsharpPrj\Modules\Foundations\Concurrency\Options\concurrency-options.spec.json',
    [ValidateSet('cs', 'schema', 'all')][string]$Target = 'all',
    [string]$OutFile = ''
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path

if ($Target -ne 'all' -and [string]::IsNullOrWhiteSpace($OutFile)) {
    Write-Error '-Target cs|schema 须给 -OutFile（生成器只按时写单份产物）'
    exit 2
}

$specAbs = if ([System.IO.Path]::IsPathRooted($Spec)) { $Spec } else { Join-Path $root $Spec }
if (-not (Test-Path -LiteralPath $specAbs)) { Write-Error "spec 缺失：$specAbs"; exit 2 }

$specJson = Get-Content -LiteralPath $specAbs -Raw -Encoding UTF8 | ConvertFrom-Json

# ---------------- 校验（fail-fast；不产半成品） ----------------
$violations = [System.Collections.Generic.List[string]]::new()
$selectorWhitelist = @(
    'text_length', 'deliverable_count', 'target_kind_count',
    'node_count', 'cross_module_count', 'sub_agent_count'
)
$typeWhitelist = @('double', 'long', 'int', 'bool', 'string')

if ([string]$specJson.schemaVersion -ne 'concurrency-options-spec-v1') {
    $violations.Add("schemaVersion 非法：'$($specJson.schemaVersion)'（期望 concurrency-options-spec-v1）")
}

$seenKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($sectionName in @('TaskComplexity', 'ConcurrencyBudget')) {
    $section = $specJson.sections.$sectionName
    if ($null -eq $section) { $violations.Add("缺少节：$sectionName"); continue }
    if (@($section.params).Count -lt 1) { $violations.Add("节 $sectionName 无参数行"); continue }

    foreach ($p in @($section.params)) {
        $key = [string]$p.key
        $type = [string]$p.type
        if ($key -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') { $violations.Add("$sectionName`: 非法标识符 '$key'"); continue }
        if (-not $seenKeys.Add($sectionName + '.' + $key)) { $violations.Add("$sectionName`: 重复键 '$key'") }
        if ($type -notin $typeWhitelist) { $violations.Add("${sectionName}.${key}: 未知类型 '$type'") }
        if ([string]::IsNullOrWhiteSpace([string]$p.consumer)) { $violations.Add("${sectionName}.${key}: consumer 为空（零消费方键不得声明）") }
        if ([string]::IsNullOrWhiteSpace([string]$p.level)) { $violations.Add("${sectionName}.${key}: level 为空") }

        $hasMin = $null -ne $p.PSObject.Properties['min'] -and $null -ne $p.min
        $hasMax = $null -ne $p.PSObject.Properties['max'] -and $null -ne $p.max
        if ($hasMin -and $hasMax -and ([double]$p.min -gt [double]$p.max)) {
            $violations.Add("${sectionName}.${key}: min > max")
        }
        if ($null -ne $p.PSObject.Properties['default'] -and $null -ne $p.default -and $type -in @('double', 'long', 'int')) {
            $dv = [double]$p.default
            if ($hasMin -and $dv -lt [double]$p.min) { $violations.Add("${sectionName}.${key}: default < min") }
            if ($hasMax -and $dv -gt [double]$p.max) { $violations.Add("${sectionName}.${key}: default > max") }
        }
    }

    foreach ($invariant in @($section.invariants)) {
        if ($null -eq $invariant) { continue }
        if ([string]$invariant.kind -notin @('lessThan', 'bothOrNeither')) { $violations.Add("$sectionName`: 未知 invariant kind '$($invariant.kind)'") }
    }
}

$features = @($specJson.sections.TaskComplexity.features)
if ($features.Count -lt 1) { $violations.Add('TaskComplexity: features 为空') }
$weightSum = 0.0
$featureKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($f in $features) {
    if ($null -eq $f) { continue }
    if (-not $featureKeys.Add([string]$f.key)) { $violations.Add("features: 重复 key '$($f.key)'") }
    if ([string]$f.selector -notin $selectorWhitelist) { $violations.Add("features.$($f.key): 未登记 selector '$($f.selector)'") }
    if ([double]$f.saturateAt -le 0) { $violations.Add("features.$($f.key): saturateAt 必须 > 0") }
    if ([double]$f.weight -lt 0) { $violations.Add("features.$($f.key): weight 必须 ≥ 0") }
    $weightSum += [double]$f.weight
}
if ($weightSum -le 0) { $violations.Add('features: Σweight 必须 > 0（否则归一化不可判定）') }

$split = $specJson.sections.TaskComplexity.split
if ($null -eq $split) { $violations.Add('TaskComplexity: 缺少 split 声明') }
else {
    if (@($split.separators).Count -lt 1) { $violations.Add('split.separators 为空') }
    if ([int]$split.minSegments -lt 1) { $violations.Add('split.minSegments 必须 ≥ 1') }
    if ([int]$split.maxMembers -lt 1 -or [int]$split.maxMembers -gt 32) { $violations.Add('split.maxMembers 须 ∈ [1,32]') }
}

if ($violations.Count -gt 0) {
    Write-Host '[FAIL] spec 校验未通过：' -ForegroundColor Yellow
    foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
    exit 2
}
# ---------------- 格式化助手（唯一，禁各处重写） ----------------
$inv = [System.Globalization.CultureInfo]::InvariantCulture

function Format-Double([double]$Value) {
    $s = $Value.ToString('R', $inv)
    if ($s -notmatch '[.eE]') { $s += '.0' }
    return $s + 'd'
}

function Format-Int([double]$Value) { return ([int]$Value).ToString($inv) }

function Format-CSharpString([string]$Value) {
    if ($null -eq $Value) { return 'null' }
    $s = $Value -replace '\\', '\\\\' -replace '"', '\"' -replace "`r", '\r' -replace "`n", '\n' -replace "`t", '\t'
    return '"' + $s + '"'
}

function Format-DefaultText($Param) {
    if ($null -eq $Param.PSObject.Properties['default'] -or $null -eq $Param.default) { return $null }
    switch ([string]$Param.type) {
        'double' { return $Param.default.ToString('R', $inv) }
        'long'   { return ([long]$Param.default).ToString($inv) }
        'int'    { return ([int]$Param.default).ToString($inv) }
        'bool'   { return ([bool]$Param.default).ToString().ToLowerInvariant() }
        'string' { return [string]$Param.default }
    }
    return $null
}

function Get-CSharpLiteral($Param) {
    switch ([string]$Param.type) {
        'double' { return Format-Double ([double]$Param.default) }
        'long'   { return ([long]$Param.default).ToString($inv) + 'L' }
        'int'    { return ([int]$Param.default).ToString($inv) }
        'bool'   { return ([bool]$Param.default).ToString().ToLowerInvariant() }
        'string' { return Format-CSharpString ([string]$Param.default) }
    }
    return 'null'
}

function New-GeneratedCs {
    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine('// <auto-generated/>')
    [void]$sb.AppendLine('// 此文件由 Generate-ConcurrencyOptions.ps1 从 concurrency-options.spec.json 生成，禁止手工编辑。')
    [void]$sb.AppendLine('// 真源 = Frame\CsharpPrj\Modules\Foundations\Concurrency\Options\concurrency-options.spec.json')
    [void]$sb.AppendLine('#nullable enable')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('namespace Modules.Concurrency')
    [void]$sb.AppendLine('{')

    foreach ($sectionName in @('TaskComplexity', 'ConcurrencyBudget')) {
        $recordName = "${sectionName}Options"
        $section = $specJson.sections.$sectionName
        [void]$sb.AppendLine('    // >>> section ' + $sectionName)
        [void]$sb.AppendLine('    /// <summary>' + [string]$section.description + '</summary>')
        [void]$sb.AppendLine('    public sealed record ' + $recordName)
        [void]$sb.AppendLine('    {')
        foreach ($p in @($section.params)) {
            $typeName = switch ([string]$p.type) {
                'double' { 'double' }
                'long'   { 'long' }
                'int'    { 'int' }
                'bool'   { 'bool' }
                'string' { 'string?' }
            }
            $decl = '        public ' + $typeName + ' ' + $p.key + ' { get; init; }'
            $hasDefault = $null -ne $p.PSObject.Properties['default'] -and $null -ne $p.default
            if ($hasDefault) { $decl += ' = ' + (Get-CSharpLiteral $p) + ';' }
            if (-not [string]::IsNullOrWhiteSpace([string]$p.description)) {
                [void]$sb.AppendLine('        /// <summary>' + [string]$p.description + '</summary>')
            }
            [void]$sb.AppendLine($decl)
        }
        [void]$sb.AppendLine('')
        [void]$sb.AppendLine('        /// <summary>值域/不变量校验（声明面驱动；非法即抛，禁静默钳制）。</summary>')
        [void]$sb.AppendLine('        public void Validate()')
        [void]$sb.AppendLine('        {')
        $checks = 0

        foreach ($p in @($section.params)) {
            $type = [string]$p.type
            $hasMin = $null -ne $p.PSObject.Properties['min'] -and $null -ne $p.min
            $hasMax = $null -ne $p.PSObject.Properties['max'] -and $null -ne $p.max
            if (-not $hasMin -and -not $hasMax) { continue }

            if ($type -eq 'double') {
                $lo = if ($hasMin) { Format-Double ([double]$p.min) } else { $null }
                $hi = if ($hasMax) { Format-Double ([double]$p.max) } else { $null }
            }
            else {
                $lo = if ($hasMin) { Format-Int ([double]$p.min) } else { $null }
                $hi = if ($hasMax) { Format-Int ([double]$p.max) } else { $null }
            }
            if ($lo -and $hi) {
                $cond = '!((' + $p.key + ' >= ' + $lo + ') && (' + $p.key + ' <= ' + $hi + '))'
                $range = '[' + $p.min + ',' + $p.max + ']'
            }
            elseif ($lo) { $cond = '!(' + $p.key + ' >= ' + $lo + ')'; $range = '>=' + $p.min }
            else { $cond = '!(' + $p.key + ' <= ' + $hi + ')'; $range = '<=' + $p.max }

            $msg = $sectionName + '.' + $p.key + '={' + $p.key + '} 超出声明域 ' + $range
            [void]$sb.AppendLine('            if (' + $cond + ')')
            [void]$sb.AppendLine('                throw new System.InvalidOperationException($"' + $msg + '");')
            $checks++
        }

        foreach ($invariant in @($section.invariants)) {
            if ($null -eq $invariant) { continue }
            $msg = ($sectionName + ': ' + [string]$invariant.message) -replace '"', '\"'
            switch ([string]$invariant.kind) {
                'lessThan' {
                    [void]$sb.AppendLine('            if (!(' + $invariant.left + ' < ' + $invariant.right + '))')
                    [void]$sb.AppendLine('                throw new System.InvalidOperationException("' + $msg + '");')
                }
                'bothOrNeither' {
                    [void]$sb.AppendLine('            if (string.IsNullOrWhiteSpace(' + $invariant.left + ') != string.IsNullOrWhiteSpace(' + $invariant.right + '))')
                    [void]$sb.AppendLine('                throw new System.InvalidOperationException("' + $msg + '");')
                }
            }
            $checks++
        }
        if ($checks -eq 0) { [void]$sb.AppendLine('            // 无声明值域：本记录无校验项。') }
        [void]$sb.AppendLine('        }')
        [void]$sb.AppendLine('    }')
        [void]$sb.AppendLine('    // <<< section ' + $sectionName)
        [void]$sb.AppendLine('')
    }
    [void]$sb.AppendLine('    /// <summary>并发策略不可变快照：组合根一次生成，内核只读；唯一构造入口 = From（含值域校验）。</summary>')
    [void]$sb.AppendLine('    public sealed record ConcurrencyOptions(')
    [void]$sb.AppendLine('        TaskComplexityOptions TaskComplexity,')
    [void]$sb.AppendLine('        ConcurrencyBudgetOptions Budget)')
    [void]$sb.AppendLine('    {')
    [void]$sb.AppendLine('        public static ConcurrencyOptions From(TaskComplexityOptions? complexity, ConcurrencyBudgetOptions? budget)')
    [void]$sb.AppendLine('        {')
    [void]$sb.AppendLine('            var options = new ConcurrencyOptions(')
    [void]$sb.AppendLine('                complexity ?? new TaskComplexityOptions(),')
    [void]$sb.AppendLine('                budget ?? new ConcurrencyBudgetOptions());')
    [void]$sb.AppendLine('            options.TaskComplexity.Validate();')
    [void]$sb.AppendLine('            options.Budget.Validate();')
    [void]$sb.AppendLine('            return options;')
    [void]$sb.AppendLine('        }')
    [void]$sb.AppendLine('    }')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('    /// <summary>参数声明行（Section/Key/Type/Default/值域/层级/消费方）。</summary>')
    [void]$sb.AppendLine('    public sealed record ConcurrencyOptionSpec(')
    [void]$sb.AppendLine('        string Section, string Key, string Type, string? DefaultText, double? Min, double? Max, string Level, string Consumer);')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('    /// <summary>复杂度特征声明行（顺序 = 声明序；分数 = Σ(norm·w)/Σw）。</summary>')
    [void]$sb.AppendLine('    public sealed record ComplexityFeatureSpec(string Key, string Selector, double SaturateAt, double Weight);')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('    /// <summary>拆分规则声明（分隔符/最少段数/成员上限）。</summary>')
    [void]$sb.AppendLine('    public sealed record SplitPolicySpec(')
    [void]$sb.AppendLine('        System.Collections.Generic.IReadOnlyList<string> Separators, int MinSegments, int MaxMembers);')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('    /// <summary>声明表（spec 投影；用例/门禁据此对拍，禁再手抄）。</summary>')
    [void]$sb.AppendLine('    public static class ConcurrencyOptionSpecs')
    [void]$sb.AppendLine('    {')
    [void]$sb.AppendLine('        public const string SchemaVersion = "' + [string]$specJson.schemaVersion + '";')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('        public static readonly System.Collections.Generic.IReadOnlyList<ConcurrencyOptionSpec> All = new[]')
    [void]$sb.AppendLine('        {')

    foreach ($sectionName in @('TaskComplexity', 'ConcurrencyBudget')) {
        foreach ($p in @($specJson.sections.$sectionName.params)) {
            $defaultText = Format-DefaultText $p
            $defaultLiteral = if ($null -eq $defaultText) { 'null' } else { Format-CSharpString ([string]$defaultText) }
            $minLiteral = if ($null -ne $p.PSObject.Properties['min'] -and $null -ne $p.min) { Format-Double ([double]$p.min) } else { 'null' }
            $maxLiteral = if ($null -ne $p.PSObject.Properties['max'] -and $null -ne $p.max) { Format-Double ([double]$p.max) } else { 'null' }
            $line = '            new ConcurrencyOptionSpec("' + $sectionName + '", "' + $p.key + '", "' + $p.type + '", ' +
                    $defaultLiteral + ', ' + $minLiteral + ', ' + $maxLiteral + ', "' + $p.level + '", "' + $p.consumer + '"),'
            [void]$sb.AppendLine($line)
        }
    }
    [void]$sb.AppendLine('        };')
    [void]$sb.AppendLine('')

    foreach ($sectionName in @('TaskComplexity', 'ConcurrencyBudget')) {
        $keys = @($specJson.sections.$sectionName.params | ForEach-Object { '"' + $_.key + '"' })
        [void]$sb.AppendLine('        public static readonly System.Collections.Generic.IReadOnlyList<string> ' + $sectionName + 'Keys = new[]')
        [void]$sb.AppendLine('        {')
        [void]$sb.AppendLine('            ' + ($keys -join ', '))
        [void]$sb.AppendLine('        };')
        [void]$sb.AppendLine('')
    }

    [void]$sb.AppendLine('        public static readonly System.Collections.Generic.IReadOnlyList<ComplexityFeatureSpec> Features = new[]')
    [void]$sb.AppendLine('        {')
    foreach ($f in $features) {
        [void]$sb.AppendLine('            new ComplexityFeatureSpec("' + $f.key + '", "' + $f.selector + '", ' +
            (Format-Double ([double]$f.saturateAt)) + ', ' + (Format-Double ([double]$f.weight)) + '),')
    }
    [void]$sb.AppendLine('        };')
    [void]$sb.AppendLine('')

    $sepLiterals = @($split.separators | ForEach-Object { Format-CSharpString ([string]$_) })
    [void]$sb.AppendLine('        public static readonly SplitPolicySpec Split = new(')
    [void]$sb.AppendLine('            new[] { ' + ($sepLiterals -join ', ') + ' },')
    [void]$sb.AppendLine('            ' + [int]$split.minSegments + ',')
    [void]$sb.AppendLine('            ' + [int]$split.maxMembers + ');')
    [void]$sb.AppendLine('    }')
    [void]$sb.AppendLine('}')
    return $sb.ToString()
}
function New-GeneratedSchema {
    $out = [ordered]@{}
    $out['$comment'] = '此文件由 Generate-ConcurrencyOptions.ps1 生成，禁止手改（concurrency-options-schema-v1）'
    foreach ($sectionName in @('TaskComplexity', 'ConcurrencyBudget')) {
        $section = $specJson.sections.$sectionName
        $props = [ordered]@{}
        foreach ($p in @($section.params)) {
            $node = [ordered]@{}
            $node['type'] = switch ([string]$p.type) {
                'double' { 'number' }
                'long'   { 'integer' }
                'int'    { 'integer' }
                'bool'   { 'boolean' }
                'string' { 'string' }
            }
            if ($null -ne $p.PSObject.Properties['min'] -and $null -ne $p.min) { $node['minimum'] = $p.min }
            if ($null -ne $p.PSObject.Properties['max'] -and $null -ne $p.max) { $node['maximum'] = $p.max }
            if ($null -ne $p.PSObject.Properties['default'] -and $null -ne $p.default) { $node['default'] = $p.default }
            if (-not [string]::IsNullOrWhiteSpace([string]$p.description)) { $node['description'] = [string]$p.description }
            $props[[string]$p.key] = $node
        }
        $out[$sectionName] = [ordered]@{
            type                 = 'object'
            description          = [string]$section.description
            additionalProperties = $false
            properties           = $props
        }
    }
    return ($out | ConvertTo-Json -Depth 12)
}

# ---------------- 写出 ----------------
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
$wrote = 0

function Write-Artifact([string]$Path, [string]$Text, [string]$Label) {
    $dir = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($dir) -and -not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    [System.IO.File]::WriteAllText($Path, $Text, $utf8NoBom)
    Write-Host ("[OK] {0} → {1}" -f $Label, $Path) -ForegroundColor Green
}

if ($Target -eq 'cs' -or $Target -eq 'all') {
    $csPath = if ($Target -eq 'all') { Join-Path $root 'Frame\CsharpPrj\Modules\Foundations\Concurrency\Generated\ConcurrencyOptions.g.cs' } else { $OutFile }
    Write-Artifact -Path $csPath -Text (New-GeneratedCs) -Label 'ConcurrencyOptions.g.cs'
    $wrote++
}
if ($Target -eq 'schema' -or $Target -eq 'all') {
    $schemaPath = if ($Target -eq 'all') { Join-Path $root 'Projects\Plug\Agent\Config\generated\concurrency-options.schema.generated.json' } else { $OutFile }
    Write-Artifact -Path $schemaPath -Text (New-GeneratedSchema) -Label 'concurrency-options.schema.generated.json'
    $wrote++
}

Write-Host ("[OK] 生成完成（{0} 件；spec 版本 {1}）" -f $wrote, $specJson.schemaVersion) -ForegroundColor Green
exit 0




