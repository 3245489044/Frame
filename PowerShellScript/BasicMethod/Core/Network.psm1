<#
.SYNOPSIS
    网络检测通用函数模块 — 提供 TCP 端口检测、HTTP 端点检测、端口连通性测试

.DESCRIPTION
    从 Build/Test-MCPServices.ps1 和 Build/Fix-MCPServices.ps1 中提取的通用网络检测函数。
    不依赖任何项目特定常量（端口号、URL 等由调用方传入）。

    包含函数:
        Test-TcpPort       — 测试 TCP 端口是否开放
        Test-HttpEndpoint  — 测试 HTTP 端点是否可达
        Test-PortPing      — 综合端口检测（TCP + HTTP 双重检测）

    用法:
        Import-Module .\Core\Network.psm1
        if (Test-TcpPort -Port 3100) { "端口开放" }
        if (Test-HttpEndpoint -Url "http://localhost:3100/mcp") { "端点可达" }
        $result = Test-PortPing -Port 3100 -Name "MCP 服务"
#>

# ============================================================
# TCP 端口检测
# ============================================================

function Test-TcpPort {
    <#
    .SYNOPSIS
        测试 TCP 端口是否开放
    .DESCRIPTION
        使用 .NET TcpClient 尝试连接指定端口，支持超时控制。
    .PARAMETER Port
        端口号
    .PARAMETER HostName
        主机地址（默认 localhost）
    .PARAMETER TimeoutSeconds
        超时秒数（默认 3）
    .OUTPUTS
        [bool] 端口开放返回 $true，否则 $false
    .EXAMPLE
        if (Test-TcpPort -Port 3100) { "端口 3100 开放" }
        if (Test-TcpPort -Port 80 -HostName "192.168.1.1" -TimeoutSeconds 5) { "远程端口开放" }
    #>
    param(
        [Parameter(Mandatory = $true)]
        [int]$Port,

        [string]$HostName = "localhost",

        [int]$TimeoutSeconds = 3
    )

    try {
        $tcpClient = New-Object System.Net.Sockets.TcpClient
        $asyncResult = $tcpClient.BeginConnect($HostName, $Port, $null, $null)
        $waitResult = $asyncResult.AsyncWaitHandle.WaitOne([TimeSpan]::FromSeconds($TimeoutSeconds), $false)

        if ($waitResult -and $tcpClient.Connected) {
            $tcpClient.EndConnect($asyncResult)
            $tcpClient.Close()
            return $true
        }

        $tcpClient.Close()
        return $false
    } catch {
        return $false
    }
}

# ============================================================
# HTTP 端点检测
# ============================================================

function Test-HttpEndpoint {
    <#
    .SYNOPSIS
        测试 HTTP 端点是否可达
    .DESCRIPTION
        向指定 URL 发送 HTTP 请求，检查响应状态码。
        支持自定义请求方法和预期状态码。
    .PARAMETER Url
        完整 URL（如 http://localhost:3100/mcp）
    .PARAMETER Method
        HTTP 方法（默认 GET，支持 GET/POST/HEAD）
    .PARAMETER ExpectedStatus
        预期状态码（默认 200）
    .PARAMETER TimeoutSeconds
        超时秒数（默认 5）
    .PARAMETER Body
        请求体（POST 方法时使用）
    .PARAMETER ContentType
        请求 Content-Type（默认 application/json）
    .OUTPUTS
        [bool] 端点可达且状态码匹配返回 $true，否则 $false
    .EXAMPLE
        if (Test-HttpEndpoint -Url "http://localhost:3100/mcp") { "MCP 端点可达" }
        if (Test-HttpEndpoint -Url "http://localhost:3100/mcp" -Method POST -Body '{"jsonrpc":"2.0","id":1,"method":"initialize"}') { "MCP 初始化成功" }
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url,

        [ValidateSet("GET", "POST", "HEAD")]
        [string]$Method = "GET",

        [int]$ExpectedStatus = 200,

        [int]$TimeoutSeconds = 5,

        [string]$Body = "",

        [string]$ContentType = "application/json"
    )

    try {
        $webClient = New-Object System.Net.WebClient
        $webClient.Headers.Add("Content-Type", $ContentType)

        if ($Method -eq "GET") {
            $responseText = $webClient.DownloadString($Url)
            $webClient.Dispose()
            return $true
        } elseif ($Method -eq "POST") {
            $responseBytes = $webClient.UploadData($Url, "POST", [System.Text.Encoding]::UTF8.GetBytes($Body))
            $responseText = [System.Text.Encoding]::UTF8.GetString($responseBytes)
            $webClient.Dispose()
            return $true
        } else {
            # HEAD 请求
            $webRequest = [System.Net.WebRequest]::Create($Url)
            $webRequest.Method = "HEAD"
            $webRequest.Timeout = $TimeoutSeconds * 1000
            $response = $webRequest.GetResponse()
            $statusCode = [int]$response.StatusCode
            $response.Close()
            return ($statusCode -eq $ExpectedStatus)
        }
    } catch {
        return $false
    }
}

# ============================================================
# 综合端口检测
# ============================================================

function Test-PortPing {
    <#
    .SYNOPSIS
        综合端口检测（TCP + HTTP 双重检测）
    .DESCRIPTION
        先检测 TCP 端口是否开放，再检测 HTTP 端点是否可达。
        返回详细的状态对象，包含 TCP 状态、HTTP 状态和检测耗时。
    .PARAMETER Port
        端口号
    .PARAMETER Name
        服务名称（用于输出显示）
    .PARAMETER HostName
        主机地址（默认 localhost）
    .PARAMETER HttpPath
        HTTP 检测路径（默认 /mcp）
    .PARAMETER TimeoutSeconds
        超时秒数（默认 3）
    .OUTPUTS
        [PSCustomObject] @{ Port; Name; TcpOk; HttpOk; LatencyMs; Error }
    .EXAMPLE
        $result = Test-PortPing -Port 3100 -Name "MCP 服务"
        if ($result.TcpOk) { "TCP 端口开放" }
        if ($result.HttpOk) { "HTTP 端点可达" }
    #>
    param(
        [Parameter(Mandatory = $true)]
        [int]$Port,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [string]$HostName = "localhost",

        [string]$HttpPath = "/mcp",

        [int]$TimeoutSeconds = 3
    )

    $startTime = Get-Date
    $result = [PSCustomObject]@{
        Port      = $Port
        Name      = $Name
        TcpOk     = $false
        HttpOk    = $false
        LatencyMs = 0
        Error     = ""
    }

    # TCP 检测
    try {
        $result.TcpOk = Test-TcpPort -Port $Port -HostName $HostName -TimeoutSeconds $TimeoutSeconds
    } catch {
        $result.Error = "TCP 检测异常: $_"
    }

    # HTTP 检测（仅 TCP 开放时）
    if ($result.TcpOk) {
        try {
            $url = "http://${HostName}:${Port}${HttpPath}"
            $result.HttpOk = Test-HttpEndpoint -Url $url -TimeoutSeconds $TimeoutSeconds
        } catch {
            $result.Error = "HTTP 检测异常: $_"
        }
    }

    $result.LatencyMs = [math]::Round(((Get-Date) - $startTime).TotalMilliseconds, 0)
    return $result
}

# ============================================================
# 模块导出
# ============================================================

Export-ModuleMember -Function @(
    'Test-TcpPort',
    'Test-HttpEndpoint',
    'Test-PortPing'
)
