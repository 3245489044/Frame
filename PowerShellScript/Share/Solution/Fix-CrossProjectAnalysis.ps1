<#
.SYNOPSIS
    跨项目分析修复工具 — 统一 TargetFramework + 修复 .sln 中缺失的 Build.0 配置

.DESCRIPTION
    解决 Visual Studio 无法跨项目分析引用关系的两个核心问题：
    
    问题 1: TargetFramework 不一致
    将 net10.0 统一为 net10.0-windows（保留 netstandard2.0 的 UniversalLayoutMath 不变）
    
    问题 2: .sln 中部分项目缺少 Build.0 配置
    为 ProjectConfigurationPlatforms 中只有 ActiveCfg 但没有 Build.0 的项目补全配置

    用法:
        Fix-CrossProjectAnalysis.ps1              交互模式（检查并提示修复）
        Fix-CrossProjectAnalysis.ps1 -Check       仅检查差异，不修改
        Fix-CrossProjectAnalysis.ps1 -Force       强制修复（跳过确认）
        Fix-CrossProjectAnalysis.ps1 -Help        显示帮助

    示例:
        Fix-CrossProjectAnalysis.ps1
        Fix-CrossProjectAnalysis.ps1 -Check
        Fix-CrossProjectAnalysis.ps1 -Force
#>

param(
    [switch]$Check = $false,
    [switch]$Force = $false,
    [switch]$Help = $false
)

$ErrorActionPreference = "Continue"

# ============================================
#  路径配置
# ============================================

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = (Get-Item $scriptDir).Parent.Parent.FullName
$slnFile = Join-Path $rootDir "CsharpPrj\MainProj.sln"

# ============================================
#  加载 OutputHelpers 模块
# ============================================

$modulePath = Join-Path $PSScriptRoot "..\Modules\Core\OutputHelpers.psm1"
if (-not (Get-Module -Name OutputHelpers)) {
    Import-Module $modulePath -Force -ErrorAction Stop
}


# ============================================
#  检查 .sln 文件是否存在
# ============================================

function Test-SlnFile {
    if (-not (Test-Path $slnFile)) {
        Write-Header "文件缺失"
        Write-Error "找不到解决方案文件: $slnFile"
        return $false
    }
    return $true
}

# ============================================
#  步骤 1：检查并修复 TargetFramework
# ============================================

function Get-TargetFrameworkIssues {
    <#
    .SYNOPSIS
        扫描所有 .csproj，找出可能需要统一 TargetFramework 的项目
    .DESCRIPTION
        只处理确实需要 Windows 支持的项目（引用 WinForms/WPF 依赖的项目），
        纯控制台应用或后台服务（如 CentralConsole）保持 net10.0 不变。
    #>
    $issues = @()

    # 需要 Windows 支持的项目白名单（引用 WinForms/WPF 依赖的项目）
    $windowsRequiredProjects = @(
        'WdUI.csproj',
        'FileDriven.UI.csproj',
        'BasicMethod.csproj',
        'FileDriven.Launcher.csproj',
        'FileDriven.Execution.csproj'
    )

    Get-ChildItem -Path $rootDir -Recurse -Filter "*.csproj" -ErrorAction SilentlyContinue | ForEach-Object {
        $fullPath = $_.FullName

        # 排除备份目录和构建输出目录
        if ($fullPath -match '\\(backups|BuildOutput|bin|obj|node_modules|\.git|\.vs)\\') { return }

        $content = Get-Content $fullPath -Raw
        if ($content -match '<TargetFramework[^>]*>(.*?)</TargetFramework>') {
            $tf = $matches[1]
            # 只处理 net10.0（不含 -windows），且项目在白名单中
            if ($tf -eq 'net10.0' -and ($_.Name -in $windowsRequiredProjects)) {
                $issues += [PSCustomObject]@{
                    FilePath = $fullPath
                    RelativePath = $fullPath.Substring($rootDir.Length + 1)
                    CurrentTF = $tf
                    NewTF = 'net10.0-windows'
                }
            }
        }
    }

    return $issues
}

