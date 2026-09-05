<#
.SYNOPSIS
    Docker 通用操作函数模块 — 提供 Docker 容器/镜像/引擎的通用查询函数

.DESCRIPTION
    从 DockerCommon.psm1 和 Install-McpServer.ps1 中提取的通用 Docker 操作函数。
    不依赖任何项目特定常量（容器名、路径等由调用方传入）。

    包含函数:
        Test-ContainerRunning     — 检查容器是否运行
        Test-ContainerExists      — 检查容器是否存在
        Get-ContainerState        — 获取容器状态
        Get-ContainerHealth       — 获取容器健康状态
        Get-ContainerUptime       — 获取容器运行时间
        Test-ImageExists          — 检查镜像是否存在
        Test-DockerEngineRunning  — 检查 Docker 引擎是否运行
        Get-DockerVersion         — 获取 Docker 版本
        Test-PortHttpReachable    — 测试 HTTP 端口可达性
        Build-DockerImage         — 构建 Docker 镜像
        New-DockerContainer       — 创建并启动 Docker 容器
        Remove-DockerContainer    — 停止并删除 Docker 容器
        Invoke-DockerCommand      — 在容器内执行命令
        Test-DockerVolumeMount    — 验证容器卷挂载

    用法:
        Import-Module .\BasicMethod-Docker.psm1
        if (Test-ContainerRunning "my-container") { ... }
        $state = Get-ContainerState "my-container"
        $version = Get-DockerVersion
#>

# ============================================
# Docker 容器查询函数
# ============================================

function Test-ContainerRunning {
    <#
    .SYNOPSIS
        检查指定容器是否正在运行
    .PARAMETER ContainerName
        容器名称
    .OUTPUTS
        [bool] 运行中返回 $true，否则 $false
    .EXAMPLE
        if (Test-ContainerRunning "my-container") { "容器运行中" }
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ContainerName
    )
    try {
        $state = docker inspect --format "{{.State.Status}}" $ContainerName 2>$null
        return ($state -eq "running")
    } catch {
        return $false
    }
}

function Test-ContainerExists {
    <#
    .SYNOPSIS
        检查指定容器是否存在
    .PARAMETER ContainerName
        容器名称
    .OUTPUTS
        [bool] 存在返回 $true，否则 $false
    .EXAMPLE
        if (Test-ContainerExists "my-container") { "容器存在" }
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ContainerName
    )
    try {
        $state = docker inspect --format "{{.State.Status}}" $ContainerName 2>$null
        return ($LASTEXITCODE -eq 0)
    } catch {
        return $false
    }
}

function Get-ContainerState {
    <#
    .SYNOPSIS
        获取容器的运行状态
    .PARAMETER ContainerName
        容器名称
    .OUTPUTS
        [string] 状态文本（running / exited / paused / unknown）
    .EXAMPLE
        $state = Get-ContainerState "my-container"
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ContainerName
    )
    try {
        return docker inspect --format "{{.State.Status}}" $ContainerName 2>&1
    } catch {
        return "unknown"
    }
}

function Get-ContainerHealth {
    <#
    .SYNOPSIS
        获取容器的健康状态
    .PARAMETER ContainerName
        容器名称
    .OUTPUTS
        [string] 健康状态（healthy / unhealthy / starting / none / unknown）
    .EXAMPLE
        $health = Get-ContainerHealth "my-container"
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ContainerName
    )
    try {
        return docker inspect --format "{{.State.Health.Status}}" $ContainerName 2>&1
    } catch {
        return "unknown"
    }
}

function Get-ContainerUptime {
    <#
    .SYNOPSIS
        获取容器的运行时间（人类可读格式）
    .PARAMETER ContainerName
        容器名称
    .OUTPUTS
        [string] 运行时间字符串（如 "3天 2小时 15分钟"），失败返回 $null
    .EXAMPLE
        $uptime = Get-ContainerUptime "my-container"
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ContainerName
    )
    try {
        $uptime = docker inspect --format "{{.State.StartedAt}}" $ContainerName 2>&1
        if ($LASTEXITCODE -eq 0 -and $uptime) {
            $startTime = [DateTime]::Parse($uptime)
            $duration = (Get-Date) - $startTime
            return "$($duration.Days)天 $($duration.Hours)小时 $($duration.Minutes)分钟"
        }
    } catch {}
    return $null
}

# ============================================
# Docker 镜像查询函数
# ============================================

