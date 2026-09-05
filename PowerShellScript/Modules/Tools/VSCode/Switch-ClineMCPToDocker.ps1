<#
.SYNOPSIS
    CLine MCP 配置切换工具 — 在本地 stdio 与 Docker stdio 模式之间切换
.DESCRIPTION
    自动修改 cline_mcp_settings.json，将 MCP 服务器从本地 stdio 模式切换到
    Docker stdio 模式（通过 docker exec -i 在容器内运行），或反向切换。

    支持两种模式：
    - docker : 所有 MCP 服务器通过 docker exec -i 在容器内以 stdio 模式运行
    - stdio  : 所有 MCP 服务器在本地直接启动子进程（原始模式）

.PARAMETER Mode
    目标模式：docker 或 stdio（默认 docker）
.PARAMETER Force
    跳过所有确认提示
.PARAMETER WhatIf
    仅预览操作，不实际执行
.PARAMETER DockerComposePath
    docker-compose.yml 的路径，默认自动检测项目中的路径
.PARAMETER ContainerName
    Docker 容器名称，默认 "workflow-mcp-env"

.EXAMPLE
    .\Switch-ClineMCPToDocker.ps1
    交互式切换到 Docker stdio 模式

.EXAMPLE
    .\Switch-ClineMCPToDocker.ps1 -Mode docker -Force
    自动切换到 Docker stdio 模式，跳过确认

.EXAMPLE
    .\Switch-ClineMCPToDocker.ps1 -Mode stdio
    切换回本地 stdio 模式

.EXAMPLE
    .\Switch-ClineMCPToDocker.ps1 -WhatIf
    预览切换到 Docker 模式的操作，不实际执行
#>

param(
    [ValidateSet("docker", "stdio")]
    [string]$Mode = "docker",

    [switch]$Force,

    [switch]$WhatIf,

    [string]$DockerComposePath = "",

    [string]$ContainerName = "workflow-mcp-env"
)

# ============================================================
# 常量
# ============================================================

$ScriptName = "Switch-ClineMCPToDocker.ps1"
$ClineSettingsPath = Join-Path $env:APPDATA "Code\User\globalStorage\saoudrizwan.claude-dev\settings\cline_mcp_settings.json"
$BackupPath = "$ClineSettingsPath.bak"

# MCP 服务器 → 端口映射（用于 http 模式）
# 端口从 PortRange 参数解析
$ParsedPorts = @{}
$PortStart, $PortEnd = $PortRange -split ":" | ForEach-Object { [int]$_ }

# MCP 服务器命令映射表（用于 docker→stdio 回退时还原 command/args）
# 键是 MCP 服务器名称，值是命令配置
$McpCommandMap = @{
    "mcp-sequentialthinking-tools" = @{
        command = "npx"
        args    = @("-y", "mcp-sequentialthinking-tools")
        env     = @{ "MAX_HISTORY_SIZE" = "1000" }
    }
    "github.com/upstash/context7-mcp" = @{
        command = "node"
        args    = @()
        env     = @{}
    }
    "context-mode" = @{
        command = "node"
        args    = @()
        env     = @{}
    }
    "github.com/modelcontextprotocol/servers/tree/main/src/memory" = @{
        command = "node"
        args    = @()
        env     = @{}
    }
    "github.com/modelcontextprotocol/servers/tree/main/src/filesystem" = @{
        command = "node"
        args    = @()
        env     = @{}
    }
    "lexis-mcp" = @{
        command = "node"
        args    = @()
        env     = @{}
    }
    "token-savior-recall" = @{
        command = "token-savior"
        args    = @()
        env     = @{
            "WORKSPACE_ROOTS"    = "d:\Project\Program\WorkFlowPrj"
            "TOKEN_SAVIOR_CLIENT" = "claude-code"
            "TOKEN_SAVIOR_PROFILE" = "optimized"
        }
    }
    "csproj-context" = @{
        command = "node"
        args    = @()
        env     = @{}
    }
}

