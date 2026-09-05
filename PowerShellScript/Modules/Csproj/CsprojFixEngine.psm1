# CsprojFixEngine.psm1 - .csproj 配置修复引擎（编排入口）
# 提供 .csproj 文件的自动检测与修复功能
#
# 本模块是编排层，整合以下子模块：
#   - CsprojDetection.psm1      （检测功能）
#   - CsprojFixActions.psm1     （修复操作）
#   - CsprojNewProjectInit.psm1 （新项目初始化）
#
# 依赖基础模块：
#   - XmlHelpers.psm1
#   - FileSystemHelpers.psm1
#   - InteractiveHelpers.psm1
#
# 工作流：
#   1. 检测项目目录下多余的 BuildOutput\（非根 BuildOutput\ 的输出）
#   2. 分析 .csproj 配置问题（OutputPath、TargetFramework、UseWindowsForms 等）
#   3. 执行修复（自动备份原文件）
#   4. 清理多余的 BuildOutput\ 目录

# 导入子模块
$modulePath = Split-Path -Parent $MyInvocation.MyCommand.Path
Import-Module (Join-Path $modulePath "CsprojDetection.psm1") -Force
Import-Module (Join-Path $modulePath "CsprojFixActions.psm1") -Force
Import-Module (Join-Path $modulePath "CsprojNewProjectInit.psm1") -Force

# ============================================================
# 一键修复功能
# ============================================================

<#
.SYNOPSIS
    一键检测并修复项目本地 BuildOutput 问题
.DESCRIPTION
    完整的检测→修复→清理工作流：
    1. 检测所有项目目录下多余的 BuildOutput\
    2. 对每个有问题的项目，检查并修复 .csproj 配置
    3. 清理多余的 BuildOutput\ 目录
.PARAMETER rootDir
    解决方案根目录
.PARAMETER backupDir
    备份目录路径
.PARAMETER invalidTfmMap
    无效 TFM 映射表（可选）
.PARAMETER globalTargetFramework
    全局 TargetFramework（可选）
.PARAMETER winFormsDependentProjects
    需要 WinForms 的项目引用列表（可选）
.PARAMETER autoFix
    是否自动修复（跳过确认提示），默认 $false
.OUTPUTS
    [hashtable] 包含检测和修复结果的哈希表
