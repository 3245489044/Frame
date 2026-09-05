<#
.SYNOPSIS
    Docker 构建环境通用模块 — 镜像构建、容器管理、命令执行
.DESCRIPTION
    将 Build/ 目录中分散的 Docker 逻辑抽象为通用模块，提供：
    - 镜像构建与检查（支持自定义 Dockerfile、标签、--no-cache）
    - 容器创建与生命周期管理（支持自定义入口点、卷挂载）
    - 容器内命令执行（支持交互/非交互模式）
    - 卷挂载管理（NuGet 缓存、BuildOutput 等）
    - Docker 环境检查（可用性、Linux 容器模式）
    - 状态查询与诊断

    设计原则：
    - 所有配置值通过 DockerCommon 的 Get-ConfigValue 获取，不硬编码
    - 所有输出使用 DockerCommon 的 Write-* 函数（委托 BasicMethod）
    - 通用 Docker 操作（Build-DockerImage、New-DockerContainer、Remove-DockerContainer、
      Invoke-DockerCommand、Test-DockerVolumeMount）委托 BasicMethod/DevOps/Docker.psm1
    - 与 DockerManagement.psm1 互补：本模块专注 docker run 方式，
      DockerManagement 专注 docker compose 方式
    - 与 DockerCommon.psm1 不重复定义同名函数（Invoke-ContainerScript、Get-DockerVersion 由 DockerCommon 提供）

.NOTES
    模块名: DockerBuild
    依赖: DockerCommon（共享常量、辅助函数、路径解析）
           BasicMethod/DevOps/Docker.psm1（通用 Docker 操作）
    版本: 1.2.0
#>

# ============================================================
# 模块初始化
# ============================================================

# 依赖：DockerCommon 由 Docker.psd1 的 NestedModules 先加载

# ============================================================
# 镜像管理
# ============================================================

<#
.SYNOPSIS
    检查 Docker 镜像是否存在
.PARAMETER ImageName
    镜像名称（默认从配置读取）
.PARAMETER ImageTag
    镜像标签（默认 latest）
.EXAMPLE
    Test-DockerImageExists
    Test-DockerImageExists -ImageName "my-image" -ImageTag "v1.0"
#>
function Test-DockerImageExists {
    [CmdletBinding()]
    param(
    [string]$ImageName = (Get-ConfigValue "dotnetBuilder" "imageName"),
    [string]$ImageTag = "latest"
    )

    $fullName = "$ImageName`:$ImageTag"
    $result = & docker images -q $fullName 2>&1
    return [bool]$result
}

<#
.SYNOPSIS
    确保 Docker 镜像存在，不存在则自动构建
.PARAMETER ImageName
    镜像名称（默认从配置读取）
.PARAMETER ImageTag
    镜像标签（默认 latest）
.PARAMETER Dockerfile
    Dockerfile 路径（默认从配置读取）
.PARAMETER BuildContext
    构建上下文目录（默认项目根目录）
.PARAMETER NoCache
    不使用缓存，完全重新构建
.PARAMETER Force
    跳过确认提示
.EXAMPLE
    Ensure-DockerImage
    Ensure-DockerImage -NoCache -Force
#>
function Ensure-DockerImage {
    [CmdletBinding()]
    param(
        [string]$ImageName = (Get-ConfigValue "dotnetBuilder" "imageName"),
        [string]$ImageTag = "latest",
        [string]$Dockerfile = "",
        [string]$BuildContext = "",
        [switch]$NoCache,
        [switch]$Force
    )

    $fullName = "$ImageName`:$ImageTag"

    if (Test-DockerImageExists -ImageName $ImageName -ImageTag $ImageTag) {
        $size = & docker images $fullName --format '{{.Size}}' 2>&1
        Write-Info "镜像 $fullName 已存在（$size）"
        return $true
    }

    # 自动构建
    Write-Host "  ⏳ 镜像 $fullName 不存在，正在构建..." -ForegroundColor Yellow
    return Build-DockerImage -ImageName $ImageName -ImageTag $ImageTag -Dockerfile $Dockerfile -BuildContext $BuildContext -NoCache:$NoCache -Force:$Force
}

