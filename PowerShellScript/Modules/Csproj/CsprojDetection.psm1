# CsprojDetection.psm1 - .csproj 检测功能模块
# 提供 .csproj 文件的只读检测功能
# 依赖：FileSystemHelpers.psm1, XmlHelpers.psm1
#
# 本模块是 CsprojFixEngine 的子模块，专注于检测功能

<#
.SYNOPSIS
    检测项目目录下多余的 BuildOutput\ 目录
.DESCRIPTION
    扫描所有 .csproj 所在目录，查找项目本地的 BuildOutput\ 目录
    这些目录是由于 Directory.Build.props 未正确加载时产生的错误输出
.PARAMETER csprojFiles
    csproj 文件列表
.PARAMETER rootDir
    解决方案根目录（用于排除根 BuildOutput\）
.OUTPUTS
    [hashtable[]] 包含 ProjectDir, BuildOutputDir, SizeMB 的哈希表数组
#>
function Get-ProjectLocalBuildOutput {
    param(
        [System.IO.FileInfo[]]$csprojFiles,
        [string]$rootDir
    )
    $rootBuildOutput = Join-Path $rootDir "BuildOutput"
    $projectDirs = $csprojFiles | ForEach-Object { $_.DirectoryName } | Select-Object -Unique
    $foundDirs = Get-ChildDirectoriesByName -parentDirs $projectDirs -dirName "BuildOutput" -excludePath $rootBuildOutput

    $result = $foundDirs | ForEach-Object {
        $item = $_
        $csproj = $csprojFiles | Where-Object { $_.DirectoryName -eq $item.ParentDir } | Select-Object -First 1
        @{
            ProjectDir = $item.ParentDir
            BuildOutputDir = $item.ChildDir
            SizeMB = $item.SizeMB
            ProjectName = if ($csproj) { $csproj.BaseName } else { (Split-Path $item.ParentDir -Leaf) }
        }
    }
    return $result
}

<#
.SYNOPSIS
    检查 TargetFramework 配置情况
.DESCRIPTION
    分析所有项目的 TargetFramework 配置，返回分类统计信息。
    ★ 重构：使用 Get-CsprojPropertyValues 统一解析 XML，替代正则表达式
.PARAMETER csprojFiles
    csproj 文件列表
.OUTPUTS
    [hashtable] 包含 missingTargetFramework, hasWindowsTargetFramework,
                hasMultiTargetFramework, hasConditionalTargetFramework 的哈希表
#>
function Get-TargetFrameworkConfiguration {
    param([System.IO.FileInfo[]]$csprojFiles)

    $result = @{
        missingTargetFramework = @()
        hasWindowsTargetFramework = @()
        hasMultiTargetFramework = @()
        hasConditionalTargetFramework = @()
    }

    foreach ($csproj in $csprojFiles) {
        # ★ 使用 Get-CsprojPropertyValues 统一解析 XML（单文件模式）
        $props = Get-CsprojPropertyValues -csprojPath $csproj.FullName

        $hasMultiTargetFrameworks = ($props.TargetFrameworks -ne $null -and $props.TargetFrameworks -ne '')
        $hasExplicitTargetFramework = ($props.TargetFramework -ne $null -and $props.TargetFramework -ne '')
        $hasConditionalTf = ($props.TargetFramework_Conditional -ne $null -and $props.TargetFramework_Conditional -ne '')

        if ($hasMultiTargetFrameworks) {
            $result.hasMultiTargetFramework += $csproj
            if ($props.TargetFrameworks -match 'windows') {
                $result.hasWindowsTargetFramework += $csproj
            }
            if ($hasConditionalTf) {
                $result.hasConditionalTargetFramework += $csproj
            }
        } elseif ($hasExplicitTargetFramework) {
            if ($props.TargetFramework -match 'windows') {
                $result.hasWindowsTargetFramework += $csproj
            }
            if ($hasConditionalTf) {
                $result.hasConditionalTargetFramework += $csproj
            }
        } else {
            $result.missingTargetFramework += $csproj
        }
    }

    return $result
}

# 导出公共函数
Export-ModuleMember -Function Get-ProjectLocalBuildOutput,
                               Get-TargetFrameworkConfiguration
