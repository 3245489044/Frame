<#
.SYNOPSIS
    Docker 公共模块 — 共享常量、辅助函数、MCP 服务清单
.DESCRIPTION
    提供所有 Docker 相关模块和脚本共享的：
    - 常量（容器名、路径、端口范围）— 通过 DockerConfig.json 配置驱动
    - 辅助函数（Write-Header, Write-Info, Test-CommandExists 等）— 委托 BasicMethod
    - MCP 服务注册清单（单一数据源）— 通过 mcp-servers-config.json 加载
    - 路径解析（ProjectRoot 初始化）
    - 诊断框架（New-DiagnosticContext, Show-DiagnosticSummary 等）— 委托 BasicMethod
    - MCP 协议测试（Invoke-McpRequest 等）
    - 容器文件操作（Copy-FileToContainer, Invoke-ContainerScript 等）
.NOTES
    模块名: DockerCommon
    用途: 消除 Modules/Docker/ 和 Tools/Docker/ 之间的代码重复
    依赖: BasicMethod-Output, BasicMethod-Docker, BasicMethod-Json, BasicMethod-Diagnostic
#>

# ============================================================
# 导入 BasicMethod 模块（提供 Write-Header, Write-Success, Test-CommandExists 等）
# ============================================================

$Script:BasicMethodLoaded = $false

function Initialize-BasicMethod {
    <#
    .SYNOPSIS
        加载 BasicMethod 模块
    .DESCRIPTION
        从 BasicMethod 目录加载所有基础模块。
        使用缓存避免重复加载。
    #>
    if ($Script:BasicMethodLoaded) { return }

    $moduleDir = Split-Path -Parent $PSScriptRoot
    $projectRoot = Resolve-Path (Join-Path $moduleDir "..\..")
    $basicMethodDir = Join-Path $projectRoot "BasicMethod"

    $modules = @(
        @{ Name = "Output"; Path = "Core\Output.psm1" }
        @{ Name = "Docker"; Path = "DevOps\Docker.psm1" }
        @{ Name = "Json"; Path = "Core\Json.psm1" }
        @{ Name = "Path"; Path = "Core\Path.psm1" }
        @{ Name = "Utils"; Path = "Core\Utils.psm1" }
        @{ Name = "Diagnostic"; Path = "Tooling\Diagnostic.psm1" }
        @{ Name = "FileDiff"; Path = "FileOps\FileDiff.psm1" }
    )

    foreach ($mod in $modules) {
        $modPath = Join-Path $basicMethodDir $mod.Path
        if (Test-Path $modPath) {
            Import-Module $modPath -Force -DisableNameChecking -ErrorAction SilentlyContinue
        }
    }

    $Script:BasicMethodLoaded = $true
}

# ============================================================
# 配置加载（优先环境变量 → 配置文件 → 函数参数默认值）
# ============================================================

$Script:ConfigLoaded = $false
$Script:Config = $null

function Convert-PsCustomObjectToHashtable {
    <#
    .SYNOPSIS
        将 PSCustomObject 转换为哈希表（内建版本，避免循环依赖）
    .DESCRIPTION
        必须在 Initialize-DockerConfig 之前定义，因为后者会调用此函数。
    #>
    param($InputObject)

    if ($InputObject -is [System.Collections.IEnumerable] -and $InputObject -isnot [string]) {
        $result = @()
        foreach ($item in $InputObject) { $result += Convert-PsCustomObjectToHashtable $item }
        return $result
    }

    if ($InputObject -is [System.Management.Automation.PSCustomObject]) {
        $hash = @{}
        $InputObject.PSObject.Properties | ForEach-Object {
            $value = $_.Value
            if ($value -is [System.Management.Automation.PSCustomObject]) {
                $hash[$_.Name] = Convert-PsCustomObjectToHashtable $value
            } elseif ($value -is [System.Collections.IEnumerable] -and $value -isnot [string]) {
                $arr = @()
                foreach ($v in $value) {
                    $arr += if ($v -is [System.Management.Automation.PSCustomObject]) { Convert-PsCustomObjectToHashtable $v } else { $v }
                }
                $hash[$_.Name] = $arr
            } else {
                $hash[$_.Name] = $value
            }
        }
        return $hash
    }
    return $InputObject
}

