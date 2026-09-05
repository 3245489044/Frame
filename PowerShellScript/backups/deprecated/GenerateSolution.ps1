<#
.SYNOPSIS
    解决方案文件精简器 - 删除 .sln 中 x64/x86 的冗余构建配置

.DESCRIPTION
    读取 WorkFlowPrj.sln，删除其中 Debug|x64、Debug|x86、Release|x64、Release|x86
    的冗余配置行，只保留 Debug|Any CPU 和 Release|Any CPU。

    ⚠️ 注意：此脚本已被 Clean-SolutionConfig.ps1 取代（功能更完善，有备份机制）。
    建议使用 Clean-SolutionConfig.ps1 替代。

    用法:
        GenerateSolution.ps1              交互模式（检查并提示精简）
        GenerateSolution.ps1 --check      仅检查 .sln 是否包含冗余配置
        GenerateSolution.ps1 --force      强制精简
        GenerateSolution.ps1 --help       显示帮助

    示例:
        GenerateSolution.ps1
        GenerateSolution.ps1 --check
        GenerateSolution.ps1 --force
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
$rootDir = (Get-Item $scriptDir).Parent.FullName
$slnFile = Join-Path $rootDir "WorkFlowPrj.sln"

# 需要删除的冗余平台配置模式
$redundantPlatforms = @(
    'Debug|x64',
    'Debug|x86',
    'Release|x64',
    'Release|x86'
)

# ============================================
#  辅助函数（优先从 OutputHelpers 模块获取）
# ============================================

# 尝试从 OutputHelpers 模块获取辅助函数，失败则内联定义
if (-not (Get-Command -Name 'Write-Header' -ErrorAction SilentlyContinue)) {
    function Write-Header {
        param([string]$Text)
        Write-Host ("=" * 44) -ForegroundColor Cyan
        Write-Host "  $Text" -ForegroundColor Cyan
        Write-Host ("=" * 44) -ForegroundColor Cyan
    }

    function Write-Info {
        param([string]$Label, [string]$Value)
        Write-Host ("  {0,-12} {1}" -f ($Label + ":"), $Value) -ForegroundColor Gray
    }

    function Write-Success {
        param([string]$Text)
        Write-Host "  [成功] $Text" -ForegroundColor Green
    }

    function Write-Warning {
        param([string]$Text)
        Write-Host "  [警告] $Text" -ForegroundColor Yellow
    }

    function Write-Error {
        param([string]$Text)
        Write-Host "  [错误] $Text" -ForegroundColor Red
    }
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

    foreach ($line in $Lines) {
        foreach ($platform in $redundantPlatforms) {
            if ($line -match [regex]::Escape($platform)) {
                return $true
            }
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
    foreach ($line in $Lines) {
        foreach ($platform in $redundantPlatforms) {
            if ($line -match [regex]::Escape($platform)) {
                $count++
                break
            }
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
        # 备份原文件
        $backupPath = $slnFile + ".backup"
        if (-not (Test-Path $backupPath)) {
            Copy-Item $slnFile $backupPath -Force
            Write-Info "备份文件" "$backupPath"
        }

        # 读取文件
        $content = Get-Content $slnFile -Raw -ErrorAction Stop
        $lines = $content -split "`r`n|`n"
        Write-Info "读取文件" "WorkFlowPrj.sln（$($lines.Count) 行）"

        # 过滤冗余行
        $newLines = @()
        $removedCount = 0
        $inProjectConfig = $false

        foreach ($line in $lines) {
            $isRedundant = $false

            # 检查是否是冗余的平台配置行
            foreach ($platform in $redundantPlatforms) {
                if ($line -match [regex]::Escape($platform)) {
                    $isRedundant = $true
                    break
                }
            }

            if ($isRedundant) {
                $removedCount++
            } else {
                $newLines += $line
            }
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
    $scriptName = Split-Path -Leaf $MyInvocation.MyCommand.Path

    Write-Header "GenerateSolution - 帮助"
    Write-Host ""
    Write-Host "  用法:"
    Write-Host "    $scriptName              交互模式（检查并提示精简）"
    Write-Host "    $scriptName --check      仅检查 .sln 是否包含冗余配置"
    Write-Host "    $scriptName --force      强制精简"
    Write-Host "    $scriptName --help       显示本帮助"
    Write-Host ""
    Write-Host "  说明:"
    Write-Host "    读取 WorkFlowPrj.sln，删除其中 Debug|x64、Debug|x86、"
    Write-Host "    Release|x64、Release|x86 的冗余配置行，只保留"
    Write-Host "    Debug|Any CPU 和 Release|Any CPU。"
    Write-Host ""
    Write-Host "    如果 VS 添加新项目后自动生成了 x64/x86 配置，"
    Write-Host "    运行此脚本一键清理。"
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
        Write-Warning "WorkFlowPrj.sln 包含 $redundantCount 行冗余配置（x64/x86）"
        exit 1
    } else {
        Write-Success "WorkFlowPrj.sln 已是最简，无冗余配置"
        exit 0
    }
}

# --force 模式：强制精简
if ($Force) {
    if (-not $hasRedundant) {
        Write-Success "WorkFlowPrj.sln 已是最简，无需精简"
        exit 0
    }

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
    Write-Success "WorkFlowPrj.sln 已是最简，无冗余配置"
    Write-Host ""
    Write-Host "按任意键退出..."
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    exit 0
}

Write-Header "发现冗余配置"
Write-Warning "WorkFlowPrj.sln 包含 $redundantCount 行冗余配置（x64/x86）"
Write-Host ""
$choice = Read-Host "是否删除这些冗余配置？(Y/N，默认 Y)"

if ($choice -eq "" -or $choice -eq "Y" -or $choice -eq "y") {
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