# Docker 容器内 MCP 服务器命令映射表（用于 stdio→docker 切换）
# 键是 MCP 服务器名称，值是容器内的命令配置
$DockerCommandMap = @{
    "mcp-sequentialthinking-tools" = @{
        command = "docker"
        args    = @("exec", "-i", "workflow-mcp-env", "npx", "-y", "mcp-sequentialthinking-tools")
        env     = @{ "MAX_HISTORY_SIZE" = "1000" }
    }
    "github.com/upstash/context7-mcp" = @{
        command = "docker"
        args    = @("exec", "-i", "workflow-mcp-env", "context7-mcp")
        env     = @{}
    }
    "context-mode" = @{
        command = "docker"
        args    = @("exec", "-i", "workflow-mcp-env", "node", "/usr/local/lib/node_modules/context-mode/start.mjs")
        env     = @{}
    }
    "github.com/modelcontextprotocol/servers/tree/main/src/memory" = @{
        command = "docker"
        args    = @("exec", "-i", "workflow-mcp-env", "mcp-server-memory")
        env     = @{ "MEMORY_FILE_PATH" = "/opt/mcp-data/memory.json" }
    }
    "github.com/modelcontextprotocol/servers/tree/main/src/filesystem" = @{
        command = "docker"
        args    = @("exec", "-i", "workflow-mcp-env", "mcp-server-filesystem", "/workspace/WorkFlowPrj")
        env     = @{}
    }
    "lexis-mcp" = @{
        command = "docker"
        args    = @("exec", "-i", "workflow-mcp-env", "node", "/usr/local/lib/node_modules/lexis-mcp/dist/cli/main.js", "mcp", "--path", "/workspace/WorkFlowPrj")
        env     = @{}
    }
    "token-savior-recall" = @{
        command = "docker"
        args    = @("exec", "-i", "workflow-mcp-env", "token-savior")
        env     = @{
            "WORKSPACE_ROOTS"       = "/workspace/WorkFlowPrj"
            "TOKEN_SAVIOR_CLIENT"   = "claude-code"
            "TOKEN_SAVIOR_PROFILE"  = "optimized"
        }
    }
    "csproj-context" = @{
        command = "docker"
        args    = @("exec", "-i", "workflow-mcp-env", "node", "/workspace/WorkFlowPrj/CsprojContext/build/index.js")
        env     = @{}
    }
}

# 已知 MCP 服务器的端口分配（按名称匹配）
$McpPortMap = @{
    "mcp-sequentialthinking-tools"                                       = 3100
    "github.com/upstash/context7-mcp"                                    = 3101
    "context-mode"                                                        = 3102
    "github.com/modelcontextprotocol/servers/tree/main/src/memory"        = 3103
    "github.com/modelcontextprotocol/servers/tree/main/src/filesystem"    = 3104
    "lexis-mcp"                                                           = 3105
    "token-savior-recall"                                                 = 3106
    "csproj-context"                                                      = 3107
}

# 自动检测 docker-compose.yml 路径
$AutoDockerComposePaths = @(
    "$PSScriptRoot\..\..\DevEnvironment\Docker\docker-compose.yml",
    "$PSScriptRoot\..\..\Docker\docker-compose.yml",
    "$PSScriptRoot\..\..\docker-compose.yml"
)

# ============================================================
# 辅助函数
# ============================================================

function Write-Banner {
    param([string]$Color = "Cyan")
    Write-Host "`n╔══════════════════════════════════════════════════════════════╗" -ForegroundColor $Color
    Write-Host "║     CLine MCP 配置切换工具                                ║" -ForegroundColor $Color
    Write-Host "║     本地 stdio ↔ Docker stdio (docker exec -i)            ║" -ForegroundColor $Color
    Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor $Color
    Write-Host ""
}

function Write-Step($Title, $Color = "Cyan") {
    Write-Host "`n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor $Color
    Write-Host "  $Title" -ForegroundColor $Color
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor $Color
}

function Write-Info($Msg)    { Write-Host "  ℹ️  $Msg" -ForegroundColor Gray }
function Write-Success($Msg) { Write-Host "  ✅ $Msg" -ForegroundColor Green }
function Write-Warning($Msg) { Write-Host "  ⚠️  $Msg" -ForegroundColor Yellow }
function Write-Error($Msg)   { Write-Host "  ❌ $Msg" -ForegroundColor Red }
function Write-WhatIf($Msg)  { Write-Host "  [WhatIf] $Msg" -ForegroundColor Magenta }

