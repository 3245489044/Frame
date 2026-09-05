# @requires: BasicMethod/DevOps/Docker
# @version: 1.0.0
# @author: WorkFlowPrj
# @category: install
# @alias: install-executor
# @exports: Get-Executor, Test-ExecutorAvailable, Invoke-ExecutorCommand

<#
.SYNOPSIS
    Install Framework 执行器抽象模块
.DESCRIPTION
    提供本地/远程（Docker）执行器的统一抽象，
    使得 Provider 可以透明地在不同环境中执行命令。

    执行器是一个脚本块：{ param($cmd) <执行命令> }
    返回命令输出，设置 $LASTEXITCODE。
.NOTES
    依赖：BasicMethod/DevOps/Docker
#>

function Get-Executor {
    <#
    .SYNOPSIS
        获取执行器脚本块
    .PARAMETER Target
        目标环境：local | docker
    .PARAMETER ContainerName
        Docker 容器名称（Target=docker 时）
    .EXAMPLE
        $executor = Get-Executor -Target local
    .EXAMPLE
        $executor = Get-Executor -Target docker -ContainerName "workflow-mcp-env"
    #>
    param(
        [ValidateSet("local", "docker")]
        [string]$Target = "local",

        [string]$ContainerName = ""
    )

    if ($Target -eq "docker") {
        if (-not $ContainerName) {
            Write-Error "Docker 执行器需要指定 ContainerName"
            return $null
        }

        $container = $ContainerName
        return {
            param($cmd)
            $escapedCmd = $cmd -replace "'", "'\''"
            docker exec $container sh -c "'$escapedCmd'" 2>&1
        }.GetNewClosure()
    }

    return {
        param($cmd)
        & powershell -NoProfile -Command $cmd 2>&1
    }
}

function Test-ExecutorAvailable {
    <#
    .SYNOPSIS
        检查执行器是否可用
    .PARAMETER Target
        目标环境
    .PARAMETER ContainerName
        Docker 容器名称
    .EXAMPLE
        if (Test-ExecutorAvailable -Target docker -ContainerName "workflow-mcp-env") { ... }
    #>
    param(
        [ValidateSet("local", "docker")]
        [string]$Target = "local",

        [string]$ContainerName = ""
    )

    if ($Target -eq "docker") {
        if (-not (Test-DockerEngineRunning)) {
            Write-Error "Docker 引擎未运行"
            return $false
        }
        if (-not (Test-ContainerRunning -ContainerName $ContainerName)) {
            Write-Error "容器 $ContainerName 未运行"
            return $false
        }
        return $true
    }

    return $true
}

function Invoke-ExecutorCommand {
    <#
    .SYNOPSIS
        通过执行器执行命令
    .PARAMETER Executor
        执行器脚本块
    .PARAMETER Command
        要执行的命令
    .PARAMETER Description
        命令描述（用于输出）
    .EXAMPLE
        Invoke-ExecutorCommand -Executor $executor -Command "npm --version" -Description "检查 npm 版本"
    #>
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Executor,

        [Parameter(Mandatory = $true)]
        [string]$Command,

        [string]$Description = ""
    )

    if ($Description) {
        Write-Info "$Description..."
    }

    try {
        $result = & $Executor $Command
        $exitCode = $LASTEXITCODE

        if ($exitCode -ne 0) {
            Write-Warning "命令执行失败 (exit code: $exitCode)"
            Write-Host "  命令: $Command" -ForegroundColor DarkGray
            if ($result) {
                Write-Host "  输出: $result" -ForegroundColor DarkGray
            }
            return @{
                Success = $false
                ExitCode = $exitCode
                Output = $result
            }
        }

        return @{
            Success = $true
            ExitCode = $exitCode
            Output = $result
        }
    } catch {
        Write-Error "命令执行异常: $($_.Exception.Message)"
        return @{
            Success = $false
            ExitCode = -1
            Output = $_.Exception.Message
        }
    }
}

if ($MyInvocation.MyCommand.Source -eq $null) {
    Export-ModuleMember -Function @('Get-Executor', 'Test-ExecutorAvailable', 'Invoke-ExecutorCommand')
}

