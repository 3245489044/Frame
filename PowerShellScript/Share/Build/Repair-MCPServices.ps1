<#
.SYNOPSIS
    MCP 服务修复工具 — 诊断并修复 Docker 容器内 MCP 服务异常

.DESCRIPTION
    通用维护脚本，自动诊断并修复 Docker 容器内 MCP 服务进程崩溃/端口不可达的问题。
    支持自定义容器名、端口列表、entrypoint 路径，可复用其他项目。

    修复流程：
        1. 诊断阶段：检查容器状态、端口状态、日志分析
        2. 修复 entrypoint.sh（修复硬编码路径、缺失命令等已知问题）
        3. 在容器内安装缺失依赖（pip install、创建目录等）
        4. 构建缺失的编译产物（如 csproj-context）
        5. 更新 entrypoint.sh 到容器并重启
        6. 验证所有端口就绪

    用法:
        # 直接运行（传统方式）
        .\Repair-MCPServices.ps1                       交互模式
        .\Repair-MCPServices.ps1 -Force                 跳过确认
        .\Repair-MCPServices.ps1 -ContainerName "my-container" -Ports @(3100,3101)
        .\Repair-MCPServices.ps1 -SkipRestart           修复后不重启容器
        .\Repair-MCPServices.ps1 -DiagnoseOnly          仅诊断，不执行修复

        # 通过 PScriptManager 调用（解耦路径）
        Invoke-Function -Name "Repair-MCPServices" -Arguments @{Force=$true}

    示例:
        # 默认修复 workflow-mcp-env
        .\Repair-MCPServices.ps1 -Force

        # 仅诊断
        .\Repair-MCPServices.ps1 -DiagnoseOnly

        # 自定义容器和端口
        .\Repair-MCPServices.ps1 -ContainerName "my-mcp-env" -Ports @(3000,3001,3002)

.NOTES
    依赖: BasicMethod/DevOps/Docker.psm1（Docker 操作函数）
           BasicMethod/Core/Output.psm1（输出函数）
           BasicMethod/Core/Utils.psm1（确认函数）
    默认容器: workflow-mcp-env
    默认端口: 3100-3107（不含 3104）
#>
# @version: 1.0.0
# @author: WorkFlowPrj
# @category: 运维
# @exports: Repair-MCPServices
# @requires: Write-Header, Write-Step, Write-Success, Write-Info, Write-Warn, Write-Error
# @requires: Test-CommandExists, Confirm-Action
# @requires: Invoke-DockerCommand, Test-ContainerRunning, Copy-FileToContainer, Get-ContainerState

# ============================================================
# 参数定义
# ============================================================
param(
    # 容器名称
    [string]$ContainerName = "workflow-mcp-env",

    # 要检查的端口列表
    [int[]]$Ports = @(3100, 3101, 3102, 3103, 3105, 3106, 3107),

    # entrypoint.sh 宿主机路径（默认自动推导）
    [string]$EntrypointPath = "",

    # Docker Compose 目录（用于 L2 重置回退）
    [string]$ComposeDir = "",

    # 跳过确认提示
    [switch]$Force,

    # 仅诊断，不执行修复
    [switch]$DiagnoseOnly,

    # 跳过容器重启
    [switch]$SkipRestart
)

# ============================================================
# 模块加载（通过 Import-Modules.ps1 按需加载）
# ============================================================

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$moduleDir = Resolve-Path (Join-Path $scriptDir "..\Modules")
. "$moduleDir\Import-Modules.ps1"
$projectRoot = Get-ProjectRoot

# ============================================================
# 配置
# ============================================================

# 自动推导路径
if (-not $EntrypointPath) {
    $EntrypointPath = Join-Path $projectRoot "DevEnvironment\Docker\scripts\entrypoint.sh"
}
if (-not $ComposeDir) {
    $ComposeDir = Join-Path $projectRoot "DevEnvironment\Docker"
}

# 服务端口映射（用于诊断输出）
$script:ServicePortMap = @{
    3100 = "mcp-sequentialthinking-tools"
    3101 = "context7-mcp"
    3102 = "context-mode"
    3103 = "memory"
    3105 = "lexis-mcp"
    3106 = "token-savior-recall"
    3107 = "csproj-context"
}

