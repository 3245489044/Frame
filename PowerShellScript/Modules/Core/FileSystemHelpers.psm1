# FileSystemHelpers.psm1 - 文件系统工具模块
# 提供文件备份、目录大小计算、目录删除等通用功能

<#
.SYNOPSIS
    备份文件到指定备份目录
.DESCRIPTION
    在备份目录中创建带时间戳的文件副本
.PARAMETER filePath
    要备份的文件路径
.PARAMETER backupDir
    备份目录路径（可选，默认为脚本所在目录下的 backups 文件夹）
.OUTPUTS
    [string] 备份文件的完整路径
#>
function Backup-File {
    <#
    .SYNOPSIS
        备份文件到指定备份目录
    .DESCRIPTION
        在备份目录中创建带时间戳的文件副本。
        增强版：支持自定义备份目录，默认备份到脚本所在目录下的 backups 文件夹。
        如果未指定 backupDir 且文件路径包含目录，则默认使用文件所在目录下的 backups 子目录。
    .PARAMETER filePath
        要备份的文件路径
    .PARAMETER backupDir
        备份目录路径（可选）
        - 未指定时：优先使用文件所在目录下的 backups 子目录
        - 如果文件所在目录不可写，回退到脚本所在目录下的 backups 文件夹
    .PARAMETER UseFileDir
        是否优先使用文件所在目录下的 backups 子目录（默认：$true）
    .OUTPUTS
        [string] 备份文件的完整路径
    .EXAMPLE
        Backup-File -filePath "C:\Project\src\file.txt"
        # 备份到 C:\Project\src\backups\20250101_120000_file.txt
    .EXAMPLE
        Backup-File -filePath "file.txt" -backupDir "C:\backups"
        # 备份到 C:\backups\20250101_120000_file.txt
    #>
    param(
        [string]$filePath,
        [string]$backupDir,
        [switch]$UseFileDir = $true
    )
    if (-not (Test-Path $filePath)) { return $null }

    # 确定备份目录
    if (-not $backupDir) {
        if ($UseFileDir) {
            $fileParent = Split-Path -Parent $filePath -ErrorAction SilentlyContinue
            if ($fileParent) {
                $backupDir = Join-Path $fileParent "backups"
            } else {
                $backupDir = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "backups"
            }
        } else {
            $backupDir = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "backups"
        }
    }
    if (-not (Test-Path $backupDir)) {
        New-Item -Path $backupDir -ItemType Directory -Force | Out-Null
    }
    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $fileName = [System.IO.Path]::GetFileName($filePath)
    $backupPath = Join-Path $backupDir "${timestamp}_${fileName}"
    Copy-Item -Path $filePath -Destination $backupPath -Force
    return $backupPath
}

<#
.SYNOPSIS
    计算目录大小（MB）
.DESCRIPTION
    递归计算目录中所有文件的总大小，以 MB 为单位返回
.PARAMETER dirPath
    目录路径
.OUTPUTS
    [double] 目录大小（MB），保留两位小数
#>
function Get-DirectorySizeMB {
    param([string]$dirPath)
    if (-not (Test-Path $dirPath)) { return 0 }
    $size = (Get-ChildItem -Path $dirPath -Recurse -File -ErrorAction SilentlyContinue |
             Measure-Object -Property Length -Sum).Sum
    return [math]::Round($size / 1MB, 2)
}

<#
.SYNOPSIS
    删除目录并返回是否成功
.DESCRIPTION
    递归强制删除目录，失败时输出错误信息
.PARAMETER dirPath
    要删除的目录路径
.OUTPUTS
    [bool] 删除成功返回 $true，失败返回 $false
#>
function Remove-DirectoryWithLog {
    param([string]$dirPath)
    try {
        Remove-Item -Path $dirPath -Recurse -Force -ErrorAction Stop
        return $true
    } catch {
        Write-Host "  [失败] $dirPath - $_" -ForegroundColor Red
        return $false
    }
}

function Get-CsprojFiles {
    <#
    .SYNOPSIS
        递归扫描指定根目录下所有 .csproj 文件
    .DESCRIPTION
        递归搜索 .csproj 文件，可排除指定目录路径（支持子路径匹配）。
        增强版：增加 -ErrorAction SilentlyContinue 避免权限错误中断，
        增加 -File 确保只返回文件，排除逻辑支持路径片段匹配。
    .PARAMETER rootDir
        根目录路径
    .PARAMETER excludePaths
        要排除的路径模式数组（可选），支持路径片段匹配，
        例如传入 'bin' 会排除所有路径中包含 '\bin\' 的 .csproj
    .OUTPUTS
        [System.IO.FileInfo[]] csproj 文件列表
    .EXAMPLE
        Get-CsprojFiles -rootDir "C:\Project" -excludePaths @('node_modules', '.git', 'bin', 'obj')
    #>
    param(
        [string]$rootDir,
        [string[]]$excludePaths = @()
    )
    $files = Get-ChildItem -Path $rootDir -Filter "*.csproj" -Recurse -File -ErrorAction SilentlyContinue

    foreach ($exclude in $excludePaths) {
        # 支持两种排除模式：
        # 1. 精确路径匹配（如 "D:\Project\BuildOutput"）
        # 2. 路径片段匹配（如 "node_modules" 匹配任何包含该片段的路径）
        $files = $files | Where-Object {
            $path = $_.FullName
            $matched = $false
            if ($path -like "$exclude\*" -or $path -like "$exclude") {
                $matched = $true
            }
            elseif ($path -match [regex]::Escape($exclude)) {
                $matched = $true
            }
            -not $matched
        }
    }
    return $files
}

