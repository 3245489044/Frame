#requires -Version 5.1
<#
.SYNOPSIS
    关系写面守卫（WriteSurface）—— 清单 ↔ 声明 ↔ 调用点 三面对账。

.DESCRIPTION
    C1 清单↔声明：migrated 条目的 Operation 必须存在于 relation-ops.ushar（Edge/Intent 一致）。
    C2 调用点覆盖：migrated 条目所在文件仍含旧写原语 ⇒ 红（状态谎报）。
    C3 棘轮：pending 计数 ≤ 基线（RelationSurface.baseline.json）。
    C4 写入口唯一：生产文件出现 AddReferenceAsync( 调用 ⇒ 仅白名单（内核/图原语）允许。
    C5 负对照：合成「migrated 仍直写」必报；合成「pending」不报。

.PARAMETER Check
    校验模式：存在违规 → 退出码 1。缺省为报告态（恒 0，仅输出计数）。

.PARAMETER ShowDetail
    输出逐条明细（违规与 pending 清单）。

.PARAMETER NegativeControl
    附加负对照自检输出（判据空转检测）。
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

$manifestRelative = 'Frame\PowerShellScript\Operation\DSL\Node\WriteSurface.psd1'
$baselineRelative = 'Frame\PowerShellScript\Operation\DSL\Node\WriteSurface.baseline.json'
$oldWritePattern = 'AddReferenceAsync\(|AttachEdgeAsync\(|AddRelationAsync\('

$writeEntryWhitelist = @(
    'Frame/CsharpPrj/Operation/DataStoreDriven/Relations/DeclaredRelationKernel.cs',
    'Frame/CsharpPrj/Operation/DataStoreDriven/Relations/Rebuild/RelationRebuildEngine.cs',
    'Frame/CsharpPrj/Operation/DataStoreDriven/Core/Graph/GraphEdgeWriter.cs',
    'Frame/CsharpPrj/Operation/DataStoreDriven/Graph/ReferenceGraphCoordinator.cs',
    # 数据流边专用写面（归属边已走内核；未声明边不走 [Relation]）
    'Projects/Plug/Agent/src/Agent.Core/Engine/TaskPool/TaskGraphService.cs',
    'Frame/CsharpPrj/Operation/LoopFoldMethod/Reference/Gateways/NodeRelationGateway.cs',
    'Frame/CsharpPrj/Operation/DataStoreDriven/Domains/Hierarchy/EndpointKeyRekey.cs',
    # UI 经 NodeRelationGateway 转发（同名 API 文本命中，非直写图）
    'Projects/Plug/Agent/src/Agent.Ui/Core/Functions/Data/AgentUiLibraryData.cs'
)

$violations = New-Object System.Collections.Generic.List[string]
$details = New-Object System.Collections.Generic.List[string]

function Read-RelativeText([string]$relative) {
    $path = Join-Path $Root $relative
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    return Get-Content -LiteralPath $path -Raw
}

function Get-DeclaredOperations([string]$text) {
    $map = @{}
    if ([string]::IsNullOrWhiteSpace($text)) { return $map }
    foreach ($line in ($text -split "`n")) {
        if ($line -notmatch '^\s*\[Relation\]\s+([A-Za-z0-9_\-]+)\s*:') { continue }
        $id = $Matches[1]
        $fields = @{}
        foreach ($m in [regex]::Matches($line, '([A-Za-z_]+)\s*=\s*([^|]+)')) {
            $fields[$m.Groups[1].Value] = $m.Groups[2].Value.Trim()
        }
        $map[$id] = [pscustomobject]@{ Id = $id; Edge = $fields['edge']; Intent = $fields['intent'] }
    }
    return $map
}

function Test-OldWritePresent([string]$content) {
    if ([string]::IsNullOrWhiteSpace($content)) { return $false }
    return [regex]::IsMatch($content, $oldWritePattern)
}

# ---- 清单 / 基线 ----
$manifestPath = Join-Path $Root $manifestRelative
if (-not (Test-Path -LiteralPath $manifestPath)) {
    $violations.Add("C1 清单缺失：$manifestRelative")
    $entries = @()
}
else {
    $manifest = Import-PowerShellDataFile -LiteralPath $manifestPath
    $entries = @($manifest.Entries)
}

$declared = Get-DeclaredOperations (Read-RelativeText 'Projects\Plug\Agent\Config\relations\relation-ops.ushar')
if ($declared.Count -eq 0) { $violations.Add('C1 relation-ops.ushar 缺失或无 [Relation] 声明') }

$baselinePath = Join-Path $Root $baselineRelative
if (-not (Test-Path -LiteralPath $baselinePath)) {
    $violations.Add("C3 基线缺失：$baselineRelative")
    $baselinePending = -1
}
else {
    $baseline = Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json
    $baselinePending = [int]$baseline.pending
}

# ---- C1 清单 ↔ 声明 ----
foreach ($entry in $entries) {
    if ($entry.Status -ne 'migrated') { continue }
    if ([string]::IsNullOrWhiteSpace($entry.Operation) -or $entry.Operation -eq '*') { continue }
    if (-not $declared.ContainsKey($entry.Operation)) {
        $violations.Add("C1 $($entry.Id) Operation=$($entry.Operation) 未在 relation-ops.ushar 声明")
        continue
    }
    $op = $declared[$entry.Operation]
    if ($entry.Edge -ne '*' -and $op.Edge -ne $entry.Edge) {
        $violations.Add("C1 $($entry.Id) Edge 漂移：清单=$($entry.Edge) 声明=$($op.Edge)")
    }
    if ($entry.Intent -ne '*' -and $op.Intent -ne $entry.Intent) {
        $violations.Add("C1 $($entry.Id) Intent 漂移：清单=$($entry.Intent) 声明=$($op.Intent)")
    }
}

# ---- C2 状态谎报（migrated 仍含旧写原语；数据流残留须与 C4 白名单双向一致）----
foreach ($entry in $entries) {
    if ($entry.Status -ne 'migrated') { continue }
    $content = Read-RelativeText ($entry.File -replace '/', '\')
    if ($null -eq $content) {
        $violations.Add("C2 $($entry.Id) File 不存在：$($entry.File)")
        continue
    }

    if ($entry.ContainsKey('DataflowResidual') -and [bool]$entry.DataflowResidual) {
        $normalized = $entry.File -replace '\\', '/'
        if ($writeEntryWhitelist -notcontains $normalized) {
            $violations.Add("C2 $($entry.Id) 声明 DataflowResidual 但文件不在 C4 白名单（数据流写面须显式登记）")
        }
        continue
    }

    if ($entry.ContainsKey('ForwardedVia')) {
        $normalized = $entry.File -replace '\\', '/'
        if ($writeEntryWhitelist -notcontains $normalized) {
            $violations.Add("C2 $($entry.Id) 声明 ForwardedVia 但文件不在 C4 白名单（转发面须显式登记）")
        }
        continue
    }

    if (Test-OldWritePresent $content) {
        $violations.Add("C2 $($entry.Id) Status=migrated 但文件仍含旧写原语：$($entry.File)")
    }
}

# ---- C3 棘轮 ----
$pending = @($entries | Where-Object { $_.Status -eq 'pending' })

# ---- C3 棘轮（pending 只降不升）----
$pending = @($entries | Where-Object { $_.Status -eq 'pending' })
if ($baselinePending -ge 0 -and $pending.Count -gt $baselinePending) {
    $violations.Add("C3 pending 上升：实测=$($pending.Count) 基线=$baselinePending")
}

# ---- C4 写入口唯一（生产文件出现 AddReferenceAsync( ⇒ 仅白名单允许）----
$productionFiles = @(
    $entries |
        ForEach-Object { ($_.File -replace '\\', '/') } |
        Where-Object { $_ -notmatch '(?i)tests?/' } |
        Select-Object -Unique
)
foreach ($file in $productionFiles) {
    if ($writeEntryWhitelist -contains $file) { continue }
    $content = Read-RelativeText ($file -replace '/', '\')
    if ($null -eq $content) { continue }
    if ([regex]::IsMatch($content, 'AddReferenceAsync\(')) {
        $violations.Add("C4 绕过内核直写 AddReferenceAsync：$file")
    }
}

# ---- C6 operation/edge 字面量单源（生产写面文件只允许经常量/声明面）----
$literalPattern = '"(mount_[a-z_]+|unmount_[a-z_]+|cross_db_link|history_owns_task|session_owns|task_owns_task)"'
$literalAllowlist = @(
    'Frame/CsharpPrj/Operation/DataStoreDriven/Relations/RelationSurfaces.cs'
)
foreach ($file in $productionFiles) {
    if ($literalAllowlist -contains $file) { continue }
    $content = Read-RelativeText ($file -replace '/', '\')
    if ($null -eq $content) { continue }
    if ([regex]::IsMatch($content, $literalPattern)) {
        $violations.Add("C6 operation/edge 字面量越界（须经 RelationOperationIds / RelationEdgeNames）：$file")
    }
}

# ---- C5 负对照（判据空转检测）----
function Test-MigratedEntry([string]$status, [string]$content) {
    if ($status -ne 'migrated') { return $false }
    return Test-OldWritePresent $content
}

$syntheticDirect = 'await _resources.AddReferenceAsync(source, target, refType, strength, ct);'
$syntheticClean = 'await _relations.EnsureNodeTaskAsync(taskKey, nodeKey, workflowId, ct);'

if (-not (Test-MigratedEntry -status 'migrated' -content $syntheticDirect)) {
    $violations.Add('C5 负对照失效：migrated + 直写未判红')
}
if (Test-MigratedEntry -status 'pending' -content $syntheticDirect) {
    $violations.Add('C5 负对照失效：pending 被误报')
}
if (Test-MigratedEntry -status 'migrated' -content $syntheticClean) {
    $violations.Add('C5 负对照失效：干净文件被误报')
}

$syntheticCallLiteral = 'await Ports.Commands.EnsureAsync("mount_node_task", request, ct);'
if (-not [regex]::IsMatch($syntheticCallLiteral, $literalPattern)) {
    $violations.Add('C6 负对照失效：调用点 operation 字面量未命中')
}

# ---- 输出 ----
$verdict = if ($violations.Count -eq 0) { 'PASS' } else { 'FAIL' }
Write-Output ("WriteSurface: {0} | entries={1} pending={2} violations={3} baselinePending={4}" -f `
    $verdict, $entries.Count, $pending.Count, $violations.Count, $baselinePending)

if ($ShowDetail -or $Check) {
    foreach ($entry in $pending) { Write-Output ("  · pending {0} [{1}] {2}" -f $entry.Id, $entry.Wave, $entry.File) }
    foreach ($violation in $violations) { Write-Output ("  x {0}" -f $violation) }
}

if ($NegativeControl) {
    Write-Output '  · negative-control: synthetic migrated+direct/write verified'
}

if ($Check -and $violations.Count -gt 0) { exit 1 }
exit 0
