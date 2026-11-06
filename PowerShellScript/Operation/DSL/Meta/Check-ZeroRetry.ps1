#requires -Version 5.1
<#
.SYNOPSIS
    零重试守卫（ZeroRetry）—— 把「框架内禁止重试 / 禁止隐式循环 / 禁止第三态」从语义变成机器判据。
    依据：Docs\analysis\2026\10\10.22\2026-10-22-二值终止与零内部重试-框架级重构施工单.md §5.1
.DESCRIPTION
    判据（命中即候选；增量超基线 ⇒ 违规）：
      Z1 retrySymbolsInEngine    Agent 面出现 Retry*/InfraRetry*（Engine/Domain 禁重试）
      Z2 retrySymbolsInFrame     Frame 面出现 Retry*/InfraRetry*（仅 Infrastructure 装配点允许，棘轮）
      Z3 whileTrue               while(true)/for(;;) 隐式循环
      Z4 attemptWrites           attempt++ / Round++（唯一写入者 = AttemptLedger）
      Z5 continueSymbols         LoopDecision.Continue / Action.Retry / Action.Switch
      Z6 failedPathReenqueue     失败置回（MarkDirty / 自入队）
      Z7 fireAndForgetTaskRun    Task.Run(
      Z8 thirdStateReturns       RenderOutput.Skip / IsSkip / Unchanged
      W1 catchSwallow            catch { / catch (Exception（S1-S4 期仅告警）
      W2 nullLiteralCoalesce     ?? string.Empty / ?? null（S1-S4 期仅告警）
    模式：默认报告模式；-Check 校验模式（增量 → 退出码 1）。
    棘轮基线：..\Registry\zero-retry-baseline.json（只减不增；-UpdateBaseline 冻结；禁降基线过关）。
.PARAMETER Root
    仓库超根（缺省 = 上溯至含 Frame\ 与 Projects\ 的目录）。
.PARAMETER Check
    校验模式：增量 → 退出码 1；缺省报告模式（不设退出码）。
.PARAMETER ShowDetail
    输出逐条命中明细（文件:行）。
.PARAMETER UpdateBaseline
    以本次实测重写基线（写操作，聚合器禁用）。
.EXAMPLE
    .\Check-ZeroRetry.ps1
    .\Check-ZeroRetry.ps1 -Check -ShowDetail
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail,
    [switch]$UpdateBaseline,
    [string]$BaselinePath = ''
)

$ErrorActionPreference = 'Stop'

# ---- 路径定位（深度无关；GateRuntime → DirectoryMap 单源）----
$__repoRoot = $PSScriptRoot
while ($__repoRoot -and -not ((Test-Path -LiteralPath (Join-Path $__repoRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $__repoRoot 'Projects'))) { $__repoRoot = Split-Path -Parent $__repoRoot }
if (-not $__repoRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $__repoRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
if ([string]::IsNullOrWhiteSpace($Root)) {
    $ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Key 'ZeroRetry'
    $Root = $ctx.Root
}
if ([string]::IsNullOrWhiteSpace($BaselinePath)) {
    $BaselinePath = Join-Path (Get-MappedPath -Key 'GateRegistry') 'zero-retry-baseline.json'
}

$agentRoot = Get-MappedPath -Key 'AgentSrc'
$frameRoot = Join-Path (Get-MappedPath -Key 'Frame') 'CsharpPrj'
$scanRoots = @()
if (Test-Path -LiteralPath $agentRoot) { $scanRoots += (Resolve-Path -LiteralPath $agentRoot).Path }
if (Test-Path -LiteralPath $frameRoot) { $scanRoots += (Resolve-Path -LiteralPath $frameRoot).Path }
if ($scanRoots.Count -eq 0) {
    Write-Host '[ZeroRetry] 扫描面为空（Frame\CsharpPrj / Agent\src 均缺失）' -ForegroundColor Red
    exit 2
}

$excludeRx = '\\(obj|bin|BuildOutput|_deprecated|backups)\\'
$files = @(Get-ChildItem -Path $scanRoots -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch $excludeRx })
$agentFiles = @($files | Where-Object { $_.FullName.StartsWith($agentRoot, [System.StringComparison]::OrdinalIgnoreCase) })

Write-Host '[ZeroRetry] 扫描面:' -ForegroundColor Cyan
foreach ($r in $scanRoots) { Write-Host ("  - {0}" -f $r) -ForegroundColor Cyan }
Write-Host ("[ZeroRetry] 文件数: {0}（Agent 面 {1}）" -f $files.Count, $agentFiles.Count) -ForegroundColor Cyan

function Get-HitCount {
    param([System.IO.FileInfo[]]$Set, [string]$Pattern)
    if ($Set.Count -eq 0) { return 0 }
    $m = @($Set | Select-String -Pattern $Pattern -ErrorAction SilentlyContinue)
    return $m.Count
}

function Show-Hits {
    param([System.IO.FileInfo[]]$Set, [string]$Pattern, [string]$Name, [int]$Limit = 40)
    Write-Host ("---- {0} ----" -f $Name) -ForegroundColor DarkGray
    @($Set | Select-String -Pattern $Pattern -ErrorAction SilentlyContinue) |
        Select-Object -First $Limit |
        ForEach-Object { Write-Host ("  {0}:{1}: {2}" -f $_.Path, $_.LineNumber, $_.Line.Trim()) }
}

# ---- 计数（Z 系列可阻断，W 系列告警）----
$counts = [ordered]@{}
$counts['retrySymbolsInEngine'] = Get-HitCount -Set $agentFiles -Pattern '\b(Infra)?Retry[A-Za-z]*\b'
$counts['retrySymbolsInFrame']  = Get-HitCount -Set $files -Pattern '\b(Infra)?Retry[A-Za-z]*\b'
$counts['whileTrue']            = Get-HitCount -Set $files -Pattern '(while\s*\(\s*true\s*\)|for\s*\(\s*;\s*;\s*\))'
$counts['attemptWrites']        = Get-HitCount -Set $files -Pattern '(\battempt\s*\+\+|\bRound\s*\+\+)'
$counts['continueSymbols']      = Get-HitCount -Set $files -Pattern '(LoopDecision\.Continue|Action\.Retry|Action\.Switch)'
$counts['failedPathReenqueue']  = Get-HitCount -Set $files -Pattern '\bMarkDirty\s*\('
$counts['fireAndForgetTaskRun'] = Get-HitCount -Set $files -Pattern '\bTask\.Run\s*\('
$counts['thirdStateReturns']    = Get-HitCount -Set $files -Pattern '(RenderOutput\.Skip|\bIsSkip\b|\bUnchanged\b)'

# 循环/重试通用框架判据（F 波；首版报告，C7/F7 转阻断）
$kernelFiles = @($files | Where-Object { $_.Name -eq 'LoopKernel.cs' })
$counts['kernelLoopBody']           = Get-HitCount -Set $kernelFiles -Pattern '(\bfor\s*\(|\bwhile\s*\(|LoopKernel\.ExecuteOnceAsync)'
$counts['agentConditionalRegion']   = Get-HitCount -Set $agentFiles -Pattern 'RegionIterationKind\.(WhileTrue|UntilTrue|Recursion)'
$counts['judgeFallbackTrue']        = Get-HitCount -Set $files -Pattern 'FeedbackJudge[\s\S]{0,400}return\s+current\s*;'
$counts['unboundedLoopCtor']        = Get-HitCount -Set $files -Pattern 'new\s+(SnapshotDrivenLoopEngine|SpinalPipeline)<'
$counts['rawWhileLoop']             = Get-HitCount -Set @($files | Where-Object { $_.FullName -match '\\(StateHoldDriven|Refresh)\\' }) -Pattern '(while\s*\(\s*true\s*\)|for\s*\(\s*;\s*;\s*\))'
$counts['foreignAttemptWrite']      = Get-HitCount -Set $files -Pattern '(\battempt\s*\+\+|\bround\s*\+\+|\bRound\s*\+\+)'
$counts['failedPathRetry']          = Get-HitCount -Set $files -Pattern '(InfraRetry|MarkDirty)'
$counts['directorNull']             = Get-HitCount -Set @($files | Where-Object { $_.Name -eq 'LlmStateDirector.cs' }) -Pattern 'return\s+null\s*;'

$warn = [ordered]@{}
$warn['catchSwallow']        = Get-HitCount -Set $files -Pattern '(catch\s*\{|catch\s*\(\s*Exception)'
$warn['nullLiteralCoalesce'] = Get-HitCount -Set $files -Pattern '(\?\?\s*string\.Empty|\?\?\s*null)'

$baseline = $null
if (Test-Path -LiteralPath $BaselinePath) {
    try { $baseline = Get-Content -LiteralPath $BaselinePath -Raw | ConvertFrom-Json }
    catch { Write-Host ("[ZeroRetry] 基线不可解析: {0}" -f $BaselinePath) -ForegroundColor Yellow }
}

$violations = New-Object System.Collections.Generic.List[string]

Write-Host ''
Write-Host '[ZeroRetry] 计数（Z 系列 = 阻断面）:' -ForegroundColor Cyan
foreach ($k in $counts.Keys) {
    $cur = [int]$counts[$k]
    $base = $null
    if ($null -ne $baseline -and $null -ne $baseline.counts -and $null -ne $baseline.counts.$k) {
        $base = [int]$baseline.counts.$k
    }
    $flag = ''
    if ($null -eq $base) { $flag = '（无基线）' }
    elseif ($cur -gt $base) {
        $flag = ('  <== 增量 +{0}' -f ($cur - $base))
        $violations.Add(("{0}: {1} > 基线 {2}" -f $k, $cur, $base))
    }
    Write-Host ("  {0,-26} {1,6}  基线 {2}{3}" -f $k, $cur, $(if ($null -eq $base) { '-' } else { $base }), $flag)
}

Write-Host ''
Write-Host '[ZeroRetry] 告警面（W 系列，S5 转阻断）:' -ForegroundColor Yellow
foreach ($k in $warn.Keys) {
    $cur = [int]$warn[$k]
    $base = $null
    if ($null -ne $baseline -and $null -ne $baseline.warn -and $null -ne $baseline.warn.$k) {
        $base = [int]$baseline.warn.$k
    }
    $flag = ''
    if ($null -ne $base -and $cur -gt $base) { $flag = ('  <== 增量 +{0}' -f ($cur - $base)) }
    Write-Host ("  {0,-26} {1,6}  基线 {2}{3}" -f $k, $cur, $(if ($null -eq $base) { '-' } else { $base }), $flag)
}

if ($ShowDetail) {
    Show-Hits -Set $agentFiles -Pattern '\b(Infra)?Retry[A-Za-z]*\b' -Name 'Z1 retrySymbolsInEngine'
    Show-Hits -Set $files -Pattern '(while\s*\(\s*true\s*\)|for\s*\(\s*;\s*;\s*\))' -Name 'Z3 whileTrue'
    Show-Hits -Set $files -Pattern '(\battempt\s*\+\+|\bRound\s*\+\+)' -Name 'Z4 attemptWrites'
    Show-Hits -Set $files -Pattern '(LoopDecision\.Continue|Action\.Retry|Action\.Switch)' -Name 'Z5 continueSymbols'
    Show-Hits -Set $files -Pattern '(RenderOutput\.Skip|\bIsSkip\b|\bUnchanged\b)' -Name 'Z8 thirdStateReturns'
}

if ($UpdateBaseline) {
    $dir = Split-Path -Parent $BaselinePath
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $obj = [ordered]@{
        version   = '1.0'
        generated = (Get-Date -Format 'yyyy-MM-dd')
        scan      = [ordered]@{
            frame   = 'Frame\CsharpPrj'
            agent   = 'Projects\Plug\Agent\src'
            exclude = @('obj', 'bin', 'BuildOutput', '_deprecated', 'backups')
        }
        counts    = $counts
        warn      = $warn
        note      = '零重试轴计数基线（只减不增）。-UpdateBaseline 冻结；禁降基线过关。'
    }
    ($obj | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $BaselinePath -Encoding UTF8
    Write-Host ("[ZeroRetry] 基线已写入: {0}" -f $BaselinePath) -ForegroundColor Green
    exit 0
}

if ($Check -and $violations.Count -gt 0) {
    Write-Host ''
    Write-Host '[ZeroRetry] 违规（增量超基线）:' -ForegroundColor Red
    foreach ($v in $violations) { Write-Host ("  - {0}" -f $v) -ForegroundColor Red }
    exit 1
}

if ($Check) { Write-Host '[ZeroRetry] PASS' -ForegroundColor Green }
exit 0

