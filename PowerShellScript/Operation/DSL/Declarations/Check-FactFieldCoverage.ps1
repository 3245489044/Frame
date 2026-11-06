#requires -Version 7.0
<#
.SYNOPSIS
    事实字段覆盖守卫（UPC D-F4）：面字段必须与产品承诺全等；上下文事实必须有字段位。

.DESCRIPTION
    判据载体 = 声明文件静态结构（[Product] out / [Face] fields / read-paths 数值 / C# 常量）。
      FC1 面-产品闭合：face.product 必须存在。
      FC2 字段全等：face.fields 与 product.out **集合全等**（比 GD1 的 ⊆ 更严：字段集相等可复算）。
      FC3 事实位：上下文必需事实位 {empty_reason, synthetic, window_digest} 必须在某产品 out 中。
      FC4 数值同源：read-paths.taskContext.{maxMembers,memberWindowTake} == HistoryPortContextSpec 常量。
      FC5 负对照（-NegativeControl）：合成「面字段少一位」样本必须判红。
    只读仓库，零侧写。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

$rxProduct = '^\s*\[Product\]\s+(?<name>[A-Za-z0-9_.]+):.*\bout=(?<out>[A-Za-z0-9_,]+)'
$rxFace = '^\s*\[Face\]\s+(?<name>[A-Za-z0-9_.]+):.*\bproduct=(?<product>[A-Za-z0-9_.]+).*\bfields=(?<fields>[A-Za-z0-9_,]+)'

function Split-Fields([string]$raw) {
    return @($raw.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ } | Sort-Object -Unique)
}

function Read-Graph([string]$path) {
    $products = @{}
    $faces = [System.Collections.Generic.List[object]]::new()
    if (-not (Test-Path -LiteralPath $path)) { throw "声明面缺失：$path（判据不可判定）" }
    foreach ($line in [IO.File]::ReadAllLines($path)) {
        $p = [regex]::Match($line, $rxProduct)
        if ($p.Success) {
            $products[$p.Groups['name'].Value] = Split-Fields $p.Groups['out'].Value
            continue
        }
        $f = [regex]::Match($line, $rxFace)
        if ($f.Success) {
            $faces.Add([pscustomobject]@{
                Name    = $f.Groups['name'].Value
                Product = $f.Groups['product'].Value
                Fields  = Split-Fields $f.Groups['fields'].Value
            })
        }
    }
    return @{ Products = $products; Faces = $faces }
}

function Test-Coverage([hashtable]$Products, $Faces, [string[]]$RequiredFacts) {
    $violations = [System.Collections.Generic.List[string]]::new()
    if ($Products.Count -eq 0) { $violations.Add('[FC0] 未解析到任何 [Product]（判据空转）') }
    foreach ($face in $Faces) {
        if (-not $Products.ContainsKey($face.Product)) {
            $violations.Add("[FC1] 面 $($face.Name) 指向未声明产品 $($face.Product)")
            continue
        }
        $out = @($Products[$face.Product])
        $diff = @(Compare-Object -ReferenceObject $out -DifferenceObject @($face.Fields))
        if ($diff.Count -gt 0) {
            $violations.Add("[FC2] 面 $($face.Name) fields 与产品 $($face.Product) out 不全等（差异 $($diff.Count) 项）")
        }
    }
    $allOut = @($Products.Values | ForEach-Object { $_ } | Sort-Object -Unique)
    foreach ($fact in $RequiredFacts) {
        if ($allOut -notcontains $fact) {
            $violations.Add("[FC3] 事实位 $fact 未被任何产品 out 覆盖（事实无字段位 = 不可交付）")
        }
    }
    return $violations
}

$graphPath = Join-Path $Root 'Projects\Plug\Agent\Config\model\graph\graph.ushar'
$declared = Read-Graph $graphPath
$required = @('empty_reason', 'synthetic', 'window_digest')
$violations = Test-Coverage -Products $declared.Products -Faces $declared.Faces -RequiredFacts $required

# FC4 数值同源（read-paths ↔ C# 常量）
$readPaths = Join-Path $Root 'Projects\Plug\Agent\Config\nodes\read-paths.json'
$specPath = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Session\Projection\History\HistoryPortContextSpec.cs'
if ((Test-Path -LiteralPath $readPaths) -and (Test-Path -LiteralPath $specPath)) {
    $json = Get-Content -LiteralPath $readPaths -Raw | ConvertFrom-Json
    $context = $json.windowContext.taskContext
    $spec = Get-Content -LiteralPath $specPath -Raw
    $max = [regex]::Match($spec, 'MaxMembers\s*=\s*(?<n>\d+)')
    $take = [regex]::Match($spec, 'WindowTake\s*=\s*(?<n>\d+)')
    if (-not $max.Success -or -not $take.Success) {
        $violations.Add('[FC4] HistoryPortContextSpec 常量不可判定')
    } else {
        if ([int]$context.maxMembers -ne [int]$max.Groups['n'].Value) {
            $violations.Add("[FC4] maxMembers 声明=$($context.maxMembers) ≠ C#=$($max.Groups['n'].Value)")
        }
        if ([int]$context.memberWindowTake -ne [int]$take.Groups['n'].Value) {
            $violations.Add("[FC4] memberWindowTake 声明=$($context.memberWindowTake) ≠ C#=$($take.Groups['n'].Value)")
        }
    }
} else {
    $violations.Add('[FC4] 数值同源输入缺失（read-paths 或 HistoryPortContextSpec）')
}

if ($NegativeControl) {
    $sampleProducts = @{ 'p.ok' = @('a', 'b') }
    $sampleFaces = @([pscustomobject]@{ Name = 'f.bad'; Product = 'p.ok'; Fields = @('a') })
    $sampleViolations = Test-Coverage -Products $sampleProducts -Faces $sampleFaces -RequiredFacts @('a')
    if ($sampleViolations.Count -eq 0) { throw '负对照失败：面字段缺位样本未被判红（判据空转）' }
    Write-Host '[OK] 负对照通过：面字段缺位样本可判红。' -ForegroundColor Green
}

if ($Check) {
    Write-GateVerdict -Violations $violations -Check -Key 'FactFieldCoverage'
}

exit 0
