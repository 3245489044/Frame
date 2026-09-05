<#
.SYNOPSIS
    MCP 服务器安装管理模块
.DESCRIPTION
    提供 MCP 服务器的安装、更新、卸载管理功能。
    支持 npm 和 pip 两种安装方式。
.NOTES
    模块名: McpInstallation
    依赖: DockerCommon（共享常量、辅助函数、MCP 服务清单）
#>

# ============================================================
# 核心函数
# ============================================================

<#
.SYNOPSIS
    获取 MCP 服务器清单
.DESCRIPTION
    返回 MCP 服务器注册清单，可选从容器内 mcp-servers.json 加载。
.PARAMETER FromContainer
    从容器内加载最新配置
.EXAMPLE
    Get-McpServerList
#>
function Get-McpServerList {
    [CmdletBinding()]
    param(
        [switch]$FromContainer
    )

    if ($FromContainer) {
        try {
            $state = Get-ContainerState
            if ($state -eq "running") {
                $json = docker exec $(Get-ContainerName) sh -c "cat /opt/mcp-gateway/config/mcp-servers.json" 2>&1
                if ($LASTEXITCODE -eq 0) {
                    $servers = $json | ConvertFrom-Json
                    Write-Success "从容器加载 MCP 服务器清单"
                    return $servers
                }
            }
        } catch {
            Write-Warning "无法从容器加载，使用默认清单"
        }
    }

    return Get-McpServers
}

<#
.SYNOPSIS
    检查 MCP 服务器是否已安装
.DESCRIPTION
    检查指定的 MCP 服务器是否已在容器内安装。
.PARAMETER ServerName
    MCP 服务器名称
.PARAMETER ContainerName
    容器名称（默认 workflow-mcp-env）
.EXAMPLE
    Test-McpServerInstalled -ServerName "context7-mcp"
#>
function Test-McpServerInstalled {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ServerName,

        [string]$ContainerName = (Get-ContainerName)
    )

    $server = Get-McpServers | Where-Object { $_.Name -eq $ServerName }
    if (-not $server) {
        Write-Error "未知的 MCP 服务器: $ServerName"
        return $false
    }

    try {
        $state = Get-ContainerState -ContainerName $ContainerName
        if ($state -ne "running") {
            Write-Error "容器未在运行"
            return $false
        }

        switch ($server.Type) {
            "npm" {
                $checkCmd = if ($server.Global) {
                    "npm list -g --depth=0 2>/dev/null | grep -i '$($server.Package)'"
                } else {
                    "npm list --depth=0 2>/dev/null | grep -i '$($server.Package)'"
                }
                $result = docker exec $ContainerName sh -c $checkCmd 2>&1
                return ($LASTEXITCODE -eq 0)
            }
            "pip" {
                $result = docker exec $ContainerName sh -c "pip3 list 2>/dev/null | grep -i '$($server.Package)'" 2>&1
                return ($LASTEXITCODE -eq 0)
            }
            "local" {
                $result = docker exec $ContainerName sh -c "test -d '$($server.Package)'" 2>&1
                return ($LASTEXITCODE -eq 0)
            }
            default {
                return $false
            }
        }
    } catch {
        return $false
    }
}

<#
.SYNOPSIS
    获取 MCP 服务器版本
.DESCRIPTION
    获取已安装的 MCP 服务器版本号。
.PARAMETER ServerName
    MCP 服务器名称
.EXAMPLE
    Get-McpServerVersion -ServerName "context7-mcp"
#>
function Get-McpServerVersion {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ServerName
    )

    $server = Get-McpServers | Where-Object { $_.Name -eq $ServerName }
    if (-not $server) {
        Write-Error "未知的 MCP 服务器: $ServerName"
        return $null
    }

    try {
        $state = Get-ContainerState
        if ($state -ne "running") {
            Write-Error "容器未在运行"
            return $null
        }

        switch ($server.Type) {
            "npm" {
                $checkCmd = if ($server.Global) {
                    "npm list -g $($server.Package) --depth=0 2>/dev/null"
                } else {
                    "npm list $($server.Package) --depth=0 2>/dev/null"
                }
                $result = docker exec $(Get-ContainerName) sh -c $checkCmd 2>&1
                if ($result -match '@([\d.]+)') {
                    return $matches[1]
                }
                return $null
            }
            "pip" {
                $result = docker exec $(Get-ContainerName) sh -c "pip3 show $($server.Package) 2>/dev/null | grep Version" 2>&1
                if ($result -match 'Version:\s*([\d.]+)') {
                    return $matches[1]
                }
                return $null
            }
            default {
                return $null
            }
        }
    } catch {
        return $null
    }
}

