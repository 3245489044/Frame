#requires -Version 5.1
<#
.SYNOPSIS
    聚拢字段映射单源守卫（GatherFieldMapping）—— 存储行字段键名不散落字面量。

.DESCRIPTION
    判据（B2/SCT-03）：
      G1 零字面量：`StoreReadAdapter.cs` 内字段键字面量 `["kind"]/["table"]/["key"]/["summary"]/["updatedAt"]` = 0。
      G2 单源在位：`StoreRowField` 单源类在位且被适配器引用。
      G3 正/负对照：合成样本（字段字面量）必被捕获。

    模式：默认报告；-Check 时违规 → 退出码 1。
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
Write-Host '  聚拢字段映射单源守卫（GatherFieldMapping）'
Write-Host '============================================================'

function Get-Count([string]$Text, [string]$Pattern) { return [regex]::Matches($Text, $Pattern).Count }

$adapter = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Modules\Framework\Gather\StoreReadAdapter.cs'
$fields = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Modules\Framework\Gather\StoreRowField.cs'

if (-not (Test-Path -LiteralPath $adapter)) {
    $violations.Add('[G1] 未见 StoreReadAdapter.cs')
} else {
    $t = [System.IO.File]::ReadAllText($adapter)
    $literals = Get-Count $t '\["(kind|table|key|summary|updatedAt)"\]'
    if ($literals -gt 0) { $violations.Add("[G1] 字段键字面量 × $literals（应经 StoreRowField）") }
    if ((Get-Count $t 'StoreRowField\.') -eq 0) { $violations.Add('[G2] 适配器未引用 StoreRowField 单源') }
}

if (-not (Test-Path -LiteralPath $fields)) { $violations.Add('[G2] 未见 StoreRowField.cs 单源') }

# 正/负对照
if ((Get-Count '["summary"] = x,' '\["(kind|table|key|summary|updatedAt)"\]') -eq 0) {
    $violations.Add('[G3] 负对照失效：合成样本（字段键字面量）未被捕获')
}

if ($violations.Count -eq 0) {
    Write-Host '  [成功] G1/G2 聚拢字段映射单源通过（正负对照绿）' -ForegroundColor Green
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
