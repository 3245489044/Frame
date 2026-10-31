#requires -Version 5.1
<#
.SYNOPSIS
    前缀纯度守卫（R3）：进前缀的正文段禁含信封字段（MessageId/Timestamp/TraceId/CreatedAt）。
.DESCRIPTION
    判据载体 = 源码行文本；正/负对照由 Assert-GateControls 统一执行（判据自证）。
    -Check 违规 exit 1；默认报告模式。只读仓库。

    框架接线：根定位 → New-GateContext；路径 → directory-map(AgentSrc)；自证 → Assert-GateControls；
             裁决 → Write-GateVerdict。
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
$ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Key 'ContextGeometryPrefixPurity'
$violations = $ctx.Violations

Write-GateHeader '前缀纯度守卫（R3）'

$agentSrc = Get-MappedPath -Key 'AgentSrc'

# 信封字段（禁入 L0/Skill/Tool 段）；仅检测「写入前缀正文」的形态。
$forbidden = @(
    @{ Id = 'R3a'; Pattern = 'BodyText\s*=\s*[^;]*(MessageId|Timestamp|TraceId|CreatedAt)'; Why = '信封字段进入前缀正文' }
    @{ Id = 'R3b'; Pattern = 'CanonicalText\s*=\s*[^;]*(MessageId|Timestamp|TraceId|CreatedAt)'; Why = '信封字段进入规范字节' }
    @{ Id = 'R3c'; Pattern = 'new\("prefix\.(system|tools|role)"[^)]*(MessageId|Timestamp|TraceId|CreatedAt)'; Why = '信封字段进入前缀段' }
)

function Test-PrefixImpurity([string]$text) {
    foreach ($item in $forbidden) { if ($text -match $item.Pattern) { return $true } }
    return $false
}

# 判据自证（逐条：合法样本不误报 / 违规样本必捕获）
$controls = @(
    @{ Id = 'R3a'; Predicate = { param($t) $t -match 'BodyText\s*=\s*[^;]*(MessageId|Timestamp|TraceId|CreatedAt)' }
       Good = 'BodyText = canonicalPrefix.System;'; Bad = 'BodyText = "prefix" + MessageId;' }
    @{ Id = 'R3b'; Predicate = { param($t) $t -match 'CanonicalText\s*=\s*[^;]*(MessageId|Timestamp|TraceId|CreatedAt)' }
       Good = 'CanonicalText = canonicalPrefix.System;'; Bad = 'CanonicalText = canonical + TraceId;' }
    @{ Id = 'R3c'; Predicate = { param($t) $t -match 'new\("prefix\.(system|tools|role)"[^)]*(MessageId|Timestamp|TraceId|CreatedAt)' }
       Good = 'new("prefix.system", canonicalPrefix.System);'; Bad = 'new("prefix.system", MessageId);' }
)
foreach ($issue in (Assert-GateControls -Cases $controls -Label 'ContextGeometryPrefixPurity')) { $violations.Add($issue) }

$targets = @(
    (Join-Path $agentSrc 'Agent.Modules\SubAgent\Runtime\RolePrefixSource.cs'),
    (Join-Path $agentSrc 'Agent.Modules\Framework\ContextGeometry\GeometryRequestAdapters.cs'),
    (Join-Path $agentSrc 'Agent.Modules\Framework\ContextGeometry\ContextGeometryHost.cs')
)

foreach ($file in $targets) {
    if (-not (Test-Path -LiteralPath $file)) { $violations.Add("[R3] 缺失目标文件：$file"); continue }
    $lineNo = 0
    foreach ($line in [System.IO.File]::ReadAllLines($file)) {
        $lineNo++
        if (Test-PrefixImpurity $line) {
            $why = ($forbidden | Where-Object { $line -match $_.Pattern } | Select-Object -First 1).Why
            $violations.Add("[R3] $(Split-Path $file -Leaf):$lineNo 命中（$why）")
        }
    }
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'ContextGeometryPrefixPurity'
