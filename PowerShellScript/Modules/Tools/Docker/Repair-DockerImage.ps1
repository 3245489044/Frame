<#
.SYNOPSIS
    Docker 镜像修复脚本 — 修补 healthcheck.sh/entrypoint.sh + 安装 iproute2 + 修复 ENTRYPOINT
.DESCRIPTION
    利用 Update-DockerImage.ps1 框架，在旧镜像基础上执行以下修复：
    1. 安装 iproute2（让 ss 命令可用，消除 entrypoint 警告）
    2. 复制修复后的 healthcheck.sh（检测根路径 404 而非 SSE 端点）
    3. 复制修复后的 entrypoint.sh（使用 /proc/net/tcp 检测端口）
    4. 用 docker commit --change 修复 ENTRYPOINT（防止被 sleep 覆盖）
    5. 重启容器并验证健康状态

.PARAMETER ImageName
    要修复的旧镜像名称（默认 workflow-mcp-env:latest）
.PARAMETER RestartContainer
    修复完成后重启正在运行的容器（开关）
.PARAMETER WhatIf
    仅预览操作，不实际执行

.EXAMPLE
    .\Repair-DockerImage.ps1
    一键修复所有问题

.EXAMPLE
    .\Repair-DockerImage.ps1 -WhatIf
    预览修复操作
#>

param(
    [string]$ImageName = "",

    [switch]$RestartContainer = $true,

    [switch]$WhatIf
)

# ============================================================
# 导入共享模块
# ============================================================
$ModulePath = Join-Path $PSScriptRoot "..\..\Modules\Docker\DockerCommon.psm1"
Import-Module $ModulePath -Force -DisableNameChecking

# ============================================================
# 常量
# ============================================================

$ScriptRoot = $PSScriptRoot
$UpdateScriptPath = Join-Path $ScriptRoot "Update-DockerImage.ps1"
$HealthcheckSource = Resolve-ProjectPath "DevEnvironment\Docker\scripts\healthcheck.sh"
$EntrypointSource = Resolve-ProjectPath "DevEnvironment\Docker\scripts\entrypoint.sh"
$DockerComposePath = Resolve-ProjectPath (Get-ComposeFile)
$TempContainerName = "temp-mcp-repair"

# ============================================================
# 修复函数
# ============================================================


<#
.SYNOPSIS
    步骤 1: 检查环境
#>
function Step-CheckEnvironment {
    Write-Step "步骤 1/8: 环境检查"

    # 检查 Docker
    if (-not (Test-CommandExists "docker")) {
        Write-Error "Docker 命令不可用，请先安装 Docker Desktop"
        return $false
    }
    Write-Success "Docker 命令可用"

    # 检查旧镜像
    if (-not (Test-ImageExists -ImageName $ImageName)) {
        Write-Error "旧镜像不存在: $ImageName"
        Write-Info "请先构建基础镜像: docker compose -f `"$DockerComposePath`" build"
        return $false
    }
    Write-Success "旧镜像: $ImageName"

    # 检查源文件
    $filesToCheck = @(
        @{ Path = $HealthcheckSource; Label = "healthcheck.sh" },
        @{ Path = $EntrypointSource; Label = "entrypoint.sh" }
    )
    foreach ($f in $filesToCheck) {
        $resolvedPath = [System.IO.Path]::GetFullPath($f.Path)
        if (-not (Test-Path $resolvedPath)) {
            Write-Error "源文件不存在: $resolvedPath ($($f.Label))"
            return $false
        }
        Write-Success "源文件: $resolvedPath"
    }

    # 检查 Update-DockerImage.ps1
    $updatePath = [System.IO.Path]::GetFullPath($UpdateScriptPath)
    if (-not (Test-Path $updatePath)) {
        Write-Warning "Update-DockerImage.ps1 不存在，将使用内置逻辑"
    } else {
        Write-Success "Update-DockerImage.ps1 可用"
    }

    return $true
}

<#
.SYNOPSIS
    步骤 2: 启动临时容器
#>
function Step-StartTempContainer {
    Write-Step "步骤 2/8: 启动临时容器"

    # 清理同名的旧容器
    $existing = docker ps -a --filter "name=$TempContainerName" --format "{{.ID}}" 2>$null
    if ($existing) {
        Write-Info "清理旧临时容器: $TempContainerName"
        docker rm -f $TempContainerName 2>$null
    }

    Write-Info "用镜像 $ImageName 启动临时容器..."
    $result = docker run -d --name $TempContainerName --entrypoint sleep $ImageName infinity 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Error "启动临时容器失败: $result"
        return $false
    }

    Write-Success "临时容器已启动: $TempContainerName"
    return $true
}

<#
.SYNOPSIS
    步骤 3: 安装 iproute2（让 ss 命令可用）
#>
function Step-InstallIproute2 {
    Write-Step "步骤 3/8: 安装 iproute2"

    if ($WhatIf) {
        Write-WhatIf "在容器内执行: apt-get update && apt-get install -y iproute2"
        return $true
    }

    Write-Info "执行: apt-get update..."
    $result = docker exec $TempContainerName sh -c "apt-get update -qq" 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Error "apt-get update 失败: $result"
        return $false
    }
    Write-Success "apt-get update 完成"

    Write-Info "执行: apt-get install -y iproute2..."
    $result = docker exec $TempContainerName sh -c "apt-get install -y -qq iproute2" 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Error "安装 iproute2 失败: $result"
        return $false
    }
    Write-Success "iproute2 安装完成"

    # 验证 ss 命令可用
    $ssCheck = docker exec $TempContainerName sh -c "command -v ss && ss --version" 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Success "ss 命令可用"
    } else {
        Write-Warning "ss 命令仍不可用，但 entrypoint.sh 已有备用检测方案"
    }

    return $true
}

<#
.SYNOPSIS
    步骤 4: 复制修复后的 healthcheck.sh
#>
function Step-CopyHealthcheck {
    Write-Step "步骤 4/8: 复制修复后的 healthcheck.sh"

    $sourcePath = [System.IO.Path]::GetFullPath($HealthcheckSource)

    if ($WhatIf) {
        Write-WhatIf "复制: $sourcePath → $TempContainerName`:/healthcheck.sh"
        return $true
    }

    Write-Info "复制: $sourcePath → $TempContainerName`:/healthcheck.sh"
    $result = docker cp $sourcePath "${TempContainerName}:/healthcheck.sh" 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Error "复制 healthcheck.sh 失败: $result"
        return $false
    }

    # 设置可执行权限
    docker exec $TempContainerName chmod +x /healthcheck.sh 2>$null
    Write-Success "healthcheck.sh 已复制"

    # 验证内容
    $content = docker exec $TempContainerName sh -c "head -10 /healthcheck.sh" 2>&1
    if ($content -match "max-time") {
        Write-Success "healthcheck.sh 内容正确（包含 --max-time）"
    } else {
        Write-Warning "healthcheck.sh 内容可能不正确，请检查"
        Write-Info "当前内容:"
        $content | ForEach-Object { Write-Host "    $_" -ForegroundColor Gray }
    }

    return $true
}

