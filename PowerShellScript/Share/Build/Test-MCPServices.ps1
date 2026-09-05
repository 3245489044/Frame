<#
.SYNOPSIS
    通用 MCP 服务校验脚本 — 检测 Docker 容器内 MCP 服务的运行状态

.DESCRIPTION
    数据驱动：从 mcp-servers.json 配置文件读取服务清单，无需硬编码。
    三层检测：
        L1 - 容器层：Docker 容器是否运行
        L2 - 端口层：各 MCP 端口是否监听（/ping 端点）
        L3 - 协议层：MCP HTTP/SSE 端点是否正常响应（/mcp 端点）
    输出彩色表格 + 汇总统计。

    用法:
        .\Test-MCPServices.ps1                       默认检测
        .\Test-MCPServices.ps1 -Quick                 快速模式（仅 L1+L2）
        .\Test-MCPServices.ps1 -ContainerName "my-mcp" 指定容器
        .\Test-MCPServices.ps1 -ConfigPath "D:\path\to\mcp-servers.json" 指定配置
        .\Test-MCPServices.ps1 -TargetHost "192.168.1.100"  远程主机检测
        .\Test-MCPServices.ps1 -Continuous            持续监控模式（每 10 秒刷新）
        .\Test-MCPServices.ps1 -ExportJson            导出结果为 JSON

        # 通过 PScriptManager 调用
        Invoke-Function -Name "Test-MCPServices" -Arguments @{Quick=$true}

.NOTES
    依赖: BasicMethod/Core/Output.psm1（输出函数）
           BasicMethod/Core/Utils.psm1（工具函数）
           BasicMethod/DevOps/Docker.psm1（Docker 操作函数）
    默认容器: workflow-mcp-env
    默认配置: DevEnvironment/Docker/config/mcp-servers.json
    默认端口范围: 3100-3108
#>
# @version: 1.1.0
# @author: WorkFlowPrj
# @category: 运维
# @exports: Test-MCPServices
# @requires: Write-Header, Write-Step, Write-Success, Write-Info, Write-Warn, Write-Error
# @requires: Test-CommandExists, Confirm-Action
# @requires: Test-ContainerRunning, Get-ContainerState, Get-ContainerUptime, Test-PortHttpReachable

# ============================================================
# 参数定义
# ============================================================
param(
    # 容器名称
    [string]$ContainerName = "workflow-mcp-env",

    # MCP 服务配置文件路径
    [string]$ConfigPath = "",

    # 目标主机（默认 localhost，支持远程检测）
    [string]$TargetHost = "localhost",

    # 超时秒数
    [int]$TimeoutSec = 5,

    # 快速模式（仅检测 L1 容器 + L2 端口，不检测 L3 协议端点）
    [switch]$Quick,

    # 持续监控模式（每 N 秒刷新）
    [switch]$Continuous,

    # 持续监控间隔（秒）
    [int]$Interval = 10,

    # 导出结果为 JSON
    [switch]$ExportJson,

    # JSON 导出路径
    [string]$ExportPath = "",

    # 跳过 Docker 检测（直接检测端口）
    [switch]$SkipDocker,

    # 详细输出
    [switch]$Detailed,

    # 显示配置文件路径映射（默认开启）
    [switch]$ShowConfig = $true,

    # MCPBridge 诊断模式（新增 L4 工具列表响应时间 + L5 编译/进程检查）
    [switch]$BridgeDiagnostic
)

# ============================================================
# 模块加载
# ============================================================

$scriptDir = $PSScriptRoot
if (-not $scriptDir) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
}
$projectRoot = Resolve-Path (Join-Path $scriptDir "..\..")
$basicMethodDir = Join-Path $projectRoot "PowerShellScript\BasicMethod"

# 加载 BasicMethod 核心模块
$coreModules = @(
    "Core\Output.psm1"
    "Core\Utils.psm1"
    "DevOps\Docker.psm1"
)
foreach ($mod in $coreModules) {
    $modPath = Join-Path $basicMethodDir $mod
    if (Test-Path $modPath) {
        try {
            Import-Module $modPath -Force -DisableNameChecking -ErrorAction Stop
        } catch {
            try { . $modPath } catch { }
        }
    }
}

# ============================================================
# 默认配置路径
# ============================================================

if (-not $ConfigPath) {
    $ConfigPath = Join-Path $projectRoot "DevEnvironment\Docker\config\mcp-servers.json"
}

