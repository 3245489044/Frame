<#
.SYNOPSIS
    开发环境迁移工具 — 低风险版
.DESCRIPTION
    仅设置环境变量 + 删除 C 盘原数据，无需复制。
    支持策略：env（NuGet/pip/Python/DotNet）、config（npm）
.PARAMETER All
    一键迁移所有已启用项
.PARAMETER Items
    指定项目，逗号分隔，如 "NuGet,npm"
.PARAMETER ConfigPath
    自定义配置文件路径
.PARAMETER Force
    跳过所有确认提示
.EXAMPLE
    .\Migrate-DevEnv.ps1
    .\Migrate-DevEnv.ps1 -All
    .\Migrate-DevEnv.ps1 -Items "NuGet,npm"
    .\Migrate-DevEnv.ps1 -Force
#>

param(
    [switch]$All, [string]$Items = "", [string]$ConfigPath = "",
    [switch]$Force
)

# ===== 加载公共模块 =====
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $scriptPath "EnvMigration-Common.ps1")

# ===== 加载配置 =====
Write-Banner -Title "开发环境迁移工具" -Subtitle "低风险模式 — 仅设置环境变量 + 删除 C 盘原数据" -Color "Cyan"
if (!$ConfigPath) { $ConfigPath = Join-Path $scriptPath "config.json" }
$Config = Load-MigrationConfig $ConfigPath
$devEnvRoot = $Config.devEnvRoot
Write-Host "📄 配置: $ConfigPath" -ForegroundColor Gray
Write-Host "📂 目标: $devEnvRoot" -ForegroundColor Gray

# ===== 阶段 1: 扫描 =====
Write-StepTitle "阶段 1/3: 扫描 C 盘源数据"
$scanResults = Scan-SourceData $Config (Get-EnabledItems $Config)
Write-ScanResultsTable $scanResults

# ===== 阶段 2: 确认 =====
Write-StepTitle "阶段 2/3: 确认迁移清单"
$itemsToMigrate = Select-MigrationItems $Config $All $Force $Items

if (!$Force -and !(Confirm-Prompt "确认开始迁移？（将设置环境变量并删除 C 盘原数据）")) {
    Write-Host "已取消。"; exit 0
}

# ===== 阶段 3: 执行迁移 =====
Write-StepTitle "阶段 3/3: 执行迁移"
$migrationLog = Invoke-MigrationPipeline $Config $devEnvRoot $itemsToMigrate

# ===== 验证 =====
Write-StepTitle "迁移结果"
$allPassed = Test-MigrationResult $migrationLog

# ===== 完成 =====
Write-StepTitle "迁移完成" -Color "Green"
Write-MigrationSummary $devEnvRoot $allPassed
