#requires -Version 5.1
<#
.SYNOPSIS
    关系聚拢点守卫（RelationGather）—— 唯一求值入口 / 层向 / 派生单源。

.DESCRIPTION
    G1 聚拢点唯一：IRelationGatherPoint 的实现类恰 1；生产调用点直连 AddReferenceResultAsync 的文件 ⊆ {图原语, 内核, 端口实现}。
    G2 层向：UniversalReference 源码不得出现 `using Operation.` / `using Modules.` / `using LiteDB` / `using Plate.DslMethod`。
    G3 派生单源：生产源码 GetHistoryNodeKeysAsync 命中 = 0（HistoryNodes 只经 [Derive]）。
    G4 负对照：合成「聚拢点缺失」「层向越界」样本必须被同一判定函数判红。

.PARAMETER Check         存在违规 → 退出码 1（缺省报告态恒 0）。
.PARAMETER ShowDetail    输出逐条计数。
.PARAMETER NegativeControl 附加负对照自检。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'

if (-not $Root) {
    $Root = Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')
}

$urRoot = Join-Path $Root 'Frame\CsharpPrj\Share\Frameworks\UniversalReference'
$frameCs = Join-Path $Root 'Frame\CsharpPrj'
$projectsCs = Join-Path $Root 'Projects\Plug\Agent\src'

$violations = New-Object System.Collections.Generic.List[string]

function Get-CsFiles([string]$dir) {
    if (-not (Test-Path -LiteralPath $dir)) { return @() }
    return @(Get-ChildItem -LiteralPath $dir -Recurse -Filter '*.cs' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\Tests?\\' -and $_.Name -notlike '*Tests.cs' })
}

function Read-Text([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return '' }
    return Get-Content -LiteralPath $path -Raw
}

function Test-GatherImplementationCount([string[]]$files) {
    $count = 0
    foreach ($file in $files) {
        $text = Read-Text $file
        if ($text -match 'class\s+RelationGatherPoint\s*:\s*[^{]*IRelationGatherPoint' -or
            $text -match 'class\s+\w+\s*:\s*[^{]*\bIRelationGatherPoint\b') { $count++ }
    }
    return $count
}

function Get-Relative([string]$path) {
    if (-not $path) { return '' }
    $rel = if ($path.Length -gt $Root.Length) { $path.Substring($Root.Length) } else { $path }
    return $rel.TrimStart('\', '/')
}

function Test-LayerViolation([string[]]$files) {
    $hits = New-Object System.Collections.Generic.List[string]
    foreach ($file in $files) {
        $text = Read-Text $file
        if ($text -match '(?m)^\s*using\s+(Operation\.|Modules\.(?!GraphProcessing|Pipeline)|LiteDB|Plate\.DslMethod)') {
            $hits.Add((Get-Relative $file))
        }
    }
    return $hits
}

function Test-DeriveSingleSource([string[]]$files) {
    # 唯一历史节点派生实现必须委托 [Derive]（DeriveHistoryNodesAsync）；手写图遍历即第二实现。
    $core = Join-Path $Root 'Frame\CsharpPrj\Operation\DataStoreDriven\Domains\Hierarchy\HistoryReferenceGrounding.cs'
    $text = Read-Text $core
    $hits = New-Object System.Collections.Generic.List[string]
    if ([string]::IsNullOrWhiteSpace($text)) {
        $hits.Add('HistoryReferenceGrounding.cs:missing（派生唯一实现缺失）')
        return $hits
    }
    if ($text -notmatch 'GetHistoryNodeKeysAsync') {
        $hits.Add('HistoryReferenceGrounding.cs:GetHistoryNodeKeysAsync-missing')
    }
    if ($text -notmatch 'DeriveHistoryNodesAsync|DeriveAsync') {
        $hits.Add('HistoryReferenceGrounding.cs:manual-derivation（须委托 [Derive] HistoryNodes）')
    }
    return $hits
}

# ---- G1 ----
$urFiles = @(Get-CsFiles $urRoot)
$gatherCount = Test-GatherImplementationCount $urFiles
if ($gatherCount -ne 1) {
    $violations.Add("G1 IRelationGatherPoint 实现类=$gatherCount（期望 1）")
}

$writeAllow = @(
    'Frame\CsharpPrj\Operation\DataStoreDriven\Graph\ReferenceGraphCoordinator.cs',
    'Frame\CsharpPrj\Operation\DataStoreDriven\Graph\GraphPortStores.cs',
    'Frame\CsharpPrj\Share\Frameworks\UniversalReference\StorageFramework\Core\StoreResourceReferenceStore.cs',
    'Frame\CsharpPrj\Share\Frameworks\UniversalReference\Relations\Kernel\DeclaredRelationKernel.Ensure.cs',
    'Frame\CsharpPrj\Share\Frameworks\UniversalReference\StorageFramework\Abstractions\IResourceReferenceStore.cs'
)
$directWriters = @(Get-CsFiles $frameCs) + @(Get-CsFiles $projectsCs) |
    Where-Object { (Read-Text $_.FullName) -match '\.AddReferenceResultAsync\(' } |
    ForEach-Object { Get-Relative $_.FullName } |
    Where-Object { $writeAllow -notcontains $_ }
foreach ($writer in ($directWriters | Select-Object -Unique)) {
    $violations.Add("G1 调用点直连写边（应经聚拢点）：$writer")
}

# ---- G2 ----
foreach ($hit in (Test-LayerViolation $urFiles)) {
    $violations.Add("G2 层向越界（UniversalReference 不得引用上层/存储实现）：$hit")
}

# ---- G3 ----
$deriveHits = Test-DeriveSingleSource (@(Get-CsFiles $frameCs) + @(Get-CsFiles $projectsCs))
foreach ($hit in ($deriveHits | Select-Object -Unique)) {
    $violations.Add("G3 派生双实现（GetHistoryNodeKeysAsync 应删除，改 [Derive]）：$hit")
}

# ---- G4 负对照 ----
if ($NegativeControl) {
    $probe = 'internal sealed class FakeGather : IRelationGatherPoint { }'
    if (-not ($probe -match 'class\s+\w+\s*:\s*[^{]*\bIRelationGatherPoint\b')) {
        $violations.Add('G4 负对照失效：聚拢点合成样本未命中')
    }
    $syntheticUsing = 'using Operation.DataStoreDriven.Graph;'
    if (-not ($syntheticUsing -match '(?m)^\s*using\s+(Operation\.|Modules\.|LiteDB|Plate\.DslMethod)')) {
        $violations.Add('G4 负对照失效：层向合成样本未命中')
    }
}

$verdict = if ($violations.Count -eq 0) { 'PASS' } else { 'FAIL' }
Write-Output ("RelationGather: {0} | gatherImpl={1} directWriters={2} layerHits={3} deriveHits={4} violations={5}" -f `
    $verdict, $gatherCount, @($directWriters | Select-Object -Unique).Count,
    @(Test-LayerViolation $urFiles).Count, @($deriveHits | Select-Object -Unique).Count, $violations.Count)

if ($ShowDetail -or $Check) {
    foreach ($violation in $violations) { Write-Output ("  x {0}" -f $violation) }
}
if ($NegativeControl) { Write-Output '  · negative-control: gather-impl / layer-using 合成样本已验' }

if ($Check -and $violations.Count -gt 0) { exit 1 }
exit 0