<#
.SYNOPSIS
    安装 MCP 服务器
.DESCRIPTION
    在容器内安装指定的 MCP 服务器。
.PARAMETER ServerName
    MCP 服务器名称
.PARAMETER Version
    指定版本（默认最新版）
.EXAMPLE
    Install-McpServer -ServerName "context7-mcp"
.EXAMPLE
    Install-McpServer -ServerName "context7-mcp" -Version "1.2.0"
#>
function Install-McpServer {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ServerName,

        [string]$Version = ""
    )

    $server = Get-McpServers | Where-Object { $_.Name -eq $ServerName }
    if (-not $server) {
        Write-Error "未知的 MCP 服务器: $ServerName"
        return $false
    }

    # 检查是否已安装
    if (Test-McpServerInstalled -ServerName $ServerName) {
        $currentVer = Get-McpServerVersion -ServerName $ServerName
        Write-Warning "$ServerName 已安装 (版本: $currentVer)"
        $force = Read-Host "  是否重新安装？(y/N)"
        if ($force -ne 'y' -and $force -ne 'Y') {
            return $true
        }
    }

    try {
        $state = Get-ContainerState
        if ($state -ne "running") {
            Write-Error "容器未在运行，请先启动容器"
            return $false
        }

        $packageSpec = if ($Version) { "$($server.Package)@$Version" } else { $server.Package }

        Write-Header "安装 MCP 服务器: $ServerName"
        Write-Info "包: $packageSpec"
        Write-Info "类型: $($server.Type)"

        switch ($server.Type) {
            "npm" {
                $installCmd = if ($server.Global) {
                    "npm install -g $packageSpec 2>&1"
                } else {
                    "npm install $packageSpec 2>&1"
                }
                Write-Info "执行: npm install..."
                $result = docker exec $(Get-ContainerName) sh -c $installCmd 2>&1
                if ($LASTEXITCODE -eq 0) {
                    Write-Success "$ServerName 安装成功"
                    return $true
                } else {
                    Write-Error "安装失败: $result"
                    return $false
                }
            }
            "pip" {
                Write-Info "执行: pip3 install..."
                $result = docker exec $(Get-ContainerName) sh -c "pip3 install $packageSpec 2>&1" 2>&1
                if ($LASTEXITCODE -eq 0) {
                    Write-Success "$ServerName 安装成功"
                    return $true
                } else {
                    Write-Error "安装失败: $result"
                    return $false
                }
            }
            "local" {
                Write-Info "本地项目，检查目录是否存在..."
                $dirExists = docker exec $(Get-ContainerName) sh -c "test -d '$($server.Package)' && echo yes" 2>&1
                if ($dirExists -eq "yes") {
                    Write-Success "本地项目目录存在: $($server.Package)"
                    return $true
                } else {
                    Write-Error "本地项目目录不存在: $($server.Package)"
                    Write-Info "请确保项目已挂载到容器内"
                    return $false
                }
            }
            default {
                Write-Error "不支持的安装类型: $($server.Type)"
                return $false
            }
        }
    } catch {
        Write-Error "安装失败: $_"
        return $false
    }
}

<#
.SYNOPSIS
    卸载 MCP 服务器
.DESCRIPTION
    从容器内卸载指定的 MCP 服务器。
.PARAMETER ServerName
    MCP 服务器名称
.EXAMPLE
    Uninstall-McpServer -ServerName "context7-mcp"
