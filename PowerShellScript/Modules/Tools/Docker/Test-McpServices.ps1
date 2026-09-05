<#
.SYNOPSIS
    MCP 服务验证脚本 — 验证 Docker 容器内 MCP 服务的运行状态
.DESCRIPTION
    按以下流程逐项验证 MCP 服务：
    1. 环境预检（Docker 引擎、容器状态）
    2. 端口可达性测试
    3. MCP 协议初始化测试
    4. MCP 工具列表获取
    5. MCP 工具抽样调用
    6. CLine 配置验证
    7. 综合验证结论

.PARAMETER OutputPath
    验证报告输出路径（默认输出到控制台）
.PARAMETER Quick
    快速模式，只检查端口和初始化
.PARAMETER SkipConfig
    跳过 CLine 配置检查

.EXAMPLE
    .\Test-McpServices.ps1
    运行完整验证

.EXAMPLE
    .\Test-McpServices.ps1 -Quick
    快速验证模式
#>

param(
    [string]$OutputPath,
    [switch]$Quick,
    [switch]$SkipConfig
)

# ============================================================
# 导入共享模块
# ============================================================
$ModulePath = Join-Path $PSScriptRoot "..\..\Modules\Docker\DockerCommon.psm1"
Import-Module $ModulePath -Force -DisableNameChecking

# ============================================================
# 常量（使用 DockerCommon 的函数获取）
# ============================================================
$ContainerName = Get-ContainerName
$PortStart = Get-PortStart
$PortEnd = Get-PortEnd
$McpServers = Get-McpServers

# ============================================================
# 验证函数
# ============================================================

# ─── 1. 环境预检 ───

function Check-Environment {
    Write-Section "1. 环境预检"

    # 1.1 Docker 引擎
    Write-SubSection "1.1 Docker 引擎"
    if (Test-DockerEngineRunning) {
        $version = Get-DockerVersion
        Write-Diagnostic "环境预检" "Docker引擎" "PASS" "版本 $version"
    } else {
        Write-Diagnostic "环境预检" "Docker引擎" "FAIL" "Docker 引擎不可用"
        return $false
    }

    # 1.2 容器状态
    Write-SubSection "1.2 容器状态"
    $state = Get-ContainerState
    if ($state -eq "running") {
        $uptime = Get-ContainerUptime
        Write-Diagnostic "环境预检" "容器状态" "PASS" "运行中（已运行 $uptime）"
    } elseif ($state -eq "exited" -or $state -eq "created") {
        Write-Diagnostic "环境预检" "容器状态" "FAIL" "容器状态: $state（需要启动）"
        return $false
    } else {
        Write-Diagnostic "环境预检" "容器状态" "FAIL" "容器 $ContainerName 不存在"
        return $false
    }

    return $true
}

# ─── 2. 端口可达性测试 ───

function Check-Ports {
    Write-Section "2. 端口可达性测试"

    $allPassed = $true
    foreach ($server in $McpServers) {
        $port = $server.Port
        $name = $server.Name
        $desc = $server.Description

        Write-Host "  ⏳ 测试 $desc ($name) 端口 $port..." -ForegroundColor Gray

        if (Test-PortHttpReachable -Port $port) {
            Write-Diagnostic "端口测试" $name "PASS" "端口 $port 可达"
        } else {
            Write-Diagnostic "端口测试" $name "FAIL" "端口 $port 不可达"
            $allPassed = $false
        }
    }

    return $allPassed
}

# ─── 3. MCP 协议初始化测试 ───

function Check-McpInitialize {
    Write-Section "3. MCP 协议初始化测试"

    $allPassed = $true
    foreach ($server in $McpServers) {
        $ok = Test-McpServiceInitialize -Port $server.Port -Name $server.Name -TimeoutSeconds 15
        if (-not $ok) { $allPassed = $false }
    }

    return $allPassed
}

# ─── 4. MCP 工具列表获取 ───

function Check-McpToolsList {
    Write-Section "4. MCP 工具列表获取"

    $allPassed = $true
    foreach ($server in $McpServers) {
        $ok = Test-McpServiceToolsList -Port $server.Port -Name $server.Name -TimeoutSeconds 15
        if (-not $ok) { $allPassed = $false }
    }

    return $allPassed
}

# ─── 5. MCP 工具抽样调用 ───