function Test-ImageExists {
    <#
    .SYNOPSIS
        检查指定镜像是否存在
    .PARAMETER ImageName
        镜像名称（如 "node:18"）
    .OUTPUTS
        [bool] 存在返回 $true，否则 $false
    .EXAMPLE
        if (Test-ImageExists "node:18") { "镜像存在" }
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ImageName
    )
    $result = docker images --format "{{.Repository}}:{{.Tag}}" $ImageName 2>$null
    return [string]::IsNullOrWhiteSpace($result) -eq $false
}

# ============================================
# Docker 引擎查询函数
# ============================================

function Test-DockerEngineRunning {
    <#
    .SYNOPSIS
        检查 Docker 引擎是否正在运行
    .OUTPUTS
        [bool] 运行中返回 $true，否则 $false
    .EXAMPLE
        if (Test-DockerEngineRunning) { "Docker 引擎运行中" }
    #>
    $version = Get-DockerVersion
    return $null -ne $version
}

function Get-DockerVersion {
    <#
    .SYNOPSIS
        获取 Docker 引擎版本号
    .OUTPUTS
        [string] 版本号（如 "24.0.6"），失败返回 $null
    .EXAMPLE
        $ver = Get-DockerVersion
    #>
    try {
        $version = docker info --format "{{.ServerVersion}}" 2>&1
        if ($version -match '\d+\.\d+\.\d+') {
            return $version.Trim()
        }
    } catch {}
    return $null
}

# ============================================
# 网络测试函数
# ============================================

function Test-PortHttpReachable {
    <#
    .SYNOPSIS
        测试指定端口的 HTTP 服务是否可达
    .DESCRIPTION
        向 http://localhost:{Port}/mcp 发送 POST 请求，检查是否返回 accepted 状态。
    .PARAMETER Port
        端口号
    .PARAMETER TimeoutSeconds
        超时秒数（默认 3）
    .OUTPUTS
        [bool] 可达返回 $true，否则 $false
    .EXAMPLE
        if (Test-PortHttpReachable -Port 3100) { "端口 3100 可达" }
    #>
    param(
        [Parameter(Mandatory = $true)]
        [int]$Port,

        [int]$TimeoutSeconds = 3
    )

    try {
        $body = '{"jsonrpc":"2.0","id":1,"method":"initialize"}'
        $webClient = New-Object System.Net.WebClient
        $webClient.Headers.Add("Content-Type", "application/json")
        $responseBytes = $webClient.UploadData("http://localhost:$Port/mcp", "POST", [System.Text.Encoding]::UTF8.GetBytes($body))
        $responseText = [System.Text.Encoding]::UTF8.GetString($responseBytes)
        $webClient.Dispose()
        if ($responseText -match '"status":"accepted"') {
            return $true
        }
        return $false
    } catch {
        return $false
    }
}

# ============================================
# Docker 环境检查函数
# ============================================

function Test-DockerAvailable {
    <#
    .SYNOPSIS
        检查 Docker 是否可用（命令可执行）
    .OUTPUTS
        [bool] 可用返回 $true，否则 $false
    .EXAMPLE
        if (Test-DockerAvailable) { "Docker 可用" }
    #>
    try {
        $null = docker --version 2>&1
        return $LASTEXITCODE -eq 0
    } catch {
        return $false
    }
}

function Test-DockerLinuxMode {
    <#
    .SYNOPSIS
        检查 Docker 是否运行在 Linux 容器模式
    .OUTPUTS
        [bool] Linux 模式返回 $true，否则 $false
    .EXAMPLE
        if (Test-DockerLinuxMode) { "Linux 容器模式" }
    #>
    try {
        $osType = docker info --format "{{.OSType}}" 2>&1
        return ($osType -eq "linux")
    } catch {
        return $false
    }
}

function Get-DockerImageInfo {
    <#
    .SYNOPSIS
        获取 Docker 镜像的详细信息
    .PARAMETER ImageName
        镜像名称（如 "node:18"）
    .OUTPUTS
        [hashtable] 包含镜像信息的哈希表，失败返回 $null
    .EXAMPLE
        $info = Get-DockerImageInfo -ImageName "node:18"
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ImageName
    )

    try {
        $inspect = docker image inspect $ImageName 2>&1 | ConvertFrom-Json
        if ($LASTEXITCODE -eq 0 -and $inspect) {
            return @{
                Id = $inspect.Id
                Created = $inspect.Created
                Size = $inspect.Size
                Os = $inspect.Os
                Architecture = $inspect.Architecture
            }
        }
    } catch {}
    return $null
}