function Fix-TargetFramework {
    <#
    .SYNOPSIS
        修复 TargetFramework 不一致问题
    #>
    param([object[]]$Issues)

    $fixedCount = 0
    $backupDir = Join-Path $rootDir "BuildOutput\backups\csproj_backups"
    if (-not (Test-Path $backupDir)) {
        New-Item -Path $backupDir -ItemType Directory -Force | Out-Null
    }

    foreach ($issue in $Issues) {
        $content = Get-Content $issue.FilePath -Raw

        # 备份
        $backupFileName = $issue.RelativePath -replace '\\', '_' -replace '/', '_'
        $backupPath = Join-Path $backupDir "$backupFileName.bak"
        if (-not (Test-Path $backupPath)) {
            Copy-Item $issue.FilePath $backupPath -Force
        }

        # 替换 TargetFramework
        $newContent = $content -replace '<TargetFramework[^>]*>net10\.0</TargetFramework>', '<TargetFramework>net10.0-windows</TargetFramework>'
        [System.IO.File]::WriteAllText($issue.FilePath, $newContent, [System.Text.UTF8Encoding]::new($false))
        $fixedCount++
    }

    return $fixedCount
}

# ============================================
#  步骤 2：检查并修复 .sln 中缺失的 Build.0
# ============================================

function Get-MissingBuildConfigIssues {
    <#
    .SYNOPSIS
        解析 .sln 文件，找出 ProjectConfigurationPlatforms 中缺少 Build.0 的项目
    #>
    $issues = @()

    if (-not (Test-Path $slnFile)) { return $issues }

    $lines = Get-Content $slnFile
    $inProjectConfig = $false
    $projectGuids = @{}  # @{ Guid = @{ HasActive = $false; HasBuild = $false; ActiveLine = "" } }

    foreach ($line in $lines) {
        $trimmed = $line.Trim()

        if ($trimmed -eq 'GlobalSection(ProjectConfigurationPlatforms) = postSolution') {
            $inProjectConfig = $true
            continue
        }
        if ($inProjectConfig -and $trimmed -eq 'EndGlobalSection') {
            $inProjectConfig = $false
            continue
        }

        if ($inProjectConfig) {
            # 匹配格式: {GUID}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
            if ($trimmed -match '^\{(?<Guid>[^}]+)\}\.Debug\|Any CPU\.(?<Type>ActiveCfg|Build\.0)\s*=') {
                $guid = $matches['Guid']
                $type = $matches['Type']

                if (-not $projectGuids.ContainsKey($guid)) {
                    $projectGuids[$guid] = @{ HasActive = $false; HasBuild = $false; ActiveLine = "" }
                }

                if ($type -eq 'ActiveCfg') {
                    $projectGuids[$guid].HasActive = $true
                    $projectGuids[$guid].ActiveLine = $trimmed
                }
                elseif ($type -eq 'Build.0') {
                    $projectGuids[$guid].HasBuild = $true
                }
            }
        }
    }

    # 找出有 ActiveCfg 但没有 Build.0 的项目
    foreach ($guid in $projectGuids.Keys) {
        $info = $projectGuids[$guid]
        if ($info.HasActive -and -not $info.HasBuild) {
            # 从 ActiveLine 提取项目名称（需要从 .sln 的项目定义中查找）
            $issues += [PSCustomObject]@{
                Guid = "{$guid}"
                ActiveLine = $info.ActiveLine
            }
        }
    }

    return $issues
}

function Get-ProjectNameFromGuid {
    <#
    .SYNOPSIS
        从 .sln 文件中根据 GUID 查找项目名称
    #>
    param([string]$Guid, [string[]]$SlnLines)

    $projectRegex = [regex]'^Project\("[^"]+"\)\s*=\s*"(?<Name>[^"]+)",\s*"[^"]*",\s*"(?<Guid>[^"]+)"'
    foreach ($line in $SlnLines) {
        $match = $projectRegex.Match($line)
        if ($match.Success) {
            if ($match.Groups['Guid'].Value -eq $Guid) {
                return $match.Groups['Name'].Value
            }
        }
    }
    return $Guid
}

