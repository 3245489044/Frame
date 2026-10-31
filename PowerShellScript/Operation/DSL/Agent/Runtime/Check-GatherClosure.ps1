#requires -Version 5.1
<#
.SYNOPSIS
    聚拢闭合守卫（GatherClosure）—— 直呼域方法 / 未声明写回的可复算清单（G0 → G2）。
.DESCRIPTION
    判据（结构事实：源码 file:line + 符号名，非执行者叙述）：
      C1 读侧直呼：域取数方法不得绕过 GatherHub（允许集 = producer 实现文件）。
      C2 写侧直呼：DOM / 节点 / 归档写回不得绕过 Hub.Emit（同上）。
      C3 棘轮：违规数 ≤ 基线（Baselines\Process\GatherClosure.psd1，只降不升）。
      C4 负对照：合成直呼样本必须被判据捕获（防判据恒真）。
      C5 出口闭合（C-OUT）：Emit 的字面量产物名必须 ∈ 声明面 produces（未声明写回 ⇒ 红）；常量引用放行。
    模式：默认报告（不设退出码）；-Check 时超基线 → 退出码 1。
.PARAMETER Root
    仓库根（默认由本脚本位置上溯五级）。
.PARAMETER Check
    校验模式（超基线判红）。
.PARAMETER ShowDetail
    输出逐个命中位置。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root))
{
    $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
}

Write-Host ''
Write-Host '============================================================'
Write-Host '  聚拢闭合守卫（GatherClosure）'
Write-Host '============================================================'

$patterns = @(
    @{ Domain = 'conversation'; Symbol = 'ReadScopeConversationAsync'; Regex = '\bReadScopeConversationAsync\s*\(' },
    @{ Domain = 'histories';    Symbol = 'GetHistoryAsync';            Regex = '\bGetHistoryAsync\s*\(' },
    @{ Domain = 'histories';    Symbol = 'ListHistoriesAsync';         Regex = '\bListHistoriesAsync\s*\(' },
    @{ Domain = 'badges';       Symbol = 'BuildRuntimeBadgesAsync';    Regex = '\bBuildRuntimeBadgesAsync\s*\(' },
    @{ Domain = 'cost';         Symbol = 'CostBadgeProvider.BuildAsync'; Regex = '\bCostBadgeProvider\.BuildAsync\s*\(' },
    @{ Domain = 'prompt';       Symbol = 'PromptSurfaceResolver.ResolveAsync'; Regex = '\bPromptSurfaceResolver\.ResolveAsync\s*\(' },
    @{ Domain = 'board';        Symbol = 'TryReadHistoryBoardAsync';   Regex = '\bTryReadHistoryBoardAsync\s*\(' },
    @{ Domain = 'dom';          Symbol = 'WriteOutputAsync';           Regex = '\bWriteOutputAsync\s*\(' },
    @{ Domain = 'node';         Symbol = 'NodeWriteGate';              Regex = '\bNodeWriteGate\b' },
    @{ Domain = 'node-write';   Symbol = 'WriteBatchAsync';            Regex = '\bWriteBatchAsync\s*\(' }
)

# 允许集（producer 实现侧）：声明了 produce/need 的唯一落点；其余直呼即违规。
$allowFileNames = @(
    'ConversationViewService.Nodes.cs',
    'ConversationCanonGatherModule.Write.cs',
    # 契约声明与端口实现：写原语的定义面，不是「绕过 Hub 的调用点」。
    'NodeDomainPortContracts.cs',
    'NodeDomainPort.cs',
    'GatherHub.cs'
)

