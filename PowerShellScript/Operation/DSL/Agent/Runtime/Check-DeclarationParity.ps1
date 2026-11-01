#requires -Version 7.0
<#
.SYNOPSIS
    声明面对账守卫（DeclarationParity）：视图程序 / 读路径 / 聚拢声明三段必须同源闭合。
.DESCRIPTION
    D1 [Gather] 段全覆盖：每个域必须声明 read-adapter（读适配器构造唯一来源 = 声明）。
    D2 适配器闭集：read-adapter ∈ {domain-node, stream-frame, observability, store, db-root, composite}。
    D3 键族耦合：stream-frame 必须声明 read-key-family；其余适配器不得携带 read-key-family。
    D4 视图内容域：ui-regions.views[].content.domain ∈ [Gather] 域集合（禁引用未声明域）。
    D5 读路径域：read-paths.paths[].domain ∈ [Gather] 域集合。
    D6 渲染无域分支：Agent.Ui 源码不得出现 `view == Nodes/Task/History` 形态。
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
$configRoot = Join-Path $Root 'Projects\Plug\Agent\Config'
$gatherPath = Join-Path $configRoot 'nodes\node-gather.ushar'
$uiRegionsPath = Join-Path $configRoot 'ui\ui-regions.json'
$readPathsPath = Join-Path $configRoot 'nodes\read-paths.json'
$uiSrc = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui'

$adapters = @('domain-node', 'stream-frame', 'observability', 'store', 'db-root', 'composite')
$violations = [System.Collections.Generic.List[string]]::new()

function Get-GatherDomains([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return @() }
    $text = Get-Content -LiteralPath $path -Raw
    return @([regex]::Matches($text, '(?m)^\[Gather\]\s+(\S+)') | ForEach-Object { $_.Groups[1].Value })
}

# 判据本体（抽为函数：正/负对照复用）。
function Test-AdapterEntry($entry, [string]$domain) {
    $errors = [System.Collections.Generic.List[string]]::new()
    if ($null -eq $entry) { $errors.Add(("[D1] 域 '{0}' 未声明读适配器" -f $domain)); return $errors }
    $adapter = [string]$entry.adapter
    $keyFamily = [string]$entry.keyFamily
    if ([string]::IsNullOrWhiteSpace($adapter)) { $errors.Add(("[D1] 域 '{0}' 未声明 adapter" -f $domain)); return $errors }
    if ($adapters -notcontains $adapter) { $errors.Add(("[D2] 域 '{0}' 读适配器 '{1}' 不在闭集（{2}）" -f $domain, $adapter, ($adapters -join '/'))) }
    if ($adapter -eq 'stream-frame' -and [string]::IsNullOrWhiteSpace($keyFamily)) { $errors.Add(("[D3] 域 '{0}' stream-frame 未声明 keyFamily" -f $domain)) }
    if ($adapter -ne 'stream-frame' -and -not [string]::IsNullOrWhiteSpace($keyFamily)) { $errors.Add(("[D3] 域 '{0}' 适配器 '{1}' 不得携带 keyFamily" -f $domain, $adapter)) }
    return $errors
}

if ($NegativeControl) {
    if ((Test-AdapterEntry ([pscustomobject]@{ adapter = 'stream-frame'; keyFamily = 'message-stream' }) 'x').Count -ne 0) { throw '负对照失败：D 判据对合法条目误报' }
    if ((Test-AdapterEntry ([pscustomobject]@{ adapter = 'bogus' }) 'x').Count -eq 0) { throw '负对照失败：D2 未捕获闭集外适配器' }
    if ((Test-AdapterEntry ([pscustomobject]@{ adapter = 'stream-frame' }) 'x').Count -eq 0) { throw '负对照失败：D3 未捕获缺键族' }
    if ((Test-AdapterEntry ([pscustomobject]@{ adapter = 'store'; keyFamily = 'process' }) 'x').Count -eq 0) { throw '负对照失败：D3 未捕获越界键族' }
    Write-Host '[OK] 负对照通过：D1/D2/D3 判据区分正负样本。' -ForegroundColor Green
    return
}

