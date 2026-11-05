<#
.SYNOPSIS
    启动冒烟门禁（Gate=LaunchSmoke）—— macro 判据「能启动」：越过 bootstrap 即 pass。
.DESCRIPTION
    判据载体 = 进程生命周期 + 诊断文件集合（结构事实；禁读 console 文本 / 禁读日志措辞）。
      S1 启动器存在（缺 ⇒ fail-fast，不按超时计）
      S2 进程在超时内结束（未结束 ⇒ 判超时并强杀）
      S3 未新增 %TEMP%\FileDriven_StartupFailure_*.log（启动失败 = 未越过 bootstrap）
      S4 未新增 %TEMP%\FileDriven_FunctionLibFallback_*.log（函数库降级 = 装配闭包不完整）
    说明：探针内部可能因业务原因失败（如引擎状态），不属于本判据对象 —— 只判 bootstrap 层。
    退出码：0 pass / 1 fail / 2 前置缺失。
#>
[CmdletBinding()]
param([switch]$Check, [int]$TimeoutSeconds = 0)

$ErrorActionPreference = 'Stop'
$baselinePath = Join-Path $PSScriptRoot '..\..\..\Baselines\Meta\LaunchSmoke.psd1'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..\..'))

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  启动冒烟（LaunchSmoke）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

if (-not (Test-Path -LiteralPath $baselinePath)) { Write-Host "  [FAIL] 基线缺失: $baselinePath" -ForegroundColor Red; exit 2 }
$baseline = Import-PowerShellDataFile -LiteralPath $baselinePath

if ($TimeoutSeconds -le 0) { $TimeoutSeconds = [int]$baseline.TimeoutSeconds.Value }
$probe = [string]$baseline.ProbeExec.Value
$configuration = [string]$env:WF_CONFIGURATION
if ([string]::IsNullOrWhiteSpace($configuration)) { $configuration = 'Debug' }
$launcherRel = ([string]$baseline.LauncherRelative.Value) -replace '\$\{Configuration\}', $configuration
$launcher = Join-Path $repoRoot $launcherRel

$violations = New-Object System.Collections.Generic.List[string]

if (-not (Test-Path -LiteralPath $launcher)) {
    Write-Host ("  [FAIL] 启动器缺失（fail-fast）: {0}" -f $launcher) -ForegroundColor Red
    Write-Host '  处置: Frame\PowerShellScript\Share\Build\Invoke-QuickBuild.ps1 -Configuration Debug' -ForegroundColor Yellow
    if ($Check) { exit 1 }
    exit 0
}

function Get-DiagSet {
    $set = @{}
    foreach ($p in @('FileDriven_StartupFailure_*.log', 'FileDriven_FunctionLibFallback_*.log')) {
        foreach ($f in @(Get-ChildItem -LiteralPath $env:TEMP -Filter $p -File -ErrorAction SilentlyContinue)) {
            $set[$f.FullName] = $true
        }
    }
    return $set
}

$before = Get-DiagSet
Write-Host ("  探针: {0} --cli --exec {1}（超时 {2}s）" -f (Split-Path -Leaf $launcher), $probe, $TimeoutSeconds) -ForegroundColor Gray

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $launcher
$psi.Arguments = "--cli --exec `"$probe`" --log-mode console --auto-close"
$psi.WorkingDirectory = $repoRoot
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true

$proc = $null
try {
    $proc = [System.Diagnostics.Process]::Start($psi)
    $exited = $proc.WaitForExit($TimeoutSeconds * 1000)
    if (-not $exited) {
        $violations.Add(("[S2] 未在 {0}s 内结束（疑似 bootstrap 阻塞）" -f $TimeoutSeconds))
        try { $proc.Kill() } catch { }
    }
    else {
        Write-Host ("  进程已结束: ExitCode={0}" -f $proc.ExitCode) -ForegroundColor Gray
    }
}
catch {
    $violations.Add(("[S1] 启动异常: {0}" -f $_.Exception.Message))
}
finally {
    if ($proc) { try { $proc.Dispose() } catch { } }
}

$after = Get-DiagSet
$newDiag = @($after.Keys | Where-Object { -not $before.ContainsKey($_) })
foreach ($d in $newDiag) {
    $leaf = Split-Path -Leaf $d
    if ($leaf -like 'FileDriven_StartupFailure_*') { $violations.Add(("[S3] 新增启动失败诊断: {0}" -f $leaf)) }
    else { $violations.Add(("[S4] 新增函数库降级诊断: {0}" -f $leaf)) }
    Write-Host ("         {0}" -f $d) -ForegroundColor DarkGray
}

Write-Host ''
if ($violations.Count -gt 0) {
    Write-Host ("  [FAIL] 未越过启动引导：{0} 项" -f $violations.Count) -ForegroundColor Red
    foreach ($v in $violations) { Write-Host ("         {0}" -f $v) -ForegroundColor DarkRed }
    if ($Check) { exit 1 }
    exit 0
}
Write-Host '  [OK] 启动冒烟通过（未新增 bootstrap 失败/函数库降级诊断）' -ForegroundColor Green
exit 0
