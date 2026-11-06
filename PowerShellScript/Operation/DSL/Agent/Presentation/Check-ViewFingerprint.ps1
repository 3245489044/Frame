#requires -Version 5.1
<#
.SYNOPSIS
    视图指纹守卫（ViewFingerprint）—— 三视图两两不相交 + history 禁静默截断。
.DESCRIPTION
    判据（结构事实）：
      V1 七元组 (regions, sidebar, content.domain, renderMode, sectionFilter, board, composer) 去重数 = 视图数。
      V2 fingerprint.badge/accent（声明面 ui-view-facets.json 存在时）两两不同。
      V3 sectionFilter = 'l0-only' ⇒ 红（不可见语义截断）。
      NC 负对照：合成两份同七元组必须被判据捕获。
    只读；-Check 违规 → 1。
#>
[CmdletBinding()]
param([switch]$Check, [switch]$NegativeControl)

$ErrorActionPreference = 'Stop'

function Find-RepoRoot {
    $d = $PSScriptRoot
    while ($d -and -not ((Test-Path (Join-Path $d 'Frame')) -and (Test-Path (Join-Path $d 'Projects')))) { $d = Split-Path $d -Parent }
    if (-not $d) { throw '[ViewFingerprint] 无法定位仓库根' }
    return $d
}

function Fingerprint([object]$view) {
    $regions = (@($view.regions) -join ',')
    $domain = if ($null -ne $view.content) { [string]$view.content.domain } else { '' }
    return "$regions|$($view.sidebar)|$domain|$($view.renderMode)|$($view.sectionFilter)|$($view.board)|$($view.composer)"
}

$root = Find-RepoRoot
$regionsPath = Join-Path $root 'Projects\Plug\Agent\Config\ui\ui-regions.json'
$facetsPath = Join-Path $root 'Projects\Plug\Agent\Config\ui\ui-view-facets.json'
$violations = [System.Collections.Generic.List[string]]::new()

if (-not (Test-Path -LiteralPath $regionsPath)) { throw "缺文件: $regionsPath" }
$views = @((Get-Content -LiteralPath $regionsPath -Raw | ConvertFrom-Json).views)
if ($views.Count -lt 3) { $violations.Add("V1 视图数 $($views.Count) < 3（判据无标的）") }

$seen = @{}
foreach ($view in $views) {
    $key = Fingerprint $view
    if ($seen.ContainsKey($key)) {
        $violations.Add("V1 视图 '$($view.view)' 与 '$($seen[$key])' 指纹相同：$key")
    }
    else {
        $seen[$key] = [string]$view.view
    }

    if ([string]$view.sectionFilter -eq 'l0-only') {
        $violations.Add("V3 视图 '$($view.view)' sectionFilter=l0-only（不可见语义截断；改显式 filters）")
    }
}

$badges = @{}
if (Test-Path -LiteralPath $facetsPath) {
    $facets = Get-Content -LiteralPath $facetsPath -Raw | ConvertFrom-Json
    foreach ($p in $facets.views.PSObject.Properties) {
        $pair = "$($p.Value.fingerprint.badge)|$($p.Value.fingerprint.accent)"
        if ($badges.ContainsKey($pair)) {
            $violations.Add("V2 视图 '$($p.Name)' 与 '$($badges[$pair])' fingerprint 相同：$pair")
        }
        else {
            $badges[$pair] = $p.Name
        }
    }
}

Write-Host ("[ViewFingerprint] views={0} distinct={1} facets={2}" -f $views.Count, $seen.Count, $badges.Count)

if ($NegativeControl) {
    $a = [pscustomobject]@{ view = 'a'; regions = @('X'); sidebar = 's'; content = [pscustomobject]@{ domain = 'd' }; renderMode = 'r'; sectionFilter = 'all'; board = 'b'; composer = 'c' }
    $b = [pscustomobject]@{ view = 'b'; regions = @('X'); sidebar = 's'; content = [pscustomobject]@{ domain = 'd' }; renderMode = 'r'; sectionFilter = 'all'; board = 'b'; composer = 'c' }
    if ((Fingerprint $a) -ne (Fingerprint $b)) { $violations.Add('NC 负对照失效：同七元组未被判据判同') }
    else { Write-Host '[ViewFingerprint] NC caught' }
}

if ($violations.Count -gt 0) {
    foreach ($v in $violations) { Write-Host ("[VIOLATION] {0}" -f $v) }
    if ($Check) { exit 1 }
}
else {
    Write-Host '[ViewFingerprint] 通过（三视图指纹互异、无截断）'
    if ($Check) { exit 0 }
}
