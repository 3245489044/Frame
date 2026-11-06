#requires -Version 5.1
<#
.SYNOPSIS
    声明优先门禁（UsharpFirst）—— C# 不得承载规则表 / 判定魔数 / 同事实双源。

.DESCRIPTION
    判据载体 = 源码静态结构（字符串字面量分支 / 初始化器键数 / 判定条件数字 / const 重名异值）；
    执行者叙述不参与。权威: axioms:AX.DimensionalityReduction / SEP.DeclarativeProcessDisjoint。

      UF1 规则表 switch：`switch(...)` 内 `case "..."` ≥ 3 的文件须在基线棘轮在册
      UF2 规则表字典：`new Dictionary<string,...>{ ["..."]= }` 字面量键 ≥ 3 的文件须在基线棘轮在册
      UF3 判定魔数：if/switch 条件内 ≥2 的裸数字出现次数 ≤ 基线（默认 0）
      UF4 同事实双源：同名 const 不同值对数 ≤ 基线（默认 0）
      UF5 负对照（-NegativeControl）：三类合成样本必须被判红

    模式：默认报告；-Check 违规 ⇒ 退出码 1。只读仓库，零侧写。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'

$__gateRoot = $PSScriptRoot
while ($__gateRoot -and -not (Test-Path -LiteralPath (Join-Path $__gateRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1'))) { $__gateRoot = Split-Path -Parent $__gateRoot }
if (-not $__gateRoot) { throw '未定位 GateRuntime（Frame\PowerShellScript\Baselines\GateRuntime.psm1）' }
Import-Module (Join-Path $__gateRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root)) { $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot }

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name UsharpFirst
$allowedSwitches = @($baseline.RuleTableSwitch | ForEach-Object { ([string]$_.Value -replace '/', '\') })
$allowedDictionaries = @($baseline.RuleTableDictionary | ForEach-Object { ([string]$_.Value -replace '/', '\') })
$magicLimit = [int]@($baseline.MagicNumberSites)[0].Value
$duplicateLimit = [int]@($baseline.DuplicateConstantPairs)[0].Value

$scanRoot = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views'

function Get-SwitchLiteralCount([string]$text) {
    $count = 0
    foreach ($m in [regex]::Matches($text, 'switch\s*\([^)]*\)\s*\{')) {
        $segment = $text.Substring($m.Index, [Math]::Min(1500, $text.Length - $m.Index))
        if ([regex]::Matches($segment, 'case\s+"').Count -ge 3) { $count++ }
    }
    return $count
}

function Get-DictionaryLiteralCount([string]$text) {
    $count = 0
    foreach ($m in [regex]::Matches($text, 'new\s+Dictionary<string\s*,[^>]*>\s*\{')) {
        $segment = $text.Substring($m.Index, [Math]::Min(1500, $text.Length - $m.Index))
        if ([regex]::Matches($segment, '\["').Count -ge 3) { $count++ }
    }
    return $count
}

# 判定魔数：if/switch 条件内 ≥2 的裸数字（排除 0/1、成员访问、泛型参数）。
function Get-MagicNumberCount([string]$text) {
    $count = 0
    foreach ($line in ($text -split "`r?`n")) {
        if ($line -match '^\s*//') { continue }
        if ($line -notmatch '\b(if|switch)\s*\(') { continue }
        foreach ($m in [regex]::Matches($line, '(?<![\w.])([2-9]|[1-9][0-9]+)(?![\w.])')) { $count++ }
    }
    return $count
}

function Get-DuplicateConstantPairs([hashtable]$values) {
    $duplicates = 0
    foreach ($name in $values.Keys) {
        $distinct = @($values[$name] | Sort-Object -Unique)
        if ($distinct.Count -gt 1) { $duplicates++ }
    }
    return $duplicates
}

if ($NegativeControl) {
    $switchProbe = 'switch (x) { case "a": break; case "b": break; case "c": break; }'
    $dictProbe = 'var m = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2", ["c"] = "3" };'
    $magicProbe = 'if (retry > 5) { return; }'
    if ((Get-SwitchLiteralCount $switchProbe) -lt 1) {
        Write-Host '[UsharpFirst][NEGATIVE-CONTROL-FAILED] 合成字符串 switch 未被判红' -ForegroundColor Red
        exit 1
    }
    if ((Get-DictionaryLiteralCount $dictProbe) -lt 1) {
        Write-Host '[UsharpFirst][NEGATIVE-CONTROL-FAILED] 合成规则表字典未被判红' -ForegroundColor Red
        exit 1
    }
    if ((Get-MagicNumberCount $magicProbe) -lt 1) {
        Write-Host '[UsharpFirst][NEGATIVE-CONTROL-FAILED] 合成判定魔数未被判红' -ForegroundColor Red
        exit 1
    }
    Write-Host '[UsharpFirst][NEGATIVE-CONTROL-OK] switch/字典/魔数 均被判红' -ForegroundColor Green
    exit 0
}

$violations = New-Object System.Collections.Generic.List[string]
$switchFiles = New-Object System.Collections.Generic.List[string]
$dictionaryFiles = New-Object System.Collections.Generic.List[string]
$magicTotal = 0
$constants = @{}

if (-not (Test-Path -LiteralPath $scanRoot)) {
    Write-Host "[UsharpFirst] 扫描面不存在：$scanRoot（判据不可判定）" -ForegroundColor Red
    if ($Check) { exit 1 }
    exit 0
}

foreach ($file in (Get-ChildItem -LiteralPath $scanRoot -Recurse -File -Filter '*.cs')) {
    $relPath = $file.FullName.Substring($Root.Length).TrimStart('\', '/')
    $text = Get-Content -LiteralPath $file.FullName -Raw

    if ((Get-SwitchLiteralCount $text) -gt 0) {
        $switchFiles.Add($relPath)
        if ($allowedSwitches -notcontains $relPath) { $violations.Add("UF1 字符串规则表 switch 未在基线在册：$relPath") }
    }
    if ((Get-DictionaryLiteralCount $text) -gt 0) {
        $dictionaryFiles.Add($relPath)
        if ($allowedDictionaries -notcontains $relPath) { $violations.Add("UF2 规则表字典未在基线在册：$relPath") }
    }

    $magicTotal += Get-MagicNumberCount $text

    foreach ($m in [regex]::Matches($text, 'const\s+[A-Za-z0-9_<>?\[\],\s]+\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*([^;]+);')) {
        $name = $m.Groups[1].Value
        $value = $m.Groups[2].Value.Trim()
        if (-not $constants.ContainsKey($name)) { $constants[$name] = New-Object System.Collections.Generic.List[string] }
        $constants[$name].Add($value)
    }
}

if ($magicTotal -gt $magicLimit) { $violations.Add("UF3 判定魔数 = $magicTotal > 基线 $magicLimit") }
$duplicatePairs = Get-DuplicateConstantPairs $constants
if ($duplicatePairs -gt $duplicateLimit) { $violations.Add("UF4 同名常量异值对 = $duplicatePairs > 基线 $duplicateLimit") }

if ($ShowDetail) {
    Write-Host "  · switch 文件 = $($switchFiles.Count) / 字典文件 = $($dictionaryFiles.Count) / 魔数 = $magicTotal / 同源常量对 = $duplicatePairs"
    foreach ($item in $switchFiles) { Write-Host "    switch: $item" }
    foreach ($item in $dictionaryFiles) { Write-Host "    dict:   $item" }
}

if ($violations.Count -gt 0) {
    Write-Host "[UsharpFirst] 违规 = $($violations.Count)" -ForegroundColor Red
    foreach ($v in $violations) { Write-Host "  - $v" }
    if ($Check) { exit 1 }
    exit 0
}

Write-Host '[UsharpFirst] C# 规则表/判定魔数/同事实双源均在基线棘轮内' -ForegroundColor Green
exit 0