function Confirm-Prompt($Prompt, $Default = "n") {
    if ($Force) { return $true }
    $optStr = if ($Default -eq "y") { "Y/n" } else { "y/N" }
    $r = Read-Host "  $Prompt [$optStr]"
    if ([string]::IsNullOrWhiteSpace($r)) { $r = $Default }
    return $r.ToLower() -eq "y"
}

function Test-CommandExists($Command) {
    return $null -ne (Get-Command $Command -ErrorAction SilentlyContinue)
}

# ============================================================
# 核心函数
# ============================================================

<#
.SYNOPSIS
    检测当前 MCP 配置的模式
.DESCRIPTION
    读取 cline_mcp_settings.json，检查第一个 MCP 服务器的 type 字段
    来判断当前是 stdio 还是 http 模式。
#>
function Get-CurrentMode {
    if (-not (Test-Path $ClineSettingsPath)) {
        return $null
    }

    try {
        $config = Get-Content $ClineSettingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $firstServer = $config.mcpServers.PSObject.Properties | Select-Object -First 1
        if ($firstServer -and $firstServer.Value.type) {
            return $firstServer.Value.type
        }
    } catch {
        Write-Warning "无法解析当前配置: $($_.Exception.Message)"
    }

    return $null
}

<#
.SYNOPSIS
    获取 MCP 服务器对应的端口号
.DESCRIPTION
    先按名称精确匹配，再按顺序分配端口。
#>
function Get-ServerPort {
    param([string]$ServerName, [int]$Index)

    if ($McpPortMap.ContainsKey($ServerName)) {
        return $McpPortMap[$ServerName]
    }

    # 按顺序分配
    $port = $PortStart + $Index
    if ($port -gt $PortEnd) {
        $port = $PortStart + ($Index % ($PortEnd - $PortStart + 1))
    }
    return $port
}

<#
.SYNOPSIS
    将 stdio 配置转换为 docker 配置
.DESCRIPTION
    将 command/args 替换为 docker exec -i 命令，保持 type:stdio。
#>
function Convert-ToDockerConfig {
    param(
        [PSCustomObject]$ServerConfig,
        [string]$ServerName
    )

    $cmdInfo = $DockerCommandMap[$ServerName]
    if (-not $cmdInfo) {
        Write-Warning "未知 MCP 服务器 '$ServerName'，将保留原配置"
        return $ServerConfig
    }

    $newConfig = [PSCustomObject]@{
        type         = "stdio"
        autoApprove  = $ServerConfig.autoApprove
        disabled     = if ($null -ne $ServerConfig.disabled) { $ServerConfig.disabled } else { $false }
        timeout      = if ($null -ne $ServerConfig.timeout) { $ServerConfig.timeout } else { 60 }
        command      = $cmdInfo.command
        args         = $cmdInfo.args
    }

    # 合并 env
    $mergedEnv = @{}
    if ($cmdInfo.env) {
        $cmdInfo.env.Keys | ForEach-Object { $mergedEnv[$_] = $cmdInfo.env[$_] }
    }
    if ($ServerConfig.env) {
        $ServerConfig.env.PSObject.Properties | ForEach-Object { $mergedEnv[$_.Name] = $_.Value }
    }
    if ($mergedEnv.Keys.Count -gt 0) {
        $newConfig | Add-Member -NotePropertyName "env" -NotePropertyValue ($mergedEnv | ConvertTo-PSCustomObject)
    }

    return $newConfig
}

<#
.SYNOPSIS
    将 docker 或 http 配置转换为本地 stdio 配置
.DESCRIPTION
    还原为本地 command/args（从映射表获取）。
#>
function Convert-ToStdioConfig {
    param(
        [PSCustomObject]$ServerConfig,
        [string]$ServerName
    )

    $cmdInfo = $McpCommandMap[$ServerName]
    if (-not $cmdInfo) {
        Write-Warning "未知 MCP 服务器 '$ServerName'，将保留最小配置"
        $cmdInfo = @{ command = ""; args = @(); env = @{} }
    }

    $newConfig = [PSCustomObject]@{
        type         = "stdio"
        autoApprove  = $ServerConfig.autoApprove
        disabled     = if ($null -ne $ServerConfig.disabled) { $ServerConfig.disabled } else { $false }
        timeout      = if ($null -ne $ServerConfig.timeout) { $ServerConfig.timeout } else { 60 }
        command      = $cmdInfo.command
        args         = $cmdInfo.args
    }

    # 合并 env：优先保留原配置中的 env，再补充映射表中的默认值
    $mergedEnv = @{}
    if ($cmdInfo.env) {
        $cmdInfo.env.Keys | ForEach-Object { $mergedEnv[$_] = $cmdInfo.env[$_] }
    }
    if ($ServerConfig.env) {
        $ServerConfig.env.PSObject.Properties | ForEach-Object { $mergedEnv[$_.Name] = $_.Value }
    }
    if ($mergedEnv.Keys.Count -gt 0) {
        $newConfig | Add-Member -NotePropertyName "env" -NotePropertyValue ($mergedEnv | ConvertTo-PSCustomObject)
    }

    return $newConfig
}

