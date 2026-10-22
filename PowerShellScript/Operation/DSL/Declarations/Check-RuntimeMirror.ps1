#requires -Version 5.1
<#
.SYNOPSIS
    运行期镜像一致性门禁（F-09）：源码真源 → Runtime/Config 副本逐文件 SHA256 相等。
.DESCRIPTION
    Runtime 分发由 BuildFramework\Distribution\DistributionPublish.psm1 组装：
      * 日志消息：Projects\FileDriven / Frame\CsharpPrj / Projects\Plug\Agent 下 *_log_messages.json
        → Runtime\Config\LogMessages\<同名>
      * 驱动配置：Projects\Information\DrivenConfig\** → Runtime\Config\DrivenConfig\**
    判据载体 = 文件内容指纹（SHA256，非 mtime / 非执行者叙述）。
    未构建 Runtime（目录缺失）⇒ 不适用，PASS 并提示（避免开发树误红）。
.PARAMETER Root
    仓库根（缺省自动上溯含 Projects 与 Frame 的目录）。
.PARAMETER Check
    校验模式：不一致 ⇒ 退出码 1；缺省仅报告。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = $PSScriptRoot
    while ($Root -and $Root -ne (Split-Path $Root -Parent)) {
        if ((Test-Path (Join-Path $Root 'Projects')) -and (Test-Path (Join-Path $Root 'Frame'))) { break }
        $Root = Split-Path $Root -Parent
    }
}
if (-not $Root -or -not (Test-Path -LiteralPath $Root)) { throw "无法定位仓库根: $Root" }

$runtimeConfig = Join-Path $Root 'Runtime\Config'
if (-not (Test-Path -LiteralPath $runtimeConfig)) {
    Write-Host "[RuntimeMirror] 未构建 Runtime（$runtimeConfig 不存在）⇒ 不适用，PASS" -ForegroundColor Yellow
    exit 0
}

function Get-Sha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

# 正/负对照（哈希判据自证：同内容相等、异内容不等）
$probeA = [Text.Encoding]::UTF8.GetBytes('runtime-mirror-probe')
$probeB = [Text.Encoding]::UTF8.GetBytes('runtime-mirror-probe-2')
$sha = [Security.Cryptography.SHA256]::Create()
$hashA1 = ([BitConverter]::ToString($sha.ComputeHash($probeA))).Replace('-', '')
$hashA2 = ([BitConverter]::ToString($sha.ComputeHash($probeA))).Replace('-', '')
$hashB = ([BitConverter]::ToString($sha.ComputeHash($probeB))).Replace('-', '')
$sha.Dispose()
if ($hashA1 -ne $hashA2 -or $hashA1 -eq $hashB) {
    Write-Host '[RuntimeMirror] 负对照失效：哈希判据不可区分内容' -ForegroundColor Red
    exit 1
}

$violations = [System.Collections.Generic.List[string]]::new()
$checked = 0

function Test-MirrorFile([string]$Source, [string]$Target) {
    if (-not (Test-Path -LiteralPath $Target)) {
        $script:violations.Add("缺失副本: $Target（源 $Source）")
        return
    }
    if ((Get-Sha256 $Source) -ne (Get-Sha256 $Target)) {
        $script:violations.Add("指纹不一致: $Source ≠ $Target")
    }
    $script:checked++
}

# ---- 1) 日志消息（与 DistributionPublish 的枚举口径一致）----
$logDest = Join-Path $runtimeConfig 'LogMessages'
$logRoots = @('Projects\FileDriven', 'Frame\CsharpPrj', 'Projects\Plug\Agent')
foreach ($relativeRoot in $logRoots) {
    $fullRoot = Join-Path $Root $relativeRoot
    if (-not (Test-Path -LiteralPath $fullRoot)) { continue }
    Get-ChildItem -LiteralPath $fullRoot -Recurse -File -Filter '*_log_messages.json' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\BuildOutput\\|\\obj\\' } |
        ForEach-Object { Test-MirrorFile -Source $_.FullName -Target (Join-Path $logDest $_.Name) }
}

# ---- 2) DrivenConfig\Scripts（纯复制面；Configs/Templates 存在构建期变换，不纳入哈希对账）----
$drivenSource = Join-Path $Root 'Projects\Information\DrivenConfig\Scripts'
$drivenTarget = Join-Path $runtimeConfig 'DrivenConfig\Scripts'
if (Test-Path -LiteralPath $drivenSource) {
    Get-ChildItem -LiteralPath $drivenSource -Recurse -File -ErrorAction SilentlyContinue |
        ForEach-Object {
            $relative = $_.FullName.Substring($drivenSource.Length).TrimStart('\')
            Test-MirrorFile -Source $_.FullName -Target (Join-Path $drivenTarget $relative)
        }
}

Write-Host ("[RuntimeMirror] 核对 {0} 个文件，违规 {1} 处" -f $checked, $violations.Count) -ForegroundColor Cyan
if ($violations.Count -eq 0) {
    Write-Host '[OK] RuntimeMirror：源与运行期副本指纹全部一致' -ForegroundColor Green
    exit 0
}

foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
Write-Host '  修复口径：重新执行分发构建（Build-Distribution）以重新复制；禁止手改 Runtime 副本。' -ForegroundColor Gray
if ($Check) { exit 1 }
exit 0
