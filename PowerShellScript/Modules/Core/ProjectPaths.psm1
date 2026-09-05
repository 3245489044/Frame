<#
.SYNOPSIS
    项目路径框架模块 - 统一管理项目根目录和子目录路径

.DESCRIPTION
    提供项目根目录智能检测、子目录快捷变量、路径别名导出等功能。
    功能等价于 BatTool\Tools\Utils\BatToolConfig.bat 的路径框架部分，
    但以 PowerShell 模块方式提供，供所有 .ps1 脚本使用。

    检测策略（与 C# 的 ProjectPathManager 一致）：
    1. 从调用方脚本目录向上查找 .sln / .git 标记文件
    2. 如果找不到，回退到从模块位置上移固定级数

    用法:
        Import-Module ProjectPaths  # 或通过 Import-Modules.ps1 统一加载
        $projectRoot = Get-ProjectRoot
        $dirs = Get-ProjectDirectories
        $aliases = Get-PathAliases

.NOTES
    此模块设计为通过 Import-Module 加载，提供全局函数。
    与 BatToolConfig.bat 保持路径定义一致。
#>

# ============================================
#  引入项目配置文件读取器
#  从 Directory.Build.props 动态读取构建配置，消除硬编码路径
# ============================================

$script:ModuleDir = Split-Path -Parent $PSCommandPath
. "$script:ModuleDir\ReadProjectConfig.ps1"

# ============================================
#  内部常量
# ============================================

# 标记文件列表（已弃用）
# ★ MainProj.sln 和 Directory.Build.props 位于 CsharpPrj 子目录下，
#   用作根目录标记会导致路径重复。改用 .git 目录作为唯一标记。
#   与 C# ProjectPathManager 和 ReadProjectConfig.ps1 保持一致。
$script:MarkerFiles = @()

# 标记目录列表
$script:MarkerDirs = @(
    ".git"
)


# ============================================
#  项目根目录检测
#  ★ 委托给 ReadProjectConfig.ps1 中的 Get-ProjectRoot 实现
#  ★ 该实现包含缓存和 ForceRescan 支持，与 ProjectPaths 旧版逻辑一致
#  ★ 此模块不再重复定义 Get-ProjectRoot，避免函数重复和循环委托
# ============================================

function Reset-ProjectRootCache {
    <#
    .SYNOPSIS
        清除项目根目录缓存（用于测试或配置变更后重新检测）
    #>
    # ★ 直接操作 ReadProjectConfig.ps1 的缓存变量
    #   该变量通过点引入存在于模块作用域中
    $script:RPC_CachedProjectRoot = $null
}



# ============================================
#  子目录快捷变量
# ============================================

function Get-ProjectDirectories {
    <#
    .SYNOPSIS
        获取所有项目子目录路径

    .DESCRIPTION
        返回包含所有子目录路径的 hashtable，
        等价于 BatToolConfig.bat 的 DIR_* 变量。

    .PARAMETER ProjectRoot
        项目根目录，默认自动检测

    .OUTPUTS
        hashtable，包含以下键（与 BatToolConfig.bat 一致）：
        - ProjectRoot      : 项目根目录
        - BasicMethod      : BasicMethod
        - BatTool          : BatTool
        - CellFrame        : CellFrame
        - CentralConsole   : CentralConsole
        - DataManage       : DataManage
        - FileDriven       : FileDriven
        - FunctionStorage  : FunctionStorage
        - Godot            : Godot
        - JsonProcess      : JsonProcess
        - PythonModel      : PythonModel
        - RoslynMethod     : RoslynMethod
        - Script           : Script
        - Tools            : Tools
        - UniversalReference: UniversalReference
        - WdUI             : WdUI
        - WorkFlow         : WorkFlow
        - WorkFlowMethod   : WorkFlowMethod
        - Docs             : docs
        - EnableMent       : EnableMent
        - DataStorage      : DataStorage
        - PowerShellTests  : PowerShellTests
        - BuildOutput      : BuildOutput
    #>
    param(
        [Parameter(Mandatory=$false)]
        [string]$ProjectRoot = ""
    )

    if (-not $ProjectRoot) {
        $ProjectRoot = Get-ProjectRoot
    }

    # ★ 使用统一路径函数获取 FileDriven 目录（自动拼接 CsharpProjectRoot）
    $fileDrivenDir = Get-FileDrivenDir -ProjectRoot $ProjectRoot

    return @{
        ProjectRoot        = $ProjectRoot
        BasicMethod        = Join-Path $ProjectRoot "BasicMethod"
        BatTool            = Join-Path $ProjectRoot "BatTool"
        CellFrame          = Join-Path $ProjectRoot "CellFrame"
        CentralConsole     = Join-Path $ProjectRoot "CentralConsole"
        DataManage         = Join-Path $ProjectRoot "DataManage"
        FileDriven         = $fileDrivenDir
        FunctionStorage    = Join-Path $ProjectRoot "FunctionStorage"
        Godot              = Join-Path $ProjectRoot "Godot"
        JsonProcess        = Join-Path $ProjectRoot "JsonProcess"
        PythonModel        = Join-Path $ProjectRoot "PythonModel"
        RoslynMethod       = Join-Path $ProjectRoot "RoslynMethod"
        Script             = Join-Path $ProjectRoot "PowerShellScript"
        Tools              = Join-Path $ProjectRoot "Tools"
        UniversalReference = Join-Path $ProjectRoot "UniversalReference"
        WdUI               = Join-Path $ProjectRoot "WdUI"
        WorkFlow           = Join-Path $ProjectRoot "WorkFlow"
        WorkFlowMethod     = Join-Path $ProjectRoot "WorkFlowMethod"
        Docs               = Join-Path $ProjectRoot "docs"
        EnableMent         = Join-Path $ProjectRoot "EnableMent"
        DataStorage        = Join-Path $ProjectRoot "DataStorage"
        PowerShellTests    = Join-Path $ProjectRoot "PowerShellTests"
        BuildOutput        = Join-Path $ProjectRoot "BuildOutput"
    }
}

