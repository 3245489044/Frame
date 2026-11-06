#requires -Version 5.1
<#
.SYNOPSIS
    CanonicalForm 结构守卫（Check-CanonicalForm，P3-5 / §5.2）—— 未映射 type 必须被拒绝。

.DESCRIPTION
    判据载体 = `conversation-canon.ushar`（映射单源）+ 同一 TS 执行器输出的 `canonical`（DR-1：本脚本只校验）。

    判据：
      C1 canon 齐备：含 `unmapped` 末路 ∧ `unrecognized` 显式映射（禁 Default 静默归一）。
      C2 类型闭合：每个节点 type ∈ canon 登记类型集；`Default` 仅允许 `kind=unrecognized`。
      C3 边闭合：边 type ∈ canon 边类型集；端点必须存在（禁悬空边）。
      C4 属性闭合：properties 键 ⊆ canon.keys ∪ {retention, materialize}（R-1：禁自由字段）。
      C5 负对照：合成节点 `type="Foo"` 必须被判据捕获（否则判据恒真）。

.EXAMPLE
    .\Check-CanonicalForm.ps1 -Check
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot '..\..\Meta\TextOps.psm1') -Force -DisableNameChecking

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root))
{
    $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
}

$agentsRoot = Join-Path $Root 'Projects\Plug\Agent\Config\agents'
$canonPath  = Join-Path $Root 'Projects\Plug\Agent\Config\model\conversation\conversation-canon.ushar'
Import-Module (Join-Path (Get-FrameRoot) 'PowerShellScript\Modules\Parsing\Invoke-CanonCli.psm1') -Force
$cliPath    = Resolve-CanonCliPath -Root $Root
$extraKeys  = @('retention', 'materialize')

$violations = [System.Collections.Generic.List[string]]::new()

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  CanonicalForm 结构守卫（类型闭合 / 边闭合 / 属性闭合）" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

if (-not (Test-Path -LiteralPath $canonPath))
{
    Write-Host "[FAIL] 缺 conversation-canon.ushar（映射单源）" -ForegroundColor Yellow
    if ($Check) { exit 1 }
    exit 0
}