<#
.SYNOPSIS
    步骤 5: 复制修复后的 entrypoint.sh
#>
function Step-CopyEntrypoint {
    Write-Step "步骤 5/8: 复制修复后的 entrypoint.sh"

    $sourcePath = [System.IO.Path]::GetFullPath($EntrypointSource)

    if ($WhatIf) {
        Write-WhatIf "复制: $sourcePath → $TempContainerName`:/entrypoint.sh"
        return $true
    }

    Write-Info "复制: $sourcePath → $TempContainerName`:/entrypoint.sh"
    $result = docker cp $sourcePath "${TempContainerName}:/entrypoint.sh" 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Error "复制 entrypoint.sh 失败: $result"
        return $false
    }

    # 设置可执行权限
    docker exec $TempContainerName chmod +x /entrypoint.sh 2>$null
    Write-Success "entrypoint.sh 已复制"

    return $true
}

<#
.SYNOPSIS
    步骤 6: 验证容器内文件
#>
function Step-VerifyFiles {
    Write-Step "步骤 6/8: 验证容器内文件"

    if ($WhatIf) {
        Write-WhatIf "验证容器内文件内容"
        return $true
    }

    # 验证 healthcheck.sh
    $hcContent = docker exec $TempContainerName sh -c "cat /healthcheck.sh" 2>&1
    if ($hcContent -match "max-time") {
        Write-Success "healthcheck.sh: 包含 --max-time 参数 ✅"
    } else {
        Write-Error "healthcheck.sh: 缺少 --max-time 参数 ❌"
        return $false
    }
    if ($hcContent -match "grep -q .404.") {
        Write-Success "healthcheck.sh: 检测 404 状态码 ✅"
    } else {
        Write-Error "healthcheck.sh: 状态码检测不正确 ❌"
        return $false
    }

    # 验证 entrypoint.sh
    $epContent = docker exec $TempContainerName sh -c "cat /entrypoint.sh" 2>&1
    if ($epContent -match "ss -tlnp") {
        Write-Success "entrypoint.sh: 包含 ss 检测逻辑 ✅"
    } else {
        Write-Warning "entrypoint.sh: 可能不包含 ss 检测逻辑"
    }

    # 验证 ss 命令
    $ssCheck = docker exec $TempContainerName sh -c "command -v ss" 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Success "ss 命令已安装 ✅"
    } else {
        Write-Warning "ss 命令未安装，但 entrypoint.sh 有备用方案"
    }

    # 测试 healthcheck.sh 执行
    Write-Info "测试 healthcheck.sh 执行..."
    $hcResult = docker exec $TempContainerName sh -c "bash /healthcheck.sh; echo EXIT_CODE=\$?" 2>&1
    if ($hcResult -match "EXIT_CODE=0") {
        Write-Success "healthcheck.sh 执行成功 ✅"
    } else {
        Write-Warning "healthcheck.sh 执行失败，但容器内可能没有 MCP 服务在运行"
        Write-Info "输出: $hcResult"
    }

    return $true
}

