<#
.SYNOPSIS
    CLine MCP 配置管理模块
.DESCRIPTION
    提供 CLine MCP 配置文件的读取、写入、备份、恢复功能。
    支持从 Docker 配置模板同步到 CLine 配置。
.NOTES
    模块名: McpConfiguration
    依赖: DockerCommon（共享常量、辅助函数、路径解析）
#>

# ============================================================
# 配置访问（所有值通过 DockerCommon 的 Get-* 函数获取，避免硬编码）
#   不再使用 $Script:ClineConfigPath / $Script:BackupDir / $Script:DockerConfigTemplate
#   这些变量从未被初始化，统一替换为函数调用
# ============================================================

# 核心函数
# ============================================================

<#
.SYNOPSIS
    读取当前 CLine MCP 配置
.DESCRIPTION
    读取 cline_mcp_settings.json 文件并返回解析后的对象。
.PARAMETER Path
    配置文件路径（默认自动检测）
.EXAMPLE
    Get-ClineMcpConfig
#>
function Get-ClineMcpConfig {
    [CmdletBinding()]
    param(
        [string]$Path = (Get-ConfigValue "cline" "configPath")
    )

    if (-not (Test-Path $Path)) {
        Write-Error "CLine 配置文件不存在: $Path"
        return $null
    }

    try {
        $config = Get-Content $Path -Raw -Encoding UTF8 | ConvertFrom-Json
        Write-Success "已读取 CLine 配置: $Path"
        return $config
    } catch {
        Write-Error "读取配置失败: $_"
        return $null
    }
}

<#
.SYNOPSIS
    写入 CLine MCP 配置
.DESCRIPTION
    将配置对象写入 cline_mcp_settings.json，写入前自动备份。
.PARAMETER Config
    配置对象（必须包含 mcpServers 属性）
.PARAMETER Path
    配置文件路径（默认自动检测）
.PARAMETER SkipBackup
    跳过备份
.EXAMPLE
    Set-ClineMcpConfig -Config $configObject
#>
function Set-ClineMcpConfig {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object]$Config,

        [string]$Path = (Get-ConfigValue "cline" "configPath"),
        [switch]$SkipBackup
    )

    # 验证配置结构
    if (-not $Config.mcpServers) {
        Write-Error "配置对象缺少 mcpServers 属性"
        return $false
    }

    # 自动备份
    if (-not $SkipBackup) {
        Backup-ClineMcpConfig -Path $Path | Out-Null
    }

    try {
        # 确保目录存在
        $configDir = Split-Path $Path -Parent
        if (-not (Test-Path $configDir)) {
            New-Item -Path $configDir -ItemType Directory -Force | Out-Null
        }

        # 写入配置（带格式）
        $json = $Config | ConvertTo-Json -Depth 10
        $json | Out-File -FilePath $Path -Encoding UTF8 -Force

        Write-Success "配置已写入: $Path"
        return $true
    } catch {
        Write-Error "写入配置失败: $_"
        return $false
    }
}

<#
.SYNOPSIS
    备份当前 CLine 配置
.DESCRIPTION
    将当前配置备份到备份目录，文件名包含时间戳。
.PARAMETER Path
    配置文件路径（默认自动检测）
.EXAMPLE
    Backup-ClineMcpConfig
#>
function Backup-ClineMcpConfig {
    [CmdletBinding()]
    param(
        [string]$Path = (Get-ConfigValue "cline" "configPath")
    )

    if (-not (Test-Path $Path)) {
        Write-Warning "配置文件不存在，跳过备份: $Path"
        return $null
    }

    # 确保备份目录存在
    $backupDir = Get-ConfigValue "cline" "backupDir"
    if (-not (Test-Path $backupDir)) {
        New-Item -Path $backupDir -ItemType Directory -Force | Out-Null
    }

    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $backupFile = Join-Path $backupDir "cline_mcp_settings_$timestamp.json"

    try {
        Copy-Item -Path $Path -Destination $backupFile -Force
        Write-Success "配置已备份: $backupFile"
        return $backupFile
    } catch {
        Write-Error "备份失败: $_"
        return $null
    }
}

<#
.SYNOPSIS
    从备份恢复 CLine 配置
.DESCRIPTION
    从备份目录恢复指定或最新的配置备份。
.PARAMETER BackupFile
    备份文件路径（默认恢复最新的备份）
.EXAMPLE
    Restore-ClineMcpConfig
.EXAMPLE
    Restore-ClineMcpConfig -BackupFile "C:\Users\...\cline_mcp_settings_20240101_120000.json"
