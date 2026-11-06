#requires -Version 7.0
<#
.SYNOPSIS
    三出口同形守卫（UPC D-F5）：UI/Prompt/Diagnostics 只读同一投影，禁各自裁剪字段。

.DESCRIPTION
    判据载体 = 源码静态结构（实现数/投影调用数/类体内裁剪语法）。
      PS1 实现数：IPortContextConsumer 实现 = 3（ui/prompt/diagnostics）。
      PS2 同源：3 个实现均调用 PortContextProjection.Project(context)（唯一投影）。
      PS3 无裁剪：任一 Consumer 类体内不得出现 .Where(/.Select(/.Skip( 等字段裁剪。
      PS4 负对照（-NegativeControl）：合成「单侧裁剪」样本必须判红。
    只读仓库，零侧写。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'

# 根定位（深度无关）：GateRuntime → DirectoryMap 以「Frame\ + Projects\」marker 上溯（迁 Environment 后不依赖脚本深度）
$__repoRoot = $PSScriptRoot
while ($__repoRoot -and -not ((Test-Path -LiteralPath (Join-Path $__repoRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $__repoRoot 'Projects')))) { $__repoRoot = Split-Path -Parent $__repoRoot }
if (-not $__repoRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $__repoRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
if ([string]::IsNullOrWhiteSpace($Root)) {
    $ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Key 'PortContextShapeEquality'
    $Root = $ctx.Root
}

$consumerPath = Join-Path (Get-MappedPath -Key 'Frame') 'CsharpPrj\Share\Frameworks\PortContext\PortContextConsumers.cs'
$rxCut = '\.(Where|Select|Skip|Take)\s*\('
$violations = [System.Collections.Generic.List[string]]::new()

if (-not (Test-Path -LiteralPath $consumerPath)) {
    $violations.Add("[PS0] 出口契约文件缺失：$consumerPath（判据不可判定）")
} else {
    $text = [IO.File]::ReadAllText($consumerPath)

    $impl = [regex]::Matches($text, ':\s*IPortContextConsumer').Count
    if ($impl -ne 3) { $violations.Add("[PS1] IPortContextConsumer 实现数=$impl（须 =3）") }

    $projections = [regex]::Matches($text, 'PortContextProjection\.Project\(context\)').Count
    if ($projections -ne 3) { $violations.Add("[PS2] 同源投影调用数=$projections（须 =3：三出口同一投影）") }

    # 类体切片：只检查三个 Consumer 类体（Projection 内部允许 Select）。
    foreach ($name in @('PortContextUiConsumer', 'PortContextPromptConsumer', 'PortContextDiagnosticsConsumer')) {
        $m = [regex]::Match($text, "class\s+$name\b(?<body>[\s\S]*?)\n}")
        if (-not $m.Success) { $violations.Add("[PS3] 未定位 Consumer 类体：$name"); continue }
        if ([regex]::IsMatch($m.Groups['body'].Value, $rxCut)) {
            $violations.Add("[PS3] $name 内含字段裁剪（破坏字段集相等；差异须登记 shape-exception）")
        }
    }
}

if ($NegativeControl) {
    $sample = 'public sealed class X : IPortContextConsumer { public PortContextViewModel Project(PortContext c) => PortContextProjection.Project(c) is var v && v.Fields.Where(f => true).Any() ? v : v; }'
    if (-not [regex]::IsMatch($sample, $rxCut)) { throw '负对照失败：裁剪样本未被识别（判据空转）' }
    $positive = 'public sealed class X : IPortContextConsumer { public PortContextViewModel Project(PortContext c) => PortContextProjection.Project(c); }'
    if ([regex]::IsMatch($positive.Substring($positive.IndexOf(':')), $rxCut)) { throw '正对照失败：同源样本被误判为裁剪' }
    Write-Host '[OK] 负对照通过：单侧裁剪样本可判红。' -ForegroundColor Green
}

if ($Check) {
    Write-GateVerdict -Violations $violations -Check -Key 'PortContextShapeEquality'
}

exit 0