<#
.SYNOPSIS
    构建 Docker 镜像
.PARAMETER ImageName
    镜像名称（默认从配置读取）
.PARAMETER ImageTag
    镜像标签（默认 latest）
.PARAMETER Dockerfile
    Dockerfile 路径（默认从配置读取）
.PARAMETER BuildContext
    构建上下文目录（默认项目根目录）
.PARAMETER NoCache
    不使用缓存，完全重新构建
.PARAMETER Force
    跳过确认提示
.EXAMPLE
    Build-DockerImage
    Build-DockerImage -NoCache
    Build-DockerImage -ImageName "my-image" -Dockerfile "path/to/Dockerfile"
#>
function Build-DockerImage {
    [CmdletBinding()]
    param(
        [string]$ImageName = (Get-ConfigValue "dotnetBuilder" "imageName"),
        [string]$ImageTag = "latest",
        [string]$Dockerfile = "",
        [string]$BuildContext = "",
        [switch]$NoCache,
        [switch]$Force
    )

    $fullName = "$ImageName`:$ImageTag"

    # 解析 Dockerfile 路径
    if (-not $Dockerfile) {
        $Dockerfile = Get-ConfigValue "dotnetBuilder" "dockerfile"
    }
    $dockerfilePath = Resolve-ProjectPath $Dockerfile

    if (-not (Test-Path $dockerfilePath)) {
        Write-Error "Dockerfile 不存在: $dockerfilePath"
        return $false
    }

    # 解析构建上下文
    if (-not $BuildContext) {
        $BuildContext = Get-ProjectRoot
    }

    Write-Header "构建 Docker 镜像"
    Write-Info "Dockerfile: $dockerfilePath"
    Write-Info "构建上下文: $BuildContext"
    Write-Info "镜像标签: $fullName"

    if ($NoCache) {
        Write-Info "模式: 无缓存重建"
    }

    # 构建命令
    $buildArgs = @("build", "-f", $dockerfilePath, "-t", $fullName)
    if ($NoCache) {
        $buildArgs += "--no-cache"
    }
    $buildArgs += $BuildContext

    Write-Host "`n  开始构建..." -ForegroundColor Yellow
    try {
        $output = & docker @buildArgs 2>&1
        $output | ForEach-Object { Write-Host "    $_" -ForegroundColor Gray }

        if ($LASTEXITCODE -eq 0) {
            $size = & docker images $fullName --format '{{.Size}}' 2>&1
            Write-Success "镜像构建完成（$size）"
            return $true
        } else {
            Write-Error "镜像构建失败（退出码: $LASTEXITCODE）"
            return $false
        }
    } catch {
        Write-Error "构建异常: $_"
        return $false
    }
}

<#
.SYNOPSIS
    获取镜像信息
.PARAMETER ImageName
    镜像名称（默认从配置读取）
.PARAMETER ImageTag
    镜像标签（默认 latest）
.EXAMPLE
    Get-DockerImageInfo
#>
function Get-DockerImageInfo {
    [CmdletBinding()]
    param(
        [string]$ImageName = (Get-ConfigValue "dotnetBuilder" "imageName"),
        [string]$ImageTag = "latest"
    )

    $fullName = "$ImageName`:$ImageTag"

    if (-not (Test-DockerImageExists -ImageName $ImageName -ImageTag $ImageTag)) {
        return $null
    }

    try {
        $info = & docker images $fullName --format '{{.Repository}}:{{.Tag}}\t{{.Size}}\t{{.CreatedAt}}' 2>&1
        if ($info -match '(.+?):(.+?)\t(.+?)\t(.+)') {
            return @{
                Repository  = $matches[1]
                Tag         = $matches[2]
                Size        = $matches[3]
                CreatedAt   = $matches[4]
            }
        }
        return @{ FullName = $fullName }
    } catch {
        return $null
    }
}

