<#
.SYNOPSIS
    项目根目录检测模块 - 零依赖的智能项目根目录定位
.DESCRIPTION
    从调用方脚本目录（或指定起始路径）逐层向上查找，
    通过标记文件（.git 等）定位项目根目录。
    支持缓存，多次调用时性能更优。
    
    设计原则：
    - 零依赖：不依赖任何外部模块，可被任何 .ps1 脚本点引入使用
    - 单一职责：只负责项目根目录检测
    - 自动适配：支持从任意子目录调用
    
    用法:
        . "$PSScriptRoot\RootDetection.ps1"
        $root = Get-ProjectRoot
        $root = Get-ProjectRoot -StartPath "D:\my\script\dir" -ForceRescan
.NOTES
    此脚本设计为通过点引入（dot-source）方式加载，不直接执行。
    标记目录：.git
#>

# 根解析单源 = PScriptManager\Framework\DirectoryMap.ps1
. (Join-Path $PSScriptRoot '..\..\..\PScriptManager\Framework\DirectoryMap.ps1')

$script:RD_CachedProjectRoot = $null

$script:RD_MarkerDirs = @(
    ".git"
)

function Get-ProjectRoot {
    <#
    .SYNOPSIS
        智能检测项目根目录
    .DESCRIPTION
        从调用方脚本目录（或指定起始路径）逐层向上查找，
        通过标记文件（.git 等）定位项目根目录。
        此函数为独立实现，不依赖任何外部模块。
        支持缓存，多次调用时性能更优。
    .PARAMETER StartPath
        起始路径，默认使用调用方脚本目录
    .PARAMETER ForceRescan
        强制重新扫描，忽略缓存
    .OUTPUTS
        string: 项目根目录的完整路径
    #>
    param(
        [Parameter(Mandatory=$false)]
        [string]$StartPath = "",

        [Parameter(Mandatory=$false)]
        [switch]$ForceRescan
    )

    if (-not $ForceRescan -and $script:RD_CachedProjectRoot) {
        return $script:RD_CachedProjectRoot
    }

    if (-not $StartPath) {
        $StartPath = $PSScriptRoot
    }

    $script:RD_CachedProjectRoot = Get-SuperRoot -StartPath $StartPath
    return $script:RD_CachedProjectRoot
}

function Reset-ProjectRootCache {
    <#
    .SYNOPSIS
        清除项目根目录缓存（用于测试或配置变更后重新检测）
    #>
    $script:RD_CachedProjectRoot = $null
}