#>
function Restore-ClineMcpConfig {
    [CmdletBinding()]
    param(
        [string]$BackupFile = ""
    )

    $backupDir = Get-ConfigValue "cline" "backupDir"
    if (-not (Test-Path $backupDir)) {
        Write-Error "备份目录不存在: $backupDir"
        return $false
    }

    # 如果未指定备份文件，找最新的
    if (-not $BackupFile) {
        $backups = Get-ChildItem -Path $backupDir -Filter "cline_mcp_settings_*.json" |
            Sort-Object LastWriteTime -Descending

        if ($backups.Count -eq 0) {
            Write-Error "没有找到备份文件"
            return $false
        }

        $BackupFile = $backups[0].FullName
        Write-Info "使用最新备份: $BackupFile"
    }

    if (-not (Test-Path $BackupFile)) {
        Write-Error "备份文件不存在: $BackupFile"
        return $false
    }

    try {
        $config = Get-Content $BackupFile -Raw -Encoding UTF8 | ConvertFrom-Json
        $result = Set-ClineMcpConfig -Config $config -SkipBackup
        if ($result) {
            Write-Success "配置已从备份恢复: $BackupFile"
        }
        return $result
    } catch {
        Write-Error "恢复失败: $_"
        return $false
    }
}

<#
.SYNOPSIS
    列出所有配置备份
.DESCRIPTION
    列出备份目录中的所有配置备份文件。
.EXAMPLE
    Get-ClineMcpBackupList
#>
function Get-ClineMcpBackupList {
    [CmdletBinding()]
    param()

    $backupDir = Get-ConfigValue "cline" "backupDir"
    if (-not (Test-Path $backupDir)) {
        Write-Info "备份目录不存在: $backupDir"
        return @()
    }

    $backups = Get-ChildItem -Path $backupDir -Filter "cline_mcp_settings_*.json" |
        Sort-Object LastWriteTime -Descending

    if ($backups.Count -eq 0) {
        Write-Info "没有找到备份文件"
        return @()
    }

    Write-Header "CLine 配置备份列表"
    Write-Host "  $('文件名'.PadRight(50)) $('大小'.PadRight(10)) $('修改时间')" -ForegroundColor White
    Write-Host "  $('-' * 50) $('-' * 10) $('-' * 20)" -ForegroundColor DarkGray

    foreach ($backup in $backups) {
        $name = $backup.Name.PadRight(50)
        $size = "$([math]::Round($backup.Length/1KB, 1)) KB".PadRight(10)
        $time = $backup.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")
        Write-Host "  $name $size $time" -ForegroundColor Gray
    }

    return $backups
}

<#
.SYNOPSIS
    启用 MCP 服务
.DESCRIPTION
    在 CLine 配置中启用指定的 MCP 服务。
.PARAMETER ServerName
    MCP 服务名称
.PARAMETER ConfigPath
    配置文件路径（默认自动检测）
.EXAMPLE
    Enable-McpServer -ServerName "csproj-context"
#>
function Enable-McpServer {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ServerName,

        [string]$ConfigPath = (Get-ConfigValue "cline" "configPath")
    )

    $config = Get-ClineMcpConfig -Path $ConfigPath
    if (-not $config) { return $false }

    if ($config.mcpServers.$ServerName) {
        Write-Warning "服务 $ServerName 已启用"
        return $true
    }

    Write-Error "服务 $ServerName 不在配置中，请先使用 Add-McpServerConfig 添加"
    return $false
}

<#
.SYNOPSIS
    禁用 MCP 服务
.DESCRIPTION
    在 CLine 配置中禁用指定的 MCP 服务（通过设置 disabled 属性）。
.PARAMETER ServerName
    MCP 服务名称
.PARAMETER ConfigPath
    配置文件路径（默认自动检测）
.EXAMPLE
    Disable-McpServer -ServerName "csproj-context"
#>
function Disable-McpServer {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ServerName,

        [string]$ConfigPath = (Get-ConfigValue "cline" "configPath")
    )

    $config = Get-ClineMcpConfig -Path $ConfigPath
    if (-not $config) { return $false }

    if (-not $config.mcpServers.$ServerName) {
        Write-Warning "服务 $ServerName 不在配置中"
        return $true
    }

    # 通过移除配置来禁用
    $config.mcpServers.PSObject.Properties.Remove($ServerName)

    $result = Set-ClineMcpConfig -Config $config -Path $ConfigPath
    if ($result) {
        Write-Success "服务 $ServerName 已禁用"
    }
    return $result
}

<#
.SYNOPSIS
    添加 MCP 服务配置
.DESCRIPTION
    向 CLine 配置中添加新的 MCP 服务配置。
.PARAMETER ServerName
    MCP 服务名称
.PARAMETER Type
    服务类型（http 或 stdio）
