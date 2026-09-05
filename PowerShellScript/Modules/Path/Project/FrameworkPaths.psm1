<#
.SYNOPSIS
    框架路径注册模块 - 提供各框架数据目录的统一注册和解析机制

.DESCRIPTION
    提供以下功能：
        Register-FrameworkPath — 注册框架的目录映射
        Get-FrameworkPath      — 获取框架指定目录的完整路径
        Get-AllFrameworkPaths  — 获取所有已注册的框架路径
        Clear-FrameworkPaths   — 清除所有注册的框架路径

    设计原则：
    - 可扩展：各框架（Install、BatTool、Tools 等）可自注册目录
    - 统一：替代分散在各框架中的硬编码路径拼接
    - 灵活：支持默认值和自定义覆盖

    用法:
        Import-Module .\Path\Project\FrameworkPaths.psm1
        Register-FrameworkPath -Name "Install" -Directories @{
            Manifests = "PowerShellScript\Install\Manifests"
            Providers = "PowerShellScript\Install\Providers"
            Core      = "PowerShellScript\Install\Core"
        }
        $manifestDir = Get-FrameworkPath -Framework "Install" -Directory "Manifests"
#>

$script:FP_RegisteredFrameworks = @{}

function Register-FrameworkPath {
    <#
    .SYNOPSIS
        注册框架的目录映射
    .PARAMETER Name
        框架名称（如 Install、BatTool、Tools）
    .PARAMETER Directories
        目录映射哈希表（键为目录别名，值为相对于项目根目录的路径）
    .PARAMETER ProjectRoot
        项目根目录（可选，默认自动检测）
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [hashtable]$Directories,

        [string]$ProjectRoot = ""
    )

    if (-not $ProjectRoot) {
        $ProjectRoot = Get-ProjectRoot
    }

    $resolvedDirs = @{}
    foreach ($key in $Directories.Keys) {
        $resolvedDirs[$key] = Join-Path $ProjectRoot $Directories[$key]
    }

    $script:FP_RegisteredFrameworks[$Name] = @{
        ProjectRoot = $ProjectRoot
        Directories = $resolvedDirs
    }
}

function Get-FrameworkPath {
    <#
    .SYNOPSIS
        获取框架指定目录的完整路径
    .PARAMETER Framework
        框架名称
    .PARAMETER Directory
        目录别名（如 Manifests、Providers、Core）
    .PARAMETER ProjectRoot
        项目根目录（可选，用于动态解析）
    .OUTPUTS
        [string] 目录完整路径，框架或目录未注册时返回 $null
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Framework,

        [Parameter(Mandatory = $true)]
        [string]$Directory,

        [string]$ProjectRoot = ""
    )

    if (-not $script:FP_RegisteredFrameworks.ContainsKey($Framework)) {
        return $null
    }

    $frameworkConfig = $script:FP_RegisteredFrameworks[$Framework]
    if (-not $frameworkConfig.Directories.ContainsKey($Directory)) {
        return $null
    }

    return $frameworkConfig.Directories[$Directory]
}

function Get-AllFrameworkPaths {
    <#
    .SYNOPSIS
        获取所有已注册的框架路径
    .OUTPUTS
        [hashtable] 所有框架的目录映射
    #>
    return $script:FP_RegisteredFrameworks
}

function Clear-FrameworkPaths {
    <#
    .SYNOPSIS
        清除所有注册的框架路径（用于测试或重新初始化）
    #>
    $script:FP_RegisteredFrameworks = @{}
}

function Get-FrameworkDataPath {
    <#
    .SYNOPSIS
        获取框架数据文件路径（如 manifest.json）
    .PARAMETER Framework
        框架名称
    .PARAMETER Directory
        目录别名
    .PARAMETER FileName
        文件名
    .OUTPUTS
        [string] 文件完整路径
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Framework,

        [Parameter(Mandatory = $true)]
        [string]$Directory,

        [Parameter(Mandatory = $true)]
        [string]$FileName
    )

    $dirPath = Get-FrameworkPath -Framework $Framework -Directory $Directory
    if (-not $dirPath) {
        return $null
    }

    return Join-Path $dirPath $FileName
}

Export-ModuleMember -Function @(
    'Register-FrameworkPath',
    'Get-FrameworkPath',
    'Get-AllFrameworkPaths',
    'Clear-FrameworkPaths',
    'Get-FrameworkDataPath'
)