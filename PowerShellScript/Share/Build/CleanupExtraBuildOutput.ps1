# 构建输出清理与项目配置修复脚本
# 功能：
#   0. 自动修复 .csproj 中的配置问题（备份原文件）
#      0.1 修复无效的 TargetFramework（如 net10.0-windows7.0 → net10.0-windows）
#      0.2 移除自定义的 OutputPath（让 Directory.Build.props 的全局配置生效）
#      0.3 补充缺失的 UseWindowsForms（当项目引用 WinForms 项目时）
#      0.4 移除与全局一致的冗余配置（如 TargetFramework 与全局相同可移除）
#      0.5 修复 Directory.Build.props 中的 $(SolutionDir) 问题
#   1. 检查新建项目是否缺少必要的 TargetFramework 配置（导致输出路径不一致）
#   2. 删除每个项目目录下多余的 bin\ 和 obj\ 目录（输出已统一到根 BuildOutput\）
#   3. 清理 BuildOutput 根目录下不符合 $(Configuration)\$(MSBuildProjectName)\ 模式的旧目录
#   4. 检查项目输出目录完整性
#   5. 检测项目目录下多余的 BuildOutput\ 并修复 .csproj 配置
#
# 模块化重构版本：核心逻辑已提取到 Script\Modules\ 目录下的独立模块中
# @requires Write-Header, Write-Success, Write-Info, Write-SubHeader, Write-Warning, Write-Error
# @requires Confirm-Action, Invoke-Step, Invoke-VerificationCheck
# @requires Test-CommandExists, Get-ProjectRoot, Get-ProjectDirectories
# @requires Get-ProjectConfig, Get-GlobalTargetFramework, Get-InvalidTfmMap
# @requires Get-WinFormsDependentProjects, Get-RedundantPlatforms
# @requires Backup-File, Get-DirectorySizeMB, Remove-DirectoryWithLog
# @requires Get-CsprojFiles, Get-BinObjDirectories, Test-ProjectReferences
# @requires Get-ChildDirectoriesByName
# @requires Read-XmlFile, Test-XmlElementExists, Test-ContentHasElement
# @requires Remove-XmlElementLines, Get-MsbuildNamespaceManager, Select-MsbuildNodes
# @requires Update-XmlElementValue, Add-XmlElementAfterSibling, Get-CsprojPropertyValues
# @requires Confirm-And-DeleteDirectories, Confirm-And-FixProjects, Group-ByFilePath

# ============================================================
# 加载模块
# ============================================================
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$moduleDir = Resolve-Path (Join-Path $scriptDir "..\Modules")
. "$moduleDir\Import-Modules.ps1"


# ============================================================
# 配置
# ============================================================
$rootDir = Resolve-Path (Join-Path $scriptDir "..")
$rootBuildOutput = Join-Path $rootDir "BuildOutput"
$backupDir = Join-Path $scriptDir "backups"

# 已知的合法 Configuration 名称（可扩展）
$knownConfigurations = @('Debug', 'Release', 'Staging', 'Test', 'Beta')

# 全局 TargetFramework（来自 Directory.Build.props，优先从 ProjectConfig 模块获取）
try {
    $globalTargetFramework = Get-GlobalTargetFramework
} catch {
    $globalTargetFramework = 'net10.0'
}

# 定义无效 TFM 映射表（优先从 ProjectConfig 模块获取）
try {
    $invalidTfmMap = Get-InvalidTfmMap
} catch {
    $invalidTfmMap = @{ 'net10.0-windows7.0' = 'net10.0-windows' }
}

# 需要 UseWindowsForms 的项目引用关系（优先从 ProjectConfig 模块获取）
try {
    $winFormsDependentProjects = Get-WinFormsDependentProjects
} catch {
    $winFormsDependentProjects = @(
        'WdUI.csproj',
        'FileDriven.UI.csproj',
        'BasicMethod.csproj'
    )
}


Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  构建输出清理与项目配置修复" -ForegroundColor Cyan
Write-Host "  根目录: $rootDir" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# ============================================================
# 获取所有 csproj 文件
# ============================================================
$csprojFiles = Get-CsprojFiles -rootDir $rootDir -excludePaths @(Join-Path $rootDir "BuildOutput")

