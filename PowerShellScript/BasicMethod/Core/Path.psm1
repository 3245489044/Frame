<#
.SYNOPSIS
    路径处理工具函数模块 — 提供容器路径↔宿主机路径转换、路径归一化等通用路径处理

.DESCRIPTION
    从 DockerCommon.psm1 中提取的纯通用路径处理函数，去除了所有 Docker 特定依赖。
    适用于任何需要路径格式转换、归一化、检测的场景。

    包含函数:
        Convert-ContainerPathToHost  — 容器路径→宿主机路径转换
        Convert-HostPathToContainer  — 宿主机路径→容器路径转换
        Normalize-Path               — 智能路径归一化（统一分隔符、去重）
        Test-IsContainerPath         — 检测路径是否为容器路径格式
        Resolve-ProjectPath          — 项目路径解析（自动处理容器路径）

    用法:
        Import-Module .\Core\Path.psm1
        $hostPath = Convert-ContainerPathToHost -Path "/workspace/WorkFlowPrj/test.txt" -ProjectRoot "D:\Project\Program\WorkFlowPrj"
        $normalized = Normalize-Path -Path "D:\Project\Program\WorkFlowPrj\PowerShellScript\test.ps1"
        if (Test-IsContainerPath "/workspace/...") { "是容器路径" }
#>

# ============================================================
# 路径转换函数
# ============================================================

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
    .EXAMPLE
        Convert-ContainerPathToHost -Path "/workspace/WorkFlowPrj/PowerShellScript/test.ps1"
        # 返回: D:\Project\Program\WorkFlowPrj\PowerShellScript\test.ps1
    .EXAMPLE
        Convert-ContainerPathToHost -Path "/workspace/WorkFlowPrj/test.txt" -ProjectRoot "C:\MyProject"
        # 返回: C:\MyProject\test.txt
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [string]$ProjectRoot = ""
    )

    # 获取项目根目录
    if (-not $ProjectRoot) {
        $ProjectRoot = Get-ProjectRoot
    }

    # 情况1: 已经是宿主机路径（包含盘符）
    if ($Path -match '^[A-Za-z]:\\') {
        return $Path
    }

    # 情况2: 双重拼接路径，需要修复（如 D:\...\workspace\WorkFlowPrj\...）
    if ($Path -match '\\workspace\\') {
        if ($Path -match '\\workspace\\[^\\]+\\?(.*)') {
            $relativePath = $matches[1]
            return Join-Path $ProjectRoot $relativePath
        }
        return $Path
    }

    # 情况3: 容器路径格式（/workspace/...）
    if ($Path -match '^/workspace/') {
        $relativePath = $Path -replace '^/workspace/[^/]+/', ''
        if ([string]::IsNullOrWhiteSpace($relativePath)) {
            return $ProjectRoot
        }
        return Join-Path $ProjectRoot $relativePath
    }

    # 情况4: 相对路径，直接拼接
    if ($Path -notmatch '^[A-Za-z]:\\' -and $Path -notmatch '^/') {
        return Join-Path $ProjectRoot $Path
    }

    # 其他情况，原样返回
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
    .EXAMPLE
        Convert-HostPathToContainer -Path "D:\Project\Program\WorkFlowPrj\test.txt"
        # 返回: /workspace/WorkFlowPrj/test.txt
    .EXAMPLE
        Convert-HostPathToContainer -Path "C:\Data\file.txt" -ContainerWorkspace "/data"
        # 返回: /data/file.txt
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [string]$ProjectRoot = "",

        [string]$ContainerWorkspace = "/workspace"
    )

    # 获取项目根目录
    if (-not $ProjectRoot) {
        $ProjectRoot = Get-ProjectRoot
    }

    # 情况1: 已经是容器路径
    if ($Path -match '^/workspace/' -or $Path -match '^/') {
        return $Path
    }

    # 情况2: 宿主机路径，转换为容器路径
    if ($Path -match '^[A-Za-z]:\\') {
        # 统一分隔符
        $normalized = $Path -replace '\\', '/'
        $projectNormalized = $ProjectRoot -replace '\\', '/'

        # 如果在项目根目录下，使用 /workspace/ 前缀
        if ($normalized -like "$projectNormalized*") {
            $relative = $normalized.Substring($projectNormalized.Length).TrimStart('/')
            if ($relative) {
                return "$ContainerWorkspace/$relative"
            }
            return $ContainerWorkspace
        }

        # 不在项目根目录下，直接返回归一化路径
        return $normalized
    }

    # 情况3: 相对路径，拼接项目根目录后转换
    if ($Path -notmatch '^[A-Za-z]:\\' -and $Path -notmatch '^/') {
        $fullPath = Join-Path $ProjectRoot $Path
        return Convert-HostPathToContainer -Path $fullPath -ProjectRoot $ProjectRoot -ContainerWorkspace $ContainerWorkspace
    }

    return $Path
}

# ============================================================
# 路径归一化函数
# ============================================================

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
    .EXAMPLE
        Normalize-Path -Path "/workspace/WorkFlowPrj/test.txt"
        Normalize-Path -Path "PowerShellScript/test.ps1"
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [string]$ProjectRoot = ""
    )

    # 获取项目根目录
    if (-not $ProjectRoot) {
        $ProjectRoot = Get-ProjectRoot
    }

    # 委托 Convert-ContainerPathToHost 处理路径转换
    $converted = Convert-ContainerPathToHost -Path $Path -ProjectRoot $ProjectRoot

    # 统一分隔符为 Windows 风格
    $converted = $converted -replace '/', '\'

    # 移除可能的重复分隔符
    while ($converted -match '\\\\') {
        $converted = $converted -replace '\\\\', '\'
    }

    return $converted
}

# ============================================================
# 路径检测函数
# ============================================================

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
    .EXAMPLE
        if (Test-IsContainerPath "/workspace/WorkFlowPrj/test.txt") { "是容器路径" }
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    return $Path -match '^/workspace/'
}

# ============================================================
# 路径解析函数
# ============================================================

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
    .EXAMPLE
        Resolve-ProjectPath -RelativePath "DevEnvironment\Docker\config\mcp-gateway-config.json"
        Resolve-ProjectPath -RelativePath "/workspace/WorkFlowPrj/DevEnvironment/Docker/config/mcp-gateway-config.json"
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$RelativePath,

        [string]$ProjectRoot = ""
    )

    # 获取项目根目录
    if (-not $ProjectRoot) {
        $ProjectRoot = Get-ProjectRoot
    }

    # 委托 Convert-ContainerPathToHost 处理所有路径转换逻辑
    return Convert-ContainerPathToHost -Path $RelativePath -ProjectRoot $ProjectRoot
}

# ============================================================
# 模块导出
# ============================================================

Export-ModuleMember -Function @(
    'Convert-ContainerPathToHost',
    'Convert-HostPathToContainer',
    'Normalize-Path',
    'Test-IsContainerPath',
    'Resolve-ProjectPath'
)