function Initialize-DockerConfig {
    <#
    .SYNOPSIS
        加载 DockerConfig.json 配置文件
    .DESCRIPTION
        从 DockerConfig.json 加载配置，支持环境变量覆盖。
        所有默认值仅存在于 DockerConfig.json 中，代码不包含硬编码默认值。
    .PARAMETER Force
        强制重新加载配置（忽略缓存）
    #>
    param(
        [switch]$Force
    )

    if ($Script:ConfigLoaded -and -not $Force) { return }

    # 确保 BasicMethod 已加载
    Initialize-BasicMethod

    $configPath = Join-Path $PSScriptRoot "DockerConfig.json"

    if (Test-Path $configPath) {
        try {
            $raw = Get-Content $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
            $Script:Config = Convert-PsCustomObjectToHashtable $raw
            $Script:ConfigLoaded = $true
            return
        } catch {
            Write-Host "  ⚠️  配置文件解析失败: $_" -ForegroundColor Yellow
        }
    }

    Write-Host "  ⚠️  配置文件不存在: $configPath，部分功能可能不可用" -ForegroundColor Yellow
    $Script:Config = @{}
    $Script:ConfigLoaded = $true
}

function Get-ConfigValue {
    <#
    .SYNOPSIS
        获取配置值（支持环境变量覆盖）
    .DESCRIPTION
        优先返回环境变量 WF_DOCKER_<KEY> 的值，其次返回配置文件值。
    .PARAMETER Section
        配置节名称（如 "container", "docker", "cline", "mcp", "timeout"）
    .PARAMETER Key
        配置键名
    .PARAMETER Default
        默认值（当配置和环境变量都不存在时使用）
    .EXAMPLE
        Get-ConfigValue "container" "name" "workflow-mcp-env"
        Get-ConfigValue "mcp" "portStart" 3100
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Section,
        [Parameter(Mandatory = $true)]
        [string]$Key,
        $Default = $null
    )

    Initialize-DockerConfig

    # 1. 环境变量覆盖（优先级最高）
    $envKey = "WF_DOCKER_$($Section)_$($Key)".ToUpper()
    $envVal = [Environment]::GetEnvironmentVariable($envKey)
    if ($null -ne $envVal -and $envVal -ne "") {
        return $envVal
    }

    # 2. 配置文件值
    if ($Script:Config -and $Script:Config.ContainsKey($Section)) {
        $section = $Script:Config[$Section]
        if ($section -is [hashtable] -and $section.ContainsKey($Key)) {
            $value = $section[$Key]
            # 展开 $env: 变量（如 $env:LOCALAPPDATA\Docker → C:\Users\...\AppData\Local\Docker）
            if ($value -is [string] -and $value -match '\$env:(\w+)') {
                $matches = [regex]::Matches($value, '\$env:(\w+)')
                foreach ($match in $matches) {
                    $envVar = $match.Groups[1].Value
                    $envVal = [Environment]::GetEnvironmentVariable($envVar)
                    if ($envVal) {
                        $value = $value -replace [regex]::Escape($match.Value), $envVal
                    }
                }
            }
            return $value
        }
    }

    # 3. 默认值
    return $Default
}

# ============================================================
# 模块初始化
# ============================================================

$Script:ProjectRoot = $null

function Initialize-DockerCommon {
    <#
    .SYNOPSIS
        初始化公共模块路径
    #>
    if (-not $Script:ProjectRoot) {
        $moduleDir = Split-Path -Parent $PSScriptRoot
        $Script:ProjectRoot = Resolve-Path (Join-Path $moduleDir "..\..")
    }
    Initialize-DockerConfig
}

# ============================================================
# 注意：所有配置值通过 Get-ConfigValue 直接获取，不再提供 Get-* 包装函数。
# 调用方应直接使用 Get-ConfigValue "section" "key" 获取配置值。
# 例如: Get-ConfigValue "container" "name"  代替 Get-ContainerName
#       Get-ConfigValue "cline" "configPath" 代替 Get-ClineConfigPath
# ============================================================

# ============================================================
# MCP 服务注册清单（单一数据源 — 从配置文件加载）
# ============================================================

$Script:DefaultMcpServers = $null

