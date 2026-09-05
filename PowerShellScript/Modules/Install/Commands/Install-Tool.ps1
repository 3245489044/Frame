<#
.SYNOPSIS
    Install Framework — 通用安装工具入口点
.DESCRIPTION
    根据安装清单（manifest）自动安装指定的工具/包。
    支持 npm/pip/github-release/git-clone/zip-extract/rustup/msi/local 等安装类型。
    支持本地和 Docker 容器两种目标环境。

    通过 PScriptManager 调用：
        Invoke-Function -Name 'Install-Tool' -Arguments @{Manifest='mcp-servers.json'}
.NOTES
    版本: 1.1.0
    作者: WorkFlowPrj
    依赖: PScriptManager, BasicMethod, Install Framework Core
#>
# @requires: Install/Core/Config, Install/Core/Executor, Install/Core/Pipeline
# @version: 1.1.0
# @author: WorkFlowPrj
# @category: install
# @alias: install
# @exports: Install-Tool

function Install-Tool {
    <#
    .SYNOPSIS
        通用安装工具 — 根据清单安装工具/包
    .PARAMETER Manifest
        安装清单文件名（位于 Install/Manifests/ 目录）
    .PARAMETER Name
        安装单个项目名称
    .PARAMETER All
        安装清单中的所有项目（默认）
    .PARAMETER Force
        强制重新安装（跳过已安装检查）
    .PARAMETER WhatIf
        仅预览安装操作，不实际执行
    .PARAMETER List
        列出所有可用的安装清单
    .PARAMETER Target
        安装目标: local | docker（默认: local）
    .PARAMETER ContainerName
        Docker 容器名称（-Target docker 时必填）
    .EXAMPLE
        Install-Tool -List
    .EXAMPLE
        Install-Tool -Manifest mcp-servers.json
    .EXAMPLE
        Install-Tool -Manifest mcp-servers.json -Name context7-mcp
    .EXAMPLE
        Install-Tool -Manifest mcp-servers.json -Target docker -ContainerName workflow-env
    #>
    param(
        [string]$Manifest = "",
        [string]$Name = "",
        [switch]$All,
        [switch]$Force,
        [switch]$WhatIf,
        [switch]$List,
        [ValidateSet("local", "docker")]
        [string]$Target = "local",
        [string]$ContainerName = ""
    )

    # ============================================================
    # 初始化 — 依赖 PScriptManager 已加载 Install Core 模块
    # ============================================================

    # 确保 Install Core 模块已加载
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

    # ============================================================
    # 辅助函数
    # ============================================================

    function Write-Usage {
        Write-Banner -Title "Install-Tool — 通用安装工具" -Color "Cyan"
        Write-Host ""
        Write-Host "  用法:" -ForegroundColor Yellow
        Write-Host "    Install-Tool -List" -ForegroundColor White
        Write-Host "    Install-Tool -Manifest <文件名>" -ForegroundColor White
        Write-Host "    Install-Tool -Manifest <文件名> -Name <项目名>" -ForegroundColor White
        Write-Host "    Install-Tool -Manifest <文件名> -All" -ForegroundColor White
        Write-Host ""
        Write-Host "  参数:" -ForegroundColor Yellow
        Write-Host "    -Manifest    指定安装清单文件（位于 Install/Manifests/ 目录）" -ForegroundColor White
        Write-Host "    -Name        安装单个项目（需要与 -Manifest 配合使用）" -ForegroundColor White
        Write-Host "    -All         安装清单中的所有项目（默认）" -ForegroundColor White
        Write-Host "    -Force       强制重新安装（跳过已安装检查）" -ForegroundColor White
        Write-Host "    -WhatIf      仅预览安装操作，不实际执行" -ForegroundColor White
        Write-Host "    -List        列出所有可用的安装清单" -ForegroundColor White
        Write-Host "    -Target      安装目标: local | docker（默认: local）" -ForegroundColor White
        Write-Host "    -ContainerName Docker 容器名称（-Target docker 时必填）" -ForegroundColor White
        Write-Host ""
        Write-Host "  示例:" -ForegroundColor Yellow
        Write-Host "    Install-Tool -List" -ForegroundColor White
        Write-Host "    Install-Tool -Manifest mcp-servers.json" -ForegroundColor White
        Write-Host "    Install-Tool -Manifest mcp-servers.json -Name context7-mcp" -ForegroundColor White
        Write-Host "    Install-Tool -Manifest mcp-servers.json -Target docker -ContainerName workflow-env" -ForegroundColor White
        Write-Host "    Install-Tool -Manifest mcp-servers.json -Force" -ForegroundColor White
        Write-Host "    Install-Tool -Manifest mcp-servers.json -WhatIf" -ForegroundColor White
        Write-Host ""
    }

    # ============================================================
    # 主逻辑
    # ============================================================

    if (-not $List -and -not $Manifest) {
        Write-Usage
        return
    }

    if ($List) {
        Show-ManifestList
        return
    }

    if (-not $Manifest) {
        Write-Error "请使用 -Manifest 参数指定安装清单"
        Write-Hint "使用 -List 查看可用清单"
        return
    }

    $manifest = Load-InstallManifest -ManifestName $Manifest
    if (-not $manifest) {
        return
    }

    $target = if ($Target -and $Target -ne "") { $Target } else { $manifest.target }
    $containerName = if ($ContainerName -and $ContainerName -ne "") { $ContainerName } else { $manifest.containerName }

    $items = $manifest.items
    if ($Name) {
        $items = $items | Where-Object { $_.name -eq $Name }
        if (-not $items) {
            Write-Error "未找到项目: $Name"
            Write-Hint "可用项目: $($manifest.items.name -join ', ')"
            return
        }
    }

    $hasFailures = $false
    $typeGroups = $items | Group-Object type
    foreach ($group in $typeGroups) {
        $provider = Load-Provider -Type $group.Name
        if (-not $provider) {
            continue
        }

        Write-Banner -Title "Provider: $($provider.Name)" -Color "Magenta"

        $executor = $null
        if ($target -eq "docker") {
            if (-not (Test-ExecutorAvailable -Target $target -ContainerName $containerName)) {
                Write-Warn "Docker 执行器不可用，跳过此组"
                continue
            }
            $executor = Get-Executor -Target $target -ContainerName $containerName
        } else {
            $executor = Get-Executor -Target $target
        }

        $result = Invoke-InstallPipeline -Items $group.Group -Provider $provider -Target $target -ContainerName $containerName -Force:$Force -WhatIf:$WhatIf -Executor $executor
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
    # 通过 PScriptManager 调用时，参数已通过 $args 传入
    $splat = @{}
    if ($args.Count -gt 0) {
        for ($i = 0; $i -lt $args.Count; $i += 2) {
            $key = $args[$i] -replace '^-', ''
            $value = $args[$i + 1]
            $splat[$key] = $value
        }
    }
    Install-Tool @splat
}
