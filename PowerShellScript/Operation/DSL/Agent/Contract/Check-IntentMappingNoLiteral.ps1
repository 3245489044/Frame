#requires -Version 5.1
<#
.SYNOPSIS
    intent 映射字面量守卫（IntentMappingNoLiteral）—— 映射表键引用常量，零 wire 字面量。

.DESCRIPTION
    判据（B4/DUP-10）：
      G1 零 wire 字面量：AgentIntentMapping 的 `_fnToIntent` 内键不得为字符串字面量
         （`["db_*"] =` / `["create_node_reference"] =` / `["call_func"] =`）= 0。
      G2 常量引用：映射表引用常量类（`WorkflowFunctionNames.` fn= 名 / `ToolFaceNames.` 工具面 wire 名）。
      G3 正/负对照：合成样本（wire 字面量键）必被捕获。

    模式：默认报告；-Check 时违规 → 退出码 1。

.PARAMETER Root
    仓库根（默认为本脚本位置上溯五级）。
.PARAMETER Check
    校验模式。
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

# 上下文/裁决（根定位 + 违规清单 + 统一出口）
$ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Key 'IntentMappingNoLiteral'
$Root = $ctx.Root
$violations = $ctx.Violations

Write-Host ''
Write-Host '============================================================'
Write-Host '  intent 映射字面量守卫（IntentMappingNoLiteral）'
Write-Host '============================================================'

function Get-Count([string]$Text, [string]$Pattern) { return [regex]::Matches($Text, $Pattern).Count }

$mappingPath = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Contract\Api\AgentIntentMapping.cs'

if (-not (Test-Path -LiteralPath $mappingPath)) {
    $violations.Add("[G1] 缺件：$mappingPath")
} else {
    $t = [System.IO.File]::ReadAllText($mappingPath)
    $wireLit = Get-Count $t '\[\s*"(db_|create_node_reference|call_func)[^"]*"\s*\]\s*='
    if ($wireLit -ne 0) { $violations.Add("[G1] 映射表 wire 字面量键命中 $wireLit 处（应 0，改引用 WorkflowFunctionNames.* / ToolFaceNames.*）") }
    if ($t -notmatch '(WorkflowFunctionNames|ToolFaceNames)\.') { $violations.Add('[G2] 映射表未引用常量（WorkflowFunctionNames / ToolFaceNames）') }
}

# 正/负对照
if ((Get-Count '        ["db_read_node"] = AgentIntents.NodeRead,' '\[\s*"(db_|create_node_reference|call_func)[^"]*"\s*\]\s*=') -eq 0) {
    $violations.Add('[G3] 负对照失效：合成样本（wire 字面量键）未被捕获')
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'IntentMappingNoLiteral'
