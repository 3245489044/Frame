<#
.SYNOPSIS
    工具调用桥接通用模块 — 提供沙箱执行、CLI 命令、Git 状态等通用工具操作

.DESCRIPTION
    从 AIDecisionFramework/ToolBridge.ps1 提取的纯通用工具操作函数。
    不依赖任何项目特定常量或全局变量，完全自包含。

    包含函数:
        Invoke-CtxExecute       沙箱执行代码（JS/Python/Shell/TS/CS）
        Invoke-CtxExecuteFile   沙箱分析文件
        Invoke-CtxBatchExecute  批量执行命令
        Invoke-CliCommand       执行 CLI 命令
        Get-GitStatus           获取 Git 状态

    用法:
        Import-Module .\BasicMethod-ToolBridge.psm1
        Invoke-CtxExecute -Language "javascript" -Code "console.log('hello')"
        Invoke-CliCommand -Command "git status"
#>

<#
.SYNOPSIS
    在沙箱中执行代码（对应 ctx_execute）
.DESCRIPTION
    通过 PowerShell 调用 python/node 等运行时在沙箱中执行代码
.PARAMETER Language
    运行语言: "javascript" | "python" | "shell" | "typescript" | "csharp"
.PARAMETER Code
    要执行的代码
.PARAMETER Timeout
    超时时间（毫秒）
.EXAMPLE
    Invoke-CtxExecute -Language "javascript" -Code "console.log('hello')"
#>
function Invoke-CtxExecute {
    param(
        [Parameter(Mandatory)]
        [ValidateSet("javascript", "python", "shell", "typescript", "csharp")]
        [string]$Language,

        [Parameter(Mandatory)]
        [string]$Code,

        [int]$Timeout = 30000
    )

    $tempDir = Join-Path $env:TEMP "BasicMethod-ToolBridge"
    if (-not (Test-Path $tempDir)) { New-Item -Path $tempDir -ItemType Directory -Force | Out-Null }

    $result = $null
    $errorMsg = $null
    $exitCode = 0

    switch ($Language) {
        "javascript" {
            $tmpFile = Join-Path $tempDir "sandbox_$(Get-Random).js"
            Set-Content -Path $tmpFile -Value $Code -Encoding UTF8
            $result = & node $tmpFile 2>&1
            $exitCode = $LASTEXITCODE
            Remove-Item $tmpFile -Force -ErrorAction SilentlyContinue
        }
        "python" {
            $tmpFile = Join-Path $tempDir "sandbox_$(Get-Random).py"
            Set-Content -Path $tmpFile -Value $Code -Encoding UTF8
            $result = & python $tmpFile 2>&1
            $exitCode = $LASTEXITCODE
            Remove-Item $tmpFile -Force -ErrorAction SilentlyContinue
        }
        "shell" {
            $tmpFile = Join-Path $tempDir "sandbox_$(Get-Random).ps1"
            Set-Content -Path $tmpFile -Value $Code -Encoding UTF8
            $result = & powershell -File $tmpFile 2>&1
            $exitCode = $LASTEXITCODE
            Remove-Item $tmpFile -Force -ErrorAction SilentlyContinue
        }
        "typescript" {
            $tmpFile = Join-Path $tempDir "sandbox_$(Get-Random).ts"
            Set-Content -Path $tmpFile -Value $Code -Encoding UTF8
            $result = & npx --yes tsx $tmpFile 2>&1
            $exitCode = $LASTEXITCODE
            Remove-Item $tmpFile -Force -ErrorAction SilentlyContinue
        }
        "csharp" {
            $tmpFile = Join-Path $tempDir "sandbox_$(Get-Random).csx"
            Set-Content -Path $tmpFile -Value $Code -Encoding UTF8
            $result = & dotnet script $tmpFile 2>&1
            $exitCode = $LASTEXITCODE
            Remove-Item $tmpFile -Force -ErrorAction SilentlyContinue
        }
    }

    $outputStr = $result | Out-String

    return [PSCustomObject]@{
        Language = $Language
        Success = ($exitCode -eq 0)
        ExitCode = $exitCode
        Output = $outputStr.Trim()
        OutputLineCount = ($outputStr.Trim() -split "`r`n|`n").Count
    }
}