function Get-DockerVolumeArgs {
    <#
    .SYNOPSIS
        生成 Docker 卷挂载参数
    .DESCRIPTION
        将宿主机路径→容器路径的映射数组转换为 docker run -v 参数列表。
    .PARAMETER VolumeMappings
        卷映射数组，每项为 @{HostPath="..."; ContainerPath="..."}
    .OUTPUTS
        [string[]] docker run -v 参数数组
    .EXAMPLE
        $volumes = @(
            @{HostPath="D:\data"; ContainerPath="/data"},
            @{HostPath="D:\config"; ContainerPath="/config"}
        )
        $args = Get-DockerVolumeArgs -VolumeMappings $volumes
    #>
    param(
        [Parameter(Mandatory = $true)]
        [array]$VolumeMappings
    )

    $volumeArgs = @()
    foreach ($mapping in $VolumeMappings) {
        $hostPath = $mapping.HostPath
        $containerPath = $mapping.ContainerPath
        $volumeArgs += "-v"
        $volumeArgs += "${hostPath}:${containerPath}"
    }
    return $volumeArgs
}

# ============================================
# 容器内端口检测函数
# ============================================

function Test-PortInContainer {
    <#
    .SYNOPSIS
        检查容器内指定端口是否在监听
    .PARAMETER ContainerName
        容器名称
    .PARAMETER Port
        端口号
    .OUTPUTS
        [bool] 端口在监听返回 $true
    .EXAMPLE
        if (Test-PortInContainer -ContainerName "my-container" -Port 3100) { "端口就绪" }
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ContainerName,

        [Parameter(Mandatory = $true)]
        [int]$Port
    )
    $result = docker exec $ContainerName sh -c "ss -tlnp 2>/dev/null | grep -q ':$Port ' && echo 'OK'" 2>&1
    return ($result -match 'OK')
}

function Get-ContainerLogs {
    <#
    .SYNOPSIS
        获取容器日志
    .PARAMETER ContainerName
        容器名称
    .PARAMETER Tail
        返回最后 N 行（默认 100）
    .OUTPUTS
        [string] 日志内容
    .EXAMPLE
        $logs = Get-ContainerLogs -ContainerName "my-container" -Tail 50
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ContainerName,

        [int]$Tail = 100
    )
    return docker logs $ContainerName --tail $Tail 2>&1
}

function Get-ContainerInfo {
    <#
    .SYNOPSIS
        获取容器详细信息（状态、运行时间、健康状态）
    .PARAMETER ContainerName
        容器名称
    .OUTPUTS
        [hashtable] @{ Running; State; Uptime; Health; Error }
    .EXAMPLE
        $info = Get-ContainerInfo -ContainerName "my-container"
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ContainerName
    )

    $info = @{
        Running = $false
        State   = "unknown"
        Uptime  = $null
        Health  = "none"
        Error   = ""
    }

    try {
        $state = docker inspect --format "{{.State.Status}}" $ContainerName 2>&1
        if ($LASTEXITCODE -eq 0) {
            $info.State = $state.Trim()
            $info.Running = ($state.Trim() -eq "running")

            $startedAt = docker inspect --format "{{.State.StartedAt}}" $ContainerName 2>&1
            if ($LASTEXITCODE -eq 0 -and $startedAt) {
                try {
                    $startTime = [DateTime]::Parse($startedAt.Trim())
                    $duration = (Get-Date) - $startTime
                    $info.Uptime = "$($duration.Days)天 $($duration.Hours)小时 $($duration.Minutes)分钟"
                } catch { }
            }

            $health = docker inspect --format "{{.State.Health.Status}}" $ContainerName 2>&1
            if ($LASTEXITCODE -eq 0 -and $health -and $health -ne "<nil>") {
                $info.Health = $health.Trim()
            }
        } else {
            $info.Error = "容器不存在或无法访问"
        }
    } catch {
        $info.Error = $_.Exception.Message
    }

    return $info
}

