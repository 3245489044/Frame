#requires -Version 5.1
<#
.SYNOPSIS
    聚拢声明约束守卫（GatherDecl）—— 框架公开面与装配点受控。
.DESCRIPTION
    判据（结构事实：源码符号）：
      G1 公开类型白名单：Modules\Gather 的 public 类型 ⊆ 白名单（新增公开面须登记，防长成杂物间）。
      G2 实现受控：`IGatherModule` 实现只允许出现在 `*GatherModule*.cs` 或 Modules\Gather 内。
      G3 装配受控：`new GatherHub(` 只允许在 Modules\Gather 与 UiViewProjection.cs（唯一适配器）。
      G4 负对照：合成样本（未登记公开类型 / 非法实现点）必须被同一判据捕获。
    模式：默认报告；-Check 时违规 → 退出码 1。
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

$violations = [System.Collections.Generic.List[string]]::new()

$publicAllow = @(
    'GatherSpecSet', 'GatherNeedSpec', 'GatherStageSpec', 'GatherProduceSpec', 'GatherViewSpec',
    'GatherCostClass', 'GatherDeclKind', 'GatherDecl', 'GatherSchema',
    'GatherToken', 'GatherProduceToken', 'GatherProducer', 'IGatherModule', 'GatherModuleBuilder',
    'GatherRegistry', 'IGatherProducer', 'GatherStageResult', 'GatherClosureViolationException', 'GatherHub',
    'GatherSpecLoader', 'GatherEmitTicket'
)

Write-Host ''
Write-Host '============================================================'
Write-Host '  聚拢声明约束守卫（GatherDecl）'
Write-Host '============================================================'

function Get-PublicTypeNames
{
    param([string]$Text)
    $pattern = 'public\s+(?:sealed\s+|readonly\s+|static\s+|abstract\s+)*(?:record\s+struct\s+|record\s+|class\s+|interface\s+|enum\s+|delegate\s+[A-Za-z0-9_<>?,\s]+?\s+)([A-Za-z_][A-Za-z0-9_]*)'
    return @([regex]::Matches($Text, $pattern) | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
}

$gatherDir = Join-Path $Root 'Frame\CsharpPrj\Modules\Gather'
$gatherFiles = @(Get-ChildItem -LiteralPath $gatherDir -Filter '*.cs' -File -ErrorAction SilentlyContinue)
if ($gatherFiles.Count -eq 0) {
    $violations.Add("[G1] 聚拢框架源码缺失：$gatherDir")
}

$declared = @()
foreach ($file in $gatherFiles) {
    $names = Get-PublicTypeNames -Text ([System.IO.File]::ReadAllText($file.FullName))
    foreach ($name in $names) {
        $declared += $name
        if ($publicAllow -notcontains $name) {
            $violations.Add("[G1] $($file.Name) 新增未登记公开类型 '$name'（须先在本门禁白名单登记并说明职责）")
        }
    }
}

# G2/G3：实现点与装配点受控（扫描 Frame + Agent 源码）。
$scanRoots = @(
    (Join-Path $Root 'Frame\CsharpPrj'),
    (Join-Path $Root 'Projects\Plug\Agent\src')
)
$implPattern = '(?<cls>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*(?:(?:global::)?[A-Za-z0-9_.]+\.)?IGatherModule\b'
$hubCtorPattern = 'new\s+(?:[A-Za-z0-9_.]+\.)?GatherHub\s*\('
$implHits = 0
$hubHits = 0

foreach ($scanRoot in $scanRoots) {
    if (-not (Test-Path -LiteralPath $scanRoot)) { continue }
    foreach ($file in @(Get-ChildItem -LiteralPath $scanRoot -Recurse -Filter '*.cs' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\obj\\' -and $_.FullName -notmatch '\\bin\\' })) {
        $text = [System.IO.File]::ReadAllText($file.FullName)
        $code = (($text -split "`n" | Where-Object {
            $t = $_.TrimStart(); -not ($t.StartsWith('//') -or $t.StartsWith('///') -or $t.StartsWith('*'))
        }) -join "`n")

        if ($code -match $implPattern) {
            $implHits++
            $implClass = $Matches['cls']
            $inFramework = $file.FullName -like (Join-Path $gatherDir '*')
            $byConvention = $implClass -like '*GatherModule*'
            if (-not $inFramework -and -not $byConvention) {
                $violations.Add("[G2] $($file.FullName)：IGatherModule 实现类 '$implClass' 须以 GatherModule 命名（或落在 Modules\Gather）")
            }
        }

        if ($code -match $hubCtorPattern) {
            $hubHits++
            $inFramework = $file.FullName -like (Join-Path $gatherDir '*')
            $isAdapter = $file.Name -eq 'UiViewProjection.cs'
            if (-not $inFramework -and -not $isAdapter) {
                $violations.Add("[G3] $($file.FullName)：new GatherHub 只允许在 Modules\Gather 与 UiViewProjection.cs")
            }
        }
    }
}

# ---------------- 负对照（防假绿） ----------------
$probePublic = 'public sealed record GhostDecl(string Name);'
if (@(Get-PublicTypeNames -Text $probePublic | Where-Object { $publicAllow -notcontains $_ }).Count -eq 0) {
    $violations.Add('[G4] 负对照失效：未登记公开类型未被捕获')
}
$probeImpl = 'internal sealed class GhostModule : global::Modules.Gather.IGatherModule'
if ($probeImpl -notmatch $implPattern) {
    $violations.Add('[G4] 负对照失效：合成 IGatherModule 实现未被捕获')
}

if ($ShowDetail) {
    Write-Host ("  框架公开类型 {0} 个 / 模块实现点 {1} 处 / Hub 装配点 {2} 处" -f `
        @($declared | Sort-Object -Unique).Count, $implHits, $hubHits) -ForegroundColor Gray
}

if ($violations.Count -eq 0) {
    Write-Host '[OK] 聚拢声明约束：0 违规（公开面受控 / 实现点受控 / 装配点受控 / 负对照在位）' -ForegroundColor Green
    exit 0
}

Write-Host "[FAIL] 聚拢声明约束命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($item in $violations) { Write-Host "  - $item" -ForegroundColor Yellow }

if ($Check) { exit 1 }
exit 0