<#
.SYNOPSIS
    将 PSCustomObject 转换为 Hashtable（用于 JSON 序列化）
#>
function ConvertTo-Hashtable {
    param($InputObject)

    if ($null -eq $InputObject) { return $null }

    if ($InputObject -is [PSCustomObject]) {
        $hash = @{}
        $InputObject.PSObject.Properties | ForEach-Object {
            $hash[$_.Name] = ConvertTo-Hashtable $_.Value
        }
        return $hash
    }

    if ($InputObject -is [System.Collections.IList]) {
        $list = @()
        foreach ($item in $InputObject) {
            $list += ConvertTo-Hashtable $item
        }
        return $list
    }

    return $InputObject
}

<#
.SYNOPSIS
    将 Hashtable 转换为 PSCustomObject（递归）
#>
function ConvertTo-PSCustomObject {
    param($InputObject)

    if ($null -eq $InputObject) { return $null }

    if ($InputObject -is [System.Collections.IDictionary]) {
        $obj = [PSCustomObject]@{}
        $InputObject.Keys | ForEach-Object {
            $obj | Add-Member -NotePropertyName $_ -NotePropertyValue (ConvertTo-PSCustomObject $InputObject[$_])
        }
        return $obj
    }

    if ($InputObject -is [System.Collections.IList]) {
        $list = @()
        foreach ($item in $InputObject) {
            $list += ConvertTo-PSCustomObject $item
        }
        return [PSCustomObject[]]$list
    }

    return $InputObject
}

<#
.SYNOPSIS
    切换 MCP 配置模式
.DESCRIPTION
    读取当前配置，遍历所有 MCP 服务器，逐个转换配置。
#>
function Switch-McpMode {
    param([string]$TargetMode)

    Write-Step "切换 MCP 配置: stdio → $TargetMode"

    if ($WhatIf) {
        Write-WhatIf "读取配置: $ClineSettingsPath"
        Write-WhatIf "目标模式: $TargetMode"
        return $true
    }

    # 读取当前配置
    $config = Get-Content $ClineSettingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $newServers = [PSCustomObject]@{}
    $index = 0

    foreach ($server in $config.mcpServers.PSObject.Properties) {
        $name = $server.Name
        $value = $server.Value

        if ($TargetMode -eq "docker") {
            $newValue = Convert-ToDockerConfig -ServerConfig $value -ServerName $name
            Write-Info "[$name] → docker exec -i $ContainerName ($($newValue.args -join ' '))"
        } else {
            $newValue = Convert-ToStdioConfig -ServerConfig $value -ServerName $name
            Write-Info "[$name] → stdio ($($newValue.command))"
        }

        $newServers | Add-Member -NotePropertyName $name -NotePropertyValue $newValue
        $index++
    }

    # 构建新配置
    $newConfig = [PSCustomObject]@{
        mcpServers = $newServers
    }

    # 序列化为 JSON — 使用 Node.js 确保无 BOM、标准 2 空格缩进
    # PowerShell 的 ConvertTo-Json 会生成带 BOM 的格式，CLine 无法解析
    $tempScriptPath = [System.IO.Path]::GetTempFileName() + ".ps1.json"
    try {
        # 将配置导出为临时 JSON 文件
        $newConfig | ConvertTo-Json -Depth 10 | Set-Content $tempScriptPath -Encoding UTF8 -Force

        # 用 Node.js 重新序列化（去 BOM、标准化缩进）
        $nodeScript = @"
const fs = require('fs');
const p = require('path');
const input = fs.readFileSync('$($tempScriptPath.Replace('\','\\'))', 'utf8');
// 去 BOM
const clean = input.charCodeAt(0) === 0xFEFF ? input.substring(1) : input;
const config = JSON.parse(clean);
const json = JSON.stringify(config, null, 2) + '\n';
fs.writeFileSync('$($ClineSettingsPath.Replace('\','\\'))', Buffer.from(json, 'utf8'));
console.log('OK');
"@
        $result = node -e $nodeScript 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "Node.js 序列化失败: $result"
        }
    } finally {
        if (Test-Path $tempScriptPath) { Remove-Item $tempScriptPath -Force }
    }

    Write-Success "配置已更新: $ClineSettingsPath"
    return $true
}

