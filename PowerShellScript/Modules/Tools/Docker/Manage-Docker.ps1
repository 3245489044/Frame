<#
.SYNOPSIS
    Docker 统一管理入口 — 交互式菜单 + 命令行模式
.DESCRIPTION
    提供 Docker 容器和 MCP 服务的统一管理界面。
    支持交互式菜单和命令行两种模式。

    命令行模式:
        .\Manage-Docker.ps1 -Command status      查看状态
        .\Manage-Docker.ps1 -Command build        构建镜像
        .\Manage-Docker.ps1 -Command start        启动容器
        .\Manage-Docker.ps1 -Command stop         停止容器
        .\Manage-Docker.ps1 -Command restart      重启容器
        .\Manage-Docker.ps1 -Command logs         查看日志
        .\Manage-Docker.ps1 -Command shell        进入容器
        .\Manage-Docker.ps1 -Command diag         运行诊断
        .\Manage-Docker.ps1 -Command mcp-test     验证 MCP 服务
        .\Manage-Docker.ps1 -Command mcp-status   查看 MCP 安装状态
        .\Manage-Docker.ps1 -Command mcp-install  安装 MCP 服务器
        .\Manage-Docker.ps1 -Command mcp-config   管理 CLine 配置
        .\Manage-Docker.ps1 -Command sync-config  从 Docker 同步配置到 CLine

    交互式模式:
        .\Manage-Docker.ps1

.PARAMETER Command
    要执行的命令（不指定则进入交互式菜单）
.PARAMETER Quick
    快速模式（用于 diag 和 mcp-test 命令）
.PARAMETER Force
    强制模式（用于 repair 命令）
.PARAMETER NoCache
    无缓存构建（用于 build 命令）

.EXAMPLE
    .\Manage-Docker.ps1
    进入交互式菜单

.EXAMPLE
    .\Manage-Docker.ps1 -Command status
    查看 Docker 状态

.EXAMPLE
    .\Manage-Docker.ps1 -Command mcp-test -Quick
    快速验证 MCP 服务
#>

param(
    [ValidateSet("status", "build", "start", "stop", "restart", "logs", "shell",
                 "diag", "mcp-test", "mcp-status", "mcp-install", "mcp-download", "mcp-config",
                 "sync-config", "repair-engine", "repair-image", "stats", "backup",
                 "restore", "help", "")]
    [string]$Command = "",

    [switch]$Quick,
    [switch]$Force,
    [switch]$NoCache
)

# ============================================================
# 模块加载
# ============================================================

$ScriptRoot = $PSScriptRoot
$ProjectRoot = Resolve-Path (Join-Path $ScriptRoot "..\..")

# 通过 Docker.psd1 统一加载整个模块
$modulePath = Join-Path $ProjectRoot "Modules\Docker\Docker.psd1"
if (Test-Path $modulePath) {
    Import-Module $modulePath -Force -ErrorAction SilentlyContinue
} else {
    # 回退：逐个加载
    $moduleFiles = @(
        "Modules\Docker\DockerCommon.psm1",
        "Modules\Docker\DockerManagement.psm1",
        "Modules\Docker\McpInstallation.psm1",
        "Modules\Docker\McpConfiguration.psm1"
    )
    foreach ($file in $moduleFiles) {
        $path = Join-Path $ProjectRoot $file
        if (Test-Path $path) {
            Import-Module $path -Force -ErrorAction SilentlyContinue
        }
    }
}

# ============================================================
# 菜单显示（辅助函数由 DockerCommon.psm1 提供）
# ============================================================


