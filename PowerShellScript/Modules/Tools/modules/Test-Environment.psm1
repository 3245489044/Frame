<#
.SYNOPSIS
    环境检测模块 — 提供统一的系统环境检测函数
.DESCRIPTION
    包括命令可用性检测、管理员权限检测、Docker/WSL 状态检测等。
    所有 Tools 脚本共享的环境检测函数。

    用法:
        Import-Module .\Test-Environment.psm1 -Force
        Test-CommandExists "docker"
        Test-AdminPrivilege
        Test-DockerRunning
        Test-ImageExists -ImageName "workflow-mcp-env:latest"
        Test-ContainerRunning -ContainerName "workflow-mcp-env"
#>

function Test-CommandExists {
    <#
    .SYNOPSIS
        检查命令是否可用
    .PARAMETER Command
        命令名称
    .EXAMPLE
        Test-CommandExists "docker"
    #>
    param([string]$Command)
    return $null -ne (Get-Command $Command -ErrorAction SilentlyContinue)
}

function Test-AdminPrivilege {
    <#
    .SYNOPSIS
        检查是否以管理员权限运行
    .EXAMPLE
        if (-not (Test-AdminPrivilege)) { Write-Warning "请以管理员身份运行" }
    #>
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-DockerRunning {
    <#
    .SYNOPSIS
        检查 Docker 引擎是否运行
    .DESCRIPTION
        通过 docker info 快速检测 Docker 引擎是否可用。
    .EXAMPLE
        if (Test-DockerRunning) { Write-Success "Docker 运行中" }
    #>
    if (-not (Test-CommandExists "docker")) { return $false }
    try {
        $version = docker info --format "{{.ServerVersion}}" 2>&1
        return ($version -match '\d+\.\d+\.\d+')
    } catch {
        return $false
    }
}

function Get-DockerVersion {
    <#
    .SYNOPSIS
        获取 Docker 引擎版本号
    .EXAMPLE
        $ver = Get-DockerVersion
    #>
    if (-not (Test-CommandExists "docker")) { return $null }
    try {
        return (docker info --format "{{.ServerVersion}}" 2>&1).Trim()
    } catch {
        return $null
    }
}

function Test-ImageExists {
    <#
    .SYNOPSIS
        检查 Docker 镜像是否存在
    .PARAMETER ImageName
        镜像名称（如 workflow-mcp-env:latest）
    .EXAMPLE
        Test-ImageExists -ImageName "workflow-mcp-env:latest"
    #>
    param([string]$ImageName)
    $result = docker images --format "{{.Repository}}:{{.Tag}}" $ImageName 2>$null
    return [string]::IsNullOrWhiteSpace($result) -eq $false
}

function Test-ContainerRunning {
    <#
    .SYNOPSIS
        检查 Docker 容器是否在运行
    .PARAMETER ContainerName
        容器名称
    .EXAMPLE
        Test-ContainerRunning -ContainerName "workflow-mcp-env"
    #>
    param([string]$ContainerName)
    try {
        $state = docker inspect --format "{{.State.Status}}" $ContainerName 2>$null
        return ($state -eq "running")
    } catch {
        return $false
    }
}

function Get-ContainerHealth {
    <#
    .SYNOPSIS
        获取 Docker 容器健康状态
    .PARAMETER ContainerName
        容器名称
    .EXAMPLE
        $health = Get-ContainerHealth -ContainerName "workflow-mcp-env"
    #>
    param([string]$ContainerName)
    try {
        return docker inspect --format "{{.State.Health.Status}}" $ContainerName 2>&1
    } catch {
        return $null
    }
}

function Test-WslCommand {
    <#
    .SYNOPSIS
        检查 WSL 命令是否可用
    #>
    return Test-CommandExists "wsl"
}

function Test-WslDockerDesktop {
    <#
    .SYNOPSIS
        检查 WSL docker-desktop 发行版状态
    .RETURNS
        "running" / "exists" / "missing"
    #>
    if (-not (Test-WslCommand)) { return "missing" }
    try {
        $wslList = wsl --list --verbose 2>&1
        if ($wslList -match "docker-desktop\s+Running") { return "running" }
        if ($wslList -match "docker-desktop") { return "exists" }
        return "missing"
    } catch {
        return "missing"
    }
}

function Find-VscodeCmd {
    <#
    .SYNOPSIS
        查找 VS Code 的 code.cmd 路径
    .EXAMPLE
        $codeCmd = Find-VscodeCmd
    #>
    # 先尝试 PATH 中的 code
    $codeCmd = Get-Command "code" -ErrorAction SilentlyContinue
    if ($codeCmd) { return "code" }

    # 遍历常见安装目录
    $searchPaths = @(
        "${env:ProgramFiles}\Microsoft VS Code\bin\code.cmd",
        "${env:ProgramFiles(x86)}\Microsoft VS Code\bin\code.cmd",
        "${env:LOCALAPPDATA}\Programs\Microsoft VS Code\bin\code.cmd",
        "D:\File\VSCode\Microsoft VS Code\bin\code.cmd",
        "${env:USERPROFILE}\AppData\Local\Programs\Microsoft VS Code\bin\code.cmd"
    )

    foreach ($path in $searchPaths) {
        if (Test-Path $path) { return $path }
    }

    return $null
}

# 导出所有函数
Export-ModuleMember -Function Test-CommandExists, Test-AdminPrivilege, Test-DockerRunning,
    Get-DockerVersion, Test-ImageExists, Test-ContainerRunning, Get-ContainerHealth,
    Test-WslCommand, Test-WslDockerDesktop, Find-VscodeCmd
