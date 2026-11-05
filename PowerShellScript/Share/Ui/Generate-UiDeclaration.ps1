#requires -Version 7.0
<#
.SYNOPSIS
    UI 声明产物生成器（ui-declaration-v1 + ui-regions-v1 兼容视图）。

.DESCRIPTION
    唯一编译点 = Frame\Usharp\src\Usharp.Adapters.Declarations\DeclaredBundleBuilder（经 Usharp.Bundle 调用）；
    发射器 = Usharp.Emit.UsharpUiDeclarationEmitter（视图/loc = Record 记录，containers 派生）。
    声明面（构建期）：Config\ui\ui-view.ushar + Config\ui\ui-loc.ushar（专用闭包，不进主 bundle）。
    产物（入 git）：
      · declaration → Agent.Ui\Ui\Views\Declaration\ui-declaration.generated.json（运行期零解析唯一入口）
      · regions     → Config\ui\ui-regions.json（**生成物**，兼容运行期 ViewCompiler 与既有门禁；
                       带 "$comment = GENERATED from Config/ui/ui-view.ushar" 标记）
    fingerprint = 正文内容规范化 JSON 的 SHA256（覆盖容器/迁移/生命周期 ⇒ 改容器必变）。

.PARAMETER Check
    复算并与既有产物比对（结构化逐键比较；漂移 ⇒ 退出码 1）。不写盘。
.PARAMETER Apply
    写盘（唯一允许的写模式）。
.PARAMETER Target
    目标产物（all = declaration + regions；单产物配合 -OutFile 供漂移门复算）。
.PARAMETER OutFile
    产物路径（缺省 = 各目标规范位置；多目标时禁止指定）。
#>
[CmdletBinding(DefaultParameterSetName = 'Check')]
param(
    [Parameter(ParameterSetName = 'Check')][switch]$Check,
    [Parameter(ParameterSetName = 'Apply')][switch]$Apply,
    [ValidateSet('all', 'declaration', 'regions')][string]$Target = 'all',
    [string]$OutFile = ''
)

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$configRoot = Join-Path $root 'Projects\Plug\Agent\Config'
$uiDir = Join-Path $configRoot 'ui'
$productPath = Join-Path $root 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\Declaration\ui-declaration.generated.json'
$regionsPath = Join-Path $uiDir 'ui-regions.json'

$toolDll = Join-Path $root 'Frame\Usharp\BuildOutput\Debug\Usharp.Bundle.dll'
if (-not (Test-Path -LiteralPath $toolDll)) {
    Write-Error "Usharp.Bundle 未构建：$toolDll（先构建 Frame\Usharp\src\Usharp.Bundle\Usharp.Bundle.csproj）"
    exit 2
}
foreach ($declaration in @('ui-view.ushar', 'ui-loc.ushar')) {
    $path = Join-Path $uiDir $declaration
    if (-not (Test-Path -LiteralPath $path)) { Write-Error "声明缺失：$path"; exit 2 }
}

$effectiveTarget = $Target
# 兼容既有漂移门（仅传 -OutFile）：缺省视为 declaration（单产物复算）。
if ($OutFile -and -not $PSBoundParameters.ContainsKey('Target')) { $effectiveTarget = 'declaration' }

$targets = @()
if ($effectiveTarget -in @('all', 'declaration')) { $targets += 'declaration' }
if ($effectiveTarget -in @('all', 'regions')) { $targets += 'regions' }
if ($OutFile -and $targets.Count -gt 1) {
    Write-Error '指定 -OutFile 时须同时指定 -Target declaration|regions（单产物复算口径）'
    exit 2
}

function ConvertTo-NormalizedJson([string]$text) {
    return (($text | ConvertFrom-Json) | ConvertTo-Json -Depth 100 -Compress)
}

$utf8NoBom = New-Object System.Text.UTF8Encoding $false
$results = @()
foreach ($target in $targets) {
    $emitKind = if ($target -eq 'declaration') { 'ui' } else { 'uiregions' }
    $destination = if ($OutFile) { $OutFile } elseif ($target -eq 'declaration') { $productPath } else { $regionsPath }

    $tempFile = Join-Path ([System.IO.Path]::GetTempPath()) ('ui-decl-' + [Guid]::NewGuid().ToString('N') + '.json')
    try {
        & dotnet $toolDll $emitKind $configRoot $tempFile
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $tempFile)) {
            Write-Error "ui 声明编译失败（$target）：声明缺项/引用未声明区域（明细见上）"
            exit 1
        }

        $results += [pscustomobject]@{ Target = $target; Path = $destination; Text = [System.IO.File]::ReadAllText($tempFile) }
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
$summary = $results | ForEach-Object { $_.Target }
Write-Host ("[OK] UI 声明无漂移：{0}" -f ($summary -join ' + ')) -ForegroundColor Green
exit 0
