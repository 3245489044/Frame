<#
.SYNOPSIS
    配置 Docker 容器内的 .NET SDK 环境 — 验证安装、配置 NuGet、安装额外组件

.DESCRIPTION
    在已运行的 Docker 容器中执行 .NET SDK 环境配置：
      1. 验证 .NET SDK 安装版本
      2. 配置 NuGet 源（支持国内镜像加速）
      3. 安装额外的 .NET 工作负载（如需要）
      4. 安装常用全局工具
      5. 输出环境摘要

    用法:
        .\Initialize-DotnetSdk.ps1                             配置默认容器
        .\Initialize-DotnetSdk.ps1 -ContainerName "my-builder"  指定容器
        .\Initialize-DotnetSdk.ps1 -NuGetMirror "https://nuget.cdn.azure.cn/v3/index.json"  使用国内镜像
        .\Initialize-DotnetSdk.ps1 -InstallWorkload             安装额外工作负载
        .\Initialize-DotnetSdk.ps1 -InstallTools                安装全局工具
        .\Initialize-DotnetSdk.ps1 -StatusOnly                  仅查看状态，不修改

.NOTES
    依赖: BasicMethod 模块（Core/Output.psm1, Core/Utils.psm1）
    默认容器名: workflow-mcp-env（复用 MCP 容器）
    版本: 2.2.0
    变更:
        v2.2.0 - 使用 BasicMethod 模块替代内联辅助函数
        v2.1.0 - 统一模块加载方式（Import-Modules.ps1），移除对 Deploy-DotnetBuilder.ps1 的隐式依赖
#>

param(
    # 目标容器名称
    [string]$ContainerName = "workflow-mcp-env",

    # NuGet 镜像源地址（默认使用官方源）
    [string]$NuGetMirror = "",

    # 安装额外工作负载（如 wasm-tools、android、ios 等）
    [switch]$InstallWorkload,

    # 安装常用全局工具
    [switch]$InstallTools,

    # 仅查看状态，不执行任何配置
    [switch]$StatusOnly,

    # 跳过确认提示
    [switch]$Force
)

# ============================================================
# 加载 BasicMethod 模块
# ============================================================

$basicMethodDir = Resolve-Path (Join-Path $PSScriptRoot "..\BasicMethod")
. "$basicMethodDir\_init.ps1"

# ============================================================
# 容器内命令执行（保留，因需要返回输出而非仅成功/失败）
# ============================================================

function Exec-InContainer {
    <#
    .SYNOPSIS
        在容器中执行命令并返回输出
    .DESCRIPTION
        直接调用 docker exec 执行命令，返回输出和退出码。
        与 Invoke-DockerCommand 不同，此函数返回原始输出供调用方处理。
    #>
    param(
        [string]$Command,
        [string]$Description
    )

    Write-Info "$Description..."
    $output = & docker exec $ContainerName sh -c "$Command" 2>&1
    $exitCode = $LASTEXITCODE

    if ($output) { $output | ForEach-Object { Write-Host "    $_" -ForegroundColor Gray } }
    if ($exitCode -eq 0) { Write-Success "$Description 完成" }
    else { Write-Warn "$Description 返回非零退出码: $exitCode" }
    return @{ Output = $output; ExitCode = $exitCode }
}

# ============================================================
# 前置检查
# ============================================================

Write-Header "Initialize-DotnetSdk — 配置 .NET SDK 环境"

# 检查 Docker
if (-not (Test-CommandExists "docker")) {
    Write-Error "Docker 未安装或未运行"
    exit 1
}
Write-Info "Docker 可用"

# 检查容器是否运行
$containerStatus = & docker ps --filter "name=^/${ContainerName}$" --format '{{.Status}}' 2>&1
if (-not $containerStatus) {
    $allStatus = & docker ps -a --filter "name=^/${ContainerName}$" --format '{{.Status}}' 2>&1
    if ($allStatus) {
        Write-Error "容器 '$ContainerName' 已停止，请先启动: docker start $ContainerName"
    } else {
        Write-Error "容器 '$ContainerName' 不存在，请先创建容器"
    }
    exit 1
}
Write-Success "容器 '$ContainerName' 运行中（$containerStatus）"

# ============================================================
# 步骤 1：验证 .NET SDK 安装
# ============================================================

Write-Step "步骤 1/4：验证 .NET SDK 安装"

$sdkResult = Exec-InContainer "dotnet --version" "检查 .NET SDK 版本"
$sdkVersion = $sdkResult.Output.Trim()

if ($sdkResult.ExitCode -eq 0 -and $sdkVersion) {
    Write-Success ".NET SDK $sdkVersion 已安装"
} else {
    Write-Error ".NET SDK 未安装或无法访问"
    exit 1
}

# 显示 SDK 和运行时列表
$sdkList = Exec-InContainer "dotnet --list-sdks" "列出已安装 SDK"
$runtimeList = Exec-InContainer "dotnet --list-runtimes" "列出已安装运行时"

# ============================================================
# 步骤 2：配置 NuGet 源
# ============================================================

