<#
.SYNOPSIS
    启动冒烟（GUI 存活断言）—— 唯一实现（门禁 Check-LaunchSmoke 与 -Action launch-smoke 共用）。

.DESCRIPTION
    判据载体 = 进程存活 + 诊断文件集合（结构事实；禁读 console 文本 / 禁读日志措辞）：
      S1 启动器存在（缺 ⇒ fail-fast，不按超时计）
      S2 进程在 AliveSeconds 内仍存活（提前退出 ⇒ 未越过 bootstrap）
      S3 未新增 %TEMP%\FileDriven_StartupFailure_*.log
      S4 未新增 %TEMP%\FileDriven_FunctionLibFallback_*.log
    启动器路径来自装配闭包（契约 Host.Group），不写死 BuildOutput 路径。

    退出码：0 pass / 1 fail / 2 前置缺失。
#>
[CmdletBinding()]
param(
    [string]$Product = 'filedriven',
    [int]$AliveSeconds = 0
)

$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'LaunchContract.psm1') -Force -DisableNameChecking

$repoRoot = Get-LaunchRepoRoot -StartPath $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($repoRoot)) { Write-Host '[smoke] 仓库根定位失败' -ForegroundColor Red; exit 2 }

$contractPath = Get-LaunchContractPath -RepoRoot $repoRoot -Product $Product
if (-not (Test-Path -LiteralPath $contractPath)) { Write-Host ("[smoke] 契约缺失 {0}" -f $contractPath) -ForegroundColor Red; exit 2 }
$contract = Import-LaunchContract -Path $contractPath

if ($AliveSeconds -le 0) { $AliveSeconds = [int]$contract['Smoke']['AliveSeconds'] }
if ($AliveSeconds -le 0) { $AliveSeconds = 5 }
$timeoutSeconds = [int]$contract['Smoke']['TimeoutSeconds']
if ($timeoutSeconds -le 0) { $timeoutSeconds = 90 }

$configuration = [string]$env:WF_CONFIGURATION
if ([string]::IsNullOrWhiteSpace($configuration)) { $configuration = [string]$contract['Build']['Configuration'] }

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  启动冒烟（LaunchSmoke / GUI 存活断言）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

Import-Module (Join-Path $repoRoot 'Frame\PowerShellScript\Share\AssemblyClosure\AssemblyClosure.psm1') -Force -DisableNameChecking
$groupStates = @(Get-ClosureStates -RepoRoot $repoRoot -Configuration $configuration |
    Where-Object { $_.Id -eq [string]$contract['Host']['Group'] })
if ($groupStates.Count -eq 0) {
    Write-Host ("  [FAIL] 装配闭包未声明组 '{0}'" -f [string]$contract['Host']['Group']) -ForegroundColor Red
    exit 2
}
$launcher = Join-Path ([string]$groupStates[0].Root) ([string]$contract['Host']['Entry'])
if (-not (Test-Path -LiteralPath $launcher)) {
    Write-Host ("  [FAIL] 启动器缺失（fail-fast）: {0}" -f $launcher) -ForegroundColor Red
    Write-Host '  处置: Frame\Invoke\Invoke-Frame.ps1 -Action invoke-buildeffective' -ForegroundColor Yellow
    exit 2
}

function Get-DiagSet {
    $set = @{}
    foreach ($p in @('FileDriven_StartupFailure_*.log', 'FileDriven_FunctionLibFallback_*.log')) {
        foreach ($f in @(Get-ChildItem -LiteralPath $env:TEMP -Filter $p -File -ErrorAction SilentlyContinue)) { $set[$f.FullName] = $true }
    }
    return $set
}

$before = Get-DiagSet
Write-Host ("  启动器: {0}（存活断言 {1}s / 超时 {2}s）" -f $launcher, $AliveSeconds, $timeoutSeconds) -ForegroundColor Gray

$violations = [System.Collections.Generic.List[string]]::new()
$psi = [System.Diagnostics.ProcessStartInfo]::new($launcher)
$psi.WorkingDirectory = $repoRoot
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true

$proc = $null
$earlyExit = $false
$midDiag = $null
try {
    $proc = [System.Diagnostics.Process]::Start($psi)
    if ($null -eq $proc) { throw '进程创建失败' }
    [void]$proc.StandardOutput.ReadToEndAsync()
    [void]$proc.StandardError.ReadToEndAsync()

    $elapsed = 0
    while ($elapsed -lt ($AliveSeconds * 1000)) {
        if ($proc.HasExited) { $earlyExit = $true; break }
        Start-Sleep -Milliseconds 200
        $elapsed += 200
    }
    if ($earlyExit) {
        $violations.Add(("[S2] 未存活到 {0}s 即退出（ExitCode={1}）" -f $AliveSeconds, $proc.ExitCode))
    }
    else {
        Write-Host ("  进程存活 {0}s：pid={1}" -f $AliveSeconds, $proc.Id) -ForegroundColor Gray
        # 诊断快照必须在「强制收掉」之前取：kill 会写外部终止诊断，不能算启动失败
        $midDiag = Get-DiagSet
    }
}
catch {
    $violations.Add(("[S1] 启动异常: {0}" -f $_.Exception.Message))
}
finally {
    if ($null -ne $proc) {
        try { if (-not $proc.HasExited) { $proc.Kill($true) } } catch { }
        try { $proc.Dispose() } catch { }
    }
}

if ($null -ne $midDiag) {
    foreach ($d in @($midDiag.Keys | Where-Object { -not $before.ContainsKey($_) })) {
        $leaf = Split-Path -Leaf $d
        if ($leaf -like 'FileDriven_StartupFailure_*') { $violations.Add(("[S3] 新增启动失败诊断: {0}" -f $leaf)) }
        else { $violations.Add(("[S4] 新增函数库降级诊断: {0}" -f $leaf)) }
        Write-Host ("         {0}" -f $d) -ForegroundColor DarkGray
    }
}

Write-Host ''
if ($violations.Count -gt 0) {
    Write-Host ("  [FAIL] 未越过启动引导：{0} 项" -f $violations.Count) -ForegroundColor Red
    foreach ($v in $violations) { Write-Host ("         {0}" -f $v) -ForegroundColor DarkRed }
    exit 1
}
Write-Host '  [OK] 启动冒烟通过（GUI 存活且未新增 bootstrap 失败/函数库降级诊断）' -ForegroundColor Green
exit 0
