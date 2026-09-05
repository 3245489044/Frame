<#
.SYNOPSIS
    解决方案配置精简器 - 将 WorkFlowPrj.sln 精简为只保留 Debug|Any CPU 单一配置

.DESCRIPTION
    读取 WorkFlowPrj.sln，删除其中所有 Release、x64、x86 的冗余构建配置，
    只保留 Debug|Any CPU 单一平台 + Debug 配置，大幅减少 .sln 文件体积。

    精简内容：
    - SolutionConfigurationPlatforms: 只保留 Debug|Any CPU
    - ProjectConfigurationPlatforms:  每个项目只保留 Debug|Any CPU 的 ActiveCfg 和 Build.0
    - 保留所有 Project 定义、Solution Folder、NestedProjects 不变

    用法:
        Clean-SolutionConfig.ps1              交互模式（检查并提示精简）
        Clean-SolutionConfig.ps1 --check      仅检查 .sln 是否包含冗余配置
        Clean-SolutionConfig.ps1 --force      强制精简
        Clean-SolutionConfig.ps1 --help       显示帮助

    示例:
        Clean-SolutionConfig.ps1
        Clean-SolutionConfig.ps1 --check
        Clean-SolutionConfig.ps1 --force
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

# 需要保留的唯一配置
$keepConfig = 'Debug|Any CPU'

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
        Write-Error "找不到文件: $slnFile"
        Write-Host ""
        return $false
    }
    return $true
}

# ============================================
#  检查是否包含冗余配置
# ============================================

function Test-HasRedundantConfig {
    param([string[]]$Lines)

    $inProjectConfig = $false
    foreach ($line in $Lines) {
        $trimmed = $line.Trim()

        if ($trimmed -eq 'GlobalSection(ProjectConfigurationPlatforms) = postSolution') {
            $inProjectConfig = $true
            continue
        }
        if ($inProjectConfig -and $trimmed -eq 'EndGlobalSection') {
            $inProjectConfig = $false
            continue
        }

        # 在 ProjectConfigurationPlatforms 中检查非 Debug|Any CPU 的行
        # ★ 只匹配配置行（以 {GUID}. 开头），忽略空行和注释
        if ($inProjectConfig -and $trimmed -match '^\{[A-F0-9-]+\}\.') {
            if ($trimmed -notmatch [regex]::Escape($keepConfig)) {
                return $true
            }
        }
    }

    # 也检查 SolutionConfigurationPlatforms
    $inSlnConfig = $false
    foreach ($line in $Lines) {
        $trimmed = $line.Trim()
        if ($trimmed -eq 'GlobalSection(SolutionConfigurationPlatforms) = preSolution') {
            $inSlnConfig = $true
            continue
        }
        if ($inSlnConfig -and $trimmed -eq 'EndGlobalSection') {
            $inSlnConfig = $false
            continue
        }
        if ($inSlnConfig -and $trimmed -ne '' -and $trimmed -notmatch [regex]::Escape($keepConfig)) {
            return $true
        }
    }

    return $false
}

# ============================================
#  统计冗余行数
# ============================================

function Get-RedundantLineCount {
    param([string[]]$Lines)

    $count = 0
    $inProjectConfig = $false

    foreach ($line in $Lines) {
        $trimmed = $line.Trim()

        if ($trimmed -eq 'GlobalSection(ProjectConfigurationPlatforms) = postSolution') {
            $inProjectConfig = $true
            continue
        }
        if ($inProjectConfig -and $trimmed -eq 'EndGlobalSection') {
            $inProjectConfig = $false
            continue
        }

        if ($inProjectConfig -and $trimmed -match '\.(Debug\|Any CPU|Release\|Any CPU|Debug\|x64|Debug\|x86|Release\|x64|Release\|x86)') {
            if ($trimmed -notmatch [regex]::Escape($keepConfig)) {
                $count++
            }
        }
    }

    # 统计 SolutionConfigurationPlatforms 中的冗余
    $inSlnConfig = $false
    foreach ($line in $Lines) {
        $trimmed = $line.Trim()
        if ($trimmed -eq 'GlobalSection(SolutionConfigurationPlatforms) = preSolution') {
            $inSlnConfig = $true
            continue
        }
        if ($inSlnConfig -and $trimmed -eq 'EndGlobalSection') {
            $inSlnConfig = $false
            continue
        }
        if ($inSlnConfig -and $trimmed -ne '' -and $trimmed -notmatch [regex]::Escape($keepConfig)) {
            $count++
        }
    }

    return $count
}

# ============================================
#  精简 .sln 文件
# ============================================