# ============================================================
# 容器生命周期管理
# ============================================================

<#
.SYNOPSIS
    创建并启动 Docker 容器
.DESCRIPTION
    创建并启动一个后台运行的容器，支持自定义入口点、卷挂载。
.PARAMETER ContainerName
    容器名称（默认从配置读取）
.PARAMETER ImageName
    镜像名称（默认从配置读取）
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
.PARAMETER Force
    跳过确认提示
.PARAMETER WhatIf
    模拟模式，仅预览不执行
.EXAMPLE
    New-DockerContainer
    New-DockerContainer -Volumes @("D:\data:/data") -Environment @("DEBUG=1")
    New-DockerContainer -Entrypoint "dotnet" -EntrypointArgs @("--help")
#>
function New-DockerContainer {
    [CmdletBinding()]
    param(
        [string]$ContainerName = (Get-ConfigValue "dotnetBuilder" "containerName"),
        [string]$ImageName = (Get-ConfigValue "dotnetBuilder" "imageName"),
        [string]$ImageTag = "latest",
        [string]$Entrypoint = "",
        [string[]]$EntrypointArgs = @(),
        [string]$WorkingDir = "/workspace",
        [string[]]$Volumes = @(),
        [string[]]$Environment = @("DOTNET_CLI_TELEMETRY_OPTOUT=1"),
        [switch]$RemoveOld,
        [switch]$Force,
        [switch]$WhatIf
    )

    $fullName = "$ImageName`:$ImageTag"

    Write-Header "创建 Docker 容器"

    # 检查镜像是否存在
    if (-not (Test-DockerImageExists -ImageName $ImageName -ImageTag $ImageTag)) {
        Write-Error "镜像 $fullName 不存在，请先执行 Build-DockerImage"
        return $false
    }

    # 检查并处理旧容器
    $existing = & docker ps -a --filter "name=^/${ContainerName}$" --format '{{.Names}}' 2>&1
    if ($existing) {
        $status = & docker ps -a --filter "name=^/${ContainerName}$" --format '{{.Status}}' 2>&1
        Write-Warning "容器 '$ContainerName' 已存在（状态: $status）"

        if ($RemoveOld) {
            if ($WhatIf) {
                Write-Info "[模拟] 将停止并删除容器 '$ContainerName'"
            } else {
                Write-Info "正在停止并删除旧容器..."
                & docker stop $ContainerName 2>$null | Out-Null
                & docker rm $ContainerName 2>$null | Out-Null
                Write-Ok "旧容器已删除"
            }
        } else {
            Write-Info "使用现有容器"
            return $true
        }
    }

    if ($WhatIf) {
        Write-Info "[模拟] docker run -d ..."
        Write-Info "  名称: $ContainerName"
        Write-Info "  镜像: $fullName"
        Write-Info "  入口: $Entrypoint $($EntrypointArgs -join ' ')"
        Write-Info "  工作目录: $WorkingDir"
        foreach ($v in $Volumes) { Write-Info "  挂载: $v" }
        foreach ($e in $Environment) { Write-Info "  环境变量: $e" }
        return $true
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

    Write-Info "创建容器: $ContainerName"
    Write-Info "镜像: $fullName"

    try {
        $output = & docker @runArgs 2>&1

        if ($LASTEXITCODE -eq 0) {
            Write-Ok "容器 '$ContainerName' 已创建并启动"

            # 验证容器状态
            Start-Sleep -Seconds 1
            $status = & docker ps --filter "name=^/${ContainerName}$" --format '{{.Status}}' 2>&1
            Write-Ok "容器状态: $status"
            return $true
        } else {
            Write-Error "容器创建失败: $output"
            return $false
        }
    } catch {
        Write-Error "容器创建异常: $_"
        return $false
    }
}

<#
.SYNOPSIS
    停止并删除 Docker 容器
.PARAMETER ContainerName
    容器名称（默认从配置读取）
.PARAMETER Force
    跳过确认提示
.EXAMPLE
    Remove-DockerContainer
    Remove-DockerContainer -ContainerName "my-container" -Force
#>
function Remove-DockerContainer {
    [CmdletBinding()]
    param(
        [string]$ContainerName = (Get-ConfigValue "dotnetBuilder" "containerName"),
        [switch]$Force
    )

    $existing = & docker ps -a --filter "name=^/${ContainerName}$" --format '{{.Names}}' 2>&1
    if (-not $existing) {
        Write-Info "容器 '$ContainerName' 不存在"
        return $true
    }

    if (-not $Force) {
        $answer = Read-Host "确认删除容器 '$ContainerName'？（y/N）"
        if ($answer -ne 'y') {
            Write-Info "已取消"
            return $false
        }
    }

    Write-Info "正在停止并删除容器 '$ContainerName'..."
    & docker stop $ContainerName 2>$null | Out-Null
    & docker rm $ContainerName 2>$null | Out-Null

    if ($LASTEXITCODE -eq 0) {
        Write-Ok "容器 '$ContainerName' 已删除"
        return $true
    } else {
        Write-Error "删除容器失败"
        return $false
    }
}

<#
.SYNOPSIS
    进入容器交互 Shell
.PARAMETER ContainerName
    容器名称（默认从配置读取）
.PARAMETER Shell
    要启动的 Shell（默认 bash）
.EXAMPLE
    Enter-DockerContainer
    Enter-DockerContainer -ContainerName "my-container" -Shell "sh"
#>
function Enter-DockerContainer {
    [CmdletBinding()]
    param(
        [string]$ContainerName = (Get-ConfigValue "dotnetBuilder" "containerName"),
        [string]$Shell = "bash"
    )

    $state = Get-ContainerState -ContainerName $ContainerName
    if ($state -ne "running") {
        Write-Error "容器 '$ContainerName' 未在运行"
        return $false
    }

    Write-Info "进入容器: $ContainerName"
    Write-Info "输入 exit 退出容器`n"
    & docker exec -it $ContainerName $Shell 2>&1
    return $true
}

# ============================================================
# 容器内命令执行
# ============================================================

<#
.SYNOPSIS
    在 Docker 容器中执行命令
.DESCRIPTION
    在指定容器中执行命令，支持交互/非交互模式。
    命令通过 sh -c 传递，避免参数拆分问题。
.PARAMETER Command
    要执行的命令字符串
.PARAMETER ContainerName
    容器名称（默认从配置读取）
.PARAMETER Description
    命令描述（用于输出）
.PARAMETER Interactive
    是否使用交互模式（-it）
.PARAMETER WhatIf
    模拟模式，仅预览不执行
.EXAMPLE
    Invoke-DockerCommand -Command "dotnet build CsharpPrj/MainProj.sln -c Debug" -Description "编译解决方案"
    Invoke-DockerCommand -Command "bash" -Description "进入容器" -Interactive
#>
function Invoke-DockerCommand {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Command,

        [string]$ContainerName = (Get-ConfigValue "dotnetBuilder" "containerName"),
        [string]$Description = "",
        [switch]$Interactive,
        [switch]$WhatIf
    )

    if ($WhatIf) {
        Write-Info "[模拟] docker exec $ContainerName $Command"
        return $true
    }

    # 检查容器状态
    $state = Get-ContainerState -ContainerName $ContainerName
    if ($state -ne "running") {
        Write-Error "容器 '$ContainerName' 未在运行"
        return $false
    }

    if ($Description) {
        Write-Info "执行: $Description"
    }
    Write-Info "命令: $Command"

    try {
        # 使用 sh -c 传递完整命令，避免参数拆分问题
        if ($Interactive) {
            & docker exec -it $ContainerName sh -c "$Command" 2>&1 | ForEach-Object { Write-Host $_ }
        } else {
            $output = & docker exec $ContainerName sh -c "$Command" 2>&1
            $output | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
        }

        if ($LASTEXITCODE -eq 0) {
            if ($Description) { Write-Success "$Description 完成" }
            return $true
        } else {
            if ($Description) { Write-Error "$Description 失败（退出码: $LASTEXITCODE）" }
            return $false
        }
    } catch {
        Write-Error "命令执行异常: $_"
        return $false
    }
}

