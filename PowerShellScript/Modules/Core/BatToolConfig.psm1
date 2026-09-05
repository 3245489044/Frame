<#
.SYNOPSIS
    BatTool 配置模块 - 统一管理项目路径别名和 BatTool 遗留功能

.DESCRIPTION
    从 BatTool/Tools/Utils/BatToolConfig.bat 迁移而来的 PowerShell 模块。
    提供项目路径别名管理功能，供 FileDriven 和其他脚本使用。

    用法:
        Import-Module BatToolConfig  # 或通过 Import-Modules.ps1 统一加载
        $aliases = Get-BatToolAliases

    返回的别名对象包含:
        - Configs   : FileDriven JsonConfig 目录
        - Scripts   : FileDriven Scripts 目录
        - WorkFlow  : FileDriven WorkFlow 目录
        - Output    : 构建输出目录
        - Root      : 项目根目录
#>

# ============================================
#  路径别名管理
# ============================================

function Get-BatToolAliases {
    <#
    .SYNOPSIS
        获取所有路径别名（替代 BatToolConfig.bat --export-aliases）

    .DESCRIPTION
        返回包含所有 FileDriven 路径别名的 hashtable。
        调用方可以通过 for /f 或 PowerShell 直接使用这些路径。

    .OUTPUTS
        hashtable，包含以下键：
        - Configs   : FileDriven JsonConfig 目录路径
        - Scripts   : FileDriven Scripts 目录路径
        - WorkFlow  : FileDriven WorkFlow 目录路径
        - Output    : 构建输出目录路径（动态检测）
        - Root      : 项目根目录路径
    #>

    $projectRoot = Get-ProjectRoot
    # ★ 使用统一路径函数获取 FileDriven 目录（自动拼接 CsharpProjectRoot）
    $fileDrivenDir = Get-FileDrivenDir -ProjectRoot $projectRoot

    # 动态获取 Output 路径
    $outputDir = Get-DynamicOutputPath -projectRoot $projectRoot

    return @{
        Configs  = Join-Path $fileDrivenDir "DrivenConfig\JsonConfig"
        Scripts  = Join-Path $fileDrivenDir "DrivenConfig\Scripts"
        WorkFlow = Join-Path $fileDrivenDir "DrivenConfig\WorkFlow"
        Output   = $outputDir
        Root     = $projectRoot
    }
}

function Show-BatToolAliases {
    <#
    .SYNOPSIS
        打印所有路径别名（替代 BatToolConfig.bat --list-aliases）

    .DESCRIPTION
        在控制台格式化显示所有 FileDriven 路径别名。
    #>

    $aliases = Get-BatToolAliases

    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host "  FileDriven 路径别名列表" -ForegroundColor White
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host "  Configs  → $($aliases.Configs)" -ForegroundColor Gray
    Write-Host "  Scripts  → $($aliases.Scripts)" -ForegroundColor Gray
    Write-Host "  WorkFlow → $($aliases.WorkFlow)" -ForegroundColor Gray
    Write-Host "  Output   → $($aliases.Output)" -ForegroundColor Gray
    Write-Host "  Root     → $($aliases.Root)" -ForegroundColor Gray
    Write-Host "============================================" -ForegroundColor Cyan
}

# ============================================
#  内部辅助函数
# ============================================

function Get-ProjectRoot {
    <#
    .SYNOPSIS
        获取项目根目录路径
    #>

    # 优先使用 $MyInvocation.MyCommand.Path（模块正常加载时）
    $scriptPath = $null
    if ($MyInvocation.MyCommand.Path) {
        $scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
    }
    # 回退：通过 $PSScriptRoot（模块导入时自动设置）
    elseif ($PSScriptRoot) {
        $scriptPath = $PSScriptRoot
    }
    # 最终回退：通过模块路径推断
    else {
        $module = Get-Module BatToolConfig
        if ($module -and $module.Path) {
            $scriptPath = Split-Path -Parent $module.Path
        }
    }

    if (-not $scriptPath) {
        throw "无法确定模块路径"
    }

    # Script\Modules\Core\ -> Script\ -> 项目根目录
    return (Get-Item (Join-Path $scriptPath "..\..\..")).FullName
}

function Get-DynamicOutputPath {
    <#
    .SYNOPSIS
        动态获取构建输出目录路径

    .PARAMETER projectRoot
        项目根目录路径
    #>

    param([string]$projectRoot)

    # 尝试通过 ReadProjectConfig.ps1 获取
    $readConfigPath = Join-Path $PSScriptRoot "ReadProjectConfig.ps1"
    if (Test-Path $readConfigPath) {
        try {
            . $readConfigPath
            $dir = Get-LauncherOutputDir -AutoBuild $false
            if ($dir) { return $dir }

            $dir = Get-BuildOutputPath -Configuration Debug
            if ($dir) { return $dir }
        }
        catch {
            # 忽略错误，使用默认路径
        }
    }

    # 回退：使用默认路径
    return Join-Path $projectRoot "BuildOutput\Debug"
}

# ============================================
#  模块导出
# ============================================

Export-ModuleMember -Function @(
    'Get-BatToolAliases',
    'Show-BatToolAliases'
)