.PARAMETER Url
    HTTP 模式的服务 URL（Type 为 http 时必填）
.PARAMETER Command
    Stdio 模式的启动命令（Type 为 stdio 时必填）
.PARAMETER Args
    Stdio 模式的启动参数
.PARAMETER ConfigPath
    配置文件路径（默认自动检测）
.EXAMPLE
    Add-McpServerConfig -ServerName "my-server" -Type "http" -Url "http://localhost:3108/mcp"
.EXAMPLE
    Add-McpServerConfig -ServerName "my-server" -Type "stdio" -Command "node" -Args @("server.js")
#>
function Add-McpServerConfig {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ServerName,

        [Parameter(Mandatory = $true)]
        [ValidateSet("http", "stdio")]
        [string]$Type,

        [string]$Url = "",
        [string]$Command = "",
        [string[]]$Args = @(),
        [string]$ConfigPath = (Get-ConfigValue "cline" "configPath")
    )

    $config = Get-ClineMcpConfig -Path $ConfigPath
    if (-not $config) { return $false }

    if ($config.mcpServers.$ServerName) {
        Write-Warning "服务 $ServerName 已存在，将被覆盖"
    }

    # 构建服务配置
    $serverConfig = @{
        type = $Type
    }

    if ($Type -eq "http") {
        if (-not $Url) {
            Write-Error "HTTP 模式需要提供 Url 参数"
            return $false
        }
        $serverConfig.url = $Url
    } else {
        if (-not $Command) {
            Write-Error "Stdio 模式需要提供 Command 参数"
            return $false
        }
        $serverConfig.command = $Command
        if ($Args.Count -gt 0) {
            $serverConfig.args = $Args
        }
    }

    # 添加配置
    $config.mcpServers | Add-Member -MemberType NoteProperty -Name $ServerName -Value $serverConfig -Force

    $result = Set-ClineMcpConfig -Config $config -Path $ConfigPath
    if ($result) {
        Write-Success "服务 $ServerName 已添加到配置"
    }
    return $result
}

<#
.SYNOPSIS
    移除 MCP 服务配置
.DESCRIPTION
    从 CLine 配置中移除指定的 MCP 服务。
.PARAMETER ServerName
    MCP 服务名称
.PARAMETER ConfigPath
    配置文件路径（默认自动检测）
.EXAMPLE
    Remove-McpServerConfig -ServerName "my-server"
#>
function Remove-McpServerConfig {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ServerName,

        [string]$ConfigPath = (Get-ConfigValue "cline" "configPath")
    )

    $config = Get-ClineMcpConfig -Path $ConfigPath
    if (-not $config) { return $false }

    if (-not $config.mcpServers.$ServerName) {
        Write-Warning "服务 $ServerName 不在配置中"
        return $true
    }

    $config.mcpServers.PSObject.Properties.Remove($ServerName)

    $result = Set-ClineMcpConfig -Config $config -Path $ConfigPath
    if ($result) {
        Write-Success "服务 $ServerName 已从配置中移除"
    }
    return $result
}

<#
.SYNOPSIS
    从 Docker 配置模板同步到 CLine
.DESCRIPTION
    读取 Docker 配置模板，生成对应的 CLine HTTP 模式配置并写入。
.PARAMETER ConfigPath
    CLine 配置文件路径（默认自动检测）
.PARAMETER TemplatePath
    Docker 配置模板路径
.EXAMPLE
    Sync-McpConfigFromDocker
#>
function Sync-McpConfigFromDocker {
    [CmdletBinding()]
    param(
        [string]$ConfigPath = (Get-ConfigValue "cline" "configPath"),
        [string]$TemplatePath = ""
    )

    # 确定模板路径
    if (-not $TemplatePath) {
        $TemplatePath = Resolve-ProjectPath (Get-ConfigValue "mcp" "configTemplate")

    }

    if (-not (Test-Path $TemplatePath)) {
        Write-Error "Docker 配置模板不存在: $TemplatePath"
        return $false
    }

    try {
        # 读取 Docker 配置模板
        $template = Get-Content $TemplatePath -Raw -Encoding UTF8 | ConvertFrom-Json
        Write-Info "已读取 Docker 配置模板: $TemplatePath"

        # 读取当前 CLine 配置
        $clineConfig = Get-ClineMcpConfig -Path $ConfigPath
        if (-not $clineConfig) {
            # 创建新配置
            $clineConfig = @{
                mcpServers = @{}
            }
        }

        # 从模板中提取 MCP 服务配置并转换为 HTTP 模式
        $services = $template.services
        if (-not $services) {
            Write-Error "模板中未找到 services 配置"
            return $false
        }

        $addedCount = 0
        $updatedCount = 0

        foreach ($service in $services.PSObject.Properties) {
            $name = $service.Name
            $config = $service.Value

            # 跳过非 MCP 服务
            if (-not $config.port) { continue }

            $port = $config.port
            $httpConfig = @{
                type = "http"
                url  = "http://localhost:$port/mcp"
            }

            if ($clineConfig.mcpServers.$name) {
                $updatedCount++
            } else {
                $addedCount++
            }

            $clineConfig.mcpServers | Add-Member -MemberType NoteProperty -Name $name -Value $httpConfig -Force
        }

        # 写入配置
        $result = Set-ClineMcpConfig -Config $clineConfig -Path $ConfigPath
        if ($result) {
            Write-Success "同步完成: 新增 $addedCount, 更新 $updatedCount 个服务"
        }
        return $result

    } catch {
        Write-Error "同步失败: $_"
        return $false
    }
}

