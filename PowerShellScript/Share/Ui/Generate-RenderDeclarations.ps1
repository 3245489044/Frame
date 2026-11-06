#requires -Version 7.0
<#
.SYNOPSIS
    渲染声明生成器（聚拢视图 / 字段词表 / 载体映射）——唯一编译点 = Usharp.Bundle。
.DESCRIPTION
    真源（构建期，唯一手写面）：
      Config\gather\gather-views.ushar   → Config\gather\gather-views.generated.json
      Config\nodes\fields.ushar          → Config\nodes\fields.generated.json
      Config\ui\ui.ushar                 → Config\ui\ui.generated.json（token/slot/skin/part/carrier）
    派生投影（无独立手写真源）：
      ui_part_*                          → Config\ui\surface-map.generated.json
    产物入 git；漂移门 = Check-GeneratedArtifactsDrift（哨兵 + 复算逐字比对）。
    -Check 只读复算；-Apply 写盘（唯一允许的写模式）；-OutFile 供漂移门复算。
#>
[CmdletBinding(DefaultParameterSetName = 'Check')]
param(
    [Parameter(ParameterSetName = 'Check')][switch]$Check,
    [Parameter(ParameterSetName = 'Apply')][switch]$Apply,
    [ValidateSet('gatherviews', 'fields', 'surfacemap', 'uiprogram', 'all')][string]$Target = 'all',
    [string]$OutFile = ''
)

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$configRoot = Join-Path $root 'Projects\Plug\Agent\Config'

$definitions = @(
    @{ Target = 'gatherviews'; Kind = 'gatherviews'; Out = (Join-Path $configRoot 'gather\gather-views.generated.json') }
    @{ Target = 'fields';      Kind = 'fields';      Out = (Join-Path $configRoot 'nodes\fields.generated.json') }
    @{ Target = 'surfacemap';  Kind = 'surfacemap';  Out = (Join-Path $configRoot 'ui\surface-map.generated.json') }
    @{ Target = 'uiprogram';   Kind = 'uiprogram';   Out = (Join-Path $configRoot 'ui\ui.generated.json') }
)

if ($OutFile -and $Target -eq 'all') {
    Write-Error '指定 -OutFile 时须同时指定 -Target gatherviews|fields|surfacemap（单产物复算口径）'
    exit 2
}

$selected = @($definitions | Where-Object { $Target -eq 'all' -or $_.Target -eq $Target })
$toolDll = Join-Path $root 'Frame\Usharp\BuildOutput\Debug\Usharp.Bundle.dll'
if (-not (Test-Path -LiteralPath $toolDll)) {
    Write-Error "Usharp.Bundle 未构建：$toolDll（先构建 Frame\Usharp\src\Usharp.Bundle\Usharp.Bundle.csproj）"
    exit 2
}

function ConvertTo-NormalizedJson([string]$text) {
    return (($text | ConvertFrom-Json) | ConvertTo-Json -Depth 100 -Compress)
}

$utf8NoBom = New-Object System.Text.UTF8Encoding $false
$results = @()
foreach ($definition in $selected) {
    $destination = if ($OutFile) { $OutFile } else { $definition.Out }
    $tempFile = Join-Path ([System.IO.Path]::GetTempPath()) ($definition.Target + '-' + [Guid]::NewGuid().ToString('N') + '.json')
    try {
        & dotnet $toolDll $definition.Kind $configRoot $tempFile
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $tempFile)) {
            Write-Error "$($definition.Target) 声明编译失败（声明缺项/形态非法，明细见上）"
            exit 1
        }

        $results += [pscustomobject]@{ Target = $definition.Target; Path = $destination; Text = [System.IO.File]::ReadAllText($tempFile) }
    } finally {
        Remove-Item -LiteralPath $tempFile -Force -ErrorAction SilentlyContinue
    }
}

if ($Apply -or $OutFile) {
    foreach ($result in $results) {
        $dir = Split-Path $result.Path -Parent
        if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
        [System.IO.File]::WriteAllText($result.Path, $result.Text, $utf8NoBom)
        Write-Host ("[OK] 已生成 {0}：{1}" -f $result.Target, $result.Path) -ForegroundColor Green
    }
    exit 0
}

$drift = 0
foreach ($result in $results) {
    if (-not (Test-Path -LiteralPath $result.Path)) {
        Write-Host ("[MISS] {0} 产物缺失：{1}（先 -Apply）" -f $result.Target, $result.Path) -ForegroundColor Red
        $drift++
        continue
    }

    $existing = [System.IO.File]::ReadAllText($result.Path)
    if ((ConvertTo-NormalizedJson $existing) -ne (ConvertTo-NormalizedJson $result.Text)) {
        Write-Host ("[DRIFT] {0} 漂移：既有产物 ≠ 声明复算（跑 -Apply 重生成）" -f $result.Target) -ForegroundColor Red
        $drift++
    }
}

if ($drift -gt 0) { exit 1 }
Write-Host ("[OK] 渲染声明无漂移：{0}" -f (($results | ForEach-Object { $_.Target }) -join ' + ')) -ForegroundColor Green
exit 0