function Wait-ContainerReady {
    <#
    .SYNOPSIS
        等待容器进入运行状态
    .PARAMETER ContainerName
        容器名称
    .PARAMETER MaxWait
        最大等待秒数（默认 60）
    .PARAMETER Interval
        轮询间隔秒数（默认 3）
    .OUTPUTS
        [hashtable] @{ Ready; Waited; State }
    .EXAMPLE
        $result = Wait-ContainerReady -ContainerName "my-container" -MaxWait 30
        if ($result.Ready) { "容器已就绪" }
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ContainerName,

        [int]$MaxWait = 60,

        [int]$Interval = 3
    )

    $waited = 0
    $containerReady = $false

    while ($waited -lt $MaxWait) {
        $state = Get-ContainerState -ContainerName $ContainerName
        if ($state -eq "running") {
            $containerReady = $true
            break
        }
        Start-Sleep -Seconds $Interval
        $waited += $Interval
    }

    return @{
        Ready = $containerReady
        Waited = $waited
        State = if ($containerReady) { "running" } else { (Get-ContainerState -ContainerName $ContainerName) }
    }
}

# ============================================
# 容器文件操作函数
# ============================================

function Copy-FileToContainer {
    <#
    .SYNOPSIS
        复制文件到容器内
    .PARAMETER ContainerName
        目标容器名称
    .PARAMETER SourcePath
        宿主机源文件路径
    .PARAMETER DestPath
        容器内目标路径
    .OUTPUTS
        [bool] 成功返回 $true，失败返回 $false
    .EXAMPLE
        Copy-FileToContainer -ContainerName "my-container" -SourcePath ".\config.json" -DestPath "/app/config.json"
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ContainerName,

        [Parameter(Mandatory = $true)]
        [string]$SourcePath,

        [Parameter(Mandatory = $true)]
        [string]$DestPath
    )

    try {
        docker cp $SourcePath "${ContainerName}:${DestPath}" 2>&1 | Out-Null
        return $LASTEXITCODE -eq 0
    } catch {
        return $false
    }
}