<#
.SYNOPSIS
    获取项目目录下的 bin\ 和 obj\ 目录列表
.DESCRIPTION
    遍历所有 csproj 文件所在目录，收集 bin\ 和 obj\ 目录路径
.PARAMETER csprojFiles
    csproj 文件列表
.PARAMETER excludePaths
    要排除的路径模式数组（可选）
.OUTPUTS
    [string[]] bin\ 和 obj\ 目录路径列表
#>
function Get-BinObjDirectories {
    param(
        [System.IO.FileInfo[]]$csprojFiles,
        [string[]]$excludePaths = @()
    )
    $dirs = @()
    foreach ($csproj in $csprojFiles) {
        $projectDir = $csproj.DirectoryName
        $binDir = Join-Path $projectDir "bin"
        $objDir = Join-Path $projectDir "obj"
        if (Test-Path $binDir) { $dirs += $binDir }
        if (Test-Path $objDir) { $dirs += $objDir }
    }
    foreach ($exclude in $excludePaths) {
        $dirs = $dirs | Where-Object {
            $_ -ne $exclude -and $_ -notlike "$exclude\*"
        }
    }
    return $dirs
}

<#
.SYNOPSIS
    检查 .csproj 中的 ProjectReference 路径是否正确
.DESCRIPTION
    遍历所有 .csproj 文件，检查 ProjectReference 的 Include 路径指向的文件是否存在。
    支持相对路径和绝对路径的解析。
.PARAMETER csprojFiles
    csproj 文件列表（System.IO.FileInfo[]）
.OUTPUTS
    [hashtable[]] 包含 CsprojFile, ReferencePath, FullPath, Exists 的哈希表数组
.EXAMPLE
    Test-ProjectReferences -csprojFiles $csprojFiles
#>
function Test-ProjectReferences {
    param([System.IO.FileInfo[]]$csprojFiles)

    $issues = @()
    foreach ($csproj in $csprojFiles) {
        $content = Get-Content -Path $csproj.FullName -Raw
        $projectDir = $csproj.DirectoryName

        # 匹配所有 ProjectReference
        $refMatches = [regex]::Matches($content, '<ProjectReference\s+Include="([^"]+)"')
        foreach ($match in $refMatches) {
            $refPath = $match.Groups[1].Value
            $fullPath = if ([System.IO.Path]::IsPathRooted($refPath)) {
                $refPath
            } else {
                [System.IO.Path]::GetFullPath((Join-Path $projectDir $refPath))
            }

            if (-not (Test-Path $fullPath)) {
                $issues += @{
                    CsprojFile = $csproj
                    ReferencePath = $refPath
                    FullPath = $fullPath
                    Exists = $false
                }
            }
        }
    }

    return $issues
}

<#
.SYNOPSIS
    在多个父目录中查找指定名称的子目录
.DESCRIPTION
    给定一组父目录路径，在每个父目录中查找指定名称的子目录，
    并可选择排除特定路径（如根目录下的同名目录）。
.PARAMETER parentDirs
    父目录路径数组
.PARAMETER dirName
    要查找的子目录名称（如 "BuildOutput"、"bin"、"obj" 等）
.PARAMETER excludePath
    要排除的完整路径（可选），匹配此路径的目录将被过滤掉
.OUTPUTS
    [hashtable[]] 包含 ParentDir, ChildDir, SizeMB 的哈希表数组
.EXAMPLE
    Get-ChildDirectoriesByName -parentDirs @("C:\Project\Sub1", "C:\Project\Sub2") -dirName "BuildOutput" -excludePath "C:\Project\BuildOutput"
#>
function Get-ChildDirectoriesByName {
    param(
        [string[]]$parentDirs,
        [string]$dirName,
        [string]$excludePath = ""
    )
    $result = @()
    foreach ($parentDir in $parentDirs) {
        $childDir = Join-Path $parentDir $dirName
        if (Test-Path $childDir) {
            if ($excludePath -and ($childDir -eq $excludePath)) {
                continue
            }
            $sizeMB = Get-DirectorySizeMB -dirPath $childDir
            $result += @{
                ParentDir = $parentDir
                ChildDir = $childDir
                SizeMB = $sizeMB
                DirName = $dirName
            }
        }
    }
    return $result
}

# 导出公共函数
Export-ModuleMember -Function Backup-File,
                              Get-DirectorySizeMB,
                              Remove-DirectoryWithLog,
                              Get-CsprojFiles,
                              Get-BinObjDirectories,
                              Test-ProjectReferences,
                              Get-ChildDirectoriesByName