function Get-McpServers {
    <#
    .SYNOPSIS
        获取 MCP 服务清单（单一数据源）
    .DESCRIPTION
        从 mcp-servers-config.json 加载 MCP 服务清单。
        所有模块和脚本应通过此函数获取 MCP 服务信息。
        如果配置文件不存在或解析失败，返回空数组。
    #>
    if ($Script:DefaultMcpServers) {
        return $Script:DefaultMcpServers
    }

    Initialize-DockerCommon

    # 从配置文件加载
    $configFile = Get-ConfigValue "mcp" "serversConfigFile"

    $configPath = Join-Path $Script:ProjectRoot $configFile

    if (Test-Path $configPath) {
        try {
            $raw = Get-Content $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($raw.mcpServers -and $raw.mcpServers.Count -gt 0) {
                $servers = Convert-PsCustomObjectToHashtable $raw.mcpServers
                # 转换为模块内部格式（添加 Port 字段）
                $Script:DefaultMcpServers = @()
                $port = [int](Get-ConfigValue "mcp" "portStart")
                foreach ($s in $servers) {
                    $Script:DefaultMcpServers += @{
                        Name        = $s.name
                        Port        = $port
                        Type        = $s.type
                        Package     = $s.package
                        Global      = if ($s.global -eq $true) { $true } else { $false }
                        Description = if ($s.description) { $s.description } else { "" }
                    }
                    $port++
                }
                return $Script:DefaultMcpServers
            }
        } catch {
            Write-Host "  ⚠️  MCP 服务配置文件解析失败: $_" -ForegroundColor Yellow
        }
    }

    Write-Host "  ⚠️  MCP 服务配置文件不存在或为空: $configPath" -ForegroundColor Yellow
    $Script:DefaultMcpServers = @()
    return $Script:DefaultMcpServers
}

# ============================================================
# 路径解析函数
# ============================================================

function Get-ProjectRoot {
    <#
    .SYNOPSIS
        获取项目根路径
    #>
    Initialize-DockerCommon
    return $Script:ProjectRoot
}

function Resolve-ProjectPath {
    <#
    .SYNOPSIS
        解析相对于项目根目录的路径
    .DESCRIPTION
        自动处理容器路径（/workspace/WorkFlowPrj/...）到宿主机路径的转换。
        如果传入的路径已经是宿主机绝对路径，则直接返回。
        委托 Convert-ContainerPathToHost 处理路径转换逻辑。
    .PARAMETER RelativePath
        相对于项目根目录的路径，或容器路径，或宿主机绝对路径
    .EXAMPLE
        Resolve-ProjectPath "DevEnvironment\Docker\config\mcp-gateway-config.json"
        Resolve-ProjectPath "/workspace/WorkFlowPrj/DevEnvironment/Docker/config/mcp-gateway-config.json"
    #>
    param([string]$RelativePath)

    Initialize-DockerCommon

    # 委托 Convert-ContainerPathToHost 处理所有路径转换逻辑
    return Convert-ContainerPathToHost -Path $RelativePath
}

# ============================================================
# MCP 协议测试框架
# ============================================================

function Invoke-McpRequest {
    <#
    .SYNOPSIS
        向 MCP 服务发送请求并获取 SSE 响应
    .DESCRIPTION
        通过 Node.js 建立 SSE 连接，向 MCP 服务发送 JSON-RPC 请求并获取真实响应。
        适用于测试 MCP 服务的 initialize、tools/list、tools/call 等方法。
    .PARAMETER Port
        MCP 服务端口
    .PARAMETER Method
        JSON-RPC 方法名（如 "initialize"、"tools/list"、"tools/call"）
    .PARAMETER Params
        方法参数哈希表
    .PARAMETER TimeoutSeconds
        超时秒数（默认 10）
    .EXAMPLE
        Invoke-McpRequest -Port 3100 -Method "initialize"
    .EXAMPLE
        Invoke-McpRequest -Port 3104 -Method "tools/call" -Params @{ name = "read_file"; arguments = @{ path = "/workspace/README.md" } }
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [int]$Port,

        [Parameter(Mandatory = $true)]
        [string]$Method,

        [hashtable]$Params = @{},

        [int]$TimeoutSeconds = 10
    )

    $body = @{
        jsonrpc = "2.0"
        id      = 1
        method  = $Method
    }
    if ($Params.Count -gt 0) {
        $body.params = $Params
    }

    $jsonBody = $body | ConvertTo-Json -Compress -Depth 5

    # 对 JSON 字符串做完整的 JS 字符串转义（反斜杠、单引号、换行、回车、制表符）
    $escapedBody = $jsonBody `
        -replace '\\', '\\' `
        -replace "'", "\'" `
        -replace "`n", '\n' `
        -replace "`r", '\r' `
        -replace "`t", '\t'

    # 生成 Node.js 脚本（使用 .mjs 扩展名 + import 语法支持顶层 await）
    $nodeScript = @"
