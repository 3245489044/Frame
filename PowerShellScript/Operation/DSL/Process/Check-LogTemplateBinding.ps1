#requires -Version 5.1
<#
.SYNOPSIS
    日志模板绑定门禁（LogTemplateBinding）—— 「模板声明」与「调用点载荷」必须对得上。

.DESCRIPTION
    判据载体 = 结构事实（不含执行者叙述）：
      T1 模板面：全部 *_log_messages.json 的 msgId → {占位符}
      C1 未登记：调用点 msgId 不在模板面 ⇒ 运行期只会打 WARN + 原始模板（实例：Command_Show / MainForm_restore_bounds）
      C2 漏绑定：模板占位符 ∉ 调用点匿名对象键集 ⇒ 运行期打出字面量（实例：RelationPlanProvider 传 {errors}，模板要 {message}）
    模式：默认报告；-Check 仅对「基线之外的新问题」判红；-WriteBaseline 写当前问题集（治理存量，不豁免新增）。
    基线：Frame\PowerShellScript\Baselines\Process\LogTemplateBinding.psd1

    权威：axioms:AX-META.FormatFidelity / AX.NoSilentOutlet
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$WriteBaseline
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$baselineRel = 'Frame\PowerShellScript\Baselines\Process\LogTemplateBinding.psd1'
$baselinePath = Join-Path $Root $baselineRel
$excludeRx = '\\(obj|bin|BuildOutput|_deprecated|Runtime|node_modules)\\'

function Get-SourceFiles {
    param([string]$Base, [string[]]$Roots, [string]$Filter)
    foreach ($rel in $Roots) {
        $dir = Join-Path $Base $rel
        if (-not (Test-Path -LiteralPath $dir)) { continue }
        Get-ChildItem -LiteralPath $dir -Filter $Filter -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch $excludeRx }
    }
}

# ── T1 模板面 ──
$templateMap = [System.Collections.Generic.Dictionary[string, System.Collections.Generic.HashSet[string]]]::new([StringComparer]::Ordinal)
foreach ($file in Get-SourceFiles -Base $Root -Roots @('Frame\CsharpPrj', 'Projects') -Filter '*_log_messages.json') {
    try { $json = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json } catch { continue }
    if ($null -eq $json.messages) { continue }
    foreach ($prop in $json.messages.PSObject.Properties) {
        $tpl = $prop.Value.template
        if ([string]::IsNullOrWhiteSpace($tpl)) { continue }
        if (-not $templateMap.ContainsKey($prop.Name)) {
            $templateMap[$prop.Name] = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        }
        foreach ($m in [regex]::Matches($tpl, '\{([A-Za-z_][A-Za-z0-9_]*)\}')) {
            [void]$templateMap[$prop.Name].Add($m.Groups[1].Value)
        }
    }
}

# ── C1/C2 调用点 ──
$issues = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$callHeadRx = [regex]'\.Log\(\s*"(?<id>[A-Za-z][A-Za-z0-9_]*)"'

function Get-BalancedSpan {
    param([string]$Text, [int]$OpenIndex, [char]$Open, [char]$Close)
    if ($OpenIndex -lt 0 -or $OpenIndex -ge $Text.Length -or $Text[$OpenIndex] -ne $Open) { return $null }
    $depth = 0
    $quote = [char]0
    for ($i = $OpenIndex; $i -lt $Text.Length; $i++) {
        $ch = $Text[$i]
        if ($quote -ne [char]0) {
            if ($ch -eq $quote) { $quote = [char]0 }
            continue
        }
        if ($ch -eq '"' -or $ch -eq "'") { $quote = $ch; continue }
        if ($ch -eq $Open) { $depth++ }
        elseif ($ch -eq $Close) {
            $depth--
            if ($depth -eq 0) { return $Text.Substring($OpenIndex + 1, $i - $OpenIndex - 1) }
        }
    }
    return $null
}

