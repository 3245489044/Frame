#requires -Version 5.1
<#
.SYNOPSIS
    状态根审计（R9 → 能力 D）：常驻、可复算的三态判据（state.root-single / -split / -unknown）。

.DESCRIPTION
    只读、零裁决、零写：同一逻辑根连续取样两次 → 比较「路径 / 承载者身份 / 内容指纹」，输出三态。
      Single  = 两次取根同一承载者且内容一致
      Split   = 两份快照（路径/身份/内容任一不一致）
      Unknown = 样本不足 / 根未解析 / 根为空（**不得**冒充 Single，AX.NoSilentOutlet）

    根解析口径（有意收窄）：只认 -StateRoot 与 WF_DATA_ROOT；都未给 ⇒ Unknown(root-unresolved)。
    本脚本不复制第二份根解析（既有两份：Read-AgentDb 内联 + C# AgentStateLocator/RouteStateLocation）。

    退出码：0 成功（三态均算成功，诊断不产判决）/ 2 依赖 DLL 缺失。

.EXAMPLE
    Frame\Invoke\Invoke-Frame.ps1 -Action state-root-audit -Json
.EXAMPLE
    Frame\Invoke\Invoke-Frame.ps1 -Action state-root-audit -StateRoot 'D:\Project\Main\Data'
#>
[CmdletBinding()]
param(
    [string]$StateRoot = '',
    [switch]$Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Fail { param([string]$Message) [Console]::Error.WriteLine("[StateRootAudit] $Message") }

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..\..')).Path

# ── 根解析（只认显式入参与环境变量；不新建解析实现）──
if ([string]::IsNullOrWhiteSpace($StateRoot) -and -not [string]::IsNullOrWhiteSpace($env:WF_DATA_ROOT)) {
    $StateRoot = $env:WF_DATA_ROOT
}

function New-Payload {
    param([string]$Verdict, [string]$Reason, [string]$Root, [object[]]$Samples)
    [pscustomobject]@{
        Schema  = 'state-root-audit-v1'
        Code    = "state.root-$Verdict"
        Verdict = $Verdict
        Reason  = $Reason
        Root    = $Root
        Samples = $Samples
    }
}

if ([string]::IsNullOrWhiteSpace($StateRoot)) {
    $payload = New-Payload -Verdict 'unknown' -Reason 'root-unresolved' -Root '' -Samples @()
    if ($Json) { $payload | ConvertTo-Json -Depth 6 } else { Write-Host 'state.root-unknown reason=root-unresolved（未给 -StateRoot / WF_DATA_ROOT）' }
    exit 0
}

try { $StateRoot = (Resolve-Path -LiteralPath $StateRoot).Path }
catch { Write-Fail "状态根不存在：$StateRoot"; exit 1 }

# ── 依赖程序集定位（构建输出，非 PATH）──
$dllName = 'Share.StateManagement.dll'
$candidates = @(
    (Join-Path $repoRoot 'Projects\FileDriven\BuildOutput\Debug'),
    (Join-Path $repoRoot 'Projects\FileDriven\BuildOutput\Release'),
    (Join-Path $repoRoot 'Frame\CsharpPrj\BuildOutput\Debug'),
    (Join-Path $repoRoot 'Frame\CsharpPrj\BuildOutput\Release')
)
$dllDir = $candidates | Where-Object { Test-Path -LiteralPath (Join-Path $_ $dllName) } | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($dllDir)) {
    Write-Fail "未找到 $dllName；请先构建（缺依赖 ⇒ 退出码 2）。"
    exit 2
}
try { Add-Type -Path (Join-Path $dllDir $dllName) -ErrorAction Stop }
catch { if ($_.Exception.Message -notmatch 'already') { Write-Fail $_.Exception.Message; exit 2 } }

# ── 两次取样（同一逻辑根；跨进程身份 = 规范化路径指纹）──
$now = [DateTimeOffset]::UtcNow
$s1 = [Share.StateManagement.Audit.StateRootSampler]::Sample($StateRoot, $now)
$s2 = [Share.StateManagement.Audit.StateRootSampler]::Sample($StateRoot, [DateTimeOffset]::UtcNow)
$reason = ''
$verdict = [Share.StateManagement.Audit.StateRootAudit]::Inspect($s1, $s2, [ref]$reason)

$verdictText = switch ([string]$verdict) {
    'Single' { 'single' }
    'Split' { 'split' }
    default { 'unknown' }
}

$samples = @(
    [pscustomobject]@{ Root = $s1.RootPath; Identity = $s1.IdentityHash; Content = $s1.ContentHash; AtUtc = $s1.AtUtc.ToString('o') },
    [pscustomobject]@{ Root = $s2.RootPath; Identity = $s2.IdentityHash; Content = $s2.ContentHash; AtUtc = $s2.AtUtc.ToString('o') }
)
$payload = New-Payload -Verdict $verdictText -Reason $reason -Root $StateRoot -Samples $samples

if ($Json) {
    $payload | ConvertTo-Json -Depth 6
} else {
    Write-Host ("{0} reason={1} root={2}" -f $payload.Code, $payload.Reason, $payload.Root)
    Write-Host ("  content={0} identity={1}" -f $s1.ContentHash, $s1.IdentityHash) -ForegroundColor DarkGray
}
exit 0