<#
.SYNOPSIS
    备份当前配置
.DESCRIPTION
    将当前 cline_mcp_settings.json 备份到 .bak 文件。
#>
function Backup-CurrentConfig {
    if ($WhatIf) {
        Write-WhatIf "备份配置: $ClineSettingsPath → $BackupPath"
        return $true
    }

    if (-not (Test-Path $ClineSettingsPath)) {
        Write-Error "配置文件不存在: $ClineSettingsPath"
        return $false
    }

    try {
        Copy-Item $ClineSettingsPath $BackupPath -Force
        Write-Success "配置已备份: $BackupPath"
        return $true
    } catch {
        Write-Error "备份失败: $($_.Exception.Message)"
        return $false
    }
}

<#
.SYNOPSIS
    检查 Docker 容器状态
.DESCRIPTION
    检查 workflow-mcp-env 容器是否在运行。
#>
function Test-DockerContainerRunning {
    if (-not (Test-CommandExists "docker")) {
        return $false
    }

    try {
        $state = docker inspect --format "{{.State.Status}}" workflow-mcp-env 2>$null
        return ($state -eq "running")
    } catch {
        return $false
    }
}

<#
.SYNOPSIS
    获取 docker-compose.yml 路径
.DESCRIPTION
    按优先级自动检测 docker-compose.yml 路径。
#>
function Resolve-DockerComposePath {
    if ($DockerComposePath -and (Test-Path $DockerComposePath)) {
        return $DockerComposePath
    }

    foreach ($path in $AutoDockerComposePaths) {
        $resolvedPath = [System.IO.Path]::GetFullPath($path)
        if (Test-Path $resolvedPath) {
            Write-Info "自动检测到 docker-compose: $resolvedPath"
            return $resolvedPath
        }
    }

    return $null
}

<#
.SYNOPSIS
    提示用户启动 Docker 容器
.DESCRIPTION
    如果切换到 docker 模式但容器未运行，提示用户启动。
#>
function Invoke-DockerStartPrompt {
    $composePath = Resolve-DockerComposePath
    if (-not $composePath) {
        Write-Warning "未找到 docker-compose.yml，请手动构建并启动 Docker 容器"
        Write-Info "参考文档: DevEnvironment/Docker_MCP_环境搭建指南.md"
        return
    }

    Write-Info "Docker Compose 文件: $composePath"

    if (Test-DockerContainerRunning) {
        Write-Success "Docker 容器 'workflow-mcp-env' 已在运行"
        return
    }

    Write-Warning "Docker 容器 'workflow-mcp-env' 未运行"

    if ($Force) {
        Write-Info "正在启动 Docker 容器..."
        $result = & docker compose -f $composePath up -d 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Success "Docker 容器已启动"
        } else {
            Write-Error "启动失败: $result"
        }
        return
    }

    if (Confirm-Prompt "是否构建并启动 Docker 容器？" "n") {
        Write-Info "正在构建并启动 Docker 容器..."
        Write-Info "首次构建可能需要 15-30 分钟（Python 源码编译）"

        if (Confirm-Prompt "是否先构建镜像？" "n") {
            $buildResult = & docker compose -f $composePath build 2>&1
            if ($LASTEXITCODE -eq 0) {
                Write-Success "镜像构建完成"
            } else {
                Write-Error "构建失败: $buildResult"
                return
            }
        }

        $upResult = & docker compose -f $composePath up -d 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Success "Docker 容器已启动"
        } else {
            Write-Error "启动失败: $upResult"
        }
    } else {
        Write-Info "请手动执行以下命令启动容器："
        Write-Host "  docker compose -f $composePath up -d" -ForegroundColor Yellow
    }
}

<#
.SYNOPSIS
    验证配置