# ============================================================
# 辅助函数
# ============================================================

function Get-ServiceList {
    <#
    .SYNOPSIS
        从配置文件读取 MCP 服务清单
    .OUTPUTS
        [array] 服务对象数组
    #>
    if (-not (Test-Path $ConfigPath)) {
        Write-Warn "配置文件不存在: $ConfigPath"
        Write-Info "使用默认服务列表（端口 3100-3108）"

        return @(
            @{ name = "mcp-sequentialthinking-tools"; port = 3100; description = "顺序推理工具" }
            @{ name = "context7-mcp";                 port = 3101; description = "Context7 文档查询" }
            @{ name = "context-mode";                 port = 3102; description = "上下文模式" }
            @{ name = "memory";                       port = 3103; description = "知识图谱记忆" }
            @{ name = "filesystem";                   port = 3104; description = "文件系统操作" }
            @{ name = "lexis-mcp";                    port = 3105; description = "代码搜索与分析" }
            @{ name = "token-savior-recall";          port = 3106; description = "Token 节省与召回" }
            @{ name = "csproj-context";               port = 3107; description = "C# 项目上下文" }
            @{ name = "fetch-mcp";                    port = 3108; description = "通用网页抓取" }
        )
    }

    try {
        $config = Get-Content $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $services = @()
        $entrypoint = if ($config.entrypoint) { $config.entrypoint } else { "" }

        foreach ($server in $config.servers) {
            $services += @{
                name        = $server.name
                port        = $server.port
                description = $server.description
                command     = $server.command
                args        = $server.args -join " "
                configFile  = if ($server.configFile) { $server.configFile } else { $entrypoint }
                configLine  = if ($server.configLine) { $server.configLine } else { 0 }
            }
        }

        if ($services.Count -eq 0) {
            Write-Warn "配置文件中没有服务定义"
            return @()
        }

        return $services
    } catch {
        Write-Error "读取配置文件失败: $_"
        return @()
    }
}

function Test-PortPing {
    <#
    .SYNOPSIS
        检测端口是否可通过 HTTP /ping 端点访问
    .PARAMETER TargetHost
        目标主机
    .PARAMETER Port
        端口号
    .PARAMETER TimeoutSec
        超时秒数
    .OUTPUTS
        [bool] 可达返回 $true
    #>
    param(
        [string]$TargetHost,
        [int]$Port,
        [int]$TimeoutSec = 5
    )

    try {
        $url = "http://${TargetHost}:${Port}/ping"
        $response = Invoke-WebRequest -Uri $url -Method GET -TimeoutSec $TimeoutSec -UseBasicParsing -ErrorAction Stop
        return ($response.StatusCode -eq 200)
    } catch {
        return $false
    }
}