<#
.SYNOPSIS
    在沙箱中分析文件（对应 ctx_execute_file）
.DESCRIPTION
    读取文件内容并在沙箱中执行分析代码
.PARAMETER Path
    文件路径
.PARAMETER Language
    运行语言
.PARAMETER Code
    分析代码（使用 $FILE_CONTENT 变量引用文件内容）
.EXAMPLE
    Invoke-CtxExecuteFile -Path "large.log" -Language "javascript" -Code "const lines = FILE_CONTENT.split('\n'); console.log(lines.length + ' lines')"
#>
function Invoke-CtxExecuteFile {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [ValidateSet("javascript", "python", "shell")]
        [string]$Language,

        [Parameter(Mandatory)]
        [string]$Code
    )

    $resolvedPath = Resolve-Path $Path -ErrorAction Stop
    $fileContent = Get-Content -Path $resolvedPath -Raw

    # 将 FILE_CONTENT 注入到代码中
    $injectedCode = switch ($Language) {
        "javascript" {
            $escaped = $fileContent.Replace("\", "\\").Replace("'", "\'").Replace("`n", "\\n").Replace("`r", "\\r")
            "const FILE_CONTENT = `"$escaped`";`n$Code"
        }
        "python" {
            $escaped = $fileContent.Replace("\", "\\").Replace("'", "\'").Replace("`n", "\\n").Replace("`r", "\\r")
            "FILE_CONTENT = '''$escaped'''`n$Code"
        }
        "shell" {
            '$FILE_CONTENT_PATH = "' + $resolvedPath + '"' + "`n$Code"
        }
    }

    return Invoke-CtxExecute -Language $Language -Code $injectedCode
}

<#
.SYNOPSIS
    批量执行多个命令（对应 ctx_batch_execute）
.PARAMETER Commands
    命令数组，每个元素包含 Label 和 Command
.PARAMETER Concurrency
    并行数
.EXAMPLE
    $cmds = @(
        @{Label = "git status"; Command = "git status"},
        @{Label = "disk usage"; Command = "df -h"}
    )
    Invoke-CtxBatchExecute -Commands $cmds -Concurrency 2
#>
function Invoke-CtxBatchExecute {
    param(
        [Parameter(Mandatory)]
        [array]$Commands,

        [int]$Concurrency = 1
    )

    $results = @()

    if ($Concurrency -le 1) {
        foreach ($cmd in $Commands) {
            Write-Host "  [执行] $($cmd.Label)..." -ForegroundColor Gray
            $output = & powershell -NoProfile -Command $cmd.Command 2>&1 | Out-String
            $exitCode = $LASTEXITCODE
            $results += [PSCustomObject]@{
                Label = $cmd.Label
                Command = $cmd.Command
                Success = ($exitCode -eq 0)
                ExitCode = $exitCode
                Output = $output.Trim()
            }
        }
    }
    else {
        $jobs = @()
        foreach ($cmd in $Commands) {
            $jobs += Start-Job -ScriptBlock {
                param($c)
                $output = & powershell -NoProfile -Command $c 2>&1 | Out-String
                $exitCode = $LASTEXITCODE
                return @{ Label = $null; Command = $c; Output = $output; ExitCode = $exitCode }
            } -ArgumentList $cmd.Command
        }

        $jobResults = Receive-Job -Job $jobs -Wait -AutoRemoveJob
        for ($i = 0; $i -lt $Commands.Count; $i++) {
            $results += [PSCustomObject]@{
                Label = $Commands[$i].Label
                Command = $Commands[$i].Command
                Success = ($jobResults[$i].ExitCode -eq 0)
                ExitCode = $jobResults[$i].ExitCode
                Output = $jobResults[$i].Output.Trim()
            }
        }
    }

    return [PSCustomObject]@{
        TotalCommands = $Commands.Count
        Concurrency = $Concurrency
        Results = $results
    }
}

<#
.SYNOPSIS
    执行 CLI 命令
.PARAMETER Command
    要执行的命令
.PARAMETER Timeout
    超时时间（秒）
.EXAMPLE
    Invoke-CliCommand -Command "dotnet build CsharpPrj/MainProj.sln" -Timeout 120
#>
function Invoke-CliCommand {
    param(
        [Parameter(Mandatory)]
        [string]$Command,

        [int]$Timeout = 60
    )

    $result = & powershell -NoProfile -Command $Command 2>&1
    $outputStr = $result | Out-String

    return [PSCustomObject]@{
        Command = $Command
        Success = ($LASTEXITCODE -eq 0)
        ExitCode = $LASTEXITCODE
        Output = $outputStr.Trim()
    }
}

<#
.SYNOPSIS
    获取 Git 状态
.EXAMPLE
    Get-GitStatus
#>
function Get-GitStatus {
    $branch = & git rev-parse --abbrev-ref HEAD 2>&1
    $status = & git status --short 2>&1
    $log = & git log --oneline -5 2>&1

    return [PSCustomObject]@{
        Branch = $branch.Trim()
        HasChanges = ($status.Trim().Length -gt 0)
        Changes = ($status | Out-String).Trim()
        RecentCommits = ($log | Out-String).Trim()
    }
}

# ============================================
# MCP 协议测试函数
# ============================================

function Invoke-McpRequest {
    <#
    .SYNOPSIS
        发送 MCP JSON-RPC 请求
    .DESCRIPTION
        通过 Node.js SSE 向 MCP 服务发送 JSON-RPC 请求。
        支持 initialize、tools/list、tools/call 等标准 MCP 方法。
    .PARAMETER Endpoint
        MCP 服务端点 URL（如 http://localhost:3100/mcp）
    .PARAMETER Method
        JSON-RPC 方法名（如 "initialize"、"tools/list"、"tools/call"）
    .PARAMETER Params
        方法参数（可选，哈希表）
    .PARAMETER TimeoutSeconds
        超时秒数（默认 10）
    .OUTPUTS
        [hashtable] 包含 Success、Result、Error 的哈希表
    .EXAMPLE
        $result = Invoke-McpRequest -Endpoint "http://localhost:3100/mcp" -Method "initialize"
        $result = Invoke-McpRequest -Endpoint "http://localhost:3100/mcp" -Method "tools/call" -Params @{name="my-tool"; arguments=@{}}
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Endpoint,

        [Parameter(Mandatory = $true)]
        [string]$Method,

        [hashtable]$Params = @{},

        [int]$TimeoutSeconds = 10
    )

    $requestId = Get-Random -Minimum 1 -Maximum 99999

    # 构建 JSON-RPC 请求体
    $body = @{
        jsonrpc = "2.0"
        id = $requestId
        method = $Method
    }
    if ($Params.Count -gt 0) {
        $body.params = $Params
    }

    $jsonBody = $body | ConvertTo-Json -Depth 10

    try {
        $webClient = New-Object System.Net.WebClient
        $webClient.Headers.Add("Content-Type", "application/json")
        $webClient.Encoding = [System.Text.Encoding]::UTF8

        $responseBytes = $webClient.UploadData($Endpoint, "POST", [System.Text.Encoding]::UTF8.GetBytes($jsonBody))
        $responseText = [System.Text.Encoding]::UTF8.GetString($responseBytes)
        $webClient.Dispose()

        $responseObj = $responseText | ConvertFrom-Json

        if ($responseObj.error) {
            return @{
                Success = $false
                Result = $null
                Error = $responseObj.error.message
                ErrorCode = $responseObj.error.code
                RequestId = $requestId
            }
        }

        return @{
            Success = $true
            Result = $responseObj.result
            Error = $null
            ErrorCode = $null
            RequestId = $requestId
        }
    } catch {
        return @{
            Success = $false
            Result = $null
            Error = $_.Exception.Message
            ErrorCode = -1
            RequestId = $requestId
        }
    }
}