function Get-PayloadKeys {
    param([string]$ArgSpan)
    $keys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    # 注意：PowerShell 会把 HashSet 逐项摊平，必须用逗号阻止展开（否则空集变成 $null）。
    # 返回 $null = 该调用点载荷不是匿名对象字面量（变量/字典/记录）⇒ 本门不做绑定判定（避免假红）。
    if ([string]::IsNullOrWhiteSpace($ArgSpan)) { return $null }

    # 找到 new 之后的第一个 '{'（允许 new { ... } / new\n{ ... }）
    $m = [regex]::Match($ArgSpan, '\bnew\s*\{')
    if (-not $m.Success) { return $null }

    $openIndex = $m.Index + $m.Length - 1
    $body = Get-BalancedSpan -Text $ArgSpan -OpenIndex $openIndex -Open '{' -Close '}'
    if ($null -eq $body) { return $null }

    # 顶层逗号切分（嵌套括号内的逗号不切）
    $depth = 0; $quote = [char]0; $start = 0
    $parts = @()
    for ($i = 0; $i -lt $body.Length; $i++) {
        $ch = $body[$i]
        if ($quote -ne [char]0) { if ($ch -eq $quote) { $quote = [char]0 }; continue }
        if ($ch -eq '"' -or $ch -eq "'") { $quote = $ch; continue }
        if ($ch -in @('(', '[', '{')) { $depth++; continue }
        if ($ch -in @(')', ']', '}')) { $depth--; continue }
        if ($ch -eq ',' -and $depth -eq 0) { $parts += $body.Substring($start, $i - $start); $start = $i + 1 }
    }
    $parts += $body.Substring($start)

    foreach ($part in $parts) {
        $name = ($part -split '=')[0].Trim()
        if ($name -match '^[A-Za-z_][A-Za-z0-9_]*$') { [void]$keys.Add($name) }
    }
    return ,$keys
}

foreach ($file in Get-SourceFiles -Base $Root -Roots @('Frame\CsharpPrj', 'Projects') -Filter '*.cs') {
    $text = Get-Content -LiteralPath $file.FullName -Raw
    if ([string]::IsNullOrEmpty($text)) { continue }
    if ($text.IndexOf('.Log(', [StringComparison]::Ordinal) -lt 0) { continue }

    foreach ($m in $callHeadRx.Matches($text)) {
        $id = $m.Groups['id'].Value
        if ($id.IndexOf('_') -lt 0) { continue }

        # Log( 的实参区间：从 id 之后的 '(' 起做括号配对（内部函数调用的 ')' 不再截断）
        $openParen = $text.IndexOf('(', $m.Index)
        $argSpan = Get-BalancedSpan -Text $text -OpenIndex $openParen -Open '(' -Close ')'
        if ($null -eq $argSpan) { continue }

        $keys = Get-PayloadKeys -ArgSpan $argSpan

        if (-not $templateMap.ContainsKey($id)) {
            [void]$issues.Add("unregistered:${id}")
            continue
        }

        if ($null -eq $keys) { continue }

        foreach ($need in $templateMap[$id]) {
            if (-not $keys.Contains($need)) {
                [void]$issues.Add("missing:${id}:${need}")
            }
        }
    }
}

$current = @($issues | Sort-Object)
$known = @()
if (Test-Path -LiteralPath $baselinePath) {
    try {
        $data = Import-PowerShellDataFile -LiteralPath $baselinePath
        if ($data.ContainsKey('KnownIssues')) { $known = @($data.KnownIssues) }
    } catch { }
}
$new = @($current | Where-Object { $known -notcontains $_ })

if ($WriteBaseline) {
    $dir = Split-Path -Parent $baselinePath
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $body = "@{`r`n    # 存量基线（只降不升）：新增问题不得写入此处，须修真因。`r`n    KnownIssues = @(`r`n"
    foreach ($i in $current) {
        $safe = $i -replace "'", "''"
        $body += "        '$safe'`r`n"
    }
    $body += "    )`r`n}`r`n"
    Set-Content -LiteralPath $baselinePath -Value $body -Encoding UTF8
    Write-Host "[LogTemplateBinding] 基线已写入: $baselineRel（$($current.Count) 条）"
    exit 0
}

Write-Host "[LogTemplateBinding] 模板=$($templateMap.Count) 当前问题=$($current.Count) 基线=$($known.Count) 新增=$($new.Count)"
if ($new.Count -eq 0) {
    Write-Host '[LogTemplateBinding] 通过：无基线外新增问题'
    exit 0
}

foreach ($i in $new) { Write-Host "[LogTemplateBinding] 新增: $i" }
if ($Check) { exit 1 }
exit 0