function Show-MainMenu {
    Clear-Host
    Write-Banner

    # 显示当前状态摘要（使用 DockerCommon 提供的函数）
    try {
        $containerName = Get-ContainerName
        $state = Get-ContainerState -ContainerName $containerName
        if ($state -ne "unknown") {
            $health = Get-ContainerHealth -ContainerName $containerName
            Write-Host "  📊 当前状态: 容器=$state  健康=$health" -ForegroundColor $(if ($state -eq "running") { "Green" } else { "Red" })
        } else {
            Write-Host "  📊 当前状态: 容器未创建" -ForegroundColor Yellow
        }
    } catch {
        Write-Host "  📊 当前状态: 无法获取" -ForegroundColor Yellow
    }
    Write-Host ""

    Write-MenuHeader "Docker 容器管理"
    Write-MenuItem "1" "查看状态概览" "Green"
    Write-MenuItem "2" "构建镜像" "Yellow"
    Write-MenuItem "3" "启动容器" "Green"
    Write-MenuItem "4" "停止容器" "Red"
    Write-MenuItem "5" "重启容器" "Yellow"
    Write-MenuItem "6" "查看日志" "Gray"
    Write-MenuItem "7" "进入容器 Shell" "Gray"
    Write-MenuItem "8" "查看资源占用" "Gray"

    Write-MenuHeader "诊断与修复"
    Write-MenuItem "D" "运行 Docker 环境诊断" "Yellow"
    Write-MenuItem "R" "修复 Docker 引擎" "Red"
    Write-MenuItem "I" "修复 Docker 镜像" "Yellow"

    Write-MenuHeader "MCP 服务管理"
    Write-MenuItem "T" "验证 MCP 服务" "Green"
    Write-MenuItem "S" "查看 MCP 安装状态" "Cyan"
    Write-MenuItem "A" "安装/更新 MCP 服务器" "Cyan"

    Write-MenuHeader "CLine 配置管理"
    Write-MenuItem "C" "查看 CLine 配置" "Cyan"
    Write-MenuItem "Y" "从 Docker 同步配置" "Cyan"
    Write-MenuItem "B" "备份/恢复配置" "Gray"
    Write-MenuItem "V" "验证配置" "Yellow"

    Write-MenuHeader "其他"
    Write-MenuItem "H" "帮助" "Gray"
    Write-MenuItem "Q" "退出" "Red"

    Write-Host ""
}

# ============================================================
# 命令处理
# ============================================================

function Invoke-InteractiveMenu {
    do {
        Show-MainMenu
        $choice = Read-Host "  请选择操作"

        switch ($choice.ToUpper()) {
            # ─── Docker 容器管理 ───
            "1" {
                Clear-Host
                Get-DockerStatus
                Pause-Menu
            }
            "2" {
                Clear-Host
                Build-DockerImage -NoCache:$NoCache
                Pause-Menu
            }
            "3" {
                Clear-Host
                Start-DockerContainer
                Pause-Menu
            }
            "4" {
                Clear-Host
                Stop-DockerContainer
                Pause-Menu
            }
            "5" {
                Clear-Host
                Restart-DockerContainer
                Pause-Menu
            }
            "6" {
                Clear-Host
                Get-DockerLogs -Tail 50
                Pause-Menu
            }
            "7" {
                Clear-Host
                Enter-DockerContainer
                Pause-Menu
            }
            "8" {
                Clear-Host
                Get-DockerStats
                Pause-Menu
            }

            # ─── 诊断与修复 ───
            "D" {
                Clear-Host
                Test-DockerEnvironment -Quick:$Quick
                Pause-Menu
            }
            "R" {
                Clear-Host
                Repair-DockerEngine -Force:$Force
                Pause-Menu
            }
            "I" {
                Clear-Host
                & (Join-Path $ScriptRoot "Repair-DockerImage.ps1")
                Pause-Menu
            }

            # ─── MCP 服务管理 ───
            "T" {
                Clear-Host
                Test-McpServices -Quick:$Quick
                Pause-Menu
            }
            "S" {
                Clear-Host
                Show-McpServerStatus
                Pause-Menu
            }
            "A" {
                Clear-Host
                Show-McpInstallMenu
            }

            # ─── CLine 配置管理 ───
            "C" {
                Clear-Host
                Show-ClineMcpConfig
                Pause-Menu
            }
            "Y" {
                Clear-Host
                Sync-McpConfigFromDocker
                Pause-Menu
            }
            "B" {
                Clear-Host
                Show-BackupMenu
            }
            "V" {
                Clear-Host
                Test-ClineMcpConfig
                Pause-Menu
            }

            # ─── 其他 ───
            "H" {
                Clear-Host
                Show-Help
                Pause-Menu
            }
            "Q" {
                Write-Host "`n  再见！" -ForegroundColor Cyan
                return
            }
            default {
                Write-Host "`n  ❌ 无效选择，请重新输入" -ForegroundColor Red
                Start-Sleep -Seconds 1
            }
        }
    } while ($true)
}

