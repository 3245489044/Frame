<#
.SYNOPSIS
    修复 CsprojContext MCP 配置 — 更新 CLine MCP 配置中的 csproj-context 服务 URL
.DESCRIPTION
    读取 Docker 容器中 csproj-context 服务的实际端口映射，更新 CLine MCP 配置。
    支持 WhatIf 预览和 Force 强制模式。

.PARAMETER Force
    直接执行修复，不提示确认
.PARAMETER WhatIf
    预览将要执行的更改，不实际修改
.PARAMETER Backup
    修改前备份配置文件（默认 $true）

.EXAMPLE
    .\Fix-CsprojContextConfig.ps1
    交互式修复

.EXAMPLE
    .\Fix-CsprojContextConfig.ps1 -WhatIf
    预览更改

.EXAMPLE
    .\Fix-CsprojContextConfig.ps1 -Force
    直接修复，不提示
#>

param(
    [switch]$Force,
    [switch]$WhatIf,
    [switch]$Backup = $true
)

# ============================================================
# 导入共享模块
# ============================================================
$ModulePath = Join-Path $PSScriptRoot "..\..\..\PowerShellScript\Modules\Docker\DockerCommon.psm1"
Import-Module $ModulePath -Force -DisableNameChecking

# ============================================================
# 常量（使用 DockerCommon 的函数获取）
# ============================================================
$ContainerName = Get-ContainerName
$ClineConfigPath = Get-ClineConfigPath
$BackupDir = Get-BackupDir

# ============================================================
# 辅助函数
# ============================================================

function Get-CsprojContextPort {
    <#
    .SYNOPSIS
        从 Docker 容器中获取 csproj-context 服务的实际端口映射
    .DESCRIPTION
        执行 docker port 命令获取容器内 3107 端口映射到宿主机的实际端口。
    #>
    try {
        $portInfo = docker port $ContainerName 3107 2>&1
        if ($LASTEXITCODE -eq 0 -and $portInfo) {
            if ($portInfo -match '0\.0\.0\.0:(\d+)') {
                return [int]$Matches[1]
            }
        }
        return 3107  # 默认端口
    } catch {
        return 3107
    }
}

function Get-CurrentConfig {
    <#
    .SYNOPSIS
        读取当前 CLine MCP 配置
    #>
    if (-not (Test-Path $ClineConfigPath)) {
        Write-Error "CLine 配置文件不存在: $ClineConfigPath"
        return $null
    }

    try {
        $content = Get-Content $ClineConfigPath -Raw -Encoding UTF8
        return $content | ConvertFrom-Json
    } catch {
        Write-Error "读取 CLine 配置失败: $_"
        return $null
    }
}

function Backup-ClineConfig {
    <#
    .SYNOPSIS
        备份 CLine MCP 配置文件
    #>
    if (-not $Backup) { return $true }

    if (-not (Test-Path $BackupDir)) {
        New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null
    }

    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $backupFile = Join-Path $BackupDir "cline_mcp_settings_$timestamp.json"

    try {
        Copy-Item $ClineConfigPath $backupFile -Force
        Write-Success "配置已备份: $backupFile"
        return $true
    } catch {
        Write-Error "备份失败: $_"
        return $false
    }
}

# ============================================================
# 主流程
# ============================================================

function Main {
    Write-Banner -Title "CsprojContext 配置修复工具" -Subtitle "更新 CLine MCP 配置中的 csproj-context 服务 URL"

    # 1. 获取实际端口
    Write-Step "步骤 1: 获取 csproj-context 服务端口"
    $actualPort = Get-CsprojContextPort
    Write-Info "csproj-context 服务端口: $actualPort"

    # 2. 读取当前配置
    Write-Step "步骤 2: 读取当前 CLine 配置"
    $config = Get-CurrentConfig
    if (-not $config) { return }

    $currentUrl = $config.mcpServers."csproj-context".url
    Write-Info "当前配置 URL: $currentUrl"

    # 3. 构建新 URL
    $newUrl = "http://localhost:$actualPort/mcp"
    Write-Info "新配置 URL: $newUrl"

    if ($currentUrl -eq $newUrl) {
        Write-Success "配置已是最新，无需修改"
        return
    }

    # 4. 确认
    Write-Step "步骤 3: 确认修改"
    Write-Host "  配置变更:" -ForegroundColor Yellow
    Write-Host "    旧: $currentUrl" -ForegroundColor Red
    Write-Host "    新: $newUrl" -ForegroundColor Green

    if (-not (Confirm-Action -Message "确认更新 CLine 配置？" -Force:$Force -WhatIf:$WhatIf)) {
        Write-Info "已取消"
        return
    }

    if ($WhatIf) {
        Write-WhatIf "将更新 csproj-context URL: $currentUrl → $newUrl"
        return
    }

    # 5. 备份
    Write-Step "步骤 4: 备份配置"
    $backupOk = Backup-ClineConfig
    if (-not $backupOk) { return }

    # 6. 更新配置
    Write-Step "步骤 5: 更新配置"
    try {
        $config.mcpServers."csproj-context".url = $newUrl
        $json = $config | ConvertTo-Json -Depth 10
        $json | Out-File -FilePath $ClineConfigPath -Encoding UTF8 -Force
        Write-Success "CLine 配置已更新"
    } catch {
        Write-Error "更新配置失败: $_"
        return
    }

    # 7. 验证
    Write-Step "步骤 6: 验证更新"
    $verifyConfig = Get-CurrentConfig
    if ($verifyConfig) {
        $verifyUrl = $verifyConfig.mcpServers."csproj-context".url
        if ($verifyUrl -eq $newUrl) {
            Write-Success "验证通过，配置已正确更新"
        } else {
            Write-Error "验证失败，配置未正确更新"
        }
    }

    Write-Success "修复完成！请重启 VS Code 让 Cline 加载新配置"
}

# ============================================================
# 启动
# ============================================================

Main
