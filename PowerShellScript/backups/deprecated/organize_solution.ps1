<#
.SYNOPSIS
    ⚠️ 已废弃 — 整理 WorkFlowPrj.sln 解决方案文件
.DESCRIPTION
    ⚠️ 此脚本已被 Sync-SlnToDisk.ps1 取代。
    原因：
    1. 不保留现有项目 GUID，每次运行都会生成新 GUID，破坏 git 历史
    2. 硬编码 6 个平台配置（Debug/Release × Any CPU/x64/x86），生成大量冗余配置
    3. Sync-SlnToDisk.ps1 功能更完善：保留 GUID、保留构建配置、支持 -Check/-Force 模式
    
    建议使用 Sync-SlnToDisk.ps1 替代。
#>

param(
    [string]$SolutionPath = "..\..\WorkFlowPrj.sln",
    [string]$ProjectRoot = "..\.."
)

# 获取绝对路径
$SolutionPath = Resolve-Path (Join-Path $PSScriptRoot $SolutionPath)
$ProjectRoot = Resolve-Path (Join-Path $PSScriptRoot $ProjectRoot)

Write-Host "解决方案文件: $SolutionPath" -ForegroundColor Cyan
Write-Host "项目根目录: $ProjectRoot" -ForegroundColor Cyan

# ============================================================
# 1. 读取当前 .sln 文件内容
# ============================================================
$slnContent = Get-Content $SolutionPath -Raw
$slnLines = Get-Content $SolutionPath

# ============================================================
# 2. 解析 .sln 中现有的项目
# ============================================================
$existingProjects = @()  # 格式: [PSCustomObject]@{Name=; Path=; Guid=; TypeGuid=}
$currentSection = ""

foreach ($line in $slnLines) {
    if ($line -match '^Project\("(?<TypeGuid>[^"]+)"\)\s*=\s*"(?<Name>[^"]+)",\s*"(?<Path>[^"]+)",\s*"(?<Guid>[^"]+)"') {
        $existingProjects += [PSCustomObject]@{
            Name     = $matches['Name']
            Path     = $matches['Path']
            Guid     = $matches['Guid']
            TypeGuid = $matches['TypeGuid']
        }
    }
}

Write-Host "当前 .sln 中的项目数: $($existingProjects.Count)" -ForegroundColor Yellow

# ============================================================
# 3. 扫描实际存在的 .csproj 文件
# ============================================================
$actualProjects = @()
Get-ChildItem -Path $ProjectRoot -Recurse -Filter "*.csproj" | ForEach-Object {
    $relativePath = $_.FullName.Substring($ProjectRoot.Length + 1)
    $projectName = $_.BaseName
    $actualProjects += [PSCustomObject]@{
        Name = $projectName
        Path = $relativePath
        FullPath = $_.FullName
    }
}

Write-Host "实际存在的项目数: $($actualProjects.Count)" -ForegroundColor Yellow

# ============================================================
# 4. 找出需要删除的项目（在 .sln 中但实际不存在）
# ============================================================
$projectsToRemove = @()
foreach ($proj in $existingProjects) {
    $fullPath = Join-Path $ProjectRoot $proj.Path
    if (-not (Test-Path $fullPath)) {
        $projectsToRemove += $proj
        Write-Host "  要删除的项目 (文件不存在): $($proj.Name) ($($proj.Path))" -ForegroundColor Red
    }
}

# ============================================================
# 5. 找出需要添加的项目（实际存在但不在 .sln 中）
# ============================================================
$projectsToAdd = @()
foreach ($actual in $actualProjects) {
    $found = $false
    foreach ($existing in $existingProjects) {
        # 比较相对路径（忽略大小写）
        if ($existing.Path -replace '/', '\' -ieq $actual.Path) {
            $found = $true
            break
        }
    }
    if (-not $found) {
        $projectsToAdd += $actual
        Write-Host "  要添加的项目: $($actual.Name) ($($actual.Path))" -ForegroundColor Green
    }
}

# ============================================================
# 6. 生成新的 GUID
# ============================================================
function New-GuidString {
    return [System.Guid]::NewGuid().ToString("D").ToUpper()
}

# C# 项目类型 GUID (FAE04EC0-301F-11D3-BF4B-00C04F79EFBC)
$csharpProjectTypeGuid = "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}"
# Solution Folder 类型 GUID
$solutionFolderTypeGuid = "{2150E333-8FDC-42A3-9474-1A3956D46DE8}"

# ============================================================
# 7. 构建新的项目列表（保留现有 + 添加新的）
# ============================================================
$newProjectList = @()

# 先添加所有现有的项目（除了要删除的）
foreach ($proj in $existingProjects) {
    $shouldRemove = $false
    foreach ($remove in $projectsToRemove) {
        if ($proj.Guid -eq $remove.Guid) {
            $shouldRemove = $true
            break
        }
    }
    if (-not $shouldRemove) {
        $newProjectList += $proj
    }
}

# 再添加新项目
foreach ($proj in $projectsToAdd) {
    $newGuid = "{" + (New-GuidString) + "}"
    $newProjectList += [PSCustomObject]@{
        Name     = $proj.Name
        Path     = $proj.Path
        Guid     = $newGuid
        TypeGuid = $csharpProjectTypeGuid
    }
    Write-Host "  为新项目 $($proj.Name) 生成 GUID: $newGuid" -ForegroundColor Gray
}