.DESCRIPTION
    检查配置文件的 JSON 格式是否正确。
#>
function Test-Configuration {
    if ($WhatIf) {
        Write-WhatIf "验证配置: $ClineSettingsPath"
        return $true
    }

    if (-not (Test-Path $ClineSettingsPath)) {
        Write-Error "配置文件不存在: $ClineSettingsPath"
        return $false
    }

    try {
        $config = Get-Content $ClineSettingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $serverCount = ($config.mcpServers.PSObject.Properties | Measure-Object).Count
        Write-Success "配置格式正确，共 $serverCount 个 MCP 服务器"

        # 显示摘要
        Write-Host ""
        Write-Host "  MCP 服务器列表:" -ForegroundColor Cyan
        foreach ($server in $config.mcpServers.PSObject.Properties) {
            $name = $server.Name
            $value = $server.Value
            $status = if ($value.disabled) { "❌" } else { "✅" }
            $modeInfo = if ($value.type -eq "http") { $value.url } else { "$($value.command) $($value.args -join ' ')" }
            Write-Host "  $status [$($value.type)] $name" -ForegroundColor White
            Write-Host "       → $modeInfo" -ForegroundColor Gray
        }

        return $true
    } catch {
        Write-Error "配置格式错误: $($_.Exception.Message)"
        return $false
    }
}

<#
.SYNOPSIS
    显示切换前后的对比
.DESCRIPTION
    读取备份和当前配置，显示差异。
#>
function Show-ConfigDiff {
    if (-not (Test-Path $BackupPath)) {
        Write-Info "无备份文件，跳过对比"
        return
    }

    Write-Step "配置变更对比"

    try {
        $oldConfig = Get-Content $BackupPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $newConfig = Get-Content $ClineSettingsPath -Raw -Encoding UTF8 | ConvertFrom-Json

        $oldServers = $oldConfig.mcpServers.PSObject.Properties | Sort-Object Name
        $newServers = $newConfig.mcpServers.PSObject.Properties | Sort-Object Name

        $maxLen = ($oldServers | ForEach-Object { $_.Name.Length } | Measure-Object -Maximum).Maximum
        if ($maxLen -lt 20) { $maxLen = 20 }

        Write-Host ("  {0,-$maxLen} {1,-12} {2,-12}" -f "服务器", "旧模式", "新模式") -ForegroundColor Yellow
        Write-Host ("  $("-"*$maxLen) ------------ ------------") -ForegroundColor DarkGray

        for ($i = 0; $i -lt [Math]::Max($oldServers.Count, $newServers.Count); $i++) {
            $old = if ($i -lt $oldServers.Count) { $oldServers[$i] } else { $null }
            $new = if ($i -lt $newServers.Count) { $newServers[$i] } else { $null }
            $name = if ($old) { $old.Name } else { $new.Name }
            $oldMode = if ($old) { $old.Value.type } else { "-" }
            $newMode = if ($new) { $new.Value.type } else { "-" }
            $arrow = if ($oldMode -ne $newMode) { " → " } else { "   " }
            Write-Host ("  {0,-$maxLen} {1,-6}{2}{3,-6}" -f $name, $oldMode, $arrow, $newMode) -ForegroundColor White
        }
    } catch {
        Write-Warning "无法对比配置: $($_.Exception.Message)"
    }
}

# ============================================================
# 主流程
# ============================================================