# ============================================================
# 诊断阶段
# ============================================================

function Invoke-Diagnose {
    <#
    .SYNOPSIS
        诊断容器状态、端口状态、日志分析
    .OUTPUTS
        [hashtable] @{ AllPortsReady; Issues; PortResults }
    #>
    Write-Step "诊断阶段：检查容器状态"

    $result = @{
        AllPortsReady = $true
        Issues        = @()
        PortResults   = @{}
    }

    # 1. 检查 Docker 引擎
    if (-not (Test-CommandExists "docker")) {
        Write-Error "Docker 不可用，请先安装 Docker Desktop"
        return $result
    }
    $ver = docker --version 2>&1
    Write-Success "Docker 引擎: $($ver.Trim())"

    # 2. 检查容器状态
    $containerRunning = Test-ContainerRunning -ContainerName $ContainerName
    if (-not $containerRunning) {
        Write-Error "容器 '$ContainerName' 未运行"
        return $result
    }
    Write-Success "容器 '$ContainerName' 运行中"

    # 3. 检查端口状态
    Write-Info "检查端口状态..."
    foreach ($port in $Ports) {
        $ready = Test-PortInContainer -ContainerName $ContainerName -Port $port
        $result.PortResults[$port] = $ready
        $serviceName = $script:ServicePortMap[$port]
        $label = if ($serviceName) { "$port ($serviceName)" } else { "$port" }

        if ($ready) {
            Write-Success "  端口 $label ✅"
        } else {
            Write-Warning "  端口 $label ❌"
            $result.AllPortsReady = $false
        }
    }

    if ($result.AllPortsReady) {
        Write-Success "所有端口已就绪，无需修复"
        return $result
    }

    # 4. 分析容器日志中的错误
    Write-Info "分析容器日志中的错误..."
    $logs = Get-ContainerLogs -ContainerName $ContainerName -Tail 150

    # 已知问题模式匹配
    $knownPatterns = @(
        @{ Pattern = 'spawn context7-mcp ENOENT';          Service = "context7-mcp";          Port = 3101; Issue = "命令未找到，需要全局安装或改用 npx" }
        @{ Pattern = '/Users/quanle96/Documents/mcp-servers/memory/log.txt'; Service = "memory"; Port = 3103; Issue = "硬编码了宿主机路径，容器内不存在" }
        @{ Pattern = "ModuleNotFoundError.*No module named 'mcp'"; Service = "token-savior-recall"; Port = 3106; Issue = "Python 缺少 mcp 模块" }
        @{ Pattern = 'csproj-context 未构建';               Service = "csproj-context";        Port = 3107; Issue = "编译产物不存在" }
        @{ Pattern = 'could not start the proxy';           Service = "mcp-proxy";             Port = 0;    Issue = "mcp-proxy 启动失败" }
    )

    foreach ($kp in $knownPatterns) {
        if ($logs -match $kp.Pattern) {
            $issue = @{ Service = $kp.Service; Port = $kp.Port; Issue = $kp.Issue }
            $result.Issues += $issue
            Write-Warning "  $($kp.Service) ($($kp.Port)): $($kp.Issue)"
        }
    }

    if ($result.Issues.Count -eq 0) {
        Write-Info "未检测到已知问题模式，但端口未就绪，可能进程已崩溃"
    }

    return $result
}

# ============================================================
# 修复 entrypoint.sh
# ============================================================