# ============================================================
# 步骤 0：自动修复 .csproj 配置问题（复用 CsprojFixEngine）
# ============================================================
Write-Host "╔════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  步骤 0：自动修复 .csproj 配置问题            ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

$fixSummaryWrapper = @{Value = @()}  # 记录修复摘要（通过哈希表包装传递引用）

# ----- 步骤 0.1：修复无效的 TargetFramework -----
Write-Host "【步骤 0.1】检查并修复无效的 TargetFramework..." -ForegroundColor Yellow
$fixedCount = Fix-InvalidTargetFramework -csprojFiles $csprojFiles -invalidTfmMap $invalidTfmMap -backupDir $backupDir
if ($fixedCount -gt 0) { $fixSummaryWrapper.Value += "修复了 $fixedCount 个无效 TargetFramework" }
Write-Host ""

# ----- 步骤 0.2：移除自定义的 OutputPath -----
Write-Host "【步骤 0.2】检查并移除自定义的 OutputPath..." -ForegroundColor Yellow
$fixedCount = Fix-CustomOutputPath -csprojFiles $csprojFiles -backupDir $backupDir
if ($fixedCount -gt 0) { $fixSummaryWrapper.Value += "移除了 $fixedCount 个自定义 OutputPath" }
Write-Host ""

# ----- 步骤 0.3：补充缺失的 UseWindowsForms -----
Write-Host "【步骤 0.3】检查并补充缺失的 UseWindowsForms..." -ForegroundColor Yellow
$fixedCount = Fix-MissingUseWindowsForms -csprojFiles $csprojFiles -winFormsDependentProjects $winFormsDependentProjects -backupDir $backupDir
if ($fixedCount -gt 0) { $fixSummaryWrapper.Value += "为 $fixedCount 个项目添加了 UseWindowsForms=true" }
Write-Host ""

# ----- 步骤 0.4：移除与全局一致的冗余配置 -----
Write-Host "【步骤 0.4】检查并移除与全局一致的冗余 TargetFramework..." -ForegroundColor Yellow
$fixedCount = Fix-RedundantTargetFramework -csprojFiles $csprojFiles -globalTargetFramework $globalTargetFramework -backupDir $backupDir
if ($fixedCount -gt 0) { $fixSummaryWrapper.Value += "移除了 $fixedCount 个冗余 TargetFramework 配置" }
Write-Host ""

# 显示步骤 0.1-0.4 的修复摘要
Show-FixSummary -summary $fixSummaryWrapper.Value -backupDir $backupDir

# ----- 步骤 0.5：修复 Directory.Build.props 中的 $(SolutionDir) 问题 -----
Write-Host "【步骤 0.5】检查并修复 Directory.Build.props 中的 SolutionDir 引用..." -ForegroundColor Yellow
$directoryBuildProps = Join-Path $rootDir "Directory.Build.props"
$fixed = Fix-SolutionDirInDirectoryBuildProps -directoryBuildPropsPath $directoryBuildProps -backupDir $backupDir
Write-Host ""

# ============================================================
# 步骤 1：检查新建项目是否缺少 TargetFramework 配置
# ============================================================
Write-Host "╔════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  步骤 1：检查 TargetFramework 配置             ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# 注意：Directory.Build.props 中定义了全局 TargetFramework=net10.0
# 但有些项目需要 net10.0-windows（WinForms/WPF 相关），必须在 .csproj 中显式覆盖
# 如果新建项目没有显式设置 TargetFramework，会使用全局的 net10.0
# 这会导致输出路径为 BuildOutput\Debug\项目名\net10.0\ 而不是 net10.0-windows\
# 从而与引用它的项目（期望 net10.0-windows）产生路径不一致

$tfAnalysis = Get-TargetFrameworkConfiguration -csprojFiles $csprojFiles