function Fix-MissingBuildConfig {
    <#
    .SYNOPSIS
        修复 .sln 中缺失的 Build.0 配置
    #>
    param([object[]]$Issues)

    if ($Issues.Count -eq 0) { return 0 }

    $lines = Get-Content $slnFile
    $newLines = [System.Collections.ArrayList]::new()
    $fixedCount = 0

    # 收集需要补全的 GUID 集合
    $missingBuildGuids = @{}
    foreach ($issue in $Issues) {
        $missingBuildGuids[$issue.Guid] = $true
    }

    $inProjectConfig = $false

    foreach ($line in $lines) {
        $trimmed = $line.Trim()

        if ($trimmed -eq 'GlobalSection(ProjectConfigurationPlatforms) = postSolution') {
            $inProjectConfig = $true
            [void]$newLines.Add($line)
            continue
        }
        if ($inProjectConfig -and $trimmed -eq 'EndGlobalSection') {
            $inProjectConfig = $false
            [void]$newLines.Add($line)
            continue
        }

        [void]$newLines.Add($line)

        # 在 ActiveCfg 行后面插入对应的 Build.0 行
        if ($inProjectConfig -and $trimmed -match '^\{(?<Guid>[^}]+)\}\.Debug\|Any CPU\.ActiveCfg\s*=\s*(?<Value>.+)$') {
            $guid = "{$($matches['Guid'])}"
            $value = $matches['Value'].Trim()
            if ($missingBuildGuids.ContainsKey($guid)) {
                $buildLine = "`t`t$guid.Debug|Any CPU.Build.0 = $value"
                [void]$newLines.Add($buildLine)
                $fixedCount++
            }
        }
    }

    # 写回文件
    $newContent = $newLines -join "`r`n"
    [System.IO.File]::WriteAllText($slnFile, $newContent, [System.Text.UTF8Encoding]::new($false))

    return $fixedCount
}

# ============================================
#  显示帮助
# ============================================

function Show-Help {
    $scriptName = Split-Path -Leaf $MyInvocation.MyCommand.Path

    Write-Header "Fix-CrossProjectAnalysis - 帮助"
    Write-Host ""
    Write-Host "  功能: 修复 Visual Studio 跨项目分析引用关系的问题"
    Write-Host ""
    Write-Host "  修复内容:"
    Write-Host "    1. 统一 TargetFramework: net10.0 → net10.0-windows"
    Write-Host "    2. 补全 .sln 中缺失的 Build.0 配置"
    Write-Host ""
    Write-Host "  用法:"
    Write-Host "    $scriptName              交互模式（检查并提示修复）"
    Write-Host "    $scriptName -Check       仅检查差异，不修改"
    Write-Host "    $scriptName -Force       强制修复（跳过确认）"
    Write-Host "    $scriptName -Help        显示本帮助"
    Write-Host ""
    Write-Host "  说明:"
    Write-Host "    · 修改前自动备份 .sln 和所有被修改的 .csproj"
    Write-Host "    · UniversalLayoutMath (netstandard2.0) 保持不变"
    Write-Host ""
}

# ============================================
#  主逻辑
# ============================================

if ($Help) {
    Show-Help
    exit 0
}

# 检查 .sln 文件
if (-not (Test-SlnFile)) {
    Write-Host "按任意键退出..."
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    exit 1
}

# ============================================
#  步骤 1：检查 TargetFramework
# ============================================
Write-Header "步骤 1/2：检查 TargetFramework 一致性"

$tfIssues = Get-TargetFrameworkIssues

if ($tfIssues.Count -eq 0) {
    Write-Success "所有项目的 TargetFramework 已统一为 net10.0-windows"
}
else {
    Write-Warning "发现 $($tfIssues.Count) 个项目使用 net10.0（非 windows），需要统一为 net10.0-windows:"
    foreach ($issue in $tfIssues) {
        Write-Diff "  $($issue.RelativePath)  ($($issue.CurrentTF) → $($issue.NewTF))" "Yellow"
    }
}
Write-Host ""

# ============================================
#  步骤 2：检查 .sln 中缺失的 Build.0
# ============================================
Write-Header "步骤 2/2：检查 .sln 中缺失的 Build.0 配置"

$slnLines = Get-Content $slnFile
$buildIssues = Get-MissingBuildConfigIssues