#>
function Invoke-CsprojRepair {
    param(
        [string]$rootDir = "",
        [string]$backupDir = "",
        [hashtable]$invalidTfmMap = $null,
        [string]$globalTargetFramework = 'net10.0',
        [string[]]$winFormsDependentProjects = $null,
        [bool]$autoFix = $false
    )

    # 设置默认值（在函数体内设置，避免 param() 块中复杂表达式导致解析错误）
    if ([string]::IsNullOrEmpty($rootDir)) {
        $rootDir = Resolve-Path (Join-Path $PSScriptRoot "..\..")
    }
    if ([string]::IsNullOrEmpty($backupDir)) {
        $backupDir = Join-Path $PSScriptRoot "..\backups"
    }
    if ($null -eq $invalidTfmMap) {
        # 尝试从 ProjectConfig 模块获取，失败则使用硬编码默认值
        try {
            $invalidTfmMap = Get-InvalidTfmMap
        } catch {
            $invalidTfmMap = @{ 'net10.0-windows7.0' = 'net10.0-windows' }
        }
    }
    if ($null -eq $winFormsDependentProjects) {
        try {
            $winFormsDependentProjects = Get-WinFormsDependentProjects
        } catch {
            $winFormsDependentProjects = @('WdUI.csproj', 'FileDriven.UI.csproj', 'BasicMethod.csproj')
        }
    }

    $result = @{
        localBuildOutputFound = @()
        fixedInvalidTfm = 0
        fixedCustomOutputPath = 0
        fixedUseWindowsForms = 0
        fixedRedundantTf = 0
        fixedSolutionDir = $false
        cleanedDirectories = @()
    }

    Write-Host "╔════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║  CsprojFixEngine - .csproj 配置修复工具      ║" -ForegroundColor Cyan
    Write-Host "╚════════════════════════════════════════════════╝" -ForegroundColor Cyan
    Write-Host ""

    # 获取所有 csproj 文件
    $csprojFiles = Get-CsprojFiles -rootDir $rootDir -excludePaths @(Join-Path $rootDir "BuildOutput")

    # ============================================================
    # 步骤 1：检测项目本地 BuildOutput
    # ============================================================
    Write-Host "【步骤 1】检测项目目录下多余的 BuildOutput\..." -ForegroundColor Yellow
    $localBuildOutputs = Get-ProjectLocalBuildOutput -csprojFiles $csprojFiles -rootDir $rootDir

    if ($localBuildOutputs.Count -eq 0) {
        Write-Host "  未发现项目本地 BuildOutput\ 目录，配置正确。" -ForegroundColor Green
    } else {
        Write-Host "  发现 $($localBuildOutputs.Count) 个项目存在本地 BuildOutput\ 目录：" -ForegroundColor Yellow
        foreach ($item in $localBuildOutputs) {
            Write-Host "    - [$($item.SizeMB) MB] $($item.ProjectName) ($($item.BuildOutputDir))" -ForegroundColor Magenta
        }
        $result.localBuildOutputFound = $localBuildOutputs
    }
    Write-Host ""

    # ============================================================
    # 步骤 2：修复 .csproj 配置问题
    # ============================================================
    Write-Host "【步骤 2】检查并修复 .csproj 配置问题..." -ForegroundColor Yellow

    # 2.1 修复无效 TargetFramework
    Write-Host "  [2.1] 检查无效 TargetFramework..." -ForegroundColor Gray
    $result.fixedInvalidTfm = Fix-InvalidTargetFramework -csprojFiles $csprojFiles -invalidTfmMap $invalidTfmMap -backupDir $backupDir

    # 2.2 移除自定义 OutputPath
    Write-Host "  [2.2] 检查自定义 OutputPath..." -ForegroundColor Gray
    $result.fixedCustomOutputPath = Fix-CustomOutputPath -csprojFiles $csprojFiles -backupDir $backupDir

    # 2.3 补充 UseWindowsForms
    Write-Host "  [2.3] 检查缺失的 UseWindowsForms..." -ForegroundColor Gray
    $result.fixedUseWindowsForms = Fix-MissingUseWindowsForms -csprojFiles $csprojFiles -winFormsDependentProjects $winFormsDependentProjects -backupDir $backupDir

    # 2.4 移除冗余 TargetFramework
    Write-Host "  [2.4] 检查冗余 TargetFramework..." -ForegroundColor Gray
    $result.fixedRedundantTf = Fix-RedundantTargetFramework -csprojFiles $csprojFiles -globalTargetFramework $globalTargetFramework -backupDir $backupDir

    # 2.5 修复 Directory.Build.props 中的 SolutionDir
    Write-Host "  [2.5] 检查 Directory.Build.props..." -ForegroundColor Gray
    $directoryBuildProps = Join-Path $rootDir "Directory.Build.props"
    $result.fixedSolutionDir = Fix-SolutionDirInDirectoryBuildProps -directoryBuildPropsPath $directoryBuildProps -backupDir $backupDir

    Write-Host ""

    # ============================================================
    # 步骤 3：清理多余的 BuildOutput 目录
    # ============================================================
    if ($localBuildOutputs.Count -gt 0) {
        Write-Host "【步骤 3】清理项目本地 BuildOutput\ 目录..." -ForegroundColor Yellow
        $dirsToClean = $localBuildOutputs | ForEach-Object { $_.BuildOutputDir }
        $cleanResult = Confirm-And-DeleteDirectories -dirList $dirsToClean -promptMessage "确认删除以上项目本地的 BuildOutput\ 目录？(Y/N/S=逐项确认)"
        # 只记录实际已删除的目录，跳过未删除的
        if ($cleanResult.deletedCount -gt 0) {
            $result.cleanedDirectories = $dirsToClean
        }
    }

    # ============================================================
    # 完成
    # ============================================================
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host "  修复完成" -ForegroundColor Cyan
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  修复摘要：" -ForegroundColor Cyan
    Write-Host "    - 检测到 $($localBuildOutputs.Count) 个项目本地 BuildOutput" -ForegroundColor Gray
    Write-Host "    - 修复 $($result.fixedInvalidTfm) 个无效 TargetFramework" -ForegroundColor Gray
    Write-Host "    - 移除 $($result.fixedCustomOutputPath) 个自定义 OutputPath" -ForegroundColor Gray
    Write-Host "    - 补充 $($result.fixedUseWindowsForms) 个 UseWindowsForms" -ForegroundColor Gray
    Write-Host "    - 移除 $($result.fixedRedundantTf) 个冗余 TargetFramework" -ForegroundColor Gray
    if ($result.fixedSolutionDir) {
        Write-Host "    - 已修复 Directory.Build.props 中的 SolutionDir 引用" -ForegroundColor Gray
    }
    Write-Host ""

    return $result
}

# 导出公共函数
Export-ModuleMember -Function Get-ProjectLocalBuildOutput,
                               Get-TargetFrameworkConfiguration,
                               Fix-InvalidTargetFramework,
                               Fix-CustomOutputPath,
                               Fix-MissingUseWindowsForms,
                               Fix-RedundantTargetFramework,
                               Fix-SolutionDirInDirectoryBuildProps,
                               Initialize-NewProject,
                               Invoke-CsprojRepair