# ============================================
#  路径别名管理
# ============================================

function Get-PathAliases {
    <#
    .SYNOPSIS
        获取 FileDriven 路径别名

    .DESCRIPTION
        返回 FILEDRIVEN_ALIAS_* 路径别名 hashtable，
        等价于 BatToolConfig.bat 的 --export-aliases 输出。

    .PARAMETER ProjectRoot
        项目根目录，默认自动检测

    .OUTPUTS
        hashtable，包含以下键：
        - Configs  : FileDriven\DrivenConfig\JsonConfig
        - Scripts  : FileDriven\DrivenConfig\Scripts
        - WorkFlow : FileDriven\DrivenConfig\WorkFlow
        - Output   : BuildOutput\Debug\FileDriven.Launcher\net10.0-windows
        - Root     : 项目根目录
    #>
    param(
        [Parameter(Mandatory=$false)]
        [string]$ProjectRoot = ""
    )

    if (-not $ProjectRoot) {
        $ProjectRoot = Get-ProjectRoot
    }

    $fileDrivenDir = Get-FileDrivenDir -ProjectRoot $ProjectRoot

    # ★ 使用 ReadProjectConfig 动态获取启动器输出目录
    #   优先查找实际存在的 exe 所在目录，回退到从 Directory.Build.props 解析的路径
    $launcherDir = Get-LauncherOutputDir -ProjectRoot $ProjectRoot -AutoBuild $false
    if (-not $launcherDir) {
        # 如果未找到 exe，使用从 Directory.Build.props 解析的路径
        $launcherDir = Get-BuildOutputPath -ProjectRoot $ProjectRoot -Configuration "Debug"
    }

    return @{
        Configs  = Join-Path $fileDrivenDir "DrivenConfig\JsonConfig"
        Scripts  = Join-Path $fileDrivenDir "DrivenConfig\Scripts"
        WorkFlow = Join-Path $fileDrivenDir "DrivenConfig\WorkFlow"
        Output   = $launcherDir
        Root     = $ProjectRoot
    }


}

function Set-AliasEnvironmentFromPaths {
    <#
    .SYNOPSIS
        将路径别名注入到环境变量

    .DESCRIPTION
        将 Get-PathAliases 返回的别名设置为 FILEDRIVEN_ALIAS_* 环境变量，
        供子进程（如 Launcher.exe）使用。

    .PARAMETER Aliases
        别名 hashtable（来自 Get-PathAliases），默认自动获取

    .PARAMETER Overrides
        要覆盖的别名键值对 hashtable（可选）
        例如: @{ "Output" = "D:\custom\output\path" }
    #>
    param(
        [Parameter(Mandatory=$false)]
        [hashtable]$Aliases,

        [Parameter(Mandatory=$false)]
        [hashtable]$Overrides = @{}
    )

    if (-not $Aliases) {
        $Aliases = Get-PathAliases
    }

    foreach ($key in $Aliases.Keys) {
        $envKey = "FILEDRIVEN_ALIAS_$key"
        $value = $Aliases[$key]
        if ($Overrides.ContainsKey($key)) {
            $value = $Overrides[$key]
        }
        Set-Item -Path "env:$envKey" -Value $value
    }
}

# ============================================
#  Launcher 输出目录查找
# ============================================

function Get-LauncherOutputDir {
    <#
    .SYNOPSIS
        查找 FileDriven.Launcher.exe 的输出目录

    .DESCRIPTION
        委托给 ReadProjectConfig.ps1 中的 Get-LauncherOutputDir 实现，
        该实现包含多策略查找和自动构建功能。
        此函数仅作为模块导出接口，保持与旧代码的兼容性。

    .PARAMETER ProjectRoot
        项目根目录，默认自动检测

    .PARAMETER AutoBuild
        是否在未找到时自动构建，默认为 $true

    .OUTPUTS
        string: 启动器目录路径，未找到时返回 $null
    #>
    param(
        [Parameter(Mandatory=$false)]
        [string]$ProjectRoot = "",

        [Parameter(Mandatory=$false)]
        [bool]$AutoBuild = $true
    )

    # ★ 委托给 ReadProjectConfig.ps1 中的 Find-LauncherExe 实现
    #   该实现包含多策略查找（OutputPath 模板路径 → BuildOutput\Debug 根目录 → 自动构建）
    #   注意：不能直接调用 Get-LauncherOutputDir（会递归调用自身），
    #   而是调用 Find-LauncherExe（该函数名在 ReadProjectConfig.ps1 中定义，不会被覆盖）
    $exePath = Find-LauncherExe -ProjectRoot $ProjectRoot -AutoBuild $AutoBuild
    if ($exePath) {
        return Split-Path $exePath -Parent
    }
    return $null


}


# ============================================
#  模块导出
# ============================================

Export-ModuleMember -Function @(
    'Get-ProjectRoot',
    'Reset-ProjectRootCache',
    'Get-ProjectDirectories',
    'Get-PathAliases',
    'Set-AliasEnvironmentFromPaths',
    'Get-LauncherOutputDir',
    'Get-CsharpProjectRoot'
)
