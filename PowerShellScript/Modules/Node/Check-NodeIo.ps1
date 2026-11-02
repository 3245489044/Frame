#requires -Version 5.1
<#
.SYNOPSIS
    Share.NodeIo 节点文档内核门禁（读写同源 / loc 控制反转 / 零语言语义）。
.DESCRIPTION
    判据（结构事实；不读执行者叙述）：
      A 语言/实现隔离：Share.NodeIo 内 `Usharp` / `INodeStore` / `Sqlite` / `using Operation.` / `using Strategy.` / `using Agent` 命中 = 0。
      B 零领域分支：`switch (domain` / `switch (domainKey` / `switch (family` 命中 = 0。
      C 解析单源：`PlacementResolver.Resolve(` 只允许出现在 NodeScopeResolver*.cs。
      D 载体调用收口：`carrier.UpsertAsync` / `carrier.ReadAsync` / `carrier.DeleteAsync` 只允许出现在 NodeDocumentPort*.cs / NodeStreamSession.cs。
      E 负对照：合成违规样本必须被同一判据函数命中（防假绿）。
.PARAMETER Root
    仓库根（缺省由脚本位置上溯四级：Frame\PowerShellScript\Modules\Node → 仓库根）。
.PARAMETER Check
    校验模式：存在违规 → 退出码 1（缺省 = 报告模式）。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$projectDir = Join-Path $Root 'Frame\CsharpPrj\Share\Frameworks\NodeIo'
$violations = [System.Collections.Generic.List[string]]::new()

function Get-Source([string]$text) {
    return [pscustomobject]@{ Path = '<synthetic>'; Text = $text }
}

function Test-Forbidden([string]$text, [string]$pattern, [string]$code, [string]$why) {
    if ($text -match $pattern) { return "[$code] $why" }
    return $null
}

function Test-CarrierCallSite([string]$file, [string]$text) {
    $allowed = @('NodeDocumentPort.cs', 'NodeDocumentPort.Apply.cs', 'NodeDocumentPort.Patch.cs',
                 'NodeDocumentPort.Delete.cs', 'NodeStreamSession.cs')
    $leaf = Split-Path $file -Leaf
    if ($text -match 'carrier\.(UpsertAsync|ReadAsync|DeleteAsync|EnumerateKeysAsync)\s*\(') {
        if ($allowed -notcontains $leaf) {
            return "[D] $file 直接调用载体原语（唯一收口 = NodeDocumentPort*.cs / NodeStreamSession.cs）"
        }
    }
    return $null
}

if (-not (Test-Path $projectDir)) {
    $violations.Add("[A] 缺少工程目录：$projectDir")
}
else {
    $files = @(Get-ChildItem -LiteralPath $projectDir -Recurse -File -Filter *.cs |
        Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' })
    foreach ($file in $files) {
        $text = Get-Content -LiteralPath $file.FullName -Raw
        $leaf = $file.Name
        foreach ($pattern in @('Usharp', 'INodeStore', 'Sqlite', 'using\s+Operation\.', 'using\s+Strategy\.', 'using\s+Agent')) {
            $hit = Test-Forbidden $text $pattern 'A' "命中 '$pattern'（内核须语言/实现无关）"
            if ($hit) { $violations.Add("$hit @ $leaf") }
        }

        $hit = Test-Forbidden $text 'switch\s*\(\s*(domain|domainKey|family)' 'B' '出现领域分支 switch（须由 loc/声明驱动）'
        if ($hit) { $violations.Add("$hit @ $leaf") }

        if ($text -match 'PlacementResolver\.Resolve\s*\(' -and $leaf -notlike 'NodeScopeResolver*.cs') {
            $violations.Add("[C] $leaf 直接调用位置反解（唯一解析点 = NodeScopeResolver）")
        }

        $hit = Test-CarrierCallSite $leaf $text
        if ($hit) { $violations.Add($hit) }
    }
}

# ---------------- E 负对照：合成违规必须被同一判据命中 ----------------
$syntheticForbidden = Get-Source 'using Usharp.Semantics;'
$syntheticDomainSwitch = Get-Source 'switch (domain) { }'
$syntheticCarrier = Get-Source 'await carrier.UpsertAsync(key, json);'
$negativeMissed = @()
if (-not (Test-Forbidden $syntheticForbidden.Text 'Usharp' 'A' 'x')) { $negativeMissed += 'A' }
if (-not (Test-Forbidden $syntheticDomainSwitch.Text 'switch\s*\(\s*(domain|domainKey|family)' 'B' 'x')) { $negativeMissed += 'B' }
if (-not (Test-CarrierCallSite 'Allowed.cs' $syntheticCarrier.Text)) { $negativeMissed += 'D' }
if ($negativeMissed.Count -gt 0) {
    $violations.Add("[E] 负对照未被命中：$($negativeMissed -join ',')（门禁判据失效）")
}

if ($violations.Count -eq 0) {
    Write-Host 'OK: Share.NodeIo 读写同源 / loc 控制反转 / 零语言语义。' -ForegroundColor Green
    exit 0
}

$violations | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
if ($Check) { exit 1 }
exit 0