function Main {
    Write-Banner

    if ($WhatIf) {
        Write-Host "  🔮 WhatIf 模式 — 仅预览，不实际执行`n" -ForegroundColor Magenta
    }

    # ─── 阶段 1: 环境检查 ───
    Write-Step "阶段 1/5: 环境检查"

    # 检查配置文件是否存在
    if (-not (Test-Path $ClineSettingsPath)) {
        Write-Error "CLine 配置文件不存在: $ClineSettingsPath"
        Write-Info "请确保已安装 CLine 扩展并至少运行过一次"
        exit 1
    }
    Write-Success "CLine 配置: $ClineSettingsPath"

    # 检测当前模式
    $currentMode = Get-CurrentMode
    if ($null -eq $currentMode) {
        Write-Warning "无法检测当前模式，将尝试直接切换"
    } else {
        Write-Info "当前模式: $currentMode"
        if ($currentMode -eq $Mode) {
            Write-Warning "已经是 $Mode 模式，无需切换"
            if (-not $Force -and -not (Confirm-Prompt "是否强制重新切换？" "n")) {
                Write-Info "已取消。"
                exit 0
            }
        }
    }

    # 检查 Docker 命令（切换到 docker 时需要）
    if ($Mode -eq "docker") {
        if (Test-CommandExists "docker") {
            Write-Success "Docker 命令可用"
        } else {
            Write-Warning "Docker 命令不可用，请先安装 Docker Desktop"
            Write-Info "下载地址: https://www.docker.com/products/docker-desktop/"
            if (-not $Force -and -not (Confirm-Prompt "是否继续？" "n")) {
                Write-Info "已取消。"
                exit 0
            }
        }
    }

    # ─── 阶段 2: 确认 ───
    Write-Step "阶段 2/5: 确认操作"

    $modeLabel = if ($Mode -eq "docker") { "Docker stdio (docker exec -i)" } else { "本地 stdio" }
    Write-Host "  将切换 MCP 配置到: $modeLabel 模式" -ForegroundColor White
    Write-Host "  配置文件: $ClineSettingsPath" -ForegroundColor White

    if (-not $Force -and -not $WhatIf) {
        if (-not (Confirm-Prompt "确认切换？" "n")) {
            Write-Info "已取消。"
            exit 0
        }
    }

    # ─── 阶段 3: 备份 ───
    Write-Step "阶段 3/5: 备份当前配置"

    $backupOk = Backup-CurrentConfig
    if (-not $backupOk -and -not $WhatIf) {
        Write-Error "备份失败，终止操作"
        exit 1
    }

    # ─── 阶段 4: 切换配置 ───
    Write-Step "阶段 4/5: 切换配置"

    $switchOk = Switch-McpMode -TargetMode $Mode
    if (-not $switchOk -and -not $WhatIf) {
        Write-Error "切换失败"
        exit 1
    }

    # ─── 阶段 5: 验证 ───
    Write-Step "阶段 5/5: 验证"

    $verifyOk = Test-Configuration

    if ($verifyOk -and $Mode -eq "docker" -and -not $WhatIf) {
        Show-ConfigDiff
        Write-Host ""
        Invoke-DockerStartPrompt
    }

    # ─── 摘要 ───
    Write-Step "操作完成" -Color "Green"

    if ($WhatIf) {
        Write-Host "  🔮 WhatIf 模式结束 — 未执行任何实际操作" -ForegroundColor Magenta
        Write-Host ""
        Write-Host "  预览摘要:" -ForegroundColor Cyan
        Write-Host "  目标模式: $Mode" -ForegroundColor White
        Write-Host "  配置文件: $ClineSettingsPath" -ForegroundColor White
        Write-Host "  备份文件: $BackupPath" -ForegroundColor White
        return
    }

    if ($verifyOk) {
        Write-Host "  ✅ MCP 配置已切换到 $Mode 模式" -ForegroundColor Green
        Write-Host ""
        Write-Host "  📂 配置文件: $ClineSettingsPath" -ForegroundColor Cyan
        Write-Host "  📂 备份文件: $BackupPath" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "  💡 请重启 VS Code 使配置生效" -ForegroundColor Yellow

        if ($Mode -eq "docker") {
            Write-Host ""
            Write-Host "  📋 后续步骤:" -ForegroundColor Cyan
            Write-Host "  1. 重启 VS Code" -ForegroundColor White
            Write-Host "  2. 在 CLine 中检查 MCP 服务器状态（应全部显示 ✅）" -ForegroundColor White
            Write-Host "  3. 如果服务器显示 ❌，请检查 Docker 容器日志:" -ForegroundColor White
            Write-Host "     docker logs workflow-mcp-env" -ForegroundColor Gray
            Write-Host ""
            Write-Host "  🔙 如需回退到本地模式:" -ForegroundColor Yellow
            Write-Host "     .\Switch-ClineMCPToDocker.ps1 -Mode stdio" -ForegroundColor Gray
        }
    } else {
        Write-Host "  ❌ 配置验证失败，请检查配置文件" -ForegroundColor Red
        Write-Host "  备份文件: $BackupPath" -ForegroundColor Yellow
        Write-Host "  如需恢复，请手动复制备份文件覆盖" -ForegroundColor Yellow
    }
}

# ============================================================
# 启动
# ============================================================

Main