function Repair-Entrypoint {
    <#
    .SYNOPSIS
        修复 entrypoint.sh 中的已知问题
    .DESCRIPTION
        自动检测并修复以下问题：
        - context7-mcp 命令未找到 → 改用 npx -y
        - memory 硬编码路径 → 确保 /opt/mcp-data 目录存在
        - token-savior-recall 缺少 Python 模块 → 添加 pip install 前置检查
        - 端口检查无重试 → 添加重试逻辑
    .OUTPUTS
        [bool] 修复成功或无需修复返回 $true
    #>
    Write-Step "修复 entrypoint.sh"

    if (-not (Test-Path $EntrypointPath)) {
        Write-Error "entrypoint.sh 未找到: $EntrypointPath"
        return $false
    }

    $content = Get-Content $EntrypointPath -Raw
    $changed = $false

    # ── 修复 1: context7-mcp — 改用 npx 运行 ──
    if ($content -match 'npx mcp-proxy --port 3101 --stateless -- context7-mcp') {
        $content = $content -replace 'npx mcp-proxy --port 3101 --stateless -- context7-mcp',
            'npx mcp-proxy --port 3101 --stateless -- npx -y context7-mcp'
        Write-Success "  context7-mcp: 改为使用 npx -y 运行"
        $changed = $true
    }

    # ── 修复 2: memory — 确保数据目录存在 ──
    $oldMemorySection = @"
# 4. memory (端口 3103)
MEMORY_FILE_PATH=/opt/mcp-data/memory.json npx mcp-proxy --port 3103 --stateless -- mcp-server-memory &
"@
    $newMemorySection = @"
# 4. memory (端口 3103)
# ★ 修复：确保数据目录存在，避免硬编码路径问题
mkdir -p /opt/mcp-data 2>/dev/null || true
MEMORY_FILE_PATH=/opt/mcp-data/memory.json npx mcp-proxy --port 3103 --stateless -- mcp-server-memory &
"@
    if ($content -match [regex]::Escape($oldMemorySection)) {
        $content = $content -replace [regex]::Escape($oldMemorySection), $newMemorySection
        Write-Success "  memory: 添加了 mkdir -p /opt/mcp-data 确保目录存在"
        $changed = $true
    }

    # ── 修复 3: token-savior-recall — 添加 pip install 前置检查 ──
    $oldTokenLine = @"
# 7. token-savior-recall (端口 3106)
WORKSPACE_ROOTS=/workspace/WorkFlowPrj TOKEN_SAVIOR_CLIENT=claude-code TOKEN_SAVIOR_PROFILE=optimized \
  npx mcp-proxy --port 3106 --stateless -- token-savior &
"@
    $newTokenLine = @"
# 7. token-savior-recall (端口 3106)
# ★ 修复：启动前确保 Python mcp 模块已安装
pip install mcp -q 2>/dev/null || true
WORKSPACE_ROOTS=/workspace/WorkFlowPrj TOKEN_SAVIOR_CLIENT=claude-code TOKEN_SAVIOR_PROFILE=optimized \
  npx mcp-proxy --port 3106 --stateless -- token-savior &
"@
    if ($content -match [regex]::Escape($oldTokenLine)) {
        $content = $content -replace [regex]::Escape($oldTokenLine), $newTokenLine
        Write-Success "  token-savior-recall: 添加了 pip install mcp 前置检查"
        $changed = $true
    }

    # ── 修复 4: 增加服务启动后的等待时间 ──
    if ($content -match 'sleep 5') {
        $content = $content -replace 'sleep 5', 'sleep 8'
        Write-Success "  增加等待时间: sleep 5 → sleep 8"
        $changed = $true
    }

    # ── 修复 5: 端口检查添加重试逻辑 ──
    $oldPortCheck = @"
echo "[entrypoint] 检查 MCP 服务状态..."
for port in 3100 3101 3102 3103 3104 3105 3106 3107; do
  if command -v ss > /dev/null 2>&1; then
    if ss -tlnp | grep -q ":$port "; then
      echo "  ✅ 端口 $port 已就绪"
    else
      echo "  ❌ 端口 $port 未就绪"
    fi
  fi
done
"@
    $newPortCheck = @"
echo "[entrypoint] 检查 MCP 服务状态..."
# ★ 修复：对未就绪的端口等待后重试一次
for port in 3100 3101 3102 3103 3104 3105 3106 3107; do
  if command -v ss > /dev/null 2>&1; then
    if ss -tlnp | grep -q ":$port "; then
      echo "  ✅ 端口 $port 已就绪"
    else
      echo "  ⏳ 端口 $port 未就绪，等待 3 秒后重试..."
      sleep 3
      if ss -tlnp | grep -q ":$port "; then
        echo "  ✅ 端口 $port 已就绪（重试后）"
      else
        echo "  ❌ 端口 $port 未就绪"
      fi
    fi
  fi
done
"@
    if ($content -match [regex]::Escape($oldPortCheck)) {
        $content = $content -replace [regex]::Escape($oldPortCheck), $newPortCheck
        Write-Success "  端口检查: 添加了重试逻辑"
        $changed = $true
    }

    if (-not $changed) {
        Write-Info "  entrypoint.sh 无需修改"
        return $true
    }

    # 写入文件
    try {
        $content | Set-Content -Path $EntrypointPath -NoNewline -Encoding UTF8
        Write-Success "entrypoint.sh 已更新"
        return $true
    } catch {
        Write-Error "写入 entrypoint.sh 失败: $_"
        return $false
    }
}