<#
.SYNOPSIS
    步骤 7: 提交新镜像（修复 ENTRYPOINT）
.DESCRIPTION
    用 docker commit --change 提交新镜像，同时修复 ENTRYPOINT。
    关键：必须用 --change 参数恢复正确的 ENTRYPOINT，否则会被 sleep 覆盖。
#>
function Step-CommitImage {
    Write-Step "步骤 7/8: 提交新镜像（修复 ENTRYPOINT）"

    if ($WhatIf) {
        Write-WhatIf "执行: docker commit --change `"ENTRYPOINT [\`"/usr/bin/tini\`", \`"--\`", \`"/entrypoint.sh\`"]`" --change `"CMD []`" $TempContainerName $ImageName"
        return $true
    }

    Write-Info "提交容器 $TempContainerName → $ImageName"
    Write-Info "同时修复 ENTRYPOINT: /usr/bin/tini -- /entrypoint.sh"

    # 注意：PowerShell 中双引号内的反斜杠转义
    $result = docker commit --change "ENTRYPOINT [\`"/usr/bin/tini\`", \`"--\`", \`"/entrypoint.sh\`"]" --change "CMD []" $TempContainerName $ImageName 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Error "提交失败: $result"
        return $false
    }

    Write-Success "新镜像已创建: $ImageName"

    # 验证 ENTRYPOINT
    $entrypoint = docker inspect --format "{{json .Config.Entrypoint}}" $ImageName 2>&1
    Write-Info "新镜像 ENTRYPOINT: $entrypoint"
    if ($entrypoint -match "entrypoint.sh") {
        Write-Success "ENTRYPOINT 修复成功 ✅"
    } else {
        Write-Error "ENTRYPOINT 修复失败 ❌"
        return $false
    }

    return $true
}

<#
.SYNOPSIS
    步骤 8: 清理并重启
#>
function Step-CleanupAndRestart {
    Write-Step "步骤 8/8: 清理并重启"

    # 清理临时容器
    if (-not $WhatIf) {
        $existing = docker ps -a --filter "name=$TempContainerName" --format "{{.ID}}" 2>$null
        if ($existing) {
            docker rm -f $TempContainerName 2>$null
            Write-Success "临时容器已清理: $TempContainerName"
        }
    } else {
        Write-WhatIf "清理临时容器: $TempContainerName"
    }

    # 重启正式容器
    if ($RestartContainer) {
        $containerName = Get-ContainerName

        if ($WhatIf) {
            Write-WhatIf "重启容器: $containerName"
            return $true
        }

        if (Test-ContainerRunning -ContainerName $containerName) {
            Write-Info "重启容器: $containerName"
            docker restart $containerName 2>$null
            Write-Success "容器已重启: $containerName"
        } else {
            Write-Warning "容器未在运行: $containerName"
            Write-Info "正在通过 docker compose 启动..."

            $composePath = [System.IO.Path]::GetFullPath($DockerComposePath)
            if (Test-Path $composePath) {
                $result = docker compose -f $composePath up -d 2>&1
                if ($LASTEXITCODE -eq 0) {
                    Write-Success "容器已启动"
                } else {
                    Write-Error "启动失败: $result"
                    return $false
                }
            } else {
                Write-Error "docker-compose.yml 不存在: $composePath"
                Write-Info "请手动启动: docker run -d --name $containerName $ImageName"
                return $false
            }
        }
    } else {
        Write-Info "跳过重启容器（使用 -RestartContainer 开关可启用）"
    }

    return $true
}

<#
.SYNOPSIS
    验证最终结果
#>
function Step-VerifyResult {
    Write-Step "验证最终结果"

    if ($WhatIf) {
        Write-WhatIf "验证健康状态和 MCP 服务"
        return $true
    }

    $containerName = Get-ContainerName

    # 等待容器就绪
    Write-Info "等待容器就绪（约 5 秒）..."
    Start-Sleep -Seconds 5

    # 检查健康状态
    $health = docker inspect --format "{{.State.Health.Status}}" $containerName 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Info "健康状态: $health"
        if ($health -eq "healthy") {
            Write-Success "容器健康 ✅"
        } elseif ($health -eq "starting") {
            Write-Warning "容器仍在启动中（start_period=30s），请稍后检查"
        } else {
            Write-Warning "容器状态: $health"
        }
    } else {
        Write-Warning "无法获取健康状态（可能未配置 healthcheck）"
    }

    # 检查容器日志
    Write-Info "容器日志（最后 10 行）:"
    $logs = docker logs $containerName 2>&1
    $logs[-10..-1] | ForEach-Object { Write-Host "    $_" -ForegroundColor Gray }

    # 检查 MCP 服务端口
    Write-Info "检查 MCP 服务端口..."
    $portCheck = docker exec $containerName sh -c "cat /proc/net/tcp 2>/dev/null | grep -c '0C1'" 2>&1
    if ($LASTEXITCODE -eq 0 -and [int]$portCheck -ge 7) {
        Write-Success "MCP 服务端口全部监听 ✅（检测到 $portCheck 个端口）"
    } else {
        Write-Warning "端口检测异常，请手动检查: docker logs $containerName"
    }

    # 测试 HTTP 访问
    Write-Info "测试 HTTP 访问..."
    $httpTest = curl.exe -s --max-time 3 http://localhost:3100/ 2>&1
    if ($LASTEXITCODE -eq 0 -or $LASTEXITCODE -eq 22) {
        # 22 = curl HTTP error，但说明端口可达
        Write-Success "端口 3100 可达 ✅"
    } else {
        Write-Warning "端口 3100 不可达，请检查端口映射"
    }

    return $true
}

<#
.SYNOPSIS
    显示修复摘要
#>
function Show-Summary {
    Write-Step "修复完成" -Color "Green"

    Write-Host "  ✅ 镜像: $ImageName" -ForegroundColor Green
    Write-Host "  📦 安装: iproute2 (ss 命令)" -ForegroundColor Cyan
    Write-Host "  📄 更新: healthcheck.sh（修复 SSE 超时问题）" -ForegroundColor Cyan
    Write-Host "  📄 更新: entrypoint.sh（保持最新）" -ForegroundColor Cyan
    Write-Host "  🔧 修复: ENTRYPOINT（恢复 tini → entrypoint.sh）" -ForegroundColor Cyan

    $containerName = Get-ContainerName

    if ($RestartContainer) {
        Write-Host "  🔄 已重启: $containerName" -ForegroundColor Cyan
    }

    Write-Host ""
    Write-Host "  💡 后续验证:" -ForegroundColor Yellow
    Write-Host "  1. 查看容器日志: docker logs $containerName" -ForegroundColor Gray
    Write-Host "  2. 检查健康状态: docker inspect --format '{{.State.Health.Status}}' $containerName" -ForegroundColor Gray
    Write-Host "  3. 测试 MCP 服务: curl -s --max-time 3 http://localhost:3100/" -ForegroundColor Gray
}

# ============================================================
# 主流程
# ============================================================

function Main {
    Write-Banner

    if ($WhatIf) {
        Write-Host "  🔮 WhatIf 模式 — 仅预览，不实际执行`n" -ForegroundColor Magenta
    }

    # ─── 参数默认值（从配置读取） ───
    if (-not $ImageName) { $ImageName = Get-ImageName }

    # ─── 步骤 1: 环境检查 ───
    $ok = Step-CheckEnvironment
    if (-not $ok) { exit 1 }

    # ─── 步骤 2: 启动临时容器 ───
    $ok = Step-StartTempContainer
    if (-not $ok) { exit 1 }

    try {
        # ─── 步骤 3: 安装 iproute2 ───
        $ok = Step-InstallIproute2
        if (-not $ok) { exit 1 }

        # ─── 步骤 4: 复制 healthcheck.sh ───
        $ok = Step-CopyHealthcheck
        if (-not $ok) { exit 1 }

        # ─── 步骤 5: 复制 entrypoint.sh ───
        $ok = Step-CopyEntrypoint
        if (-not $ok) { exit 1 }

        # ─── 步骤 6: 验证文件 ───
        $ok = Step-VerifyFiles
        if (-not $ok) { exit 1 }

        # ─── 步骤 7: 提交新镜像 ───
        $ok = Step-CommitImage
        if (-not $ok) { exit 1 }

    } finally {
        # ─── 步骤 8: 清理并重启 ───
        $ok = Step-CleanupAndRestart
    }

    # ─── 验证 ───
    if (-not $WhatIf) {
        $ok = Step-VerifyResult
    }

    # ─── 摘要 ───
    Show-Summary

    if ($WhatIf) {
        Write-Host "`n  🔮 WhatIf 模式结束 — 未执行任何实际操作" -ForegroundColor Magenta
    }
}

# ============================================================
# 启动
# ============================================================

Main
