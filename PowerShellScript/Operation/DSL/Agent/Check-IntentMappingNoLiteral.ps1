#requires -Version 5.1
<#
.SYNOPSIS
    intent 映射字面量守卫（IntentMappingNoLiteral）—— 映射表键引用常量，零 wire 字面量。

.DESCRIPTION
    判据（B4/DUP-10）：
      G1 零 wire 字面量：AgentIntentMapping 的 `_fnToIntent` 内键不得为字符串字面量
         （`["db_*"] =` / `["create_node_reference"] =` / `["call_func"] =`）= 0。
      G2 常量引用：映射表引用 `WorkflowFunctionNames.`。
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
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$violations = [System.Collections.Generic.List[string]]::new()

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
    if ($wireLit -ne 0) { $violations.Add("[G1] 映射表 wire 字面量键命中 $wireLit 处（应 0，改引用 WorkflowFunctionNames.*）") }
    if ($t -notmatch 'WorkflowFunctionNames\.') { $violations.Add('[G2] 映射表未引用 WorkflowFunctionNames 常量') }
}

# 正/负对照
if ((Get-Count '        ["db_read_node"] = AgentIntents.NodeRead,' '\[\s*"(db_|create_node_reference|call_func)[^"]*"\s*\]\s*=') -eq 0) {
    $violations.Add('[G3] 负对照失效：合成样本（wire 字面量键）未被捕获')
}

if ($violations.Count -eq 0) {
    Write-Host '  [成功] G1/G2 映射零字面量通过（正负对照绿）' -ForegroundColor Green
    Write-Host ''
    exit 0
}

Write-Host ''
Write-Host "  [违规] $($violations.Count) 项：" -ForegroundColor Red
foreach ($v in $violations) { Write-Host "    - $v" -ForegroundColor Red }
Write-Host '  结果: 失败' -ForegroundColor Red
Write-Host ''
if ($Check) { exit 1 }
exit 0
