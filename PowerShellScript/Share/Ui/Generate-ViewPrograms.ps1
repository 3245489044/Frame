#requires -Version 7.0
<#
.SYNOPSIS
    视图程序生成器（ViewProgram）—— ui-regions.json ⊕ loc 反转 → view-programs.generated.json。

.DESCRIPTION
    唯一合并点（单源）：容器所有权 / 生命周期 / 状态迁移 / 容器键（loc 反转后的 DOM 名）只在此生成。
    输入：
      Config\ui\ui-regions.json   regions[]（kind → key + role） + views[]（区域集 + lifecycle/stateMigration 可选覆盖）
    输出：
      Config\ui\view-programs.generated.json（入 git，可逐键 diff）

    判据（结构事实）：
      G1 每个视图容器键在视图内唯一
      G2 所有者 = 声明视图（禁跨视图共享同一 DOM 容器）
      G3 状态迁移键 ⊆ 该视图容器键
      G4 至多一个 stream 容器（append-only 面）
    任一不满足 ⇒ 退出码 1（不产出半成品）。

.PARAMETER Stdout
    只输出 JSON（门禁一致性比对用；不写盘）。

.EXAMPLE
    .\Generate-ViewPrograms.ps1
#>
[CmdletBinding()]
param(
    [string]$Regions = '',
    [string]$OutFile = '',
    [switch]$Stdout
)

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
if ([string]::IsNullOrWhiteSpace($Regions)) { $Regions = Join-Path $root 'Projects\Plug\Agent\Config\ui\ui-regions.json' }
if ([string]::IsNullOrWhiteSpace($OutFile)) { $OutFile = Join-Path $root 'Projects\Plug\Agent\Config\ui\view-programs.generated.json' }

if (-not (Test-Path -LiteralPath $Regions)) { throw "缺件（判据不可判）：$Regions" }

$declaration = Get-Content -LiteralPath $Regions -Raw -Encoding utf8 | ConvertFrom-Json

# ---- regions[]：kind → { key, role }（loc 反转后的 DOM 名 + 角色） ----
$kindToKey = @{}
$kindToRole = @{}
foreach ($region in @($declaration.regions)) {
    if ($null -eq $region) { continue }
    $kind = [string]$region.kind
    $key = [string]$region.key
    if ([string]::IsNullOrWhiteSpace($kind) -or [string]::IsNullOrWhiteSpace($key)) { throw "regions[] 条目缺 kind/key" }
    $kindToKey[$kind] = $key
    $kindToRole[$kind] = if ([string]::Equals([string]$region.role, 'stream', 'OrdinalIgnoreCase')) { 'stream' } else { 'region' }
}

# ---- views[] → 容器集（声明序去重；owner = 视图） ----
$views = [System.Collections.Generic.List[object]]::new()
$orderedViews = @($declaration.views) | Sort-Object -Property @{ Expression = { if ($null -ne $_.selectorOrder) { [int]$_.selectorOrder } else { 100 } } }, @{ Expression = { [string]$_.view } }

foreach ($view in $orderedViews) {
    if ($null -eq $view) { continue }
    $viewKey = [string]$view.view
    if ([string]::IsNullOrWhiteSpace($viewKey)) { throw "views[] 条目缺 view" }

    $containers = [System.Collections.Generic.List[object]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    # 壳槽（结构位，非领域区域）：选择器/标题/副标题对每个视图恒在（写目标 = 稳定槽）。
    foreach ($chrome in @('region-seg', 'region-head', 'region-subhead')) {
        if ($seen.Add($chrome)) { $containers.Add([ordered]@{ key = $chrome; ownerView = $viewKey; role = 'region' }) }
    }
    foreach ($kind in @($view.regions)) {
        $kindText = [string]$kind
        if (-not $kindToKey.ContainsKey($kindText)) { throw "视图 '$viewKey' 引用未声明区域：$kindText" }
        $key = [string]$kindToKey[$kindText]
        if (-not $seen.Add($key)) { continue }
        $containers.Add([ordered]@{ key = $key; ownerView = $viewKey; role = [string]$kindToRole[$kindText] })
    }

    $migrations = [System.Collections.Generic.List[object]]::new()
    foreach ($migration in @($view.stateMigration)) {
        if ($null -eq $migration) { continue }
        $migrationKind = [string]$migration.kind
        $migrationKey = [string]$migration.key
        if ([string]::IsNullOrWhiteSpace($migrationKind) -or [string]::IsNullOrWhiteSpace($migrationKey)) { throw "视图 '$viewKey' stateMigration 缺 kind/key" }
        if (-not $seen.Contains($migrationKey)) { throw "G3 视图 '$viewKey' 状态迁移指向不存在容器：$migrationKey" }
        $migrations.Add([ordered]@{ kind = $migrationKind; key = $migrationKey })
    }

    # 判据（结构事实）：容器唯一/所有者一致/至多一个流容器
    if ($containers.Count -eq 0) { throw "视图 '$viewKey' 无容器（视图不可挂载）" }
    if (@($containers | Where-Object { $_.role -eq 'stream' }).Count -gt 1) { throw "G4 视图 '$viewKey' 流容器多于一个" }

    $lifecycle = if ([string]::Equals([string]$view.lifecycle, 'persistent', 'OrdinalIgnoreCase')) { 'persistent' } else { 'on-demand' }
    $views.Add([ordered]@{
        viewKey         = $viewKey
        lifecycle       = $lifecycle
        containers      = $containers.ToArray()
        stateMigration  = $migrations.ToArray()
    })
}

$program = [ordered]@{
    schema   = 'view-programs-v1'
    rootName = 'ui-root'
    views    = $views.ToArray()
}

$json = ($program | ConvertTo-Json -Depth 10) -replace "`r`n", "`n"

if ($Stdout) {
    Write-Output $json
    exit 0
}

[System.IO.File]::WriteAllText($OutFile, $json + "`n", [System.Text.UTF8Encoding]::new($false))
Write-Host "[OK] 视图程序产物：$OutFile（$($views.Count) 视图）" -ForegroundColor Green
exit 0
