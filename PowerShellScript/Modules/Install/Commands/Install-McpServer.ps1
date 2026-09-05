<#
.SYNOPSIS
    MCP 服务器安装工具 — 通过 Install Framework 安装
.DESCRIPTION
    使用 Install-Tool 框架安装 MCP 服务器。
    支持安装到 Docker 容器或本地主机环境。

    安装源类型:
        npm             - 从 npm 注册表安装 (npm install -g)
        pip             - 从 PyPI 安装 (pip3 install)
        github-release  - 从 GitHub Releases 下载 asset 并解压
        git-clone       - git clone 仓库后执行构建
        local           - 使用本地项目目录

    目标环境:
        docker          - 在 workflow-mcp-env 容器内安装（默认）
        local           - 在宿主机本地安装

    通过 PScriptManager 调用：
        Invoke-Function -Name 'Install-McpServer' -Arguments @{Name='context7-mcp'}
.NOTES
    版本: 2.1.0
    作者: WorkFlowPrj
    依赖: PScriptManager, BasicMethod, Install Framework Core
#>
# @requires: Install/Core/Config, Install/Core/Executor, Install/Core/Pipeline, Modules/Docker/DockerCommon
# @version: 2.1.0
# @author: WorkFlowPrj
# @category: install
# @alias: mcp-install
# @exports: Install-McpServer