# Invoke-ContainerScript 由 DockerCommon.psm1 提供（功能更完善）
# 此处不再重复定义，避免同名函数冲突

# ============================================================
# 卷挂载管理
# ============================================================

<#
.SYNOPSIS
    生成默认的卷挂载参数列表
.DESCRIPTION
    生成包含项目根目录挂载的卷参数列表，可选包含 NuGet 缓存和 BuildOutput。
.PARAMETER IncludeNuGet
    包含 NuGet 包缓存挂载
.PARAMETER IncludeBuildOutput
    包含 BuildOutput 目录挂载
.PARAMETER ProjectRoot
    项目根目录（默认自动检测）
.EXAMPLE
    Get-DockerVolumeArgs
    Get-DockerVolumeArgs -IncludeNuGet -IncludeBuildOutput
#>
function Get-DockerVolumeArgs {
    [CmdletBinding()]
    param(
        [switch]$IncludeNuGet,
        [switch]$IncludeBuildOutput,
        [string]$ProjectRoot = (Get-ProjectRoot)
    )

    $volumes = @("${ProjectRoot}:/workspace")

    # 共享 NuGet 缓存
    if ($IncludeNuGet) {
        $nugetSource = [System.IO.Path]::Combine($env:USERPROFILE, ".nuget", "packages")
        if (Test-Path $nugetSource) {
            $volumes += "${nugetSource}:/root/.nuget/packages"
        } else {
            Write-Warning "NuGet 缓存目录不存在: $nugetSource，跳过共享"
        }
    }

    # 共享 BuildOutput 目录
    if ($IncludeBuildOutput) {
        $buildOutputDir = Join-Path $ProjectRoot "BuildOutput"
        if (-not (Test-Path $buildOutputDir)) {
            $null = New-Item -ItemType Directory -Path $buildOutputDir -Force
        }
        $volumes += "${buildOutputDir}:/workspace/BuildOutput"
    }

    return $volumes
}