Write-Step "步骤 2/4：配置 NuGet 源"

if ($StatusOnly) {
    $nugetSources = Exec-InContainer "dotnet nuget list source" "列出 NuGet 源"
} else {
    # 添加 NuGet 源
    $nugetUrl = if ($NuGetMirror) { $NuGetMirror } else { "https://api.nuget.org/v3/index.json" }
    $sourceName = if ($NuGetMirror) { "nuget-mirror" } else { "nuget.org" }

    $existingSources = Exec-InContainer "dotnet nuget list source" "检查现有 NuGet 源"

    if ($existingSources.Output -match $sourceName) {
        Write-Success "NuGet 源 '$sourceName' 已存在"
    } else {
        $result = Exec-InContainer "dotnet nuget add source $nugetUrl -n $sourceName" "添加 NuGet 源 '$sourceName'"
        if ($result.ExitCode -eq 0) {
            Write-Success "NuGet 源已添加: $sourceName -> $nugetUrl"
        } else {
            Write-Warn "添加 NuGet 源失败（可能已存在）"
        }
    }

    # 显示所有源
    $nugetSources = Exec-InContainer "dotnet nuget list source" "列出 NuGet 源"
}

# ============================================================
# 步骤 3：安装工作负载（可选）
# ============================================================

if ($InstallWorkload -and -not $StatusOnly) {
    Write-Step "步骤 3/4：安装 .NET 工作负载"

    $workloads = @(
        "wasm-tools"     # WebAssembly 工具
    )

    foreach ($wl in $workloads) {
        Write-Info "安装工作负载: $wl"
        $result = Exec-InContainer "dotnet workload install $wl" "安装 $wl"
        if ($result.ExitCode -eq 0) {
            Write-Success "工作负载 '$wl' 安装完成"
        } else {
            Write-Warn "工作负载 '$wl' 安装失败"
        }
    }
} else {
    Write-Step "步骤 3/4：工作负载（跳过）"
    if ($StatusOnly) {
        $installedWorkloads = Exec-InContainer "dotnet workload list" "查看已安装工作负载"
    } else {
        Write-Info "跳过（使用 -InstallWorkload 安装额外工作负载）"
    }
}

# ============================================================
# 步骤 4：安装全局工具（可选）
# ============================================================

if ($InstallTools -and -not $StatusOnly) {
    Write-Step "步骤 4/4：安装 .NET 全局工具"

    $tools = @(
        # 常用 .NET 全局工具（按需启用）
        # "dotnet-ef",           # Entity Framework Core 工具
        # "dotnet-outdated",     # 检查过期包
        # "dotnet-format",       # 代码格式化
        # "dotnet-trace",        # 性能追踪
        # "dotnet-counters",     # 性能计数器
        # "dotnet-dump",         # 内存转储分析
        # "dotnet-gcdump",       # GC 转储
    )

    if ($tools.Count -eq 0) {
        Write-Info "未配置全局工具，跳过"
    } else {
        foreach ($tool in $tools) {
            Write-Info "安装全局工具: $tool"
            $result = Exec-InContainer "dotnet tool install -g $tool" "安装 $tool"
            if ($result.ExitCode -eq 0) {
                Write-Success "全局工具 '$tool' 安装完成"
            } else {
                Write-Warn "全局工具 '$tool' 安装失败（可能已安装）"
            }
        }
    }
} else {
    Write-Step "步骤 4/4：全局工具（跳过）"
    if ($StatusOnly) {
        $installedTools = Exec-InContainer "dotnet tool list -g" "查看已安装全局工具"
    } else {
        Write-Info "跳过（使用 -InstallTools 安装全局工具）"
    }
}

# ============================================================
# 环境摘要
# ============================================================

Write-Step "环境摘要"

# 获取完整环境信息
$info = Exec-InContainer @"
echo "=== .NET SDK ==="
dotnet --version
echo ""
echo "=== SDK 列表 ==="
dotnet --list-sdks
echo ""
echo "=== 运行时列表 ==="
dotnet --list-runtimes
echo ""
echo "=== NuGet 源 ==="
dotnet nuget list source 2>/dev/null | head -20
echo ""
echo "=== 工作负载 ==="
dotnet workload list 2>/dev/null | head -20
echo ""
echo "=== 全局工具 ==="
dotnet tool list -g 2>/dev/null | head -20
echo ""
echo "=== 系统信息 ==="
cat /etc/os-release 2>/dev/null | head -5
echo "架构: \$(uname -m)"
"@ "收集环境摘要"

# ============================================================
# 完成
# ============================================================

Write-Header "SDK 环境配置完成"
Write-Success "容器: $ContainerName"
Write-Success "SDK:  $sdkVersion"
Write-Info ""
Write-Info "快速编译:"
Write-Info "  docker exec $ContainerName dotnet build CsharpPrj/MainProj.sln -c Debug"
Write-Info "  docker exec $ContainerName dotnet test CsharpPrj/MainProj.sln -c Debug"
Write-Info "  docker exec -it $ContainerName bash"
