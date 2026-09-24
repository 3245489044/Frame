# Check-AgentLayerBoundary.ps1
# Agent 运行层 × 数据层 边界门禁（对齐 Frame\DataCenter\Doc\DataCenter-Agent运行时数据接入-设计与实施.md
#   §2.1 分层与职责、§2.3 根持有什么、§3.1 四条接入通道；与 Check-DataCenterGenericity.ps1 (K9/G1/G2)、
#   Check-AgentRuntimeSlots.ps1 (K10)、Check-SingleWriter.ps1 (D 类真源写门) 互补，不重复其判据）。
#
# 三条不变式：
#   [INV-L1] 运行层不得直引数据实现命名空间（数据访问必须经 DataCenter 标准端口）。
#   [INV-L2] 运行层不得自造数据根 / 不得直引引擎存储设施（构造点归数据面与宿主装配层）。
#   [INV-L3] 单一来源：运行态受控词定义点、任务快照写入口、节点写入口，各自计数只降不升。
# 运行层扫描面 = Agent.Core 的 Loops（循环与决策）/ Composition（运行组合根）/ RunState（运行态）。
#
# 模式：
#   - 报告模式（默认）：输出三类命中清单，不设退出码（迁移期看板）。
#   - 校验模式（-Check）：任一计数高于脚本内冻结基线 $frozen → 退出码 1（CI 门禁，防层间回渗）。
#   - 基线模式（-Baseline）：打印当前计数，供收敛后下移 $frozen。
#
# CI 集成：返回码 0 = 未回渗；非 0 = 层边界回归。

[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$Baseline,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    # 脚本位于 Frame\PowerShellScript\Operation\DSL\，上溯四级即仓库根
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

# 冻结基线（只降不升）：收敛后经 -Baseline 取值下移本表。
$frozen = [ordered]@{
    'INV-L1' = 36   # 运行层直引数据实现命名空间（Agent.Core: Loops/Composition/RunState）；死 using 已摘 2 条
    'INV-L2' = 3    # 运行层自造数据根 / 引擎存储设施（引擎单元构造待经 DataCenter.Hosting 端口化）
    'INV-L3' = 16   # 单一来源计数（RunStateStage 定义 1 + NodeRunState 定义 0 + ur_tasks 写入口 6 + 节点写入口 9）
}

# ── 运行层扫描面 ──
$runLayerDirs = @(
    (Join-Path $Root 'Projects\Plug\Agent\src\Agent.Core\Loops'),
    (Join-Path $Root 'Projects\Plug\Agent\src\Agent.Core\Composition'),
    (Join-Path $Root 'Projects\Plug\Agent\src\Agent.Core\RunState')
)

$runLayerFiles = @()
foreach ($dir in $runLayerDirs) {
    if (-not (Test-Path $dir)) { continue }
    $runLayerFiles += Get-ChildItem -Path $dir -Recurse -File -Filter *.cs -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' -and $_.FullName -notlike '*\_deprecated\*' }
}

if ($runLayerFiles.Count -eq 0) {
    Write-Error "未找到运行层扫描面（Agent.Core\Loops|Composition|RunState）：$($runLayerDirs -join ', ')"
    exit 2
}

# ── INV-L1：运行层不得直引数据实现命名空间 ──
$dataImplUsings = @(
    'using Operation.DataStoreDriven',
    'using Operation.StateBlock',
    'using Share.StateManagement',
    'using Modules.Node',
    'using UniversalWorkflowFramework.StorageFramework',
    'using Modules.InternalStorage'
)
$invL1 = @()
foreach ($pattern in $dataImplUsings) {
    $invL1 += $runLayerFiles | Select-String -Pattern ([regex]::Escape($pattern)) -ErrorAction SilentlyContinue
}

# ── INV-L2：运行层不得自造数据根 / 直引引擎存储设施 ──
$rootCreationPatterns = @('new StateCenter\(', 'new StorageCell\(', 'new Sqlite')
$invL2 = @()
foreach ($pattern in $rootCreationPatterns) {
    $invL2 += $runLayerFiles | Select-String -Pattern $pattern -ErrorAction SilentlyContinue
}

# ── INV-L3：单一来源计数（定义点 / 写入口） ──
$allCs = Get-ChildItem -Path $Root -Recurse -File -Filter *.cs -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' -and $_.FullName -notlike '*\_deprecated\*' }

$runStateStageDefs = $allCs | Select-String -Pattern '(enum|class)\s+RunStateStage\b' -ErrorAction SilentlyContinue
$nodeRunStateDefs = $allCs | Select-String -Pattern 'class\s+NodeRunState\b' -ErrorAction SilentlyContinue
$taskSnapshotWriters = $allCs |
    Where-Object { $_.FullName -match '\\Projects\\Plug\\Agent\\src\\' } |
    Select-String -Pattern '\.UpdatePayloadAsync\(' -ErrorAction SilentlyContinue
$nodeWriteEntrypoints = $allCs |
    Where-Object { $_.FullName -match '\\Operation\\HierarchyMethod\\Nodes\\' } |
    Select-String -Pattern '\.WriteOutputAsync\(|_service\.PersistAsync\(|PersistRoundAsync\(|RunWriteAsync\(' -ErrorAction SilentlyContinue

$invL3Count = $runStateStageDefs.Count + $nodeRunStateDefs.Count +
    $taskSnapshotWriters.Count + $nodeWriteEntrypoints.Count

$counts = [ordered]@{
    'INV-L1' = $invL1.Count
    'INV-L2' = $invL2.Count
    'INV-L3' = $invL3Count
}

Write-Host "=== [LayerBoundary] Agent 运行层 × 数据层 边界门禁（INV-L1/L2/L3） ===" -ForegroundColor White
Write-Host "运行层扫描面: $($runLayerFiles.Count) 文件（Agent.Core：Loops / Composition / RunState）"
Write-Host "[INV-L1] 运行层直引数据实现命名空间: $($invL1.Count)"
Write-Host "[INV-L2] 运行层自造数据根 / 引擎存储设施: $($invL2.Count)"
Write-Host "[INV-L3] 单一来源计数: $invL3Count（RunStateStage 定义 $($runStateStageDefs.Count) · NodeRunState 定义 $($nodeRunStateDefs.Count) · ur_tasks 写入口 $($taskSnapshotWriters.Count) · 节点写入口 $($nodeWriteEntrypoints.Count)）"

if ($ShowDetail -or -not $Check) {
    if ($invL1.Count -gt 0) {
        Write-Host "--- [INV-L1] 命中清单（应改经 DataCenter 标准端口：IDataCell / IRootProvider / IRuntimeSlot / IStateProjector） ---" -ForegroundColor Yellow
        $invL1 | ForEach-Object { Write-Host "  $($_.Path -replace [regex]::Escape($Root + '\'), '')`:$($_.LineNumber): $($_.Line.Trim())" }
    }
    if ($invL2.Count -gt 0) {
        Write-Host "--- [INV-L2] 命中清单（构造点应归数据面 / 宿主装配层，运行层只持端口） ---" -ForegroundColor Yellow
        $invL2 | ForEach-Object { Write-Host "  $($_.Path -replace [regex]::Escape($Root + '\'), '')`:$($_.LineNumber): $($_.Line.Trim())" }
    }
    Write-Host "--- [INV-L3] 单一来源定义点 / 写入口 ---" -ForegroundColor Yellow
    $runStateStageDefs | ForEach-Object { Write-Host "  [RunStateStage 定义] $($_.Path -replace [regex]::Escape($Root + '\'), '')`:$($_.LineNumber)" }
    $nodeRunStateDefs | ForEach-Object { Write-Host "  [NodeRunState 定义] $($_.Path -replace [regex]::Escape($Root + '\'), '')`:$($_.LineNumber)" }
}

if ($Baseline) {
    Write-Host "--- 基线取值（下移脚本内 `$frozen） ---" -ForegroundColor Cyan
    $counts.Keys | ForEach-Object { Write-Host "  '$_' = $($counts[$_])" }
    exit 0
}

$regressions = @()
foreach ($key in $counts.Keys) {
    if ($counts[$key] -gt $frozen[$key]) { $regressions += "$key 回渗：$($counts[$key]) > 冻结 $($frozen[$key])" }
}

if (-not $Check) {
    if ($regressions.Count -eq 0) {
        Write-Host "LayerBoundary(报告): 未超冻结基线。" -ForegroundColor Green
    } else {
        Write-Host "LayerBoundary(报告): 超冻结基线 —— $($regressions -join '；')" -ForegroundColor Yellow
    }
    exit 0
}

if ($regressions.Count -eq 0) {
    Write-Host "Check-AgentLayerBoundary: OK —— 运行层未回渗数据实现面。" -ForegroundColor Green
    exit 0
}

Write-Host "Check-AgentLayerBoundary: FAIL —— 运行层 × 数据层边界回渗：" -ForegroundColor Red
$regressions | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
exit 1