<#
.SYNOPSIS
    验证容器卷挂载是否正常
.PARAMETER ContainerName
    容器名称（默认从配置读取）
.PARAMETER CheckPaths
    要检查的容器内路径列表
.EXAMPLE
    Test-DockerVolumeMount -ContainerName "my-container" -CheckPaths @("/workspace/PowerShellScript", "/workspace/CsharpPrj")
#>
function Test-DockerVolumeMount {
    [CmdletBinding()]
    param(
        [string]$ContainerName = (Get-ConfigValue "dotnetBuilder" "containerName"),
        [string[]]$CheckPaths = @("/workspace")
    )

    $state = Get-ContainerState -ContainerName $ContainerName
    if ($state -ne "running") {
        Write-Error "容器 '$ContainerName' 未在运行"
        return $false
    }

    $allOk = $true
    foreach ($path in $CheckPaths) {
        $result = & docker exec $ContainerName sh -c "test -d '$path' && echo 'exists'" 2>&1
        if ($result -match 'exists') {
            Write-Ok "卷挂载正常: $path"
        } else {
            Write-Warning "卷挂载可能异常: $path"
            $allOk = $false
        }
    }

    return $allOk
}

# ============================================================
# Docker 环境检查
# ============================================================

<#
.SYNOPSIS
    检查 Docker 是否可用
.EXAMPLE
    Test-DockerAvailable
#>
function Test-DockerAvailable {
    try {
        $version = & docker --version 2>&1
        return ($LASTEXITCODE -eq 0)
    } catch {
        return $false
    }
}

<#
.SYNOPSIS
    检查 Docker 是否为 Linux 容器模式
.EXAMPLE
    Test-DockerLinuxMode
