<#
.SYNOPSIS
    脚本上下文模块 - 提供可靠的脚本目录获取和同级模块路径解析

.DESCRIPTION
    提供以下功能：
        Get-ScriptDirectory  — 可靠获取调用方脚本所在目录
        Resolve-SiblingPath   — 解析相对于脚本目录的同级/上级路径

    设计原则：
    - 多级 fallback：$PSScriptRoot → $MyInvocation.MyCommand.Path → Get-PSCallStack
    - 支持任意调用深度，正确返回实际脚本位置
    - 消除重复的 Split-Path -Parent $MyInvocation.MyCommand.Path 模式

    用法:
        Import-Module .\Path\Core\ScriptContext.psm1
        $scriptDir = Get-ScriptDirectory
        $coreModule = Resolve-SiblingPath "..\Core\ProviderBase.ps1"
#>

function Get-ScriptDirectory {
    <#
    .SYNOPSIS
        可靠获取调用方脚本所在目录
    .DESCRIPTION
        通过多级 fallback 机制获取脚本目录：
        1. $PSScriptRoot - 模块加载时可用
        2. $MyInvocation.MyCommand.Path - 点引入时可用
        3. Get-PSCallStack - 递归调用时可用
    .PARAMETER CallStackDepth
        调用栈深度（默认 1，即获取直接调用方的目录）
    .OUTPUTS
        [string] 脚本所在目录的完整路径
    #>
    param(
        [int]$CallStackDepth = 1
    )

    if ($PSScriptRoot) {
        return $PSScriptRoot
    }

    if ($MyInvocation.MyCommand.Path) {
        return Split-Path -Parent $MyInvocation.MyCommand.Path
    }

    $callStack = Get-PSCallStack
    $targetIndex = $CallStackDepth + 1
    if ($callStack.Count -gt $targetIndex) {
        $scriptPath = $callStack[$targetIndex].ScriptName
        if ($scriptPath) {
            return Split-Path -Parent $scriptPath
        }
    }

    return $PWD.Path
}

function Resolve-SiblingPath {
    <#
    .SYNOPSIS
        解析相对于脚本目录的同级/上级路径
    .DESCRIPTION
        将相对路径（如 "..\Core\ProviderBase.ps1"）解析为完整路径，
        基于 Get-ScriptDirectory 获取的脚本目录。
    .PARAMETER RelativePath
        相对于脚本目录的路径
    .PARAMETER CallStackDepth
        调用栈深度（传递给 Get-ScriptDirectory）
    .OUTPUTS
        [string] 解析后的完整路径
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$RelativePath,

        [int]$CallStackDepth = 1
    )

    $scriptDir = Get-ScriptDirectory -CallStackDepth $CallStackDepth
    return Join-Path $scriptDir $RelativePath
}

Export-ModuleMember -Function @(
    'Get-ScriptDirectory',
    'Resolve-SiblingPath'
)