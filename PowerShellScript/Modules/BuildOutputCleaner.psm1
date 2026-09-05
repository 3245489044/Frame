# BuildOutputCleaner.psm1 - 构建输出清理引擎
# 提供构建输出目录的清理和检查功能
# 依赖：FileSystemHelpers.psm1, InteractiveHelpers.psm1

<#
.SYNOPSIS
    获取 BuildOutput 根目录下不符合 $(Configuration)\$(MSBuildProjectName)\ 模式的旧目录
.DESCRIPTION
    检查 BuildOutput 根目录下的直接子目录，排除合法的 Configuration 目录和 obj 目录
.PARAMETER rootBuildOutput
    BuildOutput 根目录路径
.PARAMETER knownConfigurations
    已知合法的 Configuration 名称数组
.OUTPUTS
    [string[]] 需要清理的旧目录路径列表
#>
function Get-ObsoleteBuildOutputDirectories {
    param(
        [string]$rootBuildOutput,
        [string[]]$knownConfigurations
    )
    if (-not (Test-Path $rootBuildOutput)) {
        return @()
    }

    $obsoleteDirs = Get-ChildItem -Path $rootBuildOutput -Directory | Where-Object {
        $_.Name -notin $knownConfigurations -and $_.Name -ne 'obj'
    }
    return ($obsoleteDirs | ForEach-Object { $_.FullName })
}

<#
.SYNOPSIS
    检查项目输出目录完整性
.DESCRIPTION
    检查指定 Configuration 下所有项目是否有对应的输出目录
.PARAMETER rootBuildOutput
    BuildOutput 根目录路径
.PARAMETER csprojFiles
    csproj 文件列表
.PARAMETER configurations
    要检查的 Configuration 数组（如 @('Debug', 'Release')）
.OUTPUTS
    [hashtable] 每个 Configuration 的缺失项目列表
#>
function Check-OutputDirectoryIntegrity {
    param(
        [string]$rootBuildOutput,
        [System.IO.FileInfo[]]$csprojFiles,
        [string[]]$configurations = @('Debug', 'Release')
    )
    $result = @{}

    foreach ($config in $configurations) {
        $configOutputDir = Join-Path $rootBuildOutput $config
        if (-not (Test-Path $configOutputDir)) {
            $result[$config] = @{
                exists = $false
                missingProjects = @()
            }
            continue
        }

        $projectsInConfig = Get-ChildItem -Path $configOutputDir -Directory | ForEach-Object { $_.Name }

        $missingInConfig = @()
        foreach ($csproj in $csprojFiles) {
            $projectName = $csproj.BaseName
            if ($projectName -notin $projectsInConfig) {
                $missingInConfig += $csproj
            }
        }

        $result[$config] = @{
            exists = $true
            missingProjects = $missingInConfig
        }
    }

    return $result
}

<#
.SYNOPSIS
    检查 TargetFramework 子目录完整性
.DESCRIPTION
    检查指定 Configuration 输出目录下每个项目是否包含 TargetFramework 子目录
.PARAMETER rootBuildOutput
    BuildOutput 根目录路径
.PARAMETER configuration
    要检查的 Configuration 名称（默认 Debug）
.OUTPUTS
    [hashtable] 包含 withTargetFramework 和 withoutTargetFramework 的项目名称列表
#>
function Check-TargetFrameworkSubdirectories {
    param(
        [string]$rootBuildOutput,
        [string]$configuration = 'Debug'
    )
    $configOutputDir = Join-Path $rootBuildOutput $configuration

    $result = @{
        withTargetFramework = @()
        withoutTargetFramework = @()
    }

    if (-not (Test-Path $configOutputDir)) {
        return $result
    }

    foreach ($projectDir in (Get-ChildItem -Path $configOutputDir -Directory)) {
        $subDirs = Get-ChildItem -Path $projectDir.FullName -Directory
        # 过滤掉非 TargetFramework 命名的子目录（如 bin、obj 等）
        $tfmSubDirs = $subDirs | Where-Object { $_.Name -match '^net\d+' }
        if ($tfmSubDirs.Count -gt 0) {
            $result.withTargetFramework += $projectDir.Name
        } else {
            $result.withoutTargetFramework += $projectDir.Name
        }
    }

    return $result
}

<#
.SYNOPSIS
    显示输出目录完整性检查结果
.DESCRIPTION
    格式化显示每个 Configuration 的检查结果
.PARAMETER integrityResult
    Check-OutputDirectoryIntegrity 的返回结果
.PARAMETER csprojFiles
    csproj 文件列表
#>
function Show-IntegrityCheckResult {
    param(
        [hashtable]$integrityResult,
        [System.IO.FileInfo[]]$csprojFiles
    )
    foreach ($config in $integrityResult.Keys) {
        $info = $integrityResult[$config]
        if (-not $info.exists) {
            Write-Host "  [$config] 输出目录不存在（可能尚未以该配置构建）。" -ForegroundColor Gray
            continue
        }

        if ($info.missingProjects.Count -eq 0) {
            Write-Host "  [$config] 所有项目在 BuildOutput\$config\ 下都有对应的输出目录。" -ForegroundColor Green
        } else {
            Write-Host "  [$config] 以下项目在 BuildOutput\$config\ 下缺少输出目录（可能尚未构建）：" -ForegroundColor Yellow
            foreach ($p in $info.missingProjects) {
                Write-Host "    - $($p.Name) ($($p.DirectoryName))" -ForegroundColor Yellow
            }
        }
    }
}

# 导出公共函数
Export-ModuleMember -Function Get-ObsoleteBuildOutputDirectories,
                              Check-OutputDirectoryIntegrity,
                              Check-TargetFrameworkSubdirectories,
                              Show-IntegrityCheckResult