Write-Host "  共 $($csprojFiles.Count) 个项目：" -ForegroundColor Gray
Write-Host "    - $($tfAnalysis.hasWindowsTargetFramework.Count) 个项目显式设置了 windows 相关的 TargetFramework" -ForegroundColor Gray
if ($tfAnalysis.hasWindowsTargetFramework.Count -gt 0) {
    foreach ($p in $tfAnalysis.hasWindowsTargetFramework) {
        Write-Host "      * $($p.Name) ($($p.DirectoryName))" -ForegroundColor DarkGray
    }
}
if ($tfAnalysis.hasMultiTargetFramework.Count -gt 0) {
    Write-Host "    - $($tfAnalysis.hasMultiTargetFramework.Count) 个项目使用了多框架编译 (TargetFrameworks)" -ForegroundColor Gray
    foreach ($p in $tfAnalysis.hasMultiTargetFramework) {
        Write-Host "      * $($p.Name) ($($p.DirectoryName))" -ForegroundColor DarkGray
    }
}
if ($tfAnalysis.hasConditionalTargetFramework.Count -gt 0) {
    Write-Host "    - $($tfAnalysis.hasConditionalTargetFramework.Count) 个项目使用了条件性 TargetFramework" -ForegroundColor Gray
    foreach ($p in $tfAnalysis.hasConditionalTargetFramework) {
        Write-Host "      * $($p.Name) ($($p.DirectoryName))" -ForegroundColor DarkGray
    }
}
Write-Host "    - $($tfAnalysis.missingTargetFramework.Count) 个项目没有显式 TargetFramework（使用全局 net10.0）" -ForegroundColor Gray
if ($tfAnalysis.missingTargetFramework.Count -gt 0) {
    foreach ($p in $tfAnalysis.missingTargetFramework) {
        Write-Host "      * $($p.Name) ($($p.DirectoryName))" -ForegroundColor DarkGray
    }
}
Write-Host ""

# ============================================================
# 步骤 2：删除每个项目目录下多余的 bin\ 和 obj\ 目录
# ============================================================
Write-Host "╔════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  步骤 2：删除项目目录下多余的 bin\ 和 obj\    ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

$binObjDirs = Get-BinObjDirectories -csprojFiles $csprojFiles -excludePaths @(
    (Join-Path $rootBuildOutput "obj")
)

Confirm-And-DeleteDirectories -dirList $binObjDirs -promptMessage "确认删除以上所有 bin\ 和 obj\ 目录？(Y/N/S=逐项确认)"
Write-Host ""

# ============================================================
# 步骤 3：清理 BuildOutput 根目录下不符合 $(Configuration)\$(MSBuildProjectName)\ 模式的旧目录
# ============================================================
Write-Host "╔════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  步骤 3：清理 BuildOutput 下不一致的旧目录    ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# 根据 Directory.Build.props 的配置：
#   OutputPath = $(SolutionDir)BuildOutput\$(Configuration)\$(MSBuildProjectName)\
# 正确的模式是：BuildOutput\Debug\项目名\ 或 BuildOutput\Release\项目名\
# 根目录下不应该有直接的项目目录（如 BuildOutput\BasicMethod.ConfigurationModels\）
#
# 注意：Directory.Build.props 中 AppendTargetFrameworkToOutputPath=true，
# 实际输出路径为 BuildOutput\Debug\项目名\net10.0\ 或 net10.0-windows\

Write-Host "  已知合法的 Configuration: $($knownConfigurations -join ', ')" -ForegroundColor Gray
$customConfig = Read-Host "  如有其他合法的 Configuration 名称，请用逗号分隔输入（直接回车跳过）"
if ($customConfig -and $customConfig.Trim() -ne '') {
    $knownConfigurations += $customConfig.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' }
    $knownConfigurations = $knownConfigurations | Select-Object -Unique
}

$obsoleteDirs = Get-ObsoleteBuildOutputDirectories -rootBuildOutput $rootBuildOutput -knownConfigurations $knownConfigurations
Confirm-And-DeleteDirectories -dirList $obsoleteDirs -promptMessage "确认删除以上不一致的旧目录？(Y/N/S=逐项确认)"
Write-Host ""

