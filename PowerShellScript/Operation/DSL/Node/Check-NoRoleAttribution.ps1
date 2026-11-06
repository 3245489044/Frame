#requires -Version 5.1
<#
.SYNOPSIS
    归属判据门禁（NoRoleAttribution）—— 归属判定禁按 Role 否认式。

.DESCRIPTION
    判据载体 = 源码静态结构（C# 中 `Role == ConversationRole.*` / `Role != ConversationRole.*` 形态）；
    执行者叙述不参与。权威: formula:Gate.CallAdmissibility / axioms:SEP.JudgmentCarrierStructural。

    面选择必须走肯定式白名单（UiFaceSpec/UiWindowContextSpec.IncludedChannels），
    `Role` 只可作为呈现兜底，不得参与「事实归属哪个面/哪个流水」的判定。

      NR1 归属判定点 = 0（基线 Allow 名单在册者除外，棘轮只降不升）
      NR2 面选择唯一入口存在（UiWindowContextPolicy.Apply 调用 = 1 处/面）
      NR3 负对照（-NegativeControl）：合成样本必须被判红，否则判「判据空转」

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
$baseline = Import-GateBaseline -Name NoRoleAttribution

$scanRoots = @(
    'Projects\Plug\Agent\src',
    'Frame\CsharpPrj\Share\Frameworks\Conversation'
)

$allow = @()
if ($baseline -and $baseline.ContainsKey('Allow')) { $allow = @($baseline.Allow | ForEach-Object { [string]$_.Value }) }

$pattern = '\.Role\s*(==|!=)\s*ConversationRole\.'

function Find-AttributionSites {
    param([string[]]$Files, [string]$Text = $null)
    $hits = [System.Collections.Generic.List[string]]::new()
    if ($null -ne $Text) {
        foreach ($line in ($Text -split "`r?`n")) {
            if ($line -match $pattern) { $hits.Add($line.Trim()) }
        }
        return $hits
    }

    foreach ($file in $Files) {
        $rel = $file.FullName.Substring($Root.Length).TrimStart('\', '/')
        if ($allow -contains $rel) { continue }
        $lineNo = 0
        foreach ($line in (Get-Content -LiteralPath $file.FullName)) {
            $lineNo++
            if ($line -match $pattern) { $hits.Add(('{0}:{1}: {2}' -f $rel, $lineNo, $line.Trim())) }
        }
    }
    return $hits
}

$files = @()
foreach ($rel in $scanRoots) {
    $dir = Join-Path $Root $rel
    if (-not (Test-Path -LiteralPath $dir)) { continue }
    $files += @(Get-ChildItem -LiteralPath $dir -Recurse -File -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '\\(obj|bin|BuildOutput)\\' })
}

$violations = @(Find-AttributionSites -Files $files)
$negativeHits = @(Find-AttributionSites -Files @() -Text 'if (message.Role != ConversationRole.Assistant) { return; }')

if ($NegativeControl) {
    if ($negativeHits.Count -eq 0) {
        Write-Host '[NoRoleAttribution][NEGATIVE-CONTROL-FAILED] 合成违规样本未被判红（判据空转）' -ForegroundColor Red
        exit 1
    }
    Write-Host "[NoRoleAttribution][NEGATIVE-CONTROL-OK] 合成样本命中 $($negativeHits.Count) 处" -ForegroundColor Green
    exit 0
}

if ($violations.Count -gt 0) {
    Write-Host "[NoRoleAttribution] 归属判定点 = $($violations.Count)（判据目标 = 0）" -ForegroundColor Red
    foreach ($v in $violations) { Write-Host "  - $v" }
    if ($Check) { exit 1 }
    exit 0
}

Write-Host '[NoRoleAttribution] 归属判定点 = 0' -ForegroundColor Green
exit 0
