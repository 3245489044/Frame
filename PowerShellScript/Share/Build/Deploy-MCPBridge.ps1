<#
.SYNOPSIS
    一键部署 MCPBridge — 编译 TS + 更新 entrypoint + 重启容器

.DESCRIPTION
    自动完成以下步骤：
      1. 在容器内逐个 workspace 编译 TypeScript（避免 turbo 卡住）
      2. 更新 entrypoint.sh 到容器（docker cp，无需重建镜像）
      3. 重启容器
      4. 验证部署状态

    用法:
        .\Deploy-MCPBridge.ps1              交互模式
        .\Deploy-MCPBridge.ps1 -Force        跳过确认，直接执行
        .\Deploy-MCPBridge.ps1 -SkipBuild    跳过 TS 编译（只更新 entrypoint + 重启）
        .\Deploy-MCPBridge.ps1 -SkipRestart  跳过容器重启（只编译 + 更新 entrypoint）

.NOTES
    依赖: Docker 引擎（docker CLI）
    项目根目录: 自动检测（脚本所在目录的上级）
    注意: entrypoint.sh 通过 docker cp 更新，不需要重建 Docker 镜像
# @requires Write-Header, Write-Success, Write-Info, Write-SubHeader, Write-Warning, Write-Error
# @requires Confirm-Action, Invoke-Step, Get-ProjectRoot
# @requires Test-ContainerRunning, Get-ContainerState, Invoke-DockerCommand
# @requires Test-CommandExists, Test-DockerEngineRunning
#>

param(
    # 跳过确认提示
    [switch]$Force,

    # 跳过 TypeScript 编译
    [switch]$SkipBuild,

    # 跳过容器重启
    [switch]$SkipRestart
)

# ============================================================
# 配置
# ============================================================

$script:ProjectRoot   = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$script:DockerDir     = Join-Path $script:ProjectRoot "DevEnvironment\Docker"
$script:EntrypointSrc = Join-Path $script:DockerDir "scripts\entrypoint.sh"
$script:ContainerName = "workflow-mcp-env"
$script:TsRoot        = "/workspace/WorkFlowPrj/TypeScriptPrj"

# Workspace 编译顺序（依赖顺序：BasicMethod → MCPFramework → MCPBridge → CsprojContext）
$script:Workspaces = @(
    @{ Name = "BasicMethod";   Path = "TsWorkspace/BasicMethod" }
    @{ Name = "MCPFramework";  Path = "TsWorkspace/MCPFramework" }
    @{ Name = "MCPBridge";     Path = "TsWorkspace/MCPBridge" }
    @{ Name = "CsprojContext"; Path = "TsWorkspace/CsprojContext" }
)

# 端口列表（不含已删除的 filesystem 3104）
$script:Ports = @(3100, 3101, 3102, 3103, 3105, 3106, 3107)

# ============================================================
# 加载模块（通过 Import-Modules.ps1 按需加载）
# ============================================================

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$moduleDir = Resolve-Path (Join-Path $scriptDir "..\Modules")
. "$moduleDir\Import-Modules.ps1"


# ============================================================
# 前置检查
# ============================================================

Write-Header "Deploy-MCPBridge — 一键部署"

if (-not (Test-CommandExists "docker")) {
    Write-Error "Docker 不可用，请先安装 Docker Desktop"
    exit 1
}

if (-not (Test-Path $script:EntrypointSrc)) {
    Write-Error "entrypoint.sh 未找到: $script:EntrypointSrc"
    exit 1
}

# 确认提示
if (-not $Force) {
    $actions = @()
    if (-not $SkipBuild) { $actions += "编译 TypeScript（在容器 $script:ContainerName 内）" }
    $actions += "更新 entrypoint.sh 到容器"
    if (-not $SkipRestart) { $actions += "重启容器 $script:ContainerName" }
    $actions += "验证部署状态"

    $confirmed = Confirm-Action -ActionDescription $actions
    if (-not $confirmed) { Write-Info "已取消"; exit 0 }
}

$global:allSuccess = $true

# ============================================================
# Step 1: 确保容器运行 + 安装依赖
# ============================================================

Write-Step "Step 1/4: 确保容器运行 + 安装 pnpm 依赖"

# 检查容器是否运行
$containerState = docker ps --filter "name=^/${script:ContainerName}$" --format '{{.Status}}' 2>&1
if ($containerState -match '^Up ') {
    Write-Success "容器 '$script:ContainerName' 已在运行"
} else {
    Write-Info "容器未运行，尝试启动..."
    $upOutput = docker compose -f (Join-Path $script:DockerDir "docker-compose.yml") up -d 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Error "容器启动失败: $upOutput"
        exit 1
    }
    Write-Success "容器已启动"
    Start-Sleep -Seconds 5
}

# 安装 pnpm 依赖（确保 node_modules/.bin/ 有 tsc）
Write-Info "安装 pnpm 依赖..."
$npmOk = Invoke-DockerCommand -ContainerName $script:ContainerName -Command "cd $script:TsRoot && pnpm install 2>&1 | tail -5" -Description "pnpm install"
if (-not $npmOk) {
    Write-Error "pnpm install 失败"
    $global:allSuccess = $false
}