# ============================================================
# 步骤 4：检查项目输出目录完整性（同时检查 Debug 和 Release）
# ============================================================
Write-Host "╔════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  步骤 4：检查项目输出目录完整性                ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

$integrityResult = Check-OutputDirectoryIntegrity -rootBuildOutput $rootBuildOutput -csprojFiles $csprojFiles -configurations @('Debug', 'Release')
Show-IntegrityCheckResult -integrityResult $integrityResult -csprojFiles $csprojFiles

# 额外检查 TargetFramework 子目录是否存在
Write-Host ""
Write-Host "  [额外检查] 检查 TargetFramework 子目录完整性..." -ForegroundColor Gray
$tfSubDirResult = Check-TargetFrameworkSubdirectories -rootBuildOutput $rootBuildOutput
Write-Host "    - $($tfSubDirResult.withTargetFramework.Count) 个项目包含 TargetFramework 子目录" -ForegroundColor Gray
if ($tfSubDirResult.withoutTargetFramework.Count -gt 0) {
    Write-Host "    - $($tfSubDirResult.withoutTargetFramework.Count) 个项目缺少 TargetFramework 子目录（可能为旧构建产物）" -ForegroundColor Gray
}

Write-Host ""

# ============================================================
# 步骤 5：检测项目目录下多余的 BuildOutput\ 并修复 .csproj 配置
# ============================================================
Write-Host "╔════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  步骤 5：检测项目本地 BuildOutput\ 并修复配置 ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

$localBuildOutputs = Get-ProjectLocalBuildOutput -csprojFiles $csprojFiles -rootDir $rootDir

if ($localBuildOutputs.Count -eq 0) {
    Write-Host "  未发现项目本地 BuildOutput\ 目录，配置正确。" -ForegroundColor Green
} else {
    Write-Host "  发现 $($localBuildOutputs.Count) 个项目存在本地 BuildOutput\ 目录：" -ForegroundColor Yellow
    foreach ($item in $localBuildOutputs) {
        Write-Host "    - [$($item.SizeMB) MB] $($item.ProjectName) ($($item.BuildOutputDir))" -ForegroundColor Magenta
    }
    Write-Host ""
    Write-Host "  这些目录是由于 Directory.Build.props 未正确加载时产生的错误输出。" -ForegroundColor Gray
    Write-Host "  建议先修复 .csproj 配置，再清理这些目录。" -ForegroundColor Gray
    Write-Host ""

    $confirm = Read-Host "  是否执行一键修复（检测→修复 .csproj 配置→清理）？(Y/N)"
    if ($confirm -eq 'Y' -or $confirm -eq 'y') {
        Invoke-CsprojRepair -rootDir $rootDir -backupDir $backupDir `
            -invalidTfmMap $invalidTfmMap `
            -globalTargetFramework $globalTargetFramework `
            -winFormsDependentProjects $winFormsDependentProjects
    } else {
        Write-Host "  已跳过步骤 5。" -ForegroundColor Gray
    }
}

Write-Host ""

# ============================================================
# 完成
# ============================================================
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  清理完成" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "📌 新建项目注意事项：" -ForegroundColor Yellow
Write-Host "  1. 新建项目时，.csproj 中不要设置 <OutputPath>，让 Directory.Build.props 的全局配置生效" -ForegroundColor Gray
Write-Host "  2. 如果项目需要 WinForms/WPF 支持（引用 WdUI、FileDriven.UI 等），必须在 .csproj 中显式设置：" -ForegroundColor Gray
Write-Host "     <TargetFramework>net10.0-windows</TargetFramework>" -ForegroundColor Gray
Write-Host "  3. 如果项目是纯类库（不依赖 Windows 特定 API），可以省略 TargetFramework，使用全局 net10.0" -ForegroundColor Gray
Write-Host "  4. 首次构建后，运行此脚本清理项目本地的 bin\ 和 obj\ 目录" -ForegroundColor Gray
Write-Host "  5. 如果构建后出现重复输出目录，运行 'dotnet clean' 清理所有构建缓存" -ForegroundColor Gray