function Show-McpInstallMenu {
    Write-MenuHeader "MCP 服务器安装管理"
    Write-MenuItem "1" "安装所有 MCP 服务器" "Green"
    Write-MenuItem "2" "更新所有 MCP 服务器" "Yellow"
    Write-MenuItem "3" "安装单个 MCP 服务器" "Cyan"
    Write-MenuItem "4" "更新单个 MCP 服务器" "Cyan"
    Write-MenuItem "5" "卸载 MCP 服务器" "Red"
    Write-MenuItem "B" "返回主菜单" "Gray"
    Write-Host ""

    $choice = Read-Host "  请选择操作"

    switch ($choice.ToUpper()) {
        "1" {
            Clear-Host
            Install-AllMcpServers -Force:$Force
            Pause-Menu
        }
        "2" {
            Clear-Host
            Update-AllMcpServers
            Pause-Menu
        }
        "3" {
            Clear-Host
            $name = Read-Host "  请输入 MCP 服务器名称"
            if ($name) {
                Install-McpServer -ServerName $name
            }
            Pause-Menu
        }
        "4" {
            Clear-Host
            $name = Read-Host "  请输入 MCP 服务器名称"
            if ($name) {
                Update-McpServer -ServerName $name
            }
            Pause-Menu
        }
        "5" {
            Clear-Host
            $name = Read-Host "  请输入 MCP 服务器名称"
            if ($name) {
                Uninstall-McpServer -ServerName $name
            }
            Pause-Menu
        }
    }
}

function Show-BackupMenu {
    Write-MenuHeader "CLine 配置备份管理"
    Write-MenuItem "1" "查看备份列表" "Gray"
    Write-MenuItem "2" "创建备份" "Green"
    Write-MenuItem "3" "恢复最新备份" "Yellow"
    Write-MenuItem "B" "返回主菜单" "Gray"
    Write-Host ""

    $choice = Read-Host "  请选择操作"

    switch ($choice.ToUpper()) {
        "1" {
            Clear-Host
            Get-ClineMcpBackupList
            Pause-Menu
        }
        "2" {
            Clear-Host
            Backup-ClineMcpConfig
            Pause-Menu
        }
        "3" {
            Clear-Host
            Restore-ClineMcpConfig
            Pause-Menu
        }
    }
}