<#
.SYNOPSIS
    验证 CLine 配置格式和完整性
.DESCRIPTION
    检查 CLine 配置文件的 JSON 格式和必要字段。
.PARAMETER Path
    配置文件路径（默认自动检测）
.EXAMPLE
    Test-ClineMcpConfig
#>
function Test-ClineMcpConfig {
    [CmdletBinding()]
    param(
        [string]$Path = (Get-ConfigValue "cline" "configPath")
    )

    Write-Header "验证 CLine MCP 配置"

    # 1. 文件存在性
    if (-not (Test-Path $Path)) {
        Write-Error "配置文件不存在: $Path"
        return $false
    }
    Write-Success "配置文件存在"

    # 2. JSON 格式
    try {
        $config = Get-Content $Path -Raw -Encoding UTF8 | ConvertFrom-Json
        Write-Success "JSON 格式正确"
    } catch {
        Write-Error "JSON 格式错误: $_"
        return $false
    }

    # 3. mcpServers 字段
    if (-not $config.mcpServers) {
        Write-Error "缺少 mcpServers 字段"
        return $false
    }
    Write-Success "包含 mcpServers 字段"

    # 4. 服务数量
    $serverCount = ($config.mcpServers.PSObject.Properties | Measure-Object).Count
    Write-Info "MCP 服务数量: $serverCount"

    # 5. 检查每个服务的配置
    $validCount = 0
    $invalidCount = 0

    foreach ($server in $config.mcpServers.PSObject.Properties) {
        $name = $server.Name
        $value = $server.Value

        $hasType = [bool]$value.type
        $hasUrlOrCmd = [bool]$value.url -or [bool]$value.command

        if ($hasType -and $hasUrlOrCmd) {
            $validCount++
            $typeInfo = if ($value.url) { "url: $($value.url)" } else { "cmd: $($value.command)" }
            Write-Info "  ✅ $name ($typeInfo)"
        } else {
            $invalidCount++
            Write-Warning "  ❌ $name (配置不完整)"
        }
    }

    # 6. 总结
    Write-Host ""
    if ($invalidCount -eq 0) {
        Write-Success "配置验证通过: $validCount 个服务配置正确"
        return $true
    } else {
        Write-Warning "配置验证完成: $validCount 个正确, $invalidCount 个异常"
        return $false
    }
}

<#
.SYNOPSIS
    显示当前 CLine 配置概览
.DESCRIPTION
    以表格形式显示当前 CLine 配置中的所有 MCP 服务。
.PARAMETER Path
    配置文件路径（默认自动检测）
.EXAMPLE
    Show-ClineMcpConfig
#>
function Show-ClineMcpConfig {
    [CmdletBinding()]
    param(
        [string]$Path = (Get-ConfigValue "cline" "configPath")
    )

    $config = Get-ClineMcpConfig -Path $Path
    if (-not $config) { return }

    Write-Header "CLine MCP 配置概览"

    $servers = $config.mcpServers.PSObject.Properties | Sort-Object Name

    Write-Host "  $('名称'.PadRight(30)) $('类型'.PadRight(8)) $('地址/命令')" -ForegroundColor White
    Write-Host "  $('-' * 30) $('-' * 8) $('-' * 40)" -ForegroundColor DarkGray

    foreach ($server in $servers) {
        $name = $server.Name.PadRight(30)
        $type = $server.Value.type.PadRight(8)
        $endpoint = if ($server.Value.url) {
            $server.Value.url
        } elseif ($server.Value.command) {
            "$($server.Value.command) $($server.Value.args -join ' ')"
        } else {
            "?"
        }
        Write-Host "  $name $type $endpoint" -ForegroundColor Gray
    }

    Write-Host "`n  总计: $($servers.Count) 个 MCP 服务" -ForegroundColor White
}



