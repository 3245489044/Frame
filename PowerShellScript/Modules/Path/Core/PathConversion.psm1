<#
.SYNOPSIS
    路径转换与归一化模块 — 提供容器路径↔宿主机路径转换、路径归一化等通用路径处理

.DESCRIPTION
    包含函数:
        Convert-ContainerPathToHost  — 容器路径→宿主机路径转换
        Convert-HostPathToContainer  — 宿主机路径→容器路径转换
        Normalize-Path               — 智能路径归一化（统一分隔符、去重）
        Test-IsContainerPath         — 检测路径是否为容器路径格式
        Resolve-ProjectPath          — 项目路径解析（自动处理容器路径）

    依赖:
        RootDetection.ps1 - Get-ProjectRoot

    用法:
        Import-Module .\Path\Core\PathConversion.psm1
        $hostPath = Convert-ContainerPathToHost -Path "/workspace/WorkFlowPrj/test.txt"
        $normalized = Normalize-Path -Path "D:\Project\Program\WorkFlowPrj\test.ps1"
#>

$script:ModuleDir = Split-Path -Parent $PSCommandPath
. "$script:ModuleDir\RootDetection.ps1"

function Convert-ContainerPathToHost {
    <#
    .SYNOPSIS
        将容器路径转换为宿主机路径
    .DESCRIPTION
        将 Docker 容器内的路径（如 /workspace/WorkFlowPrj/PowerShellScript/test.ps1）
        转换为宿主机 Windows 路径（如 D:\Project\Program\WorkFlowPrj\PowerShellScript\test.ps1）。
        如果传入的路径已经是宿主机路径格式，则直接返回。
        自动统一分隔符为 Windows 风格（/ → \）并移除重复分隔符。
    .PARAMETER Path
        容器内路径或宿主机路径
    .PARAMETER ProjectRoot
        项目根目录（可选，默认自动检测）
    .OUTPUTS
        [string] 转换后的宿主机路径
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [string]$ProjectRoot = ""
    )

    if (-not $ProjectRoot) {
        $ProjectRoot = Get-ProjectRoot
    }

    if ($Path -match '^[A-Za-z]:\\') {
        return $Path
    }

    if ($Path -match '\\workspace\\') {
        if ($Path -match '\\workspace\\[^\\]+\\?(.*)') {
            $relativePath = $matches[1]
            return Join-Path $ProjectRoot $relativePath
        }
        return $Path
    }

    if ($Path -match '^/workspace/') {
        $relativePath = $Path -replace '^/workspace/[^/]+/', ''
        if ([string]::IsNullOrWhiteSpace($relativePath)) {
            return $ProjectRoot
        }
        return Join-Path $ProjectRoot $relativePath
    }

    if ($Path -notmatch '^[A-Za-z]:\\' -and $Path -notmatch '^/') {
        return Join-Path $ProjectRoot $Path
    }

    return $Path
}

function Convert-HostPathToContainer {
    <#
    .SYNOPSIS
        将宿主机路径转换为容器路径
    .DESCRIPTION
        将宿主机 Windows 路径（如 D:\Project\Program\WorkFlowPrj\test.txt）
        转换为 Docker 容器内路径（如 /workspace/WorkFlowPrj/test.txt）。
        如果传入的路径已经是容器路径格式，则直接返回。
    .PARAMETER Path
        宿主机路径或容器路径
    .PARAMETER ProjectRoot
        项目根目录（可选，默认自动检测）
    .PARAMETER ContainerWorkspace
        容器内工作目录（默认 /workspace）
    .OUTPUTS
        [string] 转换后的容器路径
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [string]$ProjectRoot = "",

        [string]$ContainerWorkspace = "/workspace"
    )

    if (-not $ProjectRoot) {
        $ProjectRoot = Get-ProjectRoot
    }

    if ($Path -match '^/workspace/' -or $Path -match '^/') {
        return $Path
    }

    if ($Path -match '^[A-Za-z]:\\') {
        $normalized = $Path -replace '\\', '/'
        $projectNormalized = $ProjectRoot -replace '\\', '/'

        if ($normalized -like "$projectNormalized*") {
            $relative = $normalized.Substring($projectNormalized.Length).TrimStart('/')
            if ($relative) {
                return "$ContainerWorkspace/$relative"
            }
            return $ContainerWorkspace
        }

        return $normalized
    }

    if ($Path -notmatch '^[A-Za-z]:\\' -and $Path -notmatch '^/') {
        $fullPath = Join-Path $ProjectRoot $Path
        return Convert-HostPathToContainer -Path $fullPath -ProjectRoot $ProjectRoot -ContainerWorkspace $ContainerWorkspace
    }

    return $Path
}

function Normalize-Path {
    <#
    .SYNOPSIS
        智能路径归一化入口
    .DESCRIPTION
        智能路径归一化，自动处理以下情况：
        - 容器路径 → 宿主机路径
        - 双重拼接路径 → 修复为正确路径
        - 相对路径 → 拼接为绝对路径
        - 绝对路径 → 原样返回
    .PARAMETER Path
        要归一化的路径
    .PARAMETER ProjectRoot
        项目根目录（可选，默认自动检测）
    .OUTPUTS
        [string] 归一化后的宿主机绝对路径
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [string]$ProjectRoot = ""
    )

    if (-not $ProjectRoot) {
        $ProjectRoot = Get-ProjectRoot
    }

    $converted = Convert-ContainerPathToHost -Path $Path -ProjectRoot $ProjectRoot
    $converted = $converted -replace '/', '\'

    while ($converted -match '\\\\') {
        $converted = $converted -replace '\\\\', '\'
    }

    return $converted
}

function Test-IsContainerPath {
    <#
    .SYNOPSIS
        检测路径是否为容器内路径格式
    .DESCRIPTION
        检查路径是否以 /workspace/ 开头（Docker 容器内路径特征）。
    .PARAMETER Path
        要检测的路径
    .OUTPUTS
        [bool] 是容器路径返回 $true，否则 $false
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    return $Path -match '^/workspace/'
}

function Resolve-ProjectPath {
    <#
    .SYNOPSIS
        解析相对于项目根目录的路径
    .DESCRIPTION
        自动处理容器路径（/workspace/WorkFlowPrj/...）到宿主机路径的转换。
        如果传入的路径已经是宿主机绝对路径，则直接返回。
    .PARAMETER RelativePath
        相对于项目根目录的路径，或容器路径，或宿主机绝对路径
    .PARAMETER ProjectRoot
        项目根目录（可选，默认自动检测）
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$RelativePath,

        [string]$ProjectRoot = ""
    )

    if (-not $ProjectRoot) {
        $ProjectRoot = Get-ProjectRoot
    }

    return Convert-ContainerPathToHost -Path $RelativePath -ProjectRoot $ProjectRoot
}

Export-ModuleMember -Function @(
    'Convert-ContainerPathToHost',
    'Convert-HostPathToContainer',
    'Normalize-Path',
    'Test-IsContainerPath',
    'Resolve-ProjectPath'
)