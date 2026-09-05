<#
.SYNOPSIS
    同步 .sln 解决方案文件与磁盘目录结构 — 让 VS 解决方案资源管理器镜像真实文件层级

.DESCRIPTION
    读取 .sln 文件，按磁盘真实目录结构重新生成虚拟文件夹层级：
    1. 删除 .sln 中磁盘上已不存在的项目
    2. 添加磁盘上存在但 .sln 中未包含的项目
    3. 按磁盘目录树重建 Solution Folder（支持多级嵌套）
    4. 保留现有项目的 GUID（git 历史稳定）
    5. 保留所有构建配置

    用法:
        Sync-SlnToDisk.ps1              交互模式（检查并提示同步）
        Sync-SlnToDisk.ps1 -Check       仅检查差异，不修改
        Sync-SlnToDisk.ps1 -Force       强制同步（跳过确认）
        Sync-SlnToDisk.ps1 -Help        显示帮助

    示例:
        Sync-SlnToDisk.ps1
        Sync-SlnToDisk.ps1 -Check
        Sync-SlnToDisk.ps1 -Force
        Sync-SlnToDisk.ps1 -SolutionPath "D:\Project\Other\Other.sln"
#>

param(
    [string]$SolutionPath = "",
    [switch]$Check = $false,
    [switch]$Force = $false,
    [switch]$Help = $false
)

# ============================================
#  路径配置
# ============================================

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = (Get-Item $scriptDir).Parent.Parent.FullName

if ([string]::IsNullOrEmpty($SolutionPath)) {
    $SolutionPath = Join-Path $rootDir "CsharpPrj\MainProj.sln"
}

# 解决方案目录（用于计算相对路径）
$slnDir = Split-Path -Parent (Resolve-Path $SolutionPath -ErrorAction SilentlyContinue)

# 项目类型 GUID 常量
$csharpProjectTypeGuid = "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}"
$solutionFolderTypeGuid = "{2150E333-8FDC-42A3-9474-1A3956D46DE8}"

# ============================================
#  加载 OutputHelpers 模块
# ============================================

$modulePath = Join-Path $PSScriptRoot "..\Modules\Core\OutputHelpers.psm1"
if (-not (Get-Module -Name OutputHelpers)) {
    Import-Module $modulePath -Force -ErrorAction Stop
}


function Write-Diff {
    param([string]$Text, [string]$Color = "Gray")
    Write-Host "  $Text" -ForegroundColor $Color
}

function New-GuidString {
    return [System.Guid]::NewGuid().ToString("D").ToUpper()
}

# ============================================
#  解析 .sln 文件
# ============================================