function Check-McpToolCalls {
    Write-Section "5. MCP 工具抽样调用"

    $allPassed = $true

    # 定义抽样测试用例
    $sampleTests = @(
        @{ Name = "sequentialthinking"; Port = 3100; Tool = "sequentialthinking_tools"; Args = @{} }
        @{ Name = "filesystem"; Port = 3104; Tool = "read_file"; Args = @{ path = "/workspace/README.md" } }
        @{ Name = "memory"; Port = 3103; Tool = "search_nodes"; Args = @{ query = "test" } }
    )

    foreach ($test in $sampleTests) {
        $result = Test-McpServiceToolCall -Port $test.Port -Name $test.Name -ToolName $test.Tool -Arguments $test.Args -TimeoutSeconds 15
        if ($result) {
            Write-Diagnostic "抽样测试" "$($test.Name)/$($test.Tool)" "PASS" "调用成功"
        } else {
            Write-Diagnostic "抽样测试" "$($test.Name)/$($test.Tool)" "WARN" "调用失败（可能不支持该工具）"
            $allPassed = $false
        }
    }

    return $allPassed
}

# ─── 6. CLine 配置验证 ───

function Check-ClineConfig {
    Write-Section "6. CLine 配置验证"

    $clineConfigPath = Get-ClineConfigPath
    if (-not (Test-Path $clineConfigPath)) {
        Write-Diagnostic "CLine配置" "配置文件" "FAIL" "不存在: $clineConfigPath"
        return $false
    }

    Write-Diagnostic "CLine配置" "配置文件" "PASS" $clineConfigPath

    try {
        $config = Get-Content $clineConfigPath -Raw | ConvertFrom-Json
        $mcpServers = $config."mcpServers"

        if (-not $mcpServers) {
            Write-Diagnostic "CLine配置" "mcpServers" "FAIL" "配置中缺少 mcpServers 节点"
            return $false
        }

        Write-Diagnostic "CLine配置" "mcpServers" "PASS" "存在"

        # 验证每个 MCP 服务是否在 CLine 配置中
        $allConfigured = $true
        foreach ($server in $McpServers) {
            $serverName = $server.Name
            $configured = $null -ne $mcpServers.$serverName

            if ($configured) {
                $url = $mcpServers.$serverName.url
                Write-Diagnostic "CLine配置" $serverName "PASS" "已配置: $url"
            } else {
                Write-Diagnostic "CLine配置" $serverName "WARN" "未在 CLine 配置中找到"
                $allConfigured = $false
            }
        }

        return $allConfigured
    } catch {
        Write-Diagnostic "CLine配置" "解析" "FAIL" "JSON 解析失败: $_"
        return $false
    }
}

# ============================================================
# 主流程
# ============================================================

function Main {
    Write-Banner -Title "MCP 服务验证工具 v1.0" -Subtitle "验证 Docker 容器内 MCP 服务的运行状态"
    Write-Host "  开始时间: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Gray
    if ($Quick) {
        Write-Host "  模式: 快速验证" -ForegroundColor Yellow
    }
    Write-Host ""

    # 初始化诊断上下文
    New-DiagnosticContext

    # 1. 环境预检
    $envOk = Check-Environment
    if (-not $envOk) {
        Write-Host "`n  ⛔ 环境预检失败，无法继续验证" -ForegroundColor Red
        Show-DiagnosticSummary -Title "7. 综合验证结论"
        if ($OutputPath) {
            Save-DiagnosticReport -Path $OutputPath
        }
        return
    }

    # 2. 端口可达性测试
    Check-Ports

    if ($Quick) {
        # 快速模式：只检查端口
        Show-DiagnosticSummary -Title "7. 综合验证结论"
        if ($OutputPath) {
            Save-DiagnosticReport -Path $OutputPath
        }
        return
    }

    # 3. MCP 协议初始化测试
    Check-McpInitialize

    # 4. MCP 工具列表获取
    Check-McpToolsList

    # 5. MCP 工具抽样调用
    Check-McpToolCalls

    # 6. CLine 配置验证
    if (-not $SkipConfig) {
        Check-ClineConfig
    }

    # 7. 综合验证结论
    Show-DiagnosticSummary -Title "7. 综合验证结论"

    if ($OutputPath) {
        Save-DiagnosticReport -Path $OutputPath
    }

    Write-Host "`n  完成时间: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Gray
    Write-Host ""
}

# ============================================================
# 启动
# ============================================================

Main
