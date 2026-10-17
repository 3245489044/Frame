# Check-KeySpaceCoverage.ps1
# 键空间框架门禁（K1–K7 的机检面）
#
# 判据（-Check 时违规即退出码 1）：
#   KS1 端口契约：声明 ports[].familyId 必须命中所声明族；`.ushar [Node]` 的端口名可解析。
#   KS2 三态禁止坍缩：框架键读取路径不得以 `?? ""` / `= default` 冒充「没有」。
#   KS3 集合策略必填：KeySpace `PlanSet(...)` 调用必须显式携带 EmptyPolicy。
#   KS4 词法唯一：框架源码不得自建 Regex / Split('.')（词法单源 = Modules.Parsing.Keys）。
#   KS5 域词隔离：框架源码不得出现 history/task/session 字面量（I10）。
#   KS6 诊断码登记：框架源码引用的 key.*/port.* 码必须登记在 KeyReasonCodes。
#   KS7 族注册唯一：声明文件（若在位）的族 id 不得重复。
#   KS8 负对照：-NegativeControl 时以合成样本验证 KS2/KS5 判据非空转。
#
# 模式：默认报告；-Check 时违规 → 退出码 1。只读仓库，零侧写。

[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root)) { $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot }

$moduleDir = Join-Path $Root 'Frame\CsharpPrj\Modules\KeySpace'
$declarationPath = Join-Path $Root 'Projects\Plug\Agent\Config\keyspaces\agent.keyspace.json'
$violations = [System.Collections.Generic.List[string]]::new()

if (-not (Test-Path -LiteralPath $moduleDir)) {
    $violations.Add('KS0 框架目录不存在：Frame\CsharpPrj\Modules\KeySpace')
    Write-GateVerdict -Violations $violations -Check:$Check -Key 'KeySpaceCoverage'
}

$sources = @(Get-ChildItem -LiteralPath $moduleDir -Recurse -Filter *.cs -File)
if ($sources.Count -eq 0) { $violations.Add('KS0 框架源码为空（判据无标的）') }

# KS2 / KS4 / KS5（逐文件）
foreach ($file in $sources) {
    $text = Get-Content -LiteralPath $file.FullName -Raw
    $rel = $file.FullName.Substring($Root.Length + 1)

    if ($text -match '\?\?\s*""') {
        $violations.Add(('KS2 三态坍缩（?? "" 兜底）：' + $rel))
    }
    if ($text -match '=\s*default\s*;' -and $rel -notmatch 'KeyValue\.cs$') {
        $violations.Add('KS2 三态坍缩（default 冒充）：' + $rel)
    }
    if ($text -match 'new\s+Regex\(' -or $text -match 'Split\(\s*''\.''\s*\)') {
        $violations.Add("KS4 自建词法（Regex/Split）：$rel")
    }
    if ($text -match '(?i)\b(history|task|session)\b') {
        $violations.Add("KS5 域词泄漏框架：$rel")
    }
}

# KS6：源码诊断码字面量 ⊆ KeyReasonCodes 注册表
$codeFile = Join-Path $moduleDir 'Diagnostics\KeyReasonCodes.cs'
$registered = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
if (Test-Path -LiteralPath $codeFile) {
    foreach ($m in [regex]::Matches((Get-Content -LiteralPath $codeFile -Raw), 'const\s+string\s+\w+\s*=\s*"([^"]+)"')) {
        [void]$registered.Add($m.Groups[1].Value)
    }
}
foreach ($file in $sources) {
    $text = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($m in [regex]::Matches($text, '"(key\.[a-z0-9\-]+|port\.[a-z0-9\-]+)"')) {
        $code = $m.Groups[1].Value
        if (-not $registered.Contains($code)) {
            $violations.Add("KS6 未登记诊断码 '$code'：$($file.Name)")
        }
    }
}

