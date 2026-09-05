<#
.SYNOPSIS
    项目路径框架模块 - 统一管理项目根目录和子目录路径

.DESCRIPTION
    提供项目根目录智能检测、子目录快捷变量、路径别名导出等功能。
    功能等价于 BatTool\Tools\Utils\BatToolConfig.bat 的路径框架部分，
    但以 PowerShell 模块方式提供，供所有 .ps1 脚本使用。

    依赖:
        RootDetection.ps1 - Get-ProjectRoot
        BuildConfig.ps1 - Get-FileDrivenDir, Get-LauncherOutputDir, Get-BuildOutputPath

    用法:
        Import-Module .\Path\Project\ProjectPaths.psm1
        $projectRoot = Get-ProjectRoot
        $dirs = Get-ProjectDirectories
        $aliases = Get-PathAliases
#>

$script:ModuleDir = Split-Path -Parent $PSCommandPath
. "$script:ModuleDir\..\Core\RootDetection.ps1"
. "$script:ModuleDir\BuildConfig.ps1"

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

    $launcherDir = Get-LauncherOutputDir -ProjectRoot $ProjectRoot -AutoBuild $false
    if (-not $launcherDir) {
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

Export-ModuleMember -Function @(
    'Get-ProjectRoot',
    'Reset-ProjectRootCache',
    'Get-ProjectDirectories',
    'Get-PathAliases',
    'Set-AliasEnvironmentFromPaths',
    'Get-LauncherOutputDir',
    'Get-CsharpProjectRoot'
)