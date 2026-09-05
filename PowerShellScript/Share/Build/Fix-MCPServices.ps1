<#
.SYNOPSIS
    MCP 服务一键修复脚本 — 诊断并修复 Docker 容器内 MCP 服务异常

.DESCRIPTION
    一次构建完成的修复脚本，复用已有下载器模块。
    在宿主机完成编译和下载，再复制到容器内，避免容器内操作超时。

    修复流程：
        1. 诊断阶段：检查容器状态、端口状态、日志分析
        2. 修复 entrypoint.sh（直接覆盖写入修复后的完整内容）
        3. 宿主机编译 csproj-context（调用 Invoke-TypeScriptBuild.ps1）
        4. 复制编译产物到容器
        5. 复制 entrypoint.sh 到容器并重启
        6. 验证所有端口就绪

    用法:
        .\Fix-MCPServices.ps1                       交互模式
        .\Fix-MCPServices.ps1 -Force                 跳过确认
        .\Fix-MCPServices.ps1 -DiagnoseOnly          仅诊断，不执行修复
        .\Fix-MCPServices.ps1 -ContainerName "my-container" -Ports @(3100,3101)

        # 通过 PScriptManager 调用
        Invoke-Function -Name "Fix-MCPServices" -Arguments @{Force=$true}

.NOTES
    依赖: BasicMethod/DevOps/Docker.psm1（Docker 操作函数）
           BasicMethod/Core/Output.psm1（输出函数）
           BasicMethod/Core/Utils.psm1（确认函数）
           BasicMethod/FileOps/Download.psm1（下载函数）
    默认容器: workflow-mcp-env
    默认端口: 3100-3107
#>
# @version: 1.1.0
# @author: WorkFlowPrj
# @category: 运维
# @exports: Fix-MCPServices
# @requires: Write-Header, Write-Step, Write-Success, Write-Info, Write-Warn, Write-Error
# @requires: Test-CommandExists, Confirm-Action
# @requires: Test-ContainerRunning, Invoke-ContainerScript, Copy-FileToContainer, Get-ContainerState
# @requires: Invoke-FileDownload

# ============================================================
# 参数定义
# ============================================================
param(
    # 容器名称
    [string]$ContainerName = "workflow-mcp-env",

    # 要检查的端口列表
    [int[]]$Ports = @(3100, 3101, 3102, 3103, 3104, 3105, 3106, 3107),

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

$scriptDir = $PSScriptRoot
if (-not $scriptDir) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
}
$moduleDir = Resolve-Path (Join-Path $scriptDir "..\Modules")
. "$moduleDir\Import-Modules.ps1"
$projectRoot = Get-ProjectRoot

# ============================================================
# 配置
# ============================================================

$entrypointPath = Join-Path $projectRoot "DevEnvironment\Docker\scripts\entrypoint.sh"
$csprojContextDir = Join-Path $projectRoot "TypeScriptPrj\TsWorkspace\CsprojContext"
$csprojBuildPath = Join-Path $csprojContextDir "build\index.js"

# 服务端口映射
$script:ServicePortMap = @{
    3100 = "mcp-sequentialthinking-tools"
    3101 = "context7-mcp"
    3102 = "context-mode"
    3103 = "memory"
    3104 = "filesystem"
    3105 = "lexis-mcp"
    3106 = "token-savior-recall"
    3107 = "csproj-context"
}

# ============================================================
# 辅助函数
# ============================================================

function Test-PortInContainer {
    <#
    .SYNOPSIS
        检查容器内指定端口是否在监听
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
    #>
    param(
        [string]$ContainerName,
        [int]$Tail = 100
    )
    return docker logs $ContainerName --tail $Tail 2>&1
}

# ============================================================
# 修复后的 entrypoint.sh 完整内容
# ============================================================