#>
function Test-DockerLinuxMode {
    try {
        $osType = & docker info --format '{{.OSType}}' 2>&1
        return ($LASTEXITCODE -eq 0 -and $osType -eq 'linux')
    } catch {
        return $false
    }
}

# Get-DockerVersion 由 DockerCommon.psm1 提供（返回服务端版本）
# 此处不再重复定义，避免同名函数冲突

# ============================================================
# 状态查询
# ============================================================

<#
.SYNOPSIS
    获取 Docker 引擎 + 容器状态概览
.PARAMETER ContainerName
    容器名称（默认从配置读取）
.PARAMETER ImageName
    镜像名称（默认从配置读取）
.PARAMETER ImageTag
    镜像标签（默认 latest）
.EXAMPLE
    Get-DockerBuildStatus
#>
function Get-DockerBuildStatus {
    [CmdletBinding()]
    param(
        [string]$ContainerName = (Get-ConfigValue "dotnetBuilder" "containerName"),
        [string]$ImageName = (Get-ConfigValue "dotnetBuilder" "imageName"),
        [string]$ImageTag = "latest"
    )

    $fullName = "$ImageName`:$ImageTag"

    Write-Header "Docker 构建环境状态"

    # 1. Docker 引擎
    $dockerAvailable = Test-DockerAvailable
    Write-Host "  ── Docker 引擎 ──" -ForegroundColor White
    if ($dockerAvailable) {
        Write-Success "Docker: $(Get-DockerVersion)"
        $osType = & docker info --format '{{.OSType}}' 2>&1
        Write-Info "容器模式: $osType"
    } else {
        Write-Error "Docker 不可用"
        return @{ Engine = "unavailable"; Image = "unknown"; Container = "unknown" }
    }

    # 2. 镜像状态
    Write-Host "`n  ── 镜像状态 ──" -ForegroundColor White
    if (Test-DockerImageExists -ImageName $ImageName -ImageTag $ImageTag) {
        $info = Get-DockerImageInfo -ImageName $ImageName -ImageTag $ImageTag
        Write-Success "镜像: $fullName"
        if ($info) {
            Write-Info "大小: $($info.Size)"
            Write-Info "创建时间: $($info.CreatedAt)"
        }
        $imageStatus = "exists"
    } else {
        Write-Warning "镜像: $fullName 不存在"
        $imageStatus = "not_found"
    }

    # 3. 容器状态
    Write-Host "`n  ── 容器状态 ──" -ForegroundColor White
    $state = Get-ContainerState -ContainerName $ContainerName
    if ($state -ne "unknown") {
        Write-Success "容器: $ContainerName"
        Write-Info "状态: $state"

        if ($state -eq "running") {
            # 资源占用
            try {
                $stats = & docker stats $ContainerName --no-stream --format "table {{.Name}}\t{{.CPUPerc}}\t{{.MemUsage}}\t{{.MemPerc}}\t{{.NetIO}}\t{{.BlockIO}}" 2>&1
                Write-Host "`n  ── 资源占用 ──" -ForegroundColor White
                $stats | ForEach-Object { Write-Host "    $_" -ForegroundColor Gray }
            } catch { }
        }
        $containerStatus = $state
    } else {
        Write-Warning "容器: $ContainerName 不存在"
        $containerStatus = "not_found"
    }

    return @{
        Engine    = if ($dockerAvailable) { "running" } else { "unavailable" }
        Image     = $imageStatus
        Container = $containerStatus
    }
}

# ============================================================
# .NET 编译专用辅助函数
# ============================================================

<#
.SYNOPSIS
    在容器内执行 dotnet 命令（.NET 编译专用）
.DESCRIPTION
    在已运行的持久容器内执行 dotnet 命令，自动确保容器和镜像就绪。
    使用 docker exec 而非 docker run --rm，与 New-DockerContainer 创建的持久容器一致。
.PARAMETER Command
    dotnet 命令参数（如 "build CsharpPrj/MainProj.sln -c Debug"）
.PARAMETER Description
    命令描述（用于输出）