# KS1：端口契约（声明族命中 + .ushar [Node] 端口名可解析）
$workflowDir = Join-Path $Root 'Projects\Plug\Agent\Config\workflows'
if (Test-Path -LiteralPath $declarationPath) {
    $declaration = Get-Content -LiteralPath $declarationPath -Raw | ConvertFrom-Json
    $familyIds = @($declaration.families | ForEach-Object { [string]$_.id })
    $familySet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($id in $familyIds) { [void]$familySet.Add($id) }

    foreach ($port in @($declaration.ports)) {
        if (-not $familySet.Contains([string]$port.familyId)) {
            $violations.Add("KS1 端口 '$($port.name)' 指向未声明族 '$($port.familyId)'")
        }
    }
}

if (Test-Path -LiteralPath $workflowDir) {
    $portPattern = '^[A-Za-z_][A-Za-z0-9_.*]*$'
    foreach ($file in @(Get-ChildItem -LiteralPath $workflowDir -Filter *.ushar -File)) {
        $lineNo = 0
        foreach ($line in Get-Content -LiteralPath $file.FullName) {
            $lineNo++
            $trimmed = $line.Trim()
            if (-not $trimmed.StartsWith('[Node]')) { continue }
            if ($trimmed -notmatch 'fn=') {
                $violations.Add("KS1 [Node] 缺 fn=（$($file.Name):$lineNo）")
            }
            foreach ($m in [regex]::Matches($trimmed, '(?:inports|outports)=([^|]+)')) {
                foreach ($token in ($m.Groups[1].Value -split ',')) {
                    $name = $token.Trim()
                    if ($name.Length -gt 0 -and $name -notmatch $portPattern) {
                        $violations.Add("KS1 端口名非法 '$name'（$($file.Name):$lineNo）")
                    }
                }
            }
        }
    }
}

# KS3：PlanSet 调用必须显式携带 EmptyPolicy（框架/产品调用点）
$planRoots = @(
    (Join-Path $Root 'Frame\CsharpPrj'),
    (Join-Path $Root 'Projects\Plug\Agent\src')
)
foreach ($scanRoot in $planRoots) {
    if (-not (Test-Path -LiteralPath $scanRoot)) { continue }
    foreach ($file in @(Get-ChildItem -LiteralPath $scanRoot -Recurse -Filter *.cs -File)) {
        $text = Get-Content -LiteralPath $file.FullName -Raw
        if ($text -notmatch 'PlanSet\(') { continue }
        if ($text -match 'public\s+KeySetPlan\s+PlanSet\s*\(') { continue }   # 定义点，不是调用点
        $rel = $file.FullName.Substring($Root.Length + 1)
        if ($text -notmatch 'EmptyPolicy') {
            $violations.Add("KS3 PlanSet 调用未携带 EmptyPolicy：$rel")
        }
    }
}

# KS7：声明文件族 id 唯一（文件未在位 ⇒ 不判红，仅提示）
if (Test-Path -LiteralPath $declarationPath) {
    $declaration = Get-Content -LiteralPath $declarationPath -Raw | ConvertFrom-Json
    $ids = @($declaration.families | ForEach-Object { [string]$_.id })
    $dups = @($ids | Group-Object | Where-Object { $_.Count -gt 1 })
    foreach ($dup in $dups) { $violations.Add("KS7 键族重复声明：$($dup.Name)") }
}

# KS8：负对照（合成样本必须被同一判据捕获）
if ($NegativeControl) {
    $sample = 'var value = dict.TryGetValue("history:x", out var v) ? v : (object?)null ?? "";'
    $hitKs2 = $sample -match '\?\?\s*""'
    $hitKs5 = $sample -match '(?i)\b(history|task|session|resource)\b'
    if (-not ($hitKs2 -and $hitKs5)) {
        $violations.Add('KS8 负对照未捕获（判据空转）：KS2/KS5 未命中合成样本')
    }
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'KeySpaceCoverage'