function Get-FixedEntrypointContent {
    <#
    .SYNOPSIS
        返回修复后的 entrypoint.sh 完整内容
    .DESCRIPTION
        直接硬编码修复后的内容，避免模式匹配的复杂性和不确定性。
        同步自 DevEnvironment/Docker/scripts/entrypoint.sh（最新版 v2.0）。
    #>
    return @'
#!/bin/bash
# ============================================================
# WorkFlowPrj MCP 环境容器入口脚本
# 使用 mcp-proxy 将 stdio MCP 服务转为 HTTP/SSE
# ============================================================
# ⚠️ 使用 set +e 而非 set -e：后台进程失败不应导致整个脚本退出
# 否则容器会进入 Restarting 状态
set +e

echo "============================================"
echo " WorkFlowPrj MCP 开发环境"
echo " 基础系统: Debian 12 Slim"
echo " Node.js:  $(node --version)"
echo " .NET SDK: $(dotnet --version 2>/dev/null || echo 'N/A')"
echo " Python:   $(python3 --version 2>/dev/null || echo 'N/A')"
echo "============================================"

# ============================================================
# 启动前清理
# ============================================================

echo "[entrypoint] 清理 /tmp 临时文件..."
rm -rf /tmp/* 2>/dev/null || true

echo "[entrypoint] 清理包管理器缓存..."
npm cache clean --force 2>/dev/null || true
pnpm store prune 2>/dev/null || true

echo "[entrypoint] 清理 .NET 临时文件..."
dotnet nuget locals all --clear 2>/dev/null || true

# 设置 Node.js 内存限制
export NODE_OPTIONS="--max-old-space-size=2048"
echo "[entrypoint] NODE_OPTIONS=$NODE_OPTIONS"


# 启动所有 MCP 服务（通过 mcp-proxy 转为 HTTP）
echo "[entrypoint] 启动 MCP 服务..."

# 1. mcp-sequentialthinking-tools (端口 3100)
npx mcp-proxy --port 3100 --stateless -- npx -y mcp-sequentialthinking-tools &
sleep 1

# 2. context7-mcp (端口 3101)
# 注意: npm 包名为 @upstash/context7-mcp，非 context7-mcp
npx mcp-proxy --port 3101 --stateless -- npx -y @upstash/context7-mcp &
sleep 1

# 3. context-mode (端口 3102)
npx mcp-proxy --port 3102 --stateless -- node /usr/local/lib/node_modules/context-mode/start.mjs &
sleep 1

# 4. memory (端口 3103)
# 注意: mcp-server-memory 硬编码了日志路径 /Users/quanle96/Documents/mcp-servers/memory/log.txt
mkdir -p /opt/mcp-data /Users/quanle96/Documents/mcp-servers/memory 2>/dev/null || true
MEMORY_FILE_PATH=/opt/mcp-data/memory.json npx mcp-proxy --port 3103 --stateless -- mcp-server-memory &
sleep 1

# 5. filesystem (端口 3104)
npx mcp-proxy --port 3104 --stateless -- mcp-server-filesystem /workspace/WorkFlowPrj &
sleep 1

# 6. lexis-mcp (端口 3105)
npx mcp-proxy --port 3105 --stateless -- node /usr/local/lib/node_modules/lexis-mcp/dist/cli/main.js mcp --path /workspace/WorkFlowPrj &
sleep 1

# 7. token-savior-recall (端口 3106)
pip install mcp -q 2>/dev/null || true
WORKSPACE_ROOTS=/workspace/WorkFlowPrj TOKEN_SAVIOR_CLIENT=claude-code TOKEN_SAVIOR_PROFILE=optimized \
  npx mcp-proxy --port 3106 --stateless -- token-savior &
sleep 1

# 8. csproj-context (端口 3107)
CSPROJ_DIR=/workspace/WorkFlowPrj/TypeScriptPrj/TsWorkspace/CsprojContext
if [ -f "$CSPROJ_DIR/build/index.js" ]; then
  echo "[entrypoint] 启动 csproj-context (端口 3107)..."
  # 检查 @modelcontextprotocol/sdk 是否可用（pnpm 符号链接在容器内可能失效）
  if [ ! -d "$CSPROJ_DIR/node_modules/@modelcontextprotocol/sdk" ]; then
    echo "[entrypoint] csproj-context: 安装 @modelcontextprotocol/sdk 依赖..."
    (cd "$CSPROJ_DIR" && npm install @modelcontextprotocol/sdk@1.29.0 --no-save --prefix "$CSPROJ_DIR" 2>/dev/null)
  fi
  (cd "$CSPROJ_DIR" && exec /usr/local/bin/mcp-proxy --port 3107 --stateless -- node "$CSPROJ_DIR/build/index.js") &
  CSPROJ_PID=$!
  echo "[entrypoint] csproj-context 已启动 (PID: $CSPROJ_PID)"
else
  echo "[entrypoint] 警告: csproj-context 未构建，跳过端口 3107"
fi

echo "[entrypoint] 等待所有服务就绪..."
sleep 8

echo "[entrypoint] 检查 MCP 服务状态..."
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

# 保持容器运行
echo "[entrypoint] 容器就绪，等待请求..."
wait
'@
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
        $result.AllPortsReady = $false
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
# 修复 entrypoint.sh（直接覆盖写入）
# ============================================================

function Repair-Entrypoint {
    <#
    .SYNOPSIS
        直接覆盖写入修复后的 entrypoint.sh
    .OUTPUTS
        [bool] 成功返回 $true
    #>
    Write-Step "修复 entrypoint.sh（直接覆盖写入）"

    if (-not (Test-Path $entrypointPath)) {
        Write-Error "entrypoint.sh 未找到: $entrypointPath"
        return $false
    }

    # 备份原文件
    $backupPath = "$entrypointPath.bak.$(Get-Date -Format 'yyyyMMddHHmmss')"
    try {
        Copy-Item $entrypointPath $backupPath -Force
        Write-Info "  已备份原文件: $backupPath"
    } catch {
        Write-Warn "  备份失败，继续执行"
    }

    # 直接覆盖写入修复后的内容
    # ⚠️ 必须使用 UTF8WithoutBOM，否则 shebang 行会被 BOM 破坏
    try {
        $fixedContent = Get-FixedEntrypointContent
        # PowerShell 5.1 的 Set-Content -Encoding UTF8 会带 BOM
        # 使用 .NET 方法写入 UTF8 without BOM
        $utf8NoBom = New-Object System.Text.UTF8Encoding $false
        [System.IO.File]::WriteAllText($entrypointPath, $fixedContent, $utf8NoBom)
        Write-Success "entrypoint.sh 已修复（UTF8 without BOM，直接覆盖写入）"
        return $true
    } catch {
        Write-Error "写入 entrypoint.sh 失败: $_"
        # 尝试恢复备份
        if (Test-Path $backupPath) {
            Copy-Item $backupPath $entrypointPath -Force
            Write-Info "已恢复备份"
        }
        return $false
    }
}

# ============================================================
# 宿主机编译 csproj-context
# ============================================================

function Build-CsprojContext {
    <#
    .SYNOPSIS
        在宿主机编译 csproj-context TypeScript 项目
    .OUTPUTS
        [bool] 编译成功或已存在返回 $true
    #>
    Write-Step "宿主机编译 csproj-context"

    # 检查宿主机是否已有编译产物
    if (Test-Path $csprojBuildPath) {
        Write-Success "csproj-context 编译产物已存在: $csprojBuildPath"
        return $true
    }

    # 检查 TypeScript 项目目录是否存在
    if (-not (Test-Path $csprojContextDir)) {
        Write-Error "csproj-context 项目目录不存在: $csprojContextDir"
        return $false
    }

    # 调用 Invoke-TypeScriptBuild.ps1 编译
    $tsBuildScript = Join-Path $scriptDir "Invoke-TypeScriptBuild.ps1"
    if (-not (Test-Path $tsBuildScript)) {
        Write-Error "Invoke-TypeScriptBuild.ps1 未找到: $tsBuildScript"
        return $false
    }

    Write-Info "正在编译 csproj-context..."
    try {
        $output = & $tsBuildScript -Project "CsprojContext" -Force 2>&1
        $output | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
    } catch {
        Write-Warn "Invoke-TypeScriptBuild.ps1 调用失败: $_"
    }

    # 验证编译产物
    if (Test-Path $csprojBuildPath) {
        Write-Success "csproj-context 编译成功: $csprojBuildPath"
        return $true
    }

    # 如果 Invoke-TypeScriptBuild.ps1 失败，尝试直接编译
    Write-Info "尝试直接使用 npx tsc 编译..."
    Push-Location $csprojContextDir
    try {
        $output = npx --no-install tsc 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Success "csproj-context 编译成功（直接编译）"
            return $true
        } else {
            Write-Warn "直接编译失败: $output"
            return $false
        }
    } catch {
        Write-Warn "直接编译异常: $_"
        return $false
    } finally {
        Pop-Location
    }
}

# ============================================================
# 复制编译产物到容器
# ============================================================

function Copy-BuildToContainer {
    <#
    .SYNOPSIS
        将宿主机编译产物复制到容器内
    .OUTPUTS
        [bool] 成功返回 $true
    #>
    Write-Step "复制编译产物到容器"

    if (-not (Test-Path $csprojBuildPath)) {
        Write-Warn "编译产物不存在，跳过复制"
        return $false
    }

    $containerBuildDir = "/workspace/WorkFlowPrj/TypeScriptPrj/TsWorkspace/CsprojContext/build"

    # 确保容器内目标目录存在
    Invoke-ContainerScript -ContainerName $ContainerName -ScriptBlock "mkdir -p $containerBuildDir" | Out-Null

    # 复制 build 目录到容器
    $localBuildDir = Split-Path -Parent $csprojBuildPath
    try {
        docker cp "$localBuildDir\." "${ContainerName}:${containerBuildDir}/" 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) {
            Write-Success "编译产物已复制到容器: $containerBuildDir"
            return $true
        } else {
            Write-Error "复制编译产物失败"
            return $false
        }
    } catch {
        Write-Error "复制编译产物异常: $_"
        return $false
    }
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
        -SourcePath $entrypointPath `
        -DestPath "/entrypoint.sh"
    if (-not $cpOk) {
        Write-Error "复制 entrypoint.sh 失败"
        return $false
    }

    # 2. 设置可执行权限
    Invoke-ContainerScript -ContainerName $ContainerName -ScriptBlock "chmod +x /entrypoint.sh" | Out-Null
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
# 主入口函数 — 供 PScriptManager 调用
# ============================================================

function Fix-MCPServices {
    <#
    .SYNOPSIS
        MCP 服务一键修复 — 诊断并修复 Docker 容器内 MCP 服务异常
    .PARAMETER ContainerName
        容器名称（默认：workflow-mcp-env）
    .PARAMETER Ports
        要检查的端口列表
    .PARAMETER Force
        跳过确认提示
    .PARAMETER DiagnoseOnly
        仅诊断，不执行修复
    .PARAMETER SkipRestart
        跳过容器重启
    .EXAMPLE
        Fix-MCPServices -Force
        Fix-MCPServices -DiagnoseOnly
        Fix-MCPServices -ContainerName "my-mcp-env" -Ports @(3000,3001)
    #>
    param(
        [string]$ContainerName = "workflow-mcp-env",
        [int[]]$Ports = @(3100, 3101, 3102, 3103, 3104, 3105, 3106, 3107),
        [switch]$Force,
        [switch]$DiagnoseOnly,
        [switch]$SkipRestart
    )

    # ── 主流程 ──
    Write-Header "Fix-MCPServices — MCP 服务一键修复"
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

    # 如果容器未运行，尝试启动
    $containerRunning = Test-ContainerRunning -ContainerName $ContainerName
    if (-not $containerRunning) {
        Write-Info "容器未运行，尝试启动..."
        $startOutput = docker start $ContainerName 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Error "容器启动失败: $startOutput"
            return $false
        }
        Write-Success "容器已启动"
        # 等待容器就绪
        Start-Sleep -Seconds 5
    }

    # 确认提示
    if (-not $Force) {
        $actions = @(
            "修复 entrypoint.sh（直接覆盖写入修复后的完整内容）",
            "宿主机编译 csproj-context（如未构建）",
            "复制编译产物到容器",
            "复制 entrypoint.sh 到容器",
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

    # Step 2: 宿主机编译 csproj-context
    $buildOk = Build-CsprojContext
    if (-not $buildOk) {
        Write-Warn "csproj-context 未构建，端口 3107 将不可用"
    }

    # Step 3: 复制编译产物到容器
    if ($buildOk) {
        $copyOk = Copy-BuildToContainer
        if (-not $copyOk) {
            Write-Warn "复制编译产物失败，端口 3107 可能不可用"
        }
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
$isInvokedByPScriptManager = $MyInvocation.Line -match 'Invoke-Function.*Fix-MCPServices'

if (-not $isInvokedByPScriptManager) {
    # 直接运行脚本 → 执行主流程
    Fix-MCPServices -ContainerName $ContainerName `
        -Ports $Ports `
        -Force:$Force `
        -DiagnoseOnly:$DiagnoseOnly `
        -SkipRestart:$SkipRestart
}
