#requires -Version 5.1
<#
.SYNOPSIS
    能力位注册表投影守卫（IntentSupportsProjected）—— 禁逐 intent case 分支。

.DESCRIPTION
    判据（B3/DUP-11）：
      G1 零逐 intent 分支：`AgentApiIntentSource.Supports` 内 `case AgentIntents.` = 0。
      G2 探针表在位：`Probes` + `BuildProbes` 在位。
      G3 正/负对照：合成样本（逐 intent case）必被捕获。

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
Write-Host '  能力位注册表投影守卫（IntentSupportsProjected）'
Write-Host '============================================================'

function Get-Count([string]$Text, [string]$Pattern) { return [regex]::Matches($Text, $Pattern).Count }

$file = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Core\Entry\Handlers\AgentApiIntentSource.cs'
if (-not (Test-Path -LiteralPath $file)) {
    $violations.Add('[G1] 未见 AgentApiIntentSource.cs')
} else {
    $t = [System.IO.File]::ReadAllText($file)
    $m = [regex]::Match($t, 'public\s+bool\s+Supports\s*\((?<body>.*?)\n\s*\}\s*\n', 'Singleline')
    if (-not $m.Success) {
        $violations.Add('[G1] 未定位 Supports 方法体')
    } else {
        $cases = Get-Count $m.Groups['body'].Value 'case\s+AgentIntents\.'
        if ($cases -gt 0) { $violations.Add("[G1] Supports 内逐 intent case × $cases") }
    }
    if ((Get-Count $t 'BuildProbes') -eq 0 -or (Get-Count $t 'Probes\.TryGetValue') -eq 0) {
        $violations.Add('[G2] 探针表（Probes/BuildProbes）缺失')
    }
}

# 正/负对照
if ((Get-Count 'case AgentIntents.TaskGet:' 'case\s+AgentIntents\.') -eq 0) {
    $violations.Add('[G3] 负对照失效：合成样本（逐 intent case）未被捕获')
}

if ($violations.Count -eq 0) {
    Write-Host '  [成功] G1/G2 能力位注册表投影通过（正负对照绿）' -ForegroundColor Green
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