# ============================================================
# 8. 按目录结构排序项目
# ============================================================
# 定义排序优先级（按目录深度和名称）
$sortedProjects = $newProjectList | Sort-Object @{
    Expression = {
        $path = $_.Path -replace '\\', '/'
        $parts = $path.Split('/')
        # 根目录项目优先，然后按目录深度和名称排序
        if ($parts.Count -eq 1) {
            return "0_$($parts[0])"
        } else {
            return "1_$($parts[0])_$path"
        }
    }
}

# ============================================================
# 9. 构建 Solution Folder 结构
# ============================================================
# 收集所有目录
$allDirs = @{}
foreach ($proj in $sortedProjects) {
    $path = $proj.Path -replace '\\', '/'
    $parts = $path.Split('/')
    if ($parts.Count -gt 1) {
        $dirName = $parts[0]
        if (-not $allDirs.ContainsKey($dirName)) {
            $allDirs[$dirName] = @{
                Guid = "{" + (New-GuidString) + "}"
                Projects = @()
            }
        }
        $allDirs[$dirName].Projects += $proj
    }
}

# ============================================================
# 10. 生成新的 .sln 文件内容
# ============================================================
$newContent = @"
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.5.320.0
MinimumVisualStudioVersion = 10.0.40219.1

"@

# 添加 Solution Folder 定义
foreach ($dirName in ($allDirs.Keys | Sort-Object)) {
    $folderGuid = $allDirs[$dirName].Guid
    $newContent += "Project(`"$solutionFolderTypeGuid`") = `"$dirName`", `"$dirName`", `"$folderGuid`"`r`n"
    $newContent += "EndProject`r`n"
}

# 添加项目定义
foreach ($proj in $sortedProjects) {
    $newContent += "Project(`"$($proj.TypeGuid)`") = `"$($proj.Name)`", `"$($proj.Path)`", `"$($proj.Guid)`"`r`n"
    $newContent += "EndProject`r`n"
}

# Global 节
$newContent += "Global`r`n"

# SolutionConfigurationPlatforms
$newContent += "`tGlobalSection(SolutionConfigurationPlatforms) = preSolution`r`n"
$newContent += "`t`tDebug|Any CPU = Debug|Any CPU`r`n"
$newContent += "`t`tDebug|x64 = Debug|x64`r`n"
$newContent += "`t`tDebug|x86 = Debug|x86`r`n"
$newContent += "`t`tRelease|Any CPU = Release|Any CPU`r`n"
$newContent += "`t`tRelease|x64 = Release|x64`r`n"
$newContent += "`t`tRelease|x86 = Release|x86`r`n"
$newContent += "`tEndGlobalSection`r`n"

# ProjectConfigurationPlatforms
$newContent += "`tGlobalSection(ProjectConfigurationPlatforms) = postSolution`r`n"
foreach ($proj in $sortedProjects) {
    $guid = $proj.Guid
    $configs = @("Debug|Any CPU", "Debug|x64", "Debug|x86", "Release|Any CPU", "Release|x64", "Release|x86")
    foreach ($config in $configs) {
        $newContent += "`t`t$guid.$config.ActiveCfg = $config`r`n"
        $newContent += "`t`t$guid.$config.Build.0 = $config`r`n"
    }
}
$newContent += "`tEndGlobalSection`r`n"

# SolutionProperties
$newContent += "`tGlobalSection(SolutionProperties) = preSolution`r`n"
$newContent += "`t`tHideSolutionNode = FALSE`r`n"
$newContent += "`tEndGlobalSection`r`n"

# NestedProjects - 建立项目与 Solution Folder 的层级关系
$newContent += "`tGlobalSection(NestedProjects) = preSolution`r`n"
foreach ($dirName in ($allDirs.Keys | Sort-Object)) {
    $folderGuid = $allDirs[$dirName].Guid
    foreach ($proj in $allDirs[$dirName].Projects) {
        $newContent += "`t`t$($proj.Guid) = $folderGuid`r`n"
    }
}
$newContent += "`tEndGlobalSection`r`n"

$newContent += "EndGlobal`r`n"

# ============================================================
# 11. 备份原文件并写入新内容
# ============================================================
$backupPath = $SolutionPath + ".backup"
Copy-Item $SolutionPath $backupPath -Force
Write-Host "已备份原文件到: $backupPath" -ForegroundColor Cyan

Set-Content $SolutionPath $newContent -NoNewline
Write-Host "已更新解决方案文件: $SolutionPath" -ForegroundColor Green

# ============================================================
# 12. 输出统计信息
# ============================================================
Write-Host "`n========== 整理完成 ==========" -ForegroundColor Cyan
Write-Host "删除的项目 (文件不存在): $($projectsToRemove.Count)" -ForegroundColor Red
foreach ($proj in $projectsToRemove) {
    Write-Host "  - $($proj.Name)" -ForegroundColor Red
}
Write-Host "添加的项目: $($projectsToAdd.Count)" -ForegroundColor Green
foreach ($proj in $projectsToAdd) {
    Write-Host "  + $($proj.Name)" -ForegroundColor Green
}
Write-Host "Solution Folder 数量: $($allDirs.Count)" -ForegroundColor Yellow
Write-Host "最终项目总数: $($sortedProjects.Count)" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan
