#requires -Version 5.1
<#
.SYNOPSIS
    DataStoreDriven 领域边界守卫（DataStoreDomainBoundary）—— 控制反转的机器判据。

.DESCRIPTION
    目标：核心工程 Operation.DataStoreDriven **物理** Agent 无关（不引用 Share.Conversation / Share.TaskStatusPool），
    领域实现集中在子工程 DataStoreDriven.Domains（单向：Domains → 核心）。

    判据：
      P1 核心（排除 Domains\）不含 using Share.Conversation / using Share.TaskStatusPool
      P2 DataStoreDriven.csproj 不含 Proj_Share_Conversation / Proj_TaskStatusPool
      P3 DataStoreDriven.csproj 不含 Proj_DataStoreDriven_Domains（禁反向引用）
      P4 核心（排除 Domains\）不含领域字面量：history_root: / agent-round / "taskpool" / ur_sessions / ur_session_logs
      P5 Agent.Launcher 不含 using Operation.DataStoreDriven.Sqlite（宿主不再依赖具体介质）
      P6 正对照：Domains\History\SqliteConversationLogStore.cs 含 IConversationLogStore；Domains\DataStoreDrivenDomains.cs 含 All(
      P7 棘轮：Operation 同层引用 ≤ 基线；WorkFlowMethod ↔ DataStoreDriven = 0；HierarchyMethod/LoopFoldMethod → Domains = 0

    模式：默认报告（不设退出码）；-Check 违规 → 退出码 1。只读仓库，零侧写。

.PARAMETER Root
    仓库根（缺省由本脚本位置上溯五级推导）。
.PARAMETER Check
    校验模式：存在 P1–P7 违规 → 退出码 1。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$dsRoot      = Join-Path $Root 'Frame\CsharpPrj\Operation\DataStoreDriven'
$coreProj    = Join-Path $dsRoot 'DataStoreDriven.csproj'
$domainsRoot = Join-Path $dsRoot 'Domains'
$launcher    = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Launcher'
Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$dataStoreBaseline = Import-GateBaseline -Name DataStoreDomainBoundary

$violations = [System.Collections.Generic.List[string]]::new()
$notes      = [System.Collections.Generic.List[string]]::new()

function Get-TextFiles([string]$dir, [string[]]$skipDirNames) {
    if (-not (Test-Path -LiteralPath $dir)) { return @() }
    Get-ChildItem -LiteralPath $dir -Recurse -Filter '*.cs' -File |
        Where-Object {
            $_.FullName -notmatch '\\(obj|bin|BuildOutput)\\'
            $skip = $false
            foreach ($s in $skipDirNames) { if ($_.FullName -match ('\\' + [regex]::Escape($s) + '\\')) { $skip = $true; break } }
            -not $skip
        }
}

Write-Host ''
Write-Host '============================================================'
Write-Host '  DataStoreDriven 领域边界守卫（DataStoreDomainBoundary）'
Write-Host '============================================================'

# ---------------- P1 核心域 import ----------------
$coreFiles = @(Get-ChildItem -LiteralPath $dsRoot -Recurse -Filter '*.cs' -File |
    Where-Object { $_.FullName -notmatch '\\(obj|bin|BuildOutput)\\' -and $_.FullName -notmatch '\\Domains\\' })
$p1Hits = @()
foreach ($f in $coreFiles) {
    $line = 1
    foreach ($text in [System.IO.File]::ReadAllLines($f.FullName)) {
        if ($text -match '^\s*using\s+Share\.(Conversation|TaskStatusPool)\b') {
            $p1Hits += [pscustomobject]@{ File = $f.FullName; Line = $line; Text = $text.Trim() }
        }
        $line++
    }
}
foreach ($h in $p1Hits) { $violations.Add("[P1] 核心域 import：$($h.File):$($h.Line) $($h.Text)") }
Write-Host ("  P1 核心（排除 Domains）域 import：{0} 处 / 扫描 {1} 文件" -f $p1Hits.Count, $coreFiles.Count) -ForegroundColor Gray
# 基线：核心域 import 只降不增（上限）/ 核心文件数正对照下限（判据无标的即红）
$coreUsingLimit = [int]$dataStoreBaseline.CoreDomainUsingFilesBaseline.Value
$coreFilesFloor = [int]$dataStoreBaseline.CoreDomainFilesBaseline.Value
if ($p1Hits.Count -gt $coreUsingLimit) { $violations.Add("[P1] 核心域 import $($p1Hits.Count) 处 > 基线 $coreUsingLimit（只减不增）") }
if ($coreFiles.Count -lt $coreFilesFloor) { $violations.Add("[P1] 核心文件数 $($coreFiles.Count) < 正对照下限 $coreFilesFloor（判据无标的）") }

# ---------------- P2 / P3 工程引用 ----------------
$coreProjText = if (Test-Path -LiteralPath $coreProj) { [System.IO.File]::ReadAllText($coreProj) } else { '' }
if ($coreProjText -match 'Proj_Share_Conversation') { $violations.Add('[P2] DataStoreDriven.csproj 仍引用 Proj_Share_Conversation') }
if ($coreProjText -match 'Proj_TaskStatusPool')      { $violations.Add('[P2] DataStoreDriven.csproj 仍引用 Proj_TaskStatusPool') }
if ($coreProjText -match 'Proj_DataStoreDriven_Domains') { $violations.Add('[P3] DataStoreDriven.csproj 反向引用 Proj_DataStoreDriven_Domains（R1 违规）') }
if (-not $coreProjText -match 'Compile Remove="Domains\\\*\*\\\*\.cs"') { $violations.Add('[P3] DataStoreDriven.csproj 缺少 <Compile Remove="Domains\**\*.cs" />（防重复编译）') }

# ---------------- P4 领域字面量 ----------------
$literals = @('history_root:', '"taskpool"', 'ur_sessions', 'ur_session_logs')
$defaultWorkflowProvider = 'IDefaultWorkflowIdProvider.cs'
$p4Hits = @()
foreach ($f in $coreFiles) {
    if ($f.Name -eq $defaultWorkflowProvider) { continue }   # U9 唯一允许的默认值字面量落点
    $line = 1
    foreach ($text in [System.IO.File]::ReadAllLines($f.FullName)) {
        foreach ($lit in $literals) {
            if ($text.Contains($lit)) { $p4Hits += [pscustomobject]@{ File = $f.FullName; Line = $line; Literal = $lit } }
        }
        if ($text.Contains('agent-round')) { $p4Hits += [pscustomobject]@{ File = $f.FullName; Line = $line; Literal = 'agent-round' } }
        $line++
    }
}
foreach ($h in $p4Hits) { $violations.Add("[P4] 核心领域字面量 '$($h.Literal)'：$($h.File):$($h.Line)") }
Write-Host ("  P4 核心领域字面量：{0} 处" -f $p4Hits.Count) -ForegroundColor Gray

# ---------------- P5 宿主：不得依赖具体领域存储（通用介质类型允许） ----------------
$domainStoreTypes = @(
    'SqliteConversationLogStore', 'SqliteContainerRootStore', 'SqliteSessionStore', 'SqliteSessionTranscriptStore',
    'LegacyFallbackSessionTranscriptStore', 'SqliteTaskPoolStore'
)
$p5Hits = @()
foreach ($f in @(Get-TextFiles $launcher @())) {
    $line = 1
    foreach ($text in [System.IO.File]::ReadAllLines($f.FullName)) {
        $trimmed = $text.TrimStart()
        $isComment = $trimmed.StartsWith('//') -or $trimmed.StartsWith('*')
        if (-not $isComment) {
            foreach ($t in $domainStoreTypes) {
                if ([regex]::IsMatch($text, ('(?<![\w])(new\s+)?' + [regex]::Escape($t) + '\b'))) {
                    $p5Hits += [pscustomobject]@{ File = $f.FullName; Line = $line; Type = $t }
                }
            }
        }
        $line++
    }
}
foreach ($h in $p5Hits) { $violations.Add("[P5] 宿主依赖具体领域存储 $($h.Type)：$($h.File):$($h.Line)") }
Write-Host ("  P5 宿主具体领域存储引用：{0} 处（通用介质 SqliteConnectionFactory 等不在此判据）" -f $p5Hits.Count) -ForegroundColor Gray

# ---------------- P6 正对照（防判据空转） ----------------
$historyStore = Join-Path $domainsRoot 'History\SqliteConversationLogStore.cs'
$domainsList  = Join-Path $domainsRoot 'DataStoreDrivenDomains.cs'
if (-not (Test-Path -LiteralPath $historyStore)) {
    $violations.Add("[P6] 正对照失效：缺 $historyStore")
} elseif (-not ([System.IO.File]::ReadAllText($historyStore)).Contains('IConversationLogStore')) {
    $violations.Add("[P6] 正对照失效：$historyStore 未实现 IConversationLogStore")
}
if (-not (Test-Path -LiteralPath $domainsList)) {
    $violations.Add("[P6] 正对照失效：缺 $domainsList")
} elseif (-not ([System.IO.File]::ReadAllText($domainsList)).Contains('All(')) {
    $violations.Add("[P6] 正对照失效：$domainsList 未提供 All() 模块清单")
}
Write-Host ("  P6 正对照：historyStore={0} / domainsList={1}" -f (Test-Path -LiteralPath $historyStore), (Test-Path -LiteralPath $domainsList)) -ForegroundColor Gray

# ---------------- P7 同层引用棘轮 ----------------
$propsPath = Join-Path $Root 'Frame\generated\ModulePaths.generated.props'
$projMap = @{}
if (Test-Path -LiteralPath $propsPath) {
    foreach ($m in [regex]::Matches([System.IO.File]::ReadAllText($propsPath), '<Proj_(?<n>[\w\.]+)>(?<p>[^<]+)</Proj_')) {
        $path = $m.Groups['p'].Value
        $path = $path.Replace('$(CsharpPrjDir)', (Join-Path $Root 'Frame\CsharpPrj\'))
        $path = $path.Replace('/', '\')
        $projMap[$m.Groups['n'].Value] = $path
    }
}
$operationDir = Join-Path $Root 'Frame\CsharpPrj\Operation'
$sameLayer = [System.Collections.Generic.List[string]]::new()
if (Test-Path -LiteralPath $operationDir) {
    foreach ($proj in (Get-ChildItem -LiteralPath $operationDir -Recurse -Filter '*.csproj' -File | Where-Object { $_.FullName -notmatch '\\obj\\' })) {
        $text = [System.IO.File]::ReadAllText($proj.FullName)
        foreach ($m in [regex]::Matches($text, '<ProjectReference\s+Include="\$\((?<p>Proj_[\w\.]+)\)"')) {
            $name = $m.Groups['p'].Value -replace '^Proj_', ''
            if (-not $projMap.ContainsKey($name)) { continue }
            $target = $projMap[$name] -replace '/', '\'
            if (-not $target.StartsWith($operationDir, [System.StringComparison]::OrdinalIgnoreCase)) { continue }
            $fromRel = $proj.FullName.Substring($Root.Length).TrimStart('\')
            $toRel = $target.Substring($Root.Length).TrimStart('\')
            $sameLayer.Add("$fromRel -> $toRel")
        }
    }
}
$measuredSame = @($sameLayer | Sort-Object -Unique)
$sameLayerRefLimit = [int]$dataStoreBaseline.SameLayerRefBaseline.Value
if ($measuredSame.Count -gt $sameLayerRefLimit) {
    $violations.Add("[P7] Operation 同层引用 $($measuredSame.Count) 条 > 基线 $sameLayerRefLimit（只减不增）")
}
$mustBeZero = @(
    'Operation\WorkFlowMethod\WorkFlowMethod.csproj -> Operation\DataStoreDriven\DataStoreDriven.csproj',
    'Operation\DataStoreDriven\DataStoreDriven.csproj -> Operation\WorkFlowMethod\WorkFlowMethod.csproj',
    'Operation\HierarchyMethod\HierarchyMethod.csproj -> Operation\DataStoreDriven\Domains\DataStoreDriven.Domains.csproj',
    'Operation\LoopFoldMethod\LoopFoldMethod.csproj -> Operation\DataStoreDriven\Domains\DataStoreDriven.Domains.csproj'
)
foreach ($pair in $mustBeZero) {
    if ($measuredSame -contains $pair) { $violations.Add("[P7] 目标对须恒 0：$pair") }
}
Write-Host ("  P7 Operation 同层引用：{0} 条 / 基线 {1}" -f $measuredSame.Count, $sameLayerRefLimit) -ForegroundColor Gray

# ---------------- 汇总 ----------------
$notes.Add(("基线：核心域 import {0} 处 / 核心文件 {1} 个" -f $p1Hits.Count, $coreFiles.Count))
foreach ($n in $notes) { Write-Host ("  [OK] " + $n) -ForegroundColor Gray }

if ($violations.Count -eq 0) {
    Write-Host '[OK] DataStoreDriven 领域边界：0 违规（P1–P7）' -ForegroundColor Green
    exit 0
}

Write-Host ("[WARN] DataStoreDriven 领域边界命中 $($violations.Count) 处：") -ForegroundColor Yellow
foreach ($item in $violations) { Write-Host ("  [ERR] " + $item) -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0