if ($buildIssues.Count -eq 0) {
    Write-Success "所有项目在 .sln 中都有完整的 Build.0 配置"
}
else {
    Write-Warning "发现 $($buildIssues.Count) 个项目缺少 Build.0 配置:"
    foreach ($issue in $buildIssues) {
        $projName = Get-ProjectNameFromGuid -Guid $issue.Guid -SlnLines $slnLines
        Write-Diff "  $projName  ($($issue.Guid))" "Yellow"
        Write-Diff "    现有: $($issue.ActiveLine)" "Gray"
        Write-Diff "    缺少: $($issue.Guid).Debug|Any CPU.Build.0 = ..." "Gray"
    }
}
Write-Host ""

# ============================================
#  汇总
# ============================================
$totalIssues = $tfIssues.Count + $buildIssues.Count

if ($totalIssues -eq 0) {
    Write-Header "检查完成"
    Write-Success "未发现问题，跨项目分析功能应正常工作"
    Write-Host ""
    Write-Host "按任意键退出..."
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    exit 0
}

# --Check 模式：仅检查
if ($Check) {
    Write-Header "检查完成"
    Write-Warning "共发现 $totalIssues 个问题（TargetFramework: $($tfIssues.Count) 个, Build.0: $($buildIssues.Count) 个）"
    exit 1
}

# ============================================
#  确认修复
# ============================================
if ($Force) {
    Write-Header "强制修复模式"
    Write-Host "  即将执行以下修复（跳过确认）:"
}
else {
    Write-Header "确认修复"
    Write-Host "  即将执行以下修复:"
}
if ($tfIssues.Count -gt 0) {
    Write-Host "    1. 统一 $($tfIssues.Count) 个项目的 TargetFramework: net10.0 → net10.0-windows"
}
if ($buildIssues.Count -gt 0) {
    Write-Host "    2. 补全 $($buildIssues.Count) 个项目的 Build.0 配置"
}

if (-not $Force) {
    Write-Host ""
    $choice = Read-Host "是否执行修复？(Y/N，默认 Y)"

    if ($choice -ne "" -and $choice -ne "Y" -and $choice -ne "y") {
        Write-Host ""
        Write-Host "已取消修复。"
        exit 0
    }
    Write-Host ""
}
else {
    Write-Host "  [强制模式] 跳过确认，直接执行修复..."
    Write-Host ""
}

# ============================================
#  执行修复
# ============================================
Write-Header "正在执行修复"

# 备份 .sln
$slnBackupPath = $slnFile + ".bak"
if (-not (Test-Path $slnBackupPath)) {
    Copy-Item $slnFile $slnBackupPath -Force
    Write-Success "已备份 .sln 到: $slnBackupPath"
}

# 修复 TargetFramework
if ($tfIssues.Count -gt 0) {
    Write-Info "修复 TargetFramework" "正在处理 $($tfIssues.Count) 个项目..."
    $fixedTF = Fix-TargetFramework -Issues $tfIssues
    Write-Success "已修复 $fixedTF 个项目的 TargetFramework（net10.0 → net10.0-windows）"
}
else {
    Write-Info "修复 TargetFramework" "无需修复"
}

Write-Host ""

# 修复 Build.0
if ($buildIssues.Count -gt 0) {
    Write-Info "修复 Build.0 配置" "正在处理 $($buildIssues.Count) 个项目..."
    $fixedBuild = Fix-MissingBuildConfig -Issues $buildIssues
    Write-Success "已补全 $fixedBuild 个项目的 Build.0 配置"
}
else {
    Write-Info "修复 Build.0 配置" "无需修复"
}

Write-Host ""

# ============================================
#  完成
# ============================================
Write-Header "修复完成"
Write-Success "请重启 Visual Studio 并重新加载解决方案以生效"
Write-Host ""
Write-Host "  修复摘要:"
Write-Info "TargetFramework 修复" "$($tfIssues.Count) 个"
Write-Info "Build.0 补全" "$($buildIssues.Count) 个"
Write-Host ""
Write-Host "  备份文件:"
Write-Info ".sln 备份" "$slnBackupPath"
if ($tfIssues.Count -gt 0) {
    Write-Info "csproj 备份" "BuildOutput\backups\csproj_backups\"
}
Write-Host ""
Write-Host "按任意键退出..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
