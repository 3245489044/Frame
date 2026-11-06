#requires -Version 5.1
<#
.SYNOPSIS
    异常网关守卫（ExceptionGateway）——「全库异常订阅点各 ≤ 1 且收敛到唯一出口」由纪律变为机器判据。
.DESCRIPTION
    判据（命中即违规）：
      1. `Application.ThreadException +=`               全库订阅点 ≤ 1
      2. `AppDomain.CurrentDomain.UnhandledException +=` 全库订阅点 ≤ 1
      3. `TaskScheduler.UnobservedTaskException +=`      全库订阅点 ≤ 1
      4. 上述任一订阅点不位于 ExceptionGateway.cs ⇒ 违规（散落第二出口）
    依据：Docs\analysis\2026\10\10.13\2026-10-13-框架内建缺陷收集与崩溃取证-通用框架设计与施工单.md §9.2
.PARAMETER Root
    仓库超根（缺省 = 本脚本上溯四级；含 Frame\ 与 Projects\ 两个子模块）。
.PARAMETER Check
    校验模式：违规 → 退出码 1；缺省报告模式（不设退出码）。
.EXAMPLE
    .\Check-ExceptionGateway.ps1 -Check
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

# 根定位（深度无关）：GateRuntime → DirectoryMap 以「Frame\ + Projects\」marker 上溯（迁 Environment 后不依赖脚本深度）
$__repoRoot = $PSScriptRoot
while ($__repoRoot -and -not ((Test-Path -LiteralPath (Join-Path $__repoRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $__repoRoot 'Projects'))) { $__repoRoot = Split-Path -Parent $__repoRoot }
if (-not $__repoRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $__repoRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
$ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Key 'ExceptionGateway'
$Root = $ctx.Root

$scanRoots = @()
foreach ($relPair in @(@{K = 'Frame'; S = 'CsharpPrj' }, @{K = 'Projects'; S = '' })) {
    $p = if ($relPair.S) { Join-Path (Get-MappedPath -Key $relPair.K) $relPair.S } else { Get-MappedPath -Key $relPair.K }
    if (Test-Path -LiteralPath $p) { $scanRoots += (Resolve-Path $p).Path }
}
$excludeRx = '\\(obj|bin|_deprecated|BuildOutput|backups)\\'

$patterns = [ordered]@{
    ThreadException     = 'Application\.ThreadException\s*\+='
    AppDomainUnhandled  = 'AppDomain\.CurrentDomain\.UnhandledException\s*\+='
    UnobservedTask      = 'TaskScheduler\.UnobservedTaskException\s*\+='
}

$violations = 0
$report = @()

foreach ($kind in $patterns.Keys) {
    $rx = [regex]$patterns[$kind]
    $hits = @()
    foreach ($scanRoot in $scanRoots) {
        $files = @(Get-ChildItem -LiteralPath $scanRoot -Recurse -Filter '*.cs' -File -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch $excludeRx })
        foreach ($f in $files) {
            $lines = [System.IO.File]::ReadAllLines($f.FullName)
            for ($i = 0; $i -lt $lines.Count; $i++) {
                if ($rx.IsMatch($lines[$i])) {
                    $hits += [pscustomobject]@{ File = $f.FullName; Line = ($i + 1) }
                }
            }
        }
    }

    if ($hits.Count -gt 1) {
        $violations++
        $report += ("[{0}] 订阅点 {1} > 1（禁多出口）" -f $kind, $hits.Count)
    }
    foreach ($h in $hits) {
        if ($h.File -notmatch 'ExceptionGateway\.cs$') {
            $violations++
            $report += ("[{0}] 订阅点不在 ExceptionGateway.cs：{1}:{2}" -f $kind, $h.File, $h.Line)
        }
    }
    Write-Host ("  {0}: 订阅点 {1} 处" -f $kind, $hits.Count) -ForegroundColor Gray
}

Write-Host '[ExceptionGateway] 核对完成' -ForegroundColor Cyan
foreach ($line in $report) { Write-Host ("  ❌ {0}" -f $line) -ForegroundColor Red }
if ($violations -eq 0) {
    Write-Host '[ExceptionGateway] 通过：三类订阅点各 ≤ 1 且收敛于 ExceptionGateway' -ForegroundColor Green
}

$exitCode = 0
if ($Check -and $violations -gt 0) { $exitCode = 1 }
exit $exitCode