Write-Host '=== [DeclarationParity] 声明面对账 ==='
$adaptersPath = Join-Path $configRoot 'nodes\node-gather-adapters.json'
if (-not (Test-Path -LiteralPath $gatherPath)) {
    $violations.Add(("[D1] 缺 {0}" -f $gatherPath))
} elseif (-not (Test-Path -LiteralPath $adaptersPath)) {
    $violations.Add(("[D1] 缺 {0}（域读适配器声明面）" -f $adaptersPath))
} else {
    $gatherText = Get-Content -LiteralPath $gatherPath -Raw
    $declaredDomains = @([regex]::Matches($gatherText, '(?m)^\[Gather\]\s+(\S+)') | ForEach-Object { $_.Groups[1].Value })
    $adapterDecl = Get-Content -LiteralPath $adaptersPath -Raw | ConvertFrom-Json -Depth 16
    $declaredAdapters = $adapterDecl.domains.PSObject.Properties
    $known = @($declaredAdapters | ForEach-Object { $_.Name })
    foreach ($d in $declaredDomains) {
        $entry = $declaredAdapters[$d]
        foreach ($e in (Test-AdapterEntry $entry.Value $d)) { $violations.Add($e) }
    }

    foreach ($name in $known) {
        if ($declaredDomains -notcontains $name) {
            $violations.Add(("[D1] 读适配器声明域 '{0}' 未在 node-gather.ushar 出现（悬空声明）" -f $name))
        }
    }
}

$domains = Get-GatherDomains $gatherPath
if (Test-Path -LiteralPath $uiRegionsPath) {
    $ui = Get-Content -LiteralPath $uiRegionsPath -Raw | ConvertFrom-Json -Depth 16
    foreach ($v in @($ui.views)) {
        $domain = [string]$v.content.domain
        if ([string]::IsNullOrWhiteSpace($domain) -or $domains -notcontains $domain) {
            $violations.Add(("[D4] 视图 '{0}' 内容域 '{1}' 未在 [Gather] 声明" -f $v.view, $domain))
        }
    }
} else {
    $violations.Add(("[D4] 缺 {0}" -f $uiRegionsPath))
}

if (Test-Path -LiteralPath $readPathsPath) {
    $paths = Get-Content -LiteralPath $readPathsPath -Raw | ConvertFrom-Json -Depth 16
    foreach ($p in @($paths.paths)) {
        if ($domains -notcontains [string]$p.domain) {
            $violations.Add(("[D5] 读路径 '{0}' 域 '{1}' 未在 [Gather] 声明" -f $p.id, $p.domain))
        }
    }
} else {
    $violations.Add(("[D5] 缺 {0}" -f $readPathsPath))
}

# D7：读适配器词禁止写回共享声明面（旧二进制不认识 ⇒ 整份声明编译失败 ⇒ 视图全空；唯一载体 = sidecar JSON）。
if (Test-Path -LiteralPath $gatherPath) {
    $gatherText = Get-Content -LiteralPath $gatherPath -Raw
    foreach ($marker in @('read-adapter', 'read-key-family', 'read-selection', 'read-domain')) {
        if ($gatherText -match ('(?m)^\s*' + [regex]::Escape($marker) + '\s+\S')) {
            $violations.Add(("[D7] node-gather.ushar 含 '{0}'（旧二进制不识别 ⇒ 声明整体编译失败）；读适配器唯一载体 = node-gather-adapters.json" -f $marker))
        }
    }
}

if (Test-Path -LiteralPath $uiSrc) {
    $bad = @(Get-ChildItem -LiteralPath $uiSrc -Recurse -File -Filter '*.cs' -ErrorAction SilentlyContinue |
        Where-Object { (Get-Content -LiteralPath $_.FullName -Raw) -cmatch '\bview\s*==\s*(MainDialogViewType\.)?(Nodes|Task|History)\b' })
    foreach ($b in $bad) { $violations.Add(("[D6] 渲染层残留视图域分支：{0}" -f $b.FullName)) }
}

Write-Host ("域数: {0} | 违规命中: {1}" -f ($domains.Count), $violations.Count)
if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Host ("  [违规] {0}" -f $_) }
    if ($Check) { Write-Host '校验失败：声明面未闭合（P/S/行集三段不同源）。'; exit 1 }
}

if ($violations.Count -eq 0) { Write-Host '结果: 通过' -ForegroundColor Green }
exit 0
