<#
.SYNOPSIS
    MCP 服务通用操作模块 — 提供 MCP 端点检测、端口映射、服务列表查询

.DESCRIPTION
    从 Build/Test-MCPServices.ps1、Build/Fix-MCPServices.ps1、Build/Repair-MCPServices.ps1
    中提取的通用 MCP 操作函数。
    不依赖任何项目特定常量（容器名、端口号等由调用方传入）。

    依赖:
        Core\Network.psm1（提供 Test-TcpPort、Test-HttpEndpoint）
        DevOps\Docker.psm1（提供 Test-ContainerRunning、Get-ContainerState 等）

    包含函数:
        Test-McpEndpoint       — 测试单个 MCP 端点是否可用
        Get-MCPServicePortMap  — 从 JSON 配置中提取服务→端口映射
        Get-MCPServiceList     — 获取 MCP 服务列表（含状态检测）

    用法:
        Import-Module .\DevOps\MCP.psm1
        $result = Test-McpEndpoint -Port 3100 -Name "MCP 服务"
        $portMap = Get-MCPServicePortMap -ConfigPath "config/mcp-servers.json"
        $services = Get-MCPServiceList -ConfigPath "config/mcp-servers.json"
#>

# ============================================================
# MCP 端点检测
# ============================================================

function Test-McpEndpoint {
    <#
    .SYNOPSIS
        测试单个 MCP 端点是否可用
    .DESCRIPTION
        先检测 TCP 端口是否开放，再发送 MCP 初始化请求验证服务响应。
    .PARAMETER Port
        端口号
    .PARAMETER Name
        服务名称（用于输出显示）
    .PARAMETER HostName
        主机地址（默认 localhost）
    .PARAMETER TimeoutSeconds
        超时秒数（默认 5）
    .OUTPUTS
        [PSCustomObject] @{ Port; Name; TcpOk; McpOk; LatencyMs; Error }
    .EXAMPLE
        $result = Test-McpEndpoint -Port 3100 -Name "MCP 服务"
        if ($result.McpOk) { "MCP 服务正常" }
    #>
    param(
        [Parameter(Mandatory = $true)]
        [int]$Port,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [string]$HostName = "localhost",

        [int]$TimeoutSeconds = 5
    )

    $startTime = Get-Date
    $result = [PSCustomObject]@{
        Port      = $Port
        Name      = $Name
        TcpOk     = $false
        McpOk     = $false
        LatencyMs = 0
        Error     = ""
    }

    # TCP 端口检测
    try {
        $tcpClient = New-Object System.Net.Sockets.TcpClient
        $asyncResult = $tcpClient.BeginConnect($HostName, $Port, $null, $null)
        $waitResult = $asyncResult.AsyncWaitHandle.WaitOne([TimeSpan]::FromSeconds($TimeoutSeconds), $false)

        if ($waitResult -and $tcpClient.Connected) {
            $tcpClient.EndConnect($asyncResult)
            $tcpClient.Close()
            $result.TcpOk = $true
        } else {
            $tcpClient.Close()
            $result.Error = "TCP 端口未开放"
            $result.LatencyMs = [math]::Round(((Get-Date) - $startTime).TotalMilliseconds, 0)
            return $result
        }
    } catch {
        $result.Error = "TCP 检测异常: $_"
        $result.LatencyMs = [math]::Round(((Get-Date) - $startTime).TotalMilliseconds, 0)
        return $result
    }

    # MCP 初始化请求
    try {
        $body = '{"jsonrpc":"2.0","id":1,"method":"initialize"}'
        $webClient = New-Object System.Net.WebClient
        $webClient.Headers.Add("Content-Type", "application/json")
        $responseBytes = $webClient.UploadData("http://${HostName}:${Port}/mcp", "POST", [System.Text.Encoding]::UTF8.GetBytes($body))
        $responseText = [System.Text.Encoding]::UTF8.GetString($responseBytes)
        $webClient.Dispose()

        if ($responseText -match '"status":"accepted"') {
            $result.McpOk = $true
        } else {
            $result.Error = "MCP 响应异常: 未收到 accepted 状态"
        }
    } catch {
        $result.Error = "MCP 请求异常: $_"
    }

    $result.LatencyMs = [math]::Round(((Get-Date) - $startTime).TotalMilliseconds, 0)
    return $result
}

# ============================================================
# MCP 服务端口映射提取
# ============================================================

function Get-MCPServicePortMap {
    <#
    .SYNOPSIS
        从 JSON 配置中提取服务名称→端口号的映射
    .DESCRIPTION
        解析 MCP 服务器 JSON 配置文件，提取每个服务的名称和端口号。
        支持标准 MCP 配置格式（mcpServers 节点）。
    .PARAMETER ConfigPath
        JSON 配置文件路径
    .PARAMETER PortField
        端口字段名（默认 args 中查找 --port 参数）
    .OUTPUTS
        [hashtable] @{ ServiceName = PortNumber; ... }
    .EXAMPLE
        $portMap = Get-MCPServicePortMap -ConfigPath "DevEnvironment/Docker/config/mcp-servers.json"
        $portMap["mcp-service"]  # 返回端口号
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ConfigPath,

        [string]$PortField = "args"
    )

    $portMap = @{}

    if (-not (Test-Path $ConfigPath)) {
        Write-Warning "配置文件不存在: $ConfigPath"
        return $portMap
    }

    try {
        $config = Get-Content $ConfigPath -Raw | ConvertFrom-Json

        # 支持 mcpServers 节点
        $services = $config.mcpServers
        if (-not $services) {
            $services = $config
        }

        foreach ($service in $services.PSObject.Properties) {
            $name = $service.Name
            $args = $service.Value.args

            if ($args) {
                $portIndex = [array]::IndexOf($args, "--port")
                if ($portIndex -ge 0 -and $portIndex -lt $args.Count - 1) {
                    $port = $args[$portIndex + 1]
                    $portMap[$name] = [int]$port
                }
            }
        }
    } catch {
        Write-Warning "解析配置文件失败: $_"
    }

    return $portMap
}

# ============================================================
# MCP 服务列表（含状态检测）
# ============================================================

function Get-MCPServiceList {
    <#
    .SYNOPSIS
        获取 MCP 服务列表，包含每个服务的端口和状态检测结果
    .DESCRIPTION
        从 JSON 配置中读取 MCP 服务列表，对每个服务执行端口检测和 MCP 端点检测。
    .PARAMETER ConfigPath
        JSON 配置文件路径
    .PARAMETER TimeoutSeconds
        每个服务的检测超时秒数（默认 3）
    .OUTPUTS
        [PSCustomObject[]] @{ Name; Port; TcpOk; McpOk; LatencyMs; Error }
    .EXAMPLE
        $services = Get-MCPServiceList -ConfigPath "DevEnvironment/Docker/config/mcp-servers.json"
        $services | Where-Object { -not $_.McpOk } | ForEach-Object { "服务异常: $($_.Name)" }
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$ConfigPath,

        [int]$TimeoutSeconds = 3
    )

    $portMap = Get-MCPServicePortMap -ConfigPath $ConfigPath
    $results = @()

    foreach ($entry in $portMap.GetEnumerator()) {
        $name = $entry.Key
        $port = $entry.Value

        $testResult = Test-McpEndpoint -Port $port -Name $name -TimeoutSeconds $TimeoutSeconds
        $results += $testResult
    }

    return $results
}

# ============================================================
# 模块导出
# ============================================================

Export-ModuleMember -Function @(
    'Test-McpEndpoint',
    'Get-MCPServicePortMap',
    'Get-MCPServiceList'
)
