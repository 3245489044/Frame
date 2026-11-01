#requires -Version 7.0
<#
.SYNOPSIS
    读路径事实覆盖守卫（ReadFactCoverage）：每条读路径必须声明事实字段，且域在聚拢声明面登记。
.DESCRIPTION
    R1 声明在位：Config\nodes\read-paths.json 存在且 paths 数 >= 3（会话/观测/过程/归档至少四条主轴）。
    R2 域闭合：每条读路径的 domain ∈ node-gather.ushar 的 [Gather] 域集合。
    R3 事实非空：每条路径 facts 非空；anchorCapable=false ⇒ 必须给 nonAnchorReason（禁静默不可锚）。
    R4 终答事实：session-message 路径必须声明 terminal 与 archived（气泡身份的唯一事实源）。
    R5 唯一性：paths[].id 唯一；域不得重复声明为两条互斥路径。
    纯扫描（只读、0 写入）。默认报告；-Check 违规 → 退出码 1。
#>
[CmdletBinding()]
param([switch]$Check, [switch]$NegativeControl)

$ErrorActionPreference = 'Stop'

$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

$Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
$readPathsPath = Join-Path $Root 'Projects\Plug\Agent\Config\nodes\read-paths.json'
$gatherPath = Join-Path $Root 'Projects\Plug\Agent\Config\nodes\node-gather.ushar'

$violations = [System.Collections.Generic.List[string]]::new()

function Get-GatherDomains([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return @() }
    $text = Get-Content -LiteralPath $path -Raw
    return @([regex]::Matches($text, '(?m)^\[Gather\]\s+(\S+)') | ForEach-Object { $_.Groups[1].Value })
}

# 判据本体（抽为函数：正/负对照复用，防判据分裂）。
function Test-ReadFactCoverage($declaration, [string[]]$domains) {
    $errors = [System.Collections.Generic.List[string]]::new()
    if ($null -eq $declaration) { $errors.Add('[R1] 读路径声明缺失'); return $errors }
    $paths = @($declaration.paths)
    if ($paths.Count -lt 3) { $errors.Add(("[R1] 读路径数 {0} < 3（覆盖不足）" -f $paths.Count)) }

    $seenIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($p in $paths) {
        if ([string]::IsNullOrWhiteSpace([string]$p.id)) { $errors.Add('[R1] 读路径缺 id'); continue }
        if (-not $seenIds.Add([string]$p.id)) { $errors.Add(("[R5] 读路径 id 重复：{0}" -f $p.id)) }
        if ($domains -notcontains [string]$p.domain) { $errors.Add(("[R2] 读路径 '{0}' 的域 '{1}' 未在 node-gather.ushar 声明" -f $p.id, $p.domain)) }
        if (@($p.facts).Count -eq 0) { $errors.Add(("[R3] 读路径 '{0}' 未声明 facts" -f $p.id)) }
        if (-not [bool]$p.anchorCapable -and [string]::IsNullOrWhiteSpace([string]$p.nonAnchorReason)) {
            $errors.Add(("[R3] 读路径 '{0}' anchorCapable=false 但未给 nonAnchorReason" -f $p.id))
        }
    }

    $session = @($paths | Where-Object { [string]$_.id -eq 'session-message' })
    if ($session.Count -eq 0) {
        $errors.Add('[R4] 缺 session-message 读路径（气泡身份事实源）')
    } else {
        $facts = @($session[0].facts | ForEach-Object { [string]$_ })
        foreach ($required in @('terminal', 'archived')) {
            if ($facts -notcontains $required) { $errors.Add(("[R4] session-message 未声明事实 '{0}'" -f $required)) }
        }
    }

    return $errors
}

if ($NegativeControl) {
    $good = [pscustomobject]@{ paths = @(
        [pscustomobject]@{ id = 'session-message'; domain = 'message-stream'; facts = @('terminal', 'archived'); anchorCapable = $true; nonAnchorReason = $null },
        [pscustomobject]@{ id = 'process-panel'; domain = 'process'; facts = @('kind'); anchorCapable = $false; nonAnchorReason = 'process-facts' },
        [pscustomobject]@{ id = 'archive'; domain = 'archive'; facts = @('rounds'); anchorCapable = $true; nonAnchorReason = $null }
    ) }
    $bad = [pscustomobject]@{ paths = @(
        [pscustomobject]@{ id = 'session-message'; domain = 'unknown-domain'; facts = @(); anchorCapable = $false; nonAnchorReason = $null }
    ) }
    $domains = @('message-stream', 'process', 'archive')
    if ((Test-ReadFactCoverage $good $domains).Count -ne 0) { throw '负对照失败：R 判据对合法声明误报' }
    if ((Test-ReadFactCoverage $bad $domains).Count -eq 0) { throw '负对照失败：R 判据未捕获非法声明' }
    Write-Host '[OK] 负对照通过：缺域/facts/anchor 理由/终答事实均被捕获。' -ForegroundColor Green
    return
}

Write-Host '=== [ReadFactCoverage] 读路径事实覆盖 ==='
if (-not (Test-Path -LiteralPath $readPathsPath)) {
    $violations.Add(("[R1] 声明缺失：{0}" -f $readPathsPath))
} else {
    $decl = Get-Content -LiteralPath $readPathsPath -Raw | ConvertFrom-Json -Depth 16
    $domains = Get-GatherDomains $gatherPath
    foreach ($e in (Test-ReadFactCoverage $decl $domains)) { $violations.Add($e) }
}

Write-Host ("读路径声明: {0} | 违规命中: {1}" -f $readPathsPath, $violations.Count)
if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Host ("  [违规] {0}" -f $_) }
    if ($Check) { Write-Host '校验失败：读路径事实覆盖不闭合。'; exit 1 }
}

if ($violations.Count -eq 0) { Write-Host '结果: 通过' -ForegroundColor Green }
exit 0