function Invoke-ContainerScript {
    <#
    .SYNOPSIS
        在容器内执行脚本命令
    .PARAMETER ContainerName
        容器名称
    .PARAMETER ScriptBlock
        要执行的命令或脚本
    .PARAMETER WorkingDirectory
        容器内工作目录（可选）
    .OUTPUTS
        [string] 命令输出
    .EXAMPLE
        $output = Invoke-ContainerScript -ContainerName "my-container" -ScriptBlock "ls -la /app"
    .EXAMPLE
        $output = Invoke-ContainerScript -ContainerName "my-container" -ScriptBlock "npm install" -WorkingDirectory "/app"
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ContainerName,

        [Parameter(Mandatory = $true)]
        [string]$ScriptBlock,

        [string]$WorkingDirectory = ""
    )

    try {
        $cmd = if ($WorkingDirectory) {
            "docker exec -w `"$WorkingDirectory`" $ContainerName $ScriptBlock"
        } else {
            "docker exec $ContainerName $ScriptBlock"
        }
        $result = Invoke-Expression $cmd 2>&1
        return $result
    } catch {
        return $null
    }
}

function Remove-ContainerFile {
    <#
    .SYNOPSIS
        删除容器内的文件
    .PARAMETER ContainerName
        容器名称
    .PARAMETER FilePath
        容器内文件路径
    .OUTPUTS
        [bool] 成功返回 $true，失败返回 $false
    .EXAMPLE
        Remove-ContainerFile -ContainerName "my-container" -FilePath "/app/temp.txt"
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ContainerName,

        [Parameter(Mandatory = $true)]
        [string]$FilePath
    )

    try {
        docker exec $ContainerName rm -f $FilePath 2>&1 | Out-Null
        return $LASTEXITCODE -eq 0
    } catch {
        return $false
    }
}

# ============================================
# Docker 镜像构建函数
# ============================================

function Build-DockerImage {
    <#
    .SYNOPSIS
        构建 Docker 镜像
    .PARAMETER ImageName
        镜像名称
    .PARAMETER ImageTag
        镜像标签（默认 latest）
    .PARAMETER Dockerfile
        Dockerfile 路径
    .PARAMETER BuildContext
        构建上下文目录
    .PARAMETER NoCache
        不使用缓存，完全重新构建
    .OUTPUTS
        [bool] 成功返回 $true，失败返回 $false
    .EXAMPLE
        Build-DockerImage -ImageName "my-image" -Dockerfile "path/to/Dockerfile"
        Build-DockerImage -ImageName "my-image" -NoCache
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ImageName,

        [string]$ImageTag = "latest",

        [Parameter(Mandatory = $true)]
        [string]$Dockerfile,

        [string]$BuildContext = ".",

        [switch]$NoCache
    )

    $fullName = "$ImageName`:$ImageTag"

    if (-not (Test-Path $Dockerfile)) {
        Write-Error "Dockerfile 不存在: $Dockerfile"
        return $false
    }

    # 构建命令
    $buildArgs = @("build", "-f", $Dockerfile, "-t", $fullName)
    if ($NoCache) {
        $buildArgs += "--no-cache"
    }
    $buildArgs += $BuildContext

    try {
        $output = & docker @buildArgs 2>&1
        if ($LASTEXITCODE -eq 0) {
            return $true
        }
        return $false
    } catch {
        return $false
    }
}

# ============================================
# Docker 容器生命周期管理函数
# ============================================

function New-DockerContainer {
    <#
    .SYNOPSIS
        创建并启动 Docker 容器
    .DESCRIPTION
        创建并启动一个后台运行的容器，支持自定义入口点、卷挂载、环境变量。
    .PARAMETER ContainerName
        容器名称
    .PARAMETER ImageName
        镜像名称
    .PARAMETER ImageTag
        镜像标签（默认 latest）
    .PARAMETER Entrypoint
        容器入口点（默认 sleep infinity 保持运行）
    .PARAMETER EntrypointArgs
        入口点参数
    .PARAMETER WorkingDir
        容器内工作目录（默认 /workspace）
    .PARAMETER Volumes
        卷挂载列表，格式：@("hostPath:containerPath", ...)
    .PARAMETER Environment
        环境变量列表，格式：@("KEY=VALUE", ...)
    .PARAMETER RemoveOld
        如果容器已存在，先删除再重建
    .OUTPUTS
        [bool] 成功返回 $true，失败返回 $false
    .EXAMPLE
        New-DockerContainer -ContainerName "my-container" -ImageName "ubuntu:22.04"
        New-DockerContainer -ContainerName "my-container" -ImageName "ubuntu:22.04" -Volumes @("D:\data:/data")
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ContainerName,

        [Parameter(Mandatory = $true)]
        [string]$ImageName,

        [string]$ImageTag = "latest",

        [string]$Entrypoint = "sleep",
        [string[]]$EntrypointArgs = @("infinity"),

        [string]$WorkingDir = "/workspace",

        [string[]]$Volumes = @(),
        [string[]]$Environment = @(),

        [switch]$RemoveOld
    )

    $fullName = "$ImageName`:$ImageTag"

    # 检查并处理旧容器
    $existing = & docker ps -a --filter "name=^/${ContainerName}$" --format '{{.Names}}' 2>&1
    if ($existing) {
        if ($RemoveOld) {
            & docker stop $ContainerName 2>$null | Out-Null
            & docker rm $ContainerName 2>$null | Out-Null
        } else {
            return $true
        }
    }

    # 构建 docker run 参数
    $runArgs = @("run", "-d", "--name", $ContainerName)

    # 卷挂载
    foreach ($v in $Volumes) {
        $runArgs += "-v"
        $runArgs += $v
    }

    # 环境变量
    foreach ($e in $Environment) {
        $runArgs += "-e"
        $runArgs += $e
    }

    # 工作目录
    if ($WorkingDir) {
        $runArgs += "-w"
        $runArgs += $WorkingDir
    }

    # 入口点
    if ($Entrypoint) {
        $runArgs += "--entrypoint"
        $runArgs += $Entrypoint
    }

    $runArgs += $fullName
    $runArgs += $EntrypointArgs

    try {
        $output = & docker @runArgs 2>&1
        return ($LASTEXITCODE -eq 0)
    } catch {
        return $false
    }
}

function Remove-DockerContainer {
    <#
    .SYNOPSIS
        停止并删除 Docker 容器
    .PARAMETER ContainerName
        容器名称
    .OUTPUTS
        [bool] 成功返回 $true，失败返回 $false
    .EXAMPLE
        Remove-DockerContainer -ContainerName "my-container"
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ContainerName
    )

    $existing = & docker ps -a --filter "name=^/${ContainerName}$" --format '{{.Names}}' 2>&1
    if (-not $existing) {
        return $true
    }

    & docker stop $ContainerName 2>$null | Out-Null
    & docker rm $ContainerName 2>$null | Out-Null
    return ($LASTEXITCODE -eq 0)
}

# ============================================
# 容器内命令执行函数
# ============================================

