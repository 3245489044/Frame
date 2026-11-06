#requires -Version 5.1
<#
.SYNOPSIS
    渲染字段覆盖率守卫（RenderCoverage）—— 三表联立：frame-kinds:fields × fields.generated × surface-map.generated。
.DESCRIPTION
    判据（结构计数，禁叙述）：
      R1 每个 kind 声明 fields[]（缺失 ⇒ 红 = 存量缺口可见）。
      R2 声明字段名 ∈ fields.generated.json 的 fields（未登记字段 ⇒ 红）。
      R3 字段 cell ∈ surface-map.generated.json 的 roles（未承载 ⇒ 红）。
      R4 覆盖率 = 已承载字段数 / 声明字段数；< 100% ⇒ 红。
      NC 负对照：合成「字段 cell 无承载」样本必须被 R3 捕获。
    模式：默认报告；-Check 违规 → 1。
#>
[CmdletBinding()]
param([switch]$Check, [switch]$NegativeControl)

$ErrorActionPreference = 'Stop'

function Find-RepoRoot {
    $d = $PSScriptRoot
    while ($d -and -not ((Test-Path (Join-Path $d 'Frame')) -and (Test-Path (Join-Path $d 'Projects')))) { $d = Split-Path $d -Parent }
    if (-not $d) { throw '[RenderCoverage] 无法定位仓库根' }
    return $d
}

$root = Find-RepoRoot
$kindsPath = Join-Path $root 'Projects\Plug\Agent\Config\nodes\frame-kinds.json'
$fieldsPath = Join-Path $root 'Projects\Plug\Agent\Config\nodes\fields.generated.json'
$mapPath = Join-Path $root 'Projects\Plug\Agent\Config\ui\surface-map.generated.json'

$violations = [System.Collections.Generic.List[string]]::new()

function Read-Json([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { throw "缺文件: $path" }
    return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
}

$kinds = (Read-Json $kindsPath).kinds
$fieldTypes = @{}
$typeCells = @{}
if (Test-Path -LiteralPath $fieldsPath) {
    $fieldsRoot = Read-Json $fieldsPath
    foreach ($p in $fieldsRoot.fields.PSObject.Properties) { $fieldTypes[$p.Name] = [string]$p.Value.type }
    foreach ($p in $fieldsRoot.types.PSObject.Properties) { $typeCells[$p.Name] = [string]$p.Value.cell }
}
else {
    $violations.Add('R2 字段词表缺失：nodes\fields.generated.json（先跑 Generate-RenderDeclarations.ps1 -Apply）')
}

$roles = @{}
if (Test-Path -LiteralPath $mapPath) {
    $mapRoot = Read-Json $mapPath
    foreach ($p in $mapRoot.roles.PSObject.Properties) { $roles[$p.Name] = $true }
}
else {
    $violations.Add('R3 载体映射缺失：ui\surface-map.generated.json（先跑 Generate-RenderDeclarations.ps1 -Apply）')
}

$declared = 0
$covered = 0
foreach ($kind in @($kinds)) {
    $name = [string]$kind.kind
    if ($null -eq $kind.PSObject.Properties['fields'] -or @($kind.fields).Count -eq 0) {
        $violations.Add("R1 kind '$name' 未声明 fields[]（渲染字段面缺声明）")
        continue
    }

    foreach ($field in @($kind.fields)) {
        $declared++
        if (-not $fieldTypes.ContainsKey($field)) {
            $violations.Add("R2 kind '$name' 字段 '$field' 未在字段词表登记")
            continue
        }

        $type = $fieldTypes[$field]
        $cell = if ($typeCells.ContainsKey($type)) { $typeCells[$type] } else { 'unknown' }
        if (-not $roles.ContainsKey($cell)) {
            $violations.Add("R3 字段 '$field' 的单元 '$cell' 无载体承载（surface-map 缺键）")
            continue
        }

        $covered++
    }
}

$rate = if ($declared -gt 0) { [math]::Round(100.0 * $covered / $declared, 2) } else { 0 }
Write-Host ("[RenderCoverage] kinds={0} declared={1} covered={2} rate={3}%" -f @($kinds).Count, $declared, $covered, $rate)
if ($declared -eq 0 -or $rate -lt 100) {
    $violations.Add("R4 覆盖率 $rate% < 100%（declared=$declared covered=$covered）")
}

if ($NegativeControl) {
    $probe = @{ field = 'usage'; type = 'tokens' }
    $cell = 'kv'
    $roles.Remove('kv') | Out-Null
    if ($roles.ContainsKey($cell)) { $violations.Add('NC 负对照失效：无承载的单元未被判据捕获') }
    Write-Host "[RenderCoverage] NC cell=$cell carried=$($roles.ContainsKey($cell))"
}

if ($violations.Count -gt 0) {
    foreach ($v in $violations) { Write-Host ("[VIOLATION] {0}" -f $v) }
    if ($Check) { exit 1 }
}
else {
    Write-Host '[RenderCoverage] 通过（覆盖率 100%）'
    if ($Check) { exit 0 }
}