function Parse-SolutionFile {
    param([string]$Path)

    # 从脚本作用域获取 GUID 常量
    $folderTypeGuid = $script:solutionFolderTypeGuid

    $result = @{
        Projects = @()          # [PSCustomObject]@{Name; Path; Guid; TypeGuid}
        Folders = @{}           # @{ Guid = @{Name; Path} }
        NestedProjects = @{}    # @{ ChildGuid = ParentGuid }
        ConfigLines = @()       # Global 节中的配置行（保留原样）
        HeaderLines = @()       # 文件头行（Global 之前的所有行，包括项目定义）
        HasNestedProjects = $false
    }

    if (-not (Test-Path $Path)) {
        return $result
    }

    $lines = Get-Content $Path
    $inGlobal = $false
    $inNestedProjects = $false
    $globalLines = @()
    $headerLines = @()

    # 使用 .NET 正则引擎
    $projectRegex = [regex]'^Project\("(?<TypeGuid>[^"]+)"\)\s*=\s*"(?<Name>[^"]+)",\s*"(?<Path>[^"]+)",\s*"(?<Guid>[^"]+)"'
    $nestedRegex = [regex]'^\s+\{(?<Child>[^}]+)\}\s*=\s*\{(?<Parent>[^}]+)\}'
    $globalSectionRegex = [regex]'^\s*GlobalSection\((?<Section>[^)]+)\)'
    $endGlobalSectionRegex = [regex]'^\s*EndGlobalSection'

    foreach ($line in $lines) {
        if (-not $inGlobal) {
            # 还没到 Global 节：收集所有行到 HeaderLines
            if ($line -match "^Global$") {
                $inGlobal = $true
                # Global 行本身也加入 HeaderLines（用于重建文件头）
                $headerLines += $line
            } else {
                $headerLines += $line

                # 同时解析项目条目（项目定义在 Global 之前）
                $projMatch = $projectRegex.Match($line)
                if ($projMatch.Success) {
                    # TypeGuid 和 Guid 已经包含花括号（来自 .sln 文件格式）
                    $typeGuid = $projMatch.Groups['TypeGuid'].Value
                    $projGuid = $projMatch.Groups['Guid'].Value
                    $projName = $projMatch.Groups['Name'].Value
                    $projPath = $projMatch.Groups['Path'].Value

                    if ($typeGuid -eq $folderTypeGuid) {
                        # 虚拟文件夹
                        $result.Folders[$projGuid] = @{
                            Name = $projName
                            Path = $projPath
                        }
                    } else {
                        # 实际项目
                        $result.Projects += [PSCustomObject]@{
                            Name     = $projName
                            Path     = $projPath
                            Guid     = $projGuid
                            TypeGuid = $typeGuid
                        }
                    }
                }
            }
        } else {
            # 在 Global 节中
            if ($line -match "^EndGlobal$") {
                $inGlobal = $false
                continue
            }

            $sectionMatch = $globalSectionRegex.Match($line)
            if ($sectionMatch.Success) {
                $sectionName = $sectionMatch.Groups['Section'].Value
                if ($sectionName -eq 'NestedProjects') {
                    $inNestedProjects = $true
                    $result.HasNestedProjects = $true
                } else {
                    $globalLines += $line
                }
                continue
            }

            if ($inNestedProjects) {
                if ($endGlobalSectionRegex.Match($line).Success) {
                    $inNestedProjects = $false
                    continue
                }
                # 解析嵌套关系: {ChildGuid} = {ParentGuid}
                $nestedMatch = $nestedRegex.Match($line)
                if ($nestedMatch.Success) {
                    $childGuid = "{" + $nestedMatch.Groups['Child'].Value + "}"
                    $parentGuid = "{" + $nestedMatch.Groups['Parent'].Value + "}"
                    $result.NestedProjects[$childGuid] = $parentGuid
                }
                continue
            }

            # 收集所有非 NestedProjects 的 Global 行（保留原样）
            $globalLines += $line
        }
    }

    # 只保留文件头（Global 之前的非项目行）
    $cleanHeaderLines = @()
    $headerProjectRegex = [regex]'^Project\('
    foreach ($line in $headerLines) {
        if ($line -match "^Global$") {
            $cleanHeaderLines += $line
            break
        }
        if (-not $headerProjectRegex.Match($line).Success -and $line -notmatch "^EndProject$") {
            $cleanHeaderLines += $line
        }
    }

    $result.HeaderLines = $cleanHeaderLines
    $result.ConfigLines = $globalLines
    return $result
}

# ============================================
#  扫描磁盘上的 .csproj 文件
# ============================================

