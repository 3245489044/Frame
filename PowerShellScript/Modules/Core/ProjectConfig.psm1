<#
.SYNOPSIS
    项目配置模块 - 统一管理项目构建配置常量

.DESCRIPTION
    提供项目构建相关的配置常量和映射表，消除 CleanupExtraBuildOutput.ps1
    和 CsprojFixEngine.psm1 之间的配置重复。

    用法:
        Import-Module ProjectConfig  # 或通过 Import-Modules.ps1 统一加载
        $config = Get-ProjectConfig

    返回的配置对象包含:
        - GlobalTargetFramework      : 全局 TargetFramework（如 'net10.0'）
        - InvalidTfmMap              : 无效 TFM 映射表
        - WinFormsDependentProjects  : 需要 UseWindowsForms 的项目列表
        - RedundantPlatforms         : 冗余平台配置列表
#>

# ============================================
#  项目构建配置
# ============================================

# 全局 TargetFramework
$script:GlobalTargetFramework = 'net10.0'

# 无效 TFM 映射表（便于扩展）
$script:InvalidTfmMap = @{
    'net10.0-windows7.0' = 'net10.0-windows'
}

# 需要 UseWindowsForms 的项目引用关系
$script:WinFormsDependentProjects = @(
    'WdUI.csproj',
    'FileDriven.UI.csproj',
    'BasicMethod.csproj'
)

# 冗余平台配置（用于 .sln 精简）
$script:RedundantPlatforms = @(
    'Debug|x64',
    'Debug|x86',
    'Release|x64',
    'Release|x86'
)

# ============================================
#  导出函数
# ============================================

function Get-ProjectConfig {
    <#
    .SYNOPSIS
        获取项目构建配置对象

    .DESCRIPTION
        返回包含所有项目构建配置常量的 hashtable，
        供 CleanupExtraBuildOutput.ps1、CsprojFixEngine.psm1 等脚本使用。

    .OUTPUTS
        hashtable，包含以下键：
        - GlobalTargetFramework      : 全局 TargetFramework 字符串
        - InvalidTfmMap              : 无效 TFM 映射表
        - WinFormsDependentProjects  : 需要 WinForms 的项目名称数组
        - RedundantPlatforms         : 冗余平台配置数组
    #>
    return @{
        GlobalTargetFramework      = $script:GlobalTargetFramework
        InvalidTfmMap              = $script:InvalidTfmMap.Clone()
        WinFormsDependentProjects  = $script:WinFormsDependentProjects.Clone()
        RedundantPlatforms         = $script:RedundantPlatforms.Clone()
    }
}

function Get-GlobalTargetFramework {
    <#
    .SYNOPSIS
        获取全局 TargetFramework
    #>
    return $script:GlobalTargetFramework
}

function Get-InvalidTfmMap {
    <#
    .SYNOPSIS
        获取无效 TFM 映射表
    #>
    return $script:InvalidTfmMap.Clone()
}

function Get-WinFormsDependentProjects {
    <#
    .SYNOPSIS
        获取需要 UseWindowsForms 的项目列表
    #>
    return $script:WinFormsDependentProjects.Clone()
}

function Get-RedundantPlatforms {
    <#
    .SYNOPSIS
        获取冗余平台配置列表
    #>
    return $script:RedundantPlatforms.Clone()
}

# ============================================
#  模块导出
# ============================================

Export-ModuleMember -Function @(
    'Get-ProjectConfig',
    'Get-GlobalTargetFramework',
    'Get-InvalidTfmMap',
    'Get-WinFormsDependentProjects',
    'Get-RedundantPlatforms'
)
