<#
.SYNOPSIS
    通用辅助函数模块 — 提供管理员权限检测、确认交互、模块加载等通用工具函数

.DESCRIPTION
    从 DockerCommon.psm1 中提取的纯通用辅助函数，去除了所有 Docker 特定依赖。
    适用于任何 PowerShell 脚本的通用操作。

    包含函数:
        Test-AdminPrivilege    — 检查是否以管理员权限运行
        Confirm-Action         — 通用确认函数（支持 Force/WhatIf 模式）
        Import-ModuleByPath    — 通用模块加载器（检测路径→加载→失败退出）

    用法:
        Import-Module .\Core\Utils.psm1
        if (Test-AdminPrivilege) { "以管理员运行" }
        if (Confirm-Action -Message "确认执行？" -Force) { ... }
        Import-ModuleByPath -ModulePath ".\MyModule.psm1"
#>

# ============================================================
# 权限检测
# ============================================================

function Test-AdminPrivilege {
    <#
    .SYNOPSIS
        检查是否以管理员权限运行
    .DESCRIPTION
        检查当前 PowerShell 进程是否以管理员权限运行。
    .OUTPUTS
        [bool] 管理员返回 $true，否则 $false
    .EXAMPLE
        if (Test-AdminPrivilege) { "以管理员权限运行" } else { "请以管理员身份运行" }
    #>
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# ============================================================
# 确认交互
# ============================================================

function Confirm-Action {
    <#
    .SYNOPSIS
        通用确认函数
    .DESCRIPTION
        提供统一的用户确认交互，支持 Force 跳过和 WhatIf 预览模式。
    .PARAMETER Message
        确认消息
    .PARAMETER Force
        跳过确认，直接返回 $true
    .PARAMETER WhatIf
        预览模式，返回 $false（不执行）
    .PARAMETER DefaultYes
        默认值是否为 Yes（默认 No）
    .OUTPUTS
        [bool] 确认返回 $true，取消返回 $false
    .EXAMPLE
        if (Confirm-Action -Message "确认删除？") { "执行删除" }
        if (Confirm-Action -Message "确认执行？" -Force) { "强制执行" }
        if (Confirm-Action -Message "确认？" -WhatIf) { "不会执行" }
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message,

        [switch]$Force,
        [switch]$WhatIf,
        [switch]$DefaultYes
    )

    if ($WhatIf) { return $false }
    if ($Force) { return $true }

    $prompt = if ($DefaultYes) { "(Y/n)" } else { "(y/N)" }
    $response = Read-Host "  $Message $prompt"

    if ($DefaultYes) {
        return ($response -ne 'n' -and $response -ne 'N')
    }
    return ($response -eq 'y' -or $response -eq 'Y')
}

# ============================================================
# 模块加载
# ============================================================

function Import-ModuleByPath {
    <#
    .SYNOPSIS
        通用模块加载器
    .DESCRIPTION
        封装模块路径解析和加载逻辑，自动检测模块路径并加载。
        失败时根据策略输出错误或退出。
    .PARAMETER ModulePath
        模块文件路径（绝对路径或相对路径）
    .PARAMETER Force
        强制重新加载模块
    .PARAMETER ExitOnFailure
        加载失败时是否退出脚本（默认 $true）
    .PARAMETER ErrorMessage
        自定义错误消息（可选）
    .OUTPUTS
        [bool] 加载成功返回 $true，失败返回 $false（ExitOnFailure 为 $false 时）
    .EXAMPLE
        Import-ModuleByPath -ModulePath ".\MyModule.psm1"
        Import-ModuleByPath -ModulePath ".\MyModule.psm1" -Force
        Import-ModuleByPath -ModulePath ".\Optional.psm1" -ExitOnFailure $false
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ModulePath,

        [switch]$Force,
        [switch]$ExitOnFailure = $true,
        [string]$ErrorMessage = ""
    )

    # 解析绝对路径
    $resolvedPath = if ([System.IO.Path]::IsPathRooted($ModulePath)) {
        $ModulePath
    } else {
        $fullPath = Join-Path (Get-Location).Path $ModulePath
        if (Test-Path $fullPath) { $fullPath } else { $ModulePath }
    }

    if (Test-Path $resolvedPath) {
        try {
            $params = @{
                Force = $true
                DisableNameChecking = $true
                ErrorAction = "Stop"
            }
            if ($Force) { $params.Force = $true }
            Import-Module $resolvedPath @params
            return $true
        } catch {
            $errMsg = if ($ErrorMessage) { $ErrorMessage } else { "无法加载模块: $resolvedPath" }
            Write-Host "❌ $errMsg" -ForegroundColor Red
            Write-Host "   错误: $_" -ForegroundColor Red
            if ($ExitOnFailure) { exit 1 }
            return $false
        }
    } else {
        $errMsg = if ($ErrorMessage) { $ErrorMessage } else { "模块文件不存在: $resolvedPath" }
        Write-Host "❌ $errMsg" -ForegroundColor Red
        if ($ExitOnFailure) { exit 1 }
        return $false
    }
}