function Optimize-SolutionFile {
    Write-Header "正在精简 WorkFlowPrj.sln"

    try {
        # 读取文件
        $content = Get-Content $slnFile -Raw -ErrorAction Stop
        $lines = $content -split "`r`n|`n"
        Write-Info "读取文件" "WorkFlowPrj.sln（$($lines.Count) 行）"

        $newLines = [System.Collections.ArrayList]::new()
        $removedCount = 0

        # 状态跟踪
        $inSlnConfig = $false
        $inProjectConfig = $false
        $inOtherSection = $false

        foreach ($line in $lines) {
            $trimmed = $line.Trim()
            $isRedundant = $false

        # ---- 检测节开始/结束 ----

        # SolutionConfigurationPlatforms
        if ($trimmed -eq 'GlobalSection(SolutionConfigurationPlatforms) = preSolution') {
            $inSlnConfig = $true
            [void]$newLines.Add($line)
            continue
        }
        if ($inSlnConfig -and $trimmed -eq 'EndGlobalSection') {
            $inSlnConfig = $false
            [void]$newLines.Add($line)
            continue
        }

        # ProjectConfigurationPlatforms
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

        # ---- 在 SolutionConfigurationPlatforms 中 ----
        if ($inSlnConfig) {
            # 只保留 Debug|Any CPU 的配置行
            if ($trimmed -match '^[ \t]*Debug\|Any CPU') {
                [void]$newLines.Add($line)
            } else {
                $removedCount++
            }
            continue
        }

        # ---- 在 ProjectConfigurationPlatforms 中 ----
        if ($inProjectConfig) {
            # 只保留 Debug|Any CPU 的配置行（匹配 {GUID}.Debug|Any CPU. 格式）
            if ($trimmed -match '\.Debug\|Any CPU\.') {
                [void]$newLines.Add($line)
            } else {
                $removedCount++
            }
            continue
        }


            # ---- 其他行（Project 定义、Solution Folder、NestedProjects 等） ----
            [void]$newLines.Add($line)
        }

        # 写入文件
        $newContent = $newLines -join "`r`n"
        [System.IO.File]::WriteAllText($slnFile, $newContent, [System.Text.UTF8Encoding]::new($false))

        # 验证结果
        $finalLines = (Get-Content $slnFile).Count
        Write-Success "已删除 $removedCount 行冗余配置"
        Write-Info "精简后" "WorkFlowPrj.sln（$finalLines 行）"

        return $true
    }
    catch {
        Write-Error "精简失败: $($_.Exception.Message)"
        return $false
    }
}

# ============================================
#  显示帮助
# ============================================

function Show-Help {
    $scriptName = "Clean-SolutionConfig.ps1"


    Write-Header "Clean-SolutionConfig - 帮助"
    Write-Host ""
    Write-Host "  用法:"
    Write-Host "    $scriptName              交互模式（检查并提示精简）"
    Write-Host "    $scriptName --check      仅检查 .sln 是否包含冗余配置"
    Write-Host "    $scriptName --force      强制精简"
    Write-Host "    $scriptName --help       显示本帮助"
    Write-Host ""
    Write-Host "  说明:"
    Write-Host "    读取 WorkFlowPrj.sln，删除其中所有 Release、x64、x86 的"
    Write-Host "    冗余构建配置，只保留 Debug|Any CPU 单一平台 + Debug 配置。"
    Write-Host ""
    Write-Host "  精简内容:"
    Write-Host "    · SolutionConfigurationPlatforms: 只保留 Debug|Any CPU"
    Write-Host "    · ProjectConfigurationPlatforms:  每个项目只保留 Debug|Any CPU"
    Write-Host "    · 保留所有 Project 定义、Solution Folder、NestedProjects 不变"
    Write-Host ""
    Write-Host "  精简效果:"
    Write-Host "    · 精简前: 738 行, 6 个平台配置, 33 个项目 × 6 = 198 组配置"
    Write-Host "    · 精简后: ~100 行, 1 个平台配置, 33 个项目 × 1 = 33 组配置"
    Write-Host "    · 减少约 86% 的冗余内容"
    Write-Host ""
}

# ============================================
#  主逻辑
# ============================================

if ($Help) {
    Show-Help
    exit 0
}

# 检查文件是否存在
if (-not (Test-SlnFile)) {
    Write-Host "按任意键退出..."
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    exit 1
}

# 读取文件内容用于检查
$lines = Get-Content $slnFile
$hasRedundant = Test-HasRedundantConfig -Lines $lines
$redundantCount = Get-RedundantLineCount -Lines $lines

# --check 模式：仅检查状态
if ($Check) {
    if ($hasRedundant) {
        Write-Warning "WorkFlowPrj.sln 包含 $redundantCount 行冗余配置（Release/x64/x86）"
        exit 1
    } else {
        Write-Success "WorkFlowPrj.sln 已是最简（仅 Debug|Any CPU），无冗余配置"
        exit 0
    }
}

# --force 模式：强制精简
if ($Force) {
    if (-not $hasRedundant) {
        Write-Success "WorkFlowPrj.sln 已是最简（仅 Debug|Any CPU），无需精简"
        exit 0
    }

    # 备份
    $backupPath = $slnFile + ".backup"
    Copy-Item $slnFile $backupPath -Force
    Write-Info "备份文件" "$backupPath"

    $success = Optimize-SolutionFile
    if ($success) {
        Write-Host ""
        Write-Success "精简完成！"
    }
    exit $(if ($success) { 0 } else { 1 })
}

# 默认交互模式
if (-not $hasRedundant) {
    Write-Header "状态检查"
    Write-Success "WorkFlowPrj.sln 已是最简（仅 Debug|Any CPU），无冗余配置"
    Write-Host ""
    Write-Host "按任意键退出..."
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    exit 0
}

Write-Header "发现冗余配置"
Write-Warning "WorkFlowPrj.sln 包含 $redundantCount 行冗余配置（Release/x64/x86）"
Write-Host ""
Write-Host "  精简后效果:"
Write-Host "    · 当前行数: $($lines.Count) 行"
Write-Host "    · 预计行数: $($lines.Count - $redundantCount) 行"
Write-Host "    · 减少约 $([math]::Round($redundantCount / $lines.Count * 100, 0))%"
Write-Host ""
$choice = Read-Host "是否删除这些冗余配置？(Y/N，默认 Y)"

if ($choice -eq "" -or $choice -eq "Y" -or $choice -eq "y") {
    # 备份
    $backupPath = $slnFile + ".backup"
    Copy-Item $slnFile $backupPath -Force
    Write-Info "备份文件" "$backupPath"

    Write-Host ""
    $success = Optimize-SolutionFile
    if ($success) {
        Write-Host ""
        Write-Success "精简完成！"
    }
} else {
    Write-Host ""
    Write-Host "已取消精简。"
}

Write-Host ""
Write-Host "按任意键退出..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