function Install-McpServer {
    <#
    .SYNOPSIS
        安装 MCP 服务器
    .PARAMETER Name
        MCP 服务器名称（支持通配符 * 表示全部）
    .PARAMETER ConfigFile
        MCP 服务器清单 JSON 配置文件路径（默认使用 Manifests/mcp-servers.json）
    .PARAMETER Target
        安装目标环境: docker | local（默认 docker）
    .PARAMETER Version
        指定安装版本（默认 latest）
    .PARAMETER Force
        强制重新安装（跳过已安装检查）
    .PARAMETER WhatIf
        预览将要执行的操作，不实际安装
    .PARAMETER List
        列出所有可安装的 MCP 服务器
    .PARAMETER ContainerName
        目标 Docker 容器名称（默认从 DockerConfig.json 读取）
    .EXAMPLE
        Install-McpServer -List
    .EXAMPLE
        Install-McpServer -Name "context7-mcp"
    .EXAMPLE
        Install-McpServer -Name "*" -Force
    .EXAMPLE
        Install-McpServer -Name "context7-mcp" -Target local
    #>
    param(
        [string]$Name = "",
        [string]$ConfigFile = "",
        [ValidateSet("docker", "local")]
        [string]$Target = "docker",
        [string]$Version = "",
        [switch]$Force,
        [switch]$WhatIf,
        [switch]$List,
        [string]$ContainerName = ""
    )

    # ============================================================
    # 初始化 — 确保 Install Core 模块已加载
    # ============================================================

    $installCoreLoaded = (Get-Command 'Load-InstallManifest' -ErrorAction SilentlyContinue) -ne $null
    if (-not $installCoreLoaded) {
        $installRoot = Join-Path $global:PScriptManagerProjectRoot "PowerShellScript\Install"
        $coreModules = @(
            'Core\Config.ps1',
            'Core\Executor.ps1',
            'Core\Pipeline.ps1'
        )
        foreach ($module in $coreModules) {
            $modulePath = Join-Path $installRoot $module
            if (Test-Path $modulePath) {
                . $modulePath
            }
        }
    }

    # 加载 DockerCommon 模块
    $dockerCommonLoaded = (Get-Command 'Get-ContainerName' -ErrorAction SilentlyContinue) -ne $null
    if (-not $dockerCommonLoaded) {
        $dockerCommonPath = Join-Path $global:PScriptManagerProjectRoot "PowerShellScript\Modules\Docker\DockerCommon.psm1"
        if (Test-Path $dockerCommonPath) {
            Import-Module $dockerCommonPath -Force -DisableNameChecking -ErrorAction SilentlyContinue
        }
    }

    # ============================================================
    # 辅助函数
    # ============================================================

    function Show-McpServerList {
        param(
            [object]$Manifest,
            [string]$Target,
            [string]$ContainerName
        )

        Write-Banner -Title "可用 MCP 服务器" -Color "Cyan"
        
        $executor = $null
        if ($Target -eq "docker") {
            $executor = Get-Executor -Target $Target -ContainerName $ContainerName
        } else {
            $executor = Get-Executor -Target $Target
        }

        Write-Host "  $('名称'.PadRight(30)) $('类型'.PadRight(15)) $('版本'.PadRight(15)) 状态" -ForegroundColor White
        Write-Host "  $('-' * 30) $('-' * 15) $('-' * 15) $('-' * 15)" -ForegroundColor DarkGray

        foreach ($item in $Manifest.items) {
            $provider = Load-Provider -Type $item.type
            $installed = $false
            $version = "未安装"
            
            if ($provider) {
                $installed = & $provider.'Test-Installed' -Item $item -Target $Target -ContainerName $ContainerName -Executor $executor
                if ($installed) {
                    $version = & $provider.'Get-Version' -Item $item -Target $Target -ContainerName $ContainerName -Executor $executor
                    if (-not $version) { $version = "已安装" }
                }
            }

            $status = if ($installed) { "✓ 已安装" } else { "✗ 未安装" }
            $statusColor = if ($installed) { "Green" } else { "Red" }
            
            Write-Host "  $($item.name.PadRight(30)) $($item.type.PadRight(15)) $($version.PadRight(15))" -ForegroundColor Gray -NoNewline
            Write-Host " $status" -ForegroundColor $statusColor
        }
        
        Write-Host ""
        Write-Hint "使用 -Name 参数安装指定服务器，或使用 -Name '*' 安装全部"
    }

    # ============================================================
    # 主逻辑
    # ============================================================

    if (-not $ContainerName) {
        $ContainerName = Get-ContainerName
    }

    Write-Banner -Title "MCP 服务器安装工具" -Subtitle "v2.1 - Install Framework" -Color "Cyan"

    if ($List) {
        if (-not $ConfigFile) {
            $ConfigFile = "mcp-servers.json"
        }
        $manifest = Load-InstallManifest -ManifestName $ConfigFile
        if (-not $manifest) {
            return
        }
        $targetEnv = if ($Target -ne "") { $Target } else { $manifest.target }
        $containerName = if ($ContainerName -ne "") { $ContainerName } else { $manifest.containerName }
        Show-McpServerList -Manifest $manifest -Target $targetEnv -ContainerName $containerName
        return
    }

    if (-not $ConfigFile) {
        $ConfigFile = "mcp-servers.json"
    }

    $manifest = Load-InstallManifest -ManifestName $ConfigFile
    if (-not $manifest) {
        return
    }

    $targetEnv = if ($Target -ne "") { $Target } else { $manifest.target }
    $containerName = if ($ContainerName -ne "") { $ContainerName } else { $manifest.containerName }

    $items = $manifest.items
    if ($Name) {
        if ($Name -eq "*") {
            $items = $manifest.items
        } else {
            $items = $items | Where-Object { $_.name -like $Name }
            if (-not $items) {
                Write-Error "未找到项目: $Name"
                Write-Hint "可用项目: $($manifest.items.name -join ', ')"
                return
            }
        }
    }

    if ($targetEnv -eq "docker") {
        if (-not (Test-ExecutorAvailable -Target $targetEnv -ContainerName $containerName)) {
            return
        }
    }

    Write-Info "目标环境: $targetEnv"
    if ($targetEnv -eq "docker") {
        Write-Info "容器名称: $containerName"
    }
    Write-Info "将安装 $($items.Count) 个 MCP 服务器"
    Write-Host ""

    $hasFailures = $false
    $typeGroups = $items | Group-Object type
    foreach ($group in $typeGroups) {
        $provider = Load-Provider -Type $group.Name
        if (-not $provider) {
            continue
        }

        Write-Banner -Title "Provider: $($provider.Name)" -Color "Magenta"

        $executor = $null
        if ($targetEnv -eq "docker") {
            $executor = Get-Executor -Target $targetEnv -ContainerName $containerName
        } else {
            $executor = Get-Executor -Target $targetEnv
        }

        foreach ($item in $group.Group) {
            if ($Version) {
                $item.version = $Version
            }
        }

        $result = Invoke-InstallPipeline -Items $group.Group -Provider $provider -Target $targetEnv -ContainerName $containerName -Force:$Force -WhatIf:$WhatIf -Executor $executor
        if ($result.Failed -gt 0) {
            $hasFailures = $true
        }
    }

    Write-Banner -Title "安装完成" -Color "Cyan"

    if ($hasFailures) {
        return $false
    }
    return $true
}

# 当直接执行脚本时（非 dot-source），调用函数
if ($MyInvocation.InvocationName -eq $null -or $MyInvocation.InvocationName -eq '') {
    $splat = @{}
    if ($args.Count -gt 0) {
        for ($i = 0; $i -lt $args.Count; $i += 2) {
            $key = $args[$i] -replace '^-', ''
            $value = $args[$i + 1]
            $splat[$key] = $value
        }
    }
    Install-McpServer @splat
}