.PARAMETER ContainerName
    容器名称（默认从配置读取）
.PARAMETER IncludeNuGet
    共享 NuGet 缓存（仅容器不存在时创建用）
.PARAMETER IncludeBuildOutput
    共享 BuildOutput 目录（仅容器不存在时创建用）
.PARAMETER Interactive
    交互模式
.PARAMETER WhatIf
    模拟模式
.EXAMPLE
    Invoke-DotnetCommand -Command "build CsharpPrj/MainProj.sln -c Debug" -Description "编译解决方案"
    Invoke-DotnetCommand -Command "test CsharpPrj/MainProj.sln -c Debug" -IncludeNuGet
#>
function Invoke-DotnetCommand {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Command,

        [string]$Description = "",
        [string]$ContainerName = (Get-ConfigValue "dotnetBuilder" "containerName"),
        [string]$ImageName = (Get-ConfigValue "dotnetBuilder" "imageName"),
        [string]$ImageTag = "latest",
        [switch]$IncludeNuGet,
        [switch]$IncludeBuildOutput,
        [switch]$Interactive,
        [switch]$WhatIf
    )

    $fullName = "$ImageName`:$ImageTag"

    if ($WhatIf) {
        Write-Info "[模拟] docker exec $ContainerName dotnet $Command"
        return $true
    }

    # 确保镜像存在
    $imageOk = Ensure-DockerImage -ImageName $ImageName -ImageTag $ImageTag
    if (-not $imageOk) {
        Write-Error "镜像不可用，无法执行命令"
        return $false
    }

    # 检查容器是否运行，未运行则自动创建并启动
    $state = Get-ContainerState -ContainerName $ContainerName
    if ($state -ne "running") {
        Write-Info "容器 '$ContainerName' 未运行，正在创建并启动..."
        $volumes = Get-DockerVolumeArgs -IncludeNuGet:$IncludeNuGet -IncludeBuildOutput:$IncludeBuildOutput
        $containerOk = New-DockerContainer -ContainerName $ContainerName -ImageName $ImageName -ImageTag $ImageTag -Volumes $volumes -RemoveOld
        if (-not $containerOk) {
            Write-Error "无法创建容器 '$ContainerName'"
            return $false
        }
    }

    # 使用 docker exec 在持久容器中执行命令
    if ($Description) {
        Write-Info "执行: $Description"
    }
    Write-Info "命令: dotnet $Command"

    try {
        if ($Interactive) {
            & docker exec -it $ContainerName sh -c "dotnet $Command" 2>&1 | ForEach-Object { Write-Host $_ }
        } else {
            $output = & docker exec $ContainerName sh -c "dotnet $Command" 2>&1
            $output | ForEach-Object { Write-Host $_ }
        }

        if ($LASTEXITCODE -eq 0) {
            if ($Description) { Write-Success "$Description 完成" }
            return $true
        } else {
            if ($Description) { Write-Error "$Description 失败（退出码: $LASTEXITCODE）" }
            return $false
        }
    } catch {
        Write-Error "命令执行异常: $_"
        return $false
    }
}

# ============================================================
# 模块导出
# ============================================================

Export-ModuleMember -Function @(
    # 镜像管理
    "Test-DockerImageExists"
    "Ensure-DockerImage"
    "Build-DockerImage"
    "Get-DockerImageInfo"

    # 容器生命周期
    "New-DockerContainer"
    "Remove-DockerContainer"
    "Enter-DockerContainer"

    # 命令执行
    "Invoke-DockerCommand"
    # Invoke-ContainerScript 由 DockerCommon.psm1 提供

    # 卷挂载管理
    "Get-DockerVolumeArgs"
    "Test-DockerVolumeMount"

    # 环境检查
    "Test-DockerAvailable"
    "Test-DockerLinuxMode"
    # Get-DockerVersion 由 DockerCommon.psm1 提供

    # 状态查询
    "Get-DockerBuildStatus"

    # .NET 编译专用
    "Invoke-DotnetCommand"
)