# ============================================================
# 在容器内安装缺失依赖
# ============================================================

function Install-ContainerDependencies {
    <#
    .SYNOPSIS
        在容器内安装缺失的依赖
    .DESCRIPTION
        安装 Python mcp 模块、创建必要目录、检查 npx 包可用性。
    .OUTPUTS
        [bool] 始终返回 $true（部分失败不阻断流程）
    #>
    Write-Step "在容器内安装缺失依赖"

    # 1. 安装 Python mcp 模块（token-savior-recall 需要）
    Invoke-DockerCommand -ContainerName $ContainerName `
        -Command "pip install mcp -q 2>&1 | tail -3" `
        -Description "安装 Python mcp 模块" | Out-Null

    # 2. 确保 /opt/mcp-data 目录存在
    Invoke-DockerCommand -ContainerName $ContainerName `
        -Command "mkdir -p /opt/mcp-data && chmod 777 /opt/mcp-data" `
        -Description "创建 /opt/mcp-data 目录" | Out-Null

    # 3. 检查 context7-mcp 是否可通过 npx 使用
    $ctx7Result = Invoke-DockerCommand -ContainerName $ContainerName `
        -Command "npx -y context7-mcp --version 2>&1 || echo 'NOT_FOUND'" `
        -Description "检查 context7-mcp 可用性" -PassThru
    if ($ctx7Result -and $ctx7Result -match 'NOT_FOUND') {
        Write-Warn "  context7-mcp 通过 npx 不可用，将在重启后重试"
    }

    # 4. 检查 mcp-sequentialthinking-tools 是否可通过 npx 使用
    $seqResult = Invoke-DockerCommand -ContainerName $ContainerName `
        -Command "npx -y mcp-sequentialthinking-tools --version 2>&1 || echo 'NOT_FOUND'" `
        -Description "检查 mcp-sequentialthinking-tools 可用性" -PassThru
    if ($seqResult -and $seqResult -match 'NOT_FOUND') {
        Write-Warn "  mcp-sequentialthinking-tools 通过 npx 不可用"
    }

    Write-Success "依赖安装完成"
    return $true
}

# ============================================================
# 构建 csproj-context
# ============================================================

function Build-CsprojContext {
    <#
    .SYNOPSIS
        在容器内编译 csproj-context TypeScript 项目
    .OUTPUTS
        [bool] 编译成功或已存在返回 $true
    #>
    Write-Step "构建 csproj-context"

    $containerBuildPath = "/workspace/WorkFlowPrj/TypeScriptPrj/TsWorkspace/CsprojContext/build/index.js"

    # 检查容器内是否已有编译产物
    $exists = Invoke-DockerCommand -ContainerName $ContainerName `
        -Command "test -f '$containerBuildPath' && echo 'EXISTS' || echo 'NOT_FOUND'" `
        -Description "检查容器内 csproj-context 编译产物" -PassThru
    if ($exists -and $exists -match 'EXISTS') {
        Write-Success "csproj-context 编译产物已存在"
        return $true
    }

    # 尝试在容器内编译
    $buildResult = Invoke-DockerCommand -ContainerName $ContainerName `
        -Command "cd /workspace/WorkFlowPrj/TypeScriptPrj/TsWorkspace/CsprojContext && npx --no-install tsc 2>&1" `
        -Description "编译 csproj-context" -PassThru

    if (-not $buildResult) {
        Write-Warn "csproj-context 编译失败，将跳过端口 3107"
        return $false
    }

    # 验证编译产物
    $verifyResult = Invoke-DockerCommand -ContainerName $ContainerName `
        -Command "test -f '$containerBuildPath' && echo 'OK' || echo 'FAIL'" `
        -Description "验证 csproj-context 编译产物" -PassThru
    if ($verifyResult -and $verifyResult -match 'OK') {
        Write-Success "csproj-context 编译成功"
        return $true
    }

    Write-Warn "csproj-context 编译后产物未找到"
    return $false
}