function Invoke-DockerCommand {
    <#
    .SYNOPSIS
        在 Docker 容器中执行命令
    .DESCRIPTION
        在指定容器中执行命令，命令通过 sh -c 传递，避免参数拆分问题。
        支持带描述的输出、工作目录指定、返回成功/失败状态。
    .PARAMETER Command
        要执行的命令字符串
    .PARAMETER ContainerName
        容器名称
    .PARAMETER Description
        命令描述（可选，用于输出提示）
    .PARAMETER WorkingDir
        容器内工作目录（可选）
    .PARAMETER Interactive
        是否使用交互模式（-it）
    .PARAMETER PassThru
        返回原始输出而非成功/失败状态
    .OUTPUTS
        默认返回 [bool] 成功返回 $true，失败返回 $false
        使用 -PassThru 返回 [string] 命令输出
    .EXAMPLE
        $output = Invoke-DockerCommand -ContainerName "my-container" -Command "ls -la /app"
        if (Invoke-DockerCommand -ContainerName "my-container" -Command "dotnet build" -Description "编译项目") { "成功" }
        Invoke-DockerCommand -ContainerName "my-container" -Command "npm install" -WorkingDir "/app"
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Command,

        [Parameter(Mandatory = $true)]
        [string]$ContainerName,

        [string]$Description = "",

        [string]$WorkingDir = "",

        [switch]$Interactive,

        [switch]$PassThru
    )

    try {
        # 构建 docker exec 参数
        $execArgs = @("exec")
        if ($Interactive) {
            $execArgs += "-it"
        }
        if ($WorkingDir) {
            $execArgs += "-w"; $execArgs += $WorkingDir
        }
        $execArgs += $ContainerName
        $execArgs += @("sh", "-c", $Command)

        if ($Description) {
            Write-Host "  ⏳ $Description..." -ForegroundColor Yellow
        }

        $output = & docker @execArgs 2>&1
        $exitCode = $LASTEXITCODE

        if ($PassThru) {
            return $output
        }

        if ($exitCode -eq 0) {
            if ($Description) { Write-Host "  ✅ $Description 完成" -ForegroundColor Green }
            return $true
        } else {
            if ($Description) { Write-Host "  ❌ $Description 失败（退出码: $exitCode）" -ForegroundColor Red }
            return $false
        }
    } catch {
        if ($Description) { Write-Host "  ❌ $Description 异常: $_" -ForegroundColor Red }
        if ($PassThru) { return $null }
        return $false
    }
}


# ============================================
# 卷挂载验证函数
# ============================================

function Test-DockerVolumeMount {
    <#
    .SYNOPSIS
        验证容器卷挂载是否正常
    .PARAMETER ContainerName
        容器名称
    .PARAMETER CheckPaths
        要检查的容器内路径列表
    .OUTPUTS
        [bool] 所有路径挂载正常返回 $true，否则 $false
    .EXAMPLE
        Test-DockerVolumeMount -ContainerName "my-container" -CheckPaths @("/workspace", "/data")
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ContainerName,

        [Parameter(Mandatory = $true)]
        [string[]]$CheckPaths
    )

    $allOk = $true
    foreach ($path in $CheckPaths) {
        $result = & docker exec $ContainerName sh -c "test -d '$path' && echo 'exists'" 2>&1
        if ($result -match 'exists') {
            Write-Output "卷挂载正常: $path"
        } else {
            Write-Warning "卷挂载可能异常: $path"
            $allOk = $false
        }
    }

    return $allOk
}

# ============================================
# 模块导出
# ============================================

Export-ModuleMember -Function @(
    'Test-ContainerRunning',
    'Test-ContainerExists',
    'Get-ContainerState',
    'Get-ContainerHealth',
    'Get-ContainerUptime',
    'Test-ImageExists',
    'Test-DockerEngineRunning',
    'Get-DockerVersion',
    'Test-PortHttpReachable',
    'Test-DockerAvailable',
    'Test-DockerLinuxMode',
    'Get-DockerImageInfo',
    'Get-DockerVolumeArgs',
    'Test-PortInContainer',
    'Get-ContainerLogs',
    'Get-ContainerInfo',
    'Wait-ContainerReady',
    'Copy-FileToContainer',
    'Invoke-ContainerScript',
    'Remove-ContainerFile',
    'Build-DockerImage',
    'New-DockerContainer',
    'Remove-DockerContainer',
    'Invoke-DockerCommand',
    'Test-DockerVolumeMount'
)