import http from 'http';
import { EventEmitter } from 'events';

const PORT = $Port;
const REQUEST_BODY = '$escapedBody';
const TIMEOUT = $($TimeoutSeconds) * 1000;

// 创建 SSE 连接
const sseEmitter = new EventEmitter();
let responseData = null;

const sseReq = http.get('http://localhost:' + PORT + '/mcp', (res) => {
    let buffer = '';
    res.on('data', (chunk) => {
        buffer += chunk.toString();
        // 解析 SSE 数据行
        const lines = buffer.split('\n');
        buffer = lines.pop() || '';
        for (const line of lines) {
            if (line.startsWith('data: ')) {
                const data = line.substring(6);
                try {
                    const parsed = JSON.parse(data);
                    responseData = parsed;
                    sseEmitter.emit('response', parsed);
                } catch (e) {
                    // 不是 JSON，忽略
                }
            }
        }
    });
    res.on('error', (err) => {
        sseEmitter.emit('error', err.message);
    });
});

sseReq.on('error', (err) => {
    sseEmitter.emit('error', err.message);
});

// 等待 SSE 连接建立后发送请求
setTimeout(() => {
    const postData = JSON.stringify(JSON.parse(REQUEST_BODY));
    const postReq = http.request({
        hostname: 'localhost',
        port: PORT,
        path: '/mcp',
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'Content-Length': Buffer.byteLength(postData)
        }
    }, (res) => {
        // POST 返回 202 Accepted，忽略
    });
    postReq.on('error', (err) => {
        sseEmitter.emit('error', err.message);
    });
    postReq.write(postData);
    postReq.end();
}, 500);

// 等待响应或超时
const result = { success: false, data: null, error: null };
const timeout = setTimeout(() => {
    if (!result.success) {
        result.error = 'Timeout';
        sseEmitter.emit('done');
    }
}, TIMEOUT);

sseEmitter.on('response', (data) => {
    result.success = true;
    result.data = data;
    clearTimeout(timeout);
    sseEmitter.emit('done');
});

sseEmitter.on('error', (err) => {
    result.error = err;
    clearTimeout(timeout);
    sseEmitter.emit('done');
});

// 等待完成（顶层 await 仅在 ESM 模块中有效）
await new Promise((resolve) => sseEmitter.once('done', resolve));

// 关闭 SSE 连接
sseReq.destroy();

// 输出结果
console.log('---MCP-RESULT-START---');
console.log(JSON.stringify(result));
console.log('---MCP-RESULT-END---');
"@

    $tempFile = $null
    try {
        $tempFile = [System.IO.Path]::GetTempFileName() + ".mjs"
        $nodeScript | Out-File -FilePath $tempFile -Encoding UTF8 -Force
        $output = node $tempFile 2>&1

        # 解析输出
        $startMarker = "---MCP-RESULT-START---"
        $endMarker = "---MCP-RESULT-END---"
        $startIdx = $output.IndexOf($startMarker)
        $endIdx = $output.IndexOf($endMarker)

        if ($startIdx -ge 0 -and $endIdx -gt $startIdx) {
            $jsonStr = $output.Substring($startIdx + $startMarker.Length, $endIdx - $startIdx - $startMarker.Length).Trim()
            $parsed = $jsonStr | ConvertFrom-Json
            if ($parsed.success) {
                return @{ Success = $true; Data = $parsed.data }
            } else {
                return @{ Success = $false; Error = $parsed.error }
            }
        } else {
            return @{ Success = $false; Error = "无法解析 Node.js 输出: $output" }
        }
    } catch {
        return @{ Success = $false; Error = "Node.js 执行异常: $_" }
    } finally {
        if ($tempFile -and (Test-Path $tempFile)) {
            Remove-Item $tempFile -Force -ErrorAction SilentlyContinue
        }
    }
}