# ============================================================
# 项目路径推导
# ============================================================

function Get-ProjectRoot {
    <#
    .SYNOPSIS
        从任意脚本路径推导项目根目录
    .DESCRIPTION
        从脚本所在目录向上回溯两层（Build/ → PowerShellScript/ → 项目根），
        兼容通过 PScriptManager 调用时 $PSScriptRoot 为空的情况。
    .PARAMETER Path
        脚本文件路径（可选，默认使用调用者路径）
    .OUTPUTS
        [string] 项目根目录绝对路径
    .EXAMPLE
        $root = Get-ProjectRoot
        $root = Get-ProjectRoot -Path $PSScriptRoot
    #>
    param(
        [string]$Path = ""
    )

    if (-not $Path) {
        $Path = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
    }
    return Resolve-Path (Join-Path $Path "..\..")
}

# ============================================================
# BasicMethod 模块加载器
# ============================================================

function Initialize-BasicMethod {
    <#
    .SYNOPSIS
        一键加载所有 BasicMethod 核心模块
    .DESCRIPTION
        自动加载 Core/Output.psm1、Core/Utils.psm1、DevOps/Docker.psm1 等核心模块。
        支持 Import-Module 和 dot-source 双重回退机制。
    .PARAMETER ProjectRoot
        项目根目录（可选，自动推导）
    .PARAMETER Modules
        要加载的模块路径数组（相对于 BasicMethod 目录，默认加载核心模块）
    .PARAMETER SkipMissing
        跳过不存在的模块而不报错（默认 $true）
    .EXAMPLE
        Initialize-BasicMethod
        Initialize-BasicMethod -ProjectRoot "D:\Project\Program\WorkFlowPrj"
        Initialize-BasicMethod -Modules @("Core\Output.psm1", "Core\Utils.psm1")
    #>
    [CmdletBinding()]
    param(
        [string]$ProjectRoot = "",
        [string[]]$Modules = @(),
        [switch]$SkipMissing = $true
    )

    # 推导项目根目录
    if (-not $ProjectRoot) {
        $callerPath = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
        $ProjectRoot = Get-ProjectRoot -Path $callerPath
    }

    $basicMethodDir = Join-Path $ProjectRoot "PowerShellScript\BasicMethod"

    # 默认核心模块
    if ($Modules.Count -eq 0) {
        $Modules = @(
            "Core\Output.psm1"
            "Core\Utils.psm1"
            "Core\Network.psm1"
            "DevOps\Docker.psm1"
            "DevOps\MCP.psm1"
            "Tooling\Diagnostic.psm1"
        )
    }

    $loaded = @()
    $failed = @()

    foreach ($mod in $Modules) {
        $modPath = Join-Path $basicMethodDir $mod
        if (-not (Test-Path $modPath)) {
            if (-not $SkipMissing) {
                Write-Warning "模块不存在: $modPath"
                $failed += $mod
            }
            continue
        }

        try {
            Import-Module $modPath -Force -DisableNameChecking -ErrorAction Stop
            $loaded += $mod
        } catch {
            try {
                . $modPath
                $loaded += $mod
            } catch {
                if (-not $SkipMissing) {
                    Write-Warning "模块加载失败: $modPath ($_)"
                }
                $failed += $mod
            }
        }
    }

    return [PSCustomObject]@{
        ProjectRoot    = $ProjectRoot
        BasicMethodDir = $basicMethodDir
        Loaded         = $loaded
        Failed         = $failed
        Success        = ($failed.Count -eq 0)
    }
}

# ============================================================
# 命令存在性检查
# ============================================================

function Test-CommandExists {
    <#
    .SYNOPSIS
        检查指定命令是否可用
    .PARAMETER Command
        命令名称（如 docker, node, dotnet）
    .OUTPUTS
        [bool] 可用返回 $true，否则 $false
    .EXAMPLE
        if (Test-CommandExists "docker") { "Docker 可用" }
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Command
    )
    try {
        $null = Get-Command $Command -ErrorAction Stop
        return $true
    } catch {
        return $false
    }
}

# ============================================================
# 模块导出
# ============================================================

Export-ModuleMember -Function @(
    'Test-AdminPrivilege',
    'Confirm-Action',
    'Import-ModuleByPath',
    'Test-CommandExists',
    'Get-ProjectRoot',
    'Initialize-BasicMethod'
)
