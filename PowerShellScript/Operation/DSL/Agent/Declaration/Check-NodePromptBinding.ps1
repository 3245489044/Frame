#requires -Version 5.1
<#
.SYNOPSIS
    节点提示词绑定守卫（NodePromptBinding）—— 模板槽 / 绑定域 / 读路径的机器判据。

.DESCRIPTION
    判据：
      G1 模板槽 ⊆ PromptSlot 词表（Config\workflows\prompt-compose.ushar 的 [Space] PromptSlot）
      G2 声明面 node:// 引用的 domain ∈ Config\nodes\*.ushar 已声明域
      G3 组装实现禁 SQL 旁路（SELECT / Sqlite / ExecuteReader）
      G4 新代码禁直接使用 legacy ContextNodeKind（extractor adapter 豁免）
      G5 正/负对照（合成缺失槽 / SQL 旁路必须命中）
    模式：默认报告；-Check 违规 → 退出码 1。只读仓库，零侧写。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
}

$violations = [System.Collections.Generic.List[string]]::new()
$workflowDir = Join-Path $Root 'Projects\Plug\Agent\Config\workflows'
$nodesDir = Join-Path $Root 'Projects\Plug\Agent\Config\nodes'
$skillsDir = Join-Path $Root 'Projects\Plug\Agent\skills'
$contextDir = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Modules\Framework\Context'
$workflowAgentDir = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Modules\SubAgent\Agents\Workflow'

# ---------------- G1 模板槽词表 ----------------
$promptCompose = Join-Path $workflowDir 'prompt-compose.ushar'
$declaredSlots = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
if (-not (Test-Path -LiteralPath $promptCompose)) {
    $violations.Add("[G1] 缺件：$promptCompose")
} else {
    $text = Get-Content -LiteralPath $promptCompose -Raw
    $block = [regex]::Match($text, '(?ms)^\[Space\]\s+PromptSlot\s*:[^\r\n]*\r?\n(?<body>.*?)(?=^\[|\z)')
    if (-not $block.Success) {
        $violations.Add('[G1] prompt-compose.ushar 缺 [Space] PromptSlot 词表')
    } else {
        foreach ($line in ($block.Groups['body'].Value -split "`r?`n")) {
            $token = $line.Trim()
            if ($token.Length -gt 0 -and -not $token.StartsWith('#')) { [void]$declaredSlots.Add($token) }
        }
        if ($declaredSlots.Count -eq 0) { $violations.Add('[G1] PromptSlot 词表为空') }
    }
}

if ($declaredSlots.Count -gt 0 -and (Test-Path -LiteralPath $skillsDir)) {
    foreach ($file in @(Get-ChildItem -LiteralPath $skillsDir -Recurse -File -Filter 'SKILL.md')) {
        $body = Get-Content -LiteralPath $file.FullName -Raw
        foreach ($m in [regex]::Matches($body, '<<(?<slot>[A-Za-z_][A-Za-z0-9_.:-]*)>>')) {
            $slot = $m.Groups['slot'].Value
            if (-not $declaredSlots.Contains($slot)) {
                $violations.Add("[G1] 模板槽未声明：$($file.FullName) → $slot")
            }
        }
    }
}

# G1b 模板技能（*-context）的标量槽 {slot} 亦须 ∈ 词表。
if ($declaredSlots.Count -gt 0 -and (Test-Path -LiteralPath $skillsDir)) {
    foreach ($file in @(Get-ChildItem -LiteralPath $skillsDir -Recurse -File -Filter 'SKILL.md' | Where-Object { $_.FullName -match '-context' })) {
        $body = Get-Content -LiteralPath $file.FullName -Raw
        foreach ($m in [regex]::Matches($body, '(?<!\{)\{(?<slot>[A-Za-z_][A-Za-z0-9_.:-]*)\}(?!\})')) {
            $slot = $m.Groups['slot'].Value
            if (-not $declaredSlots.Contains($slot)) {
                $violations.Add("[G1] 标量槽未声明：$($file.FullName) → $slot")
            }
        }
    }
}
# ---------------- G2 node:// domain 已声明 ----------------
$declaredDomains = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
if (Test-Path -LiteralPath $nodesDir) {
    foreach ($file in @(Get-ChildItem -LiteralPath $nodesDir -File -Filter '*.ushar')) {
        foreach ($m in [regex]::Matches((Get-Content -LiteralPath $file.FullName -Raw), '(?m)^\[Domain\]\s+(?<d>[A-Za-z0-9_-]+)')) {
            [void]$declaredDomains.Add($m.Groups['d'].Value)
        }
    }
}
if ($declaredDomains.Count -eq 0) { $violations.Add('[G2] 未解析到任何 [Domain] 声明') }