# ============================================================
# 更新 entrypoint.sh 到容器并重启
# ============================================================

function Update-AndRestart {
    <#
    .SYNOPSIS
        复制 entrypoint.sh 到容器并重启
    .OUTPUTS
        [bool] 成功返回 $true
    #>
    Write-Step "更新 entrypoint.sh 到容器并重启"

    # 1. 复制 entrypoint.sh 到容器
    Write-Info "复制 entrypoint.sh 到容器..."
    $cpOk = Copy-FileToContainer -ContainerName $ContainerName `
        -SourcePath $EntrypointPath `
        -DestPath "/entrypoint.sh"
    if (-not $cpOk) {
        Write-Error "复制 entrypoint.sh 失败"
        return $false
    }

    # 2. 设置可执行权限
    Invoke-DockerCommand -ContainerName $ContainerName `
        -Command "chmod +x /entrypoint.sh" `
        -Description "设置 entrypoint.sh 可执行权限" | Out-Null

    Write-Success "entrypoint.sh 已更新到容器"

    # 3. 重启容器
    if (-not $SkipRestart) {
        Write-Info "重启容器 '$ContainerName'..."
        $restartOutput = docker restart $ContainerName 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Error "容器重启失败: $restartOutput"
            return $false
        }
        Write-Success "容器已重启"

        # 4. 等待容器就绪
        Write-Info "等待容器就绪（最长 60 秒）..."
        $maxWait = 60
        $waited = 0
        $containerReady = $false
        while ($waited -lt $maxWait) {
            $state = Get-ContainerState -ContainerName $ContainerName
            if ($state -eq "running") {
                $containerReady = $true
                break
            }
            Start-Sleep -Seconds 3
            $waited += 3
        }

        if (-not $containerReady) {
            Write-Error "容器重启后未进入运行状态"
            return $false
        }
        Write-Success "容器已进入运行状态（${waited}s）"

        # 5. 额外等待服务启动
        Write-Info "等待 MCP 服务启动（15 秒）..."
        Start-Sleep -Seconds 15
    } else {
        Write-Info "跳过容器重启"
    }

    return $true
}

# ============================================================
# 验证修复结果
# ============================================================

function Invoke-Verify {
    <#
    .SYNOPSIS
        验证所有端口是否就绪
    .OUTPUTS
        [bool] 全部就绪返回 $true
    #>
    Write-Step "验证修复结果"

    $allOk = $true
    $readyPorts = @()
    $failedPorts = @()

    foreach ($port in $Ports) {
        $ready = Test-PortInContainer -ContainerName $ContainerName -Port $port
        $serviceName = $script:ServicePortMap[$port]
        $label = if ($serviceName) { "$port ($serviceName)" } else { "$port" }

        if ($ready) {
            Write-Success "  端口 $label ✅"
            $readyPorts += $port
        } else {
            Write-Warn "  端口 $label ❌"
            $failedPorts += $port
            $allOk = $false
        }
    }

    Write-Host ""
    Write-Info "结果: $($readyPorts.Count)/$($Ports.Count) 端口就绪"

    if ($allOk) {
        Write-Success "所有 MCP 服务已恢复"
    } else {
        Write-Warn "以下端口仍未就绪: $($failedPorts -join ', ')"
        Write-Info "建议:"
        Write-Info "  1. 查看容器日志: docker logs $ContainerName -f"
        Write-Info "  2. 进入容器排查: docker exec -it $ContainerName bash"
        Write-Info "  3. 执行 L2 标准重置: Invoke-DockerReset.ps1 -Level L2"
    }

    return $allOk
}