function Test-McpServiceInitialize {
    <#
    .SYNOPSIS
        测试 MCP 服务初始化
    .PARAMETER Port
        MCP 服务端口
    .PARAMETER Name
        服务名称（用于输出）
    .PARAMETER TimeoutSeconds
        超时秒数（默认 15）
    .EXAMPLE
        Test-McpServiceInitialize -Port 3100 -Name "sequentialthinking"
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [int]$Port,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [int]$TimeoutSeconds = 15
    )

    Write-Host "  ⏳ 正在测试 $Name (端口 $Port)..." -ForegroundColor Gray

    $result = Invoke-McpRequest -Port $Port -Method "initialize" -Params @{
        protocolVersion = "2024-11-05"
        capabilities    = @{}
        clientInfo      = @{
            name    = "mcp-validator"
            version = "1.0.0"
        }
    } -TimeoutSeconds $TimeoutSeconds

    if ($result.Success) {
        $data = $result.Data
        $serverName = if ($data.serverInfo.name) { $data.serverInfo.name } else { "未知" }
        $serverVersion = if ($data.serverInfo.version) { "v$($data.serverInfo.version)" } else { "" }
        $protocolVer = if ($data.protocolVersion) { $data.protocolVersion } else { "未知" }
        Write-Host "  ✅ [PASS] 初始化测试 / $Name`: 服务: $serverName $serverVersion, 协议: $protocolVer" -ForegroundColor Green
        return $true
    } else {
        Write-Host "  ❌ [FAIL] 初始化测试 / $Name`: 初始化失败: $($result.Error)" -ForegroundColor Red
        return $false
    }
}

function Test-McpServiceToolsList {
    <#
    .SYNOPSIS
        获取 MCP 服务工具列表
    .PARAMETER Port
        MCP 服务端口
    .PARAMETER Name
        服务名称（用于输出）
    .PARAMETER TimeoutSeconds
        超时秒数（默认 15）
    .EXAMPLE
        Test-McpServiceToolsList -Port 3100 -Name "sequentialthinking"
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [int]$Port,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [int]$TimeoutSeconds = 15
    )

    Write-Host "  ⏳ 正在获取 $Name 的工具列表..." -ForegroundColor Gray

    $result = Invoke-McpRequest -Port $Port -Method "tools/list" -TimeoutSeconds $TimeoutSeconds

    if ($result.Success) {
        $data = $result.Data
        $tools = $data.tools
        $toolCount = if ($tools) { $tools.Count } else { 0 }
        if ($toolCount -gt 0) {
            $toolNames = ($tools | ForEach-Object { $_.name }) -join ", "
            Write-Host "  ✅ [PASS] 工具列表 / $Name`: $toolCount 个工具: $toolNames" -ForegroundColor Green
        } else {
            Write-Host "  ⚠️  [WARN] 工具列表 / $Name`: 0 个工具" -ForegroundColor Yellow
        }
        return $true
    } else {
        Write-Host "  ⚠️  [WARN] 工具列表 / $Name`: 获取失败: $($result.Error)" -ForegroundColor Yellow
        return $false
    }
}

function Test-McpServiceToolCall {
    <#
    .SYNOPSIS
        调用 MCP 服务工具
    .PARAMETER Port
        MCP 服务端口
    .PARAMETER Name
        服务名称（用于输出）
    .PARAMETER ToolName
        工具名称
    .PARAMETER Arguments
        工具参数字典
    .PARAMETER TimeoutSeconds
        超时秒数（默认 15）
    .EXAMPLE
        Test-McpServiceToolCall -Port 3104 -Name "filesystem" -ToolName "read_file" -Arguments @{ path = "/workspace/README.md" }
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [int]$Port,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$ToolName,

        [hashtable]$Arguments = @{},

        [int]$TimeoutSeconds = 15
    )

    Write-Host "  ⏳ 正在测试 $Name 的 $ToolName..." -ForegroundColor Gray

    $result = Invoke-McpRequest -Port $Port -Method "tools/call" -Params @{
        name      = $ToolName
        arguments = $Arguments
    } -TimeoutSeconds $TimeoutSeconds

    if ($result.Success) {
        Write-Host "  ✅ [PASS] 抽样测试 / $Name/$ToolName`: 调用成功" -ForegroundColor Green
        return $result.Data
    } else {
        Write-Host "  ⚠️  [WARN] 抽样测试 / $Name/$ToolName`: 调用失败: $($result.Error)" -ForegroundColor Yellow
        return $null
    }
}

# ============================================================
# 容器文件操作函数
# ============================================================