function Test-McpServiceInitialize {
    <#
    .SYNOPSIS
        测试 MCP 服务初始化
    .DESCRIPTION
        向 MCP 服务发送 initialize 请求，验证服务是否正常响应。
    .PARAMETER Endpoint
        MCP 服务端点 URL
    .OUTPUTS
        [hashtable] 包含 Success、ServerInfo、ProtocolVersion 的哈希表
    .EXAMPLE
        $result = Test-McpServiceInitialize -Endpoint "http://localhost:3100/mcp"
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Endpoint
    )

    $response = Invoke-McpRequest -Endpoint $Endpoint -Method "initialize"

    if (-not $response.Success) {
        return @{
            Success = $false
            ServerInfo = $null
            ProtocolVersion = $null
            Error = $response.Error
        }
    }

    $result = $response.Result
    return @{
        Success = $true
        ServerInfo = $result.serverInfo
        ProtocolVersion = $result.protocolVersion
        Capabilities = $result.capabilities
        Error = $null
    }
}

function Test-McpServiceToolsList {
    <#
    .SYNOPSIS
        获取 MCP 服务工具列表
    .DESCRIPTION
        向 MCP 服务发送 tools/list 请求，获取可用工具列表。
    .PARAMETER Endpoint
        MCP 服务端点 URL
    .OUTPUTS
        [hashtable] 包含 Success、Tools（数组）、ToolCount 的哈希表
    .EXAMPLE
        $result = Test-McpServiceToolsList -Endpoint "http://localhost:3100/mcp"
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Endpoint
    )

    $response = Invoke-McpRequest -Endpoint $Endpoint -Method "tools/list"

    if (-not $response.Success) {
        return @{
            Success = $false
            Tools = @()
            ToolCount = 0
            Error = $response.Error
        }
    }

    $tools = $response.Result.tools
    $toolCount = if ($tools -is [array]) { $tools.Count } else { 0 }

    return @{
        Success = $true
        Tools = $tools
        ToolCount = $toolCount
        Error = $null
    }
}

