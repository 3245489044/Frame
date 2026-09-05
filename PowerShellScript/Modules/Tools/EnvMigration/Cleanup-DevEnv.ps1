<#
.SYNOPSIS
    开发环境源数据清理工具 — 删除 C 盘已迁移的源数据
.DESCRIPTION
    在确认 Migrate-DevEnv.ps1 迁移成功后，手动执行此脚本清理 C 盘源数据。
    将数据移到回收站，可安全恢复。
    注意：symlink、wsl、docker 策略的源数据不会被此脚本清理。
.PARAMETER All
    清理所有已启用项
.PARAMETER Items
    指定项目，逗号分隔，如 "NuGet,npm,MCP"
.PARAMETER ConfigPath
    自定义配置文件路径
.PARAMETER Force
    跳过所有确认提示
.EXAMPLE
    .\Cleanup-DevEnv.ps1
    .\Cleanup-DevEnv.ps1 -All
    .\Cleanup-DevEnv.ps1 -Items "NuGet,npm"
    .\Cleanup-DevEnv.ps1 -Force
#>

param(
    [switch]$All, [string]$Items = "", [string]$ConfigPath = "",
    [switch]$Force
)

# ===== 加载公共模块 =====
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $scriptPath "EnvMigration-Common.ps1")

# ===== 加载配置 =====
Write-Banner -Color "Red"
if (!$ConfigPath) { $ConfigPath = Join-Path $scriptPath "config.json" }
$Config = Load-MigrationConfig $ConfigPath
$devEnvRoot = $Config.devEnvRoot
Write-Host "📄 配置: $ConfigPath" -ForegroundColor Gray
Write-Host "📂 目标: $devEnvRoot" -ForegroundColor Gray

# ===== 选择要清理的项目 =====
Write-Step "选择要清理的项目"
$itemsToClean = Select-MigrationItems $Config $All $Force $Items -ShowSkipInfo $true

# ===== 扫描源数据 =====
Write-Step "扫描 C 盘源数据"
$scanResults = Scan-SourceData $Config $itemsToClean

# 过滤掉 symlink/wsl/docker 策略的项（这些策略的源数据不会被清理）
$filteredResults = $scanResults | Where-Object { $Config.migrationItems.$($_.Key).strategy -notin @("symlink","wsl","docker") }
$skippedItems = $scanResults | Where-Object { $Config.migrationItems.$($_.Key).strategy -in @("symlink","wsl","docker") }
foreach ($skipped in $skippedItems) {
    Write-Host "  ⏭️ $($skipped.Name) — 策略 $($skipped.Strategy) 自动跳过" -ForegroundColor Yellow
}

Write-ScanResultsTable $filteredResults -ShowStrategy $false

# ===== 确认 =====
$hasItems = $filteredResults | Where-Object { $_.Exists }
if (-not $hasItems) { Write-Host "`n⬜ 没有需要清理的源数据。" -ForegroundColor Yellow; exit 0 }

Write-Host "`n⚠️  警告: 以下数据将被移到回收站:" -ForegroundColor Red
$hasItems | % { Write-Host "  - $($_.Name) ($($_.SizeHuman))" -ForegroundColor Yellow }

if (!$Force -and !(Confirm-Prompt "确认清理？此操作不可撤销！", "n")) {
    Write-Host "已取消。"; exit 0
}

# ===== 执行清理 =====
Write-Step "执行清理"
$cleanedCount = 0
$failedCount = 0
foreach ($result in $filteredResults) {
    if (-not $result.Exists) {
        Write-Host "  ⬜ $($result.Name): 源不存在，跳过" -ForegroundColor Gray
        continue
    }
    $item = $Config.migrationItems.$($result.Key)
    $paths = Get-ItemSourcePaths $item.source
    foreach ($src in $paths) {
        if (Test-Path $src) {
            Move-ToRecycleBin $src
            # 等待回收站操作完成（最多 3 秒），避免异步误判
            $deleted = Wait-PathDeleted $src -MaxSeconds 3
            if (-not $deleted) {
                Write-Host "  ❌ $($result.Name): 删除失败: $src" -ForegroundColor Red
                $failedCount++
            } else {
                $cleanedCount++
            }
        }
    }
}

# ===== 完成 =====
Write-Step "清理完成" -Color "Green"
Write-Host "✅ 已清理: $cleanedCount 项" -ForegroundColor Green
if ($failedCount -gt 0) { Write-Host "❌ 失败: $failedCount 项" -ForegroundColor Red }
Write-Host "`n💡 提示: 如果误删，可从回收站恢复。" -ForegroundColor Yellow
