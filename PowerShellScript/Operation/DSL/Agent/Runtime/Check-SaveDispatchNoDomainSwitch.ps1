#requires -Version 5.1
<#
.SYNOPSIS
    记录保存薄分派守卫（SaveDispatchNoDomainSwitch）—— 编排函数零域分支。

.DESCRIPTION
    判据（B3/SCT-06）：
      G1 零域分支：`SaveRecordAsync` 方法体内 `case DbStoreKeys.` = 0。
      G2 处理器表在位：`IRowSaveHandler` + `SaveHandlers` 在位。
      G3 正/负对照：合成样本（方法体内 case 分支）必被捕获。

    模式：默认报告；-Check 时违规 → 退出码 1。
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
Write-Host ''
Write-Host '============================================================'
Write-Host '  记录保存薄分派守卫（SaveDispatchNoDomainSwitch）'
Write-Host '============================================================'

function Get-Count([string]$Text, [string]$Pattern) { return [regex]::Matches($Text, $Pattern).Count }

$file = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui\Core\Functions\Batch\AgentUiLibraryBatch.cs'
if (-not (Test-Path -LiteralPath $file)) {
    $violations.Add('[G1] 未见 AgentUiLibraryBatch.cs')
} else {
    $t = [System.IO.File]::ReadAllText($file)
    $m = [regex]::Match($t, 'private\s+static\s+async\s+Task<string>\s+SaveRecordAsync\s*\((?<body>.*?)\n\s*\}\s*\n', 'Singleline')
    if (-not $m.Success) {
        $violations.Add('[G1] 未定位 SaveRecordAsync 方法体')
    } else {
        $switches = Get-Count $m.Groups['body'].Value 'case\s+DbStoreKeys\.'
        if ($switches -gt 0) { $violations.Add("[G1] SaveRecordAsync 体内域分支 case × $switches") }
    }
    if ((Get-Count $t 'IRowSaveHandler') -eq 0 -or (Get-Count $t 'SaveHandlers') -eq 0) {
        $violations.Add('[G2] 处理器表（IRowSaveHandler/SaveHandlers）缺失')
    }
}

# 正/负对照（探针：方法体样本）
$probe = @'
private static async Task<string> SaveRecordAsync(x, y, z)
{
    switch (domain) { case DbStoreKeys.Tasks: return "x"; }
}
'@
if ((Get-Count $probe 'case\s+DbStoreKeys\.') -eq 0) {
    $violations.Add('[G3] 负对照失效：合成样本（域分支）未被捕获')
}

if ($violations.Count -eq 0) {
    Write-Host '  [成功] G1/G2 记录保存薄分派通过（正负对照绿）' -ForegroundColor Green
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
