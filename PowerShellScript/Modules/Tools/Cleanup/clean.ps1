<#
.SYNOPSIS
    快速清理所有 bin 和 obj 目录

.DESCRIPTION
    轻量级清理工具，递归删除项目目录下所有 bin/obj 目录。
    适用于日常快速清理构建缓存。

    与 CleanupExtraBuildOutput.ps1 的关系：
    - clean.ps1：轻量级，仅删除 bin/obj，适合日常快速清理
    - CleanupExtraBuildOutput.ps1：重量级，包含 .csproj 配置修复、输出目录完整性检查等
    - 日常使用推荐 clean.ps1，构建配置问题使用 CleanupExtraBuildOutput.ps1

    用法:
        clean.ps1              实际删除所有 bin/obj
        clean.ps1 -WhatIf      模拟模式，仅预览不删除
        clean.ps1 -Unblock     解除脚本锁定状态
#>

param(
    [switch]$WhatIf = $false,
    [switch]$Unblock = $false
)

$ErrorActionPreference = "Continue"

$SCRIPT_DIR = Split-Path -Parent $MyInvocation.MyCommand.Path
$ROOT_DIR = (Get-Item $SCRIPT_DIR).Parent.FullName

if ($Unblock) {
    Write-Host "正在解除本脚本的锁定状态..." -ForegroundColor Cyan
    Unblock-File -Path $MyInvocation.MyCommand.Path -ErrorAction SilentlyContinue
    Write-Host "完成！请重新运行脚本。" -ForegroundColor Green
    pause
    exit
}

$deletedCount = 0
$failedCount = 0
$foundCount = 0

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  正在清理所有 bin 和 obj 目录..." -ForegroundColor Cyan
Write-Host "  根目录: $ROOT_DIR" -ForegroundColor Gray
if ($WhatIf) { Write-Host "  [模拟模式] 仅预览，不会实际删除" -ForegroundColor Yellow }
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$targetFolders = @("bin", "obj")

$allDirs = Get-ChildItem -Path $ROOT_DIR -Directory -Recurse -Force -ErrorAction SilentlyContinue

$allDirs | ForEach-Object {
    if ($_.Name -in $targetFolders) {
        $foundCount++
        if ($WhatIf) {
            Write-Host "  [模拟] 将删除: $($_.FullName)" -ForegroundColor Yellow
            $deletedCount++
        } else {
            try {
                Remove-Item -Path $_.FullName -Recurse -Force -ErrorAction Stop
                Write-Host "  [已删除] $($_.FullName)" -ForegroundColor Green
                $deletedCount++
            } catch {
                Write-Host "  [失败]    $($_.FullName) - $($_.Exception.Message)" -ForegroundColor Red
                $failedCount++
            }
        }
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  扫描到 $foundCount 个目标目录" -ForegroundColor Gray
if ($WhatIf) {
    Write-Host "  模拟完成！共匹配 $deletedCount 个目录" -ForegroundColor Yellow
} else {
    Write-Host "  清理完成！" -ForegroundColor Green
    Write-Host "  成功删除: $deletedCount 个目录" -ForegroundColor Green
    if ($failedCount -gt 0) {
        Write-Host "  删除失败: $failedCount 个目录（可能被占用）" -ForegroundColor Red
    }
}
Write-Host "========================================" -ForegroundColor Cyan

if (-not $WhatIf) {
    Write-Host ""
    Write-Host "按任意键退出..."
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
}