# ============================================================
# 兼容性包装函数（提供旧版 Get-* 函数，内部委托 Get-ConfigValue）
# 这些函数是为了向后兼容，新代码应直接使用 Get-ConfigValue
# ============================================================

function Get-ContainerName {
    <#
    .SYNOPSIS
        获取容器名称（兼容旧版 API）
    #>
    return Get-ConfigValue "container" "name"
}

function Get-ComposeFile {
    <#
    .SYNOPSIS
        获取 docker-compose.yml 路径（兼容旧版 API）
    #>
    return Get-ConfigValue "container" "composeFile"
}

function Get-PortStart {
    <#
    .SYNOPSIS
        获取 MCP 端口起始值（兼容旧版 API）
    #>
    return [int](Get-ConfigValue "mcp" "portStart")
}

function Get-PortEnd {
    <#
    .SYNOPSIS
        获取 MCP 端口结束值（兼容旧版 API）
    #>
    return [int](Get-ConfigValue "mcp" "portEnd")
}

function Get-ClineConfigPath {
    <#
    .SYNOPSIS
        获取 CLine 配置文件路径（兼容旧版 API）
    #>
    return Get-ConfigValue "cline" "configPath"
}

function Get-BackupDir {
    <#
    .SYNOPSIS
        获取 CLine 配置备份目录（兼容旧版 API）
    #>
    return Get-ConfigValue "cline" "backupDir"
}

function Get-DockerDataDir {
    <#
    .SYNOPSIS
        获取 Docker 数据目录（兼容旧版 API）
    #>
    return Get-ConfigValue "docker" "dataDir"
}

function Get-DockerLogDir {
    <#
    .SYNOPSIS
        获取 Docker 日志目录（兼容旧版 API）
    #>
    return Get-ConfigValue "docker" "logDir"
}

function Get-DockerConfigDir {
    <#
    .SYNOPSIS
        获取 Docker 配置目录（兼容旧版 API）
    #>
    return Get-ConfigValue "docker" "configDir"
}

function Get-DockerServiceName {
    <#
    .SYNOPSIS
        获取 Docker 服务名称（兼容旧版 API）
    #>
    return Get-ConfigValue "docker" "serviceName"
}

function Get-ImageName {
    <#
    .SYNOPSIS
        获取镜像名称（兼容旧版 API）
    #>
    return Get-ConfigValue "container" "imageName"
}

# ============================================================
# Docker 容器状态查询函数
# ============================================================

<#

.SYNOPSIS
    导入 DockerBuild 模块（供 Build/ 目录下的脚本使用）
.DESCRIPTION
    封装模块路径解析和加载逻辑，消除 4 个脚本中的重复代码。
    自动检测模块路径并加载，失败时输出错误并退出。
.PARAMETER Force
    强制重新加载模块
.EXAMPLE
    Import-DockerBuildModule
    Import-DockerBuildModule -Force
#>
function Import-DockerBuildModule {
    [CmdletBinding()]
    param([switch]$Force)

    $modulePath = Join-Path $PSScriptRoot "DockerBuild.psm1"
    if (Test-Path $modulePath) {
        $params = @{
            Force = $true
            DisableNameChecking = $true
            ErrorAction = "Stop"
        }
        if ($Force) { $params.Force = $true }
        Import-Module $modulePath @params
        return $true
    } else {
        Write-Host "❌ 无法加载 DockerBuild 模块: $modulePath" -ForegroundColor Red
        exit 1
    }
}

# ============================================================
# 模块导出
# ============================================================

# 导出所有公共函数
Export-ModuleMember -Function @(
    # 配置
    'Get-ConfigValue',
    # MCP 服务
    'Get-McpServers',
    # 路径解析
    'Get-ProjectRoot',
    'Resolve-ProjectPath',
    # MCP 协议测试
    'Invoke-McpRequest',
    'Test-McpServiceInitialize',
    'Test-McpServiceToolsList',
    'Test-McpServiceToolCall',
    # 兼容性包装
    'Get-ContainerName',
    'Get-ComposeFile',
    'Get-PortStart',
    'Get-PortEnd',
    'Get-ClineConfigPath',
    'Get-BackupDir',
    'Get-DockerDataDir',
    'Get-DockerLogDir',
    'Get-DockerConfigDir',
    'Get-DockerServiceName',
    'Get-ImageName',
    # 模块加载
    'Import-DockerBuildModule'
)