function Test-McpEndpoint {
    <#
    .SYNOPSIS
        检测 MCP HTTP/SSE 端点是否正常响应
    .PARAMETER TargetHost
        目标主机
    .PARAMETER Port
        端口号
    .PARAMETER TimeoutSec
        超时秒数
    .OUTPUTS
        [hashtable] @{ Ok = [bool]; Response = [string]; Error = [string] }
    #>
    param(
        [string]$TargetHost,
        [int]$Port,
        [int]$TimeoutSec = 5
    )

    $result = @{ Ok = $false; Response = ""; Error = "" }

    try {
        $url = "http://${TargetHost}:${Port}/mcp"

        # 发送 JSON-RPC initialize 请求
        $body = '{"jsonrpc":"2.0","id":1,"method":"initialize"}'
        $response = Invoke-WebRequest -Uri $url -Method POST `
            -ContentType "application/json" `
            -Body $body `
            -TimeoutSec $TimeoutSec `
            -UseBasicParsing `
            -ErrorAction Stop

        if ($response.StatusCode -eq 200 -or $response.StatusCode -eq 202) {
            $result.Ok = $true
            $result.Response = $response.Content.Substring(0, [Math]::Min($response.Content.Length, 200))
        } elseif ($response.StatusCode -eq 405) {
            $result.Ok = $true
            $result.Response = "HTTP 405 (Method Not Allowed - 可能只支持 GET)"
        } else {
            $result.Response = "HTTP $($response.StatusCode)"
        }
    } catch {
        $result.Error = $_.Exception.Message
    }

    return $result
}

function Test-PortTcp {
    <#
    .SYNOPSIS
        检测 TCP 端口是否开放（使用 .NET TcpClient）
    .PARAMETER TargetHost
        目标主机
    .PARAMETER Port
        端口号
    .PARAMETER TimeoutMs
        超时毫秒数
    .OUTPUTS
        [bool] 开放返回 $true
    #>
    param(
        [string]$TargetHost,
        [int]$Port,
        [int]$TimeoutMs = 3000
    )

    try {
        $tcp = New-Object System.Net.Sockets.TcpClient
        $async = $tcp.BeginConnect($TargetHost, $Port, $null, $null)
        $wait = $async.AsyncWaitHandle.WaitOne($TimeoutMs, $false)
        if ($wait) {
            $tcp.EndConnect($async) | Out-Null
            $tcp.Close()
            return $true
        }
        $tcp.Close()
        return $false
    } catch {
        return $false
    }
}

function Get-StatusIcon {
    <#
    .SYNOPSIS
        根据状态返回对应的图标和颜色
    .PARAMETER Status
        状态值: ok / fail / warn / skip / unknown
    .OUTPUTS
        [hashtable] @{ Icon; Color }
    #>
    param([string]$Status)

    $iconMap = @{
        "ok"      = @{ Icon = "✅"; Color = "Green" }
        "fail"    = @{ Icon = "❌"; Color = "Red" }
        "warn"    = @{ Icon = "⚠️"; Color = "Yellow" }
        "skip"    = @{ Icon = "⏭️"; Color = "Gray" }
        "unknown" = @{ Icon = "❓"; Color = "Gray" }
    }

    if ($iconMap.ContainsKey($Status)) {
        return $iconMap[$Status]
    }
    return $iconMap["unknown"]
}

function Write-ResultTable {
    <#
    .SYNOPSIS
        输出检测结果表格
    .PARAMETER Results
        检测结果数组
    .PARAMETER ShowConfig
        是否显示配置文件路径列
    #>
    param(
        [array]$Results,
        [bool]$ShowConfig = $true
    )

    # 表头
    Write-Host ""
    if ($ShowConfig) {
        Write-Host ("{0,-30} {1,6} {2,8} {3,8} {4,8} {5,8}  {6,-40}" -f "服务名称", "端口", "容器", "Ping", "MCP端点", "状态", "配置文件") -ForegroundColor Cyan
        Write-Host ("{0,-30} {1,6} {2,8} {3,8} {4,8} {5,8}  {6,-40}" -f ("─" * 28), ("─" * 4), ("─" * 6), ("─" * 6), ("─" * 6), ("─" * 6), ("─" * 38)) -ForegroundColor Gray
    } else {
        Write-Host ("{0,-30} {1,6} {2,8} {3,8} {4,8} {5,8}" -f "服务名称", "端口", "容器", "Ping", "MCP端点", "状态") -ForegroundColor Cyan
        Write-Host ("{0,-30} {1,6} {2,8} {3,8} {4,8} {5,8}" -f ("─" * 28), ("─" * 4), ("─" * 6), ("─" * 6), ("─" * 6), ("─" * 6)) -ForegroundColor Gray
    }

    $okCount = 0
    $failCount = 0
    $warnCount = 0
    $skipCount = 0

    foreach ($r in $Results) {
        $containerIcon = Get-StatusIcon $r.ContainerStatus
        $pingIcon = Get-StatusIcon $r.PingStatus
        $mcpIcon = Get-StatusIcon $r.McpStatus

        # 综合状态
        $overall = "ok"
        if ($r.ContainerStatus -eq "fail" -or $r.PingStatus -eq "fail") {
            $overall = "fail"
        } elseif ($r.McpStatus -eq "fail") {
            $overall = "warn"
        } elseif ($r.ContainerStatus -eq "skip") {
            $overall = "skip"
        }

        $overallIcon = Get-StatusIcon $overall

        # 配置文件路径显示
        $configLabel = ""
        if ($ShowConfig -and $r.ConfigFile) {
            $configLabel = if ($r.ConfigLine -gt 0) {
                "$($r.ConfigFile) L$($r.ConfigLine)"
            } else {
                $r.ConfigFile
            }
        }

        if ($ShowConfig) {
            Write-Host ("{0,-30} {1,6} {2,8} {3,8} {4,8} {5,8}  {6,-40}" -f
                $r.Name,
                $r.Port,
                $containerIcon.Icon,
                $pingIcon.Icon,
                $mcpIcon.Icon,
                $overallIcon.Icon,
                $configLabel) -ForegroundColor $overallIcon.Color
        } else {
            Write-Host ("{0,-30} {1,6} {2,8} {3,8} {4,8} {5,8}" -f
                $r.Name,
                $r.Port,
                $containerIcon.Icon,
                $pingIcon.Icon,
                $mcpIcon.Icon,
                $overallIcon.Icon) -ForegroundColor $overallIcon.Color
        }

        switch ($overall) {
            "ok"   { $okCount++ }
            "fail" { $failCount++ }
            "warn" { $warnCount++ }
            "skip" { $skipCount++ }
        }
    }

    # 分隔线
    if ($ShowConfig) {
        Write-Host ("{0,-30} {1,6} {2,8} {3,8} {4,8} {5,8}  {6,-40}" -f ("─" * 28), ("─" * 4), ("─" * 6), ("─" * 6), ("─" * 6), ("─" * 6), ("─" * 38)) -ForegroundColor Gray
    } else {
        Write-Host ("{0,-30} {1,6} {2,8} {3,8} {4,8} {5,8}" -f ("─" * 28), ("─" * 4), ("─" * 6), ("─" * 6), ("─" * 6), ("─" * 6)) -ForegroundColor Gray
    }

    # 汇总
    $total = $Results.Count
    Write-Host ""
    Write-Host "  总计: $total  |  ✅ 正常: $okCount  |  ❌ 失败: $failCount  |  ⚠️ 警告: $warnCount  |  ⏭️ 跳过: $skipCount" -ForegroundColor White

    # 如果有失败，显示详细信息
    if ($failCount -gt 0) {
        Write-Host ""
        Write-Host "  ❌ 失败服务详情:" -ForegroundColor Red
        foreach ($r in $Results) {
            $overall = "ok"
            if ($r.ContainerStatus -eq "fail" -or $r.PingStatus -eq "fail") { $overall = "fail" }
            elseif ($r.McpStatus -eq "fail") { $overall = "warn" }
            elseif ($r.ContainerStatus -eq "skip") { $overall = "skip" }

            if ($overall -eq "fail") {
                Write-Host "    $($r.Name) (端口 $($r.Port)):" -ForegroundColor Yellow
                if ($r.ContainerError) { Write-Host "      容器: $($r.ContainerError)" -ForegroundColor Gray }
                if ($r.PingError)      { Write-Host "      Ping: $($r.PingError)" -ForegroundColor Gray }
                if ($r.McpError)       { Write-Host "      MCP:  $($r.McpError)" -ForegroundColor Gray }
            }
        }
    }

    return @{ Total = $total; Ok = $okCount; Fail = $failCount; Warn = $warnCount; Skip = $skipCount }
}

function Get-ContainerInfo {
    <#
    .SYNOPSIS
        获取容器详细信息
    .PARAMETER ContainerName
        容器名称
    .OUTPUTS
        [hashtable] 容器信息
    #>
    param([string]$ContainerName)

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

# ============================================================
# 主检测函数
# ============================================================

function Test-MCPServices {
    <#
    .SYNOPSIS
        通用 MCP 服务校验 — 检测 Docker 容器内 MCP 服务的运行状态
    .PARAMETER ContainerName
        容器名称（默认：workflow-mcp-env）
    .PARAMETER ConfigPath
        MCP 服务配置文件路径
    .PARAMETER TargetHost
        目标主机（默认 localhost）
    .PARAMETER TimeoutSec
        超时秒数（默认 5）
    .PARAMETER Quick
        快速模式（仅 L1+L2）
    .PARAMETER Continuous
        持续监控模式
    .PARAMETER Interval
        持续监控间隔（秒，默认 10）
    .PARAMETER ExportJson
        导出结果为 JSON
    .PARAMETER ExportPath
        JSON 导出路径
    .PARAMETER SkipDocker
        跳过 Docker 检测
    .PARAMETER Detailed
        详细输出
    .EXAMPLE
        Test-MCPServices
        Test-MCPServices -Quick
        Test-MCPServices -ContainerName "my-mcp" -TargetHost "192.168.1.100"
        Test-MCPServices -Continuous -Interval 5
        Test-MCPServices -ExportJson -ExportPath "D:\report.json"
    #>
    [CmdletBinding()]
    param(
        [string]$ContainerName = "workflow-mcp-env",
        [string]$ConfigPath = "",
        [string]$TargetHost = "localhost",
        [int]$TimeoutSec = 5,
        [switch]$Quick,
        [switch]$Continuous,
        [int]$Interval = 10,
        [switch]$ExportJson,
        [string]$ExportPath = "",
        [switch]$SkipDocker,
        [switch]$Detailed,
        [switch]$BridgeDiagnostic
    )

    # ── 解析配置路径 ──
    if (-not $ConfigPath) {
        $scriptDir = $PSScriptRoot
        if (-not $scriptDir) { $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path }
        $projectRoot = Resolve-Path (Join-Path $scriptDir "..\..")
        $ConfigPath = Join-Path $projectRoot "DevEnvironment\Docker\config\mcp-servers.json"
    }

    # ── 获取服务列表 ──
    $services = Get-ServiceList
    if ($services.Count -eq 0) {
        Write-Error "没有可检测的服务"
        return $null
    }

    # ── 持续监控模式 ──
    if ($Continuous) {
        Write-Info "持续监控模式（每 ${Interval} 秒刷新一次，按 Ctrl+C 退出）"
        Write-Host ""

        while ($true) {
            Clear-Host

            Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
            Write-Host "  MCP 服务监控 | $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') | 容器: $ContainerName" -ForegroundColor Cyan
            Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan

            $results = Invoke-Detection -Services $services -ContainerName $ContainerName `
                -TargetHost $TargetHost -TimeoutSec $TimeoutSec -Quick:$Quick -SkipDocker:$SkipDocker

            $stats = Write-ResultTable -Results $results -ShowConfig $ShowConfig

            Start-Sleep -Seconds $Interval
        }

        return
    }

    # ── 单次检测模式 ──
    Write-Header "Test-MCPServices — MCP 服务通用校验"
    Write-Host "  容器: $ContainerName" -ForegroundColor Gray
    Write-Host "  主机: $TargetHost" -ForegroundColor Gray
    Write-Host "  配置: $ConfigPath" -ForegroundColor Gray
    Write-Host "  模式: $(if ($Quick) { '快速 (L1+L2)' } else { '完整 (L1+L2+L3)' })" -ForegroundColor Gray
    Write-Host "  服务: $($services.Count) 个" -ForegroundColor Gray
    Write-Host ""

    $results = Invoke-Detection -Services $services -ContainerName $ContainerName `
        -TargetHost $TargetHost -TimeoutSec $TimeoutSec -Quick:$Quick -SkipDocker:$SkipDocker

    $stats = Write-ResultTable -Results $results -ShowConfig $ShowConfig

    # ── 导出 JSON ──
    if ($ExportJson) {
        $exportData = @{
            Timestamp    = (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
            Container    = $ContainerName
            Host         = $TargetHost
            ConfigPath   = $ConfigPath
            Quick        = $Quick.IsPresent
            Services     = $results
            Summary      = $stats
        }

        if (-not $ExportPath) {
            $ExportPath = Join-Path $projectRoot "PowerShellScript\Build\mcp-test-report.json"
        }

        try {
            $exportData | ConvertTo-Json -Depth 3 | Set-Content $ExportPath -Encoding UTF8
            Write-Success "检测报告已导出: $ExportPath"
        } catch {
            Write-Error "导出 JSON 失败: $_"
        }
    }

    # ── L4/L5: MCPBridge 诊断 ──
    if ($BridgeDiagnostic) {
        Invoke-BridgeDiagnostic -Services $services -TargetHost $TargetHost -TimeoutSec $TimeoutSec
    }

    # ── 返回结果 ──
    $allOk = ($stats.Fail -eq 0)
    return @{ AllOk = $allOk; Results = $results; Summary = $stats }
}

function Invoke-Detection {
    <#
    .SYNOPSIS
        执行检测逻辑
    .PARAMETER Services
        服务列表
    .PARAMETER ContainerName
        容器名称
    .PARAMETER TargetHost
        目标主机
    .PARAMETER TimeoutSec
        超时秒数
    .PARAMETER Quick
        快速模式
    .PARAMETER SkipDocker
        跳过 Docker 检测
    .OUTPUTS
        [array] 检测结果数组
    #>
    param(
        [array]$Services,
        [string]$ContainerName,
        [string]$TargetHost,
        [int]$TimeoutSec,
        [switch]$Quick,
        [switch]$SkipDocker
    )

    $results = @()

    # ── L1: 容器检测 ──
    $containerRunning = $false
    $containerInfo = $null

    if (-not $SkipDocker) {
        $containerInfo = Get-ContainerInfo -ContainerName $ContainerName
        $containerRunning = $containerInfo.Running
    } else {
        $containerRunning = $true
    }

    # ── 逐个检测服务 ──
    foreach ($svc in $Services) {
        $port = $svc.port
        $result = @{
            Name            = $svc.name
            Port            = $port
            Description     = $svc.description
            ContainerStatus = "unknown"
            ContainerError  = ""
            PingStatus      = "unknown"
            PingError       = ""
            McpStatus       = "unknown"
            McpError        = ""
            McpResponse     = ""
            ConfigFile      = if ($svc.ContainsKey("configFile")) { $svc.configFile } else { "" }
            ConfigLine      = if ($svc.ContainsKey("configLine")) { $svc.configLine } else { 0 }
        }

        # L1: 容器状态
        if ($SkipDocker) {
            $result.ContainerStatus = "skip"
        } elseif ($containerRunning) {
            $result.ContainerStatus = "ok"
        } else {
            $result.ContainerStatus = "fail"
            $result.ContainerError = if ($containerInfo) { $containerInfo.Error } else { "容器未运行" }
        }

        # L2: 端口 Ping 检测
        $pingOk = Test-PortPing -TargetHost $TargetHost -Port $port -TimeoutSec $TimeoutSec
        if ($pingOk) {
            $result.PingStatus = "ok"
        } else {
            $tcpOk = Test-PortTcp -TargetHost $TargetHost -Port $port -TimeoutMs ($TimeoutSec * 1000)
            if ($tcpOk) {
                $result.PingStatus = "warn"
                $result.PingError = "TCP 端口开放但 /ping 无响应"
            } else {
                $result.PingStatus = "fail"
                $result.PingError = "端口不可达"
            }
        }

        # L3: MCP 协议端点检测（非快速模式）
        if (-not $Quick) {
            if ($result.PingStatus -eq "ok" -or $result.PingStatus -eq "warn") {
                $mcpResult = Test-McpEndpoint -TargetHost $TargetHost -Port $port -TimeoutSec $TimeoutSec
                if ($mcpResult.Ok) {
                    $result.McpStatus = "ok"
                    $result.McpResponse = $mcpResult.Response
                } else {
                    $result.McpStatus = "fail"
                    $result.McpError = $mcpResult.Error
                }
            } else {
                $result.McpStatus = "skip"
                $result.McpError = "端口不可达，跳过 MCP 端点检测"
            }
        } else {
            $result.McpStatus = "skip"
        }

        $results += $result
    }

    return $results
}

# ============================================================
# MCPBridge 诊断函数（L4: tools/list 响应时间 + L5: 编译/进程）
# ============================================================

function Invoke-BridgeDiagnostic {
    <#
    .SYNOPSIS
        MCPBridge 专项诊断 — 检测 tools/list 响应时间、编译状态、进程状态
    .DESCRIPTION
        L4: 对每个 MCP 服务器发送 tools/list 请求，记录响应耗时，识别超时服务器
        L5: 检查 MCPBridge TypeScript 编译状态和进程运行状态
    .PARAMETER Services
        服务列表
    .PARAMETER TargetHost
        目标主机
    .PARAMETER TimeoutSec
        超时秒数（默认 15，因为 tools/list 可能较慢）
    #>
    param(
        [array]$Services,
        [string]$TargetHost = "localhost",
        [int]$TimeoutSec = 15
    )

    Write-Host ""
    Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "  MCPBridge 诊断 | $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host ""

    # ── L5a: TypeScript 编译检查 ──
    Write-Host "  ── L5a: TypeScript 编译状态 ──" -ForegroundColor Yellow
    $bridgeRoot = Join-Path $projectRoot "TypeScriptPrj\TsWorkspace\MCPBridge"
    $tsconfigPath = Join-Path $bridgeRoot "tsconfig.json"

    if (Test-Path $tsconfigPath) {
        Write-Host "    ⏳ 检查 TypeScript 编译..." -ForegroundColor Gray
        Push-Location $bridgeRoot
        # 优先使用本地安装的 typescript，否则用 npx
        $tscCmd = if (Test-Path "node_modules\.bin\tsc.cmd") { "node_modules\.bin\tsc.cmd" } else { "npx.cmd" }
        $tscArgs = if ($tscCmd -eq "npx.cmd") { @("--yes", "typescript", "tsc", "-p", $tsconfigPath, "--noEmit") } else { @("-p", $tsconfigPath, "--noEmit") }
        $tscResult = & $tscCmd @tscArgs "2>&1"
        $tscExitCode = $LASTEXITCODE
        Pop-Location
        if ($tscExitCode -eq 0) {
            Write-Host "    ✅ TypeScript 编译通过（零错误）" -ForegroundColor Green
        } else {
            Write-Host "    ❌ TypeScript 编译有错误:" -ForegroundColor Red
            $tscResult | ForEach-Object { Write-Host "       $_" -ForegroundColor Red }
        }
    } else {
        Write-Host "    ⚠️  tsconfig.json 不存在: $tsconfigPath" -ForegroundColor Yellow
    }
    Write-Host ""

    # ── L5b: MCPBridge 进程检查 ──
    Write-Host "  ── L5b: MCPBridge 进程状态 ──" -ForegroundColor Yellow
    $bridgeProcesses = Get-Process -Name "node" -ErrorAction SilentlyContinue | Where-Object {
        $_.CommandLine -match "MCPBridge" -or $_.CommandLine -match "mcp-bridge"
    }
    if ($bridgeProcesses) {
        foreach ($proc in $bridgeProcesses) {
            Write-Host "    ✅ MCPBridge 进程运行中 (PID: $($proc.Id))" -ForegroundColor Green
        }
    } else {
        # 尝试找 build/index.js 相关的 node 进程
        $nodeProcesses = Get-Process -Name "node" -ErrorAction SilentlyContinue
        if ($nodeProcesses) {
            Write-Host "    ⚠️  Node.js 进程存在，但未明确匹配 MCPBridge" -ForegroundColor Yellow
            Write-Host "       PID: $($nodeProcesses.Id -join ', ')" -ForegroundColor Gray
        } else {
            Write-Host "    ❌ MCPBridge 进程未运行" -ForegroundColor Red
        }
    }
    Write-Host ""

    # ── L4: tools/list 响应时间检测 ──
    Write-Host "  ── L4: tools/list 响应时间 ──" -ForegroundColor Yellow
    Write-Host ""

    # 表头
    Write-Host ("    {0,-30} {1,8} {2,10} {3}" -f "服务器", "工具数", "响应时间", "状态") -ForegroundColor Cyan
    Write-Host ("    {0,-30} {1,8} {2,10} {3}" -f ("─" * 28), ("─" * 6), ("─" * 8), ("─" * 20)) -ForegroundColor Gray

    $timeoutCount = 0
    $successCount = 0
    $toolCounts = @()

    foreach ($svc in $Services) {
        $port = $svc.port
        $name = $svc.name
        $url = "http://${TargetHost}:${port}/mcp"

        Write-Host "    ⏳ $name (端口 $port)..." -NoNewline -ForegroundColor Gray

        # 发送 tools/list JSON-RPC 请求（MCP 服务器要求 Accept: application/json, text/event-stream）
        $body = '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
        $sw = [System.Diagnostics.Stopwatch]::StartNew()

        try {
            $response = Invoke-WebRequest -Uri $url -Method POST `
                -ContentType "application/json" `
                -Headers @{ "Accept" = "application/json, text/event-stream" } `
                -Body $body `
                -TimeoutSec $TimeoutSec `
                -UseBasicParsing `
                -ErrorAction Stop

            $sw.Stop()
            $elapsed = $sw.Elapsed.TotalSeconds

            if ($response.StatusCode -eq 200) {
                try {
                    # MCP 服务器返回 SSE 格式: event: message\ndata: {...}\n\n
                    $content = $response.Content
                    $toolCount = 0

                    # 从 SSE 响应中提取 data: 行
                    if ($content -match '(?m)^data:\s*(.+)$') {
                        $dataJson = $Matches[1]
                        $json = $dataJson | ConvertFrom-Json
                        if ($json.result -and $json.result.tools) {
                            $toolCount = $json.result.tools.Count
                        } elseif ($json.tools) {
                            $toolCount = $json.tools.Count
                        }
                    } else {
                        # 尝试直接解析为 JSON
                        $json = $content | ConvertFrom-Json
                        if ($json.result -and $json.result.tools) {
                            $toolCount = $json.result.tools.Count
                        } elseif ($json.tools) {
                            $toolCount = $json.tools.Count
                        }
                    }
                    $successCount++

                    # 判断响应时间
                    if ($elapsed -le 5) {
                        Write-Host " \b✅  $toolCount 个工具  ${elapsed:N1}s" -ForegroundColor Green
                    } elseif ($elapsed -le 15) {
                        Write-Host " \b⚠️  $toolCount 个工具  ${elapsed:N1}s" -ForegroundColor Yellow
                    } else {
                        Write-Host " \b❌  $toolCount 个工具  ${elapsed:N1}s (超时)" -ForegroundColor Red
                        $timeoutCount++
                    }

                    $toolCounts += @{ Name = $name; Port = $port; Tools = $toolCount; Time = $elapsed }
                } catch {
                    Write-Host " \b⚠️  响应解析失败: $($response.Content.Substring(0, [Math]::Min($response.Content.Length, 80)))" -ForegroundColor Yellow
                }
            } else {
                Write-Host " \b⚠️  HTTP $($response.StatusCode)" -ForegroundColor Yellow
            }
        } catch {
            $sw.Stop()
            $elapsed = $sw.Elapsed.TotalSeconds
            Write-Host " \b❌  超时/失败 (${elapsed:N1}s): $($_.Exception.Message)" -ForegroundColor Red
            $timeoutCount++
            $toolCounts += @{ Name = $name; Port = $port; Tools = -1; Time = $elapsed; Error = $_.Exception.Message }
        }
    }

    # ── 汇总 ──
    Write-Host ""
    Write-Host "  ── 诊断汇总 ──" -ForegroundColor Yellow
    $total = $Services.Count
    Write-Host "    总计: $total 个服务器" -ForegroundColor White
    Write-Host "    ✅ tools/list 成功: $successCount" -ForegroundColor Green
    Write-Host "    ❌ 超时/失败: $timeoutCount" -ForegroundColor Red

    if ($timeoutCount -gt 0) {
        Write-Host ""
        Write-Host "  ⚠️  问题服务器:" -ForegroundColor Yellow
        foreach ($tc in $toolCounts) {
            if ($tc.Tools -eq -1) {
                Write-Host "    ❌ $($tc.Name) (端口 $($tc.Port)): $($tc.Error)" -ForegroundColor Red
            } elseif ($tc.Time -gt 15) {
                Write-Host "    ⚠️  $($tc.Name) (端口 $($tc.Port)): 响应 ${tc.Time:N1}s（建议增加超时配置）" -ForegroundColor Yellow
            }
        }

        Write-Host ""
        Write-Host "  💡 建议:" -ForegroundColor Cyan
        Write-Host "    1. 检查超时服务器的容器内进程是否正常运行" -ForegroundColor Gray
        Write-Host "    2. 在 mcp-bridge.config.json 中增加对应服务器的 timeout 值" -ForegroundColor Gray
        Write-Host "    3. 或重启 Docker 容器: docker restart workflow-mcp-env" -ForegroundColor Gray
    }

    if ($successCount -eq $total) {
        Write-Host ""
        Write-Host "  ✅ 所有 MCP 服务器 tools/list 响应正常" -ForegroundColor Green
    }

    Write-Host ""
    Write-Host "  ── 完成 ──" -ForegroundColor Gray
}

# ============================================================
# 自动执行（当直接运行脚本时）
# ============================================================

$isInvokedByPScriptManager = $MyInvocation.Line -match 'Invoke-Function.*Test-MCPServices'

if (-not $isInvokedByPScriptManager) {
    Test-MCPServices -ContainerName $ContainerName `
        -ConfigPath $ConfigPath `
        -TargetHost $TargetHost `
        -TimeoutSec $TimeoutSec `
        -Quick:$Quick `
        -Continuous:$Continuous `
        -Interval $Interval `
        -ExportJson:$ExportJson `
        -ExportPath $ExportPath `
        -SkipDocker:$SkipDocker `
        -Detailed:$Detailed `
        -BridgeDiagnostic:$BridgeDiagnostic
}
