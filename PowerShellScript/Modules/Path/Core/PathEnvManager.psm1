<#
.SYNOPSIS
    路径环境管理模块 - 提供通用的 PATH 环境变量和环境变量管理功能

.DESCRIPTION
    提供以下功能：
        Set-PathEntries       — 添加路径条目到 PATH 环境变量（支持 local/docker）
        Set-EnvironmentVariables — 设置环境变量（支持模板替换、local/docker）
        Get-PathEntryExists   — 检查路径条目是否已存在于 PATH
        Remove-PathEntry      — 从 PATH 环境变量中移除路径条目

    设计原则：
    - 支持 local 和 docker 双模式
    - 自动路径去重
    - 支持 $installDir 模板变量替换
    - 立即生效（同时更新进程环境和用户级环境）

    用法:
        Import-Module .\Path\Core\PathEnvManager.psm1
        Set-PathEntries -PathEntries @("bin", "tools") -BaseDir "D:\tools\mytool"
        Set-EnvironmentVariables -EnvVars @{MY_TOOL_HOME="D:\tools\mytool"}
#>

function Get-PathEntryExists {
    <#
    .SYNOPSIS
        检查路径条目是否已存在于 PATH 环境变量
    .PARAMETER PathEntry
        要检查的路径条目
    .PARAMETER Target
        目标环境：local | docker
    .PARAMETER Executor
        Docker 执行器脚本块
    .OUTPUTS
        [bool] 存在返回 $true，否则 $false
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$PathEntry,

        [string]$Target = "local",
        [scriptblock]$Executor = $null
    )

    if ($Target -eq "docker" -and $Executor) {
        $result = & $Executor "echo `$PATH"
        return $result -like "*$PathEntry*"
    }

    $currentPath = [Environment]::GetEnvironmentVariable("Path", "User")
    return $currentPath -like "*$PathEntry*"
}

function Set-PathEntries {
    <#
    .SYNOPSIS
        添加路径条目到 PATH 环境变量
    .DESCRIPTION
        将相对或绝对路径添加到 PATH 环境变量，支持 local 和 docker 模式，
        自动去重，避免重复添加。
    .PARAMETER PathEntries
        路径条目数组（相对路径基于 BaseDir，绝对路径直接使用）
    .PARAMETER BaseDir
        基础目录（用于解析相对路径）
    .PARAMETER Target
        目标环境：local | docker
    .PARAMETER Executor
        Docker 执行器脚本块（Target=docker 时使用）
    #>
    param(
        [array]$PathEntries,
        [string]$BaseDir,
        [string]$Target = "local",
        [scriptblock]$Executor = $null
    )

    if (-not $PathEntries -or $PathEntries.Count -eq 0) { return }

    foreach ($entry in $PathEntries) {
        $fullPath = if ([System.IO.Path]::IsPathRooted($entry)) { $entry } else { Join-Path $BaseDir $entry }

        if ($Target -eq "docker" -and $Executor) {
            & $Executor "echo 'export PATH=`$PATH:$fullPath' >> /root/.bashrc"
        } else {
            $currentPath = [Environment]::GetEnvironmentVariable("Path", "User")
            if ($currentPath -notlike "*$fullPath*") {
                [Environment]::SetEnvironmentVariable("Path", "$currentPath;$fullPath", "User")
                $env:Path = "$env:Path;$fullPath"
            }
        }
    }
}

function Remove-PathEntry {
    <#
    .SYNOPSIS
        从 PATH 环境变量中移除路径条目
    .PARAMETER PathEntry
        要移除的路径条目
    .PARAMETER Target
        目标环境：local | docker
    .PARAMETER Executor
        Docker 执行器脚本块
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$PathEntry,

        [string]$Target = "local",
        [scriptblock]$Executor = $null
    )

    if ($Target -eq "docker" -and $Executor) {
        & $Executor "sed -i '/export PATH=`$PATH:$PathEntry/d' /root/.bashrc"
        return
    }

    $currentPath = [Environment]::GetEnvironmentVariable("Path", "User")
    $newPath = $currentPath -replace [regex]::Escape(";$PathEntry"), ""
    $newPath = $newPath -replace [regex]::Escape("$PathEntry;"), ""
    $newPath = $newPath -replace [regex]::Escape($PathEntry), ""

    [Environment]::SetEnvironmentVariable("Path", $newPath, "User")
    $env:Path = $newPath
}

function Set-EnvironmentVariables {
    <#
    .SYNOPSIS
        设置环境变量
    .DESCRIPTION
        设置用户级环境变量，支持模板替换（$installDir 或 ${installDir}），
        支持 local 和 docker 双模式。
    .PARAMETER EnvVars
        环境变量哈希表（键为变量名，值为变量值）
    .PARAMETER InstallDir
        安装目录（用于模板替换）
    .PARAMETER Target
        目标环境：local | docker
    .PARAMETER Executor
        Docker 执行器脚本块（Target=docker 时使用）
    #>
    param(
        [object]$EnvVars,
        [string]$InstallDir,
        [string]$Target = "local",
        [scriptblock]$Executor = $null
    )

    if (-not $EnvVars) { return }

    foreach ($var in $EnvVars.PSObject.Properties) {
        $value = $var.Value
        if ($value -match '\$installDir|\$\{installDir\}') {
            $value = $value -replace '\$installDir|\$\{installDir\}', $InstallDir
        }

        if ($Target -eq "docker" -and $Executor) {
            & $Executor "echo 'export $($var.Name)=$value' >> /root/.bashrc"
        } else {
            [Environment]::SetEnvironmentVariable($var.Name, $value, "User")
            Set-Item "Env:$($var.Name)" $value
        }
    }
}

function Get-EnvironmentVariable {
    <#
    .SYNOPSIS
        获取环境变量值
    .PARAMETER Name
        环境变量名
    .PARAMETER Target
        目标环境：local | docker
    .PARAMETER Executor
        Docker 执行器脚本块
    .OUTPUTS
        [string] 环境变量值
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [string]$Target = "local",
        [scriptblock]$Executor = $null
    )

    if ($Target -eq "docker" -and $Executor) {
        $result = & $Executor "echo `$$Name"
        return $result.Trim()
    }

    return [Environment]::GetEnvironmentVariable($Name, "User")
}

Export-ModuleMember -Function @(
    'Set-PathEntries',
    'Set-EnvironmentVariables',
    'Get-PathEntryExists',
    'Remove-PathEntry',
    'Get-EnvironmentVariable'
)