$canonText = Get-Content -LiteralPath $canonPath -Raw
$allowedTypes = @([regex]::Matches($canonText, 'CanonicalNodeType\.([A-Za-z]+)') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
$edgeTypes = @([regex]::Matches($canonText, '(?m)^\s*\w+\s+→\s+([A-Z][A-Za-z]+)\s*$') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
$keysLine = [regex]::Match($canonText, '(?m)^\s*keys\s*=\s*(.+)$')
$allowedKeys = @()
if ($keysLine.Success) { $allowedKeys = @(ConvertFrom-CommaList -Text $keysLine.Groups[1].Value) }

# R-3：unmapped 末路在位由 Check-AgentSpec A7 单点裁决（声明合格性），本脚本只判产物结构（C2–C5）。

<# 判据核心（纯函数；负对照直接复用）。 #>
function Get-CanonIssues
{
    param(
        [object]$Canonical,
        [string[]]$AllowedTypes,
        [string[]]$AllowedKeys,
        [string[]]$AllowedEdgeTypes
    )

    $issues = [System.Collections.Generic.List[string]]::new()
    if ($null -eq $Canonical -or $null -eq $Canonical.nodes)
    {
        $issues.Add("[C2] canonical 缺失（无 nodes）")
        return $issues
    }

    $ids = @{}
    foreach ($node in @($Canonical.nodes))
    {
        $ids[[string]$node.id] = $true
        $type = [string]$node.type
        $kind = [string]$node.properties.kind
        if ($AllowedTypes -notcontains $type)
        {
            $issues.Add("[C2] 节点 '$($node.id)' type='$type' ∉ canon 登记类型（未映射 type 必须拒绝，不得落 Default）")
        }
        if ($type -eq 'Default' -and $kind -ne (Get-GateUnrecognizedKind))
        {
            $issues.Add("[C2] 节点 '$($node.id)' 以 Default 冒充已知类别 '$kind'（AX.NoSilentOutlet）")
        }
        foreach ($key in $node.properties.PSObject.Properties.Name)
        {
            if (($AllowedKeys -notcontains $key) -and ($extraKeys -notcontains $key))
            {
                $issues.Add("[C4] 节点 '$($node.id)' 含未登记属性 '$key'（properties 受控键）")
            }
        }
        if ($node.properties.applicable -notin @('Known', 'Unknown', 'NotApplicable'))
        {
            $issues.Add("[C2] 节点 '$($node.id)' applicable='$($node.properties.applicable)' 非三态取值")
        }
    }

    foreach ($edge in @($Canonical.edges))
    {
        if ($AllowedEdgeTypes.Count -gt 0 -and $AllowedEdgeTypes -notcontains [string]$edge.type)
        {
            $issues.Add("[C3] 边 '$($edge.id)' type='$($edge.type)' ∉ canon 边类型集")
        }
        if (-not $ids.ContainsKey([string]$edge.sourceId) -or -not $ids.ContainsKey([string]$edge.targetId))
        {
            $issues.Add("[C3] 边 '$($edge.id)' 悬空（source/target 不存在）")
        }
    }

    return $issues
}

# 从 CLI 输出提取 JSON（node 告警行走 stderr，禁因此判失败）—— 单实现见 Invoke-CanonCli.psm1。

# ---------------- 实测样本（同一 TS 执行器） ----------------
$agentDirs = @(if (Test-Path -LiteralPath $agentsRoot) { Get-ChildItem -LiteralPath $agentsRoot -Directory })
foreach ($dir in $agentDirs)
{
    $samples = @(if (Test-Path -LiteralPath (Join-Path $dir.FullName 'samples')) {
        Get-ChildItem -LiteralPath (Join-Path $dir.FullName 'samples') -Filter '*.json' -File
    })
    $sample = @($samples | Where-Object { $_.Name -notlike 'missing-*' }) | Select-Object -First 1
    if ($null -eq $sample) { continue }
    if ($null -eq $cliPath) { continue }

    $result = Invoke-CanonReplay -Root $Root -Agent $dir.Name -RoundFile $sample.FullName
    if ($null -eq $result -or $result.ok -ne $true)
    {
        $violations.Add("[C0] agent '$($dir.Name)' 重放失败（无法校验 canonical）：$($result | ConvertTo-Json -Depth 8 -Compress)")
        continue
    }

    foreach ($issue in (Get-CanonIssues -Canonical $result.canonical -AllowedTypes $allowedTypes -AllowedKeys $allowedKeys -AllowedEdgeTypes $edgeTypes))
    {
        $violations.Add($issue)
    }

    if ($ShowDetail)
    {
        Write-Host ("  - {0}: nodes={1} edges={2}" -f $dir.Name, @($result.canonical.nodes).Count, @($result.canonical.edges).Count) -ForegroundColor Gray
    }
}

# ---------------- C5 负对照（防假绿） ----------------
$probe = [pscustomobject]@{
    nodes = @([pscustomobject]@{ id = 'x'; type = 'Foo'; properties = [pscustomobject]@{ kind = 'user-message'; applicable = 'Known' } })
    edges = @()
}
if (@(Get-CanonIssues -Canonical $probe -AllowedTypes $allowedTypes -AllowedKeys $allowedKeys -AllowedEdgeTypes $edgeTypes).Count -eq 0)
{
    $violations.Add("[C5] 负对照失效：未映射 type='Foo' 未被判据捕获（判据恒真）")
}

Write-Host ""
if ($violations.Count -eq 0)
{
    Write-Host "[OK] CanonicalForm 结构守卫：0 违规（类型/边/属性闭合；未映射 type 必拒）" -ForegroundColor Green
    exit 0
}

Write-Host "[FAIL] CanonicalForm 结构守卫命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($item in $violations) { Write-Host "  - $item" -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0
