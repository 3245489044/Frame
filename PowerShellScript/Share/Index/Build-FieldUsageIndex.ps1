#requires -Version 5.1
<#
.SYNOPSIS
    字段使用索引（FieldUsageIndex）生成器 —— 语义索引产物的唯一人工入口。

.DESCRIPTION
    产物 = `Environment\CentralTest\BuildOutput\<Configuration>\ConstraintIndex\FieldUsageIndex.json`（不入库）。
    门禁侧（CF-W-14 与 CW-04/10/13 消费方）统一走 ConstraintIndex.Get-ConstraintFieldIndexState：
    缺失/过期时在跨进程 Mutex 下只重算一次；本脚本是人工/流水线显式生成入口。

    失败即非零（不产出"半对"索引）：BuildOutput 缺失、排除装配进入引用集、源文件缺失、
    期望字段未被语义绑定、同名配置类型歧义 —— 均由索引工具 fail-closed。

.PARAMETER Root
    仓库根目录（默认由本脚本位置上溯四级推导）。

.PARAMETER Configuration
    构建配置（默认 Debug；决定 BuildOutput 与产物子目录）。

.PARAMETER NoBuild
    跳过索引工具构建（复用已产出的 exe；用于无并发构建时的耗时实测）。

.PARAMETER Check
    只验新鲜度，不生成；过期/缺失 → 退出码 1。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [string]$Configuration = 'Debug',
    [switch]$NoBuild,
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root))
{
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$modulePath = Join-Path $Root 'Frame\PowerShellScript\Modules\Constraint\ConstraintIndex.psm1'
if (-not (Test-Path -LiteralPath $modulePath)) { throw "缺索引模块 $modulePath" }
Import-Module -Name $modulePath -Force -DisableNameChecking

if ($Check)
{
    $reason = Test-ConstraintFieldIndexFresh -Root $Root -Configuration $Configuration
    $artifactPath = Get-ConstraintIndexArtifactPath -Root $Root -Configuration $Configuration
    if ($reason -ne '')
    {
        Write-Host "  [STALE] $artifactPath" -ForegroundColor Red
        Write-Host "  $reason" -ForegroundColor Red
        exit 1
    }
    Write-Host "  [FRESH] $artifactPath" -ForegroundColor Green
    exit 0
}

$result = Invoke-ConstraintFieldIndexBuild -Root $Root -Configuration $Configuration -NoBuild:$NoBuild
Write-Host ""
Write-Host "  产物：$($result.ArtifactPath)" -ForegroundColor Green
Write-Host ("  构建 {0} ms / 端到端 {1} ms" -f $result.BuildMs, $result.TotalMs) -ForegroundColor Gray
foreach ($line in @($result.ToolOutput)) { if ($line) { Write-Host "  $line" -ForegroundColor Gray } }