function Test-McpServiceToolCall {
    <#
    .SYNOPSIS
        调用 MCP 服务工具
    .DESCRIPTION
        向 MCP 服务发送 tools/call 请求，调用指定工具。
    .PARAMETER Endpoint
        MCP 服务端点 URL
    .PARAMETER ToolName
        工具名称
    .PARAMETER Arguments
        工具参数（哈希表，可选）
    .OUTPUTS
        [hashtable] 包含 Success、Result、Error 的哈希表
    .EXAMPLE
        $result = Test-McpServiceToolCall -Endpoint "http://localhost:3100/mcp" -ToolName "my-tool" -Arguments @{key="value"}
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Endpoint,

        [Parameter(Mandatory = $true)]
        [string]$ToolName,

        [hashtable]$Arguments = @{}
    )

    $params = @{
        name = $ToolName
        arguments = $Arguments
    }

    return Invoke-McpRequest -Endpoint $Endpoint -Method "tools/call" -Params $params
}

Export-ModuleMember -Function @(
    "Invoke-CtxExecute",
    "Invoke-CtxExecuteFile",
    "Invoke-CtxBatchExecute",
    "Invoke-CliCommand",
    "Get-GitStatus",
    "Invoke-McpRequest",
    "Test-McpServiceInitialize",
    "Test-McpServiceToolsList",
    "Test-McpServiceToolCall"
)