#>
function Uninstall-McpServer {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ServerName
    )

    $server = Get-McpServers | Where-Object { $_.Name -eq $ServerName }
    if (-not $server) {
        Write-Error "未知的 MCP 服务器: $ServerName"
        return $false
    }

    if (-not (Test-McpServerInstalled -ServerName $ServerName)) {
        Write-Warning "$ServerName 未安装"
        return $true
    }

    try {
        $state = Get-ContainerState
        if ($state -ne "running") {
            Write-Error "容器未在运行"
            return $false
        }

        Write-Header "卸载 MCP 服务器: $ServerName"

        $confirm = Read-Host "  确认卸载 $ServerName？(y/N)"
        if ($confirm -ne 'y' -and $confirm -ne 'Y') {
            Write-Info "已取消"
            return $false
        }

        switch ($server.Type) {
            "npm" {
                $uninstallCmd = if ($server.Global) {
                    "npm uninstall -g $($server.Package) 2>&1"
                } else {
                    "npm uninstall $($server.Package) 2>&1"
                }
                $result = docker exec $(Get-ContainerName) sh -c $uninstallCmd 2>&1
                if ($LASTEXITCODE -eq 0) {
                    Write-Success "$ServerName 卸载成功"
                    return $true
                } else {
                    Write-Error "卸载失败: $result"
                    return $false
                }
            }
            "pip" {
                $result = docker exec $(Get-ContainerName) sh -c "pip3 uninstall -y $($server.Package) 2>&1" 2>&1
                if ($LASTEXITCODE -eq 0) {
                    Write-Success "$ServerName 卸载成功"
                    return $true
                } else {
                    Write-Error "卸载失败: $result"
                    return $false
                }
            }
            "local" {
                Write-Warning "本地项目无法通过此方式卸载"
                Write-Info "请手动删除项目目录或从配置中移除"
                return $false
            }
            default {
                Write-Error "不支持的安装类型: $($server.Type)"
                return $false
            }
        }
    } catch {
        Write-Error "卸载失败: $_"
        return $false
    }
}

<#
.SYNOPSIS
    更新 MCP 服务器
.DESCRIPTION
    更新指定的 MCP 服务器到最新版。
.PARAMETER ServerName
    MCP 服务器名称
.EXAMPLE
    Update-McpServer -ServerName "context7-mcp"
#>
function Update-McpServer {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ServerName
    )

    $server = Get-McpServers | Where-Object { $_.Name -eq $ServerName }
    if (-not $server) {
        Write-Error "未知的 MCP 服务器: $ServerName"
        return $false
    }

    if (-not (Test-McpServerInstalled -ServerName $ServerName)) {
        Write-Warning "$ServerName 未安装，将执行安装"
        return Install-McpServer -ServerName $ServerName
    }

    $currentVer = Get-McpServerVersion -ServerName $ServerName
    Write-Info "当前版本: $currentVer"

    try {
        $state = Get-ContainerState
        if ($state -ne "running") {
            Write-Error "容器未在运行"
            return $false
        }

        Write-Header "更新 MCP 服务器: $ServerName"

        switch ($server.Type) {
            "npm" {
                $updateCmd = if ($server.Global) {
                    "npm update -g $($server.Package) 2>&1"
                } else {
                    "npm update $($server.Package) 2>&1"
                }
                $result = docker exec $(Get-ContainerName) sh -c $updateCmd 2>&1
                if ($LASTEXITCODE -eq 0) {
                    $newVer = Get-McpServerVersion -ServerName $ServerName
                    Write-Success "$ServerName 更新成功: $currentVer → $newVer"
                    return $true
                } else {
                    Write-Error "更新失败: $result"
                    return $false
                }
            }
            "pip" {
                $result = docker exec $(Get-ContainerName) sh -c "pip3 install --upgrade $($server.Package) 2>&1" 2>&1
                if ($LASTEXITCODE -eq 0) {
                    $newVer = Get-McpServerVersion -ServerName $ServerName
                    Write-Success "$ServerName 更新成功: $currentVer → $newVer"
                    return $true
                } else {
                    Write-Error "更新失败: $result"
                    return $false
                }
            }
            "local" {
                Write-Warning "本地项目需要重新构建来更新"
                Write-Info "请进入容器执行构建: cd $($server.Package) && npm run build"
                return $false
            }
            default {
                Write-Error "不支持的安装类型: $($server.Type)"
                return $false
            }
        }
    } catch {
        Write-Error "更新失败: $_"
        return $false
    }
}

<#
.SYNOPSIS
    批量安装所有 MCP 服务器
.DESCRIPTION
    安装所有未安装的 MCP 服务器。