# ============================================================
# Step 2: 编译 TypeScript（逐个 workspace）
# ============================================================

if (-not $SkipBuild) {
    Write-Step "Step 2/4: 编译 TypeScript（逐个 workspace）"

    foreach ($ws in $script:Workspaces) {
        $wsPath = "$script:TsRoot/$($ws.Path)"
        $wsName = $ws.Name

        Write-Info "编译 $wsName..."
        $buildOk = Invoke-DockerCommand -ContainerName $script:ContainerName -Command "cd '$wsPath' && npx --no-install tsc 2>&1" -Description "$wsName"

        if ($buildOk) {
            Write-Success "$wsName 编译成功"
        } else {
            Write-Error "$wsName 编译失败"
            $global:allSuccess = $false
        }
    }
} else {
    Write-Step "Step 2/4: 编译 TypeScript（已跳过）"
}

# ============================================================
# Step 3: 更新 entrypoint.sh + 重启容器
# ============================================================

Write-Step "Step 3/4: 更新 entrypoint.sh + 重启容器"

# 用 docker cp 更新 entrypoint.sh（无需重建镜像）
Write-Info "复制 entrypoint.sh 到容器..."
$cpOutput = docker cp $script:EntrypointSrc "${script:ContainerName}:/entrypoint.sh" 2>&1
if ($LASTEXITCODE -eq 0) {
    docker exec $script:ContainerName chmod +x /entrypoint.sh 2>&1 | Out-Null
    Write-Success "entrypoint.sh 已更新到容器"
} else {
    Write-Error "entrypoint.sh 复制失败: $cpOutput"
    $global:allSuccess = $false
}

if (-not $SkipRestart) {
    Write-Info "重启容器..."
    $restartOutput = docker compose -f (Join-Path $script:DockerDir "docker-compose.yml") restart 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Success "容器已重启"
    } else {
        Write-Error "容器重启失败: $restartOutput"
        $global:allSuccess = $false
    }

    Write-Info "等待容器就绪（15 秒）..."
    Start-Sleep -Seconds 15
} else {
    Write-Info "跳过容器重启"
}

# ============================================================
# Step 4: 验证部署状态
# ============================================================

Write-Step "Step 4/4: 验证部署状态"

# 检查容器是否运行
$containerState = docker ps --filter "name=^/${script:ContainerName}$" --format '{{.Status}}' 2>&1
if ($containerState -match '^Up ') {
    Write-Success "容器 '$script:ContainerName' 运行中: $containerState"
} else {
    Write-Error "容器 '$script:ContainerName' 未运行"
    $global:allSuccess = $false
}

# 检查端口是否监听
Write-Info "检查端口监听状态..."
$portCheck = docker exec $script:ContainerName sh -c "
  for port in $($script:Ports -join ' '); do
    if ss -tlnp 2>/dev/null | grep -q ':\$port '; then
      echo '  ✅ 端口 \$port 已就绪'
    else
      echo '  ❌ 端口 \$port 未就绪'
    fi
  done
" 2>&1
if ($portCheck) { $portCheck | ForEach-Object { Write-Host "    $_" } }

# 检查编译产物
Write-Info "检查编译产物..."
$checkItems = @(
    @{ Path = "TypeScriptPrj/TsWorkspace/MCPBridge/build/index.js";     Label = "MCPBridge" }
    @{ Path = "TypeScriptPrj/TsWorkspace/MCPFramework/build/index.js";  Label = "MCPFramework" }
    @{ Path = "TypeScriptPrj/TsWorkspace/BasicMethod/build/index.js";   Label = "BasicMethod" }
    @{ Path = "TypeScriptPrj/TsWorkspace/CsprojContext/build/index.js"; Label = "CsprojContext" }
)
foreach ($item in $checkItems) {
    $fullPath = Join-Path $script:ProjectRoot $item.Path
    if (Test-Path $fullPath) {
        Write-Success "$($item.Label) ✅"
    } else {
        Write-Warning "$($item.Label) ❌ 未找到"
    }
}

# 检查 entrypoint 日志
Write-Info "检查容器启动日志（最近 20 行）..."
$logs = docker logs $script:ContainerName --tail 20 2>&1
$logs | ForEach-Object { Write-Host "    $_" -ForegroundColor Gray }

# ============================================================
# 结果汇总
# ============================================================

Write-Header "部署结果汇总"

if ($global:allSuccess) {
    Write-Success "✅ MCPBridge 部署完成"
    Write-Info "容器 '$script:ContainerName' 已运行"
    Write-Info "端口: $($script:Ports -join ', ')"
    Write-Info ""
    Write-Info "查看日志: docker logs $script:ContainerName -f"
    Write-Info "进入容器: docker exec -it $script:ContainerName bash"
} else {
    Write-Error "❌ 部分步骤失败，请检查上方错误信息"
    Write-Info "提示:"
    Write-Info "  单独编译 TS: 在容器内执行 npx --no-install tsc"
    Write-Info "  更新 entrypoint: docker cp $script:EntrypointSrc ${script:ContainerName}:/entrypoint.sh"
    Write-Info "  重启容器: cd $script:DockerDir && docker compose restart"
}

exit $(if ($global:allSuccess) { 0 } else { 1 })