function Show-Help {
    Write-MenuHeader "帮助信息"
    Write-Host ""
    Write-Host "  Docker 统一管理工具 — 使用说明" -ForegroundColor White
    Write-Host ""
    Write-Host "  交互式模式:" -ForegroundColor Cyan
    Write-Host "    .\Manage-Docker.ps1" -ForegroundColor Gray
    Write-Host "    启动交互式菜单，通过数字/字母选择操作" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  命令行模式:" -ForegroundColor Cyan
    Write-Host "    .\Manage-Docker.ps1 -Command status      查看状态" -ForegroundColor Gray
    Write-Host "    .\Manage-Docker.ps1 -Command build       构建镜像" -ForegroundColor Gray
    Write-Host "    .\Manage-Docker.ps1 -Command start       启动容器" -ForegroundColor Gray
    Write-Host "    .\Manage-Docker.ps1 -Command stop        停止容器" -ForegroundColor Gray
    Write-Host "    .\Manage-Docker.ps1 -Command restart     重启容器" -ForegroundColor Gray
    Write-Host "    .\Manage-Docker.ps1 -Command logs        查看日志" -ForegroundColor Gray
    Write-Host "    .\Manage-Docker.ps1 -Command shell       进入容器" -ForegroundColor Gray
    Write-Host "    .\Manage-Docker.ps1 -Command stats       资源占用" -ForegroundColor Gray
    Write-Host "    .\Manage-Docker.ps1 -Command diag        运行诊断" -ForegroundColor Gray
    Write-Host "    .\Manage-Docker.ps1 -Command mcp-test    验证 MCP 服务" -ForegroundColor Gray
    Write-Host "    .\Manage-Docker.ps1 -Command mcp-status  查看 MCP 安装状态" -ForegroundColor Gray
    Write-Host "    .\Manage-Docker.ps1 -Command mcp-install 安装 MCP 服务器" -ForegroundColor Gray
    Write-Host "    .\Manage-Docker.ps1 -Command mcp-config  查看 CLine 配置" -ForegroundColor Gray
    Write-Host "    .\Manage-Docker.ps1 -Command sync-config 同步配置" -ForegroundColor Gray
    Write-Host "    .\Manage-Docker.ps1 -Command backup      创建备份" -ForegroundColor Gray
    Write-Host "    .\Manage-Docker.ps1 -Command restore     恢复备份" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  常用参数:" -ForegroundColor Cyan
    Write-Host "    -Quick      快速模式（跳过详细测试）" -ForegroundColor Gray
    Write-Host "    -Force      强制模式（跳过确认）" -ForegroundColor Gray
    Write-Host "    -NoCache    无缓存构建" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  示例:" -ForegroundColor Cyan
    Write-Host "    .\Manage-Docker.ps1 -Command mcp-test -Quick" -ForegroundColor Gray
    Write-Host "    .\Manage-Docker.ps1 -Command build -NoCache" -ForegroundColor Gray
    Write-Host "    .\Manage-Docker.ps1 -Command diag -Quick" -ForegroundColor Gray
}

# ============================================================
# 命令行模式
# ============================================================

function Invoke-CommandMode {
    switch ($Command) {
        "status"       { Get-DockerStatus }
        "build"        { Build-DockerImage -NoCache:$NoCache }
        "start"        { Start-DockerContainer }
        "stop"         { Stop-DockerContainer }
        "restart"      { Restart-DockerContainer }
        "logs"         { Get-DockerLogs -Tail 50 }
        "shell"        { Enter-DockerContainer }
        "stats"        { Get-DockerStats }
        "diag"         { Test-DockerEnvironment -Quick:$Quick }
        "mcp-test"     { Test-McpServices -Quick:$Quick }
        "mcp-status"   { Show-McpServerStatus }
        "mcp-install"  { Install-AllMcpServers -Force:$Force }
        "mcp-download" {
            $downloadScript = Join-Path $ProjectRoot "PowerShellScript\Tools\Install\Install-McpServer.ps1"
            if (Test-Path $downloadScript) {
                & $downloadScript -Name "*" -Force:$Force
            } else {
                Write-Error "下载安装脚本不存在: $downloadScript"
            }
        }
        "mcp-config"   { Show-ClineMcpConfig }
        "sync-config"  { Sync-McpConfigFromDocker }
        "repair-engine" { Repair-DockerEngine -Force:$Force }
        "repair-image" { & (Join-Path $ScriptRoot "Repair-DockerImage.ps1") }
        "backup"       { Backup-ClineMcpConfig }
        "restore"      { Restore-ClineMcpConfig }
        "help"         { Show-Help }
        default        { Show-Help }
    }
}

# ============================================================
# 主入口
# ============================================================

function Main {
    if ($Command) {
        # 命令行模式
        Invoke-CommandMode
    } else {
        # 交互式菜单模式
        Invoke-InteractiveMenu
    }
}

# 启动
Main