# ============================================================
# 辅助函数（复用 Docker.psm1 中已有的函数）
# ============================================================

function Test-PortInContainer {
    <#
    .SYNOPSIS
        检查容器内指定端口是否在监听
    .PARAMETER ContainerName
        容器名称
    .PARAMETER Port
        端口号
    .OUTPUTS
        [bool] 端口在监听返回 $true
    #>
    param(
        [string]$ContainerName,
        [int]$Port
    )
    $result = docker exec $ContainerName sh -c "ss -tlnp 2>/dev/null | grep -q ':$Port ' && echo 'OK'" 2>&1
    return ($result -match 'OK')
}

function Get-ContainerLogs {
    <#
    .SYNOPSIS
        获取容器日志
    .PARAMETER ContainerName
        容器名称
    .PARAMETER Tail
        返回最后 N 行
    .OUTPUTS
        [string] 日志内容
    #>
    param(
        [string]$ContainerName,
        [int]$Tail = 100
    )
    return docker logs $ContainerName --tail $Tail 2>&1
}

# ============================================================
# 主入口函数 — 供 PScriptManager 调用
# ============================================================

function Repair-MCPServices {
    <#
    .SYNOPSIS
        MCP 服务修复工具 — 诊断并修复 Docker 容器内 MCP 服务异常
    .PARAMETER ContainerName
        容器名称（默认：workflow-mcp-env）
    .PARAMETER Ports
        要检查的端口列表
    .PARAMETER EntrypointPath
        entrypoint.sh 宿主机路径
    .PARAMETER ComposeDir
        Docker Compose 目录
    .PARAMETER Force
        跳过确认提示
    .PARAMETER DiagnoseOnly
        仅诊断，不执行修复
    .PARAMETER SkipRestart
        跳过容器重启
    .EXAMPLE
        Repair-MCPServices -Force
        Repair-MCPServices -DiagnoseOnly
        Repair-MCPServices -ContainerName "my-mcp-env" -Ports @(3000,3001)
    #>
    param(
        [string]$ContainerName = "workflow-mcp-env",
        [int[]]$Ports = @(3100, 3101, 3102, 3103, 3105, 3106, 3107),
        [string]$EntrypointPath = "",
        [string]$ComposeDir = "",
        [switch]$Force,
        [switch]$DiagnoseOnly,
        [switch]$SkipRestart
    )

    # 确保依赖函数可用（通过 PScriptManager 加载）
    $depFunctions = @(
        "Write-Header", "Write-Step", "Write-Success", "Write-Info", "Write-Warn", "Write-Error",
        "Test-CommandExists", "Confirm-Action",
        "Invoke-DockerCommand", "Test-ContainerRunning", "Copy-FileToContainer", "Get-ContainerState"
    )
    foreach ($func in $depFunctions) {
        if (-not (Get-Command $func -ErrorAction SilentlyContinue)) {
            try {
                Invoke-Function -Name $func -NoDependencyCheck | Out-Null
            } catch {
                # 静默忽略，函数可能已通过 Import-Module 加载
            }
        }
    }

    # 自动推导路径
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $projectRoot = Resolve-Path (Join-Path $scriptDir "..\..")
    if (-not $EntrypointPath) {
        $EntrypointPath = Join-Path $projectRoot "DevEnvironment\Docker\scripts\entrypoint.sh"
    }
    if (-not $ComposeDir) {
        $ComposeDir = Join-Path $projectRoot "DevEnvironment\Docker"
    }

    # ── 主流程 ──
    Write-Header "Repair-MCPServices — MCP 服务修复脚本"
    Write-Host "  容器: $ContainerName" -ForegroundColor Gray
    Write-Host "  端口: $($Ports -join ', ')" -ForegroundColor Gray
    Write-Host ""

    # 前置检查
    if (-not (Test-CommandExists "docker")) {
        Write-Error "Docker 不可用，请先安装 Docker Desktop"
        return $false
    }

    # 诊断
    $diagnosis = Invoke-Diagnose
    if ($diagnosis.AllPortsReady) {
        Write-Success "无需修复，所有服务正常运行"
        return $true
    }

    # 仅诊断模式
    if ($DiagnoseOnly) {
        Write-Info "诊断完成（仅诊断模式）"
        Write-Info "发现 $($diagnosis.Issues.Count) 个问题"
        return $diagnosis
    }

    # 确认提示
    if (-not $Force) {
        $actions = @(
            "修复 entrypoint.sh（硬编码路径、缺失命令等 $($diagnosis.Issues.Count) 个已知问题）",
            "安装容器内缺失依赖（pip install、创建目录等）",
            "构建 csproj-context（如未构建）",
            "更新 entrypoint.sh 到容器",
            $(if (-not $SkipRestart) { "重启容器" } else { "跳过容器重启" }),
            "验证修复结果"
        )
        $confirmed = Confirm-Action -Message "执行以上修复步骤？" -Force:$Force
        if (-not $confirmed) { Write-Info "已取消"; return $false }
    }

    # 执行修复
    $allSuccess = $true

    # Step 1: 修复 entrypoint.sh
    $entrypointOk = Repair-Entrypoint
    if (-not $entrypointOk) {
        Write-Error "entrypoint.sh 修复失败"
        $allSuccess = $false
    }

    # Step 2: 安装容器内依赖
    $depsOk = Install-ContainerDependencies
    if (-not $depsOk) {
        Write-Warn "依赖安装可能不完整"
    }

    # Step 3: 构建 csproj-context
    $buildOk = Build-CsprojContext
    if (-not $buildOk) {
        Write-Warn "csproj-context 未构建，端口 3107 将不可用"
    }

    # Step 4: 更新 entrypoint.sh 到容器并重启
    $updateOk = Update-AndRestart
    if (-not $updateOk) {
        Write-Error "更新 entrypoint.sh 或重启容器失败"
        $allSuccess = $false
    }

    # Step 5: 验证
    $verifyOk = Invoke-Verify
    if (-not $verifyOk) {
        $allSuccess = $false
    }

    # 结果汇总
    Write-Header "修复结果汇总"

    if ($allSuccess) {
        Write-Success "✅ MCP 服务修复完成"
        Write-Info "容器 '$ContainerName' 所有 MCP 服务已恢复"
        Write-Host ""
        Write-Info "常用命令:"
        Write-Info "  查看日志: docker logs $ContainerName -f"
        Write-Info "  进入容器: docker exec -it $ContainerName bash"
        Write-Info "  查看状态: Invoke-DockerReset.ps1 -Status"
    } else {
        Write-Error "❌ 部分修复步骤失败，请检查上方错误信息"
        Write-Info "排查步骤:"
        Write-Info "  1. 查看容器日志: docker logs $ContainerName --tail 50"
        Write-Info "  2. 进入容器排查: docker exec -it $ContainerName bash"
        Write-Info "  3. 执行 L2 标准重置: Invoke-DockerReset.ps1 -Level L2"
        Write-Info "  4. 重新部署: Deploy-MCPBridge.ps1 -Force"
    }

    return $allSuccess
}

# ============================================================
# 自动执行（当直接运行脚本时）
# ============================================================

# 检测是否通过 Invoke-Function 调用
$isInvokedByPScriptManager = $MyInvocation.Line -match 'Invoke-Function.*Repair-MCPServices'

if (-not $isInvokedByPScriptManager) {
    # 直接运行脚本 → 执行主流程
    Repair-MCPServices -ContainerName $ContainerName `
        -Ports $Ports `
        -EntrypointPath $EntrypointPath `
        -ComposeDir $ComposeDir `
        -Force:$Force `
        -DiagnoseOnly:$DiagnoseOnly `
        -SkipRestart:$SkipRestart
}