if (Test-Path -LiteralPath $workflowDir) {
    foreach ($file in @(Get-ChildItem -LiteralPath $workflowDir -File -Filter '*.ushar')) {
        foreach ($m in [regex]::Matches((Get-Content -LiteralPath $file.FullName -Raw), 'node://(?<d>[A-Za-z0-9_-]+)')) {
            $domain = $m.Groups['d'].Value
            if (-not $declaredDomains.Contains($domain)) {
                $violations.Add("[G2] node:// 引用了未声明域：$($file.Name) → $domain")
            }
        }
    }
}

# ---------------- G3 SQL 旁路 ----------------
if (Test-Path -LiteralPath $contextDir) {
    foreach ($name in @('ContextPromptAssembler.cs', 'NodePromptFunctions.cs', 'NodeContextReader.cs')) {
        $file = Join-Path $contextDir $name
        if (-not (Test-Path -LiteralPath $file)) { continue }
        $text = Get-Content -LiteralPath $file -Raw
        foreach ($pattern in @('(?i)\bSELECT\s+', '(?i)\bSqlite', '(?i)ExecuteReader')) {
            if ($text -match $pattern) { $violations.Add("[G3] 组装实现疑似 SQL 旁路：$name → $pattern") }
        }
    }
}

# ---------------- G4 legacy ContextNodeKind ----------------
if (Test-Path -LiteralPath $workflowAgentDir) {
    foreach ($file in @(Get-ChildItem -LiteralPath $workflowAgentDir -Recurse -File -Filter '*.cs')) {
        if ($file.Name -eq 'ContextGraphExtractor.cs' -or $file.Name -eq 'LegacyContextKinds.cs') { continue }
        if ((Get-Content -LiteralPath $file.FullName -Raw) -match '\bContextNodeKind\b') {
            $violations.Add("[G4] 新代码直接使用 legacy ContextNodeKind：$($file.FullName)")
        }
    }
}

# ---------------- G5 正/负对照 ----------------
$probe = '<<__no_such_slot__>>'
$probeHit = $false
foreach ($m in [regex]::Matches($probe, '<<(?<slot>[A-Za-z_][A-Za-z0-9_.:-]*)>>')) {
    if (-not $declaredSlots.Contains($m.Groups['slot'].Value)) { $probeHit = $true }
}
if (-not $probeHit) { $violations.Add('[G5] 负对照失效：合成缺失槽未被捕获') }
$sqlProbe = 'SELECT * FROM ur_nodes'
if ($sqlProbe -notmatch '(?i)\bSELECT\s+') { $violations.Add('[G5] 负对照失效：合成 SQL 旁路未被捕获') }

# G2/G4 合成正/负对照（判据自身必须能命中已知违规）。
$domainProbe = 'node://__no_such_domain__/x'
$domainProbeHit = $false
foreach ($m in [regex]::Matches($domainProbe, 'node://(?<d>[A-Za-z0-9_-]+)')) {
    if (-not $declaredDomains.Contains($m.Groups['d'].Value)) { $domainProbeHit = $true }
}
if (-not $domainProbeHit) { $violations.Add('[G5] 负对照失效：合成未声明 domain 未被捕获') }
$kindProbe = 'ContextNodeKind.Utterance'
if ($kindProbe -notmatch '\bContextNodeKind\b') { $violations.Add('[G5] 负对照失效：合成 legacy kind 未被捕获') }
if ($violations.Count -eq 0) {
    Write-Host '  [成功] 节点提示词绑定判据通过（G1–G5）' -ForegroundColor Green
    exit 0
}
Write-Host "  [违规] $($violations.Count) 项：" -ForegroundColor Red
foreach ($v in $violations) { Write-Host "    - $v" -ForegroundColor Red }
if ($Check) { exit 1 }
exit 0