# 基线（单源；禁内联数值）。
Import-Module (Join-Path (Get-FrameRoot) 'PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name GatherClosure
$maxViolations = [int]$baseline.MaxViolations.Value

function Test-GatherClosureText
{
    param([string]$Text, [string]$FileName)

    $hits = [System.Collections.Generic.List[object]]::new()
    if ($allowFileNames -contains $FileName) { return , $hits }

    $lineNo = 0
    foreach ($line in ($Text -split "`n")) {
        $lineNo++
        $trimmed = $line.TrimStart()
        # 跳过注释行（判据载体 = 代码事实，不是文档）。
        if ($trimmed.StartsWith('//') -or $trimmed.StartsWith('///') -or $trimmed.StartsWith('*')) { continue }
        foreach ($p in $patterns) {
            if ($line -match $p.Regex) {
                $hits.Add([pscustomobject]@{
                    File = $FileName; Line = $lineNo; Domain = $p.Domain; Symbol = $p.Symbol
                })
            }
        }
    }
    return , $hits
}

$srcRoot = Join-Path $Root 'Projects\Plug\Agent\src'
$files = @(Get-ChildItem -LiteralPath $srcRoot -Recurse -Filter '*.cs' -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\obj\\' -and $_.FullName -notmatch '\\bin\\' })

$hits = [System.Collections.Generic.List[object]]::new()
foreach ($file in $files) {
    $text = [System.IO.File]::ReadAllText($file.FullName)
    foreach ($hit in (Test-GatherClosureText -Text $text -FileName $file.Name)) {
        $hit | Add-Member -NotePropertyName Path -NotePropertyValue $file.FullName -Force
        $hits.Add($hit)
    }
}

# ---------------- 负对照（防假绿） ----------------
$probe = "var x = await svc.GetHistoryAsync(id, ct);"
if (@(Test-GatherClosureText -Text $probe -FileName 'Probe.cs').Count -eq 0) {
    Write-Host '[FAIL] 负对照失效：合成直呼样本未被捕获（判据恒真）' -ForegroundColor Red
    if ($Check) { exit 1 }
    exit 0
}

# ---------------- C5 出口闭合（C-OUT：Emit 产物名 ∈ 声明面 produces） ----------------
$gatherDeclDir = Join-Path $Root 'Projects\Plug\Agent\Config\gather'
$declaredProduces = [System.Collections.Generic.HashSet[string]]::new()
foreach ($declFile in @(Get-ChildItem -LiteralPath $gatherDeclDir -Filter '*.json' -File -ErrorAction SilentlyContinue))
{
    try
    {
        $declSpec = Get-Content -LiteralPath $declFile.FullName -Raw | ConvertFrom-Json
        foreach ($viewProp in $declSpec.views.PSObject.Properties)
        {
            foreach ($produce in @($viewProp.Value.produces))
            {
                if (-not [string]::IsNullOrWhiteSpace([string]$produce.name)) { [void]$declaredProduces.Add([string]$produce.name) }
            }
        }
    }
    catch
    {
        Write-Host ("  [WARN] C5 声明面解析失败（跳过该文件）：{0}" -f $declFile.Name) -ForegroundColor Yellow
    }
}

# 判据：Emit 的第二参数为字面量时必须命中声明面 produces；标识符（常量）放行（常量一致性由 GatherSpec/R9-R10 承担）。
function Get-EmitProduceNameIssues
{
    param([string]$Text, [string]$FileName, [System.Collections.Generic.HashSet[string]]$Declared)

    $issues = [System.Collections.Generic.List[object]]::new()
    $lineNo = 0
    foreach ($line in ($Text -split "`n"))
    {
        $lineNo++
        $trimmed = $line.TrimStart()
        if ($trimmed.StartsWith('//') -or $trimmed.StartsWith('///') -or $trimmed.StartsWith('*')) { continue }
        foreach ($m in [regex]::Matches($line, '\.Emit\s*\(\s*[^,]+,\s*(?<arg>[^,]+?)\s*,'))
        {
            $arg = $m.Groups['arg'].Value.Trim()
            if (-not $arg.StartsWith('"')) { continue }
            $name = $arg.Trim('"')
            if (-not $Declared.Contains($name))
            {
                $issues.Add([pscustomobject]@{ File = $FileName; Line = $lineNo; Name = $name })
            }
        }
    }

    return , $issues
}

$emitViolations = [System.Collections.Generic.List[object]]::new()
foreach ($file in $files)
{
    $text = [System.IO.File]::ReadAllText($file.FullName)
    foreach ($hit in (Get-EmitProduceNameIssues -Text $text -FileName $file.Name -Declared $declaredProduces))
    {
        $hit | Add-Member -NotePropertyName Path -NotePropertyValue $file.FullName -Force
        $emitViolations.Add($hit)
    }
}

# 负对照（防假绿）：未声明的字面量产物名必须被捕获。
$emitProbe = 'var t = hub.Emit(view, "ghost-produce", payload);'
if (@(Get-EmitProduceNameIssues -Text $emitProbe -FileName 'Probe.cs' -Declared $declaredProduces).Count -eq 0)
{
    Write-Host '[FAIL] C5 负对照失效：未声明产物名未被捕获（判据恒真）' -ForegroundColor Red
    if ($Check) { exit 1 }
    exit 0
}

$count = @($hits).Count
if ($ShowDetail) {
    foreach ($hit in $hits) {
        Write-Host ("  {0}:{1}  [{2}] {3}" -f $hit.File, $hit.Line, $hit.Domain, $hit.Symbol) -ForegroundColor Gray
    }
    foreach ($hit in $emitViolations) {
        Write-Host ("  {0}:{1}  [emit] {2}" -f $hit.File, $hit.Line, $hit.Name) -ForegroundColor Gray
    }
}

Write-Host ("  实测直呼 {0} 处 / 基线上限 {1} 处（只降不升）；出口闭合违规 {2} 处" -f $count, $maxViolations, $emitViolations.Count) -ForegroundColor Gray

if ($emitViolations.Count -gt 0) {
    Write-Host ("[FAIL] C5 出口闭合：{0} 处 Emit 产物名未在声明面 produces（未声明写回）" -f $emitViolations.Count) -ForegroundColor Yellow
    foreach ($hit in $emitViolations) {
        Write-Host ("  - {0}:{1} produce='{2}'" -f $hit.File, $hit.Line, $hit.Name) -ForegroundColor Yellow
    }
    if ($Check) { exit 1 }
    exit 0
}

if ($count -le $maxViolations) {
    Write-Host '[OK] 聚拢闭合守卫：未超基线（直呼清单棘轮在位 + 出口闭合 C5 绿）' -ForegroundColor Green
    exit 0
}

Write-Host ("[FAIL] 直呼新增 {0} 处（实测 {1} > 基线 {2}）：须改走 GatherHub" -f ($count - $maxViolations), $count, $maxViolations) -ForegroundColor Yellow
foreach ($hit in $hits) {
    Write-Host ("  - {0}:{1}  [{2}] {3}" -f $hit.File, $hit.Line, $hit.Domain, $hit.Symbol) -ForegroundColor Yellow
}

if ($Check) { exit 1 }
exit 0