function Scan-DiskProjects {
    param([string]$RootDir)

    $projects = @()
    $excludeDirs = @(
        "BuildOutput",
        "bin",
        "obj",
        "node_modules",
        ".git",
        ".vs",
        "backups"
    )

    Get-ChildItem -Path $RootDir -Recurse -Filter "*.csproj" -ErrorAction SilentlyContinue | ForEach-Object {
        $fullPath = $_.FullName

        # 排除备份目录和构建输出目录
        $shouldExclude = $false
        foreach ($exDir in $excludeDirs) {
            if ($fullPath -match [regex]::Escape("\$exDir\") -or $fullPath -match [regex]::Escape("/$exDir/")) {
                $shouldExclude = $true
                break
            }
        }
        if ($shouldExclude) { return }

        $relativePath = $fullPath.Substring($RootDir.Length + 1)
        $projects += [PSCustomObject]@{
            Name     = $_.BaseName
            Path     = $relativePath
            FullPath = $fullPath
            DirPath  = if ($relativePath.Contains('\')) { [System.IO.Path]::GetDirectoryName($relativePath) } else { "" }
        }
    }

    # 处理同名项目：同名时只保留一个（优先保留路径更短的，排除 TestMent 等可能是旧目录的）
    $nameGroups = @{}
    foreach ($proj in $projects) {
        $name = $proj.Name
        if (-not $nameGroups.ContainsKey($name)) {
            $nameGroups[$name] = @()
        }
        $nameGroups[$name] += $proj
    }

    $deduped = @()
    foreach ($name in $nameGroups.Keys) {
        $group = $nameGroups[$name]
        if ($group.Count -eq 1) {
            $deduped += $group[0]
        } else {
            # 同名项目：优先保留路径更短的（更可能是正确位置）
            $sorted = $group | Sort-Object { $_.Path.Length }
            $kept = $sorted[0]
            Write-Warning "发现同名项目 '$name'，保留: $($kept.Path)，排除其余 $($group.Count - 1) 个"
            $deduped += $kept
        }
    }

    return $deduped
}

# ============================================
#  构建目录树结构
# ============================================

function Build-DirectoryTree {
    param([object[]]$Projects)

    # 只收集顶层目录（一级目录），不递归创建子目录文件夹
    $topLevelDirs = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($proj in $Projects) {
        if (-not [string]::IsNullOrEmpty($proj.DirPath)) {
            # 只取顶层目录（第一个路径段）
            $parts = $proj.DirPath -split '\\'
            $topDir = $parts[0]
            $null = $topLevelDirs.Add($topDir)
        }
    }

    # 构建目录树（只有顶层）
    $tree = @{}  # @{ DirPath = @{ Guid; Name; Parent; Projects = @() } }
    $rootGuidMap = @{}  # @{ DirPath = Guid }

    foreach ($dirPath in $topLevelDirs) {
        $guid = "{" + (New-GuidString) + "}"
        $rootGuidMap[$dirPath] = $guid

        $tree[$dirPath] = @{
            Guid     = $guid
            Name     = $dirPath
            Parent   = ""  # 顶层目录没有父目录
            Projects = @()
        }
    }

    # 将项目分配到对应的顶层目录
    foreach ($proj in $Projects) {
        if (-not [string]::IsNullOrEmpty($proj.DirPath)) {
            $parts = $proj.DirPath -split '\\'
            $topDir = $parts[0]
            if ($tree.ContainsKey($topDir)) {
                $tree[$topDir].Projects += $proj
            }
        }
    }

    return @{
        Tree = $tree
        GuidMap = $rootGuidMap
    }
}

# ============================================
#  生成 .sln 文件内容
# ============================================

function Generate-SolutionContent {
    param(
        [object[]]$Projects,
        [hashtable]$DirTree,
        [hashtable]$DirGuidMap,
        [string[]]$ConfigLines,
        [string[]]$HeaderLines
    )

    $content = ""

    # 文件头
    foreach ($line in $HeaderLines) {
        if ($line -match "^Global$") { break }
        $content += "$line`r`n"
    }

    # 按目录路径排序项目（根目录项目优先，然后按目录分组）
    $sortedProjects = $Projects | Sort-Object @{
        Expression = {
            $path = $_.Path -replace '\\', '/'
            $parts = $path.Split('/')
            if ($parts.Count -eq 1) {
                return "0_$($parts[0])"
            } else {
                return "1_$($parts[0])_$path"
            }
        }
    }

    # 收集所有需要创建的目录（按层级排序）
    $sortedDirs = $DirGuidMap.Keys | Sort-Object { ($_ -split '\\').Count, $_ }

    # 写入 Solution Folder 定义
    foreach ($dirPath in $sortedDirs) {
        $dirInfo = $DirTree[$dirPath]
        $folderGuid = $dirInfo.Guid
        $dirName = $dirInfo.Name
        $content += "Project(`"$solutionFolderTypeGuid`") = `"$dirName`", `"$dirName`", `"$folderGuid`"`r`n"
        $content += "EndProject`r`n"
    }

    # 写入项目定义
    foreach ($proj in $sortedProjects) {
        $content += "Project(`"$($proj.TypeGuid)`") = `"$($proj.Name)`", `"$($proj.Path)`", `"$($proj.Guid)`"`r`n"
        $content += "EndProject`r`n"
    }

    # Global 节
    $content += "Global`r`n"

    # 保留原有的配置行（过滤掉 NestedProjects 部分，我们会重新生成）
    $inOldNested = $false
    foreach ($line in $ConfigLines) {
        if ($line -match 'GlobalSection\(NestedProjects\)') {
            $inOldNested = $true
            continue
        }
        if ($inOldNested) {
            if ($line -match 'EndGlobalSection') {
                $inOldNested = $false
            }
            continue
        }
        $content += "$line`r`n"
    }

    # 重新生成 NestedProjects
    $content += "`tGlobalSection(NestedProjects) = preSolution`r`n"

    # 目录嵌套关系
    foreach ($dirPath in $sortedDirs) {
        $dirInfo = $DirTree[$dirPath]
        $childGuid = $dirInfo.Guid
        if (-not [string]::IsNullOrEmpty($dirInfo.Parent)) {
            $parentGuid = $DirGuidMap[$dirInfo.Parent]
            if ($parentGuid) {
                $content += "`t`t$childGuid = $parentGuid`r`n"
            }
        }
    }

    # 项目 → 顶层目录 的嵌套关系
    foreach ($proj in $sortedProjects) {
        if (-not [string]::IsNullOrEmpty($proj.DirPath)) {
            # 只取顶层目录
            $parts = $proj.DirPath -split '\\'
            $topDir = $parts[0]
            $parentGuid = $DirGuidMap[$topDir]
            if ($parentGuid) {
                $content += "`t`t$($proj.Guid) = $parentGuid`r`n"
            }
        }
    }

    $content += "`tEndGlobalSection`r`n"
    $content += "EndGlobal`r`n"

    return $content
}

# ============================================
#  显示帮助
# ============================================

function Show-Help {
    $scriptName = Split-Path -Leaf $MyInvocation.MyCommand.Path

    Write-Header "Sync-SlnToDisk - 帮助"
    Write-Host ""
    Write-Host "  功能: 同步 .sln 解决方案文件与磁盘目录结构"
    Write-Host ""
    Write-Host "  用法:"
    Write-Host "    $scriptName              交互模式（检查并提示同步）"
    Write-Host "    $scriptName -Check       仅检查差异，不修改"
    Write-Host "    $scriptName -Force       强制同步（跳过确认）"
    Write-Host "    $scriptName -Help        显示本帮助"
    Write-Host ""
    Write-Host "  参数:"
    Write-Host "    -SolutionPath <路径>     指定 .sln 文件路径（默认: WorkFlowPrj.sln）"
    Write-Host "    -Check                   仅检查差异，不修改文件"
    Write-Host "    -Force                   强制同步，跳过确认提示"
    Write-Host "    -Help                    显示本帮助"
    Write-Host ""
    Write-Host "  说明:"
    Write-Host "    1. 删除 .sln 中磁盘上已不存在的项目"
    Write-Host "    2. 添加磁盘上存在但 .sln 中未包含的项目"
    Write-Host "    3. 按磁盘目录树重建 Solution Folder（支持多级嵌套）"
    Write-Host "    4. 保留现有项目的 GUID（git 历史稳定）"
    Write-Host "    5. 保留所有构建配置"
    Write-Host ""
}

# ============================================
#  主逻辑（仅在直接执行时运行，dot-source 时不运行）
# ============================================

if ($Help) {
    Show-Help
    exit 0
}

# 如果被 dot-source（用于调试/测试），不执行主逻辑
if ($MyInvocation.InvocationName -eq '.') {
    return
}

Write-Header "Sync-SlnToDisk - 同步解决方案与磁盘结构"

# 检查 .sln 文件是否存在
if (-not (Test-Path $SolutionPath)) {
    Write-Error "找不到解决方案文件: $SolutionPath"
    Write-Host ""
    Write-Host "请指定正确的路径: Sync-SlnToDisk.ps1 -SolutionPath <路径>"
    exit 1
}

Write-Info "解决方案文件" $SolutionPath
Write-Info "项目根目录" $rootDir
Write-Host ""

# ============================================
# 步骤 1：解析现有 .sln
# ============================================
Write-Header "步骤 1/4：解析现有 .sln 文件"

$slnData = Parse-SolutionFile -Path $SolutionPath
Write-Info "现有项目" "$($slnData.Projects.Count) 个"
Write-Info "现有虚拟文件夹" "$($slnData.Folders.Count) 个"
Write-Info "嵌套关系" "$($slnData.NestedProjects.Count) 条"

# ============================================
# 步骤 2：扫描磁盘上的 .csproj
# ============================================
Write-Header "步骤 2/4：扫描磁盘上的 .csproj 文件"

$diskProjects = Scan-DiskProjects -RootDir $rootDir
Write-Info "磁盘项目" "$($diskProjects.Count) 个"

# ============================================
# 步骤 3：对比差异
# ============================================
Write-Header "步骤 3/4：对比差异"

# 找出要删除的项目（在 .sln 中但磁盘不存在）
$projectsToRemove = @()
foreach ($proj in $slnData.Projects) {
    $fullPath = Join-Path $rootDir $proj.Path
    if (-not (Test-Path $fullPath)) {
        $projectsToRemove += $proj
    }
}

# 找出要添加的项目（磁盘存在但不在 .sln 中）
$projectsToAdd = @()
foreach ($diskProj in $diskProjects) {
    $found = $false
    foreach ($slnProj in $slnData.Projects) {
        # ★ 使用 Path.GetFullPath 规范化路径后再比较，避免分隔符不一致问题
        $normalizedSlnPath = [System.IO.Path]::GetFullPath((Join-Path $rootDir $slnProj.Path))
        $normalizedDiskPath = [System.IO.Path]::GetFullPath($diskProj.FullPath)
        if ($normalizedSlnPath -ieq $normalizedDiskPath) {
            $found = $true
            break
        }
    }
    if (-not $found) {
        $projectsToAdd += $diskProj
    }
}

# 输出差异
if ($projectsToRemove.Count -eq 0 -and $projectsToAdd.Count -eq 0) {
    Write-Success "解决方案与磁盘结构一致，无需修改"
    Write-Host ""
    Write-Info "项目总数" "$($slnData.Projects.Count) 个"
    exit 0
}

if ($projectsToRemove.Count -gt 0) {
    Write-Warning "以下 $($projectsToRemove.Count) 个项目在 .sln 中但磁盘上不存在（将被删除）:"
    foreach ($proj in $projectsToRemove) {
        Write-Diff "  - $($proj.Name) ($($proj.Path))" "Red"
    }
    Write-Host ""
}

if ($projectsToAdd.Count -gt 0) {
    Write-Warning "以下 $($projectsToAdd.Count) 个项目在磁盘上但不在 .sln 中（将被添加）:"
    foreach ($proj in $projectsToAdd) {
        Write-Diff "  + $($proj.Name) ($($proj.Path))" "Green"
    }
    Write-Host ""
}

# --Check 模式：仅检查
if ($Check) {
    if ($projectsToRemove.Count -gt 0 -or $projectsToAdd.Count -gt 0) {
        exit 1
    }
    exit 0
}

# 交互模式：确认
if (-not $Force) {
    $choice = Read-Host "是否执行同步？(Y/N，默认 Y)"
    if ($choice -ne "" -and $choice -ne "Y" -and $choice -ne "y") {
        Write-Host ""
        Write-Host "已取消同步。"
        exit 0
    }
    Write-Host ""
}

# ============================================
# 步骤 4：构建新 .sln
# ============================================
Write-Header "步骤 4/4：构建新的 .sln 文件"

# 构建新的项目列表
$newProjects = @()

# 保留现有项目（排除要删除的）
$removeGuids = @{}
foreach ($proj in $projectsToRemove) {
    $removeGuids[$proj.Guid] = $true
}

foreach ($proj in $slnData.Projects) {
    if (-not $removeGuids.ContainsKey($proj.Guid)) {
        $newProjects += $proj
    }
}

# 添加新项目
foreach ($proj in $projectsToAdd) {
    $newGuid = "{" + (New-GuidString) + "}"
    $newProjects += [PSCustomObject]@{
        Name     = $proj.Name
        Path     = $proj.Path
        Guid     = $newGuid
        TypeGuid = $csharpProjectTypeGuid
        DirPath  = $proj.DirPath
    }
    Write-Diff "  为新项目 $($proj.Name) 生成 GUID: $newGuid" "Gray"
}

# 为所有项目补充 DirPath 属性（保留的项目可能没有 DirPath）
$finalProjects = @()
foreach ($proj in $newProjects) {
    if (-not $proj.DirPath) {
        $path = $proj.Path -replace '\\', '/'
        if ($path.Contains('/')) {
            $dirPath = [System.IO.Path]::GetDirectoryName($proj.Path)
            $finalProjects += [PSCustomObject]@{
                Name     = $proj.Name
                Path     = $proj.Path
                Guid     = $proj.Guid
                TypeGuid = $proj.TypeGuid
                DirPath  = $dirPath
            }
        } else {
            $finalProjects += [PSCustomObject]@{
                Name     = $proj.Name
                Path     = $proj.Path
                Guid     = $proj.Guid
                TypeGuid = $proj.TypeGuid
                DirPath  = ""
            }
        }
    } else {
        $finalProjects += $proj
    }
}

# 构建目录树
$dirTreeResult = Build-DirectoryTree -Projects $finalProjects
$dirTree = $dirTreeResult.Tree
$dirGuidMap = $dirTreeResult.GuidMap

Write-Info "最终项目数" "$($finalProjects.Count) 个"
Write-Info "目录层级" "$($dirGuidMap.Count) 个"

# 生成新内容
$newContent = Generate-SolutionContent `
    -Projects $finalProjects `
    -DirTree $dirTree `
    -DirGuidMap $dirGuidMap `
    -ConfigLines $slnData.ConfigLines `
    -HeaderLines $slnData.HeaderLines

# 备份原文件
$backupPath = $SolutionPath + ".bak"
Copy-Item $SolutionPath $backupPath -Force
Write-Success "已备份原文件到: $backupPath"

# 写入新文件
[System.IO.File]::WriteAllText($SolutionPath, $newContent, [System.Text.UTF8Encoding]::new($false))
Write-Success "已更新解决方案文件: $SolutionPath"

# ============================================
# 完成
# ============================================
Write-Header "同步完成"
Write-Info "删除的项目" "$($projectsToRemove.Count) 个"
Write-Info "添加的项目" "$($projectsToAdd.Count) 个"
Write-Info "最终项目数" "$($finalProjects.Count) 个"
Write-Info "目录层级数" "$($dirGuidMap.Count) 个"
Write-Host ""
Write-Success "请在 Visual Studio 中重新打开解决方案查看效果"
Write-Host ""
