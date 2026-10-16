#requires -Version 5.1
<#
.SYNOPSIS
    读端口记录体形态守卫（StoreReadPayloadRecordForm）—— 读=写同物。

.DESCRIPTION
    判据（B2/ERR-06）：
      G1 零列字典投影：`SqliteStoreReadPort.cs` 内 `JsonSerializer.Serialize(payload)` = 0。
      G2 记录体解码在位：`TryDeserializeRecord` 在位。
      G3 正/负对照：合成样本（列字典序列化）必被捕获。

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
Write-Host '  读端口记录体形态守卫（StoreReadPayloadRecordForm）'
Write-Host '============================================================'

function Get-Count([string]$Text, [string]$Pattern) { return [regex]::Matches($Text, $Pattern).Count }

$file = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui\Core\Functions\Db\SqliteStoreReadPort.cs'
if (-not (Test-Path -LiteralPath $file)) {
    $violations.Add('[G1] 未见 SqliteStoreReadPort.cs')
} else {
    $t = [System.IO.File]::ReadAllText($file)
    $colDict = Get-Count $t 'JsonSerializer\.Serialize\(payload\)'
    if ($colDict -gt 0) { $violations.Add("[G1] 列字典载荷投影 × $colDict（读≠写同物）") }
    if ((Get-Count $t 'TryDeserializeRecord') -eq 0) { $violations.Add('[G2] 记录体解码（TryDeserializeRecord）缺失') }
}

# 正/负对照
if ((Get-Count 'JsonSerializer.Serialize(payload)' 'JsonSerializer\.Serialize\(payload\)') -eq 0) {
    $violations.Add('[G3] 负对照失效：合成样本（列字典序列化）未被捕获')
}

if ($violations.Count -eq 0) {
    Write-Host '  [成功] G1/G2 读端口记录体形态通过（正负对照绿）' -ForegroundColor Green
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