.PARAMETER Force
    强制重新安装已存在的
.EXAMPLE
    Install-AllMcpServers
.EXAMPLE
    Install-AllMcpServers -Force
#>
function Install-AllMcpServers {
    [CmdletBinding()]
    param(
        [switch]$Force
    )

    Write-Header "批量安装 MCP 服务器"

    $servers = Get-McpServerList
    $success = 0
    $failed = 0
    $skipped = 0

    foreach ($server in $servers) {
        $name = $server.Name
        Write-Host "`n  ── $name ──" -ForegroundColor White

        $installed = Test-McpServerInstalled -ServerName $name
        if ($installed -and -not $Force) {
            $ver = Get-McpServerVersion -ServerName $name
            Write-Info "已安装 (版本: $ver)，跳过"
            $skipped++
            continue
        }

        $result = Install-McpServer -ServerName $name
        if ($result) {
            $success++
        } else {
            $failed++
        }
    }

    Write-Header "批量安装完成"
    Write-Success "成功: $success"
    if ($failed -gt 0) { Write-Error "失败: $failed" }
    if ($skipped -gt 0) { Write-Info "跳过: $skipped" }

    return $failed -eq 0
}

<#
.SYNOPSIS
    批量更新所有 MCP 服务器
.DESCRIPTION
    更新所有已安装的 MCP 服务器到最新版。
.EXAMPLE
    Update-AllMcpServers
#>
function Update-AllMcpServers {
    [CmdletBinding()]
    param()

    Write-Header "批量更新 MCP 服务器"

    $servers = Get-McpServerList
    $success = 0
    $failed = 0
    $skipped = 0

    foreach ($server in $servers) {
        $name = $server.Name
        Write-Host "`n  ── $name ──" -ForegroundColor White

        if (-not (Test-McpServerInstalled -ServerName $name)) {
            Write-Info "未安装，跳过"
            $skipped++
            continue
        }

        $currentVer = Get-McpServerVersion -ServerName $name
        Write-Info "当前版本: $currentVer"

        $result = Update-McpServer -ServerName $name
        if ($result) {
            $success++
        } else {
            $failed++
        }
    }

    Write-Header "批量更新完成"
    Write-Success "成功: $success"
    if ($failed -gt 0) { Write-Error "失败: $failed" }
    if ($skipped -gt 0) { Write-Info "跳过: $skipped" }

    return $failed -eq 0
}

<#
.SYNOPSIS
    显示 MCP 服务器安装状态
.DESCRIPTION
    列出所有 MCP 服务器的安装状态和版本。
.EXAMPLE
    Show-McpServerStatus
#>
function Show-McpServerStatus {
    [CmdletBinding()]
    param()

    Write-Header "MCP 服务器安装状态"

    $servers = Get-McpServerList

    Write-Host "  $('名称'.PadRight(30)) $('端口'.PadRight(8)) $('类型'.PadRight(8)) $('状态'.PadRight(10)) 版本" -ForegroundColor White
    Write-Host "  $('-' * 30) $('-' * 8) $('-' * 8) $('-' * 10) $('-' * 10)" -ForegroundColor DarkGray

    foreach ($server in $servers) {
        $name = $server.Name.PadRight(30)
        $port = "$($server.Port)".PadRight(8)
        $type = $server.Type.PadRight(8)

        $installed = Test-McpServerInstalled -ServerName $server.Name
        if ($installed) {
            $ver = Get-McpServerVersion -ServerName $server.Name
            $status = "✅ 已安装".PadRight(10)
            $version = if ($ver) { $ver } else { "?" }

            Write-Host "  $name $port $type $status $version" -ForegroundColor Green
        } else {
            $status = "❌ 未安装".PadRight(10)
            Write-Host "  $name $port $type $status -" -ForegroundColor Red
        }
    }
}

# ============================================================
# 模块导出
# ============================================================

Export-ModuleMember -Function @(
    "Get-McpServerList"
    "Test-McpServerInstalled"
    "Get-McpServerVersion"
    "Install-McpServer"
    "Uninstall-McpServer"
    "Update-McpServer"
    "Install-AllMcpServers"
    "Update-AllMcpServers"
    "Show-McpServerStatus"
)
